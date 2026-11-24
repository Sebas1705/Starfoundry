namespace Starfoundry.Sim;

/// <summary>
/// The extra deposits of a world whose <see cref="WorldRules.ResourceDensityPercent"/> is above 100 (ADR 0036). They are appended after every deposit
/// the base generator made, so the ids, positions and amounts of the base galaxy do not move and a density of 100 is the old galaxy exactly. Each system
/// has its own random stream (galaxy seed and system id), so nothing else disturbs it. Three kinds of extra:
/// more veins of what the system's archetype already holds (a share of its expected count per percent above 100), those near home bigger and nearer the
/// anchor, and small ore-bearing rocks beside the solid rocks of belts and clusters.
/// </summary>
internal static class ResourceGenerator
{
    private const int Cell = Units.CellUnits;

    /// <summary>Extra veins near home are this many percent bigger.</summary>
    public const int HomeRichnessPercent = 130;

    /// <summary>An ore-bearing rock holds this fraction (in percent) of a vein of its kind.</summary>
    public const int OreRockPercent = 33;

    /// <summary>Chance in percent per solid rock, per point of density above 100, of an ore-bearing rock beside it (so 140 gives 24).</summary>
    public const int OreRockChancePerPoint = 60;

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

            // More veins of the kinds the system already has.
            foreach (var spec in arch.Deposits)
            {
                int expected100 = (spec.CountMin + spec.CountMax) * 50; // the mean count, times 100
                int extra100 = expected100 * over / 100;
                int extra = extra100 / 100 + (rng.Next(0, 99) < extra100 % 100 ? 1 : 0);
                for (int k = 0; k < extra; k++)
                {
                    int amount = rng.Next(spec.AmountMin, spec.AmountMax);
                    int dLo = arch.DistanceMin, dHi = arch.DistanceMax;
                    (dLo, dHi) = arch.HasWide ? (arch.WideDistanceMin, arch.WideDistanceMax) : (arch.DistanceMin * 4, arch.DistanceMax * 4);
                    if (home) dHi = Math.Max(dLo, dLo + (dHi - dLo) / 2); // near home: the inner half of the range
                    int d = rng.Next(dLo, dHi);
                    long pct = 100;
                    if (!home)
                    {
                        pct = (long)wide.StarTypes[star].RichnessPercent * (100 + wide.DepthRichnessPercent * depth[i]) / 100;
                        pct = pct * (100 + (dHi > dLo ? wide.OuterRichnessPercent * (d - dLo) / (dHi - dLo) : 0)) / 100;
                    }
                    else pct = HomeRichnessPercent;
                    amount = (int)Math.Max(1, (long)amount * pct / 100);
                    int ox = rng.Next(-d, d);
                    int oz = (int)IntMath.ISqrt((long)d * d - (long)ox * ox) * (rng.Next(0, 1) == 0 ? -1 : 1);
                    int oy = rng.Next(-d / 4, d / 4);
                    int dens = spec.HasDensity ? rng.Next(spec.DensityMin, spec.DensityMax) : DepositRules.DefaultDensity;
                    deposits.Add(new GeneratedDeposit(i, spec.Item, amount, systems[i].X + ox, systems[i].Y + oy, systems[i].Z + oz, dens));
                }
            }

            // Small ore-bearing rocks beside the solid rocks of belts and clusters.
            int chance = Math.Min(60, over * OreRockChancePerPoint / 100);
            foreach (int id in Enumerable.Range(0, obstacles.Count).Where(n => obstacles[n].System == i))
            {
                var o = obstacles[id];
                if (wide.Kinds[o.Kind].Behaviour != ObstacleBehaviour.Solid) continue;
                if (rng.Next(0, 99) >= chance) continue;
                var spec = arch.Deposits[rng.Next(0, arch.Deposits.Length - 1)];
                int amount = Math.Max(12, rng.Next(spec.AmountMin, spec.AmountMax) * OreRockPercent / 100);
                int ring = Math.Max(3, o.Radius / Cell + 4); // cells from the rock's centre: just outside its surface
                int ox = rng.Next(-ring, ring);
                int oz = (int)IntMath.ISqrt((long)ring * ring - (long)ox * ox) * (rng.Next(0, 1) == 0 ? -1 : 1);
                int dens = spec.HasDensity ? rng.Next(spec.DensityMin, spec.DensityMax) : DepositRules.DefaultDensity;
                deposits.Add(new GeneratedDeposit(i, spec.Item, amount, o.X / Cell + ox, o.Y / Cell, o.Z / Cell + oz, dens));
            }
        }
    }
}
