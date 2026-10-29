// SPDX-FileCopyrightText: 2026 Vargostrap
//
// SPDX-License-Identifier: MPL-2.0

using System.Net.NetworkInformation;
using System.Runtime.Versioning;
using Froststrap.Vargostrap.SystemChanges;
using Microsoft.Win32;

namespace Froststrap.Vargostrap.Performance
{
    /// <summary>Windows power plans via powercfg (switching the active plan needs no admin).</summary>
    [SupportedOSPlatform("windows")]
    internal static class PowerPlans
    {
        public const string UltimateGuid = "e9a42b02-d5df-448d-aa00-03f14749eb61";
        public const string HighPerformanceGuid = "8c5e7fda-e8bf-4a96-9a85-a6e23a8c635c";

        private static string PowerCfg => Path.Combine(Environment.SystemDirectory, "powercfg.exe");

        private static readonly Regex SchemeLine = new(@"([0-9a-fA-F-]{36})\s+\((.+?)\)", RegexOptions.Compiled);

        public static string? ActiveGuid()
        {
            Match m = SchemeLine.Match(Run("/getactivescheme").Output);
            return m.Success ? m.Groups[1].Value : null;
        }

        public static List<(string Guid, string Name)> List() =>
            [.. SchemeLine.Matches(Run("/list").Output).Select(m => (m.Groups[1].Value, m.Groups[2].Value)).DistinctBy(x => x.Item1, StringComparer.OrdinalIgnoreCase)];

        /// <summary>Ultimate Performance if this PC has it, otherwise High performance. Null on PCs that hide both (most Modern Standby laptops).</summary>
        public static (string Guid, string Name)? BestPerformancePlan()
        {
            List<(string Guid, string Name)> plans = List();

            foreach (string wanted in (string[])[UltimateGuid, HighPerformanceGuid])
            {
                foreach ((string Guid, string Name) plan in plans)
                {
                    if (string.Equals(plan.Guid, wanted, StringComparison.OrdinalIgnoreCase))
                        return plan;
                }
            }

            return null;
        }

        public static bool SetActive(string guid) => Run($"/setactive {guid}").ExitCode == 0;

        private static (int ExitCode, string Output) Run(string args)
        {
            try
            {
                using Process p = Process.Start(new ProcessStartInfo(PowerCfg, args)
                {
                    UseShellExecute = false,
                    CreateNoWindow = true,
                    RedirectStandardOutput = true,
                })!;
                string output = p.StandardOutput.ReadToEnd();
                p.WaitForExit(10_000);
                return (p.ExitCode, output);
            }
            catch (Exception ex)
            {
                return (-1, ex.Message);
            }
        }
    }

    internal sealed record TweakValue(string Hive, string Key, string Name, int Value, string Description);

    /// <summary>
    /// Persistent Windows settings the user switches on in Vargostrap. Each one records the original
    /// values in the persistent journal, and switching it off (or safe-mode reset) puts them back exactly.
    /// </summary>
    [SupportedOSPlatform("windows")]
    internal static class WindowsTweaks
    {
        public const string GameModeGroup = "gamemode:";
        public const string GameBarGroup = "gamebar:";
        public const string NicPowerGroup = "nicpower:";

        public static readonly IReadOnlyList<TweakValue> GameMode =
        [
            new("HKCU", @"Software\Microsoft\GameBar", "AutoGameModeEnabled", 1, "Turn on Windows Game Mode"),
            new("HKCU", @"Software\Microsoft\GameBar", "AllowAutoGameMode", 1, "Allow Windows Game Mode"),
        ];

        public static readonly IReadOnlyList<TweakValue> GameBarCapture =
        [
            new("HKCU", @"System\GameConfigStore", "GameDVR_Enabled", 0, "Turn off Game DVR background recording"),
            new("HKCU", @"Software\Microsoft\Windows\CurrentVersion\GameDVR", "AppCaptureEnabled", 0, "Turn off Xbox Game Bar captures"),
            new("HKCU", @"Software\Microsoft\GameBar", "UseNexusForGameBarEnabled", 0, "Stop the controller/Win+G button from opening Game Bar"),
        ];

        private static ChangeJournal Journal => ChangeJournal.Persistent(Paths.Base);

        /// <summary>True if every value already has the tweak's setting (whether Vargostrap or the user set it).</summary>
        public static bool IsInEffect(IReadOnlyList<TweakValue> values) => values.All(v => ReadDword(v) == v.Value);

        public static bool IsManagedByVargostrap(string group) => Journal.Read().Any(e => e.Id.StartsWith(group, StringComparison.Ordinal));

        public static List<string> Apply(string group, IReadOnlyList<TweakValue> values)
        {
            List<string> errors = [];
            foreach (TweakValue v in values)
            {
                string? error = SystemActions.SetRegistryValue(Journal, v.Hive, v.Key, v.Name, v.Value, RegistryValueKind.DWord, v.Description, group);
                if (error is not null) errors.Add($"{v.Description}: {error}");
            }
            App.Logger.Info($"Windows tweak {group} applied" + (errors.Count > 0 ? $" with errors: {string.Join("; ", errors)}" : ""));
            return errors;
        }

        public static List<string> Undo(string group) =>
            Journal.RevertWhere(e => e.Id.StartsWith(group, StringComparison.Ordinal), m => App.Logger.Info(m));

        public static string Describe(TweakValue v) => $@"{v.Hive}\{v.Key}\{v.Name} = {v.Value} (now: {ReadDword(v)?.ToString(CultureInfo.InvariantCulture) ?? "not set"})";

        private static int? ReadDword(TweakValue v)
        {
            using RegistryKey root = v.Hive == "HKLM" ? Registry.LocalMachine : Registry.CurrentUser;
            using RegistryKey? key = root.OpenSubKey(v.Key);
            return key?.GetValue(v.Name) is int i ? i : null;
        }

        // ------------------------------------------------------------------ network adapters

        private const string NetClassKey = @"SYSTEM\CurrentControlSet\Control\Class\{4d36e972-e325-11ce-bfc1-08002be10318}";

        /// <summary>PnPCapabilities 0x18 = "don't let Windows turn this device off to save power".</summary>
        public const int PnPCapabilitiesNoPowerSaving = 0x18;

        internal sealed record AdapterInfo(string Name, string Description, bool IsWireless, long SpeedMbps, string? ClassSubkey, bool PowerSavingDisabled);

        /// <summary>The adapter Windows actually uses for internet traffic (has a default gateway), skipping virtual ones.</summary>
        public static AdapterInfo? PrimaryAdapter()
        {
            NetworkInterface? nic = NetworkInterface.GetAllNetworkInterfaces()
                .Where(n => n.OperationalStatus == OperationalStatus.Up
                    && n.NetworkInterfaceType is not (NetworkInterfaceType.Loopback or NetworkInterfaceType.Tunnel)
                    && !IsVirtual(n.Description)
                    && n.GetIPProperties().GatewayAddresses.Any(g => !g.Address.Equals(System.Net.IPAddress.Any) && !g.Address.Equals(System.Net.IPAddress.IPv6Any)))
                .OrderByDescending(n => n.Speed)
                .FirstOrDefault();

            if (nic is null)
                return null;

            string? subkey = FindClassSubkey(nic.Id);
            int? pnp = null;
            if (subkey is not null)
            {
                using RegistryKey? k = Registry.LocalMachine.OpenSubKey($@"{NetClassKey}\{subkey}");
                pnp = k?.GetValue("PnPCapabilities") is int i ? i : null;
            }

            return new AdapterInfo(nic.Name, nic.Description, nic.NetworkInterfaceType == NetworkInterfaceType.Wireless80211,
                nic.Speed / 1_000_000, subkey, pnp == PnPCapabilitiesNoPowerSaving);
        }

        public static string AdapterKey(string subkey) => $@"{NetClassKey}\{subkey}";

        private static readonly string[] VirtualAdapterWords = ["virtual", "vpn", "hyper-v", "vmware", "tap-", "wintun", "wireguard", "loopback", "bluetooth"];

        private static bool IsVirtual(string description) =>
            VirtualAdapterWords.Any(w => description.Contains(w, StringComparison.OrdinalIgnoreCase));

        /// <summary>
        /// Turns "Allow the computer to turn off this device to save power" off (or back to how it was) for
        /// the adapter, via the elevated helper's persistent journal. Takes effect after the adapter or PC restarts.
        /// Returns false if the admin prompt was declined.
        /// </summary>
        public static bool SetNicPowerSavingDisabled(AdapterInfo adapter, bool disabled)
        {
            if (!disabled)
                return ElevatedHelper.Start("unpersist", Paths.Base, NicPowerGroup);

            if (adapter.ClassSubkey is null)
                return false;

            string plan = ElevatedHelper.WritePlan(Paths.Base,
            [
                new AdminAction(AdminActionKind.RegistryHklmDword, "PnPCapabilities",
                    $"Stop Windows turning off {adapter.Description} to save power", PnPCapabilitiesNoPowerSaving,
                    Key: AdapterKey(adapter.ClassSubkey), Group: NicPowerGroup),
            ], "AdminPlan.persist.json");

            return ElevatedHelper.Start("persist", Paths.Base, plan);
        }

        public static bool NicTweakManagedByVargostrap() =>
            ChangeJournal.PersistentAdmin(Paths.Base).Read().Any(e => e.Id.StartsWith(NicPowerGroup, StringComparison.Ordinal));

        private static string? FindClassSubkey(string interfaceId)
        {
            using RegistryKey? cls = Registry.LocalMachine.OpenSubKey(NetClassKey);
            if (cls is null) return null;

            foreach (string name in cls.GetSubKeyNames())
            {
                if (!int.TryParse(name, NumberStyles.None, CultureInfo.InvariantCulture, out _)) continue;
                try
                {
                    using RegistryKey? k = cls.OpenSubKey(name);
                    if (string.Equals(k?.GetValue("NetCfgInstanceId") as string, interfaceId, StringComparison.OrdinalIgnoreCase))
                        return name;
                }
                catch (System.Security.SecurityException) { /* "Properties" subkey is admin-only */ }
            }

            return null;
        }
    }
}
