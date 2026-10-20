// One openfortivpn run by the privileged helper. No XPC here, so tests can drive it as a normal user
// (tests/OpenFortiClient.Helper.Tests, scripts/test-helper.sh).

import Foundation

enum SessionError: Error, CustomStringConvertible {
    case executableNotAllowed(String)
    case configKeyNotAllowed(String)
    case alreadyRunning
    case notVerified(String)
    case launchFailed(String)

    var description: String {
        switch self {
        case .executableNotAllowed(let path): return "\(path) is not an openfortivpn the helper may run."
        case .configKeyNotAllowed(let key): return "The config key \"\(key)\" is not allowed."
        case .alreadyRunning: return "openfortivpn is already running for this connection."
        case .notVerified(let reason): return "The bundled openfortivpn failed its signature check (\(reason)). Reinstall OpenFortiClient."
        case .launchFailed(let reason): return "Could not start openfortivpn: \(reason)"
        }
    }
}

final class OpenFortiVpnSession {
    /// The only config keys the app writes. Anything else (pppd-call, pppd-plugin, …) could make a root
    /// process run something else, so it is refused.
    static let allowedConfigKeys: Set<String> = [
        "host", "port", "username", "password", "realm", "trusted-cert",
        "user-cert", "user-key", "set-dns", "set-routes",
    ]

    var onOutput: ((String) -> Void)?
    var onExit: ((Int32) -> Void)?

    private let runtimeDirectory: URL
    private let bundled: URL
    private let verify: (URL) throws -> Void
    private let lock = NSLock()
    private var process: Process?
    private var input: FileHandle?
    private var configFile: URL?
    private var keyFile: URL?
    private var executableCopy: URL?
    private var pending = Data()
    private var promptMarkers: [String] = []

    /// - Parameters:
    ///   - bundled: the openfortivpn in the app bundle, next to the helper. The only one it runs.
    ///   - verify: checks the code signature of the private copy that will run; throws why not.
    init(runtimeDirectory: URL, bundled: URL, verify: @escaping (URL) throws -> Void) {
        self.runtimeDirectory = runtimeDirectory
        self.bundled = bundled.resolvingSymlinksInPath()
        self.verify = verify
    }

    func start(executable: String, config: String, clientKey: String?, promptMarkers: [String]) throws {
        lock.lock()
        defer { lock.unlock() }
        guard process == nil else { throw SessionError.alreadyRunning }
        guard URL(fileURLWithPath: executable).resolvingSymlinksInPath().path == bundled.path else {
            throw SessionError.executableNotAllowed(executable)
        }

        var lines = try Self.checkedLines(config)
        try Self.ensurePrivateDirectory(runtimeDirectory)
        let id = UUID().uuidString

        // Whatever fails from here on, nothing is left behind.
        var started = false
        defer {
            if !started {
                deleteConfig()
                deleteKey()
                deleteExecutableCopy()
            }
        }

        // The app bundle is writable by its owner, so the bundled file could change between a check and the
        // launch. Copy its bytes into a new root-owned file (never a link), check the copy, and run the copy.
        let copy = runtimeDirectory.appendingPathComponent("\(id).openfortivpn")
        do {
            let bytes = try Data(contentsOf: bundled)
            executableCopy = copy
            try Self.writePrivate(copy, bytes, mode: 0o500)
        } catch {
            throw SessionError.launchFailed("cannot copy \(bundled.path): \(error.localizedDescription)")
        }
        do {
            try verify(copy)
        } catch {
            throw SessionError.notVerified("\(error)")
        }

        if let clientKey {
            let keyFile = runtimeDirectory.appendingPathComponent("\(id).key")
            try Self.writePrivate(keyFile, Data(clientKey.utf8))
            self.keyFile = keyFile
            lines.removeAll { $0.hasPrefix("user-key") }
            lines.append("user-key = \(keyFile.path)")
        }

        let configFile = runtimeDirectory.appendingPathComponent("\(id).conf")
        try Self.writePrivate(configFile, Data((lines.joined(separator: "\n") + "\n").utf8))
        self.configFile = configFile

        let process = Process()
        process.executableURL = copy
        process.arguments = ["-c", configFile.path]
        let output = Pipe()
        let stdin = Pipe()
        process.standardOutput = output
        process.standardError = output
        process.standardInput = stdin
        self.promptMarkers = promptMarkers

        output.fileHandleForReading.readabilityHandler = { [weak self] handle in
            let data = handle.availableData
            if data.isEmpty {
                handle.readabilityHandler = nil
            } else {
                self?.received(data)
            }
        }
        process.terminationHandler = { [weak self] finished in
            output.fileHandleForReading.readabilityHandler = nil
            self?.finished(finished.terminationStatus, rest: output.fileHandleForReading.readDataToEndOfFile())
        }

        do {
            try process.run()
        } catch {
            throw SessionError.launchFailed(error.localizedDescription)
        }
        started = true

        self.process = process
        input = stdin.fileHandleForWriting
    }

    func write(line: String) {
        lock.lock()
        let input = self.input
        lock.unlock()
        try? input?.write(contentsOf: Data((line + "\n").utf8))
    }

    /// SIGTERM, so openfortivpn removes its routes and DNS settings. Never SIGKILL.
    func terminate() {
        lock.lock()
        let process = self.process
        lock.unlock()
        if let process, process.isRunning {
            process.terminate()
        }
    }

    // MARK: - Output

    private func received(_ data: Data) {
        var lines: [String] = []
        lock.lock()
        pending.append(data)
        while let newline = pending.firstIndex(of: UInt8(ascii: "\n")) {
            lines.append(String(decoding: pending[pending.startIndex..<newline], as: UTF8.self))
            pending.removeSubrange(pending.startIndex...newline)
        }

        // Prompts (the OTP) have no newline: deliver them as soon as they are complete.
        let rest = String(decoding: pending, as: UTF8.self).trimmingCharacters(in: .whitespaces)
        if !rest.isEmpty, promptMarkers.contains(where: { rest.hasSuffix($0) }) {
            lines.append(String(decoding: pending, as: UTF8.self))
            pending.removeAll()
        }

        // openfortivpn has read its config before it prints anything. The key stays: openfortivpn loads it
        // again for each TLS connection (login, configuration, tunnel), so it goes when the process ends.
        deleteConfig()
        lock.unlock()

        for line in lines {
            onOutput?(line.replacingOccurrences(of: "\r", with: ""))
        }
    }

    private func finished(_ status: Int32, rest: Data) {
        if !rest.isEmpty {
            received(rest)
        }

        lock.lock()
        let last = pending.isEmpty ? nil : String(decoding: pending, as: UTF8.self)
        pending.removeAll()
        deleteConfig()
        deleteKey()
        deleteExecutableCopy()
        process = nil
        input = nil
        lock.unlock()

        if let last {
            onOutput?(last)
        }

        onExit?(status)
    }

    // MARK: - Files

    /// Lines of the config, each checked against the allowed keys.
    static func checkedLines(_ config: String) throws -> [String] {
        try config.split(whereSeparator: \.isNewline).map(String.init).filter { !$0.trimmingCharacters(in: .whitespaces).isEmpty }.map { line in
            let key = line.split(separator: "=", maxSplits: 1).first.map { $0.trimmingCharacters(in: .whitespaces) } ?? ""
            guard allowedConfigKeys.contains(key), !line.contains("\0") else {
                throw SessionError.configKeyNotAllowed(key)
            }
            return line
        }
    }

    private static func ensurePrivateDirectory(_ url: URL) throws {
        try FileManager.default.createDirectory(at: url, withIntermediateDirectories: true, attributes: [.posixPermissions: 0o700])
        try FileManager.default.setAttributes([.posixPermissions: 0o700], ofItemAtPath: url.path)
    }

    /// A new file (never an existing one or a link), with its mode from the start: 0600 by default, readable
    /// by no one else.
    private static func writePrivate(_ url: URL, _ data: Data, mode: mode_t = 0o600) throws {
        let fd = open(url.path, O_WRONLY | O_CREAT | O_EXCL | O_NOFOLLOW, mode)
        guard fd >= 0 else { throw SessionError.launchFailed("cannot create \(url.lastPathComponent): \(String(cString: strerror(errno)))") }
        let handle = FileHandle(fileDescriptor: fd, closeOnDealloc: true)
        try handle.write(contentsOf: data)
    }

    /// Called with the lock held, or after the process is gone.
    private func deleteConfig() {
        if let configFile {
            try? FileManager.default.removeItem(at: configFile)
        }
        configFile = nil
    }

    private func deleteKey() {
        if let keyFile {
            try? FileManager.default.removeItem(at: keyFile)
        }
        keyFile = nil
    }

    private func deleteExecutableCopy() {
        if let executableCopy {
            try? FileManager.default.removeItem(at: executableCopy)
        }
        executableCopy = nil
    }
}
