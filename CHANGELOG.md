# Changelog

## 1.1.0 (2026-10-21)

- **macOS VPN** (macOS only): the VPN services set up in System Settings (L2TP, Cisco IPSec, PPTP), such as a company L2TP VPN the gateway is reached through, appear on the Connect page and in the menu bar. They show their status (since when, which address), switch on and off, and can be kept up: started with the app and reconnected after a drop, with the same backoff as *Always up*. Quitting the app leaves them running.
- **Edit a macOS L2TP VPN** (macOS only): server, account, password, shared secret, *Send all traffic* and the keep-alive that decides how fast a dead connection is noticed, saved into the System Settings VPN through the privileged helper. The password and shared secret are never shown; left empty, they stay as saved.

## 1.0.0 (2026-10-20)

The first public release of OpenFortiClient, a ResultLab open-source client for Fortinet SSL-VPN gateways on macOS (Apple Silicon, macOS 14 or later).

- Connect, disconnect and see the status of SSL-VPN connections, with password, one-time password (OTP) and client certificate login.
- Several connections, each with several gateways tried in order (failover), and an optional saved password in the Keychain.
- A prompt to trust an unknown gateway certificate, remembered per connection.
- "Always up": reconnects with backoff after a dropped tunnel or a network change; auto-connect when the app starts.
- A menu bar icon for quick connect and disconnect; the app keeps running when its window closes.
- Notifications history, system notifications, backup and restore of connections, log export.
- A privileged helper (`SMAppService`) runs openfortivpn as root, so no sudoers rule is needed. It accepts only this app, runs only the openfortivpn bundled with it after checking its signature, and allows only the config keys the app writes.
- openfortivpn 1.24.1 is bundled, built from source with OpenSSL 3.5.9 (static); the release publishes both sources next to the DMG.
- Signed with Developer ID, hardened runtime, notarised. Installable with `brew install --cask imahungover/tap/openforticlient`.
