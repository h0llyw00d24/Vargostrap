// SPDX-FileCopyrightText: 2026 Vargostrap
//
// SPDX-License-Identifier: MPL-2.0

namespace Froststrap.Vargostrap.Network
{
    internal enum RegionRankSource { Preferred, Distance }

    internal sealed record RankedRegion(string Region, RegionRankSource Source);

    /// <summary>
    /// Re-orders the region list Better Matchmaking tries: your preferred regions first, in your order,
    /// then the rest in upstream's nearest-first order. A region picked as the manual override (upstream's
    /// Region Selector) still bypasses this entirely, as before.
    ///
    /// Measured per-region latency isn't possible from outside Roblox: Roblox game servers and the routers
    /// in front of them don't answer ICMP, and probing them with Roblox's own UDP protocol would not be a
    /// public API.
    /// </summary>
    internal static class RegionRanking
    {
        public static List<RankedRegion> RankDetailed(IReadOnlyList<string> distanceSorted)
        {
            var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            List<RankedRegion> ranked = [];

            foreach (string region in VargoApp.Settings.Prop.PreferredRegions)
                if (seen.Add(region)) ranked.Add(new RankedRegion(region, RegionRankSource.Preferred));

            foreach (string region in distanceSorted)
                if (seen.Add(region)) ranked.Add(new RankedRegion(region, RegionRankSource.Distance));

            return ranked;
        }

        /// <summary>Hook for upstream's Better Matchmaking. Never throws; falls back to the input order.</summary>
        public static List<string> Rank(List<string> distanceSorted)
        {
            try
            {
                List<string> ranked = [.. RankDetailed(distanceSorted).Select(r => r.Region)];
                App.Logger.Info($"Vargostrap region order: {string.Join(", ", ranked.Take(5))}...");
                return ranked;
            }
            catch (Exception ex)
            {
                App.Logger.Warn($"Region ranking failed, using distance order: {ex.Message}");
                return distanceSorted;
            }
        }
    }
}
