# Changelog

All notable changes to SteamRouteTool are documented here.
Versions follow `MAJOR.MINOR.PATCH`; every release bumps at least the patch number.

## [1.1.0] - 2026-10-28

_Updated by [eyyMinda](https://github.com/eyyMinda)._

### Added

- Pings from the last session are saved (`%LOCALAPPDATA%\SteamRouteTool\pings.json`) and shown greyed as soon as the app opens, until fresh results replace them.

### Changed

- Pinging adapts to the network. A quick round pings one relay per route with up to 8 pings at a time, which on most networks fills the whole list in a second or two. Whatever didn't answer is then retried one at a time, so networks that drop replies to several pings at once (e.g. router flood protection) still get reliable results.
- Routes visible on screen are pinged first.

## [1.0.9] - 2026-10-28

_Updated by [eyyMinda](https://github.com/eyyMinda)._

### Fixed

- A collapsed route showed `-1` whenever its first relay didn't answer pings, even if its other relays did. Collapsed routes now try their relays in order until one answers and show the best known ping; `-1` only means none of the tried relays answered.

### Changed

- Ping results are remembered per relay, so collapsing a route shows the best result right away instead of pinging again.

## [1.0.8] - 2026-10-28

_Updated by [eyyMinda](https://github.com/eyyMinda)._

### Fixed

- Pings almost always showed `-1`: since 1.0.5 all relays were pinged at once, and many routers and ISPs drop ICMP replies when several pings are in flight. Pings are now sent one at a time, only for the rows that are shown, and fill in from top to bottom.
- Slow, frozen startup: every launch removed and re-created all firewall rules (up to ~270 firewall changes) on the UI thread. Rules are now compared with what they should be and only changed rules are rewritten, in the background. An unchanged launch makes no firewall changes.
- The route list didn't fill the width of the window.

### Changed

- Window is larger, and the Name column fills the available width.
- Routes with several relays show **▸** / **▾**. Clicking the name expands them so relays can be blocked individually; routes with a single relay no longer react to name clicks.
- A pending ping shows a grey "…" instead of a black cell.
- Ping timeout lowered from 2 to 1 second.
- Clear Rules and the checkboxes are enabled once the firewall rules have been read on launch.

## [1.0.7] - 2026-10-28

_Updated by [eyyMinda](https://github.com/eyyMinda)._

### Added

- Unit tests (xUnit v3 on Microsoft.Testing.Platform) for config parsing, firewall rule-name parsing, and which rules are kept or dropped on launch.
- GitHub Actions: build and test on every pull request and push to `main`; pushing a `v*` tag publishes the self-contained exe, `SHA256SUMS.txt` and the changelog section as a GitHub Release.
- `.editorconfig` with the project's code style.

### Changed

- Nullable reference types and implicit usings enabled; all code uses file-scoped namespaces.
- Grid columns renamed to `colName` / `colPing` / `colBlocked` (fixes the last compiler warning).
- Solution migrated from `.sln` to `.slnx`.
- Line endings are now fixed to CRLF on checkout via `.gitattributes`, independent of local Git settings.

## [1.0.6] - 2026-10-28

_Updated by [eyyMinda](https://github.com/eyyMinda)._

### Fixed

- Blocking was incomplete for routes whose relays use ports above 27068 (Atlanta, Dallas, Sterling, Los Angeles, Chicago, Seattle, Stockholm and Sydney use up to 27140). Rules now use each relay's port range from the config instead of a fixed `27015-27068`.
- Collapsing a route no longer hides that some of its relays are blocked: a partly blocked route shows an indeterminate checkbox.
- Clicking a checkbox only changes it if Windows Firewall accepted the change.

### Changed

- Firewall rules are grouped under **SteamRouteTool** in Windows Defender Firewall.
- Internals split out of the main window into models (`Pop`, `Relay`) and services (`SdrConfigClient`, `FirewallService`, `PingService`), so the UI can be redesigned without touching the logic.
- Removed the obsolete `cloud-test` route filter (no longer present in Steam's config) and the unused `partners` flag.

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

_By [zaxo7 ](https://github.com/zaxo7)._

### Fixed

- Adjusted parsing to work with the newer `network_config.json` format.

## [1.0.0] - 2019-06-27

_By [Froody](https://github.com/dfrood)._

- Initial release.
