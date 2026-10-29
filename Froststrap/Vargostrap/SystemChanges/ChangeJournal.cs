// SPDX-FileCopyrightText: 2026 Vargostrap
//
// SPDX-License-Identifier: MPL-2.0

namespace Froststrap.Vargostrap.SystemChanges
{
    internal enum ChangeKind
    {
        /// <summary>Another app's CPU priority was lowered. Data: original ProcessPriorityClass.</summary>
        ProcessPriority,
        /// <summary>Another app was suspended (paused).</summary>
        ProcessSuspend,
        /// <summary>A registry value was set. Data: original value, or null if it didn't exist.</summary>
        RegistryValue,
        /// <summary>A Windows service was stopped. Data: "running" if it was running before.</summary>
        ServiceStop,
        /// <summary>A Windows QoS policy was created (throttle or DSCP). Target: policy name.</summary>
        QosPolicy,
        /// <summary>The active power plan was switched. Data: the original plan's GUID.</summary>
        PowerPlan,
    }

    /// <summary>One applied system change and everything needed to undo it.</summary>
    internal sealed record JournalEntry(
        string Id, ChangeKind Kind, string Target, string? Data, string Description, DateTime AppliedUtc,
        int ProcessId = 0, long ProcessStartTicks = 0, string? ValueKind = null);

    /// <summary>
    /// Write-ahead journal of system changes. An entry is written <b>before</b> a change is made and
    /// removed only after it has been undone, so whatever happens (Roblox closes, Vargostrap crashes,
    /// the PC loses power) the journal lists exactly what still needs undoing.
    ///
    /// Two files: <c>ChangeJournal.json</c> for changes Vargostrap makes as the normal user, and
    /// <c>ChangeJournal.admin.json</c>, written only by the elevated helper for changes that need admin.
    /// </summary>
    internal sealed class ChangeJournal
    {
        private static readonly Lock _fileLock = new();
        private readonly string _path;

        public ChangeJournal(string path) => _path = path;

        public static ChangeJournal User(string baseDir) => new(Path.Combine(baseDir, "ChangeJournal.json"));

        public static ChangeJournal Admin(string baseDir) => new(Path.Combine(baseDir, "ChangeJournal.admin.json"));

        /// <summary>
        /// Settings the user switched on deliberately (Game Mode, Game Bar capture, adapter power saving).
        /// Never undone automatically: only when the user switches the tweak off, or by safe-mode reset.
        /// Entry ids start with the tweak's group name.
        /// </summary>
        public static ChangeJournal Persistent(string baseDir) => new(Path.Combine(baseDir, "PersistentChanges.json"));

        public static ChangeJournal PersistentAdmin(string baseDir) => new(Path.Combine(baseDir, "PersistentChanges.admin.json"));

        public bool IsEmpty => Read().Count == 0;

        public List<JournalEntry> Read()
        {
            lock (_fileLock)
                return ReadUnlocked();
        }

        public void Add(JournalEntry entry)
        {
            lock (_fileLock)
            {
                List<JournalEntry> all = ReadUnlocked();
                all.Add(entry);
                Write(all);
            }
        }

        public void Remove(string id)
        {
            lock (_fileLock)
            {
                List<JournalEntry> all = ReadUnlocked();
                if (all.RemoveAll(e => e.Id == id) > 0)
                    Write(all);
            }
        }

        /// <summary>Undoes every entry (newest first). Returns descriptions of anything that couldn't be undone.</summary>
        public List<string> RevertAll(Action<string>? log = null) => RevertWhere(_ => true, log);

        /// <summary>Undoes the matching entries (newest first). Returns descriptions of anything that couldn't be undone.</summary>
        public List<string> RevertWhere(Func<JournalEntry, bool> match, Action<string>? log = null)
        {
            List<string> failures = [];

            foreach (JournalEntry entry in Read().Where(match).OrderByDescending(e => e.AppliedUtc))
            {
                string? error = SystemActions.Revert(entry);
                if (error is null)
                {
                    Remove(entry.Id);
                    log?.Invoke($"Undone: {entry.Description}");
                }
                else
                {
                    // a target that's gone (process exited) can't be "still changed": drop it
                    if (error.StartsWith(SystemActions.GonePrefix, StringComparison.Ordinal))
                    {
                        Remove(entry.Id);
                        log?.Invoke($"Nothing to undo (already gone): {entry.Description}");
                    }
                    else
                    {
                        failures.Add($"{entry.Description}: {error}");
                        log?.Invoke($"Couldn't undo: {entry.Description}: {error}");
                    }
                }
            }

            return failures;
        }

        private List<JournalEntry> ReadUnlocked()
        {
            List<JournalEntry> result = [];
            if (!File.Exists(_path))
                return result;

            try
            {
                using JsonDocument doc = JsonDocument.Parse(File.ReadAllText(_path));
                foreach (JsonElement e in doc.RootElement.EnumerateArray())
                {
                    result.Add(new JournalEntry(
                        e.GetProperty("id").GetString()!,
                        Enum.Parse<ChangeKind>(e.GetProperty("kind").GetString()!),
                        e.GetProperty("target").GetString()!,
                        e.TryGetProperty("data", out JsonElement d) && d.ValueKind == JsonValueKind.String ? d.GetString() : null,
                        e.GetProperty("description").GetString()!,
                        e.GetProperty("appliedUtc").GetDateTime(),
                        e.TryGetProperty("pid", out JsonElement pid) ? pid.GetInt32() : 0,
                        e.TryGetProperty("startTicks", out JsonElement st) ? st.GetInt64() : 0,
                        e.TryGetProperty("valueKind", out JsonElement vk) && vk.ValueKind == JsonValueKind.String ? vk.GetString() : null));
                }
            }
            catch (Exception ex)
            {
                // Never silently drop pending undos: keep the unreadable file for inspection.
                string backup = _path + $".unreadable-{DateTime.Now:yyyyMMddHHmmss}";
                try { File.Copy(_path, backup); } catch { /* best effort */ }
                throw new InvalidDataException($"Change journal {_path} is unreadable (copied to {backup}): {ex.Message}", ex);
            }

            return result;
        }

        private void Write(List<JournalEntry> all)
        {
            Directory.CreateDirectory(Path.GetDirectoryName(_path)!);

            using var stream = new MemoryStream();
            using (var w = new Utf8JsonWriter(stream, new JsonWriterOptions { Indented = true }))
            {
                w.WriteStartArray();
                foreach (JournalEntry e in all)
                {
                    w.WriteStartObject();
                    w.WriteString("id", e.Id);
                    w.WriteString("kind", e.Kind.ToString());
                    w.WriteString("target", e.Target);
                    if (e.Data is null) w.WriteNull("data"); else w.WriteString("data", e.Data);
                    w.WriteString("description", e.Description);
                    w.WriteString("appliedUtc", e.AppliedUtc);
                    if (e.ProcessId != 0) w.WriteNumber("pid", e.ProcessId);
                    if (e.ProcessStartTicks != 0) w.WriteNumber("startTicks", e.ProcessStartTicks);
                    if (e.ValueKind is not null) w.WriteString("valueKind", e.ValueKind);
                    w.WriteEndObject();
                }
                w.WriteEndArray();
            }

            // write-then-rename so a crash mid-write can't corrupt the journal
            string temp = _path + ".tmp";
            File.WriteAllBytes(temp, stream.ToArray());
            File.Move(temp, _path, overwrite: true);
        }
    }
}
