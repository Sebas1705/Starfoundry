namespace Starfoundry.Sim;

/// <summary>Fixed rules of shields and sensors. Part of the simulation, so changing them changes states.</summary>
public static class ShieldSensorRules
{
    /// <summary>A shield or sensor only works while the satisfaction of its structure is at least this (per mille).</summary>
    public const int MinSatisfaction = PowerRules.TurretMinSatisfaction;

    /// <summary>Sensors look for hidden deposits every this many ticks (once a second), not every tick.</summary>
    public const int SensorIntervalTicks = 30;
}

/// <summary>Enemies of one type in a forecast wave.</summary>
public readonly record struct WaveEnemyCount(int EnemyType, int Count);

/// <summary>
/// What a sensor tells about the next wave of the system its structure is in.
/// </summary>
/// <param name="Wave">Number of the next wave, counted from 1.</param>
/// <param name="Enemies">Enemy count per type (types that do not appear are left out), in content order.</param>
/// <param name="TicksToArrival">Ticks until the wave spawns: exact once the warning has started, an estimate from the current alert
/// before that, or -1 when the system is quiet and no wave is coming.</param>
/// <param name="WarningActive">True once the warning countdown is running (the arrival time is exact).</param>
public readonly record struct WaveForecast(int System, int Wave, IReadOnlyList<WaveEnemyCount> Enemies, int TotalEnemies, int TicksToArrival, bool WarningActive);

public sealed partial class BeltNetwork
{
    // ---- shields -----------------------------------------------------------------------------------

    /// <summary>
    /// Percent of the damage a hit on the module at <paramref name="key"/> loses to the shields of its structure: the first powered
    /// shield absorbs <c>shield_percent</c>, every further one adds <c>shield_stack_percent</c>, capped at <c>shield_max_percent</c>.
    /// A shield never protects itself, so a lone shield takes full damage. Shields below the minimum satisfaction do nothing.
    /// </summary>
    private int ShieldPercentFor(Structure s, (int Mx, int My, int Mz) key)
    {
        if (!StructureRules || Content?.Structures is not { } kinds || s.Satisfaction < ShieldSensorRules.MinSatisfaction) return 0;
        int others = 0;
        foreach (var (k, kind) in s.ModuleMap)
            if (kind == ModuleKind.Shield && k != key) others++;
        if (others == 0) return 0;
        var def = kinds.Get(ModuleKind.Shield);
        int points = _bonus[(int)BonusKind.ShieldAbsorb]; // field theory research: percentage points on top
        return Math.Min(def.ShieldMaxPercent + points, def.ShieldPercent + points + def.ShieldStackPercent * (others - 1));
    }

    /// <summary>The damage that gets through the shields: never below 1 for a positive hit.</summary>
    private int AfterShields(Structure s, (int Mx, int My, int Mz) key, int damage)
    {
        int percent = ShieldPercentFor(s, key);
        // The shields of the station a ship is docked at cover it too, with the best of the two (ADR 0029).
        if (s.DockedTo >= 0 && _structures.TryGetValue(s.DockedTo, out var host)) percent = Math.Max(percent, ShieldPercentFor(host, (int.MinValue, int.MinValue, int.MinValue)));
        if (percent == 0) return damage;
        return Math.Max(1, damage - (int)((long)damage * percent / 100));
    }

    /// <summary>The percent of damage the shields of a structure absorb for an ordinary (non-shield) module. 0 without a powered shield.</summary>
    public int ShieldAbsorbPercent(int structure) =>
        _structures.TryGetValue(structure, out var s) ? ShieldPercentFor(s, (int.MinValue, int.MinValue, int.MinValue)) : 0;

    // ---- sensors -----------------------------------------------------------------------------------

    /// <summary>Reveal range of a sensor module in cells, with the research bonus (bio-sensors, quantum sensors).</summary>
    public long SensorRangeCells(ModuleKind kind) => (long)(Content?.Structures?.SensorRangeCells(kind) ?? 0) * (100 + _bonus[(int)BonusKind.SensorRange]) / 100;

    /// <summary>True when the structure has a sensor module and enough power for it to work.</summary>
    public bool SensorActive(int structure) =>
        StructureRules && _structures.TryGetValue(structure, out var s) && s.Satisfaction >= ShieldSensorRules.MinSatisfaction
        && s.ModuleMap.Values.Contains(ModuleKind.Sensor);

    /// <summary>
    /// Once a second every powered structure's sensors reveal the hidden deposits within <c>sensor_range_cells</c> of the sensor module
    /// (structure worlds only), the same reveal the scanner does but without a command or cooldown.
    /// </summary>
    private void StepSensors()
    {
        if (!StructureRules || _structures.Count == 0 || Content?.Structures is not { } kinds || Tick % ShieldSensorRules.SensorIntervalTicks != 0) return;
        long cu = Units.CellUnits;
        bool any = false;
        foreach (var s in _structures.Values)
        {
            if (s.Satisfaction < ShieldSensorRules.MinSatisfaction) continue;
            foreach (var (key, kind) in s.ModuleMap)
            {
                if (kind != ModuleKind.Sensor) continue;
                var c = ModuleWorldCentre(s.Id, key.Mx, key.My, key.Mz);
                long range = SensorRangeCells(kind) * cu;
                foreach (var d in _deposits)
                {
                    if (d.Revealed || d.Free) continue;
                    long dx = d.X * cu + cu / 2 - c.X, dy = d.Y * cu + cu / 2 - c.Y, dz = d.Z * cu + cu / 2 - c.Z;
                    if (dx * dx + dy * dy + dz * dz > range * range) continue;
                    d.Revealed = true;
                    any = true;
                }
            }
        }
        if (any) StructureVersion++;
    }

    /// <summary>
    /// The next wave of the system the structure is in, for the HUD: its number, its enemies by type and when it spawns. Null without a
    /// powered sensor in the structure, in worlds without threats and for unknown structures. The composition follows the wave table
    /// the way <c>SpawnWave</c> reads it (including the cap on live enemies).
    /// </summary>
    public WaveForecast? NextWaveInfo(int structure)
    {
        if (!ThreatActive || !SensorActive(structure) || Content?.Threat is not { } rules) return null;
        var s = _structures[structure];
        var first = s.ModuleMap.Keys.First();
        var c = ModuleWorldCentre(structure, first.Mx, first.My, first.Mz);
        int system = Galaxy!.SystemAt(c.X, c.Y, c.Z);
        if (system < 0 || !IsSystem(system)) return null;
        var t = _threat[system];

        var counts = new SortedDictionary<int, int>();
        int total = 0;
        int room = Math.Max(0, rules.MaxEnemies - _enemies.Count);
        foreach (var spec in Content.Waves)
        {
            int n = WorldRules.ScaleMin1(t.Wave < spec.FromWave ? 0 : spec.Base + spec.PerWave * (t.Wave - spec.FromWave), Rules.WaveSizePercent); // same scaling as SpawnWave
            n = Math.Min(n, room - total);
            if (n <= 0) continue;
            counts[spec.Enemy] = counts.GetValueOrDefault(spec.Enemy) + n;
            total += n;
        }

        int ticks;
        if (t.Warning > 0) ticks = t.Warning;
        else
        {
            int signature = SystemSignature(system);
            int rate = signature - rules.SignatureThreshold;
            if (rate <= 0) ticks = -1;
            else
            {
                bool graceOn = t.Wave == 0 && rules.FirstWaveGraceTicks > 0;
                int grace = graceOn ? Math.Max(0, rules.FirstWaveGraceTicks - t.Grace) : 0;
                int alertTicks = Math.Max(0, (AlertMaxNow - t.Alert + rate - 1) / rate);
                ticks = Math.Max(grace, alertTicks) + rules.WarningTicks;
            }
        }
        return new WaveForecast(system, t.Wave + 1, counts.Select(p => new WaveEnemyCount(p.Key, p.Value)).ToArray(), total, ticks, t.Warning > 0);
    }
}
