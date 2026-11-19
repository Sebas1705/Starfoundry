using System.Diagnostics;
using Starfoundry.Sim;

// Usage: dotnet run -c Release --project tools/Starfoundry.Bench -- [layers] [rowsPerLayer] [cellsPerRow] [ticks]
int layers = Arg(0, 10);
int rows = Arg(1, 5);
int cells = Arg(2, 50);
int ticks = Arg(3, 3000);

var net = Scenarios.ParallelRows(layers, rows, cells);
net.Step(30 * 30); // warm-up: fill every belt

var samples = new double[ticks];
var sw = new Stopwatch();
for (int i = 0; i < ticks; i++)
{
    sw.Restart();
    net.Step();
    sw.Stop();
    samples[i] = sw.Elapsed.TotalMilliseconds;
}

Array.Sort(samples);
double avg = samples.Average();
double p99 = samples[(int)(ticks * 0.99)];
double max = samples[^1];
const double budgetMs = 1000.0 / Units.TicksPerSecond;

// Command cost: a tick that applies an atomic batch clones the network for rollback, so measure it.
var world = new SimWorld(net);
const int batches = 20;
var batchMs = new double[batches];
for (int i = 0; i < batches; i++)
{
    world.Submit(0, new Batch(new Command[]
    {
        new PlaceSegment(0, layers * 2 + 2 + i * 2, 0, Dir.PosX, cells, BeltTier.Mk1),
        new SetSource(-1, new ushort[] { 0 }),
        new SetSink(-1),
    }));
    sw.Restart();
    world.Step();
    sw.Stop();
    if (!world.LastResults.Single().Ok) throw new InvalidOperationException("The benchmark batch was rejected.");
    batchMs[i] = sw.Elapsed.TotalMilliseconds;
}
Array.Sort(batchMs);

Console.WriteLine($"segments:    {net.SegmentCount - batches}");
Console.WriteLine($"items:       {net.InTransit}");
Console.WriteLine($"ticks:       {ticks} after warm-up");
Console.WriteLine($"avg ms/tick: {avg:F4}");
Console.WriteLine($"p99 ms/tick: {p99:F4}");
Console.WriteLine($"max ms/tick: {max:F4}");
Console.WriteLine($"batch tick:  median {batchMs[batches / 2]:F3} ms, max {batchMs[^1]:F3} ms (tick that applies one atomic batch)");
Console.WriteLine($"budget:      {budgetMs:F2} ms/tick (30 UPS), target < 5 ms for 10k items");
bool conserved = net.Spawned == net.Consumed + net.Destroyed + net.InTransit;
Console.WriteLine($"conserved:   {conserved}");
Console.WriteLine($"checksum:    {net.Checksum():X16}");

return conserved && p99 < budgetMs && batchMs[^1] < budgetMs ? 0 : 1;

static int Arg(int index, int fallback)
{
    var args = Environment.GetCommandLineArgs();
    return args.Length > index + 1 && int.TryParse(args[index + 1], out int v) ? v : fallback;
}
