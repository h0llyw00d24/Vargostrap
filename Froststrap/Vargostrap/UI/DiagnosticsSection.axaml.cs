// SPDX-FileCopyrightText: 2026 Vargostrap
//
// SPDX-License-Identifier: MPL-2.0

using Avalonia.Controls;

namespace Froststrap.Vargostrap.UI
{
    /// <summary>Section of the Ping &amp; Performance page (Windows only).</summary>
    internal partial class DiagnosticsSection : UserControl
    {
        public DiagnosticsSection()
        {
            InitializeComponent();

            if (!OperatingSystem.IsWindows())
            {
                IsVisible = false;
                return;
            }

            var vm = new DiagnosticsViewModel();
            vm.GetTopLevel = () => TopLevel.GetTopLevel(this);
            DataContext = vm;
        }
    }
}
