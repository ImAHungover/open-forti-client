// Registers the privileged helper with SMAppService. ServiceManagement is not available to the Catalyst
// app, but it is to this macOS library. Called from src/OpenFortiClient.App/Platforms/MacCatalyst/HelperControl.cs.

import Foundation
import ServiceManagement

@objc(OFCHelperControl)
public final class OFCHelperControl: NSObject {
    private let service = SMAppService.daemon(plistName: helperPlistName)

    /// "enabled", "requiresApproval", "notRegistered" or "notFound".
    @objc public func status() -> NSString {
        switch service.status {
        case .enabled: return "enabled"
        case .requiresApproval: return "requiresApproval"
        case .notFound: return "notFound"
        default: return "notRegistered"
        }
    }

    /// nil when it worked, otherwise why not. After this the user still approves it in System Settings.
    @objc public func register() -> NSString? {
        do {
            try service.register()
            return nil
        } catch {
            return error.localizedDescription as NSString
        }
    }

    @objc public func unregister() -> NSString? {
        do {
            try service.unregister()
            return nil
        } catch {
            return error.localizedDescription as NSString
        }
    }

    @objc public func openLoginItemsSettings() {
        SMAppService.openSystemSettingsLoginItems()
    }
}
