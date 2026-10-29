// SPDX-FileCopyrightText: 2026 Vargostrap
//
// SPDX-License-Identifier: MPL-2.0

using Froststrap.Integrations;
using Froststrap.Vargostrap.Ahk;
using Froststrap.Vargostrap.Network;
using Froststrap.Vargostrap.RobloxSettings;
using Froststrap.Vargostrap.SystemChanges;

namespace Froststrap.Vargostrap
{
    /// <summary>
    /// Vargostrap's per-Roblox-session hook. Upstream's <see cref="Watcher"/> calls <see cref="Begin"/> once
    /// Roblox is running and <see cref="EndAsync"/> after it exits; every Vargostrap feature that needs to
    /// act during a session hangs off this one class, so <c>Watcher.cs</c> only has two hook lines.
    /// Nothing here may throw into the watcher.
    /// </summary>
    internal sealed class VargoSession
    {
        private readonly SettingsLock.SessionCheck? _lockCheck;
        private readonly AhkRunner? _ahk;
        private readonly SessionNetworkWatcher? _network;
        private readonly SessionSystemChanges? _system;

        private VargoSession(WatcherData data, ActivityWatcher? activity)
        {
            _lockCheck = SettingsLock.SessionCheck.Begin(data);

            try { _network = SessionNetworkWatcher.Begin(data, activity); }
            catch (Exception ex) { App.Logger.Error(ex, "Starting the network monitor failed"); }

            if (OperatingSystem.IsWindows())
            {
                try { _system = SessionSystemChanges.Begin(data); }
                catch (Exception ex) { App.Logger.Error(ex, "Applying session system changes failed"); }
            }

            if (OperatingSystem.IsWindows() && data.LaunchMode == LaunchMode.Player)
            {
                try { _ahk = AhkRunner.StartFor(VargoApp.Settings.Prop); }
                catch (Exception ex) { App.Logger.Error(ex, "Starting AutoHotkey scripts failed"); }
            }
        }

        /// <summary>
        /// True when a Vargostrap feature needs the watcher even if upstream wouldn't start one
        /// (upstream only starts it for activity tracking or auto-close integrations).
        /// </summary>
        public static bool NeedsWatcher
        {
            get
            {
                try
                {
                    var s = VargoApp.Settings.Prop;
                    return s.RobloxSettingsLockEnabled || (s.AhkEnabled && s.AhkScripts.Any(x => x.Enabled))
                        || s.PriorityEnabled || s.TrafficControlEnabled || s.PowerPlanDuringPlay;
                }
                catch { return false; }
            }
        }

        public static VargoSession? Begin(WatcherData data, ActivityWatcher? activity)
        {
            try
            {
                return new VargoSession(data, activity);
            }
            catch (Exception ex)
            {
                App.Logger.Error(ex, "Vargostrap session start failed");
                return null;
            }
        }

        public async Task EndAsync()
        {
            try
            {
                // close scripts first so keys are released before any dialog appears
                _ahk?.Dispose();

                // undo system changes before anything that might show a dialog
                if (OperatingSystem.IsWindows())
                    _system?.End();

                if (_network is not null)
                    await _network.DisposeAsync();

                if (_lockCheck is not null)
                    await _lockCheck.EndAsync();
            }
            catch (Exception ex)
            {
                App.Logger.Error(ex, "Vargostrap session end failed");
            }
        }
    }
}
