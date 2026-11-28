using Starfoundry.Sim;

namespace Starfoundry.Ui;

/// <summary>A link of the interior draft, named by the cells of its two ends (a belt by its first cell, a machine by its own).</summary>
public readonly record struct DraftLink(BlueprintLinkKind Kind, (int X, int Y, int Z) From, (int X, int Y, int Z) To);

/// <summary>Why the floor plan refuses to put a part somewhere: the Loc key shown as the ghost's reason.</summary>
public static class PlanReasons
{
    /// <summary>A machine footprint (ADR 0050) would leave the floor of its module.</summary>
    public const string Footprint = "bp.plan.footprint";
    public const string NoModule = "bp.plan.noModule", NotFloor = "bp.plan.notFloor", NotAllowed = "bp.plan.notAllowed", Occupied = "bp.plan.occupied", Locked = "bp.plan.locked", Gap = "bp.plan.gap", Bounds = "bp.plan.bounds", Many = "bp.plan.many";
}

/// <summary>
/// The engine-free draft of a station's interior in the blueprint editor's floor plan (ADR 0044): the machines, belt segments and links the player is drawing, with the
/// ghost rules for putting a part down (the module under the cell must exist, allow it and the cell must be on its floor and free), grid-snapped belt paths that become
/// straight segments joined at the bends, the same links the game makes when you place something that touches, and an undo and redo history. <see cref="ToInterior"/> is
/// what <c>ApplyBlueprint</c> carries; the simulation owns the final verdict.
/// </summary>
public sealed class InteriorDraft
{
    public const int MaxHistory = 200;

    private readonly ContentDb _content;
    private readonly SortedDictionary<(int X, int Y, int Z), BlueprintMachine> _machines = new();
    private readonly SortedDictionary<(int X, int Y, int Z), BlueprintBelt> _belts = new();
    private readonly HashSet<DraftLink> _links = new();
    private readonly List<Snapshot> _undo = new(), _redo = new();
    private BlueprintInterior _original = BlueprintInterior.Empty;
    private Func<(int Mx, int My, int Mz), ModuleKind?> _moduleAt = _ => null;
    private bool _freeBuilding;
    private Func<(int Mx, int My, int Mz), (int Mx, int My, int Mz), bool> _sameFloor = (a, b) => a == b;

    private sealed record Snapshot(BlueprintMachine[] Machines, BlueprintBelt[] Belts, DraftLink[] Links);

    public InteriorDraft(ContentDb content) => _content = content;

    public IReadOnlyCollection<BlueprintMachine> Machines => _machines.Values;
    public IReadOnlyCollection<BlueprintBelt> Belts => _belts.Values;
    public IReadOnlyCollection<DraftLink> Links => _links;
    public BlueprintInterior Original => _original;

    /// <summary>True in a world from before the floor rule (ADR 0031), where machines may stand above the floor.</summary>
    public bool FreeBuilding => _freeBuilding;
    public bool CanUndo => _undo.Count > 0;
    public bool CanRedo => _redo.Count > 0;
    public event Action? Changed;

    /// <summary>The kind of the (draft) module that stands at a module key, so the ghost rules follow the module edits made in the same session.</summary>
    public void SetModules(Func<(int Mx, int My, int Mz), ModuleKind?> moduleAt, bool freeBuilding, Func<(int Mx, int My, int Mz), (int Mx, int My, int Mz), bool>? sameFloor = null) { _moduleAt = moduleAt; _freeBuilding = freeBuilding; _sameFloor = sameFloor ?? ((a, b) => a == b); }

    // ---- loading and the plain state ---------------------------------------------------------------------------

    /// <summary>Loads the station's interior as the draft and as what Revert goes back to; the history is cleared.</summary>
    public void Load(BlueprintInterior interior)
    {
        _original = interior;
        _undo.Clear(); _redo.Clear();
        Restore(FromInterior(interior));
        Changed?.Invoke();
    }

    private static Snapshot FromInterior(BlueprintInterior it)
    {
        static (int, int, int) B(BlueprintBelt b) => (b.X, b.Y, b.Z);
        static (int, int, int) M(BlueprintMachine m) => (m.X, m.Y, m.Z);
        var links = it.Links.Where(l => l.Kind switch
        {
            BlueprintLinkKind.BeltToBelt => (uint)l.From < (uint)it.Belts.Length && (uint)l.To < (uint)it.Belts.Length,
            BlueprintLinkKind.BeltToMachine => (uint)l.From < (uint)it.Belts.Length && (uint)l.To < (uint)it.Machines.Length,
            _ => (uint)l.From < (uint)it.Machines.Length && (uint)l.To < (uint)it.Belts.Length,
        }).Select(l => l.Kind switch
        {
            BlueprintLinkKind.BeltToBelt => new DraftLink(l.Kind, B(it.Belts[l.From]), B(it.Belts[l.To])),
            BlueprintLinkKind.BeltToMachine => new DraftLink(l.Kind, B(it.Belts[l.From]), M(it.Machines[l.To])),
            _ => new DraftLink(l.Kind, M(it.Machines[l.From]), B(it.Belts[l.To])),
        }).ToArray();
        return new Snapshot(it.Machines, it.Belts, links);
    }

    private Snapshot Capture() => new(_machines.Values.ToArray(), _belts.Values.ToArray(), _links.ToArray());

    private void Restore(Snapshot s)
    {
        _machines.Clear(); _belts.Clear(); _links.Clear();
        foreach (var m in s.Machines) _machines[(m.X, m.Y, m.Z)] = m;
        foreach (var b in s.Belts) _belts[(b.X, b.Y, b.Z)] = b;
        foreach (var l in s.Links) _links.Add(l);
    }

    private void Push()
    {
        _undo.Add(Capture());
        if (_undo.Count > MaxHistory) _undo.RemoveAt(0);
        _redo.Clear();
    }

    private void Done() => Changed?.Invoke();

    public bool Undo()
    {
        if (_undo.Count == 0) return false;
        _redo.Add(Capture());
        Restore(_undo[^1]);
        _undo.RemoveAt(_undo.Count - 1);
        Done();
        return true;
    }

    public bool Redo()
    {
        if (_redo.Count == 0) return false;
        _undo.Add(Capture());
        Restore(_redo[^1]);
        _redo.RemoveAt(_redo.Count - 1);
        Done();
        return true;
    }

    /// <summary>Replaces the whole interior by a saved design's (one undoable step); the original it is compared with stays.</summary>
    public void ReplaceWith(BlueprintInterior interior)
    {
        Push();
        Restore(FromInterior(interior));
        Done();
    }

    /// <summary>Goes back to the station's interior (one undoable step).</summary>
    public void Revert()
    {
        if (!Dirty) return;
        Push();
        Restore(FromInterior(_original));
        Done();
    }

    /// <summary>The interior the command carries: machines and belts in cell order, the links by index.</summary>
    public BlueprintInterior ToInterior()
    {
        var machines = _machines.Values.ToArray();
        var belts = _belts.Values.ToArray();
        var mi = new Dictionary<(int, int, int), int>();
        for (int i = 0; i < machines.Length; i++) mi[(machines[i].X, machines[i].Y, machines[i].Z)] = i;
        var bi = new Dictionary<(int, int, int), int>();
        for (int i = 0; i < belts.Length; i++) bi[(belts[i].X, belts[i].Y, belts[i].Z)] = i;
        var links = new List<BlueprintLink>();
        foreach (var l in _links.OrderBy(l => ((int)l.Kind, l.From, l.To)))
        {
            switch (l.Kind)
            {
                case BlueprintLinkKind.BeltToBelt when bi.TryGetValue(l.From, out int a) && bi.TryGetValue(l.To, out int b): links.Add(new(l.Kind, a, b)); break;
                case BlueprintLinkKind.BeltToMachine when bi.TryGetValue(l.From, out int a) && mi.TryGetValue(l.To, out int m): links.Add(new(l.Kind, a, m)); break;
                case BlueprintLinkKind.MachineToBelt when mi.TryGetValue(l.From, out int m) && bi.TryGetValue(l.To, out int b): links.Add(new(l.Kind, m, b)); break;
            }
        }
        return new BlueprintInterior(machines, belts, links.ToArray());
    }

    /// <summary>True when the draft differs from the station's interior (the parts, or the links among them).</summary>
    public bool Dirty
    {
        get
        {
            var o = FromInterior(_original);
            if (o.Machines.Length != _machines.Count || o.Belts.Length != _belts.Count || o.Links.Length != _links.Count) return true;
            foreach (var m in o.Machines) if (!_machines.TryGetValue((m.X, m.Y, m.Z), out var d) || d != m) return true;
            foreach (var b in o.Belts) if (!_belts.TryGetValue((b.X, b.Y, b.Z), out var d) || d != b) return true;
            foreach (var l in o.Links) if (!_links.Contains(l)) return true;
            return false;
        }
    }

    public int PartCount => _machines.Count + _belts.Count;

    // ---- what is where -----------------------------------------------------------------------------------------------

    /// <summary>The cells a machine of the draft covers (its footprint, turned; ADR 0050).</summary>
    public IEnumerable<(int X, int Y, int Z)> CellsOf(BlueprintMachine m)
    {
        foreach (var (dx, dz) in _content.Machines[m.Type].Cells(m.Turn)) yield return (m.X + dx, m.Y, m.Z + dz);
    }

    /// <summary>The machine that covers a cell (any cell of its footprint), if any.</summary>
    public BlueprintMachine? MachineAt((int X, int Y, int Z) cell)
    {
        if (_machines.TryGetValue(cell, out var m)) return m;
        foreach (var o in _machines.Values)
            if (_content.Machines[o.Type].IsMultiCell && CellsOf(o).Contains(cell)) return o;
        return null;
    }

    private bool TouchesMachine(BlueprintMachine m, (int X, int Y, int Z) c) => CellsOf(m).Any(k => Touch(k, c));

    /// <summary>The belt segment that covers a cell, if any.</summary>
    public BlueprintBelt? BeltAt((int X, int Y, int Z) cell)
    {
        foreach (var b in _belts.Values)
        {
            var (dx, dy, dz) = Dirs.Vector(b.Dir);
            for (int k = 0; k < b.Length; k++)
                if ((b.X + dx * k, b.Y + dy * k, b.Z + dz * k) == cell) return b;
        }
        return null;
    }

    public bool IsFree((int X, int Y, int Z) cell) => MachineAt(cell) is null && BeltAt(cell) is null;

    /// <summary>Why a belt or machine may not go on a cell, or null when it may: the module under it must exist and allow it, the cell must be on the floor and free.</summary>
    public string? PlaceProblem((int X, int Y, int Z) cell, bool belt, MachineRole role = MachineRole.Crafter)
    {
        if (content_Kinds() is not { } kinds) return PlanReasons.NotAllowed;
        if (!StructureGrid.InLocalBounds(cell.X) || !StructureGrid.InLocalBounds(cell.Y) || !StructureGrid.InLocalBounds(cell.Z)) return PlanReasons.Bounds;
        if (_moduleAt(StationGrid.ModuleOfCell(cell.X, cell.Y, cell.Z)) is not { } kind) return PlanReasons.NoModule;
        switch (kinds.CellProblem(kind, cell.Y, belt, role, _freeBuilding))
        {
            case Reject.NotOnFloor: return PlanReasons.NotFloor;
            case Reject.None: break;
            default: return PlanReasons.NotAllowed;
        }
        return IsFree(cell) ? null : PlanReasons.Occupied;
    }

    private StructureContent? content_Kinds() => _content.Structures;

    // ---- edits ---------------------------------------------------------------------------------------------------------

    /// <summary>Puts a machine on a cell and links it to what touches it. Returns the reason it was refused, or null.</summary>
    public string? PlaceMachine((int X, int Y, int Z) cell, int type, int recipe = -1, int turn = 0)
    {
        if (!_content.IsMachine(type)) return PlanReasons.NotAllowed;
        var def = _content.Machines[type];
        if (def.Role is MachineRole.Ship or MachineRole.Drill) return PlanReasons.NotAllowed;
        if (FootprintProblem(cell, type, turn) is { } why) return why;
        if (PartCount >= BlueprintInterior.MaxParts) return PlanReasons.Many;
        Push();
        _machines[cell] = new BlueprintMachine(cell.X, cell.Y, cell.Z, type, recipe, def.IsMultiCell ? turn & 3 : 0);
        LinkMachine(cell);
        Done();
        return null;
    }

    /// <summary>
    /// Why a machine of <paramref name="type"/>, turned <paramref name="turn"/> quarter turns, may not have its anchor on <paramref name="cell"/> (every covered cell must be free floor of
    /// one module, ADR 0050), or null when it may. <paramref name="ignore"/> is a machine of the draft that is being turned and so does not block itself.
    /// </summary>
    public string? FootprintProblem((int X, int Y, int Z) cell, int type, int turn, BlueprintMachine? ignore = null)
    {
        var def = _content.Machines[type];
        var anchorModule = StationGrid.ModuleOfCell(cell.X, cell.Y, cell.Z);
        foreach (var (dx, dz) in def.Cells(turn))
        {
            var c = (X: cell.X + dx, Y: cell.Y, Z: cell.Z + dz);
            if (StructureGrid.InLocalBounds(c.X) && StructureGrid.InLocalBounds(c.Z) && !_sameFloor(anchorModule, StationGrid.ModuleOfCell(c.X, c.Y, c.Z))) return PlanReasons.Footprint; // one module, or the joined floor of one large module (ADR 0051)
            if (ignore is { } ig && CellsOf(ig).Contains(c))
            {
                // the machine being turned does not block itself, but a belt on the cell still does
                if (BeltAt(c) is not null) return PlanReasons.Occupied;
                continue;
            }
            if (PlaceProblem(c, belt: false, def.Role) is { } why) return why;
        }
        return null;
    }

    /// <summary>Turns the machine on a cell by a quarter turn about its anchor when the turned footprint fits (one undoable step). False when it does not fit or the machine has one cell.</summary>
    public bool TurnMachine((int X, int Y, int Z) cell)
    {
        if (MachineAt(cell) is not { } m || !_content.Machines[m.Type].IsMultiCell) return false;
        int turn = (m.Turn + 1) & 3;
        if (FootprintProblem((m.X, m.Y, m.Z), m.Type, turn, m) is not null) return false;
        var key = (m.X, m.Y, m.Z);
        Push();
        _machines[key] = m with { Turn = turn };
        _links.RemoveWhere(l => (l.Kind == BlueprintLinkKind.BeltToMachine && l.To == key) || (l.Kind == BlueprintLinkKind.MachineToBelt && l.From == key));
        LinkMachine(key);
        Done();
        return true;
    }

    /// <summary>
    /// Draws a belt along a path of cells (the drag in the plan): adjacent cells, one step at a time, snapped to the grid. The path becomes straight segments that start a
    /// new one at every bend, joined to each other; both ends link to what they touch. Returns the reason it was refused (nothing changes), or null.
    /// </summary>
    public string? PlaceBelt(IReadOnlyList<(int X, int Y, int Z)> path, BeltTier tier = BeltTier.Mk1)
    {
        if (path.Count == 0) return PlanReasons.Gap;
        for (int i = 0; i < path.Count; i++)
        {
            if (i > 0 && Math.Abs(path[i].X - path[i - 1].X) + Math.Abs(path[i].Y - path[i - 1].Y) + Math.Abs(path[i].Z - path[i - 1].Z) != 1) return PlanReasons.Gap;
            if (PlaceProblem(path[i], belt: true) is { } why) return why;
            if (path.Take(i).Contains(path[i])) return PlanReasons.Occupied;
        }
        var segments = Split(path, tier);
        if (PartCount + segments.Count > BlueprintInterior.MaxParts) return PlanReasons.Many;
        Push();
        foreach (var s in segments) _belts[(s.X, s.Y, s.Z)] = s;
        for (int i = 0; i + 1 < segments.Count; i++)
            _links.Add(new DraftLink(BlueprintLinkKind.BeltToBelt, (segments[i].X, segments[i].Y, segments[i].Z), (segments[i + 1].X, segments[i + 1].Y, segments[i + 1].Z)));
        foreach (var s in segments) LinkBelt((s.X, s.Y, s.Z));
        Done();
        return null;
    }

    /// <summary>True when <see cref="PlaceBelt"/> would accept the path (the ghost of a drag is green, else red).</summary>
    public bool CanPlaceBelt(IReadOnlyList<(int X, int Y, int Z)> path)
    {
        if (path.Count == 0) return false;
        for (int i = 0; i < path.Count; i++)
        {
            if (i > 0 && Math.Abs(path[i].X - path[i - 1].X) + Math.Abs(path[i].Y - path[i - 1].Y) + Math.Abs(path[i].Z - path[i - 1].Z) != 1) return false;
            if (PlaceProblem(path[i], belt: true) is not null || path.Take(i).Contains(path[i])) return false;
        }
        return PartCount + Split(path, BeltTier.Mk1).Count <= BlueprintInterior.MaxParts;
    }

    /// <summary>The straight segments of a path: a new one starts after every bend, so the bend cell belongs to the segment that arrives at it.</summary>
    public static List<BlueprintBelt> Split(IReadOnlyList<(int X, int Y, int Z)> path, BeltTier tier)
    {
        var list = new List<BlueprintBelt>();
        static Dir DirOfStep((int X, int Y, int Z) a, (int X, int Y, int Z) b) =>
            b.X > a.X ? Dir.PosX : b.X < a.X ? Dir.NegX : b.Y > a.Y ? Dir.PosY : b.Y < a.Y ? Dir.NegY : b.Z > a.Z ? Dir.PosZ : Dir.NegZ;
        int start = 0;
        while (start < path.Count)
        {
            if (start == path.Count - 1)
            {
                // a lone last cell continues the way the previous segment went (or +X for a one-cell belt)
                var d0 = list.Count > 0 ? list[^1].Dir : Dir.PosX;
                list.Add(new BlueprintBelt(path[start].X, path[start].Y, path[start].Z, d0, 1, tier));
                break;
            }
            var dir = DirOfStep(path[start], path[start + 1]);
            int end = start + 1;
            while (end + 1 < path.Count && DirOfStep(path[end], path[end + 1]) == dir) end++;
            list.Add(new BlueprintBelt(path[start].X, path[start].Y, path[start].Z, dir, end - start + 1, tier));
            start = end + 1;
        }
        return list;
    }

    /// <summary>The cells a drag from one cell to another covers on the floor: along X first, then along Z (an L), one step at a time.</summary>
    public static List<(int X, int Y, int Z)> DragPath((int X, int Y, int Z) from, (int X, int Y, int Z) to, bool zFirst = false)
    {
        var path = new List<(int, int, int)> { from };
        int x = from.X, z = from.Z;
        void StepX() { while (x != to.X) { x += Math.Sign(to.X - x); path.Add((x, from.Y, z)); } }
        void StepZ() { while (z != to.Z) { z += Math.Sign(to.Z - z); path.Add((x, from.Y, z)); } }
        if (zFirst) { StepZ(); StepX(); } else { StepX(); StepZ(); }
        return path;
    }

    /// <summary>Removes the machine or the belt segment on a cell, with its links. Returns false when there is nothing there.</summary>
    public bool Erase((int X, int Y, int Z) cell)
    {
        if (MachineAt(cell) is { } hit)
        {
            var anchor = (hit.X, hit.Y, hit.Z);
            Push();
            _machines.Remove(anchor);
            _links.RemoveWhere(l => (l.Kind == BlueprintLinkKind.BeltToMachine && l.To == anchor) || (l.Kind == BlueprintLinkKind.MachineToBelt && l.From == anchor));
            Done();
            return true;
        }
        if (BeltAt(cell) is { } b)
        {
            var key = (b.X, b.Y, b.Z);
            Push();
            _belts.Remove(key);
            _links.RemoveWhere(l => (l.Kind != BlueprintLinkKind.MachineToBelt && l.From == key) || (l.Kind != BlueprintLinkKind.BeltToMachine && l.To == key));
            Done();
            return true;
        }
        return false;
    }

    /// <summary>Sets a machine's recipe (-1 for none); false when there is no machine or the recipe is not its own.</summary>
    public bool SetRecipe((int X, int Y, int Z) cell, int recipe)
    {
        if (MachineAt(cell) is not { } m) return false;
        cell = (m.X, m.Y, m.Z);
        if (recipe != -1 && (!_content.IsRecipe(recipe) || _content.Recipes[recipe].Machine != m.Type)) return false;
        if (m.Recipe == recipe) return true;
        Push();
        _machines[cell] = m with { Recipe = recipe };
        Done();
        return true;
    }

    /// <summary>Removes everything on the floor of a module (the parts whose cells lie in it) in one step.</summary>
    public int ClearModule((int Mx, int My, int Mz) module)
    {
        var machines = _machines.Keys.Where(c => StationGrid.ModuleOfCell(c.X, c.Y, c.Z) == module).ToList();
        var belts = _belts.Values.Where(b => StationGrid.ModuleOfCell(b.X, b.Y, b.Z) == module).Select(b => (b.X, b.Y, b.Z)).ToList();
        if (machines.Count + belts.Count == 0) return 0;
        Push();
        foreach (var c in machines) _machines.Remove(c);
        foreach (var k in belts) _belts.Remove(k);
        _links.RemoveWhere(l => !LinkAlive(l));
        Done();
        return machines.Count + belts.Count;
    }

    /// <summary>Drops the parts that no longer have a module to stand in (after a module edit) and the links that went with them; returns how many parts went.</summary>
    public int DropOrphans()
    {
        var machines = _machines.Keys.Where(c => _moduleAt(StationGrid.ModuleOfCell(c.X, c.Y, c.Z)) is null).ToList();
        var belts = _belts.Values.Where(b => BeltCells(b).Any(c => _moduleAt(StationGrid.ModuleOfCell(c.X, c.Y, c.Z)) is null)).Select(b => (b.X, b.Y, b.Z)).ToList();
        if (machines.Count + belts.Count == 0) return 0;
        Push();
        foreach (var c in machines) _machines.Remove(c);
        foreach (var k in belts) _belts.Remove(k);
        _links.RemoveWhere(l => !LinkAlive(l));
        Done();
        return machines.Count + belts.Count;
    }

    private bool LinkAlive(DraftLink l) => l.Kind switch
    {
        BlueprintLinkKind.BeltToBelt => _belts.ContainsKey(l.From) && _belts.ContainsKey(l.To),
        BlueprintLinkKind.BeltToMachine => _belts.ContainsKey(l.From) && _machines.ContainsKey(l.To),
        _ => _machines.ContainsKey(l.From) && _belts.ContainsKey(l.To),
    };

    public static IEnumerable<(int X, int Y, int Z)> BeltCells(BlueprintBelt b)
    {
        var (dx, dy, dz) = Dirs.Vector(b.Dir);
        for (int k = 0; k < b.Length; k++) yield return (b.X + dx * k, b.Y + dy * k, b.Z + dz * k);
    }

    // ---- links the way the game makes them -------------------------------------------------------------------------------

    private static bool Touch((int X, int Y, int Z) a, (int X, int Y, int Z) b) => Math.Abs(a.X - b.X) + Math.Abs(a.Y - b.Y) + Math.Abs(a.Z - b.Z) == 1;

    private int BeltIn((int, int, int) belt) => _links.Count(l => l.To == belt && (l.Kind == BlueprintLinkKind.BeltToBelt || l.Kind == BlueprintLinkKind.MachineToBelt));
    private int BeltOutBelts((int, int, int) belt) => _links.Count(l => l.Kind == BlueprintLinkKind.BeltToBelt && l.From == belt);
    private bool BeltFeedsMachine((int, int, int) belt) => _links.Any(l => l.Kind == BlueprintLinkKind.BeltToMachine && l.From == belt);
    private int MachineOuts((int, int, int) machine) => _links.Count(l => l.Kind == BlueprintLinkKind.MachineToBelt && l.From == machine);

    private bool CanFeed(BlueprintMachine m) => _content.Machines[m.Type].Role is MachineRole.Crafter or MachineRole.Drill or MachineRole.Unloader;
    private bool CanTake(BlueprintMachine m) => _content.Machines[m.Type].Role is MachineRole.Crafter or MachineRole.Lab or MachineRole.Depot or MachineRole.Turret or MachineRole.Repairer;

    /// <summary>Links a belt segment to what touches its ends: one input (a belt end, else a machine) and one output (a belt start, else a machine).</summary>
    private void LinkBelt((int X, int Y, int Z) key)
    {
        var b = _belts[key];
        var start = (b.X, b.Y, b.Z);
        var end = BeltNetwork.LastCell(b);
        if (BeltIn(start) == 0)
        {
            bool linked = false;
            foreach (var o in _belts.Values)
            {
                var ok = (o.X, o.Y, o.Z);
                if (ok == start || !Touch(BeltNetwork.LastCell(o), start) || BeltFeedsMachine(ok) || BeltOutBelts(ok) >= 2) continue;
                _links.Add(new DraftLink(BlueprintLinkKind.BeltToBelt, ok, start));
                linked = true;
                break;
            }
            if (!linked)
                foreach (var m in _machines.Values)
                    if (CanFeed(m) && TouchesMachine(m, start) && MachineOuts((m.X, m.Y, m.Z)) < 2)
                    {
                        _links.Add(new DraftLink(BlueprintLinkKind.MachineToBelt, (m.X, m.Y, m.Z), start));
                        break;
                    }
        }
        if (BeltOutBelts(start) == 0 && !BeltFeedsMachine(start))
        {
            bool linked = false;
            foreach (var o in _belts.Values)
            {
                var ok = (o.X, o.Y, o.Z);
                if (ok == start || !Touch(end, ok) || BeltIn(ok) > 0) continue;
                _links.Add(new DraftLink(BlueprintLinkKind.BeltToBelt, start, ok));
                linked = true;
                break;
            }
            if (!linked)
                foreach (var m in _machines.Values)
                    if (CanTake(m) && TouchesMachine(m, end) && !_links.Contains(new DraftLink(BlueprintLinkKind.MachineToBelt, (m.X, m.Y, m.Z), start)))
                    {
                        _links.Add(new DraftLink(BlueprintLinkKind.BeltToMachine, start, (m.X, m.Y, m.Z)));
                        break;
                    }
        }
    }

    /// <summary>Links a new machine to the belts that touch it: a belt that ends at it feeds it (the first one that is free), belts that start at it take its output (up to two).</summary>
    private void LinkMachine((int X, int Y, int Z) cell)
    {
        var m = _machines[cell];
        if (CanTake(m))
            foreach (var b in _belts.Values)
            {
                var key = (b.X, b.Y, b.Z);
                if (!TouchesMachine(m, BeltNetwork.LastCell(b)) || BeltOutBelts(key) > 0 || BeltFeedsMachine(key)) continue;
                _links.Add(new DraftLink(BlueprintLinkKind.BeltToMachine, key, cell));
                break;
            }
        if (CanFeed(m))
            foreach (var b in _belts.Values)
            {
                if (MachineOuts(cell) >= 2) break;
                var start = (b.X, b.Y, b.Z);
                if (TouchesMachine(m, start) && BeltIn(start) == 0) _links.Add(new DraftLink(BlueprintLinkKind.MachineToBelt, cell, start));
            }
    }

    // ---- reading the draft ---------------------------------------------------------------------------------------------------

    /// <summary>Belt ends that lead nowhere and belt starts that nothing feeds (belt gaps): the cells to mark, for the validation list.</summary>
    public (List<(int X, int Y, int Z)> DeadEnds, List<(int X, int Y, int Z)> Unfed) BeltGaps()
    {
        var dead = new List<(int, int, int)>();
        var unfed = new List<(int, int, int)>();
        foreach (var b in _belts.Values)
        {
            var key = (b.X, b.Y, b.Z);
            if (BeltOutBelts(key) == 0 && !BeltFeedsMachine(key)) dead.Add(BeltNetwork.LastCell(b));
            if (BeltIn(key) == 0) unfed.Add(key);
        }
        return (dead, unfed);
    }

    /// <summary>The power the machines of the draft draw when they all run.</summary>
    public int PowerUse() => _machines.Values.Sum(m => _content.Machines[m.Type].PowerUse);
}
