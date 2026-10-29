// SPDX-FileCopyrightText: 2026 Vargostrap
//
// SPDX-License-Identifier: MPL-2.0

using System.Net;
using System.Net.NetworkInformation;

namespace Froststrap.Vargostrap.Network
{
    /// <summary>Minimal ICMP traceroute (increasing TTL), the same technique as Windows' tracert, without DNS lookups.</summary>
    internal static class Traceroute
    {
        private const int MaxHops = 30;
        private const int TimeoutMs = 1000;

        public static async Task<string> RunAsync(string host, CancellationToken cancellationToken)
        {
            var sb = new StringBuilder();
            sb.AppendLine(CultureInfo.InvariantCulture, $"Traceroute to {host} at {DateTime.Now:yyyy-MM-dd HH:mm:ss}");

            using var ping = new Ping();
            byte[] payload = new byte[32];

            for (int ttl = 1; ttl <= MaxHops; ttl++)
            {
                cancellationToken.ThrowIfCancellationRequested();

                var sw = Stopwatch.StartNew();
                PingReply reply;
                try
                {
                    reply = await ping.SendPingAsync(host, TimeoutMs, payload, new PingOptions(ttl, dontFragment: true));
                }
                catch (PingException ex)
                {
                    sb.AppendLine(CultureInfo.InvariantCulture, $"{ttl,2}  error: {ex.Message}");
                    break;
                }

                string address = reply.Address is { } a && !a.Equals(IPAddress.Any) ? a.ToString() : "*";
                string time = reply.Status is IPStatus.Success or IPStatus.TtlExpired ? $"{sw.ElapsedMilliseconds} ms" : "timed out";
                sb.AppendLine(CultureInfo.InvariantCulture, $"{ttl,2}  {time,10}  {address}");

                if (reply.Status == IPStatus.Success)
                    break;
            }

            return sb.ToString();
        }
    }
}
