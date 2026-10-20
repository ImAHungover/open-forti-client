# Roadmap and future improvements

What comes after version 1.0.0. The delivered phases are in the [README](../README.md#roadmap); going live (notarised DMG, Homebrew) is [release.md](release.md).

Status words: **Done**; **Next**: planned, in this order; **TBC**, to be confirmed: wanted, but the need or the approach still has to be confirmed; **TBD**, to be decided: open, nothing decided yet.

## Next up

**Why IPsec matters.** openfortivpn speaks only Fortinet's SSL VPN tunnel mode, and FortiOS 7.6.3 removed tunnel mode on every FortiGate model in favour of IPsec (also over TCP 443); 7.6.0 and 7.6.1 had already removed it on smaller models. Each gateway upgraded past 7.6.3 stops working with OpenFortiClient until it has an IPsec engine. The gateway this project was built for stays on SSL VPN for now, so *Connect first* comes before IPsec, and IPsec waits for a gateway to test against.

| # | Item | Status | What |
|---|---|---|---|
| 1 | **Connect first** (chain a macOS VPN) | **Next** | A connection can name a VPN set up in System Settings (for example the company L2TP VPN). OpenFortiClient connects it first and waits until its route is up, then connects the gateway; on disconnect, optionally the other way round. Public `SCNetworkConnection` API (SystemConfiguration): start, stop and status of existing services. Small, and solves the setup in [vision.md](vision.md) without leaving the app |
| 2 | **IPsec spike** | **Parked** (2026-10-20) | Done without a gateway: a Developer ID app can drive macOS's IKEv2 client (`NEVPNManager`); no IPsec over TCP 443; IKE follows the route into the L2TP VPN but falls back to the physical interface after a network error. The rest needs an IPsec gateway and a test account: the gateway in [vision.md](vision.md) stays on SSL VPN for now. Plan and results: [ipsec-spike.md](ipsec-spike.md) |
| 3 | **IPsec engine** | After the spike | Most likely macOS's client through `NEVPNManager`, as a second engine behind `IVpnEngine`; strongSwan if the spike rules that out. The spike's decision table says which |

## Connection types

OpenFortiClient supports what openfortivpn supports: **Fortinet SSL-VPN** in tunnel mode, with username and password, an optional OTP, and an optional client certificate. FortiClient offers more. Each extra type needs its own engine behind `IVpnEngine` ([architecture.md](architecture.md#vpn-engine)) or new openfortivpn options.

| Connection type or login | Status | What it would take | Notes |
|---|---|---|---|
| SSL-VPN, username and password | **Done** | | |
| SSL-VPN, one-time password (FortiToken code, e-mail or SMS code) | **Done** | | Asked in a dialog when the gateway requests it |
| SSL-VPN, client certificate (PEM files, key in the Keychain) | **Done** | | |
| SSL-VPN, several gateways with failover | **Done** | | |
| SSL-VPN, **SAML / SSO** login (Azure AD, Okta, …) | **TBC** | openfortivpn `--saml-login`: it starts a local listener, the app opens the gateway's SAML page in the browser, and the browser sends the session cookie back. Needs a SAML option per connection and the browser hand-off in the UI | Confirm which users need it |
| SSL-VPN, **FortiToken Mobile push** (approve on the phone) | **TBC** | Depends on openfortivpn's support for the push flow. Today the user can type the code instead | Confirm whether gateways in use require push |
| SSL-VPN, **realm** (`https://gateway/realm`) | **TBC** | openfortivpn `realm` option: one field in the connection editor, one key in the helper's allowlist | Small |
| SSL-VPN through an **HTTP proxy** | **TBD** | openfortivpn has no proxy support; would need a tunnel such as `proxytunnel` or our own engine | |
| SSL-VPN, **certificate from the Keychain or a smart card** (instead of PEM files) | **TBD** | openfortivpn reads PEM files only; needs PKCS#11 / Keychain support in the engine | Phase 5 question |
| **IPsec VPN, IKEv2** | **TBC** ([spike](ipsec-spike.md), parked) | Not openfortivpn. Options: macOS's built-in IKEv2 through `NEVPNManager` (Personal VPN entitlement; whether Developer ID allows it is one of the spike's questions), or strongSwan as a second engine. Open: IPsec over TCP 443, split tunnel with the native client | FortiClient's "IPsec VPN" type, and Fortinet's replacement for SSL VPN tunnel mode from FortiOS 7.6.3 |
| **IPsec VPN, IKEv1 / XAuth** (FortiGate dial-up) | **TBC** ([spike](ipsec-spike.md), parked) | As IKEv2; macOS's built-in client covers IKEv1 with XAuth (`NEVPNProtocolIPSec`) | Older setups; weaker than IKEv2 |
| **L2TP over IPsec** as a connection type | **TBD**, unlikely | Old (IKEv1, pre-shared key, MS-CHAPv2), not offered by `NEVPNManager`, and built into macOS already. What the setup in [vision.md](vision.md) needs is ordering, not a new type: see *Connect first* in [Next up](#next-up) | |
| **ZTNA / EMS** (FortiClient EMS, zero trust tags, compliance) | **TBD** | Needs Fortinet's EMS protocol; there is no open implementation | Out of scope unless that changes |
| Our **own engine** (TLS plus PPP in .NET or Swift, Network Extension) | **TBD** | Phase 5 of [vision.md](vision.md): removes root and `pppd`; large and security-critical | Decide based on what openfortivpn cannot do |

## Other improvements

| Improvement | Status | Notes |
|---|---|---|
| Notarised DMG and Homebrew cask | **Done** (2026-10-20) | Phase 4b, [release.md](release.md): v1.0.0 on GitHub Releases and in `ImAHungover/homebrew-tap` |
| openfortivpn bundled in the app | **Done** (2026-10-19) | Built from pinned sources with a static OpenSSL 3.5 LTS; the helper runs only a signature-checked copy |
| Intel Macs (universal build) | **TBC** | Build the Swift parts and the bundled openfortivpn for `x86_64` too, and `lipo` them |
| Start at login (`SMAppService.mainApp`) | **TBC** | Left out of phase 3 on purpose; pairs with auto-connect |
| Native Mac look ("Optimize for Mac" instead of the scaled iPad look) | **TBD** | Larger text and Mac controls; changes every page's layout |
| Light theme | **TBD** | The ResultLab look is dark only today |
| Localisation (German, Hungarian, …) | **TBD** | All texts are English in XAML and view models today |
| Sleep and wake handling | **TBC** | A tunnel that dies during sleep is caught by "always up" today; an explicit reconnect on wake would be faster |
| Split DNS for the VPN's domains | **TBD** | Today openfortivpn sets DNS globally ("Use the VPN's DNS servers") |
| Windows front end | **TBD** | Core is plain .NET and could be reused; the engine would need a Windows openfortivpn alternative |
| Command-line front end | **TBD** | Core plus `SudoLauncher` already make this small |
| Diagnostics export with one click (logs, routes, DNS) | **TBC** | Settings → Export logs exists; adding `netstat -rn` and `scutil --dns` would help support |
