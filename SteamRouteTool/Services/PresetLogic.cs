using SteamRouteTool.Localization;
using SteamRouteTool.Models;

namespace SteamRouteTool.Services;

/// <summary>Saving the blocked routes as a preset, and turning a preset back into the relays to block.</summary>
public static class PresetLogic
{
    public const int MaxNameLength = 40;

    /// <summary>Records what's blocked: fully blocked routes by code, partly blocked routes by their blocked relays.</summary>
    public static GamePreset Capture(string name, IEnumerable<Pop> pops, IReadOnlySet<Relay> blocked)
    {
        GamePreset preset = new GamePreset { Name = name };
        foreach (Pop pop in pops)
        {
            preset.KnownRoutes.Add(pop.Code);
            List<Relay> relays = pop.Relays.Where(blocked.Contains).ToList();
            if (relays.Count == 0) { continue; }
            if (relays.Count == pop.Relays.Count) { preset.BlockedRoutes.Add(pop.Code); }
            else { preset.BlockedRelays.AddRange(relays.Select(relay => relay.Address)); }
        }
        return preset;
    }

    /// <summary>
    /// The relays to block for a preset. Routes and relays that no longer exist are skipped. With
    /// <paramref name="blockNewRoutes"/>, routes added after the preset was saved are blocked too.
    /// </summary>
    public static HashSet<Relay> Resolve(GamePreset preset, IEnumerable<Pop> pops, bool blockNewRoutes = false)
    {
        HashSet<string> routes = preset.BlockedRoutes.ToHashSet();
        HashSet<string> addresses = preset.BlockedRelays.ToHashSet();
        HashSet<string> known = preset.KnownRoutes.ToHashSet();
        bool blockUnknown = blockNewRoutes && known.Count > 0;
        return pops
            .SelectMany(pop => routes.Contains(pop.Code) || (blockUnknown && !known.Contains(pop.Code))
                ? pop.Relays
                : pop.Relays.Where(relay => addresses.Contains(relay.Address)))
            .ToHashSet();
    }

    /// <summary>Whether applying the preset would block exactly what's blocked now.</summary>
    public static bool Matches(GamePreset preset, IEnumerable<Pop> pops, IReadOnlySet<Relay> blocked, bool blockNewRoutes = false)
    {
        return Resolve(preset, pops, blockNewRoutes).SetEquals(blocked);
    }

    /// <summary>
    /// Routes in <paramref name="pops"/> that weren't in <paramref name="knownCodes"/>. Nothing is new when there's no
    /// earlier list (the first time a game is loaded).
    /// </summary>
    public static List<Pop> FindNewRoutes(IReadOnlyCollection<string>? knownCodes, IEnumerable<Pop> pops)
    {
        if (knownCodes == null || knownCodes.Count == 0) { return new List<Pop>(); }
        HashSet<string> known = knownCodes.ToHashSet();
        return pops.Where(pop => !known.Contains(pop.Code)).ToList();
    }

    /// <summary>The name as it's saved (trimmed and shortened), or null if it's empty.</summary>
    public static string? CleanName(string? name)
    {
        string trimmed = (name ?? "").Trim();
        if (trimmed.Length == 0) { return null; }
        return trimmed.Length > MaxNameLength ? trimmed[..MaxNameLength].TrimEnd() : trimmed;
    }

    /// <summary>The preset with this name, ignoring case, or null.</summary>
    public static GamePreset? Find(IEnumerable<GamePreset> presets, string name)
    {
        return presets.FirstOrDefault(preset => preset.Name.Equals(name, StringComparison.OrdinalIgnoreCase));
    }

    /// <summary>"12 routes blocked", "12 routes and 2 relays blocked" or "Nothing blocked".</summary>
    public static string Describe(GamePreset preset)
    {
        int routes = preset.BlockedRoutes.Count;
        int relays = preset.BlockedRelays.Count;
        if (routes == 0 && relays == 0) { return Loc.T("Presets.NothingBlocked"); }
        if (relays == 0) { return Loc.Plural("Presets.RoutesBlocked", routes); }
        if (routes == 0) { return Loc.Plural("Presets.RelaysBlocked", relays); }
        return Loc.T("Presets.RoutesAndRelaysBlocked", routes, relays);
    }
}
