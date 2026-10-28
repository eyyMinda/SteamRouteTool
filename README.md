# SteamRouteTool

A tool for blocking routes in Steam games that use Valve's SDR (Steam Datagram Relay) network config.

Current version: **1.0.4**. See [CHANGELOG.md](CHANGELOG.md) for release history.

## Supports:

Blocking specific routes for games that use Valve's SDR network config (e.g. Counter-Strike 2 / Counter-Strike: Global Offensive & Dota 2).

The route list is fetched from Steam's `ISteamApps/GetSDRConfig` API. Each blocked route gets three outbound Windows Firewall rules (UDP, TCP and ICMP), named `SteamRouteTool-<PROTOCOL>-<route>`.

On launch, the tool removes all of its existing `SteamRouteTool-` rules and re-creates them from the current network config. Rules with an outdated name format, for routes that no longer exist, or for relays that have been retired are dropped automatically.

## Requirements

- Windows 10 or 11.
- [.NET 10 Desktop Runtime](https://dotnet.microsoft.com/download/dotnet/10.0) (x64).
- Administrator rights (the app asks on launch), since it creates Windows Firewall rules.

## How to use

- Click the checkbox of the route(s) you'd like to block.
- Click the route name to reveal additional routes at that location (if available).
- Click "Blocked" to toggle all checkboxes.
- Click a ping value to re-ping that specific route.
- Clear Rules will clear all firewall rules.
- Ping Routes will ping all routes and update their values.

## Building

Requires the [.NET 10 SDK](https://dotnet.microsoft.com/download/dotnet/10.0) (pinned in `global.json`).

```sh
dotnet build -c Release
```

The output is in `SteamRouteTool/bin/Release/net10.0-windows/`. To open the project in Visual Studio, use Visual Studio 2026 or later.

## Images

![Main](https://dev.froody.tech/SteamRouteTool/img/Main.png)

## Credits

#### Froody (2019)

Original author: tool creation. https://github.com/dfrood/SteamRouteTool

#### eyyMinda (2026)

Second author of the 1.0.2+ updates: SDR config API, TCP/UDP/ICMP blocking, firewall rule cleanup, .NET 10 migration. https://github.com/eyyMinda/SteamRouteTool

#### Icon

https://github.com/feathericons/feather#feather
