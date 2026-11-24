using System;
using System.Collections.Generic;
using System.Linq;
using Godot;
using Starfoundry.Sim;
using Starfoundry.Ui;

namespace Starfoundry.Game;

public partial class Gallery
{
    /// <summary>
    /// <c>Gallery.tscn --focus=ship-starter|ship-row|ship-big|modules [--variant=N] --audit=all|coplanar|holes [--step=1]</c>: builds the models of the
    /// chosen layout and audits their meshes (<see cref="MeshAudit"/>). <c>coplanar</c> lists pairs of parts with overlapping faces in one plane (the
    /// cause of z-fighting flicker); <c>holes</c> casts rays from walkable points of every ship module and prints those that see no surface from the
    /// inside (sky through the hull) or are visible from outside (an opening in the skin). Prints <c>AUDIT</c> lines; the totals end the run.
    /// </summary>
    private void RunAudit(string what, float step)
    {
        System.Threading.Thread.CurrentThread.CurrentCulture = System.Globalization.CultureInfo.InvariantCulture;
        var tris = new List<AuditTri>();
        foreach (var m in _auditModules) Collect(m.Node, m.Kind.ToString(), tris);
        GD.Print($"AUDIT modules={_auditModules.Count} triangles={tris.Count}");
        int problems = 0;
        var probeArg = OS.GetCmdlineUserArgs().FirstOrDefault(a => a.StartsWith("--probe="));
        if (probeArg is not null)
        {
            // --probe=ox,oy,oz,dx,dy,dz: every triangle the line crosses, nearest first (to see what a leaking ray passes)
            GD.Print("PROBE arg " + probeArg);
            var f = probeArg[8..].Split(',').Select(s => float.Parse(s, System.Globalization.CultureInfo.InvariantCulture)).ToArray();
            var d = System.Numerics.Vector3.Normalize(new(f[3], f[4], f[5]));
            var ps = new MeshAudit.Scene(tris);
            GD.Print("PROBE first(in)=" + ps.FirstBlocker(new(f[0], f[1], f[2]), d, 90f, true)?.T + " first(out)=" + ps.FirstBlocker(new(f[0], f[1], f[2]), d, 90f, false)?.T);
            foreach (var (t, tri) in ps.AllHits(new(f[0], f[1], f[2]), d, 90f))
                GD.Print($"PROBE t={t:0.000} {tri.Part} n=({tri.N.X:0.0},{tri.N.Y:0.0},{tri.N.Z:0.0}) facing={System.Numerics.Vector3.Dot(tri.N, d):0.00}{(tri.TwoSided ? " glass" : "")} at=({f[0] + d.X * t:0.00},{f[1] + d.Y * t:0.00},{f[2] + d.Z * t:0.00})");
            return;
        }
        if (what is "all" or "coplanar")
        {
            var visScene = new MeshAudit.Scene(tris);
            var hits = MeshAudit.CoplanarOverlaps(tris, visible: visScene.CanBeSeen);
            foreach (var h in hits.Take(80))
                GD.Print($"AUDIT coplanar {h.PartA} | {h.PartB} area={h.Area:0.000} gap={h.Gap:0.0000} at=({h.Where.X:0.00},{h.Where.Y:0.00},{h.Where.Z:0.00}) n=({h.Normal.X:0.0},{h.Normal.Y:0.0},{h.Normal.Z:0.0}) tri=[{h.Lo.X:0.00},{h.Lo.Y:0.000},{h.Lo.Z:0.00}]-[{h.Hi.X:0.00},{h.Hi.Y:0.000},{h.Hi.Z:0.00}] other=[{h.Lo2.X:0.00},{h.Lo2.Y:0.000},{h.Lo2.Z:0.00}]-[{h.Hi2.X:0.00},{h.Hi2.Y:0.000},{h.Hi2.Z:0.00}]");
            GD.Print($"AUDIT coplanar pairs={hits.Count} totalArea={hits.Sum(h => h.Area):0.00}");
            problems += hits.Count;
        }
        if (what is "all" or "holes")
        {
            var scene = new MeshAudit.Scene(tris);
            var dirs = MeshAudit.Directions(5);
            int inside = 0, outside = 0, points = 0;
            foreach (var m in _auditModules)
            {
                if (!m.Ship) continue;
                var shell = HullProfile.ShellOf(m.Kind, true, m.Mask);
                int leakIn = 0, leakOut = 0;
                var examples = new List<string>();
                foreach (float y in new[] { 0.3f, 1.0f, 1.7f, 2.6f, 3.4f })
                    for (float x = 0.25f; x < 7.8f; x += 0.75f * step)
                        for (float z = 0.25f; z < 7.8f; z += 0.75f * step)
                        {
                            // a sample must stand clear of the shell (a few centimetres inside), or its own wall would be missed by the ray from it
                            if (!HullProfile.Clear(shell, x - 0.05f, z, y, y) || !HullProfile.Clear(shell, x + 0.05f, z, y, y) || !HullProfile.Clear(shell, x, z, y - 0.05f, y + 0.05f)) continue;
                            points++;
                            var p = m.At + new Vector3(x, y, z);
                            var o = new System.Numerics.Vector3(p.X, p.Y, p.Z);
                            foreach (var d in dirs)
                            {
                                bool seesSky = scene.FirstBlocker(o, d, 90f, viewerAtOrigin: true) is null;
                                bool seenFromOutside = scene.FirstBlocker(o, d, 90f, viewerAtOrigin: false) is null;
                                if (seesSky) { leakIn++; if (examples.Count < 5 && examples.All(e => !e.StartsWith("in " + Octant(d)))) examples.Add($"in {Octant(d)} from ({m.At.X + x:0.000},{m.At.Y + y:0.000},{m.At.Z + z:0.000}) dir=({d.X:0.0000},{d.Y:0.0000},{d.Z:0.0000})"); }
                                if (seenFromOutside) { leakOut++; if (examples.Count < 9 && examples.All(e => !e.StartsWith("out " + Octant(d)))) examples.Add($"out {Octant(d)} from ({m.At.X + x:0.000},{m.At.Y + y:0.000},{m.At.Z + z:0.000}) dir=({d.X:0.0000},{d.Y:0.0000},{d.Z:0.0000})"); }
                            }
                        }
                inside += leakIn; outside += leakOut;
                if (leakIn + leakOut > 0)
                    GD.Print($"AUDIT holes {m.Kind} at ({m.At.X / 8f:0},{m.At.Y / 4f:0},{m.At.Z / 8f:0}) mask={m.Mask} seeSky={leakIn} seenFromOutside={leakOut} :: {string.Join(" ; ", examples)}");
            }
            GD.Print($"AUDIT holes points={points} seeSky={inside} seenFromOutside={outside}");
            problems += inside + outside;
        }
        if (what is "all" or "walk") problems += WalkAudit(tris);
        GD.Print($"AUDIT done problems={problems}");
    }

    /// <summary>
    /// Every position the walking body may take (the real <see cref="BodyMotion.Contains"/> over the shells of the layout) is tested against the drawn
    /// triangles: any triangle that cuts through the body (a box 0.6 wide from knee to head) is a surface the player walks through, and any one within
    /// 12 cm of the eye is geometry the camera clips. Parts are listed with how many positions they touch; the floor decor below 14 cm is ignored.
    /// </summary>
    private int WalkAudit(List<AuditTri> allTris)
    {
        // light shafts, glows and lamps are not solid: only what the model draws as matter counts
        var tris = allTris.Where(t => !t.Part.Contains(":Glow") && !t.Part.Contains(":Shaft") && !t.Part.Contains("Light") && !t.Part.Contains("Nav")).ToList();
        var scene = new MeshAudit.Scene(tris);
        var parts = new Dictionary<(int Mx, int My, int Mz), ModuleKind>();
        var masks = new Dictionary<(int Mx, int My, int Mz), int>();
        foreach (var m in _auditModules)
        {
            if (!m.Ship) continue;
            var key = ((int)MathF.Round(m.At.X / 8f), (int)MathF.Round(m.At.Y / 4f), (int)MathF.Round(m.At.Z / 8f));
            parts[key] = m.Kind; masks[key] = m.Mask;
        }
        var vols = BodyMotion.Volumes(parts, k => masks[k], true);
        var body = new Dictionary<string, (int Count, string Example)>();
        var camera = new Dictionary<string, (int Count, string Example)>();
        int positions = 0;
        foreach (var b in vols)
            for (float x = b.MinX + 0.2f; x < b.MaxX; x += 0.25f)
                for (float z = b.MinZ + 0.2f; z < b.MaxZ; z += 0.25f)
                {
                    var p = new System.Numerics.Vector3(x, b.MinY, z);
                    if (!BodyMotion.Contains(p, vols)) continue;
                    positions++;
                    foreach (int i in scene.TrianglesIn(new(x - 0.2f, b.MinY + 0.3f, z - 0.2f), new(x + 0.2f, b.MinY + 1.8f, z + 0.2f)))
                    {
                        var part = scene.Tri(i).Part;
                        body[part] = body.TryGetValue(part, out var old) ? (old.Count + 1, old.Example) : (1, $"({x:0.00},{b.MinY:0.0},{z:0.00}) tri n=({scene.Tri(i).N.X:0.0},{scene.Tri(i).N.Y:0.0},{scene.Tri(i).N.Z:0.0})");
                    }
                    var eye = new System.Numerics.Vector3(x, b.MinY + BodyMotion.EyeHeight, z);
                    foreach (int i in scene.TrianglesIn(eye - new System.Numerics.Vector3(0.12f), eye + new System.Numerics.Vector3(0.12f)))
                    {
                        var part = scene.Tri(i).Part;
                        camera[part] = camera.TryGetValue(part, out var old) ? (old.Count + 1, old.Example) : (1, $"eye ({x:0.00},{eye.Y:0.00},{z:0.00})");
                    }
                }
        foreach (var kv in body.OrderByDescending(k => k.Value.Count).Take(40)) GD.Print($"AUDIT walk body {kv.Key} positions={kv.Value.Count} e.g. {kv.Value.Example}");
        foreach (var kv in camera.OrderByDescending(k => k.Value.Count).Take(25)) GD.Print($"AUDIT walk camera {kv.Key} positions={kv.Value.Count} e.g. {kv.Value.Example}");
        GD.Print($"AUDIT walk positions={positions} bodyParts={body.Count} cameraParts={camera.Count}");
        return body.Count + camera.Count;
    }

    private static string Octant(System.Numerics.Vector3 d) =>
        (MathF.Abs(d.X) >= MathF.Abs(d.Y) && MathF.Abs(d.X) >= MathF.Abs(d.Z) ? (d.X > 0 ? "+x" : "-x") : MathF.Abs(d.Y) >= MathF.Abs(d.Z) ? (d.Y > 0 ? "+y" : "-y") : (d.Z > 0 ? "+z" : "-z"));

    /// <summary>Every triangle of every mesh under <paramref name="node"/> in world space, with its normal, two-sidedness and a part label.</summary>
    private static void Collect(Node node, string prefix, List<AuditTri> into)
    {
        if (node is MeshInstance3D { Mesh: ArrayMesh mesh } mi)
        {
            var xf = mi.GlobalTransform;
            for (int s = 0; s < mesh.GetSurfaceCount(); s++)
            {
                var arrays = mesh.SurfaceGetArrays(s);
                var v = (Vector3[])arrays[(int)Mesh.ArrayType.Vertex];
                var n = (Vector3[])arrays[(int)Mesh.ArrayType.Normal];
                var idx = arrays[(int)Mesh.ArrayType.Index].AsInt32Array();
                bool two = mesh.SurfaceGetMaterial(s) is BaseMaterial3D { CullMode: BaseMaterial3D.CullModeEnum.Disabled };
                string part = $"{prefix}:{(mi.HasMeta("part") ? (string)mi.GetMeta("part") : mi.Name.ToString())}";
                int count = idx.Length > 0 ? idx.Length : v.Length;
                for (int i = 0; i + 2 < count; i += 3)
                {
                    int a = idx.Length > 0 ? idx[i] : i, b = idx.Length > 0 ? idx[i + 1] : i + 1, c = idx.Length > 0 ? idx[i + 2] : i + 2;
                    var nn = (xf.Basis * n[a]).Normalized();
                    into.Add(new AuditTri(Sys(xf * v[a]), Sys(xf * v[b]), Sys(xf * v[c]), Sys(nn), two, part));
                }
            }
        }
        foreach (var child in node.GetChildren()) Collect(child, prefix, into);
    }

    private static System.Numerics.Vector3 Sys(Vector3 v) => new(v.X, v.Y, v.Z);
}
