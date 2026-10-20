// The privileged helper's check of the openfortivpn it is about to run as root. Separate from main.swift so
// the helper tests can run it against signed, unsigned and altered binaries (scripts/test-helper.sh).

import Foundation
import Security

enum CodeSignature {
    /// Accepts only the openfortivpn the app build signed: its identifier, by this team, unaltered.
    /// Throws why not.
    static func verifyOpenFortiVpn(_ executable: URL, team: String) throws {
        let requirementText = "identifier \"io.github.openforticlient.openfortivpn\" and anchor apple generic and certificate leaf[subject.OU] = \"\(team)\""
        var code: SecStaticCode?
        var requirement: SecRequirement?
        var status = SecStaticCodeCreateWithPath(executable as CFURL, [], &code)
        if status == errSecSuccess {
            status = SecRequirementCreateWithString(requirementText as CFString, [], &requirement)
        }
        if status == errSecSuccess, let code, let requirement {
            status = SecStaticCodeCheckValidity(code, SecCSFlags(rawValue: kSecCSStrictValidate | kSecCSCheckAllArchitectures), requirement)
        }
        guard status == errSecSuccess else {
            throw SessionError.launchFailed(SecCopyErrorMessageString(status, nil) as String? ?? "OSStatus \(status)")
        }
    }
}
