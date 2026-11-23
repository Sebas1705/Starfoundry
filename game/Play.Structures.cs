using System;
using System.Globalization;
using System.Linq;
using Godot;
using Starfoundry.Sim;

namespace Starfoundry.Game;

/// <summary>
/// <c>--structure-demo</c>: a sandbox world with a ship structure and a station structure built through the sim, for screenshots of
/// the module visuals. Flags: --demo-yaw=deg, --demo-pitch=deg, --demo-dist=cells, --demo-focus=ship|station|both,
/// --demo-thrust (light the engines), --shot=path.png (saved after a short warm-up, then quit).
/// </summary>
public partial class Play
{
    private bool _structureDemo;
    private float _demoYaw = 0.5f, _demoPitch = 0.5f;
    private Vector3 _demoFocus;
    private int _demoShip, _demoStation, _demoFrames;
    private bool _demoThrust;

    private static float DemoArg(string[] args, string key, float def) =>
        args.FirstOrDefault(a => a.StartsWith(key + "=")) is { } v
        && float.TryParse(v[(key.Length + 1)..], NumberStyles.Float, CultureInfo.InvariantCulture, out float f) ? f : def;

    private void BuildStructureDemo(string[] args)
    {
        _structureDemo = true;
        _demoThrust = args.Contains("--demo-thrust");
        _demoYaw = Mathf.DegToRad(DemoArg(args, "--demo-yaw", 30f));
        _demoPitch = Mathf.DegToRad(DemoArg(args, "--demo-pitch", 30f));
        _overviewDistance = DemoArg(args, "--demo-dist", 60f);
        var net = Net;
        const long cu = Units.CellUnits;

        // A ship: cockpit at the front (-Z), then workshop, cargo, reactor and the engine at the back (+Z).
        _demoShip = net.CreateStructure(Player, StructureKind.Ship, 0, 6 * cu, 0);
        bool big = args.Contains("--demo-big");
        ModuleKind[] row = { ModuleKind.Cockpit, ModuleKind.Workshop, ModuleKind.Cargo, ModuleKind.Reactor, ModuleKind.Engine };
        int sx = big ? 1 : 0; // the spine column of the ship
        for (int i = 0; i < row.Length; i++) net.AddStructureModuleUnchecked(_demoShip, sx, 0, i, row[i]);
        if (big)
        {
            // An extended ship: a second deck, flanking hull and cargo modules, gun decks, a drone bay and three engines.
            (int, int, int, ModuleKind)[] extra =
            {
                (0, 0, 1, ModuleKind.Defence), (2, 0, 1, ModuleKind.Defence), (0, 0, 2, ModuleKind.Hull), (2, 0, 2, ModuleKind.Cargo),
                (0, 0, 3, ModuleKind.Reactor), (2, 0, 3, ModuleKind.Hull), (0, 0, 4, ModuleKind.Engine), (2, 0, 4, ModuleKind.Engine),
                (1, 1, 2, ModuleKind.Sensor), (1, 1, 3, ModuleKind.Shield), (1, 1, 1, ModuleKind.DroneBay),
            };
            foreach (var (x, y, z, k) in extra) net.AddStructureModuleUnchecked(_demoShip, x, y, z, k);
        }

        // A station: core, hull, workshop and defence, one hull block stacked on top (a bigger one with --demo-big).
        _demoStation = net.CreateStructure(Player, StructureKind.Station, 20 * cu + (big ? 16 * cu : 0), 0, 0);
        (int, int, int, ModuleKind)[] parts =
        {
            (0, 0, 0, ModuleKind.Hull), (1, 0, 0, ModuleKind.Core), (2, 0, 0, ModuleKind.Workshop),
            (1, 0, 1, ModuleKind.Defence), (0, 1, 0, ModuleKind.Hull), (3, 0, 0, ModuleKind.Hull), (2, 0, 1, ModuleKind.Defence),
        };
        (int, int, int, ModuleKind)[] bigParts =
        {
            (0, 0, 0, ModuleKind.Hull), (1, 0, 0, ModuleKind.Core), (2, 0, 0, ModuleKind.Workshop), (3, 0, 0, ModuleKind.Cargo),
            (4, 0, 0, ModuleKind.Hull), (1, 0, 1, ModuleKind.Reactor), (2, 0, 1, ModuleKind.DroneBay), (3, 0, 1, ModuleKind.Defence),
            (1, 0, -1, ModuleKind.Shield), (2, 0, -1, ModuleKind.Sensor), (3, 0, -1, ModuleKind.Hull), (1, 1, 0, ModuleKind.Hull),
            (2, 1, 0, ModuleKind.Cargo), (0, 1, 0, ModuleKind.Hull), (4, 1, 0, ModuleKind.Hull), (1, 0, 2, ModuleKind.ReactorMk2),
        };
        foreach (var (x, y, z, k) in big ? bigParts : parts) net.AddStructureModuleUnchecked(_demoStation, x, y, z, k);

        if (args.Contains("--demo-machines"))
        {
            // Machines in the ship's workshop: a smelter, an assembler and a belt, all on the floor layer (ADR 0031).
            int x0 = sx * StationGrid.ModuleX, z0 = StationGrid.ModuleZ;
            net.AddMachine(x0 + 1, 0, z0 + 2, _content.MachineIndex("core:smelter"), _content.RecipeIndex("core:smelt_iron"), structure: _demoShip);
            net.AddMachine(x0 + 5, 0, z0 + 4, _content.MachineIndex("core:assembler"), _content.RecipeIndex("core:iron_plate"), structure: _demoShip);
            net.AddSegment(x0 + 2, 0, z0 + 6, 1, 0, 0, 4, BeltTier.Mk1, _demoShip);
        }

        string focus = args.FirstOrDefault(a => a.StartsWith("--demo-focus="))?["--demo-focus=".Length..] ?? "both";
        _demoFocus = focus switch
        {
            "ship" => big ? new Vector3(12f, 8f, 20f) : new Vector3(4f, 8f, 20f),
            "station" => big ? new Vector3(56f, 3f, 8f) : new Vector3(36f, 2f, 4f),
            _ => new Vector3(24f, 2f, 16f),
        };
        // --demo-at=x,y,z puts the camera target anywhere (an interior view: a small --demo-dist and a low --demo-pitch).
        if (args.FirstOrDefault(a => a.StartsWith("--demo-at="))?["--demo-at=".Length..].Split(',') is { Length: 3 } at)
            _demoFocus = new Vector3(float.Parse(at[0], CultureInfo.InvariantCulture), float.Parse(at[1], CultureInfo.InvariantCulture), float.Parse(at[2], CultureInfo.InvariantCulture));
    }

    private void StructureDemoTick()
    {
        _stationView.SyncStructures(Net);
        _stationView.SetEngines(_demoShip, _demoThrust);
        if (_demoFrames > 3) _overviewTarget = _demoFocus; // no fly-in: the screenshot is taken a second after start
        if (++_demoFrames == 75 && _shotPath != null)
        {
            GetViewport().GetTexture().GetImage().SavePng(_shotPath);
            GetTree().Quit();
        }
    }
}
