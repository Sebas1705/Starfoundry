namespace Starfoundry.Sim;

public sealed partial class BeltNetwork
{
    // ---- antennas (ADR 0042) -------------------------------------------------------------------------

    /// <summary>The powered antenna kinds of a structure, one entry per module (none while the structure lacks power).</summary>
    private IEnumerable<ModuleKind> PoweredAntennas(Structure s) =>
        s.Satisfaction < ShieldSensorRules.MinSatisfaction ? Enumerable.Empty<ModuleKind>() : s.ModuleMap.Values.Where(ModuleKindIds.IsAntenna);

    /// <summary>
    /// How far the scanner of the player's ship reaches, in cells: the base range, or what the fitted antennas give when that is more. The reaches of the
    /// antennas stack with diminishing returns (<see cref="ShieldSensorRules.Stack"/>) and take the sensor research bonus. A world with
    /// <see cref="WorldRules.LegacyArms"/> uses the base range.
    /// </summary>
    public long ScanRangeCells(int player)
    {
        long baseRange = Galaxy?.Wide == true ? ExplorationRules.WideScanRangeCells : ExplorationRules.ScanRangeCells;
        if (Rules.LegacyArms || !StructureRules || !_players.TryGetValue(player, out var p) || !_structures.TryGetValue(p.ShipStructure, out var s)) return baseRange;
        return Math.Max(baseRange, ShieldSensorRules.Stack(PoweredAntennas(s).Select(k => SensorRangeCells(k))));
    }

    /// <summary>
    /// How far the navigation display of the player's ship sees, in cells: the base display range plus the stacked reach of the fitted antennas (the best in
    /// full, each further one at half the last). <see cref="long.MaxValue"/> in a world with <see cref="WorldRules.LegacyArms"/> (the display lists everything).
    /// </summary>
    public long NavRangeCells(int player)
    {
        if (Rules.LegacyArms || !StructureRules) return long.MaxValue;
        long bonus = 0;
        if (_players.TryGetValue(player, out var p) && _structures.TryGetValue(p.ShipStructure, out var s) && Content?.Structures is { } kinds)
            bonus = ShieldSensorRules.Stack(PoweredAntennas(s).Select(k => (long)kinds.NavRangeCells(k)));
        long range = ShieldSensorRules.NavBaseRangeCells + bonus;
        return _scale is { } sc ? range * sc.NavRangePercent / 100 : range; // the world scale widens the display: 400 m becomes 150 km (ADR 0053)
    }
}
