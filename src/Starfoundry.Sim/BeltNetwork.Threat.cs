namespace Starfoundry.Sim;

internal static class IntMath
{
    /// <summary>Integer square root (floor) of a non-negative value.</summary>
    public static long ISqrt(long v)
    {
        if (v <= 0) return 0;
        long x = (long)Math.Sqrt(v);
        while (x * x > v) x--;
        while ((x + 1) * (x + 1) <= v) x++;
        return x;
    }
}

internal sealed class SystemThreat
{
    /// <summary>Alert meter, from 0 to the content's maximum.</summary>
    public int Alert;

    /// <summary>Waves spawned here so far; the next wave is this number (0 is the first).</summary>
    public int Wave;

    /// <summary>Ticks until the scheduled wave spawns, or -1 if none is scheduled.</summary>
    public int Warning = -1;

    public SystemThreat Clone() => new() { Alert = Alert, Wave = Wave, Warning = Warning };
}

internal sealed class Enemy
{
    public int Id, Type, System, X, Y, Z, Hp, Cooldown;

    public Enemy Clone() => (Enemy)MemberwiseClone();
}

/// <summary>An enemy for rendering and tests. Position in units.</summary>
public readonly record struct EnemyInfo(int Id, int Type, int System, int X, int Y, int Z, int Hp);

public enum ShotKind { Turret, Ship, Enemy }

/// <summary>A shot fired this tick, for effects. Not part of the simulation state. Positions in units.</summary>
public readonly record struct ShotEvent(ShotKind Kind, int FromX, int FromY, int FromZ, int ToX, int ToY, int ToZ);

/// <summary>A machine that was destroyed by enemies. <see cref="SimWorld"/> uses it to free the machine's cell.</summary>
public readonly record struct DestroyedMachine(int Id, int X, int Y, int Z);

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
    public long ShipsLost { get; private set; }
    public long AmmoFired { get; private set; }

    /// <summary>True when threats were switched on at creation and the content and the world can support them.</summary>
    public bool ThreatActive => ThreatsEnabled && Content?.Threat is not null && Galaxy is not null;

    public int EnemyCount => _enemies.Count;

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
        _enemies.Select(e => new EnemyInfo(e.Id, e.Type, e.System, e.X, e.Y, e.Z, e.Hp)).ToList();

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
        return new SystemThreatInfo(system, SystemSignature(system), t.Alert, Content?.Threat?.AlertMax ?? 0, t.Wave, t.Warning,
            _enemies.Count(e => e.System == system));
    }

    private int SystemOfMachine(Machine m) =>
        Galaxy!.SystemAt(m.X * (long)Units.CellUnits + Units.CellUnits / 2, m.Y * (long)Units.CellUnits + Units.CellUnits / 2,
            m.Z * (long)Units.CellUnits + Units.CellUnits / 2);

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

    // ---- the tick ----------------------------------------------------------------------------------

    private void StepThreat()
    {
        _shots.Clear();
        if (!ThreatActive) return;
        var rules = Content!.Threat!;
        int n = Galaxy!.Systems.Count;

        // Players that are being rebuilt count down.
        foreach (var p in _players.Values)
        {
            if (p.RespawnRemaining > 0 && --p.RespawnRemaining == 0) p.Hull = rules.ShipHull;
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
                if (signature[s] > rules.SignatureThreshold) t.Alert = Math.Min(rules.AlertMax, t.Alert + (signature[s] - rules.SignatureThreshold));
                else t.Alert = Math.Max(0, t.Alert - rules.DecayPerTick);
                if (t.Alert >= rules.AlertMax) t.Warning = rules.WarningTicks;
            }
            if (t.Warning > 0 && --t.Warning == 0)
            {
                SpawnWave(s);
                t.Warning = -1;
                t.Alert = rules.AlertAfterWave;
                t.Wave++;
            }
        }

        // Enemies move and attack (in id order).
        if (_enemies.Count > 0)
        {
            foreach (var e in _enemies)
            {
                if (e.Hp <= 0) continue;
                var def = Content.Enemies[e.Type];
                if (e.Cooldown > 0) e.Cooldown--;

                // The nearest ship within 15 cells, otherwise the machine with the highest signature in the enemy's system.
                Player? ship = null;
                long best = 15L * Units.CellUnits * 15 * Units.CellUnits;
                foreach (var p in _players.Values)
                {
                    if (p.RespawnRemaining > 0) continue;
                    long dx = p.X - e.X, dy = p.Y - e.Y, dz = p.Z - e.Z;
                    long d = dx * dx + dy * dy + dz * dz;
                    if (d <= best) { best = d; ship = p; }
                }

                Machine? machine = ship is null ? PickTargetMachine(e.System) : null;
                if (ship is null && machine is null) continue;

                long tx = ship?.X ?? machine!.X * (long)Units.CellUnits + Units.CellUnits / 2;
                long ty = ship?.Y ?? machine!.Y * (long)Units.CellUnits + Units.CellUnits / 2;
                long tz = ship?.Z ?? machine!.Z * (long)Units.CellUnits + Units.CellUnits / 2;
                long rx = tx - e.X, ry = ty - e.Y, rz = tz - e.Z;
                long dist2 = rx * rx + ry * ry + rz * rz;
                long range = (long)def.RangeCells * Units.CellUnits;

                if (dist2 > range * range)
                {
                    long dist = IntMath.ISqrt(dist2);
                    long step = Math.Min(def.Speed, dist - range);
                    e.X += (int)(rx * step / dist);
                    e.Y += (int)(ry * step / dist);
                    e.Z += (int)(rz * step / dist);
                }
                else if (e.Cooldown == 0)
                {
                    e.Cooldown = def.IntervalTicks;
                    _shots.Add(new ShotEvent(ShotKind.Enemy, e.X, e.Y, e.Z, (int)tx, (int)ty, (int)tz));
                    if (ship is not null) DamageShip(ship, def.Damage);
                    else DamageMachine(machine!, def.Damage);
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
            if (m.TurretCooldown > 0 || m.In[turret.AmmoItem] <= 0) continue;

            long cx = m.X * (long)Units.CellUnits + Units.CellUnits / 2, cy = m.Y * (long)Units.CellUnits + Units.CellUnits / 2,
                cz = m.Z * (long)Units.CellUnits + Units.CellUnits / 2;
            var target = NearestEnemy(cx, cy, cz, (long)turret.RangeCells * Units.CellUnits);
            if (target is null) continue;

            target.Hp -= turret.Damage;
            m.In[turret.AmmoItem]--;
            MachineInputsConsumed++;
            AmmoFired++;
            m.TurretCooldown = turret.IntervalTicks;
            _shots.Add(new ShotEvent(ShotKind.Turret, (int)cx, (int)cy, (int)cz, target.X, target.Y, target.Z));
        }

        // Ship weapons (player id order).
        foreach (var p in _players.Values)
        {
            if (!p.Armed || p.RespawnRemaining > 0 || p.WeaponCooldown > 0) continue;
            var target = NearestEnemy(p.X, p.Y, p.Z, (long)rules.ShipWeaponRangeCells * Units.CellUnits);
            if (target is null) continue;
            target.Hp -= rules.ShipWeaponDamage;
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
        _destroyedMachines.Add(new DestroyedMachine(m.Id, m.X, m.Y, m.Z));
        MachinesLost++;
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
        p.X = anchor.X * Units.CellUnits;
        p.Y = anchor.Y * Units.CellUnits;
        p.Z = anchor.Z * Units.CellUnits;
        p.MiningDeposit = -1;
        p.MineProgress = 0;
        p.JumpTarget = -1;
        p.JumpRemaining = 0;
        ShipsLost++;
        StructureVersion++;
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

        int spawned = 0;
        foreach (var spec in Content.Waves)
        {
            int count = spec.Base + spec.PerWave * t.Wave;
            for (int k = 0; k < count; k++)
            {
                if (_enemies.Count >= rules.MaxEnemies) break;
                var def = Content.Enemies[spec.Enemy];
                _enemies.Add(new Enemy
                {
                    Id = _nextEnemyId++, Type = spec.Enemy, System = system, Hp = def.Hp,
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
        ShipsLost = other.ShipsLost;
        AmmoFired = other.AmmoFired;
        _destroyedMachines.Clear();
        _destroyedMachines.AddRange(other._destroyedMachines);
        _shots.Clear();
    }

    private void MixThreat(Action<long> mix)
    {
        mix(WavesSpawned); mix(EnemiesSpawned); mix(EnemiesKilled); mix(MachinesLost); mix(ShipsLost); mix(AmmoFired); mix(_nextEnemyId);
        foreach (var t in _threat) { mix(t.Alert); mix(t.Wave); mix(t.Warning); }
        mix(_enemies.Count);
        foreach (var e in _enemies) { mix(e.Id); mix(e.Type); mix(e.System); mix(e.X); mix(e.Y); mix(e.Z); mix(e.Hp); mix(e.Cooldown); }
    }

    private void ValidateThreat()
    {
        static void Fail(string msg) => throw new InvalidOperationException(msg);

        if (!ThreatActive)
        {
            if (_enemies.Count != 0) Fail("enemies exist while threats are inactive");
            if (WavesSpawned != 0 || MachinesLost != 0) Fail("threat counters changed while threats are inactive");
            return;
        }

        var rules = Content!.Threat!;
        if (_threat.Length != Galaxy!.Systems.Count) Fail("threat state has the wrong size");
        for (int s = 0; s < _threat.Length; s++)
        {
            var t = _threat[s];
            if (t.Alert < 0 || t.Alert > rules.AlertMax) Fail($"system {s} alert {t.Alert} out of range");
            if (t.Warning < -1 || t.Warning > rules.WarningTicks) Fail($"system {s} warning {t.Warning} out of range");
            if (t.Wave < 0) Fail($"system {s} wave count is negative");
        }

        int lastId = -1;
        foreach (var e in _enemies)
        {
            if (e.Id <= lastId) Fail("enemy ids are not increasing");
            lastId = e.Id;
            if (e.Type < 0 || e.Type >= Content.Enemies.Count) Fail($"enemy {e.Id} has an unknown type");
            else if (e.Hp < 1 || e.Hp > Content.Enemies[e.Type].Hp) Fail($"enemy {e.Id} hit points {e.Hp} out of range");
            if (!IsSystem(e.System)) Fail($"enemy {e.Id} is in an unknown system");
            if (Math.Abs(e.X) > PlayerRules.WorldLimitUnits * 2L || Math.Abs(e.Y) > PlayerRules.WorldLimitUnits * 2L || Math.Abs(e.Z) > PlayerRules.WorldLimitUnits * 2L)
                Fail($"enemy {e.Id} is far outside the world");
        }
        if (_enemies.Count > rules.MaxEnemies) Fail("more enemies than the cap");
        if (_nextEnemyId < _enemies.Count) Fail("enemy id counter is behind");
        if (EnemiesSpawned < EnemiesKilled + _enemies.Count) Fail("enemy accounting broken");

        foreach (var p in _players.Values)
        {
            if (p.Hull < 0 || p.Hull > rules.ShipHull) Fail($"player {p.Id} hull {p.Hull} out of range");
            if (p.RespawnRemaining < 0 || p.RespawnRemaining > rules.RespawnTicks) Fail($"player {p.Id} respawn time out of range");
            if ((p.RespawnRemaining > 0) != (p.Hull == 0)) Fail($"player {p.Id} hull and respawn disagree");
            if (p.WeaponCooldown < 0 || p.WeaponCooldown > rules.ShipWeaponIntervalTicks) Fail($"player {p.Id} weapon cooldown out of range");
        }
    }
}
