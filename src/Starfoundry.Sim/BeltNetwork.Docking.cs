namespace Starfoundry.Sim;

/// <summary>Fixed rules of docking a ship at a station (ADR 0029). Part of the simulation, so changing them changes states.</summary>
public static class DockRules
{
    /// <summary>A ship docks when the centre of its port is within this many cells of the station port it joins.</summary>
    public const int RangeCells = 12;

    /// <summary>A ship docks only while its commanded speed is at most this many units per tick (a quarter of the base speed of 48).</summary>
    public const int MaxSpeedUnitsPerTick = 12;

    /// <summary>Items a station core keeps in the station stock.</summary>
    public const int CoreStockCapacity = 60;

    /// <summary>Items each dock module adds to the stock of its station.</summary>
    public const int DockStockCapacity = 40;

    /// <summary>A docked ship regains module hit points this many times as often as a free one, and does so under the station's power.</summary>
    public const int RepairSpeedup = 3;

    /// <summary>The faces ports can open on: the four horizontal ones (modules are stacked in rings, not through floors).</summary>
    public static bool IsPortFace(Dir face) => face is Dir.PosX or Dir.NegX or Dir.PosZ or Dir.NegZ;

    /// <summary>The face a ship port must look at to join a station port on <paramref name="face"/>.</summary>
    public static Dir Opposite(Dir face) => face switch
    {
        Dir.PosX => Dir.NegX,
        Dir.NegX => Dir.PosX,
        Dir.PosY => Dir.NegY,
        Dir.NegY => Dir.PosY,
        Dir.PosZ => Dir.NegZ,
        _ => Dir.PosZ,
    };

    /// <summary>The port faces in their fixed order (the order ports are listed and ties are broken in).</summary>
    public static readonly Dir[] PortFaces = { Dir.PosX, Dir.NegX, Dir.PosZ, Dir.NegZ };
}

/// <summary>A docking port: a free horizontal face of a core, dock or cockpit module.</summary>
/// <param name="Free">Nothing is built against the face and no ship is docked there.</param>
/// <param name="DockedShip">The id of the ship structure docked at this port (stations only), or -1.</param>
public readonly record struct DockPort(int Structure, int Mx, int My, int Mz, Dir Face, bool Free, int DockedShip);

/// <summary>The best port to dock at, from the point of view of a ship: which ports would join and how far apart they are.</summary>
/// <param name="DistanceUnits">Distance in units between the centre of the ship's port and the station's port.</param>
/// <param name="SpeedMilli">The ship's commanded speed in thousandths of a unit per tick.</param>
public readonly record struct DockCandidate(
    int Station, int Mx, int My, int Mz, Dir Face, int ShipMx, int ShipMy, int ShipMz, long DistanceUnits, int SpeedMilli, bool InRange, bool SlowEnough, bool Aligned = true);

/// <summary>The docking state of a ship.</summary>
public readonly record struct DockState(
    int Station, int ShipMx, int ShipMy, int ShipMz, Dir ShipFace, int StationMx, int StationMy, int StationMz, Dir StationFace);

public sealed partial class BeltNetwork
{
    // ---- ports ---------------------------------------------------------------------------------------

    /// <summary>True if modules of this kind in a structure of this kind expose docking ports: the core of a station, the cockpit of a ship (on its two flanks) and the dock module of both.</summary>
    public static bool HasPorts(StructureKind structureKind, ModuleKind kind) =>
        kind == ModuleKind.Dock || (kind == ModuleKind.Core && structureKind == StructureKind.Station) || (kind == ModuleKind.Cockpit && structureKind == StructureKind.Ship);

    private static (int X, int Z) FaceStep(Dir face) => face switch
    {
        Dir.PosX => (1, 0),
        Dir.NegX => (-1, 0),
        Dir.PosZ => (0, 1),
        _ => (0, -1),
    };

    /// <summary>The ports of a structure in module key order, then face order. A port is free when no module touches it (and, for a station, no ship is docked there).</summary>
    public IEnumerable<DockPort> DockPorts(int structure)
    {
        if (!_structures.TryGetValue(structure, out var s)) yield break;
        foreach (var (key, kind) in s.ModuleMap)
        {
            if (!HasPorts(s.Kind, kind)) continue;
            foreach (var face in DockRules.PortFaces)
            {
                if (kind == ModuleKind.Cockpit && face is Dir.PosZ or Dir.NegZ) continue; // a cockpit docks sideways: its nose and the engine side are not ports
                var (dx, dz) = FaceStep(face);
                if (s.ModuleMap.ContainsKey((key.Mx + dx, key.My, key.Mz + dz))) continue; // a module is built against it: there is no outside
                int docked = DockedShipAt(s, key, face);
                yield return new DockPort(structure, key.Mx, key.My, key.Mz, face, docked < 0, docked);
            }
        }
    }

    /// <summary>The ship docked at a port of a station, or -1.</summary>
    private int DockedShipAt(Structure station, (int Mx, int My, int Mz) key, Dir face)
    {
        if (station.Kind != StructureKind.Station) return -1;
        foreach (var other in _structures.Values)
            if (other.DockedTo == station.Id && other.DockStationModule == key && other.DockStationFace == face) return other.Id;
        return -1;
    }

    /// <summary>World position (units) of the centre of a port: the middle of the module face it opens on.</summary>
    public (long X, long Y, long Z) PortWorldPoint(int structure, int mx, int my, int mz, Dir face)
    {
        var s = GetStructure(structure);
        var (dx, dz) = FaceStep(face);
        long cu = Units.CellUnits;
        return StructureToWorld(s, ((long)mx * StationGrid.ModuleX + StationGrid.ModuleX / 2 + dx * (StationGrid.ModuleX / 2)) * cu,
                ((long)my * StationGrid.ModuleY + StationGrid.ModuleY / 2) * cu,
                ((long)mz * StationGrid.ModuleZ + StationGrid.ModuleZ / 2 + dz * (StationGrid.ModuleZ / 2)) * cu);
    }

    // ---- state ---------------------------------------------------------------------------------------

    /// <summary>True if the player's ship is docked at a station.</summary>
    public bool IsDocked(int player) =>
        _players.TryGetValue(player, out var p) && p.ShipStructure >= 0 && _structures.TryGetValue(p.ShipStructure, out var s) && s.DockedTo >= 0;

    /// <summary>The docking state of the player's ship, or false when it is free.</summary>
    public bool TryGetDock(int player, out DockState state)
    {
        state = default;
        if (!_players.TryGetValue(player, out var p) || p.ShipStructure < 0 || !_structures.TryGetValue(p.ShipStructure, out var s) || s.DockedTo < 0) return false;
        state = new DockState(s.DockedTo, s.DockShipModule.Mx, s.DockShipModule.My, s.DockShipModule.Mz, s.DockShipFace,
            s.DockStationModule.Mx, s.DockStationModule.My, s.DockStationModule.Mz, s.DockStationFace);
        return true;
    }

    /// <summary>The docking state of a ship structure, or false when it is free (or not a ship).</summary>
    public bool TryGetStructureDock(int ship, out DockState state)
    {
        state = default;
        if (!_structures.TryGetValue(ship, out var s) || s.DockedTo < 0) return false;
        state = new DockState(s.DockedTo, s.DockShipModule.Mx, s.DockShipModule.My, s.DockShipModule.Mz, s.DockShipFace,
            s.DockStationModule.Mx, s.DockStationModule.My, s.DockStationModule.Mz, s.DockStationFace);
        return true;
    }

    /// <summary>The ships docked at a station, in structure id order.</summary>
    public IEnumerable<int> DockedShips(int station)
    {
        foreach (var s in _structures.Values)
            if (s.DockedTo == station) yield return s.Id;
    }

    /// <summary>The station a structure draws on: itself for a station, the station it is docked at for a ship, else null.</summary>
    private Structure? StockHolder(Structure s)
    {
        if (s.Kind == StructureKind.Station) return s;
        return s.DockedTo >= 0 && _structures.TryGetValue(s.DockedTo, out var st) ? st : null;
    }

    private static long ISqrt(long n)
    {
        if (n <= 0) return 0;
        long x = (long)Math.Sqrt(n);
        while (x * x > n) x--;
        while ((x + 1) * (x + 1) <= n) x++;
        return x;
    }

    /// <summary>How fast the player's ship is commanded to go, in thousandths of a unit per tick (0 without a ship structure, thrust or movement).</summary>
    public int ShipSpeedMilli(int player)
    {
        if (!_players.TryGetValue(player, out var p) || p.ShipStructure < 0 || !_structures.TryGetValue(p.ShipStructure, out var s)) return 0;
        long full = (long)PlayerRules.MoveUnitsPerTick * EnginePercent(s) * s.Satisfaction / StructureFlight.PercentScale; // thousandths of a unit per tick at full thrust
        if (p.HasThrust)
        {
            long mag = ISqrt((long)p.ThrustX * p.ThrustX + (long)p.ThrustY * p.ThrustY + (long)p.ThrustZ * p.ThrustZ);
            return (int)(full * mag / PlayerRules.ThrustScale);
        }
        int axes = Math.Abs(p.MoveX) + Math.Abs(p.MoveY) + Math.Abs(p.MoveZ);
        if (axes == 0) return 0;
        int speed = axes switch { 1 => PlayerRules.MoveUnitsPerTick, 2 => PlayerRules.MoveUnitsPerTickDiagonal2, _ => PlayerRules.MoveUnitsPerTickDiagonal3 };
        return (int)((long)speed * EnginePercent(s) * s.Satisfaction / StructureFlight.PercentScale);
    }

    // ---- choosing a port pair --------------------------------------------------------------------------

    /// <summary>The ship's free port on a face nearest to a world point: the lowest module key wins ties. False when it has none.</summary>
    private bool TryNearestShipPort(Structure ship, Dir face, long px, long py, long pz, out (int Mx, int My, int Mz) key, out long distSq)
    {
        key = default; distSq = long.MaxValue;
        bool found = false;
        foreach (var port in DockPorts(ship.Id))
        {
            if (!port.Free || port.Face != face) continue;
            var (x, y, z) = PortWorldPoint(ship.Id, port.Mx, port.My, port.Mz, face);
            long d = (x - px) * (x - px) + (y - py) * (y - py) + (z - pz) * (z - pz);
            if (!found || d < distSq) { found = true; distSq = d; key = (port.Mx, port.My, port.Mz); }
        }
        return found;
    }

    /// <summary>
    /// Works out how the player's ship would join a port of a station: the station port must exist and be free, and the ship must have a
    /// free port on the opposite face (the nearest is used). Does not check range, speed or overlap, which the candidate reports.
    /// </summary>
    public bool TryDockCandidate(int player, int station, int mx, int my, int mz, Dir face, out DockCandidate candidate)
    {
        candidate = default;
        if (!StructureRules || !_players.TryGetValue(player, out var p) || p.ShipStructure < 0 || p.ShipLost) return false;
        if (!_structures.TryGetValue(p.ShipStructure, out var ship) || !_structures.TryGetValue(station, out var st)) return false;
        if (st.Kind != StructureKind.Station || !DockRules.IsPortFace(face)) return false;
        bool open = false;
        foreach (var port in DockPorts(station))
            if (port.Free && port.Mx == mx && port.My == my && port.Mz == mz && port.Face == face) { open = true; break; }
        if (!open) return false;
        var (sx, sy, sz) = PortWorldPoint(station, mx, my, mz, face);
        // The ship's nose may be anywhere: look for the free port on the opposite face nearest to where the station port is now.
        if (!TryNearestShipPort(ship, DockRules.Opposite(face), sx, sy, sz, out var shipKey, out long distSq)) return false;
        long range = (long)DockRules.RangeCells * Units.CellUnits;
        int speed = ShipSpeedMilli(player);
        candidate = new DockCandidate(station, mx, my, mz, face, shipKey.Mx, shipKey.My, shipKey.Mz, ISqrt(distSq), speed,
            distSq <= range * range, speed <= DockRules.MaxSpeedUnitsPerTick * 1000, ship.Attitude.IsDockAligned);
        return true;
    }

    /// <summary>The nearest dockable port of the player's stations within <paramref name="withinCells"/> of the ship, for guidance. False when none is that close.</summary>
    public bool TryFindDockCandidate(int player, int withinCells, out DockCandidate best)
    {
        best = default;
        if (!StructureRules || IsDocked(player)) return false;
        bool found = false;
        long limit = (long)withinCells * Units.CellUnits;
        foreach (var st in _structures.Values)
        {
            if (st.Kind != StructureKind.Station || st.Owner != player) continue;
            foreach (var port in DockPorts(st.Id))
            {
                if (!port.Free || !TryDockCandidate(player, st.Id, port.Mx, port.My, port.Mz, port.Face, out var c) || c.DistanceUnits > limit) continue;
                if (!found || c.DistanceUnits < best.DistanceUnits) { best = c; found = true; }
            }
        }
        return found;
    }

    /// <summary>
    /// The ship origin (units) that puts the ship's port module against the station port: the ship's module at <paramref name="shipKey"/> lands on the
    /// module cell beyond the station port, in the station's lattice.
    /// </summary>
    private (long X, long Y, long Z) DockPose(Structure station, (int Mx, int My, int Mz) stationKey, Dir face, (int Mx, int My, int Mz) shipKey)
    {
        var (dx, dz) = FaceStep(face);
        long cu = Units.CellUnits;
        return (station.OriginX + ((long)stationKey.Mx + dx - shipKey.Mx) * StationGrid.ModuleX * cu,
                station.OriginY + ((long)stationKey.My - shipKey.My) * StationGrid.ModuleY * cu,
                station.OriginZ + ((long)stationKey.Mz + dz - shipKey.Mz) * StationGrid.ModuleZ * cu);
    }

    /// <summary>True when the ship, put at the dock pose, would intersect a module of any other structure.</summary>
    private bool DockPoseOverlaps(Structure ship, long ox, long oy, long oz)
    {
        long cu = Units.CellUnits;
        long sx = StationGrid.ModuleX * cu, sy = StationGrid.ModuleY * cu, sz = StationGrid.ModuleZ * cu;
        foreach (var other in _structures.Values)
        {
            if (other.Id == ship.Id) continue;
            foreach (var om in other.ModuleMap.Keys)
            {
                var (bmin, bmax) = ModuleWorldBounds(other, om); // a turned ship is bounded by its box around the turned module
                foreach (var m in ship.ModuleMap.Keys)
                {
                    long bx = ox + m.Mx * sx, by = oy + m.My * sy, bz = oz + m.Mz * sz;
                    if (bmin.X < bx + sx && bx < bmax.X && bmin.Y < by + sy && by < bmax.Y && bmin.Z < bz + sz && bz < bmax.Z) return true;
                }
            }
        }
        return false;
    }

    /// <summary>
    /// Checks a <see cref="DockAt"/> for the player and, when it is allowed, returns the pair of ports to join. The reject reasons are in the order the
    /// player meets them: already docked, then the ports, the range, the speed and finally the space.
    /// </summary>
    internal Reject CheckDock(int player, DockAt d, out DockCandidate candidate, out (long X, long Y, long Z) pose)
    {
        candidate = default; pose = default;
        if (!StructureRules) return Reject.NotAllowedHere;
        if (!_players.TryGetValue(player, out var p) || p.ShipStructure < 0 || p.ShipLost) return Reject.NoSuchStructure;
        var ship = _structures[p.ShipStructure];
        if (ship.DockedTo >= 0) return Reject.AlreadyDocked;
        if (d.Station <= 0 || !_structures.TryGetValue(d.Station, out var st)) return Reject.NoSuchStructure;
        if (st.Kind != StructureKind.Station || st.Owner != player || !DockRules.IsPortFace(d.Face)) return Reject.NotAllowedHere;
        if (p.JumpRemaining > 0) return Reject.Busy;
        if (!TryDockCandidate(player, d.Station, d.Mx, d.My, d.Mz, d.Face, out candidate)) return Reject.NoFreePort;
        if (!candidate.InRange) return Reject.OutOfRange;
        if (!candidate.SlowEnough) return Reject.TooFast;
        if (!candidate.Aligned) return Reject.Misaligned;
        pose = DockPose(st, (d.Mx, d.My, d.Mz), d.Face, (candidate.ShipMx, candidate.ShipMy, candidate.ShipMz));
        if (DockPoseOverlaps(ship, pose.X, pose.Y, pose.Z)) return Reject.Overlap;
        return Reject.None;
    }

    /// <summary>Locks the player's ship to the port pose (the command layer has checked it). The ship stops and keeps its structure id.</summary>
    internal void ApplyDock(int player, DockAt d, DockCandidate c, (long X, long Y, long Z) pose)
    {
        var p = _players[player];
        var ship = _structures[p.ShipStructure];
        ship.OriginX = pose.X; ship.OriginY = pose.Y; ship.OriginZ = pose.Z;
        ship.Attitude = Attitude.Identity; // the ship settles onto the port's axes: the pose is lattice aligned
        SyncPlayerToStructure(p, ship);
        p.MoveX = p.MoveY = p.MoveZ = 0;
        p.ClearThrust();
        ship.DockedTo = d.Station;
        ship.DockShipModule = (c.ShipMx, c.ShipMy, c.ShipMz); ship.DockShipFace = DockRules.Opposite(d.Face);
        ship.DockStationModule = (d.Mx, d.My, d.Mz); ship.DockStationFace = d.Face;
        StructureVersion++;
    }

    /// <summary>Frees the player's ship: it keeps its pose and may thrust again. Returns false when it was not docked.</summary>
    internal bool ApplyUndock(int player)
    {
        if (!_players.TryGetValue(player, out var p) || p.ShipStructure < 0 || !_structures.TryGetValue(p.ShipStructure, out var ship) || ship.DockedTo < 0) return false;
        ClearDock(ship);
        return true;
    }

    private void ClearDock(Structure ship)
    {
        ship.DockedTo = -1;
        ship.DockShipModule = default; ship.DockStationModule = default;
        ship.DockShipFace = default; ship.DockStationFace = default;
        StructureVersion++;
    }

    /// <summary>Undocks every ship docked at a station (the station is gone).</summary>
    private void ReleaseDocksOf(int station)
    {
        foreach (var s in _structures.Values)
            if (s.DockedTo == station) ClearDock(s);
    }

    /// <summary>True when a module added to a structure at these module coordinates would intersect a structure docked to it (or the station it is docked at).</summary>
    public bool ModuleOverlapsDocked(int structure, int mx, int my, int mz)
    {
        if (!_structures.TryGetValue(structure, out var s)) return false;
        long cu = Units.CellUnits;
        long sx = StationGrid.ModuleX * cu, sy = StationGrid.ModuleY * cu, sz = StationGrid.ModuleZ * cu;
        long ax = s.OriginX + mx * sx, ay = s.OriginY + my * sy, az = s.OriginZ + mz * sz;
        foreach (var other in _structures.Values)
        {
            if (other.Id == structure || !(other.DockedTo == structure || s.DockedTo == other.Id)) continue;
            foreach (var om in other.ModuleMap.Keys)
            {
                long bx = other.OriginX + om.Mx * sx, by = other.OriginY + om.My * sy, bz = other.OriginZ + om.Mz * sz;
                if (ax < bx + sx && bx < ax + sx && ay < by + sy && by < ay + sy && az < bz + sz && bz < az + sz) return true;
            }
        }
        return false;
    }

    /// <summary>True if the module is the port a ship is docked with, on either side.</summary>
    internal bool IsDockPortInUse(int structure, int mx, int my, int mz)
    {
        foreach (var s in _structures.Values)
        {
            if (s.DockedTo < 0) continue;
            if ((s.Id == structure && s.DockShipModule == (mx, my, mz)) || (s.DockedTo == structure && s.DockStationModule == (mx, my, mz))) return true;
        }
        return false;
    }

    /// <summary>Undocks the ship (or ships) whose port module of a structure was removed or destroyed.</summary>
    private void ReleaseDocksUsing(int structure, (int Mx, int My, int Mz) module)
    {
        foreach (var s in _structures.Values)
        {
            if (s.DockedTo < 0) continue;
            if ((s.Id == structure && s.DockShipModule == module) || (s.DockedTo == structure && s.DockStationModule == module)) ClearDock(s);
        }
    }

    // ---- the station stock -----------------------------------------------------------------------------

    /// <summary>Items a station can keep: the core, every dock module and the hold capacity of its cargo modules.</summary>
    public int StockCapacity(int station)
    {
        if (!_structures.TryGetValue(station, out var s) || s.Kind != StructureKind.Station) return 0;
        int total = 0;
        foreach (var kind in s.ModuleMap.Values)
        {
            if (kind == ModuleKind.Core) total += DockRules.CoreStockCapacity;
            else if (kind == ModuleKind.Dock) total += DockRules.DockStockCapacity;
            else if (kind == ModuleKind.Cargo) total += Content?.Structures?.HoldCapacity(kind) ?? 0;
        }
        return total;
    }

    /// <summary>Items of a kind in the stock of a station.</summary>
    public int StockCount(int station, int item) =>
        _structures.TryGetValue(station, out var s) && s.Stock is { } stock && (uint)item < (uint)stock.Length ? stock[item] : 0;

    /// <summary>All the items in the stock of a station.</summary>
    public int StockTotal(int station) =>
        _structures.TryGetValue(station, out var s) && s.Stock is { } stock ? SumOf(stock) : 0;

    private static int SumOf(int[] a)
    {
        int n = 0;
        foreach (int v in a) n += v;
        return n;
    }

    /// <summary>Adds items to a station's stock without any check (setup and tests); the capacity is not enforced.</summary>
    public void GiveStock(int station, int item, int count)
    {
        var s = GetStructure(station);
        if (s.Kind != StructureKind.Station) throw new ArgumentException("Only stations have a stock.", nameof(station));
        if (Content is null || (uint)item >= (uint)Content.Items.Count) throw new ArgumentOutOfRangeException(nameof(item));
        if (count < 1) throw new ArgumentOutOfRangeException(nameof(count));
        s.Stock ??= new int[Content.Items.Count];
        s.Stock[item] += count;
    }

    /// <summary>
    /// Moves items between the player's hold and the stock of the station the ship is docked at. Returns how many moved (0 when nothing could).
    /// A <paramref name="count"/> of zero or less asks for as many as possible.
    /// </summary>
    internal int TransferItems(int player, int item, int count, bool toStation)
    {
        var p = _players[player];
        var ship = _structures[p.ShipStructure];
        var station = _structures[ship.DockedTo];
        int want = count <= 0 ? int.MaxValue : count;
        if (toStation)
        {
            int room = StockCapacity(station.Id) - StockTotal(station.Id);
            int n = Math.Min(Math.Min(want, p.Hold[item]), Math.Max(0, room));
            if (n <= 0) return 0;
            station.Stock ??= new int[Content!.Items.Count];
            station.Stock[item] += n;
            p.Hold[item] -= n; p.HoldTotal -= n; HoldOut += n;
            return n;
        }
        else
        {
            int have = StockCount(station.Id, item);
            int room = HoldCapacityOf(p) - p.HoldTotal - p.ShipReserved;
            int n = Math.Min(Math.Min(want, have), Math.Max(0, room));
            if (n <= 0) return 0;
            station.Stock![item] -= n;
            AddToHold(p, item, n);
            return n;
        }
    }

    /// <summary>The room the transfer in a direction would have: the hold's free slots, or the stock's.</summary>
    internal bool TransferHasRoom(int player, bool toStation)
    {
        var p = _players[player];
        var ship = _structures[p.ShipStructure];
        return toStation ? StockCapacity(ship.DockedTo) > StockTotal(ship.DockedTo) : HoldCapacityOf(p) - p.HoldTotal - p.ShipReserved > 0;
    }

    // ---- power and repairs -----------------------------------------------------------------------------

    /// <summary>Takes one fuel item from the stock of the structure's station (a fuel cell first, else hydrogen). Returns the burn intervals it gives, or 0.</summary>
    private int TakeFuelFromStock(Structure s)
    {
        if (Content is null || StockHolder(s) is not { Stock: { } stock }) return 0;
        if (Content.FuelCellItem >= 0 && stock[Content.FuelCellItem] > 0) { stock[Content.FuelCellItem]--; return PowerRules.FuelCellBurnIntervals; }
        if (Content.FuelItem >= 0 && stock[Content.FuelItem] > 0) { stock[Content.FuelItem]--; return 1; }
        return 0;
    }

    // ---- state of the hash and clone -------------------------------------------------------------------

    private void MixDocking(Structure s, Action<long> mix)
    {
        if (s.DockedTo >= 0)
        {
            mix(s.DockedTo);
            mix(s.DockShipModule.Mx); mix(s.DockShipModule.My); mix(s.DockShipModule.Mz); mix((int)s.DockShipFace);
            mix(s.DockStationModule.Mx); mix(s.DockStationModule.My); mix(s.DockStationModule.Mz); mix((int)s.DockStationFace);
        }
        if (s.Stock is { } stock && SumOf(stock) > 0)
        {
            mix(-7);
            for (int i = 0; i < stock.Length; i++)
                if (stock[i] > 0) { mix(i); mix(stock[i]); }
        }
    }

    private void ValidateDocking(Structure s)
    {
        static void Fail(string m) => throw new InvalidOperationException(m);
        if (s.Stock is { } stock)
        {
            if (s.Kind != StructureKind.Station) Fail($"structure {s.Id} is no station but has a stock");
            foreach (int v in stock) if (v < 0) Fail($"station {s.Id} has a negative stock");
        }
        if (s.DockedTo < 0) return;
        if (s.Kind != StructureKind.Ship) Fail($"structure {s.Id} is docked but is no ship");
        if (!_structures.TryGetValue(s.DockedTo, out var st) || st.Kind != StructureKind.Station) { Fail($"ship {s.Id} is docked at a missing station {s.DockedTo}"); return; }
        if (!s.ModuleMap.TryGetValue(s.DockShipModule, out var sk) || !HasPorts(s.Kind, sk)) Fail($"ship {s.Id} is docked with a module that has no port");
        if (!st.ModuleMap.TryGetValue(s.DockStationModule, out var tk) || !HasPorts(st.Kind, tk)) Fail($"ship {s.Id} is docked at a module that has no port");
        if (s.DockShipFace != DockRules.Opposite(s.DockStationFace)) Fail($"ship {s.Id} docks with ports that do not face each other");
        var pose = DockPose(st, s.DockStationModule, s.DockStationFace, s.DockShipModule);
        if (s.OriginX != pose.X || s.OriginY != pose.Y || s.OriginZ != pose.Z) Fail($"ship {s.Id} is docked but not at the port pose");
    }
}
