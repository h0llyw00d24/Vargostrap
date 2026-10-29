// SPDX-FileCopyrightText: 2026 Vargostrap
//
// SPDX-License-Identifier: MPL-2.0

namespace Froststrap.Vargostrap.FastFlags
{
    /// <param name="Flags">Flag name → value. A null value means "remove, use Roblox's default".</param>
    internal sealed record FpsPreset(string Id, string Name, string Summary, string Details, IReadOnlyDictionary<string, string?> Flags);

    /// <summary>
    /// One-click presets for the FPS Optimization page. Every flag here must be on Roblox's allowlist;
    /// <see cref="ManagedFlags"/> is checked against it at runtime and offending entries are skipped.
    ///
    /// A preset owns <see cref="ManagedFlags"/> only: applying one sets or clears exactly those and never
    /// touches custom flags, the rendering API, fullscreen handling or DPI scaling.
    /// </summary>
    internal static class FpsPresets
    {
        private const string CsgL01 = "DFIntCSGLevelOfDetailSwitchingDistance";
        private const string CsgL12 = "DFIntCSGLevelOfDetailSwitchingDistanceL12";
        private const string CsgL23 = "DFIntCSGLevelOfDetailSwitchingDistanceL23";
        private const string CsgL34 = "DFIntCSGLevelOfDetailSwitchingDistanceL34";
        private const string Msaa = "FIntDebugForceMSAASamples";
        private const string GrassMin = "FIntFRMMinGrassDistance";
        private const string GrassMax = "FIntFRMMaxGrassDistance";
        private const string GrassMotion = "FIntGrassMovementReducedMotionFactor";
        private const string GraySky = "FFlagDebugSkyGray";
        private const string PauseVoxelizer = "DFFlagDebugPauseVoxelizer";
        private const string QualityLevel = "DFIntDebugFRMQualityLevelOverride";
        private const string TextureOverride = "DFFlagTextureQualityOverrideEnabled";
        private const string TextureLevel = "DFIntTextureQualityOverride";

        public static readonly IReadOnlyList<string> ManagedFlags =
        [
            CsgL01, CsgL12, CsgL23, CsgL34, Msaa, GrassMin, GrassMax, GrassMotion,
            GraySky, PauseVoxelizer, QualityLevel, TextureOverride, TextureLevel,
        ];

        public static readonly FpsPreset RobloxDefaults = new(
            "Defaults",
            "Roblox defaults",
            "Removes every preset flag so Roblox uses its own settings.",
            "Clears the flags managed by the presets. Custom flags you added in the editor are kept.",
            ManagedFlags.ToDictionary(f => f, string? (_) => null));

        public static readonly FpsPreset Balanced = new(
            "Balanced",
            "Balanced",
            "Small, mostly invisible savings. Roblox still looks normal.",
            "Turns off anti-aliasing (slightly sharper, more jagged edges) and stops grass from swaying. "
                + "Your in-game graphics slider keeps working as usual.",
            Compose(new()
            {
                [Msaa] = "1",
                [GrassMotion] = "0",
            }));

        public static readonly FpsPreset MaxFps = new(
            "MaxFps",
            "Max FPS",
            "Lowest rendering cost on a decent PC. Textures stay sharp.",
            "Forces the lowest graphics level, uses the simplest version of every mesh, removes grass, "
                + "turns off anti-aliasing, freezes lighting updates and makes the sky plain gray. "
                + "Texture quality is left alone because it mostly costs video memory, not FPS.",
            Compose(new()
            {
                [QualityLevel] = "1",
                [CsgL01] = "0",
                [CsgL12] = "0",
                [CsgL23] = "0",
                [CsgL34] = "0",
                [Msaa] = "1",
                [GrassMin] = "0",
                [GrassMax] = "0",
                [GrassMotion] = "0",
                [PauseVoxelizer] = "True",
                [GraySky] = "True",
            }));

        public static readonly FpsPreset LowEnd = new(
            "LowEnd",
            "Low-end PC",
            "Everything at minimum, including textures. For laptops and integrated graphics.",
            "Everything in Max FPS, plus the lowest texture quality. Lower texture quality reduces "
                + "video memory and RAM use, which helps on integrated graphics and PCs with 8 GB of RAM or less.",
            Compose(new()
            {
                [QualityLevel] = "1",
                [CsgL01] = "0",
                [CsgL12] = "0",
                [CsgL23] = "0",
                [CsgL34] = "0",
                [Msaa] = "1",
                [GrassMin] = "0",
                [GrassMax] = "0",
                [GrassMotion] = "0",
                [PauseVoxelizer] = "True",
                [GraySky] = "True",
                [TextureOverride] = "True",
                [TextureLevel] = "0",
            }));

        public static readonly IReadOnlyList<FpsPreset> All = [Balanced, MaxFps, LowEnd, RobloxDefaults];

        /// <summary>Fills in every managed flag the preset doesn't set with null (= cleared).</summary>
        private static Dictionary<string, string?> Compose(Dictionary<string, string?> values)
        {
            foreach (string flag in ManagedFlags)
                values.TryAdd(flag, null);

            return values;
        }

        /// <summary>
        /// Applies <paramref name="preset"/> to <see cref="App.FastFlags"/> as one undo step.
        /// Returns any flags that were skipped because they are not on the current allowlist.
        /// </summary>
        public static List<string> Apply(FpsPreset preset, AllowlistService allowlist)
        {
            List<string> skipped = [];
            FastFlagManager flags = App.FastFlags;

            flags.SaveUndoSnapshot();
            flags.suspendUndoSnapshot = true;

            try
            {
                foreach (var (flag, value) in preset.Flags)
                {
                    // Clearing a flag is always safe; only setting one needs the allowlist.
                    if (value is not null && !allowlist.IsAllowed(flag))
                    {
                        skipped.Add(flag);
                        flags.SetValue(flag, null);
                        continue;
                    }

                    flags.SetValue(flag, value);
                }
            }
            finally
            {
                flags.suspendUndoSnapshot = false;
            }

            App.Logger.Info($"Applied FPS preset '{preset.Id}'" + (skipped.Count > 0 ? $", skipped non-allowlisted: {string.Join(", ", skipped)}" : ""));
            return skipped;
        }

        /// <summary>The preset whose managed flags exactly match the current flags, if any.</summary>
        public static FpsPreset? Detect()
        {
            foreach (FpsPreset preset in All)
            {
                if (preset.Flags.All(p => App.FastFlags.GetValue(p.Key) == p.Value))
                    return preset;
            }

            return null;
        }
    }
}
