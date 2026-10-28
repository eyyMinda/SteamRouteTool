using System.Net.NetworkInformation;

namespace SteamRouteTool.Services;

/// <summary>
/// Pings hosts in two modes. Quick pings run several at a time with a short timeout, so networks without ICMP limits get
/// results in a second or two. Careful pings run one at a time with a longer timeout: many routers (flood protection) and
/// ISPs drop ICMP replies when several pings are in flight, and on such networks only sequential pings get reliable answers.
/// </summary>
public sealed class PingService
{
    const int QuickConcurrency = 8;
    static readonly TimeSpan QuickTimeout = TimeSpan.FromMilliseconds(800);
    static readonly TimeSpan CarefulTimeout = TimeSpan.FromSeconds(1);

    readonly SemaphoreSlim quickGate = new SemaphoreSlim(QuickConcurrency, QuickConcurrency);
    readonly SemaphoreSlim carefulGate = new SemaphoreSlim(1, 1);

    /// <summary>Returns the round-trip time in ms, or null if the host didn't answer. Up to 8 quick pings run at once.</summary>
    public Task<int?> PingQuicklyAsync(string address)
    {
        return PingAsync(address, quickGate, QuickTimeout);
    }

    /// <summary>Returns the round-trip time in ms, or null if the host didn't answer. Waits for earlier careful pings to finish first.</summary>
    public Task<int?> PingCarefullyAsync(string address)
    {
        return PingAsync(address, carefulGate, CarefulTimeout);
    }

    static async Task<int?> PingAsync(string address, SemaphoreSlim gate, TimeSpan timeout)
    {
        await gate.WaitAsync();
        try
        {
            using Ping ping = new Ping();
            PingReply reply = await ping.SendPingAsync(address, (int)timeout.TotalMilliseconds);
            if (reply.Status == IPStatus.Success) { return (int)reply.RoundtripTime; }
        }
        catch (PingException) { }
        finally
        {
            gate.Release();
        }
        return null;
    }
}
