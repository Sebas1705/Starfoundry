using System;
using System.Collections.Generic;
using System.Linq;
using Godot;
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
            foreach (var (t, tri) in new MeshAudit.Scene(tris).AllHits(new(f[0], f[1], f[2]), d, 90f))
                GD.Print($"PROBE t={t:0.000} {tri.Part} n=({tri.N.X:0.0},{tri.N.Y:0.0},{tri.N.Z:0.0}) facing={System.Numerics.Vector3.Dot(tri.N, d):0.00}{(tri.TwoSided ? " glass" : "")} at=({f[0] + d.X * t:0.00},{f[1] + d.Y * t:0.00},{f[2] + d.Z * t:0.00})");
            return;
        }
        if (what is "all" or "coplanar")
        {
            var hits = MeshAudit.CoplanarOverlaps(tris);
            foreach (var h in hits.Take(80))
                GD.Print($"AUDIT coplanar {h.PartA} | {h.PartB} area={h.Area:0.000} gap={h.Gap:0.0000} at=({h.Where.X:0.00},{h.Where.Y:0.00},{h.Where.Z:0.00}) n=({h.Normal.X:0.0},{h.Normal.Y:0.0},{h.Normal.Z:0.0})");
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
                            if (!HullProfile.Clear(shell, x, z, y, y)) continue;
                            points++;
                            var p = m.At + new Vector3(x, y, z);
                            var o = new System.Numerics.Vector3(p.X, p.Y, p.Z);
                            foreach (var d in dirs)
                            {
                                bool seesSky = scene.FirstBlocker(o, d, 90f, viewerAtOrigin: true) is null;
                                bool seenFromOutside = scene.FirstBlocker(o, d, 90f, viewerAtOrigin: false) is null;
                                if (seesSky) { leakIn++; if (examples.Count < 5 && examples.All(e => !e.StartsWith("in " + Octant(d)))) examples.Add($"in {Octant(d)} from ({x:0.00},{y:0.00},{z:0.00}) dir=({d.X:0.00},{d.Y:0.00},{d.Z:0.00})"); }
                                if (seenFromOutside) { leakOut++; if (examples.Count < 9 && examples.All(e => !e.StartsWith("out " + Octant(d)))) examples.Add($"out {Octant(d)} from ({x:0.00},{y:0.00},{z:0.00}) dir=({d.X:0.00},{d.Y:0.00},{d.Z:0.00})"); }
                            }
                        }
                inside += leakIn; outside += leakOut;
                if (leakIn + leakOut > 0)
                    GD.Print($"AUDIT holes {m.Kind} at ({m.At.X / 8f:0},{m.At.Y / 4f:0},{m.At.Z / 8f:0}) mask={m.Mask} seeSky={leakIn} seenFromOutside={leakOut} :: {string.Join(" ; ", examples)}");
            }
            GD.Print($"AUDIT holes points={points} seeSky={inside} seenFromOutside={outside}");
            problems += inside + outside;
        }
        GD.Print($"AUDIT done problems={problems}");
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
                string part = $"{prefix}:{mi.Name}";
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
