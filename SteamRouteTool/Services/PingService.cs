using System;
using System.Net.NetworkInformation;
using System.Threading.Tasks;

namespace SteamRouteTool.Services
{
    public sealed class PingService
    {
        readonly int timeoutMs;

        public PingService(TimeSpan timeout)
        {
            timeoutMs = (int)timeout.TotalMilliseconds;
        }

        /// <summary>Returns the round-trip time in ms, or null if the host didn't answer in time.</summary>
        public async Task<int?> PingAsync(string address)
        {
            try
            {
                using (Ping ping = new Ping())
                {
                    PingReply reply = await ping.SendPingAsync(address, timeoutMs);
                    if (reply.Status == IPStatus.Success) { return (int)reply.RoundtripTime; }
                }
            }
            catch (PingException) { }
            return null;
        }
    }
}
