using Starfoundry.Sim;
using Xunit;

namespace Starfoundry.Sim.Tests;

/// <summary>The ore port (core:ore_unloader) is the cargo port for ore: it is what lets hand-mined ore reach a smelter.</summary>
public class StationOrePortTests
{
    private static readonly ContentDb C = TestContent.Core;
    private static readonly ulong Seed = 20261121UL;
    private static readonly int OrePort = C.MachineIndex("core:ore_unloader");
    private static readonly int CargoPort = C.MachineIndex("core:unloader");
    private static readonly int Smelter = C.MachineIndex("core:smelter");
    private static readonly int Ore = C.ItemIndex("core:iron_ore");
    private static readonly int Ingot = C.ItemIndex("core:iron_ingot");

    private static SimWorld World(bool rules, GameMode mode)
    {
        var w = SimWorld.CreateGalaxy(C, mode, Seed, threats: false, stationRules: rules);
        w.Network.EnsurePlayer(0);
        w.Network.GiveItems(0, Ingot, 5000);
        return w;
    }

    private static int Ok(SimWorld w, Command c)
    {
        w.Submit(0, c);
        w.Step();
        var r = w.LastResults.Single();
        Assert.True(r.Ok, $"{c} was rejected: {r.Reject}");
        return r.CreatedMachines.Count > 0 ? r.CreatedMachines[0] : r.Created.Count > 0 ? r.Created[0] : -1;
    }

    /// <summary>Core module, ore port, belt, smelter, belt to a sink, all inside module (0,0,0). Returns (port, sink belt).</summary>
    private static (int Port, int Out) Line(SimWorld w)
    {
        Ok(w, new PlaceModule(0, 0, 0));
        int port = Ok(w, new PlaceMachine(0, 1, 1, OrePort, -1));
        int b1 = Ok(w, new PlaceSegment(1, 1, 1, Dir.PosX, 3, BeltTier.Mk1));
        int smelter = Ok(w, new PlaceMachine(4, 1, 1, Smelter, C.RecipeIndex("core:smelt_iron")));
        int b2 = Ok(w, new PlaceSegment(5, 1, 1, Dir.PosX, 3, BeltTier.Mk1));
        Ok(w, new ConnectFromMachine(port, b1));
        Ok(w, new ConnectToMachine(b1, smelter));
        Ok(w, new ConnectFromMachine(smelter, b2));
        Ok(w, new SetSink(b2));
        return (port, b2);
    }

    [Fact]
    public void Ore_port_moves_ore_from_hold_to_belt()
    {
        var w = World(rules: true, GameMode.Campaign);
        var (_, outBelt) = Line(w);
        w.Network.GiveItems(0, Ore, 20);
        w.Step(30 * 120);
        w.ValidateInvariants();
        Assert.True(w.Network.PassedBy(outBelt) > 0, "no ingot came out of the smelter");
        Assert.Equal(0, w.Network.HoldCount(0, Ore)); // the port took the ore out of the hold
        Assert.Equal(20, w.Network.PassedBy(outBelt)); // one ingot per ore
    }

    [Fact]
    public void Ore_port_has_no_reserve()
    {
        var def = C.Machines[OrePort].Unloader!;
        Assert.Equal(Ore, def.Item);
        Assert.Equal(0, def.Reserve);
        Assert.Equal(C.Machines[CargoPort].Unloader!.IntervalTicks, def.IntervalTicks);
        Assert.Equal(MachineRole.Unloader, C.Machines[OrePort].Role);
        // A single ore is enough to move.
        var w = World(rules: true, GameMode.Campaign);
        var (_, outBelt) = Line(w);
        w.Network.GiveItems(0, Ore, 1);
        w.Step(30 * 60);
        Assert.Equal(0, w.Network.HoldCount(0, Ore));
        Assert.Equal(1, w.Network.PassedBy(outBelt));
    }

    [Fact]
    public void Ore_port_buildable_without_research_with_rules_on()
    {
        var w = World(rules: true, GameMode.Campaign);
        Assert.False(w.Network.IsMachineLocked(OrePort));
        Ok(w, new PlaceModule(0, 0, 0));
        Ok(w, new PlaceMachine(2, 2, 1, OrePort, -1));
    }

    [Fact]
    public void Ore_port_gated_like_cargo_port_with_rules_off()
    {
        var off = World(rules: false, GameMode.Campaign);
        Assert.True(off.Network.IsMachineLocked(OrePort));
        Assert.Equal(off.Network.IsMachineLocked(CargoPort), off.Network.IsMachineLocked(OrePort));
        off.Submit(0, new PlaceMachine(2, 2, 1, OrePort, -1));
        off.Step();
        Assert.Equal(Reject.Locked, off.LastResults.Single().Reject);

        var node = C.ResearchIndex("core:jump_drive");
        Assert.Contains(OrePort, C.Research[node].UnlockMachines);
        Assert.Contains(CargoPort, C.Research[node].UnlockMachines);
        off.Network.GrantResearch(node);
        Assert.False(off.Network.IsMachineLocked(OrePort));
        Ok(off, new PlaceMachine(2, 2, 1, OrePort, -1));
    }
}
