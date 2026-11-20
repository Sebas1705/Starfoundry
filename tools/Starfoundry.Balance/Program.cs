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
int observe = 0, turrets = 1, walls = 0, bays = 0, upgrades = 0;
var repairs = RepairMode.Never;
var path = UpgradePath.Defence;
bool extraPlates = false;
string granted = "";
foreach (var a in args)
{
    if (a.StartsWith("--observe=")) observe = int.Parse(a["--observe=".Length..]) * 60;
    else if (a.StartsWith("--turrets=")) turrets = int.Parse(a["--turrets=".Length..]);
    else if (a.StartsWith("--walls=")) walls = int.Parse(a["--walls=".Length..]);
    else if (a.StartsWith("--bays=")) bays = int.Parse(a["--bays=".Length..]);
    else if (a.StartsWith("--upgrades=")) upgrades = int.Parse(a["--upgrades=".Length..]);
    else if (a == "--extra-plates") extraPlates = true;
    else if (a.StartsWith("--grant=")) granted = a["--grant=".Length..];

    else if (a.StartsWith("--path=")) path = Enum.Parse<UpgradePath>(a["--path=".Length..], ignoreCase: true);
    else if (a.StartsWith("--repairs=")) repairs = Enum.Parse<RepairMode>(a["--repairs=".Length..], ignoreCase: true);
}
var report = new BalanceBot(content, seed)
{
    ActionDelaySeconds = delay,
    ObserveAfterSeconds = observe,
    Turrets = turrets,
    Walls = walls,
    Bays = bays,
    Upgrades = upgrades,
    Path = path,
    ExtraPlateLine = extraPlates,
    Granted = granted,

    Repairs = repairs,
    Trace = args.Contains("--trace") ? Console.Out : null,
}.Run(minutes * 60);
Console.WriteLine($"Balance run: action delay {delay} s, seed {seed}\n");
Console.WriteLine(BalanceBot.Format(report));
if (args.Contains("--waves")) Console.WriteLine("\n" + BalanceBot.FormatWaves(report));
return report.Finished ? 0 : 2;
