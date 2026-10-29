// SPDX-FileCopyrightText: 2026 Vargostrap
//
// SPDX-License-Identifier: MPL-2.0

using System.Runtime.InteropServices;
using System.Runtime.Versioning;

namespace Froststrap.Vargostrap.Native
{
    /// <summary>
    /// The few Win32 calls Vargostrap needs (job objects, suspended process creation, file associations).
    /// Source-generated LibraryImport, so it's Native AOT safe and doesn't touch upstream's NativeMethods.txt.
    /// </summary>
    [SupportedOSPlatform("windows")]
    internal static unsafe partial class Win32
    {
        public const uint CREATE_SUSPENDED = 0x00000004;
        public const uint JOB_OBJECT_LIMIT_KILL_ON_JOB_CLOSE = 0x00002000;
        public const int JobObjectExtendedLimitInformation = 9;
        public const uint ASSOCSTR_COMMAND = 1;
        public const uint ASSOCF_NONE = 0;

        [StructLayout(LayoutKind.Sequential)]
        internal struct STARTUPINFOW
        {
            public uint cb;
            public IntPtr lpReserved, lpDesktop, lpTitle;
            public uint dwX, dwY, dwXSize, dwYSize, dwXCountChars, dwYCountChars, dwFillAttribute, dwFlags;
            public ushort wShowWindow, cbReserved2;
            public IntPtr lpReserved2, hStdInput, hStdOutput, hStdError;
        }

        [StructLayout(LayoutKind.Sequential)]
        internal struct PROCESS_INFORMATION
        {
            public IntPtr hProcess, hThread;
            public uint dwProcessId, dwThreadId;
        }

        [StructLayout(LayoutKind.Sequential)]
        internal struct JOBOBJECT_BASIC_LIMIT_INFORMATION
        {
            public long PerProcessUserTimeLimit, PerJobUserTimeLimit;
            public uint LimitFlags;
            public UIntPtr MinimumWorkingSetSize, MaximumWorkingSetSize;
            public uint ActiveProcessLimit;
            public UIntPtr Affinity;
            public uint PriorityClass, SchedulingClass;
        }

        [StructLayout(LayoutKind.Sequential)]
        internal struct IO_COUNTERS
        {
            public ulong ReadOperationCount, WriteOperationCount, OtherOperationCount, ReadTransferCount, WriteTransferCount, OtherTransferCount;
        }

        [StructLayout(LayoutKind.Sequential)]
        internal struct JOBOBJECT_EXTENDED_LIMIT_INFORMATION
        {
            public JOBOBJECT_BASIC_LIMIT_INFORMATION BasicLimitInformation;
            public IO_COUNTERS IoInfo;
            public UIntPtr ProcessMemoryLimit, JobMemoryLimit, PeakProcessMemoryUsed, PeakJobMemoryUsed;
        }

        [LibraryImport("kernel32.dll", EntryPoint = "CreateJobObjectW", SetLastError = true, StringMarshalling = StringMarshalling.Utf16)]
        public static partial IntPtr CreateJobObject(IntPtr lpJobAttributes, string? lpName);

        [LibraryImport("kernel32.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        public static partial bool SetInformationJobObject(IntPtr hJob, int infoClass, void* lpInfo, uint cbInfo);

        [LibraryImport("kernel32.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        public static partial bool AssignProcessToJobObject(IntPtr hJob, IntPtr hProcess);

        [LibraryImport("kernel32.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        public static partial bool TerminateJobObject(IntPtr hJob, uint exitCode);

        [LibraryImport("kernel32.dll", EntryPoint = "CreateProcessW", SetLastError = true, StringMarshalling = StringMarshalling.Utf16)]
        [return: MarshalAs(UnmanagedType.Bool)]
        public static partial bool CreateProcess(
            string? lpApplicationName, char* lpCommandLine, IntPtr lpProcessAttributes, IntPtr lpThreadAttributes,
            [MarshalAs(UnmanagedType.Bool)] bool bInheritHandles, uint dwCreationFlags, IntPtr lpEnvironment,
            string? lpCurrentDirectory, STARTUPINFOW* lpStartupInfo, PROCESS_INFORMATION* lpProcessInformation);

        [LibraryImport("kernel32.dll", SetLastError = true)]
        public static partial uint ResumeThread(IntPtr hThread);

        [LibraryImport("kernel32.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        public static partial bool CloseHandle(IntPtr handle);

        [LibraryImport("shlwapi.dll", EntryPoint = "AssocQueryStringW", StringMarshalling = StringMarshalling.Utf16)]
        public static partial int AssocQueryString(uint flags, uint str, string pszAssoc, string? pszExtra, char* pszOut, uint* pcchOut);
        // --- processes ---

        public const uint PROCESS_SET_INFORMATION = 0x0200;
        public const uint PROCESS_SUSPEND_RESUME = 0x0800;
        public const uint PROCESS_QUERY_LIMITED_INFORMATION = 0x1000;

        public const int ProcessMemoryPriority = 0;
        public const int ProcessPowerThrottling = 4;
        public const uint MEMORY_PRIORITY_NORMAL = 5;
        public const uint PROCESS_POWER_THROTTLING_CURRENT_VERSION = 1;
        public const uint PROCESS_POWER_THROTTLING_EXECUTION_SPEED = 1;

        [StructLayout(LayoutKind.Sequential)]
        internal struct PROCESS_POWER_THROTTLING_STATE
        {
            public uint Version, ControlMask, StateMask;
        }

        [LibraryImport("kernel32.dll", SetLastError = true)]
        public static partial IntPtr OpenProcess(uint access, [MarshalAs(UnmanagedType.Bool)] bool inherit, uint pid);

        [LibraryImport("kernel32.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        public static partial bool SetProcessInformation(IntPtr hProcess, int infoClass, void* info, uint size);

        /// <summary>Exe path of a process using only PROCESS_QUERY_LIMITED_INFORMATION (no memory reads).</summary>
        [LibraryImport("kernel32.dll", EntryPoint = "QueryFullProcessImageNameW", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        public static partial bool QueryFullProcessImageName(IntPtr hProcess, uint flags, char* exeName, uint* size);

        [LibraryImport("kernel32.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        public static partial bool GetProcessIoCounters(IntPtr hProcess, IO_COUNTERS* counters);

        /// <summary>Undocumented but stable since Windows XP; what Resource Monitor's "Suspend process" uses.</summary>
        [LibraryImport("ntdll.dll")]
        public static partial int NtSuspendProcess(IntPtr hProcess);

        [LibraryImport("ntdll.dll")]
        public static partial int NtResumeProcess(IntPtr hProcess);

        // --- network connection tables (no admin needed) ---

        public const int AF_INET = 2;
        public const int AF_INET6 = 23;
        public const int TCP_TABLE_OWNER_PID_ALL = 5;
        public const int UDP_TABLE_OWNER_PID = 1;
        public const uint MIB_TCP_STATE_ESTAB = 5;

        [LibraryImport("iphlpapi.dll")]
        public static partial uint GetExtendedTcpTable(void* table, uint* size, [MarshalAs(UnmanagedType.Bool)] bool order, int af, int tableClass, uint reserved);

        [LibraryImport("iphlpapi.dll")]
        public static partial uint GetExtendedUdpTable(void* table, uint* size, [MarshalAs(UnmanagedType.Bool)] bool order, int af, int tableClass, uint reserved);

        // --- services ---

        public const uint SC_MANAGER_CONNECT = 0x0001;
        public const uint SERVICE_QUERY_STATUS = 0x0004;
        public const uint SERVICE_START = 0x0010;
        public const uint SERVICE_STOP = 0x0020;
        public const uint SERVICE_CONTROL_STOP = 1;
        public const uint SERVICE_STOPPED = 1;
        public const uint SERVICE_RUNNING = 4;

        [StructLayout(LayoutKind.Sequential)]
        internal struct SERVICE_STATUS
        {
            public uint dwServiceType, dwCurrentState, dwControlsAccepted, dwWin32ExitCode, dwServiceSpecificExitCode, dwCheckPoint, dwWaitHint;
        }

        [LibraryImport("advapi32.dll", EntryPoint = "OpenSCManagerW", SetLastError = true, StringMarshalling = StringMarshalling.Utf16)]
        public static partial IntPtr OpenSCManager(string? machine, string? database, uint access);

        [LibraryImport("advapi32.dll", EntryPoint = "OpenServiceW", SetLastError = true, StringMarshalling = StringMarshalling.Utf16)]
        public static partial IntPtr OpenService(IntPtr scm, string name, uint access);

        [LibraryImport("advapi32.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        public static partial bool QueryServiceStatus(IntPtr service, SERVICE_STATUS* status);

        [LibraryImport("advapi32.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        public static partial bool ControlService(IntPtr service, uint control, SERVICE_STATUS* status);

        [LibraryImport("advapi32.dll", EntryPoint = "StartServiceW", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        public static partial bool StartService(IntPtr service, uint argc, IntPtr argv);

        [LibraryImport("advapi32.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        public static partial bool CloseServiceHandle(IntPtr handle);
        // --- misc ---

        [StructLayout(LayoutKind.Sequential)]
        internal struct FILETIME
        {
            public uint Low, High;
            public readonly ulong Value => ((ulong)High << 32) | Low;
        }

        /// <summary>System-wide idle/kernel/user CPU time, for whole-PC CPU usage.</summary>
        [LibraryImport("kernel32.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        public static partial bool GetSystemTimes(FILETIME* idle, FILETIME* kernel, FILETIME* user);

        /// <summary>Used only by the command-line safe reset, which runs without the app's UI.</summary>
        [LibraryImport("user32.dll", EntryPoint = "MessageBoxW", StringMarshalling = StringMarshalling.Utf16)]
        public static partial int MessageBox(IntPtr hWnd, string text, string caption, uint type);
    }
}

