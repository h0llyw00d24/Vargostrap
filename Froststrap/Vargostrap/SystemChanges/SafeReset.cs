// SPDX-FileCopyrightText: 2026 Vargostrap
//
// SPDX-License-Identifier: MPL-2.0

using System.Runtime.Versioning;
using Froststrap.Vargostrap.RobloxSettings;

namespace Froststrap.Vargostrap.SystemChanges
{
    /// <summary>
    /// Crash recovery (on every Vargostrap start) and the safe-mode reset (a button on the
    /// Ping &amp; Performance page). Both undo whatever the change journals still list.
    /// </summary>
    [SupportedOSPlatform("windows")]
    internal static class SafeReset
    {
        public static List<JournalEntry> PendingUserChanges() => SafeRead(ChangeJournal.User(Paths.Base));

        public static List<JournalEntry> PendingAdminChanges() => SafeRead(ChangeJournal.Admin(Paths.Base));

        public static List<JournalEntry> PersistentTweaks() =>
            [.. SafeRead(ChangeJournal.Persistent(Paths.Base)), .. SafeRead(ChangeJournal.PersistentAdmin(Paths.Base))];

        private static List<JournalEntry> SafeRead(ChangeJournal journal)
        {
            try { return journal.Read(); }
            catch (Exception ex) { App.Logger.Error(ex, "Change journal unreadable"); return []; }
        }

        /// <summary>
        /// Called once at startup. If a previous session didn't finish (crash or power loss) and Roblox
        /// isn't running now, undo its changes. Admin changes need a UAC prompt, so the user is asked first.
        /// </summary>
        public static async Task RecoverAtStartupAsync()
        {
            try
            {
                if (Utility.Processes.IsRobloxRunning())
                    return; // a live session owns these entries

                ChangeJournal user = ChangeJournal.User(Paths.Base);
                if (!user.IsEmpty)
                {
                    App.Logger.Warn("Undoing system changes left over from a session that didn't finish");
                    foreach (string failure in user.RevertAll(m => App.Logger.Info(m)))
                        App.Logger.Warn($"Undo failed: {failure}");
                }

                List<JournalEntry> admin = PendingAdminChanges();
                if (admin.Count == 0)
                    return;

                var answer = await Frontend.ShowMessageBox(
                    "A previous Roblox session ended before Vargostrap could undo these changes:\n\n"
                        + string.Join("\n", admin.Select(e => "• " + e.Description))
                        + "\n\nUndoing them needs administrator permission. Undo now?",
                    MessageBoxImage.Warning, MessageBoxButton.YesNo);

                if (answer == MessageBoxResult.Yes)
                    ElevatedHelper.Start("revert", Paths.Base);
            }
            catch (Exception ex)
            {
                App.Logger.Error(ex, "Startup recovery failed");
            }
        }

        /// <summary>
        /// Command-line safe reset (<c>Vargostrap.exe --vargostrap-reset</c>), for when the app itself won't
        /// start. Runs before any UI, shows the summary in a plain Windows message box, and exits.
        /// </summary>
        public static int RunFromCommandLine()
        {
            try
            {
                Paths.Initialize();
                string summary = ResetAll();
                Native.Win32.MessageBox(IntPtr.Zero, summary, $"{App.ProjectName} safe-mode reset", 0x40 /* MB_ICONINFORMATION */);
                return 0;
            }
            catch (Exception ex)
            {
                Native.Win32.MessageBox(IntPtr.Zero, $"Safe-mode reset failed:\n\n{ex.Message}", $"{App.ProjectName} safe-mode reset", 0x10 /* MB_ICONERROR */);
                return 1;
            }
        }

        /// <summary>
        /// Safe-mode reset: undo every pending system change, release the Roblox settings lock, and turn off
        /// the features that make system changes, so nothing is re-applied on the next launch.
        /// Returns a summary for the user.
        /// </summary>
        public static string ResetAll()
        {
            List<string> lines = [];

            if (Utility.Processes.IsRobloxRunning())
                lines.Add("Roblox is running. Its current session's changes will also be undone when it closes.");

            ChangeJournal user = ChangeJournal.User(Paths.Base);
            int userCount = PendingUserChanges().Count;
            List<string> failures = user.RevertAll(m => App.Logger.Info(m));
            lines.Add(userCount == 0 ? "No pending app changes." : $"Undid {userCount - failures.Count} of {userCount} app change(s).");
            lines.AddRange(failures.Select(f => "  Couldn't undo: " + f));

            ChangeJournal persistent = ChangeJournal.Persistent(Paths.Base);
            int persistentCount = SafeRead(persistent).Count;
            if (persistentCount > 0)
            {
                List<string> pFailures = persistent.RevertAll(m => App.Logger.Info(m));
                lines.Add($"Put back {persistentCount - pFailures.Count} of {persistentCount} Windows setting(s) (Game Mode, Game Bar).");
                lines.AddRange(pFailures.Select(f => "  Couldn't undo: " + f));
            }

            int adminCount = PendingAdminChanges().Count + SafeRead(ChangeJournal.PersistentAdmin(Paths.Base)).Count;
            if (adminCount > 0)
            {
                bool started = ElevatedHelper.Start("revert-all", Paths.Base);
                lines.Add(started
                    ? $"Undoing {adminCount} admin change(s) (Windows asked for permission)."
                    : $"{adminCount} admin change(s) still pending: permission was declined. Run the reset again to retry, or restart Windows (QoS policies clear on restart).");
            }

            if (SettingsLock.IsLocked || VargoApp.Settings.Prop.RobloxSettingsLockEnabled)
            {
                SettingsLock.Disable(reason: "safe-mode reset");
                lines.Add("Roblox settings unlocked.");
            }

            var s = VargoApp.Settings.Prop;
            if (s.PriorityEnabled || s.TrafficControlEnabled || s.PowerPlanDuringPlay)
            {
                s.PriorityEnabled = false;
                s.TrafficControlEnabled = false;
                s.PowerPlanDuringPlay = false;
                lines.Add("Priority management, background traffic control and the power-plan switch turned off.");
            }
            VargoApp.Settings.Save();

            App.Logger.Info("Safe-mode reset:\n" + string.Join("\n", lines));
            return string.Join("\n", lines);
        }
    }
}
