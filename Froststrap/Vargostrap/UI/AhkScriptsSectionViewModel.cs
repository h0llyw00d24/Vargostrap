// SPDX-FileCopyrightText: 2026 Vargostrap
//
// SPDX-License-Identifier: MPL-2.0

using System.Collections.ObjectModel;
using System.Windows.Input;
using Avalonia.Controls;
using Avalonia.Platform.Storage;
using CommunityToolkit.Mvvm.Input;
using Froststrap.UI.ViewModels;
using Froststrap.Vargostrap.Ahk;
using Froststrap.Vargostrap.Models;

namespace Froststrap.Vargostrap.UI
{
    /// <summary>
    /// "AutoHotkey scripts" section on the Bootstrapper page: the scripts to run alongside Roblox.
    /// Saved with the window's Save button.
    /// </summary>
    internal sealed class AhkScriptsSectionViewModel : NotifyPropertyChangedViewModel
    {
        private const string WarningText =
            "Before you use AutoHotkey scripts with Roblox:\n\n"
            + "• Scripts that only remap keys (for example, moving a key to another button) are generally fine.\n"
            + "• Macros that play for you (auto-clicking, auto-farming, or anything else that automates gameplay) can break Roblox's Terms of Use or a game's own rules, and may get your account banned.\n"
            + "• You are responsible for the scripts you run.\n\n"
            + "Vargostrap only starts the script files you choose, as separate programs, and closes them when Roblox closes. "
            + "It never changes Roblox or writes scripts for you.\n\n"
            + "This message is shown once.";

        private static VargostrapSettings S => VargoApp.Settings.Prop;

        public ObservableCollection<AhkScript> Scripts { get; } = new(S.AhkScripts);

        public AhkScriptsSectionViewModel()
        {
            Scripts.CollectionChanged += (_, _) =>
            {
                S.AhkScripts = [.. Scripts];
                OnPropertyChanged(nameof(HasScripts));
            };
        }

        public static bool IsWindows => OperatingSystem.IsWindows();

        public static bool AhkInstalled => OperatingSystem.IsWindows() && AhkInstallation.Find() is not null;

        public static bool AhkMissing => OperatingSystem.IsWindows() && !AhkInstalled;

        public static string AhkStatus => OperatingSystem.IsWindows() && AhkInstallation.Find() is AhkInstallation a
            ? $"AutoHotkey found: {a.ExecutablePath}"
            : "AutoHotkey isn't installed.";

        public static ICommand OpenDownloadCommand => GlobalViewModel.OpenWebpageCommand;

        public static string DownloadUrl => AhkInstallation.DownloadUrl;

        public bool HasScripts => Scripts.Count > 0;

        public bool Enabled
        {
            get => S.AhkEnabled;
            set
            {
                if (value && !S.AhkWarningAcknowledged)
                {
                    _ = EnableAfterWarningAsync();
                    return;
                }

                S.AhkEnabled = value;
                OnPropertyChanged();
            }
        }

        private async Task EnableAfterWarningAsync()
        {
            if (await ConfirmWarningAsync())
                S.AhkEnabled = true;

            OnPropertyChanged(nameof(Enabled));
        }

        private static async Task<bool> ConfirmWarningAsync()
        {
            if (S.AhkWarningAcknowledged)
                return true;

            var answer = await Frontend.ShowMessageBox(WarningText, MessageBoxImage.Warning, MessageBoxButton.OKCancel);
            if (answer != MessageBoxResult.OK)
                return false;

            S.AhkWarningAcknowledged = true;
            VargoApp.Settings.Save();
            return true;
        }

        public IAsyncRelayCommand AddScriptCommand => new AsyncRelayCommand<Control>(async control =>
        {
            if (control is null || TopLevel.GetTopLevel(control) is not TopLevel topLevel)
                return;

            if (!await ConfirmWarningAsync())
                return;

            var files = await topLevel.StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
            {
                Title = "Choose AutoHotkey scripts",
                AllowMultiple = true,
                FileTypeFilter = [new FilePickerFileType("AutoHotkey script") { Patterns = ["*.ahk", "*.ah2"] }]
            });

            foreach (var file in files)
            {
                string path = file.Path.LocalPath;
                if (!Scripts.Any(s => string.Equals(s.Path, path, StringComparison.OrdinalIgnoreCase)))
                    Scripts.Add(new AhkScript { Path = path, Enabled = true });
            }
        });

        public ICommand RemoveScriptCommand => new RelayCommand<AhkScript>(script =>
        {
            if (script is not null)
                Scripts.Remove(script);
        });
    }
}
