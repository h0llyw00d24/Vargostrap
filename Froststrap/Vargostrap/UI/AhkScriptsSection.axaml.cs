// SPDX-FileCopyrightText: 2026 Vargostrap
//
// SPDX-License-Identifier: MPL-2.0

using Avalonia.Controls;

namespace Froststrap.Vargostrap.UI
{
    /// <summary>Dropped into upstream's BehaviourPage (the "Bootstrapper" page) with one line; brings its own view model.</summary>
    internal partial class AhkScriptsSection : UserControl
    {
        public AhkScriptsSection()
        {
            InitializeComponent();
            DataContext = new AhkScriptsSectionViewModel();
        }
    }
}
