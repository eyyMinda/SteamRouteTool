namespace SteamRouteTool.Models;

/// <summary>A Steam point of presence (route), e.g. "ams" / "Amsterdam (Netherlands)", and its relays.</summary>
public sealed record Pop(string Code, string Description, IReadOnlyList<Relay> Relays);
