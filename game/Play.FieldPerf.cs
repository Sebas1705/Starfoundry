using System;
using System.Collections.Generic;
using System.Linq;
using Godot;
using Starfoundry.Sim;

namespace Starfoundry.Game;

/// <summary>
/// <c>--field-perf</c>: what the renderer pays for the rock field at the centre of the biggest cluster nearby (ADR 0046). It finds the cluster of richness 5 with the most
/// rocks within 40,000 cells of home, puts the starter ship at the free spot nearest its centre, loads the sectors around it, scans so every rock shows, switches vertical sync
/// off, lets 120 frames pass while the batches build and then times 300 frames. It prints one line, <c>FIELDPERF ...</c>, with the nodes of the scene, the draw calls and objects
/// of a frame, the frame time and what the field itself holds, and quits. <c>--rock-nodes</c> draws every rock as its own node, as before the batches, to compare;
/// <c>--field-perf-shot=path</c> also saves the last frame. Nothing of this is a saved game.
/// </summary>
public partial class Play
{
    private bool _fieldPerf;
    private string? _fieldPerfShot;
    private int _fieldPerfFrame;
    private (int X, int Y, int Z, int Rocks, long Ore, int Richness) _fieldPerfSpot;
    private readonly List<double> _fpFrames = new(), _fpProcess = new(), _fpDraws = new(), _fpObjects = new(), _fpPrims = new();
    private double _fpNodes, _fpSyncMs, _fpGlowMs;
    private ulong _fpWallStart;

    private void FieldPerfSetup(string[] args)
    {
        _fieldPerf = true;
        _fieldPerfShot = args.FirstOrDefault(a => a.StartsWith("--field-perf-shot="))?["--field-perf-shot=".Length..];
        _wantCapture = false;
        _shotPhase = 3;
    }

    /// <summary>The richest cluster of the world within 40,000 cells that holds the most rocks, and the free spot nearest its centre.</summary>
    private (int X, int Y, int Z, int Rocks, long Ore, int Richness) FindDensestCluster()
    {
        var f = _content.World!.Field!;
        var c = f.Clusters ?? throw new InvalidOperationException("the content has no clusters");
        var systems = Net.Galaxy!.Systems;
        ulong seed = Net.Galaxy.Seed;
        int density = Net.Rules.LegacyResources ? 100 : Net.Rules.ResourceDensityPercent;
        var parents = ClusterField.ParentsInBox(f, c, seed, -40000, 40000, -3000, 3000, -40000, 40000, density).Where(p => p.Richness == 5).OrderByDescending(p => p.Radius).Take(12).ToList();
        (int, int, int, int, long, int) best = default;
        int bestRocks = -1;
        var tmp = new List<FieldRock>();
        foreach (var p in parents)
        {
            var rocks = new List<FieldRock>();
            int r = p.Radius + 40;
            int x0 = (int)Math.Floor((p.X - r) / 200.0), x1 = (int)Math.Floor((p.X + r) / 200.0), y0 = (int)Math.Floor((p.Y - r) / 200.0), y1 = (int)Math.Floor((p.Y + r) / 200.0);
            int z0 = (int)Math.Floor((p.Z - r) / 200.0), z1 = (int)Math.Floor((p.Z + r) / 200.0);
            for (int x = x0; x <= x1; x++) for (int y = y0; y <= y1; y++) for (int z = z0; z <= z1; z++)
            {
                tmp.Clear();
                ClusterField.Generate(f, c, systems, seed, x, y, z, density, tmp);
                foreach (var k in tmp) if (k.Cluster == p.Id) rocks.Add(k);
            }
            if (rocks.Count <= bestRocks) continue;
            bestRocks = rocks.Count;
            // the free spot nearest the centre: a grid around it, at least 30 cells from every rock (a giant is up to 16 cells wide)
            (int X, int Y, int Z) spot = (p.X, p.Y, p.Z);
            double bestD = double.MaxValue;
            for (int dx = -90; dx <= 90; dx += 15) for (int dy = -60; dy <= 60; dy += 15) for (int dz = -90; dz <= 90; dz += 15)
            {
                int sx = p.X + dx, sy = p.Y + dy, sz = p.Z + dz;
                double nearest = rocks.Min(k => Math.Sqrt((double)(k.X - sx) * (k.X - sx) + (double)(k.Y - sy) * (k.Y - sy) + (double)(k.Z - sz) * (k.Z - sz)));
                if (nearest < 30) continue;
                double d = dx * dx + dy * dy + dz * dz;
                if (d < bestD) { bestD = d; spot = (sx, sy, sz); }
            }
            best = (spot.X, spot.Y, spot.Z, rocks.Count, rocks.Sum(k => (long)k.Amount), p.Richness);
        }
        return best;
    }

    private void FieldPerfTick(double delta)
    {
        _fieldPerfFrame++;
        if (_fieldPerfFrame == 4)
        {
            _fieldPerfSpot = FindDensestCluster();
            var (x, y, z, _, _, _) = _fieldPerfSpot;
            int from = int.TryParse(System.Environment.GetCommandLineArgs().FirstOrDefault(a => a.StartsWith("--field-perf-from="))?["--field-perf-from=".Length..], out int fromCells) ? fromCells : 0; // --field-perf-from=N: N cells away from the centre, looking at it
            Teleport(from > 0 ? View.FromCells(x - from, y, z) : View.FromCells(x, y, z));
            _world.Step(20 * 15);
            _world.Submit(Player, new Scan());
            _world.Step(2);
            LookFromSeat(from > 0 ? View.FromCells(x, y, z) : View.FromCells(x + 60, y, z - 60));
            DisplayServer.WindowSetVsyncMode(DisplayServer.VSyncMode.Disabled); // measure what the work costs, not the refresh rate
            return;
        }
        if (_fieldPerfFrame <= 4 + 120) return;
        if (_fpFrames.Count == 0) _fpWallStart = Time.GetTicksMsec();
        _fpFrames.Add(delta * 1000.0);
        _fpProcess.Add(Performance.GetMonitor(Performance.Monitor.TimeProcess) * 1000.0);
        _fpDraws.Add(Performance.GetMonitor(Performance.Monitor.RenderTotalDrawCallsInFrame));
        _fpObjects.Add(Performance.GetMonitor(Performance.Monitor.RenderTotalObjectsInFrame));
        _fpPrims.Add(Performance.GetMonitor(Performance.Monitor.RenderTotalPrimitivesInFrame));
        _fpNodes = Performance.GetMonitor(Performance.Monitor.ObjectNodeCount);
        if (_fpFrames.Count < 300) return;
        var sorted = _fpFrames.OrderBy(v => v).ToList();
        int revealed = Enumerable.Range(0, Net.DepositCount).Count(d => Net.DepositIsFieldRock(d) && Net.IsDepositRevealed(d) && Net.DepositRemaining(d) > 0);
        string mode = _rockField is not null ? "multimesh" : "nodes";
        string field = _rockField is not null ? $" batches={_rockField.BatchCount} near_batches={_rockField.NearBatches} multimesh_nodes={_rockField.MultiMeshNodes} drawn_rocks={_rockField.DrawnRocks}" : "";
        GD.Print($"FIELDPERF mode={mode} cluster_rocks={_fieldPerfSpot.Rocks} cluster_ore={_fieldPerfSpot.Ore} richness={_fieldPerfSpot.Richness} at=({_fieldPerfSpot.X},{_fieldPerfSpot.Y},{_fieldPerfSpot.Z}) loaded={Net.FieldLoadedRocks} sectors={Net.FieldLoadedSectors} revealed={revealed}"
            + $" nodes={_fpNodes:F0} draw_calls={_fpDraws.Average():F0} objects={_fpObjects.Average():F0} primitives={_fpPrims.Average():F0} frames={sorted.Count} avg_ms={sorted.Average():F2} p95_ms={sorted[(int)(sorted.Count * 0.95)]:F2} worst_ms={sorted[^1]:F2} wall_ms={(Time.GetTicksMsec() - _fpWallStart) / (double)_fpFrames.Count:F2} fps={1000.0 / sorted.Average():F0}" + field + $" sync_ms={_fpSyncMs / _fpFrames.Count:F3} glow_ms={_fpGlowMs / _fpFrames.Count:F3}");
        if (_fieldPerfShot is not null) Save(_fieldPerfShot);
        GetTree().Quit();
    }
}
