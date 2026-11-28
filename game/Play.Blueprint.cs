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
                Toast(L(_blueprintStation >= 0 ? "bp.applied.station" : "bp.applied", Net.GetStructure(_blueprintStation >= 0 ? _blueprintStation : Net.ShipStructure(Player)).Modules.Count), 4.0);
                AudioManager.PlayUi(Sfx.UiClick);
            }
        }
    }

    /// <summary>For the agent harness (run_script): what the editor shows right now.</summary>
    public string BlueprintState() => _blueprint?.Describe() ?? "none";

    /// <summary>For the agent harness: opens the editor (the ship must be docked at a shipyard).</summary>
    public void AgentOpenBlueprint() => OpenBlueprint();

    /// <summary>
    /// For the agent harness: one editing step of the open blueprint editor, through the same calls the pointer and the keyboard use. <c>place</c> puts a defence module on a free
    /// cell next to the draft, <c>floor</c> opens the floor plan of the core, <c>back</c> leaves it, <c>apply</c> sends the draft; any other word changes nothing. Returns <see cref="BlueprintState"/> (or the reason).
    /// </summary>
    public string AgentBlueprint(string op)
    {
        if (_blueprint is not { Visible: true } ed) return "closed";
        var s = ed.Session;
        switch (op)
        {
            case "place":
                var spots = BlueprintSpots();
                if (spots.Count == 0) return "no free cell";
                ed.PickKind(ModuleKind.Defence);
                ed.ClickCell(spots[0]);
                break;
            case "floor":
                var core = s.Draft.Modules.Where(kv => kv.Value == ModuleKind.Core).Select(kv => kv.Key).FirstOrDefault();
                ed.SetTool(BlueprintTool.Select);
                ed.ClickCell(core);
                if (!ed.OpenFloorOfSelection()) return "floor refused";
                break;
            case "back": ed.CloseFloor(); break;
            case "apply": ed.Apply(); break;
        }
        return ed.Describe();
    }

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
                int st = Net.CreateStructure(Player, StructureKind.Station, o.OriginX - (Net.DockPieceRules ? 16 : 26) * cu, o.OriginY, o.OriginZ + StationGrid.ModuleZ * cu);
                Net.AddStructureModuleUnchecked(st, 0, 0, 0, ModuleKind.Core);
                Net.AddStructureModuleUnchecked(st, 0, 0, -1, Net.DockPieceRules ? ModuleKind.Hull : ModuleKind.Dock); // ADR 0047: a hull module that carries the docking collar (a dock module before it)
                if (Net.DockPieceRules) Net.AddDockPieceUnchecked(st, 0, 0, -1, Dir.PosX);
                Net.AddStructureModuleUnchecked(st, 0, 0, 1, ModuleKind.Shipyard);
                Net.AddStructureModuleUnchecked(st, Net.DockPieceRules ? -1 : 1, 0, 0, ModuleKind.Reactor); // ADR 0047: the docked ship takes the cells on the +X side
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
                _bpWait = 0; _bpPhase = 20; // on to the station shots
                CloseBlueprint();
                _wantCapture = false;
                OpenBlueprint(_bpStation);
                break;
            case 20: // station, 3D plan then top-down (shot mode only; needs a window, the headless renderer draws nothing)
                if (Wait(25)) break;
                Shot("station.orbit");
                ed.SetView(PlanView.Top);
                break;
            case 21:
                if (Wait(20)) break;
                Shot("station.top");
                ed.SetView(PlanView.Orbit);
                if (s.Draft.Modules.FirstOrDefault(kv => kv.Value == ModuleKind.Core) is { Value: ModuleKind.Core } core && ed.OpenFloor(core.Key))
                {
                    var c = core.Key;
                    s.PickPlanMachine(_content.MachineIndex("core:smelter")); ed.FloorClick(FloorCell(c.Mx, c.Mz, 3, 3));
                    s.PickPlanMachine(_content.MachineIndex("core:depot")); ed.FloorClick(FloorCell(c.Mx, c.Mz, 6, 3));
                    s.SetPlanTier(BeltTier.Mk1); ed.FloorDrag(FloorCell(c.Mx, c.Mz, 4, 3), FloorCell(c.Mx, c.Mz, 5, 3));
                }
                break;
            case 22:
                if (Wait(20)) break;
                Shot("floor");
                ed.CloseFloor();
                ed.RunAction("bpWalk", 0);
                break;
            case 23:
                if (Wait(25)) break;
                Shot("walk");
                ed.Cancel();
                ed.Store = new MemoryBlueprintStore();
                ed.OpenDesigns();
                break;
            case 24:
                if (Wait(15)) break;
                Shot("designs");
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
            case 10: // the floor plan of the station's core (ADR 0044)
                if (Wait(6)) break;
                BlueprintCheckFloorOpen();
                break;
            case 11:
                if (Wait(4)) break;
                BlueprintCheckFloorEdited();
                break;
            case 12:
                if (_blueprintAwait || Net.Tick < _bpTick + 3) break;
                BlueprintCheckFloorApplied();
                break;
            case 13: // saved designs and the walkthrough (phase 5)
                if (Wait(4)) break;
                BlueprintCheckDesigns();
                break;
            case 14:
                if (Wait(4)) break;
                BlueprintCheckWalkFrame();
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
        Check("the real structure is drawn (modules and docking collars)", d0.Contains($"realNodes={s.Draft.Original.Count + s.Draft.OriginalPieces.Count()} "), d0);
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
        Check("station: the real structure is drawn", d0.Contains($"realNodes={s.Draft.Original.Count + s.Draft.OriginalPieces.Count()} "), d0);
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
        if (s.Draft.UsesSpans)
        {
            // ADR 0051: a large bay, 2 x 2 modules of one floor, built, priced, removed whole and undone
            Net.GrantResearch(_content.ResearchIndex("core:large_structures")); // the check world is a campaign one
            Check("large: the palette lists the large kinds", ed.FindChild("PaletteLarge_Workshop_2", true, false) is Button && ed.FindChild("PaletteLarge_Workshop_3", true, false) is Button && ed.FindChild("PaletteLarge_Sensor_2", true, false) is null);
            ed.PickKind(ModuleKind.Workshop, 2);
            Check("large: picking a large kind sets the size", s.LargeSize == 2 && s.Kind == ModuleKind.Workshop);
            var anchor = (-3, 0, 0);
            ed.ClickCell(anchor);
            Check("large: one click builds the whole square", s.Draft.Spans.Count() == 1 && new[] { (-3, 0, 0), (-2, 0, 0), (-3, 0, 1), (-2, 0, 1) }.All(c => s.Draft.Modules.GetValueOrDefault(c) == ModuleKind.Workshop), $"spans={s.Draft.Spans.Count()} {s.LastMessageKey}");
            Check("large: the simulation accepts the square (only the price may refuse it here)", s.Report.Reject is Reject.None or Reject.NotEnoughItems && s.Draft.ToCommand() is ApplyBlueprint { Spans.Length: 1 }, s.Report.Reject.ToString());
            Check("large: the frame is drawn", ed.Describe().Contains("ghostNodes=") && s.Draft.SameFloor((-3, 0, 0), (-2, 0, 1)));
            s.Draft.Remove((-2, 0, 1));
            Check("large: removing one cell removes the whole large module", !s.Draft.Spans.Any() && !s.Draft.Modules.ContainsKey((-3, 0, 0)));
            ed.RunAction("bpUndo", 0);
            Check("large: undo brings it back", s.Draft.Spans.Count() == 1);
            s.SetTool(BlueprintTool.Select);
            ed.RunAction("bpUndo", 0);
            Check("large: undoing the square leaves the draft as it was", !s.Draft.Spans.Any() && !s.Draft.Modules.ContainsKey((-3, 0, 0)));
        }
        if (s.Draft.UsesPieces && s.DockPort() is { } dp)
        {
            // ADR 0047: the collar tool turns a collar to the next face; the docked collar cannot leave its face, and undo brings it back
            ed.SetTool(BlueprintTool.Collar);
            ed.ClickCell(dp.Port);
            Check("station: moving the docked collar is an error", s.Validation.Entries.Any(x => x.Code == "port" && x.Blocks) && !s.CanApply);
            ed.RunAction("bpUndo", 0);
            Check("station: undo puts the docked collar back", !s.Validation.Entries.Any(x => x.Code == "port"));
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
        _bpWait = 0; _bpPhase = 10; // on to the floor plan
    }

    // ---- the floor plan section of --blueprint-check -------------------------------------------------------------------

    private static (int X, int Y, int Z) FloorCell(int mx, int mz, int cx, int cz) => (mx * StationGrid.ModuleX + cx, 0, mz * StationGrid.ModuleZ + cz);

    private void BlueprintCheckFloorOpen()
    {
        var ed = _blueprint!;
        var s = ed.Session;
        var core = s.Draft.Modules.First(kv => kv.Value == ModuleKind.Core).Key;
        Check("floor: the core opens its floor plan", ed.OpenFloor(core) && ed.FloorOpenNow && s.FloorModule == core);
        foreach (var n in new[] { "FloorPlan", "FloorPalette", "FloorTool_Select", "FloorTool_Machine", "FloorTool_Belt", "FloorTool_Erase", "FloorMachine_core_smelter", "FloorMachine_core_depot", "FloorMachinePanel", "FloorUndo", "FloorBackButton", "FloorClearButton" })
            Check("floor: node " + n, ed.FindChild(n, true, false) is not null);
        ed.LayoutProblems(new Vector2(1280, 720)); // refreshes the panels so the plan is shown
        Check("floor: the plan replaces the 3D view", ed.FindChild("FloorPlan", true, false) is Control { Visible: true } && ed.FindChild("PlanView", true, false) is Control { Visible: false });
        Check("floor: the 3D palette is hidden and the plan's shown", ed.FindChild("Palette_Defence", true, false) is Control { } pd && !pd.IsVisibleInTree() && ed.FindChild("FloorMachine_core_smelter", true, false) is Control { } fm && fm.IsVisibleInTree());
        var original = Loc.Lang;
        foreach (var lang in new[] { Language.Es, Language.En })
        {
            Loc.Lang = lang;
            foreach (var size in new[] { new Vector2(1280, 720), new Vector2(1920, 1080), new Vector2(960, 540), new Vector2(1152, 648) })
            {
                var problems = ed.LayoutProblems(size);
                Check($"floor: layout {lang} {size.X}x{size.Y}", problems.Count == 0, string.Join(" | ", problems));
            }
        }
        Loc.Lang = original;
        foreach (var item in _content.Items) Net.GiveStock(_bpStation, item.Index, 500); // the plan's parts are paid from the station's stock
        int smelter = _content.MachineIndex("core:smelter"), depot = _content.MachineIndex("core:depot");
        s.PickPlanMachine(smelter);
        ed.FloorClick(FloorCell(core.Mx, core.Mz, 3, 3));
        s.PickPlanMachine(depot);
        ed.FloorClick(FloorCell(core.Mx, core.Mz, 6, 3));
        s.SetPlanTier(BeltTier.Mk1);
        ed.FloorDrag(FloorCell(core.Mx, core.Mz, 4, 3), FloorCell(core.Mx, core.Mz, 5, 3));
        Check("floor: three parts are drawn", s.Interior.Machines.Count == 2 && s.Interior.Belts.Count == 1 && s.Dirty);
        s.SetPlanTool(PlanTool.Machine);
        var cell = FloorCell(core.Mx, core.Mz, 1, 1);
        ed.FloorClick((cell.X, 1, cell.Z));
        Check("floor: a cell above the floor is refused with a reason", s.Interior.Machines.Count == 2 && s.LastMessageKey == PlanReasons.NotFloor);
    }

    private void BlueprintCheckFloorEdited()
    {
        var ed = _blueprint!;
        var s = ed.Session;
        var core = s.FloorModule!.Value;
        var d = ed.Describe();
        Check("floor: the simulation agrees with the plan", s.Report.Ok && s.Report.PartsAdded == 3 && s.InteriorChange.Added == 3, $"{s.Report.Reject} added={s.Report.PartsAdded}");
        Check("floor: cost equals the simulation's", s.Cost.ToPay.SequenceEqual(s.Report.Cost) && s.Cost.ToRefund.SequenceEqual(s.Report.Refund) && s.Report.Cost.Length > 0);
        Check("floor: the describe line counts the parts", d.Contains("parts=3") && d.Contains("partsAdded=3") && d.Contains("floor=" + core.Mx), d);
        Check("floor: a belt that joins its neighbours has no gap warnings", !s.Validation.Entries.Any(e => e.Code is "beltEnd" or "beltStart") && s.CanApply);
        s.SetPlanTier(BeltTier.Mk1);
        ed.FloorDrag(FloorCell(core.Mx, core.Mz, 1, 6), FloorCell(core.Mx, core.Mz, 2, 6));
        Check("floor: a dangling belt warns about its ends but does not block", s.Validation.Entries.Any(e => e.Code == "beltEnd" && !e.Blocks) && s.Validation.Entries.Any(e => e.Code == "beltStart" && !e.Blocks) && s.CanApply, $"belts={s.Interior.Belts.Count} msg={s.LastMessageKey} entries={string.Join(",", s.Validation.Entries.Select(e => e.Code))}");
        Check("floor: the gap entries have icon and both languages", s.Validation.Entries.Where(e => e.Code is "beltEnd" or "beltStart").All(e => IconCatalog.IsRegistered(e.Icon) && e.Text(Language.Es) != e.Text(Language.En)));
        ed.RunAction("bpUndo", 0);
        Check("floor: undo takes the dangling belt back", s.Interior.Belts.Count == 1 && !s.Validation.Entries.Any(e => e.Code is "beltEnd" or "beltStart"));
        ed.RunAction("bpRedo", 0);
        ed.RunAction("bpUndo", 0);
        s.SetPlanTool(PlanTool.Select);
        ed.FloorClick(FloorCell(core.Mx, core.Mz, 3, 3));
        ed.LayoutProblems(new Vector2(1280, 720)); // refreshes the panels
        Check("floor: selecting a machine shows its panel", s.PlanSelection == FloorCell(core.Mx, core.Mz, 3, 3) && ed.FindChild("FloorRecipeButton", true, false) is Control { Visible: true });
        int iron = _content.RecipeIndex("core:smelt_iron");
        s.Interior.SetRecipe(FloorCell(core.Mx, core.Mz, 3, 3), iron);
        Check("floor: a recipe is set on the draft", s.Interior.MachineAt(FloorCell(core.Mx, core.Mz, 3, 3))!.Value.Recipe == iron && s.Report.Ok);
        _bpTick = Net.Tick;
        ed.Apply();
        Check("floor: apply sends one ApplyBlueprint with the interior", _blueprintAwait);
        _bpWait = 0; _bpPhase = 12;
    }

    private void BlueprintCheckFloorApplied()
    {
        var ed = _blueprint!;
        var s = ed.Session;
        var read = Net.ReadInterior(_bpStation);
        Check("floor: the station has the machines, the belt and the links", read.Machines.Length == 2 && read.Belts.Length == 1 && read.Links.Length == 2, $"machines={read.Machines.Length} belts={read.Belts.Length} links={read.Links.Length}");
        Check("floor: the smelter has its recipe", read.Machines.Any(m => m.Type == _content.MachineIndex("core:smelter") && m.Recipe == _content.RecipeIndex("core:smelt_iron")));
        Check("floor: the editor reloaded the applied interior", !s.Dirty && s.Interior.Machines.Count == 2 && !s.CanApply);
        Check("floor: placing a machine in the world is refused", Net.InteriorBlueprintRules);
        BlueprintCheckFootprints();
        ed.CloseFloor();
        Check("floor: back to the 3D plan", !ed.FloorOpenNow);
        _bpWait = 0; _bpPhase = 13;
    }

    /// <summary>The multi-cell machines of ADR 0050 on the open floor plan: the palette, the footprint ghost, turning, the cells a machine covers, erasing it from any of them and undo.</summary>
    private void BlueprintCheckFootprints()
    {
        var ed = _blueprint!;
        var s = ed.Session;
        if (!ed.FloorOpenNow) ed.OpenFloor(s.Draft.Modules.First(kv => kv.Value == ModuleKind.Core).Key); // applying closed the plan: open the core's again
        var core = s.FloorModule!.Value;
        int large = _content.MachineIndex("core:large_smelter"), plus = _content.MachineIndex("core:reactor_core");
        Net.GrantResearch(_content.ResearchIndex("core:heavy_industry")); // the check world is a campaign one: the machines unlock with this research
        Check("footprint: the palette lists the four multi-cell machines", new[] { "core:large_smelter", "core:long_assembler", "core:l_fabricator", "core:reactor_core" }.All(id => ed.FindChild("FloorMachine_" + id.Replace(':', '_'), true, false) is Button));
        Check("footprint: the turn button exists", ed.FindChild("FloorTurn", true, false) is Button);
        int before = s.Interior.Machines.Count;
        s.PickPlanMachine(large);
        Check("footprint: a footprint that crosses the module edge is refused with a reason", s.Interior.FootprintProblem(FloorCell(core.Mx, core.Mz, 7, 5), large, 0) == PlanReasons.Footprint);
        ed.FloorClick(FloorCell(core.Mx, core.Mz, 5, 5));
        Check("footprint: the large smelter is placed on four cells", s.Interior.Machines.Count == before + 1 && new[] { (5, 5), (6, 5), (5, 6), (6, 6) }.All(c => s.Interior.MachineAt(FloorCell(core.Mx, core.Mz, c.Item1, c.Item2)) is not null));
        Check("footprint: the simulation agrees with the plan", s.Report.Ok, s.Report.Reject.ToString());
        s.SetPlanTool(PlanTool.Select);
        ed.FloorClick(FloorCell(core.Mx, core.Mz, 6, 6));
        Check("footprint: a click on any covered cell selects the machine", s.PlanSelection is not null && s.Interior.MachineAt(s.PlanSelection.Value) is { } m0 && m0.Type == large);
        ed.RunAction("bpRotate", 0);
        var turned = s.Interior.MachineAt(FloorCell(core.Mx, core.Mz, 5, 5));
        Check("footprint: the rotate key turns the machine a quarter turn", turned is { Turn: 1 } && s.Report.Ok && new[] { (5, 5), (4, 5), (5, 6), (4, 6) }.All(c => s.Interior.MachineAt(FloorCell(core.Mx, core.Mz, c.Item1, c.Item2)) is not null), s.Report.Reject.ToString());
        s.PickPlanMachine(plus);
        ed.RunAction("bpRotate", 0);
        Check("footprint: with the Machine tool the key turns the next one", s.PlanTurn >= 0);
        Check("footprint: the plus fits on free floor and not across the module edge", s.Interior.FootprintProblem(FloorCell(core.Mx, core.Mz, 5, 0), plus, 0) is null && s.Interior.FootprintProblem(FloorCell(core.Mx, core.Mz, 0, 0), plus, 0) == PlanReasons.Footprint);
        s.SetPlanTool(PlanTool.Erase);
        ed.FloorClick(FloorCell(core.Mx, core.Mz, 4, 6));
        Check("footprint: erasing any covered cell removes the whole machine", s.Interior.Machines.Count == before && s.Interior.MachineAt(FloorCell(core.Mx, core.Mz, 5, 5)) is null);
        s.Interior.Undo();
        Check("footprint: undo brings it back whole", s.Interior.Machines.Count == before + 1 && s.Interior.MachineAt(FloorCell(core.Mx, core.Mz, 4, 5)) is not null);
        for (int i = 0; i < 5 && s.Interior.Machines.Count > before; i++) s.Interior.Undo();
        s.SetPlanTool(PlanTool.Select);
        Check("footprint: the draft is back to the applied plan", s.Interior.Machines.Count == before && !s.Interior.Dirty);
    }

    // ---- the saved designs and the walkthrough section of --blueprint-check (phase 5) ---------------------------------------

    private void BlueprintCheckDesigns()
    {
        var ed = _blueprint!;
        var s = ed.Session;
        ed.Store = new MemoryBlueprintStore(); // never the user's own files
        var lib = new BlueprintLibrary(ed.Store, _content);
        ed.OpenDesigns();
        Check("designs: the list opens", ed.DesignsOpen);
        foreach (var n in new[] { "DesignsPanel", "DesignList", "DesignName", "DesignVariant", "DesignSave", "DesignLoad", "DesignRename", "DesignDuplicate", "DesignPreset", "DesignDelete", "DesignClose" })
            Check("designs: node " + n, ed.FindChild(n, true, false) is Control { } c && c.IsVisibleInTree());
        var original = Loc.Lang;
        foreach (var lang in new[] { Language.Es, Language.En })
        {
            Loc.Lang = lang;
            foreach (var size in new[] { new Vector2(1280, 720), new Vector2(1920, 1080), new Vector2(960, 540), new Vector2(1152, 648) })
            {
                ed.LayoutProblems(size);
                var r = ed.FindChild("DesignsPanel", true, false) is Control p ? new Rect2(p.Position, p.Size) : default;
                Check($"designs: panel fits {lang} {size.X}x{size.Y}", r.Position.X >= 0 && r.Position.Y >= 0 && r.End.X <= size.X + 0.5f && r.End.Y <= size.Y + 0.5f && r.Size.X > 300, r.ToString());
            }
        }
        Loc.Lang = original;
        var name = (LineEdit)ed.FindChild("DesignName", true, false)!;
        var variant = (LineEdit)ed.FindChild("DesignVariant", true, false)!;
        int machines = s.Interior.Machines.Count, belts = s.Interior.Belts.Count;
        ed.SaveDesign();
        Check("designs: an empty name saves nothing", lib.List("station").Count == 0);
        name.Text = "Hall";
        ed.SaveDesign();
        Check("designs: a design is saved", lib.List("station").Count == 1 && lib.List("station")[0].Parts == machines + belts && machines + belts > 0);
        ed.SaveDesign();
        Check("designs: saving the same name needs a second press", lib.List("station").Count == 1);
        ed.SaveDesign();
        Check("designs: the second press replaces it", lib.List("station").Count == 1);
        ((Button)ed.FindChild("Design_0", true, false)!).EmitSignal(BaseButton.SignalName.Pressed);
        variant.Text = "compact";
        ed.DuplicateDesign();
        Check("designs: a variant is a second slot", lib.List("station").Count == 2 && lib.List("station").Any(i => i.Variant == "compact"));
        // the draft changes, then the design brings it back
        s.Interior.ClearModule(s.Draft.Modules.First(kv => kv.Value == ModuleKind.Core).Key);
        Check("designs: the draft was emptied", s.Interior.PartCount < machines + belts);
        ((Button)ed.FindChild("Design_0", true, false)!).EmitSignal(BaseButton.SignalName.Pressed);
        ed.LoadDesign();
        Check("designs: loading restores the parts and closes the list", !ed.DesignsOpen && s.Interior.Machines.Count == machines && s.Interior.Belts.Count == belts);
        s.Undo();
        s.Interior.Undo();
        Check("designs: the load is undoable", s.Interior.PartCount < machines + belts);
        s.Interior.Redo();
        ed.QuickSave();
        Check("designs: the quick save replaces the loaded design without a prompt", lib.List("station").Count == 2);
        ed.OpenDesigns();
        ((Button)ed.FindChild("Design_0", true, false)!).EmitSignal(BaseButton.SignalName.Pressed);
        name.Text = "Foundry";
        ed.RenameDesign();
        Check("designs: rename", lib.List("station").Any(i => i.Name == "Foundry"));
        ed.TogglePreset();
        Check("designs: preset", lib.List("station").Any(i => i.Preset));
        int before = lib.List("station").Count;
        ed.DeleteDesign();
        Check("designs: delete needs a second press", lib.List("station").Count == before);
        ed.DeleteDesign();
        Check("designs: delete", lib.List("station").Count == before - 1);
        ed.RunAction("cancel", 0);
        Check("designs: Esc closes the list first", !ed.DesignsOpen);

        // the walkthrough
        ed.RunAction("bpWalk", 0);
        Check("walk: the key starts the walkthrough at eye height", s.Camera.View == PlanView.Walk && Math.Abs(s.Camera.Position.Y - (s.Camera.WalkLayer * StationGrid.ModuleY + BodyMotion.EyeHeight)) < 0.01f, s.Camera.Position.ToString());
        var eye = s.Camera.Position;
        ed.RunAction("bpNudge", 0);
        Check("walk: an arrow walks", s.Camera.Position != eye);
        _bpWait = 0; _bpPhase = 14;
    }

    private void BlueprintCheckWalkFrame()
    {
        var ed = _blueprint!;
        var s = ed.Session;
        var cam3 = ed.FindChild("PlanCamera", true, false) as Camera3D;
        Check("walk: the camera is inside the module with the wide view", cam3 is not null && Math.Abs(cam3.Fov - BlueprintCamera.WalkFov) < 0.01f && Math.Abs(cam3.Position.Y - s.Camera.Position.Y) < 0.01f, cam3 is null ? "no camera" : $"fov={cam3.Fov} y={cam3.Position.Y}");
        ed.Cancel();
        Check("walk: Esc leaves it", s.Camera.View == PlanView.Orbit && Math.Abs(s.Camera.FovDegrees - 45f) < 0.01f);
        GD.Print($"BLUEPRINTCHECK done pass={_bpPass} fail={_bpFail}");
        GetTree().Quit(_bpFail > 0 ? 1 : 0);
    }
}
