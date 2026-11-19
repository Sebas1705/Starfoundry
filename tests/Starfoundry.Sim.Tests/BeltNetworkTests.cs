using Starfoundry.Sim;
using Xunit;

namespace Starfoundry.Sim.Tests;

public class BeltNetworkTests
{
    private static void AssertSpacingAndOrder(BeltNetwork net)
    {
        var buf = new ItemSample[net.InTransit + 8];
        int n = net.FillSamples(buf);
        // Samples are grouped per segment, front (largest position) first.
        for (int i = 1; i < n; i++)
        {
            if (buf[i].Segment != buf[i - 1].Segment) continue;
            int gap = buf[i - 1].Pos - buf[i].Pos;
            Assert.True(gap >= Units.ItemSpacing, $"gap {gap} < spacing in segment {buf[i].Segment}");
        }
        for (int i = 0; i < n; i++)
        {
            var info = net.GetSegmentInfo(buf[i].Segment);
            Assert.InRange(buf[i].Pos, 0, info.Length);
        }
    }

    [Fact]
    public void SingleItemTravelsAtBeltSpeed()
    {
        var net = new BeltNetwork();
        int seg = net.AddSegment(0, 0, 0, 1, 0, 0, 3, BeltTier.Mk1);
        net.SetSink(seg);
        net.SetSource(seg, new ushort[] { 7 });

        // The source stops being relevant once we only watch the first item: run until first consumption.
        int ticks = 0;
        while (net.Consumed == 0 && ticks < 10_000) { net.Step(); ticks++; }

        // Spawned at tick 0 end (pos 0), then moves 8 per tick over 720 units: 90 ticks, +1 for the spawn tick.
        Assert.Equal(91, ticks);
    }

    [Theory]
    [InlineData(BeltTier.Mk1)]
    [InlineData(BeltTier.Mk2)]
    [InlineData(BeltTier.Mk3)]
    public void SaturatedThroughputMatchesTier(BeltTier tier)
    {
        var net = new BeltNetwork();
        int seg = net.AddSegment(0, 0, 0, 1, 0, 0, 20, tier);
        net.SetSource(seg, new ushort[] { 0 });
        net.SetSink(seg);

        net.Step(30 * 30); // fill the belt
        long before = net.Consumed;
        net.Step(30 * 20);
        double perSecond = (net.Consumed - before) / 20.0;

        double expected = BeltTiers.ItemsPerSecond(tier);
        Assert.InRange(perSecond, expected * 0.97, expected * 1.03);
    }

    [Fact]
    public void ChainedSegmentsKeepFullThroughput()
    {
        var net = new BeltNetwork();
        int a = net.AddSegment(0, 0, 0, 1, 0, 0, 7, BeltTier.Mk3);
        int b = net.AddSegment(7, 0, 0, 1, 0, 0, 7, BeltTier.Mk3);
        int c = net.AddSegment(14, 0, 0, 1, 0, 0, 7, BeltTier.Mk3);
        net.Connect(a, b);
        net.Connect(b, c);
        net.SetSink(c);
        net.SetSource(a, new ushort[] { 0 });

        net.Step(30 * 20);
        long before = net.Consumed;
        net.Step(30 * 20);
        double perSecond = (net.Consumed - before) / 20.0;

        Assert.InRange(perSecond, 24 * 0.97, 24 * 1.03);
    }

    [Fact]
    public void BlockedBeltFillsWithoutLosingOrOverlappingItems()
    {
        var net = new BeltNetwork();
        int seg = net.AddSegment(0, 0, 0, 1, 0, 0, 5, BeltTier.Mk2); // dead end: no sink, no output
        net.SetSource(seg, new ushort[] { 0, 1 });

        net.Step(30 * 60);

        Assert.Equal(0, net.Consumed);
        Assert.Equal(net.Spawned, net.InTransit);
        Assert.Equal(5 * 4 + 1, net.InTransit); // 5 cells at 4 items per cell, plus the item on the end line
        AssertSpacingAndOrder(net);
    }

    [Fact]
    public void ItemsAreConservedOnEveryTick()
    {
        var net = Scenarios.ParallelRows(layers: 2, rowsPerLayer: 3, cellsPerRow: 12);
        for (int t = 0; t < 2000; t++)
        {
            net.Step();
            Assert.Equal(net.Spawned, net.Consumed + net.InTransit);
        }
        AssertSpacingAndOrder(net);
    }

    [Fact]
    public void SplitterAlternatesBetweenOutputs()
    {
        var (net, _, a, b) = Scenarios.Splitter(BeltTier.Mk2);
        net.Step(30 * 60);

        long ca = net.ConsumedBy(a);
        long cb = net.ConsumedBy(b);
        Assert.True(ca > 100 && cb > 100, $"outputs should both receive items ({ca}, {cb})");
        Assert.InRange(Math.Abs(ca - cb), 0, 2);
        Assert.Equal(net.Spawned, net.Consumed + net.InTransit);
    }

    [Fact]
    public void SplitterSendsEverythingToFreeOutputWhenOtherIsBlocked()
    {
        var net = new BeltNetwork();
        int input = net.AddSegment(0, 0, 0, 1, 0, 0, 10, BeltTier.Mk2);
        int blocked = net.AddSegment(10, 0, 0, 1, 0, 0, 3, BeltTier.Mk2); // dead end
        int free = net.AddSegment(10, 0, 1, 1, 0, 0, 3, BeltTier.Mk2);
        net.Connect(input, blocked);
        net.Connect(input, free);
        net.SetSink(free);
        net.SetSource(input, new ushort[] { 0 });

        net.Step(30 * 60);

        // The dead-end output fills up, afterwards all flow goes to the free output.
        Assert.Equal(3 * 4 + 1, net.CountOn(blocked));
        Assert.True(net.ConsumedBy(free) > 300);
        Assert.Equal(net.Spawned, net.Consumed + net.InTransit);
    }

    [Fact]
    public void MergeDoesNotOverlapOrLoseItems()
    {
        var net = new BeltNetwork();
        int a = net.AddSegment(0, 0, 0, 1, 0, 0, 6, BeltTier.Mk2);
        int b = net.AddSegment(0, 0, 1, 1, 0, 0, 6, BeltTier.Mk2);
        int outSeg = net.AddSegment(6, 0, 0, 1, 0, 0, 10, BeltTier.Mk2);
        net.Connect(a, outSeg);
        net.Connect(b, outSeg);
        net.SetSink(outSeg);
        net.SetSource(a, new ushort[] { 0 });
        net.SetSource(b, new ushort[] { 1 });

        for (int t = 0; t < 3000; t++)
        {
            net.Step();
            Assert.Equal(net.Spawned, net.Consumed + net.InTransit);
        }
        AssertSpacingAndOrder(net);
        // The merged belt cannot carry more than its own rate (10 items/s).
        Assert.True(net.Consumed <= 10.0 * 100 + 20);
    }

    private static (BeltNetwork Net, int[] Inputs, int Output) BuildMerge(BeltTier outTier, params BeltTier[] inTiers)
    {
        var net = new BeltNetwork();
        var inputs = new int[inTiers.Length];
        for (int i = 0; i < inTiers.Length; i++)
            inputs[i] = net.AddSegment(0, 0, i, 1, 0, 0, 6, inTiers[i]);
        int output = net.AddSegment(6, 0, 0, 1, 0, 0, 10, outTier);
        foreach (int input in inputs)
        {
            net.Connect(input, output);
            net.SetSource(input, new ushort[] { (ushort)input });
        }
        net.SetSink(output);
        return (net, inputs, output);
    }

    [Fact]
    public void SaturatedMergeSplitsEvenlyBetweenTwoInputs()
    {
        var (net, inputs, _) = BuildMerge(BeltTier.Mk2, BeltTier.Mk2, BeltTier.Mk2);
        net.Step(30 * 30);
        long a0 = net.PassedBy(inputs[0]), b0 = net.PassedBy(inputs[1]);
        net.Step(30 * 60);
        long a = net.PassedBy(inputs[0]) - a0, b = net.PassedBy(inputs[1]) - b0;

        Assert.InRange(Math.Abs(a - b), 0, 2);
        Assert.InRange(a + b, 600 * 0.97, 600 * 1.03); // output belt limit: 10 items/s for 60 s
    }

    [Fact]
    public void SaturatedMergeSplitsEvenlyBetweenThreeInputs()
    {
        var (net, inputs, _) = BuildMerge(BeltTier.Mk2, BeltTier.Mk2, BeltTier.Mk2, BeltTier.Mk2);
        net.Step(30 * 30);
        var before = inputs.Select(net.PassedBy).ToArray();
        net.Step(30 * 60);
        var got = inputs.Select((id, i) => net.PassedBy(id) - before[i]).ToArray();

        Assert.InRange(got.Max() - got.Min(), 0, 3);
        Assert.InRange(got.Sum(), 600 * 0.97, 600 * 1.03);
    }

    [Fact]
    public void MergeIsMaxMinFairWhenOneInputIsSlow()
    {
        // A Mk1 input can only offer 4 items/s. It must get all of it; the fast input takes the rest.
        var (net, inputs, _) = BuildMerge(BeltTier.Mk3, BeltTier.Mk1, BeltTier.Mk3);
        net.Step(30 * 30);
        long slow0 = net.PassedBy(inputs[0]), fast0 = net.PassedBy(inputs[1]);
        net.Step(30 * 60);
        long slow = net.PassedBy(inputs[0]) - slow0, fast = net.PassedBy(inputs[1]) - fast0;

        Assert.InRange(slow, 240 * 0.97, 240 * 1.03); // 4 items/s for 60 s
        Assert.InRange(slow + fast, 1440 * 0.97, 1440 * 1.03); // output belt limit: 24 items/s
    }

    [Fact]
    public void MergeFairnessDoesNotDependOnSegmentOrder()
    {
        // Same merge with the inputs created in the opposite order must give the same split.
        long Gap(bool reversed)
        {
            var net = new BeltNetwork();
            int first = net.AddSegment(0, 0, 0, 1, 0, 0, 6, BeltTier.Mk2);
            int second = net.AddSegment(0, 0, 1, 1, 0, 0, 6, BeltTier.Mk2);
            int output = net.AddSegment(6, 0, 0, 1, 0, 0, 10, BeltTier.Mk2);
            if (reversed) { net.Connect(second, output); net.Connect(first, output); }
            else { net.Connect(first, output); net.Connect(second, output); }
            net.SetSink(output);
            net.SetSource(first, new ushort[] { 0 });
            net.SetSource(second, new ushort[] { 1 });
            net.Step(30 * 90);
            return Math.Abs(net.PassedBy(first) - net.PassedBy(second));
        }

        Assert.InRange(Gap(false), 0, 2);
        Assert.InRange(Gap(true), 0, 2);
    }

    [Fact]
    public void SimulationIsDeterministic()
    {
        ulong Run()
        {
            var net = Scenarios.ParallelRows(3, 3, 20);
            net.Step(5000);
            return net.Checksum();
        }

        Assert.Equal(Run(), Run());
    }

    [Fact]
    public void ChecksumChangesWhenStateChanges()
    {
        var net = Scenarios.ParallelRows(1, 1, 10);
        net.Step(100);
        ulong before = net.Checksum();
        net.Step();
        Assert.NotEqual(before, net.Checksum());
    }

    [Fact]
    public void CyclesAreBrokenByIdAndStayConsistent()
    {
        var net = new BeltNetwork();
        int a = net.AddSegment(0, 0, 0, 1, 0, 0, 4, BeltTier.Mk1);
        int b = net.AddSegment(4, 0, 0, 0, 0, 1, 4, BeltTier.Mk1);
        int c = net.AddSegment(4, 0, 4, -1, 0, 0, 4, BeltTier.Mk1);
        int d = net.AddSegment(0, 0, 4, 0, 0, -1, 4, BeltTier.Mk1);
        net.Connect(a, b); net.Connect(b, c); net.Connect(c, d); net.Connect(d, a);
        net.SetSource(a, new ushort[] { 0 });

        net.Step(3000);

        Assert.Equal(net.Spawned, net.InTransit);
        AssertSpacingAndOrder(net);
    }

    [Fact]
    public void RejectsInvalidWiring()
    {
        var net = new BeltNetwork();
        int a = net.AddSegment(0, 0, 0, 1, 0, 0, 2, BeltTier.Mk1);
        int b = net.AddSegment(2, 0, 0, 1, 0, 0, 2, BeltTier.Mk1);
        int c = net.AddSegment(2, 0, 1, 1, 0, 0, 2, BeltTier.Mk1);
        int d = net.AddSegment(2, 0, 2, 1, 0, 0, 2, BeltTier.Mk1);
        net.Connect(a, b);
        net.Connect(a, c);
        Assert.Throws<InvalidOperationException>(() => net.Connect(a, d));
        Assert.Throws<InvalidOperationException>(() =>
        {
            int e = net.AddSegment(9, 0, 0, 1, 0, 0, 2, BeltTier.Mk1);
            int f = net.AddSegment(11, 0, 0, 1, 0, 0, 2, BeltTier.Mk1);
            net.Connect(e, f);
            net.Connect(e, f);
        });
        Assert.Throws<InvalidOperationException>(() => net.SetSink(a));
        net.SetSink(d);
        Assert.Throws<InvalidOperationException>(() => net.Connect(d, a));
        Assert.Throws<ArgumentOutOfRangeException>(() => net.Connect(a, 99));
    }
}
