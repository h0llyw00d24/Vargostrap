// SPDX-FileCopyrightText: 2026 Vargostrap
//
// SPDX-License-Identifier: MPL-2.0

using Froststrap.Models.APIs.RoValra;

namespace Froststrap.Vargostrap.Network
{
    /// <summary>
    /// Roblox datacenter locations, from the same RoValra list upstream's Region Selector / Better
    /// Matchmaking uses, so region names match exactly. Cached on disk for a day to keep requests rare.
    /// </summary>
    internal static class DatacenterDirectory
    {
        private const string Url = "https://apis.rovalra.com/v1/datacenters/list";
        private static readonly TimeSpan CacheFor = TimeSpan.FromHours(24);
        private static readonly SemaphoreSlim _gate = new(1, 1);
        private static List<DatacenterEntry>? _entries;

        private static string CachePath => Path.Combine(Paths.Cache, "datacenters.json");

        /// <summary>Same format as upstream's RobloxServerFetcher.BuildRegionKey ("City, CC").</summary>
        public static string RegionKey(string? city, string? country) =>
            string.IsNullOrWhiteSpace(city) && string.IsNullOrWhiteSpace(country)
                ? "Unknown"
                : $"{city}, {country}".Trim().Trim(',', ' ');

        public static async Task<List<DatacenterEntry>> GetAsync(CancellationToken cancellationToken = default)
        {
            if (_entries is not null)
                return _entries;

            await _gate.WaitAsync(cancellationToken);
            try
            {
                // another caller may have finished loading while we waited
                if (Volatile.Read(ref _entries) is List<DatacenterEntry> loaded)
                    return loaded;

                string? json = null;
                bool fresh = File.Exists(CachePath) && DateTime.UtcNow - File.GetLastWriteTimeUtc(CachePath) < CacheFor;

                if (fresh)
                    json = await File.ReadAllTextAsync(CachePath, cancellationToken);

                if (json is null)
                {
                    try
                    {
                        json = await App.HttpClient.GetStringAsync(new Uri(Url), cancellationToken);
                        Directory.CreateDirectory(Paths.Cache);
                        await File.WriteAllTextAsync(CachePath, json, cancellationToken);
                    }
                    catch (Exception ex) when (ex is not OperationCanceledException && File.Exists(CachePath))
                    {
                        App.Logger.Warn($"Datacenter list refresh failed, using stale cache: {ex.Message}");
                        json = await File.ReadAllTextAsync(CachePath, cancellationToken);
                    }
                }

                _entries = JsonHelper.Deserialize<List<DatacenterEntry>>(json) ?? [];
                return _entries;
            }
            finally
            {
                _gate.Release();
            }
        }

        public static async Task<List<string>> GetRegionsAsync(CancellationToken cancellationToken = default) =>
            [.. (await GetAsync(cancellationToken))
                .Select(e => RegionKey(e.Location?.City, e.Location?.Country))
                .Where(r => r != "Unknown")
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .Order(StringComparer.OrdinalIgnoreCase)];
    }
}
