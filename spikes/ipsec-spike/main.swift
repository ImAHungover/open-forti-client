import Foundation
@preconcurrency import NetworkExtension
import Security

// Throwaway tool for docs/ipsec-spike.md: creates, starts and removes a macOS IKEv2 configuration
// through NEVPNManager, the way an IPsec engine would. Run the executable inside the built app:
//
//   IPsecSpike.app/Contents/MacOS/IPsecSpike save <config.json>   save the configuration (macOS asks to allow it)
//   IPsecSpike.app/Contents/MacOS/IPsecSpike start [seconds]       connect and print status changes (default 30 s)
//   IPsecSpike.app/Contents/MacOS/IPsecSpike stop | status | show | remove
//
// The EAP password, if any, comes from the environment variable OFC_SPIKE_PASSWORD, never from the
// command line or the JSON file. It is stored in the login keychain under the service below.

let keychainService = "io.github.openforticlient.ipsec-spike"

struct Config: Decodable {
    var server: String
    var remoteIdentifier: String
    var localIdentifier: String?
    /// "eap" (username and password, the default), "certificate", or "none".
    var authentication: String?
    var username: String?
    /// IKE and child SA: "aes256" (default), "aes256gcm" or "chacha20poly1305"; "sha256" (default), "sha384" or "sha512"; DH 14 (default) to 21.
    var encryption: String?
    var integrity: String?
    var dhGroup: Int?
    var lifetimeMinutes: Int32?
    var useInternalSubnet: Bool?
    var enablePFS: Bool?
    var disableMOBIKE: Bool?
    var serverCertificateCommonName: String?
    var serverCertificateIssuerCommonName: String?
    var includeAllNetworks: Bool?
    var mtu: Int?
}

enum SpikeError: Error, CustomStringConvertible {
    case usage, invalid(String), keychain(OSStatus)
    var description: String {
        switch self {
        case .usage: return "usage: IPsecSpike save <config.json> | start [seconds] | stop | status | show | remove"
        case .invalid(let message): return message
        case .keychain(let status): return "keychain error \(status): \(SecCopyErrorMessageString(status, nil) as String? ?? "")"
        }
    }
}

func encryption(_ name: String) throws -> NEVPNIKEv2EncryptionAlgorithm {
    switch name.lowercased() {
    case "aes256": return .algorithmAES256
    case "aes256gcm": return .algorithmAES256GCM
    case "chacha20poly1305": return .algorithmChaCha20Poly1305
    default: throw SpikeError.invalid("encryption \(name): use aes256, aes256gcm or chacha20poly1305 (macOS 26 has no DES, 3DES or SHA-1)")
    }
}

func integrity(_ name: String) throws -> NEVPNIKEv2IntegrityAlgorithm {
    switch name.lowercased() {
    case "sha256": return .SHA256
    case "sha384": return .SHA384
    case "sha512": return .SHA512
    default: throw SpikeError.invalid("integrity \(name): use sha256, sha384 or sha512")
    }
}

/// A persistent reference to the password in the login keychain, which NEVPNManager needs.
func passwordReference(account: String, password: String) throws -> Data {
    let query: [String: Any] = [kSecClass as String: kSecClassGenericPassword,
                                kSecAttrService as String: keychainService,
                                kSecAttrAccount as String: account]
    SecItemDelete(query as CFDictionary)
    var add = query
    add[kSecValueData as String] = Data(password.utf8)
    add[kSecReturnPersistentRef as String] = true
    var result: CFTypeRef?
    let status = SecItemAdd(add as CFDictionary, &result)
    guard status == errSecSuccess, let reference = result as? Data else { throw SpikeError.keychain(status) }
    return reference
}

func makeProtocol(_ config: Config) throws -> NEVPNProtocolIKEv2 {
    let ike = NEVPNProtocolIKEv2()
    ike.serverAddress = config.server
    ike.remoteIdentifier = config.remoteIdentifier
    ike.localIdentifier = config.localIdentifier
    let authentication = config.authentication ?? "eap"
    switch authentication {
    case "eap":
        guard let username = config.username else { throw SpikeError.invalid("eap needs a username") }
        ike.authenticationMethod = .none
        ike.useExtendedAuthentication = true
        ike.username = username
        if let password = ProcessInfo.processInfo.environment["OFC_SPIKE_PASSWORD"] {
            ike.passwordReference = try passwordReference(account: username, password: password)
        }
    case "certificate":
        ike.authenticationMethod = .certificate
    case "none":
        ike.authenticationMethod = .none
    default:
        throw SpikeError.invalid("authentication \(authentication): use eap, certificate or none")
    }
    let dhGroup = config.dhGroup ?? 14
    for parameters in [ike.ikeSecurityAssociationParameters, ike.childSecurityAssociationParameters] {
        parameters.encryptionAlgorithm = try encryption(config.encryption ?? "aes256")
        parameters.integrityAlgorithm = try integrity(config.integrity ?? "sha256")
        guard let group = NEVPNIKEv2DiffieHellmanGroup(rawValue: dhGroup), (14...21).contains(dhGroup) else {
            throw SpikeError.invalid("dhGroup \(dhGroup): use 14 to 21")
        }
        parameters.diffieHellmanGroup = group
        if let minutes = config.lifetimeMinutes { parameters.lifetimeMinutes = minutes }
    }
    ike.useConfigurationAttributeInternalIPSubnet = config.useInternalSubnet ?? true
    ike.enablePFS = config.enablePFS ?? false
    ike.disableMOBIKE = config.disableMOBIKE ?? false
    ike.deadPeerDetectionRate = .medium
    ike.serverCertificateCommonName = config.serverCertificateCommonName
    ike.serverCertificateIssuerCommonName = config.serverCertificateIssuerCommonName
    ike.includeAllNetworks = config.includeAllNetworks ?? false
    if let mtu = config.mtu { ike.mtu = mtu }
    return ike
}

func describe(_ status: NEVPNStatus) -> String {
    switch status {
    case .invalid: return "invalid (no configuration)"
    case .disconnected: return "disconnected"
    case .connecting: return "connecting"
    case .connected: return "connected"
    case .reasserting: return "reasserting"
    case .disconnecting: return "disconnecting"
    @unknown default: return "unknown (\(status.rawValue))"
    }
}

func log(_ message: String) {
    print("\(ISO8601DateFormatter().string(from: Date()))  \(message)")
}

func printLastDisconnectError(_ connection: NEVPNConnection) async {
    let error: Error? = await withCheckedContinuation { continuation in
        connection.fetchLastDisconnectError { continuation.resume(returning: $0) }
    }
    if let error = error as NSError? {
        log("last disconnect error: \(error.domain) \(error.code): \(error.localizedDescription)")
    }
}

/// Prints every status change for `seconds`, or until the tunnel is down again after having started.
func watch(_ connection: NEVPNConnection, seconds: Double) async {
    log("status: \(describe(connection.status))")
    let observer = NotificationCenter.default.addObserver(forName: .NEVPNStatusDidChange, object: connection, queue: nil) { _ in
        log("status: \(describe(connection.status))")
    }
    let deadline = Date().addingTimeInterval(seconds)
    var seenActive = false
    while Date() < deadline {
        try? await Task.sleep(nanoseconds: 250_000_000)
        switch connection.status {
        case .connecting, .connected, .reasserting, .disconnecting: seenActive = true
        case .disconnected, .invalid: if seenActive { break }
        @unknown default: break
        }
        if seenActive && (connection.status == .disconnected || connection.status == .invalid) { break }
    }
    NotificationCenter.default.removeObserver(observer)
    if connection.status == .connected, let date = connection.connectedDate {
        log("connected since \(date)")
    }
    await printLastDisconnectError(connection)
}

func run() async throws {
    let arguments = Array(CommandLine.arguments.dropFirst())
    guard let command = arguments.first else { throw SpikeError.usage }
    let manager = NEVPNManager.shared()
    try await manager.loadFromPreferences()

    switch command {
    case "save":
        guard arguments.count == 2 else { throw SpikeError.usage }
        let config = try JSONDecoder().decode(Config.self, from: Data(contentsOf: URL(fileURLWithPath: arguments[1])))
        manager.protocolConfiguration = try makeProtocol(config)
        manager.localizedDescription = "OpenFortiClient IPsec spike"
        manager.isEnabled = true
        try await manager.saveToPreferences()
        try await manager.loadFromPreferences()
        log("saved: \(config.server), remote ID \(config.remoteIdentifier); System Settings > VPN shows it")
    case "start":
        let seconds = arguments.count > 1 ? Double(arguments[1]) ?? 30 : 30
        guard manager.protocolConfiguration != nil else { throw SpikeError.invalid("no configuration: run save first") }
        if !manager.isEnabled {
            manager.isEnabled = true
            try await manager.saveToPreferences()
        }
        try manager.connection.startVPNTunnel()
        await watch(manager.connection, seconds: seconds)
    case "stop":
        manager.connection.stopVPNTunnel()
        await watch(manager.connection, seconds: 5)
    case "status":
        log("status: \(describe(manager.connection.status))")
        await printLastDisconnectError(manager.connection)
    case "show":
        guard let ike = manager.protocolConfiguration as? NEVPNProtocolIKEv2 else {
            log("no IKEv2 configuration saved")
            return
        }
        let sa = ike.ikeSecurityAssociationParameters
        log("server \(ike.serverAddress ?? "-"), remote ID \(ike.remoteIdentifier ?? "-"), local ID \(ike.localIdentifier ?? "-")")
        log("auth \(ike.authenticationMethod.rawValue), EAP \(ike.useExtendedAuthentication), user \(ike.username ?? "-"), password saved \(ike.passwordReference != nil)")
        log("IKE SA: encryption \(sa.encryptionAlgorithm.rawValue), integrity \(sa.integrityAlgorithm.rawValue), DH \(sa.diffieHellmanGroup.rawValue), lifetime \(sa.lifetimeMinutes) min")
        log("internal subnet \(ike.useConfigurationAttributeInternalIPSubnet), PFS \(ike.enablePFS), MOBIKE off \(ike.disableMOBIKE), all networks \(ike.includeAllNetworks), enabled \(manager.isEnabled)")
    case "remove":
        try await manager.removeFromPreferences()
        let query: [String: Any] = [kSecClass as String: kSecClassGenericPassword, kSecAttrService as String: keychainService]
        SecItemDelete(query as CFDictionary)
        log("removed the configuration and its keychain item")
    default:
        throw SpikeError.usage
    }
}

do {
    try await run()
} catch {
    let nsError = error as NSError
    FileHandle.standardError.write(Data("error: \(error) [\(nsError.domain) \(nsError.code)]\n".utf8))
    exit(1)
}
