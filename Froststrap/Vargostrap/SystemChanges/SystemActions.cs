// SPDX-FileCopyrightText: 2026 Vargostrap
//
// SPDX-License-Identifier: MPL-2.0

using System.Runtime.Versioning;
using Froststrap.Vargostrap.Native;
using Froststrap.Vargostrap.Performance;
using Microsoft.Win32;

namespace Froststrap.Vargostrap.SystemChanges
{
    /// <summary>
    /// The individual reversible system changes. Every Apply method journals the original state first,
    /// then makes the change, and removes the journal entry again if the change fails.
    /// Only standard Windows APIs, and never on Roblox's process, critical system processes or another
    /// user's processes.
    /// </summary>
    [SupportedOSPlatform("windows")]
    internal static class SystemActions
    {
        /// <summary>Prefix of a revert error meaning "the target no longer exists", so there's nothing to undo.</summary>
        public const string GonePrefix = "gone:";

        /// <summary>Processes Vargostrap never pauses or re-prioritizes, whatever the user's rules say.</summary>
        private static readonly HashSet<string> Protected = new(StringComparer.OrdinalIgnoreCase)
        {
            "System", "Idle", "Registry", "smss", "csrss", "wininit", "winlogon", "services", "lsass", "lsaiso",
            "svchost", "dwm", "explorer", "fontdrvhost", "sihost", "ctfmon", "RuntimeBroker", "taskhostw",
            "audiodg", "spoolsv", "MsMpEng", "NisSrv", "SecurityHealthService", "SecurityHealthSystray",
            "StartMenuExperienceHost", "ShellExperienceHost", "SearchHost", "TextInputHost", "conhost",
            "dllhost", "WmiPrvSE", "MemCompression", "LsaIso", "WUDFHost", "Vmmem",
            // Roblox itself and Vargostrap
            "RobloxPlayerBeta", "RobloxPlayerLauncher", "RobloxCrashHandler", "RobloxStudioBeta", Brand.AssemblyName,
        };

        public static bool IsProtected(Process p)
        {
            try
            {
                return Protected.Contains(p.ProcessName)
                    || p.Id == Environment.ProcessId
                    || p.SessionId != Process.GetCurrentProcess().SessionId; // other sessions = services / other users
            }
            catch
            {
                return true; // can't inspect it: leave it alone
            }
        }

        private static string NewId(string prefix = "") => prefix + Guid.NewGuid().ToString("N");

        private static long StartTicks(Process p)
        {
            try { return p.StartTime.ToUniversalTime().Ticks; }
            catch { return 0; }
        }

        private static Process? FindSame(int pid, long startTicks)
        {
            try
            {
                var p = Process.GetProcessById(pid);
                if (startTicks != 0 && StartTicks(p) != startTicks)
                {
                    p.Dispose();
                    return null; // PID was reused by a different process
                }
                return p;
            }
            catch (ArgumentException)
            {
                return null;
            }
        }

        // ---------------------------------------------------------------- processes (no admin)

        public static string? LowerPriority(ChangeJournal journal, Process p, ProcessPriorityClass target)
        {
            if (IsProtected(p)) return "protected process";

            ProcessPriorityClass original;
            try { original = p.PriorityClass; }
            catch (Exception ex) { return ex.Message; }

            if (original <= target && original != ProcessPriorityClass.Idle)
                return null; // already at or below the target; nothing to change

            var entry = new JournalEntry(NewId(), ChangeKind.ProcessPriority, p.ProcessName, ((int)original).ToString(CultureInfo.InvariantCulture),
                $"Lowered priority of {p.ProcessName} (PID {p.Id}) from {original} to {target}", DateTime.UtcNow, p.Id, StartTicks(p));

            journal.Add(entry);
            try
            {
                p.PriorityClass = target;
                return null;
            }
            catch (Exception ex)
            {
                journal.Remove(entry.Id);
                return ex.Message;
            }
        }

        public static unsafe string? Suspend(ChangeJournal journal, Process p)
        {
            if (IsProtected(p)) return "protected process";

            var entry = new JournalEntry(NewId(), ChangeKind.ProcessSuspend, p.ProcessName, null,
                $"Paused {p.ProcessName} (PID {p.Id})", DateTime.UtcNow, p.Id, StartTicks(p));

            journal.Add(entry);

            IntPtr h = Win32.OpenProcess(Win32.PROCESS_SUSPEND_RESUME, false, (uint)p.Id);
            if (h == IntPtr.Zero)
            {
                journal.Remove(entry.Id);
                return "access denied";
            }

            try
            {
                int status = Win32.NtSuspendProcess(h);
                if (status != 0)
                {
                    journal.Remove(entry.Id);
                    return $"NtSuspendProcess failed (0x{status:X8})";
                }
                return null;
            }
            finally
            {
                Win32.CloseHandle(h);
            }
        }

        // ---------------------------------------------------------------- registry

        /// <param name="hive">"HKCU" (no admin) or "HKLM" (admin)</param>
        public static string? SetRegistryValue(ChangeJournal journal, string hive, string key, string name, object value, RegistryValueKind kind, string description, string idPrefix = "")
        {
            try
            {
                using RegistryKey root = hive == "HKLM" ? Registry.LocalMachine : Registry.CurrentUser;

                object? existing;
                string? originalKind;
                bool keyExisted;
                using (RegistryKey? current = root.OpenSubKey(key))
                {
                    keyExisted = current is not null;
                    existing = current?.GetValue(name);
                    originalKind = existing is null ? null : current!.GetValueKind(name).ToString();
                }

                string? original = existing?.ToString();
                if (existing is not null && string.Equals(original, value.ToString(), StringComparison.Ordinal))
                    return null; // already set; don't journal (and don't remove it on undo)

                // journal before touching anything, including creating the key, so undo can also remove a key we created
                var entry = new JournalEntry(NewId(idPrefix), ChangeKind.RegistryValue, $@"{hive}\{key}", original, description,
                    DateTime.UtcNow, ValueKind: $"{originalKind ?? "absent"}|{name}|{(keyExisted ? "" : "createdKey")}");

                journal.Add(entry);
                try
                {
                    using RegistryKey sub = root.CreateSubKey(key, writable: true);
                    sub.SetValue(name, value, kind);
                    return null;
                }
                catch
                {
                    journal.Remove(entry.Id);
                    throw;
                }
            }
            catch (Exception ex)
            {
                return ex.Message;
            }
        }

        // ---------------------------------------------------------------- services (admin)

        public static unsafe string? StopService(ChangeJournal journal, string serviceName, string displayName)
        {
            IntPtr scm = Win32.OpenSCManager(null, null, Win32.SC_MANAGER_CONNECT);
            if (scm == IntPtr.Zero) return "can't open the service manager";

            try
            {
                IntPtr svc = Win32.OpenService(scm, serviceName, Win32.SERVICE_QUERY_STATUS | Win32.SERVICE_STOP);
                if (svc == IntPtr.Zero) return "can't open the service (admin required)";

                try
                {
                    Win32.SERVICE_STATUS status;
                    if (!Win32.QueryServiceStatus(svc, &status)) return "can't query the service";
                    if (status.dwCurrentState == Win32.SERVICE_STOPPED) return null; // nothing to pause

                    var entry = new JournalEntry(NewId(), ChangeKind.ServiceStop, serviceName, "running",
                        $"Paused the {displayName} service ({serviceName})", DateTime.UtcNow);
                    journal.Add(entry);

                    if (!Win32.ControlService(svc, Win32.SERVICE_CONTROL_STOP, &status))
                    {
                        journal.Remove(entry.Id);
                        return $"stop refused ({System.Runtime.InteropServices.Marshal.GetLastPInvokeError()})";
                    }

                    return null;
                }
                finally
                {
                    Win32.CloseServiceHandle(svc);
                }
            }
            finally
            {
                Win32.CloseServiceHandle(scm);
            }
        }

        private static string? StartService(string serviceName)
        {
            IntPtr scm = Win32.OpenSCManager(null, null, Win32.SC_MANAGER_CONNECT);
            if (scm == IntPtr.Zero) return "can't open the service manager";

            try
            {
                IntPtr svc = Win32.OpenService(scm, serviceName, Win32.SERVICE_START);
                if (svc == IntPtr.Zero) return "can't open the service (admin required)";

                try
                {
                    if (Win32.StartService(svc, 0, IntPtr.Zero))
                        return null;

                    int err = System.Runtime.InteropServices.Marshal.GetLastPInvokeError();
                    return err == 1056 ? null : $"start failed ({err})"; // 1056 = already running
                }
                finally
                {
                    Win32.CloseServiceHandle(svc);
                }
            }
            finally
            {
                Win32.CloseServiceHandle(scm);
            }
        }

        // ---------------------------------------------------------------- QoS policies (admin)

        public static string? AddQosPolicy(ChangeJournal journal, string policyName, string newNetQosPolicyArgs, string description)
        {
            var entry = new JournalEntry(NewId(), ChangeKind.QosPolicy, policyName, null, description, DateTime.UtcNow);
            journal.Add(entry);

            // replace any leftover policy of the same name first
            PowerShell.Run($"Remove-NetQosPolicy -Name '{policyName}' -PolicyStore ActiveStore -Confirm:$false -ErrorAction SilentlyContinue");
            var (ok, output) = PowerShell.Run($"New-NetQosPolicy -Name '{policyName}' -PolicyStore ActiveStore {newNetQosPolicyArgs} -ErrorAction Stop | Out-Null");

            if (!ok)
            {
                journal.Remove(entry.Id);
                return output;
            }

            return null;
        }

        // ---------------------------------------------------------------- power plan (no admin)

        public static string? SwitchPowerPlan(ChangeJournal journal, string targetGuid, string targetName)
        {
            string? current = PowerPlans.ActiveGuid();
            if (current is null) return "couldn't read the active power plan";
            if (string.Equals(current, targetGuid, StringComparison.OrdinalIgnoreCase)) return null; // already active

            var entry = new JournalEntry(NewId(), ChangeKind.PowerPlan, current, current,
                $"Switched the power plan to {targetName}", DateTime.UtcNow);
            journal.Add(entry);

            if (!PowerPlans.SetActive(targetGuid))
            {
                journal.Remove(entry.Id);
                return "powercfg refused the change";
            }
            return null;
        }

        // ---------------------------------------------------------------- undo

        public static string? Revert(JournalEntry e)
        {
            try
            {
                switch (e.Kind)
                {
                    case ChangeKind.ProcessPriority:
                    {
                        using Process? p = FindSame(e.ProcessId, e.ProcessStartTicks);
                        if (p is null) return GonePrefix + " process exited";
                        p.PriorityClass = (ProcessPriorityClass)int.Parse(e.Data!, CultureInfo.InvariantCulture);
                        return null;
                    }

                    case ChangeKind.ProcessSuspend:
                    {
                        using Process? p = FindSame(e.ProcessId, e.ProcessStartTicks);
                        if (p is null) return GonePrefix + " process exited";

                        IntPtr h = Win32.OpenProcess(Win32.PROCESS_SUSPEND_RESUME, false, (uint)p.Id);
                        if (h == IntPtr.Zero) return "access denied";
                        try
                        {
                            int status = Win32.NtResumeProcess(h);
                            return status == 0 ? null : $"NtResumeProcess failed (0x{status:X8})";
                        }
                        finally
                        {
                            Win32.CloseHandle(h);
                        }
                    }

                    case ChangeKind.RegistryValue:
                    {
                        string hive = e.Target[..4];
                        string key = e.Target[5..];
                        // "<original kind or absent>|<value name>|<createdKey or empty>"
                        string[] parts = e.ValueKind!.Split('|', 3);
                        string name = parts[1];
                        bool createdKey = parts.Length > 2 && parts[2] == "createdKey";

                        using RegistryKey root = hive == "HKLM" ? Registry.LocalMachine : Registry.CurrentUser;
                        bool keyNowEmpty;
                        using (RegistryKey? sub = root.OpenSubKey(key, writable: true))
                        {
                            if (sub is null) return null;

                            if (parts[0] == "absent")
                                sub.DeleteValue(name, throwOnMissingValue: false);
                            else
                            {
                                var kind = Enum.Parse<RegistryValueKind>(parts[0]);
                                object value = kind == RegistryValueKind.DWord ? int.Parse(e.Data!, CultureInfo.InvariantCulture) : e.Data!;
                                sub.SetValue(name, value, kind);
                            }

                            keyNowEmpty = sub.ValueCount == 0 && sub.SubKeyCount == 0;
                        }

                        // remove a key Vargostrap created, but only if nothing else has been put in it since
                        if (createdKey && keyNowEmpty)
                            root.DeleteSubKey(key, throwOnMissingSubKey: false);

                        return null;
                    }

                    case ChangeKind.PowerPlan:
                        return PowerPlans.SetActive(e.Data!) ? null : "powercfg refused the change";

                    case ChangeKind.ServiceStop:
                        return e.Data == "running" ? StartService(e.Target) : null;

                    case ChangeKind.QosPolicy:
                    {
                        // ActiveStore policies vanish on reboot, so "not there any more" is a normal outcome
                        var (ok, output) = PowerShell.Run(
                            $"if (Get-NetQosPolicy -Name '{e.Target}' -PolicyStore ActiveStore -ErrorAction SilentlyContinue) " +
                            $"{{ Remove-NetQosPolicy -Name '{e.Target}' -PolicyStore ActiveStore -Confirm:$false -ErrorAction Stop; 'removed' }} else {{ 'absent' }}");
                        if (!ok) return output;
                        return output.EndsWith("absent", StringComparison.Ordinal) ? GonePrefix + " policy already removed" : null;
                    }

                    default:
                        return $"unknown change kind {e.Kind}";
                }
            }
            catch (Exception ex)
            {
                return ex.Message;
            }
        }
    }

    /// <summary>Runs one Windows PowerShell command (used only by the elevated helper, for QoS policy cmdlets).</summary>
    [SupportedOSPlatform("windows")]
    internal static class PowerShell
    {
        public static (bool Ok, string Output) Run(string command)
        {
            try
            {
                var psi = new ProcessStartInfo
                {
                    FileName = Path.Combine(Environment.SystemDirectory, @"WindowsPowerShell\v1.0\powershell.exe"),
                    ArgumentList = { "-NoProfile", "-NonInteractive", "-ExecutionPolicy", "Bypass", "-Command", command },
                    UseShellExecute = false,
                    CreateNoWindow = true,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                };

                using Process p = Process.Start(psi)!;
                string stdout = p.StandardOutput.ReadToEnd();
                string stderr = p.StandardError.ReadToEnd();

                if (!p.WaitForExit(60_000))
                {
                    p.Kill();
                    return (false, "PowerShell timed out");
                }

                return (p.ExitCode == 0 && string.IsNullOrWhiteSpace(stderr), (stderr + stdout).Trim());
            }
            catch (Exception ex)
            {
                return (false, ex.Message);
            }
        }
    }
}
