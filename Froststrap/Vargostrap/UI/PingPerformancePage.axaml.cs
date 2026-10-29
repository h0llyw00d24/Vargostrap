// SPDX-FileCopyrightText: 2026 Vargostrap
//
// SPDX-License-Identifier: MPL-2.0

using Avalonia;
using Avalonia.Controls;
using Avalonia.Interactivity;

namespace Froststrap.Vargostrap.UI
{
    internal partial class PingPerformancePage : UserControl
    {
        public PingPerformancePage()
        {
            InitializeComponent();
            App.FrostRPC?.SetPage(VargoStrings.PingPerformanceTitle);
        }

        protected override void OnDataContextChanged(EventArgs e)
        {
            base.OnDataContextChanged(e);

            if (DataContext is PingPerformanceViewModel vm)
                vm.GetTopLevel = () => TopLevel.GetTopLevel(this);
        }

        protected override void OnUnloaded(RoutedEventArgs e)
        {
            (DataContext as PingPerformanceViewModel)?.Detach();
            base.OnUnloaded(e);
        }
    }
}
