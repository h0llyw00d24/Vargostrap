// SPDX-FileCopyrightText: 2026 Vargostrap
//
// SPDX-License-Identifier: MPL-2.0

using System.Runtime.Versioning;

namespace Froststrap.Vargostrap.SystemChanges
{
    /// <summary>
    /// Creates a Windows System Restore point before Vargostrap's first admin changes of the day.
    /// Windows itself allows only one restore point per 24 hours by default and needs System Protection
    /// turned on for the system drive; if either stops it, the change journal is still the undo.
    /// </summary>
    [SupportedOSPlatform("windows")]
    internal static class RestorePoint
    {
        private static readonly TimeSpan MinInterval = TimeSpan.FromHours(24);

        public static void CreateIfDue(string baseDir, Action<string> log)
        {
            string marker = Path.Combine(baseDir, "LastRestorePoint.txt");

            if (File.Exists(marker) && DateTime.UtcNow - File.GetLastWriteTimeUtc(marker) < MinInterval)
            {
                log("Restore point: one was created in the last 24 hours, skipping.");
                return;
            }

            var (ok, output) = PowerShell.Run(
                "Checkpoint-Computer -Description 'Vargostrap: before system changes' -RestorePointType 'MODIFY_SETTINGS' -ErrorAction Stop");

            if (ok)
            {
                File.WriteAllText(marker, DateTime.UtcNow.ToString("o", CultureInfo.InvariantCulture));
                log("Restore point created.");
            }
            else
            {
                log($"Restore point not created (System Protection may be off, or Windows' 24-hour limit applies): {output}");
            }
        }

        public static DateTime? LastCreated(string baseDir)
        {
            string marker = Path.Combine(baseDir, "LastRestorePoint.txt");
            return File.Exists(marker) ? File.GetLastWriteTime(marker) : null;
        }
    }
}
