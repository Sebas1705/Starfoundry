namespace Starfoundry.Sim;

/// <summary>
/// The infinite rock field of a world (ADR 0040): resource asteroids materialized around the ships, sector by sector, from the world seed.
/// A rock is an ordinary deposit (so hand mining, the laser, drills and drones work on it unchanged) that lives in a slot of the deposit list. Sectors near a
/// ship are loaded; sectors far from every ship are forgotten and their slots reused, so the list stays bounded. What the player did to a forgotten rock
/// (ore taken, an emptied rock) is remembered in a small table, so loading the sector again gives the same world.
/// </summary>
public sealed partial class BeltNetwork
{
    private FieldRules? _field;
    private readonly SortedDictionary<long, List<int>> _fieldCells = new();
    private readonly SortedDictionary<(long Cell, int Index), int> _fieldMemory = new();
    private readonly SortedSet<int> _freeSlots = new();
    private int _fieldSerial;
    private int _fieldLoaded;

    /// <summary>True when this world streams the infinite rock field.</summary>
    public bool HasRockField => _field is not null;

    /// <summary>The limit of the world in units on each axis: the finite cube, or the content's wide limit for a world with a rock field.</summary>
    internal int WorldLimit => _field?.WorldLimitUnits ?? PlayerRules.WorldLimitUnits;

    /// <summary>Rocks of the field materialized right now.</summary>
    public int FieldLoadedRocks => _fieldLoaded;

    /// <summary>Sectors of the field materialized right now.</summary>
    public int FieldLoadedSectors => _fieldCells.Count;

    /// <summary>Emptied or partly mined rocks of forgotten sectors the world remembers.</summary>
    public int FieldRememberedRocks => _fieldMemory.Count;

    private ClusterRules? _clusters;
    private WorldScaleRules? _scale;

    /// <summary>The world scale of this world (ADR 0053, stage 2 slice): null for content without a scale block, a legacy world and a world that is not wide.</summary>
    public WorldScaleRules? Scale => _scale;

    /// <summary>True when the rocks of this world come from the clustered generator (ADR 0046): a rock field, content with clusters and no <see cref="WorldRules.LegacyRockClusters"/>.</summary>
    public bool HasRockClusters => _clusters is not null;

    private void InitField()
    {
        _field = Galaxy is { Wide: true } && !Rules.LegacyField ? Content?.World?.Field : null;
        _scale = _field is not null && !Rules.LegacyWorldScale ? Content?.World?.Scale : null;
        if (_scale is not null && _field is not null) _field = _scale.Apply(_field);
        _clusters = _field is not null && !Rules.LegacyRockClusters ? _field.Clusters : null;
    }

    /// <summary>True when a bounded field (ADR 0053) cannot hold a rock of this cluster: its centre is farther from every system anchor than the field radius plus the cluster's own reach.</summary>
    private bool ParentBeyondField(in ClusterParent p)
    {
        if (_field is not { AnchorRadiusCells: > 0 } f || Galaxy is null) return false;
        long r = (long)f.AnchorRadiusCells + p.Radius, r2 = r * r;
        foreach (var s in Galaxy.Systems)
        {
            long dx = p.X - s.X, dy = p.Y - s.Y, dz = p.Z - s.Z;
            if (dx * dx + dy * dy + dz * dz <= r2) return false;
        }
        return true;
    }

    /// <summary>The cluster a field rock belongs to, or null for a stray, an ordinary deposit and the uniform field (ADR 0046).</summary>
    public ClusterParent? DepositCluster(int id)
    {
        var d = GetDeposit(id);
        if (_clusters is null || _field is null || !d.Streamed || d.Cluster < 0 || Galaxy is null) return null;
        return ClusterField.ParentById(_field, _clusters, Galaxy.Seed, d.Cluster, Rules.LegacyResources ? 100 : Rules.ResourceDensityPercent);
    }

    /// <summary>The id of the cluster a field rock belongs to, -1 for none.</summary>
    public long DepositClusterId(int id) => GetDeposit(id).Cluster;

    /// <summary>The packed key of the sector a field rock was generated in (the renderer batches the rocks of a sector).</summary>
    public long DepositSectorKey(int id) => GetDeposit(id).CellKey;

    /// <summary>Side of a sector in cells (0 without a rock field).</summary>
    public int FieldSectorCells => _field?.SectorCells ?? 0;

    /// <summary>The rock radius law in percent of the reference size: 250 in the clustered field, 100 in the uniform one (the size words and the model classes divide by it).</summary>
    public int RockRadiusPercent => _clusters?.RadiusPercent ?? 100;

    /// <summary>The cluster parents whose centre lies within a box of <paramref name="radiusCells"/> around a point in units, for the far glow and the map: empty without clusters.</summary>
    public IReadOnlyList<ClusterParent> ClustersNear(long xUnits, long yUnits, long zUnits, int radiusCells)
    {
        if (_clusters is null || _field is null || Galaxy is null) return Array.Empty<ClusterParent>();
        long cx = xUnits / Units.CellUnits, cy = yUnits / Units.CellUnits, cz = zUnits / Units.CellUnits;
        var all = ClusterField.ParentsInBox(_field, _clusters, Galaxy.Seed, cx - radiusCells, cx + radiusCells, cy - radiusCells, cy + radiusCells, cz - radiusCells, cz + radiusCells,
            Rules.LegacyResources ? 100 : Rules.ResourceDensityPercent);
        if (_field.AnchorRadiusCells <= 0) return all;
        var kept = new List<ClusterParent>(all.Count); // a bounded field (ADR 0053) has no clusters in the far void
        foreach (var p in all) if (!ParentBeyondField(p)) kept.Add(p);
        return kept;
    }

    /// <summary>
    /// Extra reach in units a laser or a hand needs for the rock of a deposit: the rocks of the clustered field are big and solid, so a ship stops at their
    /// surface and the range is measured to it, not to the centre (0 for every other deposit and for the uniform field).
    /// </summary>
    private long MineReachExtraUnits(Deposit d) =>
        _clusters is not null && d.Streamed ? (long)d.RadiusUnits * (WorldGen?.DepositHitPercent ?? 100) / 100 : 0;

    private void StepField()
    {
        var f = _field;
        if (f is null || Galaxy is null || Tick % f.StepTicks != 0) return;
        long cu = Units.CellUnits, sectorUnits = (long)f.SectorCells * cu, load = f.LoadCells * cu, keep = f.KeepCells * cu;

        foreach (var p in _players.Values)
        {
            int x0 = SectorOf(p.X - load, f), x1 = SectorOf(p.X + load, f);
            int y0 = SectorOf(p.Y - load, f), y1 = SectorOf(p.Y + load, f);
            int z0 = SectorOf(p.Z - load, f), z1 = SectorOf(p.Z + load, f);
            List<(long D, int X, int Y, int Z)>? wanted = _clusters is not null ? new() : null; // clustered fields can fill the cap: the nearest sectors load first
            for (int cx = x0; cx <= x1; cx++)
                for (int cy = y0; cy <= y1; cy++)
                    for (int cz = z0; cz <= z1; cz++)
                    {
                        long dist2 = SectorDistance2(p, cx, cy, cz, sectorUnits);
                        if (dist2 > load * load) continue;
                        long key = SectorField.Key(cx, cy, cz);
                        if (_fieldCells.ContainsKey(key)) continue;
                        if (wanted is not null) wanted.Add((dist2, cx, cy, cz));
                        else LoadSector(f, key, cx, cy, cz);
                    }
            if (wanted is not null)
            {
                wanted.Sort((a, b) => a.D != b.D ? a.D.CompareTo(b.D) : a.X != b.X ? a.X.CompareTo(b.X) : a.Y != b.Y ? a.Y.CompareTo(b.Y) : a.Z.CompareTo(b.Z));
                foreach (var (_, cx, cy, cz) in wanted) LoadSector(f, SectorField.Key(cx, cy, cz), cx, cy, cz);
            }
        }

        if (_fieldCells.Count == 0) return;
        List<long>? far = null;
        foreach (var key in _fieldCells.Keys)
        {
            var (cx, cy, cz) = SectorField.Unkey(key);
            bool near = false;
            foreach (var p in _players.Values)
                if (SectorDistance2(p, cx, cy, cz, sectorUnits) <= keep * keep) { near = true; break; }
            if (!near) (far ??= new()).Add(key);
        }
        if (far is null) return;
        var pinned = PinnedDeposits();
        foreach (long key in far) UnloadSector(key, pinned);
    }

    private static int SectorOf(long units, FieldRules f) => SectorField.SectorOf(units, f.SectorCells);

    /// <summary>Squared distance in units from a ship to the box of a sector.</summary>
    private static long SectorDistance2(Player p, int cx, int cy, int cz, long sectorUnits)
    {
        long dx = Outside(p.X, cx * sectorUnits, (cx + 1) * sectorUnits);
        long dy = Outside(p.Y, cy * sectorUnits, (cy + 1) * sectorUnits);
        long dz = Outside(p.Z, cz * sectorUnits, (cz + 1) * sectorUnits);
        return dx * dx + dy * dy + dz * dz;
    }

    private static long Outside(long v, long lo, long hi) => v < lo ? lo - v : v > hi ? v - hi : 0;

    private int ClaimSlot()
    {
        if (_freeSlots.Count > 0)
        {
            int slot = _freeSlots.Min;
            _freeSlots.Remove(slot);
            return slot;
        }
        _deposits.Add(Deposit.FreeSlot(_deposits.Count));
        return _deposits.Count - 1;
    }

    private void LoadSector(FieldRules f, long key, int cx, int cy, int cz)
    {
        var rocks = new List<FieldRock>(8);
        int density = Rules.LegacyResources ? 100 : Rules.ResourceDensityPercent;
        if (_clusters is not null) ClusterField.Generate(f, _clusters, Galaxy!.Systems, Galaxy.Seed, cx, cy, cz, density, rocks);
        else SectorField.Generate(f, Galaxy!.Systems, Galaxy.Seed, cx, cy, cz, density, rocks);
        if (_fieldLoaded + rocks.Count > f.MaxLoaded) return; // full: the sector is tried again at the next pass
        var slots = new List<int>(rocks.Count);
        foreach (var r in rocks)
        {
            int slot = ClaimSlot();
            int amount = Math.Max(1, WorldRules.Scale(r.Amount, Rules.DepositRichnessPercent));
            var d = new Deposit(slot, r.Item, amount, r.X, r.Y, r.Z)
            {
                Revealed = false, Density = r.Density, Radius = r.Radius, Streamed = true, CellKey = key, CellIndex = r.Index, Serial = ++_fieldSerial, Cluster = r.Cluster,
            };
            if (_fieldMemory.Remove((key, r.Index), out int taken)) d.Remaining = Math.Max(0, amount - taken);
            _deposits[slot] = d;
            slots.Add(slot);
        }
        _fieldCells[key] = slots;
        _fieldLoaded += slots.Count;
        StructureVersion++;
    }

    /// <summary>Forgets a sector unless something still uses one of its rocks (a link, a drone assignment, mining, ore in flight).</summary>
    private void UnloadSector(long key, HashSet<int> pinned)
    {
        var slots = _fieldCells[key];
        foreach (int slot in slots) if (pinned.Contains(slot)) return;
        foreach (int slot in slots)
        {
            var d = _deposits[slot];
            int taken = d.Initial - d.Remaining;
            if (taken > 0) _fieldMemory[(key, d.CellIndex)] = taken;
            _deposits[slot] = Deposit.FreeSlot(slot);
            _freeSlots.Add(slot);
        }
        _fieldLoaded -= slots.Count;
        _fieldCells.Remove(key);
        StructureVersion++;
    }

    private HashSet<int> PinnedDeposits()
    {
        var set = new HashSet<int>();
        foreach (var p in _players.Values) if (p.MiningDeposit >= 0) set.Add(p.MiningDeposit);
        foreach (var m in _machines) if (m is not null && m.Deposit >= 0) set.Add(m.Deposit);
        foreach (var s in _structures.Values) if (s.DroneDeposit >= 0) set.Add(s.DroneDeposit);
        foreach (var k in _pickups) if (k.Source >= 0) set.Add(k.Source);
        return set;
    }

    // ---- readers for the renderer and panels ---------------------------------------------------------------

    /// <summary>True for a rock of the infinite field.</summary>
    public bool DepositIsFieldRock(int id) => GetDeposit(id).Streamed;

    /// <summary>The radius of the deposit's rock in units.</summary>
    public int DepositRadiusUnits(int id) => GetDeposit(id).RadiusUnits;

    /// <summary>The size of the deposit's rock in thousandths of the reference rock: the scale the model is drawn at (the same rock the collision uses).</summary>
    public int DepositRockScalePermille(int id)
    {
        var d = GetDeposit(id);
        return d.Radius > 0 ? (int)((long)d.Radius * 1000 / DepositRules.BaseRockRadiusUnits) : DepositRules.RockScalePermille(d.Initial);
    }

    /// <summary>Changes when the slot holds another rock than before (a field rock was forgotten and the slot reused); 0 for an ordinary deposit.</summary>
    public int DepositSerial(int id) => GetDeposit(id).Serial;

    // ---- state: copy, hash, validation ---------------------------------------------------------------------

    private void CopyFieldFrom(BeltNetwork other)
    {
        _field = other._field;
        _clusters = other._clusters;
        _scale = other._scale;
        _fieldSerial = other._fieldSerial;
        _fieldLoaded = other._fieldLoaded;
        _fieldCells.Clear();
        foreach (var (k, v) in other._fieldCells) _fieldCells[k] = new List<int>(v);
        _fieldMemory.Clear();
        foreach (var (k, v) in other._fieldMemory) _fieldMemory[k] = v;
        _freeSlots.Clear();
        foreach (int s in other._freeSlots) _freeSlots.Add(s);
    }

    private void MixField(Action<long> mix)
    {
        if (_fieldSerial == 0 && _fieldMemory.Count == 0) return; // a world that never materialized a rock hashes as before the field existed
        mix(-701); mix(_fieldSerial); mix(_fieldLoaded); mix(_fieldMemory.Count);
        foreach (var (k, v) in _fieldMemory) { mix(k.Cell); mix(k.Index); mix(v); }
        foreach (var (k, v) in _fieldCells) { mix(k); mix(v.Count); foreach (int s in v) mix(s); }
        foreach (int s in _freeSlots) { mix(-703); mix(s); }
        foreach (var d in _deposits)
            if (d.Streamed) { mix(-702); mix(d.Id); mix(d.CellKey); mix(d.CellIndex); mix(d.Radius); mix(d.Serial); }
    }

    /// <summary>Extra ore the table of forgotten rocks has taken from the world (for the deposit accounting).</summary>
    private long FieldRememberedExtracted()
    {
        long sum = 0;
        foreach (int v in _fieldMemory.Values) sum += v;
        return sum;
    }

    private void ValidateField()
    {
        static void Fail(string msg) => throw new InvalidOperationException(msg);
        if (_field is null)
        {
            if (_fieldCells.Count != 0 || _fieldMemory.Count != 0 || _freeSlots.Count != 0 || _fieldLoaded != 0) Fail("rock field state exists without a field");
            foreach (var d in _deposits) if (d.Streamed || d.Free) Fail($"deposit {d.Id} is a field rock without a field");
            return;
        }
        int loaded = 0;
        var seen = new HashSet<int>();
        foreach (var (key, slots) in _fieldCells)
        {
            foreach (int s in slots)
            {
                if ((uint)s >= (uint)_deposits.Count || !seen.Add(s)) Fail($"sector slot {s} is out of range or used twice");
                var d = _deposits[s];
                if (!d.Streamed || d.Free || d.CellKey != key) Fail($"deposit {s} does not belong to its sector");
                if (d.Remaining < 0 || d.Remaining > d.Initial) Fail($"field rock {s} remaining out of range");
                if (_fieldMemory.ContainsKey((key, d.CellIndex))) Fail($"field rock {s} is also in the table of forgotten rocks");
                loaded++;
            }
        }
        if (loaded != _fieldLoaded || loaded > _field.MaxLoaded) Fail("loaded rock count is wrong or over the cap");
        foreach (int s in _freeSlots)
            if ((uint)s >= (uint)_deposits.Count || !_deposits[s].Free || seen.Contains(s)) Fail($"free slot {s} is not free");
        foreach (var d in _deposits)
        {
            if (d.Free && !_freeSlots.Contains(d.Id)) Fail($"deposit {d.Id} is free but not in the free set");
            if (d.Streamed && !seen.Contains(d.Id)) Fail($"field rock {d.Id} belongs to no sector");
            if (d.Free && (d.Remaining != 0 || d.Revealed)) Fail($"free slot {d.Id} holds ore or is revealed");
        }
        foreach (var (k, v) in _fieldMemory) if (v < 1) Fail("a remembered rock took nothing");
    }
}
