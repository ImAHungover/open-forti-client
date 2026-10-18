// OpenFortiClient's privileged helper: a launch daemon registered with SMAppService that runs openfortivpn
// as root for the app, and nothing else. Accepts XPC connections only from the OpenFortiClient app signed
// by the same team. See docs/architecture.md, "Elevation".

import Foundation

let runtimeDirectory = URL(fileURLWithPath: "/var/run/openforticlient", isDirectory: true)

/// One app connection, one openfortivpn.
final class HelperService: NSObject, OFCHelperProtocol {
    private weak var connection: NSXPCConnection?
    private let session = OpenFortiVpnSession(runtimeDirectory: runtimeDirectory)

    init(connection: NSXPCConnection) {
        self.connection = connection
        super.init()
        session.onOutput = { [weak self] line in self?.client?.output(line: line) }
        session.onExit = { [weak self] code in self?.client?.exited(code: code) }
    }

    private var client: OFCHelperClientProtocol? {
        connection?.remoteObjectProxy as? OFCHelperClientProtocol
    }

    func start(executable: String, config: String, clientKey: String?, promptMarkers: [String], reply: @escaping (String?) -> Void) {
        do {
            try session.start(executable: executable, config: config, clientKey: clientKey, promptMarkers: promptMarkers)
            reply(nil)
        } catch {
            reply("\(error)")
        }
    }

    func write(line: String) {
        session.write(line: line)
    }

    func terminate() {
        session.terminate()
    }
}

final class ListenerDelegate: NSObject, NSXPCListenerDelegate {
    func listener(_ listener: NSXPCListener, shouldAcceptNewConnection connection: NSXPCConnection) -> Bool {
        connection.exportedInterface = NSXPCInterface(with: OFCHelperProtocol.self)
        connection.remoteObjectInterface = NSXPCInterface(with: OFCHelperClientProtocol.self)
        let service = HelperService(connection: connection)
        connection.exportedObject = service

        // The app went away (quit, crash): never leave openfortivpn running.
        connection.invalidationHandler = { service.terminate() }
        connection.interruptionHandler = { service.terminate() }
        connection.resume()
        return true
    }
}

guard !helperTeamIdentifier.isEmpty else {
    // An unsigned build has no team to check callers against; refuse to serve rather than serve anyone.
    FileHandle.standardError.write(Data("OpenFortiClientHelper: built without a team identifier, refusing to run.\n".utf8))
    exit(1)
}

let delegate = ListenerDelegate()
let listener = NSXPCListener(machServiceName: helperMachServiceName)
listener.setConnectionCodeSigningRequirement(
    "identifier \"io.github.openforticlient\" and anchor apple generic and certificate leaf[subject.OU] = \"\(helperTeamIdentifier)\"")
listener.delegate = delegate
listener.resume()
RunLoop.main.run()
