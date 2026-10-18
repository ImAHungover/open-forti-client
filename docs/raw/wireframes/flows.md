# User journey and flows



The target is have a good opensource alternative to forticlient VPN. Current version works well on windows but not on MAC (in some case)


## Problem Statement:

I have use my client(s) VPN network in germany to access his infrastructure. Client has Region restriction and I have to access it via main companies office VPN network to expose client servers . But on MAC I have some impediments .

### What’s happening

On every attempt (15:22, 15:23, 15:26, 15:27) `vpn-provider.log` shows the same thing:

```
Remote Hostname: vpn.client.example
TLS tunnel connection state: PREPARING
... 30 s later ...
WAITING (POSIXErrorCode 60: Operation timed out)  →  "Tunnel error: startTunnelFailed"
```

The TCP connection to the client's gateway never even opens. It isn’t a login, certificate or password problem.

The odd part is that a few seconds earlier, FortiTray (the app itself) **did** reach the same server. It got the pre-login answer (`ApiServerSalt: …`) from `https://vpn.client.example:443`. So one part of FortiClient gets through and another part doesn’t.

**Why:** on macOS, FortiClient 7.4.3 builds the tunnel in a separate system component (the “Network Extension”). That component sends its connection out the **physical network adapter** and ignores the company's L2TP VPN (`ppp0`). The connection therefore arrives at the client from your real, region-blocked IP. The client's firewall drops it silently, which is why you see a timeout and not a “refused” error. On Windows, FortiClient simply follows the routing table, so it goes through the company VPN and works.

### Tests on your Mac

| Path to vpn.client.example (203.0.113.10)       | Result                                      |
| ----------------------------------------------- | ------------------------------------------- |
| Through the company L2TP VPN (`ppp0`, the normal route) | ✅ HTTP 303 in 0.2 s                        |
| Bound to the physical adapter (`en7`)         | ❌ timeout, the same symptom as FortiClient |

### Things in the logs that are *not* the problem

* `System Extension is not enabled` (15:21:41): only on the first try after reinstalling. The extension is now `activated enabled`.
* `Failed to open TCC.db`, `DB select query failed`, `vpn_bk.plist not exist`: harmless noise.
* `JetsamEvent…ips`: a macOS memory-pressure report, unrelated.
* Another VPN app is installed (its network extension is active). It isn’t the cause here, but disable it while testing.

### Options to fix it

1. **Ask the client to allow your country or your current IP** for their gateway. This is the cleanest fix.
2. **Do the company VPN on a router, not on the Mac** (a travel router or a company-provided device), so all traffic leaves through the company network anyway.
3. **Use `openfortivpn`** (an open-source FortiGate SSL-VPN client, `brew install openfortivpn`). It follows the normal routing table, so it should go through `ppp0`. Check with the company's and the client's IT that it’s allowed first.
4. **Use a Windows VM on the Mac** with FortiClient inside it, since FortiClient works on Windows.
5. Report it to Fortinet or the client's IT as “FortiClient 7.4.3 macOS Network Extension bypasses L2TP VPN route”. An older FortiClient version may behave differently, but I haven’t checked that.

Solution is to use forticlient , wait 3 month to get the right enablement from and enterprise environment is not option. :)


## Main / Connect view

_(Screenshot of the official FortiClient screen; kept locally, not published.)_

Main window we can chose the VPN connection to connect . List view of the added VPN networks and configurations.  On the selected VPN network and configuration we can connect .

## Add or Edit Server

_(Screenshot of the official FortiClient screen; kept locally, not published.)_

On the Main View we have the oportunty to create new or edit the existing connection and configuration.

## Add or Edit Server Details

_(Screenshot of the official FortiClient screen; kept locally, not published.)_

On the Edit and Create new view we can :

chose the VPN Connection Type,

set the Connection Name m Description add Remote gateways (multiple on + we can define more)

Chose the authentication mode Set the username and add the Client Cert


## Settings Menu

_(Screenshot of the official FortiClient screen; kept locally, not published.)_

* Settings menu we can :
* Backup the configuration(s)

* Export or Clear the logs
* Set VPN options

* Advanced settings ie: Cookies etc.


## Notification

_(Screenshot of the official FortiClient screen; kept locally, not published.)_

List of notifications update happened etc..

## Information

_(Screenshot of the official FortiClient screen; kept locally, not published.)_


Version information and license agreements..
