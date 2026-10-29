// SPDX-FileCopyrightText: 2026 Vargostrap
//
// SPDX-License-Identifier: MPL-2.0

using System.Runtime.Versioning;
using Froststrap.Vargostrap.Models;
using Froststrap.Vargostrap.Native;
using Froststrap.Vargostrap.SystemChanges;

namespace Froststrap.Vargostrap.Performance
{
    internal sealed record BackgroundAppStatus(
        BackgroundApp App, int Instances, bool ServiceRunning, long BytesPerSecond, int Connections, BackgroundAction Action)
    {
        public bool IsActive => Instances > 0 || ServiceRunning;
    }

    /// <summary>
    /// Finds running background apps without admin rights. Windows only provides exact per-app network
    /// byte counts through kernel tracing (admin), so the activity figure is each app's total I/O rate
    /// (network plus disk, from GetProcessIoCounters), shown alongside its count of open internet
    /// connections (from the TCP/UDP tables). Together they show what's busy right now.
    /// </summary>
    [SupportedOSPlatform("windows")]
    internal sealed class BackgroundScanner
    {
        private const long OtherAppMinBytesPerSecond = 100 * 1024;
        private const int OtherAppLimit = 8;

        private Dictionary<int, (long Bytes, DateTime At)> _previous = [];

        /// <summary>Catalogue/custom apps that are running, plus up to 8 other busy apps with internet connections.</summary>
        public List<BackgroundAppStatus> Scan()
        {
            Dictionary<int, int> connections = ConnectionsByPid();
            Dictionary<int, (long Bytes, DateTime At)> current = [];
            Dictionary<int, long> rates = [];
            List<Process> processes = [.. Process.GetProcesses()];

            try
            {
                foreach (Process p in processes)
                {
                    long? bytes = TotalIoBytes(p.Id);
                    if (bytes is null) continue;

                    DateTime now = DateTime.UtcNow;
                    current[p.Id] = (bytes.Value, now);

                    if (_previous.TryGetValue(p.Id, out var prev) && (now - prev.At).TotalSeconds > 0.2)
                        rates[p.Id] = (long)((bytes.Value - prev.Bytes) / (now - prev.At).TotalSeconds);
                }

                _previous = current;

                List<BackgroundAppStatus> result = [];
                var matchedPids = new HashSet<int>();

                foreach (BackgroundApp app in BackgroundAppCatalog.AllConfigured())
                {
                    Process[] mine = [.. processes.Where(p => app.ProcessNames.Contains(SafeName(p), StringComparer.OrdinalIgnoreCase))];
                    foreach (Process p in mine) matchedPids.Add(p.Id);

                    bool serviceRunning = app.Service is not null && IsServiceRunning(app.Service);
                    if (mine.Length == 0 && !serviceRunning)
                        continue;

                    result.Add(new BackgroundAppStatus(app, mine.Length, serviceRunning,
                        mine.Sum(p => rates.GetValueOrDefault(p.Id)),
                        mine.Sum(p => connections.GetValueOrDefault(p.Id)),
                        BackgroundAppCatalog.ActionFor(app)));
                }

                // other busy apps with internet connections, so the user can add a rule for them
                var others = processes
                    .Where(p => !matchedPids.Contains(p.Id) && connections.ContainsKey(p.Id) && !SystemActions.IsProtected(p))
                    .GroupBy(SafeName, StringComparer.OrdinalIgnoreCase)
                    .Select(g => (Name: g.Key, Rate: g.Sum(p => rates.GetValueOrDefault(p.Id)), Conns: g.Sum(p => connections[p.Id]), Count: g.Count()))
                    .Where(x => x.Rate >= OtherAppMinBytesPerSecond && x.Name.Length > 0)
                    .OrderByDescending(x => x.Rate)
                    .Take(OtherAppLimit);

                foreach (var o in others)
                {
                    BackgroundApp app = BackgroundAppCatalog.Custom(o.Name);
                    result.Add(new BackgroundAppStatus(app, o.Count, false, o.Rate, o.Conns, BackgroundAppCatalog.ActionFor(app)));
                }

                return result;
            }
            finally
            {
                foreach (Process p in processes) p.Dispose();
            }
        }

        private static string SafeName(Process p)
        {
            try { return p.ProcessName; }
            catch { return ""; }
        }

        private static unsafe long? TotalIoBytes(int pid)
        {
            IntPtr h = Win32.OpenProcess(Win32.PROCESS_QUERY_LIMITED_INFORMATION, false, (uint)pid);
            if (h == IntPtr.Zero) return null;

            try
            {
                Win32.IO_COUNTERS c;
                return Win32.GetProcessIoCounters(h, &c)
                    ? (long)(c.ReadTransferCount + c.WriteTransferCount + c.OtherTransferCount)
                    : null;
            }
            finally
            {
                Win32.CloseHandle(h);
            }
        }

        public static unsafe bool IsServiceRunning(string name)
        {
            IntPtr scm = Win32.OpenSCManager(null, null, Win32.SC_MANAGER_CONNECT);
            if (scm == IntPtr.Zero) return false;

            try
            {
                IntPtr svc = Win32.OpenService(scm, name, Win32.SERVICE_QUERY_STATUS);
                if (svc == IntPtr.Zero) return false;

                try
                {
                    Win32.SERVICE_STATUS status;
                    return Win32.QueryServiceStatus(svc, &status) && status.dwCurrentState == Win32.SERVICE_RUNNING;
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

        /// <summary>Established TCP connections and bound UDP sockets per process, IPv4 and IPv6, ignoring loopback.</summary>
        private static unsafe Dictionary<int, int> ConnectionsByPid()
        {
            Dictionary<int, int> counts = [];

            void Count(int pid)
            {
                if (pid > 4) counts[pid] = counts.GetValueOrDefault(pid) + 1;
            }

            // TCP v4: MIB_TCPROW_OWNER_PID = state, localAddr, localPort, remoteAddr, remotePort, pid (6 x uint)
            ReadTable(isTcp: true, Win32.AF_INET, rowSize: 24, row =>
            {
                uint* r = (uint*)row;
                uint remote = r[3];
                if (r[0] == Win32.MIB_TCP_STATE_ESTAB && (remote & 0xFF) != 127)
                    Count((int)r[5]);
            });

            // TCP v6: 16 local addr, scope, port, 16 remote addr, scope, port, state, pid = 56 bytes
            ReadTable(isTcp: true, Win32.AF_INET6, rowSize: 56, row =>
            {
                byte* b = (byte*)row;
                uint state = *(uint*)(b + 48);
                bool loopback = true;
                for (int i = 0; i < 15; i++) loopback &= b[24 + i] == 0;
                loopback &= b[24 + 15] == 1;
                if (state == Win32.MIB_TCP_STATE_ESTAB && !loopback)
                    Count((int)*(uint*)(b + 52));
            });

            // UDP v4: localAddr, localPort, pid (3 x uint)
            ReadTable(isTcp: false, Win32.AF_INET, rowSize: 12, row =>
            {
                uint* r = (uint*)row;
                if ((r[0] & 0xFF) != 127) Count((int)r[2]);
            });

            // UDP v6: 16 addr, scope, port, pid = 28 bytes
            ReadTable(isTcp: false, Win32.AF_INET6, rowSize: 28, row => Count((int)*(uint*)((byte*)row + 24)));

            return counts;
        }

        private static unsafe void ReadTable(bool isTcp, int af, int rowSize, Action<IntPtr> onRow)
        {
            uint size = 0;
            int tableClass = isTcp ? Win32.TCP_TABLE_OWNER_PID_ALL : Win32.UDP_TABLE_OWNER_PID;

            for (int attempt = 0; attempt < 3; attempt++)
            {
                byte[] buffer = new byte[Math.Max(size, 4)];
                fixed (byte* p = buffer)
                {
                    uint err = isTcp
                        ? Win32.GetExtendedTcpTable(p, &size, false, af, tableClass, 0)
                        : Win32.GetExtendedUdpTable(p, &size, false, af, tableClass, 0);

                    if (err == 122) continue; // ERROR_INSUFFICIENT_BUFFER: size was updated, retry
                    if (err != 0) return;

                    uint count = *(uint*)p;
                    byte* rows = p + 4; // rows follow the DWORD entry count (all row fields are DWORD-aligned)
                    for (uint i = 0; i < count && 4 + (i + 1) * rowSize <= buffer.Length; i++)
                        onRow((IntPtr)(rows + i * rowSize));
                    return;
                }
            }
        }
    }
}
