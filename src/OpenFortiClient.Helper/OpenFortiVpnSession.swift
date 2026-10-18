// One openfortivpn run by the privileged helper. No XPC here, so tests can drive it as a normal user
// (tests/OpenFortiClient.Helper.Tests, scripts/test-helper.sh).

import Foundation

enum SessionError: Error, CustomStringConvertible {
    case executableNotAllowed(String)
    case configKeyNotAllowed(String)
    case alreadyRunning
    case launchFailed(String)

    var description: String {
        switch self {
        case .executableNotAllowed(let path): return "\(path) is not an openfortivpn the helper may run."
        case .configKeyNotAllowed(let key): return "The config key \"\(key)\" is not allowed."
        case .alreadyRunning: return "openfortivpn is already running for this connection."
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

    /// Homebrew's openfortivpn, after following symlinks: /opt/homebrew or /usr/local.
    static func isHomebrewOpenFortiVpn(_ path: String) -> Bool {
        let resolved = URL(fileURLWithPath: path).resolvingSymlinksInPath().path
        let pattern = #"^/(opt/homebrew|usr/local)/(Cellar/openfortivpn/[^/]+/bin|bin)/openfortivpn$"#
        return resolved.range(of: pattern, options: .regularExpression) != nil
    }

    var onOutput: ((String) -> Void)?
    var onExit: ((Int32) -> Void)?

    private let runtimeDirectory: URL
    private let isAllowed: (String) -> Bool
    private let lock = NSLock()
    private var process: Process?
    private var input: FileHandle?
    private var configFile: URL?
    private var keyFile: URL?
    private var pending = Data()
    private var promptMarkers: [String] = []

    init(runtimeDirectory: URL, isAllowed: @escaping (String) -> Bool = OpenFortiVpnSession.isHomebrewOpenFortiVpn) {
        self.runtimeDirectory = runtimeDirectory
        self.isAllowed = isAllowed
    }

    func start(executable: String, config: String, clientKey: String?, promptMarkers: [String]) throws {
        lock.lock()
        defer { lock.unlock() }
        guard process == nil else { throw SessionError.alreadyRunning }
        guard isAllowed(executable) else { throw SessionError.executableNotAllowed(executable) }

        var lines = try Self.checkedLines(config)
        try Self.ensurePrivateDirectory(runtimeDirectory)
        let id = UUID().uuidString
        if let clientKey {
            let keyFile = runtimeDirectory.appendingPathComponent("\(id).key")
            try Self.writePrivate(keyFile, clientKey)
            self.keyFile = keyFile
            lines.removeAll { $0.hasPrefix("user-key") }
            lines.append("user-key = \(keyFile.path)")
        }

        let configFile = runtimeDirectory.appendingPathComponent("\(id).conf")
        try Self.writePrivate(configFile, lines.joined(separator: "\n") + "\n")
        self.configFile = configFile

        let process = Process()
        process.executableURL = URL(fileURLWithPath: executable)
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
            deleteConfig()
            deleteKey()
            throw SessionError.launchFailed(error.localizedDescription)
        }

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

    /// Created with mode 0600 from the start, never readable by anyone else.
    private static func writePrivate(_ url: URL, _ text: String) throws {
        let fd = open(url.path, O_WRONLY | O_CREAT | O_EXCL, 0o600)
        guard fd >= 0 else { throw SessionError.launchFailed("cannot create \(url.lastPathComponent): \(String(cString: strerror(errno)))") }
        let handle = FileHandle(fileDescriptor: fd, closeOnDealloc: true)
        try handle.write(contentsOf: Data(text.utf8))
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
}
