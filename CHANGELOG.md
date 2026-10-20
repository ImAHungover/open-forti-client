# Changelog

## 1.0.0 (unreleased)

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
