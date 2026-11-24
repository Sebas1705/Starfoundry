using System;
using System.Collections.Generic;
using System.Linq;
using Godot;
using Starfoundry.Ui;

namespace Starfoundry.Game;

/// <summary>
/// The Blender-authored detail of the starter cockpit (docs/prototypes/blender-pipeline.md): the console, the pilot seat, the window and windscreen frames,
/// the overhead panel, the cable runs, the glass and the emissive marks of <c>assets/blender/cockpit_wide.glb</c>, built by <c>tools/blender/cockpit.py</c>
/// from the numbers of <c>tools/blender/shell.json</c>. The shell (hull, floor, joints) and the collision stay procedural: <c>HullProfile.Fixture</c> is still
/// the truth for what is solid. Behind a flag (<c>--blender-cockpit</c>, or <c>blender_cockpit=true</c> in the settings file) while the procedural cockpit stays
/// the default.
/// <para>
/// The file is read at run time with <see cref="GltfDocument"/> (no editor import, so a fresh checkout runs it). Mesh nodes are mapped to the game's
/// materials by name: <c>*_Hull</c> to <see cref="Materials.ModuleHull"/>, <c>*_Struct</c> to <see cref="Materials.ModuleStruct"/>, <c>Glass</c> to
/// <see cref="Materials.Glass"/>, <c>Glow_*</c> to the lamp materials (the nodes are named <c>GlowDash</c>, so <see cref="Models.Animate"/> treats them like the
/// procedural displays' lamps). Vertex colour (1, 0, 1) is the accent tag and is painted with the structure's trim colour. The group named <c>Seat</c> keeps
/// its name so <see cref="StationView.SetSeatVisible"/> hides it while seated.
/// </para>
/// </summary>
public static class BlenderCockpit
{
    public const string ResPath = "res://assets/blender/cockpit_wide.glb";
    public const string GroupName = "BlenderCockpit";

    /// <summary>The parts of the procedural cockpit the glTF replaces (the console and its lamps, the seat, the windscreen ribs and glass, the side window frames and glass).</summary>
    private static readonly HashSet<string> Replaced = new() { "Seat", "Console", "GlowDash", "CanopyRibs", "Canopy", "ViewFrameL", "ViewFrameR", "ViewGlassL", "ViewGlassR" };

    private sealed record PartDef(string Name, string Kind, bool InSeat, ArrayMesh Mesh);

    private static List<PartDef>? _parts;
    private static bool _failed;
    private static readonly Dictionary<string, List<(PartDef Def, ArrayMesh Mesh)>> Variants = new();

    /// <summary>Time the last load took, in milliseconds, and the triangle count of the file (for the measurements).</summary>
    public static double LoadMs { get; private set; }
    public static int Triangles { get; private set; }

    /// <summary>The flag or the persisted debug option is on.</summary>
    public static bool Enabled
    {
        get
        {
            var args = OS.GetCmdlineUserArgs();
            if (args.Contains("--blender-cockpit=off")) return false;
            return args.Contains("--blender-cockpit") || SettingsStore.Current.BlenderCockpit;
        }
    }

    /// <summary>True when a cockpit built for this mask should take the Blender detail (the glTF is made for the starter ship's cockpit shell).</summary>
    public static bool Wanted(bool ship, int mask) => ship && CockpitShell.IsStarterShell(mask) && Enabled && !_failed;

    private static byte[] ReadFile()
    {
        var bytes = Godot.FileAccess.GetFileAsBytes(ResPath);
        if (bytes.Length > 0) return bytes;
        string beside = System.IO.Path.Combine(System.IO.Path.GetDirectoryName(OS.GetExecutablePath()) ?? ".", "assets", "blender", "cockpit_wide.glb");
        return System.IO.File.Exists(beside) ? System.IO.File.ReadAllBytes(beside) : Array.Empty<byte>();
    }

    private static bool Load()
    {
        if (_parts is not null) return true;
        if (_failed) return false;
        ulong t0 = Time.GetTicksUsec();
        var bytes = ReadFile();
        if (bytes.Length == 0) { GD.PrintErr($"BLENDERCOCKPIT: {ResPath} not found"); _failed = true; return false; }
        var doc = new GltfDocument();
        var state = new GltfState();
        var err = doc.AppendFromBuffer(bytes, "", state);
        if (err != Error.Ok) { GD.PrintErr("BLENDERCOCKPIT: glTF parse failed: " + err); _failed = true; return false; }
        var scene = doc.GenerateScene(state);
        if (scene is null) { GD.PrintErr("BLENDERCOCKPIT: glTF scene failed"); _failed = true; return false; }
        var list = new List<PartDef>();
        int tris = 0;
        void Walk(Node n, bool inSeat)
        {
            inSeat |= n.Name == "Seat";
            if (n is MeshInstance3D mi && mi.Mesh is ArrayMesh am && am.GetSurfaceCount() > 0)
            {
                string name = mi.Name;
                string kind = name.EndsWith("_Hull", StringComparison.Ordinal) ? "Hull" : name.EndsWith("_Struct", StringComparison.Ordinal) ? "Struct" : name == "Glass" ? "Glass" : name.StartsWith("Glow_", StringComparison.Ordinal) ? name : "";
                if (kind.Length > 0)
                {
                    if (mi.Transform != Transform3D.Identity && n.GetParent() is { } p && p.Name != "Seat") GD.PrintErr($"BLENDERCOCKPIT: {name} has a transform (parts must be baked into the module frame)");
                    list.Add(new PartDef(name, kind, inSeat, am));
                    var arrays = am.SurfaceGetArrays(0);
                    var idx = arrays[(int)Mesh.ArrayType.Index].AsInt32Array();
                    tris += idx.Length / 3;
                }
                else GD.PrintErr($"BLENDERCOCKPIT: unknown node {name}");
            }
            foreach (var c in n.GetChildren()) Walk(c, inSeat);
        }
        Walk(scene, false);
        scene.Free();
        if (list.Count == 0) { GD.PrintErr("BLENDERCOCKPIT: no meshes in the glTF"); _failed = true; return false; }
        _parts = list;
        Triangles = tris;
        LoadMs = (Time.GetTicksUsec() - t0) / 1000.0;
        GD.Print($"BLENDERCOCKPIT loaded {list.Count} meshes, {tris} triangles, {bytes.Length / 1024} KiB in {LoadMs:F1} ms");
        return true;
    }

    private static Material MaterialOf(string kind) => kind switch
    {
        "Hull" => Materials.ModuleHull(),
        "Struct" => Materials.ModuleStruct(),
        "Glass" => Materials.Glass(),
        "Glow_Amber" => Models.Glow(new Color(1f, 0.70f, 0.18f), 1.1f),
        "Glow_Green" => Models.Glow(new Color(0.30f, 0.95f, 0.50f), 1.0f),
        "Glow_Cyan" => Models.Glow(new Color(0.25f, 0.70f, 1f), 1.1f),
        "Glow_Red" => Models.Glow(new Color(0.90f, 0.22f, 0.22f), 1.2f),
        "Glow_Warm" => Models.Glow(new Color(1f, 0.92f, 0.65f), 1.3f),
        _ => Materials.ModuleHull(),
    };

    /// <summary>The parts of the file with the accent tag painted in <paramref name="accent"/> and the game's materials set (one set per accent colour, shared by every cockpit).</summary>
    private static List<(PartDef Def, ArrayMesh Mesh)> VariantFor(Color accent)
    {
        string key = accent.ToHtml();
        if (Variants.TryGetValue(key, out var v)) return v;
        v = new();
        foreach (var def in _parts!)
        {
            var arrays = def.Mesh.SurfaceGetArrays(0);
            ArrayMesh mesh;
            if (def.Kind is "Hull" or "Struct" && arrays[(int)Mesh.ArrayType.Color].VariantType != Variant.Type.Nil)
            {
                var colours = arrays[(int)Mesh.ArrayType.Color].AsColorArray();
                for (int i = 0; i < colours.Length; i++)
                {
                    var c = colours[i];
                    if (c.R > 0.9f && c.G < 0.1f && c.B > 0.9f) colours[i] = accent;
                }
                arrays[(int)Mesh.ArrayType.Color] = colours;
                mesh = new ArrayMesh();
                mesh.AddSurfaceFromArrays(Mesh.PrimitiveType.Triangles, arrays);
            }
            else
            {
                mesh = new ArrayMesh();
                mesh.AddSurfaceFromArrays(Mesh.PrimitiveType.Triangles, arrays);
            }
            mesh.SurfaceSetMaterial(0, MaterialOf(def.Kind));
            mesh.ResourceName = "blender." + def.Name + "." + key;
            v.Add((def, mesh));
        }
        Variants[key] = v;
        return v;
    }

    /// <summary>
    /// Replaces the procedural console, seat, frames and glass of the cockpit module <paramref name="root"/> with the glTF detail. Returns false (and leaves the
    /// procedural parts alone) when the file cannot be loaded.
    /// </summary>
    public static bool Apply(Node3D root, Color accent)
    {
        if (!Load()) return false;
        foreach (var child in root.GetChildren().ToArray())
        {
            string part = child.HasMeta("part") ? child.GetMeta("part").AsString() : child.Name.ToString();
            if (!Replaced.Contains(part)) continue;
            root.RemoveChild(child);
            child.Free();
        }
        var group = new Node3D { Name = GroupName };
        var seat = new Node3D { Name = "Seat" };
        root.AddChild(group);
        root.AddChild(seat);
        foreach (var (def, mesh) in VariantFor(accent))
        {
            string nodeName = def.Kind.StartsWith("Glow_", StringComparison.Ordinal) ? "GlowDash" : def.Name;
            var mi = new MeshInstance3D { Name = nodeName, Mesh = mesh };
            mi.SetMeta("part", nodeName);
            if (def.Kind is "Glass") mi.CastShadow = GeometryInstance3D.ShadowCastingSetting.Off;
            (def.InSeat ? seat : group).AddChild(mi);
        }
        return true;
    }

    /// <summary>True when the module node carries the glTF detail (the displays then skip their plain bezel boxes: the console has recessed wells).</summary>
    public static bool IsOn(Node3D? module) => module is not null && module.GetNodeOrNull(GroupName) is not null;
}
