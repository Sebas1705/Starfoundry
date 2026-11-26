using System;
using System.Collections.Generic;
using System.Linq;
using Godot;
using Starfoundry.Sim;
using Starfoundry.Ui;

namespace Starfoundry.Game;

/// <summary>
/// The blueprint editor in the game (docs/prototypes/blueprint-editor.md): the full-screen 3D plan of the ship, opened from the ship computer's Designer tab
/// and by F at the shipyard console (unless the Classic designer setting is on), applied with one <see cref="DesignerApply"/>. This file also holds the headless
/// self-check <c>--blueprint-check</c> (prints PASS and FAIL lines) and the screenshot flag <c>--blueprint-shot=path</c> (implemented, to run with a window).
/// </summary>
public partial class Play
{
    private BlueprintEditor? _blueprint;
    private bool _blueprintAwait;

    private bool BlueprintOpen => _blueprint is { Visible: true };

    private bool UseBlueprintEditor => !SettingsStore.Current.ClassicDesigner;

    /// <summary>The station the editor is on (ADR 0043), or -1 when it edits the ship.</summary>
    private int _blueprintStation = -1;

    /// <summary>The player's own station the ship is docked at, or -1: the Designer tab opens its blueprint there when no shipyard is at hand.</summary>
    private int DockedOwnStation =>
        _structuresOn && Net.HasPlayer(Player) && Net.BlueprintRules && Net.TryGetDock(Player, out var dock) && Net.StructureExists(dock.Station) && Net.GetStructure(dock.Station).Owner == Player
            ? dock.Station : -1;

    private void BuildBlueprint()
    {
        _blueprint = new BlueprintEditor(_content, ClientContent.Handling, () => _world, Player, Stacks);
        _blueprint.ApplyRequested += cmd => { Send(cmd); _blueprintAwait = true; };
        _blueprint.CloseRequested += CloseBlueprint;
        _blueprint.Say += text => Toast(text, 4.0);
        _blueprint.HelpOpen = () => _helpOpen;
        _uiRoot.AddChild(_blueprint);
        if (_designer is not null) _designer.PlanRequested += () => { CloseDesigner(); OpenBlueprint(); };
    }

    /// <summary>Opens the 3D plan on the ship's current layout (docked at a shipyard), or on a station's modules (ADR 0043) when <paramref name="station"/> is its id.</summary>
    private void OpenBlueprint(int station = -1)
    {
        if (_blueprint is null || !_structuresOn) return;
        SetLaser(false);
        _wantCapture = false;
        _blueprintStation = station;
        _blueprint.Open(station);
    }

    /// <summary>F at the station's core console: its blueprint opens (only in a world where stations are edited that way).</summary>
    private void OpenStationBlueprint(int station)
    {
        if (_blueprint is null || !Net.BlueprintRules || !Net.StructureExists(station) || Net.GetStructure(station).Owner != Player) return;
        OpenBlueprint(station);
    }

    private void CloseBlueprint()
    {
        _blueprint?.Close();
        _blueprintAwait = false;
        _wantCapture = true;
    }

    /// <summary>The Designer tab of the ship computer, or F at the shipyard: the 3D plan takes over from the computer (a frame later, the tab is still being shown).</summary>
    private void OpenBlueprintFromComputer()
    {
        if (_computerTab == ComputerTab.Designer) CloseComputer();
        OpenBlueprint(ShipyardDocked ? -1 : DockedOwnStation);
    }

    /// <summary>Per frame: closes the editor when the shipyard is gone; after an apply waits for the simulation and reloads the layout.</summary>
    private void UpdateBlueprint()
    {
        if (!BlueprintOpen) return;
        if (_blueprintStation >= 0) { if (!Net.StructureExists(_blueprintStation) && !_blueprintCheckOn) { CloseBlueprint(); return; } }
        else if (!ShipyardDocked && !_uiAudit && !_blueprintCheckOn) { CloseBlueprint(); return; }
        if (_blueprintAwait && _world.PendingCommands == 0)
        {
            _blueprintAwait = false;
            if (_designerRejected) _designerRejected = false; // the refusal was said already (CollectResults) and the draft stays so it can be fixed
            else
            {
                _blueprint!.Reload();
                Toast(L("bp.applied", Net.GetStructure(_blueprintStation >= 0 ? _blueprintStation : Net.ShipStructure(Player)).Modules.Count), 4.0);
                AudioManager.PlayUi(Sfx.UiClick);
            }
        }
    }

    /// <summary>For the agent harness (run_script): what the editor shows right now.</summary>
    public string BlueprintState() => _blueprint?.Describe() ?? "none";

    /// <summary>For the agent harness: opens the editor (the ship must be docked at a shipyard).</summary>
    public void AgentOpenBlueprint() => OpenBlueprint();

    /// <summary>For the agent harness: opens the editor on a station's modules.</summary>
    public void AgentOpenStationBlueprint(int station) => OpenStationBlueprint(station);

    // ---- --blueprint-check and --blueprint-shot=path ------------------------------------------------------------------

    private string? _blueprintShotPath;
    private bool _blueprintCheckOn;
    private int _bpPhase, _bpWait, _bpPass, _bpFail;
    private long _bpTick;
    private int _bpModulesBefore, _bpLasersBefore;
    private (int Mx, int My, int Mz) _bpSpot, _bpFar;
    private int _bpStation = -1, _bpStationModules;
    private string _bpLang = "";

    private void BlueprintSetup(string[] args)
    {
        _blueprintShotPath = args.FirstOrDefault(a => a.StartsWith("--blueprint-shot="))?["--blueprint-shot=".Length..];
        _blueprintCheckOn = args.Contains("--blueprint-check");
        if (args.Contains("--ui-lang=es")) Loc.Lang = Language.Es;
        if (args.Contains("--ui-lang=en")) Loc.Lang = Language.En;
    }

    private bool BlueprintScripted => _blueprintShotPath is not null || _blueprintCheckOn;

    private void Check(string name, bool ok, string detail = "")
    {
        if (ok) _bpPass++; else _bpFail++;
        GD.Print($"BLUEPRINTCHECK {(ok ? "PASS" : "FAIL")} {name}{(detail.Length > 0 ? " - " + detail : "")}");
    }

    /// <summary>Free cells that touch the draft and are not in front of the docked port, the roof first.</summary>
    private List<(int Mx, int My, int Mz)> BlueprintSpots()
    {
        var s = _blueprint!.Session;
        var face = s.DockPort()?.Face;
        var steps = new[] { (1, 0, 0), (-1, 0, 0), (0, 1, 0), (0, 0, 1), (0, 0, -1) };
        return s.Draft.Modules.Keys.SelectMany(k => steps.Select(t => (k.Mx + t.Item1, k.My + t.Item2, k.Mz + t.Item3)))
            .Where(c => s.Draft.CanAddAt(c) && (face is null || c != face.Value)).Distinct().OrderByDescending(c => c.Item2).ThenBy(c => c).ToList();
    }

    private void BlueprintShotFrame()
    {
        if (!_structuresOn || ShipStruct < 0) return;
        int ship = ShipStruct;
        _bpWait++;
        bool Wait(int frames) { if (_bpWait < frames) return true; _bpWait = 0; _bpPhase++; return false; }
        void Shot(string name)
        {
            if (_blueprintShotPath is null) return;
            string p = _blueprintShotPath + "." + name + ".png";
            GetViewport().GetTexture().GetImage().SavePng(p);
            GD.Print("SHOT saved " + p);
        }
        long cu = Units.CellUnits;
        var ed = _blueprint!;
        var s = ed.Session;
        switch (_bpPhase)
        {
            case 0:
            {
                _wantCapture = false;
                var o = Net.GetStructure(ship);
                // ADR 0041: the ship's dock module joins the dock module beside the station core (the same layout the editor tests use).
                int st = Net.CreateStructure(Player, StructureKind.Station, o.OriginX - 26 * cu, o.OriginY, o.OriginZ + StationGrid.ModuleZ * cu);
                Net.AddStructureModuleUnchecked(st, 0, 0, 0, ModuleKind.Core);
                Net.AddStructureModuleUnchecked(st, 0, 0, -1, ModuleKind.Dock);
                Net.AddStructureModuleUnchecked(st, 0, 0, 1, ModuleKind.Shipyard);
                Net.AddStructureModuleUnchecked(st, 1, 0, 0, ModuleKind.Reactor);
                _bpStation = st;
                foreach (var (item, n) in new[] { ("core:iron_plate", 80), ("core:iron_ingot", 40), ("core:carbon", 30), ("core:circuit", 20), ("core:titanium_alloy", 20), ("core:focus_lens", 4), ("core:coolant", 10), ("core:composite_plate", 4) })
                    Net.GiveStock(st, _content.ItemIndex(item), n);
                if (Wait(5)) break;
                Send(new DockAt(st, 0, 0, -1, Dir.PosX));
                break;
            }
            case 1:
                if (IsDockedNow) { _bpWait = 0; _bpPhase = 2; }
                else if (_bpWait > 300) { GD.Print("BLUEPRINTCHECK FAIL setup - did not dock"); GetTree().Quit(1); }
                break;
            case 2:
                if (Wait(60)) break;
                var win = GetWindow();
                var sizeArg = OS.GetCmdlineUserArgs().FirstOrDefault(a => a.StartsWith("--blueprint-size="))?["--blueprint-size=".Length..].Split('x');
                if (_blueprintShotPath is not null)
                {
                    if (sizeArg is { Length: 2 } && int.TryParse(sizeArg[0], out int w) && int.TryParse(sizeArg[1], out int h)) SetWindowSize(win, w, h);
                    else SetWindowSize(win, 1280, 720);
                }
                OpenBlueprint();
                break;
            case 3:
                if (Wait(8)) break;
                if (_blueprintCheckOn) BlueprintCheckOpened(); else BlueprintShotStage();
                break;
            case 4:
                if (Wait(_blueprintCheckOn ? 4 : 25)) break;
                if (_blueprintCheckOn) BlueprintCheckEdited(); else { Shot("orbit"); ed.SetView(PlanView.Top); }
                break;
            case 5:
                if (Wait(_blueprintCheckOn ? 4 : 20)) break;
                if (_blueprintCheckOn) BlueprintCheckApply(); else { Shot("top"); ed.SetView(PlanView.Section); }
                break;
            case 6:
                if (_blueprintCheckOn)
                {
                    if (_blueprintAwait || Net.Tick < _bpTick + 3) break;
                    BlueprintCheckApplied();
                    break;
                }
                if (Wait(20)) break;
                Shot("section");
                s.Camera.SetOrbit();
                Shot("done");
                GetTree().Quit();
                break;
            case 7: // station blueprint (ADR 0043): the same editor on the station's modules
                if (Wait(6)) break;
                BlueprintCheckStationOpen();
                break;
            case 8:
                if (Wait(4)) break;
                BlueprintCheckStationEdited();
                break;
            case 9:
                if (_blueprintAwait || Net.Tick < _bpTick + 3) break;
                BlueprintCheckStationApplied();
                break;
        }
    }

    /// <summary>The shot's draft: a new laser (green), the gun removed (red), a hull moved (yellow) and a module cut off (red hatch).</summary>
    private void BlueprintShotStage()
    {
        var ed = _blueprint!;
        var s = ed.Session;
        var spots = BlueprintSpots();
        ed.PickKind(ModuleKind.MiningLaser);
        ed.ClickCell(spots[0]);
        var gun = s.Draft.Modules.Where(kv => kv.Value == ModuleKind.Gun).Select(kv => ((int, int, int)?)kv.Key).FirstOrDefault();
        if (gun is { } g) { ed.SetTool(BlueprintTool.Delete); ed.ClickCell(g); }
        var far = s.Draft.Modules.Keys.Max(k => k.Mx) + 4;
        ed.PickKind(ModuleKind.Hull);
        ed.ClickCell((far, 0, 0));
        ed.SetTool(BlueprintTool.Select);
        ed.SetView(PlanView.Orbit);
        ed.FrameAll();
    }

    // ---- the checks (each prints a PASS or FAIL line) ------------------------------------------------------------------

    private void BlueprintCheckOpened()
    {
        var ed = _blueprint!;
        var s = ed.Session;
        Check("editor opens docked at a shipyard", ed.IsOpen && BlueprintOpen);
        foreach (var n in new[] { "PlanView", "PlanViewport", "TopBar", "PalettePanel", "SidePanel", "FootBar", "CostPanel", "StatsPanel", "ValidationPanel", "ApplyButton", "RevertButton", "SaveButton", "CloseButton", "DeckSlider", "ToolSelect", "ToolPlace", "ToolMove", "ToolRotate", "ToolDelete", "ToolMirror", "ToolUndo", "ToolRedo", "ViewOrbit", "ViewTop", "ViewSection", "Palette_MiningLaser", "Palette_Cargo", "PlanCamera" })
            Check("node " + n, ed.FindChild(n, true, false) is not null);
        Check("the unchanged ship has nothing to apply", !s.Dirty && !s.CanApply && s.Validation.Entries.Any(e => e.Code == "unchanged"));
        var d0 = ed.Describe();
        Check("the real structure is drawn", d0.Contains($"realNodes={s.Draft.Original.Count} "), d0);
        var original = Loc.Lang;
        foreach (var lang in new[] { Language.Es, Language.En })
        {
            Loc.Lang = lang;
            foreach (var size in new[] { new Vector2(1280, 720), new Vector2(1920, 1080), new Vector2(960, 540), new Vector2(1152, 648) })
            {
                var problems = ed.LayoutProblems(size);
                Check($"layout {lang} {size.X}x{size.Y}", problems.Count == 0, string.Join(" | ", problems));
            }
        }
        Loc.Lang = original;

        _bpModulesBefore = s.Draft.Count;
        _bpLasersBefore = Net.MiningLasers(Player).Count;
        var spots = BlueprintSpots();
        _bpSpot = spots[0];
        ed.PickKind(ModuleKind.MiningLaser);
        ed.ClickCell(_bpSpot);
        Check("a placed module makes the draft dirty", s.Dirty && s.Draft.Modules.ContainsKey(_bpSpot));
        _bpFar = (s.Draft.Modules.Keys.Max(k => k.Mx) + 4, 0, 0);
    }

    private void BlueprintCheckEdited()
    {
        var ed = _blueprint!;
        var s = ed.Session;
        var d = ed.Describe();
        Check("diff shows one added ghost", s.Diff.Added.Count == 1 && s.Diff.Removed.Count == 0 && d.Contains("ghostNodes=1"), d);
        Check("the simulation agrees with the diff", s.Report.Ok && s.Report.Added == s.Diff.AddedTotal && s.Report.Removed == s.Diff.RemovedTotal, $"{s.Report.Reject} added={s.Report.Added}");
        Check("cost equals the simulation's", s.Cost.ToPay.SequenceEqual(s.Report.Cost) && s.Cost.ToRefund.SequenceEqual(s.Report.Refund), $"pays={s.Cost.Pays}");
        Check("cost panel lists the items", !s.Cost.Pays || s.Cost.Lines.Count > 0 && d.Contains($"costRows={(s.Cost.Lines.Count * 4) + 4}"), d);
        Check("the draft can be applied", s.CanApply && !s.Validation.HasErrors);
        Check("stats follow the draft", s.Stats.Lasers == s.StatsBefore.Lasers + 1 && s.Stats.PowerOutput == s.StatsBefore.PowerOutput && s.Stats.Mass > s.StatsBefore.Mass);

        // a module cut off from the rest: an error, red hatch, Apply blocked; clicking the entry frames it
        ed.PickKind(ModuleKind.Hull);
        ed.ClickCell(_bpFar);
        var e = s.Validation.Entries.FirstOrDefault(x => x.Code == "disconnected");
        Check("a cut-off module is an error", e is not null && e.Blocks && e.Frame == _bpFar && s.Diff.Invalid.Contains(_bpFar) && !s.CanApply);
        Check("the entry has icon and both languages", e is not null && IconCatalog.IsRegistered(e.Icon) && e.Text(Language.Es).Length > 5 && e.Text(Language.En).Length > 5 && e.Text(Language.Es) != e.Text(Language.En));
        if (e is not null) s.Frame(e);
        Check("clicking the entry frames the part", MathF.Abs(s.Camera.Target.X - BlueprintCamera.CellCentre(_bpFar).X) < 0.5f);
        ed.RunAction("bpUndo", 0);
        Check("undo removes the cut-off module", !s.Draft.Modules.ContainsKey(_bpFar) && s.CanApply);
        ed.RunAction("bpRedo", 0);
        Check("redo brings it back", s.Draft.Modules.ContainsKey(_bpFar) && !s.CanApply);
        ed.RunAction("bpUndo", 0);

        // removing the cockpit
        var cockpit = s.Draft.Modules.First(kv => kv.Value == ModuleKind.Cockpit).Key;
        ed.SetTool(BlueprintTool.Delete);
        ed.ClickCell(cockpit);
        Check("no cockpit is an error", s.Validation.Entries.Any(x => x.Code == "noCockpit" && x.Blocks) && !s.CanApply);
        ed.RunAction("bpUndo", 0);
        Check("undo restores the cockpit", s.Draft.Modules.ContainsKey(cockpit) && s.CanApply);

        // the camera views
        ed.SetView(PlanView.Top);
        Check("top view looks down", s.Camera.View == PlanView.Top && s.Camera.Back.Y > 0.99f);
        ed.SetView(PlanView.Section);
        Check("section view picks a deck", s.Camera.View == PlanView.Section);
        ed.SetView(PlanView.Orbit);
        s.Camera.Zoom(2f);
        Check("orbit view returns", s.Camera.View == PlanView.Orbit);
        ed.SetTool(BlueprintTool.Select);
        ed.ClickCell(_bpSpot);
        Check("select picks the module", s.Selection.Contains(_bpSpot));
        var side = (_bpSpot.Mx + 1, _bpSpot.My, _bpSpot.Mz);
        if (s.Draft.CanAddAt(side))
        {
            ed.PickKind(ModuleKind.Hull);
            ed.ClickCell(side);
            ed.SetTool(BlueprintTool.Select);
            ed.ClickCell(side);
            int before = s.Draft.Count;
            ed.RunAction("bpMirror", 0);
            Check("mirror adds the reflected copy", s.Draft.Count == before + 1 && s.Draft.Modules.ContainsKey(BlueprintDraft.Reflect(side, GridAxis.X, 0)));
            ed.RunAction("bpUndo", 0);
            ed.RunAction("bpUndo", 0);
            Check("undo takes the mirror and the test module back", s.Draft.Count == _bpModulesBefore + 1);
        }
    }

    private void BlueprintCheckApply()
    {
        var ed = _blueprint!;
        var s = ed.Session;
        Check("the draft is ready to apply", s.CanApply, s.Validation.Entries.Count > 0 ? string.Join(",", s.Validation.Entries.Select(x => x.Code)) : "");
        _bpTick = Net.Tick;
        ed.Apply();
        Check("apply sends one command", _blueprintAwait);
    }

    private void BlueprintCheckApplied()
    {
        var ed = _blueprint!;
        var s = ed.Session;
        int ship = ShipStruct;
        Check("the ship has the new module", Net.GetStructure(ship).Modules.Count == _bpModulesBefore + 1 && Net.MiningLasers(Player).Count == _bpLasersBefore + 1, $"modules={Net.GetStructure(ship).Modules.Count} lasers={Net.MiningLasers(Player).Count}");
        Check("the editor reloaded the applied layout", !s.Dirty && s.Draft.Count == Net.GetStructure(ship).Modules.Count && !s.CanApply);
        _bpWait = 0; _bpPhase = 7; // on to the station
        CloseBlueprint();
        _wantCapture = false;
        OpenBlueprint(_bpStation);
    }

    // ---- the station section of --blueprint-check ------------------------------------------------------------------

    private void BlueprintCheckStationOpen()
    {
        var ed = _blueprint!;
        var s = ed.Session;
        Check("station: the editor opens on the station", ed.IsOpen && BlueprintOpen && _blueprintStation == _bpStation && s.StructureKind == StructureKind.Station && s.StructureId == _bpStation);
        foreach (var n in new[] { "PlanView", "PalettePanel", "SidePanel", "FootBar", "CostPanel", "StatsPanel", "ValidationPanel", "ApplyButton", "Palette_Defence", "Palette_Shipyard", "Palette_Workshop" })
            Check("station: node " + n, ed.FindChild(n, true, false) is not null);
        Check("station: the palette has no ship parts", ed.FindChild("Palette_Cockpit", true, false) is null && ed.FindChild("Palette_Engine", true, false) is null && ed.FindChild("Palette_Gun", true, false) is null && ed.FindChild("Palette_Core", true, false) is null);
        Check("station: the unchanged station has nothing to apply", !s.Dirty && !s.CanApply);
        Check("station: no ship-only errors", !s.Validation.Entries.Any(e => e.Code is "noCockpit" or "noEngine" or "noReactor" or "noCargo"));
        var d0 = ed.Describe();
        Check("station: the real structure is drawn", d0.Contains($"realNodes={s.Draft.Original.Count} "), d0);
        var original = Loc.Lang;
        foreach (var lang in new[] { Language.Es, Language.En })
        {
            Loc.Lang = lang;
            foreach (var size in new[] { new Vector2(1280, 720), new Vector2(1920, 1080), new Vector2(960, 540), new Vector2(1152, 648) })
            {
                var problems = ed.LayoutProblems(size);
                Check($"station: layout {lang} {size.X}x{size.Y}", problems.Count == 0, string.Join(" | ", problems));
            }
        }
        Loc.Lang = original;
        _bpStationModules = s.Draft.Count;
        _bpSpot = BlueprintSpots()[0];
        ed.PickKind(ModuleKind.Defence);
        ed.ClickCell(_bpSpot);
        Check("station: a placed module makes the draft dirty", s.Dirty && s.Draft.Modules.TryGetValue(_bpSpot, out var k) && k == ModuleKind.Defence);
    }

    private void BlueprintCheckStationEdited()
    {
        var ed = _blueprint!;
        var s = ed.Session;
        Check("station: the simulation agrees with the diff", s.Report.Ok && s.Report.Added == s.Diff.AddedTotal && s.Report.Removed == s.Diff.RemovedTotal, s.Report.Reject.ToString());
        Check("station: cost equals the simulation's", s.Cost.ToPay.SequenceEqual(s.Report.Cost) && s.Cost.ToRefund.SequenceEqual(s.Report.Refund));
        Check("station: the draft can be applied", s.CanApply && s.ToCommand() is ApplyBlueprint ab && ab.Structure == _bpStation && ab.Modules.Length == _bpStationModules + 1);
        // the core cannot go: an error, Apply blocked, and the validation entry frames it
        var core = s.Draft.Modules.First(kv => kv.Value == ModuleKind.Core).Key;
        ed.SetTool(BlueprintTool.Delete);
        ed.ClickCell(core);
        var e = s.Validation.Entries.FirstOrDefault(x => x.Code == "noCore");
        Check("station: removing the core is an error", e is not null && e.Blocks && !s.CanApply && IconCatalog.IsRegistered(e.Icon) && e.Text(Language.Es) != e.Text(Language.En));
        ed.RunAction("bpUndo", 0);
        Check("station: undo restores the core", s.Draft.Modules.ContainsKey(core) && s.CanApply);
        // a second dock module may be added but the last one cannot go (ADR 0041)
        var dock = s.Draft.Modules.Where(kv => kv.Value == ModuleKind.Dock).Select(kv => ((int, int, int)?)kv.Key).FirstOrDefault();
        if (dock is { } dk)
        {
            ed.ClickCell(dk);
            Check("station: removing the last dock module is an error", s.Validation.Entries.Any(x => x.Code == "noDock" && x.Blocks) && !s.CanApply);
            ed.RunAction("bpUndo", 0);
        }
        ed.SetTool(BlueprintTool.Select);
        _bpTick = Net.Tick;
        ed.Apply();
        Check("station: apply sends one ApplyBlueprint", _blueprintAwait);
        _bpWait = 0; _bpPhase = 9;
    }

    private void BlueprintCheckStationApplied()
    {
        var ed = _blueprint!;
        var s = ed.Session;
        var st = Net.GetStructure(_bpStation);
        Check("station: the station has the new module", st.Modules.Count == _bpStationModules + 1 && st.Modules.TryGetValue(_bpSpot, out var k) && k == ModuleKind.Defence, $"modules={st.Modules.Count}");
        Check("station: the editor reloaded the applied layout", !s.Dirty && s.Draft.Count == st.Modules.Count && !s.CanApply);
        Check("station: the module tool is refused in the world", Net.BlueprintRules);
        GD.Print($"BLUEPRINTCHECK done pass={_bpPass} fail={_bpFail}");
        GetTree().Quit(_bpFail > 0 ? 1 : 0);
    }
}
