using Starfoundry.Sim;

namespace Starfoundry.Ui;

/// <summary>The steps of the guided first hour, in the order a new player should meet them.</summary>
public enum Objective
{
    Fly,
    Mine,
    Refine,
    Drill,
    Smelter,
    Depot,
    Assembler,
    Lab,
    Research,
    Scan,
    Defend,
    Expand,

    /// <summary>Station rules only: a station core module exists.</summary>
    Core,

    /// <summary>Station rules only: a cargo port (unloader) is built.</summary>
    CargoPort,

    /// <summary>Structures only: an assembler made a plate (or a depot delivered something), so the factory runs.</summary>
    WatchFactory,

    /// <summary>Structures only: the player owns a station.</summary>
    FoundStation,

    /// <summary>Structures only: a hydrogen cracker has made hydrogen, or the hold holds more than the starting stock.</summary>
    MakeFuel,

    /// <summary>Structures only: the player added modules beyond the starter set.</summary>
    AddModule,

    /// <summary>Structures only: a drone bay module exists in a structure of the player and has a deposit assigned.</summary>
    Automate,

    /// <summary>Structures only: a structure of the player has a fabrication bay (or a station core), where machines and belts can be built (ADR 0031).</summary>
    FabBay,

    /// <summary>Structures only: the ship has docked at a station (ADR 0029).</summary>
    Dock,

    /// <summary>Structures only: a wreck was salvaged for its loot (ADR 0028).</summary>
    Salvage,

    /// <summary>Structures only: a second laser or gun, or a Mk2 hardpoint, is fitted (ADR 0032, the shipyard).</summary>
    Upgrade,
}

public sealed record ObjectiveState(Objective Objective, bool Done, bool StationRules = false)
{
    /// <summary>Steps whose advice changes under the station rules; they have an extra <c>.hint.station</c> key.</summary>
    private static readonly HashSet<Objective> StationHints = new() { Objective.Mine, Objective.Refine, Objective.Smelter, Objective.Depot };

    /// <summary>Localisation key of the title, for example <c>obj.mine</c>.</summary>
    public string TitleKey => "obj." + char.ToLowerInvariant(Objective.ToString()[0]) + Objective.ToString()[1..];

    /// <summary>Localisation key of the hint that says how to do it, for example <c>obj.mine.hint</c>.</summary>
    public string HintKey => TitleKey + (StationRules && StationHints.Contains(Objective) ? ".hint.station" : ".hint");
}

/// <summary>
/// Watches the simulation and ticks off the guided steps. Completion is sticky: once a step is done it stays
/// done even if the player later tears the thing down. Pure logic, so it is tested without the engine.
/// </summary>
public sealed class ObjectiveTracker
{
    private readonly ContentDb _content;
    private readonly bool[] _done = new bool[Enum.GetValues<Objective>().Length];

    private readonly Objective[] _order;

    private static readonly Objective[] OldOrder =
    {
        Objective.Fly, Objective.Mine, Objective.Refine, Objective.Drill, Objective.Smelter, Objective.Depot,
        Objective.Assembler, Objective.Lab, Objective.Research, Objective.Scan, Objective.Defend, Objective.Expand,
    };

    private static readonly Objective[] StationOrder =
    {
        Objective.Fly, Objective.Mine, Objective.Refine, Objective.Core, Objective.CargoPort, Objective.Smelter, Objective.Depot,
        Objective.Assembler, Objective.Lab, Objective.Research, Objective.Scan, Objective.Defend, Objective.Expand,
    };

    private static readonly Objective[] StructuresOrder =
    {
        Objective.Fly, Objective.Mine, Objective.Refine, Objective.FoundStation, Objective.FabBay, Objective.WatchFactory, Objective.Dock, Objective.MakeFuel, Objective.AddModule,
        Objective.Defend, Objective.Research, Objective.Scan, Objective.Salvage, Objective.Automate, Objective.Upgrade, Objective.Expand,
    };

    /// <summary>Modules of the starter ship; a player with more than this has added some.</summary>
    public const int StarterModuleCount = 7; // five ship modules plus the nose laser and the top gun (ADR 0032)

    /// <summary>Hydrogen the starter ship's hold starts with; more than this in the hold means the player made some.</summary>
    public const int StarterHydrogen = 20;

    /// <param name="content">Game content.</param>
    /// <param name="stationRules">True when machines may only be built inside station modules (a different step list).</param>
    /// <param name="structures">True for the structures mode (ships and stations); it wins over <paramref name="stationRules"/>.</param>
    public ObjectiveTracker(ContentDb content, bool stationRules = false, bool structures = false)
    {
        _content = content;
        Structures = structures;
        StationRules = stationRules && !structures;
        _order = structures ? StructuresOrder : stationRules ? StationOrder : OldOrder;
    }

    public bool StationRules { get; }

    public bool Structures { get; }

    public IReadOnlyList<ObjectiveState> States =>
        _order.Select(o => new ObjectiveState(o, _done[(int)o], StationRules)).ToList();

    /// <summary>The first step that is not done yet, or null when everything is.</summary>
    public Objective? Current
    {
        get
        {
            foreach (var o in _order)
                if (!_done[(int)o]) return o;
            return null;
        }
    }

    public bool IsDone(Objective o) => _done[(int)o];

    public int DoneCount => _order.Count(o => _done[(int)o]);

    /// <summary>Re-evaluates every step against the world for one player. Cheap enough to call every frame.</summary>
    public void Update(BeltNetwork net, int player)
    {
        Mark(Objective.Fly, () =>
        {
            for (int d = 0; d < net.DepositCount; d++)
                if (net.IsDepositRevealed(d) && net.DepositRemaining(d) > 0 && net.InMineRange(player, d)) return true;
            return false;
        });
        Mark(Objective.Mine, () => net.MinedByHand >= 5);
        Mark(Objective.Refine, () => Has(net, player, "core:iron_ingot", 3));
        if (Structures)
        {
            Mark(Objective.WatchFactory, () => AnyMachine(net, m => _content.Machines[m.Type].Id == "core:assembler" && (m.OutputItems > 0 || m.Crafting)) || net.DepotDelivered > 0); // a machine made it, not the workbench
            Mark(Objective.FabBay, () => net.Structures().Any(st => st.Owner == player && st.Kind == StructureKind.Station && st.Modules.Values.Any(k => k == ModuleKind.Workshop)) // ADR 0039: automation is built in the station, a bay or its core floor
                || AnyMachine(net, m => m.Structure != 0 && net.StructureExists(m.Structure) && net.GetStructure(m.Structure).Kind == StructureKind.Station && net.GetStructure(m.Structure).Owner == player));
            Mark(Objective.FoundStation, () => net.Structures().Any(st => st.Owner == player && st.Kind == StructureKind.Station));
            Mark(Objective.MakeFuel, () =>
                Has(net, player, "core:hydrogen", StarterHydrogen + 1)
                || AnyMachine(net, m => _content.Machines[m.Type].Id == "core:hydrogen_cracker" && m.OutputItems > 0));
            Mark(Objective.AddModule, () =>
            {
                int total = 0;
                foreach (var st in net.Structures())
                {
                    if (st.Owner != player) continue;
                    total += st.Modules.Count(m => m.Value != ModuleKind.Core); // a station core is not an added module
                    if (st.Kind == StructureKind.Station && st.Modules.Count > 1) return true;
                }
                return total > StarterModuleCount;
            });
            Mark(Objective.Automate, () => net.Structures().Any(st => st.Owner == player && net.DroneBays(st.Id) > 0 && net.AssignedDeposit(st.Id) >= 0));
            Mark(Objective.Dock, () => net.IsDocked(player));
            Mark(Objective.Salvage, () => net.WrecksSalvaged() > 0);
            Mark(Objective.Upgrade, () => ScenarioProgress.OwnModules(net, player, ModuleKind.MiningLaser) >= 2 || ScenarioProgress.OwnModules(net, player, ModuleKind.Gun) >= 2
                || ScenarioProgress.OwnModules(net, player, ModuleKind.MiningLaserMk2) > 0 || ScenarioProgress.OwnModules(net, player, ModuleKind.GunMk2) > 0);
        }
        else if (StationRules)
        {
            Mark(Objective.Core, () => net.ModuleCount(player) > 0);
            // The ore port: it is the one that brings hand-mined ore to a smelter (the cargo port only moves ingots).
            Mark(Objective.CargoPort, () => AnyMachine(net, m => _content.Machines[m.Type].Id == "core:ore_unloader"));
        }
        else
            Mark(Objective.Drill, () => AnyMachine(net, m => _content.Machines[m.Type].Role == MachineRole.Drill && m.Deposit >= 0));
        Mark(Objective.Smelter, () => AnyMachine(net, m => _content.Machines[m.Type].Id == "core:smelter"));
        Mark(Objective.Depot, () => net.DepotDelivered > 0);
        Mark(Objective.Assembler, () => AnyMachine(net, m => _content.Machines[m.Type].Id == "core:assembler" && m.Recipe >= 0));
        Mark(Objective.Lab, () => AnyMachine(net, m => _content.Machines[m.Type].Role == MachineRole.Lab));
        Mark(Objective.Research, () => net.ResearchedCount > 0);
        Mark(Objective.Scan, () => net.ScanCooldown(player) > 0);
        // Structure worlds: the turret stands in a defence module and its structure has enough power to fire.
        Mark(Objective.Defend, () => AnyMachine(net, m => _content.Machines[m.Type].Role == MachineRole.Turret && m.InputItems > 0
            && (!net.StructureRules || (m.Structure != 0 && net.TryGetModuleKindAt(m.Structure, m.X, m.Y, m.Z, out var kind) && kind == ModuleKind.Defence
                && net.PowerStatus(m.Structure).SatisfactionPermille >= PowerRules.TurretMinSatisfaction))));
        Mark(Objective.Expand, () =>
        {
            for (int i = 1; i < net.SystemCount; i++)
                if (net.IsSystemVisited(i)) return true;
            return false;
        });
    }

    private void Mark(Objective o, Func<bool> test)
    {
        if (!_done[(int)o] && test()) _done[(int)o] = true;
    }

    private bool Has(BeltNetwork net, int player, string itemId, int count)
    {
        var item = _content.Items.FirstOrDefault(i => i.Id == itemId);
        return item is not null && net.HoldCount(player, item.Index) >= count;
    }

    private static bool AnyMachine(BeltNetwork net, Func<MachineInfo, bool> test)
    {
        for (int id = 0; id < net.MachineIdLimit; id++)
            if (net.IsMachineAlive(id) && test(net.GetMachineInfo(id))) return true;
        return false;
    }
}
