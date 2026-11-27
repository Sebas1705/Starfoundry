using System.Diagnostics;
using Starfoundry.Sim;

/// <summary>
/// Measures the shipped clustered generator (ADR 0046, <see cref="ClusterField"/>) with the same method as the study: a far region, then every cluster whose
/// centre lies well inside it, with the rocks and ore it holds. Run with the section name <c>shipped</c>. Seed and region match the study.
/// </summary>
internal static class ShippedReport
{
    public static void Run(ContentDb content, FieldRules f, Galaxy galaxy, ulong seed, Action<string> p)
    {
        var c = f.Clusters ?? throw new InvalidOperationException("the content has no clusters");
        const int D = 100;
        p("## Shipped clustered generator (ADR 0046)");
        var tmp = new List<FieldRock>();
        int empty = 0, sectors = 0, ore80Sectors = 0;
        long totalRocks = 0, totalOre = 0, maxOre = 0;
        var perSector = new List<(long Ore, int Rocks)>();
        var radii = new List<double>();
        var sw = Stopwatch.StartNew();
        // far region: 40 x 6 x 40 sectors, 12000+ cells from home
        for (int x = 60; x < 100; x++) for (int y = -3; y < 3; y++) for (int z = 60; z < 100; z++)
        {
            tmp.Clear();
            ClusterField.Generate(f, c, galaxy.Systems, seed, x, y, z, D, tmp);
            sectors++;
            if (tmp.Count == 0) empty++;
            long ore = 0;
            foreach (var r in tmp) { ore += r.Amount; maxOre = Math.Max(maxOre, r.Amount); radii.Add(r.Radius / (double)Units.CellUnits); }
            totalRocks += tmp.Count; totalOre += ore;
            perSector.Add((ore, tmp.Count));
        }
        sw.Stop();
        perSector.Sort((a, b) => b.Ore.CompareTo(a.Ore));
        long acc = 0; int n80 = 0;
        foreach (var s in perSector) { acc += s.Ore; n80++; if (acc >= totalOre * 80 / 100) break; }
        ore80Sectors = n80;
        radii.Sort();
        p($"- {sectors} sectors: empty {100.0 * empty / sectors:F1}%, rocks per sector mean {totalRocks / (double)sectors:F2} max {perSector.Max(s => s.Rocks)}, 80% of the ore in {100.0 * ore80Sectors / sectors:F1}% of the sectors, largest rock {maxOre} ore, radius cells p50 {radii[radii.Count / 2]:F1} p99 {radii[radii.Count * 99 / 100]:F1} max {radii[^1]:F1}, {sw.Elapsed.TotalMilliseconds * 1000 / sectors:F1} microseconds per sector");

        // clusters well inside the region
        var inner = ClusterField.ParentsInBox(f, c, seed, 66 * 200, 94 * 200, -300, 300, 66 * 200, 94 * 200, D);
        var byCluster = inner.ToDictionary(q => q.Id, _ => new List<FieldRock>());
        for (int x = 60; x < 100; x++) for (int y = -3; y < 3; y++) for (int z = 60; z < 100; z++)
        {
            tmp.Clear();
            ClusterField.Generate(f, c, galaxy.Systems, seed, x, y, z, D, tmp);
            foreach (var r in tmp) if (r.Cluster >= 0 && byCluster.TryGetValue(r.Cluster, out var l)) l.Add(r);
        }
        p($"- {inner.Count} clusters whose centre is well inside the region");
        p("| richness | clusters | radius cells p50 | rocks p50 | rocks max | ore p50 | ore p90 | ore max | giants per cluster | biggest rock ore p50 |");
        p("| --- | --- | --- | --- | --- | --- | --- | --- | --- | --- |");
        double Pct(List<double> v, int q) { if (v.Count == 0) return 0; v.Sort(); return v[Math.Min(v.Count - 1, v.Count * q / 100)]; }
        foreach (var grp in inner.GroupBy(q => q.Richness).OrderBy(g => g.Key))
        {
            var rocks = grp.Select(q => (double)byCluster[q.Id].Count).ToList();
            var ore = grp.Select(q => (double)byCluster[q.Id].Sum(r => (long)r.Amount)).ToList();
            var giants = grp.Select(q => (double)byCluster[q.Id].Count(r => r.Mass >= c.GiantMass.Min)).ToList();
            var big = grp.Select(q => byCluster[q.Id].Count == 0 ? 0.0 : byCluster[q.Id].Max(r => r.Amount)).ToList();
            p($"| {grp.Key} | {grp.Count()} | {Pct(grp.Select(q => (double)q.Radius).ToList(), 50):F0} | {Pct(rocks.ToList(), 50):F0} | {rocks.Max():F0} | {Pct(ore.ToList(), 50):F0} | {Pct(ore.ToList(), 90):F0} | {ore.Max():F0} | {giants.Average():F1} | {Pct(big, 50):F0} |");
        }
        var allOre = inner.Select(q => (double)byCluster[q.Id].Sum(r => (long)r.Amount)).ToList();
        var allRocks = inner.Select(q => (double)byCluster[q.Id].Count).ToList();
        p($"- all clusters: ore p10 {Pct(allOre.ToList(), 10):F0} p50 {Pct(allOre.ToList(), 50):F0} p90 {Pct(allOre.ToList(), 90):F0} max {allOre.Max():F0}; rocks p10 {Pct(allRocks.ToList(), 10):F0} p50 {Pct(allRocks.ToList(), 50):F0} p90 {Pct(allRocks.ToList(), 90):F0} max {allRocks.Max():F0}");
        var densest = inner.OrderByDescending(q => byCluster[q.Id].Count).First();
        p($"- densest: id {densest.Id} at ({densest.X}, {densest.Y}, {densest.Z}) cells, richness {densest.Richness}, shape {densest.Shape}, radius {densest.Radius}, item {content.Items[densest.Item].Id}, {byCluster[densest.Id].Count} rocks, {byCluster[densest.Id].Sum(r => (long)r.Amount)} ore");

        // the nearest concentration from random points (8 or more rocks within 100 cells of one rock) is measured by the study; here the gap between clusters
        var all = ClusterField.ParentsInBox(f, c, seed, 66 * 200, 94 * 200, -300, 300, 66 * 200, 94 * 200, D);
        p($"- sample of the gap: {all.Count} parents in {28 * 200}x600x{28 * 200} cells");
        p("");
    }
}
