namespace Starfoundry.Sim;

/// <summary>Fixed simulation constants. Every gameplay value is an integer (see ADR 0002).</summary>
public static class Units
{
    /// <summary>Simulation ticks per second.</summary>
    public const int TicksPerSecond = 30;

    /// <summary>Sub-units per grid cell. Item positions on a belt are measured in sub-units.</summary>
    public const int CellUnits = 240;

    /// <summary>Minimum distance between two items on a belt, in sub-units (4 items per cell).</summary>
    public const int ItemSpacing = 60;
}

/// <summary>Belt speed tiers. Throughput is speed * TicksPerSecond / ItemSpacing items per second.</summary>
public enum BeltTier
{
    /// <summary>4 items per second.</summary>
    Mk1 = 0,

    /// <summary>10 items per second.</summary>
    Mk2 = 1,

    /// <summary>24 items per second.</summary>
    Mk3 = 2,
}

public static class BeltTiers
{
    /// <summary>Movement per tick in sub-units. Must stay below <see cref="Units.ItemSpacing"/>.</summary>
    public static int SpeedPerTick(BeltTier tier) => tier switch
    {
        BeltTier.Mk1 => 8,
        BeltTier.Mk2 => 20,
        BeltTier.Mk3 => 48,
        _ => throw new ArgumentOutOfRangeException(nameof(tier)),
    };

    /// <summary>Theoretical saturated throughput in items per second.</summary>
    public static double ItemsPerSecond(BeltTier tier) =>
        (double)SpeedPerTick(tier) * Units.TicksPerSecond / Units.ItemSpacing;
}
