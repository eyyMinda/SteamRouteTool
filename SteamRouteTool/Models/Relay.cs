namespace SteamRouteTool.Models
{
    /// <summary>A Steam Datagram Relay server. Game traffic goes to <see cref="Address"/> on ports <see cref="MinPort"/>-<see cref="MaxPort"/>.</summary>
    public sealed record Relay(string Address, int MinPort, int MaxPort);
}
