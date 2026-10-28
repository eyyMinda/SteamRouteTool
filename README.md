# SteamRouteTool

A tool for blocking routes in Steam games that use Valve's SDR (Steam Datagram Relay) network config.

Current version: **1.1.0**. See [CHANGELOG.md](CHANGELOG.md) for release history.

## Supports:

Blocking specific routes for games that use Valve's SDR network config (e.g. Counter-Strike 2 / Counter-Strike: Global Offensive & Dota 2).

The route list is fetched from Steam's `ISteamApps/GetSDRConfig` API. Each blocked route gets three outbound Windows Firewall rules (UDP, TCP and ICMP), named `SteamRouteTool-<PROTOCOL>-<route>` and grouped under **SteamRouteTool** in Windows Defender Firewall. The blocked ports come from each relay's port range in the config.

On launch, the tool reads its existing `SteamRouteTool-` rules, shows what's blocked, and brings the rules in line with the current network config in the background. Only rules that differ are rewritten; rules with an outdated name format, for routes that no longer exist, or for relays that have been retired are removed.

## Requirements

- Windows 10 or 11.
- [.NET 10 Desktop Runtime](https://dotnet.microsoft.com/download/dotnet/10.0) (x64).
- Administrator rights (the app asks on launch), since it creates Windows Firewall rules.

## How to use

- Click the checkbox of the route(s) you'd like to block.
- Routes marked **▸** have several relay servers. Click the name to expand (**▾**) and block individual relays; click again to collapse. A filled checkbox (■) means only some relays of that route are blocked.
- Click "Blocked" to toggle all checkboxes.
- Click a ping value to re-ping that specific relay.
- Clear Rules will clear all firewall rules.
- Ping Routes will ping all routes and update their values, routes on screen first. A collapsed route shows the best ping of its relays; `-1` means none of them replied.
- Greyed pings are from your last session and are replaced as fresh results arrive.

Pinging runs in two rounds: a quick round with several pings at a time fills the list in a second or two on most networks. Routes that didn't answer are then retried one at a time, because some routers (flood protection) and ISPs drop replies when several pings are in flight; on such networks the list fills in over 10–20 seconds.

## Building

Requires the [.NET 10 SDK](https://dotnet.microsoft.com/download/dotnet/10.0) (pinned in `global.json`).

```sh
dotnet build -c Release
dotnet test
```

The output is in `SteamRouteTool/bin/Release/net10.0-windows/`. To open the solution (`SteamRouteTool.slnx`) in Visual Studio, use Visual Studio 2026 or later.

Tests use xUnit v3 on Microsoft.Testing.Platform (enabled in `global.json`) and cover config parsing and firewall rule handling without touching the real firewall. GitHub Actions builds and tests every pull request and push to `main`.

## Releases

Pushing a tag that matches the version in `SteamRouteTool/SteamRouteTool.csproj` (e.g. `v1.0.7`) publishes a self-contained `SteamRouteTool-<version>-win-x64.exe` to [GitHub Releases](../../releases), with `SHA256SUMS.txt` and the matching `CHANGELOG.md` section as release notes.

## Images

![Steam Route Tool main window](docs/screenshot.png)

## Credits

#### Froody (2019)

Original author: tool creation. https://github.com/dfrood/SteamRouteTool

#### eyyMinda (2026)

Second author of the 1.0.2+ updates: SDR config API, TCP/UDP/ICMP blocking, firewall rule cleanup, .NET 10 migration. https://github.com/eyyMinda/SteamRouteTool

#### Icon

https://github.com/feathericons/feather#feather
