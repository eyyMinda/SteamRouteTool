using System.Diagnostics;
using SteamRouteTool.Models;

namespace SteamRouteTool.Services;

/// <summary>
/// Notices when a supported game starts, by looking at the running processes every few seconds (no extra components
/// needed). Games already running when watching starts don't count as started. Events are raised on a background thread.
/// </summary>
public sealed class GameWatcher : IDisposable
{
    static readonly TimeSpan Interval = TimeSpan.FromSeconds(4);

    readonly Timer timer;
    HashSet<int>? running;

    public GameWatcher()
    {
        timer = new Timer(_ => Check(), null, TimeSpan.Zero, Interval);
    }

    /// <summary>Raised when a game's process appears that wasn't running at the last check.</summary>
    public event Action<Game>? GameStarted;

    void Check()
    {
        HashSet<int> now = Game.Supported.Where(IsRunning).Select(game => game.AppId).ToHashSet();
        HashSet<int>? before = running;
        running = now;
        if (before == null) { return; }
        foreach (Game game in FindStarted(before, now)) { GameStarted?.Invoke(game); }
    }

    /// <summary>The games running now that weren't before.</summary>
    internal static IReadOnlyList<Game> FindStarted(IReadOnlySet<int> before, IReadOnlySet<int> now)
    {
        return Game.Supported.Where(game => now.Contains(game.AppId) && !before.Contains(game.AppId)).ToList();
    }

    static bool IsRunning(Game game)
    {
        Process[] processes = Process.GetProcessesByName(game.ProcessName);
        foreach (Process process in processes) { process.Dispose(); }
        return processes.Length > 0;
    }

    public void Dispose() => timer.Dispose();
}
