// SPDX-FileCopyrightText: 2026 Vargostrap
//
// SPDX-License-Identifier: MPL-2.0

using System.Windows.Input;
using CommunityToolkit.Mvvm.Input;
using Froststrap.Enums.GBSPresets;
using Froststrap.UI.ViewModels.Settings;
using Froststrap.Vargostrap.RobloxSettings;

namespace Froststrap.Vargostrap.UI
{
    /// <summary>
    /// Roblox Settings page (replaces upstream's Global Settings page). Shows only the settings a player can
    /// change in Roblox's own in-game Settings menu, plus backup/restore and the opt-in read-only lock.
    /// Everything else in the file is still reachable through upstream's advanced editor, which this
    /// inherits (with import/export) from <see cref="GlobalSettingsViewModel"/>.
    /// </summary>
    internal sealed class RobloxSettingsViewModel(IDialogServiceGlobal dialogService) : GlobalSettingsViewModel(dialogService)
    {
        private const string Path_FramerateCap = "Rendering.FramerateCap";
        private const string Path_Quality = "Rendering.SavedQualityLevel";
        private const string Path_Fullscreen = "Rendering.Fullscreen";
        private const string Path_Volume = "Audio.MasterVolume";
        private const string Path_Sensitivity = "User.MouseSensitivity";
        private const string Path_InvertY = "User.CameraYInverted";
        private const string Path_ShiftLock = "User.ShiftLock";
        private const string Path_Transparency = "UI.Transparency";
        private const string Path_ReducedMotion = "UI.ReducedMotion";
        private const string Path_TextSize = "UI.FontSize";
        private const string Path_PerfStats = "Misc.PerformanceStatsVisible";
        private const string Path_ChatTranslation = "Misc.ChatTranslationEnabled";

        public static bool FileAvailable => App.GlobalSettings.Loaded;

        public static bool FileMissing => !FileAvailable;

        public static bool RobloxRunning => Utility.Processes.IsRobloxRunning();

        public static string FileLocation => GBSEditor.FileLocation;

        #region Backup

        public static string BackupStatus => SettingsBackup.CreatedAt is DateTime t
            ? $"Original saved {t.ToString("g", CultureInfo.CurrentCulture)}. It is never overwritten."
            : "No backup yet. Vargostrap saves a copy of your original file automatically the first time it changes anything.";

        public static bool CanRestore => SettingsBackup.Exists;

        public ICommand RestoreBackupCommand => new AsyncRelayCommand(async () =>
        {
            var confirm = await Frontend.ShowMessageBox(
                "Replace your current Roblox settings with the original backup?\n\nThis also turns off the read-only lock. Any unsaved changes on this page are discarded.",
                MessageBoxImage.Warning, MessageBoxButton.YesNo);

            if (confirm != MessageBoxResult.Yes)
                return;

            string? error = SettingsBackup.Restore();
            if (error is not null)
            {
                await Frontend.ShowMessageBox($"Couldn't restore the backup.\n\n{error}", MessageBoxImage.Error);
                return;
            }

            RefreshAll();
            await Frontend.ShowMessageBox("Your original Roblox settings are back.", MessageBoxImage.Information);
        });

        public static ICommand OpenBackupFolderCommand => new RelayCommand(() =>
        {
            Directory.CreateDirectory(SettingsBackup.BackupDirectory);
            Utility.Threading.ShellExecute(SettingsBackup.BackupDirectory);
        });

        #endregion

        #region Lock

        public bool LockSettings
        {
            get => SettingsLock.IsLocked;
            set
            {
                if (value == SettingsLock.IsLocked)
                    return;

                if (value)
                    _ = EnableLockAsync();
                else
                {
                    SettingsLock.Disable(reason: "user");
                    RefreshLock();
                }
            }
        }

        private async Task EnableLockAsync()
        {
            var confirm = await Frontend.ShowMessageBox(
                "Lock Roblox's settings file?\n\n"
                    + "• Roblox won't be able to overwrite the values you set here, for example when a game or the in-game menu changes them.\n"
                    + "• Changes you make in Roblox's own Settings menu won't be remembered after you close Roblox.\n"
                    + "• Vargostrap watches each Roblox session and tells you if the lock seems to cause a problem.\n\n"
                    + "Turn this off at any time to undo it completely. It only marks the file read-only and changes nothing else.",
                MessageBoxImage.Question, MessageBoxButton.YesNo);

            if (confirm == MessageBoxResult.Yes)
            {
                try
                {
                    SettingsLock.Enable();
                }
                catch (Exception ex)
                {
                    App.Logger.Error(ex, "Enabling Roblox settings lock failed");
                    await Frontend.ShowMessageBox($"Couldn't lock the settings file.\n\n{ex.Message}", MessageBoxImage.Error);
                }
            }

            RefreshLock();
        }

        public static bool LockRemovedExternally => SettingsLock.WasRemovedExternally;

        public static string? LockReport => VargoApp.Settings.Prop.LastLockReport is string r && VargoApp.Settings.Prop.LastLockReportUtc is DateTime t
            ? $"Last Roblox session ({t.ToLocalTime().ToString("g", CultureInfo.CurrentCulture)}):\n\n{r}"
            : null;

        public static bool HasLockReport => LockReport is not null;

        public ICommand DismissLockReportCommand => new RelayCommand(() =>
        {
            SettingsLock.ClearReport();
            RefreshLock();
        });

        public ICommand RelockCommand => new RelayCommand(() =>
        {
            SettingsLock.Enable();
            RefreshLock();
        });

        private void RefreshLock()
        {
            OnPropertyChanged(nameof(LockSettings));
            OnPropertyChanged(nameof(LockRemovedExternally));
            OnPropertyChanged(nameof(LockReport));
            OnPropertyChanged(nameof(HasLockReport));
            OnPropertyChanged(nameof(BackupStatus));
            OnPropertyChanged(nameof(CanRestore));
        }

        #endregion

        #region Graphics

        public static IReadOnlyList<FramerateCapOption> FramerateCapOptions => FpsOptimizationViewModel.FramerateCapOptions;

        public FramerateCapOption? SelectedFramerateCap
        {
            get => FpsOptimizationViewModel.OptionForStoredCap(App.GlobalSettings.GetPreset(Path_FramerateCap));
            set
            {
                if (value is null) return;
                App.GlobalSettings.SetPreset(Path_FramerateCap, value.Value);
                OnPropertyChanged();
            }
        }

        /// <summary>0 = Automatic, 1–10 = manual graphics quality, as in Roblox's menu.</summary>
        public double QualityLevel
        {
            get => Math.Clamp(ReadInt(Path_Quality, 0), 0, 10);
            set
            {
                App.GlobalSettings.SetPreset(Path_Quality, ((int)Math.Round(value)).ToString(CultureInfo.InvariantCulture));
                OnPropertyChanged();
                OnPropertyChanged(nameof(QualityLabel));
            }
        }

        public string QualityLabel => QualityLevel == 0 ? "Automatic" : $"Level {QualityLevel:0}";

        public bool FullscreenEnabled
        {
            get => ReadBool(Path_Fullscreen);
            set { WriteBool(Path_Fullscreen, value); OnPropertyChanged(); }
        }

        #endregion

        #region Audio

        /// <summary>Stored 0.0–1.0, shown as 0–10 like Roblox's volume slider.</summary>
        public double Volume
        {
            get => Math.Round(ReadDouble(Path_Volume, 1.0) * 10, 1);
            set
            {
                double stored = Math.Clamp(Math.Round(value) / 10.0, 0, 1);
                App.GlobalSettings.SetPreset(Path_Volume, stored.ToString(CultureInfo.InvariantCulture));
                OnPropertyChanged();
            }
        }

        #endregion

        #region Camera and controls

        /// <summary>Camera sensitivity; Roblox's slider runs 0.2–4.0 (default 1.0).</summary>
        public double CameraSensitivity
        {
            get => Math.Clamp(ReadDouble(Path_Sensitivity, 1.0), 0.2, 4.0);
            set
            {
                double v = Math.Round(Math.Clamp(value, 0.2, 4.0), 2);
                App.GlobalSettings.SetPreset(Path_Sensitivity, v.ToString(CultureInfo.InvariantCulture));
                OnPropertyChanged();
            }
        }

        public bool InvertCamera
        {
            get => ReadBool(Path_InvertY);
            set { WriteBool(Path_InvertY, value); OnPropertyChanged(); }
        }

        /// <summary>"Shift Lock Switch": ControlMode token 1 = enabled.</summary>
        public bool ShiftLockSwitch
        {
            get => App.GlobalSettings.GetPreset(Path_ShiftLock) == "1";
            set { App.GlobalSettings.SetPreset(Path_ShiftLock, value ? "1" : "0"); OnPropertyChanged(); }
        }

        #endregion

        #region Interface

        /// <summary>Roblox stores 0.0 (solid) – 1.0 (default); shown as a percentage.</summary>
        public double BackgroundTransparency
        {
            get => Math.Round(Math.Clamp(ReadDouble(Path_Transparency, 1.0), 0, 1) * 100);
            set
            {
                double stored = Math.Clamp(Math.Round(value) / 100.0, 0, 1);
                App.GlobalSettings.SetPreset(Path_Transparency, stored.ToString(CultureInfo.InvariantCulture));
                OnPropertyChanged();
            }
        }

        public static IReadOnlyDictionary<FontSize, string?> TextSizes => GBSEditor.FontSizes;

        public FontSize SelectedTextSize
        {
            get => TextSizes.FirstOrDefault(x => x.Value == App.GlobalSettings.GetPreset(Path_TextSize)).Key;
            set { App.GlobalSettings.SetPreset(Path_TextSize, TextSizes[value]); OnPropertyChanged(); }
        }

        public bool ReducedMotionEnabled
        {
            get => ReadBool(Path_ReducedMotion);
            set { WriteBool(Path_ReducedMotion, value); OnPropertyChanged(); }
        }

        #endregion

        #region Other

        public bool PerformanceStats
        {
            get => ReadBool(Path_PerfStats);
            set { WriteBool(Path_PerfStats, value); OnPropertyChanged(); }
        }

        public bool ChatTranslation
        {
            get => ReadBool(Path_ChatTranslation);
            set { WriteBool(Path_ChatTranslation, value); OnPropertyChanged(); }
        }

        #endregion

        /// <summary>Re-reads every value, e.g. after a restore reloaded the file.</summary>
        public void RefreshAll() => OnPropertyChanged(string.Empty);

        private static int ReadInt(string path, int fallback) =>
            int.TryParse(App.GlobalSettings.GetPreset(path), NumberStyles.Integer, CultureInfo.InvariantCulture, out int v) ? v : fallback;

        private static double ReadDouble(string path, double fallback) =>
            double.TryParse(App.GlobalSettings.GetPreset(path), NumberStyles.Float, CultureInfo.InvariantCulture, out double v) ? v : fallback;

        private static bool ReadBool(string path) =>
            string.Equals(App.GlobalSettings.GetPreset(path), "true", StringComparison.OrdinalIgnoreCase);

        private static void WriteBool(string path, bool value) =>
            App.GlobalSettings.SetPreset(path, value ? "true" : "false");
    }
}
