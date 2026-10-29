// SPDX-FileCopyrightText: 2026 Vargostrap
//
// SPDX-License-Identifier: MPL-2.0

namespace Froststrap.Vargostrap.Network
{
    internal sealed record NetworkSample(DateTime TimeUtc, double? LatencyMs, double JitterMs, double LossPercent, bool IsSpike);

    /// <summary>
    /// Measures latency to one target every second: jitter (RFC 3550 smoothing), packet loss over the
    /// last <see cref="Window"/> samples, and spikes (a sample well above the recent median, or 3 losses in
    /// a row), which trigger a traceroute at most once per <see cref="TracerouteCooldown"/>. Every sample is
    /// appended to a CSV log as it's taken, so other Vargostrap windows can follow a session live.
    /// </summary>
    internal sealed class NetworkMonitor : IAsyncDisposable
    {
        public const int Window = 60;
        private static readonly TimeSpan Interval = TimeSpan.FromSeconds(1);
        private static readonly TimeSpan TracerouteCooldown = TimeSpan.FromSeconds(60);

        private readonly LatencyProbe _probe;
        private readonly CancellationTokenSource _cts = new();
        private readonly Queue<double?> _recent = new();
        private readonly List<double> _allLatencies = [];
        private readonly StreamWriter? _csv;
        private readonly string? _csvPath;
        private readonly int _spikeThresholdMs;
        private readonly bool _tracerouteOnSpike;

        private Task? _loop;
        private double? _lastLatency;
        private double _jitter;
        private int _consecutiveLosses;
        private DateTime _lastTraceroute = DateTime.MinValue;
        private ProbeMethod _loggedMethod = ProbeMethod.Icmp;

        public string Target => _probe.Target;
        public string Label { get; }
        public ProbeMethod Method => _probe.Method;
        public string MeasuredHost => _probe.MeasuredHost;
        public int SampleCount { get; private set; }
        public int LostCount { get; private set; }
        public int SpikeCount { get; private set; }

        /// <summary>Raised on the monitor's thread after each sample.</summary>
        public event Action<NetworkSample>? SampleAdded;

        public NetworkMonitor(string target, string label, string? csvPath, int spikeThresholdMs, bool tracerouteOnSpike)
        {
            _probe = new LatencyProbe(target);
            Label = label;
            _spikeThresholdMs = Math.Max(20, spikeThresholdMs);
            _tracerouteOnSpike = tracerouteOnSpike;

            if (csvPath is not null)
            {
                try
                {
                    Directory.CreateDirectory(Path.GetDirectoryName(csvPath)!);
                    var stream = new FileStream(csvPath, FileMode.Create, FileAccess.Write, FileShare.ReadWrite | FileShare.Delete);
                    _csv = new StreamWriter(stream, new UTF8Encoding(false)) { AutoFlush = true };
                    _csvPath = csvPath;
                    NetworkSessionLog.WriteHeader(_csv, target, label);
                }
                catch (Exception ex)
                {
                    App.Logger.Warn($"Network log unavailable: {ex.Message}");
                }
            }
        }

        /// <summary>Median of the recent successful samples (the session's "typical" latency).</summary>
        public double? RecentMedian
        {
            get
            {
                double[] ok = [.. _recent.Where(x => x.HasValue).Select(x => x!.Value).Order()];
                return ok.Length == 0 ? null : ok[ok.Length / 2];
            }
        }

        public double? SessionMedian
        {
            get
            {
                if (_allLatencies.Count == 0) return null;
                double[] sorted = [.. _allLatencies.Order()];
                return sorted[sorted.Length / 2];
            }
        }

        public void Start()
        {
            _loop ??= Task.Run(() => RunAsync(_cts.Token));
            App.Logger.Info($"Network monitor started for {Target} ({Label})");
        }

        private async Task RunAsync(CancellationToken token)
        {
            using var timer = new PeriodicTimer(Interval);

            try
            {
                do
                {
                    double? latency = await _probe.ProbeAsync(token);
                    Record(latency);
                }
                while (await timer.WaitForNextTickAsync(token));
            }
            catch (OperationCanceledException) { }
            catch (Exception ex)
            {
                App.Logger.Error(ex, "Network monitor stopped unexpectedly");
            }
        }

        private void Record(double? latency)
        {
            double? median = RecentMedian;

            if (_probe.Method != _loggedMethod)
            {
                _loggedMethod = _probe.Method;
                _csv?.WriteLine($"# method: tcp443 {LatencyProbe.FallbackHost} (target ignores ping)");
                // samples before the switch were all losses against a silent target; don't count them
                _recent.Clear();
                LostCount = 0;
                _consecutiveLosses = 0;
            }

            _recent.Enqueue(latency);
            while (_recent.Count > Window)
                _recent.Dequeue();

            SampleCount++;
            bool spike = false;

            if (latency is double ms)
            {
                _consecutiveLosses = 0;
                _allLatencies.Add(ms);

                if (_lastLatency is double prev)
                    _jitter += (Math.Abs(ms - prev) - _jitter) / 16.0;
                _lastLatency = ms;

                spike = median is double m && SampleCount > 10 && ms > m + _spikeThresholdMs;
            }
            else
            {
                LostCount++;
                spike = ++_consecutiveLosses == 3;
            }

            double loss = _recent.Count == 0 ? 0 : 100.0 * _recent.Count(x => x is null) / _recent.Count;
            var sample = new NetworkSample(DateTime.UtcNow, latency, _jitter, loss, spike);

            if (spike)
            {
                SpikeCount++;
                MaybeTraceroute();
            }

            try { NetworkSessionLog.WriteSample(_csv, sample); }
            catch (Exception ex) { App.Logger.Warn($"Network log write failed: {ex.Message}"); }

            SampleAdded?.Invoke(sample);
        }

        private void MaybeTraceroute()
        {
            if (!_tracerouteOnSpike || DateTime.UtcNow - _lastTraceroute < TracerouteCooldown)
                return;

            _lastTraceroute = DateTime.UtcNow;
            string host = MeasuredHost;
            string? tracePath = _csvPath is null ? null : Path.ChangeExtension(_csvPath, ".traceroute.txt");

            _ = Task.Run(async () =>
            {
                try
                {
                    string result = await Traceroute.RunAsync(host, _cts.Token);
                    App.Logger.Info($"Latency spike traceroute:\n{result}");
                    if (tracePath is not null)
                        await File.AppendAllTextAsync(tracePath, result + Environment.NewLine);
                }
                catch (OperationCanceledException) { }
                catch (Exception ex) { App.Logger.Warn($"Traceroute failed: {ex.Message}"); }
            });
        }

        public async ValueTask DisposeAsync()
        {
            await _cts.CancelAsync();

            if (_loop is not null)
            {
                try { await _loop; } catch (OperationCanceledException) { }
            }

            _csv?.Dispose();
            _probe.Dispose();
            _cts.Dispose();

            App.Logger.Info($"Network monitor stopped for {Target}: {SampleCount} samples, {LostCount} lost, {SpikeCount} spikes");
        }
    }
}
