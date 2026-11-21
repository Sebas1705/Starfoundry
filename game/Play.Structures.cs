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
        ModuleKind[] row = { ModuleKind.Cockpit, ModuleKind.Workshop, ModuleKind.Cargo, ModuleKind.Reactor, ModuleKind.Engine };
        for (int i = 0; i < row.Length; i++) net.AddStructureModuleUnchecked(_demoShip, 0, 0, i, row[i]);

        // A station: core, hull, workshop and defence, one hull block stacked on top.
        _demoStation = net.CreateStructure(Player, StructureKind.Station, 20 * cu, 0, 0);
        (int, int, int, ModuleKind)[] parts =
        {
            (0, 0, 0, ModuleKind.Hull), (1, 0, 0, ModuleKind.Core), (2, 0, 0, ModuleKind.Workshop),
            (1, 0, 1, ModuleKind.Defence), (0, 1, 0, ModuleKind.Hull), (3, 0, 0, ModuleKind.Hull), (2, 0, 1, ModuleKind.Defence),
        };
        foreach (var (x, y, z, k) in parts) net.AddStructureModuleUnchecked(_demoStation, x, y, z, k);

        string focus = args.FirstOrDefault(a => a.StartsWith("--demo-focus="))?["--demo-focus=".Length..] ?? "both";
        _demoFocus = focus switch
        {
            "ship" => new Vector3(10f, 2f, 20f),
            "station" => new Vector3(36f, 2f, 8f),
            _ => new Vector3(24f, 2f, 16f),
        };
    }

    private void StructureDemoTick()
    {
        _stationView.SyncStructures(Net);
        _stationView.SetEngines(_demoShip, _demoThrust);
        if (++_demoFrames == 75 && _shotPath != null)
        {
            GetViewport().GetTexture().GetImage().SavePng(_shotPath);
            GetTree().Quit();
        }
    }
}
