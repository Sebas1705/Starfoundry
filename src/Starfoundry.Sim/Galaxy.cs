namespace Starfoundry.Sim;

/// <summary>
/// Small deterministic random generator (SplitMix64). Simulation code must never use System.Random: this one
/// gives the same sequence on every machine, so a galaxy is a pure function of its seed and the content.
/// </summary>
public struct SplitMix64
{
    private ulong _state;

    public SplitMix64(ulong seed) => _state = seed;

    public ulong Next()
    {
        ulong z = _state += 0x9E3779B97F4A7C15UL;
        z = (z ^ (z >> 30)) * 0xBF58476D1CE4E5B9UL;
        z = (z ^ (z >> 27)) * 0x94D049BB133111EBUL;
        return z ^ (z >> 31);
    }

    /// <summary>A uniform integer in [<paramref name="min"/>, <paramref name="max"/>] (both inclusive).</summary>
    public int Next(int min, int max)
    {
        if (max < min) throw new ArgumentOutOfRangeException(nameof(max));
        ulong span = (ulong)((long)max - min + 1);
        return (int)(min + (long)(Next() % span));
    }
}

/// <summary>A star system: an anchor position in cells, an archetype and lanes to other systems.</summary>
public sealed record StarSystem(int Id, string Name, int X, int Y, int Z, int Archetype, int[] Lanes, int Star = 0);

/// <summary>
/// An obstacle the generator placed. Positions and radius are in units (240 per cell); the id is the index in <see cref="Galaxy.Obstacles"/>.
/// A drifter moves along <paramref name="Axis"/> (0 x, 1 y, 2 z) between plus and minus <paramref name="Amplitude"/> units, starting at <paramref name="Phase"/>.
/// A wreck holds <paramref name="LootCount"/> of item <paramref name="LootItem"/>. <paramref name="Seed"/> shapes the model.
/// </summary>
public sealed record GeneratedObstacle(int System, int Kind, int X, int Y, int Z, int Radius, int Seed, int Axis = 0, int Amplitude = 0, int Phase = 0, int LootItem = -1, int LootCount = 0);

/// <summary>A deposit the generator created for a system; the world adds them in this order, so ids match indices.</summary>
public sealed record GeneratedDeposit(int System, int Item, int Amount, int X, int Y, int Z, int Density = DepositRules.DefaultDensity);

/// <summary>
/// An immutable generated galaxy. Systems sit on a jittered grid in one shared coordinate space, so belts,
/// machines and ships need no special handling: a system is just a region. Lanes always form a connected graph.
/// </summary>
public sealed class Galaxy
{
    public ulong Seed { get; }
    public GalaxyRules Rules { get; }
    public IReadOnlyList<StarSystem> Systems { get; }
    public IReadOnlyList<GeneratedDeposit> Deposits { get; }

    /// <summary>Obstacles of the wide galaxy (ADR 0028); empty for a legacy galaxy.</summary>
    public IReadOnlyList<GeneratedObstacle> Obstacles { get; }

    /// <summary>True when this galaxy was made by the wide generator (ADR 0028).</summary>
    public bool Wide { get; }

    private readonly int[][] _obstaclesBySystem;

    /// <summary>Indices into <see cref="Obstacles"/> of the obstacles of a system.</summary>
    public IReadOnlyList<int> ObstaclesOf(int system) => (uint)system < (uint)_obstaclesBySystem.Length ? _obstaclesBySystem[system] : Array.Empty<int>();

    private Galaxy(ulong seed, GalaxyRules rules, StarSystem[] systems, GeneratedDeposit[] deposits, GeneratedObstacle[]? obstacles = null, bool wide = false)
    {
        Seed = seed;
        Rules = rules;
        Systems = systems;
        Deposits = deposits;
        Obstacles = obstacles ?? Array.Empty<GeneratedObstacle>();
        Wide = wide;
        _obstaclesBySystem = Enumerable.Range(0, systems.Length).Select(s => Enumerable.Range(0, Obstacles.Count).Where(i => Obstacles[i].System == s).ToArray()).ToArray();
    }

    public bool HasLane(int a, int b) =>
        (uint)a < (uint)Systems.Count && (uint)b < (uint)Systems.Count && Array.IndexOf(Systems[a].Lanes, b) >= 0;

    /// <summary>The system whose anchor is nearest to the position (units), if within the system radius; otherwise -1.</summary>
    public int SystemAt(long xUnits, long yUnits, long zUnits)
    {
        long radius = (long)Rules.SystemRadiusCells * Units.CellUnits;
        long best = radius * radius + 1;
        int found = -1;
        foreach (var s in Systems)
        {
            long dx = s.X * (long)Units.CellUnits - xUnits, dy = s.Y * (long)Units.CellUnits - yUnits, dz = s.Z * (long)Units.CellUnits - zUnits;
            long d = dx * dx + dy * dy + dz * dz;
            if (d < best) { best = d; found = s.Id; }
        }
        return found;
    }

    public IEnumerable<int> DepositsOf(int system) =>
        Enumerable.Range(0, Deposits.Count).Where(i => Deposits[i].System == system);

    private static readonly string[] Starts = { "Ka", "Vel", "Or", "Tha", "Mi", "Sor", "Ni", "Bra", "Ze", "Lu", "Ar", "Cae" };
    private static readonly string[] Ends = { "ron", "tis", "dax", "lume", "vara", "phex", "nor", "sia", "kai", "reth" };

    /// <summary>Generates the galaxy for a seed. Pure and deterministic: the same content and seed give the same galaxy everywhere.</summary>
    public static Galaxy Generate(ContentDb content, ulong seed, bool legacy = false)
    {
        var rules = content.Galaxy ?? throw new InvalidOperationException("The content defines no galaxy.");
        // The wide generator (ADR 0028) runs only for content that defines a 'world' section and worlds that are not legacy
        // (WorldRules.LegacyGalaxy, set for logs older than version 10); everything else generates as it always did.
        var wide = legacy ? null : content.World;
        if (wide is not null) rules = rules with { SpacingCells = wide.SpacingCells, SystemRadiusCells = wide.SystemRadiusCells };
        var rng = new SplitMix64(seed ^ 0xA5A5_5A5A_1234_8765UL);
        int n = rules.SystemCount;

        // Positions: home at the origin, the rest on a grid around it (nearest cells first), each jittered.
        var cells = new List<(int Gx, int Gz)>();
        for (int gx = -4; gx <= 4; gx++)
            for (int gz = -4; gz <= 4; gz++)
                if (gx != 0 || gz != 0) cells.Add((gx, gz));
        cells.Sort((a, b) =>
        {
            int c = (a.Gx * a.Gx + a.Gz * a.Gz).CompareTo(b.Gx * b.Gx + b.Gz * b.Gz);
            if (c != 0) return c;
            c = a.Gx.CompareTo(b.Gx);
            return c != 0 ? c : a.Gz.CompareTo(b.Gz);
        });

        int jitter = rules.SpacingCells / 4;
        var pos = new (int X, int Y, int Z)[n];
        pos[0] = (0, 0, 0);
        for (int i = 1; i < n; i++)
        {
            var (gx, gz) = cells[i - 1];
            pos[i] = (gx * rules.SpacingCells + rng.Next(-jitter, jitter), rng.Next(-100, 100), gz * rules.SpacingCells + rng.Next(-jitter, jitter));
        }

        // Lanes: a minimum spanning tree (always connected), then nearest neighbours up to the wanted degree.
        var edges = new List<(long D, int A, int B)>();
        for (int a = 0; a < n; a++)
            for (int b = a + 1; b < n; b++)
                edges.Add((DistSq(pos[a], pos[b]), a, b));
        edges.Sort((e1, e2) =>
        {
            int c = e1.D.CompareTo(e2.D);
            if (c != 0) return c;
            c = e1.A.CompareTo(e2.A);
            return c != 0 ? c : e1.B.CompareTo(e2.B);
        });

        var lanes = new HashSet<int>[n];
        for (int i = 0; i < n; i++) lanes[i] = new HashSet<int>();
        var parent = Enumerable.Range(0, n).ToArray();
        int Find(int x) { while (parent[x] != x) x = parent[x] = parent[parent[x]]; return x; }
        foreach (var (_, a, b) in edges)
        {
            int ra = Find(a), rb = Find(b);
            if (ra == rb) continue;
            parent[ra] = rb;
            lanes[a].Add(b);
            lanes[b].Add(a);
        }
        for (int a = 0; a < n; a++)
        {
            foreach (var (_, x, y) in edges)
            {
                if (lanes[a].Count >= rules.LanesPerSystem) break;
                if (x != a && y != a) continue;
                int other = x == a ? y : x;
                if (lanes[a].Add(other)) lanes[other].Add(a);
            }
        }

        // Archetypes: home for system 0, a weighted pick for the others. Done after the lanes (which use no randomness) so
        // an archetype can be limited to systems a number of jumps from home; without limits the picks are the legacy ones.
        var depth = new int[n];
        Array.Fill(depth, -1);
        depth[0] = 0;
        var queue = new Queue<int>();
        queue.Enqueue(0);
        while (queue.Count > 0)
        {
            int u = queue.Dequeue();
            foreach (int v in lanes[u].OrderBy(x => x))
                if (depth[v] < 0) { depth[v] = depth[u] + 1; queue.Enqueue(v); }
        }
        var home = content.Archetypes.First(a => a.IsHome);
        var fullPool = content.Archetypes.Where(a => !a.IsHome && a.Weight > 0).ToList();
        var archetype = new int[n];
        archetype[0] = home.Index;
        for (int i = 1; i < n; i++)
        {
            var pool = fullPool.Where(a => a.AllowedAt(depth[i])).ToList();
            if (pool.Count == 0) pool = fullPool.Where(a => !a.HasDepthLimit).ToList();
            int roll = rng.Next(0, pool.Sum(a => a.Weight) - 1);
            foreach (var a in pool)
            {
                if (roll < a.Weight) { archetype[i] = a.Index; break; }
                roll -= a.Weight;
            }
        }
        // Guaranteed archetypes always appear: if the dice skipped one, the nearest eligible system that is not the only holder of another
        // guaranteed archetype takes it (the farthest system if none is eligible). No randomness, so unlimited content is unaffected.
        foreach (var g in fullPool.Where(a => a.Guaranteed))
        {
            if (archetype.Skip(1).Contains(g.Index)) continue;
            // A system is free to take over unless it is the only holder of another guaranteed archetype.
            var free = Enumerable.Range(1, n - 1)
                .Where(i => !content.Archetypes[archetype[i]].Guaranteed || archetype.Skip(1).Count(x => x == archetype[i]) > 1).ToList();
            var eligible = free.Where(i => g.AllowedAt(depth[i])).ToList();
            if (eligible.Count > 0) archetype[eligible.OrderBy(i => depth[i]).ThenBy(i => i).First()] = g.Index;
            else if (free.Count > 0) archetype[free.OrderByDescending(i => depth[i]).ThenBy(i => i).First()] = g.Index; // a shallow galaxy: the farthest system
        }

        var systems = new StarSystem[n];
        var deposits = new List<GeneratedDeposit>();
        var obstacles = new List<GeneratedObstacle>();
        for (int i = 0; i < n; i++)
        {
            var arch = content.Archetypes[archetype[i]];
            string name = $"{Starts[rng.Next(0, Starts.Length - 1)]}{Ends[rng.Next(0, Ends.Length - 1)]}-{i}";
            int star = wide is null ? 0 : ObstacleGenerator.PickStar(wide, seed, i, depth[i]);
            systems[i] = new StarSystem(i, i == 0 ? "Home" : name, pos[i].X, pos[i].Y, pos[i].Z, arch.Index, lanes[i].OrderBy(x => x).ToArray(), star);
            int firstDeposit = deposits.Count;

            foreach (var spec in arch.Deposits)
            {
                int count = rng.Next(spec.CountMin, spec.CountMax);
                for (int k = 0; k < count; k++)
                {
                    int amount = rng.Next(spec.AmountMin, spec.AmountMax);
                    int dLo = arch.DistanceMin, dHi = arch.DistanceMax;
                    if (wide is not null) (dLo, dHi) = arch.HasWide ? (arch.WideDistanceMin, arch.WideDistanceMax) : (arch.DistanceMin * 4, arch.DistanceMax * 4);
                    int d = rng.Next(dLo, dHi);
                    if (wide is not null && i != 0)
                    {
                        // Veins grow with the star's richness, the jumps from home and the distance from the anchor.
                        long pct = (long)wide.StarTypes[star].RichnessPercent * (100 + wide.DepthRichnessPercent * depth[i]) / 100;
                        pct = pct * (100 + (dHi > dLo ? wide.OuterRichnessPercent * (d - dLo) / (dHi - dLo) : 0)) / 100;
                        amount = (int)Math.Max(1, (long)amount * pct / 100);
                    }
                    int ox = rng.Next(-d, d);
                    int oz = ISqrt((long)d * d - (long)ox * ox) * (rng.Next(0, 1) == 0 ? -1 : 1);
                    int oy = rng.Next(-d / 4, d / 4);
                    int density = spec.HasDensity ? rng.Next(spec.DensityMin, spec.DensityMax) : DepositRules.DefaultDensity;
                    deposits.Add(new GeneratedDeposit(i, spec.Item, amount, pos[i].X + ox, pos[i].Y + oy, pos[i].Z + oz, density));
                }
            }

            if (wide is not null)
                obstacles.AddRange(ObstacleGenerator.Generate(wide, seed, systems[i], depth[i], deposits.Skip(firstDeposit).Select(d => (d.X, d.Y, d.Z)).ToList()));
        }

        return new Galaxy(seed, rules, systems, deposits.ToArray(), obstacles.ToArray(), wide is not null);
    }

    private static long DistSq((int X, int Y, int Z) a, (int X, int Y, int Z) b)
    {
        long dx = a.X - b.X, dy = a.Y - b.Y, dz = a.Z - b.Z;
        return dx * dx + dy * dy + dz * dz;
    }

    /// <summary>Integer square root (floor).</summary>
    private static int ISqrt(long v)
    {
        if (v <= 0) return 0;
        long x = (long)Math.Sqrt(v);
        while (x * x > v) x--;
        while ((x + 1) * (x + 1) <= v) x++;
        return (int)x;
    }
}
