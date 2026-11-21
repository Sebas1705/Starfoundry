using System.Numerics;
using Starfoundry.Sim;
using Starfoundry.Ui;
using Xunit;

namespace Starfoundry.Sim.Tests;

public class SeatLocatorTests
{
    private static Dictionary<(int, int, int), ModuleKind> Mods(params ((int, int, int) At, ModuleKind Kind)[] m) =>
        m.ToDictionary(x => x.At, x => x.Kind);

    [Fact]
    public void Single_cockpit_at_origin_gives_the_classic_offsets()
    {
        var s = SeatLocator.Locate(Mods(((0, 0, 0), ModuleKind.Cockpit)));
        Assert.True(s.FromCockpit);
        Assert.Equal(new Vector3(4f, 0f, 4.6f), s.Seat);
        Assert.Equal(new Vector3(4f, 2.7f, 6.6f), s.Eye);
        Assert.Equal(new Vector3(4f, 0f, 6.6f), s.Stand);
    }

    [Fact]
    public void Offsets_follow_the_cockpit_module_origin()
    {
        var s = SeatLocator.Locate(Mods(((0, 0, 0), ModuleKind.Cargo), ((2, 1, 3), ModuleKind.Cockpit)));
        var origin = new Vector3(2 * StationGrid.ModuleX, 1 * StationGrid.ModuleY, 3 * StationGrid.ModuleZ);
        Assert.Equal((2, 1, 3), s.Module);
        Assert.Equal(origin + SeatLocator.SeatOffset, s.Seat);
        Assert.Equal(origin + SeatLocator.EyeOffset, s.Eye);
    }

    [Fact]
    public void Picks_the_first_cockpit_by_sorted_coordinate_regardless_of_insertion_order()
    {
        var a = new Dictionary<(int, int, int), ModuleKind> { [(3, 0, 0)] = ModuleKind.Cockpit, [(1, 0, 2)] = ModuleKind.Cockpit, [(1, 0, 1)] = ModuleKind.Cockpit };
        var b = new Dictionary<(int, int, int), ModuleKind> { [(1, 0, 1)] = ModuleKind.Cockpit, [(1, 0, 2)] = ModuleKind.Cockpit, [(3, 0, 0)] = ModuleKind.Cockpit };
        Assert.Equal((1, 0, 1), SeatLocator.Locate(a).Module);
        Assert.Equal((1, 0, 1), SeatLocator.Locate(b).Module);
    }

    [Fact]
    public void Removing_the_first_cockpit_moves_the_seat_to_the_survivor()
    {
        var m = Mods(((0, 0, 0), ModuleKind.Cockpit), ((0, 0, 1), ModuleKind.Cockpit));
        Assert.Equal((0, 0, 0), SeatLocator.Locate(m).Module);
        m.Remove((0, 0, 0));
        var s = SeatLocator.Locate(m);
        Assert.True(s.FromCockpit);
        Assert.Equal((0, 0, 1), s.Module);
        Assert.Equal(StationGrid.ModuleZ + 4.6f, s.Seat.Z, 3);
    }

    [Fact]
    public void Without_a_cockpit_the_structure_origin_is_used()
    {
        var s = SeatLocator.Locate(Mods(((1, 0, 0), ModuleKind.Cargo)));
        Assert.False(s.FromCockpit);
        Assert.Equal((0, 0, 0), s.Module);
        Assert.Equal(SeatLocator.EyeOffset, s.Eye);
        Assert.Null(SeatLocator.FirstCockpit(Mods()));
    }

    [Fact]
    public void The_starter_ship_seat_matches_its_cockpit()
    {
        var w = SimWorld.CreateGalaxy(TestContent.Core, GameMode.Sandbox, 20261121UL, threats: false, stationRules: false, structures: true);
        var mods = w.Network.GetStructure(w.Network.ShipStructure(0)).Modules;
        var s = SeatLocator.Locate(mods);
        Assert.True(s.FromCockpit);
        Assert.Equal(ModuleKind.Cockpit, mods[s.Module]);
    }
}
