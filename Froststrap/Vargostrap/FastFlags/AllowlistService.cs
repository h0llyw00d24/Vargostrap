// SPDX-FileCopyrightText: 2026 Vargostrap
//
// SPDX-License-Identifier: MPL-2.0

namespace Froststrap.Vargostrap.FastFlags
{
    internal enum AllowlistOrigin
    {
        /// <summary>Snapshot compiled into Vargostrap. Used only when nothing better is available.</summary>
        BuiltIn,
        /// <summary>Loaded from the on-disk cache written by a previous successful refresh.</summary>
        Cache,
        /// <summary>Fetched during this session.</summary>
        Live
    }

    internal sealed record AllowlistViolation(string Flag, string? CaseMismatchOf);

    /// <summary>
    /// Roblox's Fast Flag Allowlist: only these flags in ClientAppSettings.json take effect, the rest are
    /// silently ignored. Roblox publishes the list as a DevForum post (no machine-readable endpoint), so
    /// by default this reads the copy Froststrap mirrors in its signed config/Data.json.
    ///
    /// Updating the source: set <c>VargostrapSettings.AllowlistSourceUrl</c> (UI: FPS Optimization page)
    /// to a URL returning any of:
    ///   * a JSON array of flag names: <c>["FFlagX", "DFIntY"]</c>
    ///   * an object with an <c>Allowed</c> (or <c>flags</c>) array
    ///   * Froststrap's Data.json shape: <c>{ "allowedFastFlags": "&lt;base64 of {\"Allowed\": [...]}&gt;" }</c>
    /// and refresh. When Roblox changes the list, also update <see cref="BuiltInSnapshot"/>.
    /// </summary>
    internal sealed class AllowlistService
    {
        public const string OfficialListUrl = "https://devforum.roblox.com/t/allowlist-for-local-client-configuration-via-fast-flags/3966569";

        public const string BuiltInSnapshotDate = "2026-10-29";

        // Verified against the official DevForum post and Froststrap's Data.json on BuiltInSnapshotDate.
        public static readonly IReadOnlyList<string> BuiltInSnapshot =
        [
            // Geometry
            "DFIntCSGLevelOfDetailSwitchingDistance",
            "DFIntCSGLevelOfDetailSwitchingDistanceL12",
            "DFIntCSGLevelOfDetailSwitchingDistanceL23",
            "DFIntCSGLevelOfDetailSwitchingDistanceL34",

            // Rendering
            "FFlagHandleAltEnterFullscreenManually",
            "DFFlagTextureQualityOverrideEnabled",
            "DFIntTextureQualityOverride",
            "FIntDebugForceMSAASamples",
            "DFFlagDisableDPIScale",
            "FFlagDebugGraphicsPreferD3D11",
            "FFlagDebugSkyGray",
            "DFFlagDebugPauseVoxelizer",
            "DFIntDebugFRMQualityLevelOverride",
            "FIntFRMMaxGrassDistance",
            "FIntFRMMinGrassDistance",
            "FFlagDebugGraphicsPreferVulkan",
            "FFlagDebugGraphicsPreferOpenGL",

            // User Interface
            "FIntGrassMovementReducedMotionFactor",
        ];

        private static readonly TimeSpan StaleAfter = TimeSpan.FromDays(1);

        private readonly Lock _lock = new();
        private HashSet<string> _flags;
        private Dictionary<string, string> _flagsByUppercase;

        public AllowlistOrigin Origin { get; private set; }
        public DateTime? FetchedAtUtc { get; private set; }
        public string Source { get; private set; }
        public string? LastError { get; private set; }

        public bool IsStale => Origin != AllowlistOrigin.Live
            && (FetchedAtUtc is null || DateTime.UtcNow - FetchedAtUtc.Value > StaleAfter);

        public IReadOnlyCollection<string> Flags
        {
            get { lock (_lock) return [.. _flags]; }
        }

        /// <summary>Raised (on a background thread) whenever the list or its status changes.</summary>
        public event EventHandler? Changed;

        private static string CachePath => Path.Combine(Paths.Cache, "fflag-allowlist.json");

        public AllowlistService()
        {
            _flags = [.. BuiltInSnapshot];
            _flagsByUppercase = BuildUppercaseIndex(_flags);
            Origin = AllowlistOrigin.BuiltIn;
            Source = $"Built-in snapshot ({BuiltInSnapshotDate})";

            TryLoadCache();
        }

        public bool IsAllowed(string flag)
        {
            lock (_lock) return _flags.Contains(flag);
        }

        /// <summary>Returns every flag in <paramref name="flagNames"/> that Roblox will ignore.</summary>
        public List<AllowlistViolation> FindViolations(IEnumerable<string> flagNames)
        {
            List<AllowlistViolation> violations = [];

            lock (_lock)
            {
                foreach (string flag in flagNames.Distinct(StringComparer.Ordinal).OrderBy(x => x, StringComparer.Ordinal))
                {
                    if (_flags.Contains(flag))
                        continue;

                    _flagsByUppercase.TryGetValue(flag.ToUpperInvariant(), out string? caseMatch);
                    violations.Add(new AllowlistViolation(flag, caseMatch));
                }
            }

            return violations;
        }

        public async Task<bool> RefreshAsync(CancellationToken cancellationToken = default)
        {
            string customUrl = VargoApp.Settings.Prop.AllowlistSourceUrl.Trim();

            try
            {
                HashSet<string> fetched;
                string source;

                if (string.IsNullOrEmpty(customUrl))
                {
                    fetched = await FetchSignedRemoteDataAsync(cancellationToken);
                    source = "Froststrap config (signed Data.json)";
                }
                else
                {
                    if (!Uri.TryCreate(customUrl, UriKind.Absolute, out Uri? uri) || uri.Scheme != Uri.UriSchemeHttps)
                        throw new InvalidOperationException("The allowlist source must be an https:// URL.");

                    string body = await App.HttpClient.GetStringAsync(uri, cancellationToken);
                    fetched = Parse(body);
                    source = uri.Host + uri.AbsolutePath;
                }

                if (fetched.Count == 0)
                    throw new InvalidDataException("The source returned an empty allowlist.");

                lock (_lock)
                {
                    _flags = fetched;
                    _flagsByUppercase = BuildUppercaseIndex(fetched);
                    Origin = AllowlistOrigin.Live;
                    FetchedAtUtc = DateTime.UtcNow;
                    Source = source;
                    LastError = null;
                }

                WriteCache();
                App.Logger.Info($"FastFlag allowlist refreshed from {source}: {fetched.Count} flags");
                Changed?.Invoke(this, EventArgs.Empty);
                return true;
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                App.Logger.Error($"FastFlag allowlist refresh failed: {ex.Message}");
                lock (_lock) LastError = ex.Message;
                Changed?.Invoke(this, EventArgs.Empty);
                return false;
            }
        }

        /// <summary>Refreshes in the background if the current list is older than a day.</summary>
        public void RefreshIfStale()
        {
            if (IsStale)
                _ = Task.Run(() => RefreshAsync());
        }

        private static async Task<HashSet<string>> FetchSignedRemoteDataAsync(CancellationToken cancellationToken)
        {
            Task<byte[]> dataTask = App.HttpClient.GetByteArrayAsync(new Uri(App.ProjectRemoteDataLink), cancellationToken);
            Task<byte[]> sigTask = App.HttpClient.GetByteArrayAsync(new Uri($"{App.ProjectRemoteDataLink}.p256.sig"), cancellationToken);
            await Task.WhenAll(dataTask, sigTask);

            if (!RemoteDataManager.VerifyEcdsa(await dataTask, await sigTask, RemoteDataManager.ConfigPublicKey))
                throw new InvalidDataException("Data.json signature check failed; refusing to use it.");

            return Parse(Encoding.UTF8.GetString(await dataTask));
        }

        internal static HashSet<string> Parse(string json)
        {
            using JsonDocument doc = JsonDocument.Parse(json, new JsonDocumentOptions { CommentHandling = JsonCommentHandling.Skip, AllowTrailingCommas = true });
            return ParseElement(doc.RootElement, depth: 0);
        }

        private static HashSet<string> ParseElement(JsonElement element, int depth)
        {
            if (depth > 2)
                throw new InvalidDataException("Allowlist data is nested too deeply.");

            HashSet<string> result = new(StringComparer.Ordinal);

            switch (element.ValueKind)
            {
                case JsonValueKind.Array:
                    foreach (JsonElement item in element.EnumerateArray())
                    {
                        string? flag = item.ValueKind == JsonValueKind.String ? item.GetString()?.Trim() : null;
                        if (!string.IsNullOrEmpty(flag))
                            result.Add(flag);
                    }
                    return result;

                case JsonValueKind.Object:
                    foreach (string key in (string[])["Allowed", "allowed", "flags", "Flags"])
                    {
                        if (element.TryGetProperty(key, out JsonElement list) && list.ValueKind == JsonValueKind.Array)
                            return ParseElement(list, depth + 1);
                    }

                    if (element.TryGetProperty("allowedFastFlags", out JsonElement encoded) && encoded.ValueKind == JsonValueKind.String)
                    {
                        string base64 = encoded.GetString()!.Trim().Replace("\n", "", StringComparison.Ordinal).Replace("\r", "", StringComparison.Ordinal);
                        return Parse(Encoding.UTF8.GetString(Convert.FromBase64String(base64)));
                    }

                    throw new InvalidDataException("No 'Allowed', 'flags' or 'allowedFastFlags' field found.");

                default:
                    throw new InvalidDataException($"Unexpected JSON {element.ValueKind}; expected an array or object.");
            }
        }

        private static Dictionary<string, string> BuildUppercaseIndex(IEnumerable<string> flags)
        {
            Dictionary<string, string> index = [];
            foreach (string flag in flags)
                index.TryAdd(flag.ToUpperInvariant(), flag);
            return index;
        }

        private void TryLoadCache()
        {
            try
            {
                if (!File.Exists(CachePath))
                    return;

                using JsonDocument doc = JsonDocument.Parse(File.ReadAllText(CachePath));
                JsonElement root = doc.RootElement;

                HashSet<string> flags = ParseElement(root.GetProperty("flags"), depth: 1);
                if (flags.Count == 0)
                    return;

                _flags = flags;
                _flagsByUppercase = BuildUppercaseIndex(flags);
                Origin = AllowlistOrigin.Cache;
                FetchedAtUtc = root.GetProperty("fetchedAtUtc").GetDateTime().ToUniversalTime();
                Source = root.GetProperty("source").GetString() ?? "cache";
            }
            catch (Exception ex)
            {
                App.Logger.Warn($"Ignoring unreadable FastFlag allowlist cache: {ex.Message}");
            }
        }

        private void WriteCache()
        {
            try
            {
                Directory.CreateDirectory(Path.GetDirectoryName(CachePath)!);

                using var stream = new MemoryStream();
                using (var writer = new Utf8JsonWriter(stream, new JsonWriterOptions { Indented = true }))
                {
                    writer.WriteStartObject();
                    lock (_lock)
                    {
                        writer.WriteString("source", Source);
                        writer.WriteString("fetchedAtUtc", FetchedAtUtc ?? DateTime.UtcNow);
                        writer.WriteStartArray("flags");
                        foreach (string flag in _flags.OrderBy(x => x, StringComparer.Ordinal))
                            writer.WriteStringValue(flag);
                        writer.WriteEndArray();
                    }
                    writer.WriteEndObject();
                }

                File.WriteAllBytes(CachePath, stream.ToArray());
            }
            catch (Exception ex)
            {
                App.Logger.Warn($"Could not write FastFlag allowlist cache: {ex.Message}");
            }
        }
    }
}
