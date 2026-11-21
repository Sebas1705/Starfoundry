namespace Starfoundry.Sim;

public sealed partial class BeltNetwork
{
    /// <summary>Structures other than the implicit world (structure 0), sorted by id so iteration, checksum and clones are deterministic.</summary>
    private SortedDictionary<int, Structure> _structures = new();

    private int _nextStructureId = 1;

    /// <summary>Creates a ship or station at a world origin (units) and returns its id (1, 2, ...; ids are never reused).</summary>
    public int CreateStructure(int owner, StructureKind kind, long ox, long oy, long oz)
    {
        if (!Enum.IsDefined(kind)) throw new ArgumentOutOfRangeException(nameof(kind));
        int id = _nextStructureId++;
        _structures.Add(id, new Structure(id, kind, owner, ox, oy, oz));
        if (kind == StructureKind.Ship && owner >= 0)
        {
            var p = GetOrCreatePlayer(owner);
            if (p.ShipStructure < 0)
            {
                p.ShipStructure = id;
                if (StructureRules) SyncPlayerToStructure(p, _structures[id]);
            }
        }
        StructureVersion++;
        return id;
    }

    /// <summary>The ship structure a player flies, or -1 when there is none.</summary>
    public int ShipStructure(int player) => _players.TryGetValue(player, out var p) ? p.ShipStructure : -1;

    /// <summary>The structure that carries the player's ship while structure rules are on, or -1 (legacy flight).</summary>
    private int FlightStructure(Player p) => StructureRules && p.ShipStructure >= 0 ? p.ShipStructure : -1;

    private static long ClampToWorld(long v) => Math.Clamp(v, -PlayerRules.WorldLimitUnits, PlayerRules.WorldLimitUnits);

    /// <summary>Makes the player position mirror the structure origin (pilot position = origin + the cockpit offset).</summary>
    private static void SyncPlayerToStructure(Player p, Structure s)
    {
        p.X = (int)ClampToWorld(s.OriginX + StructureFlight.CockpitOffsetX);
        p.Y = (int)ClampToWorld(s.OriginY + StructureFlight.CockpitOffsetY);
        p.Z = (int)ClampToWorld(s.OriginZ + StructureFlight.CockpitOffsetZ);
        s.OriginX = p.X - StructureFlight.CockpitOffsetX;
        s.OriginY = p.Y - StructureFlight.CockpitOffsetY;
        s.OriginZ = p.Z - StructureFlight.CockpitOffsetZ;
    }

    /// <summary>Teleports the ship (jump, rebuild): the ship structure goes with it so both stay in sync.</summary>
    private void SetShipPosition(Player p, int x, int y, int z)
    {
        p.X = x; p.Y = y; p.Z = z;
        if (FlightStructure(p) >= 0)
        {
            var s = _structures[p.ShipStructure];
            s.OriginX = x - StructureFlight.CockpitOffsetX;
            s.OriginY = y - StructureFlight.CockpitOffsetY;
            s.OriginZ = z - StructureFlight.CockpitOffsetZ;
        }
    }

    private int ModuleCount(Structure s, ModuleKind kind)
    {
        int n = 0;
        foreach (var k in s.ModuleMap.Values) if (k == kind) n++;
        return n;
    }

    /// <summary>Summed engine speed of a structure in percent of the base speed, clamped to <see cref="StructureFlight.MaxSpeedPercent"/>.</summary>
    private int EnginePercent(Structure s)
    {
        var kinds = Content?.Structures;
        if (kinds is null) return 0;
        long total = 0;
        foreach (var k in s.ModuleMap.Values) if (k == ModuleKind.Engine) total += kinds.EngineSpeedPercent(k);
        return (int)Math.Min(total, StructureFlight.MaxSpeedPercent);
    }

    /// <summary>Ship speed along one axis in units per tick for the player's ship structure: the base speed times the engines' percent
    /// (0 without a ship structure or engines). Integer division truncates; flight keeps the remainder so nothing is lost.</summary>
    public int ShipSpeedUnitsPerTick(int player)
    {
        if (!_players.TryGetValue(player, out var p) || p.ShipStructure < 0) return 0;
        var s = _structures[p.ShipStructure];
        long satisfaction = StructureRules ? s.Satisfaction : PowerRules.FullSatisfaction;
        return (int)(PlayerRules.MoveUnitsPerTick * (long)EnginePercent(s) * satisfaction / (StructureFlight.PercentScale * PowerRules.FullSatisfaction));
    }

    /// <summary>True if the ship may move: always in legacy worlds and for players without a ship structure; otherwise the ship
    /// structure needs at least one cockpit and one engine module.</summary>
    public bool CanFly(int player)
    {
        if (!_players.TryGetValue(player, out var p)) return true;
        int fs = FlightStructure(p);
        return fs < 0 || CanFlyStructure(_structures[fs]);
    }

    private bool CanFlyStructure(Structure s) => ModuleCount(s, ModuleKind.Cockpit) > 0 && ModuleCount(s, ModuleKind.Engine) > 0;

    /// <summary>Items the player's hold can carry: the sum of the cargo modules of the ship structure when structure rules are on
    /// and the player has one, otherwise <see cref="PlayerRules.HoldCapacity"/>.</summary>
    public int HoldCapacity(int player) =>
        _players.TryGetValue(player, out var p) ? HoldCapacityOf(p) : PlayerRules.HoldCapacity;

    /// <summary>True when structure rules are on and the hold is at or above <see cref="PlayerRules.MiningHoldLimitPercent"/>.</summary>
    private bool MiningHoldLimitReached(Player p) =>
        StructureRules && (long)p.HoldTotal * 100 >= (long)HoldCapacityOf(p) * PlayerRules.MiningHoldLimitPercent;

    private int HoldCapacityOf(Player p)
    {
        int fs = FlightStructure(p);
        var kinds = Content?.Structures;
        if (fs < 0 || kinds is null) return PlayerRules.HoldCapacity;
        int total = 0;
        foreach (var k in _structures[fs].ModuleMap.Values) total += kinds.HoldCapacity(k);
        return total;
    }

    private void MoveShipStructure(Player p, int structure)
    {
        var s = _structures[structure];
        if (!CanFlyStructure(s))
        {
            // The last engine or cockpit is gone: the ship stops where it is.
            p.MoveX = p.MoveY = p.MoveZ = 0;
            p.ClearThrust();
            return;
        }

        int percent = EnginePercent(s);
        static int Advance(long numerator, ref int rem, int denominator)
        {
            long acc = numerator + rem;
            rem = (int)(acc % denominator);
            return (int)(acc / denominator);
        }

        int nx, ny, nz;
        if (p.HasThrust)
        {
            int den = PlayerRules.ThrustScale * StructureFlight.RemainderScale;
            long k = (long)PlayerRules.MoveUnitsPerTick * percent * s.Satisfaction;
            nx = p.X + Advance(p.ThrustX * k, ref p.ThrustRemX, den);
            ny = p.Y + Advance(p.ThrustY * k, ref p.ThrustRemY, den);
            nz = p.Z + Advance(p.ThrustZ * k, ref p.ThrustRemZ, den);
        }
        else
        {
            int axes = Math.Abs(p.MoveX) + Math.Abs(p.MoveY) + Math.Abs(p.MoveZ);
            int speed = axes switch
            {
                1 => PlayerRules.MoveUnitsPerTick,
                2 => PlayerRules.MoveUnitsPerTickDiagonal2,
                _ => PlayerRules.MoveUnitsPerTickDiagonal3,
            };
            long k = (long)speed * percent * s.Satisfaction;
            nx = p.X + Advance(p.MoveX * k, ref p.ThrustRemX, StructureFlight.RemainderScale);
            ny = p.Y + Advance(p.MoveY * k, ref p.ThrustRemY, StructureFlight.RemainderScale);
            nz = p.Z + Advance(p.MoveZ * k, ref p.ThrustRemZ, StructureFlight.RemainderScale);
        }

        nx = (int)ClampToWorld(nx); ny = (int)ClampToWorld(ny); nz = (int)ClampToWorld(nz);
        s.OriginX += nx - p.X; s.OriginY += ny - p.Y; s.OriginZ += nz - p.Z;
        p.X = nx; p.Y = ny; p.Z = nz;
    }

    /// <summary>True if the drill's cell centre is within <see cref="ExplorationRules.DrillRangeCells"/> of the deposit centre (world positions, so
    /// a machine of a moving structure counts where it is now).</summary>
    public bool DrillInRange(int machine, int deposit)
    {
        var m = GetMachine(machine);
        var d = GetDeposit(deposit);
        var (wx, wy, wz) = WorldCentre(m.Structure, m.X, m.Y, m.Z);
        long cu = Units.CellUnits;
        long dx = wx - (d.X * cu + cu / 2), dy = wy - (d.Y * cu + cu / 2), dz = wz - (d.Z * cu + cu / 2);
        long r = (long)ExplorationRules.DrillRangeCells * cu;
        return dx * dx + dy * dy + dz * dz <= r * r;
    }

    /// <summary>Drops the deposit link of every linked drill that stands in a structure and is now beyond drill range.
    /// Machines of structure 0 never move, so they are skipped. Holds no state: running it every tick is the same as checking
    /// only the structures that moved.</summary>
    private void RecheckDrillLinks()
    {
        if (_structures.Count == 0) return;
        foreach (var m in _machines)
        {
            if (m is null || m.Structure == 0 || m.Deposit < 0) continue;
            if (!DrillInRange(m.Id, m.Deposit)) { m.Deposit = -1; StructureVersion++; }
        }
    }

    /// <summary>The structures in id order (structure 0 is not listed).</summary>
    public IEnumerable<Structure> Structures() => _structures.Values;

    /// <summary>True for 0 (the legacy world) and for every created structure.</summary>
    public bool StructureExists(int id) => id == 0 || _structures.ContainsKey(id);

    public Structure GetStructure(int id) =>
        _structures.TryGetValue(id, out var s) ? s : throw new ArgumentOutOfRangeException(nameof(id), "No such structure.");

    /// <summary>Moves a structure's world origin (units). Local cells and occupancy do not change.</summary>
    public void SetStructureOrigin(int id, long ox, long oy, long oz)
    {
        var s = GetStructure(id);
        s.OriginX = ox; s.OriginY = oy; s.OriginZ = oz;
        foreach (var p in _players.Values)
            if (p.ShipStructure == id && FlightStructure(p) >= 0) SyncPlayerToStructure(p, s); // the pilot goes with the ship
        StructureVersion++;
    }

    /// <summary>Adds a module to a structure without any rule check (commands validate before calling this).</summary>
    public void AddStructureModuleUnchecked(int id, int mx, int my, int mz, ModuleKind kind)
    {
        if (!Enum.IsDefined(kind)) throw new ArgumentOutOfRangeException(nameof(kind));
        if (!GetStructure(id).ModuleMap.TryAdd((mx, my, mz), kind)) throw new InvalidOperationException("The module already exists.");
        StructureVersion++;
    }

    /// <summary>Removes a module of a structure without any rule check (commands validate before calling this).</summary>
    public void RemoveStructureModuleUnchecked(int id, int mx, int my, int mz)
    {
        var s = GetStructure(id);
        if (!s.ModuleMap.Remove((mx, my, mz))) throw new InvalidOperationException("No such module.");
        s.BurnRemaining.Remove((mx, my, mz)); // a removed reactor loses the hydrogen it was burning
        s.DroneProgress.Remove((mx, my, mz));
        if (s.DroneDeposit >= 0 && ModuleCount(s, ModuleKind.DroneBay) == 0) s.DroneDeposit = -1; // the last bay is gone: nothing mines
        StructureVersion++;
    }

    /// <summary>The kind of the module that contains a local cell of a structure, or false if the cell lies in no module.</summary>
    public bool TryGetModuleKindAt(int structure, int x, int y, int z, out ModuleKind kind)
    {
        kind = default;
        return _structures.TryGetValue(structure, out var s) && s.ModuleMap.TryGetValue(StationGrid.ModuleOfCell(x, y, z), out kind);
    }

    /// <summary>True if the module shares a face with a module of the structure (diagonal neighbours do not count).</summary>
    public bool StructureModuleTouches(int id, int mx, int my, int mz)
    {
        var map = GetStructure(id).ModuleMap;
        foreach (var (dx, dy, dz) in FaceSteps)
            if (map.ContainsKey((mx + dx, my + dy, mz + dz))) return true;
        return false;
    }

    /// <summary>True if the structure's modules, minus the given one, are still one face-connected piece (or none are left).</summary>
    public bool StructureStaysConnectedWithout(int id, int mx, int my, int mz)
    {
        var remaining = new HashSet<(int, int, int)>(GetStructure(id).ModuleMap.Keys);
        remaining.Remove((mx, my, mz));
        return IsConnected(remaining);
    }

    /// <summary>True if a world point (units) is within the range (cells) of the player's ship.</summary>
    public bool WorldPointInRange(int player, long wx, long wy, long wz, int rangeCells)
    {
        var p = _players.TryGetValue(player, out var found) ? found : null;
        long dx = wx - (p?.X ?? 0), dy = wy - (p?.Y ?? 0), dz = wz - (p?.Z ?? 0);
        long r = (long)rangeCells * Units.CellUnits;
        return dx * dx + dy * dy + dz * dz <= r * r;
    }

    /// <summary>World position in units of the centre of a local cell: origin + local * cell size + half a cell, per axis.
    /// Structure 0 is the legacy world with its origin at 0.</summary>
    public (long X, long Y, long Z) WorldCentre(int structure, int x, int y, int z)
    {
        long ox = 0, oy = 0, oz = 0;
        if (structure != 0)
        {
            var s = GetStructure(structure);
            ox = s.OriginX; oy = s.OriginY; oz = s.OriginZ;
        }
        long cu = Units.CellUnits;
        return (ox + x * cu + cu / 2, oy + y * cu + cu / 2, oz + z * cu + cu / 2);
    }

    /// <summary>Throws if a structure other than 0 has a coordinate outside the local grid limit.</summary>
    private static void CheckLocal(int structure, long x, long y, long z)
    {
        if (structure < 0) throw new ArgumentOutOfRangeException(nameof(structure));
        if (structure != 0 && !(StructureGrid.InLocalBounds(x) && StructureGrid.InLocalBounds(y) && StructureGrid.InLocalBounds(z)))
            throw new ArgumentOutOfRangeException(nameof(x), "Local coordinates are beyond StructureGrid.LocalLimit.");
    }

    private void CopyStructuresFrom(BeltNetwork other)
    {
        _structures = new SortedDictionary<int, Structure>(other._structures.ToDictionary(kv => kv.Key, kv => kv.Value.Clone()));
        _nextStructureId = other._nextStructureId;
    }

    private void MixStructures(Action<long> mix)
    {
        if (_structures.Count == 0) return; // a world without structures hashes exactly as before they existed
        mix(_structures.Count); mix(_nextStructureId);
        foreach (var s in _structures.Values)
        {
            mix(s.Id); mix((int)s.Kind); mix(s.Owner); mix(s.OriginX); mix(s.OriginY); mix(s.OriginZ);
            mix(s.ModuleMap.Count);
            foreach (var (m, kind) in s.ModuleMap) { mix(m.Mx); mix(m.My); mix(m.Mz); mix((int)kind); }
            if (!StructureRules) continue; // power state exists only under the structure rules
            mix(s.PowerProduction); mix(s.PowerDemand); mix(s.Satisfaction); mix(s.BurnRemaining.Count);
            foreach (var (m, rem) in s.BurnRemaining) { mix(m.Mx); mix(m.My); mix(m.Mz); mix(rem); }
            if (s.DroneDeposit >= 0 || s.DroneProgress.Count > 0) // structures without drone work hash as before drones existed
            {
                mix(s.DroneDeposit); mix(s.DroneProgress.Count);
                foreach (var (m, pr) in s.DroneProgress) { mix(m.Mx); mix(m.My); mix(m.Mz); mix(pr); }
            }
        }
    }

    private void ValidateStructures()
    {
        static void Fail(string m) => throw new InvalidOperationException(m);
        foreach (var (id, s) in _structures)
        {
            if (s.Id != id) Fail($"structure {id} has id {s.Id}");
            if (id >= _nextStructureId) Fail($"structure {id} is beyond the id counter");
            if (!Enum.IsDefined(s.Kind)) Fail($"structure {id} has an unknown kind");
            foreach (var (m, kind) in s.ModuleMap)
                if (!Enum.IsDefined(kind)) Fail($"structure {id} module {m} has an unknown kind");
            foreach (var (m, rem) in s.BurnRemaining)
            {
                if (!s.ModuleMap.TryGetValue(m, out var rk) || rk != ModuleKind.Reactor) Fail($"structure {id} burns a module {m} that is no reactor");
                if (rem < 1 || rem > (Content?.Structures?.BurnIntervalTicks(ModuleKind.Reactor) ?? 0)) Fail($"structure {id} burn time {rem} out of range");
            }
            if (s.DroneDeposit != -1 && !IsDeposit(s.DroneDeposit)) Fail($"structure {id} drones mine an unknown deposit");
            if (s.DroneDeposit >= 0 && ModuleCount(s, ModuleKind.DroneBay) == 0) Fail($"structure {id} has a drone assignment but no drone bay");
            foreach (var (m, pr) in s.DroneProgress)
            {
                if (!s.ModuleMap.TryGetValue(m, out var dk) || dk != ModuleKind.DroneBay) Fail($"structure {id} has drone progress in a module {m} that is no drone bay");
                if (s.DroneDeposit < 0 && pr > 0) Fail($"structure {id} has drone progress without a deposit");
                if (pr < 1) Fail($"structure {id} drone progress {pr} out of range");
            }
            if (s.Satisfaction is < 0 or > PowerRules.FullSatisfaction) Fail($"structure {id} satisfaction {s.Satisfaction} out of range");
        }
        foreach (var p in _players.Values)
        {
            if (p.ShipStructure < 0) continue;
            if (!_structures.TryGetValue(p.ShipStructure, out var ship) || ship.Kind != StructureKind.Ship || ship.Owner != p.Id)
                Fail($"player {p.Id} has an invalid ship structure {p.ShipStructure}");
            else if (StructureRules && (p.X != ship.OriginX + StructureFlight.CockpitOffsetX || p.Y != ship.OriginY + StructureFlight.CockpitOffsetY
                || p.Z != ship.OriginZ + StructureFlight.CockpitOffsetZ))
                Fail($"player {p.Id} is not at the cockpit of ship structure {p.ShipStructure}");
        }
        foreach (var m in _machines)
        {
            if (m is null || m.Structure == 0) continue;
            if (!_structures.ContainsKey(m.Structure)) Fail($"machine {m.Id} is in missing structure {m.Structure}");
            if (!(StructureGrid.InLocalBounds(m.X) && StructureGrid.InLocalBounds(m.Y) && StructureGrid.InLocalBounds(m.Z)))
                Fail($"machine {m.Id} lies beyond the local grid limit");
        }
        foreach (var s in _segs)
        {
            if (s is null || s.Structure == 0) continue;
            if (!_structures.ContainsKey(s.Structure)) Fail($"segment {s.Id} is in missing structure {s.Structure}");
            int n = s.Length / Units.CellUnits - 1;
            if (!(StructureGrid.InLocalBounds(s.OriginX) && StructureGrid.InLocalBounds(s.OriginY) && StructureGrid.InLocalBounds(s.OriginZ)
                && StructureGrid.InLocalBounds(s.OriginX + (long)s.DirX * n) && StructureGrid.InLocalBounds(s.OriginY + (long)s.DirY * n)
                && StructureGrid.InLocalBounds(s.OriginZ + (long)s.DirZ * n)))
                Fail($"segment {s.Id} lies beyond the local grid limit");
        }
    }
}
