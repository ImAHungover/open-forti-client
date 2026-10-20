# IPsec spike: can macOS's own IPsec client replace openfortivpn?

Status (2026-10-20): **parked.** Part B is done without a gateway (Q6 answered, Q5 answered from the SDK, a first Q3 finding). Part A is parked: the gateway this project was built for stays on SSL VPN for now (its admins have no plans to migrate), so there is no IPsec gateway to test against. Resume when a FortiGate with IPsec dial-up and a test account is available. Owner: the maintainer.

## Why

openfortivpn speaks only Fortinet's **SSL VPN tunnel mode**. FortiOS 7.6.3 removed tunnel mode on every FortiGate model (7.6.0 and 7.6.1 had already removed it on smaller models), and IPsec, also over TCP 443, is Fortinet's replacement. Settings are not migrated, so every gateway upgraded past 7.6.3 needs an IPsec client. Without one, OpenFortiClient stops working there.

Before writing an engine, this spike answers whether **macOS's built-in IPsec client** (IKEv2, or IKEv1 with XAuth) connects to a FortiGate, and whether it keeps working in the setup this project exists for: a gateway reachable only through another VPN (L2TP), see [vision.md](vision.md). The spike writes no app code: the test tool in [spikes/ipsec-spike/](../spikes/ipsec-spike/) is throwaway.

## Questions to answer

| # | Question | Why it matters |
|---|---|---|
| Q1 | Which FortiOS version does the gateway run, and does it offer IPsec dial-up (IKEv2, IKEv1/XAuth)? | Tells us how soon SSL VPN goes away there, and which IPsec flavour to test |
| Q2 | Does macOS's built-in IKEv2 client connect, and with which login (EAP user and password, certificate)? | The built-in client cannot be tuned much: ciphers are fixed unless set in a configuration profile |
| Q3 | With the L2TP VPN up, do IKE (UDP 500/4500) and ESP go **through the L2TP interface** (`ppp0`), and does the gateway see the company's address? | This is exactly where FortiClient fails today. If macOS's IPsec does the same, the approach does not solve our problem |
| Q4 | Which routes and DNS does the tunnel set: only the VPN's subnets (split tunnel) or a default route? | Users report that the native client gets only a default route from a FortiGate |
| Q5 | If the gateway offers IPsec only over **TCP 443**, does macOS's client connect at all? | Not verified. If not, gateways that allow only TCP 443 are out of reach for this approach |
| Q6 | Can an app signed with our **Developer ID** create and start the same tunnel through `NEVPNManager`? | The engine would do this. It needs the Personal VPN entitlement, and whether Developer ID distribution allows it is unconfirmed |

## Before starting

**From the gateway's admin** (a test account is enough, no production change):

- [ ] The gateway's address and FortiOS version.
- [ ] Whether an IPsec dial-up tunnel exists, and if not, whether they will set up a test one. Fortinet's guide for Apple's native client is the reference: IKEv2, AES-256 / SHA-256 / DH group 14, EAP for users.
- [ ] Its details: IKE version, port (UDP 500/4500, or TCP), login method (EAP-MSCHAPv2, certificate, pre-shared key with XAuth for IKEv1), the gateway's **local ID** (macOS calls it *Remote ID*), and the CA that signed the gateway's certificate.
- [ ] Split tunnel or full tunnel, and the DNS servers and domains the tunnel should give.
- [ ] Someone who can run `diagnose debug application ike -1` on the FortiGate during the test, in case it fails.

Never put the pre-shared key or passwords in this document, a ticket or a log you share.

**On the Mac:** the L2TP VPN set up in System Settings as today, and the gateway's CA certificate in the login keychain (trusted for IPsec) if the gateway's certificate is not publicly trusted.

## Part A: by hand (Q1 to Q5)

1. **Baseline.** Connect the L2TP VPN. Record:
   ```sh
   route -n get <gateway-ip>        # interface must be ppp0
   netstat -rn -f inet > before-routes.txt
   scutil --dns > before-dns.txt
   ```
2. **Set up the tunnel.** System Settings → VPN → Add VPN Configuration → **IKEv2** (or **Cisco IPSec** for IKEv1 with XAuth): server address, Remote ID (the gateway's local ID), Local ID if the admin asks for one, and the login (Username for EAP, or Certificate).
   - **2b. If that fails or gives only a default route:** the dialog hides most settings, so try again with all of them set before concluding anything. Either fill in [fortigate-ikev2.mobileconfig.template](../spikes/ipsec-spike/fortigate-ikev2.mobileconfig.template) (ciphers, DH group, lifetimes, PFS, `UseConfigurationAttributeInternalIPSubnet` for the split tunnel) and install it, or put the same values in a `config.<name>.json` for the spike tool (Part B). Filled-in copies are gitignored; never commit them.
3. **Watch while connecting.** In two terminals:
   ```sh
   sudo tcpdump -ni any 'udp port 500 or udp port 4500 or esp or tcp port 443' -w ike.pcap
   log stream --info --predicate 'subsystem == "com.apple.networkextension" OR process == "neagent" OR process == "nesessionmanager"'
   ```
   Then connect it in System Settings.
4. **Record the result.**
   ```sh
   tcpdump -nr ike.pcap -e | head -50   # which interface carried IKE and ESP (Q3)
   netstat -rn -f inet > after-routes.txt; diff before-routes.txt after-routes.txt   # Q4
   scutil --dns > after-dns.txt; diff before-dns.txt after-dns.txt
   ping -c 3 <a host behind the gateway>
   ```
5. **If IKE leaves through the physical adapter (Q3 fails):** disconnect, add a host route for the gateway through the L2TP interface, and repeat steps 3 and 4. This is what the helper could do for the engine.
   ```sh
   sudo route -n add -host <gateway-ip> -interface ppp0
   sudo route -n delete -host <gateway-ip>    # afterwards
   ```
6. **If it fails to connect:** keep the `log stream` output and ask the admin for the FortiGate's IKE debug for the same minute. Typical causes: a Remote ID that does not match the gateway's local ID, an untrusted gateway certificate, or ciphers the gateway does not offer.

## Part B: from an app (Q6)

The tool is [spikes/ipsec-spike/](../spikes/ipsec-spike/): a command-line program inside an app bundle (`io.github.openforticlient.ipsec-spike`, its own App ID with the **Personal VPN** capability, so the release App ID and profile stay untouched). It saves, starts, stops and removes an `NEVPNProtocolIKEv2` configuration through `NEVPNManager`, with every IKE setting from a JSON file ([config.dummy.json](../spikes/ipsec-spike/config.dummy.json) shows the keys; the EAP password comes from `OFC_SPIKE_PASSWORD`).

```sh
./spikes/ipsec-spike/build.sh <TEAM_ID> --developer-id      # or without --developer-id: Apple Development
X=artifacts/ipsec-spike/developer-id/IPsecSpike.app/Contents/MacOS/IPsecSpike
$X save spikes/ipsec-spike/config.dummy.json                # macOS asks to allow adding a VPN configuration
$X start 45; $X stop; $X show; $X remove
log show --last 5m --info --predicate 'process == "NEIKEv2Provider"'   # /usr/bin/log in zsh
```

The dummy configuration points at `192.0.2.1`, an address reserved for documentation: nothing real is contacted.

**Q3 without a gateway.** Route `192.0.2.1` through the L2TP VPN and see which interface the IKE provider picks:
```sh
# L2TP connected
sudo route -n add -host 192.0.2.1 -interface ppp0
$X start 20; $X stop
log show --last 2m --info --predicate 'process == "NEIKEv2Provider"' | grep -E 'scoped interface|got socket'
sudo route -n delete -host 192.0.2.1
```
`interface ppp0` in the `got socket` line means IKE follows the route; the physical interface (`en0`, `en7`, …) means it ignores it, as FortiClient does.

## Results

Fill in when done.

| # | Result | Evidence |
|---|---|---|
| Q1 | Open: needs the admin | |
| Q2 | Open: needs a gateway | Ciphers are limited: macOS 26 removed DES, 3DES and SHA-1 (`API_OBSOLETED` in `NEVPNProtocolIKEv2.h`), AES-128 is deprecated since macOS 14. The FortiGate's proposals must include AES-256 (or AES-256-GCM / ChaCha20-Poly1305), SHA-2 and DH group 14 or higher |
| Q3 | **Open, but promising** (2026-10-20) | Apple's IKEv2 provider binds IKE to one *scoped* interface, chosen from the network path. With the L2TP VPN up and `192.0.2.1` routed through `ppp0`, it **chose `ppp0` first** (`got socket … interface ppp0`, source 192.168.2.2): unlike FortiClient, it follows the route. 45 ms later the send failed with `Network is unreachable`, because the provider's network answers ICMP *Destination Net Unreachable* for that documentation address (`ping` shows it from 62.155.240.192), and the provider then **retried on `en7`** ("Stopping tunnel before reattempting on new path"). So it falls back to the physical interface after a network error. Not yet known: whether it stays on `ppp0` when the gateway is reachable there. That needs a real gateway behind the L2TP VPN; an engine must also detect and stop a fallback to the wrong interface (the gateway would not answer from there) |
| Q4 | Open: needs a gateway | `useConfigurationAttributeInternalIPSubnet` exists for split tunnels |
| Q5 | **No** (from the SDK, 2026-10-20) | Neither `NEVPNProtocolIKEv2` nor `NEVPNProtocolIPSec` has any TCP or encapsulation setting: IKE runs on UDP 500/4500 only (`local 0.0.0.0:500` in the log). Gateways that accept IPsec only over TCP 443 are out of reach for macOS's client |
| Q6 | **Yes** (2026-10-20) | Built with Developer ID through Xcode's automatic signing, Apple issued "Mac Team Direct Provisioning Profile: io.github.openforticlient.ipsec-spike" (`ProvisionsAllDevices`, `com.apple.developer.networking.vpn.api = allow-vpn`). The Developer ID build saved the configuration (after the user allowed it once), started it, and got clear states and errors: `connecting`, then `NEIKEv2ErrorDomain Code=3 PeerDidNotRespond` after 31 s, then a retry. The configuration lives in the Network Extension store: `scutil --nc list` does not list it |

## Decision

| Outcome | Next step |
|---|---|
| Connects, and IKE goes through the L2TP VPN (by itself or with the host route) | Build an `NEVPNManager` engine behind `IVpnEngine` ([architecture.md](architecture.md#vpn-engine)), next to openfortivpn: an IPsec option per connection, the gateway host route through the helper if needed |
| Connects, but the traffic ignores the L2TP VPN even with the host route | The built-in client has FortiClient's problem. Evaluate strongSwan as a second engine behind the helper |
| Does not connect: TCP 443 only, or FortiGate needs FortiClient-specific behaviour | Evaluate strongSwan, and check which FortiOS IPsec features FortiClient relies on |
| Part B fails only on the Developer ID entitlement | Ship IPsec connections as a configuration profile (`.mobileconfig`) that the app generates and the user installs, and control it through `SCNetworkConnection` |
| The gateway offers no IPsec yet and stays below 7.6.3 | Park IPsec, do "Connect first" ([roadmap.md](roadmap.md#next-up)), and repeat the spike when the gateway moves to IPsec |

Record the outcome in [roadmap.md](roadmap.md), and keep this page as the record of how it was tested.

## Sources

- [OpenVPN: FortiOS 7.6.3, SSL VPN tunnel mode removed](https://openvpn.net/fortinet-ssl-vpn-migration/); [DEV Community](https://dev.to/madhushan/fortios-763-update-ssl-vpn-tunnel-mode-no-longer-supported-27hg); [Infortech](https://www.infortech.com/2025/12/18/fortinets-ssl-vpn-is-going-away-migrate-to-ipsec-vpn-now/). These are third-party summaries: check Fortinet's 7.6.3 release notes for your gateway.
- [Fortinet: Apple native IKEv2 to FortiGate](https://community.fortinet.com/fortigate-3/technical-tip-apple-ios-native-vpn-using-ikev2-connection-for-ipsec-vpn-to-fortigate-97833)
- [Fortinet forum: native macOS client gets only a default route](https://community.fortinet.com/t5/Support-Forum/Dial-UP-IPSec-IKEv2-Split-Tunnel/m-p/420587)
- [Fortinet KB: macOS IKEv2 certificate login fails with "Peer certificate is invalid"](https://community.fortinet.com/fortigate-3/troubleshooting-tip-macos-ikev2-dial-up-vpn-fails-with-peer-certificate-is-invalid-227693)
