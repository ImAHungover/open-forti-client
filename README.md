# open-forti-client

**OpenFortiClient** is a [ResultLab](https://resultlab.io/products) open-source product.

An open-source, macOS-first desktop client for **Fortinet SSL-VPN** gateways, built as a free alternative to FortiClient VPN.

## Why

FortiClient VPN 7.4.3 works well on Windows but can fail on macOS. Its Network Extension sends tunnel traffic straight out of the physical network adapter and ignores other active VPN routes (for example an L2TP company VPN). Gateways that restrict access by region then silently drop the connection, so the user only sees a timeout.

`openfortivpn` follows the normal system routing table, so it does not have this problem. This project wraps it in a friendly native UI.

## Documentation

| Document | What it covers |
|---|---|
| [docs/vision.md](docs/vision.md) | Problem, goals, non-goals, target users, phases, open questions |
| [docs/architecture.md](docs/architecture.md) | Components, VPN engine, connection states, root access, profiles, secrets, logs |
| [docs/development.md](docs/development.md) | Dependencies and versions, setup, build, run, debug, tests, troubleshooting |
| [docs/release.md](docs/release.md) | Going live: Developer ID, notarised DMG, Homebrew cask, release checklist |
| [docs/roadmap.md](docs/roadmap.md) | Future improvements, including the remaining connection types (TBC/TBD) |
| [docs/raw/wireframes/flows.md](docs/raw/wireframes/flows.md) | Problem analysis and the screens of the official client |

## Approach

| Layer | Choice | Notes |
|---|---|---|
| VPN engine | [openfortivpn](https://github.com/adrienverge/openfortivpn) (GPLv3) | Used as-is for now and run as a separate process. A custom engine may come later. |
| UI | [.NET MAUI](https://learn.microsoft.com/dotnet/maui/) on macOS (Mac Catalyst) | Native look on the Mac. The same code base can target Windows later. |
| Engine abstraction | `IVpnEngine` interface | The UI never calls openfortivpn directly, so the engine can be swapped later. |
| Root access | `sudoers` rule (MVP), then an `SMAppService` helper | openfortivpn needs root to start `pppd` and set routes and DNS. |
| Configuration | One profile per connection, stored locally | Passwords go in the macOS Keychain, never in plain files. |

The UI never touches the network itself. It writes an openfortivpn config, starts and stops the openfortivpn process with root rights, and reads its output to show the connection state and logs. Details are in [docs/architecture.md](docs/architecture.md).

```
┌──────────────────────┐   start/stop, config   ┌──────────────────┐   TLS / PPP   ┌──────────────┐
│  .NET MAUI app (UI)  │ ─────────────────────▶ │   openfortivpn   │ ────────────▶ │   FortiGate  │
│  profiles, settings, │ ◀───────────────────── │  (root process)  │               │   SSL-VPN    │
│  logs, notifications │     stdout / status    └──────────────────┘               └──────────────┘
└──────────────────────┘
```

## Features

The features follow the screens of the official client (see [flows.md](docs/raw/wireframes/flows.md)):

- **Connect view**: pick a saved connection, enter credentials, connect or disconnect, and see the status.
- **Connection management**: add, edit and delete connections. Each one has a name, a description, several remote gateways, username or saved login, and an optional client certificate.
- **Settings**: back up and restore connections, set the log level, export or clear logs, and set VPN options such as accepting an invalid server certificate.
- **Notifications**: a history of connection events and errors.
- **About**: version and licence information.
- **Menu bar icon** (phase 3, not built yet): quick connect and disconnect without opening the main window.

Out of scope for now: IPsec VPN, EMS / Zero Trust features, SAML/SSO login (to be evaluated later, since openfortivpn has partial support).

## Install

OpenFortiClient runs on Macs with Apple Silicon and macOS 14 or later, and needs openfortivpn (`brew install openfortivpn`). There is no notarised download yet (phase 4b), so build it yourself:

```sh
./scripts/provision-signing.sh <TEAM_ID>   # once, and every 7 days on a free Apple team (see the guide)
./scripts/build-release.sh                 # artifacts/release/OpenFortiClient-<version>.dmg
./scripts/install.sh                       # copies it to /Applications and opens it
```

Then in the app: **Settings → Privileged helper → Install**, and allow OpenFortiClient in **System Settings → General → Login Items**. The helper runs openfortivpn as root for the app, so no sudoers rule is needed. Remove everything again with `./scripts/install.sh --uninstall` (add `--purge` to also remove connections, settings and logs).

## Getting started (development)

You need macOS 14 or later, the .NET 10 SDK, openfortivpn, Xcode and the .NET MAUI workload. The full setup, code signing and troubleshooting are in [docs/development.md](docs/development.md).

```sh
dotnet test OpenFortiClient.Core.slnf                      # Core and tests, no Xcode needed
./scripts/install-sudoers-rule.sh                          # once: Debug builds start openfortivpn with sudo
./scripts/provision-signing.sh <TEAM_ID>                   # once, and every 7 days on a free team: signing for saved passwords
dotnet build src/OpenFortiClient.App -f net10.0-maccatalyst -t:Run
./scripts/test-helper.sh                                   # the privileged helper's tests
./scripts/run-ui-tests.sh                                  # end-to-end UI tests (needs Appium, see the guide)
```

## Project structure

```
OpenFortiClient.slnx            all projects
OpenFortiClient.Core.slnf       Core + tests only, for machines without Xcode / MAUI
src/
  OpenFortiClient.Core/         plain .NET 10 library, no UI
    Engine/                     IVpnEngine, VpnState, SudoLauncher
      OpenFortiVpn/             engine, output parser, config writer
    Profiles/                   VpnProfile, gateway parsing, JSON store
    Settings/                   AppSettings, JSON store, backup and restore
    Secrets/                    ISecretStore
    Logging/                    rotating file log, secret masking
    Notifications/              notification history
  OpenFortiClient.App/          .NET MAUI app (Mac Catalyst)
  OpenFortiClient.Native/       Swift: menu bar icon, helper client and control, built into the app as an AppKit library
  OpenFortiClient.Helper/       Swift: the privileged helper (launch daemon) that runs openfortivpn as root
    Views/ ViewModels/          one page per FortiClient screen, MVVM
    Services/                   VpnSession, Keychain store, dialogs
      EndToEnd/                 Debug-only test mode for the UI tests
    Platforms/MacCatalyst/      entry point, Info.plist, entitlements
tests/
  OpenFortiClient.Core.Tests/   xUnit tests for Core (engine runs against a fake launcher)
  OpenFortiClient.App.UiTests/  end-to-end tests: the real app through Appium, scripted openfortivpn
  OpenFortiClient.Helper.Tests/ the helper's session logic against a fake openfortivpn
scripts/
  install-sudoers-rule.sh       one-time root-access setup
  provision-signing.sh          signing certificate and profile for a signed build
  provisioning/                 stub Xcode project the signing script builds
  run-ui-tests.sh               builds the test copy of the app and runs the UI tests (--release too)
  test-helper.sh                builds and runs the helper tests
  build-release.sh              release app and DMG
  install.sh                    install into /Applications, or --uninstall
third-party/
  fonts/                        licences of the bundled Inter and JetBrains Mono fonts
docs/
  vision.md                     why we build it, goals, phases
  architecture.md               how it is built
  development.md                setting up a Mac, signing, troubleshooting
  raw/                          source material (unedited)
    wireframes/flows.md         user journey, problem statement, screen descriptions
    wireframes/image/           screenshots of the official FortiClient VPN UI
    logs/                       FortiClient 7.4.3 macOS logs from the failing session
```

## Project status

As of 2026-10-18: **phases 1 to 4 (free tier) are done; this is version 1.0.0.** The Mac app connects to a real FortiGate through openfortivpn, routed through the company L2TP VPN as intended. Connections, saved passwords, several gateways with failover, the certificate prompt, notifications, automatic reconnect ("always up"), auto-connect and the menu bar icon all work and are covered by unit tests and end-to-end UI tests.

Next: phase 4b, a public release (Developer ID, notarisation, Homebrew cask), once there is a paid Apple Developer account.

## Roadmap

| Phase | Outcome | Status |
|---|---|---|
| 0. Foundations | Problem analysis, wireframes, vision, architecture, developer guide | ✅ Done |
| 1. MVP | Connect view and one profile; start and stop openfortivpn; status and log view | ✅ Works against a real gateway (2026-10-17) |
| 2. Daily driver | Profile management, Keychain, several gateways, certificate trust prompt, notifications | ✅ Done (2026-10-17), covered by unit and end-to-end tests |
| 3. Comfort | Menu bar icon, auto-connect, reconnect on network change, backup and restore | ✅ Done (2026-10-17): always up with backoff and retry on network change, auto-connect at start, menu bar icon (the app keeps running when its window closes), backup and restore. Start at login is left for later |
| 4. Polish and release (free tier) | `SMAppService` privileged helper, client private key in the Keychain, release build, install script, licence | ✅ Done (2026-10-18). Release builds use the helper; the sudoers rule is for development builds only. Version 1.0.0, MIT |
| 4b. Public release | Developer ID signing and notarisation, Homebrew cask, GitHub repository and releases, a verified openfortivpn inside the app | Not started: needs a paid Apple Developer account and hosting. Runbook: [docs/release.md](docs/release.md) |
| Later | More connection types (SAML, IPsec, …) and other improvements | TBC/TBD: [docs/roadmap.md](docs/roadmap.md) |
| 5. Decide on the engine | Keep openfortivpn, or build our own implementation | Not started (after phase 3) |

## Licence

[MIT](LICENSE), © 2026 ResultLab. openfortivpn is GPLv3; it is not bundled, and the app runs it as a separate program without linking it, so it does not affect the licence of this project's code. See [THIRD-PARTY-NOTICES.md](THIRD-PARTY-NOTICES.md).

## Disclaimer

This project is not affiliated with or endorsed by Fortinet. FortiClient and FortiGate are trademarks of Fortinet, Inc. Check with your network administrator that third-party VPN clients are allowed before you use one.
