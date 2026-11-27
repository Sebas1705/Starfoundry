using Starfoundry.Sim;
using Xunit;

namespace Starfoundry.Sim.Tests;

/// <summary>The starter ship that structure worlds start in, and founding stations.</summary>
public class StarterShipTests
{
    private static readonly ContentDb C = TestContent.Core;
    private static readonly int Ore = C.ItemIndex("core:iron_ore");
    private static readonly int Ingot = C.ItemIndex("core:iron_ingot");
    private static readonly int Plate = C.ItemIndex("core:iron_plate");
    private const ulong Seed = 20261121UL;
    private const int Cell = Units.CellUnits;

    private static SimWorld World(GameMode mode = GameMode.Sandbox, bool structures = true) =>
        SimWorld.CreateGalaxy(C, mode, Seed, threats: false, stationRules: false, structures: structures);

    private static CommandResult Run(SimWorld w, Command c)
    {
        w.Submit(0, c);
        w.Step();
        return w.LastResults.Single();
    }

    private static int HomeIron(SimWorld w) =>
        Enumerable.Range(0, w.Network.DepositCount).First(d => w.Network.DepositItem(d) == Ore && w.Network.IsDepositRevealed(d));

    /// <summary>Puts the ship (and pilot) on top of a deposit so hand mining is in range.</summary>
    private static void ParkAt(SimWorld w, int deposit)
    {
        var (x, y, z) = w.Network.DepositPosition(deposit);
        w.Network.SetStructureOrigin(w.Network.ShipStructure(0), x * (long)Cell, y * (long)Cell, z * (long)Cell);
    }

    [Fact]
    public void Starter_ship_has_the_listed_modules_and_machines()
    {
        var w = World();
        var n = w.Network;
        int ship = n.ShipStructure(0);
        Assert.True(ship > 0);
        var s = n.GetStructure(ship);
        Assert.Equal(StructureKind.Ship, s.Kind);
        Assert.Equal(0, s.Owner);
        Assert.Equal((0L, 0L, 0L), (s.OriginX, s.OriginY, s.OriginZ));
        Assert.Equal(ObjectiveStarterModules, s.Modules.Count);
        Assert.Equal(ModuleKind.Cockpit, s.Modules[(0, 0, 0)]);
        Assert.Equal(ModuleKind.Workshop, s.Modules[(0, 0, 1)]);
        Assert.Equal(ModuleKind.Cargo, s.Modules[(0, 0, 2)]);
        Assert.Equal(ModuleKind.Reactor, s.Modules[(0, 0, 3)]);
        Assert.Equal(ModuleKind.Engine, s.Modules[(0, 0, 4)]);
        Assert.Equal(ModuleKind.MiningLaser, s.Modules[(0, 0, -1)]); // the nose laser: a ship mines with its laser modules (ADR 0032)
        Assert.Equal(ModuleKind.Gun, s.Modules[(0, 1, 0)]);
        Assert.Equal(100, n.HoldCapacity(0));
        Assert.Single(n.Structures());

        Assert.Equal(4, n.MachineCount);
        Assert.Equal(4, n.SegmentCount);
        var types = Enumerable.Range(0, n.MachineIdLimit).Where(n.IsMachineAlive).Select(n.GetMachineInfo).ToList();
        Assert.All(types, m => Assert.Equal(ship, m.Structure));
        Assert.Equal(new[] { "core:ore_unloader", "core:smelter", "core:assembler", "core:depot" },
            types.Select(m => C.Machines[m.Type].Id).ToArray());
        Assert.Equal(C.RecipeIndex("core:smelt_iron"), types[1].Recipe);
        Assert.Equal(C.RecipeIndex("core:iron_plate"), types[2].Recipe);
        Assert.All(Enumerable.Range(0, n.SegmentIdLimit).Where(n.IsAlive).Select(n.GetSegmentInfo), b => Assert.Equal(ship, b.Structure));
        w.ValidateInvariants();
    }

    private const int ObjectiveStarterModules = 7; // five ship modules plus the nose mining laser and the top gun (ADR 0032); the dock is a collar on the cockpit flank, not a module (ADR 0047)

    [Fact]
    public void Starter_ship_is_only_built_with_structure_rules()
    {
        var w = World(structures: false);
        Assert.Equal(-1, w.Network.ShipStructure(0));
        Assert.Equal(0, w.Network.MachineCount);
        Assert.Empty(w.Network.Structures());
    }

    [Fact]
    public void Starter_ship_turns_hand_mined_ore_into_ingots_and_plates_by_itself()
    {
        var w = World(GameMode.Campaign);
        ParkAt(w, HomeIron(w));
        Assert.True(w.Network.InMineRange(0, HomeIron(w)));
        Assert.True(Run(w, new StartMining(HomeIron(w))).Ok);
        int firstIngot = -1, firstPlate = -1;
        for (int t = 1; t <= 3000; t++)
        {
            w.Step();
            if (firstIngot < 0 && w.Network.HoldCount(0, Ingot) > 0) firstIngot = t;
            if (firstPlate < 0 && w.Network.HoldCount(0, Plate) > 0) firstPlate = t;
        }
        Assert.True(firstIngot > 0, "no ingot reached the hold");
        Assert.True(firstPlate > firstIngot, "no plate reached the hold");
        Assert.True(w.Network.HoldCount(0, Ingot) + w.Network.HoldCount(0, Plate) > 5);
        Assert.True(firstPlate < 1200, $"first plate took {firstPlate} ticks");
        w.ValidateInvariants();
    }

    [Fact]
    public void Starter_ship_can_fly()
    {
        var w = World();
        Assert.True(w.Network.CanFly(0));
        var before = w.Network.GetPlayerInfo(0);
        Assert.True(Run(w, new SetThrust(PlayerRules.ThrustScale, 0, 0)).Ok);
        w.Step(100);
        var after = w.Network.GetPlayerInfo(0);
        Assert.True(after.X > before.X + 1000);
        var s = w.Network.GetStructure(w.Network.ShipStructure(0));
        Assert.Equal(after.X, (int)s.OriginX);
        w.ValidateInvariants();
    }

    private static int Stations(SimWorld w) => w.Network.Structures().Count(s => s.Kind == StructureKind.Station);

    [Fact]
    public void Found_station_costs_items_and_creates_a_core()
    {
        var w = World(GameMode.Campaign);
        var coreCost = C.Structures!.ModuleCost(ModuleKind.Core);
        int cost = coreCost.Single(c => c.Item == Ingot).Count;
        int plates = coreCost.Single(c => c.Item == Plate).Count;
        Assert.Equal(Reject.NotEnoughItems, Run(w, new FoundStation(12, 0, 0)).Reject);
        Assert.Equal(0, Stations(w));

        w.Network.GiveItems(0, Ingot, cost + 4);
        w.Network.GiveItems(0, Plate, plates);
        var r = Run(w, new FoundStation(12, 0, 0));
        Assert.True(r.Ok, r.Reject.ToString());
        Assert.Equal(4, w.Network.HoldCount(0, Ingot));
        var st = w.Network.Structures().Single(s => s.Kind == StructureKind.Station);
        Assert.Equal(0, st.Owner);
        Assert.Equal((12L * Cell, 0L, 0L), (st.OriginX, st.OriginY, st.OriginZ));
        // ADR 0047: a station is founded with its core and a docking collar on it, at no extra cost.
        Assert.Equal(new[] { ModuleKind.Core }, st.Modules.Values.ToArray());
        Assert.Equal(ModuleKind.Core, st.Modules[(0, 0, 0)]);
        Assert.Equal(new[] { new DockPieceSpec(0, 0, 0, Dir.NegZ) }, w.Network.DockPieces(st.Id).ToArray()); // ADR 0047: the collar comes with the core, on its -Z face

        // A sandbox founding is free.
        var sb = World();
        Assert.True(Run(sb, new FoundStation(12, 0, 0)).Ok);
        Assert.Equal(1, Stations(sb));
        sb.ValidateInvariants();
        w.ValidateInvariants();
    }

    [Fact]
    public void Found_station_rejected_too_far_or_overlapping()
    {
        var w = World();
        // Beyond build range (24 cells) of the ship at the origin.
        Assert.Equal(Reject.OutOfRange, Run(w, new FoundStation(PlayerRules.BuildRangeCells + 5, 0, 0)).Reject);
        // On the ship's own volume (workshop is module z=1: cells z 8..15).
        Assert.Equal(Reject.Overlap, Run(w, new FoundStation(2, 1, 10)).Reject);
        Assert.Equal(Reject.Overlap, Run(w, new FoundStation(-7, 0, 0)).Reject);
        Assert.Equal(0, Stations(w));

        Assert.True(Run(w, new FoundStation(10, 0, 0)).Ok);
        // Overlapping the new station, and touching it face to face (allowed).
        Assert.Equal(Reject.Overlap, Run(w, new FoundStation(14, 0, 3)).Reject);
        Assert.True(Run(w, new FoundStation(10 + StationGrid.ModuleX, 0, 0)).Ok);
        Assert.Equal(2, Stations(w));
        w.ValidateInvariants();
    }

    [Fact]
    public void Found_station_requires_structure_rules()
    {
        var w = World(structures: false);
        Assert.Equal(Reject.NotAllowedHere, Run(w, new FoundStation(12, 0, 0)).Reject);
        Assert.Empty(w.Network.Structures());
    }

    [Fact]
    public void Template_is_part_of_the_world_checksum_deterministically()
    {
        var a = World();
        var b = World();
        Assert.Equal(a.Checksum(), b.Checksum());
        a.Step(200);
        b.Step(200);
        Assert.Equal(a.Checksum(), b.Checksum());
        Assert.NotEqual(World(structures: false).Checksum(), World().Checksum());
    }

    [Fact]
    public void Save_roundtrip_with_starter_ship_flight_and_station()
    {
        var p = World(); // sandbox: founding is free, so the log needs no ingots
        int dep = HomeIron(p);
        var (x, _, z) = p.Network.DepositPosition(dep);
        // Everything goes through commands: fly, mine by hand, let the factory make ingots, fly again and found a station.
        ParkViaFlight(p, dep, x, p.Network.DepositPosition(dep).Y, z);
        p.Submit(0, new StartMining(dep));
        p.Step(1500);
        p.Submit(0, new StopMining());
        p.Step(300);
        Assert.True(p.Network.HoldCount(0, Ingot) + p.Network.HoldCount(0, Plate) > 0, "the factory made nothing");
        p.Submit(0, new SetThrust(PlayerRules.ThrustScale, 0, 0));
        p.Step(50);
        p.Submit(0, new SetThrust(0, 0, 0));
        p.Submit(0, new FoundStation(p.Network.GetPlayerInfo(0).X / Cell + 14, p.Network.GetPlayerInfo(0).Y / Cell, p.Network.GetPlayerInfo(0).Z / Cell));
        p.Step(200);
        Assert.Equal(1, Stations(p));

        var bytes = new MemoryStream();
        SaveGame.Write(bytes, p, C, GameMode.Sandbox, Seed, threats: false, player: 0, structures: true);
        var loaded = SaveGame.Read(new MemoryStream(bytes.ToArray()), C, out var info);
        Assert.True(info.Structures);
        Assert.Equal(p.Checksum(), loaded.Checksum());
        Assert.Equal(p.Tick, loaded.Tick);
        Assert.Equal(1, Stations(loaded));
        Assert.Equal(p.Network.ShipStructure(0), loaded.Network.ShipStructure(0));
        loaded.ValidateInvariants();
        p.Step(300);
        loaded.Step(300);
        Assert.Equal(p.Checksum(), loaded.Checksum());
    }

    /// <summary>Flies the ship with commands to within mining range of a deposit, one axis after the other.</summary>
    private static void ParkViaFlight(SimWorld w, int deposit, int dx, int dy, int dz)
    {
        for (int i = 0; i < 4000 && !w.Network.InMineRange(0, deposit); i++)
        {
            var p = w.Network.GetPlayerInfo(0);
            int tx = Math.Abs(dx * Cell - p.X) > 100 ? Math.Sign(dx * Cell - p.X) : 0;
            int tz = Math.Abs(dz * Cell - p.Z) > 100 ? Math.Sign(dz * Cell - p.Z) : 0;
            int ty = Math.Abs(dy * Cell - p.Y) > 100 ? Math.Sign(dy * Cell - p.Y) : 0;
            w.Submit(0, new SetMovement(tx, ty, tz));
            w.Step();
        }
        w.Submit(0, new SetMovement(0, 0, 0));
        w.Step();
        Assert.True(w.Network.InMineRange(0, deposit), "the ship did not reach the deposit");
    }
}
