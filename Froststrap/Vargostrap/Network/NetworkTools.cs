// SPDX-FileCopyrightText: 2026 Vargostrap
//
// SPDX-License-Identifier: MPL-2.0

using System.Buffers.Binary;
using System.Net;
using System.Net.NetworkInformation;
using System.Net.Sockets;

namespace Froststrap.Vargostrap.Network
{
    internal sealed record BufferbloatResult(
        double IdleMs, double DownloadLoadedMs, double UploadLoadedMs, double DownloadMbps, double UploadMbps, string Grade, string Advice);

    internal sealed record DnsResult(string Name, string Address, double? MedianMs, int Failures, bool IsSystem);

    /// <summary>
    /// Bufferbloat test: latency at idle, then while the connection is saturated downloading and then
    /// uploading (Cloudflare's public speed-test endpoints, the same ones speed.cloudflare.com uses).
    /// If latency jumps under load, the router's queue is too deep, and Smart Queue / SQM (fq_codel or CAKE)
    /// fixes it. Uses roughly a few hundred MB of data, depending on your connection speed.
    /// </summary>
    internal static class Bufferbloat
    {
        private static readonly TimeSpan IdlePhase = TimeSpan.FromSeconds(8);
        private static readonly TimeSpan LoadPhase = TimeSpan.FromSeconds(12);
        private const int Streams = 4;
        private const string DownloadUrl = "https://speed.cloudflare.com/__down?bytes=250000000";
        private const string UploadUrl = "https://speed.cloudflare.com/__up";

        public static async Task<BufferbloatResult> RunAsync(IProgress<string> progress, CancellationToken ct)
        {
            using var http = new HttpClient { Timeout = Timeout.InfiniteTimeSpan };
            http.DefaultRequestHeaders.UserAgent.ParseAdd($"{App.ProjectName}/{App.Version}");

            progress.Report("Measuring idle latency…");
            double idle = Median(await SampleLatencyAsync(IdlePhase, ct));

            progress.Report("Measuring latency while downloading…");
            var (downLatency, downBytes) = await UnderLoadAsync(token => DownloadAsync(http, token), ct);

            progress.Report("Measuring latency while uploading…");
            var (upLatency, upBytes) = await UnderLoadAsync(token => UploadAsync(http, token), ct);

            double down = Median(downLatency), up = Median(upLatency);
            double worstIncrease = Math.Max(down - idle, up - idle);

            string grade = worstIncrease switch
            {
                < 5 => "A+",
                < 30 => "A",
                < 60 => "B",
                < 200 => "C",
                < 400 => "D",
                _ => "F",
            };

            string advice = worstIncrease < 30
                ? "Your connection keeps latency low under load. Downloads and streams on your network shouldn't cause lag spikes in Roblox."
                : $"Latency rises by about {worstIncrease:0} ms when your connection is busy, so anyone downloading or streaming on your network will cause lag in Roblox. "
                  + "Turn on Smart Queue / SQM (also called fq_codel, CAKE, or \"Adaptive QoS\") in your router, set to about 90% of your measured speeds. "
                  + "If your router has no such option, the background traffic control above helps on this PC.";

            return new BufferbloatResult(idle, down, up,
                downBytes * 8 / LoadPhase.TotalSeconds / 1_000_000, upBytes * 8 / LoadPhase.TotalSeconds / 1_000_000, grade, advice);
        }

        private static async Task<(List<double> Latency, long Bytes)> UnderLoadAsync(Func<CancellationToken, Task<long>> load, CancellationToken ct)
        {
            using var loadCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
            Task<long>[] loads = [.. Enumerable.Range(0, Streams).Select(_ => Task.Run(() => load(loadCts.Token)))];

            await Task.Delay(1500, ct); // let the load ramp up
            List<double> latency = await SampleLatencyAsync(LoadPhase, ct);

            await loadCts.CancelAsync();
            long bytes = 0;
            foreach (Task<long> t in loads)
            {
                try { bytes += await t; } catch { /* cancelled */ }
            }
            return (latency, bytes);
        }

        private static async Task<List<double>> SampleLatencyAsync(TimeSpan duration, CancellationToken ct)
        {
            List<double> samples = [];
            using var probe = new LatencyProbe(LatencyProbe.FallbackHost);
            var sw = Stopwatch.StartNew();

            while (sw.Elapsed < duration)
            {
                if (await probe.ProbeAsync(ct) is double ms) samples.Add(ms);
                await Task.Delay(250, ct);
            }
            return samples;
        }

        private static async Task<long> DownloadAsync(HttpClient http, CancellationToken ct)
        {
            long total = 0;
            byte[] buffer = new byte[64 * 1024];
            try
            {
                using HttpResponseMessage response = await http.GetAsync(new Uri(DownloadUrl), HttpCompletionOption.ResponseHeadersRead, ct);
                await using Stream stream = await response.Content.ReadAsStreamAsync(ct);
                int read;
                while ((read = await stream.ReadAsync(buffer, ct)) > 0) total += read;
            }
            catch (Exception ex) when (ex is OperationCanceledException or IOException or HttpRequestException) { }
            return total;
        }

        private static async Task<long> UploadAsync(HttpClient http, CancellationToken ct)
        {
            var stream = new CountingRandomStream(ct);
            try
            {
                using var content = new StreamContent(stream, 64 * 1024);
                using HttpResponseMessage _ = await http.PostAsync(new Uri(UploadUrl), content, ct);
            }
            catch (Exception ex) when (ex is OperationCanceledException or IOException or HttpRequestException) { }
            return stream.BytesRead;
        }

        private static double Median(List<double> v)
        {
            if (v.Count == 0) return double.NaN;
            double[] s = [.. v.Order()];
            return s[s.Length / 2];
        }

        /// <summary>Endless upload body (random bytes so nothing compresses it) that stops when cancelled.</summary>
        private sealed class CountingRandomStream(CancellationToken ct) : Stream
        {
            private readonly byte[] _chunk = CreateChunk();
            public long BytesRead { get; private set; }
            public override bool CanRead => true;
            public override bool CanSeek => false;
            public override bool CanWrite => false;
            public override long Length => throw new NotSupportedException();
            public override long Position { get => BytesRead; set => throw new NotSupportedException(); }
            public override void Flush() { }
            public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
            public override void SetLength(long value) => throw new NotSupportedException();
            public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();

            public override int Read(byte[] buffer, int offset, int count)
            {
                if (ct.IsCancellationRequested) return 0;
                int n = Math.Min(count, _chunk.Length);
                Buffer.BlockCopy(_chunk, 0, buffer, offset, n);
                BytesRead += n;
                return n;
            }

            private static byte[] CreateChunk()
            {
                byte[] b = new byte[64 * 1024];
                System.Security.Cryptography.RandomNumberGenerator.Fill(b);
                return b;
            }
        }
    }

    /// <summary>
    /// Times DNS lookups of Roblox's domains against your current DNS and well-known public resolvers,
    /// using plain DNS queries over UDP. It only measures; it never changes your DNS settings.
    /// </summary>
    internal static class DnsBenchmark
    {
        private static readonly string[] Domains =
            ["roblox.com", "www.roblox.com", "apis.roblox.com", "gamejoin.roblox.com", "setup.rbxcdn.com", "tr.rbxcdn.com"];

        private static readonly (string Name, string Address)[] PublicResolvers =
        [
            ("Cloudflare", "1.1.1.1"),
            ("Google", "8.8.8.8"),
            ("Quad9", "9.9.9.9"),
            ("OpenDNS", "208.67.222.222"),
        ];

        public static async Task<List<DnsResult>> RunAsync(IProgress<string> progress, CancellationToken ct)
        {
            List<(string Name, string Address, bool IsSystem)> resolvers = [.. SystemResolvers().Select(a => ("Your current DNS", a, true))];
            foreach (var (name, address) in PublicResolvers)
                if (!resolvers.Any(r => r.Address == address)) resolvers.Add((name, address, false));

            List<DnsResult> results = [];
            foreach (var (name, address, isSystem) in resolvers)
            {
                progress.Report($"Testing {name} ({address})…");
                List<double> times = [];
                int failures = 0;

                // 3 rounds; the first warms the resolver's cache, like real browsing does
                for (int round = 0; round < 3; round++)
                {
                    foreach (string domain in Domains)
                    {
                        double? ms = await QueryAsync(address, domain, ct);
                        if (round == 0) continue;
                        if (ms is double t) times.Add(t); else failures++;
                    }
                }

                double[] sorted = [.. times.Order()];
                results.Add(new DnsResult(name, address, sorted.Length == 0 ? null : sorted[sorted.Length / 2], failures, isSystem));
            }

            return [.. results.OrderBy(r => r.MedianMs ?? double.MaxValue)];
        }

        private static IEnumerable<string> SystemResolvers() =>
            NetworkInterface.GetAllNetworkInterfaces()
                .Where(n => n.OperationalStatus == OperationalStatus.Up && n.NetworkInterfaceType != NetworkInterfaceType.Loopback
                    && n.GetIPProperties().GatewayAddresses.Count > 0)
                .SelectMany(n => n.GetIPProperties().DnsAddresses)
                .Where(a => a.AddressFamily == AddressFamily.InterNetwork)
                .Select(a => a.ToString())
                .Distinct()
                .Take(2);

        private static async Task<double?> QueryAsync(string server, string domain, CancellationToken ct)
        {
            using var udp = new UdpClient();
            ushort id = (ushort)System.Security.Cryptography.RandomNumberGenerator.GetInt32(1, ushort.MaxValue); // unpredictable IDs resist spoofed replies
            byte[] query = BuildQuery(id, domain);

            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(ct);
            timeout.CancelAfter(2000);
            var sw = Stopwatch.StartNew();

            try
            {
                await udp.SendAsync(query, new IPEndPoint(IPAddress.Parse(server), 53), timeout.Token);
                while (true)
                {
                    UdpReceiveResult reply = await udp.ReceiveAsync(timeout.Token);
                    if (reply.Buffer.Length >= 12 && BinaryPrimitives.ReadUInt16BigEndian(reply.Buffer) == id)
                        return sw.Elapsed.TotalMilliseconds;
                }
            }
            catch (Exception ex) when (ex is OperationCanceledException or SocketException)
            {
                ct.ThrowIfCancellationRequested();
                return null;
            }
        }

        /// <summary>Minimal DNS query: header (recursion desired) + one A-record question.</summary>
        private static byte[] BuildQuery(ushort id, string domain)
        {
            var bytes = new List<byte>(32 + domain.Length);
            bytes.AddRange([(byte)(id >> 8), (byte)id, 0x01, 0x00, 0x00, 0x01, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00]);
            foreach (string label in domain.Split('.'))
            {
                bytes.Add((byte)label.Length);
                bytes.AddRange(Encoding.ASCII.GetBytes(label));
            }
            bytes.AddRange([0x00, 0x00, 0x01, 0x00, 0x01]);
            return [.. bytes];
        }
    }
}
