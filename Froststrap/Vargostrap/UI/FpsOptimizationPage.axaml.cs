// SPDX-FileCopyrightText: 2026 Vargostrap
//
// SPDX-License-Identifier: MPL-2.0

using Avalonia.Controls;
using Avalonia.Interactivity;

namespace Froststrap.Vargostrap.UI
{
    internal partial class FpsOptimizationPage : UserControl
    {
        public FpsOptimizationPage()
        {
            InitializeComponent();
            App.FrostRPC?.SetPage("FPS Optimization");
        }

        protected override void OnUnloaded(RoutedEventArgs e)
        {
            (DataContext as FpsOptimizationViewModel)?.Detach();
            base.OnUnloaded(e);
        }
    }
}
