# Development

How to set up a Mac to build, sign, run, debug and test OpenFortiClient, and how to fix the problems you are likely to hit. For how the app is built, see [architecture.md](architecture.md); for publishing it, [release.md](release.md); for what comes next, [roadmap.md](roadmap.md).

**Quick start** on a Mac that has the [toolchain](#toolchain):

```sh
dotnet test OpenFortiClient.Core.slnf                                # Core tests, about a second
./scripts/install-sudoers-rule.sh                                    # once: Debug builds use sudo
./scripts/provision-signing.sh <TEAM_ID>                             # once, and every 7 days on a free team
dotnet build src/OpenFortiClient.App -f net10.0-maccatalyst -t:Build -t:Run   # build and start the app
```

## Toolchain

| Need | For | Install |
|---|---|---|
| macOS 14 or later | everything | |
| [.NET 10 SDK](https://dotnet.microsoft.com/download) | everything | The installer from dotnet.microsoft.com (see the note below) |
| autoconf, automake, pkgconf | building the bundled openfortivpn (once) | `brew install autoconf automake pkgconf` |
| openfortivpn 1.24 or later | connecting from Debug builds (sudo) | `brew install openfortivpn` |
| Xcode (the full app, not only the Command Line Tools) | the Mac app | App Store, then `sudo xcode-select -s /Applications/Xcode.app` |
| .NET MAUI workload | the Mac app | `sudo dotnet workload install maui` |
| An Apple ID signed in to Xcode | signed builds | Xcode → Settings → Accounts. A free Personal Team is enough |
| Node.js, Appium 3 and its Mac2 driver | end-to-end UI tests | `brew install node`, `npm install -g appium`, `appium driver install mac2` |

**Use one .NET SDK.** Workloads are installed per SDK. VS Code's C# Dev Kit uses the SDK in `/usr/local/share/dotnet` (from Microsoft's installer), while `brew install dotnet` adds a second one in `/opt/homebrew` that the terminal finds first. With both, the MAUI workload can end up in only one of them: the terminal builds fine, but VS Code shows `NETSDK1147 … maui-maccatalyst` and "Failed to restore NuGet packages" on `OpenFortiClient.slnx`. Install .NET with Microsoft's installer only (`brew uninstall dotnet` if you have both), then check that `which dotnet` points to `/usr/local/share/dotnet/dotnet` and `dotnet workload list` shows `maui-maccatalyst`.

### Dependencies and versions

Known to work together (2026-10-19):

| Dependency | Version | Used for | Where it is set |
|---|---|---|---|
| macOS | 27.0.1 (the app needs 14 or later) | everything | `SupportedOSPlatformVersion` 17.0 (Catalyst 17 = macOS 14) |
| Xcode | 27.1 (macOS SDK 27.0, Swift 6.4) | Mac app, Swift parts | `xcode-select` |
| .NET SDK | 10.0.401 | everything | |
| .NET MAUI workload | 10.0.110 (Mac Catalyst SDK 27.0.10722) | Mac app | `dotnet workload install maui` |
| Microsoft.Maui.Controls | 10.0.110 | UI | app csproj (`$(MauiVersion)`) |
| CommunityToolkit.Mvvm | 8.4.2 | view models (`[ObservableProperty]`, `[RelayCommand]`) | app csproj |
| Microsoft.Extensions.Logging.Debug | 10.0.0 | debug logging | app csproj |
| openfortivpn (bundled) | 1.24.1 | the VPN itself, run by the helper | `scripts/build-openfortivpn.sh` (version and SHA-256) |
| OpenSSL (in the bundled openfortivpn) | 3.5.9 LTS, static | TLS to the gateway | `scripts/build-openfortivpn.sh` |
| openfortivpn (Homebrew) | 1.24.1 | Debug builds with sudo | Settings → openfortivpn path (sudo) |
| Inter, JetBrains Mono fonts | Inter 4.1, JetBrains Mono 2.304 | the ResultLab look | `Resources/Fonts`, licences in `third-party/fonts` |
| xUnit / runner / Test SDK | 2.9.3 / 3.1.4 / 17.14.1 | Core and UI tests | test csprojs |
| Microsoft.Extensions.TimeProvider.Testing | 10.11.0 | fake clock in the reconnect tests | Core test csproj |
| Appium.WebDriver | 9.0.0 | UI tests | UI test csproj |
| Node.js | 26.12 | Appium | Homebrew |
| Appium / mac2 driver | 3.8.0 / 4.4.0 | UI tests | `npm`, `appium driver install mac2` |

The Swift parts (`src/OpenFortiClient.Native`, `src/OpenFortiClient.Helper`) have no packages: they use the macOS SDK only (AppKit, ServiceManagement, Foundation/XPC).

## One-time setup

### Root access for openfortivpn

openfortivpn needs root to start `pppd` and set routes and DNS ([architecture.md](architecture.md#elevation-running-as-root)). Release builds use the **privileged helper** (below). **Debug builds use sudo**, so a development Mac needs a sudoers rule:

```sh
./scripts/install-sudoers-rule.sh
```

Run it as yourself, **not** with `sudo`: the rule is for the user who runs it, and it asks for your password itself. It shows the rule, checks it with `visudo`, installs it as `/etc/sudoers.d/openfortivpn`, and then tests it the way the app uses it (`sudo -n openfortivpn --version`, with cached credentials dropped first):

```
<your-username> ALL=(root) NOPASSWD: /opt/homebrew/bin/openfortivpn
```

Remove it with `sudo rm /etc/sudoers.d/openfortivpn`.

### The privileged helper

The helper is built with the app (an MSBuild target compiles `src/OpenFortiClient.Helper` with `xcrun swiftc` and signs it with your identity and hardened runtime) and lives inside it. To use it:

1. Make sure `Signing.local.props` has `DevelopmentTeam`. `scripts/provision-signing.sh` writes it; the helper accepts only an app signed by that team, and refuses to run without one.
2. In the app: Settings → Privileged helper → **Install**. macOS shows the request in System Settings → General → **Login Items**; allow "OpenFortiClient".
3. In a Debug build, also turn on "Use the helper in this development build". Release builds always use it.

Check it from Terminal:

```sh
sudo launchctl print system/io.github.openforticlient.helper | head   # registered and loaded
ps -o pid,user,ppid,command -C openfortivpn                          # while connected: runs as root
```

Remove it with Settings → Privileged helper → Remove, or `./scripts/install.sh --uninstall`. After a rebuild with a new signature, remove and install it again if it stops answering.

### Code signing

Saved passwords need a signed build; everything else works unsigned.

**Why.** Passwords are stored with MAUI `SecureStorage`, which on Mac Catalyst always uses the data-protection Keychain. That Keychain needs the `keychain-access-groups` entitlement, and macOS refuses to launch an app with that entitlement unless a provisioning profile grants it. Catalyst offers no fallback to the older login keychain.

**Setup.**

1. Sign in to Xcode with your Apple ID (Settings → Accounts).
2. Find your team ID:
   ```sh
   defaults read com.apple.dt.Xcode IDEProvisioningTeamByIdentifier
   ```
3. Run:
   ```sh
   ./scripts/provision-signing.sh <TEAM_ID>
   ```

The script builds a stub macOS app ([scripts/provisioning/](../scripts/provisioning/)) with the app's bundle ID and Keychain entitlement, using Xcode's automatic signing. That makes Xcode register the bundle ID and your Mac, create an *Apple Development* certificate, and download the profile `Mac Team Provisioning Profile: io.github.openforticlient`. The script then writes `src/OpenFortiClient.App/Signing.local.props` with your certificate (by its SHA-1 hash, because `codesign` cannot find a name with non-ASCII characters) and the profile name. That file is gitignored; [Signing.local.props.example](../src/OpenFortiClient.App/Signing.local.props.example) shows its shape if you want to fill it in by hand.

**How the build picks the entitlements.**

| `CodesignProvision`                | Entitlements file                                                                                      | Result                                                                                                |
| ------------------------------------ | ------------------------------------------------------------------------------------------------------ | ----------------------------------------------------------------------------------------------------- |
| not set (no `Signing.local.props`) | [Entitlements.plist](../src/OpenFortiClient.App/Platforms/MacCatalyst/Entitlements.plist)               | Ad-hoc signed. Runs, but saving a password fails; the app logs it and asks for the password each time |
| set                                  | [Entitlements.Signed.plist](../src/OpenFortiClient.App/Platforms/MacCatalyst/Entitlements.Signed.plist) | Signed with your certificate and profile. Saved passwords work                                        |

**Renewal.** Profiles from a free Personal Team expire after **7 days**; after that the signed app no longer launches. Run the script again and rebuild with `--no-incremental`. A paid team's profiles last a year.

**Developer ID (releases).** With a paid Apple Developer account, `./scripts/provision-signing.sh <TEAM_ID> --developer-id` writes `Signing.local.props` for the *Developer ID Application* certificate and a Developer ID profile instead. Those run on any Mac and last for years; [release.md](release.md) has the one-time setup, and how to set up another Mac with the same certificate. Development builds work with either.

**Bundle ID.** A bundle ID can belong to only one team. If `io.github.openforticlient` is already taken by someone else's team, the script fails; change `ApplicationId` in the csproj and `BUNDLE_ID` in the script to an ID of your own, locally.

## Build and run

```sh
# Core library and tests only (no Xcode or MAUI needed)
dotnet test OpenFortiClient.Core.slnf

# The Mac app
dotnet build src/OpenFortiClient.App -f net10.0-maccatalyst
dotnet build src/OpenFortiClient.App -f net10.0-maccatalyst -t:Build -t:Run

# After changing Info.plist, entitlements or signing
dotnet build src/OpenFortiClient.App -f net10.0-maccatalyst --no-incremental
```

The app bundle is `src/OpenFortiClient.App/bin/Debug/net10.0-maccatalyst/maccatalyst-arm64/OpenFortiClient.app`; `open` it to start it outside `dotnet`.

### Release build

```sh
./scripts/build-release.sh    # Release, Apple Silicon only, signed; artifacts/release/OpenFortiClient-<version>.dmg
./scripts/install.sh          # install the result into /Applications
```

The release build is trimmed, uses the helper and bundles openfortivpn. Signed release builds get the **hardened runtime** and secure timestamps (notarisation needs both); the .NET SDK adds the `com.apple.security.cs.allow-jit` entitlement the runtime needs under it. Signed with your development certificate, the build runs only on Macs where you build it, and a free-team profile expires after 7 days (the script prints the date). Signed with Developer ID, it is ready for `scripts/notarize.sh` ([release.md](release.md)). The version comes from `ApplicationDisplayVersion` in the app's csproj. Intel Macs are not supported: everything is built for `arm64` only.

### Bundled openfortivpn

The app build runs [scripts/build-openfortivpn.sh](../scripts/build-openfortivpn.sh) once, when `artifacts/openfortivpn/<version>/openfortivpn` does not exist yet (about 30 seconds). It downloads OpenSSL and openfortivpn, checks their SHA-256, builds OpenSSL as static libraries and openfortivpn against them, and fails if the result links anything outside macOS. Next to the binary it keeps the licences (copied into the app's `Contents/Resources/Licenses`) and the source archives (published with each release). The csproj target `SignOpenFortiVpn` then signs a copy as `io.github.openforticlient.openfortivpn` and bundles it as `Contents/MacOS/openfortivpn`.

To update it, change the version and SHA-256 in the script (`brew info --json=v2 openfortivpn` shows the current release and its checksum) and run `./scripts/build-openfortivpn.sh --force`.

## Tests

| Suite | Covers | Run |
|---|---|---|
| [OpenFortiClient.Core.Tests](../tests/OpenFortiClient.Core.Tests/) | Engine state machine, output parsing, config files, stores | `dotnet test OpenFortiClient.Core.slnf` (seconds, no Xcode needed) |
| [OpenFortiClient.Helper.Tests](../tests/OpenFortiClient.Helper.Tests/) | The helper's session logic: only the bundled openfortivpn, run from a checked private copy, allowed config keys, 0600 files and when they go, output, OTP, SIGTERM. And its signature check against signed, ad hoc, unsigned and altered binaries (needs `Signing.local.props`, otherwise skipped) | `./scripts/test-helper.sh` (seconds, no root) |
| [OpenFortiClient.App.UiTests](../tests/OpenFortiClient.App.UiTests/) | The real app, end to end, driven through its UI | `./scripts/run-ui-tests.sh --quick` (12 tests, one per main flow, about 2 minutes) while working; `./scripts/run-ui-tests.sh` (all 47, about 9 minutes) before a release; `--release` runs against a trimmed Release build |

### End-to-end UI tests

The UI tests start the app, click and type in it like a user, and check what it shows. They run against a scripted openfortivpn, so they need no gateway, no root and no Keychain, and every scenario repeats exactly, including failures that are hard to produce on purpose (a dropped tunnel, a certificate prompt, an OTP).

```sh
./scripts/run-ui-tests.sh                                   # everything
./scripts/run-ui-tests.sh --filter Fails_over_to_the_second_gateway
OPENFORTICLIENT_E2E_KEEP=1 ./scripts/run-ui-tests.sh        # keep each test's folder (app log, profiles)
```

**One-time setup.** Install the tools from the [Toolchain](#toolchain) table. On the first run Appium builds its helper (WebDriverAgentMac) and macOS asks to let it control the computer: allow it under System Settings → Privacy & Security → Accessibility. The first run of a session can fail while that prompt is open; run again after allowing it.

**How it works.**

| Part                                                                          | What it does                                                                                                                                                                                                                                                                                                                                             |
| ----------------------------------------------------------------------------- | -------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------- |
| `-p:E2E=true` build                                                         | A copy of the app with its own bundle ID, `io.github.openforticlient.e2e`, and its own output folder (`artifacts/e2e/`). It runs next to your normal app, even while that one is connected, and never touches its preferences. Unsigned.                                                                                                             |
| Test mode ([Services/EndToEnd](../src/OpenFortiClient.App/Services/EndToEnd/)) | Debug builds only. When `OPENFORTICLIENT_E2E_DIR` names a folder, the app keeps its data and logs there, stores passwords in a plain `secrets.txt` instead of the Keychain, and replaces openfortivpn with `ScriptedLauncher`. Release builds do not contain this code.                                                                            |
| `system-notifications.log`, `secrets.txt`                                 | What the test build shows as macOS banners (`severity\|title\|body`) and saves as passwords, so tests can check both: `AppSession.SystemNotifications` and `AppSession.Secrets`                                                                                                                                                                      |
| `network-change`                                                            | `Trigger("network-change")` makes the test build's network monitor report a change, which retries a waiting reconnect                                                                                                                                                                                                                                  |
| `scenario.json`                                                             | What the scripted openfortivpn does on each start: `emit` a line, `delayMs`, `awaitInput` (an OTP, written to `input.log`), `waitForFile` (until the test calls `Trigger`), `exit`. Every start is logged to `started.log`, so a test can check which gateways were tried, and the certificates it was told to trust to `trusted.log`. |
| [AppSession](../tests/OpenFortiClient.App.UiTests/AppSession.cs)               | Creates a fresh folder per test with a seeded profile ("Test VPN", gateways `vpn1.example.com` and `vpn2.example.com`), launches the app through Appium and offers `Type`, `Tap`, `WaitForText`, `DialogButton`, `Trigger`.                                                                                                                |
| [AppiumServer](../tests/OpenFortiClient.App.UiTests/AppiumServer.cs)           | Starts Appium on port 4723 unless one is already running. Tests run one after the other: they share the screen.                                                                                                                                                                                                                                          |

**Writing a test.** Find elements by `AutomationId`; give every control a test needs one in the XAML. Use the openfortivpn lines exactly as openfortivpn prints them, ideally taken from a real log. For example, the lost-connection test replays what happened on 2026-10-17:

```csharp
using var app = AppSession.Start(new Scenario()
    .Process(p => p.Emit(Scenario.TunnelUp).WaitFor("drop").Emit("INFO:   Cancelling threads...")));
app.Type("PasswordEntry", "s3cret");
app.Tap("ConnectButton");
app.WaitForText("StatusLabel", "Connected to vpn1.example.com");
app.Trigger("drop");
app.WaitForText("ErrorLabel", "The connection to vpn1.example.com was lost.");
```

**Speed.** Every Appium call costs 0.3 to 1.5 s on macOS, because XCTest waits for the app to be idle after each action; a test also starts a fresh app (about 3 s). So `AppSession` makes as few calls as it can: no "displayed" check per lookup, one attribute per text read (`label`, then `value`), and `Type` only clears a field with `replace: true`. Fixed waits are only for "nothing happens" checks, and kept short. Tests cannot run in parallel: they share the screen and keyboard. The run script rebuilds the test copy from scratch only when `Info.plist` or the entitlements changed.

**Looking at the layout.** `app.SaveScreenshot("name")` saves the app's window (only the window, never the rest of the screen) to `artifacts/e2e/screens/name.png`; `BrandingTests` saves the Connect, About and Settings pages.

**When a test fails** on a missing element or text, the message names a file under `artifacts/e2e/failures/` with the accessibility tree of the screen at that moment: search it for the element, its `identifier` and its `title` or `label`.

Things that behave differently on Mac Catalyst:

- An alert, prompt or action sheet is a sheet on the window (`XCUIElementTypeSheet`). The tree also holds hidden copies of its buttons, so use `DialogButton`, which looks inside the sheet only.
- A button's text is in its `title`, a label's text in its `label`, an entry's text in its `value`. `AppSession.TextOf` reads all three.
- A password entry's value is shown as dots, so check a saved password by what it does (connecting), not by reading it.

## Project settings that work around the toolchain

These look odd on their own, so here is why each one exists.

| Where                                    | Setting                                                                | Why                                                                                                                                                                                                                                                                                                                                  |
| ---------------------------------------- | ---------------------------------------------------------------------- | ------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------ |
| App csproj                               | `ValidateXcodeVersion=false`                                         | The Mac Catalyst SDK accepts only the exact Xcode version it was built for (27.0). Minor Xcode updates work fine                                                                                                                                                                                                                     |
| App csproj                               | `SupportedOSPlatformVersion` 17.0                                    | The lowest Mac Catalyst version the .NET 10 SDK accepts. Catalyst 17 is macOS 14                                                                                                                                                                                                                                                     |
| App csproj                               | CommunityToolkit.Mvvm 8.4.2 or later                                   | 8.4.0 only generates `[ObservableProperty]` partial properties with `LangVersion=preview` and does not recognise C# 14                                                                                                                                                                                                           |
| App csproj                               | `Microsoft.Extensions.Logging.Debug`                                 | `MauiProgram` calls `AddDebug()` in Debug builds; MAUI does not include this package itself (its project template adds it separately)                                                                                                                                                                                            |
| `Platforms/MacCatalyst`                | `SceneDelegate` and `UIApplicationSceneManifest` in `Info.plist` | Apps built with the macOS 27 SDK must use the UIScene life cycle. One scene only: there is one VPN connection at a time                                                                                                                                                                                                              |
| `Platforms/MacCatalyst`                | Two entitlements files                                                 | See [Code signing](#code-signing)                                                                                                                                                                                                                                                                                                     |
| App csproj                               | `MauiImage` / `MauiFont` items                                     | MAUI does not pick up `Resources/Images` and `Resources/Fonts` on its own; without these items the ResultLab logos and the Inter / JetBrains Mono fonts are silently missing. Font licences live in `third-party/fonts/`, not next to the fonts (MAUI would try to load them as fonts)                                         |
| `Resources/Styles`                     | ResultLab look                                                         | Colours from resultlab.io's stylesheet (dark surfaces, cyan `#00B8D7`), Inter for text, JetBrains Mono for the uppercase section labels, 6-point corners. The app is always dark (`App.UserAppTheme`)                                                                                                                            |
| `WindowSizing`                         | Window size set on the window scene                                    | Catalyst ignores MAUI's `Window.Width/Height` and restores the last size, so the size limits are pinned to 833 × 1148 UIKit points for a moment, which forces the size, then loosened. In iPad style Catalyst draws UIKit points at 77 %, so the window is about 641 × 884 on screen: the Settings page plus about 1 cm each way |
| App csproj and `Directory.Build.props` | `E2E` switch                                                         | The end-to-end build needs its own bundle ID and its own `obj`/`bin` folders. `BaseIntermediateOutputPath` must be set before the SDK loads, so it lives in `Directory.Build.props`; the csproj then excludes the normal `obj`/`bin` from the source globs                                                               |
| `Info.plist`                           | `UIApplicationSupportsMultipleScenes` true                           | Without it UIKit quits the app when its window closes; the VPN must stay up in the menu bar. `AppDelegate.BuildMenu` removes "New Window" (`UIMenuIdentifier.NewItem` from Catalyst 26, `NewScene` before)                                                                                                                     |
| App csproj                               | `BuildNative` target                                                 | Builds `libOFCNative.dylib` and the helper with `xcrun swiftc` (only when their sources or the team changed), signs the helper, and places both, plus the helper's launchd plist, into the bundle (`PublishFolderType`)                                                                                                        |
| App csproj                               | `E2E` symbol                                                         | The UI tests' test mode is compiled only into the `-p:E2E=true` build, in Debug or Release, never into a normal build                                                                                                                                                                                                              |
| Core stored types                        | `set`, not `init`, on persisted properties                         | The source-generated JSON reader fills `init` properties like constructor arguments: a property missing from the file got `false`, `null` or `0` instead of its default. A `settings.json` without `warnInvalidServerCertificate` turned certificate warnings off (found by the UI tests, 2026-10-17)                    |

## Run and debug

### What runs where

| Part | Process | Language | Built by | Its log |
|---|---|---|---|---|
| The app | `OpenFortiClient.App` (you) | C# (MAUI, Mac Catalyst) | `dotnet build` | `~/Library/Logs/OpenFortiClient/app.log` |
| Native library (menu bar, helper client and control) | inside the app | Swift | csproj target `BuildNativeLibrary` | the app log ("menu bar", "helper" lines) and the system log |
| Privileged helper | `OpenFortiClientHelper`, as root, started by launchd | Swift | csproj target `BuildHelper` | the system log (`process == "OpenFortiClientHelper"`) |
| openfortivpn | a private copy of the bundled one, child of the helper (release); Homebrew's, child of `sudo` (debug); as root | C | `scripts/build-openfortivpn.sh`, signed by csproj target `SignOpenFortiVpn` | its output, in the app log ("openfortivpn:" lines) |

### The app

- **Start it:** `dotnet build src/OpenFortiClient.App -f net10.0-maccatalyst -t:Build -t:Run`, or `open` the built bundle. Logs go to `~/Library/Logs/OpenFortiClient/app.log`; set Settings → Log level → Debug for more.
- **Breakpoints:**
  - **VS Code** with the C# Dev Kit and .NET MAUI extensions: open the folder, pick the Mac Catalyst target, then Run and Debug.
  - **JetBrains Rider:** open `OpenFortiClient.slnx` and debug the app project with the `net10.0-maccatalyst` target.
  - Either can also attach to a running `OpenFortiClient.App` process.
- **Logic without the UI:** most behaviour lives in Core and is reachable from the Core tests, which debug like any .NET test (`dotnet test --filter <name>` or the IDE's test explorer):
  - the engine and its states (`OpenFortiVpnEngineTests`, with `FakeLauncher`)
  - output parsing
  - reconnect (`ReconnectSupervisorTests`, with a fake clock)
  - notification rules (`ConnectionEventsTests`)
- **Without a gateway:** build the test copy (`dotnet build src/OpenFortiClient.App -f net10.0-maccatalyst -p:E2E=true`) and start it with `OPENFORTICLIENT_E2E_DIR=<folder>` pointing at a folder with a `scenario.json` (see [End-to-end UI tests](#end-to-end-ui-tests)). The scripted openfortivpn then replays any output you want, such as drops, OTP prompts or certificate errors.
- **Crashes:** `~/Library/Logs/DiagnosticReports/OpenFortiClient.App-*.ips`. The faulting thread's frames name the C# method. Example: a crash in `xamarin_get_gchandle` under `PerformSelector` was the native bridge reading a return value from a method that returns nothing. That's why `NativeBridge.Call` exists.

### The Swift parts

- **Native library:** add `NSLog(...)`, rebuild, and read it with
  ```sh
  log stream --predicate 'process == "OpenFortiClient.App"' --style compact
  ```

  To try a method outside the app, `dlopen` the built `libOFCNative.dylib` from a small Swift program and call it with `perform(...)`. That's how the bridge crash above was reproduced.
- **Helper, logic:** `./scripts/test-helper.sh` runs its session logic (allowlists, files, output, OTP, SIGTERM) as the normal user against a fake openfortivpn. Add a test there first.
- **Helper, running:**
  ```sh
  sudo launchctl print system/io.github.openforticlient.helper   # registered, running, last exit
  log stream --predicate 'process == "OpenFortiClientHelper" OR process == "smd" OR process == "backgroundtaskmanagementd"' --style compact
  sfltool dumpbtm | grep -A12 openforticlient                    # the Login Items record, its team and approval
  ps -o pid,user,ppid,command -p $(pgrep -x openfortivpn)        # while connected: root, child of the helper
  ```

  To debug it with breakpoints, attach as root: `sudo lldb -n OpenFortiClientHelper`, which needs its process running (connect once).

### openfortivpn

- Its output is in the app log as "openfortivpn:" lines.
- With Settings → Log level → Debug the app adds more of its own detail.
- To try a config by hand, run the same command the app runs, with a config file of your own (mode 0600): `sudo openfortivpn -c my.conf -v`, or the bundled one: `sudo /Applications/OpenFortiClient.app/Contents/MacOS/openfortivpn -c my.conf -v`.
- If the helper refuses with "failed its signature check", the bundled openfortivpn was changed or signed by another team: `codesign -dv --verbose=2 /Applications/OpenFortiClient.app/Contents/MacOS/openfortivpn` shows what it is signed with. Reinstall the app.

### The UI tests

- Keep a test's folder (scenario, app log, profiles, secrets) with `OPENFORTICLIENT_E2E_KEEP=1`.
- A failed wait saves the screen's accessibility tree to `artifacts/e2e/failures/`.
- `SaveScreenshot` saves the window to `artifacts/e2e/screens/`.
- Appium's own log: start `appium --log-level debug --log-timestamp` yourself before the tests. The fixture reuses a running server.
- Before running tests, quit any test copy you started by hand: a running copy with the same bundle ID makes Appium wait for ever (the tests also close leftovers themselves).

## Troubleshooting

| Symptom                                                                                                       | Cause                                                                                                                                                                                                  | Fix                                                                                                                                                            |
| ------------------------------------------------------------------------------------------------------------- | ------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------ | -------------------------------------------------------------------------------------------------------------------------------------------------------------- |
| `This version of .NET for MacCatalyst (…) requires Xcode …`                                               | Xcode is newer than the SDK expects                                                                                                                                                                    | Already handled by `ValidateXcodeVersion=false`. If a major Xcode version breaks the build, update the MAUI workload                                         |
| `The SupportedOSPlatformVersion value '…' is lower than the minimum value '17.0'`                          | Minimum OS too low                                                                                                                                                                                     | Keep `SupportedOSPlatformVersion` at 17.0 or higher                                                                                                          |
| `CS9248: Partial property … must have an implementation part` everywhere                                   | The MVVM source generator did not run                                                                                                                                                                  | Use CommunityToolkit.Mvvm 8.4.2 or later                                                                                                                       |
| VS Code shows "Failed to restore NuGet packages for the solution" on `OpenFortiClient.slnx` and `NETSDK1147: … workloads must be installed: maui-maccatalyst`, while the terminal build works | Two .NET SDKs (Homebrew and Microsoft's installer); VS Code uses the one in `/usr/local/share/dotnet`, which has no MAUI workload | `sudo /usr/local/share/dotnet/dotnet workload install maui-maccatalyst`, then Developer: Reload Window. Better, keep only one SDK: see [Toolchain](#toolchain) |
| `MSB3073: The command "open -a ".../OpenFortiClient.app" --args" exited with code 1` | `-t:Run` on its own only launches the app and does not build it, so on a fresh checkout there is no bundle yet | Build and run together: `dotnet build src/OpenFortiClient.App -f net10.0-maccatalyst -t:Build -t:Run` |
| `open` fails with `Launchd job spawn failed`, POSIX error 163                                             | The app carries `keychain-access-groups` without a matching provisioning profile, or the profile has expired                                                                                         | Run `./scripts/provision-signing.sh <TEAM_ID>` and rebuild with `--no-incremental`, or delete `Signing.local.props` for an unsigned build                |
| POSIX error 163 although signing is set up; the profile's `ProvisionedDevices` (`security cms -D -i OpenFortiClient.app/Contents/embedded.provisionprofile`) does not list this Mac's Provisioning UDID (`system_profiler SPHardwareDataType`) | The profile was made without registering this Mac, for example by an older `provision-signing.sh` that built for `generic/platform=macOS` | Run `./scripts/provision-signing.sh <TEAM_ID>` again (it now builds for this Mac), then rebuild |
| `provision-signing.sh` says "No Apple Development certificate found", `security find-identity -v -p codesigning` shows 0 valid identities, and `codesign` fails with `unable to build chain to self-signed root` / `errSecInternalComponent` | The login keychain has only the old Apple WWDR intermediate (expired 2023), not the G3 one that signs new certificates | Add [AppleWWDRCAG3.cer](https://www.apple.com/certificateauthority/AppleWWDRCAG3.cer) to the login keychain (open it, or `security add-certificates -k ~/Library/Keychains/login.keychain-db AppleWWDRCAG3.cer`), then run the script again |
| The app quits right away; the system log says `UIScene life cycle is required for apps built with this SDK` | No scene delegate, or a stale `Info.plist` in the bundle                                                                                                                                             | Check `SceneDelegate.cs` and the scene manifest, then rebuild with `--no-incremental`                                                                      |
| Changes to `Info.plist` or entitlements have no effect                                                      | Incremental builds can keep the old file in the bundle                                                                                                                                                 | Rebuild with `--no-incremental`                                                                                                                              |
| The app log says `Could not save the password to the Keychain`                                              | Unsigned build: the Keychain returns `errSecMissingEntitlement` (-34018)                                                                                                                             | Set up [code signing](#code-signing)                                                                                                                            |
| No menu bar icon                                                                                              | The library failed to build or load                                                                                                                                                                    | Check the build output for `swiftc` errors, that `OpenFortiClient.app/Contents/MonoBundle/libOFCNative.dylib` exists, and the app log for "menu bar" lines |
| Connecting says "the privileged helper is not installed" or "waiting for your approval"                       | Release builds (or Debug with the helper switch on) need the helper                                                                                                                                    | Settings → Privileged helper → Install, then allow it in System Settings → General → Login Items                                                           |
| Connecting says "The privileged helper is not available (…)"                                                 | The helper is registered but does not answer: an old signature after a rebuild, or a build without `DevelopmentTeam`                                                                                 | Settings → Privileged helper → Remove, then Install again; check `sudo launchctl print system/io.github.openforticlient.helper`                            |
| The installed app does not open after a week                                                                  | The free team's 7-day profile expired                                                                                                                                                                  | `./scripts/provision-signing.sh <TEAM_ID>`, `./scripts/build-release.sh`, `./scripts/install.sh`                                                         |
| No macOS banners appear                                                                                       | Notifications for OpenFortiClient are off in System Settings → Notifications, or the setting in the app is off                                                                                        | Turn them on in both places. The permission prompt shows once, on the first start with banners enabled                                                         |
| A UI test session fails with `cannot be proxied to Mac2 Driver server because its process is not running`   | Appium's helper is not allowed to control the computer yet, or was still being built                                                                                                                   | Allow it under Privacy & Security → Accessibility, then run again                                                                                             |
| Connecting fails with `not spawned with root privileges` or `a password is required`                      | The sudoers rule is missing, points at another openfortivpn path, or was written for `root` (`sudo cat /etc/sudoers.d/openfortivpn` starts with `root`) because the script was run with `sudo` | Run `./scripts/install-sudoers-rule.sh` again, without `sudo`; it replaces the rule                                                                        |

## Diagnostics

```sh
# The app's own log and the captured openfortivpn output
ls ~/Library/Logs/OpenFortiClient/

# Errors from the app in the system log (launch failures show up here, not in the app log)
log show --last 5m --style compact \
  --predicate 'process CONTAINS "OpenFortiClient" AND (messageType == error OR messageType == fault)'

# What the bundle was signed with
codesign -d --entitlements - src/OpenFortiClient.App/bin/Debug/net10.0-maccatalyst/maccatalyst-arm64/OpenFortiClient.app

# Signing certificates and the profile's expiry date
security find-identity -v -p codesigning
for p in ~/Library/Developer/Xcode/UserData/Provisioning\ Profiles/*.provisionprofile; do
  security cms -D -i "$p" | plutil -extract Name raw -
  security cms -D -i "$p" | plutil -extract ExpirationDate raw -
done
```

Most of the system log lines the app writes (`appintents`, `coreanimation`, `UITableView … without being in the view hierarchy`) are framework noise and can be ignored.
