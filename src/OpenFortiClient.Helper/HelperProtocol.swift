// The XPC interface between OpenFortiClient and its privileged helper. Compiled into both the helper
// and the app's native library (src/OpenFortiClient.Native). Deliberately narrow: start openfortivpn
// with a config, write a line to it (the OTP), stop it; and change the settings of a macOS L2TP VPN
// service, with typed values only. See docs/architecture.md, "Elevation".

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

    /// Changes an L2TP VPN service's settings (VpnSettingsWriter). A nil password or shared secret keeps the
    /// saved one. `reply` gets nil when saved, otherwise why not.
    func writeVpnSettings(
        serviceId: String, server: String, account: String, sendAllTraffic: Bool, echoInterval: Int, echoFailures: Int,
        password: String?, sharedSecret: String?, reply: @escaping (String?) -> Void)
}

/// What the helper reports back to the app.
@objc(OFCHelperClientProtocol)
protocol OFCHelperClientProtocol {
    func output(line: String)
    func exited(code: Int32)
}
