// The VPN services set up in macOS System Settings (L2TP, Cisco IPSec, PPTP): lists them, reads their status
// and switches them on and off, with SCNetworkConnection. SystemConfiguration's connection API is not available
// to the Catalyst app, but it is to this macOS library. Called from
// src/OpenFortiClient.App/Platforms/MacCatalyst/SystemVpns.cs, which is the delegate for systemVpnChanged:
// (an NSDictionary: id plus the keys of status(_:)).

import Foundation
import SystemConfiguration

@objc(OFCSystemVpns)
public final class OFCSystemVpns: NSObject {
    private let queue = DispatchQueue(label: "io.github.openforticlient.system-vpns")
    private let lock = NSLock()
    private var connections: [String: SCNetworkConnection] = [:]
    private weak var delegate: NSObject?

    @objc public func setDelegate(_ delegate: NSObject) {
        self.delegate = delegate
    }

    /// The enabled VPN services that can be switched on and off: dictionaries with id, name and kind
    /// ("L2TP", "IPSec" or "PPTP").
    @objc public func services() -> NSArray {
        guard let prefs = SCPreferencesCreate(nil, "OpenFortiClient" as CFString, nil),
              let all = SCNetworkServiceCopyAll(prefs) as? [SCNetworkService] else {
            return []
        }

        var result: [NSDictionary] = []
        for service in all where SCNetworkServiceGetEnabled(service) {
            guard let kind = Self.kind(of: service), let id = SCNetworkServiceGetServiceID(service) as String? else {
                continue
            }

            result.append(["id": id, "name": SCNetworkServiceGetName(service) as String? ?? kind, "kind": kind])
        }

        return result as NSArray
    }

    /// Keys: status ("disconnected", "connecting", "connected" or "disconnecting"); while connected, since
    /// (seconds since 1970) and address (IPv4); lastCause (NSNumber) when macOS reports why it last went down.
    /// Also starts watching the service, so later changes reach the delegate.
    @objc public func status(_ id: NSString) -> NSDictionary {
        // Read first: once a connection is scheduled for callbacks, every status read in the process says
        // disconnected until its first callback (which then reports the real status).
        let info = Self.describe(id as String)
        _ = connection(for: id as String)
        return info
    }

    /// Starts the service with the settings and credentials saved in macOS, as `scutil --nc start` does.
    /// It stays up when the app quits (linger). nil when macOS took the request, otherwise why not.
    @objc public func start(_ id: NSString) -> NSString? {
        guard let connection = connection(for: id as String) else {
            return "The service is no longer set up in System Settings"
        }

        return SCNetworkConnectionStart(connection, nil, true) ? nil : Self.lastError()
    }

    /// nil when macOS took the request, otherwise why not.
    @objc public func stop(_ id: NSString) -> NSString? {
        guard let connection = connection(for: id as String) else {
            return "The service is no longer set up in System Settings"
        }

        return SCNetworkConnectionStop(connection, true) ? nil : Self.lastError()
    }

    deinit {
        for connection in connections.values {
            SCNetworkConnectionSetDispatchQueue(connection, nil)
        }
    }

    private func connection(for id: String) -> SCNetworkConnection? {
        lock.lock()
        defer { lock.unlock() }
        if let existing = connections[id] {
            return existing
        }

        // The callback gets this object back through info; the app keeps it alive for its whole run.
        var context = SCNetworkConnectionContext(
            version: 0, info: Unmanaged.passUnretained(self).toOpaque(), retain: nil, release: nil, copyDescription: nil)
        let callback: SCNetworkConnectionCallBack = { connection, _, info in
            guard let info else {
                return
            }

            Unmanaged<OFCSystemVpns>.fromOpaque(info).takeUnretainedValue().changed(connection)
        }

        guard let connection = SCNetworkConnectionCreateWithServiceID(nil, id as CFString, callback, &context) else {
            return nil
        }

        SCNetworkConnectionSetDispatchQueue(connection, queue)
        connections[id] = connection
        return connection
    }

    private func changed(_ connection: SCNetworkConnection) {
        lock.lock()
        let serviceId = connections.first { $0.value === connection }?.key
        lock.unlock()
        guard let serviceId else {
            return
        }

        let info = NSMutableDictionary(dictionary: Self.describe(serviceId))
        info["id"] = serviceId
        send("systemVpnChanged:", info)
    }

    private static func kind(of service: SCNetworkService) -> String? {
        guard let interface = SCNetworkServiceGetInterface(service),
              let type = SCNetworkInterfaceGetInterfaceType(interface) as String? else {
            return nil
        }

        if type == kSCNetworkInterfaceTypeIPSec as String {
            return "IPSec"
        }

        // L2TP and PPTP are PPP over an inner interface.
        guard type == kSCNetworkInterfaceTypePPP as String,
              let inner = SCNetworkInterfaceGetInterface(interface),
              let innerType = SCNetworkInterfaceGetInterfaceType(inner) as String? else {
            return nil
        }

        if innerType == kSCNetworkInterfaceTypeL2TP as String {
            return "L2TP"
        }

        return innerType == kSCNetworkInterfaceTypePPTP as String ? "PPTP" : nil
    }

    /// Reads the status through a short-lived connection of its own, not the watched one.
    private static func describe(_ id: String) -> NSDictionary {
        guard let connection = SCNetworkConnectionCreateWithServiceID(nil, id as CFString, nil, nil) else {
            return ["status": "disconnected"]
        }

        let status = SCNetworkConnectionGetStatus(connection)
        let info = NSMutableDictionary()
        info["status"] = name(of: status)

        let extended = SCNetworkConnectionCopyExtendedStatus(connection) as NSDictionary? ?? [:]
        let link = (extended["PPP"] ?? extended["IPSec"]) as? NSDictionary
        if let cause = link?["LastCause"] as? NSNumber {
            info["lastCause"] = cause
        }

        if status == .connected {
            if let address = ((extended["IPv4"] as? NSDictionary)?["Addresses"] as? [String])?.first {
                info["address"] = address
            }

            // ConnectTime counts awake time since boot, like systemUptime (checked against pppd's log).
            if let connectTime = (link?["ConnectTime"] as? NSNumber)?.doubleValue, connectTime > 0 {
                let since = Date().addingTimeInterval(-(ProcessInfo.processInfo.systemUptime - connectTime))
                info["since"] = NSNumber(value: since.timeIntervalSince1970)
            }
        }

        return info
    }

    private static func name(of status: SCNetworkConnectionStatus) -> String {
        switch status {
        case .connecting: return "connecting"
        case .connected: return "connected"
        case .disconnecting: return "disconnecting"
        default: return "disconnected"
        }
    }

    private static func lastError() -> NSString {
        String(cString: SCErrorString(SCError())) as NSString
    }

    private func send(_ selector: String, _ argument: Any) {
        let sel = NSSelectorFromString(selector)
        if let delegate, delegate.responds(to: sel) {
            delegate.perform(sel, with: argument)
        }
    }
}
