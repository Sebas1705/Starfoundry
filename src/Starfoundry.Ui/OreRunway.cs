using Starfoundry.Sim;

namespace Starfoundry.Ui;

/// <summary>
/// How much ore the player has left to mine in the system they are in. The home deposits hold a couple of hours of
/// growing production, so the interface warns when an ore is running low and the player should scan and move on.
/// </summary>
public static class OreRunway
{
    /// <summary>The warning starts when this share (percent) or less of an ore is left in the system.</summary>
    public const int LowPercent = 25;

    /// <summary>
    /// Percent of the ore of <paramref name="item"/> still in the revealed deposits of the player's system (all revealed
    /// deposits in a world without a galaxy), or -1 when no such deposit is known.
    /// </summary>
    public static int PercentLeft(BeltNetwork net, int player, int item)
    {
        int system = net.CurrentSystem(player);
        long left = 0, initial = 0;
        for (int d = 0; d < net.DepositCount; d++)
        {
            if (net.DepositItem(d) != item || !net.IsDepositRevealed(d)) continue;
            if (net.Galaxy is { } g)
            {
                var (x, y, z) = net.DepositPosition(d);
                if (g.SystemAt((long)x * Units.CellUnits, (long)y * Units.CellUnits, (long)z * Units.CellUnits) != system) continue;
            }
            left += net.DepositRemaining(d);
            initial += net.DepositInitial(d);
        }
        return initial == 0 ? -1 : (int)(left * 100 / initial);
    }

    /// <summary>True when the ore is known and at or below the warning level.</summary>
    public static bool IsLow(int percentLeft) => percentLeft >= 0 && percentLeft <= LowPercent;
}
