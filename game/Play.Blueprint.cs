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

    /// <summary>Opens the 3D plan on the ship's current layout (docked at a shipyard).</summary>
    private void OpenBlueprint()
    {
        if (_blueprint is null || !_structuresOn) return;
        SetLaser(false);
        _wantCapture = false;
        _blueprint.Open();
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
        OpenBlueprint();
    }

    /// <summary>Per frame: closes the editor when the shipyard is gone; after an apply waits for the simulation and reloads the layout.</summary>
    private void UpdateBlueprint()
    {
        if (!BlueprintOpen) return;
        if (!ShipyardDocked && !_uiAudit && !_blueprintCheckOn) { CloseBlueprint(); return; }
        if (_blueprintAwait && _world.PendingCommands == 0)
        {
            _blueprintAwait = false;
            if (_designerRejected) _designerRejected = false; // the refusal was said already (CollectResults) and the draft stays so it can be fixed
            else
            {
                _blueprint!.Reload();
                Toast(L("bp.applied", Net.GetStructure(Net.ShipStructure(Player)).Modules.Count), 4.0);
                AudioManager.PlayUi(Sfx.UiClick);
            }
        }
    }

    /// <summary>For the agent harness (run_script): what the editor shows right now.</summary>
    public string BlueprintState() => _blueprint?.Describe() ?? "none";

    /// <summary>For the agent harness: opens the editor (the ship must be docked at a shipyard).</summary>
    public void AgentOpenBlueprint() => OpenBlueprint();

    // ---- --blueprint-check and --blueprint-shot=path ------------------------------------------------------------------

    private string? _blueprintShotPath;
    private bool _blueprintCheckOn;
    private int _bpPhase, _bpWait, _bpPass, _bpFail;
    private long _bpTick;
    private int _bpModulesBefore, _bpLasersBefore;
    private (int Mx, int My, int Mz) _bpSpot, _bpFar;
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
                int st = Net.CreateStructure(Player, StructureKind.Station, o.OriginX - (8 + 10) * cu, o.OriginY, o.OriginZ);
                Net.AddStructureModuleUnchecked(st, 0, 0, 0, ModuleKind.Core);
                Net.AddStructureModuleUnchecked(st, 0, 0, 1, ModuleKind.Shipyard);
                Net.AddStructureModuleUnchecked(st, 0, 0, -1, ModuleKind.Reactor);
                foreach (var (item, n) in new[] { ("core:iron_plate", 80), ("core:iron_ingot", 40), ("core:carbon", 30), ("core:circuit", 20), ("core:titanium_alloy", 20), ("core:focus_lens", 4), ("core:coolant", 10), ("core:composite_plate", 4) })
                    Net.GiveStock(st, _content.ItemIndex(item), n);
                if (Wait(5)) break;
                Send(new DockAt(st, 0, 0, 0, Dir.PosX));
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
        GD.Print($"BLUEPRINTCHECK done pass={_bpPass} fail={_bpFail}");
        GetTree().Quit(_bpFail > 0 ? 1 : 0);
    }
}
