using SteamRouteTool.Models;

namespace SteamRouteTool.Services;

/// <summary>Picks the routes to keep for "Auto select": the fastest ones, by their best known ping.</summary>
public static class AutoSelector
{
    public const int DefaultRouteCount = 5;

    /// <summary>
    /// Returns the <paramref name="count"/> routes with the lowest known ping, fastest first. Routes without a known ping
    /// are never kept; equally fast routes keep their original order.
    /// </summary>
    public static IReadOnlyList<Pop> SelectFastest(IEnumerable<(Pop Pop, int? PingMs)> routes, int count)
    {
        return routes
            .Where(route => route.PingMs != null)
            .OrderBy(route => route.PingMs)
            .Take(count)
            .Select(route => route.Pop)
            .ToList();
    }
}
