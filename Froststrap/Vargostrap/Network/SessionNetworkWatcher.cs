// SPDX-FileCopyrightText: 2026 Vargostrap
//
// SPDX-License-Identifier: MPL-2.0

using Froststrap.Integrations;

namespace Froststrap.Vargostrap.Network
{
    /// <summary>
    /// Runs a <see cref="NetworkMonitor"/> for each game the player joins during a Roblox session, using
    /// the server address upstream's ActivityWatcher reads from the Roblox log. Roblox game servers
    /// currently ignore ping, so the monitor usually falls back to measuring the route to Roblox's edge.
    /// </summary>
    internal sealed class SessionNetworkWatcher : IAsyncDisposable
    {
        private readonly ActivityWatcher _activity;
        private readonly SemaphoreSlim _gate = new(1, 1);
        private NetworkMonitor? _monitor;

        private SessionNetworkWatcher(ActivityWatcher activity)
        {
            _activity = activity;
            _activity.OnGameJoin += OnGameJoin;
            _activity.OnGameLeave += OnGameLeave;
        }

        public static SessionNetworkWatcher? Begin(WatcherData data, ActivityWatcher? activity)
        {
            if (data.LaunchMode != LaunchMode.Player || activity is null || !VargoApp.Settings.Prop.NetMonitorEnabled)
                return null;

            return new SessionNetworkWatcher(activity);
        }

        private void OnGameJoin(object? sender, EventArgs e) => _ = Task.Run(async () =>
        {
            await _gate.WaitAsync();
            try
            {
                await FinishCurrentAsync();

                var data = _activity.Data;
                if (!data.MachineAddressValid)
                {
                    App.Logger.Info($"Network monitor: no usable server address ({data.MachineAddress}), not monitoring this game");
                    return;
                }

                var s = VargoApp.Settings.Prop;
                string label = $"Place {data.PlaceId}";
                _monitor = new NetworkMonitor(data.MachineAddress, label, NetworkSessionLog.NewSessionPath(), s.NetSpikeThresholdMs, s.NetTracerouteOnSpike);
                _monitor.Start();
            }
            catch (Exception ex)
            {
                App.Logger.Error(ex, "Network monitor failed to start");
            }
            finally
            {
                _gate.Release();
            }
        });

        private void OnGameLeave(object? sender, EventArgs e) => _ = Task.Run(async () =>
        {
            await _gate.WaitAsync();
            try { await FinishCurrentAsync(); }
            finally { _gate.Release(); }
        });

        private async Task FinishCurrentAsync()
        {
            if (_monitor is null)
                return;

            NetworkMonitor monitor = _monitor;
            _monitor = null;
            await monitor.DisposeAsync();
        }

        public async ValueTask DisposeAsync()
        {
            _activity.OnGameJoin -= OnGameJoin;
            _activity.OnGameLeave -= OnGameLeave;

            await _gate.WaitAsync();
            try { await FinishCurrentAsync(); }
            finally { _gate.Release(); }

            _gate.Dispose();
        }
    }
}
