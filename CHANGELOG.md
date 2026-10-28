# Changelog

All notable changes to SteamRouteTool are documented here.
Versions follow `MAJOR.MINOR.PATCH`; every release bumps at least the patch number.

## [1.0.2] - 2026-10-28
_Updated by [eyyMinda](https://github.com/eyyMinda)._

### Changed
- Route list is now fetched from Steam's `ISteamApps/GetSDRConfig` API (app 730) instead of the retired `network_config.json` CDN URL.
- Blocking a route now creates three outbound firewall rules (UDP, TCP and ICMP) instead of a single UDP rule, so pings and TCP traffic to blocked relays are blocked too.
- Firewall rules are named `SteamRouteTool-<PROTOCOL>-<route>`.
- Target framework updated from .NET Framework 4.7.2 to 4.8.
- About dialog and assembly info credit both authors.

### Notes
- Rules created by 1.0.1 and earlier (`SteamRouteTool-<route>`) use the old naming scheme. Use **Clear Rules** before upgrading.

## [1.0.1] - 2021-04-23
_By [Froody](https://github.com/dfrood)._

### Fixed
- Adjusted parsing to work with the newer `network_config.json` format.

## [1.0.0] - 2019-06-27
_By [Froody](https://github.com/dfrood)._

- Initial release.
