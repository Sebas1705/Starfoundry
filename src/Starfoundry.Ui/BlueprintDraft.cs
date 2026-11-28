using Starfoundry.Sim;

namespace Starfoundry.Ui;

/// <summary>An axis of the module grid (the mirror tool and the camera name them).</summary>
public enum GridAxis { X, Y, Z }

/// <summary>How many parts a bulk edit (mirror, paste, nudge, delete) changed and how many it had to skip.</summary>
public readonly record struct BulkResult(int Done, int Skipped)
{
    public bool Any => Done > 0;
}

/// <summary>
/// The engine-free draft of the blueprint editor (docs/superpowers/specs/2026-11-26-blueprint-editor-design.md, section 3): the ship's module set as the player
/// is editing it, with the edits the 3D plan offers (add, remove, move, rotate, upgrade, mirror, copy and paste of a group) and an undo and redo history.
/// Unlike <see cref="ShipDesignEditor"/> it lets the draft be temporarily wrong (a module far from the others): the validation list says so and Apply is
/// blocked, which is what the ghosts show. The simulation still owns the rules (<see cref="Check"/>, <see cref="ToCommand"/>). A part's rotation (quarter
/// turns about the vertical axis) is only kept for the view: the ship stores modules by kind and cell, so it is not sent and does not make the draft dirty.
/// </summary>
public sealed class BlueprintDraft
{
    public const int MaxHistory = 200;

    private readonly SortedDictionary<(int Mx, int My, int Mz), ModuleKind> _kinds = new();
    private readonly Dictionary<(int Mx, int My, int Mz), int> _rotation = new();
    private readonly Dictionary<(int Mx, int My, int Mz), ModuleKind> _original = new();
    private readonly SortedSet<(int Mx, int My, int Mz, int Face)> _pieces = new(), _originalPieces = new();
    private readonly SortedSet<(int Mx, int My, int Mz, int Size)> _spans = new(), _originalSpans = new();
    private readonly List<Snapshot> _undo = new(), _redo = new();
    private List<((int Mx, int My, int Mz) Offset, ModuleKind Kind, int Rotation)> _clipboard = new();

    private sealed record Snapshot(((int Mx, int My, int Mz) Key, ModuleKind Kind, int Rotation)[] Parts, (int Mx, int My, int Mz, int Face)[] Pieces, (int Mx, int My, int Mz, int Size)[] Spans);

    /// <summary>True in a world with large modules (ADR 0051): the draft then carries its spans, and <see cref="ToCommand"/> sends them. Set by <see cref="Load"/>.</summary>
    public bool UsesSpans { get; private set; }

    /// <summary>The draft's large modules, in anchor order.</summary>
    public IEnumerable<DesignSpan> Spans => _spans.Select(s => new DesignSpan(s.Mx, s.My, s.Mz, s.Size));

    /// <summary>The structure's own large modules.</summary>
    public IEnumerable<DesignSpan> OriginalSpans => _originalSpans.Select(s => new DesignSpan(s.Mx, s.My, s.Mz, s.Size));

    /// <summary>The draft's large module that covers a cell, when there is one.</summary>
    public DesignSpan? SpanAt((int Mx, int My, int Mz) cell)
    {
        foreach (var s in _spans)
            if (s.My == cell.My && cell.Mx >= s.Mx && cell.Mx < s.Mx + s.Size && cell.Mz >= s.Mz && cell.Mz < s.Mz + s.Size) return new DesignSpan(s.Mx, s.My, s.Mz, s.Size);
        return null;
    }

    /// <summary>True when two cells of the draft share one floor: the same cell or two cells of one large module.</summary>
    public bool SameFloor((int Mx, int My, int Mz) a, (int Mx, int My, int Mz) b) => a == b || (SpanAt(a) is { } sa && SpanAt(b) is { } sb && sa == sb);

    /// <summary>
    /// True in a world where the dock is a docking collar, a small piece on a module face (ADR 0047), not a dock module: the draft then carries the collars, the Dock module kind is
    /// not offered and <see cref="ToCommand"/> sends the collars. Set by <see cref="Load"/>.
    /// </summary>
    public bool UsesPieces { get; private set; }

    /// <summary>The draft's docking collars (empty unless <see cref="UsesPieces"/>), in cell then face order.</summary>
    public IEnumerable<DockPieceSpec> Pieces => _pieces.Select(p => new DockPieceSpec(p.Mx, p.My, p.Mz, (Dir)p.Face));

    /// <summary>The structure's own collars, the ones Revert goes back to.</summary>
    public IEnumerable<DockPieceSpec> OriginalPieces => _originalPieces.Select(p => new DockPieceSpec(p.Mx, p.My, p.Mz, (Dir)p.Face));

    /// <summary>The collar on a module, when it has one (the first, in face order).</summary>
    public Dir? PieceOn((int Mx, int My, int Mz) key)
    {
        foreach (var p in _pieces) if ((p.Mx, p.My, p.Mz) == key) return (Dir)p.Face;
        return null;
    }

    /// <summary>The draft's modules in cell order.</summary>
    public IReadOnlyDictionary<(int Mx, int My, int Mz), ModuleKind> Modules => _kinds;

    /// <summary>The structure's own modules (what Apply replaces and the ghosts are measured against).</summary>
    public IReadOnlyDictionary<(int Mx, int My, int Mz), ModuleKind> Original => _original;

    public int Count => _kinds.Count;

    /// <summary>What the draft is a blueprint of: a ship (applied with <see cref="DesignerApply"/>) or a station (applied with <see cref="ApplyBlueprint"/>, ADR 0043).</summary>
    public StructureKind Kind { get; private set; } = StructureKind.Ship;

    /// <summary>The structure the draft belongs to (a station's id); -1 for the player's own ship.</summary>
    public int StructureId { get; private set; } = -1;

    /// <summary>Most modules the draft may hold.</summary>
    public int Max => Kind == StructureKind.Station ? DesignerRules.MaxStationModules : DesignerRules.MaxModules;

    public bool CanUndo => _undo.Count > 0;

    public bool CanRedo => _redo.Count > 0;

    public int UndoDepth => _undo.Count;

    public int RedoDepth => _redo.Count;

    /// <summary>Parts held by the clipboard (copy puts them there, paste places them).</summary>
    public int ClipboardCount => _clipboard.Count;

    /// <summary>Raised after every change of the parts (an edit, undo, redo, revert or load).</summary>
    public event Action? Changed;

    // ---- loading and the plain state --------------------------------------------------------------------------

    /// <summary>Loads the structure's modules as the draft and as the layout Revert goes back to; the history and the clipboard are cleared.</summary>
    public void Load(IEnumerable<KeyValuePair<(int Mx, int My, int Mz), ModuleKind>> modules, StructureKind kind = StructureKind.Ship, int structureId = -1, IEnumerable<DockPieceSpec>? pieces = null, IEnumerable<DesignSpan>? spans = null)
    {
        Kind = kind; StructureId = structureId;
        _kinds.Clear(); _rotation.Clear(); _original.Clear(); _undo.Clear(); _redo.Clear(); _clipboard = new();
        _pieces.Clear(); _originalPieces.Clear(); _spans.Clear(); _originalSpans.Clear();
        UsesPieces = pieces is not null;
        UsesSpans = spans is not null;
        if (spans is not null)
            foreach (var sp in spans) { _spans.Add((sp.Mx, sp.My, sp.Mz, sp.Size)); _originalSpans.Add((sp.Mx, sp.My, sp.Mz, sp.Size)); }
        foreach (var (k, v) in modules) { _kinds[k] = v; _original[k] = v; }
        if (pieces is not null)
            foreach (var p in pieces) { _pieces.Add((p.Mx, p.My, p.Mz, (int)p.Face)); _originalPieces.Add((p.Mx, p.My, p.Mz, (int)p.Face)); }
        Changed?.Invoke();
    }

    /// <summary>True when the kinds or cells of the draft differ from the structure's.</summary>
    public bool Dirty
    {
        get
        {
            if (!_pieces.SetEquals(_originalPieces) || !_spans.SetEquals(_originalSpans)) return true;
            if (_kinds.Count != _original.Count) return true;
            foreach (var (k, v) in _kinds)
                if (!_original.TryGetValue(k, out var o) || o != v) return true;
            return false;
        }
    }

    public int RotationOf((int Mx, int My, int Mz) key) => _rotation.GetValueOrDefault(key);

    /// <summary>The module list the command carries.</summary>
    public DesignModule[] ToModules() => _kinds.Select(kv => new DesignModule(kv.Key.Mx, kv.Key.My, kv.Key.Mz, kv.Value)).ToArray();

    /// <summary>The one command that applies the draft: <see cref="ApplyBlueprint"/> for a station, <see cref="DesignerApply"/> for the ship.</summary>
    public Command ToCommand() => Kind == StructureKind.Station ? new ApplyBlueprint(StructureId, ToModules(), null, PiecesCarried(), SpansCarried()) : new DesignerApply(ToModules(), PiecesCarried(), SpansCarried());

    /// <summary>The large modules the command carries: all of them in a world that has them, null otherwise.</summary>
    public DesignSpan[]? SpansCarried() => UsesSpans ? Spans.ToArray() : null;

    /// <summary>The collars the command carries: all of them in a world with docking collars, null otherwise (the command then reads as before).</summary>
    public DockPieceSpec[]? PiecesCarried() => UsesPieces ? Pieces.ToArray() : null;

    /// <summary>The simulation's verdict and price for the draft (the same code the apply runs): for a ship docked at a shipyard, or for the station being edited.</summary>
    public DesignReport Check(SimWorld world, int player) =>
        Kind == StructureKind.Station ? world.CheckBlueprint(player, StructureId, ToModules(), null, PiecesCarried(), SpansCarried()) : world.CheckDesign(player, ToModules(), PiecesCarried(), SpansCarried());

    /// <summary>The bounds of the draft (inclusive), widened by <paramref name="margin"/> modules on every side; a small box around the origin when it is empty.</summary>
    public ((int X, int Y, int Z) Min, (int X, int Y, int Z) Max) Bounds(int margin = 1)
    {
        if (_kinds.Count == 0) return ((-margin, -margin, -margin), (margin, margin, margin));
        int x0 = int.MaxValue, y0 = int.MaxValue, z0 = int.MaxValue, x1 = int.MinValue, y1 = int.MinValue, z1 = int.MinValue;
        foreach (var k in _kinds.Keys)
        {
            x0 = Math.Min(x0, k.Mx); x1 = Math.Max(x1, k.Mx);
            y0 = Math.Min(y0, k.My); y1 = Math.Max(y1, k.My);
            z0 = Math.Min(z0, k.Mz); z1 = Math.Max(z1, k.Mz);
        }
        return ((x0 - margin, y0 - margin, z0 - margin), (x1 + margin, y1 + margin, z1 + margin));
    }

    // ---- history ------------------------------------------------------------------------------------------------

    private Snapshot Capture() => new(_kinds.Select(kv => (kv.Key, kv.Value, _rotation.GetValueOrDefault(kv.Key))).ToArray(), _pieces.ToArray(), _spans.ToArray());

    private void Restore(Snapshot s)
    {
        _kinds.Clear(); _rotation.Clear();
        foreach (var (k, kind, rot) in s.Parts) { _kinds[k] = kind; if (rot != 0) _rotation[k] = rot; }
        _pieces.Clear();
        foreach (var p in s.Pieces) _pieces.Add(p);
        _spans.Clear();
        foreach (var p in s.Spans) _spans.Add(p);
    }

    /// <summary>Records the state before an edit; a new edit forgets what could have been redone.</summary>
    private void Push()
    {
        _undo.Add(Capture());
        if (_undo.Count > MaxHistory) _undo.RemoveAt(0);
        _redo.Clear();
    }

    private void Done()
    {
        if (UsesSpans) _spans.RemoveWhere(sp => !SpanWhole(sp)); // a large module with a cell gone falls apart into ordinary modules
        if (UsesPieces) _pieces.RemoveWhere(p => !_kinds.TryGetValue((p.Mx, p.My, p.Mz), out var k) || !DockRules.CanHostPiece(k, (Dir)p.Face)); // a collar goes with its host, or when the host stops being able to carry it
        Changed?.Invoke();
    }

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

    /// <summary>Goes back to the structure's layout (one undoable step).</summary>
    public void Revert()
    {
        if (!Dirty && _rotation.Count == 0) return;
        Push();
        _kinds.Clear(); _rotation.Clear(); _pieces.Clear(); _spans.Clear();
        foreach (var sp in _originalSpans) _spans.Add(sp);
        foreach (var (k, v) in _original) _kinds[k] = v;
        foreach (var p in _originalPieces) _pieces.Add(p);
        Done();
    }

    /// <summary>Replaces the whole draft by a saved blueprint's parts (one undoable step); cells that are not valid or exceed the limit are skipped.</summary>
    public BulkResult ReplaceWith(IEnumerable<((int Mx, int My, int Mz) Cell, ModuleKind Kind, int Rotation)> parts)
    {
        var plan = parts.Where(p => StructureGrid.IsValidLocalModule(p.Cell.Mx, p.Cell.My, p.Cell.Mz)).Take(Max).ToList();
        int skipped = parts.Count() - plan.Count;
        Push();
        _kinds.Clear(); _rotation.Clear();
        foreach (var (c, kind, rot) in plan) { _kinds[c] = kind; if (rot != 0) _rotation[c] = rot & 3; }
        Done();
        return new(plan.Count, skipped);
    }

    // ---- single edits -------------------------------------------------------------------------------------------

    private bool CellFree((int Mx, int My, int Mz) key) => StructureGrid.IsValidLocalModule(key.Mx, key.My, key.Mz) && !_kinds.ContainsKey(key);

    public DesignEditResult Add((int Mx, int My, int Mz) key, ModuleKind kind, ContentDb content, int rotation = 0)
    {
        if (!Catalogue(content).Contains(kind)) return DesignEditResult.KindNotAllowed;
        if (!StructureGrid.IsValidLocalModule(key.Mx, key.My, key.Mz)) return DesignEditResult.OutOfBounds;
        if (_kinds.ContainsKey(key)) return DesignEditResult.Occupied;
        if (_kinds.Count >= Max) return DesignEditResult.TooManyModules;
        Push();
        _kinds[key] = kind;
        if (rotation != 0) _rotation[key] = rotation & 3;
        Done();
        return DesignEditResult.Ok;
    }

    public DesignEditResult Remove((int Mx, int My, int Mz) key)
    {
        if (!_kinds.ContainsKey(key)) return DesignEditResult.Empty;
        Push();
        if (SpanAt(key) is { } sp) RemoveSpanCells(sp); else { _kinds.Remove(key); _rotation.Remove(key); }
        Done();
        return DesignEditResult.Ok;
    }

    /// <summary>Removes every module of the list in one undoable step.</summary>
    public BulkResult RemoveMany(IEnumerable<(int Mx, int My, int Mz)> keys)
    {
        var list = keys.Distinct().Where(_kinds.ContainsKey).ToList();
        if (list.Count == 0) return new(0, 0);
        Push();
        foreach (var k in list) { if (SpanAt(k) is { } sp) RemoveSpanCells(sp); else { _kinds.Remove(k); _rotation.Remove(k); } }
        Done();
        return new(list.Count, 0);
    }

    public DesignEditResult Move((int Mx, int My, int Mz) from, (int Mx, int My, int Mz) to)
    {
        if (!_kinds.ContainsKey(from)) return DesignEditResult.Empty;
        if (from == to) return DesignEditResult.SameCell;
        if (!StructureGrid.IsValidLocalModule(to.Mx, to.My, to.Mz)) return DesignEditResult.OutOfBounds;
        if (_kinds.ContainsKey(to)) return DesignEditResult.Occupied;
        var moved = MoveMany(new[] { from }, (to.Mx - from.Mx, to.My - from.My, to.Mz - from.Mz));
        return moved.Any ? DesignEditResult.Ok : DesignEditResult.Occupied;
    }

    /// <summary>Moves a group by an offset in one step. Nothing moves unless every target is a valid cell that is free or is vacated by the same move.</summary>
    public BulkResult MoveMany(IEnumerable<(int Mx, int My, int Mz)> keys, (int Dx, int Dy, int Dz) offset)
    {
        var list = keys.Distinct().Where(_kinds.ContainsKey).ToList();
        if (list.Count == 0 || offset == (0, 0, 0)) return new(0, list.Count);
        if (list.Any(k => SpanAt(k) is not null)) return new(0, list.Count); // a large module moves as a whole by being taken down and built again
        var set = new HashSet<(int, int, int)>(list.Select(k => (k.Mx, k.My, k.Mz)));
        foreach (var k in list)
        {
            var t = (k.Mx + offset.Dx, k.My + offset.Dy, k.Mz + offset.Dz);
            if (!StructureGrid.IsValidLocalModule(t.Item1, t.Item2, t.Item3)) return new(0, list.Count);
            if (_kinds.ContainsKey((t.Item1, t.Item2, t.Item3)) && !set.Contains(t)) return new(0, list.Count);
        }
        Push();
        var carried = list.Select(k => (k, _kinds[k], _rotation.GetValueOrDefault(k))).ToList();
        var carriedPieces = _pieces.Where(p => set.Contains((p.Mx, p.My, p.Mz))).ToList();
        foreach (var p in carriedPieces) _pieces.Remove(p);
        foreach (var p in carriedPieces) _pieces.Add((p.Mx + offset.Dx, p.My + offset.Dy, p.Mz + offset.Dz, p.Face)); // a collar moves with its module
        foreach (var (k, _, _) in carried) { _kinds.Remove(k); _rotation.Remove(k); }
        foreach (var (k, kind, rot) in carried)
        {
            var t = (k.Mx + offset.Dx, k.My + offset.Dy, k.Mz + offset.Dz);
            _kinds[t] = kind;
            if (rot != 0) _rotation[t] = rot;
        }
        Done();
        return new(list.Count, 0);
    }

    /// <summary>Turns a group by quarter turns about the vertical axis (view only, see the class summary).</summary>
    public BulkResult Rotate(IEnumerable<(int Mx, int My, int Mz)> keys, int quarterTurns = 1)
    {
        var list = keys.Distinct().Where(_kinds.ContainsKey).ToList();
        if (list.Count == 0 || (quarterTurns & 3) == 0) return new(0, list.Count);
        Push();
        foreach (var k in list) _rotation[k] = (_rotation.GetValueOrDefault(k) + quarterTurns) & 3;
        Done();
        return new(list.Count, 0);
    }

    /// <summary>The module kinds this draft may add: the structure's catalogue, without the retired dock module in a world with docking collars (ADR 0047).</summary>
    public IReadOnlyList<ModuleKind> Catalogue(ContentDb content)
    {
        var all = ShipDesignEditor.Catalogue(content, Kind);
        return UsesPieces ? all.Where(k => k != ModuleKind.Dock).ToList() : all;
    }

    /// <summary>The horizontal faces of a module in the draft that can carry a collar now: the kind can host it and no module of the draft stands against the face.</summary>
    public IReadOnlyList<Dir> CollarFaces((int Mx, int My, int Mz) key)
    {
        var list = new List<Dir>();
        if (!_kinds.TryGetValue(key, out var kind)) return list;
        foreach (var face in DockRules.PortFaces)
        {
            if (!DockRules.CanHostPiece(kind, face)) continue;
            var v = Dirs.Vector(face);
            if (_kinds.ContainsKey((key.Mx + v.X, key.My, key.Mz + v.Z))) continue;
            list.Add(face);
        }
        return list;
    }

    /// <summary>
    /// The collar tool on a module (ADR 0047): each click moves its collar to the next face that can carry it and, after the last one, takes it away. One undoable step.
    /// </summary>
    public DesignEditResult CycleCollar((int Mx, int My, int Mz) key)
    {
        if (!UsesPieces) return DesignEditResult.KindNotAllowed;
        if (!_kinds.ContainsKey(key)) return DesignEditResult.Empty;
        var faces = CollarFaces(key);
        if (faces.Count == 0) return DesignEditResult.KindNotAllowed;
        // A module may carry several collars; a click works on its first one and never lands on a face another collar of the module holds.
        var here = _pieces.Where(p => (p.Mx, p.My, p.Mz) == key).ToList();
        Dir? current = here.Count > 0 ? (Dir)here[0].Face : null;
        var others = here.Skip(1).Select(p => (Dir)p.Face).ToHashSet();
        faces = faces.Where(f => !others.Contains(f)).ToList();
        if (current is null && (_pieces.Count >= DockRules.MaxPieces || faces.Count == 0)) return current is null && faces.Count == 0 ? DesignEditResult.Occupied : DesignEditResult.TooManyModules;
        Push();
        if (here.Count > 0) _pieces.Remove(here[0]);
        int next = 0;
        if (current is { } c)
        {
            int at = -1;
            for (int i = 0; i < faces.Count; i++) if (faces[i] == c) at = i;
            next = at + 1;
        }
        if (next < faces.Count) _pieces.Add((key.Mx, key.My, key.Mz, (int)faces[next]));
        Done();
        return DesignEditResult.Ok;
    }

    private bool SpanWhole((int Mx, int My, int Mz, int Size) sp)
    {
        if (!_kinds.TryGetValue((sp.Mx, sp.My, sp.Mz), out var kind)) return false;
        for (int dx = 0; dx < sp.Size; dx++)
            for (int dz = 0; dz < sp.Size; dz++)
                if (!_kinds.TryGetValue((sp.Mx + dx, sp.My, sp.Mz + dz), out var k) || k != kind) return false;
        return true;
    }

    private void RemoveSpanCells(DesignSpan sp)
    {
        for (int dx = 0; dx < sp.Size; dx++)
            for (int dz = 0; dz < sp.Size; dz++)
            {
                var c = (sp.Mx + dx, sp.My, sp.Mz + dz);
                _kinds.Remove(c); _rotation.Remove(c);
            }
        _spans.Remove((sp.Mx, sp.My, sp.Mz, sp.Size));
    }

    /// <summary>The side of the biggest large module a kind may be in this draft's structure (1 when large modules are off or the kind stays single).</summary>
    public int MaxSpan(ModuleKind kind) => UsesSpans ? ModuleKindIds.MaxSpan(Kind, kind) : 1;

    /// <summary>
    /// Builds a large module (ADR 0051): a square of <paramref name="size"/> by size cells of one kind from <paramref name="anchor"/> (its lowest corner), one undoable step. Every cell has to be
    /// valid and free, the kind must be allowed that large, and the square has to touch the rest of the draft (or be the whole of an empty one).
    /// </summary>
    public DesignEditResult AddSpan((int Mx, int My, int Mz) anchor, ModuleKind kind, int size, ContentDb content)
    {
        if (!UsesSpans || size < 2 || size > MaxSpan(kind)) return DesignEditResult.KindNotAllowed;
        if (!Catalogue(content).Contains(kind)) return DesignEditResult.KindNotAllowed;
        var cells = new List<(int, int, int)>();
        for (int dx = 0; dx < size; dx++)
            for (int dz = 0; dz < size; dz++) cells.Add((anchor.Mx + dx, anchor.My, anchor.Mz + dz));
        if (cells.Any(c => !StructureGrid.IsValidLocalModule(c.Item1, c.Item2, c.Item3))) return DesignEditResult.OutOfBounds;
        if (cells.Any(c => _kinds.ContainsKey(c))) return DesignEditResult.Occupied;
        if (_kinds.Count + cells.Count > Max) return DesignEditResult.TooManyModules;
        if (_kinds.Count > 0 && !cells.Any(c => ShipDesignEditor.Touches(_kinds, (c.Item1, c.Item2, c.Item3)))) return DesignEditResult.NotAdjacent;
        Push();
        foreach (var c in cells) _kinds[c] = kind;
        _spans.Add((anchor.Mx, anchor.My, anchor.Mz, size));
        Done();
        return DesignEditResult.Ok;
    }

    public DesignEditResult Upgrade((int Mx, int My, int Mz) key, ContentDb content)
    {
        if (!_kinds.TryGetValue(key, out var kind)) return DesignEditResult.Empty;
        if (ModuleKindIds.UpgradeOf(kind) is not { } up || content.Structures is not { } k || !k.IsDefined(up)) return DesignEditResult.NoUpgrade;
        Push();
        _kinds[key] = up;
        Done();
        return DesignEditResult.Ok;
    }

    // ---- mirror, copy and paste ---------------------------------------------------------------------------------

    /// <summary>The cell a cell becomes when reflected through the plane at <paramref name="plane2"/> / 2 on the axis (twice the coordinate, so a plane between two cells is possible).</summary>
    public static (int Mx, int My, int Mz) Reflect((int Mx, int My, int Mz) key, GridAxis axis, int plane2) => axis switch
    {
        GridAxis.X => (plane2 - key.Mx, key.My, key.Mz),
        GridAxis.Y => (key.Mx, plane2 - key.My, key.Mz),
        _ => (key.Mx, key.My, plane2 - key.Mz),
    };

    /// <summary>
    /// Adds the mirror image of a group across a plane (symmetric building; the plane x = 0 is a ship's centre line). A copy whose cell is taken, invalid or
    /// over the module limit is skipped. One undoable step.
    /// </summary>
    public BulkResult Mirror(IEnumerable<(int Mx, int My, int Mz)> keys, GridAxis axis, int plane2 = 0)
    {
        var parts = keys.Distinct().Where(_kinds.ContainsKey).Select(k => (Key: k, Kind: _kinds[k], Rot: _rotation.GetValueOrDefault(k))).ToList();
        if (parts.Count == 0) return new(0, 0);
        var plan = new List<((int Mx, int My, int Mz) At, ModuleKind Kind, int Rot)>();
        int skipped = 0;
        var taken = new HashSet<(int, int, int)>();
        foreach (var (key, kind, rot) in parts)
        {
            var t = Reflect(key, axis, plane2);
            bool free = CellFree(t) && taken.Add((t.Mx, t.My, t.Mz));
            if (free && _kinds.Count + plan.Count < Max) plan.Add((t, kind, axis == GridAxis.Y ? rot : (4 - rot) & 3)); else skipped++;
        }
        if (plan.Count == 0) return new(0, skipped);
        Push();
        foreach (var (at, kind, rot) in plan)
        {
            _kinds[at] = kind;
            if (rot != 0) _rotation[at] = rot;
        }
        Done();
        return new(plan.Count, skipped);
    }

    /// <summary>Puts a group on the clipboard (its offsets are measured from its lowest cell); the draft does not change.</summary>
    public int Copy(IEnumerable<(int Mx, int My, int Mz)> keys)
    {
        var list = keys.Distinct().Where(_kinds.ContainsKey).ToList();
        _clipboard = new();
        if (list.Count == 0) return 0;
        int x0 = list.Min(k => k.Mx), y0 = list.Min(k => k.My), z0 = list.Min(k => k.Mz);
        foreach (var k in list.OrderBy(k => k))
            _clipboard.Add(((k.Mx - x0, k.My - y0, k.Mz - z0), _kinds[k], _rotation.GetValueOrDefault(k)));
        return _clipboard.Count;
    }

    /// <summary>Places the clipboard with its lowest cell at <paramref name="anchor"/>. A part that would land on a taken or invalid cell is skipped. One undoable step.</summary>
    public BulkResult Paste((int Mx, int My, int Mz) anchor, ContentDb content)
    {
        if (_clipboard.Count == 0) return new(0, 0);
        var allowed = Catalogue(content);
        var plan = new List<((int Mx, int My, int Mz) At, ModuleKind Kind, int Rot)>();
        int skipped = 0;
        foreach (var (off, kind, rot) in _clipboard)
        {
            var at = (anchor.Mx + off.Mx, anchor.My + off.My, anchor.Mz + off.Mz);
            if (CellFree(at) && allowed.Contains(kind) && _kinds.Count + plan.Count < Max) plan.Add((at, kind, rot)); else skipped++;
        }
        if (plan.Count == 0) return new(0, skipped);
        Push();
        foreach (var (at, kind, rot) in plan)
        {
            _kinds[at] = kind;
            if (rot != 0) _rotation[at] = rot;
        }
        Done();
        return new(plan.Count, skipped);
    }

    // ---- snapping and neighbours --------------------------------------------------------------------------------

    /// <summary>The module cell that contains a point of the ship's own space (module sizes are <see cref="StationGrid"/>'s): the snap of a free position to the grid.</summary>
    public static (int Mx, int My, int Mz) SnapToCell(double x, double y, double z) =>
        ((int)Math.Floor(x / StationGrid.ModuleX), (int)Math.Floor(y / StationGrid.ModuleY), (int)Math.Floor(z / StationGrid.ModuleZ));

    /// <summary>True when a free, valid cell touches a module of the draft.</summary>
    public bool CanAddAt((int Mx, int My, int Mz) key) => CellFree(key) && ShipDesignEditor.Touches(_kinds, key);
}
