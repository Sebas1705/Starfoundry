namespace Starfoundry.Sim;

/// <summary>
/// The extra deposits of a world whose <see cref="WorldRules.ResourceDensityPercent"/> is above 100 (ADR 0036). They are appended after every deposit
/// the base generator made, so the ids, positions and amounts of the base galaxy do not move and a density of 100 is the old galaxy exactly. Each system
/// has its own random stream (galaxy seed and system id), so nothing else disturbs it. Two kinds of extra:
/// more veins of what the system's archetype already holds (a share of its expected count per percent above 100, those at home bigger), and small
/// ore-bearing rocks beside the solid rocks of belts and clusters. Every extra keeps <see cref="ClearCells"/> from the other deposits of its system, so the
/// ground near the starting vein stays free for a station.
/// </summary>
internal static class ResourceGenerator
{
    private const int Cell = Units.CellUnits;

    /// <summary>Extra veins at home are this many percent bigger.</summary>
    public const int HomeRichnessPercent = 130;

    /// <summary>An ore-bearing rock holds this fraction (in percent) of a vein of its kind.</summary>
    public const int OreRockPercent = 33;

    /// <summary>Chance in thousandths per solid rock, per point of density above 100, of an ore-bearing rock beside it (so 140 gives 4 percent: about one per system).</summary>
    public const int OreRockPermillePerPoint = 1;

    /// <summary>An extra deposit keeps at least this many cells from every other deposit of its system.</summary>
    public const int ClearCells = 44;

    private const int Tries = 16;

    public static void AddExtras(ContentDb content, WorldGenRules wide, ulong seed, StarSystem[] systems, int[] archetype, int[] depth,
        IReadOnlyList<GeneratedObstacle> obstacles, List<GeneratedDeposit> deposits, int densityPercent)
    {
        if (densityPercent <= 100) return;
        int over = densityPercent - 100;
        for (int i = 0; i < systems.Length; i++)
        {
            var arch = content.Archetypes[archetype[i]];
            if (arch.Deposits.Length == 0) continue;
            var rng = new SplitMix64(seed ^ 0xDE45_17E5_0D0E_0036UL ^ ((ulong)(i + 1) * 0xA24B_AED4_963E_E407UL));
            bool home = i == 0;
            int star = systems[i].Star;
            var sys = systems[i];

            // The deposits of this system so far, in cells, and a check against them and the anchor.
            var taken = new List<(long X, long Y, long Z)>();
            for (int k = 0; k < deposits.Count; k++)
                if (deposits[k].System == i) taken.Add((deposits[k].X, deposits[k].Y, deposits[k].Z));
            bool Clear(long x, long y, long z)
            {
                long need = (long)ClearCells * ClearCells;
                if (Dist2(x, y, z, sys.X, sys.Y, sys.Z) < need) return false;
                foreach (var t in taken) if (Dist2(x, y, z, t.X, t.Y, t.Z) < need) return false;
                return true;
            }

            // More veins of the kinds the system already has.
            foreach (var spec in arch.Deposits)
            {
                int expected100 = (spec.CountMin + spec.CountMax) * 50; // the mean count, times 100
                int extra100 = expected100 * over / 100;
                int extra = extra100 / 100 + (rng.Next(0, 99) < extra100 % 100 ? 1 : 0);
                for (int k = 0; k < extra; k++)
                {
                    int amount = rng.Next(spec.AmountMin, spec.AmountMax);
                    var (dLo, dHi) = arch.HasWide ? (arch.WideDistanceMin, arch.WideDistanceMax) : (arch.DistanceMin * 4, arch.DistanceMax * 4);
                    int dens = spec.HasDensity ? rng.Next(spec.DensityMin, spec.DensityMax) : DepositRules.DefaultDensity;
                    for (int attempt = 0; attempt < Tries; attempt++)
                    {
                        int d = rng.Next(dLo, dHi);
                        int ox = rng.Next(-d, d);
                        int oz = (int)IntMath.ISqrt((long)d * d - (long)ox * ox) * (rng.Next(0, 1) == 0 ? -1 : 1);
                        int oy = rng.Next(-d / 4, d / 4);
                        long x = sys.X + ox, y = sys.Y + oy, z = sys.Z + oz;
                        if (!Clear(x, y, z)) continue;
                        long pct;
                        if (home) pct = HomeRichnessPercent;
                        else
                        {
                            pct = (long)wide.StarTypes[star].RichnessPercent * (100 + wide.DepthRichnessPercent * depth[i]) / 100;
                            pct = pct * (100 + (dHi > dLo ? wide.OuterRichnessPercent * (d - dLo) / (dHi - dLo) : 0)) / 100;
                        }
                        deposits.Add(new GeneratedDeposit(i, spec.Item, (int)Math.Max(1, (long)amount * pct / 100), (int)x, (int)y, (int)z, dens));
                        taken.Add((x, y, z));
                        break;
                    }
                }
            }

            // Small ore-bearing rocks beside the solid rocks of belts and clusters.
            int chance = Math.Min(200, over * OreRockPermillePerPoint);
            for (int id = 0; id < obstacles.Count; id++)
            {
                var o = obstacles[id];
                if (o.System != i || wide.Kinds[o.Kind].Behaviour != ObstacleBehaviour.Solid) continue;
                if (rng.Next(0, 999) >= chance) continue;
                var spec = arch.Deposits[rng.Next(0, arch.Deposits.Length - 1)];
                int amount = Math.Max(12, rng.Next(spec.AmountMin, spec.AmountMax) * OreRockPercent / 100);
                int dens = spec.HasDensity ? rng.Next(spec.DensityMin, spec.DensityMax) : DepositRules.DefaultDensity;
                int ring = Math.Max(3, o.Radius / Cell + 4); // cells from the rock's centre: just outside its surface
                for (int attempt = 0; attempt < Tries; attempt++)
                {
                    int ox = rng.Next(-ring, ring);
                    int oz = (int)IntMath.ISqrt((long)ring * ring - (long)ox * ox) * (rng.Next(0, 1) == 0 ? -1 : 1);
                    long x = o.X / Cell + ox, y = o.Y / Cell, z = o.Z / Cell + oz;
                    if (!Clear(x, y, z)) continue;
                    deposits.Add(new GeneratedDeposit(i, spec.Item, amount, (int)x, (int)y, (int)z, dens));
                    taken.Add((x, y, z));
                    break;
                }
            }
        }
    }

    private static long Dist2(long ax, long ay, long az, long bx, long by, long bz)
    {
        long dx = ax - bx, dy = ay - by, dz = az - bz;
        return dx * dx + dy * dy + dz * dz;
    }
}
