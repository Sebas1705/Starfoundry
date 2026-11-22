namespace Starfoundry.Sim;

/// <summary>
/// Places the star type and the obstacles of one system for the wide galaxy (ADR 0028). Pure and deterministic: every system has its
/// own random stream, derived from the galaxy seed and the system id, so no other generator step can disturb it. Solid rocks keep clear
/// of the anchor and of every deposit (so the arrival point and the hand mining spots are never boxed in) and of each other (so a ship
/// always finds a gap); mines and drifting debris keep farther away still.
/// </summary>
internal static class ObstacleGenerator
{
    private const int Cell = Units.CellUnits;

    /// <summary>Minimum gap between the surfaces of two solid rocks, in cells.</summary>
    private const int SolidGapCells = 10;

    public static int PickStar(WorldGenRules w, ulong seed, int system, int depth)
    {
        if (system == 0) return w.HomeStar;
        var rng = new SplitMix64(seed ^ 0x57A2_7E11_0F0F_1111UL ^ ((ulong)(system + 1) * 0x9E3779B97F4A7C15UL));
        var pool = w.StarTypes.Where(s => s.MinDepth <= depth).ToList();
        int roll = rng.Next(0, pool.Sum(s => s.Weight) - 1);
        foreach (var s in pool)
        {
            if (roll < s.Weight) return s.Index;
            roll -= s.Weight;
        }
        return pool[0].Index;
    }

    private sealed class Placer
    {
        public readonly List<GeneratedObstacle> Out = new();
        public readonly List<(long X, long Y, long Z, long R)> Solids = new();
        public SplitMix64 Rng;
        public WorldGenRules W = null!;
        public StarSystem Sys = null!;
        public (long X, long Y, long Z)[] Keep = Array.Empty<(long, long, long)>(); // anchor first, then the deposits (units)
    }

    public static List<GeneratedObstacle> Generate(WorldGenRules w, ulong seed, StarSystem sys, int depth, List<(int X, int Y, int Z)> depositsCells)
    {
        var star = w.StarTypes[sys.Star];
        var p = new Placer
        {
            Rng = new SplitMix64(seed ^ 0x0B57_AC1E_5EED_0001UL ^ ((ulong)(sys.Id + 1) * 0xD1B54A32D192ED03UL)),
            W = w,
            Sys = sys,
        };
        var keep = new List<(long, long, long)> { ((long)sys.X * Cell, (long)sys.Y * Cell, (long)sys.Z * Cell) };
        foreach (var (x, y, z) in depositsCells) keep.Add(((long)x * Cell + Cell / 2, (long)y * Cell + Cell / 2, (long)z * Cell + Cell / 2));
        p.Keep = keep.ToArray();

        int Scaled(int n, int percent) => (n * percent + 50) / 100;
        int ast = star.AsteroidPercent, haz = star.HazardPercent;
        bool home = sys.Id == 0;

        // Plain rocks: a belt (a ring around the star), clusters and scattered singles.
        if (p.Rng.Next(0, 99) < 55) Belt(p, Scaled(p.Rng.Next(12, 26), ast));
        int clusters = p.Rng.Next(1, 3);
        for (int c = 0; c < clusters; c++) Cluster(p, Scaled(p.Rng.Next(5, 11), ast));
        Scatter(p, Scaled(p.Rng.Next(6, 14), ast));

        // Hazard regions (clouds exist at home too, far from everything, and are only a slowdown).
        int clouds = home ? p.Rng.Next(0, 1) : Scaled(p.Rng.Next(0, 2), haz);
        for (int c = 0; c < clouds; c++) Cloud(p, home);

        int wrecks = home ? 1 : p.Rng.Next(1, 2) + (depth >= 2 ? 1 : 0);
        for (int k = 0; k < wrecks; k++) Wreck(p);

        if (!home)
        {
            int groups = Scaled(p.Rng.Next(0, 2), haz) + (depth >= 1 ? Scaled(p.Rng.Next(0, 1), haz) : 0);
            for (int g = 0; g < groups; g++) MineGroup(p);
            int drifters = Scaled(p.Rng.Next(2, 6), haz);
            for (int d = 0; d < drifters; d++) Drifter(p);
        }
        return p.Out;
    }

    // ---- helpers ------------------------------------------------------------------------------------

    private static int PickKind(Placer p, ObstacleBehaviour b, int salt = -1)
    {
        var list = p.W.Kinds.Where(k => k.Behaviour == b).ToList();
        int roll = p.Rng.Next(0, list.Sum(k => k.Weight) - 1);
        foreach (var k in list)
        {
            if (roll < k.Weight) return k.Index;
            roll -= k.Weight;
        }
        return list[0].Index;
    }

    private static long Dist2(long ax, long ay, long az, long bx, long by, long bz)
    {
        long dx = ax - bx, dy = ay - by, dz = az - bz;
        return dx * dx + dy * dy + dz * dz;
    }

    /// <summary>A random point of the system: inside 90 percent of the radius, flattened (the systems are disc shaped).</summary>
    private static (long X, long Y, long Z) RandomPoint(Placer p, int maxRadiusCells)
    {
        long r = (long)maxRadiusCells * Cell;
        for (int tries = 0; tries < 30; tries++)
        {
            long x = p.Rng.Next(-maxRadiusCells, maxRadiusCells) * (long)Cell, z = p.Rng.Next(-maxRadiusCells, maxRadiusCells) * (long)Cell;
            if (x * x + z * z > r * r) continue;
            long y = p.Rng.Next(-maxRadiusCells / 8, maxRadiusCells / 8) * (long)Cell;
            return (p.Sys.X * (long)Cell + x, p.Sys.Y * (long)Cell + y, p.Sys.Z * (long)Cell + z);
        }
        return (p.Sys.X * (long)Cell, p.Sys.Y * (long)Cell, p.Sys.Z * (long)Cell);
    }

    private static bool ClearOfKeep(Placer p, long x, long y, long z, long radius, int anchorCells, int depositCells)
    {
        for (int i = 0; i < p.Keep.Length; i++)
        {
            long need = radius + (long)(i == 0 ? anchorCells : depositCells) * Cell;
            if (Dist2(x, y, z, p.Keep[i].X, p.Keep[i].Y, p.Keep[i].Z) < need * need) return false;
        }
        return true;
    }

    private static bool Inside(Placer p, long x, long y, long z, long radius)
    {
        long lim = (long)(p.W.SystemRadiusCells - 5) * Cell - radius;
        return Dist2(x, 0, z, p.Sys.X * (long)Cell, 0, p.Sys.Z * (long)Cell) <= lim * lim;
    }

    private static bool TrySolid(Placer p, long x, long y, long z, int gapCells = SolidGapCells)
    {
        int kind = PickKind(p, ObstacleBehaviour.Solid);
        var def = p.W.Kinds[kind];
        long r = p.Rng.Next(def.RadiusMin, def.RadiusMax) * 24L;
        if (!Inside(p, x, y, z, r) || !ClearOfKeep(p, x, y, z, r, p.W.AnchorClearCells, p.W.DepositClearCells)) return false;
        foreach (var s in p.Solids)
        {
            long need = r + s.R + (long)gapCells * Cell;
            if (Dist2(x, y, z, s.X, s.Y, s.Z) < need * need) return false;
        }
        p.Solids.Add((x, y, z, r));
        p.Out.Add(new GeneratedObstacle(p.Sys.Id, kind, (int)x, (int)y, (int)z, (int)r, p.Rng.Next(0, 1_000_000)));
        return true;
    }

    private static void Belt(Placer p, int count)
    {
        int rc = p.Rng.Next(p.W.SystemRadiusCells * 35 / 100, p.W.SystemRadiusCells * 78 / 100);
        long cx = p.Sys.X * (long)Cell, cy = p.Sys.Y * (long)Cell, cz = p.Sys.Z * (long)Cell;
        for (int i = 0; i < count; i++)
        {
            for (int tries = 0; tries < 12; tries++)
            {
                // A point on the ring without trigonometry: pick x on the circle and derive z, like the deposit placement does.
                int ox = p.Rng.Next(-rc, rc);
                int oz = (int)IntMath.ISqrt((long)rc * rc - (long)ox * ox) * (p.Rng.Next(0, 1) == 0 ? -1 : 1);
                int jx = p.Rng.Next(-8, 8), jz = p.Rng.Next(-8, 8), jy = p.Rng.Next(-5, 5);
                if (TrySolid(p, cx + (ox + jx) * (long)Cell, cy + jy * (long)Cell, cz + (oz + jz) * (long)Cell)) break;
            }
        }
    }

    private static void Cluster(Placer p, int count)
    {
        for (int tries = 0; tries < 20; tries++)
        {
            var c = RandomPoint(p, p.W.SystemRadiusCells * 85 / 100);
            if (!ClearOfKeep(p, c.X, c.Y, c.Z, 30L * Cell, p.W.AnchorClearCells, p.W.DepositClearCells)) continue;
            int placed = 0;
            for (int i = 0; i < count * 3 && placed < count; i++)
            {
                if (TrySolid(p, c.X + p.Rng.Next(-30, 30) * (long)Cell, c.Y + p.Rng.Next(-10, 10) * (long)Cell, c.Z + p.Rng.Next(-30, 30) * (long)Cell)) placed++;
            }
            if (placed > 0) return;
        }
    }

    private static void Scatter(Placer p, int count)
    {
        for (int i = 0; i < count; i++)
        {
            for (int tries = 0; tries < 10; tries++)
            {
                var c = RandomPoint(p, p.W.SystemRadiusCells * 90 / 100);
                if (TrySolid(p, c.X, c.Y, c.Z)) break;
            }
        }
    }

    private static void Cloud(Placer p, bool home)
    {
        int kind = PickKind(p, ObstacleBehaviour.Cloud);
        var def = p.W.Kinds[kind];
        long r = p.Rng.Next(def.RadiusMin, def.RadiusMax) * 24L;
        for (int tries = 0; tries < 30; tries++)
        {
            var c = RandomPoint(p, p.W.SystemRadiusCells * 80 / 100);
            // Clouds may cover rocks but never the arrival point; at home they also stay clear of every deposit.
            if (!ClearOfKeep(p, c.X, c.Y, c.Z, r, p.W.AnchorClearCells, home ? p.W.DepositClearCells + 40 : 0)) continue;
            p.Out.Add(new GeneratedObstacle(p.Sys.Id, kind, (int)c.X, (int)c.Y, (int)c.Z, (int)r, p.Rng.Next(0, 1_000_000)));
            return;
        }
    }

    private static void Wreck(Placer p)
    {
        int kind = PickKind(p, ObstacleBehaviour.Wreck);
        var def = p.W.Kinds[kind];
        long r = p.Rng.Next(def.RadiusMin, def.RadiusMax) * 24L;
        for (int tries = 0; tries < 30; tries++)
        {
            var c = RandomPoint(p, p.W.SystemRadiusCells * 85 / 100);
            if (!ClearOfKeep(p, c.X, c.Y, c.Z, r, p.W.AnchorClearCells, p.W.DepositClearCells)) continue;
            if (p.Solids.Any(s => Dist2(c.X, c.Y, c.Z, s.X, s.Y, s.Z) < (r + s.R + 4L * Cell) * (r + s.R + 4L * Cell))) continue;
            var loot = def.Loot[p.Rng.Next(0, def.Loot.Length - 1)];
            p.Out.Add(new GeneratedObstacle(p.Sys.Id, kind, (int)c.X, (int)c.Y, (int)c.Z, (int)r, p.Rng.Next(0, 1_000_000), LootItem: loot.Item, LootCount: p.Rng.Next(loot.CountMin, loot.CountMax)));
            return;
        }
    }

    private static void MineGroup(Placer p)
    {
        var mines = p.W.Kinds.Where(k => k.Behaviour == ObstacleBehaviour.Mine).ToList();
        var def = mines[p.Rng.Next(0, mines.Count - 1)];
        int count = p.Rng.Next(3, 6);
        for (int tries = 0; tries < 30; tries++)
        {
            var c = RandomPoint(p, p.W.SystemRadiusCells * 85 / 100);
            if (!ClearOfKeep(p, c.X, c.Y, c.Z, 40L * Cell, p.W.HazardClearCells, p.W.HazardClearCells)) continue;
            int placed = 0;
            for (int i = 0; i < count * 3 && placed < count; i++)
            {
                long x = c.X + p.Rng.Next(-30, 30) * (long)Cell, y = c.Y + p.Rng.Next(-8, 8) * (long)Cell, z = c.Z + p.Rng.Next(-30, 30) * (long)Cell;
                long r = p.Rng.Next(def.RadiusMin, def.RadiusMax) * 24L;
                if (!Inside(p, x, y, z, r) || !ClearOfKeep(p, x, y, z, r, p.W.HazardClearCells, p.W.HazardClearCells)) continue;
                if (p.Solids.Any(s => Dist2(x, y, z, s.X, s.Y, s.Z) < (r + s.R + 6L * Cell) * (r + s.R + 6L * Cell))) continue;
                p.Solids.Add((x, y, z, r + 8L * Cell)); // keeps rocks away from the blast too
                p.Out.Add(new GeneratedObstacle(p.Sys.Id, def.Index, (int)x, (int)y, (int)z, (int)r, p.Rng.Next(0, 1_000_000)));
                placed++;
            }
            if (placed > 0) return;
        }
    }

    private static void Drifter(Placer p)
    {
        int kind = PickKind(p, ObstacleBehaviour.Drifter);
        var def = p.W.Kinds[kind];
        long r = p.Rng.Next(def.RadiusMin, def.RadiusMax) * 24L;
        for (int tries = 0; tries < 30; tries++)
        {
            var c = RandomPoint(p, p.W.SystemRadiusCells * 75 / 100);
            int axis = p.Rng.Next(0, 2);
            if (axis == 1) axis = p.Rng.Next(0, 1) == 0 ? 0 : 2; // mostly horizontal lanes
            int amp = p.Rng.Next(60, 160) * Cell;
            bool ok = true;
            for (int s = -4; s <= 4 && ok; s++)
            {
                long x = c.X + (axis == 0 ? (long)amp * s / 4 : 0), y = c.Y + (axis == 1 ? (long)amp * s / 4 : 0), z = c.Z + (axis == 2 ? (long)amp * s / 4 : 0);
                ok = Inside(p, x, y, z, r) && ClearOfKeep(p, x, y, z, r, p.W.HazardClearCells, p.W.HazardClearCells);
            }
            if (!ok) continue;
            int phase = p.Rng.Next(0, 4 * amp - 1);
            p.Out.Add(new GeneratedObstacle(p.Sys.Id, kind, (int)c.X, (int)c.Y, (int)c.Z, (int)r, p.Rng.Next(0, 1_000_000), axis, amp, phase));
            return;
        }
    }
}
