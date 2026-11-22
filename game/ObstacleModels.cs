using System;
using System.Collections.Generic;
using Godot;
using Starfoundry.Sim;

namespace Starfoundry.Game;

/// <summary>
/// One obstacle of the wide galaxy (ADR 0028) drawn as a procedural model in the style of <see cref="DepositView"/>: rocks, mines,
/// crystals, nebulae, wrecks and drifting debris. Meshes are cached per kind and seed (the models builder caches by key), materials
/// are shared, only the blinking lights have a material of their own. Everything is in cells (1 unit = 1 cell, like the deposits).
/// </summary>
public sealed partial class ObstacleView : Node3D
{
    private readonly int _id;
    private readonly ObstacleKindDef _def;
    private readonly float _radius;
    private readonly int _seed;
    private MeshInstance3D? _light, _halo;
    private StandardMaterial3D? _lightMat;
    private Node3D? _spin;
    private readonly float _spinRate;

    public ObstacleView(int id, ObstacleInfo info, ObstacleKindDef def, string kindId)
    {
        _id = id;
        _def = def;
        _seed = info.Seed;
        _radius = info.Radius / (float)Units.CellUnits;
        Name = "Obstacle" + id;
        _spinRate = ((info.Seed % 7) - 3) * 0.09f;
        switch (def.Behaviour)
        {
            case ObstacleBehaviour.Solid: BuildRockView(kindId); break;
            case ObstacleBehaviour.Mine: if (kindId.EndsWith("crystal")) BuildCrystal(); else BuildMine(); break;
            case ObstacleBehaviour.Cloud: BuildCloud(kindId.Contains("ion")); break;
            case ObstacleBehaviour.Wreck: BuildWreck(); break;
            case ObstacleBehaviour.Drifter: BuildDebris(); break;
        }
    }

    private Node3D Holder(float scale)
    {
        var n = new Node3D { Name = "Body", Scale = Vector3.One * scale };
        AddChild(n);
        return n;
    }

    // ---- solid rocks -------------------------------------------------------------------------------

    private void BuildRockView(string kindId)
    {
        bool big = kindId.EndsWith("big");
        // The rock builder makes about 1.3 cells of radius; stretch a little so no two rocks look alike.
        var body = Holder(_radius / 1.3f);
        body.Scale = new Vector3(1f + (_seed % 5) * 0.06f, 0.82f + (_seed % 3) * 0.1f, 1f + (_seed % 4) * 0.07f) * (_radius / 1.3f);
        var tint = big ? new Color(0.42f, 0.36f, 0.32f) : new Color(0.38f, 0.36f, 0.40f);
        var rockMi = Models.Add(body, "Rock", $"obs.rock.{kindId}.{_seed % 40}", Materials.Rock(), mb => Models.BuildRock(mb, tint, _seed % 40, big ? 1.5f : 1.8f, 0.5f));
        rockMi.MaterialOverride = Materials.Rock((body.Scale.X + body.Scale.Y + body.Scale.Z) / 3f);
        body.RotationDegrees = new Vector3(_seed % 360, (_seed * 7) % 360, 0);
    }

    // ---- mines and crystals ------------------------------------------------------------------------

    private void BuildMine()
    {
        var body = Holder(_radius);
        Models.Add(body, "Shell", "obs.mine.shell", Models.Burnt(), mb =>
        {
            mb.C(new Color(0.16f, 0.16f, 0.19f)).Sphere(Vector3.Zero, Vector3.One * 0.78f, 6, 12);
            mb.C(new Color(0.30f, 0.28f, 0.26f)).Torus(Vector3.Zero, 0.78f, 0.07f, 16, 5);
            var rng = new Models.Rng(11);
            for (int i = 0; i < 14; i++)
            {
                var dir = new Vector3(rng.F() * 2 - 1, rng.F() * 2 - 1, rng.F() * 2 - 1);
                if (dir.LengthSquared() < 0.05f) dir = Vector3.Up;
                dir = dir.Normalized();
                mb.At(dir * 0.7f, new Quaternion(Vector3.Up, dir).GetEuler());
                mb.C(new Color(0.34f, 0.31f, 0.28f)).Prism(Vector3.Zero, 0.12f, 0f, 0.5f, 5);
            }
            mb.At(Vector3.Zero);
        });
        _lightMat = new StandardMaterial3D { AlbedoColor = new Color(0.5f, 0.05f, 0.05f), EmissionEnabled = true, Emission = new Color(1f, 0.12f, 0.08f), EmissionEnergyMultiplier = 2f };
        _light = new MeshInstance3D { Name = "Light", Mesh = new SphereMesh { Radius = 0.2f, Height = 0.4f, RadialSegments = 10, Rings = 5 }, MaterialOverride = _lightMat, Position = new Vector3(0, 0.82f, 0) };
        body.AddChild(_light);
        // The blast radius, shown only once the fuse burns.
        _halo = new MeshInstance3D
        {
            Name = "Blast", Visible = false, Scale = Vector3.One * (_def.BlastCells / Math.Max(0.5f, _radius)),
            Mesh = new SphereMesh { Radius = 1f, Height = 2f, RadialSegments = 24, Rings = 12 },
            MaterialOverride = Models.GlowAlpha(new Color(1f, 0.15f, 0.1f), 0.35f, 0.045f),
        };
        body.AddChild(_halo);
    }

    private void BuildCrystal()
    {
        var body = Holder(_radius);
        _spin = body;
        Models.Add(body, "Crystals", $"obs.crystal.{_seed % 12}", CrystalMat(), mb =>
        {
            var rng = new Models.Rng(_seed % 12 + 5);
            for (int c = 0; c < 8; c++)
            {
                var axis = new Vector3(rng.F() * 2 - 1, (rng.F() * 2 - 1) * 0.8f + 0.2f, rng.F() * 2 - 1).Normalized();
                float s = 0.55f + rng.F() * 0.7f;
                mb.At(axis * 0.15f, new Quaternion(Vector3.Up, axis).GetEuler());
                mb.Prism(Vector3.Zero, 0.17f * s, 0.12f * s, 0.7f * s, 6, rng.F() * 60f);
                mb.Prism(new Vector3(0, 0.7f * s, 0), 0.12f * s, 0f, 0.3f * s, 6);
            }
            mb.At(Vector3.Zero);
        });
        _lightMat = null;
        _halo = new MeshInstance3D
        {
            Name = "Blast", Visible = false, Scale = Vector3.One * (_def.BlastCells / Math.Max(0.5f, _radius)),
            Mesh = new SphereMesh { Radius = 1f, Height = 2f, RadialSegments = 24, Rings = 12 },
            MaterialOverride = Models.GlowAlpha(new Color(0.8f, 0.4f, 1f), 0.35f, 0.045f),
        };
        body.AddChild(_halo);
    }

    private static StandardMaterial3D? _crystalMat;

    /// <summary>Unstable crystals: faceted library crystal with a violet emissive mask.</summary>
    private static StandardMaterial3D CrystalMat()
    {
        if (_crystalMat is not null) return _crystalMat;
        var m = (StandardMaterial3D)Materials.Crystal(0.3f, 0.1f).Duplicate();
        var violet = new Color(0.65f, 0.35f, 1f);
        m.VertexColorUseAsAlbedo = true;
        m.AlbedoColor = violet * 0.7f;
        m.EmissionEnabled = true;
        m.Emission = violet;
        m.EmissionEnergyMultiplier = 1.6f;
        m.EmissionOperator = BaseMaterial3D.EmissionOperatorEnum.Multiply;
        return _crystalMat = m;
    }

    // ---- nebulae -----------------------------------------------------------------------------------

    private void BuildCloud(bool ion)
    {
        var body = Holder(_radius);
        _spin = body;
        var rng = new Models.Rng(_seed % 50 + 1);
        Color a = ion ? new Color(0.20f, 0.65f, 1f) : new Color(0.62f, 0.28f, 0.95f);
        Color b = ion ? new Color(0.35f, 0.95f, 0.9f) : new Color(1f, 0.55f, 0.18f);
        string key = $"obs.cloud.{(ion ? "ion" : "grav")}.{_seed % 50}";
        // Overlapping soft puffs, two colours, so the nebula reads as a volume from outside and tints the view from inside.
        Models.Add(body, "PuffsA", key + ".a", Models.GlowAlpha(a, 0.9f, 0.07f), mb =>
        {
            for (int i = 0; i < 12; i++)
            {
                var c = new Vector3(rng.F() - 0.5f, (rng.F() - 0.5f) * 0.5f, rng.F() - 0.5f) * 1.2f;
                mb.Sphere(c, new Vector3(0.35f + rng.F() * 0.35f, 0.25f + rng.F() * 0.25f, 0.35f + rng.F() * 0.35f), 5, 10);
            }
        });
        Models.Add(body, "PuffsB", key + ".b", Models.GlowAlpha(b, 1.2f, 0.06f), mb =>
        {
            for (int i = 0; i < 7; i++)
            {
                var c = new Vector3(rng.F() - 0.5f, (rng.F() - 0.5f) * 0.4f, rng.F() - 0.5f) * 0.9f;
                mb.Sphere(c, new Vector3(0.25f + rng.F() * 0.25f, 0.18f + rng.F() * 0.15f, 0.25f + rng.F() * 0.25f), 5, 10);
            }
        });
        if (ion)
        {
            // Sparks: tiny bright points scattered through the cloud.
            Models.Add(body, "Sparks", key + ".sparks", Models.Glow(new Color(0.7f, 1f, 1f), 4f), mb =>
            {
                for (int i = 0; i < 36; i++)
                {
                    var c = new Vector3(rng.F() - 0.5f, (rng.F() - 0.5f) * 0.6f, rng.F() - 0.5f) * 1.7f;
                    mb.Block(c, Vector3.One * (0.018f + rng.F() * 0.02f));
                }
            });
        }
        else
        {
            // Swirl: thin rings of two colours around the middle.
            Models.Add(body, "Swirl", key + ".swirl", Models.GlowAlpha(b, 1.6f, 0.16f), mb =>
            {
                mb.At(Vector3.Zero, new Vector3(0.25f, 0, 0.15f)).Torus(Vector3.Zero, 0.62f, 0.025f, 40, 4);
                mb.At(Vector3.Zero, new Vector3(-0.2f, 0, 0.3f)).Torus(Vector3.Zero, 0.85f, 0.02f, 48, 4);
                mb.At(Vector3.Zero, new Vector3(0.1f, 0, -0.25f)).Torus(Vector3.Zero, 0.4f, 0.025f, 32, 4);
            });
        }
    }

    // ---- wrecks ------------------------------------------------------------------------------------

    private void BuildWreck()
    {
        var body = Holder(_radius / 1.4f);
        body.RotationDegrees = new Vector3((_seed % 30) - 15, (_seed * 13) % 360, (_seed % 40) - 20);
        Models.Add(body, "Hull", $"obs.wreck.{_seed % 10}", Materials.Rust(), mb =>
        {
            var rng = new Models.Rng(_seed % 10 + 3);
            var plate = new Color(0.30f, 0.33f, 0.34f);
            var dark = new Color(0.16f, 0.17f, 0.19f);
            // Half a hull: a torn prism with ribs sticking out of the break.
            mb.At(Vector3.Zero, new Vector3(0, 0, Mathf.Pi / 2));
            mb.C(plate).Prism(new Vector3(0, -1.3f, 0), 0.7f, 0.5f, 2.2f, 6);
            mb.At(Vector3.Zero);
            for (int i = 0; i < 5; i++)
            {
                float ang = i * 1.1f + rng.F();
                var a = new Vector3(0.95f, MathF.Cos(ang) * 0.6f, MathF.Sin(ang) * 0.6f);
                var b = a + new Vector3(0.3f + rng.F() * 0.7f, (rng.F() - 0.5f) * 0.5f, (rng.F() - 0.5f) * 0.5f);
                mb.C(dark).Beam(a, b, 0.06f);
            }
            // A cockpit stub, a fin and loose plates.
            mb.C(dark).Block(new Vector3(-1.1f, 0.5f, 0), new Vector3(0.6f, 0.3f, 0.5f));
            mb.C(plate).Block(new Vector3(-0.6f, 0.9f, 0), new Vector3(0.8f, 0.5f, 0.05f), 0.4f, 1f);
            for (int i = 0; i < 4; i++)
            {
                var c = new Vector3(1.4f + rng.F() * 1.0f, (rng.F() - 0.5f) * 1.5f, (rng.F() - 0.5f) * 1.5f);
                mb.At(c, new Vector3(rng.F() * 3, rng.F() * 3, rng.F() * 3));
                mb.C(rng.F() > 0.5f ? plate : dark).Block(Vector3.Zero, new Vector3(0.45f, 0.05f, 0.35f));
            }
            mb.At(Vector3.Zero);
        });
        if (body.GetNodeOrNull<MeshInstance3D>("Hull") is { } hullMi) hullMi.MaterialOverride = Materials.Rust(body.Scale.X);
        _lightMat = new StandardMaterial3D { AlbedoColor = new Color(0.5f, 0.3f, 0.05f), EmissionEnabled = true, Emission = new Color(1f, 0.7f, 0.2f), EmissionEnergyMultiplier = 2.2f };
        _light = new MeshInstance3D { Name = "Beacon", Mesh = new SphereMesh { Radius = 0.14f, Height = 0.28f, RadialSegments = 8, Rings = 4 }, MaterialOverride = _lightMat, Position = new Vector3(-1.1f, 0.85f, 0) };
        body.AddChild(_light);
    }

    // ---- drifting debris ---------------------------------------------------------------------------

    private void BuildDebris()
    {
        var body = Holder(_radius);
        _spin = body;
        Models.Add(body, "Plates", $"obs.debris.{_seed % 16}", Materials.Rust(), mb =>
        {
            var rng = new Models.Rng(_seed % 16 + 9);
            var cols = new[] { new Color(0.34f, 0.35f, 0.38f), new Color(0.22f, 0.23f, 0.26f), new Color(0.40f, 0.28f, 0.20f) };
            for (int i = 0; i < 6; i++)
            {
                var c = new Vector3(rng.F() - 0.5f, rng.F() - 0.5f, rng.F() - 0.5f) * 1.2f;
                mb.At(c, new Vector3(rng.F() * 6, rng.F() * 6, rng.F() * 6));
                mb.C(cols[i % 3]);
                if (i % 3 == 0) mb.Beam(new Vector3(-0.4f, 0, 0), new Vector3(0.4f, 0.1f, 0), 0.07f);
                else mb.Block(Vector3.Zero, new Vector3(0.5f + rng.F() * 0.3f, 0.05f, 0.35f + rng.F() * 0.3f));
            }
            mb.At(Vector3.Zero);
        });
        if (body.GetNodeOrNull<MeshInstance3D>("Plates") is { } platesMi) platesMi.MaterialOverride = Materials.Rust(body.Scale.X);
    }

    // ---- per frame ----------------------------------------------------------------------------------

    /// <summary>Moves the view to where the simulation has the obstacle and animates its lights.</summary>
    public void Refresh(ObstacleInfo info, float time, float dt)
    {
        Position = new Vector3(info.X, info.Y, info.Z) / Units.CellUnits;
        bool armed = info.FuseTicks > 0;
        switch (_def.Behaviour)
        {
            case ObstacleBehaviour.Mine:
                Visible = !info.Gone;
                if (_lightMat is not null)
                {
                    float rate = armed ? 14f : 2.2f;
                    bool on = MathF.Sin(time * rate + _id) > (armed ? -0.2f : 0.55f);
                    _lightMat.EmissionEnergyMultiplier = on ? (armed ? 6f : 3f) : 0.15f;
                }
                if (_halo is not null) _halo.Visible = armed;
                if (_spin is not null) _spin.Rotate(Vector3.Up, _spinRate * dt);
                break;
            case ObstacleBehaviour.Wreck:
                if (_lightMat is not null)
                    _lightMat.EmissionEnergyMultiplier = info.Gone ? 0f : 0.4f + 2f * MathF.Max(0f, MathF.Sin(time * 1.3f + _id));
                if (_light is not null) _light.Visible = !info.Gone;
                break;
            case ObstacleBehaviour.Cloud:
                _spin?.Rotate(Vector3.Up, _spinRate * dt * 0.4f);
                break;
            case ObstacleBehaviour.Drifter:
                _spin?.Rotate(new Vector3(0.3f, 1f, 0.2f).Normalized(), _spinRate * dt * 4f);
                break;
        }
    }
}

/// <summary>
/// The obstacles of the system the ship is in: creates a view for each, removes them when the ship leaves, and refreshes them every
/// frame (drifting debris moves, armed mines blink faster, salvaged wrecks go dark). Obstacles of other systems are never drawn.
/// </summary>
public sealed partial class ObstacleLayer : Node3D
{
    private readonly Dictionary<int, ObstacleView> _views = new();
    private int _system = -2;

    public ObstacleLayer() { Name = "Obstacles"; }

    public int ViewCount => _views.Count;

    /// <summary>Brings the views in line with the simulation. <paramref name="system"/> is the ship's current system (-1 in deep space).</summary>
    public void Sync(BeltNetwork net, ContentDb content, int system, float time, float dt)
    {
        if (content.World is not { } world || net.Galaxy is null || net.ObstacleCount == 0) { Clear(); return; }
        if (system != _system)
        {
            Clear();
            _system = system;
            if (system >= 0)
                foreach (int i in net.Galaxy.ObstaclesOf(system))
                {
                    var info = net.GetObstacle(i);
                    var def = world.Kinds[info.Kind];
                    var v = new ObstacleView(i, info, def, def.Id);
                    AddChild(v);
                    _views[i] = v;
                }
        }
        foreach (var (i, v) in _views) v.Refresh(net.GetObstacle(i), time, dt);
    }

    private void Clear()
    {
        foreach (var v in _views.Values) v.QueueFree();
        _views.Clear();
        _system = -2;
    }
}
