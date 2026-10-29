// SPDX-FileCopyrightText: 2026 Vargostrap
//
// SPDX-License-Identifier: MPL-2.0

using System.Net.NetworkInformation;
using System.Net.Sockets;

namespace Froststrap.Vargostrap.Network
{
    internal enum ProbeMethod
    {
        /// <summary>ICMP echo (regular ping) to the target.</summary>
        Icmp,
        /// <summary>Time to complete a TCP handshake on port 443. Used when the target ignores ICMP.</summary>
        Tcp443,
    }

    /// <summary>
    /// One latency measurement at a time to a host, from outside Roblox. Starts with ICMP; if the host
    /// never answers ICMP (common for game-server proxies), switches to timing TCP handshakes with a
    /// Roblox HTTPS endpoint, which measures the path to Roblox's network edge instead of the server.
    /// </summary>
    internal sealed class LatencyProbe : IDisposable
    {
        public const string FallbackHost = "www.roblox.com";

        private const int TimeoutMs = 1000;
        private const int IcmpAttemptsBeforeFallback = 5;

        private readonly Ping _ping = new();
        private int _icmpFailuresWithoutReply;
        private bool _icmpEverReplied;

        public string Target { get; }

        public ProbeMethod Method { get; private set; } = ProbeMethod.Icmp;

        /// <summary>The host actually being measured (differs from <see cref="Target"/> after a fallback).</summary>
        public string MeasuredHost => Method == ProbeMethod.Icmp ? Target : FallbackHost;

        public LatencyProbe(string target) => Target = target;

        /// <summary>Round-trip time in ms, or null if the probe was lost.</summary>
        public async Task<double?> ProbeAsync(CancellationToken cancellationToken)
        {
            if (Method == ProbeMethod.Icmp)
            {
                double? rtt = await ProbeIcmpAsync(Target);

                if (rtt is not null)
                {
                    _icmpEverReplied = true;
                    return rtt;
                }

                if (!_icmpEverReplied && ++_icmpFailuresWithoutReply >= IcmpAttemptsBeforeFallback)
                {
                    App.Logger.Info($"{Target} doesn't answer ping; measuring TCP handshakes to {FallbackHost} instead");
                    Method = ProbeMethod.Tcp443;
                }

                return null;
            }

            return await ProbeTcpAsync(FallbackHost, 443, cancellationToken);
        }

        private async Task<double?> ProbeIcmpAsync(string host)
        {
            try
            {
                PingReply reply = await _ping.SendPingAsync(host, TimeoutMs);
                return reply.Status == IPStatus.Success ? reply.RoundtripTime : null;
            }
            catch (PingException)
            {
                return null;
            }
        }

        private static async Task<double?> ProbeTcpAsync(string host, int port, CancellationToken cancellationToken)
        {
            using var socket = new Socket(SocketType.Stream, ProtocolType.Tcp) { NoDelay = true };
            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            timeout.CancelAfter(TimeoutMs);

            var sw = Stopwatch.StartNew();
            try
            {
                await socket.ConnectAsync(host, port, timeout.Token);
                return sw.Elapsed.TotalMilliseconds;
            }
            catch (Exception ex) when (ex is SocketException or OperationCanceledException && !cancellationToken.IsCancellationRequested)
            {
                return null;
            }
        }

        public void Dispose() => _ping.Dispose();
    }
}
