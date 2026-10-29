// SPDX-FileCopyrightText: 2026 Vargostrap
//
// SPDX-License-Identifier: MPL-2.0

namespace Froststrap.Vargostrap.Performance
{
    internal sealed record CleanupCategory(string Name, string Description, string Folder, List<string> Files, long Bytes)
    {
        public string Summary => Files.Count == 0
            ? "Nothing to clean"
            : $"{Files.Count:N0} file{(Files.Count == 1 ? "" : "s")}, {FormatSize(Bytes)}";

        public static string FormatSize(long bytes) => bytes switch
        {
            >= 1L << 30 => $"{bytes / (double)(1L << 30):0.0} GB",
            >= 1L << 20 => $"{bytes / (double)(1L << 20):0.0} MB",
            >= 1L << 10 => $"{bytes / 1024.0:0} KB",
            _ => $"{bytes} bytes",
        };
    }

    /// <summary>
    /// On-demand cleanup of Roblox's own leftovers, with a preview: old log files, the downloaded-asset
    /// cache, and Roblox's temp folder. Only files listed in the preview are deleted; anything in use is
    /// skipped. Roblox re-downloads cached assets as needed, so the first game after a cleanup can load a
    /// little slower. (Upstream's scheduled Cleaner on the Bootstrapper page is separate and unchanged.)
    /// </summary>
    internal static class RobloxCleanup
    {
        public static List<CleanupCategory> Preview(int logsOlderThanDays)
        {
            DateTime cutoff = DateTime.Now.AddDays(-Math.Max(0, logsOlderThanDays));

            List<CleanupCategory> categories =
            [
                Scan("Old Roblox logs", $"Log files older than {logsOlderThanDays} day(s). Recent logs are kept because Vargostrap reads the current one.",
                    Paths.RobloxLogs, f => f.LastWriteTime < cutoff),
                Scan("Roblox download cache and temp files", "Cached game assets (textures, sounds, meshes) and temporary files. Roblox downloads them again as needed.",
                    Paths.RobloxCache, _ => true),
            ];

            // on Windows the cache *is* %TEMP%\Roblox; elsewhere the temp folder is separate
            string temp = Path.Combine(Path.GetTempPath(), "Roblox");
            if (!string.Equals(Path.GetFullPath(temp).TrimEnd('\\', '/'), Path.GetFullPath(Paths.RobloxCache).TrimEnd('\\', '/'), StringComparison.OrdinalIgnoreCase))
                categories.Add(Scan("Roblox temp files", "Temporary files Roblox leaves in the system temp folder.", temp, _ => true));

            return categories;
        }

        private static CleanupCategory Scan(string name, string description, string folder, Func<FileInfo, bool> include)
        {
            List<string> files = [];
            long bytes = 0;

            try
            {
                if (!string.IsNullOrEmpty(folder) && Directory.Exists(folder))
                {
                    foreach (string path in Directory.EnumerateFiles(folder, "*", new EnumerationOptions { RecurseSubdirectories = true, IgnoreInaccessible = true }))
                    {
                        var info = new FileInfo(path);
                        if (!include(info)) continue;
                        files.Add(path);
                        bytes += info.Length;
                    }
                }
            }
            catch (Exception ex)
            {
                App.Logger.Warn($"Cleanup scan of {folder} failed: {ex.Message}");
            }

            return new CleanupCategory(name, description, folder, files, bytes);
        }

        /// <summary>Deletes exactly the previewed files. Returns (deleted count, freed bytes, skipped count).</summary>
        public static (int Deleted, long Freed, int Skipped) Clean(IEnumerable<CleanupCategory> categories)
        {
            int deleted = 0, skipped = 0;
            long freed = 0;

            foreach (CleanupCategory c in categories)
            {
                foreach (string path in c.Files)
                {
                    try
                    {
                        long size = new FileInfo(path).Length;
                        File.Delete(path);
                        deleted++;
                        freed += size;
                    }
                    catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
                    {
                        skipped++; // in use (Roblox running) or protected
                    }
                }

                RemoveEmptyFolders(c.Folder);
            }

            App.Logger.Info($"Roblox cleanup: deleted {deleted} files ({CleanupCategory.FormatSize(freed)}), skipped {skipped}");
            return (deleted, freed, skipped);
        }

        private static void RemoveEmptyFolders(string root)
        {
            if (string.IsNullOrEmpty(root) || !Directory.Exists(root)) return;

            foreach (string dir in Directory.EnumerateDirectories(root, "*", new EnumerationOptions { RecurseSubdirectories = true, IgnoreInaccessible = true })
                         .OrderByDescending(d => d.Length))
            {
                try
                {
                    if (!Directory.EnumerateFileSystemEntries(dir).Any())
                        Directory.Delete(dir);
                }
                catch { /* in use */ }
            }
        }
    }
}
