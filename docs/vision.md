# Vision

## In one sentence

A free, open-source FortiGate SSL-VPN client for the Mac that **just connects**, with the comfort of FortiClient and none of its routing surprises.

## The problem

Many companies and their clients run FortiGate firewalls, so a lot of people have to use FortiClient VPN. On macOS that is often painful:

- **It ignores other VPN routes.** FortiClient 7.4.3 builds its tunnel in a macOS Network Extension, and that extension connects out of the physical adapter. If you must reach a gateway through another VPN first (for example an L2TP office VPN that gives you a German IP for a region-restricted client), the connection never arrives and you only see a 30-second timeout.
- **It is heavy.** The free VPN-only edition still installs EMS agents, vulnerability scanning, update services and a privileged helper, and it writes a lot of error noise to the logs.
- **Waiting for IT is not an option.** Getting an exception from an enterprise environment can take months.

The open-source command-line client [openfortivpn](https://github.com/adrienverge/openfortivpn) already solves the connection problem, because it follows the normal routing table. But it is a terminal tool. You have to write config files and remember sudo commands, and it gives you no status icon, saved profiles or notifications.

The full analysis is in [raw/wireframes/flows.md](raw/wireframes/flows.md).

## What we are building

A native-feeling Mac app that puts a FortiClient-style UI on top of openfortivpn:

- Pick a saved connection and press **Connect**, like in FortiClient.
- Manage connections: several gateways, saved login, client certificates.
- See clearly what is happening: the connection state, readable error messages and logs.
- Live in the menu bar for one-click connect and disconnect.

## Who it is for

1. **Consultants and contractors** who connect to several clients' FortiGate VPNs, sometimes chained behind another VPN.
2. **Developers and power users on the Mac** who are fed up with FortiClient's quirks but do not want to live in the terminal.
3. **Small teams** that want a light client without EMS or endpoint-management features.

## Goals

| Goal | How we know we got there |
|---|---|
| Connections that work wherever openfortivpn works | The motivating scenario (a client gateway reachable only through a company L2TP VPN) connects reliably |
| Familiar to FortiClient users | Every screen in [flows.md](raw/wireframes/flows.md) has a counterpart, and a FortiClient user needs no manual |
| Secure by default | Passwords live only in the Keychain. No secrets in logs, temp files or process arguments |
| Clear feedback | Every failure shows a human-readable cause and a next step, not a raw exit code |
| Light | One app plus openfortivpn. No background services while disconnected |

## Non-goals (for now)

- IPsec VPN, EMS / Zero Trust, endpoint compliance, vulnerability scanning, FSSO
- Replacing FortiClient for managed enterprise fleets
- Writing our own SSL-VPN protocol implementation (see the open questions below)
- App Store distribution (sandboxing would prevent us from starting openfortivpn)

## Principles

1. **Lean on proven open source.** openfortivpn does the networking. We do the experience.
2. **Swappable engine.** The UI talks to an engine interface, never to openfortivpn directly, so we can replace it later without a rewrite.
3. **Least privilege.** Only the smallest possible part runs as root, and it can do only one thing: start or stop the VPN.
4. **Honest about state.** The UI shows what the tunnel is really doing, not what we hope it is doing.
5. **Mac first, not Mac only.** We use .NET MAUI so Windows and Linux stay possible later.

## Phases

| Phase | Outcome |
|---|---|
| **0. Foundations** | Problem analysis, wireframes, architecture (this document set) |
| **1. MVP** | Connect view, one profile, start and stop openfortivpn, status and log view |
| **2. Daily driver** | Profile management, Keychain, several gateways, certificate trust prompt, notifications |
| **3. Comfort** | Menu bar icon, auto-connect, reconnect on network change, backup and restore |
| **4. Polish and release** | Privileged helper via `SMAppService`, signed and notarised build, Homebrew cask, client private key in the Keychain |
| **4b. Go live** | Developer ID signing, notarised DMG, Homebrew cask, public repository ([release.md](release.md)) |
| **5. Decide on the engine** | Keep openfortivpn, or build our own engine (see below) |

The current status of each phase is tracked in the [README roadmap](../README.md#roadmap). Improvements beyond these phases, including the connection types not supported yet, are in [roadmap.md](roadmap.md).

## Open questions

- **Own engine?** A custom .NET engine (TLS, then PPP over the Fortinet tunnel) would remove the root-level `pppd` dependency and allow a Network Extension. But it is a large, security-critical piece of work. Decide after phase 3, based on what openfortivpn cannot do.
- **SAML / SSO login.** openfortivpn has partial support (`--saml-login`). Do our users need it?
- **Two-factor authentication.** FortiToken and OTP prompts need a UI flow. Which ones do we see in practice?
- **Licence.** openfortivpn is GPLv3. We run it as a separate process, so our own code can use any licence. MIT is the working assumption.
