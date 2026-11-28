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

    private long ClampToWorld(long v) => Math.Clamp(v, -WorldLimit, WorldLimit);

    /// <summary>Makes the player position mirror the structure origin (pilot position = origin + the cockpit offset).</summary>
    private void SyncPlayerToStructure(Player p, Structure s)
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
        long percent = Math.Min(total, StructureFlight.MaxSpeedPercent);
        if (FlightInertia) percent = percent * (100 + _bonus[(int)BonusKind.EngineSpeed]) / 100; // ADR 0033: engine research raises the top speed past the engine cap
        return (int)percent;
    }

    /// <summary>True when ships of this world have inertia: structure rules, content that defines it and not <see cref="WorldRules.LegacyFlight"/> (ADR 0033).</summary>
    public bool HasFlightInertia => FlightInertia;

    /// <summary>True when ships of this world have inertia: structure rules, content that defines it and not <see cref="WorldRules.LegacyFlight"/> (ADR 0033).</summary>
    internal bool FlightInertia => StructureRules && !Rules.LegacyFlight && WorldGen is { HasInertia: true };

    /// <summary>The engine percent of a ship in flight: engines (and their research), boost and clouds. What the target velocity is made of.</summary>
    private int FlightPercent(Structure s, int structure)
    {
        int percent = EnginePercent(s);
        if (WorldGen is { } wg)
        {
            // Clouds slow the engines; boost multiplies them (ADR 0028). Both are stateless: computed from the positions of now.
            foreach (var q in _players.Values) if (q.ShipStructure == structure && q.Boost) { percent = percent * wg.BoostPercent / 100; break; }
            int slow = CloudEffectOf(structure).SlowPercent;
            if (slow > 0) percent = percent * (100 - slow) / 100;
        }
        return percent;
    }

    private const int MinVelocityRate = 4;

    /// <summary>Full engine speed in thousandths of a unit per tick for an engine percent: the length of the target velocity at full thrust.</summary>
    private long FullSpeedMilli(Structure s, int percent) =>
        (long)PlayerRules.MoveUnitsPerTick * percent * s.Satisfaction / StructureFlight.PercentScale;

    /// <summary>The acceleration limit (thousandths of a unit per tick, per tick) at an engine percent, with the engine research bonus.</summary>
    private long AccelRate(Structure s, int percent) =>
        Math.Max(MinVelocityRate, FullSpeedMilli(s, percent) / WorldGen!.AccelTicks * (100 + _bonus[(int)BonusKind.EngineAccel]) / 100);

    /// <summary>The braking limit: stronger than the acceleration limit (also with the engine research bonus).</summary>
    private long BrakeRate(Structure s, int percent) =>
        Math.Max(MinVelocityRate, FullSpeedMilli(s, percent) / WorldGen!.BrakeTicks * (100 + _bonus[(int)BonusKind.EngineAccel]) / 100);

    /// <summary>The ship's velocity in thousandths of a unit per tick (zero without inertia: there the position follows the thrust at once).</summary>
    public (int X, int Y, int Z) ShipVelocityMilli(int player) =>
        _players.TryGetValue(player, out var p) ? (p.VelX, p.VelY, p.VelZ) : (0, 0, 0);

    /// <summary>The speed the ship's engines reach at full thrust right now (boost, clouds and power included), in thousandths of a unit per tick. 0 when it cannot fly.</summary>
    public int ShipTopSpeedMilli(int player)
    {
        if (!_players.TryGetValue(player, out var p) || p.ShipStructure < 0 || !_structures.TryGetValue(p.ShipStructure, out var s) || s.DockedTo >= 0) return 0;
        return (int)FullSpeedMilli(s, FlightPercent(s, p.ShipStructure));
    }

    /// <summary>The acceleration limit of the ship now, in thousandths of a unit per tick per tick (0 without inertia or without a ship).</summary>
    public int ShipAccelMilli(int player)
    {
        if (!FlightInertia || !_players.TryGetValue(player, out var p) || p.ShipStructure < 0 || !_structures.TryGetValue(p.ShipStructure, out var s) || s.DockedTo >= 0) return 0;
        return (int)AccelRate(s, FlightPercent(s, p.ShipStructure));
    }

    /// <summary>Ship speed along one axis in units per tick for the player's ship structure: the base speed times the engines' percent
    /// (0 without a ship structure or engines). Integer division truncates; flight keeps the remainder so nothing is lost.</summary>
    public int ShipSpeedUnitsPerTick(int player)
    {
        if (!_players.TryGetValue(player, out var p) || p.ShipStructure < 0) return 0;
        var s = _structures[p.ShipStructure];
        if (s.DockedTo >= 0) return 0; // a docked ship is locked to its port
        long satisfaction = StructureRules ? s.Satisfaction : PowerRules.FullSatisfaction;
        return (int)(PlayerRules.MoveUnitsPerTick * (long)EnginePercent(s) * satisfaction / (StructureFlight.PercentScale * PowerRules.FullSatisfaction));
    }

    /// <summary>True if the ship may move: always in legacy worlds and for players without a ship structure; otherwise the ship
    /// structure needs at least one cockpit and one engine module.</summary>
    public bool CanFly(int player)
    {
        if (!_players.TryGetValue(player, out var p)) return true;
        if (p.ShipLost) return false;
        int fs = FlightStructure(p);
        return fs < 0 || CanFlyStructure(_structures[fs]);
    }

    private bool CanFlyStructure(Structure s) => s.DockedTo < 0 && ModuleCount(s, ModuleKind.Cockpit) > 0 && ModuleCount(s, ModuleKind.Engine) > 0;

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
        return total * (100 + _bonus[(int)BonusKind.HoldCapacity]) / 100;
    }

    private void MoveShipStructure(Player p, int structure)
    {
        var s = _structures[structure];
        if (s.DockedTo >= 0 || !CanFlyStructure(s))
        {
            // The last engine or cockpit is gone: the ship stops where it is.
            p.ClearMotion();
            return;
        }

        int percent = EnginePercent(s);
        if (WorldGen is { } wg)
        {
            // Clouds slow the engines; boost multiplies them (ADR 0028). Both are stateless: computed from the positions of now.
            if (p.Boost) percent = percent * wg.BoostPercent / 100;
            int slow = CloudEffectOf(structure).SlowPercent;
            if (slow > 0) percent = percent * (100 - slow) / 100;
        }
        if (FlightInertia) { MoveInertial(p, s, percent); return; }
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

    /// <summary>
    /// Flight with inertia (ADR 0033): the thrust or axis intent sets a target velocity; the velocity then approaches it by at most the acceleration
    /// limit per tick (the braking limit when it slows down or turns round), and the position moves by the velocity. All integers, the sub-unit
    /// remainder carried, so it is deterministic and loses nothing.
    /// </summary>
    private void MoveInertial(Player p, Structure s, int percent)
    {
        long tx, ty, tz;
        if (p.HasThrust)
        {
            long k = (long)PlayerRules.MoveUnitsPerTick * percent * s.Satisfaction;
            long den = (long)PlayerRules.ThrustScale * StructureFlight.PercentScale;
            tx = p.ThrustX * k / den; ty = p.ThrustY * k / den; tz = p.ThrustZ * k / den;
        }
        else
        {
            int axes = Math.Abs(p.MoveX) + Math.Abs(p.MoveY) + Math.Abs(p.MoveZ);
            int speed = axes switch { 0 => 0, 1 => PlayerRules.MoveUnitsPerTick, 2 => PlayerRules.MoveUnitsPerTickDiagonal2, _ => PlayerRules.MoveUnitsPerTickDiagonal3 };
            long k = (long)speed * percent * s.Satisfaction / StructureFlight.PercentScale;
            tx = p.MoveX * k; ty = p.MoveY * k; tz = p.MoveZ * k;
        }

        long dx = tx - p.VelX, dy = ty - p.VelY, dz = tz - p.VelZ;
        long d2 = dx * dx + dy * dy + dz * dz;
        if (d2 != 0)
        {
            long v2 = (long)p.VelX * p.VelX + (long)p.VelY * p.VelY + (long)p.VelZ * p.VelZ;
            long t2 = tx * tx + ty * ty + tz * tz, dot = tx * p.VelX + ty * p.VelY + tz * p.VelZ;
            bool braking = t2 < v2 || dot < 0;
            long rate = braking ? BrakeRate(s, percent) : AccelRate(s, percent);
            long full = FullSpeedMilli(s, percent);
            if (braking && v2 > full * full) rate = Math.Max(rate, IntMath.ISqrt(v2) / (2L * WorldGen!.BrakeTicks)); // over the engine speed (a boost that ended, a cloud): shed it faster
            if (d2 <= rate * rate) { p.VelX = (int)tx; p.VelY = (int)ty; p.VelZ = (int)tz; }
            else
            {
                long len = IntMath.ISqrt(d2);
                p.VelX += (int)(dx * rate / len); p.VelY += (int)(dy * rate / len); p.VelZ += (int)(dz * rate / len);
            }
        }

        static int Advance(int velocity, ref int rem)
        {
            int acc = velocity + rem;
            rem = acc % 1000;
            return acc / 1000;
        }
        long nx = p.X + Advance(p.VelX, ref p.VelRemX), ny = p.Y + Advance(p.VelY, ref p.VelRemY), nz = p.Z + Advance(p.VelZ, ref p.VelRemZ);
        long cx = ClampToWorld(nx), cy = ClampToWorld(ny), cz = ClampToWorld(nz);
        if (cx != nx) { p.VelX = 0; p.VelRemX = 0; }
        if (cy != ny) { p.VelY = 0; p.VelRemY = 0; }
        if (cz != nz) { p.VelZ = 0; p.VelRemZ = 0; }
        s.OriginX += cx - p.X; s.OriginY += cy - p.Y; s.OriginZ += cz - p.Z;
        p.X = (int)cx; p.Y = (int)cy; p.Z = (int)cz;
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
        s.ReactorFuel.Remove((mx, my, mz)); // and the fuel in its slot (ADR 0035)
        s.FeedSources.Remove((mx, my, mz));
        s.DroneProgress.Remove((mx, my, mz));
        s.ModuleDamage.Remove((mx, my, mz));
        s.DockPieceSet.RemoveWhere(p => p.Mx == mx && p.My == my && p.Mz == mz); // its docking collars go with it (ADR 0047)
        s.SpanSet.RemoveWhere(p => p.My == my && mx >= p.Mx && mx < p.Mx + p.Size && mz >= p.Mz && mz < p.Mz + p.Size); // a large module falls apart into ordinary modules (ADR 0051)
        ReleaseDocksUsing(id, (mx, my, mz)); // a destroyed port lets go of the ship docked there
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
        long cu = Units.CellUnits;
        if (structure == 0) return (x * cu + cu / 2, y * cu + cu / 2, z * cu + cu / 2);
        return StructureToWorld(GetStructure(structure), x * cu + cu / 2, y * cu + cu / 2, z * cu + cu / 2);
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
            if (s.SpanSet.Count > 0) // worlds without large modules hash as before (ADR 0051)
            {
                mix(-821); mix(s.SpanSet.Count);
                foreach (var (px, py, pz, ps) in s.SpanSet) { mix(px); mix(py); mix(pz); mix(ps); }
            }
            if (s.DockPieceSet.Count > 0) // worlds with dock modules hash as before docking collars existed (ADR 0047)
            {
                mix(-811); mix(s.DockPieceSet.Count);
                foreach (var (px, py, pz, pf) in s.DockPieceSet) { mix(px); mix(py); mix(pz); mix(pf); }
            }
            if (!StructureRules) continue; // power state exists only under the structure rules
            mix(s.PowerProduction); mix(s.PowerDemand); mix(s.Satisfaction); mix(s.BurnRemaining.Count);
            if (s.ImpactCooldown != 0) mix(-503 - s.ImpactCooldown);
            if (s.LaserDraw != 0) mix(s.LaserDraw); // only while a laser fires: other worlds hash as before
            foreach (var (m, rem) in s.BurnRemaining) { mix(m.Mx); mix(m.My); mix(m.Mz); mix(rem); }
            if (s.ModuleDamage.Count > 0) // undamaged structures hash as before module hit points existed
            {
                mix(s.ModuleDamage.Count);
                foreach (var (m, hp) in s.ModuleDamage) { mix(m.Mx); mix(m.My); mix(m.Mz); mix(hp); }
            }
            if (s.DroneDeposit >= 0 || s.DroneProgress.Count > 0) // structures without drone work hash as before drones existed
            {
                mix(s.DroneDeposit); mix(s.DroneProgress.Count);
                foreach (var (m, pr) in s.DroneProgress) { mix(m.Mx); mix(m.My); mix(m.Mz); mix(pr); }
            }
            MixAttitude(s, mix);
            MixDocking(s, mix); // only docked ships and stations with a stock mix anything
            MixReactorFuel(s, mix); // only reactors with fuel in their slot or a non-default feed mix anything
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
            foreach (var (px, py, pz, pf) in s.DockPieceSet)
            {
                if (!DockPieceRules) Fail($"structure {id} has a docking collar in a world with dock modules");
                else if (!s.ModuleMap.TryGetValue((px, py, pz), out var hk) || !Enum.IsDefined((Dir)pf) || !DockRules.CanHostPiece(hk, (Dir)pf)) Fail($"structure {id} has a docking collar at {(px, py, pz, pf)} on a module that cannot host it");
            }
            if (s.DockPieceSet.Count > DockRules.MaxPieces) Fail($"structure {id} has too many docking collars");
            var covered = new HashSet<(int, int, int)>();
            foreach (var (px, py, pz, ps) in s.SpanSet)
            {
                if (Rules.LegacyLargeModules) Fail($"structure {id} has a large module in a world without them");
                if (ps < 2 || ps > 3) { Fail($"structure {id} has a large module of side {ps}"); continue; }
                s.ModuleMap.TryGetValue((px, py, pz), out var sk);
                if (ModuleKindIds.MaxSpan(s.Kind, sk) < ps) Fail($"structure {id} has a large module of a kind that cannot be large");
                for (int dx = 0; dx < ps; dx++)
                    for (int dz = 0; dz < ps; dz++)
                    {
                        if (!s.ModuleMap.TryGetValue((px + dx, py, pz + dz), out var ck) || ck != sk) Fail($"structure {id} has a large module with a missing or different cell");
                        if (!covered.Add((px + dx, py, pz + dz))) Fail($"structure {id} has overlapping large modules");
                    }
            }
            foreach (var (m, rem) in s.BurnRemaining)
            {
                if (!s.ModuleMap.TryGetValue(m, out var rk) || !ModuleKindIds.IsReactor(rk)) Fail($"structure {id} burns a module {m} that is no reactor");
                else if (rem < 1 || rem > BurnInterval(rk) * PowerRules.FuelCellBurnIntervals) Fail($"structure {id} burn time {rem} out of range");
            }
            foreach (var (m, hp) in s.ModuleDamage)
            {
                if (!s.ModuleMap.TryGetValue(m, out var dk)) { Fail($"structure {id} has damage on a missing module {m}"); continue; }
                if (!StructureRules) Fail($"structure {id} has module damage without the structure rules");
                if (hp < 1 || hp >= ModuleMaxHp(dk)) Fail($"structure {id} module {m} hit points {hp} out of range");
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
            ValidateAttitude(s);
            ValidateDocking(s);
            ValidateReactorFuel(s);
        }
        foreach (var p in _players.Values)
        {
            if (p.ShipLost && !StructureRules) Fail($"player {p.Id} lost a ship without the structure rules");
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
