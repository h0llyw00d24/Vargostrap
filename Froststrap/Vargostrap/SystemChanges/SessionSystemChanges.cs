// SPDX-FileCopyrightText: 2026 Vargostrap
//
// SPDX-License-Identifier: MPL-2.0

using System.Runtime.Versioning;
using Froststrap.Vargostrap.Performance;

namespace Froststrap.Vargostrap.SystemChanges
{
    /// <summary>
    /// All system changes for one Roblox session: priority management and background traffic control.
    /// Normal-user changes are made here and journaled. Admin changes are collected into one plan and run
    /// by the elevated helper (a single UAC prompt), which also undoes them when Roblox closes.
    /// </summary>
    [SupportedOSPlatform("windows")]
    internal sealed class SessionSystemChanges
    {
        private readonly ChangeJournal _journal;
        private readonly TrafficController? _traffic;

        private SessionSystemChanges(ChangeJournal journal, TrafficController? traffic)
        {
            _journal = journal;
            _traffic = traffic;
        }

        public static SessionSystemChanges? Begin(WatcherData data)
        {
            var s = VargoApp.Settings.Prop;
            if (data.LaunchMode != LaunchMode.Player || !(s.PriorityEnabled || s.TrafficControlEnabled || s.PowerPlanDuringPlay))
                return null;

            ChangeJournal journal = ChangeJournal.User(Paths.Base);
            List<AdminAction> adminPlan = [];

            if (s.PowerPlanDuringPlay)
            {
                if (PowerPlans.BestPerformancePlan() is (string guid, string name))
                {
                    string? error = SystemActions.SwitchPowerPlan(journal, guid, name);
                    App.Logger.Info(error is null ? $"Power plan: {name} while Roblox runs" : $"Power plan not switched: {error}");
                }
                else
                {
                    App.Logger.Warn("Power plan: this PC has no High/Ultimate Performance plan (common on Modern Standby laptops)");
                }
            }

            PriorityManager.Apply(s, journal, data.ProcessId, adminPlan);
            TrafficController? traffic = TrafficController.Begin(s, journal, adminPlan);

            if (adminPlan.Count > 0)
            {
                string plan = ElevatedHelper.WritePlan(Paths.Base, adminPlan);
                App.Logger.Info($"Starting elevated helper for {adminPlan.Count} admin change(s)");

                if (!ElevatedHelper.Start("session", Paths.Base, data.ProcessId.ToString(CultureInfo.InvariantCulture), plan))
                    App.Logger.Warn("Admin changes skipped (UAC declined or unavailable); the rest still apply");
            }

            return new SessionSystemChanges(journal, traffic);
        }

        /// <summary>Undoes this session's normal-user changes. (The elevated helper undoes its own.)</summary>
        public void End()
        {
            _traffic?.Dispose();

            List<string> failures = _journal.RevertAll(m => App.Logger.Info(m));
            foreach (string f in failures)
                App.Logger.Warn($"Undo failed: {f}");
        }
    }
}
