# Changelog

All notable changes to SteamRouteTool are documented here.
Versions follow `MAJOR.MINOR.PATCH`; every release bumps at least the patch number.

## [1.0.5] - 2026-10-28
_Updated by [eyyMinda](https://github.com/eyyMinda)._

### Fixed
- Ping results were written to the route list from background threads, which could corrupt the list or crash the app. Pings now run asynchronously and update the list safely.
- Pings check the reply status instead of treating a 0 ms reply as a failure, time out after 2 seconds, and run concurrently instead of using one thread per route.
- If the Steam network config can't be loaded, the app shows the error with a **Retry** option instead of crashing.
- Firewall errors (e.g. not running as administrator, or the firewall service stopped) show a message instead of crashing.
- If re-applying firewall rules on launch fails, the list still shows every route that's currently blocked. Previously one failure stopped the rest from being shown.
- **Clear Rules** no longer unchecks everything when removing the rules fails.
- The "Blocked" header now toggles based on the actual checkbox state, and asks for confirmation before blocking every route.
- Ping Routes is disabled while a ping run is in progress.

### Changed
- README screenshot is now stored in the repository (the old externally hosted image was unreachable).

## [1.0.4] - 2026-10-28
_Updated by [eyyMinda](https://github.com/eyyMinda)._

### Changed
- Migrated from .NET Framework 4.8 to .NET 10 (LTS). Running the app now requires the .NET 10 Desktop Runtime; building requires the .NET 10 SDK.
- Project converted to the SDK-style format; version and assembly info now live in `SteamRouteTool.csproj`.
- Network config is parsed with the built-in `System.Text.Json` and downloaded with `HttpClient`.
- Windows Firewall is accessed late-bound, so the project no longer needs a COM interop reference and builds with `dotnet build`.
- App manifest declares Windows 10/11 support.

### Removed
- Newtonsoft.Json dependency (12.0.2 had a known high-severity vulnerability).
- Unused `App.config`, `packages.config`, and empty Settings/Resources files.

## [1.0.3] - 2026-10-28
_Updated by [eyyMinda](https://github.com/eyyMinda)._

### Added
- On launch, all existing SteamRouteTool firewall rules are removed and re-created from the current network config. Rules with an outdated name format, for routes that no longer exist, or for retired relays are dropped automatically.

### Fixed
- Crash on launch when firewall rules from 1.0.1 or earlier (`SteamRouteTool-<route>`) were present.
- Unchecking the "Blocked" column header now removes the firewall rules instead of only clearing the checkboxes.
- **Clear Rules** now unchecks every row, not just the first few.
- Blocking or unblocking a route can no longer leave duplicate rules behind if removing an old rule fails.

### Removed
- Automatic cleanup of CSGORoutingTool firewall rules on launch.

## [1.0.2] - 2026-10-28
_Updated by [eyyMinda](https://github.com/eyyMinda)._

### Changed
- Route list is now fetched from Steam's `ISteamApps/GetSDRConfig` API (app 730) instead of the retired `network_config.json` CDN URL.
- Blocking a route now creates three outbound firewall rules (UDP, TCP and ICMP) instead of a single UDP rule, so pings and TCP traffic to blocked relays are blocked too.
- Firewall rules are named `SteamRouteTool-<PROTOCOL>-<route>`.
- Target framework updated from .NET Framework 4.7.2 to 4.8.
- About dialog and assembly info credit both authors.

## [1.0.1] - 2021-04-23
_By [Froody](https://github.com/dfrood)._

### Fixed
- Adjusted parsing to work with the newer `network_config.json` format.

## [1.0.0] - 2019-06-27
_By [Froody](https://github.com/dfrood)._

- Initial release.
