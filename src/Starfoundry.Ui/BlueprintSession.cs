using Starfoundry.Sim;

namespace Starfoundry.Ui;

public enum BlueprintTool { Select, Place, Move, Rotate, Delete, Collar }

/// <summary>
/// Everything the blueprint editor knows about one editing session, with no engine in it: the draft, the tool and the kind in hand, the selection, the
/// camera, and the derived panels (diff, cost, stats, validation) recomputed from the simulation's verdict after every change. The Godot view only draws
/// this and forwards clicks and keys, so the whole editor logic is testable and a headless run can drive it.
/// </summary>
public sealed class BlueprintSession
{
    private readonly ContentDb _content;
    private readonly ShipHandling _handling;
    private readonly Func<SimWorld> _world;
    private readonly int _player;
    private readonly HashSet<(int Mx, int My, int Mz)> _selection = new();

    public BlueprintDraft Draft { get; } = new();

    /// <summary>The station's interior as the floor plan draws it (ADR 0044): machines, belts and links. Empty for a ship.</summary>
    public InteriorDraft Interior { get; }

    /// <summary>The module whose floor plan is open (the editor shows its top-down plan instead of the 3D view), or null.</summary>
    public (int Mx, int My, int Mz)? FloorModule { get; private set; }

    public PlanTool PlanTool { get; private set; } = PlanTool.Machine;

    /// <summary>The machine type the Machine tool places (a content machine index), or -1 for none yet.</summary>
    public int PlanMachine { get; private set; } = -1;

    /// <summary>Quarter turns the next multi-cell machine is placed with (ADR 0050); <see cref="TurnPlan"/> changes it.</summary>
    public int PlanTurn { get; private set; }

    public BeltTier PlanTier { get; private set; } = BeltTier.Mk1;

    /// <summary>The cell the Select tool picked in the floor plan, or null.</summary>
    public (int X, int Y, int Z)? PlanSelection { get; private set; }

    public InteriorDiff InteriorChange { get; private set; } = InteriorDiff.Empty;

    /// <summary>The structure being edited: -1 for the player's own ship, else a station's id (ADR 0043).</summary>
    public int StructureId { get; private set; } = -1;

    public StructureKind StructureKind => Draft.Kind;
    public BlueprintCamera Camera { get; } = new();
    public BlueprintTool Tool { get; private set; } = BlueprintTool.Select;
    public ModuleKind? Kind { get; private set; }
    public IReadOnlyCollection<(int Mx, int My, int Mz)> Selection => _selection;

    /// <summary>The cell a Move click has picked up (the next click on a free cell drops it there), when the Move tool is active.</summary>
    public (int Mx, int My, int Mz)? Carrying { get; private set; }

    public BlueprintDiff Diff { get; private set; } = null!;
    public BlueprintCost Cost { get; private set; } = null!;
    public ShipStats Stats { get; private set; } = null!;
    public ShipStats StatsBefore { get; private set; } = null!;
    public BlueprintValidation Validation { get; private set; } = null!;
    public DesignReport Report { get; private set; } = null!;

    /// <summary>The result of the last edit (the view says it as a toast), null after a successful one.</summary>
    public string? LastMessageKey { get; private set; }

    /// <summary>Raised after every change that needs a redraw (an edit, a selection, a tool, an undo).</summary>
    public event Action? Changed;

    public BlueprintSession(ContentDb content, ShipHandling handling, Func<SimWorld> world, int player)
    {
        _content = content; _handling = handling; _world = world; _player = player;
        Interior = new InteriorDraft(content);
        Draft.Changed += () => { _selection.RemoveWhere(c => !Draft.Modules.ContainsKey(c)); Recompute(); };
        Interior.Changed += () => Recompute();
    }

    public bool Dirty => Draft.Dirty || Interior.Dirty;

    /// <summary>True when Apply can be pressed: the draft differs from the ship and nothing in it blocks.</summary>
    public bool CanApply => Dirty && !Validation.HasErrors && Report.Ok;

    /// <summary>Opens the session on the ship's current layout, or on a station's when <paramref name="structure"/> names one (also after an apply).</summary>
    public void Open(int structure = -1)
    {
        var net = _world().Network;
        StructureId = structure;
        int ship = structure >= 0 ? structure : net.ShipStructure(_player);
        _selection.Clear(); Carrying = null; Kind = null; Tool = BlueprintTool.Select;
        FloorModule = null; PlanSelection = null;
        Interior.SetModules(k => Draft.Modules.TryGetValue(k, out var kind) ? kind : null, net.FreeBuilding);
        if (ship >= 0)
        {
            var st = net.GetStructure(ship);
            Interior.Load(structure >= 0 ? net.ReadInterior(structure) : BlueprintInterior.Empty);
            Draft.Load(st.Modules, st.Kind, structure >= 0 ? structure : -1, net.DockPieceRules ? net.DockPieces(ship).ToList() : null);
        }
        var (min, max) = Draft.Bounds(1);
        Camera.Frame(min, max);
        Recompute();
    }

    // ---- derived data -----------------------------------------------------------------------------------------

    private StatBonuses Bonuses()
    {
        var n = _world().Network;
        return new StatBonuses(n.BonusPercent(BonusKind.EngineSpeed), n.BonusPercent(BonusKind.EngineAccel), n.BonusPercent(BonusKind.HoldCapacity), ShipInertia.Full);
    }

    /// <summary>The cell and face-front cell of the ship's docked port, when it is docked.</summary>
    public ((int Mx, int My, int Mz) Port, (int Mx, int My, int Mz) Face)? DockPort()
    {
        var n = _world().Network;
        if (!n.TryGetDock(_player, out var dock)) return null;
        if (StructureKind == StructureKind.Station)
        {
            if (dock.Station != StructureId) return null;
            var sf = Dirs.Vector(dock.StationFace);
            return ((dock.StationMx, dock.StationMy, dock.StationMz), (dock.StationMx + sf.X, dock.StationMy, dock.StationMz + sf.Z));
        }
        var f = Dirs.Vector(dock.ShipFace);
        return ((dock.ShipMx, dock.ShipMy, dock.ShipMz), (dock.ShipMx + f.X, dock.ShipMy, dock.ShipMz + f.Z));
    }

    public bool Pays
    {
        get { var n = _world().Network; return n.Mode == GameMode.Campaign && !n.Rules.FreeBuild; }
    }

    /// <summary>Items that can be spent on a design: the hold plus the stock of the station the ship is docked at.</summary>
    public int Available(int item)
    {
        var n = _world().Network;
        int have = n.HoldCount(_player, item);
        if (StructureKind == StructureKind.Station) have += n.StockCount(StructureId, item); // a station blueprint pays from the hold, then this station's stock
        else if (n.TryGetShipyard(_player, out int station)) have += n.StockCount(station, item);
        return have;
    }

    private void Recompute()
    {
        var w = _world();
        bool station = StructureKind == StructureKind.Station;
        InteriorChange = station ? InteriorDiff.Compute(Interior.Original, Interior.ToInterior()) : InteriorDiff.Empty;
        Report = station ? w.CheckBlueprint(_player, StructureId, Draft.ToModules(), InteriorCarried(), Draft.PiecesCarried()) : Draft.Check(w, _player);
        var bonuses = Bonuses();
        Stats = ShipStats.Compute(Draft.Modules.Values, _content, _handling, bonuses);
        StatsBefore = ShipStats.Compute(Draft.Original.Values, _content, _handling, bonuses);
        if (station)
        {
            Stats = Stats with { PowerUse = Stats.PowerUse + Interior.PowerUse() };
            StatsBefore = StatsBefore with { PowerUse = StatsBefore.PowerUse + Interior.Original.Machines.Sum(m => _content.Machines[m.Type].PowerUse) };
        }
        var port = DockPort();
        Validation = BlueprintValidation.Check(Draft, _content, Report, Stats, StatsBefore, port?.Port, port?.Face, requireDock: !w.Network.Rules.LegacyDock, kind: StructureKind, interior: station ? Interior : null);
        Diff = BlueprintDiff.Compute(Draft.Original, Draft.Modules, Validation.InvalidCells);
        Cost = BlueprintCost.Compute(Diff, _content, Pays, Available, InteriorChange);
        Changed?.Invoke();
    }

    // ---- tools and the selection ----------------------------------------------------------------------------------

    public void SetTool(BlueprintTool tool)
    {
        Tool = tool;
        Carrying = null;
        if (tool != BlueprintTool.Place) Kind = tool == BlueprintTool.Select ? Kind : null;
        Changed?.Invoke();
    }

    /// <summary>Picks a kind from the palette: the Place tool is selected with it.</summary>
    public void PickKind(ModuleKind kind)
    {
        Kind = kind;
        Tool = BlueprintTool.Place;
        Carrying = null;
        Changed?.Invoke();
    }

    public void Select((int Mx, int My, int Mz) cell, bool additive)
    {
        if (!Draft.Modules.ContainsKey(cell)) { if (!additive) _selection.Clear(); }
        else if (additive) { if (!_selection.Remove(cell)) _selection.Add(cell); }
        else { _selection.Clear(); _selection.Add(cell); }
        Changed?.Invoke();
    }

    public void ClearSelection() { _selection.Clear(); Carrying = null; Changed?.Invoke(); }

    public void SelectAll()
    {
        _selection.Clear();
        foreach (var k in Draft.Modules.Keys) _selection.Add(k);
        Changed?.Invoke();
    }

    /// <summary>What a click on a cell does with the active tool. Returns the refusal (null when it worked).</summary>
    public string? Click((int Mx, int My, int Mz) cell, bool additive = false)
    {
        bool occupied = Draft.Modules.ContainsKey(cell);
        string? key = null;
        switch (Tool)
        {
            case BlueprintTool.Place:
                if (occupied) Select(cell, additive);
                else if (Kind is { } kind) key = EditKey(Draft.Add(cell, kind, _content));
                else key = "designer.hint";
                break;
            case BlueprintTool.Select:
                Select(cell, additive);
                break;
            case BlueprintTool.Move:
                if (Carrying is { } from)
                {
                    key = EditKey(Draft.Move(from, cell));
                    if (key is null) { _selection.Clear(); _selection.Add(cell); }
                    Carrying = null;
                }
                else if (occupied) { Carrying = cell; _selection.Clear(); _selection.Add(cell); Changed?.Invoke(); }
                break;
            case BlueprintTool.Rotate:
                if (occupied) Draft.Rotate(new[] { cell });
                break;
            case BlueprintTool.Delete:
                if (occupied) key = EditKey(Draft.Remove(cell));
                break;
            case BlueprintTool.Collar:
                if (occupied) key = EditKey(Draft.CycleCollar(cell)); // ADR 0047: each click turns the docking collar to the next face, then takes it off
                break;
        }
        LastMessageKey = key;
        return key;
    }

    public static string? EditKey(DesignEditResult r) => r switch
    {
        DesignEditResult.Occupied => "designer.edit.occupied",
        DesignEditResult.NotAdjacent => "designer.edit.notAdjacent",
        DesignEditResult.OutOfBounds => "designer.edit.outOfBounds",
        DesignEditResult.KindNotAllowed => "designer.edit.kindNotAllowed",
        DesignEditResult.NoUpgrade => "designer.edit.noUpgrade",
        DesignEditResult.TooManyModules => "designer.edit.tooMany",
        DesignEditResult.Empty => "designer.edit.empty",
        DesignEditResult.SameCell => "designer.edit.sameCell",
        DesignEditResult.LastDock => "designer.edit.lastDock",
        _ => null,
    };

    // ---- edits of the selection ------------------------------------------------------------------------------------

    public BulkResult DeleteSelection()
    {
        var r = Draft.RemoveMany(_selection.ToList());
        _selection.Clear();
        return r;
    }

    public BulkResult RotateSelection() => Draft.Rotate(_selection.ToList());

    public BulkResult MirrorSelection(GridAxis axis = GridAxis.X) => Draft.Mirror(_selection.Count > 0 ? _selection.ToList() : Draft.Modules.Keys.ToList(), axis);

    public int CopySelection() => Draft.Copy(_selection.ToList());

    /// <summary>Pastes at the cell under the pointer, or next to the selection's lowest corner when there is none.</summary>
    public BulkResult Paste((int Mx, int My, int Mz)? at = null)
    {
        var anchor = at ?? (Draft.Modules.Count > 0 ? (Draft.Bounds(0).Min.X, Draft.Bounds(0).Min.Y, Draft.Bounds(0).Max.Z + 1) : (0, 0, 0));
        return Draft.Paste(anchor, _content);
    }

    /// <summary>Nudges the selection one cell (the arrow keys), or the carried module.</summary>
    public BulkResult Nudge(int dx, int dy, int dz) => Draft.MoveMany(_selection.ToList(), (dx, dy, dz)) is var r && r.Any
        ? Reselect(dx, dy, dz, r)
        : r;

    private BulkResult Reselect(int dx, int dy, int dz, BulkResult r)
    {
        var moved = _selection.Select(c => (c.Mx + dx, c.My + dy, c.Mz + dz)).ToList();
        _selection.Clear();
        foreach (var c in moved) _selection.Add(c);
        Changed?.Invoke();
        return r;
    }

    public DesignEditResult UpgradeSelection()
    {
        if (_selection.Count != 1) return DesignEditResult.Empty;
        return Draft.Upgrade(_selection.First(), _content);
    }

    /// <summary>
    /// The walkthrough preview: drops the camera inside a module at eye height. The module is the one given, the selected one, the cockpit of a ship or the core of a station, or the first one.
    /// False when the draft has no module to stand in.
    /// </summary>
    public bool StartWalk((int Mx, int My, int Mz)? cell = null)
    {
        if (Draft.Modules.Count == 0) return false;
        (int Mx, int My, int Mz) at;
        if (cell is { } c && Draft.Modules.ContainsKey(c)) at = c;
        else if (_selection.Count > 0) at = _selection.Order().First();
        else
        {
            var want = StructureKind == StructureKind.Station ? ModuleKind.Core : ModuleKind.Cockpit;
            at = Draft.Modules.Where(kv => kv.Value == want).Select(kv => kv.Key).Order().Cast<(int, int, int)?>().FirstOrDefault() is { } w ? w : Draft.Modules.Keys.Order().First();
        }
        Camera.SetWalk(at);
        Changed?.Invoke();
        return true;
    }

    /// <summary>The draft as a saved design (modules with rotation, and the interior of a station).</summary>
    public DesignData ToDesign(string name, string variant = "", bool preset = false) =>
        new(StructureKind == StructureKind.Station ? "station" : "ship", name, variant, preset,
            Draft.Modules.Select(kv => (kv.Key, kv.Value, Draft.RotationOf(kv.Key))).ToList(),
            StructureKind == StructureKind.Station ? Interior.ToInterior() : BlueprintInterior.Empty);

    /// <summary>Puts a saved design into the draft as one undoable step per half (modules, then interior). Refuses a design for the other kind of structure.</summary>
    public (int Done, int Skipped)? LoadDesign(DesignData d)
    {
        if (d.Structure != (StructureKind == StructureKind.Station ? "station" : "ship")) return null;
        var r = Draft.ReplaceWith(d.Modules);
        if (StructureKind == StructureKind.Station) Interior.ReplaceWith(d.Interior);
        _selection.Clear(); Carrying = null; PlanSelection = null;
        return (r.Done, r.Skipped);
    }

    public bool Undo() => FloorModule is not null ? Interior.Undo() : Draft.Undo();

    public bool Redo() => FloorModule is not null ? Interior.Redo() : Draft.Redo();

    public void Revert() { Draft.Revert(); Interior.Revert(); _selection.Clear(); Carrying = null; PlanSelection = null; }

    // ---- the floor plan (ADR 0044) --------------------------------------------------------------------------------

    /// <summary>The floor of every module a plan can open on: the modules that allow machines or belts.</summary>
    public bool CanOpenFloor((int Mx, int My, int Mz) module) =>
        StructureKind == StructureKind.Station && Draft.Modules.TryGetValue(module, out var kind) && _content.Structures is { } k && (k.AllowsBelts(kind) || Enum.GetValues<MachineRole>().Any(r => r != MachineRole.Ship && k.AllowsRole(kind, r)));

    public bool OpenFloor((int Mx, int My, int Mz) module)
    {
        if (!CanOpenFloor(module)) return false;
        FloorModule = module; PlanSelection = null;
        if (PlanMachine < 0) PlanMachine = PlanMachines().FirstOrDefault(-1);
        Changed?.Invoke();
        return true;
    }

    public void CloseFloor() { FloorModule = null; PlanSelection = null; Changed?.Invoke(); }

    /// <summary>The machine types the floor plan offers, in the build menu's order (no ship machines or drills).</summary>
    public IReadOnlyList<int> PlanMachines() => BuildCatalog.Entries(_content, true, false).Where(e => e.Kind == BuildEntryKind.Machine).Select(e => e.Machine).ToList();

    public void SetPlanTool(PlanTool tool) { PlanTool = tool; PlanSelection = null; Changed?.Invoke(); }

    /// <summary>
    /// The turn key of the floor plan (ADR 0050): turns the selected multi-cell machine a quarter turn when it fits, else the machine that will be placed next. Returns the refusal key or null.
    /// </summary>
    public string? TurnPlan()
    {
        string? why = null;
        if (PlanSelection is { } sel && Interior.MachineAt(sel) is { } m && _content.Machines[m.Type].IsMultiCell)
        {
            if (!Interior.TurnMachine(sel)) why = PlanReasons.Footprint;
        }
        else PlanTurn = (PlanTurn + 1) & 3;
        LastMessageKey = why;
        Changed?.Invoke();
        return why;
    }

    public void PickPlanMachine(int type) { PlanMachine = type; PlanTool = PlanTool.Machine; Changed?.Invoke(); }

    public void SetPlanTier(BeltTier tier) { PlanTier = tier; PlanTool = PlanTool.Belt; Changed?.Invoke(); }

    /// <summary>A click on a cell of the floor plan with the active tool. Returns the Loc key of the refusal, or null when it worked.</summary>
    public string? PlanClick((int X, int Y, int Z) cell)
    {
        string? why = null;
        switch (PlanTool)
        {
            case PlanTool.Select: PlanSelection = Interior.IsFree(cell) ? null : cell; Changed?.Invoke(); break;
            case PlanTool.Machine:
                if (PlanMachine < 0) why = "designer.hint";
                else if (Interior.MachineAt(cell) is not null) { PlanSelection = cell; Changed?.Invoke(); }
                else why = Interior.PlaceMachine(cell, PlanMachine, DefaultRecipe(PlanMachine), PlanTurn);
                break;
            case PlanTool.Belt: why = Interior.PlaceBelt(new[] { cell }, PlanTier); break;
            case PlanTool.Erase: if (!Interior.Erase(cell)) why = "bp.plan.nothing"; break;
        }
        LastMessageKey = why;
        return why;
    }

    /// <summary>A drag of the Belt tool from one cell to another: a belt along the grid-snapped path.</summary>
    public string? PlanDrag((int X, int Y, int Z) from, (int X, int Y, int Z) to, bool zFirst = false)
    {
        var why = Interior.PlaceBelt(InteriorDraft.DragPath(from, to, zFirst), PlanTier);
        LastMessageKey = why;
        return why;
    }

    /// <summary>The recipe a new machine starts with: its only one, else none (the machine panel sets it).</summary>
    private int DefaultRecipe(int type) => _content.Recipes.Where(r => r.Machine == type).Select(r => r.Index).Take(2).ToList() is { Count: 1 } one ? one[0] : -1;

    /// <summary>The recipes a machine of this type offers (for the machine panel of the plan).</summary>
    public IReadOnlyList<int> RecipesOf(int type) => _content.Recipes.Where(r => r.Machine == type).Select(r => r.Index).ToList();

    /// <summary>The interior the command carries: null while the floor plan is untouched (a module-only blueprint, which leaves the interior alone).</summary>
    public BlueprintInterior? InteriorCarried() => StructureKind == StructureKind.Station && Interior.Dirty ? Interior.ToInterior() : null;

    /// <summary>The one command that applies the draft.</summary>
    public Command ToCommand() => StructureKind == StructureKind.Station ? new ApplyBlueprint(StructureId, Draft.ToModules(), InteriorCarried(), Draft.PiecesCarried()) : Draft.ToCommand();

    /// <summary>The rows of the stats panel for what is being edited.</summary>
    public IReadOnlyList<StatRow> StatRows() => Starfoundry.Ui.StatRows.Build(Stats, StatsBefore, StructureKind);

    /// <summary>Frames the part a validation entry is about (the whole ship when it has no cells).</summary>
    public void Frame(ValidationEntry entry)
    {
        if (entry.Frame is { } c)
        {
            Camera.FrameCell(c);
            _selection.Clear();
            foreach (var cell in entry.Cells) if (Draft.Modules.ContainsKey(cell)) _selection.Add(cell);
        }
        else
        {
            var (min, max) = Draft.Bounds(1);
            Camera.Frame(min, max);
        }
        Changed?.Invoke();
    }

    /// <summary>The deck the section view shows by default: the layer of the selection, else the layer of the cockpit, else the lowest.</summary>
    public int DefaultSectionLayer()
    {
        if (_selection.Count > 0) return _selection.First().My;
        foreach (var (k, kind) in Draft.Modules) if (kind == ModuleKind.Cockpit) return k.My;
        return Draft.Modules.Count > 0 ? Draft.Modules.Keys.Min(k => k.My) : 0;
    }
}
