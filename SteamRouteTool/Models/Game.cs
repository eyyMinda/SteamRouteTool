namespace SteamRouteTool.Models;

/// <summary>A Steam game whose matchmaking goes through Valve's Steam Datagram Relay network.</summary>
public sealed record Game(int AppId, string Name)
{
    public const int CounterStrike2 = 730;

    /// <summary>
    /// Steam returns a network config for any app ID, so only games that actually use Valve's relays are listed.
    /// All of them share the same relays: blocking a relay blocks it for every game.
    /// </summary>
    public static IReadOnlyList<Game> Supported { get; } =
    [
        new Game(CounterStrike2, "Counter-Strike 2"),
        new Game(570, "Dota 2"),
        new Game(1422450, "Deadlock"),
    ];

    public static Game FromAppId(int appId)
    {
        return Supported.FirstOrDefault(game => game.AppId == appId) ?? Supported[0];
    }

    public override string ToString() => Name;
}
