using Starfoundry.Sim;
using Xunit;

namespace Starfoundry.Sim.Tests;

public class StationTests
{
    private static readonly ContentDb C = TestContent.Core;
    private static readonly int Ingot = C.ItemIndex("core:iron_ingot");
    private static readonly int Smelter = C.MachineIndex("core:smelter");
    private static readonly ModuleRules Rules = C.Modules!;

    /// <summary>A campaign world whose player 0 holds plenty of ingots; the ship sits at the origin.</summary>
    private static SimWorld Campaign(int ingots = 1000)
    {
        var w = new SimWorld(C, GameMode.Campaign);
        w.Network.EnsurePlayer(0);
        if (ingots > 0) w.Network.GiveItems(0, Ingot, ingots);
        return w;
    }

    private static Reject Run(SimWorld w, Command c, int player = 0)
    {
        w.Submit(player, c);
        w.Step();
        return w.LastResults.Single().Reject;
    }

    private static void Ok(SimWorld w, Command c) => Assert.Equal(Reject.None, Run(w, c));

    [Fact]
    public void First_module_is_the_core()
    {
        var w = Campaign();
        Ok(w, new PlaceModule(0, 0, 0));
        Assert.Equal(1, w.Network.ModuleCount(0));
        var m = Assert.Single(w.Network.Modules(0));
        Assert.Equal((0, 0, 0, true), m);
        Ok(w, new PlaceModule(1, 0, 0));
        Assert.Equal(new[] { true, false }, w.Network.Modules(0).Select(x => x.Core).ToArray());
        w.ValidateInvariants();
    }

    [Fact]
    public void Later_module_must_touch_a_face()
    {
        var w = Campaign();
        Ok(w, new PlaceModule(0, 0, 0));
        Assert.Equal(Reject.NotAdjacent, Run(w, new PlaceModule(2, 0, 0)));
        foreach (var (dx, dy, dz) in new[] { (1, 0, 0), (-1, 0, 0), (0, 1, 0), (0, -1, 0), (0, 0, 1), (0, 0, -1) })
        {
            var probe = Campaign();
            Ok(probe, new PlaceModule(0, 0, 0));
            Ok(probe, new PlaceModule(dx, dy, dz));
        }
        Assert.Equal(1, w.Network.ModuleCount(0));
    }

    [Fact]
    public void Diagonal_neighbour_is_not_adjacent()
    {
        var w = Campaign();
        Ok(w, new PlaceModule(0, 0, 0));
        Assert.Equal(Reject.NotAdjacent, Run(w, new PlaceModule(1, 1, 0)));
        Assert.Equal(Reject.NotAdjacent, Run(w, new PlaceModule(1, 0, 1)));
        Assert.Equal(Reject.NotAdjacent, Run(w, new PlaceModule(-1, -1, -1)));
    }

    [Fact]
    public void Module_out_of_build_range_rejected()
    {
        var w = Campaign();
        // Module (3,0,0) has its centre 28 cells from the ship at the origin; the build range is 24.
        Assert.Equal(Reject.OutOfRange, Run(w, new PlaceModule(3, 0, 0)));
        Assert.Equal(0, w.Network.ModuleCount(0));
        Ok(w, new PlaceModule(2, 0, 0)); // centre 20 cells away
        // Range also applies to later modules even when adjacent.
        Ok(w, new PlaceModule(1, 0, 0));
        Assert.Equal(Reject.OutOfRange, Run(w, new PlaceModule(3, 0, 0)));
    }

    [Fact]
    public void Cost_is_taken_and_grows_with_count()
    {
        var w = Campaign();
        int core = Rules.CoreCost.Single(s => s.Item == Ingot).Count;
        int hull = Rules.HullCost.Single(s => s.Item == Ingot).Count;
        Assert.Equal(core, w.Network.NextModuleCost(0).Single().Count);

        Ok(w, new PlaceModule(0, 0, 0));
        Assert.Equal(1000 - core, w.Network.HoldCount(0, Ingot));

        // One module exists: hull cost grown by one step of growth_percent.
        int second = hull * (100 + Rules.GrowthPercent) / 100;
        Assert.Equal(second, w.Network.NextModuleCost(0).Single().Count);
        Ok(w, new PlaceModule(1, 0, 0));
        Assert.Equal(1000 - core - second, w.Network.HoldCount(0, Ingot));

        int third = hull * (100 + 2 * Rules.GrowthPercent) / 100;
        Assert.True(third > second);
        Ok(w, new PlaceModule(0, 1, 0));
        Assert.Equal(1000 - core - second - third, w.Network.HoldCount(0, Ingot));
    }

    [Fact]
    public void Missing_items_rejected_and_hold_unchanged()
    {
        int core = Rules.CoreCost.Single(s => s.Item == Ingot).Count;
        var w = Campaign(core - 1);
        ulong before = w.Checksum();
        Assert.Equal(Reject.NotEnoughItems, Run(w, new PlaceModule(0, 0, 0)));
        Assert.Equal(core - 1, w.Network.HoldCount(0, Ingot));
        Assert.Equal(0, w.Network.ModuleCount(0));
        Assert.NotEqual(0UL, before);

        // Exactly enough works and empties the hold.
        w.Network.GiveItems(0, Ingot, 1);
        Ok(w, new PlaceModule(0, 0, 0));
        Assert.Equal(0, w.Network.HoldCount(0, Ingot));

        // A second module the hold cannot pay for is rejected and changes nothing.
        Assert.Equal(Reject.NotEnoughItems, Run(w, new PlaceModule(1, 0, 0)));
        Assert.Equal(1, w.Network.ModuleCount(0));
        Assert.Equal(0, w.Network.HoldCount(0, Ingot));
    }

    [Fact]
    public void Duplicate_coordinate_rejected()
    {
        var w = Campaign();
        Ok(w, new PlaceModule(0, 0, 0));
        int hold = w.Network.HoldCount(0, Ingot);
        Assert.Equal(Reject.Overlap, Run(w, new PlaceModule(0, 0, 0)));
        Assert.Equal(hold, w.Network.HoldCount(0, Ingot));
        Assert.Equal(1, w.Network.ModuleCount(0));
    }

    [Fact]
    public void Coordinate_beyond_world_limit_rejected()
    {
        var w = Campaign();
        foreach (var c in new PlaceModule[]
        {
            new(int.MaxValue, 0, 0), new(0, int.MinValue, 0), new(0, 0, int.MaxValue),
            new(StationGrid.MaxX + 1, 0, 0), new(StationGrid.MinX - 1, 0, 0),
            new(0, StationGrid.MaxY + 1, 0), new(0, 0, StationGrid.MinZ - 1),
        })
            Assert.Equal(Reject.OutOfBounds, Run(w, c));
        foreach (var c in new RemoveModule[] { new(int.MaxValue, 0, 0), new(0, 0, int.MinValue) })
            Assert.Equal(Reject.OutOfBounds, Run(w, c));
        Assert.Equal(0, w.Network.ModuleCount(0));

        // The last valid module on each axis covers cells inside the world limits.
        Assert.True(StationGrid.IsValidModule(StationGrid.MaxX, StationGrid.MaxY, StationGrid.MaxZ));
        Assert.True((StationGrid.MaxX + 1) * StationGrid.ModuleX - 1 <= SimWorld.MaxCoord);
        Assert.True((StationGrid.MaxZ + 1) * StationGrid.ModuleZ - 1 <= SimWorld.MaxCoord);
        Assert.True(StationGrid.MinX * StationGrid.ModuleX >= -SimWorld.MaxCoord);
        Assert.True((StationGrid.MaxX + 2) * StationGrid.ModuleX - 1 > SimWorld.MaxCoord);
    }

    [Fact]
    public void Remove_refused_with_machine_or_belt_inside()
    {
        var w = new SimWorld(C); // sandbox: free, no range
        Ok(w, new PlaceModule(0, 0, 0));
        Ok(w, new PlaceModule(1, 0, 0));

        // A machine inside module (1,0,0): cells x 8..15.
        Ok(w, new PlaceMachine(9, 2, 1, Smelter, -1));
        Assert.Equal(Reject.ModuleOccupied, Run(w, new RemoveModule(1, 0, 0)));
        Assert.Equal(2, w.Network.ModuleCount(0));
        Assert.True(w.Network.IsMachineAlive(0));
        Ok(w, new RemoveMachine(0));
        Ok(w, new RemoveModule(1, 0, 0));
        Ok(w, new PlaceModule(1, 0, 0));

        // A belt cell inside it, even if the belt starts in the other module.
        Ok(w, new PlaceSegment(6, 0, 0, Dir.PosX, 4, BeltTier.Mk1));
        Assert.Equal(Reject.ModuleOccupied, Run(w, new RemoveModule(1, 0, 0)));
        Assert.Equal(Reject.ModuleOccupied, Run(w, new RemoveModule(0, 0, 0))); // the core also holds a belt cell, and anchors the station
        Assert.Equal(2, w.Network.ModuleCount(0));
        w.ValidateInvariants();
    }

    [Fact]
    public void Remove_refused_when_it_would_split_the_station()
    {
        var w = Campaign();
        Ok(w, new PlaceModule(0, 0, 0));
        Ok(w, new PlaceModule(1, 0, 0));
        Ok(w, new PlaceModule(2, 0, 0));
        Assert.Equal(Reject.ModuleOccupied, Run(w, new RemoveModule(1, 0, 0)));
        Assert.Equal(3, w.Network.ModuleCount(0));
        Ok(w, new RemoveModule(2, 0, 0)); // the end of the chain can go
        Assert.Equal(Reject.OutsideStation, Run(w, new RemoveModule(2, 0, 0))); // no module there any more
        w.ValidateInvariants();
    }

    [Fact]
    public void Remove_refunds_half()
    {
        var w = Campaign();
        int core = Rules.CoreCost.Single(s => s.Item == Ingot).Count;
        Ok(w, new PlaceModule(0, 0, 0));
        Ok(w, new PlaceModule(1, 0, 0));
        int afterTwo = w.Network.HoldCount(0, Ingot);
        int second = Rules.HullCost.Single().Count * (100 + Rules.GrowthPercent) / 100;
        Assert.Equal(1000 - core - second, afterTwo);

        Ok(w, new RemoveModule(1, 0, 0));
        Assert.Equal(afterTwo + second / 2, w.Network.HoldCount(0, Ingot));

        // The core is the last module and refunds half the core cost, rounded down.
        Ok(w, new RemoveModule(0, 0, 0));
        Assert.Equal(afterTwo + second / 2 + core / 2, w.Network.HoldCount(0, Ingot));
        Assert.Equal(0, w.Network.ModuleCount(0));
    }

    [Fact]
    public void CellInStation_matches_module_bounds()
    {
        var w = Campaign();
        Ok(w, new PlaceModule(0, 0, 0));
        Ok(w, new PlaceModule(-1, 0, 0));
        var n = w.Network;
        int X = StationGrid.ModuleX, Y = StationGrid.ModuleY, Z = StationGrid.ModuleZ;

        // Edge cells of module (0,0,0) are inside, the next cell on each side is outside.
        Assert.True(n.CellInStation(0, 0, 0, 0));
        Assert.True(n.CellInStation(0, X - 1, Y - 1, Z - 1));
        Assert.False(n.CellInStation(0, 0, Y, 0));
        Assert.False(n.CellInStation(0, 0, -1, 0));
        Assert.False(n.CellInStation(0, 0, 0, Z));
        Assert.False(n.CellInStation(0, 0, 0, -1));
        Assert.False(n.CellInStation(0, X, 0, 0)); // module (1,0,0) was never placed

        // Negative cells belong to module -1 (floor division, not truncation).
        Assert.True(n.CellInStation(0, -1, 0, 0));
        Assert.True(n.CellInStation(0, -X, Y - 1, Z - 1));
        Assert.False(n.CellInStation(0, -X - 1, 0, 0));

        // Another player has no station.
        Assert.False(n.CellInStation(1, 0, 0, 0));
    }

    [Fact]
    public void Replay_and_save_roundtrip_with_modules()
    {
        var w = new SimWorld(C);
        w.Network.EnsurePlayer(0);
        w.Submit(0, new PlaceModule(0, 0, 0));
        w.Submit(0, new PlaceModule(1, 0, 0));
        w.Step(3);
        w.Submit(0, new PlaceModule(0, 1, 0));
        w.Submit(0, new RemoveModule(1, 0, 0));
        w.Step(3);
        Assert.Equal(2, w.Network.ModuleCount(0));

        foreach (Command c in new Command[] { new PlaceModule(-3, 4, -5), new RemoveModule(7, -8, 9) })
            Assert.Equal(c, CommandCodec.FromBytes(CommandCodec.ToBytes(c)));

        var initial = new SimWorld(C);
        initial.Network.EnsurePlayer(0); // setup outside the log, as SaveGame does
        var replayed = SimWorld.Replay(w.Log, w.Tick, initial);
        Assert.Equal(w.Checksum(), replayed.Checksum());
        Assert.Equal(w.Network.Modules(0).ToArray(), replayed.Network.Modules(0).ToArray());

        var ms = new MemoryStream();
        SaveGame.Write(ms, w, C, GameMode.Sandbox, 0UL, threats: false, player: 0);
        var loaded = SaveGame.Read(new MemoryStream(ms.ToArray()), C, out _);
        Assert.Equal(w.Checksum(), loaded.Checksum());
        Assert.Equal(2, loaded.Network.ModuleCount(0));
        loaded.ValidateInvariants();
    }

    [Fact]
    public void Checksum_changes_with_a_module()
    {
        var a = new SimWorld(C);
        var b = new SimWorld(C);
        Assert.Equal(a.Checksum(), b.Checksum());
        Ok(a, new PlaceModule(0, 0, 0));
        b.Step();
        Assert.NotEqual(a.Checksum(), b.Checksum());
        Ok(b, new PlaceModule(0, 0, 0));
        a.Step(); // same tick, same station
        Assert.Equal(a.Checksum(), b.Checksum());
        Ok(b, new PlaceModule(1, 0, 0));
        a.Step();
        Assert.NotEqual(a.Checksum(), b.Checksum());

        // A clone carries the station.
        var clone = b.Network.Clone();
        Assert.Equal(b.Checksum(), clone.Checksum());
        Assert.Equal(2, clone.ModuleCount(0));
    }

    [Fact]
    public void ValidateInvariants_detects_a_floating_module()
    {
        var w = new SimWorld(C);
        Ok(w, new PlaceModule(0, 0, 0));
        w.ValidateInvariants();
        w.Network.AddModuleUnchecked(0, 5, 5, 5); // no face contact with the core
        Assert.Throws<InvalidOperationException>(() => w.ValidateInvariants());

        var outside = new SimWorld(C);
        outside.Network.AddModuleUnchecked(0, StationGrid.MaxX + 1, 0, 0);
        Assert.Throws<InvalidOperationException>(() => outside.ValidateInvariants());
    }
}
