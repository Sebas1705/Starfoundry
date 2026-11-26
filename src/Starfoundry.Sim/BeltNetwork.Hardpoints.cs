namespace Starfoundry.Sim;

/// <summary>One installed hardpoint module (ADR 0032) with the world position of its centre, where its beam or shot leaves.</summary>
/// <param name="Percent">Strength in percent of the base mining rate or gun damage (100 for the mark 1).</param>
/// <param name="RangeCells">Reach of a mining laser in cells from its module centre (0 for guns, which use the ship weapon range).</param>
public readonly record struct HardpointMount(int Mx, int My, int Mz, ModuleKind Kind, long X, long Y, long Z, int Percent, int RangeCells);

public sealed partial class BeltNetwork
{
    /// <summary>
    /// True when the player's ship needs hardpoint modules to mine and shoot (ADR 0032): structure rules, a ship structure, content that
    /// defines the hardpoint kinds and no <see cref="WorldRules.LegacyHardpoints"/>. Otherwise the ship has one implicit laser and one
    /// implicit gun, exactly as before.
    /// </summary>
    public bool HardpointsRequired(int player) => _players.TryGetValue(player, out var p) && HardpointsRequired(p);

    private bool HardpointsRequired(Player p) =>
        StructureRules && !Rules.LegacyHardpoints && p.ShipStructure >= 0 && _structures.ContainsKey(p.ShipStructure)
        && Content?.Structures is { } kinds && kinds.IsDefined(ModuleKind.MiningLaser);

    private List<HardpointMount> MountsOf(Structure s, bool lasers)
    {
        var list = new List<HardpointMount>();
        if (Content?.Structures is not { } kinds) return list;
        foreach (var (key, kind) in s.ModuleMap)
        {
            if (lasers ? !ModuleKindIds.IsMiningLaser(kind) : !ModuleKindIds.IsGun(kind)) continue;
            var c = ModuleWorldCentre(s.Id, key.Mx, key.My, key.Mz);
            list.Add(new HardpointMount(key.Mx, key.My, key.Mz, kind, c.X, c.Y, c.Z, kinds.HardpointPercent(kind), kinds.HardpointRangeCells(kind)));
        }
        return list;
    }

    /// <summary>The mining laser modules of the player's ship in module order, with the world position of each (empty without a ship structure).</summary>
    public IReadOnlyList<HardpointMount> MiningLasers(int player) =>
        _players.TryGetValue(player, out var p) && _structures.TryGetValue(p.ShipStructure, out var s) ? MountsOf(s, true) : System.Array.Empty<HardpointMount>();

    /// <summary>The gun modules of the player's ship in module order, with the world position of each.</summary>
    public IReadOnlyList<HardpointMount> Guns(int player) =>
        _players.TryGetValue(player, out var p) && _structures.TryGetValue(p.ShipStructure, out var s) ? MountsOf(s, false) : System.Array.Empty<HardpointMount>();

    /// <summary>True when the player's ship can mine at all: it has a mining laser module, or hardpoints are not required.</summary>
    public bool HasMiningLaser(int player) =>
        !_players.TryGetValue(player, out var p) || !HardpointsRequired(p) || MountsOf(_structures[p.ShipStructure], true).Count > 0;

    /// <summary>True when the player's ship can shoot at all: it has a gun module, or hardpoints are not required.</summary>
    public bool HasGun(int player) =>
        !_players.TryGetValue(player, out var p) || !HardpointsRequired(p) || MountsOf(_structures[p.ShipStructure], false).Count > 0;

    private static bool InReach(HardpointMount m, long px, long py, long pz, long rangeUnits)
    {
        long dx = px - m.X, dy = py - m.Y, dz = pz - m.Z;
        return dx * dx + dy * dy + dz * dz <= rangeUnits * rangeUnits;
    }

    /// <summary>The lasers of the player's ship that reach the deposit from where their modules are now (hardpoint worlds only).</summary>
    private List<HardpointMount> LasersInReach(Player p, int deposit)
    {
        var d = _deposits[deposit];
        long cu = Units.CellUnits, px = d.X * cu, py = d.Y * cu, pz = d.Z * cu;
        var result = new List<HardpointMount>();
        foreach (var m in MountsOf(_structures[p.ShipStructure], true))
            if (InReach(m, px, py, pz, (long)m.RangeCells * cu)) result.Add(m);
        return result;
    }

    /// <summary>The lasers that reach a deposit from where the ship is now: empty when none does or when the ship has no mining laser module.</summary>
    public IReadOnlyList<HardpointMount> LasersReaching(int player, int deposit) =>
        _players.TryGetValue(player, out var p) && HardpointsRequired(p) && IsDeposit(deposit) ? LasersInReach(p, deposit) : System.Array.Empty<HardpointMount>();

    /// <summary>Summed strength of the lasers that reach the deposit in percent of the base rate (100 for the implicit laser of a legacy world).</summary>
    public int LaserPercentFor(int player, int deposit)
    {
        if (!_players.TryGetValue(player, out var p) || !IsDeposit(deposit)) return 0;
        return LaserPercentFor(p, deposit);
    }

    /// <summary>Ticks of hand mining per item at the deposit with the lasers of the player's ship that reach it (the ticks of <see cref="MineTicksFor(int)"/> for one mark 1 laser).</summary>
    public int MineTicksFor(int player, int deposit) =>
        MineTicks(GetDeposit(deposit).Density, _players.TryGetValue(player, out var p) ? LaserPercentFor(p, deposit) : 100);

    private int LaserPercentFor(Player p, int deposit)
    {
        if (!HardpointsRequired(p)) return 100;
        int total = 0;
        foreach (var m in LasersInReach(p, deposit)) total += m.Percent;
        return total;
    }

    /// <summary>
    /// One volley of the ship's gun modules (ADR 0032): the target is the enemy nearest to any gun; every gun that reaches it adds its strength
    /// (<c>ShipWeaponDamage * percent / 100</c>, the mark 1 being 100) and the tracer leaves the nearest gun. One cooldown for the ship, as before.
    /// </summary>
    private void FireGuns(Player p, ThreatRules rules)
    {
        var guns = MountsOf(_structures[p.ShipStructure], false);
        long range = (long)rules.ShipWeaponRangeCells * Units.CellUnits;
        Enemy? target = null;
        long best = long.MaxValue;
        HardpointMount from = default;
        foreach (var g in guns)
        {
            var e = NearestEnemy(g.X, g.Y, g.Z, range);
            if (e is null) continue;
            long dx = e.X - g.X, dy = e.Y - g.Y, dz = e.Z - g.Z, d = dx * dx + dy * dy + dz * dz;
            if (d < best) { best = d; target = e; from = g; }
        }
        if (target is null) return;
        int percent = 0;
        foreach (var g in guns)
        {
            long dx = target.X - g.X, dy = target.Y - g.Y, dz = target.Z - g.Z;
            if (dx * dx + dy * dy + dz * dz <= range * range) percent += g.Percent;
        }
        target.Hp -= Math.Max(1, (int)((long)ShipWeaponDamage * percent / 100));
        p.WeaponCooldown = VolleyInterval(guns, rules.ShipWeaponIntervalTicks);
        _shots.Add(new ShotEvent(ShotKind.Ship, (int)from.X, (int)from.Y, (int)from.Z, target.X, target.Y, target.Z));
    }

    /// <summary>Power the lasers that would mine now ask for: <see cref="PlayerRules.LaserPowerUse"/> for the implicit laser, the sum of the module draws otherwise.</summary>
    private int LaserPowerFor(Player p)
    {
        if (!HardpointsRequired(p)) return PlayerRules.LaserPowerUse;
        var kinds = Content!.Structures!;
        int total = 0;
        foreach (var m in LasersInReach(p, p.MiningDeposit)) total += kinds.ModulePowerUse(m.Kind);
        return total;
    }
}
