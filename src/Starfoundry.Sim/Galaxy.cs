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
public sealed record StarSystem(int Id, string Name, int X, int Y, int Z, int Archetype, int[] Lanes);

/// <summary>A deposit the generator created for a system; the world adds them in this order, so ids match indices.</summary>
public sealed record GeneratedDeposit(int System, int Item, int Amount, int X, int Y, int Z);

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

    private Galaxy(ulong seed, GalaxyRules rules, StarSystem[] systems, GeneratedDeposit[] deposits)
    {
        Seed = seed;
        Rules = rules;
        Systems = systems;
        Deposits = deposits;
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
    public static Galaxy Generate(ContentDb content, ulong seed)
    {
        var rules = content.Galaxy ?? throw new InvalidOperationException("The content defines no galaxy.");
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

        // Archetypes: home for system 0, a weighted pick for the others.
        var home = content.Archetypes.First(a => a.IsHome);
        var pool = content.Archetypes.Where(a => !a.IsHome && a.Weight > 0).ToList();
        int totalWeight = pool.Sum(a => a.Weight);
        var archetype = new int[n];
        archetype[0] = home.Index;
        for (int i = 1; i < n; i++)
        {
            int roll = rng.Next(0, totalWeight - 1);
            foreach (var a in pool)
            {
                if (roll < a.Weight) { archetype[i] = a.Index; break; }
                roll -= a.Weight;
            }
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

        var systems = new StarSystem[n];
        var deposits = new List<GeneratedDeposit>();
        for (int i = 0; i < n; i++)
        {
            var arch = content.Archetypes[archetype[i]];
            string name = $"{Starts[rng.Next(0, Starts.Length - 1)]}{Ends[rng.Next(0, Ends.Length - 1)]}-{i}";
            systems[i] = new StarSystem(i, i == 0 ? "Home" : name, pos[i].X, pos[i].Y, pos[i].Z, arch.Index, lanes[i].OrderBy(x => x).ToArray());

            foreach (var spec in arch.Deposits)
            {
                int count = rng.Next(spec.CountMin, spec.CountMax);
                for (int k = 0; k < count; k++)
                {
                    int amount = rng.Next(spec.AmountMin, spec.AmountMax);
                    int d = rng.Next(arch.DistanceMin, arch.DistanceMax);
                    int ox = rng.Next(-d, d);
                    int oz = ISqrt((long)d * d - (long)ox * ox) * (rng.Next(0, 1) == 0 ? -1 : 1);
                    int oy = rng.Next(-d / 4, d / 4);
                    deposits.Add(new GeneratedDeposit(i, spec.Item, amount, pos[i].X + ox, pos[i].Y + oy, pos[i].Z + oz));
                }
            }
        }

        return new Galaxy(seed, rules, systems, deposits.ToArray());
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
