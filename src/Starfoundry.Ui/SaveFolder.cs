using Starfoundry.Sim;

namespace Starfoundry.Ui;

/// <summary>One file of the saves folder: its id (the file name without the extension), what it says about itself, or why it cannot be used.</summary>
public sealed record SaveEntry(string Id, long Length, DateTime ModifiedUtc, SaveMeta? Meta, SaveProblem? Problem)
{
    public bool Readable => Meta is not null;

    /// <summary>The time shown and sorted by: the save's own time, or the file's when the header is unreadable.</summary>
    public long When => Meta is { SavedAt: > 0 } m ? m.SavedAt : new DateTimeOffset(ModifiedUtc, TimeSpan.Zero).ToUnixTimeSeconds();
}

/// <summary>What the one-time wrap of the old single-file saves did.</summary>
public sealed record MigrationReport(int Wrapped, int Unreadable, IReadOnlyList<string> Moved)
{
    public static readonly MigrationReport None = new(0, 0, Array.Empty<string>());
}

/// <summary>
/// The saves folder, engine-free (plain <c>System.IO</c>; the game points it at <c>user://saves</c>): one <c>.sfslot</c> file per save, written through a
/// temporary file and an atomic rename so a crash or a full disk never leaves a half-written slot, with the previous contents of an overwritten
/// slot kept as <c>.bak</c>. Nothing here deletes a file except <see cref="Delete"/>, <see cref="PruneCheckpoints"/> (checkpoints that rotated out
/// of their ring) and <see cref="RemoveStaleTemp"/> (temporary files of an interrupted write).
/// </summary>
public sealed class SaveFolder
{
    private const string Temp = ".tmp", Backup = ".bak", LegacyExtension = ".sfsave", LegacyFolder = "legacy";

    private readonly string _root;
    private readonly Dictionary<string, (long Length, DateTime Modified, SaveEntry Entry)> _cache = new(StringComparer.Ordinal);

    public SaveFolder(string root) => _root = root;

    public string Root => _root;

    public string PathOf(string id) => Path.Combine(_root, id + SaveFile.Extension);

    public bool Exists(string id) => SaveNaming.IsValidId(id) && File.Exists(PathOf(id));

    /// <summary>Every slot file, newest first. A file that cannot be read is listed with its problem instead of being skipped or deleted.</summary>
    public IReadOnlyList<SaveEntry> List()
    {
        var entries = new List<SaveEntry>();
        if (!Directory.Exists(_root)) return entries;
        var seen = new HashSet<string>(StringComparer.Ordinal);
        foreach (string file in Directory.EnumerateFiles(_root, "*" + SaveFile.Extension))
        {
            string id = Path.GetFileNameWithoutExtension(file);
            if (!SaveNaming.IsValidId(id)) continue;
            var info = new FileInfo(file);
            seen.Add(id);
            if (_cache.TryGetValue(id, out var hit) && hit.Length == info.Length && hit.Modified == info.LastWriteTimeUtc) { entries.Add(hit.Entry); continue; }
            SaveEntry entry;
            try
            {
                var parts = SaveFile.Unpack(File.ReadAllBytes(file));
                entry = new SaveEntry(id, info.Length, info.LastWriteTimeUtc, parts.Meta, null);
            }
            catch (SaveFileException e) { entry = new SaveEntry(id, info.Length, info.LastWriteTimeUtc, null, e.Problem); }
            catch (IOException) { entry = new SaveEntry(id, info.Length, info.LastWriteTimeUtc, null, SaveProblem.Truncated); }
            _cache[id] = (info.Length, info.LastWriteTimeUtc, entry);
            entries.Add(entry);
        }
        foreach (string gone in _cache.Keys.Where(k => !seen.Contains(k)).ToList()) _cache.Remove(gone);
        entries.Sort((a, b) => b.When != a.When ? b.When.CompareTo(a.When) : string.CompareOrdinal(a.Id, b.Id));
        return entries;
    }

    public SaveEntry? Find(string id) => List().FirstOrDefault(e => e.Id == id);

    /// <summary>Reads and verifies one slot. Throws <see cref="FileNotFoundException"/> or <see cref="SaveFileException"/>.</summary>
    public SaveParts Read(string id)
    {
        if (!SaveNaming.IsValidId(id) || !File.Exists(PathOf(id))) throw new FileNotFoundException("No such save.", id);
        return SaveFile.Unpack(File.ReadAllBytes(PathOf(id)));
    }

    /// <summary>Writes a slot: temporary file, flush to disk, the old contents copied to <c>.bak</c>, atomic rename over the slot. Returns the id.</summary>
    public string Write(string id, SaveParts parts)
    {
        if (!SaveNaming.IsValidId(id)) throw new ArgumentException("Not a valid save id.", nameof(id));
        Directory.CreateDirectory(_root);
        string path = PathOf(id), tmp = path + Temp;
        byte[] bytes = SaveFile.Pack(parts);
        using (var f = new FileStream(tmp, FileMode.Create, FileAccess.Write, FileShare.None))
        {
            f.Write(bytes);
            f.Flush(true);
        }
        if (new FileInfo(tmp).Length != bytes.Length) throw new IOException("The temporary save file was not written completely.");
        if (File.Exists(path)) File.Copy(path, path + Backup, overwrite: true);
        File.Move(tmp, path, overwrite: true);
        _cache.Remove(id);
        return id;
    }

    /// <summary>Deletes one slot and its <c>.bak</c>. False when it did not exist.</summary>
    public bool Delete(string id)
    {
        if (!SaveNaming.IsValidId(id) || !File.Exists(PathOf(id))) return false;
        File.Delete(PathOf(id));
        if (File.Exists(PathOf(id) + Backup)) File.Delete(PathOf(id) + Backup);
        _cache.Remove(id);
        return true;
    }

    /// <summary>Gives a slot another name (the file keeps its id). Returns the new name, made unique among the other saves.</summary>
    public string Rename(string id, string newName)
    {
        var parts = Read(id);
        string name = SaveNaming.UniqueName(List().Where(e => e.Id != id && e.Meta is not null).Select(e => e.Meta!.Name), newName);
        Write(id, parts with { Meta = parts.Meta with { Name = name } });
        return name;
    }

    /// <summary>Copies a slot into a new manual save (a new id, a name unique among the saves). Returns the new id.</summary>
    public string Duplicate(string id, string? newName = null)
    {
        var parts = Read(id);
        var all = List();
        string name = SaveNaming.UniqueName(all.Where(e => e.Meta is not null).Select(e => e.Meta!.Name), newName ?? parts.Meta.Name);
        string copy = SaveNaming.UniqueId(Exists, SaveNaming.Slug(name));
        Write(copy, parts with { Meta = parts.Meta with { Name = name, Kind = SaveKind.Manual, Reason = SaveReasons.Copy } });
        return copy;
    }

    /// <summary>
    /// Deletes the checkpoints of a game that are beyond the newest <paramref name="keep"/> (see <see cref="SaveRotation"/>). Manual, quick and victory saves
    /// and files that cannot be read are never touched. Returns the ids removed.
    /// </summary>
    public IReadOnlyList<string> PruneCheckpoints(string gameId, int keep)
    {
        var doomed = SaveRotation.ToPrune(List(), gameId, keep);
        foreach (string id in doomed) Delete(id);
        return doomed;
    }

    /// <summary>Removes the temporary files an interrupted write left behind (they are never a valid save).</summary>
    public int RemoveStaleTemp()
    {
        if (!Directory.Exists(_root)) return 0;
        int n = 0;
        foreach (string f in Directory.EnumerateFiles(_root, "*" + Temp)) { try { File.Delete(f); n++; } catch (IOException) { } }
        return n;
    }

    /// <summary>
    /// The one-time wrap of the single-file saves of earlier versions (<c>quicksave.sfsave</c>, <c>autosave.sfsave</c>, <c>autosave.prev.sfsave</c>,
    /// <c>victory.sfsave</c>, any other <c>*.sfsave</c>, with their <c>.goals</c> beside them) into slots. Each original is moved into <c>legacy/</c>,
    /// never deleted, so nothing is lost even if the wrap or the new files turn out wrong. Safe to run on every start: with no old file it does nothing.
    /// </summary>
    public MigrationReport MigrateLegacy(long nowUnix)
    {
        if (!Directory.Exists(_root)) return MigrationReport.None;
        var legacy = Directory.EnumerateFiles(_root, "*" + LegacyExtension).OrderBy(f => f, StringComparer.Ordinal).ToList();
        if (legacy.Count == 0) return MigrationReport.None;
        int wrapped = 0, unreadable = 0;
        var moved = new List<string>();
        string old = Path.Combine(_root, LegacyFolder);
        foreach (string file in legacy)
        {
            string stem = Path.GetFileNameWithoutExtension(file);
            try
            {
                byte[] payload = File.ReadAllBytes(file);
                var info = SaveGame.Peek(new MemoryStream(payload));
                string goalsPath = Path.Combine(_root, stem + ".goals");
                string goals = File.Exists(goalsPath) ? File.ReadAllText(goalsPath) : "";
                var kind = stem switch { "quicksave" => SaveKind.Quick, "victory" => SaveKind.Victory, _ when stem.StartsWith("autosave", StringComparison.Ordinal) => SaveKind.Auto, _ => SaveKind.Manual };
                string id = SaveNaming.LegacyId(stem);
                if (Exists(id)) id = SaveNaming.UniqueId(Exists, id);
                string gameId = "legacy-" + info.GalaxySeed.ToString("x", System.Globalization.CultureInfo.InvariantCulture) + (info.ScenarioId is null ? "" : "-" + SaveNaming.Slug(info.ScenarioId));
                var meta = new SaveMeta
                {
                    Name = LegacyName(stem),
                    Kind = kind,
                    Reason = SaveReasons.Migrated,
                    GameId = gameId,
                    ScenarioId = info.ScenarioId,
                    Preset = info.Rules?.PresetId() ?? (info.Rules is { IsDefault: false } ? "custom" : ""),
                    Mode = (byte)info.Mode,
                    Seed = info.GalaxySeed,
                    Ticks = info.Ticks,
                    PlaySeconds = info.Ticks / (double)Units.TicksPerSecond,
                    SavedAt = new DateTimeOffset(File.GetLastWriteTimeUtc(file), TimeSpan.Zero).ToUnixTimeSeconds(),
                    ContentHash = SaveFile.ContentHashOf(payload),
                    LogVersion = SaveFile.LogVersionOf(payload),
                };
                Write(id, new SaveParts(meta, Array.Empty<byte>(), goals, payload));
                if (Read(id).Payload.Length != payload.Length) throw new IOException("The wrapped save does not match the original.");
                wrapped++;
            }
            catch (Exception e) when (e is InvalidDataException or IOException or EndOfStreamException or ArgumentException)
            {
                unreadable++;
            }
            Directory.CreateDirectory(old);
            foreach (string sibling in new[] { file, Path.Combine(_root, stem + ".goals"), file + Backup }.Where(File.Exists))
            {
                string target = Path.Combine(old, Path.GetFileName(sibling));
                if (File.Exists(target)) target = Path.Combine(old, nowUnix.ToString(System.Globalization.CultureInfo.InvariantCulture) + "-" + Path.GetFileName(sibling));
                File.Move(sibling, target);
                moved.Add(Path.GetFileName(sibling));
            }
        }
        return new MigrationReport(wrapped, unreadable, moved);
    }

    /// <summary>The name given to a wrapped file: none for the quick, automatic and victory saves (the interface names them in the player's language), the file name for the rest.</summary>
    private static string LegacyName(string stem) => stem is "quicksave" or "victory" || stem.StartsWith("autosave", StringComparison.Ordinal) ? "" : SaveNaming.CleanName(stem);
}
