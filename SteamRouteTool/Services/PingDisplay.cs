using SteamRouteTool.Models;

namespace SteamRouteTool.Services;

public enum PingLevel
{
    /// <summary>Not pinged yet and nothing cached.</summary>
    None,
    Good,
    Fair,
    Poor,
    /// <summary>No reply.</summary>
    Failed,
    /// <summary>A value from the last session, or a ping in progress.</summary>
    Stale,
    Blocked,
}

/// <summary>What to show in a ping cell, and how to sort it.</summary>
public readonly record struct PingText(string Text, PingLevel Level, int SortValue);

public static class PingDisplay
{
    public const int GoodMs = 50;
    public const int FairMs = 100;

    /// <summary>
    /// Describes the ping of a single relay or a whole route (its best relay). Blocked relays can't answer pings, so only
    /// unblocked ones count. Fresh results win; while pinging (or before the first ping) the last session's value is shown as stale.
    /// </summary>
    public static PingText Describe(IEnumerable<Relay> relays, IReadOnlySet<Relay> blocked, IReadOnlyDictionary<string, int?> results,
        IReadOnlySet<string> pending, IReadOnlyDictionary<string, int> cached)
    {
        List<Relay> pingable = relays.Where(relay => !blocked.Contains(relay)).ToList();
        if (pingable.Count == 0) { return new PingText("blocked", PingLevel.Blocked, int.MaxValue - 1); }

        int? best = pingable.Where(relay => results.ContainsKey(relay.Address)).Select(relay => results[relay.Address]).Min();
        if (best is int ms) { return new PingText(ms + " ms", ms <= GoodMs ? PingLevel.Good : ms <= FairMs ? PingLevel.Fair : PingLevel.Poor, ms); }

        int? stale = pingable.Select(relay => cached.TryGetValue(relay.Address, out int value) ? value : (int?)null).Min();
        bool isPending = pingable.Any(relay => pending.Contains(relay.Address));
        bool wasPinged = pingable.Any(relay => results.ContainsKey(relay.Address));
        if (isPending || !wasPinged)
        {
            if (stale is int staleMs) { return new PingText(staleMs + " ms", PingLevel.Stale, staleMs); }
            return isPending ? new PingText("…", PingLevel.Stale, int.MaxValue - 2) : new PingText("", PingLevel.None, int.MaxValue);
        }
        return new PingText("no reply", PingLevel.Failed, int.MaxValue - 3);
    }
}
