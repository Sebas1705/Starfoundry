using System.Text.Json;

namespace Starfoundry.Sim;

/// <summary>
/// The clustered rock field (ADR 0046), the optional <c>clusters</c> object of the <c>field</c> section. Cluster parents are decided per super-sector
/// (a cube of <see cref="SuperCells"/>) from integer hashes; a sector's rocks are the thinned sum of the parents that reach it, so space is mostly void
/// with dense fields (spheres, flat belts, hollow shells) of rocks in size classes and rare giants. All lengths are in cells.
/// </summary>
/// <param name="SuperCells">Side of a super-sector: it holds zero, one or two cluster parents.</param>
/// <param name="ParentNonePercent">Chance of no parent in a super-sector.</param>
/// <param name="ParentOnePercent">Chance of exactly one parent (the rest hold two).</param>
/// <param name="RadiusBaseCells">Cluster radius is <c>base + per_richness * richness</c> times a jitter of 80 to 120 percent.</param>
/// <param name="RichnessWeights">Weights of richness 1 to 5.</param>
/// <param name="CoreRocksPerSector">Rocks per 200-cell sector at the core of a cluster, by richness 1 to 5 (at resource density 100).</param>
/// <param name="ShapeWeights">Weights of sphere, disc (belt, four times thinner on one axis, one and a half times wider) and hollow shell.</param>
/// <param name="SmallMass">Mass range of small rocks (thousandths of the reference rock).</param>
/// <param name="MediumMass">Mass range of medium rocks.</param>
/// <param name="LargeMass">Mass range of large rocks (drawn skewed to the low end).</param>
/// <param name="MediumPermille">Share of medium rocks in thousandths.</param>
/// <param name="LargeBasePermille">Share of large rocks at richness 0, in thousandths; plus <paramref name="LargePerRichnessPermille"/> per richness step.</param>
/// <param name="GiantMass">Mass range of giants (drawn skewed to the low end).</param>
/// <param name="GiantRatePermille">Giants as thousandths of a cluster's core rock rate.</param>
/// <param name="GiantMargin">Giants keep this far from the faces of their sector (their reach must fit).</param>
/// <param name="GiantCandidates">Giant candidates per sector.</param>
/// <param name="RadiusPercent">Rock radius in percent of the reference size law (the rocks of a cluster are bigger than the old uniform field's).</param>
/// <param name="DensityBase">Density of a cluster rock is <c>base + per_richness * richness</c> plus or minus <paramref name="DensityJitter"/>.</param>
/// <param name="StrayDensityMax">Strays outside any cluster draw a density from the field's minimum up to this.</param>
/// <param name="DominantPercent">Share of a cluster's rocks that hold its dominant ore.</param>
/// <param name="BackgroundPermille">Strays per sector outside any cluster, in thousandths (the voids are never perfectly empty).</param>
/// <param name="Margin">Regular rocks keep this far from the faces of their sector.</param>
/// <param name="MaxRocksPerSector">Cap on the rocks of one sector.</param>
public sealed record ClusterRules(
    int SuperCells, int ParentNonePercent, int ParentOnePercent, int RadiusBaseCells, int RadiusPerRichnessCells, int[] RichnessWeights, int[] CoreRocksPerSector,
    int[] ShapeWeights, (int Min, int Max) SmallMass, (int Min, int Max) MediumMass, (int Min, int Max) LargeMass, int MediumPermille, int LargeBasePermille,
    int LargePerRichnessPermille, (int Min, int Max) GiantMass, int GiantRatePermille, int GiantMargin, int GiantCandidates, int RadiusPercent, int DensityBase,
    int DensityPerRichness, int DensityJitter, int StrayDensityMax, int DominantPercent, int BackgroundPermille, int Margin, int MaxRocksPerSector)
{
    /// <summary>Largest cluster reach in cells (disc widening included): the generator looks this far for parents.</summary>
    public int MaxReachCells => (int)((long)(RadiusBaseCells + RadiusPerRichnessCells * 5) * 1200 / 1000 * 3 / 2) + 1;

    /// <summary>Radius in units of a rock of this mass in a cluster: the size law times <see cref="RadiusPercent"/>.</summary>
    public int RadiusOf(int mass) => (int)((long)FieldRules.RadiusOfMass(mass) * RadiusPercent / 100);

    /// <summary>How far the drawn rock reaches from its centre, in cells (1.6 times its radius, rounded up).</summary>
    public static int ReachCellsOf(int radiusUnits) => (int)(((long)radiusUnits * 160 / 100 + Units.CellUnits - 1) / Units.CellUnits);

    internal static ClusterRules Load(JsonElement c, int sectorCells, int densityMin, int densityMax)
    {
        ContentDb.RequireObjectInternal(c, "clusters", "super_cells", "parents", "radius_base_cells", "radius_per_richness_cells", "richness_weights", "core_rocks_per_sector",
            "shape_weights", "small_mass", "medium_mass", "large_mass", "medium_permille", "large_base_permille", "large_per_richness_permille", "giant_mass", "giant_rate_permille",
            "giant_margin_cells", "giant_candidates", "radius_percent", "density_base", "density_per_richness", "density_jitter", "stray_density_max", "dominant_ore_percent",
            "background_permille", "margin_cells", "max_rocks_per_sector");
        var parents = c.GetProperty("parents");
        ContentDb.RequireObjectInternal(parents, "clusters parents", "none_percent", "one_percent");
        int none = ContentDb.IntInternal(parents, "none_percent", "clusters", 0, 100), one = ContentDb.IntInternal(parents, "one_percent", "clusters", 0, 100);
        if (none + one > 100) throw new InvalidDataException("'parents' in clusters: none_percent + one_percent must not pass 100.");
        int super = ContentDb.IntInternal(c, "super_cells", "clusters", sectorCells * 2, 20_000);
        int rb = ContentDb.IntInternal(c, "radius_base_cells", "clusters", 10, 2000), rp = ContentDb.IntInternal(c, "radius_per_richness_cells", "clusters", 0, 1000);
        int[] rich = IntArray(c, "richness_weights", 5, 0, 1000), core = IntArray(c, "core_rocks_per_sector", 5, 0, 120), shape = IntArray(c, "shape_weights", 3, 0, 1000);
        if (rich.Sum() < 1 || shape.Sum() < 1) throw new InvalidDataException("'richness_weights' and 'shape_weights' in clusters need a positive sum.");
        var small = ContentDb.RangeInternal(c, "small_mass", "clusters", 10, 1_000_000);
        var medium = ContentDb.RangeInternal(c, "medium_mass", "clusters", 10, 1_000_000);
        var large = ContentDb.RangeInternal(c, "large_mass", "clusters", 10, 1_000_000);
        var giant = ContentDb.RangeInternal(c, "giant_mass", "clusters", 10, 1_000_000);
        int mediumPermille = ContentDb.IntInternal(c, "medium_permille", "clusters", 0, 1000), largeBase = ContentDb.IntInternal(c, "large_base_permille", "clusters", 0, 1000);
        int largePer = ContentDb.IntInternal(c, "large_per_richness_permille", "clusters", 0, 200);
        if (mediumPermille + largeBase + largePer * 5 > 1000) throw new InvalidDataException("'medium_permille' and the large shares in clusters must not pass 1000.");
        int gRate = ContentDb.IntInternal(c, "giant_rate_permille", "clusters", 0, 200), gMargin = ContentDb.IntInternal(c, "giant_margin_cells", "clusters", 0, sectorCells / 4);
        int gCand = ContentDb.IntInternal(c, "giant_candidates", "clusters", 0, 8), radiusPct = ContentDb.IntInternal(c, "radius_percent", "clusters", 50, 1000);
        int margin = ContentDb.IntInternal(c, "margin_cells", "clusters", 0, sectorCells / 4), perSector = ContentDb.IntInternal(c, "max_rocks_per_sector", "clusters", 1, 400);
        var rules = new ClusterRules(super, none, one, rb, rp, rich, core, shape, small, medium, large, mediumPermille, largeBase, largePer, giant, gRate, gMargin, gCand, radiusPct,
            ContentDb.IntInternal(c, "density_base", "clusters", 0, 400), ContentDb.IntInternal(c, "density_per_richness", "clusters", 0, 100), ContentDb.IntInternal(c, "density_jitter", "clusters", 0, 100),
            ContentDb.IntInternal(c, "stray_density_max", "clusters", densityMin, densityMax), ContentDb.IntInternal(c, "dominant_ore_percent", "clusters", 0, 100),
            ContentDb.IntInternal(c, "background_permille", "clusters", 0, 5000), margin, perSector);
        int largestRegular = Math.Max(large.Max, Math.Max(medium.Max, small.Max));
        if (margin < ReachCellsOf(rules.RadiusOf(largestRegular))) throw new InvalidDataException($"'margin_cells' in clusters must be at least {ReachCellsOf(rules.RadiusOf(largestRegular))} (the biggest regular rock).");
        if (gCand > 0 && gMargin < ReachCellsOf(rules.RadiusOf(giant.Max))) throw new InvalidDataException($"'giant_margin_cells' in clusters must be at least {ReachCellsOf(rules.RadiusOf(giant.Max))} (the biggest giant).");
        if (rules.MaxReachCells > 4000) throw new InvalidDataException("clusters reach too far (over 4000 cells).");
        return rules;
    }

    private static int[] IntArray(JsonElement c, string name, int length, int min, int max)
    {
        var list = new List<int>();
        foreach (var e in ContentDb.ArrayInternal(c, name))
        {
            if (e.ValueKind != JsonValueKind.Number || !e.TryGetInt32(out int v) || v < min || v > max) throw new InvalidDataException($"'{name}' in clusters needs integers from {min} to {max}.");
            list.Add(v);
        }
        if (list.Count != length) throw new InvalidDataException($"'{name}' in clusters needs {length} values.");
        return list.ToArray();
    }

    internal void MixInto(Action<long> mix)
    {
        mix(-3402);
        mix(SuperCells); mix(ParentNonePercent); mix(ParentOnePercent); mix(RadiusBaseCells); mix(RadiusPerRichnessCells);
        foreach (int v in RichnessWeights) mix(v);
        foreach (int v in CoreRocksPerSector) mix(v);
        foreach (int v in ShapeWeights) mix(v);
        foreach (var (a, b) in new[] { SmallMass, MediumMass, LargeMass, GiantMass }) { mix(a); mix(b); }
        mix(MediumPermille); mix(LargeBasePermille); mix(LargePerRichnessPermille); mix(GiantRatePermille); mix(GiantMargin); mix(GiantCandidates); mix(RadiusPercent);
        mix(DensityBase); mix(DensityPerRichness); mix(DensityJitter); mix(StrayDensityMax); mix(DominantPercent); mix(BackgroundPermille); mix(Margin); mix(MaxRocksPerSector);
    }
}

/// <summary>A cluster parent: where a cluster of rocks is centred and what it is like. The id names it for ever (shape of the world, not of the run).</summary>
/// <param name="Id">Packs the super-sector and the index in it; see <see cref="ClusterField.ParentById"/>.</param>
/// <param name="Radius">Reach in cells along the widest axis.</param>
/// <param name="Richness">1 to 5.</param>
/// <param name="Item">The dominant ore.</param>
/// <param name="Shape">0 sphere, 1 disc (belt), 2 hollow shell.</param>
/// <param name="Axis">For a disc, the axis that is four times thinner (0 y, 1 x).</param>
/// <param name="CoreRocks">Rocks per sector at the core, in thousandths, for the world's resource density.</param>
public readonly record struct ClusterParent(long Id, int X, int Y, int Z, int Radius, int Richness, int Item, int Shape, int Axis, long CoreRocks);

/// <summary>
/// The generator of the clustered field (ADR 0046): the rocks of one sector are a pure function of the world seed, the content and the sector's coordinates.
/// Parents come from super-sector hashes, so the rocks of a cluster that crosses sector faces agree on both sides without any state, in any visit order.
/// Integer arithmetic only.
/// </summary>
public static class ClusterField
{
    private const ulong ParentSalt = 0xC1A5_7E25_0000_0046UL;
    private const ulong SectorSalt = 0xC1A5_7E25_5EC7_0046UL;
    private const int IdBias = 1 << 19;

    private static ulong Hash(ulong seed, ulong salt, int a, int b, int c)
    {
        ulong s = new SplitMix64(seed ^ salt ^ ((ulong)(uint)a * 0x9E37_79B9_7F4A_7C15UL)).Next();
        s = new SplitMix64(s ^ ((ulong)(uint)b * 0xC2B2_AE3D_27D4_EB4FUL)).Next();
        s = new SplitMix64(s ^ ((ulong)(uint)c * 0x1656_67B1_9E37_79F9UL)).Next();
        return s;
    }

    private static long IdOf(int index, int sx, int sy, int sz) =>
        ((long)index << 60) | ((long)(sx + IdBias) << 40) | ((long)(sy + IdBias) << 20) | (long)(sz + IdBias);

    /// <summary>The parents of one super-sector, in index order (zero, one or two), at the given resource density.</summary>
    public static void ParentsOf(FieldRules f, ClusterRules c, ulong seed, int sx, int sy, int sz, int densityPercent, List<ClusterParent> into)
    {
        var rng = new SplitMix64(Hash(seed, ParentSalt, sx, sy, sz));
        int roll = rng.Next(0, 99);
        int count = roll < c.ParentNonePercent ? 0 : roll < c.ParentNonePercent + c.ParentOnePercent ? 1 : 2;
        long S = c.SuperCells;
        int richTotal = c.RichnessWeights.Sum(), shapeTotal = c.ShapeWeights.Sum();
        for (int i = 0; i < count; i++)
        {
            int x = (int)(sx * S + rng.Next(0, c.SuperCells - 1)), y = (int)(sy * S + rng.Next(0, c.SuperCells - 1)), z = (int)(sz * S + rng.Next(0, c.SuperCells - 1));
            int ru = rng.Next(0, 1000);
            int rr = rng.Next(0, richTotal - 1), rich = 1;
            for (int w = 0, acc = 0; w < 5; w++) { acc += c.RichnessWeights[w]; if (rr < acc) { rich = w + 1; break; } }
            int itemRoll = rng.Next(0, 999_999);
            int sr = rng.Next(0, shapeTotal - 1), shape = 0;
            for (int w = 0, acc = 0; w < 3; w++) { acc += c.ShapeWeights[w]; if (sr < acc) { shape = w; break; } }
            int axis = rng.Next(0, 1);
            long fromHome = IntMath.ISqrt((long)x * x + (long)y * y + (long)z * z);
            int item = SectorField.PickItem(f, itemRoll, fromHome);
            int r = (int)((long)(c.RadiusBaseCells + c.RadiusPerRichnessCells * rich) * (800 + 400 * ru / 1000) / 1000);
            if (shape == 1) r = r * 3 / 2;
            into.Add(new ClusterParent(IdOf(i, sx, sy, sz), x, y, z, r, rich, item, shape, axis, (long)c.CoreRocksPerSector[rich - 1] * 1000 * densityPercent / 100));
        }
    }

    /// <summary>The parent a cluster id names (for the labels of the client), or null when the id is not a parent of this world.</summary>
    public static ClusterParent? ParentById(FieldRules f, ClusterRules c, ulong seed, long id, int densityPercent = 100)
    {
        if (id < 0) return null;
        int index = (int)(id >> 60), sx = (int)((id >> 40) & 0xFFFFF) - IdBias, sy = (int)((id >> 20) & 0xFFFFF) - IdBias, sz = (int)(id & 0xFFFFF) - IdBias;
        var list = new List<ClusterParent>(2);
        ParentsOf(f, c, seed, sx, sy, sz, densityPercent, list);
        foreach (var p in list) if (p.Id == id) return p;
        return null;
    }

    /// <summary>The parents whose centre lies in a box of cells (inclusive), in id order.</summary>
    public static List<ClusterParent> ParentsInBox(FieldRules f, ClusterRules c, ulong seed, long x0, long x1, long y0, long y1, long z0, long z1, int densityPercent = 100)
    {
        var res = new List<ClusterParent>();
        var tmp = new List<ClusterParent>(2);
        long S = c.SuperCells;
        for (long sx = IntMath.FloorDiv(x0, S); sx <= IntMath.FloorDiv(x1, S); sx++)
            for (long sy = IntMath.FloorDiv(y0, S); sy <= IntMath.FloorDiv(y1, S); sy++)
                for (long sz = IntMath.FloorDiv(z0, S); sz <= IntMath.FloorDiv(z1, S); sz++)
                {
                    tmp.Clear();
                    ParentsOf(f, c, seed, (int)sx, (int)sy, (int)sz, densityPercent, tmp);
                    foreach (var p in tmp) if (p.X >= x0 && p.X <= x1 && p.Y >= y0 && p.Y <= y1 && p.Z >= z0 && p.Z <= z1) res.Add(p);
                }
        res.Sort((a, b) => a.Id.CompareTo(b.Id));
        return res;
    }

    /// <summary>Falloff of a parent at a point, 0 to 1000: flat-top out to 0.58 of the radius for a sphere or a disc, a ring peaking at 0.7 for a shell.</summary>
    private static long Falloff(in ClusterParent p, long x, long y, long z)
    {
        long dx = x - p.X, dy = y - p.Y, dz = z - p.Z;
        if (p.Shape == 1) { if (p.Axis == 0) dy *= 4; else dx *= 4; }
        long d2 = dx * dx + dy * dy + dz * dz, r2 = (long)p.Radius * p.Radius;
        if (d2 >= r2) return 0;
        if (p.Shape == 2)
        {
            long xx = IntMath.ISqrt(d2) * 1000 / p.Radius;
            long u = (xx - 700) * 1000 / 300;
            if (Math.Abs(u) >= 1000) return 0;
            return Math.Min(1000L, (1000 - u * u / 1000) * 3 / 2);
        }
        return Math.Min(1000L, (1000 - d2 * 1000 / r2) * 3 / 2);
    }

    private static bool Reaches(in ClusterParent p, long s, int cx, int cy, int cz)
    {
        long dx = Outside(p.X, cx * s, (cx + 1) * s), dy = Outside(p.Y, cy * s, (cy + 1) * s), dz = Outside(p.Z, cz * s, (cz + 1) * s);
        if (p.Shape == 1) { if (p.Axis == 0) dy *= 4; else dx *= 4; }
        return dx * dx + dy * dy + dz * dz < (long)p.Radius * p.Radius;
    }

    private static long Outside(long v, long lo, long hi) => v < lo ? lo - v : v > hi ? v - hi : 0;

    private static bool Clear(List<FieldRock> into, int first, long x, long y, long z, int reach)
    {
        for (int j = first; j < into.Count; j++)
        {
            var o = into[j];
            long need = reach + ClusterRules.ReachCellsOf(o.Radius) + FieldRules.GapCells;
            long dx = x - o.X, dy = y - o.Y, dz = z - o.Z;
            if (dx * dx + dy * dy + dz * dz < need * need) return false;
        }
        return true;
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

    /// <summary>
    /// The rocks of one sector, appended to <paramref name="into"/> in index order (giants first: indices 0 to <c>giant_candidates</c> - 1, then the regular
    /// candidates). <paramref name="densityPercent"/> is the world's resource density rule (100 is the base rate).
    /// </summary>
    public static void Generate(FieldRules f, ClusterRules c, IReadOnlyList<StarSystem> systems, ulong seed, int cx, int cy, int cz, int densityPercent, List<FieldRock> into)
    {
        long sector = f.SectorCells, S = c.SuperCells, reach = c.MaxReachCells;
        long x0 = cx * sector, y0 = cy * sector, z0 = cz * sector;
        var near = new List<ClusterParent>(6);
        var tmp = new List<ClusterParent>(2);
        for (long sx = IntMath.FloorDiv(x0 - reach, S); sx <= IntMath.FloorDiv(x0 + sector + reach, S); sx++)
            for (long sy = IntMath.FloorDiv(y0 - reach, S); sy <= IntMath.FloorDiv(y0 + sector + reach, S); sy++)
                for (long sz = IntMath.FloorDiv(z0 - reach, S); sz <= IntMath.FloorDiv(z0 + sector + reach, S); sz++)
                {
                    tmp.Clear();
                    ParentsOf(f, c, seed, (int)sx, (int)sy, (int)sz, densityPercent, tmp);
                    foreach (var p in tmp) if (Reaches(p, sector, cx, cy, cz)) near.Add(p);
                }
        long sumLam = (long)c.BackgroundPermille * densityPercent / 100;
        foreach (var p in near) sumLam += p.CoreRocks;
        if (sumLam <= 0) return;
        near.Sort((a, b) => a.Id.CompareTo(b.Id));

        var rng = new SplitMix64(Hash(seed, SectorSalt, cx, cy, cz));
        int first = into.Count;
        long limit = f.WorldLimitCells - 100;
        long excl2 = (long)f.ExclusionCells * f.ExclusionCells;
        int giantSlots = c.GiantCandidates;

        // Giants first, so they claim their room: kept GiantMargin cells from the faces (their reach must fit in the sector).
        if (near.Count > 0)
        {
            int gSpan = (int)sector - 2 * c.GiantMargin - 1;
            for (int k = 0; k < giantSlots; k++)
            {
                int accRoll = rng.Next(0, giantSlots * 1000 - 1), massRoll = rng.Next(0, 1000), densRoll = rng.Next(0, 999);
                long x = x0 + c.GiantMargin + rng.Next(0, gSpan), y = y0 + c.GiantMargin + rng.Next(0, gSpan), z = z0 + c.GiantMargin + rng.Next(0, gSpan);
                long lam = 0, bestW = -1;
                ClusterParent best = default;
                foreach (var p in near)
                {
                    long w = Falloff(p, x, y, z) * p.CoreRocks * c.GiantRatePermille / 1_000_000;
                    lam += w;
                    if (w > bestW) { bestW = w; best = p; }
                }
                if (accRoll >= lam || bestW < 0 || Math.Abs(x) > limit || Math.Abs(y) > limit || Math.Abs(z) > limit || NearAnchor(systems, x, y, z, excl2)) continue;
                int mass = c.GiantMass.Min + (int)((long)(c.GiantMass.Max - c.GiantMass.Min) * massRoll * massRoll / 1_000_000);
                int dens = Math.Clamp(c.DensityBase + c.DensityPerRichness * best.Richness + densRoll % (2 * c.DensityJitter + 1) - c.DensityJitter, f.DensityMin, f.DensityMax);
                int radius = c.RadiusOf(mass);
                if (!Clear(into, first, x, y, z, ClusterRules.ReachCellsOf(radius))) continue;
                into.Add(new FieldRock(k, best.Item, (int)x, (int)y, (int)z, mass, dens, radius, f.AmountOf(mass, dens), best.Id));
            }
        }

        int M = (int)Math.Min(120, (sumLam + 999) / 1000);
        int span = (int)sector - 2 * c.Margin - 1;
        for (int k = 0; k < M; k++)
        {
            // The draws of a rock come in a fixed order whatever happens to it, so a rejected rock never shifts the next one's look.
            int accRoll = rng.Next(0, M * 1000 - 1), itemRoll = rng.Next(0, 999_999), u = rng.Next(0, 1_000_000), classRoll = rng.Next(0, 999), densRoll = rng.Next(0, 999), mixRoll = rng.Next(0, 99);
            long x = x0 + c.Margin + rng.Next(0, span), y = y0 + c.Margin + rng.Next(0, span), z = z0 + c.Margin + rng.Next(0, span);
            if (into.Count - first >= c.MaxRocksPerSector) continue;
            long lam = (long)c.BackgroundPermille * densityPercent / 100, bestW = -1;
            ClusterParent best = default;
            bool any = false;
            foreach (var p in near)
            {
                long w = Falloff(p, x, y, z) * p.CoreRocks / 1000;
                lam += w;
                if (w > bestW) { bestW = w; best = p; any = true; }
            }
            if (accRoll >= lam) continue;
            if (Math.Abs(x) > limit || Math.Abs(y) > limit || Math.Abs(z) > limit || NearAnchor(systems, x, y, z, excl2)) continue;
            bool inCluster = any && bestW > (long)c.BackgroundPermille * densityPercent / 200;
            int rich = inCluster ? best.Richness : 0;
            int largeShare = c.LargeBasePermille + c.LargePerRichnessPermille * rich;
            int mass = classRoll < 1000 - c.MediumPermille - largeShare ? c.SmallMass.Min + (int)((long)(c.SmallMass.Max - c.SmallMass.Min) * u / 1_000_000)
                : classRoll < 1000 - largeShare ? c.MediumMass.Min + (int)((long)(c.MediumMass.Max - c.MediumMass.Min) * u / 1_000_000)
                : c.LargeMass.Min + (int)((long)(c.LargeMass.Max - c.LargeMass.Min) * u / 1_000_000 * u / 1_000_000);
            int radius = c.RadiusOf(mass);
            if (!Clear(into, first, x, y, z, ClusterRules.ReachCellsOf(radius))) continue;
            long fromHome = IntMath.ISqrt(x * x + y * y + z * z);
            int item = inCluster && mixRoll < c.DominantPercent ? best.Item : SectorField.PickItem(f, itemRoll, fromHome);
            int dens = inCluster
                ? Math.Clamp(c.DensityBase + c.DensityPerRichness * rich + densRoll % (2 * c.DensityJitter + 1) - c.DensityJitter, f.DensityMin, f.DensityMax)
                : f.DensityMin + densRoll % (c.StrayDensityMax - f.DensityMin + 1);
            into.Add(new FieldRock(giantSlots + k, item, (int)x, (int)y, (int)z, mass, dens, radius, f.AmountOf(mass, dens), inCluster ? best.Id : -1));
        }
    }
}
