// SPDX-FileCopyrightText: 2026 Vargostrap
//
// SPDX-License-Identifier: MPL-2.0

using System.Collections.ObjectModel;
using System.Runtime.Versioning;
using System.Windows.Input;
using CommunityToolkit.Mvvm.Input;
using Froststrap.UI.ViewModels;
using Froststrap.Vargostrap.Models;
using Froststrap.Vargostrap.Performance;

namespace Froststrap.Vargostrap.UI
{
    /// <summary>Power plan, Game Mode, Game Bar capture, network adapter and cleanup on the Ping &amp; Performance page.</summary>
    [SupportedOSPlatform("windows")]
    internal sealed class WindowsToolsViewModel : NotifyPropertyChangedViewModel
    {
        private static VargostrapSettings S => VargoApp.Settings.Prop;

        private WindowsTweaks.AdapterInfo? _adapter;

        public WindowsToolsViewModel() => _ = RefreshAsync();

        public ICommand RefreshCommand => new AsyncRelayCommand(RefreshAsync);

        private async Task RefreshAsync()
        {
            _adapter = await Task.Run(WindowsTweaks.PrimaryAdapter);
            _plan = await Task.Run(PowerPlans.BestPerformancePlan);
            OnPropertyChanged(string.Empty);
        }

        #region Power plan

        private (string Guid, string Name)? _plan;

        public bool PowerPlanDuringPlay
        {
            get => S.PowerPlanDuringPlay;
            set { S.PowerPlanDuringPlay = value; OnPropertyChanged(); }
        }

        public string PowerPlanStatus => _plan is (string guid, string name)
            ? string.Equals(PowerPlans.ActiveGuid(), guid, StringComparison.OrdinalIgnoreCase)
                ? $"{name} is already your active plan, so nothing would change on this PC."
                : $"Switches to {name} while Roblox runs, then back to your current plan."
            : "This PC doesn't offer a High or Ultimate Performance plan (common on laptops with Modern Standby). Use Windows' power mode slider instead.";

        public bool PowerPlanAvailable => _plan is not null;

        #endregion

        #region Game Mode and Game Bar

        public bool GameMode
        {
            get => WindowsTweaks.IsInEffect(WindowsTweaks.GameMode);
            set => _ = ToggleAsync(value, "Windows Game Mode", WindowsTweaks.GameModeGroup, WindowsTweaks.GameMode, nameof(GameMode));
        }

        public bool GameBarCaptureOff
        {
            get => WindowsTweaks.IsInEffect(WindowsTweaks.GameBarCapture);
            set => _ = ToggleAsync(value, "turning off Xbox Game Bar capture", WindowsTweaks.GameBarGroup, WindowsTweaks.GameBarCapture, nameof(GameBarCaptureOff));
        }

        private async Task ToggleAsync(bool on, string name, string group, IReadOnlyList<TweakValue> values, string property)
        {
            if (on)
            {
                string preview = string.Join("\n", values.Select(v => $"• {v.Description}\n   {WindowsTweaks.Describe(v)}"));
                var answer = await Frontend.ShowMessageBox(
                    $"Apply {name}? These registry values (your user only, no admin) will be set:\n\n{preview}\n\nThe originals are saved. Switch this off later to put them back exactly.",
                    MessageBoxImage.Question, MessageBoxButton.YesNo);

                if (answer == MessageBoxResult.Yes)
                {
                    List<string> errors = WindowsTweaks.Apply(group, values);
                    if (errors.Count > 0)
                        await Frontend.ShowMessageBox("Some values couldn't be set:\n\n" + string.Join("\n", errors), MessageBoxImage.Warning);
                }
            }
            else if (WindowsTweaks.IsManagedByVargostrap(group))
            {
                List<string> failures = WindowsTweaks.Undo(group);
                if (failures.Count > 0)
                    await Frontend.ShowMessageBox("Some values couldn't be restored:\n\n" + string.Join("\n", failures), MessageBoxImage.Warning);
            }
            else
            {
                await Frontend.ShowMessageBox(
                    $"This was already set in Windows before Vargostrap changed anything, so there's nothing of Vargostrap's to undo. Change it in Windows Settings > Gaming if you want it different.",
                    MessageBoxImage.Information);
            }

            OnPropertyChanged(property);
        }

        #endregion

        #region Network adapter

        public string AdapterTitle => _adapter is { } a
            ? $"{a.Name}: {a.Description}"
            : "No active network adapter with internet access found.";

        public string AdapterDetails => _adapter is { } a
            ? $"{(a.IsWireless ? "Wi-Fi" : "Wired (Ethernet)")} · link speed {a.SpeedMbps} Mbit/s"
            : "";

        public bool IsWireless => _adapter?.IsWireless == true;

        public bool AdapterFound => _adapter is not null;

        public bool NicPowerSavingOff
        {
            get => _adapter?.PowerSavingDisabled == true;
            set => _ = ToggleNicAsync(value);
        }

        private async Task ToggleNicAsync(bool off)
        {
            if (_adapter is not { } adapter) return;

            if (off)
            {
                var answer = await Frontend.ShowMessageBox(
                    $"Stop Windows from turning off \"{adapter.Description}\" to save power?\n\n"
                        + $@"This sets PnPCapabilities = 24 under HKLM\...\Class\{{4d36e972-…}}\{adapter.ClassSubkey} (admin: Windows will ask for permission). "
                        + "It takes effect after the adapter or PC restarts. Switch this off later to put the original back.",
                    MessageBoxImage.Question, MessageBoxButton.YesNo);
                if (answer != MessageBoxResult.Yes) { OnPropertyChanged(nameof(NicPowerSavingOff)); return; }
            }
            else if (!WindowsTweaks.NicTweakManagedByVargostrap())
            {
                await Frontend.ShowMessageBox("Power saving was already off for this adapter before Vargostrap changed anything, so there's nothing to undo.", MessageBoxImage.Information);
                OnPropertyChanged(nameof(NicPowerSavingOff));
                return;
            }

            if (!WindowsTweaks.SetNicPowerSavingDisabled(adapter, off))
                await Frontend.ShowMessageBox("The change was cancelled (the permission prompt was declined, or the adapter's settings couldn't be found).", MessageBoxImage.Warning);

            await Task.Delay(4000); // the helper runs as a separate process
            await RefreshAsync();
        }

        #endregion

        #region Cleanup

        public static IReadOnlyList<int> LogAgeOptions { get; } = [1, 3, 7, 30];

        public int LogsOlderThanDays
        {
            get => S.CleanupLogsOlderThanDays;
            set { S.CleanupLogsOlderThanDays = value; OnPropertyChanged(); }
        }

        public ObservableCollection<CleanupCategory> CleanupPreview { get; } = [];

        private string? _cleanupStatus;
        public string? CleanupStatus
        {
            get => _cleanupStatus;
            private set => SetProperty(ref _cleanupStatus, value);
        }

        public bool CanClean => CleanupPreview.Any(c => c.Files.Count > 0);

        public ICommand PreviewCleanupCommand => new AsyncRelayCommand(async () =>
        {
            CleanupStatus = "Scanning…";
            List<CleanupCategory> preview = await Task.Run(() => RobloxCleanup.Preview(S.CleanupLogsOlderThanDays));

            CleanupPreview.Clear();
            foreach (CleanupCategory c in preview) CleanupPreview.Add(c);

            long total = preview.Sum(c => c.Bytes);
            CleanupStatus = CanClean ? $"Ready to free {CleanupCategory.FormatSize(total)}. Nothing is deleted until you click Clean." : "Nothing to clean.";
            OnPropertyChanged(nameof(CanClean));
        });

        public ICommand CleanCommand => new AsyncRelayCommand(async () =>
        {
            if (!CanClean) return;

            string warning = Utility.Processes.IsRobloxRunning() ? "\n\nRoblox is running: files it's using will be skipped." : "";
            var answer = await Frontend.ShowMessageBox(
                $"Delete the {CleanupPreview.Sum(c => c.Files.Count):N0} previewed files ({CleanupCategory.FormatSize(CleanupPreview.Sum(c => c.Bytes))})?{warning}",
                MessageBoxImage.Question, MessageBoxButton.YesNo);
            if (answer != MessageBoxResult.Yes) return;

            var (deleted, freed, skipped) = await Task.Run(() => RobloxCleanup.Clean(CleanupPreview));
            CleanupPreview.Clear();
            CleanupStatus = $"Deleted {deleted:N0} files, freed {CleanupCategory.FormatSize(freed)}" + (skipped > 0 ? $" ({skipped} in use, skipped)." : ".");
            OnPropertyChanged(nameof(CanClean));
        });

        #endregion
    }
}
