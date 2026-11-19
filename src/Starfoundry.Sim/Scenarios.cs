namespace Starfoundry.Sim;

/// <summary>Ready-made networks shared by tests, the benchmark and the Godot prototype.</summary>
public static class Scenarios
{
    /// <summary>
    /// Stacked parallel belt rows along +X, each fed by a source and drained by a sink. Tiers cycle
    /// Mk1, Mk2, Mk3. Saturated, it holds about layers * rowsPerLayer * cellsPerRow * 4 items.
    /// </summary>
    public static BeltNetwork ParallelRows(int layers, int rowsPerLayer, int cellsPerRow, ContentDb? content = null)
    {
        var net = new BeltNetwork(content);
        ReadOnlySpan<ushort> pattern = stackalloc ushort[] { 0, 1, 2, 3 };
        for (int l = 0; l < layers; l++)
        {
            for (int r = 0; r < rowsPerLayer; r++)
            {
                var tier = (BeltTier)((l + r) % 3);
                int id = net.AddSegment(0, l * 2, r * 2, 1, 0, 0, cellsPerRow, tier);
                net.SetSource(id, pattern);
                net.SetSink(id);
            }
        }
        return net;
    }

    /// <summary>Source into a splitter that feeds two sink belts. Used to check fair splitting.</summary>
    public static (BeltNetwork Net, int Input, int OutA, int OutB) Splitter(BeltTier tier = BeltTier.Mk2)
    {
        var net = new BeltNetwork();
        int input = net.AddSegment(0, 0, 0, 1, 0, 0, 10, tier);
        int a = net.AddSegment(10, 0, 0, 1, 0, 0, 5, tier);
        int b = net.AddSegment(10, 0, 1, 1, 0, 0, 5, tier);
        net.Connect(input, a);
        net.Connect(input, b);
        net.SetSink(a);
        net.SetSink(b);
        net.SetSource(input, new ushort[] { 0 });
        return (net, input, a, b);
    }
}
