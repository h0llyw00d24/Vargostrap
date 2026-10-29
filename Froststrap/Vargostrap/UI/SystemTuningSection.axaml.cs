// SPDX-FileCopyrightText: 2026 Vargostrap
//
// SPDX-License-Identifier: MPL-2.0

using System.Globalization;
using Avalonia.Controls;
using Avalonia.Data.Converters;
using Avalonia.Interactivity;
using Froststrap.Vargostrap.Models;
using Froststrap.Vargostrap.Performance;

namespace Froststrap.Vargostrap.UI
{
    /// <summary>Priority, background traffic and safety sections of the Ping &amp; Performance page (Windows only).</summary>
    internal partial class SystemTuningSection : UserControl
    {
        public SystemTuningSection()
        {
            InitializeComponent();

            if (OperatingSystem.IsWindows())
                DataContext = new SystemTuningViewModel();
            else
                IsVisible = false;
        }

        protected override void OnUnloaded(RoutedEventArgs e)
        {
            (DataContext as IDisposable)?.Dispose();
            base.OnUnloaded(e);
        }
    }

    internal sealed class PriorityLevelConverter : IValueConverter
    {
        public static readonly PriorityLevelConverter Instance = new();

        public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture) => value switch
        {
            RobloxPriorityLevel.AboveNormal => "Above normal",
            RobloxPriorityLevel.High => "High",
            RobloxPriorityLevel.Normal => "Normal",
            _ => System.Convert.ToString(value, culture),
        };

        public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) =>
            throw new NotSupportedException();
    }

    internal sealed class BackgroundActionConverter : IValueConverter
    {
        public static readonly BackgroundActionConverter Instance = new();

        public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture) =>
            value is BackgroundAction a ? BackgroundAppCatalog.Describe(a) : value?.ToString();

        public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) =>
            throw new NotSupportedException();
    }
}
