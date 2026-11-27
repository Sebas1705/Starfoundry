using Starfoundry.Sim;
using Starfoundry.Ui;
using Xunit;

namespace Starfoundry.Sim.Tests;

/// <summary>The save slots (ADR 0047): the container, the folder (atomic writes, rotation, rename, duplicate, delete), the migration of the old single files, the checkpoint watcher and the list.</summary>
public class SaveFilesTests : IDisposable
{
    private static readonly ContentDb C = TestContent.Core;
    private const ulong Seed = 20261120UL;
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "sf-saves-test-" + Guid.NewGuid().ToString("N")[..8]);

    public void Dispose()
    {
        try { if (Directory.Exists(_dir)) Directory.Delete(_dir, true); } catch (IOException) { }
    }

    private SaveFolder Folder() => new(_dir);

    private static SaveMeta Meta(string name, SaveKind kind = SaveKind.Manual, string game = "g1", long at = 1000, string reason = SaveReasons.Manual) =>
        new() { Name = name, Kind = kind, Reason = reason, GameId = game, SavedAt = at, Seed = 42, Ticks = 900, Wave = 2, System = "Vega-3", Preset = "normal", PlaySeconds = 65.5, ContentHash = 0xABCDEF0123456789UL, LogVersion = CommandCodec.CurrentLogVersion };

    private static SaveParts Parts(string name, SaveKind kind = SaveKind.Manual, string game = "g1", long at = 1000, string reason = SaveReasons.Manual, byte[]? payload = null) =>
        new(Meta(name, kind, game, at, reason), new byte[] { 1, 2, 3 }, "goals text", payload ?? new byte[] { 9, 8, 7, 6 });

    private static (byte[] Bytes, ulong Checksum, long Tick) RealSave(bool threats = false, int steps = 90)
    {
        var w = SimWorld.CreateGalaxy(C, GameMode.Campaign, Seed, threats);
        w.Network.EnsurePlayer(0);
        w.Step(steps);
        var ms = new MemoryStream();
        SaveGame.Write(ms, w, C, GameMode.Campaign, Seed, threats, 0);
        return (ms.ToArray(), w.Checksum(), w.Tick);
    }

    // ---- the metadata text ------------------------------------------------------------------------------------------------

    [Fact]
    public void Meta_round_trips_and_ignores_unknown_keys_and_bad_values()
    {
        var m = Meta("Back\\slash = and more", SaveKind.Auto, "game-7", 1_700_000_000, SaveReasons.Wave) with { ScenarioId = "03_under_siege" };
        var back = SaveMetaText.Parse(SaveMetaText.Serialize(m));
        Assert.Equal(m, back);
        var tolerant = SaveMetaText.Parse("future=1\nticks=banana\nseed=-5\nkind=zzz\nplay=NaN\nname=ok\u0007\u0000x\n\n=novalue\nnoequals");
        Assert.Equal(0, tolerant.Ticks);
        Assert.Equal(0UL, tolerant.Seed);
        Assert.Equal(SaveKind.Manual, tolerant.Kind);
        Assert.Equal(0, tolerant.PlaySeconds);
        Assert.Equal("okx", tolerant.Name);
        Assert.Equal(new SaveMeta(), SaveMetaText.Parse(null) with { });
    }

    // ---- the container -------------------------------------------------------------------------------------------------

    [Fact]
    public void A_slot_round_trips_with_all_its_parts()
    {
        var p = Parts("First", payload: new byte[5000]);
        var back = SaveFile.Unpack(SaveFile.Pack(p));
        Assert.Equal(p.Meta, back.Meta);
        Assert.Equal(p.Thumbnail, back.Thumbnail);
        Assert.Equal(p.Goals, back.Goals);
        Assert.Equal(p.Payload, back.Payload);
    }

    [Fact]
    public void Any_flipped_byte_or_a_cut_file_is_detected_and_never_loaded()
    {
        byte[] good = SaveFile.Pack(Parts("Good", payload: Enumerable.Range(0, 400).Select(i => (byte)i).ToArray()));
        for (int i = 0; i < good.Length; i += 7)
        {
            var bad = (byte[])good.Clone();
            bad[i] ^= 0x10;
            var ex = Assert.Throws<SaveFileException>(() => SaveFile.Unpack(bad));
            Assert.Contains(ex.Problem, new[] { SaveProblem.Corrupt, SaveProblem.NotASave, SaveProblem.NewerFormat });
        }
        foreach (int cut in new[] { 0, 3, 5, 20, good.Length / 2, good.Length - 1 })
            Assert.Throws<SaveFileException>(() => SaveFile.Unpack(good[..cut]));
        Assert.Equal(SaveProblem.NotASave, Assert.Throws<SaveFileException>(() => SaveFile.Unpack(new byte[100])).Problem);
        var newer = (byte[])good.Clone();
        newer[4] = SaveFile.Format + 1;
        Assert.Equal(SaveProblem.NewerFormat, Assert.Throws<SaveFileException>(() => SaveFile.Unpack(newer)).Problem);
    }

    [Fact]
    public void A_replayed_slot_gives_the_same_checksum()
    {
        var (bytes, checksum, tick) = RealSave(threats: true, steps: 200);
        var packed = SaveFile.Pack(new SaveParts(Meta("Real") with { Ticks = tick, ContentHash = SaveFile.ContentHashOf(bytes), LogVersion = SaveFile.LogVersionOf(bytes) }, Array.Empty<byte>(), "", bytes));
        var parts = SaveFile.Unpack(packed);
        var world = SaveGame.Read(new MemoryStream(parts.Payload), C, out var info);
        Assert.Equal(checksum, world.Checksum());
        Assert.Equal(tick, world.Tick);
        Assert.Equal(tick, info.Ticks);
        Assert.Equal(C.Hash, parts.Meta.ContentHash);
        Assert.Equal(CommandCodec.CurrentLogVersion, parts.Meta.LogVersion);
    }

    // ---- the folder ------------------------------------------------------------------------------------------------------

    [Fact]
    public void Writing_is_atomic_and_keeps_the_previous_contents_when_overwriting()
    {
        var f = Folder();
        f.Write("one", Parts("One", payload: new byte[] { 1 }));
        Assert.False(File.Exists(f.PathOf("one") + ".tmp"));
        Assert.False(File.Exists(f.PathOf("one") + ".bak"));
        f.Write("one", Parts("One again", payload: new byte[] { 2 }));
        Assert.Equal(new byte[] { 2 }, f.Read("one").Payload);
        Assert.Equal(new byte[] { 1 }, SaveFile.Unpack(File.ReadAllBytes(f.PathOf("one") + ".bak")).Payload);
        Assert.False(File.Exists(f.PathOf("one") + ".tmp"));
    }

    [Fact]
    public void An_interrupted_write_leaves_the_slot_alone_and_a_stale_temp_file_is_cleaned()
    {
        var f = Folder();
        f.Write("one", Parts("One", payload: new byte[] { 1 }));
        File.WriteAllBytes(f.PathOf("one") + ".tmp", new byte[] { 0xFF, 0xEE }); // what a crash in the middle of a write leaves
        Assert.Single(f.List());
        Assert.Equal(new byte[] { 1 }, f.Read("one").Payload);
        Assert.Equal(1, f.RemoveStaleTemp());
        Assert.False(File.Exists(f.PathOf("one") + ".tmp"));
        Assert.Equal(new byte[] { 1 }, f.Read("one").Payload);
    }

    [Fact]
    public void A_damaged_file_is_listed_with_its_problem_and_left_in_place()
    {
        var f = Folder();
        f.Write("good", Parts("Good", at: 500));
        byte[] bad = SaveFile.Pack(Parts("Bad"));
        bad[bad.Length / 2] ^= 0x01;
        File.WriteAllBytes(f.PathOf("bad"), bad);
        File.WriteAllBytes(f.PathOf("cut"), SaveFile.Pack(Parts("Cut"))[..10]);
        var list = f.List();
        Assert.Equal(3, list.Count);
        Assert.Equal(SaveProblem.Corrupt, list.Single(e => e.Id == "bad").Problem);
        Assert.NotNull(list.Single(e => e.Id == "cut").Problem);
        Assert.NotNull(list.Single(e => e.Id == "good").Meta);
        Assert.True(File.Exists(f.PathOf("bad")) && File.Exists(f.PathOf("cut")));
        Assert.Empty(f.PruneCheckpoints("g1", 0)); // unreadable files are never rotated away
        Assert.True(File.Exists(f.PathOf("bad")));
    }

    [Fact]
    public void The_list_is_newest_first_and_ids_cannot_escape_the_folder()
    {
        var f = Folder();
        f.Write("old", Parts("Old", at: 100));
        f.Write("new", Parts("New", at: 300));
        f.Write("mid", Parts("Mid", at: 200));
        Assert.Equal(new[] { "new", "mid", "old" }, f.List().Select(e => e.Id));
        foreach (string evil in new[] { "../x", "a/b", "a\\b", "", ".hidden", "UPPER", "a b", new string('a', 70), "..", "x..y" })
        {
            Assert.False(SaveNaming.IsValidId(evil), evil);
            Assert.Throws<ArgumentException>(() => f.Write(evil, Parts("E")));
            Assert.False(f.Exists(evil));
            Assert.Throws<FileNotFoundException>(() => f.Read(evil));
            Assert.False(f.Delete(evil));
        }
        Assert.Empty(Directory.GetFiles(Path.GetDirectoryName(_dir)!, "x.sfslot"));
    }

    [Fact]
    public void Rename_and_duplicate_keep_names_unique_and_the_payload_intact()
    {
        var f = Folder();
        f.Write("alpha", Parts("Alpha", payload: new byte[] { 5, 5 }));
        f.Write("beta", Parts("Beta"));
        Assert.Equal("alpha (2)", f.Rename("beta", "alpha"));
        Assert.Equal("alpha (2)", f.Read("beta").Meta.Name);
        string copy = f.Duplicate("alpha");
        Assert.NotEqual("alpha", copy);
        var c = f.Read(copy);
        Assert.Equal(new byte[] { 5, 5 }, c.Payload);
        Assert.Equal(SaveKind.Manual, c.Meta.Kind);
        Assert.Equal("Alpha (3)", c.Meta.Name); // "Alpha" and "Alpha (2)" exist
        Assert.Equal("Alpha", f.Read("alpha").Meta.Name);
        Assert.Equal(new byte[] { 5, 5 }, f.Read("alpha").Payload);
        Assert.Throws<FileNotFoundException>(() => f.Rename("nothing", "x"));
        Assert.True(f.Delete("alpha"));
        Assert.False(f.Delete("alpha"));
        Assert.False(File.Exists(f.PathOf("alpha") + ".bak"));
        Assert.True(f.Exists(copy));
    }

    [Fact]
    public void Checkpoints_rotate_per_game_and_never_touch_manual_quick_or_other_games()
    {
        var f = Folder();
        for (int i = 0; i < 9; i++) f.Write($"auto-{i}", Parts("Checkpoint", SaveKind.Auto, "g1", 1000 + i, SaveReasons.Timer));
        for (int i = 0; i < 4; i++) f.Write($"other-{i}", Parts("Checkpoint", SaveKind.Auto, "g2", 50 + i, SaveReasons.Dock));
        f.Write("mine", Parts("Mine", SaveKind.Manual, "g1", 1));
        f.Write("quick", Parts("Quick", SaveKind.Quick, "g1", 2));
        f.Write("victory-g1", Parts("Victory", SaveKind.Victory, "g1", 3));
        var removed = f.PruneCheckpoints("g1", SaveRotation.CheckpointsKept);
        Assert.Equal(3, removed.Count);
        Assert.Equal(new[] { "auto-0", "auto-1", "auto-2" }.OrderBy(x => x), removed.OrderBy(x => x)); // the oldest three
        var left = f.List().Select(e => e.Id).ToHashSet();
        Assert.Equal(6, left.Count(i => i.StartsWith("auto-", StringComparison.Ordinal)));
        Assert.Equal(4, left.Count(i => i.StartsWith("other-", StringComparison.Ordinal)));
        Assert.Contains("mine", left); Assert.Contains("quick", left); Assert.Contains("victory-g1", left);
        Assert.Empty(f.PruneCheckpoints("g1", SaveRotation.CheckpointsKept)); // idempotent
    }

    // ---- the migration of the single-file saves --------------------------------------------------------------------------

    [Fact]
    public void The_old_single_files_are_wrapped_once_and_the_originals_are_kept()
    {
        Directory.CreateDirectory(_dir);
        var (quick, qChecksum, qTick) = RealSave(steps: 60);
        var (auto, aChecksum, _) = RealSave(steps: 120);
        File.WriteAllBytes(Path.Combine(_dir, "quicksave.sfsave"), quick);
        File.WriteAllBytes(Path.Combine(_dir, "autosave.sfsave"), auto);
        File.WriteAllBytes(Path.Combine(_dir, "victory.sfsave"), auto);
        File.WriteAllText(Path.Combine(_dir, "quicksave.goals"), "SFGOALS 1\ntick 60\n");
        File.WriteAllBytes(Path.Combine(_dir, "broken.sfsave"), new byte[] { 1, 2, 3, 4, 5 });
        var f = Folder();
        var report = f.MigrateLegacy(1_800_000_000);
        Assert.Equal(3, report.Wrapped);
        Assert.Equal(1, report.Unreadable);
        var list = f.List();
        Assert.Equal(new[] { "autosave", "quick", "victory" }, list.Select(e => e.Id).OrderBy(x => x));
        var q = f.Read("quick");
        Assert.Equal(SaveKind.Quick, q.Meta.Kind);
        Assert.Equal(qTick, q.Meta.Ticks);
        Assert.Equal(Seed, q.Meta.Seed);
        Assert.Equal(C.Hash, q.Meta.ContentHash);
        Assert.Equal(CommandCodec.CurrentLogVersion, q.Meta.LogVersion);
        Assert.StartsWith("SFGOALS", q.Goals, StringComparison.Ordinal);
        Assert.Equal(SaveKind.Auto, f.Read("autosave").Meta.Kind);
        Assert.Equal(SaveKind.Victory, f.Read("victory").Meta.Kind);
        Assert.All(list, e => Assert.Equal(SaveReasons.Migrated, e.Meta!.Reason));
        // The wrapped save replays to the same state as the original file.
        Assert.Equal(qChecksum, SaveGame.Read(new MemoryStream(q.Payload), C, out _).Checksum());
        Assert.Equal(aChecksum, SaveGame.Read(new MemoryStream(f.Read("autosave").Payload), C, out _).Checksum());
        // Nothing was deleted: the originals sit in legacy/.
        string old = Path.Combine(_dir, "legacy");
        foreach (string name in new[] { "quicksave.sfsave", "autosave.sfsave", "victory.sfsave", "quicksave.goals", "broken.sfsave" })
            Assert.True(File.Exists(Path.Combine(old, name)), name);
        Assert.Empty(Directory.GetFiles(_dir, "*.sfsave"));
        // And it is a one-time thing.
        Assert.Equal(MigrationReport.None, f.MigrateLegacy(1_800_000_001));
        Assert.Equal(3, f.List().Count);
    }

    [Fact]
    public void Migration_does_nothing_without_a_folder_or_old_files()
    {
        Assert.Equal(MigrationReport.None, Folder().MigrateLegacy(1));
        Directory.CreateDirectory(_dir);
        Assert.Equal(MigrationReport.None, Folder().MigrateLegacy(1));
    }

    // ---- compatibility ---------------------------------------------------------------------------------------------------

    [Fact]
    public void A_save_of_other_content_or_a_newer_log_is_judged_from_its_header_alone()
    {
        var m = Meta("x") with { ContentHash = 777, LogVersion = 26 };
        ulong? Known(string? scenario) => scenario is null or "known" ? 777UL : null;
        Assert.Equal(SaveCompat.Ok, SaveCompatibility.Check(m, Known, 26));
        Assert.Equal(SaveCompat.OtherContent, SaveCompatibility.Check(m with { ContentHash = 5 }, Known, 26));
        Assert.Equal(SaveCompat.NewerFormat, SaveCompatibility.Check(m with { LogVersion = 27 }, Known, 26));
        Assert.Equal(SaveCompat.Ok, SaveCompatibility.Check(m with { LogVersion = 12 }, Known, 26)); // older logs replay with their legacy rules
        Assert.Equal(SaveCompat.UnknownScenario, SaveCompatibility.Check(m with { ScenarioId = "gone" }, Known, 26));
        Assert.Equal(SaveCompat.Ok, SaveCompatibility.Check(m with { ScenarioId = "known" }, Known, 26));
        foreach (var c in new[] { SaveCompat.OtherContent, SaveCompat.UnknownScenario, SaveCompat.NewerFormat })
            Assert.DoesNotContain("⟦", Loc.T(SaveCompatibility.Key(c)));
        foreach (var p in Enum.GetValues<SaveProblem>())
            Assert.DoesNotContain("⟦", Loc.T(SaveCompatibility.Key(p)));
    }

    // ---- naming ----------------------------------------------------------------------------------------------------------

    [Theory]
    [InlineData("  Mi  partida\tfavorita  ", "Mi partida favorita")]
    [InlineData("a\u0007b\nc", "ab c")]
    [InlineData("", "")]
    public void Typed_names_are_cleaned(string typed, string expected) => Assert.Equal(expected, SaveNaming.CleanName(typed));

    [Fact]
    public void Slugs_are_file_safe_and_names_are_cut_to_length()
    {
        Assert.Equal("estacion-del-norte", SaveNaming.Slug("Estación del Norte!"));
        Assert.Equal("save", SaveNaming.Slug("???"));
        Assert.Equal("save", SaveNaming.Slug(null));
        Assert.True(SaveNaming.Slug(new string('x', 200)).Length <= 40);
        Assert.True(SaveNaming.IsValidId(SaveNaming.Slug("../../etc/passwd")));
        Assert.Equal(SaveMeta.MaxNameLength, SaveNaming.CleanName(new string('n', 200)).Length);
        string longName = new string('n', SaveMeta.MaxNameLength);
        Assert.True(SaveNaming.UniqueName(new[] { longName }, longName).Length <= SaveMeta.MaxNameLength);
        Assert.Equal("Dock (3)", SaveNaming.UniqueName(new[] { "dock", "Dock (2)" }, "Dock"));
        Assert.Equal("a-3", SaveNaming.UniqueId(id => id is "a" or "a-2", "a"));
        Assert.Equal("auto-1700000000-dock", SaveNaming.AutoId(1_700_000_000, "dock"));
        Assert.Equal("Under Siege - 12:05", SaveNaming.SuggestName("Under Siege", 725.4));
        Assert.True(SaveNaming.SuggestName(new string('s', 100), 5).Length <= SaveMeta.MaxNameLength);
        Assert.Equal("quick", SaveNaming.LegacyId("quicksave"));
        Assert.Equal("autosave-prev", SaveNaming.LegacyId("autosave.prev"));
    }

    // ---- the list and the buttons that pick a save -----------------------------------------------------------------------

    private static SaveEntry E(string id, SaveKind kind, string game, long at, bool broken = false) =>
        new(id, 10, DateTime.UnixEpoch.AddSeconds(at), broken ? null : Meta(id, kind, game, at), broken ? SaveProblem.Corrupt : null);

    [Fact]
    public void The_list_filters_by_kind_and_game_and_sorts_by_date_and_name()
    {
        var all = new[] { E("a", SaveKind.Manual, "g1", 10), E("b", SaveKind.Auto, "g1", 30), E("c", SaveKind.Quick, "g2", 20), E("d", SaveKind.Auto, "g2", 5), E("x", SaveKind.Manual, "g1", 40, broken: true) };
        Assert.Equal(new[] { "x", "b", "c", "a", "d" }, SaveList.Apply(all, SaveFilter.All, SaveSort.Newest).Select(e => e.Id));
        Assert.Equal(new[] { "d", "a", "c", "b", "x" }, SaveList.Apply(all, SaveFilter.All, SaveSort.Oldest).Select(e => e.Id));
        Assert.Equal(new[] { "c", "a" }, SaveList.Apply(all, SaveFilter.Mine, SaveSort.Newest).Select(e => e.Id));
        Assert.Equal(new[] { "b", "d" }, SaveList.Apply(all, SaveFilter.Checkpoints, SaveSort.Newest).Select(e => e.Id));
        Assert.Equal(new[] { "b", "a" }, SaveList.Apply(all, SaveFilter.All, SaveSort.Newest, "g1").Select(e => e.Id));
        Assert.Equal(new[] { "a", "b", "c", "d", "x" }, SaveList.Apply(all, SaveFilter.All, SaveSort.Name).Select(e => e.Id));
    }

    [Fact]
    public void Continue_goes_to_the_last_used_save_else_the_newest_loadable_and_never_to_a_broken_one()
    {
        var all = new[] { E("old", SaveKind.Manual, "g1", 10), E("new", SaveKind.Auto, "g1", 30), E("bad", SaveKind.Manual, "g1", 99, broken: true), E("alien", SaveKind.Manual, "g2", 50) };
        Func<SaveMeta, bool> ok = m => m.Name != "alien";
        Assert.Equal("old", SaveList.ContinueTarget(all, "old", ok)?.Id);
        Assert.Equal("new", SaveList.ContinueTarget(all, "gone", ok)?.Id);
        Assert.Equal("new", SaveList.ContinueTarget(all, null, ok)?.Id);
        Assert.Equal("new", SaveList.ContinueTarget(all, "bad", ok)?.Id);
        Assert.Equal("new", SaveList.ContinueTarget(all, "alien", ok)?.Id);
        Assert.Null(SaveList.ContinueTarget(all, null, _ => false));
        Assert.Null(SaveList.ContinueTarget(Array.Empty<SaveEntry>(), null, ok));
    }

    [Fact]
    public void The_end_screen_reloads_the_newest_save_of_the_same_game_not_a_victory()
    {
        var all = new[] { E("a", SaveKind.Auto, "g1", 10), E("q", SaveKind.Quick, "g1", 20), E("v", SaveKind.Victory, "g1", 40), E("other", SaveKind.Auto, "g2", 30) };
        Func<SaveMeta, bool> ok = _ => true;
        Assert.Equal("q", SaveList.LastCheckpoint(all, "g1", ok)?.Id);
        Assert.Equal("other", SaveList.LastCheckpoint(all, "g3", ok)?.Id); // an unknown game falls back to the newest save
        Assert.Equal("a", SaveList.LastCheckpoint(all, "g1", m => m.Kind == SaveKind.Auto && m.GameId == "g1")?.Id);
        Assert.Null(SaveList.LastCheckpoint(new[] { E("v", SaveKind.Victory, "g1", 1) }, "g1", ok));
    }

    [Fact]
    public void Every_save_text_exists_in_both_languages_with_the_same_placeholders()
    {
        static string Holes(string s) => string.Join(",", System.Text.RegularExpressions.Regex.Matches(s, @"\{\d+\}").Select(m => m.Value).Distinct().OrderBy(x => x));
        var texts = Loc.All.Where(p => p.Key.StartsWith("saves.", StringComparison.Ordinal) || p.Key.StartsWith("toast.loadFailed", StringComparison.Ordinal) || p.Key.StartsWith("menu.load", StringComparison.Ordinal)
            || p.Key is "toast.checkpoint" or "toast.quickSaved" or "toast.noQuick" or "toast.saved.named" or "end.loadLast" or "pause.saves" or "menu.noLoadable").ToList();
        Assert.True(texts.Count > 80);
        foreach (var (key, (es, en)) in texts)
        {
            Assert.False(string.IsNullOrWhiteSpace(es), key);
            Assert.False(string.IsNullOrWhiteSpace(en), key);
            Assert.Equal(Holes(es), Holes(en));
        }
        foreach (string reason in SaveReasons.All) Assert.True(Loc.Has("saves.reason." + reason), reason);
        foreach (var kind in Enum.GetValues<SaveKind>())
        {
            Assert.True(Loc.Has("saves.kind." + kind.ToString().ToLowerInvariant()), kind.ToString());
            Assert.True(Loc.Has("saves.title." + kind.ToString().ToLowerInvariant()), kind.ToString());
        }
        foreach (var p in Enum.GetValues<SaveProblem>()) Assert.True(Loc.Has("saves.short." + p.ToString().ToLowerInvariant()), p.ToString());
        foreach (var (id, _) in WorldRules.Presets) Assert.True(Loc.Has("difficulty." + id), id);
    }

    [Fact]
    public void Row_text_names_nameless_saves_and_lists_the_facts()
    {
        var quick = Meta("", SaveKind.Quick, reason: SaveReasons.QuickSave);
        var auto = Meta("", SaveKind.Auto, reason: SaveReasons.Wave);
        Assert.Equal(Loc.T("saves.title.quick"), SaveText.Title(quick));
        Assert.Equal(Loc.T("saves.title.auto") + " - " + Loc.T("saves.reason.wave"), SaveText.Title(auto));
        Assert.Equal("My base", SaveText.Title(Meta("My base")));
        string facts = SaveText.Facts(Meta("x") with { Seed = 123456789, Ticks = 30 * 754, Wave = 7, System = "Vega-3", Preset = "hard" }, null);
        Assert.Contains("123456789", facts);
        Assert.Contains("12:34", facts);
        Assert.Contains("Vega-3", facts);
        Assert.Contains(Loc.T("difficulty.hard"), facts);
        Assert.DoesNotContain("Vega", SaveText.Facts(Meta("x") with { System = "" }, "Under Siege"));
        Assert.Contains("Under Siege", SaveText.Facts(Meta("x"), "Under Siege"));
        Assert.Equal("", SaveText.PresetName(Meta("x") with { Preset = "" }));
        Assert.Equal(Loc.T("saves.preset.custom"), SaveText.PresetName(Meta("x") with { Preset = "custom" }));
    }

    [Fact]
    public void Dates_and_clocks_format_plainly()
    {
        Assert.Equal("-", SaveText.Date(0));
        Assert.Matches(@"^\d{4}-\d{2}-\d{2} \d{2}:\d{2}$", SaveText.Date(1_800_000_000));
        Assert.Equal("1:05", SaveText.Clock(65.9));
        Assert.Equal("1:01:05", SaveText.Clock(3665));
        Assert.Equal("0:00", SaveText.Clock(-4));
        Assert.Equal("1:00", SaveText.GameClock(30 * 60));
    }

    // ---- the checkpoint watcher ------------------------------------------------------------------------------------------

    private static CheckpointSample S(bool docked = false, bool warning = false, int stations = 0, int research = 0) => new(docked, warning, 0, stations, research);

    [Fact]
    public void The_first_sample_only_sets_the_baseline_and_the_timer_fires_after_five_minutes()
    {
        var w = new CheckpointWatcher();
        w.Reset(0);
        Assert.Null(w.Observe(S(docked: true, stations: 3), 1)); // loading a docked game is not news
        Assert.Null(w.Observe(S(docked: true, stations: 3), 100));
        Assert.Null(w.Observe(S(docked: true, stations: 3), 299));
        Assert.Equal(SaveReasons.Timer, w.Observe(S(docked: true, stations: 3), 300));
        Assert.Null(w.Observe(S(docked: true, stations: 3), 301));
        Assert.Equal(SaveReasons.Timer, w.Observe(S(docked: true, stations: 3), 600));
    }

    [Fact]
    public void Events_become_checkpoints_with_their_reason_and_never_closer_than_the_gap()
    {
        var w = new CheckpointWatcher();
        w.Reset(0);
        w.Observe(S(), 1);
        Assert.Equal(SaveReasons.Dock, w.Observe(S(docked: true), 100));
        Assert.Null(w.Observe(S(docked: false), 101));
        Assert.Null(w.Observe(S(docked: true), 102)); // docking again 2 s later waits for the gap...
        Assert.Equal(SaveReasons.Dock, w.Observe(S(docked: true), 131)); // ...and is written when it is over
        Assert.Equal(SaveReasons.Station, w.Observe(S(docked: true, stations: 1), 200));
        Assert.Equal(SaveReasons.Research, w.Observe(S(docked: true, stations: 1, research: 1), 240));
        Assert.Equal(SaveReasons.Wave, w.Observe(S(docked: true, stations: 1, research: 1, warning: true), 280));
    }

    [Fact]
    public void In_a_burst_the_most_important_event_wins_and_an_old_pending_event_is_dropped()
    {
        var w = new CheckpointWatcher();
        w.Reset(0);
        w.Observe(S(), 1);
        Assert.Equal(SaveReasons.Dock, w.Observe(S(docked: true), 100));
        Assert.Null(w.Observe(S(docked: true, research: 1), 105));
        Assert.Null(w.Observe(S(docked: true, research: 1, warning: true), 106));
        Assert.Equal(SaveReasons.Wave, w.Observe(S(docked: true, research: 1, warning: true), 131)); // the wave beat the research
        var d = new CheckpointWatcher();
        d.Reset(0);
        d.Observe(S(), 1);
        d.Wrote(1000); // the player saved: everything restarts from there
        Assert.Null(d.Observe(S(stations: 1), 1001));
        Assert.Equal(SaveReasons.Station, d.Observe(S(stations: 1), 1031));
    }

    [Fact]
    public void A_jump_checkpoint_is_asked_for_and_not_repeated_within_seconds()
    {
        var w = new CheckpointWatcher();
        w.Reset(0);
        Assert.False(w.Request(3));
        Assert.True(w.Request(50));
        Assert.False(w.Request(52));
        Assert.True(w.Request(60));
        w.Reset(500); // a load resets the baseline and the timer
        Assert.Null(w.Observe(S(), 501));
        Assert.Null(w.Observe(S(warning: true), 502)); // the baseline was taken at 501: a warning after it is an event, but the gap since the reset has not passed
        Assert.Equal(SaveReasons.Wave, w.Observe(S(warning: true), 531));
    }

    [Fact]
    public void The_last_save_setting_round_trips_and_ignores_bad_ids()
    {
        var s = GameSettings.Defaults("en") with { LastSave = "auto-1700000000-dock" };
        Assert.Equal("auto-1700000000-dock", GameSettings.Parse(s.Serialize()).LastSave);
        Assert.Equal("", GameSettings.Parse("last_save=../../x").LastSave);
        Assert.Equal("", GameSettings.Parse("language=en").LastSave);
        Assert.Contains(s.ToPairs(), p => p.Key == "last_save");
    }
}
