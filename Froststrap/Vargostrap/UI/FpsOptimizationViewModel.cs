// SPDX-FileCopyrightText: 2026 Vargostrap
//
// SPDX-License-Identifier: MPL-2.0

using System.Collections.ObjectModel;
using System.Windows.Input;
using Avalonia.Threading;
using CommunityToolkit.Mvvm.Input;
using Froststrap.UI.ViewModels.Settings;
using Froststrap.Vargostrap.FastFlags;

namespace Froststrap.Vargostrap.UI
{
    internal sealed record FramerateCapOption(int Value, string Label);

    /// <summary>
    /// FPS Optimization page (replaces upstream's FastFlag Settings page). Inherits every preset toggle
    /// from <see cref="FastFlagsViewModel"/> so upstream fixes to those keep flowing in, and adds the
    /// allowlist checker, one-click presets and Roblox's own frame-rate cap.
    /// </summary>
    internal sealed class FpsOptimizationViewModel : FastFlagsViewModel
    {
        private readonly AllowlistService _allowlist = VargoApp.Allowlist;

        public FpsOptimizationViewModel(IDialogService dialogService)
            : base(new DefaultFastFlagsService(), new DefaultSettingsService(), dialogService)
        {
            _allowlist.Changed += OnAllowlistChanged;
            RecheckViolations();
            _allowlist.RefreshIfStale();
        }

        /// <summary>Called by the page when it unloads, so the long-lived service doesn't keep us alive.</summary>
        public void Detach() => _allowlist.Changed -= OnAllowlistChanged;

        private void OnAllowlistChanged(object? sender, EventArgs e) => Dispatcher.UIThread.Post(() =>
        {
            IsRefreshing = false;
            RecheckViolations();
            OnPropertyChanged(nameof(AllowlistStatus));
            OnPropertyChanged(nameof(AllowlistError));
            OnPropertyChanged(nameof(HasAllowlistError));
        });

        #region Allowlist checker

        public ObservableCollection<string> Violations { get; } = [];

        public bool HasViolations => Violations.Count > 0;

        public string ViolationsTitle => Violations.Count == 1
            ? "1 flag is not on Roblox's allowlist"
            : $"{Violations.Count} flags are not on Roblox's allowlist";

        public string ViolationsMessage =>
            "Roblox ignores these flags. They have no effect in game. Remove them in the custom flag editor (use \"Clean List\"), "
            + "or keep them in case Roblox adds them back later:\n\n" + string.Join("\n", Violations);

        public string AllowlistStatus
        {
            get
            {
                string when = _allowlist.FetchedAtUtc is DateTime t ? t.ToLocalTime().ToString("g", CultureInfo.CurrentCulture) : "never";
                string origin = _allowlist.Origin switch
                {
                    AllowlistOrigin.Live => $"updated {when}",
                    AllowlistOrigin.Cache => $"cached, last updated {when}",
                    _ => "offline copy built into Vargostrap",
                };
                return $"{_allowlist.Flags.Count} allowed flags ({origin}). Source: {_allowlist.Source}";
            }
        }

        public string? AllowlistError => _allowlist.LastError is null ? null : $"Last refresh failed: {_allowlist.LastError}";

        public bool HasAllowlistError => _allowlist.LastError is not null;

        private bool _isRefreshing;
        public bool IsRefreshing
        {
            get => _isRefreshing;
            private set
            {
                if (SetProperty(ref _isRefreshing, value))
                    OnPropertyChanged(nameof(CanRefresh));
            }
        }

        public bool CanRefresh => !IsRefreshing;

        public ICommand RefreshAllowlistCommand => new AsyncRelayCommand(async () =>
        {
            IsRefreshing = true;
            await Task.Run(() => _allowlist.RefreshAsync());
        });

        public static string OfficialAllowlistUrl => AllowlistService.OfficialListUrl;

        /// <summary>Custom allowlist URL; blank = default signed source. Saved immediately.</summary>
        public string AllowlistSourceUrl
        {
            get => VargoApp.Settings.Prop.AllowlistSourceUrl;
            set
            {
                VargoApp.Settings.Prop.AllowlistSourceUrl = value?.Trim() ?? "";
                VargoApp.Settings.Save();
                OnPropertyChanged();
            }
        }

        private void RecheckViolations()
        {
            Violations.Clear();

            foreach (AllowlistViolation v in _allowlist.FindViolations(App.FastFlags.Prop.Keys))
            {
                string kind = FastFlagManager.IsPreset(v.Flag) ? "preset" : "custom";
                Violations.Add(v.CaseMismatchOf is null
                    ? $"• {v.Flag} ({kind})"
                    : $"• {v.Flag} ({kind}). Capitalization differs from the allowed flag {v.CaseMismatchOf}");
            }

            OnPropertyChanged(nameof(HasViolations));
            OnPropertyChanged(nameof(ViolationsTitle));
            OnPropertyChanged(nameof(ViolationsMessage));
        }

        #endregion

        #region Presets

        public static IReadOnlyList<FpsPreset> Presets => FpsPresets.All;

        public string ActivePresetText { get; private set; } = DescribeActivePreset();

        private static string DescribeActivePreset() => FpsPresets.Detect() is FpsPreset p
            ? $"Current: {p.Name}"
            : "Current: custom mix (no preset matches your flags exactly)";

        private string? _presetNotice;
        public string? PresetNotice
        {
            get => _presetNotice;
            private set
            {
                SetProperty(ref _presetNotice, value);
                OnPropertyChanged(nameof(HasPresetNotice));
            }
        }

        public bool HasPresetNotice => !string.IsNullOrEmpty(PresetNotice);

        public ICommand ApplyPresetCommand => new RelayCommand<FpsPreset>(preset =>
        {
            if (preset is null)
                return;

            List<string> skipped = FpsPresets.Apply(preset, _allowlist);

            PresetNotice = skipped.Count == 0
                ? $"Applied \"{preset.Name}\". Click Save to write it, then restart Roblox."
                : $"Applied \"{preset.Name}\", but skipped flags Roblox no longer allows: {string.Join(", ", skipped)}.";

            RecheckViolations();
            ActivePresetText = DescribeActivePreset();
            RaiseAllPresetProperties();
        });

        // The inherited toggles read straight from App.FastFlags, so a preset changes all of them at once.
        private void RaiseAllPresetProperties()
        {
            foreach (string name in (string[])[
                nameof(RemoveGrass), nameof(LowPolyMeshesEnabled), nameof(LowPolyMeshesLevel), nameof(PauseVoxelizer),
                nameof(GraySky), nameof(SelectedMSAALevel), nameof(SelectedQualityLevel), nameof(SelectedTextureQuality),
                nameof(ActivePresetText)])
            {
                OnPropertyChanged(name);
            }
        }

        #endregion

        #region Frame rate (Roblox's own setting, not a FastFlag)

        /// <summary>
        /// No FPS-unlock flag is on the allowlist, so the cap goes through Roblox's own "Maximum Frame Rate"
        /// setting in GlobalBasicSettings_13.xml, the same value the in-game menu writes.
        /// </summary>
        public static bool FramerateCapAvailable => App.GlobalSettings.Loaded;

        public static bool FramerateCapUnavailable => !FramerateCapAvailable;

        /// <summary>
        /// "Unlimited" writes a cap far above any monitor's refresh rate. Roblox officially supports up to
        /// 240 and may still clamp to that; there is no supported way past it (the FPS-unlock FastFlag isn't
        /// allowlisted, and memory-patching unlockers are out of scope for Vargostrap).
        /// </summary>
        public const int UnlimitedFramerateCap = 9999;

        public static IReadOnlyList<FramerateCapOption> FramerateCapOptions { get; } =
        [
            new(30, "30 FPS"),
            new(60, "60 FPS (Roblox default)"),
            new(75, "75 FPS"),
            new(120, "120 FPS"),
            new(144, "144 FPS"),
            new(165, "165 FPS"),
            new(240, "240 FPS"),
            new(UnlimitedFramerateCap, "Unlimited"),
        ];

        /// <summary>Maps the stored FramerateCap value to a menu option (shared with the Roblox Settings page).</summary>
        public static FramerateCapOption OptionForStoredCap(string? stored)
        {
            if (!int.TryParse(stored, NumberStyles.Integer, CultureInfo.InvariantCulture, out int cap) || cap < 1)
                cap = 60;

            // anything far above real refresh rates (e.g. 5000 written by another tool) counts as unlimited
            if (cap >= 1000)
                return FramerateCapOptions[^1];

            return FramerateCapOptions.FirstOrDefault(o => o.Value == cap)
                ?? new FramerateCapOption(cap, $"{cap} FPS (custom)");
        }

        public FramerateCapOption? SelectedFramerateCap
        {
            get => OptionForStoredCap(App.GlobalSettings.GetPreset("Rendering.FramerateCap"));
            set
            {
                if (value is null)
                    return;

                App.GlobalSettings.SetPreset("Rendering.FramerateCap", value.Value);
                OnPropertyChanged();
            }
        }

        public static bool SettingsFileLocked => GBSEditor.GetReadOnly();

        #endregion
    }
}
