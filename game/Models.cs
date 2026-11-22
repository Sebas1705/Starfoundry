using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using Godot;

namespace Starfoundry.Game;

/// <summary>
/// Procedural low-poly 3D models for machines, ships, enemies and deposits. Everything is generated from code:
/// flat-shaded ArrayMeshes (one normal per face, vertex colours for the hull tones) and a few emissive parts.
/// Meshes are cached and shared between instances; materials are shared until an instance needs its own
/// (Animate on glow parts, SetDamage on everything), at which point they are duplicated lazily.
///
/// Conventions: machine origin = centre of a 1x1x1 grid cell, base at y = -0.45, output facing +X, size about 0.9.
/// Ships, enemies and deposits are free-flying: origin at their centre, nose along +X.
///
/// Node names used by <see cref="Animate"/> (matched by exact name, anywhere below the model root):
///   "Bit"      drill auger, spins around Y.
///   "Rocker"   extractor pumpjack beam, rocks around Z.
///   "Arm"      assembler gantry carriage, slides along Z and bobs.
///   "Ring"     research lab ring, spins around Y.
///   "Dish"     research lab radar dish, spins around Y.
///   "Spinner"  scout ring, spins around X.
///   "Glow*"    any mesh whose name starts with "Glow" pulses its emission (dim when inactive):
///              GlowMouth/GlowVent/GlowRim (smelter), GlowItem/GlowLight (assembler, extractor), GlowDome/GlowRing (lab),
///              GlowPad/GlowBay/GlowBeacons (depot), GlowMuzzle (turret), GlowEngine (ship, enemies),
///              GlowEye/GlowSpin (scout), GlowCore (striker), GlowCrystals (deposit).
/// The turret has children "Head" (yaw) and "Head/Barrel" (pitch), used by <see cref="Aim"/>.
/// </summary>
public static class Models
{
    // ---- palette -----------------------------------------------------------------------------

    private static readonly Color Hull = new(0.16f, 0.18f, 0.23f);
    private static readonly Color HullLight = new(0.27f, 0.30f, 0.37f);
    private static readonly Color Steel = new(0.46f, 0.50f, 0.58f);
    private static readonly Color Panel = new(0.72f, 0.76f, 0.83f);
    private static readonly Color White = new(0.93f, 0.95f, 0.99f);

    private static readonly Color Orange = new(0.97f, 0.42f, 0.07f);
    private static readonly Color OrangeDk = new(0.66f, 0.26f, 0.05f);
    private static readonly Color Red = new(0.82f, 0.22f, 0.17f);
    private static readonly Color RedDk = new(0.50f, 0.12f, 0.10f);
    private static readonly Color Blue = new(0.24f, 0.48f, 0.94f);
    private static readonly Color BlueDk = new(0.13f, 0.27f, 0.62f);
    private static readonly Color Purple = new(0.64f, 0.34f, 0.90f);
    private static readonly Color PurpleDk = new(0.36f, 0.17f, 0.56f);
    private static readonly Color Green = new(0.28f, 0.80f, 0.43f);
    private static readonly Color GreenDk = new(0.14f, 0.48f, 0.26f);
    private static readonly Color Yellow = new(0.98f, 0.86f, 0.18f);
    private static readonly Color YellowDk = new(0.70f, 0.57f, 0.09f);
    private static readonly Color Hot = new(1f, 0.32f, 0.10f);
    private static readonly Color HotDk = new(0.58f, 0.11f, 0.06f);
    private static readonly Color Magenta = new(0.82f, 0.10f, 0.62f);

    private const float Y0 = -0.36f; // top of the common plinth

    // ---- public API ---------------------------------------------------------------------------

    /// <summary>A machine model for a content id (core:extractor, core:drill, ...). Unknown ids give a generic crate.</summary>
    public static Node3D Machine(string machineId) => machineId switch
    {
        "core:extractor" => BuildExtractor(),
        "core:drill" => BuildDrill(),
        "core:smelter" => BuildSmelter(),
        "core:assembler" => BuildAssembler(),
        "core:research_lab" => BuildLab(),
        "core:depot" => BuildDepot(),
        "core:turret" => BuildTurret(),
        "core:wall" => BuildWall(),
        "core:repair_bay" => BuildRepairBay(),
        "core:unloader" => BuildUnloader(),
        "core:ore_unloader" => BuildUnloader(),
        "core:titanium_ore_unloader" => BuildUnloader(),
        "core:nickel_ore_unloader" => BuildUnloader(),
        "core:ice_unloader" => BuildUnloader(),
        "core:quartz_unloader" => BuildUnloader(),
        "core:carbon_unloader" => BuildUnloader(),
        "core:hydrogen_cracker" or "core:greenhouse" => BuildCracker(), // science machines reuse a model until they get their own
        "core:engineering_bench" or "core:physics_lab" => BuildAssembler(),
        _ => BuildCrate(),
    };

    /// <summary>The player's small ship (nose along +X, about 1 unit long).</summary>
    public static Node3D Ship()
    {
        var r = new Node3D { Name = "Ship" };
        Add(r, "Hull", "ship.hull", Body(), mb =>
        {
            mb.At(default, Deg(0, 0, -90)).C(White).Prism(new(0, -0.40f, 0), 0.155f, 0.15f, 0.25f, 6);
            mb.C(White).Prism(new(0, -0.15f, 0), 0.15f, 0.03f, 0.60f, 6);
            mb.C(Panel).Prism(new(0, -0.40f, 0), 0.158f, 0.158f, 0.03f, 6);
            mb.At(default, Deg(0, 0, -90)).C(Panel).Prism(new(0, 0.00f, 0), 0.153f, 0.153f, 0.02f, 6);
            // swept wings (white with blue tips)
            foreach (float s in new[] { 1f, -1f })
            {
                mb.At(default).C(White).Extrude(new[] { new Vector2(0.12f, 0.10f * s), new Vector2(-0.20f, 0.46f * s), new Vector2(-0.38f, 0.46f * s), new Vector2(-0.30f, 0.10f * s) }, -0.045f, 0.0f);
                mb.C(Blue).Extrude(new[] { new Vector2(-0.14f, 0.40f * s), new Vector2(-0.20f, 0.46f * s), new Vector2(-0.38f, 0.46f * s), new Vector2(-0.33f, 0.40f * s) }, -0.05f, 0.005f);
                mb.C(Panel).Block(new(-0.38f, -0.02f, 0.13f * s), new(0.34f, 0.07f, 0.08f));
            }
            // tail fin
            mb.C(Blue).Beam(new(-0.28f, 0.08f, 0), new(-0.40f, 0.25f, 0), 0.04f);
            mb.C(White).Beam(new(-0.20f, 0.10f, 0), new(-0.34f, 0.12f, 0), 0.035f);
        });
        Add(r, "Canopy", "ship.canopy", GlassMat(), mb => mb.Sphere(new(0.08f, 0.075f, 0), new(0.15f, 0.08f, 0.07f), 3, 8));
        Add(r, "GlowEngine", "ship.engine", Glow(new Color(0.25f, 0.65f, 1f), 3.5f), mb =>
        {
            foreach (float s in new[] { 1f, -1f })
                mb.At(default, Deg(0, 0, 90)).Prism(new(0, 0.44f, 0.13f * s), 0.055f, 0f, 0.17f, 6);
        });
        return r;
    }

    /// <summary>An enemy: 0 = scout (small, fast, red-orange), 1 = striker (bigger, armoured, magenta), 2 and above = brute (heavy, bronze and amber).</summary>
    public static Node3D Enemy(int type) => type switch { 0 => BuildScout(), 1 => BuildStriker(), _ => BuildBrute() };

    /// <summary>An irregular asteroid with glowing crystal veins in <paramref name="tint"/>; about 1.5 units radius.</summary>
    public static Node3D Deposit(Color tint, int seed)
    {
        string key = $"deposit.{seed}.{tint.ToHtml()}";
        var r = new Node3D { Name = "Deposit" };
        Add(r, "Rock", key + ".rock", Body(), mb => BuildRock(mb, tint, seed));
        Add(r, "GlowCrystals", key + ".crystals", Glow(tint, 1.8f), mb => BuildCrystals(mb, seed));
        return r;
    }

    /// <summary>Drives the model's animation. Call every frame; <paramref name="active"/> false idles the machine.</summary>
    public static void Animate(Node3D model, double timeSeconds, bool active)
    {
        var a = GetAnim(model);
        float dt = double.IsNaN(a.Last) ? 0f : (float)Math.Clamp(timeSeconds - a.Last, 0.0, 0.1);
        a.Last = timeSeconds;
        float t = (float)timeSeconds;
        a.Work += active ? dt : 0f; // phase of working animations: freezes when idle instead of snapping

        if (Valid(a.Bit)) a.Bit!.Rotation = new Vector3(0, a.Spin(0, active ? 7f : 0f, dt), 0);
        if (Valid(a.Ring)) a.Ring!.Rotation = new Vector3(0, a.Spin(1, active ? 1.8f : 0.25f, dt), 0);
        if (Valid(a.Dish)) a.Dish!.Rotation = new Vector3(0, a.Spin(2, active ? 1.1f : 0.15f, dt), 0);
        if (Valid(a.Spinner)) a.Spinner!.Rotation = new Vector3(a.Spin(3, active ? 14f : 3f, dt), 0, 0);
        if (Valid(a.Rocker)) a.Rocker!.Rotation = new Vector3(0, 0, MathF.Sin(a.Work * 3.2f) * 0.24f);
        if (Valid(a.Arm))
        {
            float k = a.Work * 1.3f;
            a.Arm!.Position = a.ArmBase + new Vector3(0, MathF.Max(0f, MathF.Sin(k * 2f)) * -0.04f, MathF.Sin(k) * 0.27f);
        }

        float dmg = a.Damage;
        for (int i = 0; i < a.Glows.Count; i++)
        {
            var g = a.Glows[i];
            if (!Valid(g.Mi)) continue;
            bool engine = g.IsEngine;
            float f;
            if (g.IsCore && a.Reactor != 0)
            {
                // The reactor core ignores the power scale: it breathes slowly while burning and shows a faint red emergency light when dark.
                bool burning = a.Reactor == 1;
                if (a.AppliedReactor != a.Reactor)
                {
                    a.AppliedReactor = a.Reactor;
                    a.Own(g);
                    if (g.Mat != null)
                    {
                        g.Mat.Emission = burning ? g.Emission : new Color(1f, 0.12f, 0.08f);
                        g.Mat.AlbedoColor = burning ? g.Albedo : new Color(0.35f, 0.05f, 0.04f);
                    }
                    g.LastF = float.NaN;
                }
                f = burning ? 0.78f + 0.22f * MathF.Sin(t * 1.4f) : 0.10f + 0.06f * MathF.Sin(t * 0.9f);
            }
            else
            {
                if (engine) f = active ? 0.85f + 0.3f * MathF.Sin(t * 24f + i) : 0.45f;
                else f = active ? 0.8f + 0.35f * MathF.Sin(t * 3f + i * 1.3f) : 0.2f;
                f *= a.PowerScale;
            }
            f *= 1f - dmg * 0.65f;
            if (dmg > 0.2f) f *= 1f - dmg * 0.4f * (0.5f + 0.5f * MathF.Sin(t * 23f + i * 1.7f)); // failing, flickering lights
            f = MathF.Max(0f, f);
            if (!float.IsNaN(g.LastF) && MathF.Abs(g.LastF - f) < 0.01f) continue; // nothing visible changed: no engine calls
            g.LastF = f;
            a.Own(g);
            if (g.Mat == null) continue;
            g.Mat.EmissionEnergyMultiplier = g.Energy * f;
            if (engine) g.Mi.Scale = new Vector3(active ? 1f + 0.18f * MathF.Sin(t * 30f) : 0.6f, 1f, 1f);
        }
    }

    /// <summary>
    /// Scales every light of the model (0 dark, 1 normal) except a reactor core: used for the power satisfaction of a structure and
    /// for machines without power. Takes effect on the next <see cref="Animate"/>.
    /// </summary>
    public static void SetPower(Node3D model, float scale01) => GetAnim(model).PowerScale = Math.Clamp(scale01, 0f, 1f);

    /// <summary>Tints the lamps and dashboards of a module amber (low power) or back to their own colour. Cheap when nothing changes.</summary>
    public static void SetAmber(Node3D model, bool amber)
    {
        var a = GetAnim(model);
        if (a.Amber == amber) return;
        a.Amber = amber;
        foreach (var g in a.Glows)
        {
            if (!g.IsLamp || !Valid(g.Mi)) continue;
            a.Own(g);
            if (g.Mat != null) g.Mat.Emission = amber ? new Color(1f, 0.62f, 0.16f) : g.Emission;
        }
    }

    /// <summary>Marks a reactor model as burning (core breathes) or dark (faint red emergency light).</summary>
    public static void SetReactor(Node3D model, bool burning) => GetAnim(model).Reactor = burning ? 1 : 2;

    /// <summary>Rotates the turret's child "Head" (yaw) and "Head/Barrel" (pitch) towards a world position.</summary>
    public static void Aim(Node3D turret, Vector3 worldTarget)
    {
        var head = turret.GetNodeOrNull<Node3D>("Head");
        var barrel = head?.GetNodeOrNull<Node3D>("Barrel");
        if (head == null || barrel == null) return;
        Vector3 local = turret.IsInsideTree() ? turret.ToLocal(worldTarget) : turret.Transform.AffineInverse() * worldTarget;
        float dx = local.X - head.Position.X, dz = local.Z - head.Position.Z;
        head.Rotation = new Vector3(0, MathF.Atan2(-dz, dx), 0);
        float horiz = MathF.Max(MathF.Sqrt(dx * dx + dz * dz) - barrel.Position.X, 0.05f);
        float pitch = MathF.Atan2(local.Y - head.Position.Y - barrel.Position.Y, horiz);
        barrel.Rotation = new Vector3(0, 0, Math.Clamp(pitch, -0.2f, 1.35f));
    }

    /// <summary>0 = clean, 1 = almost destroyed: darkens and tints red, dims the lights and adds embers.</summary>
    public static void SetDamage(Node3D model, float damage01)
    {
        var a = GetAnim(model);
        float d = Math.Clamp(damage01, 0f, 1f);
        a.Damage = d;
        if (MathF.Abs(d - a.AppliedDamage) < 0.01f) return; // unchanged (most machines, most of the time): leave the shared materials alone
        a.AppliedDamage = d;
        foreach (var rec in a.All)
        {
            if (!Valid(rec.Mi)) continue;
            a.Own(rec);
            var m = rec.Mat;
            if (m == null) continue;
            if (rec.IsGlow)
            {
                m.AlbedoColor = rec.Albedo * (1f - d * 0.6f);
                m.EmissionEnergyMultiplier = rec.Energy * (1f - d * 0.65f);
            }
            else
            {
                m.AlbedoColor = rec.Albedo.Lerp(new Color(0.22f, 0.15f, 0.14f), d * 0.85f);
                m.Roughness = Mathf.Lerp(rec.Rough, 1f, d * 0.6f);
                m.Metallic = rec.Metal * (1f - d * 0.6f);
                m.EmissionEnabled = d > 0.05f;
                m.Emission = new Color(1f, 0.25f, 0.05f);
                m.EmissionEnergyMultiplier = d * 0.16f;
            }
        }
    }

    // ---- animation bookkeeping ------------------------------------------------------------------

    private sealed class Rec
    {
        public MeshInstance3D Mi = null!;
        public StandardMaterial3D? Mat;
        public bool IsGlow, IsEngine, IsCore, IsLamp, Owned;
        public float LastF = float.NaN; // the last glow factor written, so unchanged lights cost nothing
        public Color Albedo, Emission;
        public float Energy, Rough, Metal;
    }

    private sealed class Anim
    {
        public readonly List<Rec> All = new();
        public readonly List<Rec> Glows = new();
        public Node3D? Bit, Ring, Dish, Spinner, Rocker, Arm;
        public Vector3 ArmBase;
        public double Last = double.NaN;
        public float Work, Damage;
        public float PowerScale = 1f; // multiplies every glow except the reactor core (power satisfaction)
        public int Reactor, AppliedReactor; // 0 = not a reactor, 1 = burning, 2 = dark
        public bool Amber; // the lamps and dashboards tint amber
        public float AppliedDamage; // the damage the materials were last set to
        private readonly float[] _angles = new float[4];

        public float Spin(int slot, float speed, float dt) => _angles[slot] += speed * dt;

        /// <summary>Gives the record its own material copy (the mesh keeps the shared one).</summary>
        public void Own(Rec r)
        {
            if (r.Owned) return;
            r.Owned = true;
            if (r.Mi.Mesh?.SurfaceGetMaterial(0) is not StandardMaterial3D src) return;
            var m = (StandardMaterial3D)src.Duplicate();
            r.Mi.SetSurfaceOverrideMaterial(0, m);
            r.Mat = m;
            r.Albedo = m.AlbedoColor;
            r.Emission = m.Emission;
            r.Energy = m.EmissionEnergyMultiplier;
            r.Rough = m.Roughness;
            r.Metal = m.Metallic;
        }
    }

    private static readonly ConditionalWeakTable<Node3D, Anim> Anims = new();

    private static bool Valid(GodotObject? o) => o != null && GodotObject.IsInstanceValid(o);

    private static Anim GetAnim(Node3D model)
    {
        if (Anims.TryGetValue(model, out var existing)) return existing;
        var a = new Anim();
        Collect(model, a);
        Anims.Add(model, a);
        return a;
    }

    private static void Collect(Node n, Anim a)
    {
        foreach (var c in n.GetChildren())
        {
            string name = c.Name.ToString();
            if (c is Node3D n3)
            {
                switch (name)
                {
                    case "Bit": a.Bit = n3; break;
                    case "Ring": a.Ring = n3; break;
                    case "Dish": a.Dish = n3; break;
                    case "Spinner": a.Spinner = n3; break;
                    case "Rocker": a.Rocker = n3; break;
                    case "Arm": a.Arm = n3; a.ArmBase = n3.Position; break;
                }
            }
            if (c is MeshInstance3D mi)
            {
                var rec = new Rec
                {
                    Mi = mi, IsGlow = name.StartsWith("Glow", StringComparison.Ordinal), IsEngine = name.StartsWith("GlowEngine", StringComparison.Ordinal),
                    IsCore = name == "GlowCore", IsLamp = name is "GlowLamp" or "GlowDash",
                };
                if (mi.Mesh?.SurfaceGetMaterial(0) is StandardMaterial3D m)
                {
                    rec.Albedo = m.AlbedoColor;
                    rec.Emission = m.Emission;
                    rec.Energy = m.EmissionEnergyMultiplier;
                    rec.Rough = m.Roughness;
                    rec.Metal = m.Metallic;
                }
                a.All.Add(rec);
                if (rec.IsGlow) a.Glows.Add(rec);
            }
            Collect(c, a);
        }
    }

    // ---- materials -------------------------------------------------------------------------------

    private static readonly Dictionary<string, StandardMaterial3D> Mats = new();

    internal static StandardMaterial3D Body()
    {
        if (!Mats.TryGetValue("body", out var m))
            Mats["body"] = m = new StandardMaterial3D { VertexColorUseAsAlbedo = true, AlbedoColor = Colors.White, Roughness = 0.55f, Metallic = 0.22f };
        return m;
    }

    internal static StandardMaterial3D Glow(Color c, float energy)
    {
        string key = $"glow.{c.ToHtml()}.{energy}";
        if (!Mats.TryGetValue(key, out var m))
            Mats[key] = m = new StandardMaterial3D
            {
                AlbedoColor = c * 0.45f,
                EmissionEnabled = true,
                Emission = c,
                EmissionEnergyMultiplier = energy,
                Roughness = 0.35f,
            };
        return m;
    }

    /// <summary>A translucent glow (force fields, radar sweeps): emissive, see-through, lit from both sides, no shadows.</summary>
    internal static StandardMaterial3D GlowAlpha(Color c, float energy, float alpha)
    {
        string key = $"glowalpha.{c.ToHtml()}.{energy}.{alpha}";
        if (!Mats.TryGetValue(key, out var m))
            Mats[key] = m = new StandardMaterial3D
            {
                AlbedoColor = new Color(c.R, c.G, c.B, alpha),
                Transparency = BaseMaterial3D.TransparencyEnum.Alpha,
                CullMode = BaseMaterial3D.CullModeEnum.Disabled,
                EmissionEnabled = true,
                Emission = c,
                EmissionEnergyMultiplier = energy,
                Roughness = 0.2f,
            };
        return m;
    }

    internal static StandardMaterial3D GlassMat()
    {
        if (!Mats.TryGetValue("glass", out var m))
            Mats["glass"] = m = new StandardMaterial3D
            {
                AlbedoColor = new Color(0.10f, 0.20f, 0.36f),
                Roughness = 0.12f,
                Metallic = 0.5f,
                EmissionEnabled = true,
                Emission = new Color(0.15f, 0.40f, 0.95f),
                EmissionEnergyMultiplier = 0.7f,
            };
        return m;
    }

    // ---- mesh plumbing -----------------------------------------------------------------------------

    private static readonly Dictionary<string, ArrayMesh> MeshCache = new();

    private static ArrayMesh GetMesh(string key, Material mat, Action<Mb> build)
    {
        if (!MeshCache.TryGetValue(key, out var mesh))
        {
            var mb = new Mb();
            build(mb);
            mesh = mb.ToMesh(mat);
            mesh.ResourceName = key;
            MeshCache[key] = mesh;
        }
        return mesh;
    }

    internal static MeshInstance3D Add(Node3D parent, string name, string key, Material mat, Action<Mb> build, Vector3 pos = default, Vector3 rotDeg = default)
    {
        var mi = new MeshInstance3D { Name = name, Mesh = GetMesh(key, mat, build), Position = pos, RotationDegrees = rotDeg };
        parent.AddChild(mi);
        return mi;
    }

    internal static Node3D Pivot(Node3D parent, string name, Vector3 pos)
    {
        var n = new Node3D { Name = name, Position = pos };
        parent.AddChild(n);
        return n;
    }

    internal static Vector3 Deg(float x, float y, float z) => new(Mathf.DegToRad(x), Mathf.DegToRad(y), Mathf.DegToRad(z));

    /// <summary>Flat-shaded triangle soup builder. Every triangle gets its own vertices and face normal.</summary>
    internal sealed class Mb
    {
        private readonly List<Vector3> _v = new();
        private readonly List<Vector3> _n = new();
        private readonly List<Color> _c = new();
        private Color _col = Colors.White;
        private Transform3D _xf = Transform3D.Identity;

        public int Tris => _v.Count / 3;

        public Mb C(Color c) { _col = c; return this; }

        /// <summary>Sets the transform applied to the following primitives (rotation in radians).</summary>
        public Mb At(Vector3 pos, Vector3 rot = default, Vector3? scale = null)
        {
            var basis = Basis.FromEuler(rot);
            if (scale.HasValue) basis = basis.Scaled(scale.Value);
            _xf = new Transform3D(basis, pos);
            return this;
        }

        /// <summary>Adds a triangle; winding is fixed so the face looks away from <paramref name="inside"/>.</summary>
        public void Tri(Vector3 a, Vector3 b, Vector3 c, Vector3 inside)
        {
            a = _xf * a; b = _xf * b; c = _xf * c; inside = _xf * inside;
            var n = (b - a).Cross(c - a);
            if (n.LengthSquared() < 1e-10f) return;
            n = n.Normalized();
            if (n.Dot((a + b + c) / 3f - inside) < 0f) { (b, c) = (c, b); n = -n; }
            // Godot front faces are clockwise
            _v.Add(a); _v.Add(c); _v.Add(b);
            for (int i = 0; i < 3; i++) { _n.Add(n); _c.Add(_col); }
        }

        public void Quad(Vector3 a, Vector3 b, Vector3 c, Vector3 d, Vector3 inside)
        {
            Tri(a, b, c, inside);
            Tri(a, c, d, inside);
        }

        /// <summary>Box, optionally tapering towards the top (topX/topZ scale the top face).</summary>
        public Mb Block(Vector3 c, Vector3 size, float topX = 1f, float topZ = 1f)
        {
            float hx = size.X / 2, hy = size.Y / 2, hz = size.Z / 2;
            var b0 = c + new Vector3(-hx, -hy, -hz); var b1 = c + new Vector3(hx, -hy, -hz);
            var b2 = c + new Vector3(hx, -hy, hz); var b3 = c + new Vector3(-hx, -hy, hz);
            var t0 = c + new Vector3(-hx * topX, hy, -hz * topZ); var t1 = c + new Vector3(hx * topX, hy, -hz * topZ);
            var t2 = c + new Vector3(hx * topX, hy, hz * topZ); var t3 = c + new Vector3(-hx * topX, hy, hz * topZ);
            Quad(b0, b1, b2, b3, c); Quad(t0, t1, t2, t3, c);
            Quad(b0, b1, t1, t0, c); Quad(b1, b2, t2, t1, c); Quad(b2, b3, t3, t2, c); Quad(b3, b0, t0, t3, c);
            return this;
        }

        /// <summary>Vertical prism / frustum / cone (rTop = 0) standing on <paramref name="bc"/>.</summary>
        public Mb Prism(Vector3 bc, float rb, float rt, float h, int sides, float rotDeg = 0f)
        {
            var inside = bc + new Vector3(0, h / 2, 0);
            var top = bc + new Vector3(0, h, 0);
            for (int i = 0; i < sides; i++)
            {
                float a0 = Mathf.DegToRad(rotDeg) + Mathf.Tau * i / sides, a1 = Mathf.DegToRad(rotDeg) + Mathf.Tau * (i + 1) / sides;
                var b0 = bc + new Vector3(MathF.Cos(a0) * rb, 0, MathF.Sin(a0) * rb);
                var b1 = bc + new Vector3(MathF.Cos(a1) * rb, 0, MathF.Sin(a1) * rb);
                var t0 = top + new Vector3(MathF.Cos(a0) * rt, 0, MathF.Sin(a0) * rt);
                var t1 = top + new Vector3(MathF.Cos(a1) * rt, 0, MathF.Sin(a1) * rt);
                Tri(bc, b0, b1, inside);
                if (rt > 1e-4f)
                {
                    Quad(b0, b1, t1, t0, inside);
                    Tri(top, t0, t1, inside);
                }
                else Tri(b0, b1, top, inside);
            }
            return this;
        }

        /// <summary>Ellipsoid (or its upper half when <paramref name="dome"/>).</summary>
        public Mb Sphere(Vector3 c, Vector3 radii, int rings, int segs, bool dome = false)
        {
            float maxLat = dome ? Mathf.Pi / 2 : Mathf.Pi;
            Vector3 P(int i, int j)
            {
                float lat = maxLat * i / rings, lon = Mathf.Tau * j / segs;
                return c + new Vector3(MathF.Sin(lat) * MathF.Cos(lon) * radii.X, MathF.Cos(lat) * radii.Y, MathF.Sin(lat) * MathF.Sin(lon) * radii.Z);
            }
            for (int i = 0; i < rings; i++)
                for (int j = 0; j < segs; j++)
                    Quad(P(i, j), P(i, j + 1), P(i + 1, j + 1), P(i + 1, j), c);
            if (dome)
                for (int j = 0; j < segs; j++) Tri(c, P(rings, j), P(rings, j + 1), c + new Vector3(0, radii.Y / 2, 0));
            return this;
        }

        /// <summary>Ring in the XZ plane (axis Y).</summary>
        public Mb Torus(Vector3 c, float big, float small, int segs, int sides)
        {
            for (int i = 0; i < segs; i++)
            {
                float u0 = Mathf.Tau * i / segs, u1 = Mathf.Tau * (i + 1) / segs, um = (u0 + u1) / 2;
                var mid = c + new Vector3(MathF.Cos(um) * big, 0, MathF.Sin(um) * big);
                Vector3 P(float u, int j)
                {
                    float v = Mathf.Tau * j / sides;
                    var rc = c + new Vector3(MathF.Cos(u) * big, 0, MathF.Sin(u) * big);
                    return rc + new Vector3(MathF.Cos(v) * MathF.Cos(u) * small, MathF.Sin(v) * small, MathF.Cos(v) * MathF.Sin(u) * small);
                }
                for (int j = 0; j < sides; j++) Quad(P(u0, j), P(u1, j), P(u1, j + 1), P(u0, j + 1), mid);
            }
            return this;
        }

        /// <summary>Convex polygon given as (x, z) points, extruded between two heights.</summary>
        public Mb Extrude(Vector2[] poly, float y0, float y1)
        {
            var cen = Vector2.Zero;
            foreach (var p in poly) cen += p;
            cen /= poly.Length;
            var mid = new Vector3(cen.X, (y0 + y1) / 2, cen.Y);
            for (int i = 0; i < poly.Length; i++)
            {
                var p = poly[i]; var q = poly[(i + 1) % poly.Length];
                Tri(new(cen.X, y1, cen.Y), new(p.X, y1, p.Y), new(q.X, y1, q.Y), mid);
                Tri(new(cen.X, y0, cen.Y), new(p.X, y0, p.Y), new(q.X, y0, q.Y), mid);
                Quad(new(p.X, y0, p.Y), new(q.X, y0, q.Y), new(q.X, y1, q.Y), new(p.X, y1, p.Y), mid);
            }
            return this;
        }

        /// <summary>Square strut between two points.</summary>
        public Mb Beam(Vector3 a, Vector3 b, float thick)
        {
            var d = b - a;
            float len = d.Length();
            var saved = _xf;
            _xf = saved * new Transform3D(new Basis(new Quaternion(Vector3.Up, d / len)), (a + b) / 2);
            Block(Vector3.Zero, new Vector3(thick, len, thick));
            _xf = saved;
            return this;
        }

        public ArrayMesh ToMesh(Material mat)
        {
            var arrays = new Godot.Collections.Array();
            arrays.Resize((int)Mesh.ArrayType.Max);
            arrays[(int)Mesh.ArrayType.Vertex] = _v.ToArray();
            arrays[(int)Mesh.ArrayType.Normal] = _n.ToArray();
            arrays[(int)Mesh.ArrayType.Color] = _c.ToArray();
            var mesh = new ArrayMesh();
            mesh.AddSurfaceFromArrays(Mesh.PrimitiveType.Triangles, arrays);
            mesh.SurfaceSetMaterial(0, mat);
            return mesh;
        }
    }

    // ---- shared machine parts ---------------------------------------------------------------------

    private static void Plinth(Mb mb, Color accent)
    {
        mb.At(default).C(Hull).Block(new(0, -0.42f, 0), new(0.90f, 0.06f, 0.90f), 0.95f, 0.95f);
        mb.C(accent).Block(new(0, -0.425f, 0), new(0.92f, 0.022f, 0.92f), 0.97f, 0.97f);
        // output port on the +X face
        mb.C(HullLight).Block(new(0.43f, -0.325f, 0), new(0.10f, 0.09f, 0.18f));
        mb.C(accent).Block(new(0.482f, -0.325f, 0), new(0.012f, 0.05f, 0.11f));
    }

    private static Node3D NewMachine(string name) => new() { Name = name };

    // ---- machines ------------------------------------------------------------------------------------

    private static Node3D BuildDrill()
    {
        var r = NewMachine("Drill");
        Add(r, "Body", "drill.body", Body(), mb =>
        {
            Plinth(mb, Orange);
            mb.C(OrangeDk).Block(new(0, Y0 + 0.09f, 0), new(0.70f, 0.18f, 0.70f), 0.9f, 0.9f);
            mb.C(Steel).Block(new(0, Y0 + 0.19f, 0), new(0.46f, 0.02f, 0.46f));
            foreach (float sx in new[] { -1f, 1f })
                foreach (float sz in new[] { -1f, 1f })
                    mb.C(Orange).Beam(new(sx * 0.30f, Y0 + 0.18f, sz * 0.30f), new(sx * 0.11f, Y0 + 0.67f, sz * 0.11f), 0.065f);
            foreach (float t in new[] { 0.33f, 0.66f })
            {
                float h = Mathf.Lerp(0.30f, 0.11f, t), y = Y0 + 0.18f + 0.49f * t;
                mb.C(Steel);
                mb.Beam(new(-h, y, -h), new(h, y, -h), 0.03f); mb.Beam(new(h, y, -h), new(h, y, h), 0.03f);
                mb.Beam(new(h, y, h), new(-h, y, h), 0.03f); mb.Beam(new(-h, y, h), new(-h, y, -h), 0.03f);
            }
            mb.C(Orange).Block(new(0, Y0 + 0.72f, 0), new(0.34f, 0.10f, 0.34f), 0.85f, 0.85f);
            mb.C(Steel).Prism(new(0, Y0 + 0.77f, 0), 0.08f, 0.055f, 0.04f, 8);
            mb.C(Yellow).Block(new(0.17f, Y0 + 0.725f, 0), new(0.02f, 0.05f, 0.12f)); // hazard marker on the front
        });
        var bit = Pivot(r, "Bit", new(0, Y0 + 0.20f, 0));
        Add(bit, "BitMesh", "drill.bit", Body(), mb =>
        {
            mb.C(Steel).Prism(new(0, 0, 0), 0.05f, 0.05f, 0.47f, 6);
            for (int k = 0; k < 4; k++)
                mb.C(k % 2 == 0 ? Panel : Orange).Prism(new(0, 0.04f + k * 0.11f, 0), 0.165f - k * 0.012f, 0.15f - k * 0.012f, 0.035f, 3, k * 40f);
        });
        return r;
    }

    private static Node3D BuildExtractor()
    {
        var r = NewMachine("Extractor");
        Add(r, "Body", "extractor.body", Body(), mb =>
        {
            Plinth(mb, Orange);
            mb.C(OrangeDk).Prism(new(0, Y0, 0), 0.37f, 0.33f, 0.22f, 8, 22.5f);
            mb.C(Orange).Prism(new(0, Y0 + 0.22f, 0), 0.335f, 0.335f, 0.03f, 8, 22.5f);
            mb.C(Orange).Prism(new(0, Y0 + 0.07f, 0), 0.375f, 0.375f, 0.035f, 8, 22.5f);
            mb.C(Steel).Prism(new(0.27f, Y0 + 0.25f, 0), 0.075f, 0.06f, 0.10f, 6);
            mb.C(Hull).Prism(new(0.27f, Y0 + 0.35f, 0), 0.062f, 0.062f, 0.02f, 6);
            // A-frame supporting the rocker
            foreach (float s in new[] { -1f, 1f })
            {
                mb.C(Orange).Beam(new(-0.07f, Y0 + 0.25f, s * 0.17f), new(0, Y0 + 0.50f, s * 0.07f), 0.045f);
                mb.C(Orange).Beam(new(0.09f, Y0 + 0.25f, s * 0.17f), new(0, Y0 + 0.50f, s * 0.07f), 0.045f);
            }
            mb.C(Steel).Block(new(0, Y0 + 0.50f, 0), new(0.07f, 0.07f, 0.20f));
            // pipe leading to the output port
            mb.At(default, Deg(0, 0, -90)).C(Steel).Prism(new(0, 0.30f, 0), 0.04f, 0.04f, 0.15f, 6);
        }, default);
        var rocker = Pivot(r, "Rocker", new(0, Y0 + 0.50f, 0));
        Add(rocker, "RockerMesh", "extractor.rocker", Body(), mb =>
        {
            mb.C(Orange).Block(new(0.02f, 0, 0), new(0.66f, 0.07f, 0.08f));
            mb.C(OrangeDk).Block(new(0.31f, -0.01f, 0), new(0.10f, 0.15f, 0.07f), 0.7f, 1f);
            mb.C(Steel).Beam(new(0.31f, -0.06f, 0), new(0.31f, -0.16f, 0), 0.02f);
            mb.C(OrangeDk).Block(new(-0.28f, -0.03f, 0), new(0.17f, 0.15f, 0.13f));
        });
        Add(r, "GlowLight", "extractor.light", Glow(new Color(1f, 0.65f, 0.15f), 3f), mb => mb.Block(new(0.375f, Y0 + 0.11f, 0.16f), new(0.015f, 0.05f, 0.06f)));
        return r;
    }

    private static Node3D BuildSmelter()
    {
        var r = NewMachine("Smelter");
        Add(r, "Body", "smelter.body", Body(), mb =>
        {
            Plinth(mb, Red);
            mb.C(Red).Block(new(0, Y0 + 0.20f, 0), new(0.74f, 0.40f, 0.74f), 0.85f, 0.85f);
            mb.C(Hull).Block(new(0, Y0 + 0.41f, 0), new(0.50f, 0.025f, 0.50f));
            mb.C(Hull).Block(new(0.375f, Y0 + 0.16f, 0), new(0.07f, 0.28f, 0.42f)); // furnace mouth frame
            mb.C(RedDk).Block(new(0.35f, Y0 + 0.33f, 0), new(0.10f, 0.06f, 0.46f));
            foreach (float s in new[] { -1f, 1f })
                for (int k = 0; k < 3; k++)
                    mb.C(RedDk).Block(new(-0.05f, Y0 + 0.08f + k * 0.085f, s * 0.355f), new(0.34f, 0.035f, 0.03f));
            // chimney and a smaller stack
            mb.C(Steel).Prism(new(-0.20f, Y0 + 0.41f, -0.10f), 0.125f, 0.088f, 0.39f, 8);
            mb.C(Red).Prism(new(-0.20f, Y0 + 0.56f, -0.10f), 0.108f, 0.108f, 0.03f, 8);
            mb.C(Hull).Prism(new(-0.20f, Y0 + 0.78f, -0.10f), 0.10f, 0.10f, 0.03f, 8);
            mb.C(Steel).Prism(new(-0.22f, Y0 + 0.41f, 0.20f), 0.07f, 0.055f, 0.17f, 6);
        });
        Add(r, "GlowMouth", "smelter.mouth", Glow(new Color(1f, 0.52f, 0.1f), 3.2f), mb => mb.Block(new(0.402f, Y0 + 0.15f, 0), new(0.03f, 0.20f, 0.32f)));
        Add(r, "GlowVent", "smelter.vent", Glow(new Color(1f, 0.45f, 0.08f), 2.5f), mb => mb.Block(new(0.08f, Y0 + 0.432f, 0), new(0.26f, 0.012f, 0.07f)));
        Add(r, "GlowRim", "smelter.rim", Glow(new Color(1f, 0.55f, 0.15f), 2.8f), mb => mb.Prism(new(-0.20f, Y0 + 0.785f, -0.10f), 0.072f, 0.072f, 0.012f, 8));
        return r;
    }

    private static Node3D BuildAssembler()
    {
        var r = NewMachine("Assembler");
        Add(r, "Body", "assembler.body", Body(), mb =>
        {
            Plinth(mb, Blue);
            mb.C(BlueDk).Block(new(0, Y0 + 0.06f, 0), new(0.82f, 0.12f, 0.82f), 0.95f, 0.95f);
            foreach (float s in new[] { -1f, 1f })
            {
                mb.C(Blue).Block(new(-0.08f, Y0 + 0.40f, s * 0.36f), new(0.11f, 0.56f, 0.11f), 0.9f, 0.9f);
                mb.C(Panel).Block(new(-0.08f, Y0 + 0.69f, s * 0.36f), new(0.13f, 0.03f, 0.13f));
            }
            mb.C(Blue).Block(new(-0.08f, Y0 + 0.73f, 0), new(0.13f, 0.09f, 0.86f));
            mb.C(Hull).Block(new(-0.08f, Y0 + 0.665f, 0), new(0.06f, 0.025f, 0.74f));
            // work cells
            mb.C(Blue).Block(new(0.21f, Y0 + 0.20f, 0), new(0.34f, 0.16f, 0.52f), 0.82f, 0.82f);
            mb.C(Panel).Block(new(0.21f, Y0 + 0.285f, 0), new(0.20f, 0.012f, 0.34f));
            mb.C(Steel).Block(new(-0.29f, Y0 + 0.19f, 0), new(0.18f, 0.14f, 0.40f), 0.85f, 0.85f);
            mb.C(Hull).Block(new(-0.29f, Y0 + 0.27f, 0), new(0.10f, 0.03f, 0.20f));
        });
        Add(r, "GlowItem", "assembler.item", Glow(new Color(0.45f, 0.95f, 1f), 2.4f), mb => mb.Block(new(-0.08f, Y0 + 0.17f, 0), new(0.11f, 0.10f, 0.11f), 0.8f, 0.8f));
        Add(r, "GlowLight", "assembler.light", Glow(new Color(0.4f, 0.95f, 1f), 3f), mb => mb.Block(new(0.382f, Y0 + 0.21f, 0.15f), new(0.012f, 0.04f, 0.05f)));
        var arm = Pivot(r, "Arm", new(-0.08f, Y0 + 0.63f, 0));
        Add(arm, "ArmMesh", "assembler.arm", Body(), mb =>
        {
            mb.C(Panel).Block(new(0, 0.02f, 0), new(0.13f, 0.06f, 0.13f));
            mb.C(Steel).Block(new(0, -0.14f, 0), new(0.04f, 0.28f, 0.04f));
            mb.C(Hull).Block(new(0, -0.295f, 0), new(0.08f, 0.03f, 0.09f));
            foreach (float s in new[] { -1f, 1f }) mb.C(Blue).Block(new(0, -0.335f, s * 0.04f), new(0.05f, 0.07f, 0.015f));
        });
        return r;
    }

    private static Node3D BuildLab()
    {
        var r = NewMachine("ResearchLab");
        Add(r, "Body", "lab.body", Body(), mb =>
        {
            Plinth(mb, Purple);
            mb.C(PurpleDk).Prism(new(0, Y0, 0), 0.39f, 0.34f, 0.20f, 8, 22.5f);
            mb.C(Steel).Prism(new(0, Y0 + 0.20f, 0), 0.345f, 0.345f, 0.03f, 8, 22.5f);
            mb.C(Purple).Prism(new(0, Y0 + 0.23f, 0), 0.30f, 0.27f, 0.07f, 8, 22.5f);
            // dish mast
            mb.C(Steel).Prism(new(-0.24f, Y0 + 0.14f, 0.22f), 0.045f, 0.03f, 0.40f, 6);
            mb.C(Purple).Block(new(0.30f, Y0 + 0.12f, 0), new(0.06f, 0.12f, 0.30f));
        });
        Add(r, "GlowDome", "lab.dome", Glow(new Color(0.62f, 0.38f, 1f), 1.4f), mb => mb.Sphere(new(0, Y0 + 0.30f, 0), new(0.27f, 0.25f, 0.27f), 4, 8, true));
        var ring = Pivot(r, "Ring", new(0, Y0 + 0.43f, 0));
        Add(ring, "GlowRing", "lab.ring", Glow(new Color(0.78f, 0.5f, 1f), 2.6f), mb => mb.Torus(Vector3.Zero, 0.335f, 0.024f, 14, 3), default, Deg(16, 0, 0));
        var dish = Pivot(r, "Dish", new(-0.24f, Y0 + 0.56f, 0.22f));
        Add(dish, "DishMesh", "lab.dish", Body(), mb =>
        {
            mb.At(default, Deg(0, 0, -35));
            mb.C(Panel).Prism(new(0, 0, 0), 0.02f, 0.17f, 0.08f, 8);
            mb.C(Hull).Prism(new(0, 0.07f, 0), 0.155f, 0.165f, 0.012f, 8);
            mb.C(Steel).Beam(new(0, 0.02f, 0), new(0, 0.17f, 0), 0.02f);
            mb.C(Purple).Prism(new(0, 0.165f, 0), 0.03f, 0.0f, 0.04f, 6);
        });
        return r;
    }

    private static Node3D BuildRepairBay()
    {
        var r = NewMachine("RepairBay");
        Add(r, "Body", "repairbay.body", Body(), mb =>
        {
            Plinth(mb, Green);
            mb.C(GreenDk).Block(new(0, Y0 + 0.11f, 0), new(0.70f, 0.22f, 0.70f), 0.92f, 0.92f);
            mb.C(Hull).Block(new(0, Y0 + 0.23f, 0), new(0.52f, 0.04f, 0.52f));
            // a white medical cross on the roof and a mast with the beacon dish
            mb.C(White).Block(new(0, Y0 + 0.255f, 0), new(0.30f, 0.012f, 0.08f));
            mb.C(White).Block(new(0, Y0 + 0.255f, 0), new(0.08f, 0.012f, 0.30f));
            mb.C(Steel).Prism(new(-0.22f, Y0 + 0.22f, -0.22f), 0.035f, 0.025f, 0.34f, 6);
            foreach (float s in new[] { -1f, 1f })
                mb.C(Steel).Block(new(0.30f * s, Y0 + 0.10f, 0.0f), new(0.08f, 0.16f, 0.30f));
        });
        var dish = Pivot(r, "Dish", new(-0.22f, Y0 + 0.58f, -0.22f));
        Add(dish, "Beacon", "repairbay.dish", Body(), mb =>
        {
            mb.At(default, Deg(0, 0, 0)).C(Panel).Prism(new(0, 0, 0), 0.12f, 0.03f, 0.07f, 8);
        });
        Add(r, "GlowLight", "repairbay.light", Glow(new Color(0.35f, 1f, 0.6f), 2.6f), mb =>
        {
            mb.Torus(new(0, Y0 + 0.235f, 0), 0.30f, 0.012f, 12, 3);
            mb.Sphere(new(-0.22f, Y0 + 0.60f, -0.22f), new(0.04f, 0.03f, 0.04f), 3, 6);
        });
        return r;
    }

    private static Node3D BuildUnloader()
    {
        var r = NewMachine("Unloader");
        Add(r, "Body", "unloader.body", Body(), mb =>
        {
            Plinth(mb, Green);
            mb.C(GreenDk).Block(new(-0.10f, Y0 + 0.14f, 0), new(0.60f, 0.28f, 0.70f), 0.94f, 0.94f);
            mb.C(Hull).Block(new(-0.10f, Y0 + 0.29f, 0), new(0.44f, 0.04f, 0.50f));
            // a chute pointing out of the hold towards +X, where the belt starts
            mb.C(Steel).Block(new(0.30f, Y0 + 0.12f, 0), new(0.30f, 0.14f, 0.30f), 0.8f, 0.8f);
            mb.C(Panel).Block(new(0.30f, Y0 + 0.20f, 0), new(0.34f, 0.025f, 0.34f));
            foreach (float s in new[] { -1f, 1f })
                mb.C(Steel).Prism(new(-0.30f, Y0, 0.30f * s), 0.05f, 0.04f, 0.40f, 6);
        });
        Add(r, "GlowLight", "unloader.light", Glow(new Color(0.4f, 1f, 0.6f), 2.4f), mb =>
        {
            mb.Sphere(new(-0.30f, Y0 + 0.43f, 0.30f), new(0.04f, 0.03f, 0.04f), 3, 6);
            mb.Sphere(new(-0.30f, Y0 + 0.43f, -0.30f), new(0.04f, 0.03f, 0.04f), 3, 6);
            mb.Block(new(0.30f, Y0 + 0.215f, 0), new(0.16f, 0.012f, 0.16f));
        });
        return r;
    }

    /// <summary>The hydrogen cracker: a squat reaction vessel and a tall cold tank on a plinth, with a cyan glow that breathes while it works.</summary>
    private static Node3D BuildCracker()
    {
        var r = NewMachine("Cracker");
        Add(r, "Body", "cracker.body", Body(), mb =>
        {
            Plinth(mb, Blue);
            mb.C(BlueDk).Prism(new(-0.12f, Y0, 0.12f), 0.30f, 0.30f, 0.46f, 10);
            mb.C(Panel).Prism(new(-0.12f, Y0 + 0.46f, 0.12f), 0.27f, 0.14f, 0.07f, 10);
            mb.C(Steel).Prism(new(0.18f, Y0, -0.20f), 0.17f, 0.17f, 0.82f, 8);
            mb.C(Hull).Prism(new(0.18f, Y0 + 0.82f, -0.20f), 0.15f, 0.06f, 0.07f, 8);
            mb.C(Steel).Beam(new(-0.12f, Y0 + 0.30f, 0.12f), new(0.18f, Y0 + 0.60f, -0.20f), 0.045f);
            mb.C(Blue).Block(new(0.36f, Y0 + 0.12f, 0.1f), new(0.2f, 0.12f, 0.2f), 0.85f, 0.85f);
        });
        Add(r, "GlowRim", "cracker.ring", Glow(new Color(0.4f, 0.9f, 1f), 2.8f), mb =>
        {
            mb.Prism(new(-0.12f, Y0 + 0.22f, 0.12f), 0.315f, 0.315f, 0.035f, 10);
            mb.Prism(new(0.18f, Y0 + 0.50f, -0.20f), 0.18f, 0.18f, 0.03f, 8);
        });
        Add(r, "GlowLight", "cracker.light", Glow(new Color(0.45f, 1f, 0.9f), 3f), mb => mb.Block(new(0.455f, Y0 + 0.14f, 0.1f), new(0.012f, 0.05f, 0.08f)));
        return r;
    }

    private static Node3D BuildWall()
    {
        var r = NewMachine("Wall");
        Add(r, "Body", "wall.body", Body(), mb =>
        {
            Plinth(mb, YellowDk);
            // A thick armoured slab on the plinth, with a bevelled top and hazard stripes.
            mb.C(HullLight).Block(new(0, Y0 + 0.18f, 0), new(0.90f, 0.36f, 0.34f), 0.92f, 0.82f);
            mb.C(Steel).Block(new(0, Y0 + 0.37f, 0), new(0.86f, 0.05f, 0.30f), 0.9f, 0.8f);
            foreach (float s in new[] { -1f, 1f })
            {
                mb.C(Hull).Block(new(s * 0.36f, Y0 + 0.30f, 0), new(0.14f, 0.40f, 0.38f), 0.9f, 0.9f);
                foreach (float t in new[] { -1f, 1f })
                {
                    mb.C(Yellow).Block(new(s * 0.14f, Y0 + 0.14f, t * 0.175f), new(0.20f, 0.07f, 0.012f));
                }
            }
        });
        Add(r, "GlowLight", "wall.light", Glow(new Color(1f, 0.8f, 0.15f), 2.4f), mb =>
        {
            foreach (float s in new[] { -1f, 1f })
                mb.Sphere(new(s * 0.36f, Y0 + 0.52f, 0), new(0.04f, 0.03f, 0.04f), 3, 6);
        });
        return r;
    }

    private static Node3D BuildDepot()
    {
        var r = NewMachine("Depot");
        Add(r, "Body", "depot.body", Body(), mb =>
        {
            Plinth(mb, Green);
            mb.C(GreenDk).Block(new(-0.12f, Y0 + 0.10f, 0), new(0.62f, 0.20f, 0.86f), 0.96f, 0.96f);
            mb.C(Hull).Prism(new(-0.12f, Y0 + 0.20f, 0), 0.31f, 0.30f, 0.03f, 8, 22.5f);
            mb.C(Panel).Block(new(-0.12f, Y0 + 0.2335f, 0), new(0.30f, 0.006f, 0.045f));
            mb.C(Panel).Block(new(-0.12f, Y0 + 0.2335f, 0), new(0.045f, 0.006f, 0.30f));
            // open bay at +X
            mb.C(Hull).Block(new(0.31f, Y0 + 0.03f, 0), new(0.26f, 0.05f, 0.78f));
            foreach (float s in new[] { -1f, 1f })
                mb.C(Green).Block(new(0.32f, Y0 + 0.13f, s * 0.37f), new(0.28f, 0.20f, 0.08f));
            mb.C(Green).Block(new(0.435f, Y0 + 0.235f, 0), new(0.07f, 0.06f, 0.82f));
            mb.C(Hull).Block(new(0.18f, Y0 + 0.155f, 0), new(0.04f, 0.22f, 0.66f)); // inner bay door frame
            // corner pylons
            foreach (float sx in new[] { -1f, 1f })
                foreach (float sz in new[] { -1f, 1f })
                    mb.C(Steel).Prism(new(sx * 0.395f, Y0, sz * 0.395f), 0.05f, 0.035f, 0.36f, 6);
        });
        Add(r, "GlowPad", "depot.pad", Glow(new Color(0.35f, 1f, 0.55f), 2.4f), mb => mb.Torus(new(-0.12f, Y0 + 0.24f, 0), 0.22f, 0.014f, 12, 3));
        Add(r, "GlowBay", "depot.bay", Glow(new Color(0.35f, 1f, 0.55f), 2.2f), mb => mb.Block(new(0.34f, Y0 + 0.062f, 0), new(0.20f, 0.012f, 0.50f)));
        Add(r, "GlowBeacons", "depot.beacons", Glow(new Color(0.45f, 1f, 0.6f), 3.2f), mb =>
        {
            foreach (float sx in new[] { -1f, 1f })
                foreach (float sz in new[] { -1f, 1f })
                    mb.Sphere(new(sx * 0.395f, Y0 + 0.385f, sz * 0.395f), new(0.045f, 0.045f, 0.045f), 3, 6);
        });
        return r;
    }

    private static Node3D BuildTurret()
    {
        var r = NewMachine("Turret");
        Add(r, "Body", "turret.body", Body(), mb =>
        {
            Plinth(mb, Yellow);
            mb.C(Hull).Prism(new(0, Y0, 0), 0.41f, 0.32f, 0.10f, 8, 22.5f);
            mb.C(YellowDk).Prism(new(0, Y0 + 0.10f, 0), 0.325f, 0.325f, 0.025f, 8, 22.5f);
            mb.C(Steel).Prism(new(0, Y0 + 0.125f, 0), 0.21f, 0.19f, 0.09f, 8);
            // ammo crates
            foreach (float s in new[] { -1f, 1f }) mb.C(YellowDk).Block(new(-0.16f, Y0 + 0.17f, s * 0.26f), new(0.13f, 0.10f, 0.10f));
        });
        var head = Pivot(r, "Head", new(0, Y0 + 0.22f, 0));
        Add(head, "HeadMesh", "turret.head", Body(), mb =>
        {
            mb.C(Yellow).Block(new(-0.02f, 0.075f, 0), new(0.40f, 0.15f, 0.34f), 0.82f, 0.78f);
            foreach (float s in new[] { -1f, 1f })
                mb.C(YellowDk).Block(new(0.02f, 0.085f, s * 0.185f), new(0.30f, 0.10f, 0.03f));
            mb.C(Hull).Block(new(-0.14f, 0.19f, 0), new(0.14f, 0.07f, 0.16f));
            mb.C(Hull).Block(new(0.16f, 0.09f, 0), new(0.04f, 0.10f, 0.25f)); // mantlet
        });
        var barrel = Pivot(head, "Barrel", new(0.12f, 0.09f, 0));
        Add(barrel, "BarrelMesh", "turret.barrel", Body(), mb =>
        {
            foreach (float s in new[] { -1f, 1f })
            {
                mb.At(new(0, 0, s * 0.085f), Deg(0, 0, -90));
                mb.C(Hull).Prism(new(0, 0, 0), 0.062f, 0.056f, 0.12f, 6);
                mb.C(Steel).Prism(new(0, 0.12f, 0), 0.036f, 0.03f, 0.17f, 6);
                mb.C(YellowDk).Prism(new(0, 0.29f, 0), 0.046f, 0.046f, 0.03f, 6);
            }
        });
        Add(barrel, "GlowMuzzle", "turret.muzzle", Glow(new Color(1f, 0.8f, 0.2f), 2.5f), mb =>
        {
            foreach (float s in new[] { -1f, 1f })
            {
                mb.At(new(0, 0, s * 0.085f), Deg(0, 0, -90));
                mb.Prism(new(0, 0.319f, 0), 0.028f, 0.028f, 0.008f, 6);
            }
        });
        return r;
    }

    private static Node3D BuildCrate()
    {
        var r = NewMachine("Crate");
        Add(r, "Body", "crate.body", Body(), mb =>
        {
            Plinth(mb, Orange);
            mb.C(Steel).Block(new(0, Y0 + 0.26f, 0), new(0.70f, 0.52f, 0.70f), 0.97f, 0.97f);
            mb.C(Orange).Block(new(0, Y0 + 0.26f, 0), new(0.72f, 0.08f, 0.72f));
            mb.C(Orange).Block(new(0, Y0 + 0.08f, 0), new(0.72f, 0.06f, 0.72f));
            mb.C(Hull).Block(new(0, Y0 + 0.50f, 0), new(0.40f, 0.03f, 0.40f));
        });
        return r;
    }

    // ---- ships and enemies -----------------------------------------------------------------------

    private static Node3D BuildScout()
    {
        var r = new Node3D { Name = "Scout" };
        Add(r, "Hull", "scout.hull", Body(), mb =>
        {
            mb.C(Hot).Sphere(Vector3.Zero, new(0.30f, 0.085f, 0.12f), 3, 6);
            mb.C(HotDk).Sphere(new(-0.02f, 0.03f, 0), new(0.15f, 0.07f, 0.09f), 2, 6, true);
            foreach (float s in new[] { 1f, -1f })
            {
                mb.C(HotDk).Extrude(new[] { new Vector2(0.12f, 0.05f * s), new Vector2(-0.04f, 0.32f * s), new Vector2(-0.16f, 0.32f * s), new Vector2(-0.14f, 0.05f * s) }, -0.012f, 0.012f);
                mb.C(Hot).Beam(new(-0.04f, 0f, 0.31f * s), new(-0.16f, 0f, 0.31f * s), 0.035f);
            }
        });
        Add(r, "GlowEye", "scout.eye", Glow(new Color(1f, 0.9f, 0.2f), 4f), mb => mb.Sphere(new(0.17f, 0.03f, 0), new(0.045f, 0.035f, 0.045f), 3, 6));
        Add(r, "GlowEngine", "scout.engine", Glow(new Color(1f, 0.55f, 0.1f), 4f), mb => mb.At(default, Deg(0, 0, 90)).Prism(new(0, 0.27f, 0), 0.045f, 0f, 0.13f, 6));
        var spin = Pivot(r, "Spinner", new(-0.02f, 0, 0));
        Add(spin, "GlowSpin", "scout.spin", Glow(new Color(1f, 0.4f, 0.1f), 2.6f), mb => mb.Torus(Vector3.Zero, 0.15f, 0.014f, 10, 3), default, Deg(0, 0, 90));
        return r;
    }

    private static Node3D BuildStriker()
    {
        var r = new Node3D { Name = "Striker" };
        Add(r, "Hull", "striker.hull", Body(), mb =>
        {
            mb.C(new Color(0.18f, 0.05f, 0.30f)).Block(new(0, 0, 0), new(0.62f, 0.20f, 0.46f), 0.8f, 0.72f);
            mb.C(Magenta).Block(new(0.02f, 0.115f, 0), new(0.40f, 0.04f, 0.30f), 0.9f, 0.9f);
            mb.At(default, Deg(0, 0, -90)).C(new Color(0.28f, 0.08f, 0.42f)).Prism(new(0, 0.31f, 0), 0.25f, 0.03f, 0.2f, 4, 45f);
            foreach (float s in new[] { 1f, -1f })
            {
                mb.At(default).C(new Color(0.28f, 0.08f, 0.42f)).Block(new(0, -0.03f, s * 0.31f), new(0.42f, 0.14f, 0.15f), 0.85f, 0.85f);
                mb.C(Magenta).Block(new(-0.05f, 0.05f, s * 0.31f), new(0.26f, 0.03f, 0.11f));
                mb.At(default, Deg(0, 0, -90)).C(Steel).Prism(new(0, 0.18f, s * 0.31f), 0.035f, 0.03f, 0.30f, 6);
                mb.At(default, Deg(0, 0, 90)).C(Magenta).Prism(new(0, 0.20f, s * 0.31f), 0.07f, 0f, 0.14f, 4, 45f);
                mb.At(default).C(Hull).Block(new(0.12f, 0.0f, s * 0.225f), new(0.30f, 0.10f, 0.03f));
            }
            mb.At(default).C(Magenta).Beam(new(-0.10f, 0.10f, 0), new(-0.22f, 0.30f, 0), 0.05f);
        });
        Add(r, "GlowCore", "striker.core", Glow(Magenta, 3.5f), mb => mb.Sphere(new(0.05f, 0.14f, 0), new(0.09f, 0.05f, 0.09f), 3, 8));
        Add(r, "GlowEngine", "striker.engine", Glow(new Color(1f, 0.3f, 0.85f), 3.6f), mb =>
        {
            foreach (float s in new[] { 1f, -1f })
                mb.At(default, Deg(0, 0, 90)).Prism(new(0, 0.30f, 0.12f * s), 0.07f, 0f, 0.18f, 6);
        });
        return r;
    }

    /// <summary>A heavy enemy: a wide bronze slab with a ram prow, armoured flanks, shoulder guns and three amber engines.</summary>
    private static Node3D BuildBrute()
    {
        var bronze = new Color(0.30f, 0.19f, 0.09f);
        var bronzeDk = new Color(0.16f, 0.10f, 0.06f);
        var amber = new Color(1f, 0.62f, 0.08f);
        var r = new Node3D { Name = "Brute" };
        Add(r, "Hull", "brute.hull", Body(), mb =>
        {
            mb.C(bronzeDk).Block(new(0, 0, 0), new(1.0f, 0.34f, 0.78f), 0.8f, 0.85f);
            mb.C(bronze).Block(new(-0.05f, 0.20f, 0), new(0.62f, 0.10f, 0.50f), 0.8f, 0.8f);
            // The ram: a four-sided wedge from the front edge of the hull, tip along +X.
            mb.At(default, Deg(0, 0, -90)).C(Steel).Prism(new(0, 0.50f, 0), 0.34f, 0.04f, 0.30f, 4, 45f);
            foreach (float s in new[] { 1f, -1f })
            {
                mb.At(default).C(bronze).Block(new(0.05f, -0.04f, s * 0.46f), new(0.80f, 0.26f, 0.16f), 0.9f, 0.9f);
                mb.C(amber).Block(new(0.02f, 0.10f, s * 0.47f), new(0.50f, 0.03f, 0.05f));
                mb.C(Steel).Beam(new(0.15f, 0.14f, s * 0.46f), new(0.66f, 0.14f, s * 0.46f), 0.075f);
                mb.C(bronzeDk).Block(new(0.68f, 0.14f, s * 0.46f), new(0.08f, 0.11f, 0.11f));
            }
            // Rear bulkhead the engines sit on.
            mb.C(Steel).Block(new(-0.53f, 0f, 0), new(0.14f, 0.30f, 0.50f));
        });
        Add(r, "GlowCore", "brute.core", Glow(amber, 3.2f), mb => mb.Sphere(new(0.10f, 0.27f, 0), new(0.12f, 0.06f, 0.12f), 3, 8));
        Add(r, "GlowEngine", "brute.engine", Glow(new Color(1f, 0.5f, 0.1f), 3.8f), mb =>
        {
            foreach (float z in new[] { -0.19f, 0f, 0.19f })
                mb.At(default, Deg(0, 0, 90)).Prism(new(0, 0.60f, z), 0.085f, 0f, 0.22f, 6);
        });
        return r;
    }

    // ---- deposits ---------------------------------------------------------------------------------------

    internal sealed class Rng
    {
        private uint _s;
        public Rng(int seed) { unchecked { _s = (uint)seed * 2654435761u + 0x9E3779B9u; } if (_s == 0) _s = 1; }

        public float F()
        {
            _s ^= _s << 13; _s ^= _s >> 17; _s ^= _s << 5;
            return (_s & 0xFFFFFF) / (float)0x1000000;
        }
    }

    internal static float Hash3(int x, int y, int z, int seed)
    {
        unchecked
        {
            uint h = (uint)(x * 374761393 + y * 668265263 + z * 1274126177) ^ ((uint)seed * 2246822519u);
            h = (h ^ (h >> 13)) * 1274126177u;
            h ^= h >> 16;
            return (h & 0xFFFFFF) / (float)0x1000000;
        }
    }

    private static float Noise3(Vector3 p, int seed)
    {
        int x = Mathf.FloorToInt(p.X), y = Mathf.FloorToInt(p.Y), z = Mathf.FloorToInt(p.Z);
        float fx = p.X - x, fy = p.Y - y, fz = p.Z - z;
        fx = fx * fx * (3 - 2 * fx); fy = fy * fy * (3 - 2 * fy); fz = fz * fz * (3 - 2 * fz);
        float L(float a, float b, float t) => a + (b - a) * t;
        float c00 = L(Hash3(x, y, z, seed), Hash3(x + 1, y, z, seed), fx);
        float c10 = L(Hash3(x, y + 1, z, seed), Hash3(x + 1, y + 1, z, seed), fx);
        float c01 = L(Hash3(x, y, z + 1, seed), Hash3(x + 1, y, z + 1, seed), fx);
        float c11 = L(Hash3(x, y + 1, z + 1, seed), Hash3(x + 1, y + 1, z + 1, seed), fx);
        return L(L(c00, c10, fy), L(c01, c11, fy), fz);
    }

    /// <summary>Surface point of the asteroid in direction <paramref name="d"/> (unit vector).</summary>
    internal static Vector3 RockPoint(Vector3 d, int seed)
    {
        var off = new Vector3(seed * 1.7f, seed * 0.9f, seed * 2.3f);
        float n = Noise3(d * 1.5f + off, seed) * 0.6f + Noise3(d * 3.3f + off * 2f, seed + 1) * 0.3f + Noise3(d * 7f + off, seed + 2) * 0.1f;
        n = Mathf.Clamp((n - 0.5f) * 2.0f + 0.5f, 0f, 1f);
        float rad = 1.5f * (0.68f + 0.6f * n);
        return new Vector3(d.X * rad, d.Y * rad * 0.86f, d.Z * rad);
    }

    internal static void BuildRock(Mb mb, Color tint, int seed, float tone = 1f, float tintShare = 0.13f)
    {
        // icosphere, one subdivision level above the base: 320 faces
        float t = (1f + MathF.Sqrt(5f)) / 2f;
        var verts = new List<Vector3>
        {
            new(-1, t, 0), new(1, t, 0), new(-1, -t, 0), new(1, -t, 0), new(0, -1, t), new(0, 1, t),
            new(0, -1, -t), new(0, 1, -t), new(t, 0, -1), new(t, 0, 1), new(-t, 0, -1), new(-t, 0, 1),
        };
        for (int i = 0; i < verts.Count; i++) verts[i] = verts[i].Normalized();
        var faces = new List<(int, int, int)>
        {
            (0, 11, 5), (0, 5, 1), (0, 1, 7), (0, 7, 10), (0, 10, 11), (1, 5, 9), (5, 11, 4), (11, 10, 2), (10, 7, 6), (7, 1, 8),
            (3, 9, 4), (3, 4, 2), (3, 2, 6), (3, 6, 8), (3, 8, 9), (4, 9, 5), (2, 4, 11), (6, 2, 10), (8, 6, 7), (9, 8, 1),
        };
        for (int level = 0; level < 2; level++)
        {
            var cache = new Dictionary<(int, int), int>();
            int Mid(int a, int b)
            {
                var k = a < b ? (a, b) : (b, a);
                if (cache.TryGetValue(k, out int m)) return m;
                verts.Add(((verts[a] + verts[b]) / 2f).Normalized());
                return cache[k] = verts.Count - 1;
            }
            var next = new List<(int, int, int)>();
            foreach (var (a, b, c) in faces)
            {
                int ab = Mid(a, b), bc = Mid(b, c), ca = Mid(c, a);
                next.Add((a, ab, ca)); next.Add((b, bc, ab)); next.Add((c, ca, bc)); next.Add((ab, bc, ca));
            }
            faces = next;
        }
        var pos = new Vector3[verts.Count];
        for (int i = 0; i < pos.Length; i++) pos[i] = RockPoint(verts[i], seed);
        var rock = new Color(0.09f, 0.085f, 0.10f).Lerp(tint, tintShare);
        rock = new Color(rock.R * tone, rock.G * tone, rock.B * tone);
        int fi = 0;
        foreach (var (a, b, c) in faces)
        {
            float h = Hash3(fi++, 7, 3, seed);
            float up = (verts[a].Y + verts[b].Y + verts[c].Y) / 3f;
            float shade = 0.78f + 0.3f * h + 0.12f * up;
            var col = new Color(rock.R * shade, rock.G * shade, rock.B * shade);
            mb.C(col).Tri(pos[a], pos[b], pos[c], Vector3.Zero);
        }
    }

    private static void BuildCrystals(Mb mb, int seed)
    {
        var rng = new Rng(seed + 101);
        int clusters = 6 + (int)(rng.F() * 2.99f);
        for (int k = 0; k < clusters; k++)
        {
            float y = -0.25f + rng.F() * 1.2f, ang = rng.F() * Mathf.Tau;
            float rr = MathF.Sqrt(MathF.Max(0f, 1f - Mathf.Clamp(y, -1f, 1f) * Mathf.Clamp(y, -1f, 1f)));
            var dir = new Vector3(MathF.Cos(ang) * rr, y, MathF.Sin(ang) * rr).Normalized();
            var p = RockPoint(dir, seed) - dir * 0.03f;
            int count = 2 + (int)(rng.F() * 1.99f);
            for (int c = 0; c < count; c++)
            {
                var jitter = new Vector3(rng.F() - 0.5f, rng.F() - 0.5f, rng.F() - 0.5f) * (c == 0 ? 0.15f : 0.9f);
                var axis = (dir + jitter).Normalized();
                float s = (c == 0 ? 0.9f + rng.F() * 0.5f : 0.5f + rng.F() * 0.4f) * 1.5f;
                var origin = p + new Vector3(rng.F() - 0.5f, rng.F() - 0.5f, rng.F() - 0.5f) * (c == 0 ? 0f : 0.14f);
                mb.At(origin, Vector3.Zero);
                var q = new Quaternion(Vector3.Up, axis);
                mb.At(origin, q.GetEuler());
                mb.Prism(Vector3.Zero, 0.085f * s, 0.07f * s, 0.26f * s, 6, rng.F() * 60f);
                mb.Prism(new(0, 0.26f * s, 0), 0.07f * s, 0f, 0.13f * s, 6);
            }
        }
    }
}
