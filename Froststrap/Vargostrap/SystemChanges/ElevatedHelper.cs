// SPDX-FileCopyrightText: 2026 Vargostrap
//
// SPDX-License-Identifier: MPL-2.0

using System.ComponentModel;
using System.Runtime.Versioning;
using System.Security.Principal;
using Microsoft.Win32;

namespace Froststrap.Vargostrap.SystemChanges
{
    internal enum AdminActionKind { ServiceStop, QosThrottle, QosDscp, RegistryHklm, RegistryHklmDword }

    /// <summary>One change that needs admin rights, planned by the normal-user process and carried out by the helper.</summary>
    /// <param name="Group">Journal id prefix, for persistent tweaks that are undone individually (e.g. "nicpower:").</param>
    internal sealed record AdminAction(AdminActionKind Kind, string Target, string Display, long Number = 0, string? Key = null, string? Value = null, string Group = "")
    {
        public string Describe() => Kind switch
        {
            AdminActionKind.ServiceStop => $"Pause the {Display} service ({Target}) until Roblox closes",
            AdminActionKind.QosThrottle => $"Limit {Display}'s uploads to {Number / 1_000_000.0:0.#} Mbit/s until Roblox closes (Windows QoS policy)",
            AdminActionKind.QosDscp => $"Tag Roblox's network traffic as high priority (DSCP 46) until Roblox closes (Windows QoS policy)",
            AdminActionKind.RegistryHklm or AdminActionKind.RegistryHklmDword => Display,
            _ => Kind.ToString(),
        };
    }

    /// <summary>
    /// Admin-only changes run in a separate, short-lived copy of Vargostrap started with UAC
    /// (<c>Vargostrap.exe --vargostrap-elevated …</c>). The main app never runs elevated.
    ///
    /// session mode: undo leftovers → restore point (max once a day) → apply the plan → wait for Roblox
    /// to exit → undo everything → exit. Because the helper itself waits for Roblox, the undo happens even
    /// if the main Vargostrap process crashes. If the helper dies too, the next Vargostrap start offers to
    /// undo (and QoS policies are in Windows' non-persistent store, so a reboot clears them regardless).
    ///
    /// revert mode: undo whatever the admin journal still lists (crash recovery and the safe-mode reset).
    ///
    /// persist / unpersist modes: apply or undo persistent admin tweaks (e.g. adapter power saving), kept in
    /// a separate journal that is never undone automatically.
    /// </summary>
    [SupportedOSPlatform("windows")]
    internal static class ElevatedHelper
    {
        public const string Argument = "--vargostrap-elevated";

        public static bool IsElevated
        {
            get
            {
                using var identity = WindowsIdentity.GetCurrent();
                return new WindowsPrincipal(identity).IsInRole(WindowsBuiltInRole.Administrator);
            }
        }

        // ------------------------------------------------------------------ main-app side

        /// <summary>Starts the helper with a UAC prompt. Returns false if the user declined or it couldn't start.</summary>
        public static bool Start(params string[] args)
        {
            try
            {
                var psi = new ProcessStartInfo
                {
                    FileName = Environment.ProcessPath!,
                    Arguments = string.Join(' ', new[] { Argument }.Concat(args).Select(a => $"\"{a}\"")),
                    Verb = "runas",
                    UseShellExecute = true,
                };

                Process.Start(psi);
                return true;
            }
            catch (Win32Exception ex) when (ex.NativeErrorCode == 1223)
            {
                App.Logger.Warn("Admin prompt declined; admin-only changes skipped");
                return false;
            }
            catch (Exception ex)
            {
                App.Logger.Error(ex, "Couldn't start the elevated helper");
                return false;
            }
        }

        public static string WritePlan(string baseDir, IReadOnlyList<AdminAction> actions, string fileName = "AdminPlan.json")
        {
            string path = Path.Combine(baseDir, fileName);

            using var stream = new MemoryStream();
            using (var w = new Utf8JsonWriter(stream, new JsonWriterOptions { Indented = true }))
            {
                w.WriteStartArray();
                foreach (AdminAction a in actions)
                {
                    w.WriteStartObject();
                    w.WriteString("kind", a.Kind.ToString());
                    w.WriteString("target", a.Target);
                    w.WriteString("display", a.Display);
                    w.WriteNumber("number", a.Number);
                    if (a.Key is not null) w.WriteString("key", a.Key);
                    if (a.Value is not null) w.WriteString("value", a.Value);
                    if (a.Group.Length > 0) w.WriteString("group", a.Group);
                    w.WriteEndObject();
                }
                w.WriteEndArray();
            }

            File.WriteAllBytes(path, stream.ToArray());
            return path;
        }

        // ------------------------------------------------------------------ helper side

        /// <summary>Entry point, called from Program.Main before anything else when the first argument is <see cref="Argument"/>.</summary>
        public static int Run(string[] args)
        {
            if (args.Length < 3)
                return 2;

            string mode = args[1];
            string baseDir = args[2];
            var log = new HelperLog(baseDir);

            try
            {
                if (!IsElevated)
                {
                    log.Write("Not elevated; refusing to run.");
                    return 3;
                }

                ChangeJournal journal = ChangeJournal.Admin(baseDir);

                if (mode is "revert" or "revert-all")
                {
                    List<string> failures = journal.RevertAll(log.Write);

                    // revert-all (safe-mode reset) also undoes persistent admin tweaks
                    if (mode == "revert-all")
                        failures.AddRange(ChangeJournal.PersistentAdmin(baseDir).RevertAll(log.Write));

                    log.Write(failures.Count == 0 ? "Revert complete." : $"Revert finished with {failures.Count} failure(s).");
                    return failures.Count;
                }

                if (mode == "persist" && args.Length >= 4)
                {
                    ChangeJournal persistent = ChangeJournal.PersistentAdmin(baseDir);
                    RestorePoint.CreateIfDue(baseDir, log.Write);

                    int failed = 0;
                    foreach (AdminAction action in ReadPlan(args[3]))
                    {
                        string? error = Apply(persistent, action);
                        if (error is not null) failed++;
                        log.Write(error is null ? $"Applied (persistent): {action.Describe()}" : $"Skipped: {action.Describe()}: {error}");
                    }
                    return failed;
                }

                if (mode == "unpersist" && args.Length >= 4)
                {
                    string group = args[3];
                    List<string> failures = ChangeJournal.PersistentAdmin(baseDir)
                        .RevertWhere(e => group == "*" || e.Id.StartsWith(group, StringComparison.Ordinal), log.Write);
                    log.Write(failures.Count == 0 ? $"Persistent tweaks '{group}' undone." : $"{failures.Count} persistent undo failure(s).");
                    return failures.Count;
                }

                if (mode == "session" && args.Length >= 5 && int.TryParse(args[3], out int robloxPid))
                {
                    // anything left from a session that never finished
                    journal.RevertAll(log.Write);

                    RestorePoint.CreateIfDue(baseDir, log.Write);

                    foreach (AdminAction action in ReadPlan(args[4]))
                    {
                        string? error = Apply(journal, action);
                        log.Write(error is null ? $"Applied: {action.Describe()}" : $"Skipped: {action.Describe()}: {error}");
                    }

                    WaitForExit(robloxPid, log);

                    List<string> failures = journal.RevertAll(log.Write);
                    log.Write(failures.Count == 0 ? "Session ended; everything undone." : $"Session ended; {failures.Count} undo failure(s).");
                    return failures.Count;
                }

                log.Write($"Unknown helper mode '{mode}'.");
                return 2;
            }
            catch (Exception ex)
            {
                log.Write($"Helper error: {ex}");
                return 1;
            }
        }

        private static void WaitForExit(int pid, HelperLog log)
        {
            try
            {
                using Process roblox = Process.GetProcessById(pid);
                log.Write($"Waiting for Roblox (PID {pid}) to close...");
                roblox.WaitForExit();
            }
            catch (ArgumentException)
            {
                log.Write("Roblox already closed.");
            }
        }

        private static string? Apply(ChangeJournal journal, AdminAction a) => a.Kind switch
        {
            AdminActionKind.ServiceStop => SystemActions.StopService(journal, a.Target, a.Display),
            AdminActionKind.QosThrottle => SystemActions.AddQosPolicy(journal, $"Vargostrap throttle {a.Target}",
                $"-AppPathNameMatchCondition '{a.Target}' -ThrottleRateActionBitsPerSecond {a.Number} -NetworkProfile All", a.Describe()),
            AdminActionKind.QosDscp => SystemActions.AddQosPolicy(journal, "Vargostrap Roblox DSCP",
                $"-AppPathNameMatchCondition '{a.Target}' -DSCPAction 46 -NetworkProfile All", a.Describe()),
            AdminActionKind.RegistryHklm => SystemActions.SetRegistryValue(journal, "HKLM", a.Key!, a.Target, a.Value!, RegistryValueKind.String, a.Display, a.Group),
            AdminActionKind.RegistryHklmDword => SystemActions.SetRegistryValue(journal, "HKLM", a.Key!, a.Target, (int)a.Number, RegistryValueKind.DWord, a.Display, a.Group),
            _ => "unknown action",
        };

        private static List<AdminAction> ReadPlan(string path)
        {
            List<AdminAction> actions = [];
            using JsonDocument doc = JsonDocument.Parse(File.ReadAllText(path));

            foreach (JsonElement e in doc.RootElement.EnumerateArray())
            {
                actions.Add(new AdminAction(
                    Enum.Parse<AdminActionKind>(e.GetProperty("kind").GetString()!),
                    e.GetProperty("target").GetString()!,
                    e.GetProperty("display").GetString()!,
                    e.GetProperty("number").GetInt64(),
                    e.TryGetProperty("key", out JsonElement k) ? k.GetString() : null,
                    e.TryGetProperty("value", out JsonElement v) ? v.GetString() : null,
                    e.TryGetProperty("group", out JsonElement g) ? g.GetString() ?? "" : ""));
            }

            return actions;
        }

        /// <summary>The helper runs without the app's logging setup, so it keeps its own small log.</summary>
        private sealed class HelperLog(string baseDir)
        {
            private readonly string _path = Path.Combine(baseDir, "Logs", "elevated-helper.log");

            public void Write(string message)
            {
                try
                {
                    Directory.CreateDirectory(Path.GetDirectoryName(_path)!);
                    File.AppendAllText(_path, $"{DateTime.Now:yyyy-MM-dd HH:mm:ss} {message}{Environment.NewLine}");
                }
                catch { /* logging must never break the undo */ }
            }
        }
    }
}
