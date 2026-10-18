// The XPC interface between OpenFortiClient and its privileged helper. Compiled into both the helper
// and the app's native library (src/OpenFortiClient.Native). Deliberately narrow: start openfortivpn
// with a config, write a line to it (the OTP), stop it. See docs/architecture.md, "Elevation".

import Foundation

let helperMachServiceName = "io.github.openforticlient.helper"
let helperPlistName = "io.github.openforticlient.helper.plist"

/// What the app may ask of the helper.
@objc(OFCHelperProtocol)
protocol OFCHelperProtocol {
    /// Starts openfortivpn. `reply` gets nil when it started, otherwise why not.
    func start(executable: String, config: String, clientKey: String?, promptMarkers: [String], reply: @escaping (String?) -> Void)
    func write(line: String)
    func terminate()
}

/// What the helper reports back to the app.
@objc(OFCHelperClientProtocol)
protocol OFCHelperClientProtocol {
    func output(line: String)
    func exited(code: Int32)
}
