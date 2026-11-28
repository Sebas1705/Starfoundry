using System;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Godot;
using Starfoundry.Sim;
using Starfoundry.Ui;

namespace Starfoundry.Game;

/// <summary>
/// <c>--saves-check [--ui-lang=es|en]</c>: the headless check of the save slots in the real game scene (ADR 0049). It uses a throwaway saves folder
/// (<c>user://saves-check</c>) and prints <c>SAVESCHECK PASS|FAIL</c> lines and <c>SAVESCHECK RESULT fails=N</c>; the exit code is 1 on any failure. It covers:
/// writing and loading a manual, a quick and an automatic save and the checksum after the replay; the ring of checkpoints; the end screen's target; the screen's
/// named nodes and its flows (a name that exists is not overwritten by accident, Overwrite and Delete ask first and No changes nothing, rename, duplicate);
/// a damaged file and a save of other content are refused with the file untouched; the migration of the single-file saves; and the layout of the screen in
/// Spanish and English at four window sizes (no text cut or overlapping, every named control on screen).
/// </summary>
public partial class Play
{
    private static readonly string[] CheckNodes =
    {
        "SavePanel", "Title", "Toolbar", "FilterAll", "FilterMine", "FilterCheckpoints", "ThisGameOnly", "SortNewest", "SortOldest", "SortName", "NewSaveRow", "NewSaveName", "SaveNewButton",
        "SaveList", "DetailScroll", "SaveStatus", "Actions", "LoadButton", "OverwriteButton", "RenameButton", "DuplicateButton", "DeleteButton", "BackButton", "ConfirmLayer", "ConfirmYes", "ConfirmNo", "RenameLayer", "RenameField", "RenameOk", "RenameCancel",
    };

    /// <summary>
    /// <c>--saves-shot=path</c> (run with a window, <c>--resolution 1280x720</c>): opens the Save/Load screen on the made-up fixture saves and saves four pictures,
    /// <c>path.list.png</c> (a long name selected), <c>path.checkpoint.png</c>, <c>path.damaged.png</c> and <c>path.confirm.png</c> (the delete question), then quits.
    /// </summary>
    private async void SavesShotRun(string path)
    {
        async Task Frames(int n) { for (int i = 0; i < n; i++) await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame); }
        void Shot(string name) { GetViewport().GetTexture().GetImage().SavePng(path + "." + name + ".png"); GD.Print("SHOT saved " + path + "." + name + ".png"); }
        await Frames(20);
        SaveStore.UseFolder("user://saves-audit");
        SaveFixtures.Populate(SaveStore.Folder, _content, _scenarios, _gameId);
        _wantCapture = false;
        await Frames(20);
        byte[] real = SaveStore.CaptureThumbnail(GetViewport()); // the game view, as a save written now would keep it
        string? realId = WriteSave(SaveKind.Manual, "A real screenshot", SaveReasons.Manual, silent: true);
        GD.Print($"SAVESHOT screenshot={real.Length} bytes file={(realId is null ? 0 : new FileInfo(SaveStore.Folder.PathOf(realId)).Length)} bytes");
        OpenSaves(SavesReturn.Game);
        await Frames(40);
        if (realId is not null) { _savesScreen?.SelectForTest(realId); await Frames(8); Shot("real"); }
        _savesScreen?.SelectForTest("base-with-a-very-long-name");
        await Frames(10);
        Shot("list");
        _savesScreen?.SelectForTest("auto-1");
        await Frames(6);
        Shot("checkpoint");
        _savesScreen?.SelectForTest("other-content");
        await Frames(6);
        Shot("damaged");
        _savesScreen?.SelectForTest("quick");
        _savesScreen?.AskDeleteForTest();
        await Frames(12);
        Shot("confirm");
        GetTree().Quit();
    }

    private async void SavesCheckRun()
    {
        int pass = 0, fails = 0;
        void Check(string name, bool ok, string detail = "")
        {
            if (ok) pass++; else fails++;
            GD.Print($"SAVESCHECK {(ok ? "PASS" : "FAIL")} {name}{(detail.Length > 0 ? " - " + detail : "")}");
        }
        async Task Frames(int n) { for (int i = 0; i < n; i++) await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame); }
        T? Node<T>(string name) where T : Node => _savesScreen?.FindChild(name, true, false) as T;
        void Press(string name) { if (Node<Button>(name) is { Disabled: false } b) b.EmitSignal(BaseButton.SignalName.Pressed); else GD.Print($"SAVESCHECK note: {name} is missing or disabled"); }

        try
        {
            await Frames(10);
            SaveStore.UseFolder("user://saves-check");
            var folder = SaveStore.Folder;
            if (Directory.Exists(folder.Root)) Directory.Delete(folder.Root, true);
            _noSaves = false;
            _paused = false;
            SaveStore.Skew = 0;

            // ---- a manual save, a change, a load: the replay gives the saved checksum -----------------------------------------------
            _world.Submit(Player, new Scan());
            _world.Step(45);
            ulong checksum1 = _world.Checksum();
            long tick1 = _world.Tick;
            string? one = WriteSave(SaveKind.Manual, "Check one", SaveReasons.Manual, silent: true);
            Check("manual save written", one is not null && folder.Exists(one), one ?? "null");
            var entry = one is null ? null : folder.Find(one);
            var meta = entry?.Meta;
            Check("manual save header", meta is { Name: "Check one", Kind: SaveKind.Manual } m0 && m0.Seed == _galaxySeed && m0.Ticks == tick1 && m0.GameId == _gameId && m0.ContentHash == SaveFile.ContentHashOf(folder.Read(one!).Payload), meta?.ToString() ?? "no header");
            Check("a fresh save is loadable by this build", meta is not null && CompatOf(meta) == SaveCompat.Ok);
            _world.Step(90);
            Check("the world moved on", _world.Checksum() != checksum1);
            bool loaded = LoadSave(one!);
            Check("load by replay gives the saved checksum", loaded && _world.Checksum() == checksum1 && _world.Tick == tick1, $"tick {_world.Tick} vs {tick1}");

            // ---- quick save and quick load use their own slot --------------------------------------------------------------------
            _world.Step(30);
            ulong checksumQuick = _world.Checksum();
            QuickSave();
            Check("quick save has its own slot", folder.Exists(SaveNaming.QuickId) && folder.Exists(one!));
            _world.Step(60);
            QuickLoad();
            Check("quick load restores the quick save", _world.Checksum() == checksumQuick);
            Check("quick save did not touch the manual one", folder.Read(one!).Meta.Ticks == tick1);

            // ---- checkpoints rotate -----------------------------------------------------------------------------------------------
            string[] reasons = { SaveReasons.Timer, SaveReasons.Dock, SaveReasons.Wave, SaveReasons.Station, SaveReasons.Research, SaveReasons.Jump, SaveReasons.Dock, SaveReasons.Timer };
            string? newestAuto = null;
            foreach (string reason in reasons)
            {
                SaveStore.Skew += 40;
                _world.Step(15);
                newestAuto = WriteSave(SaveKind.Auto, "", reason, silent: true);
            }
            var autos = folder.List().Where(e => e.Meta is { Kind: SaveKind.Auto } m && m.GameId == _gameId).ToList();
            Check("checkpoint ring keeps the newest few", autos.Count == SaveRotation.CheckpointsKept && autos[0].Id == newestAuto, $"{autos.Count} kept");
            Check("checkpoints never remove manual or quick saves", folder.Exists(one!) && folder.Exists(SaveNaming.QuickId));
            Check("the end screen goes back to the newest save of this game", LastCheckpoint()?.Id == newestAuto, LastCheckpoint()?.Id ?? "none");
            ulong checksumAuto = _world.Checksum();
            _world.Step(60);
            Check("a checkpoint loads", LoadSave(newestAuto!) && _world.Checksum() == checksumAuto);

            // ---- the screen: named nodes and flows --------------------------------------------------------------------------------
            OpenSaves(SavesReturn.Game);
            await Frames(8);
            Check("the screen opens and pauses", SavesOpen && _paused);
            var missing = CheckNodes.Where(n => _savesScreen?.FindChild(n, true, false) is null).ToList();
            Check("named nodes exist", missing.Count == 0, string.Join(",", missing));
            Check("rows are named after their slot", Node<Button>("Row_" + one) is not null && Node<Button>("Row_quick") is not null);
            int filesBefore = folder.List().Count;
            Node<LineEdit>("NewSaveName")!.Text = "check ONE";
            Press("SaveNewButton");
            await Frames(3);
            Check("typing the name of an existing save does not overwrite it", folder.List().Count == filesBefore && folder.Read(one!).Meta.Ticks == tick1 && Node<Label>("SaveStatus")!.Text.Length > 0, Node<Label>("SaveStatus")!.Text);
            Node<LineEdit>("NewSaveName")!.Text = "Check two";
            Press("SaveNewButton");
            await Frames(3);
            string? two = folder.List().FirstOrDefault(e => e.Meta?.Name == "Check two")?.Id;
            Check("save as new writes a new file", two is not null && folder.List().Count == filesBefore + 1, two ?? "none");

            _savesScreen!.SelectForTest(one!);
            long tickBeforeOverwrite = folder.Read(one!).Meta.Ticks;
            _world.Step(30);
            Press("OverwriteButton");
            await Frames(2);
            Check("overwrite asks first", Node<Control>("ConfirmLayer")!.Visible);
            Press("ConfirmNo");
            await Frames(2);
            Check("answering No changes nothing", !Node<Control>("ConfirmLayer")!.Visible && folder.Read(one!).Meta.Ticks == tickBeforeOverwrite);
            Press("OverwriteButton");
            await Frames(2);
            Press("ConfirmYes");
            await Frames(3);
            Check("answering Yes overwrites and keeps a backup", folder.Read(one!).Meta.Ticks == _world.Tick && folder.Read(one!).Meta.Name == "Check one" && File.Exists(folder.PathOf(one!) + ".bak"));

            _savesScreen.SelectForTest(two!);
            Press("DuplicateButton");
            await Frames(3);
            string? copy = folder.List().FirstOrDefault(e => e.Meta?.Name == "Check two (2)")?.Id;
            Check("duplicate makes a second, independently named file", copy is not null && folder.Read(copy).Payload.Length == folder.Read(two!).Payload.Length, copy ?? "none");

            _savesScreen.SelectForTest(two!);
            Press("RenameButton");
            await Frames(2);
            Check("rename opens its dialog", Node<Control>("RenameLayer")!.Visible);
            Node<LineEdit>("RenameField")!.Text = "Renamed two";
            Press("RenameOk");
            await Frames(3);
            Check("rename changes the name, not the file", folder.Read(two!).Meta.Name == "Renamed two" && folder.Exists(two!));

            _savesScreen.SelectForTest(copy!);
            Press("DeleteButton");
            await Frames(2);
            Check("delete asks first", Node<Control>("ConfirmLayer")!.Visible && folder.Exists(copy!));
            Press("ConfirmYes");
            await Frames(3);
            Check("delete removes the file after Yes", !folder.Exists(copy!));

            Check("filters and sorts", Filters());
            bool Filters()
            {
                _savesScreen!.SetFilterForTest(SaveFilter.Checkpoints, SaveSort.Oldest, true);
                int rows = _savesScreen.FindChildren("Row_*", "Button", true, false).Count;
                _savesScreen.SetFilterForTest(SaveFilter.Mine, SaveSort.Name, false);
                int mine = _savesScreen.FindChildren("Row_*", "Button", true, false).Count;
                _savesScreen.SetFilterForTest(SaveFilter.All, SaveSort.Newest, false);
                return rows == SaveRotation.CheckpointsKept && mine == folder.List().Count(e => e.Meta is { Kind: not SaveKind.Auto });
            }
            CloseSaves();
            await Frames(3);
            Check("closing resumes the game", !SavesOpen && !_paused);

            // ---- a damaged file and a save of other content are refused and left alone ---------------------------------------------------
            string? victim = WriteSave(SaveKind.Manual, "Victim", SaveReasons.Manual, silent: true);
            byte[] bytes = File.ReadAllBytes(folder.PathOf(victim!));
            bytes[bytes.Length / 2] ^= 0x20;
            File.WriteAllBytes(folder.PathOf(victim!), bytes);
            Check("a damaged file is listed with its problem", folder.Find(victim!) is { Meta: null, Problem: SaveProblem.Corrupt });
            Check("a damaged file is refused", !LoadSave(victim!, out string? why) && why is { Length: > 0 }, why ?? "");
            Check("the damaged file is still there, untouched", File.Exists(folder.PathOf(victim!)) && File.ReadAllBytes(folder.PathOf(victim!)).SequenceEqual(bytes));
            var good = folder.Read(one!);
            folder.Write("alien", good with { Meta = good.Meta with { Name = "Alien", ContentHash = good.Meta.ContentHash ^ 1UL } });
            Check("a save of other content is flagged", CompatOf(folder.Read("alien").Meta) == SaveCompat.OtherContent);
            Check("a save of other content is refused and kept", !LoadSave("alien", out string? why2) && why2 is { Length: > 0 } && folder.Exists("alien"), why2 ?? "");
            folder.Write("future", good with { Meta = good.Meta with { Name = "Future", LogVersion = CommandCodec.CurrentLogVersion + 3 } });
            Check("a save from a newer format is refused and kept", CompatOf(folder.Read("future").Meta) == SaveCompat.NewerFormat && !LoadSave("future") && folder.Exists("future"));
            Check("the world was not changed by the refused loads", _world.Tick > 0);

            // ---- the migration of the single-file saves -------------------------------------------------------------------------------
            ulong checksumLegacy = _world.Checksum();
            var ms = new MemoryStream();
            SaveGame.Write(ms, _world, _content, _gameMode, _galaxySeed, _threatsOn, Player, _stationRules, _structuresOn, _scenario is not null || !Net.Rules.IsDefault ? Net.Rules : null, _scenario);
            File.WriteAllBytes(Path.Combine(folder.Root, "quicksave.sfsave"), ms.ToArray());
            var report = folder.MigrateLegacy(SaveStore.Now);
            string? wrapped = folder.List().FirstOrDefault(e => e.Meta is { Reason: SaveReasons.Migrated })?.Id;
            Check("an old single-file save is wrapped", report.Wrapped == 1 && wrapped is not null, wrapped ?? "none");
            Check("the original is kept in the legacy folder", File.Exists(Path.Combine(folder.Root, "legacy", "quicksave.sfsave")) && !File.Exists(Path.Combine(folder.Root, "quicksave.sfsave")));
            _world.Step(30);
            Check("a migrated save replays to the same checksum", LoadSave(wrapped!) && _world.Checksum() == checksumLegacy);

            // ---- what it costs (printed, for docs/prototypes/saves.md; not a pass or fail) -----------------------------------------------
            var clock = System.Diagnostics.Stopwatch.StartNew();
            const int Timed = 40;
            for (int i = 0; i < Timed; i++) WriteSave(SaveKind.Manual, $"time {i}", SaveReasons.Manual, silent: true);
            double writeMs = clock.Elapsed.TotalMilliseconds / Timed;
            clock.Restart();
            int listed = new SaveFolder(folder.Root).List().Count;
            double coldMs = clock.Elapsed.TotalMilliseconds;
            clock.Restart();
            folder.List();
            double warmMs = clock.Elapsed.TotalMilliseconds;
            var big = new byte[600_000];
            new Random(7).NextBytes(big); // the size of a long campaign's log: the container costs the same whatever the bytes are
            clock.Restart();
            byte[] packedBig = SaveFile.Pack(new SaveParts(folder.Read(one!).Meta, new byte[12_000], "", big));
            double packMs = clock.Elapsed.TotalMilliseconds;
            clock.Restart();
            SaveFile.Unpack(packedBig);
            double unpackMs = clock.Elapsed.TotalMilliseconds;
            GD.Print($"SAVESCHECK TIMING container pack={packMs:F1}ms unpack={unpackMs:F1}ms for a {big.Length} byte save and a 12 KB screenshot (SHA-256 included)");
            GD.Print($"SAVESCHECK TIMING write={writeMs:F1}ms (build, pack, checksum and atomic write; payload {folder.Read(one!).Payload.Length} bytes) list_cold({listed} files)={coldMs:F1}ms list_warm={warmMs:F2}ms");

            // ---- the layout of the screen at four window sizes in Spanish and English --------------------------------------------------
            var savedLang = Loc.Lang;
            var win = GetWindow();
            foreach (var lang in new[] { Language.Es, Language.En })
            {
                Loc.Lang = lang;
                foreach (var (w, h) in new[] { (800, 600), (1024, 600), (1280, 720), (1920, 1080) })
                {
                    SetWindowSize(win, w, h);
                    OpenSaves(SavesReturn.Game);
                    await Frames(10);
                    foreach (string sel in new[] { one!, two!, SaveNaming.QuickId, newestAuto!, "alien", victim! })
                    {
                        _savesScreen!.SelectForTest(sel);
                        await Frames(3);
                        var problems = CheckNodes.Where(n => n is not "ConfirmLayer" and not "ConfirmYes" and not "ConfirmNo" and not "RenameLayer" and not "RenameField" and not "RenameOk" and not "RenameCancel")
                            .Where(n => _savesScreen!.FindChild(n, true, false) is Control { Visible: true } c && !GetViewport().GetVisibleRect().Grow(1f).Encloses(c.GetGlobalRect()) && n is not "SaveList" and not "DetailScroll")
                            .ToList();
                        var audit = UiAudit.Run(_savesScreen!, GetViewport().GetVisibleRect()).Where(f => f.Error).Select(f => f.ToString()).ToList();
                        Check($"layout {lang} {w}x{h} ({sel})", problems.Count == 0 && audit.Count == 0, string.Join("; ", problems.Concat(audit)));
                    }
                    CloseSaves();
                    await Frames(2);
                }
            }
            Loc.Lang = savedLang;
            SetWindowSize(win, 1280, 720);
        }
        catch (Exception e)
        {
            Check("no exception", false, e.ToString());
        }
        GD.Print($"SAVESCHECK RESULT pass={pass} fails={fails} lang={Loc.Lang}");
        GetTree().Quit(fails == 0 ? 0 : 1);
    }
}
