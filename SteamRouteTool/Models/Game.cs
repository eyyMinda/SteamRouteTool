namespace SteamRouteTool.Models;

/// <summary>A Steam game whose matchmaking goes through Valve's Steam Datagram Relay network.</summary>
/// <param name="ProcessName">The game's process (exe name without ".exe"), to notice when it starts.</param>
public sealed record Game(int AppId, string Name, string ProcessName)
{
    public const int CounterStrike2 = 730;

    /// <summary>
    /// Steam returns a network config for any app ID, so only games that actually use Valve's relays are listed.
    /// All of them share the same relays: blocking a relay blocks it for every game.
    /// </summary>
    public static IReadOnlyList<Game> Supported { get; } =
    [
        new Game(CounterStrike2, "Counter-Strike 2", "cs2"),
        new Game(570, "Dota 2", "dota2"),
        new Game(1422450, "Deadlock", "project8"),
    ];

    public static Game FromAppId(int appId)
    {
        return Supported.FirstOrDefault(game => game.AppId == appId) ?? Supported[0];
    }

    public override string ToString() => Name;
}
