using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using Godot;

namespace Starfoundry.Game;

/// <summary>
/// Review scene for the procedural models. Run with user args after "--":
/// --shot=path.png (save a screenshot after --frames=N frames and quit), --export-models=dir (write .glb files and quit),
/// --focus=machines|units|all, --angle=deg, --pitch=deg, --dist=units, --fov=deg. Without --angle the camera orbits slowly.
/// </summary>
public partial class Gallery : Node3D
{
    private readonly List<Node3D> _animated = new();
    private readonly List<Node3D> _turrets = new();
    private MeshInstance3D _marker = null!;
    private Camera3D _cam = null!;
    private double _time;
    private int _frame, _frames = 20;
    private string? _shot;
    private bool _orbit = true;
    private float _angle = 35f, _pitch = 28f, _dist = -1f, _fov = 40f;
    private Vector3 _focusPoint;

    private static readonly (string Name, Func<Node3D> Make)[] Exports =
    {
        ("extractor", () => Models.Machine("core:extractor")),
        ("drill", () => Models.Machine("core:drill")),
        ("smelter", () => Models.Machine("core:smelter")),
        ("assembler", () => Models.Machine("core:assembler")),
        ("research_lab", () => Models.Machine("core:research_lab")),
        ("depot", () => Models.Machine("core:depot")),
        ("turret", () => Models.Machine("core:turret")),
        ("wall", () => Models.Machine("core:wall")),
        ("repair_bay", () => Models.Machine("core:repair_bay")),
        ("unloader", () => Models.Machine("core:unloader")),
        ("ship", Models.Ship),
        ("enemy_scout", () => Models.Enemy(0)),
        ("enemy_striker", () => Models.Enemy(1)),
        ("enemy_brute", () => Models.Enemy(2)),
        ("deposit", () => Models.Deposit(new Color(0.4f, 0.7f, 1f), 3)),
    };

    public override void _Ready()
    {
        var args = OS.GetCmdlineUserArgs();
        string? Arg(string key) => args.FirstOrDefault(a => a.StartsWith(key + "="))?[(key.Length + 1)..];
        float F(string key, float def) => float.TryParse(Arg(key), NumberStyles.Float, CultureInfo.InvariantCulture, out float v) ? v : def;

        string? export = Arg("--export-models");
        if (export != null)
        {
            SetProcess(false);
            ExportAll(export);
            GetTree().Quit();
            return;
        }

        _shot = Arg("--shot");
        _frames = (int)F("--frames", 20);
        if (Arg("--angle") != null) { _orbit = false; _angle = F("--angle", 35f); }
        _pitch = F("--pitch", 28f);
        _dist = F("--dist", -1f);
        _fov = F("--fov", 40f);
        string focus = Arg("--focus") ?? "all";
        float defDist;
        switch (focus)
        {
            case "machines": _focusPoint = new Vector3(0, 0, 0f); defDist = 9f; break;
            case "ships": _focusPoint = new Vector3(-3.1f, 0.15f, -4.6f); defDist = 6f; break;
            case "units": _focusPoint = new Vector3(2.6f, 0.3f, -6.3f); defDist = 11f; break;
            default: _focusPoint = new Vector3(1.2f, 0.2f, -3f); defDist = 18f; break;
        }
        if (_dist < 0) _dist = defDist;

        BuildScene();
        BuildModels();
    }

    private void BuildScene()
    {
        AddChild(new WorldEnvironment
        {
            Environment = new Godot.Environment
            {
                BackgroundMode = Godot.Environment.BGMode.Color,
                BackgroundColor = new Color(0.02f, 0.03f, 0.06f),
                AmbientLightSource = Godot.Environment.AmbientSource.Color,
                AmbientLightColor = new Color(0.50f, 0.56f, 0.72f),
                AmbientLightEnergy = 0.4f,
                TonemapMode = Godot.Environment.ToneMapper.Aces,
                GlowEnabled = true,
                GlowIntensity = 0.35f,
                GlowBloom = 0.05f,
            },
        });
        var sun = new DirectionalLight3D { LightEnergy = 1.0f, ShadowEnabled = true };
        AddChild(sun);
        sun.RotationDegrees = new Vector3(-52, -38, 0);
        var fill = new DirectionalLight3D { LightEnergy = 0.4f, LightColor = new Color(0.6f, 0.7f, 1f) };
        AddChild(fill);
        fill.RotationDegrees = new Vector3(-28, 145, 0);

        AddChild(new MeshInstance3D
        {
            Mesh = new PlaneMesh { Size = new Vector2(80, 80) },
            Position = new Vector3(0, -0.47f, 0),
            MaterialOverride = new StandardMaterial3D { AlbedoColor = new Color(0.07f, 0.08f, 0.11f), Roughness = 0.9f, Metallic = 0.2f },
        });

        _cam = new Camera3D { Far = 200f, Fov = _fov };
        AddChild(_cam);
        PlaceCamera();

        _marker = new MeshInstance3D
        {
            Mesh = new SphereMesh { Radius = 0.08f, Height = 0.16f, RadialSegments = 8, Rings = 4 },
            MaterialOverride = new StandardMaterial3D { AlbedoColor = Colors.Red, EmissionEnabled = true, Emission = Colors.Red, ShadingMode = BaseMaterial3D.ShadingModeEnum.Unshaded },
        };
        AddChild(_marker);
    }

    private void BuildModels()
    {
        string[] ids = { "core:extractor", "core:drill", "core:smelter", "core:assembler", "core:research_lab", "core:depot", "core:turret", "core:wall", "core:repair_bay", "core:unloader" };
        var tile = new StandardMaterial3D { AlbedoColor = new Color(0.12f, 0.14f, 0.19f), Roughness = 0.9f };
        for (int i = 0; i < ids.Length; i++)
        {
            float x = (i - 3) * 1.45f;
            for (int row = 0; row < 2; row++)
            {
                float z = row == 0 ? -1.0f : 1.0f;
                var m = Models.Machine(ids[i]);
                m.Position = new Vector3(x, 0, z);
                AddChild(m);
                if (row == 1) Models.SetDamage(m, 0.7f);
                _animated.Add(m);
                if (ids[i] == "core:turret") _turrets.Add(m);
                AddChild(new MeshInstance3D { Mesh = new BoxMesh { Size = new Vector3(0.98f, 0.02f, 0.98f) }, MaterialOverride = tile, Position = new Vector3(x, -0.46f, z) });
                Label(ids[i]["core:".Length..] + (row == 1 ? " (damaged)" : ""), new Vector3(x, -0.3f, z + 0.62f));
            }
        }

        // free-flying units and deposits, second block towards the camera
        var units = new (string Name, Func<Node3D> Make, float X, float Y, bool Both)[]
        {
            ("ship", Models.Ship, -1.6f, 0.15f, true),
            ("scout", () => Models.Enemy(0), 0f, 0.15f, true),
            ("striker", () => Models.Enemy(1), 1.5f, 0.15f, true),
            ("brute", () => Models.Enemy(2), 3.4f, 0.15f, true),
            ("deposit", () => Models.Deposit(new Color(0.4f, 0.7f, 1f), 3), 6.2f, 0.7f, true),
            ("deposit B", () => Models.Deposit(new Color(1f, 0.6f, 0.2f), 11), 10f, 0.7f, false),
            ("deposit C", () => Models.Deposit(new Color(0.5f, 1f, 0.5f), 5), 13.8f, 0.7f, false),
        };
        foreach (var u in units)
        {
            for (int row = 0; row < (u.Both ? 2 : 1); row++)
            {
                float z = row == 0 ? -4.6f : -8.0f;
                var m = u.Make();
                m.Position = new Vector3(u.X - 3f, u.Y, z);
                AddChild(m);
                if (row == 1) Models.SetDamage(m, 0.7f);
                _animated.Add(m);
                Label(u.Name + (row == 1 ? " (damaged)" : ""), new Vector3(u.X - 3f, -0.3f, z + (u.Name.StartsWith("deposit") ? 1.7f : 0.7f)));
            }
        }
    }

    private void Label(string text, Vector3 pos) => AddChild(new Label3D
    {
        Text = text,
        Position = pos,
        PixelSize = 0.003f,
        FontSize = 56,
        OutlineSize = 12,
        Billboard = BaseMaterial3D.BillboardModeEnum.Enabled,
        Modulate = new Color(0.85f, 0.9f, 1f),
    });

    private void PlaceCamera()
    {
        float yaw = Mathf.DegToRad(_angle + (_orbit ? (float)_time * 8f : 0f)), pitch = Mathf.DegToRad(_pitch);
        var dir = new Vector3(MathF.Cos(pitch) * MathF.Sin(yaw), MathF.Sin(pitch), MathF.Cos(pitch) * MathF.Cos(yaw));
        _cam.Position = _focusPoint + dir * _dist;
        _cam.LookAt(_focusPoint, Vector3.Up);
    }

    public override void _Process(double delta)
    {
        _time += delta;
        _frame++;
        foreach (var m in _animated) Models.Animate(m, _time, true);
        var target = new Vector3(MathF.Cos((float)_time * 0.8f) * 4.5f, 1.4f + MathF.Sin((float)_time * 0.6f) * 0.8f, MathF.Sin((float)_time * 0.8f) * 3f + 1f);
        _marker.Position = target;
        foreach (var t in _turrets) Models.Aim(t, target);
        PlaceCamera();
        if (_shot != null && _frame == _frames)
        {
            string path = _shot;
            var err = GetViewport().GetTexture().GetImage().SavePng(path);
            GD.Print($"screenshot {path}: {err}");
            GetTree().Quit();
        }
    }

    private static int CountTris(Node n)
    {
        int sum = n is MeshInstance3D { Mesh: not null } mi ? mi.Mesh.GetFaces().Length / 3 : 0;
        foreach (var c in n.GetChildren()) sum += CountTris(c);
        return sum;
    }

    private static void ExportAll(string dir)
    {
        if (dir.StartsWith("res://", StringComparison.Ordinal)) dir = ProjectSettings.GlobalizePath(dir);
        DirAccess.MakeDirRecursiveAbsolute(dir);
        foreach (var (name, make) in Exports)
        {
            var node = make();
            node.Name = name;
            var doc = new GltfDocument();
            var state = new GltfState();
            var err = doc.AppendFromScene(node, state);
            string file = Path.Combine(dir, name + ".glb");
            if (err == Error.Ok) err = doc.WriteToFilesystem(state, file);
            GD.Print($"export {name}: {err} tris={CountTris(node)}" + (File.Exists(file) ? $" ({new FileInfo(file).Length} bytes)" : ""));
            node.Free();
        }
    }
}
