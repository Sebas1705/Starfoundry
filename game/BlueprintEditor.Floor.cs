using System;
using System.Collections.Generic;
using System.Linq;
using Godot;
using Starfoundry.Sim;
using Starfoundry.Ui;

namespace Starfoundry.Game;

/// <summary>
/// The floor plan of the blueprint editor (ADR 0044): a top-down plan of one module's floor, 8 by 8 cells, where machines, belts, turrets and walls are placed with the
/// ghost rules of <see cref="InteriorDraft"/>, belts are dragged along the grid with an arrow on every cell showing the flow, and the machine under the cursor says why it
/// cannot go there. The logic is in <see cref="BlueprintSession"/> (engine-free, tested); this draws it and forwards the mouse.
/// </summary>
public partial class BlueprintFloorCanvas : Control
{
    private readonly BlueprintSession _s;
    private readonly ContentDb _content;
    private (int X, int Y, int Z)? _hover, _dragFrom;
    private bool _zFirst;

    /// <summary>A click on a cell with the active tool.</summary>
    public event Action<(int X, int Y, int Z)>? CellClicked;

    /// <summary>A belt drag from one cell to another (the second flag: along Z first).</summary>
    public event Action<(int X, int Y, int Z), (int X, int Y, int Z), bool>? BeltDragged;

    /// <summary>A right click: erase what is on the cell.</summary>
    public event Action<(int X, int Y, int Z)>? CellErased;

    public BlueprintFloorCanvas(BlueprintSession session, ContentDb content)
    {
        _s = session; _content = content;
        Name = "FloorPlan";
        MouseFilter = MouseFilterEnum.Stop;
        FocusMode = FocusModeEnum.None;
        Visible = false;
    }

    /// <summary>The square the plan is drawn in: the free area between the palette and the side panel, with a margin.</summary>
    public Rect2 PlanRect(Rect2 free)
    {
        float side = Math.Max(64f, Math.Min(free.Size.X, free.Size.Y) - 24f);
        return new Rect2(free.Position + (free.Size - new Vector2(side, side)) / 2f, new Vector2(side, side));
    }

    /// <summary>The free area (set by the editor from its layout).</summary>
    public Rect2 Free { get; set; } = new(0, 0, 640, 480);

    private (int Mx, int My, int Mz) Module => _s.FloorModule ?? (0, 0, 0);

    private float Cell => PlanRect(Free).Size.X / StationGrid.ModuleX;

    /// <summary>The cell of the plan under a pixel, or null outside it.</summary>
    public (int X, int Y, int Z)? CellAt(Vector2 p)
    {
        var r = PlanRect(Free);
        if (!r.HasPoint(p)) return null;
        int cx = (int)((p.X - r.Position.X) / Cell), cz = (int)((p.Y - r.Position.Y) / Cell);
        cx = Math.Clamp(cx, 0, StationGrid.ModuleX - 1); cz = Math.Clamp(cz, 0, StationGrid.ModuleZ - 1);
        return (Module.Mx * StationGrid.ModuleX + cx, Module.My * StationGrid.ModuleY, Module.Mz * StationGrid.ModuleZ + cz);
    }

    /// <summary>The pixel rectangle of a cell of the plan (what a headless run clicks).</summary>
    public Rect2 RectOf((int X, int Y, int Z) cell)
    {
        var r = PlanRect(Free);
        int cx = cell.X - Module.Mx * StationGrid.ModuleX, cz = cell.Z - Module.Mz * StationGrid.ModuleZ;
        return new Rect2(r.Position + new Vector2(cx * Cell, cz * Cell), new Vector2(Cell, Cell));
    }

    public override void _GuiInput(InputEvent e)
    {
        switch (e)
        {
            case InputEventMouseButton mb when mb.Pressed && mb.ButtonIndex == MouseButton.Left:
                if (CellAt(mb.Position) is not { } c) break;
                if (_s.PlanTool == PlanTool.Belt) { _dragFrom = c; _zFirst = mb.ShiftPressed; QueueRedraw(); }
                else CellClicked?.Invoke(c);
                break;
            case InputEventMouseButton mb when !mb.Pressed && mb.ButtonIndex == MouseButton.Left:
                if (_dragFrom is { } from)
                {
                    var to = CellAt(mb.Position) ?? from;
                    _dragFrom = null;
                    if (from == to) CellClicked?.Invoke(from); else BeltDragged?.Invoke(from, to, _zFirst);
                    QueueRedraw();
                }
                break;
            case InputEventMouseButton mb when mb.Pressed && mb.ButtonIndex == MouseButton.Right:
                if (CellAt(mb.Position) is { } rc) CellErased?.Invoke(rc);
                break;
            case InputEventMouseMotion mm:
            {
                var c2 = CellAt(mm.Position);
                if (c2 != _hover) { _hover = c2; TooltipText = c2 is { } cc ? Reason(cc) ?? "" : ""; QueueRedraw(); }
                else if (_dragFrom is not null) QueueRedraw();
                break;
            }
        }
    }

    public override void _Notification(int what)
    {
        if (what == NotificationMouseExit) { _hover = null; _dragFrom = null; QueueRedraw(); }
    }

    /// <summary>Why the active tool cannot act on a cell (the text shown as its ghost's tooltip), or null when it can.</summary>
    public string? Reason((int X, int Y, int Z) cell)
    {
        var d = _s.Interior;
        string? key = _s.PlanTool switch
        {
            PlanTool.Machine when _s.PlanMachine >= 0 && d.MachineAt(cell) is null => d.PlaceProblem(cell, belt: false, _content.Machines[_s.PlanMachine].Role),
            PlanTool.Belt => d.PlaceProblem(cell, belt: true),
            _ => null,
        };
        return key is null ? null : Loc.T(key);
    }

    private static Color Tint(MachineRole role) => role switch
    {
        MachineRole.Crafter => new Color(0.95f, 0.62f, 0.25f),
        MachineRole.Lab => new Color(0.65f, 0.45f, 0.95f),
        MachineRole.Depot => new Color(0.35f, 0.65f, 0.95f),
        MachineRole.Turret => new Color(0.95f, 0.35f, 0.35f),
        MachineRole.Wall => new Color(0.60f, 0.64f, 0.70f),
        MachineRole.Repairer => new Color(0.45f, 0.85f, 0.55f),
        MachineRole.Unloader => new Color(0.30f, 0.80f, 0.80f),
        _ => new Color(0.8f, 0.8f, 0.8f),
    };

    private static Color Ghost(GhostState s, Color baseColour) => s switch
    {
        GhostState.Added => UiTheme.Good,
        GhostState.Removed => UiTheme.Bad,
        _ => baseColour,
    };

    public override void _Draw()
    {
        if (!Visible || _s.FloorModule is null) return;
        var r = PlanRect(Free);
        float cs = Cell;
        DrawRect(new Rect2(Vector2.Zero, Size), new Color(0.025f, 0.035f, 0.06f)); // opaque: the game behind the editor must not show through the plan
        DrawRect(r.Grow(8f), new Color(0.04f, 0.06f, 0.10f, 0.92f));
        DrawRect(r.Grow(8f), UiTheme.EdgeStrong, false, 2f);
        var d = _s.Interior;
        var diff = _s.InteriorChange;
        // the floor: which cells have a module under them that allows building
        for (int cx = 0; cx < StationGrid.ModuleX; cx++)
            for (int cz = 0; cz < StationGrid.ModuleZ; cz++)
            {
                var cell = (Module.Mx * StationGrid.ModuleX + cx, Module.My * StationGrid.ModuleY, Module.Mz * StationGrid.ModuleZ + cz);
                var rect = new Rect2(r.Position + new Vector2(cx * cs, cz * cs), new Vector2(cs, cs));
                DrawRect(rect, ((cx + cz) & 1) == 0 ? new Color(0.10f, 0.14f, 0.20f) : new Color(0.12f, 0.17f, 0.24f));
            }
        for (int i = 0; i <= StationGrid.ModuleX; i++)
        {
            DrawLine(r.Position + new Vector2(i * cs, 0), r.Position + new Vector2(i * cs, r.Size.Y), new Color(1, 1, 1, 0.08f));
            DrawLine(r.Position + new Vector2(0, i * cs), r.Position + new Vector2(r.Size.X, i * cs), new Color(1, 1, 1, 0.08f));
        }

        // parts the draft removes stay visible as red ghosts
        foreach (var m in diff.RemovedMachines) if (InModule((m.X, m.Y, m.Z))) DrawMachine(m, RectOf((m.X, m.Y, m.Z)), GhostState.Removed, 0.5f);
        foreach (var b in diff.RemovedBelts) DrawBelt(b, GhostState.Removed, 0.45f);
        foreach (var b in d.Belts) if (BeltInModule(b)) DrawBelt(b, diff.BeltState((b.X, b.Y, b.Z)), 1f);
        foreach (var m in d.Machines) if (InModule((m.X, m.Y, m.Z))) DrawMachine(m, RectOf((m.X, m.Y, m.Z)), diff.MachineState((m.X, m.Y, m.Z)), 1f);

        // belt ends that lead nowhere and starts nobody feeds
        var (dead, unfed) = d.BeltGaps();
        foreach (var c in dead) if (InModule(c)) DrawCircle(RectOf(c).GetCenter() + new Vector2(cs * 0.28f, 0), cs * 0.09f, UiTheme.Bad);
        foreach (var c in unfed) if (InModule(c)) DrawCircle(RectOf(c).GetCenter() - new Vector2(cs * 0.28f, 0), cs * 0.09f, UiTheme.Warn);

        if (_s.PlanSelection is { } sel && InModule(sel)) DrawRect(RectOf(sel).Grow(-1f), UiTheme.Gold, false, 3f);

        // the ghost under the pointer and the belt being dragged
        if (_dragFrom is { } from && _hover is { } to)
        {
            var path = InteriorDraft.DragPath(from, to, _zFirst);
            bool ok = d.CanPlaceBelt(path);
            foreach (var c in path) DrawRect(RectOf(c).Grow(-3f), new Color(ok ? UiTheme.Good : UiTheme.Bad, 0.45f));
        }
        else if (_hover is { } h)
        {
            string? why = Reason(h);
            DrawRect(RectOf(h).Grow(-1f), why is null ? UiTheme.Good : UiTheme.Bad, false, 2f);
            if (why is not null) DrawLine(RectOf(h).Position + new Vector2(4, 4), RectOf(h).End - new Vector2(4, 4), UiTheme.Bad, 2f);
        }
    }

    private bool InModule((int X, int Y, int Z) c) => StationGrid.ModuleOfCell(c.X, c.Y, c.Z) == Module;

    private bool BeltInModule(BlueprintBelt b) => InteriorDraft.BeltCells(b).Any(InModule);

    private void DrawMachine(BlueprintMachine m, Rect2 rect, GhostState state, float alpha)
    {
        var def = _content.Machines[m.Type];
        var colour = Tint(def.Role);
        var inner = rect.Grow(-Cell * 0.08f);
        DrawRect(inner, new Color(colour.R * 0.35f, colour.G * 0.35f, colour.B * 0.35f, alpha));
        DrawRect(inner, new Color(Ghost(state, colour), alpha), false, state == GhostState.Unchanged ? 2f : 3f);
        var icon = Icons.Get(IconCatalog.ForMachine(def.Id), (int)Math.Max(16f, Cell * 0.55f));
        if (icon is not null)
        {
            float s = Cell * 0.55f;
            DrawTextureRect(icon, new Rect2(inner.GetCenter() - new Vector2(s, s) / 2f, new Vector2(s, s)), false, new Color(1, 1, 1, alpha));
        }
        if (m.Recipe >= 0)
            DrawCircle(inner.Position + new Vector2(Cell * 0.14f, Cell * 0.14f), Cell * 0.07f, new Color(UiTheme.Good, alpha));
    }

    private void DrawBelt(BlueprintBelt b, GhostState state, float alpha)
    {
        var (dx, _, dz) = Dirs.Vector(b.Dir);
        var dir = new Vector2(dx, dz);
        var side = new Vector2(-dir.Y, dir.X);
        var colour = Ghost(state, b.Tier switch { BeltTier.Mk1 => new Color(0.75f, 0.78f, 0.82f), BeltTier.Mk2 => new Color(0.55f, 0.85f, 0.95f), _ => new Color(0.95f, 0.8f, 0.4f) });
        int i = 0;
        foreach (var c in InteriorDraft.BeltCells(b))
        {
            if (InModule(c))
            {
                var rect = RectOf(c);
                var centre = rect.GetCenter();
                DrawRect(rect.Grow(-Cell * 0.22f), new Color(0.07f, 0.09f, 0.13f, alpha));
                float a = Cell * 0.26f;
                DrawColoredPolygon(new[] { centre + dir * a, centre - dir * a * 0.7f + side * a * 0.8f, centre - dir * a * 0.7f - side * a * 0.8f }, new Color(colour, alpha));
                if (i == 0) DrawCircle(centre - dir * Cell * 0.36f, Cell * 0.06f, new Color(colour, alpha));
            }
            i++;
        }
    }
}

public partial class BlueprintEditor
{
    private BlueprintFloorCanvas _floor = null!;
    private VBoxContainer _floorPalette = null!, _planPanel = null!, _planMachineList = null!;
    private Button _floorOpen = null!, _floorBack = null!, _planRecipe = null!, _planClear = null!, _planUndo = null!, _planRedo = null!;
    private FitLabel _planHint = null!, _planTitle = null!, _planSelTitle = null!;
    private readonly Dictionary<PlanTool, Button> _planToolButtons = new();
    private readonly Dictionary<BeltTier, Button> _planTierButtons = new();
    private readonly List<(int Type, Button Button)> _planMachineButtons = new();
    private bool _floorShown;

    private void BuildFloor()
    {
        _floor = new BlueprintFloorCanvas(_s, _content);
        _floor.SetAnchorsAndOffsetsPreset(LayoutPreset.FullRect);
        _floor.CellClicked += c => { _discardArmed = false; Report(_s.PlanClick(c)); };
        _floor.BeltDragged += (a, b, zFirst) => { _discardArmed = false; Report(_s.PlanDrag(a, b, zFirst)); };
        _floor.CellErased += c => { _discardArmed = false; if (!_s.Interior.Erase(c)) Report("bp.plan.nothing"); };
        AddChild(_floor);
        MoveChild(_floor, _container.GetIndex() + 1); // above the 3D plan, below the panels

        // the top bar: the way in and the way out
        var row = _topBar.GetChild<HBoxContainer>(0);
        _floorOpen = IconButton("tool:build", Loc.T("bp.plan.open"), () => OpenFloorOfSelection(), "FloorButton");
        _floorBack = UiKit.Button(Loc.T("bp.plan.back"), () => CloseFloor(), 13, 150);
        _floorBack.Name = "FloorBackButton";
        row.AddChild(_floorOpen);
        row.MoveChild(_floorOpen, _frameAll.GetIndex() + 1);
        row.AddChild(_floorBack);
        row.MoveChild(_floorBack, _frameAll.GetIndex() + 2);

        // the palette of the plan, in the same panel as the 3D palette (one shows at a time)
        _floorPalette = new VBoxContainer { Name = "FloorPalette", Visible = false };
        _floorPalette.AddThemeConstantOverride("separation", 6);
        _planTitle = Label("FloorTitle", 1, UiTheme.Body, UiTheme.Small, Muted); _planTitle.SetFit(Loc.T("bp.plan.tools"));
        _floorPalette.AddChild(_planTitle);
        var tools = new HFlowContainer { Name = "FloorTools" };
        tools.AddThemeConstantOverride("h_separation", 4);
        tools.AddThemeConstantOverride("v_separation", 4);
        void Tool(PlanTool t, string icon, string key)
        {
            var b = IconButton(icon, Loc.T(key), () => { _s.SetPlanTool(t); _discardArmed = false; }, "FloorTool_" + t, toggle: true);
            _planToolButtons[t] = b;
            tools.AddChild(b);
        }
        Tool(PlanTool.Select, "tool:pick", "bp.plan.select");
        Tool(PlanTool.Machine, "tool:place", "bp.plan.machine");
        Tool(PlanTool.Belt, "belt:mk1", "bp.plan.belt");
        Tool(PlanTool.Erase, "tool:demolish", "bp.plan.erase");
        _planUndo = IconButton("tool:undo", Loc.T("bp.tool.undo") + " (" + Cap("bpUndo") + ")", () => Undo(), "FloorUndo");
        _planRedo = IconButton("tool:redo", Loc.T("bp.tool.redo") + " (" + Cap("bpRedo") + ")", () => Redo(), "FloorRedo");
        tools.AddChild(_planUndo); tools.AddChild(_planRedo);
        _floorPalette.AddChild(tools);
        var tiers = new HFlowContainer { Name = "FloorTiers" };
        tiers.AddThemeConstantOverride("h_separation", 4);
        foreach (var tier in new[] { BeltTier.Mk1, BeltTier.Mk2, BeltTier.Mk3 })
        {
            var t = tier;
            var b = IconButton(IconCatalog.ForBelt(t), Loc.T("build.desc.belt" + ((int)t + 1)), () => { _s.SetPlanTier(t); _discardArmed = false; }, "FloorTier_" + t, toggle: true);
            _planTierButtons[t] = b;
            tiers.AddChild(b);
        }
        _floorPalette.AddChild(tiers);

        var title = Label("FloorMachinesTitle", 1, UiTheme.Body, UiTheme.Small, Muted); title.SetFit(Loc.T("bp.plan.machines"));
        _floorPalette.AddChild(title);
        _planMachineList = new VBoxContainer { Name = "FloorMachineList", SizeFlagsHorizontal = SizeFlags.ExpandFill };
        _planMachineList.AddThemeConstantOverride("separation", 3);
        foreach (int type in _s.PlanMachines())
        {
            int t = type;
            var def = _content.Machines[t];
            var row2 = new HBoxContainer { Name = "FloorMachineRow_" + def.Id.Replace(':', '_'), SizeFlagsHorizontal = SizeFlags.ExpandFill };
            row2.AddThemeConstantOverride("separation", 4);
            var b = new Button { Name = "FloorMachine_" + def.Id.Replace(':', '_'), Text = Loc.T("machine." + def.Id), Alignment = HorizontalAlignment.Left, ToggleMode = true, ExpandIcon = false, FocusMode = FocusModeEnum.All, SizeFlagsHorizontal = SizeFlags.ExpandFill, CustomMinimumSize = new Vector2(0, 30), ClipText = true, TextOverrunBehavior = TextServer.OverrunBehavior.TrimEllipsis };
            UiTheme.StyleButton(b);
            b.AddThemeFontSizeOverride("font_size", 13);
            b.Icon = Icons.Get(IconCatalog.ForMachine(def.Id), 22);
            b.AddThemeConstantOverride("icon_max_width", 22);
            b.AddThemeConstantOverride("h_separation", 6);
            b.Pressed += () => { _s.PickPlanMachine(t); _discardArmed = false; };
            b.TooltipText = Loc.T("build.desc." + def.Id) + (def.Cost.Length > 0 ? "\n" + _stacks(def.Cost) : "");
            row2.AddChild(b);
            var costs = new HBoxContainer { MouseFilter = MouseFilterEnum.Ignore };
            costs.AddThemeConstantOverride("separation", 4);
            costs.CustomMinimumSize = new Vector2(44f, 0); costs.Alignment = BoxContainer.AlignmentMode.End; // one column width, so every button ends at the same edge
            foreach (var st in def.Cost)
            {
                var ic = new IconCount(IconCatalog.ForItem(_content.Items[st.Item].Id), 16, UiTheme.Caption);
                ic.SetText(IconCatalog.ForItem(_content.Items[st.Item].Id), st.Count.ToString(), Muted);
                costs.AddChild(ic);
            }
            row2.AddChild(costs);
            _planMachineList.AddChild(row2);
            _planMachineButtons.Add((t, b));
        }
        _floorPalette.AddChild(new ScrollWithHint(_planMachineList) { Name = "FloorMachineScroll", SizeFlagsVertical = SizeFlags.ExpandFill });

        // the machine panel of the plan: the selected machine and its recipe
        _planPanel = new VBoxContainer { Name = "FloorMachinePanel" };
        _planPanel.AddThemeConstantOverride("separation", 4);
        _planSelTitle = Label("FloorSelectedTitle", 1, UiTheme.Body, UiTheme.Small, Muted); _planSelTitle.SetFit(Loc.T("bp.plan.panel"));
        _planPanel.AddChild(_planSelTitle);
        _planRecipe = UiKit.Button("", () => CycleRecipe(), 13, 200);
        _planRecipe.Name = "FloorRecipeButton";
        _planRecipe.ClipText = true;
        _planPanel.AddChild(_planRecipe);
        _planHint = Label("FloorHint", 3, UiTheme.Caption, UiTheme.Caption - 1, Muted); _planHint.SetFit(Loc.T("bp.plan.flow"));
        _planPanel.AddChild(_planHint);
        _planClear = UiKit.Button(Loc.T("bp.plan.clear"), () => ClearFloor(), 13, 200);
        _planClear.Name = "FloorClearButton";
        _planPanel.AddChild(_planClear);
        _floorPalette.AddChild(_planPanel);
        _palettePanel.AddChild(_floorPalette);
    }

    // ---- state -------------------------------------------------------------------------------------------------------

    /// <summary>Opens the floor plan of the selected module (the Floor plan button and a double click).</summary>
    public bool OpenFloorOfSelection()
    {
        if (_s.Selection.Count != 1) { Say?.Invoke(Loc.T("bp.plan.nothingSelected")); return false; }
        return OpenFloor(_s.Selection.First());
    }

    public bool OpenFloor((int Mx, int My, int Mz) module)
    {
        if (!_s.OpenFloor(module)) { Say?.Invoke(Loc.T("bp.plan.notAllowed")); return false; }
        _discardArmed = false;
        return true;
    }

    public void CloseFloor() { _s.CloseFloor(); _discardArmed = false; _sceneDirty = true; }

    /// <summary>A click on a cell of the plan (no mouse): what the harness and the keyboard-only paths use.</summary>
    public void FloorClick((int X, int Y, int Z) cell) { _discardArmed = false; Report(_s.PlanClick(cell)); }

    public void FloorDrag((int X, int Y, int Z) from, (int X, int Y, int Z) to, bool zFirst = false) { _discardArmed = false; Report(_s.PlanDrag(from, to, zFirst)); }

    public bool FloorOpenNow => _s.FloorModule is not null;

    private void ClearFloor()
    {
        if (_s.FloorModule is { } m) Say?.Invoke(Loc.T("bp.plan.cleared", _s.Interior.ClearModule(m)));
    }

    private void CycleRecipe()
    {
        if (_s.PlanSelection is not { } cell || _s.Interior.MachineAt(cell) is not { } m) return;
        var list = _s.RecipesOf(m.Type);
        if (list.Count == 0) return;
        int at = list.ToList().IndexOf(m.Recipe);
        _s.Interior.SetRecipe(cell, at + 1 >= list.Count ? -1 : list[at + 1]);
    }

    /// <summary>Shows the plan or the 3D view and keeps the plan's palette and buttons in step with the session (called whenever the panels refresh).</summary>
    private void SyncFloor()
    {
        bool floor = _s.FloorModule is not null;
        if (floor != _floorShown)
        {
            _floorShown = floor;
            _sceneDirty = true;
        }
        _container.Visible = !floor;
        _floor.Visible = floor;
        _floorPalette.Visible = floor;
        foreach (var c in _palettePanel.GetChildren()) if (c is VBoxContainer v && v != _floorPalette) v.Visible = !floor;
        foreach (var b in _viewButtons.Values) b.Visible = !floor;
        _frameAll.Visible = !floor; _deck.Visible = !floor; _deckLabel.Visible = !floor;
        _floorBack.Visible = floor;
        if (!floor) _selectionLabel.FitWidth = 130f;
        _floorOpen.Visible = !floor && _s.StructureKind == StructureKind.Station;
        _floorOpen.Disabled = _s.Selection.Count != 1 || !_s.CanOpenFloor(_s.Selection.FirstOrDefault());
        _floorOpen.TooltipText = _floorOpen.Disabled ? Loc.T("bp.plan.nothingSelected") : Loc.T("bp.plan.open");
        UiFit.ShrinkButton(_floorBack, Loc.T("bp.plan.back"), 150f, 13, 10);
        if (!floor) return;

        var m0 = _s.FloorModule!.Value;
        _selectionLabel.FitWidth = 270f;
        _selectionLabel.SetFit(Loc.T("bp.plan.title", m0.Mx, m0.My, m0.Mz));
        foreach (var (t, b) in _planToolButtons) b.SetPressedNoSignal(_s.PlanTool == t);
        foreach (var (t, b) in _planTierButtons) b.SetPressedNoSignal(_s.PlanTool == PlanTool.Belt && _s.PlanTier == t);
        foreach (var (type, b) in _planMachineButtons) b.SetPressedNoSignal(_s.PlanTool == PlanTool.Machine && _s.PlanMachine == type);
        _planUndo.Disabled = !_s.Interior.CanUndo;
        _planRedo.Disabled = !_s.Interior.CanRedo;
        var sel = _s.PlanSelection is { } cell ? _s.Interior.MachineAt(cell) : null;
        _planPanel.Visible = true;
        _planRecipe.Visible = sel is not null;
        if (sel is { } mm)
        {
            string name = mm.Recipe >= 0 ? Loc.T("recipe." + _content.Recipes[mm.Recipe].Id) : Loc.T("bp.plan.noRecipe");
            _planRecipe.Text = Loc.T("bp.plan.recipe", name);
            _planRecipe.Disabled = _s.RecipesOf(mm.Type).Count == 0;
            _planSelTitle.SetFit(Loc.T("machine." + _content.Machines[mm.Type].Id));
        }
        else _planSelTitle.SetFit(Loc.T("bp.plan.panel"));
        var free = new Rect2(_palettePanel.Position.X + _palettePanel.Size.X, _topBar.Position.Y + _topBar.Size.Y, _sidePanel.Position.X - (_palettePanel.Position.X + _palettePanel.Size.X), _footBar.Position.Y - (_topBar.Position.Y + _topBar.Size.Y));
        _floor.Free = free;
        _floor.QueueRedraw();
    }
}
