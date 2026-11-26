using Starfoundry.Sim;

namespace Starfoundry.Ui;

public enum BlueprintTool { Select, Place, Move, Rotate, Delete }

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
        Draft.Changed += () => { _selection.RemoveWhere(c => !Draft.Modules.ContainsKey(c)); Recompute(); };
    }

    public bool Dirty => Draft.Dirty;

    /// <summary>True when Apply can be pressed: the draft differs from the ship and nothing in it blocks.</summary>
    public bool CanApply => Draft.Dirty && !Validation.HasErrors && Report.Ok;

    /// <summary>Opens the session on the ship's current layout, or on a station's when <paramref name="structure"/> names one (also after an apply).</summary>
    public void Open(int structure = -1)
    {
        var net = _world().Network;
        StructureId = structure;
        int ship = structure >= 0 ? structure : net.ShipStructure(_player);
        _selection.Clear(); Carrying = null; Kind = null; Tool = BlueprintTool.Select;
        if (ship >= 0)
        {
            var st = net.GetStructure(ship);
            Draft.Load(st.Modules, st.Kind, structure >= 0 ? structure : -1);
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
        Report = Draft.Check(w, _player);
        var bonuses = Bonuses();
        Stats = ShipStats.Compute(Draft.Modules.Values, _content, _handling, bonuses);
        StatsBefore = ShipStats.Compute(Draft.Original.Values, _content, _handling, bonuses);
        var port = DockPort();
        Validation = BlueprintValidation.Check(Draft, _content, Report, Stats, StatsBefore, port?.Port, port?.Face, requireDock: !w.Network.Rules.LegacyDock, kind: StructureKind);
        Diff = BlueprintDiff.Compute(Draft.Original, Draft.Modules, Validation.InvalidCells);
        Cost = BlueprintCost.Compute(Diff, _content, Pays, Available);
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

    public bool Undo() => Draft.Undo();

    public bool Redo() => Draft.Redo();

    public void Revert() { Draft.Revert(); _selection.Clear(); Carrying = null; }

    /// <summary>The one command that applies the draft.</summary>
    public Command ToCommand() => Draft.ToCommand();

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
