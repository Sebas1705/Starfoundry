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

// --content=path, else next to the executable, else the repository's content folder found by walking up.
var dir = new DirectoryInfo(AppContext.BaseDirectory);
while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "content", "core.json"))) dir = dir.Parent;
var lookup = Starfoundry.Ui.ContentLocator.Resolve(Starfoundry.Ui.ContentLocator.ExplicitPath(args), null, AppContext.BaseDirectory,
    dir is null ? null : Path.Combine(dir.FullName, "content", "core.json"), File.Exists);
if (lookup.Path is null) { Console.Error.WriteLine("content/core.json not found. Tried:\n" + lookup.TriedText); return 1; }

var content = ContentDb.FromFile(lookup.Path);
if (args.FirstOrDefault(a => a.StartsWith("--scenario=")) is { } scenarioArg)
{
    // A story level: dotnet run --project tools/Starfoundry.Balance -- --scenario=01_first_light [--minutes 90] [--trace] [--delay=6] [--seed=N]
    // Plays levels 01, 02 and 03; levels 04 and 05 are validated by reachability analysis in the content tests only.
    string id = scenarioArg["--scenario=".Length..];
    var catalog = ScenarioCatalog.FromDirectory(content, Path.Combine(Path.GetDirectoryName(lookup.Path)!, "scenarios"));
    var def = catalog.Find(id);
    if (def is null) { Console.Error.WriteLine($"Unknown scenario '{id}'. Known: {string.Join(", ", catalog.All.Select(s => s.Id))}"); return 1; }
    if (!StructureBot.PlayableScenarios.Contains(id)) { Console.Error.WriteLine($"The bot has no plan for '{id}'. Playable: {string.Join(", ", StructureBot.PlayableScenarios)}"); return 1; }
    string? Val(string name) => args.SkipWhile(a => a != name).Skip(1).FirstOrDefault() ?? args.FirstOrDefault(a => a.StartsWith(name + "="))?[(name.Length + 1)..];
    int scenarioMinutes = Val("--minutes") is { } mt2 ? int.Parse(mt2) : 90;
    ulong scenarioSeed = Val("--seed") is { } sd2 ? ulong.Parse(sd2) : 0;
    var scenarioBot = new StructureBot(content, def, scenarioSeed)
    {
        ActionDelaySeconds = args.Any(a => a.StartsWith("--delay=")) ? delay : 6,
        Trace = args.Contains("--trace") ? Console.Out : null,
    };
    var scenarioReport = scenarioBot.Run(scenarioMinutes * 60);
    Console.WriteLine($"Scenario run: {def.Id}, seed {(scenarioSeed != 0 ? scenarioSeed : def.Seed)}, action delay {scenarioBot.ActionDelaySeconds} s\n");
    Console.WriteLine(scenarioReport.Format());
    Console.WriteLine($"Stars: {scenarioBot.Stars}");
    foreach (var g in scenarioBot.GoalStates)
        Console.WriteLine($"- {(g.Primary ? "primary" : "secondary")} {g.Goal.Id}: {g.Status}{(g.CompletedSeconds is { } c ? " at " + StructureBotReport.Clock(c) : "")}{(g.FailedSeconds is { } f ? " at " + StructureBotReport.Clock(f) : "")} ({g.ProgressText})");
    return scenarioReport.Outcome == Starfoundry.Ui.OutcomeKind.Won ? 0 : 2;
}
if (args.Contains("--structures"))
{
    // The structure campaign: dotnet run --project tools/Starfoundry.Balance -- --structures --minutes 120 --seed N --trace [--delay=6] [--no-threats] [--difficulty=relaxed|normal|hard|brutal] [--continue]
    string? Value(string name) => args.SkipWhile(a => a != name).Skip(1).FirstOrDefault() ?? args.FirstOrDefault(a => a.StartsWith(name + "="))?[(name.Length + 1)..];
    ulong sSeed = Value("--seed") is { } st ? ulong.Parse(st) : 20261121UL;
    int sMinutes = Value("--minutes") is { } mt ? int.Parse(mt) : 120;
    // --difficulty=relaxed|normal|hard|brutal plays with that preset of world rules (default: normal, the content as is).
    string difficulty = Value("--difficulty") ?? "normal";
    var rules = WorldRules.Preset(difficulty);
    if (rules is null) { Console.Error.WriteLine($"Unknown difficulty '{difficulty}'. Use relaxed, normal, hard or brutal."); return 1; }
    // --richness=N scales every deposit (25..400 percent): the science tier needs far more ore than the home system holds, so
    // --science is measured with --richness=400 to study the research time rather than the ore supply.
    if (Value("--richness") is { } rich)
        rules = new WorldRules
        {
            EnemyDamagePercent = rules.EnemyDamagePercent, EnemyHpPercent = rules.EnemyHpPercent, WaveSizePercent = rules.WaveSizePercent,
            WaveIntervalPercent = rules.WaveIntervalPercent, FirstWaveGraceSeconds = rules.FirstWaveGraceSeconds,
            DepositRichnessPercent = int.Parse(rich), PowerOutputPercent = rules.PowerOutputPercent, GoalWaves = rules.GoalWaves,
            ThreatsEnabled = rules.ThreatsEnabled, FreeBuild = rules.FreeBuild, StartingItems = rules.StartingItems,
        };
    var bot = new StructureBot(content, sSeed, threats: !args.Contains("--no-threats"), rules: rules)
    {
        ActionDelaySeconds = args.Any(a => a.StartsWith("--delay=")) ? delay : 6,
        Trace = args.Contains("--trace") ? Console.Out : null,
        ContinueAfterVictory = args.Contains("--continue") || args.Contains("--science"),
    };
    var sReport = bot.Run(sMinutes * 60);
    Console.WriteLine($"Structure campaign run: seed {sSeed}, difficulty {difficulty}, goal {bot.GoalWaves} waves, action delay {bot.ActionDelaySeconds} s\n");
    Console.WriteLine(sReport.Format());
    return sReport.Outcome == Starfoundry.Ui.OutcomeKind.Won ? 0 : 2;
}
int observe = 0, turrets = 1, walls = 0, bays = 0, upgrades = 0;
var repairs = RepairMode.Never;
var path = UpgradePath.Defence;
bool extraPlates = false, expand = false, takeDown = false, port = false;
int expandAt = 100;
string granted = "";
foreach (var a in args)
{
    if (a.StartsWith("--observe=")) observe = int.Parse(a["--observe=".Length..]) * 60;
    else if (a.StartsWith("--turrets=")) turrets = int.Parse(a["--turrets=".Length..]);
    else if (a.StartsWith("--walls=")) walls = int.Parse(a["--walls=".Length..]);
    else if (a.StartsWith("--bays=")) bays = int.Parse(a["--bays=".Length..]);
    else if (a.StartsWith("--upgrades=")) upgrades = int.Parse(a["--upgrades=".Length..]);
    else if (a == "--extra-plates") extraPlates = true;
    else if (a == "--expand") expand = true;
    else if (a == "--port") port = true;
    else if (a.StartsWith("--expand-at=")) { expand = true; expandAt = int.Parse(a["--expand-at=".Length..]); }
    else if (a == "--take-down-carbon") takeDown = true;
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
    Expand = expand,
    UseUnloader = port,
    ExpandAtPercent = expandAt,
    TakeDownCarbonLine = takeDown,
    Granted = granted,

    Repairs = repairs,
    Trace = args.Contains("--trace") ? Console.Out : null,
}.Run(minutes * 60);
Console.WriteLine($"Balance run: action delay {delay} s, seed {seed}\n");
Console.WriteLine(BalanceBot.Format(report));
if (args.Contains("--waves")) Console.WriteLine("\n" + BalanceBot.FormatWaves(report));
return report.Finished ? 0 : 2;
