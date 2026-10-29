// SPDX-FileCopyrightText: 2026 Vargostrap
//
// SPDX-License-Identifier: MPL-2.0

using Avalonia;
using Avalonia.Controls;
using Froststrap.UI.Elements.Settings.Pages;
using Froststrap.UI.ViewModels.Settings;

namespace Froststrap.Vargostrap.UI
{
    internal partial class RobloxSettingsPage : UserControl
    {
        public RobloxSettingsPage()
        {
            InitializeComponent();
            App.FrostRPC?.SetPage("Roblox Settings");
        }

        protected override void OnAttachedToVisualTree(VisualTreeAttachmentEventArgs e)
        {
            base.OnAttachedToVisualTree(e);

            // Pick up changes Roblox made since the window opened. Unlike upstream's page, don't discard
            // edits that haven't been saved yet (e.g. the frame-rate cap set on the FPS page).
            if (!App.GlobalSettings.HasUnsavedChanges)
                App.GlobalSettings.Load();

            IDialogServiceGlobal dialogService = TopLevel.GetTopLevel(this)?.DataContext is MainWindowViewModel mainVm
                ? new GlobalSettingsDialogService(mainVm)
                : new DefaultGlobalDialogService();

            DataContext = new RobloxSettingsViewModel(dialogService);
        }
    }
}
