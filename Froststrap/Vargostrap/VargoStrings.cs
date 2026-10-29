// SPDX-FileCopyrightText: 2026 Vargostrap
//
// SPDX-License-Identifier: MPL-2.0

namespace Froststrap.Vargostrap
{
    /// <summary>
    /// UI text for Vargostrap pages that upstream files have to reference (nav items, titles).
    /// English only for now; kept in one place so it can move to a VargostrapStrings.resx later
    /// without touching upstream's Strings.resx.
    /// </summary>
    internal static class VargoStrings
    {
        public const string FpsOptimizationTitle = "FPS Optimization";
        public const string FpsOptimizationDescription = "Speed up Roblox with allowlisted FastFlags, presets and frame-rate settings.";

        public const string RobloxSettingsTitle = "Roblox Settings";
        public const string PingPerformanceTitle = "Ping & Performance";
        public const string PingPerformanceDescription = "Monitor your connection, pick the best servers, and tune Windows for Roblox.";

        public const string RobloxSettingsDescription = "Roblox's own in-game settings, with a backup of your original file and an optional lock.";
    }
}
