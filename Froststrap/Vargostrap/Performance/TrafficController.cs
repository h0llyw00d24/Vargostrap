// SPDX-FileCopyrightText: 2026 Vargostrap
//
// SPDX-License-Identifier: MPL-2.0

using System.Runtime.Versioning;
using Froststrap.Vargostrap.Models;
using Froststrap.Vargostrap.SystemChanges;

namespace Froststrap.Vargostrap.Performance
{
    /// <summary>
    /// Background traffic control for one Roblox session. Per app, using the user's remembered choice:
    /// <list type="bullet">
    /// <item>Lower priority: BelowNormal CPU priority (no admin).</item>
    /// <item>Pause: suspend the app's processes (no admin), or stop its Windows service (admin, via the helper).</item>
    /// <item>Throttle uploads: a Windows QoS throttle policy (admin, via the helper). Windows QoS can only
    /// limit what an app sends, not what it downloads.</item>
    /// </list>
    /// Nothing is ever killed. Apps started during the session are picked up by a rescan every 30 seconds.
    /// Everything is undone from the change journal when Roblox closes.
    /// </summary>
    [SupportedOSPlatform("windows")]
    internal sealed class TrafficController : IDisposable
    {
        private static readonly TimeSpan RescanInterval = TimeSpan.FromSeconds(30);

        private readonly ChangeJournal _journal;
        private readonly HashSet<(int Pid, BackgroundAction Action)> _handled = [];
        private readonly Timer? _rescan;

        private TrafficController(ChangeJournal journal)
        {
            _journal = journal;
            _rescan = new Timer(_ => ApplyProcessActions(), null, RescanInterval, RescanInterval);
        }

        public static TrafficController? Begin(VargostrapSettings s, ChangeJournal journal, List<AdminAction> adminPlan)
        {
            if (!s.TrafficControlEnabled)
                return null;

            foreach (BackgroundApp app in BackgroundAppCatalog.AllConfigured())
            {
                BackgroundAction action = BackgroundAppCatalog.ActionFor(app);

                if (app.Service is not null && action == BackgroundAction.Pause && BackgroundScanner.IsServiceRunning(app.Service))
                    adminPlan.Add(new AdminAction(AdminActionKind.ServiceStop, app.Service, app.Name));

                if (action == BackgroundAction.ThrottleUploads)
                {
                    foreach (string name in app.ProcessNames)
                        adminPlan.Add(new AdminAction(AdminActionKind.QosThrottle, name + ".exe", app.Name, Math.Max(1, s.ThrottleMbps) * 1_000_000L));
                }
            }

            CloseChosenApps();

            var controller = new TrafficController(journal);
            controller.ApplyProcessActions();
            return controller;
        }

        /// <summary>
        /// "Close at launch": asks each chosen app's main window to close, like clicking its X. Never forced,
        /// and apps that only live in the tray (no window) are left running. This one can't be undone,
        /// since a closed app isn't reopened, so it's never a default.
        /// </summary>
        private static void CloseChosenApps()
        {
            foreach (BackgroundApp app in BackgroundAppCatalog.AllConfigured().Where(a => BackgroundAppCatalog.ActionFor(a) == BackgroundAction.CloseOnLaunch))
            {
                foreach (string name in app.ProcessNames)
                {
                    foreach (Process p in Process.GetProcessesByName(name))
                    {
                        using (p)
                        {
                            if (SystemActions.IsProtected(p)) continue;

                            bool asked = p.MainWindowHandle != IntPtr.Zero && p.CloseMainWindow();
                            App.Logger.Info(asked
                                ? $"Background control: asked {p.ProcessName} ({p.Id}) to close"
                                : $"Background control: {p.ProcessName} ({p.Id}) has no window to close, left running");
                        }
                    }
                }
            }
        }

        private void ApplyProcessActions()
        {
            try
            {
                foreach (BackgroundApp app in BackgroundAppCatalog.AllConfigured())
                {
                    BackgroundAction action = BackgroundAppCatalog.ActionFor(app);
                    if (action is not (BackgroundAction.LowerPriority or BackgroundAction.Pause) || app.ProcessNames.Length == 0)
                        continue;

                    foreach (string name in app.ProcessNames)
                    {
                        foreach (Process p in Process.GetProcessesByName(name))
                        {
                            using (p)
                            {
                                lock (_handled)
                                {
                                    if (!_handled.Add((p.Id, action)))
                                        continue;
                                }

                                string? error = action == BackgroundAction.Pause
                                    ? SystemActions.Suspend(_journal, p)
                                    : SystemActions.LowerPriority(_journal, p, ProcessPriorityClass.BelowNormal);

                                if (error is null)
                                    App.Logger.Info($"Background control: {BackgroundAppCatalog.Describe(action)} {p.ProcessName} ({p.Id})");
                                else
                                    App.Logger.Warn($"Background control skipped {p.ProcessName} ({p.Id}): {error}");
                            }
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                App.Logger.Error(ex, "Background traffic control scan failed");
            }
        }

        /// <summary>What turning this on would do right now, for the preview dialog.</summary>
        public static List<string> Preview(VargostrapSettings s)
        {
            List<string> lines = [];

            foreach (BackgroundApp app in BackgroundAppCatalog.AllConfigured())
            {
                BackgroundAction action = BackgroundAppCatalog.ActionFor(app);
                if (action == BackgroundAction.Ignore) continue;

                int running = app.ProcessNames.Sum(n => { var ps = Process.GetProcessesByName(n); foreach (var p in ps) p.Dispose(); return ps.Length; });
                bool service = app.Service is not null && BackgroundScanner.IsServiceRunning(app.Service);
                string now = app.Service is not null
                    ? (service ? "running now" : "not running now")
                    : (running > 0 ? $"{running} process(es) running now" : "not running now");

                string what = action switch
                {
                    BackgroundAction.Pause when app.Service is not null => $"Pause the {app.Name} service (admin)",
                    BackgroundAction.Pause => $"Pause {app.Name}",
                    BackgroundAction.LowerPriority => $"Lower {app.Name}'s CPU priority",
                    BackgroundAction.ThrottleUploads => $"Limit {app.Name}'s uploads to {s.ThrottleMbps} Mbit/s (admin)",
                    BackgroundAction.CloseOnLaunch => $"Ask {app.Name} to close (not reopened afterwards)",
                    _ => app.Name,
                };

                lines.Add($"{what}: {now}");
            }

            return lines;
        }

        public void Dispose() => _rescan?.Dispose();
    }
}
