// SPDX-FileCopyrightText: 2026 Vargostrap
//
// SPDX-License-Identifier: MPL-2.0

using Avalonia.Controls;

namespace Froststrap.Vargostrap.UI
{
    /// <summary>Section of the Ping &amp; Performance page (Windows only).</summary>
    internal partial class WindowsToolsSection : UserControl
    {
        public WindowsToolsSection()
        {
            InitializeComponent();

            if (!OperatingSystem.IsWindows())
            {
                IsVisible = false;
                return;
            }

            var vm = new WindowsToolsViewModel();
            DataContext = vm;
        }
    }
}
