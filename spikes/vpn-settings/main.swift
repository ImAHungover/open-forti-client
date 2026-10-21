// Throwaway check for docs/roadmap.md "Edit macOS VPN settings", step 3.0: can root change an L2TP service's
// settings and secrets so that macOS still uses them? Run with sudo, only on a test service:
//
//   sudo ./vpn-settings <serviceID> acl                     the access lists of its password and shared secret
//   sudo ./vpn-settings <serviceID> write [--server S] [--account A] [--send-all 0|1] [--echo SECONDS FAILURES]
//   sudo OFC_PASSWORD=… OFC_SECRET=… ./vpn-settings <serviceID> write …   also changes the secrets (from the
//                                                           environment, never the command line)
//
// Secrets are changed in the existing System keychain items (data only), so their access lists stay as macOS
// made them. Prints application paths and settings, never a secret.

import Foundation
import Security
import SystemConfiguration

func fail(_ message: String) -> Never {
    FileHandle.standardError.write(Data("error: \(message)\n".utf8))
    exit(1)
}

let arguments = Array(CommandLine.arguments.dropFirst())
guard arguments.count >= 2 else { fail("usage: vpn-settings <serviceID> acl | write [options]") }
let serviceId = arguments[0]

// MARK: Keychain

func systemKeychain() -> SecKeychain {
    var keychain: SecKeychain?
    let status = SecKeychainOpen("/Library/Keychains/System.keychain", &keychain)
    guard status == errSecSuccess, let keychain else { fail("cannot open the System keychain (\(status))") }
    return keychain
}

func item(named name: String) -> SecKeychainItem? {
    var item: SecKeychainItem?
    let status = SecKeychainFindGenericPassword(systemKeychain(), UInt32(name.utf8.count), name, 0, nil, nil, nil, &item)
    return status == errSecSuccess ? item : nil
}

func describeAccess(of name: String) {
    guard let item = item(named: name) else {
        print("\(name): no item")
        return
    }

    var access: SecAccess?
    guard SecKeychainItemCopyAccess(item, &access) == errSecSuccess, let access else {
        print("\(name): no access list")
        return
    }

    var lists: CFArray?
    SecAccessCopyACLList(access, &lists)
    print("\(name):")
    for case let acl as SecACL in (lists as? [SecACL]) ?? [] {
        var applications: CFArray?
        var description: CFString?
        var prompt = SecKeychainPromptSelector()
        SecACLCopyContents(acl, &applications, &description, &prompt)
        let authorizations = (SecACLCopyAuthorizations(acl) as? [String]) ?? []
        let paths = ((applications as? [SecTrustedApplication]) ?? []).map { app -> String in
            var data: CFData?
            SecTrustedApplicationCopyData(app, &data)
            return (data as Data?).flatMap { String(data: $0, encoding: .utf8)?.trimmingCharacters(in: CharacterSet(charactersIn: "\0")) } ?? "?"
        }
        print("  \(authorizations.joined(separator: ",")) -> \(applications == nil ? "any application" : paths.joined(separator: ", "))")
    }
}

func changeSecret(named name: String, to value: String) {
    guard let item = item(named: name) else { fail("\(name): no keychain item; set it once in System Settings") }
    let data = Data(value.utf8)
    let status = data.withUnsafeBytes { SecKeychainItemModifyAttributesAndData(item, nil, UInt32(data.count), $0.baseAddress) }
    guard status == errSecSuccess else { fail("\(name): could not change it (\(status))") }
    print("\(name): changed")
}

// MARK: Settings

func write(_ options: [String]) {
    var auth: AuthorizationRef?
    guard AuthorizationCreate(nil, nil, [], &auth) == errAuthorizationSuccess,
          let prefs = SCPreferencesCreateWithAuthorization(nil, "OpenFortiClient" as CFString, nil, auth) else {
        fail("cannot open the network preferences")
    }

    guard SCPreferencesLock(prefs, true) else { fail("cannot lock the network preferences: \(String(cString: SCErrorString(SCError())))") }
    defer { SCPreferencesUnlock(prefs) }

    guard let service = SCNetworkServiceCopy(prefs, serviceId as CFString),
          let ppp = SCNetworkServiceGetInterface(service),
          SCNetworkInterfaceGetInterfaceType(ppp) as String? == kSCNetworkInterfaceTypePPP as String,
          let inner = SCNetworkInterfaceGetInterface(ppp),
          SCNetworkInterfaceGetInterfaceType(inner) as String? == kSCNetworkInterfaceTypeL2TP as String else {
        fail("\(serviceId) is not an L2TP service")
    }

    var config = SCNetworkInterfaceGetConfiguration(ppp) as? [String: Any] ?? [:]
    var sendAll: Bool?
    var index = 0
    while index < options.count {
        switch options[index] {
        case "--server": config["CommRemoteAddress"] = options[index + 1]; index += 2
        case "--account": config["AuthName"] = options[index + 1]; index += 2
        case "--send-all": sendAll = options[index + 1] == "1"; index += 2
        case "--echo":
            config["LCPEchoEnabled"] = 1
            config["LCPEchoInterval"] = Int(options[index + 1])
            config["LCPEchoFailure"] = Int(options[index + 2])
            index += 3
        default: fail("unknown option \(options[index])")
        }
    }

    guard SCNetworkInterfaceSetConfiguration(ppp, config as CFDictionary) else { fail("cannot set the PPP settings") }

    if let sendAll,
       let ipv4 = (SCNetworkServiceCopyProtocols(service) as? [SCNetworkProtocol])?
           .first(where: { SCNetworkProtocolGetProtocolType($0) as String? == kSCNetworkProtocolTypeIPv4 as String }) {
        var ipConfig = SCNetworkProtocolGetConfiguration(ipv4) as? [String: Any] ?? [:]
        ipConfig["OverridePrimary"] = sendAll ? 1 : 0
        guard SCNetworkProtocolSetConfiguration(ipv4, ipConfig as CFDictionary) else { fail("cannot set Send all traffic") }
    }

    guard SCPreferencesCommitChanges(prefs), SCPreferencesApplyChanges(prefs) else {
        fail("cannot save: \(String(cString: SCErrorString(SCError())))")
    }

    print("settings saved: \(config["CommRemoteAddress"] ?? "-"), \(config["AuthName"] ?? "-"), echo \(config["LCPEchoInterval"] ?? "-") s x \(config["LCPEchoFailure"] ?? "-"), send all \(sendAll.map(String.init) ?? "unchanged")")

    let environment = ProcessInfo.processInfo.environment
    if let password = environment["OFC_PASSWORD"] {
        changeSecret(named: config["AuthPassword"] as? String ?? serviceId, to: password)
    }

    if let secret = environment["OFC_SECRET"] {
        let ipsec = SCNetworkInterfaceGetExtendedConfiguration(ppp, "IPSec" as CFString) as? [String: Any] ?? [:]
        changeSecret(named: ipsec["SharedSecret"] as? String ?? "\(serviceId).SS", to: secret)
    }
}

guard getuid() == 0 else { fail("run it with sudo") }
switch arguments[1] {
case "acl":
    describeAccess(of: serviceId)
    describeAccess(of: "\(serviceId).SS")
case "write":
    write(Array(arguments.dropFirst(2)))
default:
    fail("unknown command \(arguments[1])")
}
