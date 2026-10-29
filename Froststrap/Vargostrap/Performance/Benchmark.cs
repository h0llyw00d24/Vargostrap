// SPDX-FileCopyrightText: 2026 Vargostrap
//
// SPDX-License-Identifier: MPL-2.0

using System.ComponentModel;
using System.Runtime.Versioning;
using Froststrap.Vargostrap.Native;

namespace Froststrap.Vargostrap.Performance
{
    internal sealed record BenchmarkResult(
        string Id, string Label, DateTime TakenLocal, int Seconds,
        double? AvgFps, double? OnePercentLowFps, double? AvgFrameTimeMs, double? P99FrameTimeMs, double? GpuBusyPercent,
        double RobloxCpuPercent, double SystemCpuPercent, double RobloxRamMb, int Frames)
    {
        public string Title => $"{Label} · {TakenLocal.ToString("g", CultureInfo.CurrentCulture)}";

        public string Summary => AvgFps is double fps
            ? $"{fps:0} FPS avg · {OnePercentLowFps:0} FPS 1% low · {AvgFrameTimeMs:0.0} ms frame time (99%: {P99FrameTimeMs:0.0} ms) · GPU {Fmt(GpuBusyPercent)} · Roblox CPU {RobloxCpuPercent:0}% · PC CPU {SystemCpuPercent:0}% · RAM {RobloxRamMb:0} MB"
            : $"FPS not measured (PresentMon not set) · Roblox CPU {RobloxCpuPercent:0}% · PC CPU {SystemCpuPercent:0}% · RAM {RobloxRamMb:0} MB";

        private static string Fmt(double? pct) => pct is double v ? $"{v:0}%" : "n/a";
    }

    /// <summary>
    /// Measures a stretch of gameplay so changes can be compared before/after. Everything is read from
    /// outside Roblox:
    /// <list type="bullet">
    /// <item>FPS, frame times and GPU busy from Intel's open-source PresentMon, which reads Windows'
    /// graphics event tracing (ETW). Its path is chosen by the user; Vargostrap doesn't bundle or
    /// download it. ETW needs admin, so PresentMon starts with a UAC prompt.</item>
    /// <item>Roblox CPU from its process times, RAM from its working set, PC CPU from GetSystemTimes.</item>
    /// </list>
    /// </summary>
    [SupportedOSPlatform("windows")]
    internal static class Benchmark
    {
        public const string PresentMonUrl = "https://github.com/GameTechDev/PresentMon/releases";
        private const string RobloxProcess = "RobloxPlayerBeta";

        public static string Folder => Path.Combine(Paths.Base, "Benchmarks");
        private static string ResultsFile => Path.Combine(Folder, "results.json");

        public static async Task<BenchmarkResult> RunAsync(string label, int seconds, string? presentMonPath, IProgress<string> progress, CancellationToken ct)
        {
            Process roblox = Process.GetProcessesByName(RobloxProcess).FirstOrDefault()
                ?? throw new InvalidOperationException("Roblox isn't running. Join a game, then start the benchmark.");

            Directory.CreateDirectory(Folder);
            string id = DateTime.Now.ToString("yyyyMMdd-HHmmss", CultureInfo.InvariantCulture);
            string csv = Path.Combine(Folder, $"frames-{id}.csv");

            Process? presentMon = null;
            if (!string.IsNullOrWhiteSpace(presentMonPath))
            {
                if (!File.Exists(presentMonPath))
                    throw new FileNotFoundException("PresentMon wasn't found at the chosen path.", presentMonPath);

                try
                {
                    presentMon = Process.Start(new ProcessStartInfo
                    {
                        FileName = presentMonPath,
                        Arguments = $"--process_name {RobloxProcess}.exe --output_file \"{csv}\" --timed {seconds} --terminate_after_timed " +
                                    "--stop_existing_session --session_name Vargostrap --no_console_stats",
                        Verb = "runas",
                        UseShellExecute = true,
                        WindowStyle = ProcessWindowStyle.Hidden,
                    });
                }
                catch (Win32Exception ex) when (ex.NativeErrorCode == 1223)
                {
                    progress.Report("Admin prompt declined: measuring CPU and RAM only.");
                }
            }

            // sample CPU and RAM once a second for the same window
            List<double> robloxCpu = [], systemCpu = [], ramMb = [];
            int cores = Environment.ProcessorCount;
            TimeSpan lastCpu = roblox.TotalProcessorTime;
            (ulong idle, ulong total) lastSys = SystemTimes();
            var sw = Stopwatch.StartNew();

            for (int i = 1; i <= seconds; i++)
            {
                await Task.Delay(1000, ct);
                progress.Report($"Measuring… {i}/{seconds} s");

                roblox.Refresh();
                if (roblox.HasExited) throw new InvalidOperationException("Roblox closed during the benchmark.");

                TimeSpan cpu = roblox.TotalProcessorTime;
                double elapsed = sw.Elapsed.TotalSeconds;
                sw.Restart();
                robloxCpu.Add((cpu - lastCpu).TotalSeconds / elapsed / cores * 100);
                lastCpu = cpu;

                (ulong idle, ulong total) sys = SystemTimes();
                ulong dTotal = sys.total - lastSys.total;
                if (dTotal > 0) systemCpu.Add(100.0 * (dTotal - (sys.idle - lastSys.idle)) / dTotal);
                lastSys = sys;

                ramMb.Add(roblox.WorkingSet64 / 1024.0 / 1024.0);
            }

            List<(double FrameMs, double? GpuMs)> frames = [];
            if (presentMon is not null)
            {
                progress.Report("Reading frame data…");
                using var timeout = CancellationTokenSource.CreateLinkedTokenSource(ct);
                timeout.CancelAfter(TimeSpan.FromSeconds(20));
                try { await presentMon.WaitForExitAsync(timeout.Token); } catch (OperationCanceledException) { }
                frames = ReadFrames(csv);
            }

            BenchmarkResult result = Summarize(id, label, seconds, frames, robloxCpu, systemCpu, ramMb);
            Save(result);
            return result;
        }

        private static unsafe (ulong Idle, ulong Total) SystemTimes()
        {
            Win32.FILETIME idle, kernel, user;
            return Win32.GetSystemTimes(&idle, &kernel, &user) ? (idle.Value, kernel.Value + user.Value) : (0, 0);
        }

        /// <summary>Accepts PresentMon 1.x and 2.x column names.</summary>
        private static List<(double FrameMs, double? GpuMs)> ReadFrames(string csv)
        {
            List<(double, double?)> frames = [];
            if (!File.Exists(csv)) return frames;

            using var reader = new StreamReader(new FileStream(csv, FileMode.Open, FileAccess.Read, FileShare.ReadWrite));
            string[] header = (reader.ReadLine() ?? "").Split(',');
            int frameCol = IndexOf(header, "MsBetweenPresents", "FrameTime", "msBetweenPresents");
            int gpuCol = IndexOf(header, "MsGPUBusy", "GPUBusy", "msGPUBusy");
            if (frameCol < 0) return frames;

            while (reader.ReadLine() is string line)
            {
                string[] parts = line.Split(',');
                if (parts.Length <= frameCol || !double.TryParse(parts[frameCol], NumberStyles.Float, CultureInfo.InvariantCulture, out double ms) || ms <= 0 || ms > 1000)
                    continue;

                double? gpu = gpuCol >= 0 && parts.Length > gpuCol && double.TryParse(parts[gpuCol], NumberStyles.Float, CultureInfo.InvariantCulture, out double g) ? g : null;
                frames.Add((ms, gpu));
            }

            return frames;
        }

        private static int IndexOf(string[] header, params string[] names)
        {
            foreach (string name in names)
            {
                int i = Array.FindIndex(header, h => h.Trim().Equals(name, StringComparison.OrdinalIgnoreCase));
                if (i >= 0) return i;
            }
            return -1;
        }

        private static BenchmarkResult Summarize(string id, string label, int seconds, List<(double FrameMs, double? GpuMs)> frames,
            List<double> robloxCpu, List<double> systemCpu, List<double> ramMb)
        {
            double? avgFps = null, lowFps = null, avgMs = null, p99Ms = null, gpu = null;

            if (frames.Count > 10)
            {
                double[] sorted = [.. frames.Select(f => f.FrameMs).Order()];
                avgMs = sorted.Average();
                avgFps = 1000.0 / avgMs;
                p99Ms = sorted[Math.Min(sorted.Length - 1, (int)Math.Ceiling(sorted.Length * 0.99) - 1)];

                // "1% low" = average FPS of the slowest 1% of frames
                double[] slowest = [.. sorted.Skip((int)(sorted.Length * 0.99))];
                lowFps = 1000.0 / (slowest.Length > 0 ? slowest.Average() : p99Ms.Value);

                double gpuSum = frames.Where(f => f.GpuMs.HasValue).Sum(f => f.GpuMs!.Value);
                if (frames.Any(f => f.GpuMs.HasValue))
                    gpu = Math.Min(100, 100 * gpuSum / frames.Where(f => f.GpuMs.HasValue).Sum(f => f.FrameMs));
            }

            return new BenchmarkResult(id, string.IsNullOrWhiteSpace(label) ? "Benchmark" : label.Trim(), DateTime.Now, seconds,
                avgFps, lowFps, avgMs, p99Ms, gpu,
                robloxCpu.DefaultIfEmpty(0).Average(), systemCpu.DefaultIfEmpty(0).Average(), ramMb.DefaultIfEmpty(0).Average(), frames.Count);
        }

        // ------------------------------------------------------------------ storage

        public static List<BenchmarkResult> Load()
        {
            List<BenchmarkResult> list = [];
            try
            {
                if (!File.Exists(ResultsFile)) return list;
                using JsonDocument doc = JsonDocument.Parse(File.ReadAllText(ResultsFile));
                foreach (JsonElement e in doc.RootElement.EnumerateArray())
                {
                    static double? N(JsonElement e, string n) => e.TryGetProperty(n, out JsonElement v) && v.ValueKind == JsonValueKind.Number ? v.GetDouble() : null;
                    list.Add(new BenchmarkResult(
                        e.GetProperty("id").GetString()!, e.GetProperty("label").GetString()!, e.GetProperty("taken").GetDateTime(),
                        e.GetProperty("seconds").GetInt32(), N(e, "avgFps"), N(e, "lowFps"), N(e, "avgMs"), N(e, "p99Ms"), N(e, "gpu"),
                        N(e, "robloxCpu") ?? 0, N(e, "systemCpu") ?? 0, N(e, "ramMb") ?? 0, e.GetProperty("frames").GetInt32()));
                }
            }
            catch (Exception ex)
            {
                App.Logger.Warn($"Couldn't read benchmark results: {ex.Message}");
            }
            return list;
        }

        private static void Save(BenchmarkResult add) => Write([.. Load(), add]);

        public static void Delete(string id) => Write([.. Load().Where(r => r.Id != id)]);

        private static void Write(List<BenchmarkResult> all)
        {
            Directory.CreateDirectory(Folder);
            using var stream = new MemoryStream();
            using (var w = new Utf8JsonWriter(stream, new JsonWriterOptions { Indented = true }))
            {
                w.WriteStartArray();
                foreach (BenchmarkResult r in all)
                {
                    w.WriteStartObject();
                    w.WriteString("id", r.Id);
                    w.WriteString("label", r.Label);
                    w.WriteString("taken", r.TakenLocal);
                    w.WriteNumber("seconds", r.Seconds);
                    void Num(string n, double? v) { if (v is double d) w.WriteNumber(n, Math.Round(d, 2)); }
                    Num("avgFps", r.AvgFps); Num("lowFps", r.OnePercentLowFps); Num("avgMs", r.AvgFrameTimeMs);
                    Num("p99Ms", r.P99FrameTimeMs); Num("gpu", r.GpuBusyPercent);
                    Num("robloxCpu", r.RobloxCpuPercent); Num("systemCpu", r.SystemCpuPercent); Num("ramMb", r.RobloxRamMb);
                    w.WriteNumber("frames", r.Frames);
                    w.WriteEndObject();
                }
                w.WriteEndArray();
            }
            File.WriteAllBytes(ResultsFile, stream.ToArray());
        }

        /// <summary>Plain-language comparison of two results ("after" relative to "before").</summary>
        public static string Compare(BenchmarkResult before, BenchmarkResult after)
        {
            static string Delta(double? a, double? b, string unit, bool higherIsBetter)
            {
                if (a is not double x || b is not double y || x == 0) return "n/a";
                double pct = (y - x) / x * 100;
                bool better = higherIsBetter ? y > x : y < x;
                string verdict = Math.Abs(pct) < 3 ? "about the same" : better ? "better" : "worse";
                return $"{x:0.#} → {y:0.#} {unit} ({pct:+0;-0}%, {verdict})";
            }

            return string.Join("\n",
                $"Average FPS: {Delta(before.AvgFps, after.AvgFps, "FPS", true)}",
                $"1% low FPS (stutter): {Delta(before.OnePercentLowFps, after.OnePercentLowFps, "FPS", true)}",
                $"99% frame time: {Delta(before.P99FrameTimeMs, after.P99FrameTimeMs, "ms", false)}",
                $"GPU busy: {Delta(before.GpuBusyPercent, after.GpuBusyPercent, "%", false)}",
                $"Roblox CPU: {Delta(before.RobloxCpuPercent, after.RobloxCpuPercent, "%", false)}",
                $"Roblox RAM: {Delta(before.RobloxRamMb, after.RobloxRamMb, "MB", false)}",
                "",
                "Games vary a lot moment to moment: compare runs in the same place, doing the same thing, for the same length.");
        }

        public static string ToCsv(IEnumerable<BenchmarkResult> results)
        {
            static string F(double? v) => v?.ToString("0.##", CultureInfo.InvariantCulture) ?? "";
            var sb = new StringBuilder("label,taken,seconds,avg_fps,one_percent_low_fps,avg_frame_ms,p99_frame_ms,gpu_busy_pct,roblox_cpu_pct,system_cpu_pct,roblox_ram_mb,frames\n");
            foreach (BenchmarkResult r in results)
                sb.Append(CultureInfo.InvariantCulture, $"\"{r.Label.Replace("\"", "\"\"", StringComparison.Ordinal)}\",{r.TakenLocal:o},{r.Seconds},{F(r.AvgFps)},{F(r.OnePercentLowFps)},{F(r.AvgFrameTimeMs)},{F(r.P99FrameTimeMs)},{F(r.GpuBusyPercent)},{F(r.RobloxCpuPercent)},{F(r.SystemCpuPercent)},{F(r.RobloxRamMb)},{r.Frames}\n");
            return sb.ToString();
        }
    }
}
