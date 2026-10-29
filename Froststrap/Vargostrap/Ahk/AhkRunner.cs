// SPDX-FileCopyrightText: 2026 Vargostrap
//
// SPDX-License-Identifier: MPL-2.0

using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using Froststrap.Vargostrap.Models;
using Froststrap.Vargostrap.Native;

namespace Froststrap.Vargostrap.Ahk
{
    /// <summary>
    /// Runs the user's chosen AutoHotkey scripts for one Roblox session. Vargostrap only starts the script
    /// files the user picked, as separate processes. It never injects into Roblox, reads or writes its
    /// memory, or generates scripts.
    ///
    /// Every script process is started suspended and put into a Windows job object with
    /// "kill on job close" before it runs, so the script and anything it starts end when Roblox closes,
    /// and also if Vargostrap itself exits or crashes.
    /// </summary>
    [SupportedOSPlatform("windows")]
    internal sealed class AhkRunner : IDisposable
    {
        private IntPtr _job;
        private readonly List<string> _started = [];

        public IReadOnlyList<string> Started => _started;

        private AhkRunner(IntPtr job) => _job = job;

        /// <summary>Starts every enabled script. Returns null when there's nothing to run.</summary>
        public static AhkRunner? StartFor(VargostrapSettings settings)
        {
            List<string> scripts = [.. settings.AhkScripts.Where(s => s.Enabled).Select(s => s.Path)];
            if (!settings.AhkEnabled || scripts.Count == 0)
                return null;

            AhkInstallation? ahk = AhkInstallation.Find();
            if (ahk is null)
            {
                App.Logger.Warn("AutoHotkey scripts are enabled but AutoHotkey isn't installed; skipping");
                return null;
            }

            IntPtr job = CreateKillOnCloseJob();
            if (job == IntPtr.Zero)
                return null;

            var runner = new AhkRunner(job);

            foreach (string script in scripts)
            {
                if (!File.Exists(script))
                {
                    App.Logger.Warn($"AutoHotkey script not found, skipping: {script}");
                    continue;
                }

                if (runner.Launch(ahk.BuildCommandLine(script), Path.GetDirectoryName(script)))
                {
                    runner._started.Add(script);
                    App.Logger.Info($"Started AutoHotkey script: {script}");
                }
            }

            return runner;
        }

        private static unsafe IntPtr CreateKillOnCloseJob()
        {
            IntPtr job = Win32.CreateJobObject(IntPtr.Zero, null);
            if (job == IntPtr.Zero)
            {
                App.Logger.Error($"CreateJobObject failed ({Marshal.GetLastPInvokeError()})");
                return IntPtr.Zero;
            }

            var info = new Win32.JOBOBJECT_EXTENDED_LIMIT_INFORMATION();
            info.BasicLimitInformation.LimitFlags = Win32.JOB_OBJECT_LIMIT_KILL_ON_JOB_CLOSE;

            if (!Win32.SetInformationJobObject(job, Win32.JobObjectExtendedLimitInformation, &info, (uint)sizeof(Win32.JOBOBJECT_EXTENDED_LIMIT_INFORMATION)))
            {
                App.Logger.Error($"SetInformationJobObject failed ({Marshal.GetLastPInvokeError()})");
                Win32.CloseHandle(job);
                return IntPtr.Zero;
            }

            return job;
        }

        private unsafe bool Launch(string commandLine, string? workingDirectory)
        {
            var startup = new Win32.STARTUPINFOW { cb = (uint)sizeof(Win32.STARTUPINFOW) };
            Win32.PROCESS_INFORMATION process;

            // CreateProcessW may write to the command-line buffer, so it must be a mutable copy
            char[] buffer = [.. commandLine, '\0'];

            fixed (char* cmd = buffer)
            {
                if (!Win32.CreateProcess(null, cmd, IntPtr.Zero, IntPtr.Zero, false, Win32.CREATE_SUSPENDED,
                        IntPtr.Zero, workingDirectory, &startup, &process))
                {
                    App.Logger.Error($"Couldn't start AutoHotkey ({Marshal.GetLastPInvokeError()}): {commandLine}");
                    return false;
                }
            }

            try
            {
                // assign before the first instruction runs, so even processes it spawns land in the job
                if (!Win32.AssignProcessToJobObject(_job, process.hProcess))
                {
                    App.Logger.Error($"AssignProcessToJobObject failed ({Marshal.GetLastPInvokeError()}); not running {commandLine}");
                    using var p = Process.GetProcessById((int)process.dwProcessId);
                    p.Kill();
                    return false;
                }

                Win32.ResumeThread(process.hThread);
                return true;
            }
            finally
            {
                Win32.CloseHandle(process.hThread);
                Win32.CloseHandle(process.hProcess);
            }
        }

        /// <summary>Ends every script started for this session (and anything they started).</summary>
        public void Dispose()
        {
            if (_job == IntPtr.Zero)
                return;

            Win32.TerminateJobObject(_job, 0);
            Win32.CloseHandle(_job);
            _job = IntPtr.Zero;

            if (_started.Count > 0)
                App.Logger.Info($"Closed {_started.Count} AutoHotkey script(s)");
        }
    }
}
