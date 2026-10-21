// Changes the settings of a macOS L2TP VPN service for the app (the "Edit" page of the macOS VPN card), as root.
// Only an existing L2TP service, only these settings (server, account, password, shared secret, send all traffic,
// PPP keep-alive), only checked values; everything else in the service stays as it is. Secrets are changed in the
// System keychain items macOS made for the service, data only, so their access lists (which system tools may
// read them) stay macOS's own. Never logs or reports a secret. See docs/architecture.md, "macOS VPN services".
//
// The checks and merges are static so the helper tests run them without root (tests/OpenFortiClient.Helper.Tests).

import Foundation
import Security
import SystemConfiguration

struct VpnSettingsRequest {
    let serviceId: String
    let server: String
    let account: String
    let sendAllTraffic: Bool
    let echoInterval: Int
    let echoFailures: Int

    /// nil keeps the saved one.
    let password: String?
    let sharedSecret: String?
}

enum VpnSettingsError: Error, CustomStringConvertible {
    case invalid(String)
    case notL2tp
    case failed(String)

    var description: String {
        switch self {
        case .invalid(let message): return message
        case .notL2tp: return "Only an L2TP VPN service set up in System Settings can be changed."
        case .failed(let message): return message
        }
    }
}

enum VpnSettingsWriter {
    static let echoIntervals = 5...120
    static let echoFailures = 1...30
    private static let maxText = 256
    private static let maxHost = 253

    /// The same rules as SystemVpnSettingsChange.Validate in the app's Core.
    static func check(_ request: VpnSettingsRequest) throws {
        let uuid = CharacterSet(charactersIn: "0123456789ABCDEFabcdef-")
        // Explicit closures, not `set.contains` passed as a function: with -O that picked an overload that
        // was never true (2026-10-21; the tests now build with -O like the helper).
        guard request.serviceId.count == 36, request.serviceId.unicodeScalars.allSatisfy({ uuid.contains($0) }) else {
            throw VpnSettingsError.invalid("Not a VPN service ID.")
        }

        let host = CharacterSet(charactersIn: "abcdefghijklmnopqrstuvwxyzABCDEFGHIJKLMNOPQRSTUVWXYZ0123456789.-:")
        guard !request.server.isEmpty, request.server.count <= maxHost, request.server.unicodeScalars.allSatisfy({ host.contains($0) }) else {
            throw VpnSettingsError.invalid("Server address must be a host name or an IP address.")
        }

        let control = CharacterSet.controlCharacters
        guard request.account.count <= maxText, !request.account.unicodeScalars.contains(where: { control.contains($0) }) else {
            throw VpnSettingsError.invalid("Account name is too long or contains control characters.")
        }

        guard echoIntervals.contains(request.echoInterval), echoFailures.contains(request.echoFailures) else {
            throw VpnSettingsError.invalid("Keep-alive must be \(echoIntervals.lowerBound) to \(echoIntervals.upperBound) seconds and \(echoFailures.lowerBound) to \(echoFailures.upperBound) failures.")
        }

        for secret in [request.password, request.sharedSecret].compactMap({ $0 }) where secret.count > maxText || secret.contains("\0") {
            throw VpnSettingsError.invalid("A password or shared secret is too long or contains a NUL character.")
        }
    }

    /// The PPP settings with only the allowed keys changed.
    static func mergedPpp(_ current: [String: Any], _ request: VpnSettingsRequest) -> [String: Any] {
        var ppp = current
        ppp["CommRemoteAddress"] = request.server
        ppp["AuthName"] = request.account
        ppp["LCPEchoEnabled"] = 1
        ppp["LCPEchoInterval"] = request.echoInterval
        ppp["LCPEchoFailure"] = request.echoFailures
        return ppp
    }

    /// The IPv4 settings with only "send all traffic" changed.
    static func mergedIpv4(_ current: [String: Any], _ request: VpnSettingsRequest) -> [String: Any] {
        var ipv4 = current
        ipv4["OverridePrimary"] = request.sendAllTraffic ? 1 : 0
        return ipv4
    }

    /// Writes the request into the service. Nothing is saved unless every part worked.
    static func write(_ request: VpnSettingsRequest) throws {
        try check(request)

        var auth: AuthorizationRef?
        guard AuthorizationCreate(nil, nil, [], &auth) == errAuthorizationSuccess,
              let prefs = SCPreferencesCreateWithAuthorization(nil, "OpenFortiClient" as CFString, nil, auth) else {
            throw VpnSettingsError.failed("Could not open the network settings.")
        }

        guard SCPreferencesLock(prefs, true) else {
            throw VpnSettingsError.failed("The network settings are in use (\(lastError())). Close System Settings → VPN and try again.")
        }

        // Unlocking without committing drops every change made below.
        defer { SCPreferencesUnlock(prefs) }

        guard let service = SCNetworkServiceCopy(prefs, request.serviceId as CFString),
              let ppp = SCNetworkServiceGetInterface(service),
              SCNetworkInterfaceGetInterfaceType(ppp) as String? == kSCNetworkInterfaceTypePPP as String,
              let inner = SCNetworkInterfaceGetInterface(ppp),
              SCNetworkInterfaceGetInterfaceType(inner) as String? == kSCNetworkInterfaceTypeL2TP as String else {
            throw VpnSettingsError.notL2tp
        }

        let current = SCNetworkInterfaceGetConfiguration(ppp) as? [String: Any] ?? [:]
        guard SCNetworkInterfaceSetConfiguration(ppp, mergedPpp(current, request) as CFDictionary) else {
            throw VpnSettingsError.failed("Could not change the VPN settings (\(lastError())).")
        }

        guard let ipv4 = (SCNetworkServiceCopyProtocols(service) as? [SCNetworkProtocol])?
                  .first(where: { SCNetworkProtocolGetProtocolType($0) as String? == kSCNetworkProtocolTypeIPv4 as String }),
              SCNetworkProtocolSetConfiguration(ipv4, mergedIpv4(SCNetworkProtocolGetConfiguration(ipv4) as? [String: Any] ?? [:], request) as CFDictionary) else {
            throw VpnSettingsError.failed("Could not change Send all traffic (\(lastError())).")
        }

        // The item names macOS uses: the service ID for the password, the service ID + ".SS" for the shared secret.
        let passwordItem = current["AuthPassword"] as? String ?? request.serviceId
        let ipsec = SCNetworkInterfaceGetExtendedConfiguration(ppp, "IPSec" as CFString) as? [String: Any] ?? [:]
        let secretItem = ipsec["SharedSecret"] as? String ?? "\(request.serviceId).SS"
        try Keychain.change(passwordItem, data: request.password, account: request.account, what: "password")
        try Keychain.change(secretItem, data: request.sharedSecret, account: nil, what: "shared secret")

        guard SCPreferencesCommitChanges(prefs), SCPreferencesApplyChanges(prefs) else {
            throw VpnSettingsError.failed("Could not save the network settings (\(lastError())).")
        }
    }

    private static func lastError() -> String {
        String(cString: SCErrorString(SCError()))
    }
}

/// The System keychain items of a VPN service. Uses the file-based keychain API, which is where macOS keeps
/// VPN secrets and their access lists.
private enum Keychain {
    /// Changes the item's data (if `data` is given) and its account (if given and different), in place.
    static func change(_ name: String, data: String?, account: String?, what: String) throws {
        var keychain: SecKeychain?
        guard SecKeychainOpen("/Library/Keychains/System.keychain", &keychain) == errSecSuccess, let keychain else {
            throw VpnSettingsError.failed("Could not open the System keychain.")
        }

        var item: SecKeychainItem?
        guard SecKeychainFindGenericPassword(keychain, UInt32(name.utf8.count), name, 0, nil, nil, nil, &item) == errSecSuccess, let item else {
            if data == nil {
                return
            }

            throw VpnSettingsError.failed("The VPN has no saved \(what) yet. Set it once in System Settings → VPN, then change it here.")
        }

        var accountBytes = Array((account ?? "").utf8)
        let status = accountBytes.withUnsafeMutableBytes { accountBuffer -> OSStatus in
            var attribute = SecKeychainAttribute(tag: SecItemAttr.accountItemAttr.rawValue, length: UInt32(accountBuffer.count), data: accountBuffer.baseAddress)
            return withUnsafeMutablePointer(to: &attribute) { pointer in
                var list = SecKeychainAttributeList(count: 1, attr: pointer)
                return withUnsafeMutablePointer(to: &list) { listPointer in
                    let bytes = Data((data ?? "").utf8)
                    return bytes.withUnsafeBytes { dataBuffer in
                        SecKeychainItemModifyAttributesAndData(
                            item,
                            account == nil ? nil : listPointer,
                            data == nil ? 0 : UInt32(bytes.count),
                            data == nil ? nil : dataBuffer.baseAddress)
                    }
                }
            }
        }

        guard status == errSecSuccess else {
            throw VpnSettingsError.failed("Could not change the \(what) in the System keychain (\(status)).")
        }
    }
}
