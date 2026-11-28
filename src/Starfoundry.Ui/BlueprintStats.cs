using Starfoundry.Sim;

namespace Starfoundry.Ui;

/// <summary>The research bonuses and the inertia setting the stats of a ship depend on.</summary>
public readonly record struct StatBonuses(int EngineSpeedPercent = 0, int EngineAccelPercent = 0, int HoldPercent = 0, ShipInertia Inertia = ShipInertia.Full);

/// <summary>What a module set adds up to, for the stats panel of the blueprint editor.</summary>
/// <param name="Mass">The summed mass of the modules (<c>ship_handling.json</c>).</param>
/// <param name="Turn">The turn rate and acceleration of a ship of this mass (heavier turns slower).</param>
/// <param name="ThrustPercent">The summed engine thrust in percent of one engine's base speed (capped like the simulation's).</param>
/// <param name="PowerOutput">Power the reactors give.</param>
/// <param name="PowerUse">The summed power draw of every module that draws (the peak, when everything runs).</param>
/// <param name="DefencePercent">The summed defence bonus of the defence modules.</param>
public sealed record ShipStats(
    int Modules, float Mass, float ReferenceMass, TurnStats Turn, int Engines, double TopCellsPerSecond, double SecondsToTop, int ThrustPercent,
    int PowerOutput, int PowerUse, int HoldCapacity, int Lasers, int LaserPercent, int Guns, int GunPercent, int DefencePercent, int Shields, int HullHp)
{
    public int PowerBalance => PowerOutput - PowerUse;

    /// <summary>The size of the design in metres along x, y and z (the bounding box of its module cells; zero when the cells are not known).</summary>
    public double SizeXMetres { get; init; }
    public double SizeYMetres { get; init; }
    public double SizeZMetres { get; init; }

    /// <summary>The longest side of the design in metres.</summary>
    public double LengthMetres => Math.Max(SizeXMetres, Math.Max(SizeYMetres, SizeZMetres));

    /// <summary>The acceleration in cells per second squared (m/s2) to the top speed (zero without engines).</summary>
    public double AccelCellsPerSecond2 => SecondsToTop > 0 ? TopCellsPerSecond / SecondsToTop : 0.0;

    /// <summary>Thrust per unit of mass: the number that says how lively the ship is (zero without engines).</summary>
    public double ThrustToMass => Mass > 0f ? ThrustPercent / 100.0 / Mass : 0.0;

    /// <summary>The stats of a design given by its module cells: the same numbers as the list overload plus the size in metres (ADR 0052).</summary>
    public static ShipStats Compute(IReadOnlyDictionary<(int Mx, int My, int Mz), ModuleKind> cells, ContentDb content, ShipHandling handling, StatBonuses bonuses)
    {
        var stats = Compute(cells.Values, content, handling, bonuses);
        if (cells.Count == 0) return stats;
        int x0 = cells.Keys.Min(k => k.Mx), x1 = cells.Keys.Max(k => k.Mx), y0 = cells.Keys.Min(k => k.My), y1 = cells.Keys.Max(k => k.My), z0 = cells.Keys.Min(k => k.Mz), z1 = cells.Keys.Max(k => k.Mz);
        return stats with
        {
            SizeXMetres = Physical.Metres((x1 - x0 + 1) * StationGrid.ModuleX),
            SizeYMetres = Physical.Metres((y1 - y0 + 1) * StationGrid.ModuleY),
            SizeZMetres = Physical.Metres((z1 - z0 + 1) * StationGrid.ModuleZ),
        };
    }

    public static ShipStats Compute(IEnumerable<ModuleKind> modules, ContentDb content, ShipHandling handling, StatBonuses bonuses)
    {
        var list = modules.ToList();
        float mass = handling.MassOf(list);
        var turn = handling.StatsFor(list, bonuses.EngineSpeedPercent, bonuses.EngineAccelPercent, bonuses.Inertia);
        var flight = ShipDesignEditor.FlightOf(list, content, bonuses.EngineSpeedPercent, bonuses.EngineAccelPercent);
        int thrust = 0, output = 0, use = 0, hold = 0, lasers = 0, lp = 0, guns = 0, gp = 0, defence = 0, shields = 0, hp = 0;
        if (content.Structures is { } kinds)
            foreach (var k in list)
            {
                if (k == ModuleKind.Engine) thrust += kinds.EngineSpeedPercent(k);
                output += kinds.PowerOutput(k);
                use += kinds.ModulePowerUse(k);
                hold += kinds.HoldCapacity(k);
                defence += kinds.DefenceBonusPercent(k);
                hp += kinds.ModuleHp(k);
                if (k == ModuleKind.Shield) shields++;
                if (ModuleKindIds.IsMiningLaser(k)) { lasers++; lp += kinds.HardpointPercent(k); }
                else if (ModuleKindIds.IsGun(k)) { guns++; gp += kinds.HardpointPercent(k); }
            }
        hold = hold * (100 + bonuses.HoldPercent) / 100;
        thrust = Math.Min(thrust, StructureFlight.MaxSpeedPercent);
        return new ShipStats(list.Count, mass, handling.ReferenceMass, turn, flight?.Engines ?? 0, flight?.TopCellsPerSecond ?? 0.0, flight?.SecondsToTop ?? 0.0, thrust,
            output, use, hold, lasers, lp, guns, gp, defence, shields, hp);
    }
}

/// <summary>One row of the stats panel: a label key, the value now, the value of the structure, and whether higher is better (for the colour of the delta).</summary>
/// <param name="Quantity">The physical quantity of the row (<see cref="Quantity.None"/> for a plain number) and <paramref name="ToSi"/> the factor from the row's value to SI base units (ADR 0052).</param>
public readonly record struct StatRow(string LabelKey, string Icon, double After, double Before, string Format, bool HigherIsBetter, Quantity Quantity = Quantity.None, double ToSi = 1.0)
{
    public double Delta => After - Before;

    /// <summary>1 when the change is for the better, -1 when for the worse, 0 when there is none.</summary>
    public int Sign => Math.Abs(Delta) < 1e-9 ? 0 : (Delta > 0) == HigherIsBetter ? 1 : -1;

    public string Text(System.Globalization.CultureInfo culture) =>
        Quantity == Quantity.None ? After.ToString(Format, culture) : (After < 0 ? "-" : "") + Metric.Format(Quantity, Math.Abs(After) * ToSi, culture);

    public string DeltaText(System.Globalization.CultureInfo culture)
    {
        if (Sign == 0) return "";
        if (Quantity == Quantity.None) return (Delta > 0 ? "+" : "") + Delta.ToString(Format, culture);
        return Metric.Signed(Quantity, Delta * ToSi, culture);
    }
}

public static class StatRows
{
    /// <summary>The rows of the panel in display order, the draft against the structure.</summary>
    public static IReadOnlyList<StatRow> Build(ShipStats after, ShipStats before, StructureKind kind = StructureKind.Ship) => kind == StructureKind.Station ? new[]
    {
        new StatRow("bp.stat.power", "status:power", after.PowerBalance, before.PowerBalance, "0", true, Quantity.Power, Physical.Watts(1)),
        new StatRow("bp.stat.cargo", "status:cargo", after.HoldCapacity, before.HoldCapacity, "0", true, Quantity.HoldItems),
        new StatRow("bp.stat.defence", "status:shield", after.DefencePercent, before.DefencePercent, "0", true),
        new StatRow("bp.stat.shields", "status:shieldOk", after.Shields, before.Shields, "0", true),
        new StatRow("bp.stat.hull", "status:hull", after.HullHp, before.HullHp, "0", true),
    } : new[]
    {
        new StatRow("bp.stat.power", "status:power", after.PowerBalance, before.PowerBalance, "0", true, Quantity.Power, Physical.Watts(1)),
        new StatRow("bp.stat.mass", "tool:hold", after.Mass, before.Mass, "0.0", false, Quantity.Mass, Physical.RelativeMassKg(1)),
        new StatRow("bp.stat.length", "tool:move", after.LengthMetres, before.LengthMetres, "0", false, Quantity.Length),
        new StatRow("bp.stat.turn", "tool:rotate", after.Turn.RateDegPerSecond, before.Turn.RateDegPerSecond, "0", true),
        new StatRow("bp.stat.thrust", "status:speed", after.TopCellsPerSecond, before.TopCellsPerSecond, "0.0", true, Quantity.Speed, Physical.MetresPerSecond(1)),
        new StatRow("bp.stat.accel", "status:speed", after.AccelCellsPerSecond2, before.AccelCellsPerSecond2, "0.0", true, Quantity.Acceleration, Physical.Metres(1)),
        new StatRow("bp.stat.cargo", "status:cargo", after.HoldCapacity, before.HoldCapacity, "0", true, Quantity.HoldItems),
        new StatRow("bp.stat.mining", "tool:mine", after.LaserPercent, before.LaserPercent, "0", true),
        new StatRow("bp.stat.guns", "tool:fireGuns", after.GunPercent, before.GunPercent, "0", true),
        new StatRow("bp.stat.defence", "status:shield", after.DefencePercent, before.DefencePercent, "0", true),
        new StatRow("bp.stat.shields", "status:shieldOk", after.Shields, before.Shields, "0", true),
        new StatRow("bp.stat.hull", "status:hull", after.HullHp, before.HullHp, "0", true),
    };
}
