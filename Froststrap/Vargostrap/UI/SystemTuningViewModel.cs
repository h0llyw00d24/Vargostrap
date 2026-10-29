// SPDX-FileCopyrightText: 2026 Vargostrap
//
// SPDX-License-Identifier: MPL-2.0

using System.Collections.ObjectModel;
using System.Runtime.Versioning;
using System.Windows.Input;
using Avalonia.Threading;
using CommunityToolkit.Mvvm.Input;
using Froststrap.UI.ViewModels;
using Froststrap.Vargostrap.Models;
using Froststrap.Vargostrap.Performance;
using Froststrap.Vargostrap.SystemChanges;

namespace Froststrap.Vargostrap.UI
{
    /// <summary>A row in the live background-apps list. Changing <see cref="Action"/> remembers the choice for that app.</summary>
    internal sealed class BackgroundAppRow(BackgroundAppStatus status) : NotifyPropertyChangedViewModel
    {
        public BackgroundApp App { get; } = status.App;

        public string Name => App.Name;

        public string Category => App.Category;

        public string Activity { get; private set; } = Format(status);

        public IReadOnlyList<BackgroundAction> AllowedActions => App.AllowedActions;

        private BackgroundAction _action = status.Action;
        public BackgroundAction Action
        {
            get => _action;
            set
            {
                if (_action == value) return;
                _action = value;
                BackgroundAppCatalog.SetAction(App, value);
                OnPropertyChanged();
            }
        }

        public void Update(BackgroundAppStatus s)
        {
            Activity = Format(s);
            OnPropertyChanged(nameof(Activity));
        }

        private static string Format(BackgroundAppStatus s)
        {
            if (s.App.Service is not null && s.Instances == 0)
                return s.ServiceRunning ? "Service running" : "Service stopped";

            string rate = s.BytesPerSecond switch
            {
                >= 1024 * 1024 => $"{s.BytesPerSecond / 1024.0 / 1024.0:0.0} MB/s",
                >= 1024 => $"{s.BytesPerSecond / 1024.0:0} KB/s",
                _ => "idle",
            };
            return $"{rate} · {s.Connections} connection{(s.Connections == 1 ? "" : "s")}";
        }
    }

    /// <summary>Priority management, background traffic control, and the safe-mode reset on the Ping &amp; Performance page.</summary>
    [SupportedOSPlatform("windows")]
    internal sealed class SystemTuningViewModel : NotifyPropertyChangedViewModel, IDisposable
    {
        private static VargostrapSettings S => VargoApp.Settings.Prop;

        private readonly BackgroundScanner _scanner = new();
        private readonly DispatcherTimer _timer = new() { Interval = TimeSpan.FromSeconds(2) };
        private bool _scanning;

        public SystemTuningViewModel()
        {
            _timer.Tick += (_, _) => _ = ScanAsync();
            _timer.Start();
            _ = ScanAsync();
        }

        public void Dispose() => _timer.Stop();

        #region Priority management

        public bool PriorityEnabled
        {
            get => S.PriorityEnabled;
            set
            {
                if (value && !S.PriorityEnabled)
                {
                    _ = ConfirmAsync("Priority management", PriorityManager.Preview(S), () => S.PriorityEnabled = true, nameof(PriorityEnabled));
                    return;
                }
                S.PriorityEnabled = value;
                OnPropertyChanged();
            }
        }

        public static IReadOnlyList<RobloxPriorityLevel> PriorityLevels { get; } = [RobloxPriorityLevel.AboveNormal, RobloxPriorityLevel.High];

        public RobloxPriorityLevel RobloxPriority
        {
            get => S.RobloxPriority;
            set { S.RobloxPriority = value; OnPropertyChanged(); }
        }

        public bool DisableEcoQos
        {
            get => S.PriorityDisableEcoQos;
            set { S.PriorityDisableEcoQos = value; OnPropertyChanged(); }
        }

        public bool MemoryPriority
        {
            get => S.PriorityMemory;
            set { S.PriorityMemory = value; OnPropertyChanged(); }
        }

        public bool HighPerformanceGpu
        {
            get => S.PriorityHighPerformanceGpu;
            set { S.PriorityHighPerformanceGpu = value; OnPropertyChanged(); }
        }

        public bool Dscp
        {
            get => S.PriorityDscp;
            set { S.PriorityDscp = value; OnPropertyChanged(); }
        }

        #endregion

        #region Background traffic control

        public bool TrafficEnabled
        {
            get => S.TrafficControlEnabled;
            set
            {
                if (value && !S.TrafficControlEnabled)
                {
                    _ = ConfirmAsync("Background traffic control", TrafficController.Preview(S), () => S.TrafficControlEnabled = true, nameof(TrafficEnabled));
                    return;
                }
                S.TrafficControlEnabled = value;
                OnPropertyChanged();
            }
        }

        public static IReadOnlyList<int> ThrottleOptions { get; } = [1, 2, 5, 10, 20];

        public int ThrottleMbps
        {
            get => S.ThrottleMbps;
            set { S.ThrottleMbps = value; OnPropertyChanged(); }
        }

        public ObservableCollection<BackgroundAppRow> Apps { get; } = [];

        public bool HasApps => Apps.Count > 0;

        private async Task ScanAsync()
        {
            if (_scanning) return;
            _scanning = true;

            try
            {
                List<BackgroundAppStatus> results = await Task.Run(_scanner.Scan);

                // update in place so open dropdowns aren't reset every refresh
                foreach (BackgroundAppRow gone in Apps.Where(r => !results.Any(x => x.App.Id == r.App.Id && x.IsActive)).ToList())
                    Apps.Remove(gone);

                foreach (BackgroundAppStatus status in results.Where(s => s.IsActive))
                {
                    BackgroundAppRow? row = Apps.FirstOrDefault(r => r.App.Id == status.App.Id);
                    if (row is null) Apps.Add(new BackgroundAppRow(status));
                    else row.Update(status);
                }

                OnPropertyChanged(nameof(HasApps));
            }
            catch (Exception ex)
            {
                App.Logger.Warn($"Background app scan failed: {ex.Message}");
            }
            finally
            {
                _scanning = false;
            }
        }

        public static ICommand PreviewCommand => new AsyncRelayCommand(async () =>
        {
            List<string> lines = [];
            if (S.PriorityEnabled) lines.AddRange(PriorityManager.Preview(S));
            if (S.TrafficControlEnabled) lines.AddRange(TrafficController.Preview(S));

            string text = lines.Count == 0
                ? "Priority management and background traffic control are both off, so nothing would change."
                : "At the next Roblox launch, Vargostrap would:\n\n" + string.Join("\n", lines.Select(l => "• " + l))
                    + "\n\nAll of it is undone when Roblox closes." + AdminNote(lines);

            await Frontend.ShowMessageBox(text, MessageBoxImage.Information);
        });

        #endregion

        #region Safety

        public static string PendingText
        {
            get
            {
                int user = SafeReset.PendingUserChanges().Count, admin = SafeReset.PendingAdminChanges().Count;
                string restore = RestorePoint.LastCreated(Paths.Base) is DateTime t
                    ? $" Last restore point: {t.ToString("g", CultureInfo.CurrentCulture)}."
                    : " No restore point created yet (one is made before the first admin change each day).";

                return (user + admin == 0
                    ? "No Vargostrap system changes are active."
                    : $"{user + admin} system change(s) are active right now (they're undone when Roblox closes).") + restore;
            }
        }

        public ICommand ResetCommand => new AsyncRelayCommand(async () =>
        {
            var confirm = await Frontend.ShowMessageBox(
                "Safe-mode reset will:\n\n"
                    + "• Undo every system change Vargostrap has made that's still active (process priorities, paused apps, services, QoS policies, registry values)\n"
                    + "• Unlock Roblox's settings file\n"
                    + "• Turn off priority management and background traffic control\n\n"
                    + "Your FastFlags, Roblox settings and other preferences are kept. Continue?",
                MessageBoxImage.Warning, MessageBoxButton.YesNo);

            if (confirm != MessageBoxResult.Yes) return;

            string summary = await Task.Run(SafeReset.ResetAll);
            OnPropertyChanged(string.Empty);
            await Frontend.ShowMessageBox(summary, MessageBoxImage.Information);
        });

        public static ICommand OpenHelperLogCommand => new RelayCommand(() =>
        {
            string log = Path.Combine(Paths.Base, "Logs", "elevated-helper.log");
            if (File.Exists(log)) Utility.Threading.ShellExecute(log);
            else _ = Frontend.ShowMessageBox("No admin changes have been made yet.", MessageBoxImage.Information);
        });

        #endregion

        private async Task ConfirmAsync(string feature, List<string> preview, Action enable, string property)
        {
            string text = $"Turn on {feature}?\n\nEach time you launch Roblox through Vargostrap it will:\n\n"
                + (preview.Count == 0 ? "• Nothing right now (no matching apps are configured)" : string.Join("\n", preview.Select(l => "• " + l)))
                + "\n\nEverything is undone automatically when Roblox closes, after a crash on the next start, or with Safe-mode reset."
                + AdminNote(preview);

            if (await Frontend.ShowMessageBox(text, MessageBoxImage.Question, MessageBoxButton.YesNo) == MessageBoxResult.Yes)
                enable();

            OnPropertyChanged(property);
        }

        private static string AdminNote(List<string> lines) => lines.Any(l => l.Contains("admin", StringComparison.OrdinalIgnoreCase))
            ? "\n\nItems marked admin need administrator permission: Windows shows one prompt per Roblox launch. Decline it and only those items are skipped."
            : "";
    }
}
