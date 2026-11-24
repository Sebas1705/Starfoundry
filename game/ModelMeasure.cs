using System;
using System.Collections.Generic;
using System.Linq;
using Godot;
using Starfoundry.Sim;

namespace Starfoundry.Game;

/// <summary>
/// <c>--measure-models</c>: how far the drawn models of the rocks, mines, wrecks, debris and deposits reach from their centre, in units of the
/// radius the simulation knows (ADR 0033). The hit radii of <c>content/core.json</c> (<c>hit_percent</c> of each obstacle kind and
/// <c>deposit_hit_percent</c>) come from this table, so a ship never visibly enters a model before it hits it. Prints one line per kind
/// with the largest vertex distance over all variants (<c>max</c>), the 95th percentile of the vertices (<c>p95</c>) and the mean, then quits.
/// </summary>
public static class ModelMeasure
{
    public static void Run(Node host, ContentDb content)
    {
        var world = content.World;
        if (world is null) { GD.Print("MEASURE no world section"); return; }
        foreach (var kind in world.Kinds)
        {
            var results = new List<float>();
            var all = new List<float>();
            int[] seeds = Enumerable.Range(0, 60).ToArray();
            foreach (int seed in seeds)
            {
                // Radius 10 cells so the numbers are ratios of the nominal radius.
                var info = new ObstacleInfo(0, 0, kind.Index, kind.Behaviour, 0, 0, 0, 10 * Units.CellUnits, false, 0, 0, -1, 0, seed);
                var view = new ObstacleView(0, info, kind, kind.Id);
                host.AddChild(view);
                var radii = VertexRadii(view);
                if (radii.Count > 0) { results.Add(radii.Max() / 10f); all.AddRange(radii.Select(r => r / 10f)); }
                view.QueueFree();
            }
            Report($"obstacle {kind.Id} ({kind.Behaviour})", results, all);
        }
        foreach (var (item, density) in new[] { ("core:iron_ore", 100), ("core:carbon", 100), ("core:ice", 150), ("core:titanium_ore", 100), ("core:nickel_ore", 100), ("core:quartz", 200) })
            foreach (int initial in new[] { 300, 1200, 4000 })
            {
                var results = new List<float>();
                var all = new List<float>();
                for (int seed = 0; seed < 24; seed++)
                {
                    var view = new DepositView(item, density, initial, seed);
                    host.AddChild(view);
                    var radii = VertexRadii(view, includeSelf: true);
                    float nominal = DepositRules.NominalRadiusUnits(initial) / (float)Units.CellUnits;
                    if (radii.Count > 0) { results.Add(radii.Max() / nominal); all.AddRange(radii.Select(r => r / nominal)); }
                    view.QueueFree();
                }
                Report($"deposit {item} initial {initial} (nominal {DepositRules.NominalRadiusUnits(initial) / (float)Units.CellUnits:F2} cells)", results, all);
            }
    }

    private static void Report(string what, List<float> maxPerVariant, List<float> vertices)
    {
        if (maxPerVariant.Count == 0) { GD.Print($"MEASURE {what}: no mesh"); return; }
        vertices.Sort();
        float p95 = vertices[(int)(vertices.Count * 0.95f)], p90 = vertices[(int)(vertices.Count * 0.90f)], mean = vertices.Average();
        GD.Print($"MEASURE {what}: max {maxPerVariant.Max():F2}  avg-of-max {maxPerVariant.Average():F2}  p95 {p95:F2}  p90 {p90:F2}  mean {mean:F2}  (x nominal radius)");
    }

    /// <summary>The distance from the view's origin of every vertex of its body meshes, in cells (the glow halo of a mine is not a body).</summary>
    private static List<float> VertexRadii(Node3D root, bool includeSelf = false)
    {
        var list = new List<float>();
        void Walk(Node3D n, Transform3D acc)
        {
            foreach (var child in n.GetChildren())
            {
                if (child is not Node3D c || c.Name == "Blast" || c.Name == "Light" || c.Name == "Beacon") continue;
                var t = acc * c.Transform;
                if (c is MeshInstance3D mi && mi.Mesh is { } mesh)
                    foreach (var v in mesh.GetFaces()) list.Add((t * v).Length());
                Walk(c, t);
            }
        }
        Walk(root, Transform3D.Identity);
        if (includeSelf) // a deposit scales its own root: apply it
        {
            float s = root.Scale.X;
            for (int i = 0; i < list.Count; i++) list[i] *= s;
        }
        return list;
    }
}
