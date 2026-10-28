using SteamRouteTool.Services;

namespace SteamRouteTool.Models;

/// <summary>A Steam point of presence (route), e.g. "ams" / "Amsterdam (Netherlands)", its relays and its region.</summary>
public sealed record Pop(string Code, string Description, IReadOnlyList<Relay> Relays, string Region = RegionClassifier.Other);
