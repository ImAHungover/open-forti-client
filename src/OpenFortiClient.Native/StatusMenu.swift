// The menu bar icon (NSStatusItem) of OpenFortiClient. Mac Catalyst apps cannot use AppKit
// directly, so this small AppKit library is built for macOS, copied into the app and loaded at
// runtime (src/OpenFortiClient.App/Platforms/MacCatalyst/StatusMenu.cs). It only draws the
// menu; every action goes back to the app through the delegate's selectors:
//   statusMenuConnect:(NSNumber profile index)  statusMenuDisconnect:  statusMenuOpen:  statusMenuQuit:
// See docs/architecture.md, "Menu bar icon".

import AppKit
import ObjectiveC

@objc(OFCStatusMenu)
public final class OFCStatusMenu: NSObject, NSMenuDelegate {
    private var statusItem: NSStatusItem?
    private weak var delegate: NSObject?
    private var info: [String: Any] = [:]

    /// Creates the icon. `delegate` receives the menu actions.
    @objc public func install(_ delegate: NSObject) {
        self.delegate = delegate
        let item = NSStatusBar.system.statusItem(withLength: NSStatusItem.variableLength)
        item.button?.setAccessibilityIdentifier("OpenFortiClientStatusItem")
        item.button?.toolTip = "OpenFortiClient"
        statusItem = item
        keepRunningWithoutWindows()
        render()
    }

    /// A Catalyst app quits when its last window closes. A VPN client must not: the tunnel stays up and
    /// the window comes back from the menu bar. UIKit's AppKit delegate answers this question, so give
    /// its class an answer of "no".
    private func keepRunningWithoutWindows() {
        guard let appDelegate = NSApp.delegate as? NSObject else { return }
        let cls: AnyClass = type(of: appDelegate)
        let selector = NSSelectorFromString("applicationShouldTerminateAfterLastWindowClosed:")
        let answer: @convention(block) (AnyObject, NSApplication) -> Bool = { _, _ in false }
        let imp = imp_implementationWithBlock(answer)
        if !class_addMethod(cls, selector, imp, "B@:@") {
            class_replaceMethod(cls, selector, imp, "B@:@")
        }
    }

    /// Keys: status (String), state ("connected" | "busy" | "reconnecting" | "error" | "disconnected"),
    /// profiles ([String]), canConnect (Bool), canDisconnect (Bool), disconnectTitle (String).
    @objc public func update(_ info: NSDictionary) {
        self.info = info as? [String: Any] ?? [:]
        render()
    }

    @objc public func terminate(_ sender: Any?) {
        NSApp.terminate(nil)
    }

    private func render() {
        guard let item = statusItem else { return }

        let state = info["state"] as? String ?? "disconnected"
        let symbol: String
        switch state {
        case "connected": symbol = "lock.shield.fill"
        case "busy": symbol = "lock.rotation"
        case "reconnecting", "error": symbol = "exclamationmark.shield"
        default: symbol = "lock.open"
        }
        let image = NSImage(systemSymbolName: symbol, accessibilityDescription: "OpenFortiClient: \(state)")
        image?.isTemplate = true
        item.button?.image = image

        let menu = NSMenu()
        let status = NSMenuItem(title: info["status"] as? String ?? "Not connected", action: nil, keyEquivalent: "")
        status.isEnabled = false
        menu.addItem(status)
        menu.addItem(.separator())

        let canConnect = info["canConnect"] as? Bool ?? false
        let profiles = info["profiles"] as? [String] ?? []
        for (index, name) in profiles.enumerated() {
            let entry = NSMenuItem(title: "Connect to \(name)", action: canConnect ? #selector(connect(_:)) : nil, keyEquivalent: "")
            entry.target = self
            entry.tag = index
            entry.isEnabled = canConnect
            menu.addItem(entry)
        }

        if info["canDisconnect"] as? Bool ?? false {
            let disconnect = NSMenuItem(title: info["disconnectTitle"] as? String ?? "Disconnect", action: #selector(disconnect(_:)), keyEquivalent: "")
            disconnect.target = self
            menu.addItem(disconnect)
        }

        menu.addItem(.separator())
        let open = NSMenuItem(title: "Open OpenFortiClient", action: #selector(open(_:)), keyEquivalent: "")
        open.target = self
        menu.addItem(open)
        let quit = NSMenuItem(title: "Quit OpenFortiClient", action: #selector(quit(_:)), keyEquivalent: "q")
        quit.target = self
        menu.addItem(quit)

        item.menu = menu
    }

    @objc private func connect(_ sender: NSMenuItem) {
        send("statusMenuConnect:", NSNumber(value: sender.tag))
    }

    @objc private func disconnect(_ sender: NSMenuItem) {
        send("statusMenuDisconnect:", nil)
    }

    @objc private func open(_ sender: NSMenuItem) {
        NSApp.activate(ignoringOtherApps: true)
        send("statusMenuOpen:", nil)
    }

    @objc private func quit(_ sender: NSMenuItem) {
        send("statusMenuQuit:", nil)
    }

    private func send(_ selector: String, _ argument: Any?) {
        let sel = NSSelectorFromString(selector)
        if let delegate, delegate.responds(to: sel) {
            delegate.perform(sel, with: argument)
        }
    }
}
