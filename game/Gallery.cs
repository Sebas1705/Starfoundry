using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using Godot;
using Starfoundry.Sim;
using Starfoundry.Ui;

namespace Starfoundry.Game;

/// <summary>
/// Review scene for the procedural models. Run with user args after "--":
/// --shot=path.png (save a screenshot after --frames=N frames and quit), --export-models=dir (write .glb files and quit),
/// --focus=ship-tower (three decks with stairwells) |/// --focus=machines|machines2|belts|units|all|framing|modules|ship-demo|station-demo|workbench (workbench = the ship workbench in a hull module, --bench=ingot|plate|ammo|off, --wall=right, --module=Hull|Cargo|Workshop|Cockpit; machines2 = the cracker, greenhouse, bench, physics lab, refinery and ports; belts = belt tiers and every item shape), --state=work|idle|off (machine lamps), --variant=N, --at=x,y,z (look-at point), --angle=deg, --pitch=deg, --dist=units, --fov=deg. Without --angle the camera orbits slowly.
/// </summary>
public partial class Gallery : Node3D
{
    private readonly List<Node3D> _animated = new();
    private readonly List<Node3D> _turrets = new();
    private readonly List<(Node3D Node, BenchKind Kind)> _benches = new();
    private MeshInstance3D _marker = null!;
    private Camera3D _cam = null!;
    private double _time;
    private int _frame, _frames = 20;
    private string? _shot;
    private bool _orbit = true;
    private float _angle = 35f, _pitch = 28f, _dist = -1f, _fov = 40f;
    private Vector3 _focusPoint;
    private int _page;
    private string _state = "work"; // --state=work|idle|off: how the machines of the gallery behave

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

        if (Arg("--focus") == "icons") { SetProcess(false); IconGallery.Run(this, args); return; }

        string? export = Arg("--export-models");
        if (export != null)
        {
            SetProcess(false);
            ExportAll(export);
            GetTree().Quit();
            return;
        }

        _shot = Arg("--shot");
        _state = Arg("--state") ?? "work";
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
            case "machines2": _focusPoint = new Vector3(0, 0, 0f); defDist = 9f; _page = 1; break;
            case "belts": _focusPoint = new Vector3(2f, 0, 0f); defDist = 8f; break;
            case "modules": _focusPoint = new Vector3(32f, 1.5f, 8f); defDist = 75f; break;
            case "ship-demo": _focusPoint = new Vector3(4f, 2f, 20f); defDist = 40f; break;
            case "ship-starter": _focusPoint = new Vector3(4f, 2f, 16f); defDist = 40f; break;
            case "ship-nose": _focusPoint = new Vector3(8f, 2f, 16f); defDist = 45f; break;
            case "ship-tower": _focusPoint = new Vector3(8f, 6f, 24f); defDist = 70f; break;
            case "ship-skins": _focusPoint = new Vector3(88f, 2f, 40f); defDist = 160f; break;
            case "ship-row": _focusPoint = new Vector3(4f, 2f, 40f); defDist = 90f; break;
            case "ship-big": _focusPoint = new Vector3(8f, 4f, 24f); defDist = 90f; break;
            case "station-demo": _focusPoint = new Vector3(12f, 3f, 8f); defDist = 40f; break;
            case "ships": _focusPoint = new Vector3(-3.1f, 0.15f, -4.6f); defDist = 6f; break;
            case "workbench": _focusPoint = new Vector3(1.7f, 0.7f, 1.6f); defDist = 6.5f; break;
            case "materials": _focusPoint = new Vector3(15f, 1f, 2f); defDist = 40f; break;
            case "deposits": _focusPoint = new Vector3(14.4f, 0.6f, 19f); defDist = 66f; break;
            case "units": _focusPoint = new Vector3(2.6f, 0.3f, -6.3f); defDist = 11f; break;
            case "framing": _focusPoint = new Vector3(14f, 1f, 8f); defDist = 40f; break;
            default: _focusPoint = new Vector3(1.2f, 0.2f, -3f); defDist = 18f; break;
        }
        if (_dist < 0) _dist = defDist;
        if (Arg("--at") is { } at && at.Split(',') is { Length: 3 } p) _focusPoint = new Vector3(float.Parse(p[0], CultureInfo.InvariantCulture), float.Parse(p[1], CultureInfo.InvariantCulture), float.Parse(p[2], CultureInfo.InvariantCulture));

        BuildScene();
        if (focus is "modules" or "ship-demo" or "station-demo" or "ship-starter" or "ship-row" or "ship-big" or "ship-nose" or "ship-tower" or "ship-skins") BuildModuleGallery(focus, (int)F("--variant", 0));
        else if (focus == "deposits") BuildDepositGallery();
        else if (focus == "materials") BuildMaterialGallery();
        else if (focus == "belts") BuildBeltGallery();
        else if (focus == "framing") BuildFramingGallery();
        else if (focus == "workbench") BuildWorkbenchGallery(Arg("--bench") ?? "plate", Arg("--wall") == "right", Enum.TryParse<ModuleKind>(Arg("--module"), true, out var mk) ? mk : ModuleKind.Hull);
        else BuildModels();
        if (Arg("--audit") is { } audit) { SetProcess(false); RunAudit(audit, F("--step", 1f)); GetTree().Quit(); }
    }

    private readonly List<(ModuleKind Kind, bool Ship, Vector3 At, int Mask, Node3D Node)> _auditModules = new();

    /// <summary>
    /// --focus=materials: every material of the library on a sphere (back row) and a box (front row), with the tint the game gives it,
    /// in world size so the texel density can be judged: plating at module scale and machine scale, structural and burnt metal, glass,
    /// lights, rock, rust, ore, ice, crystal, belt and item metal.
    /// </summary>
    private void BuildMaterialGallery()
    {
        var clock = System.Diagnostics.Stopwatch.StartNew();
        var ore = BeltModels.ItemMaterial(BeltModels.Shape.Ore);
        (string Name, StandardMaterial3D Mat, float Size, Color Tint)[] list =
        {
            ("hull (module)", Materials.Hull(), 4f, new Color(0.62f, 0.66f, 0.74f)),
            ("hull blue", Materials.Hull(), 4f, new Color(0.24f, 0.48f, 0.94f)),
            ("machine", Materials.Machine(), 1.2f, new Color(0.72f, 0.76f, 0.83f)),
            ("machine orange", Materials.Machine(), 1.2f, new Color(0.97f, 0.42f, 0.07f)),
            ("structural", Materials.Structural(), 2f, new Color(0.46f, 0.50f, 0.58f)),
            ("burnt", Materials.Burnt(), 1.5f, new Color(0.7f, 0.7f, 0.72f)),
            ("glass", Materials.Glass(), 2f, Colors.White),
            ("light", Materials.Light(new Color(1f, 0.92f, 0.65f), 1.6f), 1.2f, Colors.White),
            ("vent", Materials.Light(new Color(1f, 0.45f, 0.12f), 1.8f, true), 1.2f, Colors.White),
            ("rock", Materials.Rock(1f, 3f), 3f, new Color(0.40f, 0.37f, 0.36f)),
            ("rust", Materials.Rust(), 3f, new Color(0.40f, 0.36f, 0.34f)),
            ("ore (iron)", Materials.Ore(0.75f, 0.2f), 1.2f, new Color(0.55f, 0.27f, 0.13f)),
            ("ore (titanium)", Materials.Ore(0.45f, 0.7f), 1.2f, new Color(0.86f, 0.89f, 0.94f)),
            ("ice", Materials.Ice(), 1.4f, new Color(0.70f, 0.88f, 1.00f)),
            ("crystal", Materials.Crystal(0.06f, 0.05f), 1.2f, new Color(0.86f, 0.80f, 1.00f)),
            ("item metal", Materials.ItemMetal(), 1f, new Color(0.68f, 0.80f, 1f)),
            ("item ore", ore, 1f, new Color(0.82f, 0.46f, 0.30f)),
        };
        float x = 0f;
        foreach (var (name, mat, size, tint) in list)
        {
            float cx = x + size / 2f;
            var sph = new Models.Mb();
            sph.C(tint).Sphere(Vector3.Zero, Vector3.One * size / 2f, 12, 24);
            AddChild(new MeshInstance3D { Mesh = sph.ToMesh(mat), Position = new Vector3(cx, size / 2f - 0.46f, -size / 2f - 0.5f) });
            var box = new Models.Mb();
            box.C(tint).Block(Vector3.Zero, new Vector3(size, size * 0.7f, size));
            AddChild(new MeshInstance3D { Mesh = box.ToMesh(mat), Position = new Vector3(cx, size * 0.35f - 0.46f, size / 2f + 0.5f) });
            Label(name, new Vector3(cx, -0.3f, size + 1.6f));
            x += size + 0.7f;
        }
        // a belt run with items on it, and the same plating on a unit-sized machine, for the scale comparison
        for (int i = 0; i < 8; i++)
        {
            var mm = new MultiMesh { TransformFormat = MultiMesh.TransformFormatEnum.Transform3D, UseColors = true, Mesh = BeltModels.Tile(i % 3), InstanceCount = 1 };
            mm.SetInstanceTransform(0, new Transform3D(Basis.Identity, new Vector3(x + i, -0.46f, 0)));
            mm.SetInstanceColor(0, new Color(0.20f, 0.55f, 0.60f));
            AddChild(new MultiMeshInstance3D { Multimesh = mm });
        }
        Label("belt", new Vector3(x + 4f, -0.3f, 1.4f));
        GD.Print($"materials: every texture set built and cached in {clock.ElapsedMilliseconds} ms");
    }

    /// <summary>--focus=belts: three tiers of belt tiles with every item on them, a vertical run and the three end caps.</summary>
    private void BuildBeltGallery()
    {
        Color[] tiers = { new(0.35f, 0.40f, 0.50f), new(0.20f, 0.55f, 0.60f), new(0.85f, 0.50f, 0.20f) };
        string[] ids =
        {
            "core:iron_ore", "core:titanium_ore", "core:nickel_ore", "core:carbon", "core:quartz", "core:ice", "core:iron_ingot", "core:titanium_ingot",
            "core:nickel_ingot", "core:silicon", "core:iron_plate", "core:titanium_plate", "core:nickel_plate", "core:titanium_alloy", "core:circuit",
            "core:fuel_cell", "core:ammo", "core:hydrogen", "core:biomass", "core:science_engineering", "core:science_biology", "core:science_physics",
            "core:carbon_fiber", "core:composite_plate", "core:coolant", "core:resonant_crystal", "core:focus_lens",
        };
        // one-instance MultiMeshes, like the game: the instance colour tints, the library materials supply the texture
        void Put(Mesh mesh, Color c, Vector3 pos, float scale = 1f)
        {
            var mm = new MultiMesh { TransformFormat = MultiMesh.TransformFormatEnum.Transform3D, UseColors = true, Mesh = mesh, InstanceCount = 1 };
            mm.SetInstanceTransform(0, new Transform3D(Basis.Identity.Scaled(Vector3.One * scale), pos));
            mm.SetInstanceColor(0, c);
            AddChild(new MultiMeshInstance3D { Multimesh = mm });
        }
        for (int t = 0; t < 3; t++)
        {
            for (int i = 0; i < 12; i++)
                Put(BeltModels.Tile(t), tiers[t], new Vector3(i, -0.46f, t * 1.3f));
            for (int i = 0; i < 11; i++)
            {
                var (shape, tint) = BeltModels.StyleOf(ids[(t * 7 + i) % ids.Length]);
                Put(BeltModels.Item(shape), tint, new Vector3(i + 0.5f, -0.46f + 0.172f, t * 1.3f), 0.27f);
            }
        }
        // the whole catalogue in a row along the back, at a larger scale so the silhouettes can be judged
        for (int i = 0; i < ids.Length; i++)
        {
            var (shape, tint) = BeltModels.StyleOf(ids[i]);
            Put(BeltModels.Item(shape), tint, new Vector3(i * 0.62f - 1f, -0.46f, -1.6f), 0.5f);
        }
        Color[] caps = { new(1f, 0.78f, 0.2f), new(0.95f, 0.3f, 0.25f), new(0.35f, 0.95f, 0.55f) };
        for (int i = 0; i < 3; i++)
            Put(BeltModels.Hub(), caps[i], new Vector3(11f, -0.46f, i * 1.3f));
        // a vertical run laid out like Play.BuildGeometry does it: a rail beside the item path, caps lying flat across the two ends
        for (int i = 0; i < 4; i++)
        {
            var c = new Vector3(13.5f, 0.0f + i, 0.5f);
            var mm = new MultiMesh { TransformFormat = MultiMesh.TransformFormatEnum.Transform3D, UseColors = true, Mesh = BeltModels.Tile(1), InstanceCount = 1 };
            mm.SetInstanceTransform(0, new Transform3D(Basis.FromEuler(new Vector3(0, 0, Mathf.Pi / 2f)), new Vector3(c.X + 0.285f, c.Y, c.Z)));
            mm.SetInstanceColor(0, tiers[1]);
            AddChild(new MultiMeshInstance3D { Multimesh = mm });
        }
        Put(BeltModels.Hub(), caps[0], new Vector3(13.5f, 3.5f, 0.5f)); // top end: splitter
        Put(BeltModels.Hub(), caps[2], new Vector3(13.5f, -0.5f, 0.5f)); // bottom end: source
        Label("belts Mk1 / Mk2 / Mk3, splitter / sink / source caps, a vertical run", new Vector3(5f, -0.3f, 4.4f));
    }

    /// <summary>--focus=deposits: one row per item, columns for density 25, 100, 200 and a 100-density deposit at 30% left.</summary>
    private void BuildDepositGallery()
    {
        string[] items = { "core:iron_ore", "core:carbon", "core:ice", "core:titanium_ore", "core:nickel_ore", "core:quartz" };
        (int Density, float Left)[] cols = { (25, 1f), (100, 1f), (200, 1f), (100, 0.3f), (100, 0f) };
        for (int r = 0; r < items.Length; r++)
        {
            for (int c = 0; c < cols.Length; c++)
            {
                int initial = 800;
                var v = new DepositView(items[r], cols[c].Density, initial, 3 + r * 5 + c);
                v.Refresh((int)(initial * cols[c].Left));
                v.Position = new Vector3(c * 7.2f, 0.6f, r * 7.2f);
                AddChild(v);
                if (r == 0)
                    Label(cols[c].Left == 0f ? "depleted" : cols[c].Left < 1f ? "30% left" : $"density {cols[c].Density}", v.Position + new Vector3(0, -1.6f, -3.6f));
            }
            Label(items[r]["core:".Length..], new Vector3(-4.6f, 0.6f, r * 7.2f));
        }
    }

    /// <summary>--focus=modules: every module kind as a ship (front row) and as a station (back row); ship-demo and station-demo: small assembled structures.</summary>
    private void BuildModuleGallery(string focus, int variant)
    {
        void Put(ModuleKind kind, bool ship, Vector3 at, string? label = null, int neighbours = 0)
        {
            var m = ModuleModels.Build(kind, ship, variant, neighbours);
            m.Position = at;
            AddChild(m);
            _auditModules.Add((kind, ship, at, neighbours, m));
            _animated.Add(m);
            if (label != null) Label(label, at + new Vector3(4, -0.2f, 9.5f));
        }
        if (focus == "modules")
        {
            var kinds = Enum.GetValues<ModuleKind>();
            for (int i = 0; i < kinds.Length; i++)
            {
                Put(kinds[i], true, new Vector3(i * 9.5f, 0, 0), kinds[i] + " (ship)");
                Put(kinds[i], false, new Vector3(i * 9.5f, 0, 16f), kinds[i] + " (station)");
            }
        }
        else if (focus is "ship-starter" or "ship-row" or "ship-big" or "ship-nose" or "ship-tower" or "ship-skins")
        {
            // Ships built from the real masks (the same HullProfile.MaskOf the game uses): the starter ship with its pods, a row of every body kind of a
            // ship end to end (cockpit, hull, cargo, workshop, both reactors, defence, drone bay, dock, engine) and a big demo ship with wings of cargo.
            var parts = new System.Collections.Generic.Dictionary<(int, int, int), ModuleKind>();
            if (focus == "ship-starter")
            {
                ModuleKind[] spine = { ModuleKind.Cockpit, ModuleKind.Hull, ModuleKind.Cargo, ModuleKind.Reactor, ModuleKind.Engine };
                for (int i = 0; i < spine.Length; i++) parts[(0, 0, i)] = spine[i];
                parts[(0, 0, -1)] = ModuleKind.MiningLaser;
                parts[(0, 1, 0)] = ModuleKind.Gun;
            }
            else if (focus == "ship-nose")
            {
                // a nose cockpit with nothing in front of it, a gun on top, a second cockpit on the flank, then the usual spine with a tail
                ModuleKind[] spine = { ModuleKind.Cockpit, ModuleKind.Hull, ModuleKind.Cargo, ModuleKind.Workshop, ModuleKind.Reactor, ModuleKind.Engine };
                for (int i = 0; i < spine.Length; i++) parts[(0, 0, i)] = spine[i];
                parts[(0, 1, 0)] = ModuleKind.Gun;
                parts[(1, 0, 0)] = ModuleKind.Cockpit;
                parts[(1, 0, 1)] = ModuleKind.Hull;
            }
            else if (focus == "ship-tower")
            {
                // three decks of body kinds with stairwells (Stairwell.Capable): the audit of the ladders, the coamings and the lower decks under the openings
                ModuleKind[] low = { ModuleKind.Cockpit, ModuleKind.Hull, ModuleKind.Cargo, ModuleKind.Workshop, ModuleKind.Reactor, ModuleKind.Engine };
                for (int i = 0; i < low.Length; i++) parts[(0, 0, i)] = low[i];
                ModuleKind[] mid = { ModuleKind.Hull, ModuleKind.Workshop, ModuleKind.Cargo, ModuleKind.Hull };
                for (int i = 0; i < mid.Length; i++) { parts[(0, 1, i + 1)] = mid[i]; parts[(0, 2, i + 1)] = mid[(i + 1) % mid.Length]; }
                parts[(1, 1, 2)] = ModuleKind.Hull; parts[(1, 2, 2)] = ModuleKind.Cargo;
            }
            else if (focus == "ship-skins")
            {
                // twelve separate rows of corridor kinds (the skin follows from the place, so every pair of skins meets at some joint), a cockpit in front of every other one and an engine behind
                ModuleKind[] kinds = { ModuleKind.Hull, ModuleKind.Cargo, ModuleKind.Workshop, ModuleKind.Reactor, ModuleKind.DroneBay, ModuleKind.Hull, ModuleKind.Workshop, ModuleKind.Cargo };
                for (int col = 0; col < 12; col++)
                {
                    int x = col * 2;
                    for (int i = 0; i < kinds.Length; i++) parts[(x, 0, i + 1)] = kinds[(i + col) % kinds.Length];
                    if (col % 2 == 0) parts[(x, 0, 0)] = ModuleKind.Cockpit;
                    parts[(x, 0, kinds.Length + 1)] = ModuleKind.Engine;
                }
            }
            else if (focus == "ship-row")
            {
                ModuleKind[] a = { ModuleKind.Cockpit, ModuleKind.Hull, ModuleKind.Cargo, ModuleKind.Workshop, ModuleKind.Reactor, ModuleKind.ReactorMk2, ModuleKind.Defence, ModuleKind.DroneBay, ModuleKind.Dock, ModuleKind.Engine };
                // a second row in another order, so every kind meets different neighbours; pods hang on the first (a laser in front, a gun and a shield on top, a sensor behind a hull)
                ModuleKind[] b = { ModuleKind.Cockpit, ModuleKind.ReactorMk2, ModuleKind.Reactor, ModuleKind.DroneBay, ModuleKind.Defence, ModuleKind.Workshop, ModuleKind.Cargo, ModuleKind.Dock, ModuleKind.Hull, ModuleKind.Engine };
                for (int i = 0; i < a.Length; i++) { parts[(0, 0, i)] = a[i]; parts[(3, 0, i)] = b[i]; }
                parts[(0, 0, -1)] = ModuleKind.MiningLaserMk2;
                parts[(0, 1, 1)] = ModuleKind.Gun; parts[(0, 1, 2)] = ModuleKind.Shield; parts[(0, 1, 3)] = ModuleKind.Sensor; parts[(3, 0, -1)] = ModuleKind.Gun;
            }
            else
            {
                ModuleKind[] spine = { ModuleKind.Cockpit, ModuleKind.Workshop, ModuleKind.Cargo, ModuleKind.Reactor, ModuleKind.Cargo, ModuleKind.DroneBay, ModuleKind.Engine };
                for (int i = 0; i < spine.Length; i++) { parts[(0, 0, i)] = spine[i]; if (i is >= 1 and <= 5) { parts[(-1, 0, i)] = ModuleKind.Cargo; parts[(1, 0, i)] = i == 3 ? ModuleKind.ReactorMk2 : ModuleKind.Hull; } }
                parts[(0, 0, -1)] = ModuleKind.MiningLaser; parts[(0, 1, 0)] = ModuleKind.Gun; parts[(0, 1, 2)] = ModuleKind.Sensor; parts[(0, 1, 4)] = ModuleKind.Shield; parts[(0, 1, 6)] = ModuleKind.Hull; parts[(-1, 0, 6)] = ModuleKind.Engine; parts[(1, 0, 6)] = ModuleKind.Engine;
            }
            ModuleKind? KindAt(int x, int y, int z) => parts.TryGetValue((x, y, z), out var kind) ? kind : null;
            foreach (var ((x, y, z), k) in parts) Put(k, true, new Vector3(x * 8f, y * 4f, z * 8f), null, ModuleModels.NeighbourMask(KindAt, x, y, z));
        }
        else if (focus == "ship-demo")
        {
            // Front (-Z) first: cockpit, workshop, cargo, reactor, engine at the back.
            ModuleKind[] row = { ModuleKind.Cockpit, ModuleKind.Workshop, ModuleKind.Cargo, ModuleKind.Reactor, ModuleKind.Engine };
            // Neighbours matter: sides that touch another module get doorways, the exposed ends get bulkheads, stacked decks get a stairwell.
            (ModuleKind, int, int, int)[] ship = row.Select((k, i) => (k, 0, 0, i)).Append((ModuleKind.Cargo, 0, 1, 2)).ToArray();
            bool Has(int x, int y, int z) => ship.Any(p => p.Item2 == x && p.Item3 == y && p.Item4 == z);
            foreach (var (k, x, y, z) in ship)
                Put(k, true, new Vector3(x * 8f, y * 4f, z * 8f), null, ModuleModels.NeighbourMask(Has, x, y, z));
        }
        else
        {
            (ModuleKind, int, int, int)[] parts =
            {
                (ModuleKind.Hull, 0, 0, 0), (ModuleKind.Core, 1, 0, 0), (ModuleKind.Workshop, 2, 0, 0),
                (ModuleKind.Defence, 1, 0, 1), (ModuleKind.Hull, 0, 1, 0), (ModuleKind.Hull, 3, 0, 0), (ModuleKind.Defence, 2, 0, 1),
                (ModuleKind.Cargo, 2, 1, 0),
            };
            bool Has(int x, int y, int z) => parts.Any(p => p.Item2 == x && p.Item3 == y && p.Item4 == z);
            foreach (var (k, x, y, z) in parts) Put(k, false, new Vector3(x * 8f, y * 4f, z * 8f), null, ModuleModels.NeighbourMask(Has, x, y, z));
        }
    }

    /// <summary>
    /// --focus=workbench: the ship's workbench in a hull module of a small ship (cockpit in front, cargo behind, so the doorways are open).
    /// --bench=ingot|plate|ammo|off picks the recipe, --state=work|idle sets whether it crafts, --wall=right mirrors it, --at and --angle move the camera.
    /// </summary>
    private void BuildWorkbenchGallery(string recipe, bool right, ModuleKind host)
    {
        (ModuleKind, int)[] row = { (ModuleKind.Cockpit, -1), (host, 0), (ModuleKind.Cargo, 1) };
        bool Has(int x, int y, int z) => y == 0 && x == 0 && z is >= -1 and <= 1;
        foreach (var (kind, z) in row)
        {
            var m = ModuleModels.Build(kind, true, 0, ModuleModels.NeighbourMask(Has, 0, 0, z));
            m.Position = new Vector3(0, 0, z * 8f);
            AddChild(m);
        }
        var bench = Models.Workbench(right);
        if (host == ModuleKind.Cargo) bench.Position = new Vector3(right ? -WorkbenchSpot.CargoInset : WorkbenchSpot.CargoInset, 0f, 0f);
        AddChild(bench);
        _benches.Add((bench, recipe switch { "ingot" => BenchKind.Ingot, "plate" => BenchKind.Plate, "ammo" => BenchKind.Ammo, _ => BenchKind.Off }));
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
        string[] ids = _page == 0
            ? new[] { "core:extractor", "core:drill", "core:smelter", "core:assembler", "core:research_lab", "core:depot", "core:turret", "core:wall", "core:repair_bay", "core:unloader" }
            : new[] { "core:hydrogen_cracker", "core:greenhouse", "core:engineering_bench", "core:physics_lab", "core:micro_refinery", "core:ore_unloader", "core:titanium_ore_unloader", "core:nickel_ore_unloader", "core:ice_unloader", "core:quartz_unloader", "core:carbon_unloader", "core:crystal_unloader" };
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
        foreach (var m in _animated)
        {
            Models.SetPower(m, _state == "off" ? 0.1f : 1f);
            Models.Animate(m, _time, _state == "work");
        }
        foreach (var (b, kind) in _benches) Models.AnimateBench(b, _time, kind, _state == "work" && kind != BenchKind.Off);
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
