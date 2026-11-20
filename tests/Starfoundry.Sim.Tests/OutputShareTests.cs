using Starfoundry.Sim;
using Xunit;

namespace Starfoundry.Sim.Tests;

public class OutputShareTests
{
    private static readonly ContentDb C = TestContent.Core;
    private static readonly int Extractor = C.MachineIndex("core:extractor");
    private static readonly int Smelter = C.MachineIndex("core:smelter");
    private static readonly int MineIron = C.RecipeIndex("core:mine_iron");
    private static readonly int SmeltIron = C.RecipeIndex("core:smelt_iron");

    private static SimWorld World() => new(C); // sandbox, no threats

    private static int Ok(SimWorld w, Command c)
    {
        w.Submit(0, c);
        w.Step();
        var r = w.LastResults.Single();
        Assert.True(r.Ok, $"{c} was rejected: {r.Reject}");
        return r.CreatedMachines.Count > 0 ? r.CreatedMachines[0] : r.Created.Count > 0 ? r.Created[0] : -1;
    }

    /// <summary>A producer with two output belts, each ending in a sink so nothing ever backs up; returns the two belts.</summary>
    private static (int A, int B) TwoOutputs(SimWorld w, int machineType, int recipe)
    {
        int m = Ok(w, new PlaceMachine(5, 0, 5, machineType, recipe));
        int a = Ok(w, new PlaceSegment(6, 0, 5, Dir.PosX, 4, BeltTier.Mk1));
        int b = Ok(w, new PlaceSegment(5, 0, 4, Dir.NegZ, 4, BeltTier.Mk1));
        Ok(w, new ConnectFromMachine(m, a));
        Ok(w, new ConnectFromMachine(m, b));
        Ok(w, new SetSink(a));
        Ok(w, new SetSink(b));
        return (a, b);
    }

    [Fact]
    public void AnItemEveryEvenNumberOfTicksStillAlternatesBetweenTheTwoOutputBelts()
    {
        // The extractor finishes a craft every 30 ticks, always on the same tick parity: the old rotation per tick gave every item to one belt.
        var w = World();
        var (a, b) = TwoOutputs(w, Extractor, MineIron);
        w.Step(30 * 120);
        long pa = w.Network.PassedBy(a), pb = w.Network.PassedBy(b);
        Assert.True(pa + pb >= 100, $"only {pa + pb} items came out");
        Assert.InRange(pa - pb, -3, 3); // a few items may still be on the belts
        w.ValidateInvariants();
    }

    [Fact]
    public void ABackedUpBeltIsSkippedAndTheOtherTakesEverything()
    {
        var w = World();
        int m = Ok(w, new PlaceMachine(5, 0, 5, Extractor, MineIron));
        int a = Ok(w, new PlaceSegment(6, 0, 5, Dir.PosX, 4, BeltTier.Mk1)); // no sink: fills up and stops accepting
        int b = Ok(w, new PlaceSegment(5, 0, 4, Dir.NegZ, 4, BeltTier.Mk1));
        Ok(w, new ConnectFromMachine(m, a));
        Ok(w, new ConnectFromMachine(m, b));
        Ok(w, new SetSink(b));
        w.Step(30 * 120);
        Assert.Equal(0, w.Network.PassedBy(a));
        Assert.True(w.Network.PassedBy(b) >= 80, $"the open belt took everything the full one could not (took {w.Network.PassedBy(b)})");
        Assert.True(w.Network.CountOn(a) > 0);
        w.ValidateInvariants();
    }

    [Fact]
    public void ASlowCraftWithAnOddLengthAlsoSplitsEvenly()
    {
        // The smelter works on ore from a feeding belt; here it is fed by a second extractor through a belt.
        var w = World();
        int src = Ok(w, new PlaceMachine(0, 0, 5, Extractor, MineIron));
        int feed = Ok(w, new PlaceSegment(1, 0, 5, Dir.PosX, 3, BeltTier.Mk1));
        Ok(w, new ConnectFromMachine(src, feed));
        var (a, b) = (0, 0);
        int sm = Ok(w, new PlaceMachine(4, 0, 5, Smelter, SmeltIron));
        Ok(w, new ConnectToMachine(feed, sm));
        a = Ok(w, new PlaceSegment(5, 0, 5, Dir.PosX, 4, BeltTier.Mk1));
        b = Ok(w, new PlaceSegment(4, 0, 4, Dir.NegZ, 4, BeltTier.Mk1));
        Ok(w, new ConnectFromMachine(sm, a));
        Ok(w, new ConnectFromMachine(sm, b));
        Ok(w, new SetSink(a));
        Ok(w, new SetSink(b));
        w.Step(30 * 200);
        long pa = w.Network.PassedBy(a), pb = w.Network.PassedBy(b);
        Assert.True(pa + pb >= 40, $"only {pa + pb} ingots came out");
        Assert.InRange(pa - pb, -3, 3); // a few items may still be on the belts
    }
}
