// SPDX-FileCopyrightText: 2026 Vargostrap
//
// SPDX-License-Identifier: MPL-2.0

namespace Froststrap.Vargostrap.Network
{
    internal sealed record NetworkSessionSummary(
        string Path, DateTime StartLocal, TimeSpan Duration, string Target, string Label, string Method,
        int Samples, double? AverageMs, double? P95Ms, double? MaxMs, double JitterMs, double LossPercent, int Spikes)
    {
        public bool IsLive => DateTime.UtcNow - File.GetLastWriteTimeUtc(Path) < TimeSpan.FromSeconds(5);

        public string Title => $"{StartLocal.ToString("g", CultureInfo.CurrentCulture)} · {Label}";

        public string Stats => Samples == 0
            ? "No samples"
            : $"avg {Fmt(AverageMs)} · 95% {Fmt(P95Ms)} · max {Fmt(MaxMs)} · jitter {JitterMs:0} ms · loss {LossPercent:0.#}% · {Spikes} spikes · {Duration:h\\:mm\\:ss}";

        public string TargetText => Method == "icmp" ? $"Ping to game server {Target}" : $"Server ignores ping: TCP handshake time to {LatencyProbe.FallbackHost}";

        private static string Fmt(double? ms) => ms is double v ? $"{v:0} ms" : "–";
    }

    /// <summary>
    /// CSV session logs in %LocalAppData%\Vargostrap\NetworkLogs, one file per game join.
    /// Format: '#'-prefixed metadata lines, then <c>time_utc,latency_ms,jitter_ms,loss_pct,spike</c>
    /// (latency empty = lost). Opens directly in Excel or Sheets, which is what "Export" copies.
    /// </summary>
    internal static class NetworkSessionLog
    {
        public static string Directory => System.IO.Path.Combine(Paths.Base, "NetworkLogs");

        private const int KeepNewest = 50;

        public static string NewSessionPath() =>
            System.IO.Path.Combine(Directory, $"session-{DateTime.Now:yyyyMMdd-HHmmss}.csv");

        public static void WriteHeader(StreamWriter w, string target, string label)
        {
            w.WriteLine($"# target: {target}");
            w.WriteLine($"# label: {label.Replace('\n', ' ')}");
            w.WriteLine("# method: icmp");
            w.WriteLine("time_utc,latency_ms,jitter_ms,loss_pct,spike");
        }

        public static void WriteSample(StreamWriter? w, NetworkSample s)
        {
            w?.WriteLine(string.Join(',',
                s.TimeUtc.ToString("o", CultureInfo.InvariantCulture),
                s.LatencyMs?.ToString("0.#", CultureInfo.InvariantCulture) ?? "",
                s.JitterMs.ToString("0.#", CultureInfo.InvariantCulture),
                s.LossPercent.ToString("0.#", CultureInfo.InvariantCulture),
                s.IsSpike ? "1" : "0"));
        }

        /// <summary>Newest first. Also prunes logs beyond the newest <see cref="KeepNewest"/>.</summary>
        public static List<NetworkSessionSummary> List()
        {
            List<NetworkSessionSummary> result = [];

            if (!System.IO.Directory.Exists(Directory))
                return result;

            string[] files = [.. System.IO.Directory.GetFiles(Directory, "session-*.csv").OrderDescending()];

            foreach (string old in files.Skip(KeepNewest))
            {
                try
                {
                    File.Delete(old);
                    File.Delete(System.IO.Path.ChangeExtension(old, ".traceroute.txt"));
                }
                catch { /* in use or already gone */ }
            }

            foreach (string file in files.Take(KeepNewest))
            {
                if (TryRead(file) is NetworkSessionSummary summary)
                    result.Add(summary);
            }

            return result;
        }

        public static NetworkSessionSummary? TryRead(string path) => TryRead(path, out _);

        /// <summary>Reads a log; <paramref name="tail"/> gets the last <see cref="NetworkMonitor.Window"/> samples (for the live chart).</summary>
        public static NetworkSessionSummary? TryRead(string path, out List<NetworkSample> tail)
        {
            tail = [];

            try
            {
                using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
                using var reader = new StreamReader(stream);

                string target = "?", label = "?", method = "icmp";
                List<double> latencies = [];
                int samples = 0, lost = 0, spikes = 0;
                double jitter = 0;
                DateTime? first = null, last = null;
                var recent = new Queue<NetworkSample>();

                while (reader.ReadLine() is string line)
                {
                    if (line.StartsWith('#'))
                    {
                        if (line.StartsWith("# target: ", StringComparison.Ordinal)) target = line[10..];
                        else if (line.StartsWith("# label: ", StringComparison.Ordinal)) label = line[9..];
                        else if (line.StartsWith("# method: tcp443", StringComparison.Ordinal))
                        {
                            method = "tcp443";
                            latencies.Clear(); samples = 0; lost = 0; spikes = 0; recent.Clear();
                        }
                        continue;
                    }

                    string[] parts = line.Split(',');
                    if (parts.Length < 5 || !DateTime.TryParse(parts[0], CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind, out DateTime time))
                        continue;

                    double? latency = double.TryParse(parts[1], NumberStyles.Float, CultureInfo.InvariantCulture, out double l) ? l : null;
                    double.TryParse(parts[2], NumberStyles.Float, CultureInfo.InvariantCulture, out jitter);
                    double.TryParse(parts[3], NumberStyles.Float, CultureInfo.InvariantCulture, out double loss);
                    bool spike = parts[4] == "1";

                    first ??= time;
                    last = time;
                    samples++;
                    if (latency is double ms) latencies.Add(ms); else lost++;
                    if (spike) spikes++;

                    recent.Enqueue(new NetworkSample(time, latency, jitter, loss, spike));
                    while (recent.Count > NetworkMonitor.Window) recent.Dequeue();
                }

                tail = [.. recent];

                double[] sorted = [.. latencies.Order()];
                return new NetworkSessionSummary(
                    path,
                    (first ?? File.GetCreationTimeUtc(path)).ToLocalTime(),
                    (last - first) ?? TimeSpan.Zero,
                    target, label, method, samples,
                    sorted.Length == 0 ? null : sorted.Average(),
                    sorted.Length == 0 ? null : sorted[(int)Math.Min(sorted.Length - 1, Math.Ceiling(sorted.Length * 0.95) - 1)],
                    sorted.Length == 0 ? null : sorted[^1],
                    jitter,
                    samples == 0 ? 0 : 100.0 * lost / samples,
                    spikes);
            }
            catch (Exception ex)
            {
                App.Logger.Warn($"Couldn't read network log {path}: {ex.Message}");
                return null;
            }
        }
    }
}
