// SPDX-FileCopyrightText: 2026 Vargostrap
//
// SPDX-License-Identifier: MPL-2.0

namespace Froststrap.Vargostrap
{
    /// <summary>
    /// Single source of truth for the Vargostrap fork's identity.
    /// Upstream files reference these constants instead of hardcoding names, so an
    /// upstream merge only ever conflicts on the one-line references, never on values.
    /// </summary>
    internal static class Brand
    {
        public const string Name = "Vargostrap";

        // Must match <AssemblyName> in Froststrap.csproj (used by avares:// URIs and native resolvers).
        public const string AssemblyName = "Vargostrap";

        public const string Owner = "h0llyw00d24";
        public const string Repository = $"{Owner}/Vargostrap";
        public const string RepositoryUrl = $"https://github.com/{Repository}";
        public const string IssuesUrl = $"{RepositoryUrl}/issues";

        // Velopack update feed. Empty = self-update disabled. Never point this at Froststrap's
        // feed: Velopack would replace Vargostrap with a Froststrap build.
        public const string UpdateRepository = "";
        public static bool UpdatesEnabled => !string.IsNullOrEmpty(UpdateRepository);

        // Windows AppUserModelID (toast notifications / taskbar grouping). Distinct from
        // Froststrap's so both apps can be installed side by side.
        public const string AppUserModelId = "xyz.vargostrap.desktop";

        public const string LinuxDesktopFile = "vargostrap.desktop";
    }
}
