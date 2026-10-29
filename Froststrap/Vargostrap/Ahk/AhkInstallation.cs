// SPDX-FileCopyrightText: 2026 Vargostrap
//
// SPDX-License-Identifier: MPL-2.0

using System.Runtime.Versioning;
using Froststrap.Vargostrap.Native;
using Microsoft.Win32;

namespace Froststrap.Vargostrap.Ahk
{
    /// <summary>
    /// Finds how to run an .ahk file on this PC: the .ahk file association (what double-clicking does,
    /// which also picks v1 vs v2 for AutoHotkey v2's launcher), or failing that, AutoHotkey's install folder.
    /// </summary>
    [SupportedOSPlatform("windows")]
    internal sealed class AhkInstallation
    {
        public const string DownloadUrl = "https://www.autohotkey.com/";

        /// <summary>Command template; "%1" is replaced with the script path.</summary>
        public string CommandTemplate { get; }

        public string ExecutablePath { get; }

        private AhkInstallation(string commandTemplate, string executablePath)
        {
            CommandTemplate = commandTemplate;
            ExecutablePath = executablePath;
        }

        public static AhkInstallation? Find()
        {
            try
            {
                return FromAssociation() ?? FromInstallDir();
            }
            catch (Exception ex)
            {
                App.Logger.Warn($"AutoHotkey detection failed: {ex.Message}");
                return null;
            }
        }

        public string BuildCommandLine(string scriptPath)
        {
            string quoted = $"\"{scriptPath}\"";
            string cmd = CommandTemplate.Replace("%*", "", StringComparison.Ordinal).Trim();

            foreach (string token in (string[])["\"%1\"", "\"%L\"", "%1", "%L"])
            {
                if (cmd.Contains(token, StringComparison.OrdinalIgnoreCase))
                    return cmd.Replace(token, quoted, StringComparison.OrdinalIgnoreCase);
            }

            return $"{cmd} {quoted}";
        }

        private static unsafe AhkInstallation? FromAssociation()
        {
            uint size = 1024;
            char* buffer = stackalloc char[(int)size];

            if (Win32.AssocQueryString(Win32.ASSOCF_NONE, Win32.ASSOCSTR_COMMAND, ".ahk", "open", buffer, &size) != 0)
                return null;

            string command = new(buffer);
            string? exe = FirstToken(command);

            // .ahk can be associated with an editor instead; only accept AutoHotkey itself
            if (exe is null || !File.Exists(exe) || !Path.GetFileName(exe).StartsWith("AutoHotkey", StringComparison.OrdinalIgnoreCase))
                return null;

            return new AhkInstallation(command, exe);
        }

        private static AhkInstallation? FromInstallDir()
        {
            foreach (RegistryKey root in (RegistryKey[])[Registry.LocalMachine, Registry.CurrentUser])
            {
                using RegistryKey? key = root.OpenSubKey(@"SOFTWARE\AutoHotkey");
                if (key?.GetValue("InstallDir") is not string dir || !Directory.Exists(dir))
                    continue;

                foreach (string candidate in (string[])[@"v2\AutoHotkey64.exe", @"v2\AutoHotkey32.exe", "AutoHotkeyU64.exe", "AutoHotkey.exe"])
                {
                    string exe = Path.Combine(dir, candidate);
                    if (File.Exists(exe))
                        return new AhkInstallation($"\"{exe}\" \"%1\"", exe);
                }
            }

            return null;
        }

        private static string? FirstToken(string command)
        {
            command = command.Trim();
            if (command.StartsWith('"'))
            {
                int end = command.IndexOf('"', 1);
                return end > 1 ? command[1..end] : null;
            }

            int space = command.IndexOf(' ', StringComparison.Ordinal);
            return space > 0 ? command[..space] : command;
        }
    }
}
