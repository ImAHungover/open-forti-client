# Architecture

This document describes how open-forti-client is put together. For the why, see [vision.md](vision.md). For the screens, see [raw/wireframes/flows.md](raw/wireframes/flows.md).

## Overview

The app is a .NET MAUI application running on macOS through Mac Catalyst. It does not do any VPN networking itself. Instead it drives [openfortivpn](https://github.com/adrienverge/openfortivpn), which needs root rights, through a small privileged layer.

```
┌─────────────────────────────── user session ───────────────────────────────┐
│                                                                            │
│  OpenFortiClient.App (.NET MAUI, Mac Catalyst)                             │
│  ┌────────────┐  ┌────────────┐  ┌──────────────┐  ┌──────────────────┐    │
│  │   Views    │→ │ ViewModels │→ │   Services   │→ │   IVpnEngine     │    │
│  │  (XAML)    │  │  (MVVM)    │  │ profiles,    │  │ OpenFortiVpn-    │    │
│  └────────────┘  └────────────┘  │ secrets,     │  │ Engine           │    │
│                                  │ logs, notif. │  └────────┬─────────┘    │
│                                  └──────────────┘           │              │
└─────────────────────────────────────────────────────────────┼──────────────┘
                                                              │ start / stop
                                                              ▼
┌──────────────────────────────── root ───────────────────────────────────────┐
│  Elevation layer (MVP: sudoers rule  →  later: SMAppService helper daemon)  │
│                               │                                             │
│                               ▼                                             │
│  openfortivpn  ──spawns──▶  pppd  ──▶  ppp interface, routes, DNS           │
└─────────────────────────────────────────────────────────────────────────────┘
                               │ TLS 443 (follows system routing table)
                               ▼
                       FortiGate SSL-VPN gateway
```

## Solution layout

```
src/
  OpenFortiClient.App/          .NET MAUI app (net10.0-maccatalyst)
    Views/                      ConnectPage, ConnectionEditPage, SettingsPage,
                                NotificationsPage, AboutPage
    ViewModels/                 one per page (CommunityToolkit.Mvvm)
    Services/                   VpnSession, SecureStorageSecretStore, ShellDialogService
      EndToEnd/                 Debug-only test mode: scripted openfortivpn, file secrets
    Platforms/MacCatalyst/      entry point, Info.plist, entitlements, NativeBridge, StatusMenu,
                                HelperControl, HelperLauncher, UserNotificationsNotifier, PathNetworkMonitor
  OpenFortiClient.Native/       Swift: menu bar icon, helper client (XPC) and control (SMAppService),
                                built into libOFCNative.dylib
  OpenFortiClient.Helper/       Swift: the privileged helper (launch daemon) and its XPC protocol
  OpenFortiClient.Core/         plain .NET 10 library, no UI dependencies
    Engine/                     IVpnEngine, VpnState, IPrivilegedLauncher, SudoLauncher
      OpenFortiVpn/             OpenFortiVpnEngine, output parser, config writer
    Profiles/                   VpnProfile, GatewayAddress, JsonProfileStore
    Settings/                   AppSettings, JsonSettingsStore, ConfigurationBackupService
    Secrets/                    ISecretStore
    Logging/                    FileAppLog (rotation, export), LogSanitizer
    Notifications/              JsonNotificationHistory
tests/
  OpenFortiClient.Core.Tests/   unit tests (output parsing, engine state machine, config writing)
  OpenFortiClient.App.UiTests/  end-to-end tests through Appium (development.md, "End-to-end UI tests")
```

Keeping `Core` free of MAUI means the engine and parsing logic can be tested without a UI, and a command-line or Windows front end could reuse it later.

## Main components

### UI (MAUI, MVVM)

- Uses the MVVM pattern with [CommunityToolkit.Mvvm](https://learn.microsoft.com/dotnet/communitytoolkit/mvvm/).
- Has one page per screen in [flows.md](raw/wireframes/flows.md): Connect, Add/Edit Connection, Settings, Notifications, About.
- Uses a Shell with top tabs (home, settings, notifications, about), like the FortiClient header.
- Has a **menu bar icon**; see [Menu bar icon](#menu-bar-icon).

### VPN engine

All VPN work goes through one interface, so openfortivpn can be replaced later:

```csharp
public interface IVpnEngine
{
    VpnState State { get; }
    event EventHandler<VpnState> StateChanged;
    event EventHandler<string> LogLine;
    event EventHandler<GatewayFailure> GatewayFailed;   // a gateway failed and the next one is tried

    Task ConnectAsync(VpnProfile profile, VpnCredentials credentials, CancellationToken ct);
    Task DisconnectAsync();
}
```

`OpenFortiVpnEngine` is the first implementation. It:

1. Builds the openfortivpn config (host, port, username, password, certificate options) as text.
2. Hands it, with the client key from the Keychain, to an `IPrivilegedLauncher` (the elevation layer, below) as a `LaunchRequest`. The launcher decides where the config is written: the helper keeps it on a disk only root can read, and `SudoLauncher` uses a `0600` file in a `0700` folder (`LaunchFiles`).
3. Reads stdout and stderr line by line, masks secrets, sends each line to the log, and maps known lines to state changes.
4. The launcher deletes the config at the first output line (openfortivpn has read it by then) and the key when openfortivpn exits.
5. Disconnects by sending `SIGTERM` to the process, which tears down `pppd`, the routes and the DNS settings. If openfortivpn still runs after the disconnect timeout (10 s), it gets a second `SIGTERM`, which interrupts a logout stuck on an unreachable gateway. If it still runs after that, the state becomes Error ("did not stop"). It never gets `SIGKILL`, which would leave routes and DNS behind.
6. Treats each connect attempt as cancellable: Disconnect works at any point, also between two gateways when no openfortivpn is running. A new attempt waits, at most twice the disconnect timeout and showing Connecting, for an openfortivpn left over from a lost tunnel to exit, and never runs two at once.

`IPrivilegedLauncher` hides how root rights are obtained: the privileged helper or sudo. Input prompts such as the OTP prompt have no trailing newline, so the launcher is told which prompt texts to deliver straight away.

In the app, `VpnSession` sits between the view models and the engine. It reads and saves passwords in the Keychain, shows the OTP and certificate-trust prompts, and writes the log and notification history.

**Several gateways:** openfortivpn accepts only one host. The engine tries the profile's gateways in order and moves to the next one after a timeout or connection error. This matches FortiClient's behaviour.

### Connection state

```
              Connect
Disconnected ─────────▶ Connecting ──▶ Authenticating ──▶ Connected
     ▲                       │                │               │
     │                       ▼                ▼               │ Disconnect /
     │                  ┌─────────────── Error ◀──────────────┤ tunnel drop
     │                  │                                     ▼
     └──────────────────┴────────────────────────────── Disconnecting
```

The engine parses openfortivpn's output to tell which state it is in. The strings were taken from the openfortivpn 1.24.1 binary and live only in `OpenFortiVpnOutputParser`:

| openfortivpn output | State or action |
|---|---|
| `Connected to gateway.` | Authenticating. The gateway timeout stops and the tunnel setup timeout starts |
| `Tunnel is up and running.` | Connected |
| `Gateway certificate validation failed, and the certificate digest is not in the local whitelist` + `--trusted-cert <digest>` | Error (`UntrustedCertificate`, with the digest). The app asks the user, saves the digest to the profile and reconnects |
| `Two-factor authentication token:` / `Please enter one-time password:` | Ask the user for the OTP and write it to stdin |
| `Could not authenticate to gateway` / `… to the gateway` | Error, no other gateway is tried |
| `connect: …`, `getaddrinfo: …`, `SSL_connect: …`, `Could not get route to gateway` | Try the next gateway, or Error |
| `Could not get VPN configuration (…)` / `Could not start tunnel (…)` | The login worked but the tunnel did not. Try the next gateway, or Error |
| `Closed connection to gateway.` / `Logged out.` / `Cancelling threads...` before the tunnel is up | Stop the process (openfortivpn would otherwise keep trying to log out), then try the next gateway, or Error |
| No `Connected to gateway.` within the gateway timeout (30 s by default) | Stop the process and try the next gateway |
| No `Tunnel is up and running.` within the tunnel setup timeout (45 s after `Connected to gateway.`, paused while the user types an OTP) | Stop the process and try the next gateway |
| `sudo: a password is required`, `not spawned with root privileges` | Error: the sudoers rule is missing |
| `Cancelling threads...` / `Closed connection to gateway.` while connected, without a disconnect request | Error ("connection lost") straight away, then stop the process. Seen with openfortivpn 1.24.1: after the link drops it can keep running for a long time, trying to log out |
| Process exits while connected | Disconnected if the user asked for it, otherwise Error |

### Elevation (running as root)

openfortivpn must run as root because it starts `pppd` and changes routes and DNS. `ElevationLauncher` picks the way:

| Build | Launcher | How |
|---|---|---|
| Release | `HelperLauncher` | The **privileged helper**, a launch daemon the app registers with `SMAppService`. The user approves it once in System Settings → General → Login Items |
| Debug | `SudoLauncher` | `sudo -n openfortivpn …` with a NOPASSWD sudoers rule (`scripts/install-sudoers-rule.sh`). Settings → "Use the helper in this development build" switches to the helper |

**The helper** ([src/OpenFortiClient.Helper](../src/OpenFortiClient.Helper/)) is a small Swift program, `Contents/MacOS/OpenFortiClientHelper`, with its launchd plist in `Contents/Library/LaunchDaemons`. It serves one XPC mach service, `io.github.openforticlient.helper`, and its job is deliberately narrow:

```
start(executable, config, clientKey, promptMarkers) → nil or the reason it refused
write(line)     // the OTP
terminate()     // SIGTERM
→ output(line), exited(code)   reported back to the app
```

Its checks:

| Check | How |
|---|---|
| Only our app may connect | `setConnectionCodeSigningRequirement`: identifier `io.github.openforticlient`, Apple-issued certificate, same team ID. The app checks the helper the same way. A build without a team ID refuses to run |
| Only openfortivpn may run | The executable must resolve (after symlinks) to Homebrew's openfortivpn under `/opt/homebrew` or `/usr/local` |
| Only known config keys | host, port, username, password, realm, trusted-cert, user-cert, user-key, set-dns, set-routes. Anything else (`pppd-plugin`, `pppd-call`, …) is refused, because it could make root run another program |
| Secrets stay root-only | The config and client key are written as 0600 files under `/var/run/openforticlient` (0700, root). The config goes at openfortivpn's first output line; the key when openfortivpn exits, because openfortivpn loads it again for every TLS connection |
| Nothing is left running | When the app's connection ends (quit, crash), the helper stops openfortivpn |

The app talks to the helper through `OFCHelperClient` and registers it through `OFCHelperControl` (SMAppService), both in the native library ([src/OpenFortiClient.Native](../src/OpenFortiClient.Native/)), because the Catalyst app cannot use XPC to a daemon or ServiceManagement directly.

### Profiles and settings

Profiles are stored as JSON in `~/Library/Application Support/OpenFortiClient/profiles.json`. The fields mirror the FortiClient "Edit VPN Connection" screen:

```csharp
public sealed record VpnProfile
{
    public Guid Id { get; init; }
    public required string Name { get; init; }
    public string? Description { get; init; }
    public IReadOnlyList<Uri> Gateways { get; init; }            // tried in order
    public AuthMode AuthMode { get; init; }                      // PromptOnLogin | SaveLogin
    public string? Username { get; init; }
    public string? ClientCertificatePath { get; init; }          // PEM file, openfortivpn user-cert
    public string? ClientKeyPath { get; init; }                  // PEM file, openfortivpn user-key
    public IReadOnlyList<string> TrustedCertDigests { get; init; } // pinned gateway certificates
    public bool AutoConnect { get; init; }
    public bool AlwaysUp { get; init; }
}
```

openfortivpn reads client certificates from PEM files, so the MVP stores file paths. Using Keychain identities instead is a later step.

App settings (log level, the "do not warn about invalid server certificate" option, and similar) live in a separate `settings.json`. **Backup and restore** exports both files, without secrets.

### Secrets

- Passwords and client-certificate private keys are stored in the **macOS Keychain**, keyed by profile ID. Saving a connection with a key file moves the key into the Keychain; the profile then keeps no path to it (`ClientKeyInKeychain`). Deleting the connection removes both.
- On Mac Catalyst the Keychain needs the `keychain-access-groups` entitlement, so saved passwords only work in a signed build ([development.md](development.md#code-signing)). If the Keychain fails, the app logs it and still connects.
- They are never written to `profiles.json` or the logs, and never passed as command-line arguments (those are visible in `ps`).
- With the helper (release builds), the password and key reach the disk only as root-only files under `/var/run/openforticlient`, for as long as openfortivpn needs them. With sudo (development builds), they are `0600` files in the user's `run` folder for the same time.
- Backups hold no secrets: after a restore, a connection whose key was in the Keychain asks for the key file again.

### Reconnect (always up)

A connection marked **Always up** reconnects by itself when the tunnel is lost. `ReconnectSupervisor` in Core holds the rules:

- It acts on a lost tunnel only: Error coming straight from Connected, after a manual connect of an always-up profile.
- It waits **5 s, 10 s, 30 s, then every 60 s** before each attempt, and starts again at 5 s after a successful reconnect. The delays are a hidden setting (`reconnectDelaysSeconds`), which the UI tests make short.
- A **network change** skips the wait. `PathNetworkMonitor` (`NWPathMonitor`, collected over 2 s) reports what changed, and `NetworkChangePolicy` in Core decides whether it matters. It counts only what can make the gateway reachable again: the network coming back, a new interface, or another VPN coming up (the company L2TP VPN's `ppp0`). Interfaces going away never count, and neither does a tunnel interface (`ppp`, `utun`, `ipsec`) appearing while openfortivpn starts, because that one is the app's own. A change that arrives during an attempt makes a failure of that attempt retry at once.
- It **pauses**, with a banner, on errors only a person can fix: wrong password, untrusted certificate, OTP not given, invalid profile, openfortivpn missing, no root access.
- **Disconnect** on the Connect screen becomes **Stop reconnecting** while it waits. Connect stays available, in the window and the menu bar, and replaces the waiting reconnect.
- Reconnecting stops, with a "Reconnecting stopped." row, when the user stops it, when the connection is deleted, or when Always up is turned off for it.
- The credentials of the last manual connect stay in memory for the reconnects (the saved password, or the one typed in this session). An OTP is asked through the usual dialog.

A connection marked **Connect when OpenFortiClient starts** connects when the Connect screen first appears, if its password is saved; otherwise a history row says it was skipped. Only one connection can have it: saving one clears it on the others.

### Menu bar icon

Mac Catalyst apps cannot use AppKit, so the icon is part of a small Swift library, [src/OpenFortiClient.Native](../src/OpenFortiClient.Native/StatusMenu.swift):

| Part | How |
|---|---|
| Build | An MSBuild target in the app's csproj runs `xcrun swiftc -target arm64-apple-macos14.0 -emit-library` into `obj/native/libOFCNative.dylib`. It is built for macOS, not Catalyst, which is what AppKit code loaded into a Catalyst app needs |
| Packaging | The dylib goes to `Contents/MonoBundle` (`PublishFolderType="DynamicLibrary"`), and the SDK signs it with the app |
| Loading | `NativeBridge` (`Platforms/MacCatalyst`) `dlopen`s it; `StatusMenu` creates the `OFCStatusMenu` object and calls `install:` and `update:` with plain Foundation objects (`NSDictionary`, `NSString`, `NSNumber`) |
| Actions | Menu clicks call back into `StatusMenu` through the selectors `statusMenuConnect:`, `statusMenuDisconnect:`, `statusMenuOpen:` and `statusMenuQuit:` |
| Failure | If the library cannot be loaded, the app logs it and runs without the icon |

The menu shows the status, "Connect to {name}" per connection (connections without a saved password open the window instead), Disconnect or Stop reconnecting, Open OpenFortiClient, and Quit. The icon is an SF Symbol: open lock (disconnected), shield (connected), rotating lock (connecting), exclamation mark (error or reconnecting).

**Window and quit:**
- Closing the window keeps the app and the tunnel running in the menu bar. UIKit quits a Catalyst app whose last window closes unless it supports multiple scenes, so `Info.plist` declares scene support. The app delegate removes "New Window" to keep a single window, and the Swift library answers no to `applicationShouldTerminateAfterLastWindowClosed`.
- "Open OpenFortiClient" asks UIKit for a new window. Pages are created per window; their view models, which hold the state, are shared.
- However the app quits (the menu, Cmd-Q, logout), the `WillTerminate` hook disconnects first, so openfortivpn never stays behind as root. The menu's Quit waits until its menu has closed: quitting from inside the open menu stalls UIKit's scene teardown, and its watchdog then kills the app before the disconnect.
- Engine work runs on the thread pool (`VpnSession.RunEngineAsync`), never on the UI thread's context, so the blocking disconnect at quit cannot deadlock.

### Logs and notifications

- The app keeps its own log and the captured openfortivpn output in `~/Library/Logs/OpenFortiClient/`, with rotation.
- Lines are cleaned before they are written: passwords, cookies and OTPs are masked.
- Settings → **Export logs** zips the log folder, and **Clear logs** deletes it.
- Connection events go to a local history (`notifications.json`, newest 200) for the Notifications page. Some also show a macOS banner (`UNUserNotificationCenter`), which can be turned off in Settings. `ConnectionEvents` in Core holds the rules:

| Event | History | Banner |
|---|---|---|
| Connected | Info, "Connected to {host}." | "VPN connected" |
| Error after the tunnel was up | Error, the reason | "VPN connection lost" |
| Error before the tunnel was up | Error, the reason | "VPN connection failed" (none for an untrusted certificate: the app asks right away) |
| Error while disconnecting (openfortivpn did not stop) | Error, the reason | "VPN did not disconnect cleanly" |
| A gateway failed and the next one is tried | Warning, "{host} failed (…). Trying {next}." | "Trying another gateway" |
| Disconnected after being connected | Info, "Disconnected." | none: the user asked for it |
| Connect attempt cancelled | Info, "Connection attempt cancelled." | none |
| Always up: reconnect scheduled | Info, "Reconnecting to {host} in {n} s (attempt {k})." | none (the "lost" banner was just shown); failed attempts are rows without banners |
| Always up: reconnected | Info, "Reconnected to {host}." | "VPN reconnected" |
| Always up: paused (needs the user) | Warning, "Always up paused: {reason}" | "Always up paused" |
| Always up: stopped by the user | Info, "Reconnecting stopped." | none |

## Key decisions

| Decision | Choice | Reason |
|---|---|---|
| VPN engine | openfortivpn, run as a separate process | Proven, follows the routing table (this fixes the core problem), and GPL-safe as a separate process |
| UI framework | .NET MAUI (Mac Catalyst) | The team knows .NET, and the same code base can later target Windows |
| Engine abstraction | `IVpnEngine` | Keeps the option to build our own engine later |
| Root access | sudoers first, `SMAppService` later | Fastest path to a working MVP, with a clean upgrade path |
| Distribution | Signed DMG or Homebrew cask, no App Store | The App Store sandbox would prevent us from starting openfortivpn |

## Risks

| Risk | Mitigation |
|---|---|
| openfortivpn output changes between versions | Pin a minimum version, keep the parsing in one class with unit tests, and fall back to "process exited" |
| openfortivpn buffers its output when it writes to a pipe instead of a terminal, so state changes arrive late | Checked on 2026-10-17 with openfortivpn 1.24.1: lines arrive as they happen (gateway to tunnel up in about 1.4 s, each step logged on time). If a later version buffers, run it under a pseudo-terminal (for example `/usr/bin/script -q /dev/null openfortivpn …`) inside `SudoLauncher` |
| Mac Catalyst limits (menu bar, AppKit APIs) | Isolate the platform code in `Platforms/MacCatalyst`, and use an AppKit bundle where needed |
| `pppd` changes in future macOS versions | Track openfortivpn upstream. This is also an argument for our own engine in phase 5 |
| Users' companies forbid third-party VPN clients | Clear disclaimer, and point users to their IT policy |
| Homebrew's openfortivpn binary is writable by the admin user, and the helper runs it as root, so a process running as that user could replace it and get root. The sudoers rule has the same weakness | Accepted for now and documented. The follow-up is to ship a verified openfortivpn inside the app (phase 4b), which also removes the Homebrew dependency |
| A free Personal Team signs with a 7-day profile; the installed app stops launching when it expires | `scripts/provision-signing.sh` plus `scripts/build-release.sh` renew it; phase 4b moves to Developer ID |
