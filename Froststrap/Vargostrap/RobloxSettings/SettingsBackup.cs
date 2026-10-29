// SPDX-FileCopyrightText: 2026 Vargostrap
//
// SPDX-License-Identifier: MPL-2.0

namespace Froststrap.Vargostrap.RobloxSettings
{
    /// <summary>
    /// One-time backup of Roblox's GlobalBasicSettings file, taken right before Vargostrap first writes to it
    /// (hooked into <see cref="GBSEditor.Save"/> and <see cref="GBSEditor.ImportSettings"/>), plus restore.
    /// The backup is never overwritten, so it always holds the file as it was before Vargostrap touched it.
    /// </summary>
    internal static class SettingsBackup
    {
        public static string BackupDirectory => Path.Combine(Paths.Base, "Backups");

        public static string BackupPath => Path.Combine(BackupDirectory, Path.GetFileNameWithoutExtension(GBSEditor.FileLocation) + ".original.xml");

        public static bool Exists => File.Exists(BackupPath);

        public static DateTime? CreatedAt => Exists ? File.GetCreationTime(BackupPath) : null;

        /// <summary>Copies the current settings file to the backup location if no backup exists yet. Never throws.</summary>
        public static void EnsureBackup()
        {
            try
            {
                if (Exists || !File.Exists(GBSEditor.FileLocation))
                    return;

                Directory.CreateDirectory(BackupDirectory);
                File.Copy(GBSEditor.FileLocation, BackupPath);

                // the copy inherits the read-only attribute; the backup itself shouldn't be locked
                File.SetAttributes(BackupPath, File.GetAttributes(BackupPath) & ~FileAttributes.ReadOnly);

                App.Logger.Info($"Backed up Roblox settings to {BackupPath}");
            }
            catch (Exception ex)
            {
                App.Logger.Error($"Could not back up Roblox settings: {ex.Message}");
            }
        }

        /// <summary>
        /// Puts the original file back, removes the read-only lock (the original wasn't locked) and reloads
        /// <see cref="App.GlobalSettings"/>. Returns an error message, or null on success.
        /// </summary>
        public static string? Restore()
        {
            if (!Exists)
                return "There is no backup yet. Vargostrap makes one automatically the first time it changes your Roblox settings.";

            if (Utility.Processes.IsRobloxRunning())
                return "Close Roblox first. Roblox rewrites this file when it exits and would undo the restore.";

            try
            {
                SettingsLock.Disable(reason: "restore");

                File.Copy(BackupPath, GBSEditor.FileLocation, overwrite: true);
                App.GlobalSettings.Load();

                App.Logger.Info("Restored Roblox settings from backup");
                return null;
            }
            catch (Exception ex)
            {
                App.Logger.Error(ex, "Restoring Roblox settings failed");
                return ex.Message;
            }
        }
    }
}
