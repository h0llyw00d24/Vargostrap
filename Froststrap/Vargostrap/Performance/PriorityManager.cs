// SPDX-FileCopyrightText: 2026 Vargostrap
//
// SPDX-License-Identifier: MPL-2.0

using System.Runtime.Versioning;
using Froststrap.Vargostrap.Models;
using Froststrap.Vargostrap.Native;
using Froststrap.Vargostrap.SystemChanges;
using Microsoft.Win32;

namespace Froststrap.Vargostrap.Performance
{
    /// <summary>
    /// Priority management, applied from outside Roblox's process with standard Windows APIs only:
    /// the same calls Task Manager uses (priority class), plus the documented SetProcessInformation
    /// classes for power throttling (EcoQoS) and memory priority. Nothing touches Roblox's memory.
    ///
    /// Changes on the Roblox process end with it, so they need no undo. The GPU preference is a per-exe
    /// registry value, journaled and removed when Roblox closes. DSCP needs admin, so it is returned as
    /// part of the elevated helper's plan.
    /// Deliberately not included: Realtime priority, CPU affinity and core parking (can make things worse),
    /// and I/O priority (no documented API).
    /// </summary>
    [SupportedOSPlatform("windows")]
    internal static class PriorityManager
    {
        public const string GpuPreferencesKey = @"Software\Microsoft\DirectX\UserGpuPreferences";
        private const string QosNlaKey = @"SYSTEM\CurrentControlSet\Services\Tcpip\QoS";

        public static List<string> Apply(VargostrapSettings s, ChangeJournal journal, int robloxPid, List<AdminAction> adminPlan)
        {
            List<string> done = [];
            if (!s.PriorityEnabled)
                return done;

            Process roblox;
            try { roblox = Process.GetProcessById(robloxPid); }
            catch (ArgumentException) { return done; }

            using (roblox)
            {
                try
                {
                    ProcessPriorityClass target = s.RobloxPriority switch
                    {
                        RobloxPriorityLevel.High => ProcessPriorityClass.High,
                        RobloxPriorityLevel.AboveNormal => ProcessPriorityClass.AboveNormal,
                        _ => ProcessPriorityClass.Normal,
                    };

                    if (roblox.PriorityClass != target)
                    {
                        roblox.PriorityClass = target;
                        done.Add($"Roblox CPU priority: {target}");
                    }
                }
                catch (Exception ex) { App.Logger.Warn($"Couldn't set Roblox priority: {ex.Message}"); }

                if (s.PriorityDisableEcoQos && SetPowerThrottling(robloxPid, enabled: false))
                    done.Add("Roblox exempted from EcoQoS power throttling");

                if (s.PriorityMemory && SetMemoryPriorityNormal(robloxPid))
                    done.Add("Roblox memory priority: normal (Windows won't page it out early)");

                if (s.PriorityHighPerformanceGpu && GetExePath(robloxPid) is string exe)
                {
                    string? error = SystemActions.SetRegistryValue(journal, "HKCU", GpuPreferencesKey, exe, "GpuPreference=2;",
                        RegistryValueKind.String, $"Set Roblox ({Path.GetFileName(Path.GetDirectoryName(exe))}) to the high-performance GPU");
                    if (error is null) done.Add("Roblox set to the high-performance GPU (Windows graphics settings)");
                    else App.Logger.Warn($"GPU preference not set: {error}");
                }

                if (s.PriorityDscp)
                {
                    adminPlan.Add(new AdminAction(AdminActionKind.QosDscp, "RobloxPlayerBeta.exe", "Roblox"));
                    // without this, Windows ignores DSCP marking on PCs that aren't joined to a domain
                    adminPlan.Add(new AdminAction(AdminActionKind.RegistryHklm, "Do not use NLA",
                        "Allow Windows to apply DSCP tags on this (non-domain) PC", Key: QosNlaKey, Value: "1"));
                }
            }

            foreach (string line in done)
                App.Logger.Info($"Priority: {line}");

            return done;
        }

        /// <summary>
        /// Not Process.MainModule: that enumerates modules by reading the target's memory, which Vargostrap
        /// never does to Roblox. QueryFullProcessImageName needs only limited query access.
        /// </summary>
        private static unsafe string? GetExePath(int pid)
        {
            IntPtr h = Win32.OpenProcess(Win32.PROCESS_QUERY_LIMITED_INFORMATION, false, (uint)pid);
            if (h == IntPtr.Zero) return null;

            try
            {
                char* buffer = stackalloc char[1024];
                uint size = 1024;
                return Win32.QueryFullProcessImageName(h, 0, buffer, &size) ? new string(buffer, 0, (int)size) : null;
            }
            finally
            {
                Win32.CloseHandle(h);
            }
        }

        private static unsafe bool SetPowerThrottling(int pid, bool enabled)
        {
            IntPtr h = Win32.OpenProcess(Win32.PROCESS_SET_INFORMATION, false, (uint)pid);
            if (h == IntPtr.Zero) return false;

            try
            {
                var state = new Win32.PROCESS_POWER_THROTTLING_STATE
                {
                    Version = Win32.PROCESS_POWER_THROTTLING_CURRENT_VERSION,
                    ControlMask = Win32.PROCESS_POWER_THROTTLING_EXECUTION_SPEED,
                    StateMask = enabled ? Win32.PROCESS_POWER_THROTTLING_EXECUTION_SPEED : 0,
                };
                return Win32.SetProcessInformation(h, Win32.ProcessPowerThrottling, &state, (uint)sizeof(Win32.PROCESS_POWER_THROTTLING_STATE));
            }
            finally
            {
                Win32.CloseHandle(h);
            }
        }

        /// <summary>Normal (5) is the highest memory priority Windows allows; this stops it being lowered.</summary>
        private static unsafe bool SetMemoryPriorityNormal(int pid)
        {
            IntPtr h = Win32.OpenProcess(Win32.PROCESS_SET_INFORMATION, false, (uint)pid);
            if (h == IntPtr.Zero) return false;

            try
            {
                uint priority = Win32.MEMORY_PRIORITY_NORMAL;
                return Win32.SetProcessInformation(h, Win32.ProcessMemoryPriority, &priority, sizeof(uint));
            }
            finally
            {
                Win32.CloseHandle(h);
            }
        }

        /// <summary>What turning this on will do, for the preview dialog.</summary>
        public static List<string> Preview(VargostrapSettings s)
        {
            List<string> lines =
            [
                $"Set Roblox's CPU priority to {(s.RobloxPriority == RobloxPriorityLevel.High ? "High" : s.RobloxPriority == RobloxPriorityLevel.AboveNormal ? "Above normal" : "Normal")} (never Realtime)",
            ];
            if (s.PriorityDisableEcoQos) lines.Add("Exempt Roblox from Windows' EcoQoS power throttling");
            if (s.PriorityMemory) lines.Add("Keep Roblox's memory priority at normal");
            if (s.PriorityHighPerformanceGpu) lines.Add($@"Add a high-performance GPU entry for Roblox under HKCU\{GpuPreferencesKey} (removed when Roblox closes)");
            if (s.PriorityDscp) lines.Add(@"Admin (one UAC prompt per launch): create a DSCP 46 QoS policy for RobloxPlayerBeta.exe and set HKLM\SYSTEM\...\Tcpip\QoS ""Do not use NLA""=1 (both undone when Roblox closes)");
            return lines;
        }
    }
}
