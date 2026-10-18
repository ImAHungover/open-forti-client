# Roadmap and future improvements

What comes after version 1.0.0. The delivered phases are in the [README](../README.md#roadmap); going live (notarised DMG, Homebrew) is [release.md](release.md).

Status words: **Done**; **TBC**, to be confirmed: wanted, but the need or the approach still has to be confirmed; **TBD**, to be decided: open, nothing decided yet.

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
| **IPsec VPN, IKEv2** | **TBD** | Not openfortivpn. Options: macOS's built-in IKEv2 through `NEVPNManager` (needs the Network Extension entitlement, so a paid account), or strongSwan as a second engine | FortiClient's "IPsec VPN" type |
| **IPsec VPN, IKEv1 / XAuth** (FortiGate dial-up) | **TBD** | As IKEv2; macOS's built-in client covers IKEv1 with XAuth through `NEVPNManager` | Older setups |
| **L2TP over IPsec** | **TBD** | macOS has it built in (System Settings), so low priority | Mentioned because the problem in [vision.md](vision.md) involves an L2TP VPN |
| **ZTNA / EMS** (FortiClient EMS, zero trust tags, compliance) | **TBD** | Needs Fortinet's EMS protocol; there is no open implementation | Out of scope unless that changes |
| Our **own engine** (TLS plus PPP in .NET or Swift, Network Extension) | **TBD** | Phase 5 of [vision.md](vision.md): removes root, `pppd` and the Homebrew dependency; large and security-critical | Decide based on what openfortivpn cannot do |

## Other improvements

| Improvement | Status | Notes |
|---|---|---|
| Notarised DMG and Homebrew cask | **TBD** | Phase 4b, [release.md](release.md); needs a paid Apple Developer account |
| openfortivpn bundled in the app | **TBC** | Part of phase 4b; removes the Homebrew dependency and the "binary writable by the admin user" risk |
| Intel Macs (universal build) | **TBC** | Build the Swift parts for `x86_64` too |
| Start at login (`SMAppService.mainApp`) | **TBC** | Left out of phase 3 on purpose; pairs with auto-connect |
| Native Mac look ("Optimize for Mac" instead of the scaled iPad look) | **TBD** | Larger text and Mac controls; changes every page's layout |
| Light theme | **TBD** | The ResultLab look is dark only today |
| Localisation (German, Hungarian, …) | **TBD** | All texts are English in XAML and view models today |
| Sleep and wake handling | **TBC** | A tunnel that dies during sleep is caught by "always up" today; an explicit reconnect on wake would be faster |
| Split DNS for the VPN's domains | **TBD** | Today openfortivpn sets DNS globally ("Use the VPN's DNS servers") |
| Windows front end | **TBD** | Core is plain .NET and could be reused; the engine would need a Windows openfortivpn alternative |
| Command-line front end | **TBD** | Core plus `SudoLauncher` already make this small |
| Diagnostics export with one click (logs, routes, DNS) | **TBC** | Settings → Export logs exists; adding `netstat -rn` and `scutil --dns` would help support |
