namespace Starfoundry.Sim;

internal static class IntMath
{
    /// <summary>Integer square root (floor) of a non-negative value.</summary>
    public static long ISqrt(long v)
    {
        if (v <= 0) return 0;
        // Bit-by-bit method: pure integer, no floating point seed (determinism, see BannedSymbols.txt).
        long result = 0, bit = 1L << 62;
        while (bit > v) bit >>= 2;
        while (bit != 0)
        {
            if (v >= result + bit)
            {
                v -= result + bit;
                result = (result >> 1) + bit;
            }
            else result >>= 1;
            bit >>= 2;
        }
        return result;
    }

    /// <summary>Floor division (rounds toward negative infinity) for a positive divisor.</summary>
    public static int FloorDiv(int a, int b) => a / b - (a % b < 0 ? 1 : 0);

    /// <summary>Floor division of a long for a positive divisor.</summary>
    public static long FloorDiv(long a, long b) => a / b - (a % b < 0 ? 1 : 0);

    /// <summary>Integer cube root (floor) of a non-negative value, by bisection.</summary>
    public static long ICbrt(long v)
    {
        if (v <= 0) return 0;
        long lo = 0, hi = 2_097_151; // 2^21 cubed is just under 2^63
        while (lo < hi)
        {
            long mid = (lo + hi + 1) / 2;
            if (mid * mid * mid <= v) lo = mid; else hi = mid - 1;
        }
        return lo;
    }

    /// <summary>Ceiling division (rounds toward positive infinity) for a positive divisor.</summary>
    public static int CeilDiv(int a, int b) => a / b + (a % b > 0 ? 1 : 0);
}

internal sealed class SystemThreat
{
    /// <summary>Alert meter, from 0 to the content's maximum.</summary>
    public int Alert;

    /// <summary>Waves spawned here so far; the next wave is this number (0 is the first).</summary>
    public int Wave;

    /// <summary>Ticks until the scheduled wave spawns, or -1 if none is scheduled.</summary>
    public int Warning = -1;

    /// <summary>Structure worlds: ticks this system has spent above the signature threshold before its first wave (up to the content's grace).</summary>
    public int Grace;

    public SystemThreat Clone() => new() { Alert = Alert, Wave = Wave, Warning = Warning, Grace = Grace };
}

internal sealed class Enemy
{
    public int Id, Type, System, X, Y, Z, Hp, Cooldown;

    /// <summary>What the enemy attacked last tick (structure worlds; not part of the state): a structure id and module, or -1.</summary>
    public int TargetStructure = -1, TargetMx, TargetMy, TargetMz;

    public Enemy Clone() => (Enemy)MemberwiseClone();
}

/// <summary>An enemy for rendering and tests. Position in units.</summary>
public readonly record struct EnemyInfo(int Id, int Type, int System, int X, int Y, int Z, int Hp,
    int TargetStructure = -1, int TargetMx = 0, int TargetMy = 0, int TargetMz = 0);

public enum ShotKind { Turret, Ship, Enemy }

/// <summary>A shot fired this tick, for effects. Not part of the simulation state. Positions in units.</summary>
public readonly record struct ShotEvent(ShotKind Kind, int FromX, int FromY, int FromZ, int ToX, int ToY, int ToZ);

/// <summary>A machine that was destroyed by enemies. <see cref="SimWorld"/> uses it to free the machine's cell.</summary>
public readonly record struct DestroyedMachine(int Id, int X, int Y, int Z, int Structure = 0, int Type = 0, int Turn = 0);

/// <summary>The threat state of one system, for the HUD.</summary>
public readonly record struct SystemThreatInfo(int System, int Signature, int Alert, int AlertMax, int Wave, int WarningRemaining, int Enemies);

public sealed partial class BeltNetwork
{
    private SystemThreat[] _threat = Array.Empty<SystemThreat>();
    private List<Enemy> _enemies = new();
    private int _nextEnemyId;
    private readonly List<ShotEvent> _shots = new();
    private readonly List<DestroyedMachine> _destroyedMachines = new();

    public long WavesSpawned { get; private set; }
    public long EnemiesSpawned { get; private set; }
    public long EnemiesKilled { get; private set; }
    public long MachinesLost { get; private set; }

    /// <summary>Walls destroyed so far; they are built to be lost, so they are not counted in <see cref="MachinesLost"/>.</summary>
    public long WallsLost { get; private set; }

    /// <summary>Hit points restored by repairers so far.</summary>
    public long AutoRepaired { get; private set; }
    public long ShipsLost { get; private set; }
    public long AmmoFired { get; private set; }

    /// <summary>True when threats were switched on at creation and the content and the world can support them.</summary>
    public bool ThreatActive => ThreatsEnabled && Rules.ThreatsEnabled && Content?.Threat is not null && Galaxy is not null;

    public int EnemyCount => _enemies.Count;

    /// <summary>Alert meter capacity with the world's wave interval applied (higher percent: slower waves).</summary>
    private int AlertMaxNow => Math.Max(1, WorldRules.Scale(Content!.Threat!.AlertMax, Rules.WaveIntervalPercent));

    /// <summary>Alert after a wave, scaled like <see cref="AlertMaxNow"/> so the whole time to the next wave scales.</summary>
    private int AlertAfterWaveNow => Math.Min(AlertMaxNow - 1, WorldRules.Scale(Content!.Threat!.AlertAfterWave, Rules.WaveIntervalPercent));

    /// <summary>First wave grace in ticks: the rules' seconds when set, else the content's value.</summary>
    private int GraceTicksNow => Rules.FirstWaveGraceSeconds >= 0 ? Rules.FirstWaveGraceSeconds * Units.TicksPerSecond : Content!.Threat!.FirstWaveGraceTicks;

    /// <summary>Hit points of an enemy type at spawn, with the world's enemy hp applied.</summary>
    public int EnemyMaxHp(int type) => WorldRules.ScaleMin1(Content!.Enemies[type].Hp, Rules.EnemyHpPercent);

    /// <summary>Damage of one shot of an enemy type, with the world's enemy damage applied.</summary>
    public int EnemyShotDamage(int type) => WorldRules.ScaleMin1(Content!.Enemies[type].Damage, Rules.EnemyDamagePercent);

    /// <summary>Shots fired during the last tick (effects only).</summary>
    public IReadOnlyList<ShotEvent> Shots => _shots;

    /// <summary>Machines destroyed since the last call. The command layer calls this after every tick.</summary>
    public IReadOnlyList<DestroyedMachine> TakeDestroyedMachines()
    {
        if (_destroyedMachines.Count == 0) return Array.Empty<DestroyedMachine>();
        var copy = _destroyedMachines.ToArray();
        _destroyedMachines.Clear();
        return copy;
    }

    public IReadOnlyList<EnemyInfo> Enemies() =>
        _enemies.Select(e => new EnemyInfo(e.Id, e.Type, e.System, e.X, e.Y, e.Z, e.Hp, e.TargetStructure, e.TargetMx, e.TargetMy, e.TargetMz)).ToList();

    private void InitThreat()
    {
        _threat = new SystemThreat[SystemCount];
        for (int i = 0; i < _threat.Length; i++) _threat[i] = new SystemThreat();
    }

    /// <summary>Sum of the signatures of the machines inside a system.</summary>
    public int SystemSignature(int system)
    {
        if (Galaxy is null || Content is null) return 0;
        int total = 0;
        foreach (var m in _machines)
        {
            if (m is null) continue;
            if (SystemOfMachine(m) == system) total += Content.Machines[m.Type].Signature;
        }
        return total;
    }

    public SystemThreatInfo GetSystemThreat(int system)
    {
        if (!IsSystem(system) || _threat.Length == 0) throw new ArgumentOutOfRangeException(nameof(system));
        var t = _threat[system];
        return new SystemThreatInfo(system, SystemSignature(system), t.Alert, Content?.Threat is null ? 0 : AlertMaxNow, t.Wave, t.Warning,
            _enemies.Count(e => e.System == system));
    }

    /// <summary>World position (units) of the centre of a machine's cell; structure machines follow their structure.</summary>
    private (long X, long Y, long Z) MachineCentre(Machine m) => WorldCentre(m.Structure, m.X, m.Y, m.Z);

    private int SystemOfMachine(Machine m)
    {
        var (x, y, z) = MachineCentre(m);
        return Galaxy!.SystemAt(x, y, z);
    }

    /// <summary>Content-driven setup for tests and demos: forces a wave in a system after the usual warning, without waiting for the alert.</summary>
    public void ScheduleWave(int system)
    {
        if (!ThreatActive) throw new InvalidOperationException("Threats are not active.");
        if (!IsSystem(system)) throw new ArgumentOutOfRangeException(nameof(system));
        if (_threat[system].Warning < 0) _threat[system].Warning = Content!.Threat!.WarningTicks;
    }

    // ---- turrets -------------------------------------------------------------------------------

    private bool TryFeedTurret(Machine m, BeltSegment s, int item)
    {
        var turret = Content!.Machines[m.Type].Turret!;
        if (item != turret.AmmoItem || m.In[item] >= turret.AmmoCap) return false;
        m.In[item]++;
        s.PopFront();
        s.Passed++;
        return true;
    }

    private bool TryFeedRepairer(Machine m, BeltSegment s, int item)
    {
        var def = Content!.Machines[m.Type].Repairer!;
        if (item != def.Item || m.In[item] >= def.ItemCap) return false;
        m.In[item]++;
        s.PopFront();
        s.Passed++;
        return true;
    }

    /// <summary>Repairers (machine id order): each interval, heal the most damaged machine in range and use one item.</summary>
    private void StepRepairers()
    {
        foreach (var m in _machines)
        {
            if (m is null) continue;
            var def = Content!.Machines[m.Type].Repairer;
            if (def is null || !m.Powered) continue;
            if (m.TurretCooldown > 0) m.TurretCooldown--;
            if (m.TurretCooldown > 0 || m.In[def.Item] <= 0) continue;

            long range = (long)def.RangeCells * Units.CellUnits;
            Machine? best = null;
            foreach (var t in _machines)
            {
                if (t is null) continue;
                int max = MaxHp(t.Type);
                if (t.Hp >= max) continue;
                long dx, dy, dz;
                if (t.Structure == m.Structure)
                {
                    // Same structure (or both in the legacy world): local differences, no origin involved.
                    dx = (long)(t.X - m.X) * Units.CellUnits; dy = (long)(t.Y - m.Y) * Units.CellUnits; dz = (long)(t.Z - m.Z) * Units.CellUnits;
                }
                else
                {
                    var a = MachineCentre(t); var b = MachineCentre(m);
                    dx = a.X - b.X; dy = a.Y - b.Y; dz = a.Z - b.Z;
                }
                if (dx * dx + dy * dy + dz * dz > range * range) continue;
                // Lowest share of hit points first; the lowest id wins ties.
                if (best is null || (long)t.Hp * MaxHp(best.Type) < (long)best.Hp * max) best = t;
            }
            if (best is null)
            {
                // Nothing to mend among the machines: a bay in a structure mends the structure's modules.
                if (!HealFirstDamagedModule(m, def)) continue;
                m.In[def.Item]--;
                MachineInputsConsumed++;
                m.TurretCooldown = def.IntervalTicks;
                continue;
            }

            int before = best.Hp;
            best.Hp = Math.Min(MaxHp(best.Type), best.Hp + RepairHeal(def));
            AutoRepaired += best.Hp - before;
            m.In[def.Item]--;
            MachineInputsConsumed++;
            m.TurretCooldown = def.IntervalTicks;
        }
    }

    // ---- the tick ----------------------------------------------------------------------------------

    private void StepThreat()
    {
        _shots.Clear();
        _moduleHits.Clear();
        if (!ThreatActive) return;
        var rules = Content!.Threat!;
        int n = Galaxy!.Systems.Count;

        // Players that are being rebuilt count down.
        foreach (var p in _players.Values)
        {
            if (p.RespawnRemaining > 0 && --p.RespawnRemaining == 0) p.Hull = ShipMaxHull;
            if (p.WeaponCooldown > 0) p.WeaponCooldown--;
        }

        // Signature per system, from the machines in it.
        var signature = new int[n];
        foreach (var m in _machines)
        {
            if (m is null) continue;
            int s = SystemOfMachine(m);
            if (s >= 0) signature[s] += Content.Machines[m.Type].Signature;
        }

        // Alert and waves.
        for (int s = 0; s < n; s++)
        {
            var t = _threat[s];
            bool active = t.Warning >= 0 || _enemies.Any(e => e.System == s);
            if (!active)
            {
                int graceTicks = GraceTicksNow;
                int alertMax = AlertMaxNow;
                bool graceOn = StructureRules && t.Wave == 0 && graceTicks > 0;
                if (signature[s] > rules.SignatureThreshold)
                {
                    t.Alert = Math.Min(alertMax, t.Alert + (signature[s] - rules.SignatureThreshold));
                    if (graceOn && t.Grace < graceTicks) t.Grace++;
                }
                else t.Alert = Math.Max(0, t.Alert - rules.DecayPerTick);
                // Structure worlds: the first warning waits for the grace time, counted from when the system first got loud.
                if (t.Alert >= alertMax && !(graceOn && t.Grace < graceTicks)) t.Warning = rules.WarningTicks;
            }
            if (t.Warning > 0 && --t.Warning == 0)
            {
                SpawnWave(s);
                t.Warning = -1;
                t.Alert = AlertAfterWaveNow;
                t.Wave++;
            }
        }

        // Enemies move and attack (in id order).
        if (_enemies.Count > 0)
        {
            HashSet<int>? withMachines = null;
            foreach (var e in _enemies)
            {
                if (e.Hp <= 0) continue;
                var def = Content.Enemies[e.Type];
                if (e.Cooldown > 0) e.Cooldown--;

                Player? ship = null;
                Machine? machine = null;
                int modStructure = -1;
                (int Mx, int My, int Mz) modKey = default;
                long tx, ty, tz;
                if (StructureRules)
                {
                    // Structure worlds: walls first (decoys), then the ship's nearest module within 15 cells, otherwise the nearest
                    // module of a structure in the enemy's system that holds a machine or is a ship.
                    machine = NearestWall(e);
                    (long X, long Y, long Z) mc = default;
                    if (machine is not null) mc = MachineCentre(machine);
                    else if (TryPickShipModule(e, out modStructure, out var k1, out mc) || TryPickNearestModule(e, withMachines ??= StructuresWithMachines(), out modStructure, out k1, out mc))
                        modKey = k1;
                    else { e.TargetStructure = -1; continue; }
                    (tx, ty, tz) = mc;
                }
                else
                {
                    // The nearest ship within 15 cells, otherwise the machine with the highest signature in the enemy's system.
                    long best = 15L * Units.CellUnits * 15 * Units.CellUnits;
                    foreach (var p in _players.Values)
                    {
                        if (p.RespawnRemaining > 0) continue;
                        long dx = p.X - e.X, dy = p.Y - e.Y, dz = p.Z - e.Z;
                        long d = dx * dx + dy * dy + dz * dz;
                        if (d <= best) { best = d; ship = p; }
                    }

                    machine = ship is null ? NearestWall(e) ?? PickTargetMachine(e.System) : null;
                    if (ship is null && machine is null) continue;
                    if (ship is not null) { tx = ship.X; ty = ship.Y; tz = ship.Z; }
                    else (tx, ty, tz) = MachineCentre(machine!);
                }
                e.TargetStructure = modStructure;
                (e.TargetMx, e.TargetMy, e.TargetMz) = modKey;
                long rx = tx - e.X, ry = ty - e.Y, rz = tz - e.Z;
                long dist2 = rx * rx + ry * ry + rz * rz;
                long range = (long)def.RangeCells * Units.CellUnits;
                long dist = IntMath.ISqrt(dist2);

                // A few units of tolerance: integer steps can stop just short of the exact range and must not stall there.
                if (dist > range + 3)
                {
                    long step = Math.Min(def.Speed, dist - range);
                    e.X += (int)(rx * step / dist);
                    e.Y += (int)(ry * step / dist);
                    e.Z += (int)(rz * step / dist);
                }
                else if (e.Cooldown == 0)
                {
                    e.Cooldown = def.IntervalTicks;
                    _shots.Add(new ShotEvent(ShotKind.Enemy, e.X, e.Y, e.Z, (int)tx, (int)ty, (int)tz));
                    if (ship is not null) DamageShip(ship, EnemyShotDamage(e.Type));
                    else if (machine is not null) DamageMachine(machine, EnemyShotDamage(e.Type));
                    else DamageModule(modStructure, modKey, EnemyShotDamage(e.Type));
                }
            }
        }

        // Turrets fire (machine id order).
        foreach (var m in _machines)
        {
            if (m is null) continue;
            var turret = Content.Machines[m.Type].Turret;
            if (turret is null) continue;
            if (m.TurretCooldown > 0) m.TurretCooldown--;
            if (m.TurretCooldown > 0 || m.In[turret.AmmoItem] <= 0 || !TurretHasPower(m)) continue;

            var (cx, cy, cz) = MachineCentre(m);
            var target = NearestEnemy(cx, cy, cz, (long)turret.RangeCells * Units.CellUnits);
            if (target is null) continue;

            target.Hp -= TurretDamageOf(m, turret);
            m.In[turret.AmmoItem]--;
            MachineInputsConsumed++;
            AmmoFired++;
            m.TurretCooldown = TurretInterval(turret);
            _shots.Add(new ShotEvent(ShotKind.Turret, (int)cx, (int)cy, (int)cz, target.X, target.Y, target.Z));
        }

        StepRepairers();
        StepModuleRegen();

        // Ship weapons (player id order).
        foreach (var p in _players.Values)
        {
            if (!p.Armed || p.RespawnRemaining > 0 || p.WeaponCooldown > 0) continue;
            if (FreeFireAims(p)) continue; // ADR 0036: with an aim set the guns fire projectiles (StepFeel), not at the enemy the sim picks
            if (HardpointsRequired(p)) { FireGuns(p, rules); continue; } // ADR 0032: the ship shoots with its gun modules
            var target = NearestEnemy(p.X, p.Y, p.Z, (long)rules.ShipWeaponRangeCells * Units.CellUnits);
            if (target is null) continue;
            target.Hp -= ShipWeaponDamage;
            p.WeaponCooldown = rules.ShipWeaponIntervalTicks;
            _shots.Add(new ShotEvent(ShotKind.Ship, p.X, p.Y, p.Z, target.X, target.Y, target.Z));
        }

        // Dead enemies leave.
        if (_enemies.Count > 0)
        {
            int killed = _enemies.RemoveAll(e => e.Hp <= 0);
            EnemiesKilled += killed;
        }
    }

    private Enemy? NearestEnemy(long x, long y, long z, long range)
    {
        Enemy? best = null;
        long bestD = range * range;
        foreach (var e in _enemies)
        {
            if (e.Hp <= 0) continue;
            long dx = e.X - x, dy = e.Y - y, dz = e.Z - z;
            long d = dx * dx + dy * dy + dz * dz;
            if (d < bestD || (d == bestD && best is null)) { bestD = d; best = e; }
        }
        return best;
    }

    /// <summary>The wall nearest to the enemy in its system (lowest id on ties), or null when there is none.</summary>
    private Machine? NearestWall(Enemy e)
    {
        Machine? best = null;
        long bestD = long.MaxValue;
        foreach (var m in _machines)
        {
            if (m is null || Content!.Machines[m.Type].Role != MachineRole.Wall || SystemOfMachine(m) != e.System) continue;
            var (cx, cy, cz) = MachineCentre(m);
            long dx = cx - e.X, dy = cy - e.Y, dz = cz - e.Z;
            long d = dx * dx + dy * dy + dz * dz;
            if (d < bestD) { bestD = d; best = m; }
        }
        return best;
    }

    /// <summary>Items that restore a damaged machine to full: half its build cost scaled by the missing share, at least one of each item. Empty if it is undamaged.</summary>
    public ItemStack[] RepairCost(int machine)
    {
        var m = _machines[machine]!;
        var def = Content!.Machines[m.Type];
        int maxHp = MaxHp(m.Type);
        int missing = maxHp - m.Hp;
        if (missing <= 0) return System.Array.Empty<ItemStack>();
        return def.Cost.Select(s => new ItemStack(s.Item, Math.Max(1, (int)(((long)s.Count * missing + 2L * maxHp - 1) / (2L * maxHp))))).ToArray();
    }

    /// <summary>Sets a machine's hit points back to its maximum.</summary>
    public void RepairMachine(int machine) => _machines[machine]!.Hp = MaxHp(_machines[machine]!.Type);

    /// <summary>The machine with the highest signature in the system (lowest id on ties); machines with no signature count last.</summary>
    private Machine? PickTargetMachine(int system)
    {
        Machine? best = null;
        int bestSig = -1;
        foreach (var m in _machines)
        {
            if (m is null || SystemOfMachine(m) != system) continue;
            int sig = Content!.Machines[m.Type].Signature;
            if (sig > bestSig) { bestSig = sig; best = m; }
        }
        return best;
    }

    private void DamageMachine(Machine m, int damage)
    {
        m.Hp -= damage;
        if (m.Hp > 0) return;
        _destroyedMachines.Add(new DestroyedMachine(m.Id, m.X, m.Y, m.Z, m.Structure, m.Type, m.Turn));
        if (Content!.Machines[m.Type].Role == MachineRole.Wall) WallsLost++;
        else MachinesLost++;
        RemoveMachine(m.Id);
    }

    private void DamageShip(Player p, int damage)
    {
        if (p.RespawnRemaining > 0) return;
        p.Hull -= damage;
        if (p.Hull > 0) return;

        // The ship is lost: it is rebuilt at the anchor of the system it was in (home if in deep space), keeping its hold.
        var rules = Content!.Threat!;
        int s = Galaxy!.SystemAt(p.X, p.Y, p.Z);
        var anchor = Galaxy.Systems[s < 0 ? 0 : s];
        p.Hull = 0;
        p.RespawnRemaining = rules.RespawnTicks;
        SetShipPosition(p, anchor.X * Units.CellUnits, anchor.Y * Units.CellUnits, anchor.Z * Units.CellUnits);
        ClearMining(p);
        p.JumpTarget = -1;
        p.JumpRemaining = 0;
        p.ClearMotion();
        ShipsLost++;
        StructureVersion++;
    }

    /// <summary>The mean position of the structures that hold machines in a system (units), or null when there are none.</summary>
    private (long X, long Y, long Z)? WaveCentre(int system)
    {
        long sx = 0, sy = 0, sz = 0; int n = 0;
        foreach (int id in StructuresWithMachines())
        {
            var s = _structures[id];
            if (SystemOfStructure(s) != system) continue;
            sx += s.OriginX; sy += s.OriginY; sz += s.OriginZ; n++;
        }
        return n == 0 ? null : (sx / n, sy / n, sz / n);
    }

    private void SpawnWave(int system)
    {
        var rules = Content!.Threat!;
        var t = _threat[system];
        var anchor = Galaxy!.Systems[system];
        var rng = new SplitMix64(Galaxy.Seed ^ ((ulong)(system + 1) * 0x9E3779B97F4A7C15UL) ^ ((ulong)(t.Wave + 1) * 0xC2B2AE3D27D4EB4FUL));

        int d = rules.NodeDistanceCells;
        int ox = rng.Next(-d, d);
        int oz = (int)IntMath.ISqrt((long)d * d - (long)ox * ox) * (rng.Next(0, 1) == 0 ? -1 : 1);
        long nodeX = (anchor.X + (long)ox) * Units.CellUnits, nodeY = anchor.Y * (long)Units.CellUnits, nodeZ = (anchor.Z + (long)oz) * Units.CellUnits;
        if (Galaxy.Wide && WaveCentre(system) is { } bc)
        {
            // In the wide galaxy the base can be far from the anchor, so the wave appears the usual distance from the base, not from the anchor.
            nodeX = bc.X + (long)ox * Units.CellUnits; nodeY = bc.Y; nodeZ = bc.Z + (long)oz * Units.CellUnits;
        }

        int spawned = 0;
        foreach (var spec in Content.Waves)
        {
            int count = WorldRules.ScaleMin1(t.Wave < spec.FromWave ? 0 : spec.Base + spec.PerWave * (t.Wave - spec.FromWave), Rules.WaveSizePercent);
            for (int k = 0; k < count; k++)
            {
                if (_enemies.Count >= rules.MaxEnemies) break;
                _enemies.Add(new Enemy
                {
                    Id = _nextEnemyId++, Type = spec.Enemy, System = system, Hp = EnemyMaxHp(spec.Enemy),
                    X = (int)nodeX + rng.Next(-Units.CellUnits, Units.CellUnits),
                    Y = (int)nodeY + rng.Next(-Units.CellUnits, Units.CellUnits),
                    Z = (int)nodeZ + rng.Next(-Units.CellUnits, Units.CellUnits),
                });
                spawned++;
            }
        }
        WavesSpawned++;
        EnemiesSpawned += spawned;
    }

    // ---- state, checksum, validation --------------------------------------------------------------------

    private void CopyThreatFrom(BeltNetwork other)
    {
        _threat = other._threat.Select(t => t.Clone()).ToArray();
        _enemies = other._enemies.Select(e => e.Clone()).ToList();
        _nextEnemyId = other._nextEnemyId;
        WavesSpawned = other.WavesSpawned;
        EnemiesSpawned = other.EnemiesSpawned;
        EnemiesKilled = other.EnemiesKilled;
        MachinesLost = other.MachinesLost;
        WallsLost = other.WallsLost;
        AutoRepaired = other.AutoRepaired;
        ShipsLost = other.ShipsLost;
        ModulesLost = other.ModulesLost;
        StationsLost = other.StationsLost;
        AmmoFired = other.AmmoFired;
        _destroyedMachines.Clear();
        _destroyedMachines.AddRange(other._destroyedMachines);
        _destroyedSegments.Clear();
        _destroyedSegments.AddRange(other._destroyedSegments);
        _moduleHits.Clear();
        _shots.Clear();
    }

    private void MixThreat(Action<long> mix)
    {
        mix(WavesSpawned); mix(EnemiesSpawned); mix(EnemiesKilled); mix(MachinesLost); mix(WallsLost); mix(AutoRepaired); mix(ShipsLost); mix(AmmoFired); mix(_nextEnemyId);
        if (ModulesLost != 0 || StationsLost != 0) { mix(ModulesLost); mix(StationsLost); } // worlds that lost no module hash as before
        foreach (var t in _threat) { mix(t.Alert); mix(t.Wave); mix(t.Warning); if (t.Grace != 0) mix(t.Grace); }
        mix(_enemies.Count);
        foreach (var e in _enemies) { mix(e.Id); mix(e.Type); mix(e.System); mix(e.X); mix(e.Y); mix(e.Z); mix(e.Hp); mix(e.Cooldown); }
    }

    private void ValidateThreat()
    {
        static void Fail(string msg) => throw new InvalidOperationException(msg);

        if (!ThreatActive)
        {
            if (_enemies.Count != 0) Fail("enemies exist while threats are inactive");
            bool collisions = ObstacleCount > 0; // collisions and mines destroy modules without any threat (ADR 0028)
            if (WavesSpawned != 0 || AutoRepaired != 0 || (!collisions && (MachinesLost != 0 || WallsLost != 0 || ModulesLost != 0 || StationsLost != 0))) Fail("threat counters changed while threats are inactive"); // a mine can take machines with a module, so they count with the modules
            return;
        }

        var rules = Content!.Threat!;
        if (_threat.Length != Galaxy!.Systems.Count) Fail("threat state has the wrong size");
        for (int s = 0; s < _threat.Length; s++)
        {
            var t = _threat[s];
            if (t.Alert < 0 || t.Alert > AlertMaxNow) Fail($"system {s} alert {t.Alert} out of range");
            if (t.Warning < -1 || t.Warning > rules.WarningTicks) Fail($"system {s} warning {t.Warning} out of range");
            if (t.Wave < 0) Fail($"system {s} wave count is negative");
            if (t.Grace < 0 || t.Grace > GraceTicksNow || (t.Grace != 0 && !StructureRules)) Fail($"system {s} grace {t.Grace} out of range");
        }

        int lastId = -1;
        foreach (var e in _enemies)
        {
            if (e.Id <= lastId) Fail("enemy ids are not increasing");
            lastId = e.Id;
            if (e.Type < 0 || e.Type >= Content.Enemies.Count) Fail($"enemy {e.Id} has an unknown type");
            else if (e.Hp < 1 || e.Hp > EnemyMaxHp(e.Type)) Fail($"enemy {e.Id} hit points {e.Hp} out of range");
            if (!IsSystem(e.System)) Fail($"enemy {e.Id} is in an unknown system");
            if (Math.Abs(e.X) > WorldLimit * 2L || Math.Abs(e.Y) > WorldLimit * 2L || Math.Abs(e.Z) > WorldLimit * 2L)
                Fail($"enemy {e.Id} is far outside the world");
        }
        if (_enemies.Count > rules.MaxEnemies) Fail("more enemies than the cap");
        if (_nextEnemyId < _enemies.Count) Fail("enemy id counter is behind");
        if (EnemiesSpawned < EnemiesKilled + _enemies.Count) Fail("enemy accounting broken");

        foreach (var p in _players.Values)
        {
            if (p.Hull < 0 || p.Hull > ShipMaxHull) Fail($"player {p.Id} hull {p.Hull} out of range");
            if (p.RespawnRemaining < 0 || p.RespawnRemaining > rules.RespawnTicks) Fail($"player {p.Id} respawn time out of range");
            if ((p.RespawnRemaining > 0) != (p.Hull == 0)) Fail($"player {p.Id} hull and respawn disagree");
            if (p.WeaponCooldown < 0 || p.WeaponCooldown > rules.ShipWeaponIntervalTicks) Fail($"player {p.Id} weapon cooldown out of range");
        }
    }
}
