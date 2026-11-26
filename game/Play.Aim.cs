using System;
using System.Collections.Generic;
using System.Linq;
using Godot;
using Starfoundry.Sim;

namespace Starfoundry.Game;

/// <summary>
/// Aiming and building in the local frame of a structure. The crosshair ray is taken into a structure's frame through the inverse of
/// its root transform: stations are only translated, but the player's ship turns freely in flight (see free flight). The ghost, the belt preview and the belt anchor belong to a
/// structure and are drawn under its root node. With structures off, structure 0 is the world and everything works as before.
/// </summary>
public partial class Play
{
    /// <summary>A cell of a structure's local grid (structure 0 is the legacy world grid).</summary>
    private readonly record struct Cell(int S, int X, int Y, int Z);

    private (int S, int Mx, int My, int Mz)? _structModuleAim;
    private (int X, int Y, int Z)? _foundAim;

    private bool AimActive => _mouseLook || _cockpitShotPath is not null || _turnShotPath is not null || _stationDemo || _structuresShot || _walkShot || _flightShot || _dockShot || _keysShotPath is not null;

    private bool StationSelected => _tool == ToolMode.Build && _palette[_selected].IsStation;

    private Node3D RootOf(int structure) => structure == 0 ? this : _stationView.StructureRoot(structure) ?? this;

    private static void Reparent(Node3D node, Node3D parent)
    {
        if (node.GetParent() == parent) return;
        node.GetParent()?.RemoveChild(node);
        parent.AddChild(node);
    }

    // ---- the cell under the crosshair -------------------------------------------------------------

    /// <summary>
    /// The cell whose floor (the plane of the current local layer, Y = <c>_cursorY</c>) the crosshair ray meets. With structures the
    /// ray is taken into the local frame of each structure; a hit whose cell lies inside one of that structure's modules wins (the
    /// nearest such hit), otherwise the plane hit on the player's ship (or the nearest structure) is returned so the ghost can show
    /// why the place is refused. Null when the ray points away from every plane or the hit is too far.
    /// </summary>
    private Cell? CellUnderAim()
    {
        var origin = AimOrigin();
        var dir = AimDirection();
        if (!_structuresOn)
        {
            if (MathF.Abs(dir.Y) < 1e-4f) return null;
            float t = (_cursorY - origin.Y) / dir.Y;
            if (t < AimMinCells || t > AimMaxCells) return null; // too close: the cell would fill the screen
            var hit = origin + dir * t;
            return new Cell(0, (int)MathF.Floor(hit.X), _cursorY, (int)MathF.Floor(hit.Z));
        }

        if (FloorRule) return CellUnderAimFloor();

        Cell? inside = null, outside = null;
        float insideT = float.MaxValue, outsideT = float.MaxValue;
        bool outsideIsShip = false;
        int ship = ShipStruct;
        foreach (var st in Net.Structures())
        {
            var (local, ldir) = AimInStruct(st.Id); // the ray in the structure's own frame (the ship may be turned in any direction)
            if (MathF.Abs(ldir.Y) < 1e-4f) continue;
            float t = (_cursorY - local.Y) / ldir.Y;
            if (t < AimMinCells || t > AimMaxCells) continue;
            var hit = local + ldir * t;
            var cell = new Cell(st.Id, (int)MathF.Floor(hit.X), _cursorY, (int)MathF.Floor(hit.Z));
            if (st.Modules.ContainsKey(StationGrid.ModuleOfCell(cell.X, cell.Y, cell.Z)))
            {
                if (t < insideT) { inside = cell; insideT = t; }
            }
            else
            {
                bool isShip = st.Id == ship;
                if ((isShip && !outsideIsShip) || (isShip == outsideIsShip && t < outsideT)) { outside = cell; outsideT = t; outsideIsShip = isShip; }
            }
        }
        return inside ?? outside;
    }

    /// <summary>True under the floor-building rule (ADR 0031): machines and belts go on the floor of buildable modules, there are no layers to move.</summary>
    private bool FloorRule => _structuresOn && !Net.FreeBuilding;

    /// <summary>The layer the build plane starts on: the floor in a floor-rule world, the old workshop layer otherwise.</summary>
    private int DefaultLayer => FloorRule ? 0 : DefaultStructureLayer;

    /// <summary>
    /// The floor cell under the crosshair (floor rule): the ray is taken into each structure's frame and meets the floor plane of each module; the
    /// nearest hit inside a buildable module wins, otherwise the nearest hit on any module floor of the ship (or of a structure) so the ghost can say
    /// why the place is refused. Null when no module floor is aimed at.
    /// </summary>
    private Cell? CellUnderAimFloor()
    {
        Cell? buildable = null, other = null;
        float buildableT = float.MaxValue, otherT = float.MaxValue;
        bool otherIsShip = false;
        int ship = ShipStruct;
        var kinds = _content.Structures;
        foreach (var st in Net.Structures())
        {
            var (local, ldir) = AimInStruct(st.Id);
            if (MathF.Abs(ldir.Y) < 1e-4f) continue;
            foreach (var ((mx, my, mz), kind) in st.Modules)
            {
                int floorY = StationGrid.FloorY(my);
                float t = (floorY - local.Y) / ldir.Y;
                if (t < AimMinCells || t > AimMaxCells) continue;
                var hit = local + ldir * t;
                int cx = (int)MathF.Floor(hit.X), cz = (int)MathF.Floor(hit.Z);
                if (cx < mx * StationGrid.ModuleX || cx >= (mx + 1) * StationGrid.ModuleX || cz < mz * StationGrid.ModuleZ || cz >= (mz + 1) * StationGrid.ModuleZ) continue;
                var cell = new Cell(st.Id, cx, floorY, cz);
                if (kinds is not null && kinds.IsBuildable(kind))
                {
                    if (t < buildableT) { buildable = cell; buildableT = t; }
                }
                else
                {
                    bool isShip = st.Id == ship;
                    if ((isShip && !otherIsShip) || (isShip == otherIsShip && t < otherT)) { other = cell; otherT = t; otherIsShip = isShip; }
                }
            }
        }
        var found = buildable ?? other;
        if (found is { } f) _cursorY = f.Y;
        return found;
    }

    /// <summary>True when the cell lies in a module whose floor can be built on.</summary>
    private bool CellInBuildableModule(Cell c) =>
        Net.TryGetModuleKindAt(c.S, c.X, c.Y, c.Z, out var kind) && _content.Structures is { } k && k.IsBuildable(kind);

    private static bool RayHitsAabb(Vector3 origin, Vector3 dir, Vector3 min, Vector3 size, out float t)
    {
        float t0 = 0f, t1 = float.MaxValue;
        t = 0f;
        for (int a = 0; a < 3; a++)
        {
            float o = origin[a], d = dir[a], lo = min[a], hi = min[a] + size[a];
            if (MathF.Abs(d) < 1e-6f) { if (o < lo || o > hi) return false; continue; }
            float ta = (lo - o) / d, tb = (hi - o) / d;
            if (ta > tb) (ta, tb) = (tb, ta);
            t0 = MathF.Max(t0, ta);
            t1 = MathF.Min(t1, tb);
            if (t0 > t1) return false;
        }
        t = t0;
        return true;
    }

    private static Vector3 ModuleMin(int mx, int my, int mz) => new(mx * StationGrid.ModuleX, my * StationGrid.ModuleY, mz * StationGrid.ModuleZ);

    private static readonly Vector3 ModuleSize = new(StationGrid.ModuleX, StationGrid.ModuleY, StationGrid.ModuleZ);

    /// <summary>
    /// The structure the module tool works on: the nearest structure the crosshair ray enters from outside (so a station seen
    /// through the ship's hull is the target), otherwise the structure the eye is in, otherwise the one whose module is nearest the
    /// ray, otherwise the ship. -1 when there is no structure.
    /// </summary>
    private int TargetStructure()
    {
        int entered = -1, inside = -1, near = -1;
        float enteredT = AimMaxCells * 2f, nearD = 40f * 40f;
        foreach (var st in Net.Structures())
        {
            var (local, dir) = AimInStruct(st.Id);
            bool camInside = false;
            float hitT = float.MaxValue;
            foreach (var (mx, my, mz) in st.Modules.Keys)
            {
                var min = ModuleMin(mx, my, mz);
                bool contains = local.X >= min.X && local.X <= min.X + ModuleSize.X && local.Y >= min.Y && local.Y <= min.Y + ModuleSize.Y
                    && local.Z >= min.Z && local.Z <= min.Z + ModuleSize.Z;
                if (contains) { camInside = true; continue; }
                if (RayHitsAabb(local, dir, min, ModuleSize, out float t) && t < hitT) hitT = t;
                var to = min + ModuleSize / 2f - local;
                float along = to.Dot(dir);
                if (along > 0f)
                {
                    float d = (to - dir * along).LengthSquared();
                    if (d < nearD) { nearD = d; near = st.Id; }
                }
            }
            if (camInside) inside = st.Id;
            else if (hitT < enteredT) { enteredT = hitT; entered = st.Id; }
        }
        if (entered >= 0) return entered;
        if (inside >= 0) return inside;
        if (near >= 0) return near;
        return ShipStruct;
    }

    // ---- placing and removing ------------------------------------------------------------------------

    private void PlaceSelectedAt(Cell cell, Cell? dragStart)
    {
        var item = _palette[_selected];
        if (item.IsBelt)
        {
            var start = dragStart is { } d && d.S == cell.S ? d : cell; // a line stays inside one structure
            int dx = cell.X - start.X, dz = cell.Z - start.Z;
            Dir dir = _dir;
            int len = 1;
            if (Math.Abs(dx) >= Math.Abs(dz) && dx != 0) { dir = dx > 0 ? Dir.PosX : Dir.NegX; len = Math.Abs(dx) + 1; }
            else if (dz != 0) { dir = dz > 0 ? Dir.PosZ : Dir.NegZ; len = Math.Abs(dz) + 1; }
            len = Math.Min(len, SimWorld.MaxSegmentCells);
            if (_structuresOn) Send(new PlaceSegmentIn(cell.S, start.X, start.Y, start.Z, dir, len, item.Tier));
            else Send(new PlaceSegment(start.X, start.Y, start.Z, dir, len, item.Tier));
        }
        else
        {
            int recipe = ChosenRecipe(item.Machine);
            var role = _content.Machines[item.Machine].Role;
            int r = role is MachineRole.Lab or MachineRole.Depot ? -1 : recipe;
            if (_structuresOn) Send(new PlaceMachineIn(cell.S, cell.X, cell.Y, cell.Z, item.Machine, r));
            else Send(new PlaceMachine(cell.X, cell.Y, cell.Z, item.Machine, r));
        }
    }

    private void RemoveAt(Cell cell)
    {
        var (kind, id) = _world.EntityAt(cell.S, cell.X, cell.Y, cell.Z);
        if (kind == SimWorld.EntityKind.Segment) Send(new RemoveSegment(id));
        else if (kind == SimWorld.EntityKind.Machine) Send(new RemoveMachine(id));
        else if (_structuresOn)
        {
            // An empty cell inside a module, with the module tool in hand: take the module away (the sim refuses when it holds
            // something, would split the structure, or is the core of a station with other modules).
            if (!ModuleSelected) return;
            var m = StationGrid.ModuleOfCell(cell.X, cell.Y, cell.Z);
            if (Net.GetStructure(cell.S).Modules.ContainsKey(m)) Send(new RemoveModuleIn(cell.S, m.Mx, m.My, m.Mz));
        }
        else if (_stationRules)
        {
            var m = StationGrid.ModuleOfCell(cell.X, cell.Y, cell.Z);
            if (Net.HasModule(Player, m.Mx, m.My, m.Mz)) Send(new RemoveModule(m.Mx, m.My, m.Mz));
        }
    }

    // ---- why a placement is refused ------------------------------------------------------------------------

    /// <summary>The sim's own checks for a machine or belt in a structure cell, as text: wrong module kind, outside the modules, too far, taken, cost.</summary>
    private string? StructurePlacementProblem(Cell cell)
    {
        var item = _palette[_selected];
        if (ItemLocked(item)) return L("why.locked");
        if (!_world.InBuildRange(Player, cell.S, cell.X, cell.Y, cell.Z)) return L("why.outOfRange");
        if (!Net.TryGetModuleKindAt(cell.S, cell.X, cell.Y, cell.Z, out var kind)) return Why(Reject.OutsideStation);
        if (_content.Structures is { } kinds)
        {
            var problem = kinds.CellProblem(kind, cell.Y, item.IsBelt, item.IsBelt ? MachineRole.Crafter : _content.Machines[item.Machine].Role, Net.FreeBuilding);
            if (problem != Reject.None) return Why(problem);
        }
        if (_world.EntityAt(cell.S, cell.X, cell.Y, cell.Z).Kind != SimWorld.EntityKind.None) return L("why.overlap");
        if (_gameMode == GameMode.Campaign)
        {
            var cost = item.IsBelt ? _content.Belts[(int)item.Tier]?.CostPerCell ?? Array.Empty<ItemStack>() : _content.Machines[item.Machine].Cost;
            if (!Net.CanPay(Player, cost)) return L("why.notEnough");
        }
        return null;
    }

    private string Missing(ItemStack[] cost)
    {
        var missing = cost.Where(s => Net.HoldCount(Player, s.Item) < s.Count)
            .Select(s => new ItemStack(s.Item, s.Count - Net.HoldCount(Player, s.Item))).ToArray();
        return L("why.moduleMissing", Stacks(missing));
    }

    // ---- the module tool (structures) -------------------------------------------------------------------------

    private static readonly ModuleKind[] PlaceableKinds =
        { ModuleKind.Cockpit, ModuleKind.Engine, ModuleKind.Reactor, ModuleKind.Cargo, ModuleKind.Workshop, ModuleKind.Defence, ModuleKind.Hull, ModuleKind.DroneBay, ModuleKind.Shield, ModuleKind.Sensor, ModuleKind.SensorShort, ModuleKind.SensorLong, ModuleKind.CommsAntenna, ModuleKind.ReactorMk2, ModuleKind.Dock, ModuleKind.Shipyard };

    private string KindName(ModuleKind k) => L("module." + k.ToString().ToLowerInvariant());

    /// <summary>N: the first press picks the module tool, the next ones cycle the kind it places.</summary>
    private void CycleModuleKind()
    {
        int tool = _palette.FindIndex(b => b.IsModule);
        if (tool < 0) return;
        if (_tool == ToolMode.Build && _selected == tool)
        {
            int i = Array.IndexOf(PlaceableKinds, _moduleKind);
            _moduleKind = PlaceableKinds[(i + 1) % PlaceableKinds.Length];
            string note = DroneBayLocked(_moduleKind) ? $"\n{Why(Reject.Locked)}" : "";
            Toast($"{KindName(_moduleKind)}: {L("module." + _moduleKind.ToString().ToLowerInvariant() + ".purpose")}{note}", 4.0);
        }
        else SelectBuild(tool);
    }

    private static (long X, long Y, long Z) ModuleCentreUnits(Structure s, int mx, int my, int mz)
    {
        long cu = Units.CellUnits;
        return (s.OriginX + ((long)mx * StationGrid.ModuleX + StationGrid.ModuleX / 2) * cu,
                s.OriginY + ((long)my * StationGrid.ModuleY + StationGrid.ModuleY / 2) * cu,
                s.OriginZ + ((long)mz * StationGrid.ModuleZ + StationGrid.ModuleZ / 2) * cu);
    }

    /// <summary>The free module position, face-adjacent to a module of the target structure, the module ghost snaps to.</summary>
    private (int S, int Mx, int My, int Mz)? StructModuleUnderAim()
    {
        int s = TargetStructure();
        if (s <= 0) return null;
        var st = Net.GetStructure(s);
        var (local, dir) = AimInStruct(s);

        Vector3? planeHit = null;
        if (MathF.Abs(dir.Y) > 1e-4f)
        {
            float t = (_cursorY - local.Y) / dir.Y;
            if (t >= AimMinCells && t <= AimMaxCells) planeHit = local + dir * t;
        }
        if (planeHit is { } ph)
        {
            var m = StationGrid.ModuleOfCell((int)MathF.Floor(ph.X), _cursorY, (int)MathF.Floor(ph.Z));
            if (!st.Modules.ContainsKey(m) && StructureGrid.IsValidLocalModule(m.Mx, m.My, m.Mz) && Net.StructureModuleTouches(s, m.Mx, m.My, m.Mz))
                return (s, m.Mx, m.My, m.Mz);
        }

        (int, int, int)? best = null;
        float bestD = float.MaxValue;
        foreach (var (mx, my, mz) in st.Modules.Keys)
            foreach (var (dx, dy, dz) in new[] { (1, 0, 0), (-1, 0, 0), (0, 1, 0), (0, -1, 0), (0, 0, 1), (0, 0, -1) })
            {
                int nx = mx + dx, ny = my + dy, nz = mz + dz;
                if (st.Modules.ContainsKey((nx, ny, nz)) || !StructureGrid.IsValidLocalModule(nx, ny, nz)) continue;
                var centre = StationView.Centre(nx, ny, nz);
                float d;
                if (planeHit is { } p) d = (centre - p).LengthSquared();
                else
                {
                    var to = centre - local;
                    float along = to.Dot(dir);
                    if (along <= 0f) continue;
                    d = (to - dir * along).LengthSquared();
                }
                if (d < bestD) { bestD = d; best = (nx, ny, nz); }
            }
        return best is { } b ? (s, b.Item1, b.Item2, b.Item3) : null;
    }

    /// <summary>Why <see cref="PlaceModuleIn"/> would be refused for this module and the selected kind, or null if it would succeed.</summary>
    private string? StructModuleProblem(int s, int mx, int my, int mz)
    {
        var st = Net.GetStructure(s);
        if (DroneBayLocked(_moduleKind)) return Why(Reject.Locked);
        if (!StructureGrid.IsValidLocalModule(mx, my, mz)) return Why(Reject.OutOfBounds);
        if (st.Modules.ContainsKey((mx, my, mz))) return L("why.moduleExists");
        if (st.Modules.Count > 0)
        {
            if (!Net.StructureModuleTouches(s, mx, my, mz)) return Why(Reject.NotAdjacent);
            if (_moduleKind == ModuleKind.Core) return Why(Reject.KindNotAllowed);
        }
        else if (st.Kind == StructureKind.Station && _moduleKind != ModuleKind.Core) return Why(Reject.KindNotAllowed);
        // A station is fixed: no cockpit and no engine; and a ship has no core (ADR 0029).
        if (st.Kind == StructureKind.Station && _moduleKind is ModuleKind.Cockpit or ModuleKind.Engine) return L("why.stationFixed");
        if (st.Kind == StructureKind.Ship && _moduleKind == ModuleKind.Core) return Why(Reject.KindNotAllowed);
        if (Net.ModuleOverlapsDocked(s, mx, my, mz)) return Why(Reject.Overlap); // a docked ship sits there

        if (st.Kind == StructureKind.Station)
        {
            var (wx, wy, wz) = ModuleCentreUnits(st, mx, my, mz);
            if (!Net.WorldPointInRange(Player, wx, wy, wz, PlayerRules.BuildRangeCells)) return Why(Reject.OutOfRange);
            long cu = Units.CellUnits, bx = st.OriginX + (long)mx * StationGrid.ModuleX * cu, by = st.OriginY + (long)my * StationGrid.ModuleY * cu, bz = st.OriginZ + (long)mz * StationGrid.ModuleZ * cu;
            if (Net.BoxTouchesDepositRock(bx, by, bz, bx + StationGrid.ModuleX * cu, by + StationGrid.ModuleY * cu, bz + StationGrid.ModuleZ * cu)) return Why(Reject.OverlapsDeposit); // ADR 0033
        }
        if (_gameMode == GameMode.Campaign && _content.Structures is { } kinds)
        {
            var cost = kinds.ModuleCost(_moduleKind);
            if (!Net.CanPay(Player, cost)) return Missing(cost);
        }
        return null;
    }

    private void UpdateStructModuleGhost()
    {
        _dragGhost.Visible = false;
        _structModuleAim = StructModuleUnderAim();
        if (_structModuleAim is not { } m)
        {
            _ghost.Visible = false;
            _tooltipLabel.Text = L("why.outOfRange");
            _crosshair.Tint = Crosshair.Invalid;
            return;
        }
        _ghost.Visible = true;
        Reparent(_ghost, RootOf(m.S));
        _ghost.Scale = new Vector3(StationGrid.ModuleX, StationGrid.ModuleY, StationGrid.ModuleZ) * 0.99f;
        _ghost.Position = StationView.Centre(m.Mx, m.My, m.Mz); // local to the structure
        string? problem = StructModuleProblem(m.S, m.Mx, m.My, m.Mz);
        _crosshair.Tint = problem is null ? Crosshair.Normal : Crosshair.Invalid;
        ((StandardMaterial3D)_ghost.MaterialOverride).AlbedoColor = problem is null ? new Color(0.4f, 1f, 0.5f, 0.22f) : new Color(1f, 0.3f, 0.25f, 0.22f);
        string purpose = L("module." + _moduleKind.ToString().ToLowerInvariant() + ".purpose");
        _tooltipLabel.Text = problem ?? $"{KindName(_moduleKind)}: {purpose}\n{L("tip.cost", Stacks(_content.Structures?.ModuleCost(_moduleKind) ?? Array.Empty<ItemStack>()))}";
    }

    // ---- founding a station ------------------------------------------------------------------------------------

    /// <summary>The world cell for a new station core, centred on the aim point of the plane at the ship's altitude.</summary>
    private (int X, int Y, int Z)? FoundCellUnderAim()
    {
        if (!Net.HasPlayer(Player)) return null;
        var origin = AimOrigin();
        var dir = AimDirection();
        if (MathF.Abs(dir.Y) < 1e-4f) return null;
        int planeY = (int)MathF.Round(ShipPosition(0f).Y);
        float t = (planeY - origin.Y) / dir.Y;
        if (t < AimMinCells || t > AimMaxCells) return null;
        var hit = origin + dir * t;
        return ((int)MathF.Floor(hit.X) - StationGrid.ModuleX / 2, planeY, (int)MathF.Floor(hit.Z) - StationGrid.ModuleZ / 2);
    }

    /// <summary>Why <see cref="FoundStation"/> would be refused at this world cell (the sim's checks), or null if it would succeed.</summary>
    private string? FoundProblem((int X, int Y, int Z) c)
    {
        long cu = Units.CellUnits;
        long ox = c.X * cu, oy = c.Y * cu, oz = c.Z * cu;
        if (!Net.WorldPointInRange(Player, ox + cu / 2, oy + cu / 2, oz + cu / 2, PlayerRules.BuildRangeCells)) return Why(Reject.OutOfRange);
        long ex = ox + StationGrid.ModuleX * cu, ey = oy + StationGrid.ModuleY * cu, ez = oz + StationGrid.ModuleZ * cu;
        foreach (var other in Net.Structures())
            foreach (var (m, _) in other.Modules)
            {
                long mx0 = other.OriginX + (long)m.Mx * StationGrid.ModuleX * cu, my0 = other.OriginY + (long)m.My * StationGrid.ModuleY * cu,
                    mz0 = other.OriginZ + (long)m.Mz * StationGrid.ModuleZ * cu;
                if (ox < mx0 + StationGrid.ModuleX * cu && mx0 < ex && oy < my0 + StationGrid.ModuleY * cu && my0 < ey
                    && oz < mz0 + StationGrid.ModuleZ * cu && mz0 < ez)
                    return Why(Reject.Overlap);
            }
        if (Net.BoxTouchesDepositRock(ox, oy, oz, ex, ey, ez)) return Why(Reject.OverlapsDeposit); // ADR 0033: the rock of a deposit is solid
        if (_gameMode == GameMode.Campaign && _content.Structures is { } kinds)
        {
            var cost = kinds.ModuleCost(ModuleKind.Core);
            if (!Net.CanPay(Player, cost)) return Missing(cost);
        }
        return null;
    }

    private void UpdateFoundGhost()
    {
        _dragGhost.Visible = false;
        _foundAim = FoundCellUnderAim();
        if (_foundAim is not { } f)
        {
            _ghost.Visible = false;
            _tooltipLabel.Text = L("why.outOfRange");
            _crosshair.Tint = Crosshair.Invalid;
            return;
        }
        _ghost.Visible = true;
        Reparent(_ghost, this); // a station is founded in the world, not inside a structure
        _ghost.Scale = new Vector3(StationGrid.ModuleX, StationGrid.ModuleY, StationGrid.ModuleZ) * 0.99f;
        _ghost.Position = new Vector3(f.X, f.Y, f.Z) + ModuleSize / 2f;
        string? problem = FoundProblem(f);
        _crosshair.Tint = problem is null ? Crosshair.Normal : Crosshair.Invalid;
        ((StandardMaterial3D)_ghost.MaterialOverride).AlbedoColor = problem is null ? new Color(0.4f, 1f, 0.5f, 0.22f) : new Color(1f, 0.3f, 0.25f, 0.22f);
        _tooltipLabel.Text = problem ?? $"{L("ui.foundStation")}: {L("tip.cost", Stacks(_content.Structures?.ModuleCost(ModuleKind.Core) ?? Array.Empty<ItemStack>()))}";
    }

    // ---- HUD and markers of structures -------------------------------------------------------------------------

    /// <summary>The ship's name, kind and flight readiness; with <paramref name="details"/> also its modules by kind and every station.</summary>
    private void StructureHud(System.Text.StringBuilder sb, bool details)
    {
        int ship = ShipStruct;
        if (ship >= 0)
        {
            string flight = Net.IsDocked(Player) ? L("hud.flightDocked") : Net.CanFly(Player) ? L("hud.flightReady") : L("hud.flightNot");
            sb.Append(L("hud.structure", L("structure.ship"), flight)).Append('\n');
            if (details) sb.Append(L("hud.modulesDetail", ModuleSummary(Net.GetStructure(ship)) + ", " + L("hud.integrity", Net.StructureHpFraction(ship) / 10))).Append('\n');
        }
        DroneHud(sb);
        if (!details) return;
        foreach (var st in Net.Structures().Where(s => s.Kind == StructureKind.Station))
        {
            sb.Append(L("hud.structure", L("structure.station"), ModuleSummary(st) + ", " + L("hud.integrity", Net.StructureHpFraction(st.Id) / 10))).Append('\n');
            sb.Append("  ").Append(StructurePowerLine(st.Id, L("structure.station"))).Append('\n');
        }
    }

    /// <summary>"Cockpit 1, Workshop 2": the module count per kind, in kind order.</summary>
    private string ModuleSummary(Structure st) =>
        string.Join(", ", st.Modules.Values.GroupBy(k => k).OrderBy(g => (int)g.Key).Select(g => $"{KindName(g.Key)} {g.Count()}"));

    /// <summary>The nearest station and the machine the current guided step points at on the ship.</summary>
    private void StructureMarkers(Action<string, Color, Vector3, float> add)
    {
        var eye = _camera.GlobalPosition;
        bool found = false;
        Vector3 nearestAt = default;
        float best = float.MaxValue;
        foreach (var st in Net.Structures().Where(s => s.Kind == StructureKind.Station))
        {
            var xf = StructXform(st.Id);
            foreach (var (mx, my, mz) in st.Modules.Keys)
            {
                var c = xf * StationView.Centre(mx, my, mz);
                float d = (c - eye).LengthSquared();
                if (d < best) { best = d; nearestAt = c; found = true; }
            }
        }
        if (found) add(L("mark.station"), new Color(0.45f, 0.8f, 1f), nearestAt, 10f);

        DockMarker(add);
        IceMarker(add);
        DroneMarkers(add);

        // The guided step about the factory points at the ship's assembler (where the first plate appears).
        int ship = ShipStruct;
        if (ship >= 0 && _objectives.Current == Starfoundry.Ui.Objective.WatchFactory)
            foreach (int id in _machineIds)
            {
                if (!Net.IsMachineAlive(id)) continue;
                var m = Net.GetMachineInfo(id);
                if (m.Structure != ship || _content.Machines[m.Type].Id != "core:assembler") continue;
                add(MachineName(m.Type), new Color(1f, 0.8f, 0.35f), StructToWorld(ship, CellCenter(m.X, m.Y, m.Z)), 3f);
                break;
            }
    }
}
