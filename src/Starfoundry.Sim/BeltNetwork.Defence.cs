namespace Starfoundry.Sim;

/// <summary>Fixed rules of structure defence. Part of the simulation, so changing them changes states.</summary>
public static class DefenceRules
{
    /// <summary>The summed turret damage bonus of a structure's defence modules never exceeds this many percent.</summary>
    public const int MaxBonusPercent = 50;

    /// <summary>An enemy attacks the player's ship first when its nearest module centre is within this many cells.</summary>
    public const int ShipPriorityCells = 15;
}

/// <summary>A hit on a structure module this tick, for effects and toasts. Not part of the simulation state.</summary>
/// <param name="Hp">Hit points left after the hit (0 when destroyed).</param>
/// <param name="Destroyed">The module is gone.</param>
/// <param name="StationLost">The module was a station core: the whole station is gone.</param>
/// <param name="ShipLost">The module was the ship's last cockpit: the ship is lost.</param>
public readonly record struct ModuleHitEvent(
    int Structure, int Mx, int My, int Mz, ModuleKind Kind, int Damage, int Hp, bool Destroyed, bool StationLost, bool ShipLost);

/// <summary>A belt segment that was destroyed together with its module. <see cref="SimWorld"/> uses it to free the segment's cells.</summary>
public readonly record struct DestroyedSegment(int Id, int Structure, int OriginX, int OriginY, int OriginZ, int DirX, int DirY, int DirZ, int LengthCells);

public sealed partial class BeltNetwork
{
    private readonly List<ModuleHitEvent> _moduleHits = new();
    private readonly List<DestroyedSegment> _destroyedSegments = new();

    /// <summary>Modules destroyed so far (including those that fell with their station).</summary>
    public long ModulesLost { get; private set; }

    /// <summary>Stations destroyed so far because their core fell.</summary>
    public long StationsLost { get; private set; }

    /// <summary>Hits on modules during the last tick (effects and toasts only).</summary>
    public IReadOnlyList<ModuleHitEvent> ModuleHits => _moduleHits;

    /// <summary>Belt segments destroyed with their module since the last call. The command layer calls this after every tick.</summary>
    public IReadOnlyList<DestroyedSegment> TakeDestroyedSegments()
    {
        if (_destroyedSegments.Count == 0) return Array.Empty<DestroyedSegment>();
        var copy = _destroyedSegments.ToArray();
        _destroyedSegments.Clear();
        return copy;
    }

    /// <summary>True once the player's ship structure lost its last cockpit (structure worlds only). There is no respawn.</summary>
    public bool ShipLost(int player) => _players.TryGetValue(player, out var p) && p.ShipLost;

    /// <summary>Maximum hit points of a module of this kind (0 without structure content).</summary>
    public int ModuleMaxHp(ModuleKind kind) => Content?.Structures?.ModuleHp(kind) ?? 0;

    /// <summary>Current hit points of a module: its maximum unless it is damaged. Throws if the module does not exist.</summary>
    public int ModuleHp(int structure, int mx, int my, int mz)
    {
        var s = GetStructure(structure);
        if (!s.ModuleMap.TryGetValue((mx, my, mz), out var kind)) throw new ArgumentOutOfRangeException(nameof(mx), "No such module.");
        return s.ModuleDamage.TryGetValue((mx, my, mz), out int hp) ? hp : ModuleMaxHp(kind);
    }

    /// <summary>Summed hit points of a structure's modules in per mille of their summed maximum (1000 undamaged; 0 without modules).</summary>
    public int StructureHpFraction(int structure)
    {
        var s = GetStructure(structure);
        long cur = 0, max = 0;
        foreach (var (key, kind) in s.ModuleMap)
        {
            int m = ModuleMaxHp(kind);
            max += m;
            cur += s.ModuleDamage.TryGetValue(key, out int hp) ? hp : m;
        }
        return max == 0 ? 0 : (int)(cur * 1000 / max);
    }

    /// <summary>Percent added to the turret damage of a structure by its defence modules, capped at <see cref="DefenceRules.MaxBonusPercent"/>.
    /// Always 0 in worlds without the structure rules.</summary>
    public int DefenceBonusPercent(int structure)
    {
        if (!StructureRules || structure == 0 || Content?.Structures is not { } kinds || !_structures.TryGetValue(structure, out var s)) return 0;
        long total = (long)ModuleCount(s, ModuleKind.Defence) * kinds.DefenceBonusPercent(ModuleKind.Defence);
        return (int)Math.Min(total, DefenceRules.MaxBonusPercent);
    }

    /// <summary>Turret damage per shot of a turret machine: the research-boosted damage plus the defence bonus of its structure.</summary>
    private int TurretDamageOf(Machine m, TurretDef turret)
    {
        int bonus = DefenceBonusPercent(m.Structure);
        return bonus == 0 ? TurretDamage(turret) : Math.Max(1, TurretDamage(turret) * (100 + bonus) / 100);
    }

    /// <summary>World position (units) of the exact centre of a module.</summary>
    public (long X, long Y, long Z) ModuleWorldCentre(int structure, int mx, int my, int mz)
    {
        var s = GetStructure(structure);
        long cu = Units.CellUnits;
        return (s.OriginX + ((long)mx * StationGrid.ModuleX + StationGrid.ModuleX / 2) * cu,
                s.OriginY + ((long)my * StationGrid.ModuleY + StationGrid.ModuleY / 2) * cu,
                s.OriginZ + ((long)mz * StationGrid.ModuleZ + StationGrid.ModuleZ / 2) * cu);
    }

    // ---- targeting -----------------------------------------------------------------------------

    /// <summary>Structures that hold at least one machine.</summary>
    private HashSet<int> StructuresWithMachines()
    {
        var set = new HashSet<int>();
        foreach (var m in _machines) if (m is not null && m.Structure != 0) set.Add(m.Structure);
        return set;
    }

    /// <summary>The nearest module (by module centre) of the ship structure of any live player when it lies within
    /// <see cref="DefenceRules.ShipPriorityCells"/> of the enemy.</summary>
    private bool TryPickShipModule(Enemy e, out int structure, out (int, int, int) key, out (long X, long Y, long Z) centre)
    {
        structure = -1; key = default; centre = default;
        long best = (long)DefenceRules.ShipPriorityCells * Units.CellUnits * DefenceRules.ShipPriorityCells * Units.CellUnits;
        bool found = false;
        foreach (var p in _players.Values)
        {
            if (p.ShipStructure < 0 || !_structures.TryGetValue(p.ShipStructure, out var s)) continue;
            foreach (var k in s.ModuleMap.Keys)
            {
                var c = ModuleWorldCentre(s.Id, k.Mx, k.My, k.Mz);
                long dx = c.X - e.X, dy = c.Y - e.Y, dz = c.Z - e.Z;
                long d = dx * dx + dy * dy + dz * dz;
                if (d <= best && (d < best || !found)) { best = d; structure = s.Id; key = k; centre = c; found = true; }
            }
        }
        return found;
    }

    /// <summary>The module nearest to the enemy among the structures in its system that hold a machine or are a ship.</summary>
    private bool TryPickNearestModule(Enemy e, HashSet<int> withMachines, out int structure, out (int, int, int) key, out (long X, long Y, long Z) centre)
    {
        structure = -1; key = default; centre = default;
        long best = long.MaxValue;
        bool found = false;
        foreach (var s in _structures.Values)
        {
            if (s.Kind != StructureKind.Ship && !withMachines.Contains(s.Id)) continue;
            foreach (var k in s.ModuleMap.Keys)
            {
                var c = ModuleWorldCentre(s.Id, k.Mx, k.My, k.Mz);
                if (Galaxy!.SystemAt(c.X, c.Y, c.Z) != e.System) continue;
                long dx = c.X - e.X, dy = c.Y - e.Y, dz = c.Z - e.Z;
                long d = dx * dx + dy * dy + dz * dz;
                if (d < best) { best = d; structure = s.Id; key = k; centre = c; found = true; }
            }
        }
        return found;
    }

    // ---- damage and destruction ------------------------------------------------------------------

    /// <summary>Setup for tests and demos: hits a module exactly as an enemy attack would (structure worlds only).</summary>
    public void HitModule(int structure, int mx, int my, int mz, int damage)
    {
        if (!StructureRules) throw new InvalidOperationException("Module hit points need the structure rules.");
        if (!GetStructure(structure).ModuleMap.ContainsKey((mx, my, mz))) throw new ArgumentOutOfRangeException(nameof(mx), "No such module.");
        DamageModule(structure, (mx, my, mz), damage);
    }

    private void DamageModule(int structure, (int Mx, int My, int Mz) key, int damage)
    {
        if (!_structures.TryGetValue(structure, out var s) || !s.ModuleMap.TryGetValue(key, out var kind) || damage <= 0) return;
        int hp = (s.ModuleDamage.TryGetValue(key, out int cur) ? cur : ModuleMaxHp(kind)) - damage;
        if (hp > 0)
        {
            s.ModuleDamage[key] = hp;
            _moduleHits.Add(new ModuleHitEvent(structure, key.Mx, key.My, key.Mz, kind, damage, hp, false, false, false));
            return;
        }
        DestroyModule(s, key, damage);
    }

    private void DestroyModule(Structure s, (int Mx, int My, int Mz) key, int damage)
    {
        var kind = s.ModuleMap[key];
        if (kind == ModuleKind.Core && s.Kind == StructureKind.Station)
        {
            ModulesLost += s.ModuleMap.Count;
            StationsLost++;
            _moduleHits.Add(new ModuleHitEvent(s.Id, key.Mx, key.My, key.Mz, kind, damage, 0, true, true, false));
            DestroyStation(s);
            return;
        }

        ModulesLost++;
        DestroyContents(s.Id, key);
        RemoveStructureModuleUnchecked(s.Id, key.Mx, key.My, key.Mz);

        bool shipLost = false;
        if (kind == ModuleKind.Cockpit && s.Kind == StructureKind.Ship && ModuleCount(s, ModuleKind.Cockpit) == 0
            && _players.TryGetValue(s.Owner, out var p) && p.ShipStructure == s.Id && !p.ShipLost)
        {
            LoseShip(p);
            shipLost = true;
        }
        _moduleHits.Add(new ModuleHitEvent(s.Id, key.Mx, key.My, key.Mz, kind, damage, 0, true, false, shipLost));
    }

    /// <summary>Removes a whole station: every machine and belt segment in it, then the structure itself.</summary>
    private void DestroyStation(Structure s)
    {
        DestroyContents(s.Id, null);
        _structures.Remove(s.Id);
        StructureVersion++;
    }

    /// <summary>Destroys the machines of a structure (all of them when the module is null) and every belt segment that has a cell
    /// in the module. Machines first: removing a machine detaches the belts that touch it, so no belt keeps a dangling link.</summary>
    private void DestroyContents(int structure, (int Mx, int My, int Mz)? module)
    {
        bool Inside(int x, int y, int z) => module is null || StationGrid.ModuleOfCell(x, y, z) == module.Value;

        foreach (var m in _machines.ToArray())
        {
            if (m is null || m.Structure != structure || !Inside(m.X, m.Y, m.Z)) continue;
            _destroyedMachines.Add(new DestroyedMachine(m.Id, m.X, m.Y, m.Z, m.Structure));
            if (Content!.Machines[m.Type].Role == MachineRole.Wall) WallsLost++;
            else MachinesLost++;
            RemoveMachine(m.Id);
        }

        foreach (var seg in _segs.ToArray())
        {
            if (seg is null || seg.Structure != structure) continue;
            bool hit = false;
            int cells = seg.Length / Units.CellUnits;
            for (int k = 0; k < cells && !hit; k++) hit = Inside(seg.OriginX + seg.DirX * k, seg.OriginY + seg.DirY * k, seg.OriginZ + seg.DirZ * k);
            if (!hit) continue;
            _destroyedSegments.Add(new DestroyedSegment(seg.Id, structure, seg.OriginX, seg.OriginY, seg.OriginZ, seg.DirX, seg.DirY, seg.DirZ, cells));
            RemoveSegment(seg.Id);
        }
    }

    private void LoseShip(Player p)
    {
        p.ShipLost = true;
        p.MoveX = p.MoveY = p.MoveZ = 0;
        p.ClearThrust();
        p.MiningDeposit = -1;
        p.MineProgress = 0;
        p.JumpTarget = -1;
        p.JumpRemaining = 0;
        ShipsLost++;
        StructureVersion++;
    }

    // ---- repair and regeneration -----------------------------------------------------------------------

    /// <summary>A powered repairer heals the first damaged module (in module order) of its own structure within range by its heal amount.</summary>
    private bool HealFirstDamagedModule(Machine bay, RepairerDef def)
    {
        if (!StructureRules || bay.Structure == 0 || !_structures.TryGetValue(bay.Structure, out var s) || s.ModuleDamage.Count == 0) return false;
        long range = (long)def.RangeCells * Units.CellUnits;
        var (bx, by, bz) = MachineCentre(bay);
        foreach (var (key, hp) in s.ModuleDamage)
        {
            var c = ModuleWorldCentre(s.Id, key.Mx, key.My, key.Mz);
            long dx = c.X - bx, dy = c.Y - by, dz = c.Z - bz;
            if (dx * dx + dy * dy + dz * dz > range * range) continue;
            int max = ModuleMaxHp(s.ModuleMap[key]);
            int now = Math.Min(max, hp + RepairHeal(def));
            AutoRepaired += now - hp;
            if (now >= max) s.ModuleDamage.Remove(key); else s.ModuleDamage[key] = now;
            return true;
        }
        return false;
    }

    /// <summary>Slow passive repair: every <see cref="ThreatRules.ModuleRegenTicks"/> each damaged module of a fully powered structure gains one hit point.</summary>
    private void StepModuleRegen()
    {
        if (!StructureRules || _structures.Count == 0) return;
        int every = Content?.Threat?.ModuleRegenTicks ?? ThreatRules.DefaultModuleRegenTicks;
        if (Tick % every != 0) return;
        foreach (var s in _structures.Values)
        {
            if (s.ModuleDamage.Count == 0 || s.Satisfaction != PowerRules.FullSatisfaction) continue;
            foreach (var key in s.ModuleDamage.Keys.ToArray())
            {
                int hp = s.ModuleDamage[key] + (Content?.Structures?.RegenHp(s.ModuleMap[key]) ?? 1);
                if (hp >= ModuleMaxHp(s.ModuleMap[key])) s.ModuleDamage.Remove(key); else s.ModuleDamage[key] = hp;
            }
        }
    }
}
