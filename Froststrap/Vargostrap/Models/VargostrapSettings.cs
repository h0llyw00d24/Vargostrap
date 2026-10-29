// SPDX-FileCopyrightText: 2026 Vargostrap
//
// SPDX-License-Identifier: MPL-2.0

namespace Froststrap.Vargostrap.Models
{
    /// <summary>
    /// Vargostrap-only options, persisted to VargostrapSettings.json next to upstream's Settings.json
    /// so upstream changes to <see cref="Froststrap.Models.Persistable.Settings"/> never conflict.
    /// </summary>
    internal class VargostrapSettings
    {
        // FPS Optimization page

        /// <summary>
        /// Where the FastFlag allowlist is fetched from. Empty = Froststrap's signed Data.json
        /// (the same file upstream already trusts). Otherwise any URL returning one of the formats
        /// <see cref="FastFlags.AllowlistService"/> understands.
        /// </summary>
        public string AllowlistSourceUrl { get; set; } = "";

        // Roblox Settings page

        /// <summary>
        /// The user asked Vargostrap to keep GlobalBasicSettings read-only. The file attribute is the
        /// real state; this records the intent so a lock that something else removed can be detected.
        /// </summary>
        public bool RobloxSettingsLockEnabled { get; set; }

        /// <summary>Problems seen during the last Roblox session while the lock was on; null = none.</summary>
        public string? LastLockReport { get; set; }

        public DateTime? LastLockReportUtc { get; set; }

        // Integrations page: Vargostrap Discord RPC (the presence shown while the app is open)

        /// <summary>Your own Discord application ID (Developer Portal). Empty = upstream's, which Discord shows as "Froststrap".</summary>
        public string DiscordApplicationId { get; set; } = "";

        /// <summary>Top line. Placeholders: {page}, {version}. Empty = hidden.</summary>
        public string RpcDetails { get; set; } = "Tuning Roblox with Vargostrap";

        /// <summary>Second line. Placeholders: {page}, {version}. Empty = hidden.</summary>
        public string RpcState { get; set; } = "Page: {page}";

        public bool RpcShowTimer { get; set; } = true;

        public bool RpcShowVersion { get; set; } = true;

        public bool RpcShowLogo { get; set; } = true;

        /// <summary>
        /// Logo for Discord: an https image URL, or the name of an art asset uploaded to your own Discord
        /// application. Empty = "vargostrap" asset when a custom application ID is set, otherwise no image.
        /// </summary>
        public string RpcLogo { get; set; } = "";

        public string RpcButton1Label { get; set; } = "GitHub";
        public string RpcButton1Url { get; set; } = "";
        public string RpcButton2Label { get; set; } = "";
        public string RpcButton2Url { get; set; } = "";

        /// <summary>Also show the logo (as the small image) in the in-game Roblox presence. Needs an https <see cref="RpcLogo"/>.</summary>
        public bool RpcShowLogoInGame { get; set; }

        // Bootstrapper page: AutoHotkey scripts

        public bool AhkEnabled { get; set; }

        public List<AhkScript> AhkScripts { get; set; } = [];

        public bool AhkWarningAcknowledged { get; set; }

        // Ping & Performance page: network monitor

        /// <summary>Measure latency, jitter and loss on the connection to Roblox every second during Roblox sessions.</summary>
        public bool NetMonitorEnabled { get; set; } = true;

        /// <summary>A sample this many ms above the session's typical latency counts as a spike.</summary>
        public int NetSpikeThresholdMs { get; set; } = 80;

        public bool NetTracerouteOnSpike { get; set; } = true;

        // Ping & Performance page: server picker

        /// <summary>Regions (as "City, CC") tried first, in order, before the nearest ones.</summary>
        public List<string> PreferredRegions { get; set; } = [];

        // Ping & Performance page: priority management (applied each Roblox launch, undone when it closes)

        public bool PriorityEnabled { get; set; }

        public RobloxPriorityLevel RobloxPriority { get; set; } = RobloxPriorityLevel.AboveNormal;

        public bool PriorityDisableEcoQos { get; set; } = true;

        public bool PriorityMemory { get; set; } = true;

        public bool PriorityHighPerformanceGpu { get; set; } = true;

        /// <summary>Tag Roblox traffic DSCP 46 with a QoS policy (admin). Only helps if the router honours DSCP.</summary>
        public bool PriorityDscp { get; set; }

        // Ping & Performance page: background traffic control

        public bool TrafficControlEnabled { get; set; }

        /// <summary>Per-app choices that override the catalogue defaults (remembered per app).</summary>
        public List<AppRule> AppRules { get; set; } = [];

        public int ThrottleMbps { get; set; } = 2;

        // Ping & Performance page: power and Windows settings

        /// <summary>Switch to the High/Ultimate Performance plan while Roblox runs, then back.</summary>
        public bool PowerPlanDuringPlay { get; set; }

        // Ping & Performance page: cleanup

        public int CleanupLogsOlderThanDays { get; set; } = 3;

        // Ping & Performance page: benchmarking

        /// <summary>Path to Intel's PresentMon console exe, chosen by the user (not bundled or downloaded).</summary>
        public string PresentMonPath { get; set; } = "";

        public int BenchmarkSeconds { get; set; } = 60;
    }

    internal enum RobloxPriorityLevel { Normal, AboveNormal, High }

    // append only: values are saved as numbers
    internal enum BackgroundAction { Ignore, LowerPriority, Pause, ThrottleUploads, CloseOnLaunch }

    internal class AppRule
    {
        /// <summary>Catalogue id (e.g. "onedrive"), or "exe:&lt;name&gt;" for an app the user added from the live list.</summary>
        public string Id { get; set; } = "";

        public BackgroundAction Action { get; set; }
    }

    internal class AhkScript
    {
        public string Path { get; set; } = "";

        public bool Enabled { get; set; } = true;
    }
}
