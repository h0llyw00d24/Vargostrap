// SPDX-FileCopyrightText: 2026 Vargostrap
//
// SPDX-License-Identifier: MPL-2.0

using System.Collections.ObjectModel;
using System.Windows.Input;
using Avalonia.Controls;
using Avalonia.Platform.Storage;
using Avalonia.Threading;
using CommunityToolkit.Mvvm.Input;
using Froststrap.Integrations;
using Froststrap.UI.ViewModels;
using Froststrap.Vargostrap.Models;
using Froststrap.Vargostrap.Network;

namespace Froststrap.Vargostrap.UI
{
    internal sealed record RegionRow(int Rank, string Region, string Source);

    /// <summary>
    /// Ping &amp; Performance page. Stage 5 sections: network monitor (live chart, history, export) and the
    /// server picker (region ranking for upstream's Better Matchmaking, preferred regions, manual
    /// override, and a "find best server" test). Later stages add their sections to the same page.
    /// </summary>
    internal sealed class PingPerformanceViewModel : NotifyPropertyChangedViewModel, IDisposable
    {
        private static VargostrapSettings S => VargoApp.Settings.Prop;

        private readonly DispatcherTimer _timer = new() { Interval = TimeSpan.FromSeconds(1) };
        private NetworkMonitor? _testMonitor;
        private readonly List<NetworkSample> _testSamples = [];
        private DateTime _lastFind = DateTime.MinValue;

        public PingPerformanceViewModel()
        {
            _timer.Tick += (_, _) => RefreshLive();
            _timer.Start();
            RefreshHistory();
            _ = LoadRegionsAsync();
        }

        /// <summary>Called by the page when it unloads.</summary>
        public void Detach()
        {
            _timer.Stop();
            _ = StopTestAsync();
        }

        public void Dispose() => Detach();

        #region Network monitor settings

        public bool MonitorEnabled
        {
            get => S.NetMonitorEnabled;
            set { S.NetMonitorEnabled = value; OnPropertyChanged(); }
        }

        public bool TracerouteOnSpike
        {
            get => S.NetTracerouteOnSpike;
            set { S.NetTracerouteOnSpike = value; OnPropertyChanged(); }
        }

        public static IReadOnlyList<int> SpikeThresholds { get; } = [40, 60, 80, 120, 200];

        public int SpikeThreshold
        {
            get => S.NetSpikeThresholdMs;
            set { S.NetSpikeThresholdMs = value; OnPropertyChanged(); }
        }

        #endregion

        #region Live view

        private IReadOnlyList<NetworkSample> _liveSamples = [];
        public IReadOnlyList<NetworkSample> LiveSamples
        {
            get => _liveSamples;
            private set => SetProperty(ref _liveSamples, value);
        }

        private string _liveTitle = "Not monitoring";
        public string LiveTitle
        {
            get => _liveTitle;
            private set => SetProperty(ref _liveTitle, value);
        }

        private string _liveStats = "Join a game through Vargostrap, or run a test, to see live results here.";
        public string LiveStats
        {
            get => _liveStats;
            private set => SetProperty(ref _liveStats, value);
        }

        public bool IsTesting => _testMonitor is not null;

        public bool IsNotTesting => !IsTesting;

        public ICommand StartTestCommand => new RelayCommand(() =>
        {
            if (_testMonitor is not null) return;

            _testSamples.Clear();
            // a quick check of the path to Roblox, not tied to a game; not logged to history
            _testMonitor = new NetworkMonitor(LatencyProbe.FallbackHost, "Test", null, S.NetSpikeThresholdMs, tracerouteOnSpike: false);
            _testMonitor.SampleAdded += sample => Dispatcher.UIThread.Post(() =>
            {
                _testSamples.Add(sample);
                if (_testSamples.Count > NetworkMonitor.Window) _testSamples.RemoveAt(0);
            });
            _testMonitor.Start();
            OnPropertyChanged(nameof(IsTesting));
            OnPropertyChanged(nameof(IsNotTesting));
        });

        public ICommand StopTestCommand => new AsyncRelayCommand(StopTestAsync);

        private async Task StopTestAsync()
        {
            NetworkMonitor? monitor = _testMonitor;
            if (monitor is null) return;

            _testMonitor = null;
            await monitor.DisposeAsync();
            Dispatcher.UIThread.Post(() =>
            {
                OnPropertyChanged(nameof(IsTesting));
                OnPropertyChanged(nameof(IsNotTesting));
            });
        }

        private void RefreshLive()
        {
            if (_testMonitor is NetworkMonitor test)
            {
                LiveSamples = [.. _testSamples];
                LiveTitle = $"Test: {(test.Method == ProbeMethod.Icmp ? "ping" : "TCP handshake")} to {test.MeasuredHost}";
                LiveStats = Describe(_testSamples);
                return;
            }

            // a Roblox session may be monitored by another Vargostrap process (the one that launched
            // Roblox), so follow its log file rather than an in-memory monitor
            NetworkSessionSummary? live = History.FirstOrDefault(h => h.IsLive);
            if (live is null && History.Count > 0 && History[0].IsLive)
                live = History[0];

            if (live is null)
            {
                string? newest = Directory.Exists(NetworkSessionLog.Directory)
                    ? Directory.GetFiles(NetworkSessionLog.Directory, "session-*.csv").OrderDescending().FirstOrDefault()
                    : null;

                if (newest is not null && DateTime.UtcNow - File.GetLastWriteTimeUtc(newest) < TimeSpan.FromSeconds(5))
                {
                    RefreshHistory();
                    live = NetworkSessionLog.TryRead(newest);
                }
            }

            if (live is not null && NetworkSessionLog.TryRead(live.Path, out List<NetworkSample> tail) is NetworkSessionSummary current)
            {
                LiveSamples = tail;
                LiveTitle = $"Live: {current.Label} ({current.TargetText})";
                LiveStats = Describe(tail);
            }
            else if (LiveSamples.Count > 0 || LiveTitle != "Not monitoring")
            {
                LiveSamples = [];
                LiveTitle = "Not monitoring";
                LiveStats = "Join a game through Vargostrap, or run a test, to see live results here.";
            }
        }

        private static string Describe(List<NetworkSample> samples)
        {
            if (samples.Count == 0)
                return "Waiting for the first sample…";

            NetworkSample last = samples[^1];
            double[] ok = [.. samples.Where(s => s.LatencyMs.HasValue).Select(s => s.LatencyMs!.Value)];
            string now = last.LatencyMs is double ms ? $"{ms:0} ms" : "lost";
            string avg = ok.Length > 0 ? $"{ok.Average():0} ms" : "–";

            return $"Now {now} · average {avg} · jitter {last.JitterMs:0} ms · loss {last.LossPercent:0.#}% · {samples.Count(s => s.IsSpike)} spikes (last {samples.Count} s)";
        }

        #endregion

        #region History

        public ObservableCollection<NetworkSessionSummary> History { get; } = [];

        public bool HasHistory => History.Count > 0;

        public ICommand RefreshHistoryCommand => new RelayCommand(RefreshHistory);

        private void RefreshHistory()
        {
            History.Clear();
            foreach (NetworkSessionSummary s in NetworkSessionLog.List())
                History.Add(s);
            OnPropertyChanged(nameof(HasHistory));
        }

        /// <summary>Set by the page; needed for the save dialog.</summary>
        public Func<TopLevel?>? GetTopLevel { get; set; }

        public IAsyncRelayCommand ExportCommand => new AsyncRelayCommand<NetworkSessionSummary>(async session =>
        {
            if (session is null || GetTopLevel?.Invoke() is not TopLevel topLevel)
                return;

            IStorageFile? file = await topLevel.StorageProvider.SaveFilePickerAsync(new FilePickerSaveOptions
            {
                Title = "Export network session",
                SuggestedFileName = Path.GetFileName(session.Path),
                DefaultExtension = ".csv",
                FileTypeChoices = [new FilePickerFileType("CSV") { Patterns = ["*.csv"] }]
            });

            if (file is null) return;

            try
            {
                using var source = new FileStream(session.Path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
                await using var target = await file.OpenWriteAsync();
                await source.CopyToAsync(target);
            }
            catch (Exception ex)
            {
                await Frontend.ShowMessageBox($"Export failed.\n\n{ex.Message}", MessageBoxImage.Error);
            }
        });

        public static ICommand OpenTracerouteCommand => new RelayCommand<NetworkSessionSummary>(session =>
        {
            if (session is null) return;
            string trace = Path.ChangeExtension(session.Path, ".traceroute.txt");

            if (File.Exists(trace))
                Utility.Threading.ShellExecute(trace);
            else
                _ = Frontend.ShowMessageBox("No traceroute for this session. One runs automatically when latency spikes (if enabled above).", MessageBoxImage.Information);
        });

        public static ICommand OpenLogsFolderCommand => new RelayCommand(() =>
        {
            Directory.CreateDirectory(NetworkSessionLog.Directory);
            Utility.Threading.ShellExecute(NetworkSessionLog.Directory);
        });

        #endregion

        #region Server picker

        public static string AutoRegion => Strings.Common_Auto;

        private IReadOnlyList<string> _allRegions = [];
        public IReadOnlyList<string> AllRegions
        {
            get => _allRegions;
            private set => SetProperty(ref _allRegions, value);
        }

        // replaced as a whole (not filled in place) so the ComboBox can match the current selection
        private IReadOnlyList<string> _overrideOptions = [AutoRegion];
        public IReadOnlyList<string> OverrideOptions
        {
            get => _overrideOptions;
            private set => SetProperty(ref _overrideOptions, value);
        }

        public ObservableCollection<string> PreferredRegions { get; } = new(S.PreferredRegions);

        public ObservableCollection<RegionRow> Ranking { get; } = [];

        private string? _regionsError;
        public string? RegionsError
        {
            get => _regionsError;
            private set { SetProperty(ref _regionsError, value); OnPropertyChanged(nameof(HasRegionsError)); }
        }

        public bool HasRegionsError => RegionsError is not null;

        public static bool BetterMatchmaking
        {
            get => App.Settings.Prop.EnableBetterMatchmaking;
            set => App.Settings.Prop.EnableBetterMatchmaking = value;
        }

        public string? ManualRegion
        {
            get => string.IsNullOrEmpty(App.Settings.Prop.SelectedRegion) ? AutoRegion : App.Settings.Prop.SelectedRegion;
            set
            {
                if (value is null) return;
                App.Settings.Prop.SelectedRegion = value;
                OnPropertyChanged();
            }
        }

        private string? _regionToAdd;
        public string? RegionToAdd
        {
            get => _regionToAdd;
            set => SetProperty(ref _regionToAdd, value);
        }

        private List<string> _distanceOrder = [];

        private async Task LoadRegionsAsync()
        {
            try
            {
                List<string> regions = await Task.Run(() => DatacenterDirectory.GetRegionsAsync());

                using var fetcher = new RobloxServerFetcher();
                _distanceOrder = await Task.Run(() => fetcher.GetClosestRegionsForAutoModeAsync());

                AllRegions = regions;

                // keep a saved region selectable even if the list no longer has it
                List<string> options = [AutoRegion, .. regions];
                string? current = ManualRegion;
                if (current is not null && !options.Contains(current, StringComparer.OrdinalIgnoreCase))
                    options.Add(current);

                OverrideOptions = options;
                OnPropertyChanged(nameof(ManualRegion));
                RegionsError = null;
            }
            catch (Exception ex)
            {
                RegionsError = $"Couldn't load the region list: {ex.Message}";
            }

            RebuildRanking();
        }

        private void RebuildRanking()
        {
            Ranking.Clear();
            int rank = 1;
            foreach (RankedRegion r in RegionRanking.RankDetailed(_distanceOrder).Take(12))
            {
                string source = r.Source == RegionRankSource.Preferred ? "Preferred" : "Nearby (by distance)";
                Ranking.Add(new RegionRow(rank++, r.Region, source));
            }
        }

        private void SavePreferred()
        {
            S.PreferredRegions = [.. PreferredRegions];
            RebuildRanking();
        }

        public ICommand AddPreferredCommand => new RelayCommand(() =>
        {
            if (string.IsNullOrEmpty(RegionToAdd) || PreferredRegions.Contains(RegionToAdd, StringComparer.OrdinalIgnoreCase))
                return;
            PreferredRegions.Add(RegionToAdd);
            SavePreferred();
        });

        public ICommand RemovePreferredCommand => new RelayCommand<string>(region =>
        {
            if (region is not null && PreferredRegions.Remove(region))
                SavePreferred();
        });

        public ICommand MoveUpPreferredCommand => new RelayCommand<string>(region =>
        {
            int i = region is null ? -1 : PreferredRegions.IndexOf(region);
            if (i > 0)
            {
                PreferredRegions.Move(i, i - 1);
                SavePreferred();
            }
        });

        // Find best server now

        private string _placeIdText = "";
        public string PlaceIdText
        {
            get => _placeIdText;
            set => SetProperty(ref _placeIdText, value.Trim());
        }

        private bool _isFinding;
        public bool IsFinding
        {
            get => _isFinding;
            private set { SetProperty(ref _isFinding, value); OnPropertyChanged(nameof(CanFind)); }
        }

        public bool CanFind => !IsFinding;

        private string? _findResult;
        public string? FindResult
        {
            get => _findResult;
            private set => SetProperty(ref _findResult, value);
        }

        private string? _foundServerUri;
        public bool CanJoin => _foundServerUri is not null;

        public ICommand FindBestServerCommand => new AsyncRelayCommand(async () =>
        {
            if (!long.TryParse(PlaceIdText, NumberStyles.None, CultureInfo.InvariantCulture, out long placeId) || placeId <= 0)
            {
                FindResult = "Enter a place ID: the number in the game's URL, e.g. roblox.com/games/606849621/…";
                return;
            }

            // keep requests to Roblox and RoValra polite
            TimeSpan wait = TimeSpan.FromSeconds(10) - (DateTime.UtcNow - _lastFind);
            if (wait > TimeSpan.Zero)
            {
                FindResult = $"Please wait {Math.Ceiling(wait.TotalSeconds)} s before searching again.";
                return;
            }

            _lastFind = DateTime.UtcNow;
            IsFinding = true;
            _foundServerUri = null;
            OnPropertyChanged(nameof(CanJoin));
            FindResult = "Searching…";

            try
            {
                using var fetcher = new RobloxServerFetcher();
                RobloxServerFetcher.ServerSelectionResult result = new();
                string manual = ManualRegion ?? AutoRegion;

                if (!string.Equals(manual, AutoRegion, StringComparison.OrdinalIgnoreCase))
                    result = await fetcher.FindBestServerInSelectedRegionAsync(placeId, manual);

                if (!result.Found)
                {
                    List<string> regions = RegionRanking.Rank(await fetcher.GetClosestRegionsForAutoModeAsync());
                    result = await fetcher.FindBestServerInRegionAsync(placeId, regions);
                }

                if (result.Found)
                {
                    _foundServerUri = $"roblox://experiences/start?placeId={placeId}&gameInstanceId={result.ServerId}";
                    FindResult = $"Best server: {result.Region} (server {result.ServerId})";
                }
                else
                {
                    FindResult = "No server found. The game may have no public servers right now, or the region service is unavailable.";
                }
            }
            catch (Exception ex)
            {
                FindResult = $"Search failed: {ex.Message}";
            }
            finally
            {
                IsFinding = false;
                OnPropertyChanged(nameof(CanJoin));
            }
        });

        public ICommand JoinFoundServerCommand => new RelayCommand(() =>
        {
            if (_foundServerUri is null) return;
            // same public deep link upstream's Better Matchmaking uses
            Process.Start(new ProcessStartInfo { FileName = _foundServerUri, UseShellExecute = true });
        });

        #endregion
    }
}
