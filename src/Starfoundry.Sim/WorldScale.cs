using System.Text.Json;

namespace Starfoundry.Sim;

/// <summary>
/// The cruise drive of the world scale (ADR 0053, stage 2 slice), the <c>cruise</c> object of <c>world.scale</c>. Speeds are in units per tick (240 units per metre, 30 ticks per second:
/// 8 units per tick is 1 m/s, so 24,000 is 3 km/s); the approach governor works on the same numbers, so it is exact integer maths.
/// </summary>
/// <param name="SpeedUnitsPerTick">Top cruise speed.</param>
/// <param name="SpinUpTicks">Ticks from rest to the top cruise speed.</param>
/// <param name="BrakeTicks">Ticks from the top cruise speed to rest.</param>
/// <param name="DropUnitsPerTick">Below this allowed speed the drive drops out to normal flight (the governor never lets cruise get this slow while it is engaged).</param>
/// <param name="ZoneSectors">A rock cluster is a zone out to its reach plus this many field sectors: cruise slows to boost speed at the edge.</param>
public sealed record CruiseRules(int SpeedUnitsPerTick, int SpinUpTicks, int BrakeTicks, int DropUnitsPerTick, int ZoneSectors)
{
    /// <summary>Acceleration of the spin-up, in thousandths of a unit per tick per tick.</summary>
    public long AccelMilli => (long)SpeedUnitsPerTick * 1000 / SpinUpTicks;

    /// <summary>Braking of the drive, in thousandths of a unit per tick per tick.</summary>
    public long BrakeMilli => (long)SpeedUnitsPerTick * 1000 / BrakeTicks;

    /// <summary>The deceleration the approach curve assumes: 70 percent of the drive's braking, so the ship can always follow it.</summary>
    public long CurveMilli => BrakeMilli * 70 / 100;

    internal void MixInto(Action<long> mix) { mix(-3503); mix(SpeedUnitsPerTick); mix(SpinUpTicks); mix(BrakeTicks); mix(DropUnitsPerTick); mix(ZoneSectors); }
}

/// <summary>
/// The world scale of ADR 0053, stage 2 slice: the optional <c>scale</c> object of the <c>world</c> section. A world that has it (and is not <see cref="WorldRules.LegacyWorldScale"/>) puts the
/// systems and the rock clusters kilometres apart with a void between them, moves the ore farther out, bounds the rock field to a radius around the system anchors, widens the navigation display
/// and gets the cruise drive. Every length is in cells (1 cell is 1 m). Content without it is hashed and played exactly as before.
/// </summary>
/// <param name="SpacingCells">System spacing of the wide galaxy (replaces <c>spacing_cells</c> of the world).</param>
/// <param name="SuperCells">Side of a cluster super-sector (replaces <c>super_cells</c> of the clusters), so one cluster in about a hundred kilometres.</param>
/// <param name="BackgroundPermille">Loose rocks per sector outside the clusters, in thousandths (replaces <c>background_permille</c>): 0 leaves the void empty and safe for cruise.</param>
/// <param name="OreDistancePercent">Multiplies every <c>min_distance_cells</c> of the field items (5000 is fifty times).</param>
/// <param name="FieldRadiusCells">Rocks exist only within this distance of a system anchor.</param>
/// <param name="NavRangePercent">Multiplies the navigation range of the ship, antennas included (37500 turns 400 m into 150 km).</param>
/// <param name="Cruise">The cruise drive.</param>
public sealed record WorldScaleRules(int SpacingCells, int SuperCells, int BackgroundPermille, int OreDistancePercent, int FieldRadiusCells, int NavRangePercent, CruiseRules Cruise)
{
    internal static WorldScaleRules Load(JsonElement s, int systemRadiusCells, FieldRules? field)
    {
        ContentDb.RequireObjectInternal(s, "scale", "spacing_cells", "super_cells", "background_permille", "ore_distance_percent", "field_radius_cells", "nav_range_percent", "cruise");
        int spacing = ContentDb.IntInternal(s, "spacing_cells", "scale", 2 * systemRadiusCells, 400_000);
        int super = ContentDb.IntInternal(s, "super_cells", "scale", 400, 400_000);
        if (field?.Clusters is not null && super < field.SectorCells * 2) throw new InvalidDataException("'super_cells' in scale must be at least two field sectors.");
        int fieldRadius = ContentDb.IntInternal(s, "field_radius_cells", "scale", 1000, 4_000_000);
        if (field is not null && fieldRadius > field.WorldLimitCells) throw new InvalidDataException("'field_radius_cells' in scale must be within the world limit.");
        var c = s.GetProperty("cruise");
        ContentDb.RequireObjectInternal(c, "cruise", "speed_units_per_tick", "spin_up_ticks", "brake_ticks", "drop_units_per_tick", "zone_sectors");
        int speed = ContentDb.IntInternal(c, "speed_units_per_tick", "cruise", 100, 100_000), spin = ContentDb.IntInternal(c, "spin_up_ticks", "cruise", 1, 6000), brake = ContentDb.IntInternal(c, "brake_ticks", "cruise", 1, 6000);
        int drop = ContentDb.IntInternal(c, "drop_units_per_tick", "cruise", 1, speed), zone = ContentDb.IntInternal(c, "zone_sectors", "cruise", 0, 20);
        return new WorldScaleRules(spacing, super, ContentDb.IntInternal(s, "background_permille", "scale", 0, 5000), ContentDb.IntInternal(s, "ore_distance_percent", "scale", 100, 100_000),
            fieldRadius, ContentDb.IntInternal(s, "nav_range_percent", "scale", 100, 1_000_000), new CruiseRules(speed, spin, brake, drop, zone));
    }

    /// <summary>The field of this world at the new scale: ore farther out, the clusters in a bigger super-sector with no loose rocks, and a radius around the anchors beyond which there is none.</summary>
    internal FieldRules Apply(FieldRules f)
    {
        var items = f.Items.Select(i => i with { MinDistanceCells = (int)Math.Min(1_000_000_000L, (long)i.MinDistanceCells * OreDistancePercent / 100) }).ToArray();
        return f with
        {
            Items = items, AnchorRadiusCells = FieldRadiusCells,
            Clusters = f.Clusters is { } c ? c with { SuperCells = SuperCells, BackgroundPermille = BackgroundPermille } : null,
        };
    }

    internal void MixInto(Action<long> mix)
    {
        mix(-3501);
        mix(SpacingCells); mix(SuperCells); mix(BackgroundPermille); mix(OreDistancePercent); mix(FieldRadiusCells); mix(NavRangePercent);
        Cruise.MixInto(mix);
    }
}
