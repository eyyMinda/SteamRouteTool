using SteamRouteTool.Models;

namespace SteamRouteTool.Services;

/// <summary>Picks the routes to keep for "Auto select": the fastest ones, by their best known ping.</summary>
public static class AutoSelector
{
    /// <summary>
    /// Keeps every route with a known ping of at most <paramref name="maxPingMs"/>. If fewer than <paramref name="minRoutes"/>
    /// qualify, the fastest <paramref name="minRoutes"/> routes with a known ping are kept instead, so the selection is never
    /// empty while any ping is known. Routes without a known ping aren't kept. Returns the kept routes, fastest first.
    /// </summary>
    public static IReadOnlyList<Pop> SelectRoutesToKeep(IEnumerable<(Pop Pop, int? PingMs)> routes, int maxPingMs, int minRoutes)
    {
        List<(Pop Pop, int PingMs)> known = routes
            .Where(route => route.PingMs != null)
            .Select(route => (route.Pop, route.PingMs!.Value))
            .OrderBy(route => route.Value)
            .ToList();

        List<(Pop Pop, int PingMs)> kept = known.Where(route => route.PingMs <= maxPingMs).ToList();
        if (kept.Count < minRoutes) { kept = known.Take(minRoutes).ToList(); }
        return kept.Select(route => route.Pop).ToList();
    }
}
