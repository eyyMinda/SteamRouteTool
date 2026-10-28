using System.Net.NetworkInformation;

namespace SteamRouteTool.Services;

/// <summary>
/// Pings hosts one at a time. Many routers and ISPs drop ICMP replies when several pings are in flight at once
/// (pinging all relays concurrently got almost no replies), so requests are queued and sent sequentially.
/// </summary>
public sealed class PingService
{
    readonly int timeoutMs;
    readonly SemaphoreSlim gate = new SemaphoreSlim(1, 1);

    public PingService(TimeSpan timeout)
    {
        timeoutMs = (int)timeout.TotalMilliseconds;
    }

    /// <summary>Returns the round-trip time in ms, or null if the host didn't answer in time. Waits for earlier pings to finish first.</summary>
    public async Task<int?> PingAsync(string address)
    {
        await gate.WaitAsync();
        try
        {
            using Ping ping = new Ping();
            PingReply reply = await ping.SendPingAsync(address, timeoutMs);
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
