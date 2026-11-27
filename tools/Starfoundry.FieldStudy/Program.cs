using System.Diagnostics;
using System.Text;
using Starfoundry.Sim;

// Measurement scratch for docs/superpowers/specs/2026-11-27-resource-fields-design.md (a study, not a product change).
// Usage (from the repository root): dotnet run -c Release --project tools/Starfoundry.FieldStudy -- [content/core.json] [section]
// It measures the current rock field (ADR 0040) and prototypes candidate generators with the same statistics. Integer arithmetic only in the generators.

const ulong Seed = 20261126UL;
string path = args.Length > 0 ? args[0] : "content/core.json";
string only = args.Length > 1 ? args[1] : "";
var content = ContentDb.FromFile(path);
var F = content.World!.Field!;
var galaxy = Galaxy.Generate(content, Seed);

var gens = new List<Gen>
{
    new CurrentGen(F, galaxy, Seed),
    new NoiseGen(F, galaxy, Seed),
    new ClusterGen(F, galaxy, Seed, new ClusterParams("C basic clusters 800", Super: 800, RBase: 80, RPerRich: 40, RMaxReach: 520, Lam: 100, Shapes: false, Giants: false, RadiusPct: 100, BgPermille: 250)),
    new ClusterGen(F, galaxy, Seed, new ClusterParams("D full clusters 800", Super: 800, RBase: 80, RPerRich: 40, RMaxReach: 520, Lam: 100, Shapes: true, Giants: true, RadiusPct: 250, BgPermille: 250)),
    new ClusterGen(F, galaxy, Seed, new ClusterParams("E full clusters 1300", Super: 1300, RBase: 100, RPerRich: 55, RMaxReach: 700, Lam: 100, Shapes: true, Giants: true, RadiusPct: 250, BgPermille: 150)),
};

var sb = new StringBuilder();
void P(string s = "") { Console.WriteLine(s); sb.AppendLine(s); }

if (only is "" or "galaxy")
{
    P($"## Galaxy: {galaxy.Systems.Count} systems, x {galaxy.Systems.Min(s => s.X)}..{galaxy.Systems.Max(s => s.X)}, y {galaxy.Systems.Min(s => s.Y)}..{galaxy.Systems.Max(s => s.Y)}, z {galaxy.Systems.Min(s => s.Z)}..{galaxy.Systems.Max(s => s.Z)} cells; veins {galaxy.Deposits.Count}, ore in veins {galaxy.Deposits.Sum(d => (long)d.Amount)}, veins per system {galaxy.Deposits.Count / (double)galaxy.Systems.Count:F1}");
    P($"- field rules: sector {F.SectorCells}, load {F.LoadCells}, keep {F.KeepCells}, exclusion {F.ExclusionCells}, rocks {F.RocksMin}..{F.RocksMax}, mass {F.MassMin}..{F.MassMax} bias {F.MassBias}, max loaded {F.MaxLoaded}");
    P();
}
if (only is "" or "determinism") Determinism();
if (only is "" or "region") foreach (var g in gens) Region(g);
if (only is "" or "vantage") foreach (var g in gens) Vantage(g);
if (only is "" or "loaded") foreach (var g in gens) Loaded(g);
if (only is "" or "clusters") foreach (var g in gens.OfType<ClusterGen>()) Clusters(g);
if (only is "shipped") ShippedReport.Run(content, F, galaxy, Seed, P);
if (only is "" or "sim") SimCost();
return;

// ---- the simulation's own cost per loaded rock: the shipped generator with more rocks per sector, ship parked far from home ----------------------
void SimCost()
{
    P("## Simulation tick cost against loaded rocks (shipped generator, content edited to more rocks per sector, ship parked at 15000 cells)");
    var w0 = SimWorld.CreateGalaxy(content, GameMode.Sandbox, Seed, threats: false, stationRules: false, structures: true);
    int speed = w0.Network.ShipSpeedUnitsPerTick(0);
    P($"- ship speed: {speed} units per tick = {speed * Units.TicksPerSecond / (double)Units.CellUnits:F1} cells/s (boost x4 = {speed * 4 * Units.TicksPerSecond / (double)Units.CellUnits:F1} cells/s) at the start ship");
    P("| rocks per sector (max) | rocks loaded | ms per tick (mean of 600) | p99 ms | ms per tick without rocks |");
    P("| --- | --- | --- | --- | --- |");
    double baseline = 0;
    foreach (int max in new[] { 0, 5, 15, 40, 60 })
    {
        var root = System.Text.Json.Nodes.JsonNode.Parse(File.ReadAllText(path))!.AsObject();
        root["world"]!["field"]!["rocks"] = new System.Text.Json.Nodes.JsonArray(0, max);
        var c = ContentDb.FromJson(root.ToJsonString());
        var w = SimWorld.CreateGalaxy(c, GameMode.Sandbox, Seed, threats: false, stationRules: false, structures: true);
        int ship = w.Network.ShipStructure(0);
        var s = w.Network.GetStructure(ship);
        var p = w.Network.GetPlayerInfo(0);
        long offX = p.X - s.OriginX, offY = p.Y - s.OriginY, offZ = p.Z - s.OriginZ;
        w.Network.SetStructureOrigin(ship, 15000L * Units.CellUnits - offX, 0 - offY, 15000L * Units.CellUnits - offZ);
        for (int i = 0; i < 60; i++) w.Step();
        var times = new List<double>();
        for (int i = 0; i < 600; i++)
        {
            long t0 = Stopwatch.GetTimestamp();
            w.Step();
            times.Add((Stopwatch.GetTimestamp() - t0) * 1000.0 / Stopwatch.Frequency);
        }
        double mean = times.Average();
        if (max == 0) baseline = mean;
        P($"| {max} | {w.Network.FieldLoadedRocks} | {mean:F3} | {Pct(times, 99):F3} | {baseline:F3} |");
    }
    P("- budget: one tick is 33.3 ms at 30 ticks per second");
    P();
}

// ---------------------------------------------------------------------------------------------------------------------------------------------
void Determinism()
{
    P("## Determinism (200 random sectors, two orders, fresh generators)");
    foreach (var g in gens)
    {
        var rng = new SplitMix64(7);
        var keys = new List<(int, int, int)>();
        for (int i = 0; i < 200; i++) keys.Add((rng.Next(-300, 300), rng.Next(-3, 3), rng.Next(-300, 300)));
        ulong Sum(Gen gg, IEnumerable<(int, int, int)> order)
        {
            ulong h = 1469598103934665603UL;
            var list = new List<Rk>();
            var map = new Dictionary<(int, int, int), ulong>();
            foreach (var k in order)
            {
                list.Clear();
                gg.Sector(k.Item1, k.Item2, k.Item3, list);
                ulong s = 0;
                foreach (var r in list) s = (s ^ (ulong)((long)r.X * 31 + r.Y * 131 + r.Z * 1031 + r.Mass * 7 + r.Item + r.Ore * 13L + r.Dens * 17L)) * 1099511628211UL;
                map[k] = s;
            }
            foreach (var k in keys.OrderBy(k => k)) h = (h ^ map[k]) * 1099511628211UL;
            return h;
        }
        var g2 = g.Fresh();
        ulong a = Sum(g, keys), b = Sum(g2, Enumerable.Reverse(keys));
        P($"- {g.Name}: {(a == b ? "same" : "DIFFERENT")} ({a:X16})");
    }
    P();
}

// ---- a big far region: per-sector statistics, size and ore histograms, nearest neighbours, concentration -------------------------------------
void Region(Gen g)
{
    const int X0 = 60, X1 = 100, Y0 = -3, Y1 = 3, Z0 = 60, Z1 = 100; // 12000 to 20000 cells from home: past every system, every ore type allowed
    var all = new List<Rk>();
    var perSectorCount = new List<int>();
    var perSectorOre = new List<long>();
    var times = new List<double>();
    var tmp = new List<Rk>();
    var sw = Stopwatch.StartNew();
    for (int x = X0; x < X1; x++)
        for (int y = Y0; y < Y1; y++)
            for (int z = Z0; z < Z1; z++)
            {
                tmp.Clear();
                long t0 = Stopwatch.GetTimestamp();
                g.Sector(x, y, z, tmp);
                times.Add((Stopwatch.GetTimestamp() - t0) * 1e6 / Stopwatch.Frequency);
                perSectorCount.Add(tmp.Count);
                long ore = 0;
                foreach (var r in tmp) ore += r.Ore;
                perSectorOre.Add(ore);
                all.AddRange(tmp);
            }
    sw.Stop();
    int sectors = perSectorCount.Count;
    long totalOre = all.Sum(r => (long)r.Ore);
    double volGc3 = sectors * 8e6 / 1e9; // billions of cubic cells
    P($"## Region 40x6x40 sectors far from home: {g.Name}");
    P($"- sectors {sectors}, rocks {all.Count}, rocks/sector mean {all.Count / (double)sectors:F2}, max {perSectorCount.Max()}, empty sectors {100.0 * perSectorCount.Count(c => c == 0) / sectors:F1}%");
    P($"- rocks/sector p50 {Pct(perSectorCount.Select(c => (double)c).ToList(), 50):F0}, p90 {Pct(perSectorCount.Select(c => (double)c).ToList(), 90):F0}, p99 {Pct(perSectorCount.Select(c => (double)c).ToList(), 99):F0}");
    P($"- ore total {totalOre}, per sector {totalOre / (double)sectors:F0}, per 1e9 cells^3 {totalOre / volGc3:F0}, mean ore per rock {totalOre / (double)Math.Max(1, all.Count):F0}");
    var sortedOre = perSectorOre.OrderByDescending(v => v).ToList();
    long acc = 0; int n80 = 0;
    foreach (long v in sortedOre) { acc += v; n80++; if (acc >= totalOre * 0.8) break; }
    P($"- 80% of the ore lies in {100.0 * n80 / sectors:F1}% of the sectors");
    P($"- generation: {sw.Elapsed.TotalMilliseconds / sectors * 1000:F1} us/sector mean, p99 {Pct(times, 99):F1} us, max {times.Max():F0} us");
    var ores = all.Select(r => r.Ore).OrderBy(v => v).ToList();
    int[] edges = { 25, 50, 100, 200, 500, 1000, 3000, int.MaxValue };
    var sbh = new StringBuilder("- ore histogram (rocks / share of ore): ");
    int lo = 0;
    foreach (int e in edges)
    {
        var bin = all.Where(r => r.Ore >= lo && r.Ore < e).ToList();
        sbh.Append($"[{lo}-{(e == int.MaxValue ? "inf" : e.ToString())}) {bin.Count} / {100.0 * bin.Sum(r => (long)r.Ore) / Math.Max(1, totalOre):F0}%; ");
        lo = e;
    }
    P(sbh.ToString());
    if (all.Count > 0)
    {
        P($"- top 1% of rocks hold {100.0 * ores.TakeLast(Math.Max(1, ores.Count / 100)).Sum(v => (long)v) / totalOre:F0}% of the ore, top 10% hold {100.0 * ores.TakeLast(Math.Max(1, ores.Count / 10)).Sum(v => (long)v) / totalOre:F0}%; largest rock ore {ores[^1]}");
        var rad = all.Select(r => r.Rad / (double)Units.CellUnits).ToList();
        P($"- radius in cells p10 {Pct(rad, 10):F1}, p50 {Pct(rad, 50):F1}, p90 {Pct(rad, 90):F1}, p99 {Pct(rad, 99):F1}, max {rad.Max():F1}");
        var dens = all.Select(r => (double)r.Dens).ToList();
        P($"- density % p10 {Pct(dens, 10):F0}, p50 {Pct(dens, 50):F0}, p90 {Pct(dens, 90):F0}");
        var byItem = all.GroupBy(r => r.Item).OrderByDescending(gp => gp.Count()).Select(gp => $"{content.Items[gp.Key].Id} {100.0 * gp.Count() / all.Count:F0}%");
        P($"- items: {string.Join(", ", byItem)}");
        // Nearest neighbours and concentration through a spatial hash of 100-cell bins.
        var hash = Bin(all, 100);
        var nn = new List<double>(); var neigh = new List<int>();
        foreach (var r in all)
        {
            double best = 1e9; int within100 = 0;
            foreach (var o in Near(hash, r.X, r.Y, r.Z, 100))
            {
                if (ReferenceEquals(o, null) || (o.X == r.X && o.Y == r.Y && o.Z == r.Z)) continue;
                double d = Dist(r, o);
                if (d < best) best = d;
                if (d <= 100) within100++;
            }
            nn.Add(Math.Min(best, 200)); neigh.Add(within100);
        }
        P($"- nearest-neighbour distance in cells (capped at 200): p10 {Pct(nn, 10):F0}, p50 {Pct(nn, 50):F0}, p90 {Pct(nn, 90):F0}, mean {nn.Select(v => Math.Min(v, 200)).Average():F0}");
        P($"- rocks with 8 or more neighbours within 100 cells (a concentration): {100.0 * neigh.Count(v => v >= 8) / all.Count:F0}%; mean neighbours within 100 cells {neigh.Average():F1}");
    }
    P();
}

// ---- vantage points at 0, 500, 1500, 5000 cells from home --------------------------------------------------------------------------------------
void Vantage(Gen g)
{
    P($"## Vantage points around home: {g.Name}");
    P("| distance | scan 300 rocks mean / p10 / p50 / p90 / max | no rock in scan | scan ore mean | nav 400 rocks mean | rocks 100+ ore in scan | nearest rock (cells) p50 / p90 | nearest concentration (>=8 neighbours in 100, search 1500) p50 / p90 / none |");
    P("| --- | --- | --- | --- | --- | --- | --- | --- |");
    foreach (int dist in new[] { 0, 500, 1500, 5000 })
    {
        var rng = new SplitMix64((ulong)(dist + 99));
        int n = dist == 0 ? 1 : 300;
        var scan = new List<double>(); var nav = new List<double>(); var ore = new List<double>(); var big = new List<double>(); var nearest = new List<double>();
        var conc = new List<double>(); int concNone = 0, concTried = 0;
        for (int i = 0; i < n; i++)
        {
            (long x, long y, long z) c = (0, 0, 0);
            if (dist > 0)
            {
                long vx, vy, vz, l2;
                do { vx = rng.Next(-1000, 1000); vy = rng.Next(-1000, 1000); vz = rng.Next(-1000, 1000); l2 = vx * vx + vy * vy + vz * vz; } while (l2 > 1_000_000 || l2 < 40_000);
                long l = IMath.ISqrt(l2);
                c = (vx * dist / l, vy * dist / l, vz * dist / l);
            }
            var rocks = Gather(g, c.x, c.y, c.z, 400);
            int s = 0, v = 0, b = 0; long o = 0; double nr = 1e9;
            foreach (var r in rocks)
            {
                double d = DistP(r, c.x, c.y, c.z);
                if (d <= 300) { s++; o += r.Ore; if (r.Ore >= 100) b++; }
                if (d <= 400) v++;
            }
            if (rocks.Count == 0 || s == 0)
            {
                // nearest rock beyond 400: widen to 1500
                var wide = Gather(g, c.x, c.y, c.z, 1500);
                foreach (var r in wide) nr = Math.Min(nr, DistP(r, c.x, c.y, c.z));
            }
            else foreach (var r in rocks) nr = Math.Min(nr, DistP(r, c.x, c.y, c.z));
            scan.Add(s); nav.Add(v); ore.Add(o); big.Add(b); nearest.Add(nr);
            if (i < 40)
            {
                concTried++;
                var wide = Gather(g, c.x, c.y, c.z, 1500);
                var hash = Bin(wide, 100);
                double best = 1e9;
                foreach (var r in wide)
                {
                    double d = DistP(r, c.x, c.y, c.z);
                    if (d >= best) continue;
                    int nb = 0;
                    foreach (var o2 in Near(hash, r.X, r.Y, r.Z, 100)) if (Dist(r, o2) <= 100) nb++;
                    if (nb - 1 >= 8) best = d;
                }
                if (best > 1e8) concNone++; else conc.Add(best);
            }
        }
        string conctxt = conc.Count > 0 ? $"{Pct(conc, 50):F0} / {Pct(conc, 90):F0}" : "-";
        P($"| {dist} | {scan.Average():F1} / {Pct(scan, 10):F0} / {Pct(scan, 50):F0} / {Pct(scan, 90):F0} / {scan.Max():F0} | {100.0 * scan.Count(v => v == 0) / n:F0}% | {ore.Average():F0} | {nav.Average():F1} | {big.Average():F1} | {Pct(nearest, 50):F0} / {Pct(nearest, 90):F0} | {conctxt} / none {100 * concNone / Math.Max(1, concTried)}% |");
    }
    P();
}

// ---- rocks loaded by the streaming rule (sectors whose box is within 360 cells), generation cost of a crossing ---------------------------------
void Loaded(Gen g)
{
    var rng = new SplitMix64(4242);
    var loaded = new List<double>(); var kept = new List<double>(); var sectorsLoaded = new List<double>();
    long cu = 1; // positions here are cells
    int N = 1500;
    for (int i = 0; i < N; i++)
    {
        long px = rng.Next(12000, 20000), py = rng.Next(-300, 300), pz = rng.Next(12000, 20000);
        int a = 0, k = 0, sc = 0;
        int x0 = (int)IMath.FloorDiv(px - 360, 200), x1 = (int)IMath.FloorDiv(px + 360, 200);
        int y0 = (int)IMath.FloorDiv(py - 360, 200), y1 = (int)IMath.FloorDiv(py + 360, 200);
        int z0 = (int)IMath.FloorDiv(pz - 360, 200), z1 = (int)IMath.FloorDiv(pz + 360, 200);
        var tmp = new List<Rk>();
        for (int cx = x0; cx <= x1; cx++) for (int cy = y0; cy <= y1; cy++) for (int cz = z0; cz <= z1; cz++)
        {
            long dx = Out(px, cx * 200L, (cx + 1) * 200L), dy = Out(py, cy * 200L, (cy + 1) * 200L), dz = Out(pz, cz * 200L, (cz + 1) * 200L);
            long d2 = dx * dx + dy * dy + dz * dz;
            if (d2 > 360L * 360 * cu) continue;
            tmp.Clear(); g.Sector(cx, cy, cz, tmp); a += tmp.Count; sc++;
        }
        loaded.Add(a); sectorsLoaded.Add(sc);
        // kept set (480) bound
        int kx0 = (int)IMath.FloorDiv(px - 480, 200), kx1 = (int)IMath.FloorDiv(px + 480, 200);
        int ky0 = (int)IMath.FloorDiv(py - 480, 200), ky1 = (int)IMath.FloorDiv(py + 480, 200);
        int kz0 = (int)IMath.FloorDiv(pz - 480, 200), kz1 = (int)IMath.FloorDiv(pz + 480, 200);
        for (int cx = kx0; cx <= kx1; cx++) for (int cy = ky0; cy <= ky1; cy++) for (int cz = kz0; cz <= kz1; cz++)
        {
            long dx = Out(px, cx * 200L, (cx + 1) * 200L), dy = Out(py, cy * 200L, (cy + 1) * 200L), dz = Out(pz, cz * 200L, (cz + 1) * 200L);
            if (dx * dx + dy * dy + dz * dz > 480L * 480) continue;
            tmp.Clear(); g.Sector(cx, cy, cz, tmp); k += tmp.Count;
        }
        kept.Add(k);
    }
    P($"## Streaming (1500 random ship positions far from home): {g.Name}");
    P($"- sectors in the load sphere {sectorsLoaded.Average():F0}; rocks loaded mean {loaded.Average():F0}, p50 {Pct(loaded, 50):F0}, p90 {Pct(loaded, 90):F0}, p99 {Pct(loaded, 99):F0}, max {loaded.Max():F0} (cap {F.MaxLoaded}); rocks inside the keep sphere p99 {Pct(kept, 99):F0}, max {kept.Max():F0}");
    var one = new List<Rk>();
    var sw = Stopwatch.StartNew();
    int reps = 0;
    for (int cx = 70; cx < 90; cx++) for (int cy = -2; cy < 2; cy++) for (int cz = 70; cz < 90; cz++) { one.Clear(); g.Sector(cx, cy, cz, one); reps++; }
    sw.Stop();
    double perSector = sw.Elapsed.TotalMilliseconds / reps;
    P($"- generation {perSector * 1000:F1} us per sector; a 200-cell crossing loads about {sectorsLoaded.Average() / 3.0:F0} fresh sectors (one third of the sphere) = {perSector * sectorsLoaded.Average() / 3.0:F2} ms spread over the 15-tick passes; a teleport or jump loads all {sectorsLoaded.Average():F0} = {perSector * sectorsLoaded.Average():F1} ms");
    P();
}

void Clusters(ClusterGen g)
{
    // Clusters whose centre lies well inside the region of the other measurements, so none is cut by its border.
    var inner = g.ParentsInBox(66 * 200, 134 * 200, -300, 300, 66 * 200, 134 * 200);
    var tmp = new List<Rk>();
    var byCluster = new Dictionary<long, List<Rk>>();
    foreach (var pr in inner) byCluster[pr.Id] = new List<Rk>();
    for (int x = 60; x < 140; x++) for (int y = -3; y < 3; y++) for (int z = 60; z < 140; z++)
    {
        tmp.Clear(); g.Sector(x, y, z, tmp);
        foreach (var r in tmp) if (r.Cl >= 0 && byCluster.TryGetValue(r.Cl, out var l)) l.Add(r);
    }
    P($"## Clusters: {g.Name}");
    P($"- {inner.Count} clusters in the inner box (68x68 sectors horizontally, 6 vertically = {68 * 68 * 6 * 8e6 / 1e9:F1} billion cells^3): one per {68 * 68 * 6 * 8e6 / 1e9 / Math.Max(1, inner.Count) * 1000:F0} million cells^3, mean spacing about {Math.Pow(68 * 68 * 6 * 8e6 / Math.Max(1, inner.Count), 1.0 / 3):F0} cells");
    P("| richness | clusters | radius cells p50 | rocks p50 | rocks max | ore p50 | ore p90 | ore max | giants per cluster | biggest rock ore p50 |");
    P("| --- | --- | --- | --- | --- | --- | --- | --- | --- | --- |");
    foreach (var grp in inner.GroupBy(p => p.Rich).OrderBy(gp => gp.Key))
    {
        var rocks = grp.Select(p => byCluster[p.Id].Count).Select(v => (double)v).ToList();
        var ore = grp.Select(p => (double)byCluster[p.Id].Sum(r => (long)r.Ore)).ToList();
        var giants = grp.Select(p => (double)byCluster[p.Id].Count(r => r.Mass >= 14000)).ToList();
        var bigOre = grp.Select(p => (double)(byCluster[p.Id].Count == 0 ? 0 : byCluster[p.Id].Max(r => r.Ore))).ToList();
        P($"| {grp.Key} | {grp.Count()} | {Pct(grp.Select(p => (double)p.R).ToList(), 50):F0} | {Pct(rocks, 50):F0} | {rocks.Max():F0} | {Pct(ore, 50):F0} | {Pct(ore, 90):F0} | {ore.Max():F0} | {giants.Average():F1} | {Pct(bigOre, 50):F0} |");
    }
    var allOre = inner.Select(p => (double)byCluster[p.Id].Sum(r => (long)r.Ore)).ToList();
    var allRocks = inner.Select(p => (double)byCluster[p.Id].Count).ToList();
    var shapes = inner.GroupBy(p => p.Shape).OrderBy(gp => gp.Key).Select(gp => $"shape {gp.Key}: {gp.Count()} clusters, ore p50 {Pct(gp.Select(p => (double)byCluster[p.Id].Sum(r => (long)r.Ore)).ToList(), 50):F0}");
    P($"- all clusters: ore p10 {Pct(allOre, 10):F0}, p50 {Pct(allOre, 50):F0}, p90 {Pct(allOre, 90):F0}, max {allOre.Max():F0}; rocks p10 {Pct(allRocks, 10):F0}, p50 {Pct(allRocks, 50):F0}, p90 {Pct(allRocks, 90):F0}, max {allRocks.Max():F0}");
    P($"- {string.Join("; ", shapes)}");
    var items = inner.GroupBy(p => p.Item).OrderByDescending(gp => gp.Count()).Select(gp => $"{content.Items[gp.Key].Id} {gp.Count()}");
    P($"- dominant ore of the clusters: {string.Join(", ", items)}");
    P();
}

// ---- helpers ------------------------------------------------------------------------------------------------------------------------------------
List<Rk> Gather(Gen g, long cx, long cy, long cz, int radius)
{
    var res = new List<Rk>();
    var tmp = new List<Rk>();
    int x0 = (int)IMath.FloorDiv(cx - radius, 200), x1 = (int)IMath.FloorDiv(cx + radius, 200);
    int y0 = (int)IMath.FloorDiv(cy - radius, 200), y1 = (int)IMath.FloorDiv(cy + radius, 200);
    int z0 = (int)IMath.FloorDiv(cz - radius, 200), z1 = (int)IMath.FloorDiv(cz + radius, 200);
    for (int x = x0; x <= x1; x++) for (int y = y0; y <= y1; y++) for (int z = z0; z <= z1; z++)
    {
        tmp.Clear(); g.Sector(x, y, z, tmp);
        foreach (var r in tmp) if (DistP(r, cx, cy, cz) <= radius) res.Add(r);
    }
    return res;
}

static long Out(long v, long lo, long hi) => v < lo ? lo - v : v > hi ? v - hi : 0;
static double Dist(Rk a, Rk b) { double dx = a.X - b.X, dy = a.Y - b.Y, dz = a.Z - b.Z; return Math.Sqrt(dx * dx + dy * dy + dz * dz); }
static double DistP(Rk a, long x, long y, long z) { double dx = a.X - x, dy = a.Y - y, dz = a.Z - z; return Math.Sqrt(dx * dx + dy * dy + dz * dz); }
static double Pct(List<double> v, int p) { if (v.Count == 0) return 0; var s = v.OrderBy(x => x).ToList(); return s[Math.Min(s.Count - 1, (int)((long)s.Count * p / 100))]; }
static Dictionary<(int, int, int), List<Rk>> Bin(List<Rk> rocks, int size)
{
    var h = new Dictionary<(int, int, int), List<Rk>>();
    foreach (var r in rocks)
    {
        var k = ((int)IMath.FloorDiv(r.X, size), (int)IMath.FloorDiv(r.Y, size), (int)IMath.FloorDiv(r.Z, size));
        if (!h.TryGetValue(k, out var l)) h[k] = l = new List<Rk>();
        l.Add(r);
    }
    return h;
}
static IEnumerable<Rk> Near(Dictionary<(int, int, int), List<Rk>> h, long x, long y, long z, int size)
{
    int bx = (int)IMath.FloorDiv(x, size), by = (int)IMath.FloorDiv(y, size), bz = (int)IMath.FloorDiv(z, size);
    for (int i = -1; i <= 1; i++) for (int j = -1; j <= 1; j++) for (int k = -1; k <= 1; k++)
        if (h.TryGetValue((bx + i, by + j, bz + k), out var l)) foreach (var r in l) yield return r;
}

// =============================================================================================================================================
// Generators. Positions in cells, radius in units (240 per cell), as in the simulation.
// =============================================================================================================================================
internal readonly record struct Rk(long Cl, int Item, int X, int Y, int Z, int Mass, int Dens, int Rad, int Ore);

internal static class IMath
{
    public static long ISqrt(long v)
    {
        if (v <= 0) return 0;
        long r = (long)Math.Sqrt(v);
        while (r * r > v) r--;
        while ((r + 1) * (r + 1) <= v) r++;
        return r;
    }
    public static long ICbrt(long v)
    {
        if (v <= 0) return 0;
        long r = (long)Math.Cbrt(v);
        while (r * r * r > v) r--;
        while ((r + 1) * (r + 1) * (r + 1) <= v) r++;
        return r;
    }
    public static long FloorDiv(long a, long b) => a / b - (a % b < 0 ? 1 : 0);
    public static ulong Hash(ulong seed, ulong salt, int a, int b, int c)
    {
        ulong s = new SplitMix64(seed ^ salt ^ ((ulong)(uint)a * 0x9E37_79B9_7F4A_7C15UL)).Next();
        s = new SplitMix64(s ^ ((ulong)(uint)b * 0xC2B2_AE3D_27D4_EB4FUL)).Next();
        s = new SplitMix64(s ^ ((ulong)(uint)c * 0x1656_67B1_9E37_79F9UL)).Next();
        return s;
    }
}

internal abstract class Gen
{
    protected readonly FieldRules F;
    protected readonly Galaxy G;
    protected readonly ulong Seed;
    protected Gen(FieldRules f, Galaxy g, ulong seed) { F = f; G = g; Seed = seed; }
    public abstract string Name { get; }
    public abstract void Sector(int cx, int cy, int cz, List<Rk> into);
    public abstract Gen Fresh();

    protected bool NearAnchor(long x, long y, long z)
    {
        long e2 = (long)F.ExclusionCells * F.ExclusionCells;
        foreach (var s in G.Systems)
        {
            long dx = x - s.X, dy = y - s.Y, dz = z - s.Z;
            if (dx * dx + dy * dy + dz * dz < e2) return true;
        }
        return false;
    }

    protected int PickItem(int roll, long fromHome)
    {
        long total = 0;
        foreach (var i in F.Items) if (i.MinDistanceCells <= fromHome) total += i.Weight;
        long r = roll * total / 1_000_000;
        foreach (var i in F.Items)
        {
            if (i.MinDistanceCells > fromHome) continue;
            if (r < i.Weight) return i.Item;
            r -= i.Weight;
        }
        return F.Items[0].Item;
    }

    protected static int ReachCells(int radiusUnits) => (int)(((long)radiusUnits * 160 / 100 + Units.CellUnits - 1) / Units.CellUnits);

    protected static bool Clear(List<Rk> into, int first, long x, long y, long z, int reach)
    {
        for (int j = first; j < into.Count; j++)
        {
            var o = into[j];
            long need = reach + ReachCells(o.Rad) + FieldRules.GapCells;
            long dx = x - o.X, dy = y - o.Y, dz = z - o.Z;
            if (dx * dx + dy * dy + dz * dz < need * need) return false;
        }
        return true;
    }

    protected int MassDraw(int u, int bias, int min, int max)
    {
        long val = 1_000_000;
        for (int b = 0; b < bias; b++) val = val * u / 1_000_000;
        return (int)(min + (long)(max - min) * val / 1_000_000);
    }
}

/// <summary>The generator of ADR 0040 as shipped (called as is).</summary>
internal sealed class CurrentGen : Gen
{
    public CurrentGen(FieldRules f, Galaxy g, ulong s) : base(f, g, s) { }
    public override string Name => "A current (ADR 0040)";
    public override Gen Fresh() => new CurrentGen(F, G, Seed);
    private readonly List<FieldRock> _tmp = new();
    public override void Sector(int cx, int cy, int cz, List<Rk> into)
    {
        _tmp.Clear();
        SectorField.Generate(F, G.Systems, Seed, cx, cy, cz, 100, _tmp);
        foreach (var r in _tmp) into.Add(new Rk(-1, r.Item, r.X, r.Y, r.Z, r.Mass, r.Density, r.Radius, r.Amount));
    }
}

/// <summary>Candidate B: the same rocks, but a low-frequency 3D value noise thins them out, so there are voids and ridges (no cluster identity).</summary>
internal sealed class NoiseGen : Gen
{
    private const ulong Salt = 0xB01D_0B01_5EED_0001UL;
    public NoiseGen(FieldRules f, Galaxy g, ulong s) : base(f, g, s) { }
    public override string Name => "B noise-thinned (value noise 700 + 260 cells)";
    public override Gen Fresh() => new NoiseGen(F, G, Seed);

    private int Lattice(int ix, int iy, int iz, ulong salt) => (int)(IMath.Hash(Seed, salt, ix, iy, iz) % 1001);

    private static int Smooth(int t) => (int)((long)t * t * (3000 - 2 * t) / 1_000_000);

    /// <summary>Value noise 0..1000 with a lattice of <paramref name="l"/> cells.</summary>
    private int Noise(long x, long y, long z, int l, ulong salt)
    {
        int ix = (int)IMath.FloorDiv(x, l), iy = (int)IMath.FloorDiv(y, l), iz = (int)IMath.FloorDiv(z, l);
        int sx = Smooth((int)((x - (long)ix * l) * 1000 / l)), sy = Smooth((int)((y - (long)iy * l) * 1000 / l)), sz = Smooth((int)((z - (long)iz * l) * 1000 / l));
        long Lerp(long a, long b, int t) => a + (b - a) * t / 1000;
        long c00 = Lerp(Lattice(ix, iy, iz, salt), Lattice(ix + 1, iy, iz, salt), sx);
        long c10 = Lerp(Lattice(ix, iy + 1, iz, salt), Lattice(ix + 1, iy + 1, iz, salt), sx);
        long c01 = Lerp(Lattice(ix, iy, iz + 1, salt), Lattice(ix + 1, iy, iz + 1, salt), sx);
        long c11 = Lerp(Lattice(ix, iy + 1, iz + 1, salt), Lattice(ix + 1, iy + 1, iz + 1, salt), sx);
        return (int)Lerp(Lerp(c00, c10, sy), Lerp(c01, c11, sy), sz);
    }

    public override void Sector(int cx, int cy, int cz, List<Rk> into)
    {
        ulong s = IMath.Hash(Seed, 0xB01D_5EC7_0000_0001UL, cx, cy, cz);
        var rng = new SplitMix64(s);
        const int M = 24; // candidates per sector, the peak is 24 rocks per sector (thinned by the noise to a few percent in the voids)
        long sector = F.SectorCells, margin = F.MarginCells;
        int span = (int)(sector - 2L * margin) - 1;
        long minX = cx * sector + margin, minY = cy * sector + margin, minZ = cz * sector + margin;
        int first = into.Count;
        for (int k = 0; k < M; k++)
        {
            int itemRoll = rng.Next(0, 999_999), u = rng.Next(0, 1_000_000), densRoll = rng.Next(0, 999), accRoll = rng.Next(0, 999);
            long x = minX + rng.Next(0, span), y = minY + rng.Next(0, span), z = minZ + rng.Next(0, span);
            int n = (2 * Noise(x, y, z, 700, 11) + Noise(x, y, z, 260, 12)) / 3;
            const int thr = 560;
            int f = n <= thr ? 8 : Math.Max(8, (int)(Math.Min(1000L, (long)(n - thr) * 1000 / (1000 - thr + 100)) * Math.Min(1000L, (long)(n - thr) * 1000 / (1000 - thr + 100)) / 1000));
            if (accRoll >= f) continue;
            if (NearAnchor(x, y, z)) continue;
            int mass = MassDraw(u, F.MassBias, F.MassMin, F.MassMax);
            int radius = FieldRules.RadiusOfMass(mass);
            int reach = ReachCells(radius);
            if (!Clear(into, first, x, y, z, reach)) continue;
            long fromHome = IMath.ISqrt(x * x + y * y + z * z);
            // ore domains: one item per 1500-cell lattice cell
            ulong dom = IMath.Hash(Seed, 0xD0_0A11, (int)IMath.FloorDiv(x, 1500), (int)IMath.FloorDiv(y, 1500), (int)IMath.FloorDiv(z, 1500));
            int item = (dom % 100) < 70 ? PickItem((int)(dom / 100 % 1_000_000), fromHome) : PickItem(itemRoll, fromHome);
            int dens = Math.Clamp(F.DensityMin + n * (F.DensityMax - F.DensityMin) / 1000 + densRoll % 31 - 15, F.DensityMin, F.DensityMax);
            into.Add(new Rk(-1, item, (int)x, (int)y, (int)z, mass, dens, radius, F.AmountOf(mass, dens)));
        }
    }
}

internal sealed record ClusterParams(string Name, int Super, int RBase, int RPerRich, int RMaxReach, int Lam, bool Shapes, bool Giants, int RadiusPct, int BgPermille);

internal sealed record Parent(long Id, int X, int Y, int Z, int R, int Rich, int Item, int Shape, int Axis, int LamPermille);

/// <summary>
/// Candidates C and D: Poisson-cluster parents, one to two per super-sector (decided from the super-sector coordinates alone), each with a centre, radius,
/// richness, dominant ore and shape. A sector's rocks come from an inhomogeneous Poisson process thinned by the summed falloff of the parents that reach
/// the sector; nothing but the hash of integers is read, so any visit order gives the same rocks.
/// </summary>
internal sealed class ClusterGen : Gen
{
    private readonly ClusterParams _p;
    private readonly Dictionary<(int, int, int), Parent[]> _cache = new();
    private const ulong Salt = 0xC1A5_7E25_0000_0001UL;
    public ClusterGen(FieldRules f, Galaxy g, ulong s, ClusterParams p) : base(f, g, s) { _p = p; }
    public override string Name => _p.Name;
    public override Gen Fresh() => new ClusterGen(F, G, Seed, _p);

    private static readonly int[] RichWeights = { 35, 30, 20, 10, 5 }; private static readonly int[] LamTable = { 24, 32, 44, 56, 70 }; /* rocks per sector at the core, by richness */ // richness 1..5, per cent

    private Parent[] ParentsOf(int sx, int sy, int sz)
    {
        if (_cache.TryGetValue((sx, sy, sz), out var cached)) return cached;
        var rng = new SplitMix64(IMath.Hash(Seed, Salt, sx, sy, sz));
        int roll = rng.Next(0, 99);
        int count = roll < 30 ? 0 : roll < 80 ? 1 : 2; // mean 0.9 per super-sector
        var list = new List<Parent>();
        int S = _p.Super;
        for (int i = 0; i < count; i++)
        {
            int x = sx * S + rng.Next(0, S - 1), y = sy * S + rng.Next(0, S - 1), z = sz * S + rng.Next(0, S - 1);
            int ru = rng.Next(0, 1000);
            int r = 0; // set below, once the richness is known
            int rr = rng.Next(0, 99), rich = 1, acc = 0;
            for (int w = 0; w < RichWeights.Length; w++) { acc += RichWeights[w]; if (rr < acc) { rich = w + 1; break; } }
            int itemRoll = rng.Next(0, 999_999);
            long fromHome = IMath.ISqrt((long)x * x + (long)y * y + (long)z * z);
            int item = PickItem(itemRoll, fromHome);
            r = (int)((long)(_p.RBase + _p.RPerRich * rich) * (800 + 400 * ru / 1000) / 1000); /* radius grows with richness, 80 to 120 per cent */ int shape = 0, axis = 0;
            if (_p.Shapes)
            {
                int sr = rng.Next(0, 99);
                shape = sr < 50 ? 0 : sr < 80 ? 1 : 2; if (shape == 1) r = r * 3 / 2; // sphere, disc (belt, wider so it holds as many rocks), shell
                axis = rng.Next(0, 2);
            }
            else { rng.Next(0, 99); rng.Next(0, 2); }
            long id = ((long)i << 60) ^ ((long)(sx + 100000) << 40) ^ ((long)(sy + 100000) << 20) ^ (long)(sz + 100000);
            list.Add(new Parent(id, x, y, z, r, rich, item, shape, axis, LamTable[rich - 1] * _p.Lam * 10));
        }
        var arr = list.ToArray();
        _cache[(sx, sy, sz)] = arr;
        return arr;
    }

    public List<Parent> ParentsInBox(int x0, int x1, int y0, int y1, int z0, int z1)
    {
        var res = new List<Parent>();
        int S = _p.Super;
        for (int sx = (int)IMath.FloorDiv(x0, S); sx <= (int)IMath.FloorDiv(x1, S); sx++)
            for (int sy = (int)IMath.FloorDiv(y0, S); sy <= (int)IMath.FloorDiv(y1, S); sy++)
                for (int sz = (int)IMath.FloorDiv(z0, S); sz <= (int)IMath.FloorDiv(z1, S); sz++)
                    foreach (var p in ParentsOf(sx, sy, sz)) if (p.X >= x0 && p.X <= x1 && p.Y >= y0 && p.Y <= y1 && p.Z >= z0 && p.Z <= z1) res.Add(p);
        return res;
    }

    /// <summary>Falloff of a parent at a point, 0..1000.</summary>
    private int W(Parent p, long x, long y, long z)
    {
        long dx = x - p.X, dy = y - p.Y, dz = z - p.Z;
        if (p.Shape == 1) { if (p.Axis == 0) dy *= 4; else dx *= 4; } // a disc: four times thinner along one axis
        long d2 = dx * dx + dy * dy + dz * dz;
        long r2 = (long)p.R * p.R;
        if (d2 >= r2) return 0;
        if (p.Shape == 2)
        {
            long xx = IMath.ISqrt(d2) * 1000 / p.R; // 0..1000
            long u = (xx - 700) * 1000 / 300;
            if (Math.Abs(u) >= 1000) return 0;
            long t = 1000 - u * u / 1000;
            return (int)Math.Min(1000L, t * 3 / 2);
        }
        long q = d2 * 1000 / r2; // x^2 in permille
        long tt = 1000 - q;
        return (int)Math.Min(1000L, tt * 3 / 2); // flat-top: full intensity out to x = 0.58, then falls to 0
    }

    private bool Reaches(Parent p, int cx, int cy, int cz)
    {
        long s = F.SectorCells;
        long dx = Out(p.X, cx * s, (cx + 1) * s), dy = Out(p.Y, cy * s, (cy + 1) * s), dz = Out(p.Z, cz * s, (cz + 1) * s);
        if (p.Shape == 1) { if (p.Axis == 0) dy *= 4; else dx *= 4; }
        return dx * dx + dy * dy + dz * dz < (long)p.R * p.R;
    }

    private static long Out(long v, long lo, long hi) => v < lo ? lo - v : v > hi ? v - hi : 0;

    public override void Sector(int cx, int cy, int cz, List<Rk> into)
    {
        long sector = F.SectorCells;
        int S = _p.Super;
        long x0 = cx * sector, y0 = cy * sector, z0 = cz * sector;
        var near = new List<Parent>(4);
        int reachMax = _p.RMaxReach;
        for (int sx = (int)IMath.FloorDiv(x0 - reachMax, S); sx <= (int)IMath.FloorDiv(x0 + sector + reachMax, S); sx++)
            for (int sy = (int)IMath.FloorDiv(y0 - reachMax, S); sy <= (int)IMath.FloorDiv(y0 + sector + reachMax, S); sy++)
                for (int sz = (int)IMath.FloorDiv(z0 - reachMax, S); sz <= (int)IMath.FloorDiv(z0 + sector + reachMax, S); sz++)
                    foreach (var p in ParentsOf(sx, sy, sz)) if (Reaches(p, cx, cy, cz)) near.Add(p);
        near.Sort((a, b) => a.Id.CompareTo(b.Id));

        var rng = new SplitMix64(IMath.Hash(Seed, Salt ^ 0x5EC7, cx, cy, cz));
        int first = into.Count;
        long limit = F.WorldLimitCells - 100;

        // Giants first, so they claim their room: one or two candidates per sector, kept 30 cells from the faces (a giant reaches up to about 26).
        if (_p.Giants && near.Count > 0)
        {
            const int GM = 2, GReach = 30;
            long sumG = 0;
            foreach (var p in near) sumG += p.LamPermille * 8 / 1000; // giants: 0.8 per cent of the rock rate of the cluster
            for (int k = 0; k < GM; k++)
            {
                int accRoll = rng.Next(0, GM * 1000 - 1), massRoll = rng.Next(0, 1000), densRoll = rng.Next(0, 999);
                long x = x0 + GReach + rng.Next(0, (int)sector - 2 * GReach - 1), y = y0 + GReach + rng.Next(0, (int)sector - 2 * GReach - 1), z = z0 + GReach + rng.Next(0, (int)sector - 2 * GReach - 1);
                long lam = 0; Parent? best = null; long bestW = -1;
                foreach (var p in near)
                {
                    long w = W(p, x, y, z) * p.LamPermille * 8 / 1_000_000;
                    lam += w;
                    if (w > bestW) { bestW = w; best = p; }
                }
                if (accRoll >= lam || best is null || Math.Abs(x) > limit || Math.Abs(y) > limit || Math.Abs(z) > limit || NearAnchor(x, y, z)) continue;
                int mass = 14000 + (int)(106_000L * massRoll * massRoll / 1_000_000); // 14000 to 120000, most near the low end
                int dens = Math.Clamp(50 + 25 * best.Rich + densRoll % 41 - 20, F.DensityMin, F.DensityMax);
                int radius = (int)((long)FieldRules.RadiusOfMass(mass) * _p.RadiusPct / 100);
                int reach = ReachCells(radius);
                if (!Clear(into, first, x, y, z, reach)) continue;
                into.Add(new Rk(best.Id, best.Item, (int)x, (int)y, (int)z, mass, dens, radius, F.AmountOf(mass, dens)));
            }
        }

        long sumLam = _p.BgPermille;
        foreach (var p in near) sumLam += p.LamPermille;
        int M = (int)Math.Min(120, (sumLam + 999) / 1000);
        if (M == 0) return;
        const int Margin = 14; // a regular rock reaches at most about 13 cells at the radius scale 250
        int span = (int)sector - 2 * Margin - 1;
        for (int k = 0; k < M; k++)
        {
            int accRoll = rng.Next(0, M * 1000 - 1), itemRoll = rng.Next(0, 999_999), u = rng.Next(0, 1_000_000), classRoll = rng.Next(0, 999), densRoll = rng.Next(0, 999), mixRoll = rng.Next(0, 99);
            long x = x0 + Margin + rng.Next(0, span), y = y0 + Margin + rng.Next(0, span), z = z0 + Margin + rng.Next(0, span);
            long lam = _p.BgPermille; Parent? best = null; long bestW = -1;
            foreach (var p in near)
            {
                long w = (long)W(p, x, y, z) * p.LamPermille / 1000;
                lam += w;
                if (w > bestW) { bestW = w; best = p; }
            }
            if (accRoll >= lam) continue;
            if (Math.Abs(x) > limit || Math.Abs(y) > limit || Math.Abs(z) > limit || NearAnchor(x, y, z)) continue;
            int rich = best is null || bestW <= _p.BgPermille / 2 ? 0 : best.Rich;
            int mass;
            if (_p.Giants)
            {
                // size classes: small 300-1200, medium 1200-4000, large 4000-14000; richer clusters hold more large rocks
                int largeShare = 40 + 10 * rich, mediumShare = 280;
                mass = classRoll < 1000 - mediumShare - largeShare ? 300 + (int)(900L * u / 1_000_000) : classRoll < 1000 - largeShare ? 1200 + (int)(2800L * u / 1_000_000) : 4000 + (int)(10000L * u / 1_000_000 * u / 1_000_000);
            }
            else mass = MassDraw(u, F.MassBias, F.MassMin, F.MassMax);
            int radius = (int)((long)FieldRules.RadiusOfMass(mass) * _p.RadiusPct / 100);
            int reach = ReachCells(radius);
            if (reach > Margin) { mass = Math.Min(mass, 9000); radius = (int)((long)FieldRules.RadiusOfMass(mass) * _p.RadiusPct / 100); reach = ReachCells(radius); }
            if (!Clear(into, first, x, y, z, reach)) continue;
            long fromHome = IMath.ISqrt(x * x + y * y + z * z);
            int item = best is not null && rich > 0 && mixRoll < 75 ? best.Item : PickItem(itemRoll, fromHome);
            int dens = _p.Giants
                ? (rich > 0 ? Math.Clamp(50 + 25 * rich + densRoll % 41 - 20, F.DensityMin, F.DensityMax) : F.DensityMin + densRoll % 61)
                : F.DensityMin + densRoll * (F.DensityMax - F.DensityMin) / 999;
            into.Add(new Rk(rich > 0 && best is not null ? best.Id : -1, item, (int)x, (int)y, (int)z, mass, dens, radius, F.AmountOf(mass, dens)));
        }
    }
}
