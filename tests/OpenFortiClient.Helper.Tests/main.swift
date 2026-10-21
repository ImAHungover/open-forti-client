// Tests for the privileged helper's session logic (src/OpenFortiClient.Helper/OpenFortiVpnSession.swift),
// run as the normal user against a fake openfortivpn. Built and run by scripts/test-helper.sh.

import Foundation

var failures = 0

func check(_ condition: Bool, _ message: String, line: Int = #line) {
    if !condition {
        failures += 1
        print("  FAIL (line \(line)): \(message)")
    }
}

func test(_ name: String, _ body: () throws -> Void) {
    print("- \(name)")
    do {
        try body()
    } catch {
        failures += 1
        print("  FAIL: threw \(error)")
    }
}

/// Collects a session's output and waits for lines or the exit.
final class Recorder {
    private let lock = NSLock()
    private(set) var lines: [String] = []
    private(set) var exitCode: Int32?

    func attach(_ session: OpenFortiVpnSession) {
        session.onOutput = { line in self.lock.lock(); self.lines.append(line); self.lock.unlock() }
        session.onExit = { code in self.lock.lock(); self.exitCode = code; self.lock.unlock() }
    }

    func waitFor(_ condition: (Recorder) -> Bool, seconds: Double = 5) -> Bool {
        let until = Date().addingTimeInterval(seconds)
        while Date() < until {
            lock.lock(); let done = condition(self); lock.unlock()
            if done { return true }
            Thread.sleep(forTimeInterval: 0.05)
        }
        return false
    }
}

let root = FileManager.default.temporaryDirectory.appendingPathComponent("ofc-helper-tests-\(UUID().uuidString)")
let runtime = root.appendingPathComponent("run")
let fake = root.appendingPathComponent("openfortivpn")
try! FileManager.default.createDirectory(at: root, withIntermediateDirectories: true)

// Behaves like openfortivpn: prints its progress, asks for an OTP without a newline, stops on SIGTERM.
// It also reports the mode and content of its config, which only exists until its first line.
try! """
#!/bin/bash
trap 'echo "INFO:   Cancelling threads..."; exit 0' TERM
mode=$(stat -f %Lp "$2"); config=$(tr '\\n' ';' < "$2"); key=$(sed -n 's/^user-key = //p' "$2")
echo "MODE $mode CONFIG $config"
echo "SELF $0"
# Like the real one, the key is loaded again for each TLS connection, after the first output.
sleep 0.3
if [ -n "$key" ]; then echo "KEYMODE $(stat -f %Lp "$key") KEY $(head -n 1 "$key")"; fi
echo "INFO:   Connected to gateway."
printf "Two-factor authentication token: "
read otp
echo "OTP $otp"
echo "INFO:   Tunnel is up and running."
while true; do sleep 0.1; done
""".write(to: fake, atomically: true, encoding: .utf8)
chmod(fake.path, 0o755)

/// The files in the runtime folder, without the private copy of openfortivpn.
func runtimeFiles() -> [String] {
    ((try? FileManager.default.contentsOfDirectory(atPath: runtime.path)) ?? []).filter { !$0.hasSuffix(".openfortivpn") }
}

func newSession(verify: @escaping (URL) throws -> Void = { _ in }) -> (OpenFortiVpnSession, Recorder) {
    let session = OpenFortiVpnSession(runtimeDirectory: runtime, bundled: fake, verify: verify)
    let recorder = Recorder()
    recorder.attach(session)
    return (session, recorder)
}

let markers = ["Two-factor authentication token:"]

test("runs openfortivpn with a private config, streams output, forwards the OTP and stops on SIGTERM") {
    let (session, recorder) = newSession()
    try session.start(executable: fake.path, config: "host = vpn.example.com\npassword = s3cret\n", clientKey: nil, promptMarkers: markers)

    check(recorder.waitFor { $0.lines.contains { $0.hasPrefix("Two-factor") } }, "the OTP prompt arrives without a newline")
    check(recorder.lines.first == "MODE 600 CONFIG host = vpn.example.com;password = s3cret;", "config is 0600 and complete: \(recorder.lines.first ?? "-")")
    let leftovers = runtimeFiles()
    check(leftovers.isEmpty, "the config is deleted once openfortivpn printed something: \(leftovers)")
    check(((try? FileManager.default.attributesOfItem(atPath: runtime.path)[.posixPermissions]) as? Int) == 0o700, "the runtime folder is 0700")

    session.write(line: "123456")
    check(recorder.waitFor { $0.lines.contains("INFO:   Tunnel is up and running.") }, "the tunnel comes up after the OTP")
    check(recorder.lines.contains("OTP 123456"), "the OTP reached openfortivpn")

    session.terminate()
    check(recorder.waitFor { $0.exitCode != nil }, "openfortivpn exits after SIGTERM")
    check(recorder.exitCode == 0, "it exited cleanly: \(String(describing: recorder.exitCode))")
    check(recorder.lines.contains("INFO:   Cancelling threads..."), "it got SIGTERM, not SIGKILL")
}

test("writes the client key next to the config, 0600, and removes both") {
    let (session, recorder) = newSession()
    try session.start(executable: fake.path, config: "host = vpn.example.com\nuser-key = /tmp/old.key\n", clientKey: "-----BEGIN PRIVATE KEY-----\nMIIE\n", promptMarkers: markers)

    check(recorder.waitFor { $0.lines.contains { $0.hasPrefix("KEYMODE") } }, "the key is readable by openfortivpn")
    check(recorder.lines.contains("KEYMODE 600 KEY -----BEGIN PRIVATE KEY-----"), "key is 0600: \(recorder.lines)")
    check(!(recorder.lines.first ?? "").contains("/tmp/old.key"), "the app's user-key line is replaced by the helper's own")
    let left = runtimeFiles()
    check(left.count == 1 && left[0].hasSuffix(".key"), "the config is gone but the key stays while openfortivpn runs: \(left)")
    session.terminate()
    _ = recorder.waitFor { $0.exitCode != nil }
    check(((try? FileManager.default.contentsOfDirectory(atPath: runtime.path)) ?? []).isEmpty, "no key or config is left")
}

test("refuses any executable but the bundled openfortivpn") {
    let (session, _) = newSession()
    for path in ["/bin/sh", "/opt/homebrew/bin/openfortivpn", root.appendingPathComponent("run/../openfortivpn-other").path] {
        do {
            try session.start(executable: path, config: "host = x\n", clientKey: nil, promptMarkers: [])
            check(false, "\(path) must be refused")
        } catch SessionError.executableNotAllowed {
        }
    }
}

test("runs a private copy of the bundled openfortivpn, checked first, and removes it afterwards") {
    var checked: URL?
    let (session, recorder) = newSession(verify: { checked = $0 })
    try session.start(executable: fake.path, config: "host = x\n", clientKey: nil, promptMarkers: markers)

    check(recorder.waitFor { $0.lines.contains { $0.hasPrefix("SELF ") } }, "openfortivpn started")
    let ran = recorder.lines.first { $0.hasPrefix("SELF ") }.map { String($0.dropFirst(5)) }
    check(checked != nil && checked?.path == ran, "the file that was checked is the one that ran: \(String(describing: checked)) vs \(ran ?? "-")")
    check(checked?.deletingLastPathComponent().path == runtime.path, "the copy is in the root-only runtime folder")
    session.terminate()
    _ = recorder.waitFor { $0.exitCode != nil }
    check(((try? FileManager.default.contentsOfDirectory(atPath: runtime.path)) ?? []).isEmpty, "the copy is removed when openfortivpn exits")
}

test("refuses a bundled openfortivpn that was replaced by a link") {
    let bundled = root.appendingPathComponent("bundled-openfortivpn")
    try FileManager.default.copyItem(at: fake, to: bundled)
    let (_, recorder) = newSession()
    let session = OpenFortiVpnSession(runtimeDirectory: runtime, bundled: bundled, verify: { _ in })
    recorder.attach(session)

    try FileManager.default.removeItem(at: bundled)
    try FileManager.default.createSymbolicLink(at: bundled, withDestinationURL: fake)
    do {
        try session.start(executable: bundled.path, config: "host = x\n", clientKey: nil, promptMarkers: markers)
        check(false, "a link in place of the bundled file must be refused")
    } catch SessionError.executableNotAllowed {
    }
    check(((try? FileManager.default.contentsOfDirectory(atPath: runtime.path)) ?? []).isEmpty, "nothing is left")
}

test("does not run openfortivpn when its signature check fails") {
    struct Tampered: Error {}
    let (session, recorder) = newSession(verify: { _ in throw Tampered() })
    do {
        try session.start(executable: fake.path, config: "host = x\n", clientKey: "KEY", promptMarkers: markers)
        check(false, "a failed check must refuse the start")
    } catch SessionError.notVerified {
    }
    Thread.sleep(forTimeInterval: 0.3)
    check(recorder.lines.isEmpty, "nothing ran: \(recorder.lines)")
    check(((try? FileManager.default.contentsOfDirectory(atPath: runtime.path)) ?? []).isEmpty, "no copy, config or key is left")
}

if let signedPath = ProcessInfo.processInfo.environment["HELPER_TESTS_SIGNED"],
   let team = ProcessInfo.processInfo.environment["HELPER_TESTS_TEAM"] {
    let signed = URL(fileURLWithPath: signedPath)

    /// A copy of the signed binary, changed by `alter`.
    func variant(_ name: String, _ alter: (URL) throws -> Void) throws -> URL {
        let copy = root.appendingPathComponent(name)
        try FileManager.default.copyItem(at: signed, to: copy)
        try alter(copy)
        return copy
    }

    func codesign(_ arguments: [String]) throws {
        let process = Process()
        process.executableURL = URL(fileURLWithPath: "/usr/bin/codesign")
        process.arguments = arguments
        process.standardError = FileHandle.nullDevice
        try process.run()
        process.waitUntilExit()
    }

    func refused(_ url: URL) -> Bool {
        (try? CodeSignature.verifyOpenFortiVpn(url, team: team)) == nil
    }

    test("the signature check accepts openfortivpn signed by the team, and an unchanged copy of it") {
        try CodeSignature.verifyOpenFortiVpn(signed, team: team)
        check(!refused(try variant("copy") { _ in }), "the helper checks a copy, which keeps the signature")
    }

    test("the signature check refuses another team, another identifier, ad hoc signing and altered code") {
        check((try? CodeSignature.verifyOpenFortiVpn(signed, team: "ABCDE12345")) == nil, "another team is refused")
        check(refused(try variant("other-identifier") { try codesign(["--force", "--sign", "-", "--identifier", "io.github.openforticlient.other", $0.path]) }),
              "a different identifier is refused")
        check(refused(try variant("ad-hoc") { try codesign(["--force", "--sign", "-", "--identifier", "io.github.openforticlient.openfortivpn", $0.path]) }),
              "ad hoc signing with the right identifier is refused")
        check(refused(try variant("unsigned") { try codesign(["--remove-signature", $0.path]) }), "an unsigned binary is refused")
        check(refused(try variant("altered") { url in
            var bytes = try Data(contentsOf: url)
            bytes[0x1000] ^= 0xFF
            try bytes.write(to: url)
        }), "a changed byte in the signed code is refused")
    }
} else {
    print("- (skipped: the signature check tests need Signing.local.props)")
}

test("refuses config keys that could run other programs as root") {
    for key in ["pppd-plugin", "pppd-call", "pppd-ipparam", "persistent"] {
        do {
            _ = try OpenFortiVpnSession.checkedLines("host = x\n\(key) = /tmp/evil\n")
            check(false, "\(key) must be refused")
        } catch SessionError.configKeyNotAllowed(let refused) {
            check(refused == key, "the refused key is named: \(refused)")
        }
    }
}

test("refuses a second start on the same connection") {
    let (session, recorder) = newSession()
    try session.start(executable: fake.path, config: "host = x\n", clientKey: nil, promptMarkers: markers)
    do {
        try session.start(executable: fake.path, config: "host = x\n", clientKey: nil, promptMarkers: markers)
        check(false, "a second start must be refused")
    } catch SessionError.alreadyRunning {
    }
    session.terminate()
    _ = recorder.waitFor { $0.exitCode != nil }
}

// MARK: VPN settings (VpnSettingsWriter): the checks and merges, without root

let validRequest = VpnSettingsRequest(
    serviceId: "7A1C2E44-5B6D-4F80-9A3B-2C4D5E6F7081", server: "vpn.example.com", account: "user",
    sendAllTraffic: true, echoInterval: 10, echoFailures: 3, password: "s3cret-value", sharedSecret: nil)

func refused(_ request: VpnSettingsRequest) -> String? {
    do {
        try VpnSettingsWriter.check(request)
        return nil
    } catch {
        return "\(error)"
    }
}

test("accepts valid VPN settings") {
    check(refused(validRequest) == nil, "valid settings must pass")
}

test("refuses VPN settings that are not plain values") {
    func with(_ change: (inout [String: Any]) -> Void) -> VpnSettingsRequest {
        var v: [String: Any] = ["id": validRequest.serviceId, "server": validRequest.server, "account": validRequest.account,
                                "interval": validRequest.echoInterval, "failures": validRequest.echoFailures, "password": validRequest.password as Any]
        change(&v)
        return VpnSettingsRequest(
            serviceId: v["id"] as! String, server: v["server"] as! String, account: v["account"] as! String, sendAllTraffic: true,
            echoInterval: v["interval"] as! Int, echoFailures: v["failures"] as! Int, password: v["password"] as? String, sharedSecret: nil)
    }

    check(refused(with { $0["id"] = "../../etc/passwd" }) != nil, "a service ID that is not a UUID")
    check(refused(with { $0["server"] = "" }) != nil, "an empty server")
    check(refused(with { $0["server"] = "vpn.example.com; rm -rf /" }) != nil, "a server with shell characters")
    check(refused(with { $0["account"] = "user\nAuthPassword=x" }) != nil, "an account with a newline")
    check(refused(with { $0["interval"] = 4 }) != nil, "a keep-alive interval below 5 s")
    check(refused(with { $0["failures"] = 31 }) != nil, "more than 30 keep-alive failures")
    check(refused(with { $0["password"] = "a\0b" }) != nil, "a password with NUL")
}

test("never puts a secret into an error") {
    let bad = VpnSettingsRequest(
        serviceId: validRequest.serviceId, server: "", account: "user", sendAllTraffic: true,
        echoInterval: 10, echoFailures: 3, password: "s3cret-value", sharedSecret: "shared-value")
    let message = refused(bad) ?? ""
    check(!message.contains("s3cret-value") && !message.contains("shared-value"), "the error must not contain a secret: \(message)")
}

test("changes only the allowed VPN settings and keeps the rest") {
    let current: [String: Any] = ["CommRemoteAddress": "old.example.com", "AuthName": "old", "AuthPassword": "ITEM",
                                  "AuthPasswordEncryption": "Keychain", "LCPEchoInterval": 20, "LCPEchoFailure": 15,
                                  "DisconnectOnLogout": 1, "Logfile": "/var/log/ppp.log"]
    let ppp = VpnSettingsWriter.mergedPpp(current, validRequest)
    check(ppp["CommRemoteAddress"] as? String == "vpn.example.com", "server")
    check(ppp["AuthName"] as? String == "user", "account")
    check(ppp["LCPEchoInterval"] as? Int == 10 && ppp["LCPEchoFailure"] as? Int == 3 && ppp["LCPEchoEnabled"] as? Int == 1, "keep-alive")
    check(ppp["AuthPassword"] as? String == "ITEM" && ppp["AuthPasswordEncryption"] as? String == "Keychain", "the password stays a keychain reference")
    check(ppp["DisconnectOnLogout"] as? Int == 1 && ppp["Logfile"] as? String == "/var/log/ppp.log", "other settings stay")
    check(ppp.count == current.count + 1, "only LCPEchoEnabled is added")

    let ipv4 = VpnSettingsWriter.mergedIpv4(["ConfigMethod": "PPP", "OverridePrimary": 0], validRequest)
    check(ipv4["OverridePrimary"] as? Int == 1 && ipv4["ConfigMethod"] as? String == "PPP", "send all traffic, nothing else")
}

try? FileManager.default.removeItem(at: root)
print(failures == 0 ? "Helper tests passed." : "Helper tests: \(failures) failure(s).")
exit(failures == 0 ? 0 : 1)
