// SPDX-FileCopyrightText: 2026 Vargostrap
//
// SPDX-License-Identifier: MPL-2.0

namespace Froststrap.Vargostrap
{
    /// <summary>Vargostrap's default look. Upstream's defaults (system theme, blue gradient) point here.</summary>
    internal static class VargoTheme
    {
        /// <summary>
        /// The window gradient is only drawn for the Custom theme, so that's the default. Custom uses
        /// Avalonia's dark variant for controls and text.
        /// </summary>
        public const Theme DefaultTheme = Theme.Custom;

        /// <summary>
        /// Default window background: a warm dark-brown gradient. Fully opaque (upstream's is 50% alpha,
        /// which relies on a window backdrop being enabled; without one the desktop would show through).
        /// </summary>
        public static List<GradientStops> CreateDefaultGradient() =>
        [
            new() { Offset = 0.0, Color = "#FF4A3020" },
            new() { Offset = 0.5, Color = "#FF3A2518" },
            new() { Offset = 1.0, Color = "#FF261810" }
        ];
    }
}
