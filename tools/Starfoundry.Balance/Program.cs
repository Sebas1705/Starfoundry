using Starfoundry.Balance;
using Starfoundry.Sim;

// Usage: dotnet run --project tools/Starfoundry.Balance -- [--delay=5] [--seed=20261120] [--minutes=60]
double delay = 5;
ulong seed = 20261120UL;
int minutes = 60;
foreach (var a in args)
{
    if (a.StartsWith("--delay=")) delay = double.Parse(a["--delay=".Length..], System.Globalization.CultureInfo.InvariantCulture);
    else if (a.StartsWith("--seed=")) seed = ulong.Parse(a["--seed=".Length..]);
    else if (a.StartsWith("--minutes=")) minutes = int.Parse(a["--minutes=".Length..]);
}

var dir = new DirectoryInfo(AppContext.BaseDirectory);
while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "content", "core.json"))) dir = dir.Parent;
if (dir is null) { Console.Error.WriteLine("content/core.json not found"); return 1; }

var content = ContentDb.FromFile(Path.Combine(dir.FullName, "content", "core.json"));
int observe = 0;
foreach (var a in args)
    if (a.StartsWith("--observe=")) observe = int.Parse(a["--observe=".Length..]) * 60;
var report = new BalanceBot(content, seed)
{
    ActionDelaySeconds = delay,
    ObserveAfterSeconds = observe,
    Trace = args.Contains("--trace") ? Console.Out : null,
}.Run(minutes * 60);
Console.WriteLine($"Balance run: action delay {delay} s, seed {seed}\n");
Console.WriteLine(BalanceBot.Format(report));
return report.Finished ? 0 : 2;
