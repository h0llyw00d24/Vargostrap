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

            int adminCount = PendingAdminChanges().Count;
            if (adminCount > 0)
            {
                bool started = ElevatedHelper.Start("revert", Paths.Base);
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
            if (s.PriorityEnabled || s.TrafficControlEnabled)
            {
                s.PriorityEnabled = false;
                s.TrafficControlEnabled = false;
                lines.Add("Priority management and background traffic control turned off.");
            }
            VargoApp.Settings.Save();

            App.Logger.Info("Safe-mode reset:\n" + string.Join("\n", lines));
            return string.Join("\n", lines);
        }
    }
}
