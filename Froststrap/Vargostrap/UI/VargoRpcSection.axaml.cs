// SPDX-FileCopyrightText: 2026 Vargostrap
//
// SPDX-License-Identifier: MPL-2.0

using Avalonia.Controls;

namespace Froststrap.Vargostrap.UI
{
    /// <summary>Dropped into upstream's IntegrationsPage with one line; brings its own view model.</summary>
    internal partial class VargoRpcSection : UserControl
    {
        public VargoRpcSection()
        {
            InitializeComponent();
            DataContext = new VargoRpcSectionViewModel();
        }
    }
}
