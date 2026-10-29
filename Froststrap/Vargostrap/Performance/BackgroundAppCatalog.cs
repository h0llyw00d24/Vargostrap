// SPDX-FileCopyrightText: 2026 Vargostrap
//
// SPDX-License-Identifier: MPL-2.0

using Froststrap.Vargostrap.Models;

namespace Froststrap.Vargostrap.Performance
{
    /// <param name="Service">Windows service name for apps that run as a service (pausing = stopping it until Roblox closes).</param>
    internal sealed record BackgroundApp(string Id, string Name, string Category, string[] ProcessNames, string? Service, BackgroundAction Default)
    {
        /// <summary>Services run inside shared svchost processes, so they can only be paused or ignored.</summary>
        public IReadOnlyList<BackgroundAction> AllowedActions => Service is not null
            ? [BackgroundAction.Ignore, BackgroundAction.Pause]
            : [BackgroundAction.Ignore, BackgroundAction.LowerPriority, BackgroundAction.Pause, BackgroundAction.ThrottleUploads, BackgroundAction.CloseOnLaunch];
    }

    /// <summary>
    /// Known bandwidth-heavy apps and services, with conservative defaults: things you're unlikely to
    /// need mid-game (sync, updaters, launchers, torrents) default to Pause; browsers, voice chat and
    /// streaming default to Ignore. Every default can be changed per app and the choice is remembered.
    /// The whole feature is off until the user turns it on.
    /// </summary>
    internal static class BackgroundAppCatalog
    {
        public static readonly IReadOnlyList<BackgroundApp> Apps =
        [
            // Windows
            new("windows-update", "Windows Update", "Windows", ["TiWorker", "MoUsoCoreWorker", "usocoreworker", "wuauclt"], "wuauserv", BackgroundAction.Pause),
            new("delivery-optimization", "Delivery Optimization", "Windows", [], "DoSvc", BackgroundAction.Pause),
            new("bits", "Background Intelligent Transfer (BITS)", "Windows", [], "BITS", BackgroundAction.Ignore),

            // Cloud sync
            new("onedrive", "OneDrive", "Cloud sync", ["OneDrive"], null, BackgroundAction.Pause),
            new("google-drive", "Google Drive", "Cloud sync", ["GoogleDriveFS"], null, BackgroundAction.Pause),
            new("dropbox", "Dropbox", "Cloud sync", ["Dropbox"], null, BackgroundAction.Pause),
            new("icloud", "iCloud", "Cloud sync", ["iCloudDrive", "iCloudServices", "iCloudPhotos"], null, BackgroundAction.Pause),

            // Game launchers
            new("steam", "Steam", "Launchers", ["steam", "steamwebhelper"], null, BackgroundAction.Pause),
            new("epic", "Epic Games Launcher", "Launchers", ["EpicGamesLauncher", "EpicWebHelper"], null, BackgroundAction.Pause),
            new("battlenet", "Battle.net", "Launchers", ["Battle.net"], null, BackgroundAction.Pause),
            new("xbox", "Xbox app", "Launchers", ["XboxPcApp"], null, BackgroundAction.Pause),
            new("ea", "EA app", "Launchers", ["EADesktop"], null, BackgroundAction.Pause),
            new("ubisoft", "Ubisoft Connect", "Launchers", ["upc", "UbisoftConnect"], null, BackgroundAction.Pause),

            // Browsers (downloads): ignored by default, you may be using them
            new("chrome", "Google Chrome", "Browsers", ["chrome"], null, BackgroundAction.Ignore),
            new("edge", "Microsoft Edge", "Browsers", ["msedge"], null, BackgroundAction.Ignore),
            new("firefox", "Firefox", "Browsers", ["firefox"], null, BackgroundAction.Ignore),
            new("opera", "Opera / Opera GX", "Browsers", ["opera"], null, BackgroundAction.Ignore),
            new("brave", "Brave", "Browsers", ["brave"], null, BackgroundAction.Ignore),

            // Torrents
            new("qbittorrent", "qBittorrent", "Torrents", ["qbittorrent"], null, BackgroundAction.Pause),
            new("utorrent", "µTorrent / BitTorrent", "Torrents", ["uTorrent", "BitTorrent"], null, BackgroundAction.Pause),
            new("transmission", "Transmission", "Torrents", ["transmission-qt"], null, BackgroundAction.Pause),
            new("deluge", "Deluge", "Torrents", ["deluge"], null, BackgroundAction.Pause),

            // Voice, calls and streaming: ignored by default
            new("discord", "Discord", "Voice and streaming", ["Discord"], null, BackgroundAction.Ignore),
            new("zoom", "Zoom", "Voice and streaming", ["Zoom"], null, BackgroundAction.Ignore),
            new("teams", "Microsoft Teams", "Voice and streaming", ["ms-teams", "Teams"], null, BackgroundAction.Ignore),
            new("spotify", "Spotify", "Voice and streaming", ["Spotify"], null, BackgroundAction.Ignore),
            new("obs", "OBS Studio", "Voice and streaming", ["obs64"], null, BackgroundAction.Ignore),

            // Updaters and backup
            new("google-update", "Google Updater", "Updaters and backup", ["GoogleUpdate"], null, BackgroundAction.Pause),
            new("edge-update", "Microsoft Edge Update", "Updaters and backup", ["MicrosoftEdgeUpdate"], null, BackgroundAction.Pause),
            new("adobe", "Adobe updater", "Updaters and backup", ["AdobeARM", "AdobeUpdateService"], null, BackgroundAction.Pause),
            new("backblaze", "Backblaze", "Updaters and backup", ["bztransmit64", "bztransmit"], null, BackgroundAction.Pause),
        ];

        public const string CustomPrefix = "exe:";

        public static BackgroundApp? Find(string id) => Apps.FirstOrDefault(a => a.Id == id);

        /// <summary>An app the user picked from the live "other apps" list, matched by process name.</summary>
        public static BackgroundApp Custom(string processName) =>
            new(CustomPrefix + processName, processName, "Added by you", [processName], null, BackgroundAction.Ignore);

        public static BackgroundAction ActionFor(BackgroundApp app)
        {
            AppRule? rule = VargoApp.Settings.Prop.AppRules.FirstOrDefault(r => r.Id == app.Id);
            BackgroundAction action = rule?.Action ?? app.Default;
            return app.AllowedActions.Contains(action) ? action : BackgroundAction.Ignore;
        }

        public static void SetAction(BackgroundApp app, BackgroundAction action)
        {
            List<AppRule> rules = VargoApp.Settings.Prop.AppRules;
            rules.RemoveAll(r => r.Id == app.Id);

            // custom apps are always remembered; catalogue apps only when they differ from the default
            if (action != app.Default || app.Id.StartsWith(CustomPrefix, StringComparison.Ordinal))
                rules.Add(new AppRule { Id = app.Id, Action = action });
        }

        /// <summary>Catalogue apps plus the ones the user added.</summary>
        public static IEnumerable<BackgroundApp> AllConfigured() =>
            Apps.Concat(VargoApp.Settings.Prop.AppRules
                .Where(r => r.Id.StartsWith(CustomPrefix, StringComparison.Ordinal))
                .Select(r => Custom(r.Id[CustomPrefix.Length..])));

        public static string Describe(BackgroundAction action) => action switch
        {
            BackgroundAction.Ignore => "Ignore",
            BackgroundAction.LowerPriority => "Lower priority",
            BackgroundAction.Pause => "Pause",
            BackgroundAction.ThrottleUploads => "Throttle uploads",
            BackgroundAction.CloseOnLaunch => "Close at launch",
            _ => action.ToString(),
        };
    }
}
