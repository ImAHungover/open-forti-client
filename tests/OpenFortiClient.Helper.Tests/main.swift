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

func newSession() -> (OpenFortiVpnSession, Recorder) {
    let session = OpenFortiVpnSession(runtimeDirectory: runtime, isAllowed: { $0 == fake.path })
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
    let leftovers = (try? FileManager.default.contentsOfDirectory(atPath: runtime.path)) ?? []
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
    let left = (try? FileManager.default.contentsOfDirectory(atPath: runtime.path)) ?? []
    check(left.count == 1 && left[0].hasSuffix(".key"), "the config is gone but the key stays while openfortivpn runs: \(left)")
    session.terminate()
    _ = recorder.waitFor { $0.exitCode != nil }
    check(((try? FileManager.default.contentsOfDirectory(atPath: runtime.path)) ?? []).isEmpty, "no key or config is left")
}

test("refuses an executable that is not openfortivpn") {
    let session = OpenFortiVpnSession(runtimeDirectory: runtime)
    do {
        try session.start(executable: "/bin/sh", config: "host = x\n", clientKey: nil, promptMarkers: [])
        check(false, "/bin/sh must be refused")
    } catch SessionError.executableNotAllowed {
    }
}

test("accepts only Homebrew's openfortivpn paths") {
    check(!OpenFortiVpnSession.isHomebrewOpenFortiVpn("/tmp/openfortivpn"), "/tmp is refused")
    check(!OpenFortiVpnSession.isHomebrewOpenFortiVpn("/opt/homebrew/bin/../../../tmp/openfortivpn"), "path tricks are refused")
    if FileManager.default.fileExists(atPath: "/opt/homebrew/bin/openfortivpn") {
        check(OpenFortiVpnSession.isHomebrewOpenFortiVpn("/opt/homebrew/bin/openfortivpn"), "the Homebrew symlink is accepted")
    }
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

try? FileManager.default.removeItem(at: root)
print(failures == 0 ? "Helper tests passed." : "Helper tests: \(failures) failure(s).")
exit(failures == 0 ? 0 : 1)
