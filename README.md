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
| VPN engine | [openfortivpn](https://github.com/adrienverge/openfortivpn) (GPLv3) | Built from pinned sources, bundled in the app and run as a separate process. A custom engine may come later. |
| UI | [.NET MAUI](https://learn.microsoft.com/dotnet/maui/) on macOS (Mac Catalyst) | Native look on the Mac. The same code base can target Windows later. |
| Engine abstraction | `IVpnEngine` interface | The UI never calls openfortivpn directly, so the engine can be swapped later. |
| Root access | An `SMAppService` privileged helper (a `sudoers` rule in development builds) | openfortivpn needs root to start `pppd` and set routes and DNS. |
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
- **Menu bar icon**: quick connect and disconnect without opening the main window.
- **macOS VPN** (macOS only): the VPN services set up in System Settings, such as a company L2TP VPN the gateway is reached through, with their status, an on/off switch and *Keep it up* (start with the app, reconnect after a drop), on the Connect page and in the menu bar. An L2TP VPN's settings (server, account, password, shared secret, send all traffic, keep-alive) can be changed from the app too, through the privileged helper.

Not yet supported: IPsec VPN (FortiOS 7.6.3 removed SSL VPN tunnel mode; first findings in [docs/ipsec-spike.md](docs/ipsec-spike.md)), SAML/SSO login (openfortivpn has partial support). Out of scope: EMS / Zero Trust features.

## Install

OpenFortiClient runs on Macs with Apple Silicon and macOS 14 or later. It brings its own openfortivpn, so nothing else is needed. Install it with Homebrew:

```sh
brew install --cask imahungover/tap/openforticlient
```

or download the notarised DMG from [Releases](https://github.com/ImAHungover/open-forti-client/releases) and drag OpenFortiClient to Applications.

Then in the app: **Settings → Privileged helper → Install**, and allow OpenFortiClient in **System Settings → General → Login Items**. The helper runs openfortivpn as root for the app, so no sudoers rule is needed. Remove it with `brew uninstall --cask openforticlient` (add `--zap` to also remove connections, settings and logs), or with `./install.sh --uninstall` from the DMG.

To build it yourself instead, see [docs/development.md](docs/development.md#release-build).

## Getting started (development)

You need macOS 14 or later, the .NET 10 SDK, Xcode, the .NET MAUI workload, and autoconf, automake and pkgconf to build the bundled openfortivpn. Debug builds run Homebrew's openfortivpn with sudo. The full setup, code signing and troubleshooting are in [docs/development.md](docs/development.md).

```sh
dotnet test OpenFortiClient.Core.slnf                      # Core and tests, no Xcode needed
./scripts/install-sudoers-rule.sh                          # once: Debug builds start openfortivpn with sudo
./scripts/provision-signing.sh <TEAM_ID>                   # once, and every 7 days on a free team: signing for saved passwords
dotnet build src/OpenFortiClient.App -f net10.0-maccatalyst -t:Build -t:Run
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
    Views/ ViewModels/          one page per FortiClient screen, MVVM
    Services/                   VpnSession, Keychain store, dialogs
      EndToEnd/                 Debug-only test mode for the UI tests
    Platforms/MacCatalyst/      entry point, Info.plist, entitlements
  OpenFortiClient.Native/       Swift: menu bar icon, helper client and control, macOS VPN services; built into the app as an AppKit library
  OpenFortiClient.Helper/       Swift: the privileged helper (launch daemon) that runs the bundled openfortivpn as root
tests/
  OpenFortiClient.Core.Tests/   xUnit tests for Core (engine runs against a fake launcher)
  OpenFortiClient.App.UiTests/  end-to-end tests: the real app through Appium, scripted openfortivpn
  OpenFortiClient.Helper.Tests/ the helper's session logic and signature check
scripts/
  install-sudoers-rule.sh       one-time root-access setup
  provision-signing.sh          signing certificate and profile for a signed build
  provisioning/                 stub Xcode project the signing script builds
  run-ui-tests.sh               builds the test copy of the app and runs the UI tests (--release too)
  test-helper.sh                builds and runs the helper tests
  build-openfortivpn.sh         builds the bundled openfortivpn and OpenSSL from pinned sources
  build-release.sh              release app and DMG, with the GPL sources next to it
  notarize.sh                   notarises and staples the DMG (Developer ID)
  make-cask.sh                  writes the Homebrew cask for the DMG
  install.sh                    install into /Applications, or --uninstall
.github/workflows/ci.yml        core and helper tests on every push
third-party/
  fonts/                        licences of the bundled Inter and JetBrains Mono fonts
docs/
  vision.md                     why we build it, goals, phases
  architecture.md               how it is built
  development.md                setting up a Mac, signing, troubleshooting
  release.md                    publishing a release: Developer ID, notarisation, Homebrew
  roadmap.md                    what comes next
  raw/                          source material (unedited)
    wireframes/flows.md         user journey, problem statement, screen descriptions
    wireframes/image/           screenshots of the official FortiClient VPN UI
    logs/                       FortiClient 7.4.3 macOS logs from the failing session
```

## Project status

As of 2026-10-21: **version 1.1.0 is released** ([download](https://github.com/ImAHungover/open-forti-client/releases/latest), or `brew install --cask imahungover/tap/openforticlient`). The Mac app connects to a real FortiGate through openfortivpn, routed through the company L2TP VPN as intended. Connections, saved passwords, several gateways with failover, the certificate prompt, notifications, automatic reconnect ("always up"), auto-connect and the menu bar icon all work and are covered by unit tests and end-to-end UI tests.

Phase 4b, the public release, is done: the app is signed with Developer ID and notarised, bundles openfortivpn built from pinned sources, and the helper runs only that signed copy. CI runs the Core and helper tests on every push. How to build the next release: [docs/release.md](docs/release.md).

## Roadmap

| Phase | Outcome | Status |
|---|---|---|
| 0. Foundations | Problem analysis, wireframes, vision, architecture, developer guide | ✅ Done |
| 1. MVP | Connect view and one profile; start and stop openfortivpn; status and log view | ✅ Works against a real gateway (2026-10-17) |
| 2. Daily driver | Profile management, Keychain, several gateways, certificate trust prompt, notifications | ✅ Done (2026-10-17), covered by unit and end-to-end tests |
| 3. Comfort | Menu bar icon, auto-connect, reconnect on network change, backup and restore | ✅ Done (2026-10-17): always up with backoff and retry on network change, auto-connect at start, menu bar icon (the app keeps running when its window closes), backup and restore. Start at login is left for later |
| 4. Polish and release (free tier) | `SMAppService` privileged helper, client private key in the Keychain, release build, install script, licence | ✅ Done (2026-10-18). Release builds use the helper; the sudoers rule is for development builds only. Version 1.0.0, MIT |
| 4b. Public release | Developer ID signing and notarisation, Homebrew cask, GitHub repository and releases, a verified openfortivpn inside the app | ✅ Done (2026-10-20): v1.0.0 notarised on GitHub Releases and in the `imahungover/tap` cask. Runbook: [docs/release.md](docs/release.md) |
| Next | macOS VPN services (status, on/off, keep it up, editing L2TP settings: done in 1.1.0); IPsec once a gateway is available to test (spike parked) | [docs/roadmap.md](docs/roadmap.md#next-up), [docs/ipsec-spike.md](docs/ipsec-spike.md) |
| Later | More connection types (SAML, …) and other improvements | TBC/TBD: [docs/roadmap.md](docs/roadmap.md) |
| 5. Decide on the engine | Keep openfortivpn, or build our own implementation | Not started (after phase 3) |

## Licence

[MIT](LICENSE), © 2026 ResultLab. The app bundles openfortivpn (GPLv3) and the OpenSSL it is built with (Apache 2.0), unmodified. It runs openfortivpn as a separate program and does not link it, so the GPL does not extend to this project's code. Each release publishes the exact openfortivpn and OpenSSL sources next to the DMG. See [THIRD-PARTY-NOTICES.md](THIRD-PARTY-NOTICES.md).

## Disclaimer

This project is not affiliated with or endorsed by Fortinet. FortiClient and FortiGate are trademarks of Fortinet, Inc. Check with your network administrator that third-party VPN clients are allowed before you use one.
