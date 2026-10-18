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
