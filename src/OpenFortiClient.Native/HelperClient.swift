// One connection to the privileged helper, for one openfortivpn. The app's HelperLauncher creates one per
// start and is its delegate: it receives helperOutput: (NSString) and helperExited: (NSNumber).

import Foundation

@objc(OFCHelperClient)
public final class OFCHelperClient: NSObject, OFCHelperClientProtocol {
    private var connection: NSXPCConnection?
    private weak var delegate: NSObject?
    private let lock = NSLock()
    private var exited = false

    @objc public func setDelegate(_ delegate: NSObject) {
        self.delegate = delegate
    }

    /// Keys: executable, config (String), clientKey (String, optional), promptMarkers ([String]).
    /// Blocks until the helper answered; returns nil when openfortivpn started, otherwise why not.
    @objc public func start(_ request: NSDictionary) -> NSString? {
        let connection = NSXPCConnection(machServiceName: helperMachServiceName, options: .privileged)
        connection.remoteObjectInterface = NSXPCInterface(with: OFCHelperProtocol.self)
        connection.exportedInterface = NSXPCInterface(with: OFCHelperClientProtocol.self)
        connection.exportedObject = self

        // Talk only to our own helper, signed by the same team.
        if !helperTeamIdentifier.isEmpty {
            connection.setCodeSigningRequirement(
                "identifier \"io.github.openforticlient.helper\" and anchor apple generic and certificate leaf[subject.OU] = \"\(helperTeamIdentifier)\"")
        }

        // The helper went away (crashed, removed): openfortivpn is gone with it.
        connection.invalidationHandler = { [weak self] in self?.exited(code: -1) }
        connection.interruptionHandler = { [weak self] in self?.exited(code: -1) }
        connection.resume()
        self.connection = connection

        var failure: String?
        let helper = connection.synchronousRemoteObjectProxyWithErrorHandler { error in
            failure = "The privileged helper is not available (\(error.localizedDescription)). Allow it in System Settings → General → Login Items."
        } as? OFCHelperProtocol

        helper?.start(
            executable: request["executable"] as? String ?? "",
            config: request["config"] as? String ?? "",
            clientKey: request["clientKey"] as? String,
            promptMarkers: request["promptMarkers"] as? [String] ?? []) { error in
            failure = error
        }

        if failure != nil {
            connection.invalidate()
        }

        return failure as NSString?
    }

    /// Changes a macOS L2TP VPN service's settings through the helper (VpnSettingsWriter). Keys: serviceId,
    /// server, account (String), sendAllTraffic (Bool), echoInterval, echoFailures (NSNumber), password and
    /// sharedSecret (String, optional: missing keeps the saved one). Blocks until the helper answered; returns
    /// nil when saved, otherwise why not. Its own short connection, separate from a running openfortivpn.
    @objc public func writeVpnSettings(_ request: NSDictionary) -> NSString? {
        let connection = NSXPCConnection(machServiceName: helperMachServiceName, options: .privileged)
        connection.remoteObjectInterface = NSXPCInterface(with: OFCHelperProtocol.self)
        if !helperTeamIdentifier.isEmpty {
            connection.setCodeSigningRequirement(
                "identifier \"io.github.openforticlient.helper\" and anchor apple generic and certificate leaf[subject.OU] = \"\(helperTeamIdentifier)\"")
        }

        connection.resume()
        defer { connection.invalidate() }

        // Wait at most 20 s: a helper that macOS will not start (for example a build that is not notarised)
        // never answers, and the app must say so instead of waiting forever.
        let answered = DispatchSemaphore(value: 0)
        let lock = NSLock()
        var failure: String?
        func finish(_ result: String?) {
            lock.lock()
            failure = result
            lock.unlock()
            answered.signal()
        }

        let helper = connection.remoteObjectProxyWithErrorHandler { error in
            finish("The privileged helper is not available (\(error.localizedDescription)). Install it under Settings → Privileged helper.")
        } as? OFCHelperProtocol

        helper?.writeVpnSettings(
            serviceId: request["serviceId"] as? String ?? "",
            server: request["server"] as? String ?? "",
            account: request["account"] as? String ?? "",
            sendAllTraffic: request["sendAllTraffic"] as? Bool ?? false,
            echoInterval: (request["echoInterval"] as? NSNumber)?.intValue ?? 0,
            echoFailures: (request["echoFailures"] as? NSNumber)?.intValue ?? 0,
            password: request["password"] as? String,
            sharedSecret: request["sharedSecret"] as? String) { error in
            finish(error)
        }

        if answered.wait(timeout: .now() + 20) == .timedOut {
            return "The privileged helper did not answer. Remove and install it again under Settings → Privileged helper; if that does not help, see the app log."
        }

        lock.lock()
        defer { lock.unlock() }
        return failure as NSString?
    }

    @objc public func write(_ line: NSString) {
        (connection?.remoteObjectProxy as? OFCHelperProtocol)?.write(line: line as String)
    }

    @objc public func terminate() {
        (connection?.remoteObjectProxy as? OFCHelperProtocol)?.terminate()
    }

    // MARK: OFCHelperClientProtocol

    public func output(line: String) {
        send("helperOutput:", line as NSString)
    }

    public func exited(code: Int32) {
        lock.lock()
        let first = !exited
        exited = true
        lock.unlock()
        if first {
            send("helperExited:", NSNumber(value: code))
            connection?.invalidate()
        }
    }

    private func send(_ selector: String, _ argument: Any) {
        let sel = NSSelectorFromString(selector)
        if let delegate, delegate.responds(to: sel) {
            delegate.perform(sel, with: argument)
        }
    }
}
