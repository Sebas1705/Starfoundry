namespace Starfoundry.Sim;

/// <summary>A resource asteroid of the infinite field (ADR 0040). Positions are in cells, the radius in units.</summary>
/// <param name="Index">The rock's place in its sector's draw; with the sector it is the rock's stable identity.</param>
/// <param name="Mass">Thousandths of the reference rock; with the density it gives the amount.</param>
/// <param name="Cluster">The id of the cluster parent the rock belongs to (ADR 0046), or -1 for a stray and for the uniform field.</param>
public readonly record struct FieldRock(int Index, int Item, int X, int Y, int Z, int Mass, int Density, int Radius, int Amount, long Cluster = -1);

/// <summary>
/// The pure generator of the infinite rock field (ADR 0040): the rocks of one cubic sector are a function of the world seed, the content and the
/// sector coordinates only, so the same sector gives the same rocks whatever was visited before, whenever it is generated. Integer arithmetic only.
/// </summary>
public static class SectorField
{
    private const int BitsPerAxis = 21;
    private const int Bias = 1 << (BitsPerAxis - 1);
    private const ulong Salt = 0xF1E1_D0C4_5EC7_0040UL;

    /// <summary>The key of a sector: its three coordinates packed in one long (21 bits each, so about a million sectors either way).</summary>
    public static long Key(int cx, int cy, int cz) =>
        ((long)(cx + Bias) << (2 * BitsPerAxis)) | ((long)(cy + Bias) << BitsPerAxis) | (long)(cz + Bias);

    /// <summary>The coordinates of a sector key.</summary>
    public static (int X, int Y, int Z) Unkey(long key)
    {
        const long mask = (1L << BitsPerAxis) - 1;
        return ((int)((key >> (2 * BitsPerAxis)) & mask) - Bias, (int)((key >> BitsPerAxis) & mask) - Bias, (int)(key & mask) - Bias);
    }

    /// <summary>The sector coordinate of a position in units.</summary>
    public static int SectorOf(long units, int sectorCells) => (int)IntMath.FloorDiv(units, (long)sectorCells * Units.CellUnits);

    /// <summary>The rocks of one sector, appended to <paramref name="into"/> in index order. <paramref name="densityPercent"/> is the world's resource density rule (100 is the base count).</summary>
    public static void Generate(FieldRules f, IReadOnlyList<StarSystem> systems, ulong seed, int cx, int cy, int cz, int densityPercent, List<FieldRock> into)
    {
        ulong s = new SplitMix64(seed ^ Salt ^ ((ulong)(uint)cx * 0x9E37_79B9_7F4A_7C15UL)).Next();
        s = new SplitMix64(s ^ ((ulong)(uint)cy * 0xC2B2_AE3D_27D4_EB4FUL)).Next();
        s = new SplitMix64(s ^ ((ulong)(uint)cz * 0x1656_67B1_9E37_79F9UL)).Next();
        var rng = new SplitMix64(s);

        int baseCount = rng.Next(f.RocksMin, f.RocksMax);
        long count100 = (long)baseCount * densityPercent;
        int count = (int)(count100 / 100) + (rng.Next(0, 99) < count100 % 100 ? 1 : 0);

        long sector = f.SectorCells;
        long minX = cx * sector + f.MarginCells, minY = cy * sector + f.MarginCells, minZ = cz * sector + f.MarginCells;
        int span = (int)(sector - 2L * f.MarginCells) - 1;
        long limit = f.WorldLimitCells - 100;
        long excl2 = (long)f.ExclusionCells * f.ExclusionCells;
        int first = into.Count;

        for (int k = 0; k < count; k++)
        {
            // The draws of a rock come in a fixed order whatever happens to its position, so a rejected rock never shifts the next one's look.
            int itemRoll = rng.Next(0, 999_999);
            int u = rng.Next(0, 1_000_000);
            int density = rng.Next(f.DensityMin, f.DensityMax);
            long val = 1_000_000;
            for (int b = 0; b < f.MassBias; b++) val = val * u / 1_000_000;
            int mass = (int)(f.MassMin + (long)(f.MassMax - f.MassMin) * val / 1_000_000);
            int radius = FieldRules.RadiusOfMass(mass);
            int reachCells = (int)(((long)radius * 160 / 100 + Units.CellUnits - 1) / Units.CellUnits);

            for (int attempt = 0; attempt < FieldRules.Tries; attempt++)
            {
                long x = minX + rng.Next(0, span), y = minY + rng.Next(0, span), z = minZ + rng.Next(0, span);
                if (Math.Abs(x) > limit || Math.Abs(y) > limit || Math.Abs(z) > limit) break;
                if (NearAnchor(systems, x, y, z, excl2)) continue;
                bool clear = true;
                for (int j = first; j < into.Count; j++)
                {
                    var o = into[j];
                    long need = reachCells + (long)((long)o.Radius * 160 / 100 + Units.CellUnits - 1) / Units.CellUnits + FieldRules.GapCells;
                    long dx = x - o.X, dy = y - o.Y, dz = z - o.Z;
                    if (dx * dx + dy * dy + dz * dz < need * need) { clear = false; break; }
                }
                if (!clear) continue;
                long fromHome = IntMath.ISqrt(x * x + y * y + z * z);
                int item = PickItem(f, itemRoll, fromHome);
                into.Add(new FieldRock(k, item, (int)x, (int)y, (int)z, mass, density, radius, f.AmountOf(mass, density)));
                break;
            }
        }
    }

    private static bool NearAnchor(IReadOnlyList<StarSystem> systems, long x, long y, long z, long excl2)
    {
        for (int i = 0; i < systems.Count; i++)
        {
            long dx = x - systems[i].X, dy = y - systems[i].Y, dz = z - systems[i].Z;
            if (dx * dx + dy * dy + dz * dz < excl2) return true;
        }
        return false;
    }

    internal static int PickItem(FieldRules f, int roll, long fromHomeCells)
    {
        long total = 0;
        foreach (var i in f.Items) if (i.MinDistanceCells <= fromHomeCells) total += i.Weight;
        long r = roll * total / 1_000_000;
        foreach (var i in f.Items)
        {
            if (i.MinDistanceCells > fromHomeCells) continue;
            if (r < i.Weight) return i.Item;
            r -= i.Weight;
        }
        return f.Items[0].Item;
    }
}
