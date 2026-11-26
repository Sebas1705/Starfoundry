namespace Starfoundry.Sim;

/// <summary>Fixed rules of free fire, recoil, hold overflow and regrowth (ADR 0036). Part of the simulation, so changing them changes states.</summary>
public static class FeelRules
{
    /// <summary>Projectiles alive at once; a gun that would exceed it waits.</summary>
    public const int MaxProjectiles = 64;

    /// <summary>Distance a projectile covers per tick, in units (18.75 cells a second).</summary>
    public const int ProjectileSpeedUnits = 150;

    /// <summary>Radius within which a projectile hits an enemy, in units (a little over a cell).</summary>
    public const int EnemyHitRadiusUnits = 300;

    /// <summary>The length an aim vector may have at most (the thrust scale); it must be at least half of it.</summary>
    public const int AimScale = PlayerRules.ThrustScale;

    /// <summary>Damage drifting debris takes before it breaks up: about five volleys of the starter gun.</summary>
    public const int DebrisDurability = 40;

    /// <summary>Hand mining past this share of the hold (<see cref="PlayerRules.MiningHoldLimitPercent"/>) takes this many times longer per item.</summary>
    public const int SoftHoldPenalty = 4;

    /// <summary>Ore pickups alive at once; the surplus of a rock merges into one pickup, so this is rarely reached.</summary>
    public const int MaxPickups = 64;

    /// <summary>A stack the player dropped lies for 30 minutes (ADR 0038); it may be taken by hand within <see cref="HandReachUnits"/> on foot or by a seated pilot within <see cref="TractorRangeUnits"/>.</summary>
    public const int DropLifeTicks = 30 * 60 * Units.TicksPerSecond, HandReachUnits = 3 * Units.CellUnits, DropMergeUnits = Units.CellUnits, DropActorReachUnits = 40 * Units.CellUnits;

    /// <summary>A pickup fades after three minutes.</summary>
    public const int PickupLifeTicks = 180 * Units.TicksPerSecond;

    /// <summary>A new pickup drifts for this many ticks, at <see cref="PickupDriftUnits"/> a tick, then rests.</summary>
    public const int PickupDriftTicks = 90, PickupDriftUnits = 5;

    /// <summary>A ship with room in its hold pulls pickups within this many units towards it (6 cells), a step at a time, and takes those within <see cref="CollectRadiusUnits"/> (2 cells).</summary>
    public const int TractorRangeUnits = 6 * Units.CellUnits, TractorStepUnits = 72, CollectRadiusUnits = 2 * Units.CellUnits;

    /// <summary>Recoil values are measured at the mass of a ship of this many modules; heavier ships kick less, lighter ones more.</summary>
    public const int RecoilReferenceMass = 8;

    /// <summary>A volley never pushes a ship past this speed along one axis, in thousandths of a unit per tick.</summary>
    public const int MaxRecoilVelocity = 4 * PlayerRules.MoveUnitsPerTick * 1000;

    /// <summary>An emptied deposit regrows after twelve minutes, with 12 percent of its first size (at least 10 items).</summary>
    public const int RegrowDelayTicks = 12 * 60 * Units.TicksPerSecond, RegrowPercent = 12, RegrowMinimum = 10;

    /// <summary>A regrowth held back by a ship or station inside the rock tries again every second; the rock needs this much room around it (8 cells).</summary>
    public const int RegrowRetryTicks = Units.TicksPerSecond, RegrowClearUnits = 8 * Units.CellUnits;
}

/// <summary>What a projectile hit, for sparks and sounds.</summary>
public enum ImpactKind : byte { Enemy, Rock, Debris, Mine, Wreck, Deposit }

/// <summary>A projectile ended in something this tick. Position in units. <paramref name="Id"/> is the enemy, obstacle or deposit id. Not part of the simulation state.</summary>
public readonly record struct ImpactEvent(ImpactKind Kind, int Id, int X, int Y, int Z, bool Destroyed = false);

/// <summary>A projectile in flight, for rendering. Position in units, velocity in units per tick.</summary>
public readonly record struct ProjectileInfo(int Player, int X, int Y, int Z, int Vx, int Vy, int Vz, int Life);

public enum PickupEventKind : byte
{
    /// <summary>The first surplus of a rock became a pickup (the game says "hold full" once per rock).</summary>
    Created,

    /// <summary>A ship took <c>Count</c> items from a pickup.</summary>
    Collected,

    /// <summary>A pickup faded: <c>Count</c> items were lost.</summary>
    Expired,

    /// <summary>The surplus found no room for another pickup and was lost.</summary>
    Lost,
}

/// <summary>Something happened to an ore pickup this tick. Not part of the simulation state.</summary>
public readonly record struct PickupEvent(PickupEventKind Kind, int Source, int Item, int Count);

/// <summary>An ore pickup for rendering and tests: items that did not fit the hold, floating near the rock they came from. Position in units.</summary>
public readonly record struct PickupInfo(int Id, int Item, int Count, int Source, int X, int Y, int Z, int Life)
{
    /// <summary>True for a stack the player dropped (ADR 0038), false for ore from a rock.</summary>
    public bool Dropped => Source < 0;
}

internal sealed class Projectile
{
    public int Player, Life, Damage;
    public int X, Y, Z, Vx, Vy, Vz;

    public Projectile Clone() => (Projectile)MemberwiseClone();
}

internal sealed class OrePickup
{
    public int Id, Item, Count, Source, Life;
    public int X, Y, Z, Vx, Vy, Vz;
    /// <summary>A stack the player dropped (ADR 0038): it has no source rock (Source is -1), does not drift and no tractor pulls it.</summary>
    public bool Dropped;

    public OrePickup Clone() => (OrePickup)MemberwiseClone();
}

public sealed partial class BeltNetwork
{
    private readonly SortedDictionary<int, (int X, int Y, int Z)> _aim = new();
    private List<Projectile> _projectiles = new();
    private List<OrePickup> _pickups = new();
    private int _nextPickupId;
    private readonly List<ImpactEvent> _impacts = new();
    private readonly List<PickupEvent> _pickupEvents = new();
    private readonly HashSet<int> _dryFire = new();

    /// <summary>Items regrown into emptied deposits so far (ADR 0036). The deposit accounting adds it back: extracted + regrown = mined.</summary>
    public long RegrownTotal { get; private set; }

    /// <summary>What projectiles hit during the last tick (effects and sounds only).</summary>
    public IReadOnlyList<ImpactEvent> Impacts => _impacts;

    /// <summary>What happened to ore pickups during the last tick (toasts and sounds only).</summary>
    public IReadOnlyList<PickupEvent> PickupEvents => _pickupEvents;

    /// <summary>True when the player tried to fire during the last tick but the hold had no ammo (the game clicks and says so).</summary>
    public bool DryFired(int player) => _dryFire.Contains(player);

    public int PickupCount => _pickups.Count;

    public int ProjectileCount => _projectiles.Count;

    private void ClearFeelEvents()
    {
        _impacts.Clear();
        _pickupEvents.Clear();
        _dryFire.Clear();
    }

    // ---- aim ---------------------------------------------------------------------------------------------

    /// <summary>True for (0, 0, 0) (no aim) and for a vector whose components are within the scale and whose length is between half the scale and the scale.</summary>
    public bool AimInRange(int dx, int dy, int dz)
    {
        if ((dx | dy | dz) == 0) return true;
        if (!PlayerRules.ThrustInRange(dx, dy, dz)) return false;
        long sq = (long)dx * dx + (long)dy * dy + (long)dz * dz;
        return sq >= (long)(FeelRules.AimScale / 2) * (FeelRules.AimScale / 2);
    }

    /// <summary>True when the player's guns fire along an aim: the world has the new feel and an aim is set.</summary>
    private bool FreeFireAims(Player p) => !Rules.LegacyFeel && _aim.ContainsKey(p.Id);

    /// <summary>Sets (or with a zero vector clears) the player's aim, in the ship's frame. Commands validate with <see cref="AimInRange"/>.</summary>
    public void SetAim(int player, int dx, int dy, int dz)
    {
        GetOrCreatePlayer(player);
        if ((dx | dy | dz) == 0) _aim.Remove(player);
        else _aim[player] = (dx, dy, dz);
    }

    /// <summary>The player's aim in the ship's frame, or null when none is set (the guns then fire at an enemy the sim picks).</summary>
    public (int X, int Y, int Z)? AimOf(int player) => _aim.TryGetValue(player, out var a) ? a : null;

    /// <summary>The player's aim turned into the world by the attitude of the ship: a vector of length about <see cref="FeelRules.AimScale"/>, or null.</summary>
    public (long X, long Y, long Z)? WorldAimOf(int player)
    {
        if (!_aim.TryGetValue(player, out var a)) return null;
        return WorldAim(player, a);
    }

    private (long X, long Y, long Z) WorldAim(int player, (int X, int Y, int Z) a) =>
        _players.TryGetValue(player, out var p) && _structures.TryGetValue(p.ShipStructure, out var s) ? s.Attitude.Rotate(a.X, a.Y, a.Z) : (a.X, a.Y, a.Z);

    /// <summary>The recoil of one volley of the player's guns, in thousandths of a unit per tick at the reference mass: the sum of each gun's recoil times its strength (0 for the main guns).</summary>
    public int GunRecoil(int player)
    {
        if (!_players.TryGetValue(player, out var p) || !_structures.TryGetValue(p.ShipStructure, out var s) || Content?.Structures is not { } kinds) return 0;
        long total = 0;
        foreach (var g in MountsOf(s, false)) total += (long)kinds.Recoil(g.Kind) * g.Percent / 100;
        return (int)total;
    }

    // ---- hand mining past the limit ---------------------------------------------------------------------------

    /// <summary>True when hand mining may run: always in a world with the new feel (a full hold overflows and a filling one slows), else only with room and below the limit.</summary>
    private bool HandMiningHasRoom(Player p) =>
        !Rules.LegacyFeel || (p.HoldTotal + p.ShipReserved < HoldCapacityOf(p) && !MiningHoldLimitReached(p));

    /// <summary>The factor by which hand mining slows past <see cref="PlayerRules.MiningHoldLimitPercent"/> of the hold (structure worlds with the new feel): 1 otherwise.</summary>
    private int HandMiningPenalty(Player p) => !Rules.LegacyFeel && MiningHoldLimitReached(p) ? FeelRules.SoftHoldPenalty : 1;

    private int MineLimitFactor => Rules.LegacyFeel ? 1 : FeelRules.SoftHoldPenalty;

    /// <summary>True when the player's hand mining is slowed because the hold is past the limit (the weapon card says so).</summary>
    public bool HandMiningSlowed(int player) => _players.TryGetValue(player, out var p) && HandMiningPenalty(p) > 1;

    /// <summary>Ticks of hand mining per item at the deposit as it is now: <see cref="MineTicksFor(int, int)"/> times the slowdown past the hold limit.</summary>
    public int MineTicksNow(int player, int deposit) =>
        MineTicksFor(player, deposit) * (_players.TryGetValue(player, out var p) ? HandMiningPenalty(p) : 1);

    /// <summary>One item of hand-mined ore: into the hold, or, when the hold is full (new feel), into a pickup beside the rock.</summary>
    private void HandMinedItem(Player p, Deposit d)
    {
        if (Rules.LegacyFeel || p.HoldTotal + p.ShipReserved < HoldCapacityOf(p)) { AddToHold(p, d.Item, 1); return; }
        Overflow(p, d, 1);
    }

    private void Overflow(Player p, Deposit d, int count)
    {
        foreach (var k in _pickups)
        {
            if (k.Source != d.Id || k.Item != d.Item) continue;
            k.Count += count;
            k.Life = FeelRules.PickupLifeTicks;
            return;
        }
        if (_pickups.Count >= FeelRules.MaxPickups) { _pickupEvents.Add(new PickupEvent(PickupEventKind.Lost, d.Id, d.Item, count)); return; }
        long cu = Units.CellUnits;
        long cx = d.X * cu, cy = d.Y * cu, cz = d.Z * cu;
        long dx = p.X - cx, dy = p.Y - cy, dz = p.Z - cz, len = Math.Max(1, IntMath.ISqrt(dx * dx + dy * dy + dz * dz));
        long reach = DepositRules.NominalRadiusUnits(d.Initial) + 120; // just outside the rock, on the side of the ship
        var k2 = new OrePickup
        {
            Id = _nextPickupId++, Item = d.Item, Count = count, Source = d.Id, Life = FeelRules.PickupLifeTicks,
            X = (int)(cx + dx * reach / len), Y = (int)(cy + dy * reach / len), Z = (int)(cz + dz * reach / len),
            Vx = (int)(dx * FeelRules.PickupDriftUnits / len), Vy = (int)(dy * FeelRules.PickupDriftUnits / len), Vz = (int)(dz * FeelRules.PickupDriftUnits / len),
        };
        _pickups.Add(k2);
        _pickupEvents.Add(new PickupEvent(PickupEventKind.Created, d.Id, d.Item, count));
    }

    /// <summary>The ore pickups in creation order.</summary>
    public IReadOnlyList<PickupInfo> Pickups() =>
        _pickups.Select(k => new PickupInfo(k.Id, k.Item, k.Count, k.Source, k.X, k.Y, k.Z, k.Life)).ToList();

    /// <summary>The projectiles in flight in creation order.</summary>
    public IReadOnlyList<ProjectileInfo> Projectiles() =>
        _projectiles.Select(q => new ProjectileInfo(q.Player, q.X, q.Y, q.Z, q.Vx, q.Vy, q.Vz, q.Life)).ToList();

    // ---- the tick ----------------------------------------------------------------------------------------------

    /// <summary>
    /// Runs after the obstacles each tick: pickups drift and are collected, projectiles fly and hit, guns with an aim fire, emptied deposits count down to regrow.
    /// Cheap when none of it is in play.
    /// </summary>
    private void StepFeel()
    {
        if (!ThreatActive)
            foreach (var p in _players.Values)
                if (p.WeaponCooldown > 0) p.WeaponCooldown--; // the threat step does it in worlds that have one
        if (_pickups.Count > 0) StepPickups();
        if (_projectiles.Count > 0) StepProjectiles();
        if (!Rules.LegacyFeel && _aim.Count > 0)
            foreach (var p in _players.Values)
                if (p.Armed && p.RespawnRemaining == 0 && p.WeaponCooldown == 0 && _aim.TryGetValue(p.Id, out var aim)) FireFree(p, aim);
        if (!Rules.LegacyResources) StepRegrowth();
    }

    private void StepRegrowth()
    {
        foreach (var d in _deposits)
        {
            if (d.Remaining > 0) continue;
            if (d.RegrowTicks == 0) { d.RegrowTicks = FeelRules.RegrowDelayTicks; continue; }
            if (--d.RegrowTicks > 0) continue;
            if (RegrowthBlocked(d)) { d.RegrowTicks = FeelRules.RegrowRetryTicks; continue; } // a rock may not come back inside a ship or station: wait until they are clear
            int amount = Math.Min(d.Initial, Math.Max(FeelRules.RegrowMinimum, d.Initial * FeelRules.RegrowPercent / 100));
            d.Remaining = amount;
            RegrownTotal += amount;
            StructureVersion++;
        }
    }

    /// <summary>True when a module of a ship or station is within the rock of the deposit (as it would be at its first size) plus a margin of <see cref="FeelRules.RegrowClearUnits"/>.</summary>
    private bool RegrowthBlocked(Deposit d)
    {
        long cu = Units.CellUnits, cx = d.X * cu, cy = d.Y * cu, cz = d.Z * cu;
        long r = (long)DepositRules.NominalRadiusUnits(d.Initial) * (WorldGen?.DepositHitPercent ?? 100) / 100 + FeelRules.RegrowClearUnits;
        foreach (var s in _structures.Values)
            foreach (var key in s.ModuleMap.Keys)
                if (BoxToPoint(s, key, cx, cy, cz).D2 < r * r) return true;
        return false;
    }

    private void StepPickups()
    {
        for (int i = 0; i < _pickups.Count; i++)
        {
            var k = _pickups[i];
            if (--k.Life <= 0)
            {
                _pickupEvents.Add(new PickupEvent(PickupEventKind.Expired, k.Source, k.Item, k.Count));
                _pickups.RemoveAt(i--);
                continue;
            }
            if (k.Dropped) continue; // dropped stacks lie where they fell and are taken by hand (ADR 0038)
            if (k.Life > FeelRules.PickupLifeTicks - FeelRules.PickupDriftTicks) { k.X += k.Vx; k.Y += k.Vy; k.Z += k.Vz; }

            foreach (var p in _players.Values)
            {
                if (p.RespawnRemaining > 0) continue;
                int room = HoldCapacityOf(p) - p.HoldTotal - p.ShipReserved;
                if (room <= 0) continue;
                var (hx, hy, hz) = NearestHullPoint(p, k.X, k.Y, k.Z); // the ship is long: the tractor reaches from its nearest module, not from the seat
                long dx = hx - k.X, dy = hy - k.Y, dz = hz - k.Z, d2 = dx * dx + dy * dy + dz * dz;
                if (d2 > (long)FeelRules.TractorRangeUnits * FeelRules.TractorRangeUnits) continue;
                long dist = IntMath.ISqrt(d2);
                if (dist <= FeelRules.CollectRadiusUnits + FeelRules.TractorStepUnits) // one more pull and it is aboard: take it now (a step shorter than the gap could stall on integer rounding)
                {
                    int take = Math.Min(room, k.Count);
                    AddToHold(p, k.Item, take);
                    k.Count -= take;
                    _pickupEvents.Add(new PickupEvent(PickupEventKind.Collected, k.Source, k.Item, take));
                    if (k.Count == 0) _pickups.RemoveAt(i--);
                }
                else
                {
                    long step = FeelRules.TractorStepUnits;
                    k.X += (int)(dx * step / dist); k.Y += (int)(dy * step / dist); k.Z += (int)(dz * step / dist);
                }
                break;
            }
        }
    }

    /// <summary>The centre of the module of the player's ship nearest to a point (units), or the ship's reference point when it has none.</summary>
    private (long X, long Y, long Z) NearestHullPoint(Player p, long x, long y, long z)
    {
        if (!_structures.TryGetValue(p.ShipStructure, out var s) || s.ModuleMap.Count == 0) return (p.X, p.Y, p.Z);
        (long X, long Y, long Z) best = (p.X, p.Y, p.Z);
        long bestD = long.MaxValue;
        foreach (var key in s.ModuleMap.Keys)
        {
            var c = ModuleWorldCentre(s.Id, key.Mx, key.My, key.Mz);
            long dx = c.X - x, dy = c.Y - y, dz = c.Z - z, d2 = dx * dx + dy * dy + dz * dz;
            if (d2 < bestD) { bestD = d2; best = (c.X, c.Y, c.Z); }
        }
        return best;
    }

    // ---- guns ----------------------------------------------------------------------------------------------------

    /// <summary>One volley along the aim: a projectile leaves the gun nearest to where the aim points, the ship takes the recoil of the guns that fired.</summary>
    private void FireFree(Player p, (int X, int Y, int Z) aim)
    {
        var rules = Content?.Threat;
        int interval = rules?.ShipWeaponIntervalTicks ?? 10;
        long range = (long)(rules?.ShipWeaponRangeCells ?? 25) * Units.CellUnits;

        // The guns: the modules of a hardpoint ship, else the one implicit gun at the ship's reference point.
        List<HardpointMount> guns;
        Structure? ship = _structures.TryGetValue(p.ShipStructure, out var found) ? found : null;
        if (HardpointsRequired(p)) guns = MountsOf(ship!, false);
        else guns = new List<HardpointMount> { new(0, 0, 0, ModuleKind.Gun, p.X, p.Y, p.Z, 100, 0) };
        if (guns.Count == 0) return;
        if (_projectiles.Count >= FeelRules.MaxProjectiles) return; // too many in the air: wait for the next tick

        // Ammo, when the content asks for it.
        if (rules is { ShipAmmoPerVolley: > 0 } && ShipAmmoLacking(p, rules, interval)) return;

        var (wx, wy, wz) = WorldAim(p.Id, aim);
        long len = IntMath.ISqrt(wx * wx + wy * wy + wz * wz);
        if (len == 0) return;

        // The gun nearest to the point the aim reaches at the weapon range (lowest module on ties).
        long tx = p.X + wx * range / len, ty = p.Y + wy * range / len, tz = p.Z + wz * range / len;
        HardpointMount from = guns[0];
        long best = long.MaxValue;
        foreach (var g in guns)
        {
            long dx = tx - g.X, dy = ty - g.Y, dz = tz - g.Z, d2 = dx * dx + dy * dy + dz * dz;
            if (d2 < best) { best = d2; from = g; }
        }
        int percent = 0;
        foreach (var g in guns) percent += g.Percent;
        int damage = Math.Max(1, (int)((long)ShipWeaponDamage * percent / 100));

        var shot = new Projectile
        {
            Player = p.Id, Damage = damage, Life = (int)((range + FeelRules.ProjectileSpeedUnits - 1) / FeelRules.ProjectileSpeedUnits),
            X = (int)from.X, Y = (int)from.Y, Z = (int)from.Z,
            Vx = (int)(wx * FeelRules.ProjectileSpeedUnits / len), Vy = (int)(wy * FeelRules.ProjectileSpeedUnits / len), Vz = (int)(wz * FeelRules.ProjectileSpeedUnits / len),
        };
        _projectiles.Add(shot);
        p.WeaponCooldown = interval;
        _shots.Add(new ShotEvent(ShotKind.Ship, shot.X, shot.Y, shot.Z, shot.X + shot.Vx * 2, shot.Y + shot.Vy * 2, shot.Z + shot.Vz * 2));

        // Recoil: opposite to the firing direction, in the world, scaled by the strength of the guns that fired and the mass of the ship.
        if (ship is not null && Content?.Structures is { } kinds && FlightInertia && ship.DockedTo < 0)
        {
            long recoil = 0;
            foreach (var g in guns) recoil += (long)kinds.Recoil(g.Kind) * g.Percent / 100;
            if (recoil > 0) ApplyRecoil(p, ship, wx, wy, wz, len, recoil);
        }
    }

    private bool ShipAmmoLacking(Player p, ThreatRules rules, int interval)
    {
        if (p.Hold[rules.ShipAmmoItem] >= rules.ShipAmmoPerVolley) { DumpItems(p.Id, rules.ShipAmmoItem, rules.ShipAmmoPerVolley); return false; }
        _dryFire.Add(p.Id);
        p.WeaponCooldown = interval; // one click per interval, not one per tick
        return true;
    }

    /// <summary>The velocity change of a volley, in thousandths of a unit per tick: opposite to the firing vector, <c>recoil * reference mass / mass</c> long.</summary>
    public static (int X, int Y, int Z) RecoilImpulse(long fx, long fy, long fz, long length, long recoil, int shipModules)
    {
        if (length <= 0 || recoil <= 0) return (0, 0, 0);
        long dv = recoil * FeelRules.RecoilReferenceMass / Math.Max(1, shipModules);
        return ((int)(-fx * dv / length), (int)(-fy * dv / length), (int)(-fz * dv / length));
    }

    private void ApplyRecoil(Player p, Structure ship, long fx, long fy, long fz, long length, long recoil)
    {
        var (ix, iy, iz) = RecoilImpulse(fx, fy, fz, length, recoil, ship.ModuleMap.Count);
        static int Cap(long v) => (int)Math.Clamp(v, -FeelRules.MaxRecoilVelocity, FeelRules.MaxRecoilVelocity);
        p.VelX = Cap((long)p.VelX + ix); p.VelY = Cap((long)p.VelY + iy); p.VelZ = Cap((long)p.VelZ + iz);
    }

    // ---- projectiles -----------------------------------------------------------------------------------------------

    private void StepProjectiles()
    {
        var kept = new List<Projectile>(_projectiles.Count);
        bool anyDead = false;
        foreach (var q in _projectiles)
        {
            if (!FlyProjectile(q, ref anyDead)) kept.Add(q);
        }
        _projectiles = kept;
        if (anyDead)
        {
            int killed = _enemies.RemoveAll(e => e.Hp <= 0);
            EnemiesKilled += killed;
        }
    }

    /// <summary>Moves one projectile a tick. True when it ended (hit something or ran out of life).</summary>
    private bool FlyProjectile(Projectile q, ref bool enemyDied)
    {
        long speed = IntMath.ISqrt((long)q.Vx * q.Vx + (long)q.Vy * q.Vy + (long)q.Vz * q.Vz);
        long bestT = long.MaxValue;
        ImpactKind kind = ImpactKind.Rock;
        int id = -1;

        // The first thing on the path wins; ties go to the first found (enemies, then obstacles, then deposits, each in id order).
        foreach (var e in _enemies)
        {
            if (e.Hp <= 0) continue;
            if (SegmentHitsSphere(q, speed, e.X, e.Y, e.Z, FeelRules.EnemyHitRadiusUnits, out long t) && t < bestT) { bestT = t; kind = ImpactKind.Enemy; id = e.Id; }
        }
        if (Galaxy is { } galaxy && galaxy.Obstacles.Count > 0 && WorldGen is { } w)
        {
            int sys = galaxy.SystemAt(q.X, q.Y, q.Z);
            if (sys >= 0)
                foreach (int i in galaxy.ObstaclesOf(sys))
                {
                    var st = _obstacleState[i];
                    var def = w.Kinds[galaxy.Obstacles[i].Kind];
                    if (st.Gone || def.Behaviour == ObstacleBehaviour.Cloud) continue;
                    var c = ObstacleCentreAt(i, Tick);
                    if (SegmentHitsSphere(q, speed, c.X, c.Y, c.Z, ObstacleHitRadius(i, padded: false), out long t) && t < bestT)
                    {
                        bestT = t; id = i;
                        kind = def.Behaviour switch { ObstacleBehaviour.Mine => ImpactKind.Mine, ObstacleBehaviour.Drifter => ImpactKind.Debris, ObstacleBehaviour.Wreck => ImpactKind.Wreck, _ => ImpactKind.Rock };
                    }
                }
        }
        long cu = Units.CellUnits;
        int hitPercent = WorldGen?.DepositHitPercent ?? 100;
        foreach (var d in _deposits)
        {
            if (d.Remaining <= 0 || !d.Revealed) continue;
            if (SegmentHitsSphere(q, speed, d.X * cu, d.Y * cu, d.Z * cu, (long)DepositRules.NominalRadiusUnits(d.Initial) * hitPercent / 100, out long t) && t < bestT)
            {
                bestT = t; kind = ImpactKind.Deposit; id = d.Id;
            }
        }

        if (id >= 0)
        {
            int hx = (int)(q.X + (long)q.Vx * bestT / 1024), hy = (int)(q.Y + (long)q.Vy * bestT / 1024), hz = (int)(q.Z + (long)q.Vz * bestT / 1024);
            bool destroyed = false;
            switch (kind)
            {
                case ImpactKind.Enemy:
                {
                    var e = _enemies.First(x => x.Id == id);
                    e.Hp -= q.Damage;
                    destroyed = e.Hp <= 0;
                    enemyDied |= destroyed;
                    break;
                }
                case ImpactKind.Mine:
                {
                    var st = _obstacleState[id];
                    if (st.Fuse == 0) st.Fuse = 1; // the fuse runs out in the obstacle step: the mine goes off at once, where it floats
                    destroyed = true;
                    break;
                }
                case ImpactKind.Debris:
                {
                    var st = _obstacleState[id];
                    st.Wear += q.Damage;
                    if (st.Wear >= FeelRules.DebrisDurability) { st.Gone = true; destroyed = true; StructureVersion++; }
                    break;
                }
            }
            _impacts.Add(new ImpactEvent(kind, id, hx, hy, hz, destroyed));
            return true;
        }

        q.X += q.Vx; q.Y += q.Vy; q.Z += q.Vz;
        return --q.Life <= 0;
    }

    /// <summary>
    /// Whether the projectile's step this tick crosses the sphere, and where, in 1024ths of the step. A sphere the projectile starts inside is hit at once.
    /// Integers only: the entry is <c>(s - sqrt(s^2 - |v|^2 (|c|^2 - r^2))) / |v|^2</c> with <c>c</c> from the start to the centre and <c>s = c.v</c>.
    /// </summary>
    private static bool SegmentHitsSphere(Projectile q, long speed, long cx, long cy, long cz, long r, out long t1024)
    {
        t1024 = 0;
        long rx = cx - q.X, ry = cy - q.Y, rz = cz - q.Z;
        long reach = r + speed;
        long c2 = rx * rx + ry * ry + rz * rz;
        if (c2 > reach * reach) return false;
        if (c2 <= r * r) return true;
        long l2 = (long)q.Vx * q.Vx + (long)q.Vy * q.Vy + (long)q.Vz * q.Vz;
        if (l2 == 0) return false;
        long s = rx * q.Vx + ry * q.Vy + rz * q.Vz;
        if (s <= 0) return false; // the sphere is behind it
        long disc = s * s - l2 * (c2 - r * r);
        if (disc < 0) return false; // it passes the sphere by
        long entry = (s - IntMath.ISqrt(disc)) * 1024 / l2;
        if (entry > 1024) return false; // not this tick
        t1024 = Math.Max(0, entry);
        return true;
    }

    // ---- state, checksum, validation ---------------------------------------------------------------------------------

    private void CopyFeelFrom(BeltNetwork other)
    {
        _aim.Clear();
        foreach (var (k, v) in other._aim) _aim[k] = v;
        _projectiles = other._projectiles.Select(q => q.Clone()).ToList();
        _pickups = other._pickups.Select(k => k.Clone()).ToList();
        _nextPickupId = other._nextPickupId;
        RegrownTotal = other.RegrownTotal;
        ClearFeelEvents();
    }

    private void MixFeel(Action<long> mix)
    {
        foreach (var (player, a) in _aim) { mix(-613); mix(player); mix(a.X); mix(a.Y); mix(a.Z); }
        if (_projectiles.Count > 0)
        {
            mix(-611); mix(_projectiles.Count);
            foreach (var q in _projectiles) { mix(q.Player); mix(q.Life); mix(q.Damage); mix(q.X); mix(q.Y); mix(q.Z); mix(q.Vx); mix(q.Vy); mix(q.Vz); }
        }
        if (_pickups.Count > 0 || _nextPickupId != 0)
        {
            mix(-612); mix(_nextPickupId); mix(_pickups.Count);
            foreach (var k in _pickups) { mix(k.Id); mix(k.Item); mix(k.Count); mix(k.Source); mix(k.Life); mix(k.X); mix(k.Y); mix(k.Z); mix(k.Vx); mix(k.Vy); mix(k.Vz); }
            foreach (var k in _pickups) if (k.Dropped) { mix(-616); mix(k.Id); } // only when something was dropped
        }
        foreach (var d in _deposits)
            if (d.RegrowTicks != 0) { mix(-614); mix(d.Id); mix(d.RegrowTicks); }
        if (RegrownTotal != 0) { mix(-615); mix(RegrownTotal); }
    }

    private void ValidateFeel()
    {
        static void Fail(string msg) => throw new InvalidOperationException(msg);
        if (Rules.LegacyFeel && (_aim.Count != 0 || _projectiles.Count != 0 || _pickups.Count != 0)) Fail("aim, projectiles or pickups exist in a world with the legacy feel");
        foreach (var (player, a) in _aim)
        {
            if (!_players.ContainsKey(player)) Fail($"aim of unknown player {player}");
            if (!AimInRange(a.X, a.Y, a.Z) || (a.X | a.Y | a.Z) == 0) Fail($"aim of player {player} out of range");
        }
        if (_projectiles.Count > FeelRules.MaxProjectiles) Fail("more projectiles than the cap");
        int maxLife = (int)(((long)(Content?.Threat?.ShipWeaponRangeCells ?? 25) * Units.CellUnits + FeelRules.ProjectileSpeedUnits - 1) / FeelRules.ProjectileSpeedUnits);
        foreach (var q in _projectiles)
        {
            if (q.Life < 1 || q.Life > maxLife || q.Damage < 1) Fail("a projectile has a bad life or damage");
            if (Math.Abs((long)q.X) > PlayerRules.WorldLimitUnits * 2L || Math.Abs((long)q.Y) > PlayerRules.WorldLimitUnits * 2L || Math.Abs((long)q.Z) > PlayerRules.WorldLimitUnits * 2L) Fail("a projectile is far outside the world");
        }
        if (_pickups.Count > FeelRules.MaxPickups) Fail("more pickups than the cap");
        int lastId = -1, items = Content?.Items.Count ?? 0;
        foreach (var k in _pickups)
        {
            if (k.Id <= lastId) Fail("pickup ids are not increasing");
            lastId = k.Id;
            if (k.Count < 1 || k.Item < 0 || k.Item >= items || k.Life < 1) Fail($"pickup {k.Id} is out of range");
            if (k.Dropped ? k.Source != -1 || k.Life > FeelRules.DropLifeTicks || Rules.LegacyInventory : k.Life > FeelRules.PickupLifeTicks || !IsDeposit(k.Source)) Fail($"pickup {k.Id} is out of range");
        }
        if (_nextPickupId <= lastId) Fail("the pickup id counter is behind");
        foreach (var d in _deposits)
        {
            if (d.RegrowTicks < 0 || d.RegrowTicks > FeelRules.RegrowDelayTicks) Fail($"deposit {d.Id} regrow timer out of range");
            if (d.RegrowTicks != 0 && (d.Remaining > 0 || Rules.LegacyResources)) Fail($"deposit {d.Id} counts down to regrow but holds ore or cannot regrow");
        }
        if (RegrownTotal < 0 || (Rules.LegacyResources && RegrownTotal != 0)) Fail("regrown total out of range");
    }
}
