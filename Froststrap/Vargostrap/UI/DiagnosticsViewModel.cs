// SPDX-FileCopyrightText: 2026 Vargostrap
//
// SPDX-License-Identifier: MPL-2.0

using System.Collections.ObjectModel;
using System.Runtime.Versioning;
using System.Windows.Input;
using Avalonia.Controls;
using Avalonia.Platform.Storage;
using CommunityToolkit.Mvvm.Input;
using Froststrap.UI.ViewModels;
using Froststrap.Vargostrap.Models;
using Froststrap.Vargostrap.Network;
using Froststrap.Vargostrap.Performance;

namespace Froststrap.Vargostrap.UI
{
    /// <summary>Benchmarking, bufferbloat test and DNS benchmark on the Ping &amp; Performance page.</summary>
    [SupportedOSPlatform("windows")]
    internal sealed class DiagnosticsViewModel : NotifyPropertyChangedViewModel
    {
        private static VargostrapSettings S => VargoApp.Settings.Prop;

        /// <summary>Set by the section; needed for file dialogs.</summary>
        public Func<TopLevel?>? GetTopLevel { get; set; }

        public DiagnosticsViewModel()
        {
            foreach (BenchmarkResult r in Benchmark.Load().OrderByDescending(r => r.TakenLocal))
                Results.Add(r);
        }

        private bool _busy;
        public bool IsBusy
        {
            get => _busy;
            private set { SetProperty(ref _busy, value); OnPropertyChanged(nameof(IsIdle)); }
        }

        public bool IsIdle => !IsBusy;

        #region Benchmark

        public string PresentMonPath
        {
            get => S.PresentMonPath;
            set { S.PresentMonPath = value?.Trim().Trim('"') ?? ""; OnPropertyChanged(); OnPropertyChanged(nameof(PresentMonStatus)); }
        }

        public static string PresentMonStatus => string.IsNullOrWhiteSpace(S.PresentMonPath)
            ? "Not set: benchmarks will measure CPU and RAM only. For FPS and frame times, download the PresentMon console app (PresentMon-…-x64.exe) from Intel's GitHub and choose it here."
            : File.Exists(S.PresentMonPath) ? "PresentMon found. Windows asks for permission each run (it reads graphics events)." : "That file doesn't exist.";

        public static string PresentMonUrl => Benchmark.PresentMonUrl;

        public static ICommand OpenWebpageCommand => GlobalViewModel.OpenWebpageCommand;

        public static IReadOnlyList<int> DurationOptions { get; } = [30, 60, 120];

        public int Seconds
        {
            get => S.BenchmarkSeconds;
            set { S.BenchmarkSeconds = value; OnPropertyChanged(); }
        }

        private string _label = "Before";
        public string Label
        {
            get => _label;
            set => SetProperty(ref _label, value);
        }

        private string? _benchmarkStatus;
        public string? BenchmarkStatus
        {
            get => _benchmarkStatus;
            private set => SetProperty(ref _benchmarkStatus, value);
        }

        public ObservableCollection<BenchmarkResult> Results { get; } = [];

        public ICommand BrowsePresentMonCommand => new AsyncRelayCommand(async () =>
        {
            if (GetTopLevel?.Invoke() is not TopLevel top) return;
            var files = await top.StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
            {
                Title = "Choose PresentMon",
                AllowMultiple = false,
                FileTypeFilter = [new FilePickerFileType("PresentMon") { Patterns = ["PresentMon*.exe", "*.exe"] }],
            });
            if (files.Count > 0) PresentMonPath = files[0].Path.LocalPath;
        });

        public ICommand RunBenchmarkCommand => new AsyncRelayCommand(async () =>
        {
            IsBusy = true;
            try
            {
                var progress = new Progress<string>(m => BenchmarkStatus = m);
                BenchmarkResult result = await Task.Run(() => Benchmark.RunAsync(Label, S.BenchmarkSeconds, S.PresentMonPath, progress, CancellationToken.None));
                Results.Insert(0, result);
                BenchmarkStatus = $"Done: {result.Summary}";
                if (Label == "Before") Label = "After";
            }
            catch (Exception ex)
            {
                BenchmarkStatus = ex.Message;
            }
            finally
            {
                IsBusy = false;
            }
        });

        public ICommand DeleteResultCommand => new RelayCommand<BenchmarkResult>(r =>
        {
            if (r is null) return;
            Benchmark.Delete(r.Id);
            Results.Remove(r);
        });

        private BenchmarkResult? _compareBefore, _compareAfter;
        public BenchmarkResult? CompareBefore
        {
            get => _compareBefore;
            set { SetProperty(ref _compareBefore, value); OnPropertyChanged(nameof(Comparison)); }
        }

        public BenchmarkResult? CompareAfter
        {
            get => _compareAfter;
            set { SetProperty(ref _compareAfter, value); OnPropertyChanged(nameof(Comparison)); }
        }

        public string? Comparison => CompareBefore is { } a && CompareAfter is { } b && a != b ? Benchmark.Compare(a, b) : null;

        public ICommand ExportCommand => new AsyncRelayCommand(async () =>
        {
            if (GetTopLevel?.Invoke() is not TopLevel top || Results.Count == 0) return;
            IStorageFile? file = await top.StorageProvider.SaveFilePickerAsync(new FilePickerSaveOptions
            {
                Title = "Export benchmarks",
                SuggestedFileName = "vargostrap-benchmarks.csv",
                DefaultExtension = ".csv",
                FileTypeChoices = [new FilePickerFileType("CSV") { Patterns = ["*.csv"] }],
            });
            if (file is null) return;

            await using Stream stream = await file.OpenWriteAsync();
            await using var writer = new StreamWriter(stream);
            await writer.WriteAsync(Benchmark.ToCsv(Results.OrderBy(r => r.TakenLocal)));
        });

        #endregion

        #region Network tools

        private string? _bufferbloatStatus;
        public string? BufferbloatStatus
        {
            get => _bufferbloatStatus;
            private set => SetProperty(ref _bufferbloatStatus, value);
        }

        public ICommand RunBufferbloatCommand => new AsyncRelayCommand(async () =>
        {
            var answer = await Frontend.ShowMessageBox(
                "The bufferbloat test fills your connection for about 35 seconds (download, then upload) using Cloudflare's speed-test servers. "
                    + "It can use several hundred MB of data. Avoid it on a metered or capped connection, and expect anyone else on your network to notice a slowdown while it runs.\n\nStart the test?",
                MessageBoxImage.Question, MessageBoxButton.YesNo);
            if (answer != MessageBoxResult.Yes) return;

            IsBusy = true;
            try
            {
                var progress = new Progress<string>(m => BufferbloatStatus = m);
                BufferbloatResult r = await Task.Run(() => Bufferbloat.RunAsync(progress, CancellationToken.None));
                BufferbloatStatus =
                    $"Grade {r.Grade}\n"
                    + $"Latency: idle {r.IdleMs:0} ms · while downloading {r.DownloadLoadedMs:0} ms (+{Math.Max(0, r.DownloadLoadedMs - r.IdleMs):0}) · while uploading {r.UploadLoadedMs:0} ms (+{Math.Max(0, r.UploadLoadedMs - r.IdleMs):0})\n"
                    + $"Speed during the test: ~{r.DownloadMbps:0} Mbit/s down, ~{r.UploadMbps:0} Mbit/s up\n\n{r.Advice}";
            }
            catch (Exception ex)
            {
                BufferbloatStatus = $"Test failed: {ex.Message}";
            }
            finally
            {
                IsBusy = false;
            }
        });

        private string? _dnsStatus;
        public string? DnsStatus
        {
            get => _dnsStatus;
            private set => SetProperty(ref _dnsStatus, value);
        }

        public ICommand RunDnsCommand => new AsyncRelayCommand(async () =>
        {
            IsBusy = true;
            try
            {
                var progress = new Progress<string>(m => DnsStatus = m);
                List<DnsResult> results = await Task.Run(() => DnsBenchmark.RunAsync(progress, CancellationToken.None));

                IEnumerable<string> lines = results.Select((r, i) =>
                    $"{i + 1}. {r.Name} ({r.Address}): " + (r.MedianMs is double ms ? $"{ms:0} ms" : "no answer") + (r.Failures > 0 ? $", {r.Failures} timeouts" : ""));

                DnsResult? system = results.FirstOrDefault(r => r.IsSystem);
                DnsResult? best = results.FirstOrDefault(r => r.MedianMs.HasValue);
                string verdict = system?.MedianMs is double s && best?.MedianMs is double b && !best.IsSystem && s - b >= 10
                    ? $"\n\n{best.Name} answered about {s - b:0} ms faster than your current DNS. DNS only affects how quickly Roblox connects and loads, not in-game ping. To switch: Windows Settings > Network & internet > your connection > DNS server assignment > Edit."
                    : "\n\nYour current DNS is about as fast as the alternatives. No change needed. (DNS affects how quickly Roblox connects and loads, not in-game ping.)";

                DnsStatus = string.Join("\n", lines) + verdict;
            }
            catch (Exception ex)
            {
                DnsStatus = $"Benchmark failed: {ex.Message}";
            }
            finally
            {
                IsBusy = false;
            }
        });

        #endregion
    }
}
