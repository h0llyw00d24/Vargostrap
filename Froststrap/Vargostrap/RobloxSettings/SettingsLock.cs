// SPDX-FileCopyrightText: 2026 Vargostrap
//
// SPDX-License-Identifier: MPL-2.0

namespace Froststrap.Vargostrap.RobloxSettings
{
    /// <summary>
    /// Opt-in "lock settings": marks GlobalBasicSettings read-only so Roblox can't overwrite the values set
    /// in Vargostrap. Fully reversible (it only flips the file's read-only attribute). While it's on, every
    /// Roblox session is checked for signs that the lock caused trouble; see <see cref="SessionCheck"/>.
    /// </summary>
    internal static class SettingsLock
    {
        public static bool IsLocked => File.Exists(GBSEditor.FileLocation) && GBSEditor.GetReadOnly();

        /// <summary>The user turned the lock on but the file is no longer read-only.</summary>
        public static bool WasRemovedExternally => VargoApp.Settings.Prop.RobloxSettingsLockEnabled && !IsLocked;

        public static void Enable()
        {
            SettingsBackup.EnsureBackup();
            App.GlobalSettings.SetReadOnly(true);

            VargoApp.Settings.Prop.RobloxSettingsLockEnabled = true;
            VargoApp.Settings.Save();
            App.Logger.Info("Roblox settings lock enabled");
        }

        public static void Disable(string reason)
        {
            if (File.Exists(GBSEditor.FileLocation))
                App.GlobalSettings.SetReadOnly(false);

            if (VargoApp.Settings.Prop.RobloxSettingsLockEnabled)
            {
                VargoApp.Settings.Prop.RobloxSettingsLockEnabled = false;
                VargoApp.Settings.Save();
            }

            App.Logger.Info($"Roblox settings lock disabled ({reason})");
        }

        public static void ClearReport()
        {
            VargoApp.Settings.Prop.LastLockReport = null;
            VargoApp.Settings.Prop.LastLockReportUtc = null;
            VargoApp.Settings.Save();
        }

        /// <summary>
        /// Watches one Roblox session while the lock is on. Signs that the lock caused a problem:
        /// Roblox exiting within <see cref="QuickExit"/> of starting, Roblox log lines about failing to
        /// save settings, or the read-only attribute disappearing during the session.
        /// </summary>
        internal sealed class SessionCheck
        {
            private static readonly TimeSpan QuickExit = TimeSpan.FromSeconds(20);

            private static readonly string[] SettingsWords = ["globalbasicsettings", "usergamesettings", "gamesettings", "settings file"];
            private static readonly string[] FailureWords = ["fail", "error", "denied", "unable", "could not", "couldn't", "read-only", "readonly"];
            private static readonly string[] IgnoreWords = ["clientappsettings", "fflag", "fastflag"];

            private readonly DateTime _startedUtc = DateTime.UtcNow;
            private readonly string? _logFile;

            private SessionCheck(string? logFile) => _logFile = logFile;

            public static SessionCheck? Begin(WatcherData data)
            {
                if (data.LaunchMode != LaunchMode.Player || !VargoApp.Settings.Prop.RobloxSettingsLockEnabled)
                    return null;

                if (!IsLocked)
                    App.Logger.Warn("Roblox settings lock is enabled but the file isn't read-only at launch");

                return new SessionCheck(data.LogFile);
            }

            public async Task EndAsync()
            {
                List<string> problems = [];
                TimeSpan duration = DateTime.UtcNow - _startedUtc;

                if (duration < QuickExit)
                    problems.Add($"Roblox closed {duration.TotalSeconds:0} seconds after starting. If you didn't close it yourself, the lock may have stopped it from launching.");

                List<string> logLines = FindSaveErrors(_logFile);
                if (logLines.Count > 0)
                    problems.Add("Roblox's log shows it couldn't save its settings:\n" + string.Join("\n", logLines.Select(l => "  " + l)));

                if (!IsLocked)
                    problems.Add("The read-only lock was removed during the session (by Roblox or another program), so Roblox may have overwritten your settings.");

                if (problems.Count == 0)
                {
                    App.Logger.Info("Roblox settings lock: session ended normally");
                    return;
                }

                string report = string.Join("\n\n", problems);
                App.Logger.Warn($"Roblox settings lock: possible problems\n{report}");

                VargoApp.Settings.Prop.LastLockReport = report;
                VargoApp.Settings.Prop.LastLockReportUtc = DateTime.UtcNow;
                VargoApp.Settings.Save();

                var answer = await Frontend.ShowMessageBox(
                    "Vargostrap's \"Lock settings\" option is on, and the last Roblox session showed signs it may have caused a problem:\n\n"
                        + report
                        + "\n\nTurn the lock off? You can turn it back on any time from the Roblox Settings page.",
                    MessageBoxImage.Warning,
                    MessageBoxButton.YesNo);

                if (answer == MessageBoxResult.Yes)
                {
                    Disable(reason: "user, after session report");
                    ClearReport();
                }
            }

            private static List<string> FindSaveErrors(string? logFile)
            {
                List<string> matches = [];

                if (string.IsNullOrEmpty(logFile) || !File.Exists(logFile))
                    return matches;

                try
                {
                    // Roblox may still hold the file open
                    using var stream = new FileStream(logFile, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
                    using var reader = new StreamReader(stream);

                    while (reader.ReadLine() is string line && matches.Count < 3)
                    {
                        bool Has(string[] words) => words.Any(w => line.Contains(w, StringComparison.OrdinalIgnoreCase));

                        if (Has(SettingsWords) && Has(FailureWords) && !Has(IgnoreWords))
                            matches.Add(line.Length > 240 ? line[..240] + "…" : line);
                    }
                }
                catch (Exception ex)
                {
                    App.Logger.Warn($"Couldn't scan Roblox log for settings errors: {ex.Message}");
                }

                return matches;
            }
        }
    }
}
