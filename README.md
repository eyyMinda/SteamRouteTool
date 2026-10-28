# SteamRouteTool
A tool for blocking routes in Steam games that use Valve's SDR (Steam Datagram Relay) network config.

Current version: **1.0.3**. See [CHANGELOG.md](CHANGELOG.md) for release history.

## Supports:
Blocking specific routes for games that use Valve's SDR network config (e.g. Counter-Strike 2 / Counter-Strike: Global Offensive & Dota 2).

The route list is fetched from Steam's `ISteamApps/GetSDRConfig` API. Each blocked route gets three outbound Windows Firewall rules (UDP, TCP and ICMP), named `SteamRouteTool-<PROTOCOL>-<route>`.

On launch, the tool removes all of its existing `SteamRouteTool-` rules and re-creates them from the current network config. Rules with an outdated name format, for routes that no longer exist, or for relays that have been retired are dropped automatically.

## How to use
* Click the checkbox of the route(s) you'd like to block.
* Click the route name to reveal additional routes at that location (if available).
* Click "Blocked" to toggle all checkboxes.
* Click a ping value to re-ping that specific route.
* Clear Rules will clear all firewall rules.
* Ping Routes will ping all routes and update their values.

## Images
![Main](https://dev.froody.tech/SteamRouteTool/img/Main.png)

## Credits
#### Froody (2019)
Original author: tool creation. https://github.com/dfrood/SteamRouteTool
#### eyyMinda (2026)
Second author of the 1.0.2 updates: SDR config API, TCP/UDP/ICMP blocking, .NET 4.8. https://github.com/eyyMinda/SteamRouteTool
#### Newtonsoft
Newtonsoft.Json package, licensed under MIT. https://github.com/JamesNK/Newtonsoft.Json/
#### Icon
https://github.com/feathericons/feather#feather

Contact me on Discord for faster help: Froody#5824
