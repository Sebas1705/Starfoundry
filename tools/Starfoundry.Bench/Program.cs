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

Console.WriteLine($"segments:    {net.SegmentCount}");
Console.WriteLine($"items:       {net.InTransit}");
Console.WriteLine($"ticks:       {ticks} after warm-up");
Console.WriteLine($"avg ms/tick: {avg:F4}");
Console.WriteLine($"p99 ms/tick: {p99:F4}");
Console.WriteLine($"max ms/tick: {max:F4}");
Console.WriteLine($"budget:      {budgetMs:F2} ms/tick (30 UPS), target < 5 ms for 10k items");
Console.WriteLine($"conserved:   {net.Spawned == net.Consumed + net.InTransit}");
Console.WriteLine($"checksum:    {net.Checksum():X16}");

return net.Spawned == net.Consumed + net.InTransit && p99 < budgetMs ? 0 : 1;

static int Arg(int index, int fallback)
{
    var args = Environment.GetCommandLineArgs();
    return args.Length > index + 1 && int.TryParse(args[index + 1], out int v) ? v : fallback;
}
