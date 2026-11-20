using Starfoundry.Sim;
using Starfoundry.Ui;

namespace Starfoundry.Balance;

/// <summary>The state of the base when a wave spawned.</summary>
public enum RepairMode { Never, BetweenWaves, Always }

public enum UpgradePath { Defence, Ship }

public sealed record WaveEntry(int Index, double Seconds, int Enemies, long MachinesLost, long ShipsLost, int Turrets);

/// <summary>What one balance run measured. Times are simulated seconds since the start of the run.</summary>
public sealed class BalanceReport
{
    public Dictionary<string, double> Milestones { get; } = new();
    /// <summary>Free-form timeline lines (expansion milestones), each with the home iron left at that moment.</summary>
    public List<string> Notes { get; } = new();
    /// <summary>Iron left in the home deposit at each expansion milestone.</summary>
    public Dictionary<string, int> HomeIronLeft { get; } = new();
    /// <summary>Items the remote depots delivered after the remote lines were built, and the remote deposit's ore left at the end.</summary>
    public long RemoteDelivered { get; set; }
    public int RemoteIronLeft { get; set; } = -1;
    public double? FirstWaveWarning { get; set; }
    public double? FirstWave { get; set; }
    public double HoldFullSeconds { get; set; }
    public double WaitingForItemsSeconds { get; set; }
    public double Seconds { get; set; }
    public long MachinesLost { get; set; }
    public long Waves { get; set; }
    public long WallsLost { get; set; }
    public long EnemiesKilled { get; set; }
    public bool Finished { get; set; }
    public List<WaveEntry> WaveLog { get; } = new();
    /// <summary>The wave (1 is the first) during which the first machine was destroyed, or null if none was.</summary>
    public int? FirstLossWave { get; set; }
    public string Stuck { get; set; } = "";
}

/// <summary>
/// A scripted player that plays the guided first steps headlessly with the same commands a human sends, and records
/// when each milestone happens. It is a lower bound for a human: it never hesitates, so <see cref="ActionDelaySeconds"/>
/// adds a pause after every action to stand in for clicking and thinking.
/// </summary>
public sealed partial class BalanceBot
{
    private readonly ContentDb _c;
    private readonly SimWorld _w;
    private readonly ObjectiveTracker _objectives;
    private readonly BalanceReport _report = new();
    private readonly List<Func<bool>> _steps = new();
    private readonly Dictionary<string, int> _ids = new();
    private int _step;
    private int _cooldown;
    private int _ironDeposit = -1;
    private (int X, int Y, int Z) _intent;
    private bool _mining;

    public double ActionDelaySeconds { get; init; } = 5;

    /// <summary>How many fed turrets the base gets (1, 2 or 4).</summary>
    public int Turrets { get; init; } = 1;

    /// <summary>How many walls to put around the base (0 to 4), once the first turret stands.</summary>
    public int Walls { get; init; }

    /// <summary>How many defence upgrades to research after Logistics (0 to 6), in order: calibration, plating, loader, field repair, ship armour, ship cannon.</summary>
    public int Upgrades { get; init; }

    /// <summary>Which upgrades come first: the turret and repair line, or the ship line (armour and cannon right after their predecessors).</summary>
    public UpgradePath Path { get; init; } = UpgradePath.Defence;

    /// <summary>A second, dedicated plate line (drill, smelter, assembler, depot) once the first turret stands.</summary>
    public bool ExtraPlateLine { get; init; }

    /// <summary>Research finished before the run starts (comma-separated ids), for what-if comparisons such as "what do the ship nodes do on their own".</summary>
    public string Granted { get; init; } = "";

    /// <summary>How many repair bays to build (0 to 2), each fed ingots by a second output of a smelter.</summary>
    public int Bays { get; init; }

    /// <summary>When the player presses repair: never, only when no enemy is alive, or whenever anything is damaged.</summary>
    public RepairMode Repairs { get; init; } = RepairMode.Never;

    /// <summary>Stop as soon as a machine is destroyed; enough to find the first wave the defence cannot hold.</summary>
    public bool StopAtFirstLoss { get; init; }

    /// <summary>Keep playing (the base just sits there, mining as before) this long after the guided steps, to see the first waves.</summary>
    public int ObserveAfterSeconds { get; init; }

    private int _finishedAt;

    /// <summary>When set, a status line is written every 30 simulated seconds.</summary>
    public TextWriter? Trace { get; init; }

    public BalanceBot(ContentDb content, ulong galaxySeed = 20261120UL)
    {
        _c = content;
        _w = SimWorld.CreateGalaxy(content, GameMode.Campaign, galaxySeed, threats: true);
        _w.Network.EnsurePlayer(0);
        _objectives = new ObjectiveTracker(content);
    }

    private BeltNetwork Net => _w.Network;
    private int Item(string id) => _c.ItemIndex(id);
    private int Machine(string id) => _c.MachineIndex(id);
    private int Recipe(string id) => _c.RecipeIndex(id);

    private CommandResult Do(Command c)
    {
        _w.Submit(0, c);
        _w.Step();
        return _w.LastResults.Last();
    }

    /// <summary>Runs a command that must succeed, except that a lack of items means "try again later".</summary>
    private bool Try(Command c, out CommandResult result)
    {
        result = Do(c);
        if (result.Ok) return true;
        if (result.Reject == Reject.NotEnoughItems) return false;
        throw new InvalidOperationException($"{c} rejected with {result.Reject} at {_w.Tick / (double)Units.TicksPerSecond:F0}s");
    }

    // ---- script building blocks ----------------------------------------------------------------------

    private void Machine_(string key, int x, int z, string machine, string? recipe, Action? after = null)
    {
        _steps.Add(() =>
        {
            int r = recipe is null ? -1 : Recipe(recipe);
            if (!Try(new PlaceMachine(x, 0, z, Machine(machine), r), out var res)) return false;
            _ids[key] = res.CreatedMachines[0];
            after?.Invoke();
            return true;
        });
    }

    /// <summary>A belt of the given length that starts next to its producer; the consumer is connected by the caller.</summary>
    private void Link(string key, int x, int z, int length, string producer)
    {
        _steps.Add(() =>
        {
            if (!Try(new PlaceSegment(x, 0, z, Dir.PosX, length, BeltTier.Mk1), out var res)) return false;
            _ids[key] = res.Created[0];
            Do(new ConnectFromMachine(_ids[producer], _ids[key]));
            return true;
        });
    }

    private void Belt(string key, int x, int z, string from, string to)
    {
        _steps.Add(() =>
        {
            if (!Try(new PlaceSegment(x, 0, z, Dir.PosX, 3, BeltTier.Mk1), out var res)) return false;
            _ids[key] = res.Created[0];
            Do(new ConnectFromMachine(_ids[from], _ids[key]));
            Do(new ConnectToMachine(_ids[key], _ids[to]));
            return true;
        });
    }

    /// <summary>
    /// A drill, a belt, a smelter, a belt and an optional assembler and belt, all in a row along +X at depth z.
    /// The last belt is left unconnected at its end; the caller places what it feeds.
    /// </summary>
    private void Row(string name, int z, string? assembler)
    {
        string drill = name + ".drill", sm = name + ".smelter", asm = name + ".asm";
        Machine_(drill, 0, z, "core:drill", "core:drill_iron", () => Do(new LinkDeposit(_ids[drill], _ironDeposit)));
        BeltFrom(name + ".b1", 1, z, drill);
        Machine_(sm, 4, z, "core:smelter", "core:smelt_iron", () => Do(new ConnectToMachine(_ids[name + ".b1"], _ids[sm])));
        BeltFrom(name + ".b2", 5, z, sm);
        if (assembler is null) return;
        Machine_(asm, 8, z, "core:assembler", assembler, () => Do(new ConnectToMachine(_ids[name + ".b2"], _ids[asm])));
        BeltFrom(name + ".b3", 9, z, asm);
    }

    private void BeltFrom(string key, int x, int z, string producer)
    {
        _steps.Add(() =>
        {
            if (!Try(new PlaceSegment(x, 0, z, Dir.PosX, 3, BeltTier.Mk1), out var res)) return false;
            _ids[key] = res.Created[0];
            Do(new ConnectFromMachine(_ids[producer], _ids[key]));
            return true;
        });
    }

    private void BuildScript()
    {
        // Fly to the home iron deposit, then mine it and switch the ship refinery on.
        _steps.Add(() =>
        {
            if (_ironDeposit < 0)
                for (int d = 0; d < Net.DepositCount; d++)
                    if (Net.IsDepositRevealed(d) && Net.DepositItem(d) == Item("core:iron_ore")) { _ironDeposit = d; break; }
            if (!Net.InMineRange(0, _ironDeposit)) { Steer(Net.DepositPosition(_ironDeposit)); return false; }
            Do(new SetMovement(0, 0, 0));
            _intent = (0, 0, 0);
            Do(new StartMining(_ironDeposit));
            Do(new SetShipRecipe(Recipe("core:ship_smelt")));
            _mining = true;
            return true;
        });

        // Line 1: drill -> smelter -> depot, delivering ingots into the hold.
        Row("l1", 0, null);
        Machine_("l1.depot", 8, 0, "core:depot", null, () => Do(new ConnectToMachine(_ids["l1.b2"], _ids["l1.depot"])));

        // Line 2: drill -> smelter -> assembler (plates) -> depot.
        Row("l2", 3, "core:iron_plate");
        Machine_("l2.depot", 12, 3, "core:depot", null, () => Do(new ConnectToMachine(_ids["l2.b3"], _ids["l2.depot"])));

        // Wait for plates for the lab (4) and a turret (4), then swap the plate depot for a lab.
        int PlatesFor(string machine) => _c.Machines[Machine(machine)].Cost.Where(s => s.Item == Item("core:iron_plate")).Sum(s => s.Count);
        int platesNeeded = PlatesFor("core:research_lab") + PlatesFor("core:turret");
        _steps.Add(() => Net.HoldCount(0, Item("core:iron_plate")) >= platesNeeded);
        _steps.Add(() => Try(new RemoveMachine(_ids["l2.depot"]), out _));
        Machine_("lab", 12, 3, "core:research_lab", null, () => Do(new ConnectToMachine(_ids["l2.b3"], _ids["lab"])));
        _steps.Add(() => Net.IsResearched(_c.ResearchIndex("core:logistics")) || Try(new SetResearch(_c.ResearchIndex("core:logistics")), out _));
        _steps.Add(() => Try(new Scan(), out _));

        // Line 3: drill -> smelter -> assembler (ammo) -> turret, weapons on.
        Row("l3", 6, "core:make_ammo");
        Machine_("turret", 12, 6, "core:turret", null, () =>
        {
            Do(new ConnectToMachine(_ids["l3.b3"], _ids["turret"]));
            Do(new SetWeapons(true));
        });

        // A player who has built a defence keeps the ship inside it: at the deposit it would be out of the turrets' reach.
        _steps.Add(() =>
        {
            // Steer tick by tick: the ship covers 6 cells a second, so a once-a-second correction would overshoot forever.
            for (int tick = 0; tick < 20 * Units.TicksPerSecond; tick++)
            {
                var p = Net.GetPlayerInfo(0);
                if (Math.Abs(p.X - 6 * Units.CellUnits) <= Units.CellUnits / 2 && Math.Abs(p.Z - 3 * Units.CellUnits) <= Units.CellUnits / 2
                    && Math.Abs(p.Y) <= Units.CellUnits / 2) break;
                Steer((6, 0, 3));
                _w.Step();
            }
            Do(new SetMovement(0, 0, 0));
            _intent = (0, 0, 0);
            return true;
        });

        // More turrets, each with its own ammo line: one drill and smelter only make enough ammo for one turret firing
        // all the time. They cost plates, and the plate line feeds the lab, so once Logistics is done the lab goes back to a depot.
        if (Turrets >= 2 || Walls > 0 || Bays > 0)
        {
            _steps.Add(() => Net.IsResearched(_c.ResearchIndex("core:logistics")));
            if (Keeping && ExtraPlateLine)
            {
                // The lab keeps the first plate line for research; a second line feeds the hold for building.
                Row("p2", -3, "core:iron_plate");
                Machine_("p2.depot", 12, -3, "core:depot", null, () => Do(new ConnectToMachine(_ids["p2.b3"], _ids["p2.depot"])));
            }
            else if (Keeping)
            {
                // The lab stays for the upgrades, so the plates for building come from a second output of the assembler.
                // The assembler fills its first output first, so while the lab is researching almost nothing reaches the hold.
                Link("plates.belt", 8, 4, 4, "l2.asm");
                Machine_("plates.depot", 12, 4, "core:depot", null, () => Do(new ConnectToMachine(_ids["plates.belt"], _ids["plates.depot"])));
            }
            else
            {
                _steps.Add(() => Try(new RemoveMachine(_ids["lab"]), out _));
                Machine_("plates.depot", 12, 3, "core:depot", null, () => Do(new ConnectToMachine(_ids["l2.b3"], _ids["plates.depot"])));
            }
        }
        // Walls on four sides, inside the turrets' reach: enemies go for the nearest one, so it does not matter which side they come from.
        for (int i = 0; i < Walls; i++)
        {
            var (wx, wz) = WallSpots[i];
            _wallSpots.Add((wx, wz));
            _steps.Add(() =>
            {
                if (!Try(new PlaceMachine(wx, 0, wz, Machine("core:wall"), -1), out _)) return false;
                _wallsBuilt++; // from now on Maintain puts this wall back whenever it is lost
                return true;
            });
        }
        // Repair bays take ingots from a second output of the first two smelters, which also feed the depots.
        if (Bays >= 1)
        {
            Link("bay1.belt", 4, -1, 4, "l1.smelter");
            Machine_("bay1", 8, -1, "core:repair_bay", null, () => Do(new ConnectToMachine(_ids["bay1.belt"], _ids["bay1"])));
        }
        if (Bays >= 2)
        {
            Link("bay2.belt", 4, 2, 4, "l2.smelter");
            Machine_("bay2", 8, 2, "core:repair_bay", null, () => Do(new ConnectToMachine(_ids["bay2.belt"], _ids["bay2"])));
        }
        if (Turrets >= 2)
        {
            int[] rows = { 9, -3, 12, -6 };
            for (int i = 0; i < Turrets - 1; i++)
            {
                string name = "x" + i;
                int z = rows[i];
                Row(name, z, "core:make_ammo");
                Machine_(name + ".turret", 12, z, "core:turret", null, () => Do(new ConnectToMachine(_ids[name + ".b3"], _ids[name + ".turret"])));
            }
        }
    }

    private static readonly (int X, int Z)[] WallSpots = { (-3, 3), (6, -9), (6, 15), (18, 3) };
    private readonly List<(int X, int Z)> _wallSpots = new();

    /// <summary>
    /// What a player does between and during fights: put a destroyed wall back and press repair. Runs once a second
    /// and only spends what is in the hold.
    /// </summary>
    private void Maintain()
    {
        ResearchUpgrades();
        if (_wallSpots.Count == 0 && Repairs == RepairMode.Never) return;
        bool fighting = Net.EnemyCount > 0;
        if (!fighting)
            for (int i = 0; i < _wallsBuilt; i++)
                _w.Submit(0, new PlaceMachine(_wallSpots[i].X, 0, _wallSpots[i].Z, Machine("core:wall"), -1));
        if (Repairs == RepairMode.Never || (fighting && Repairs == RepairMode.BetweenWaves)) return;
        for (int id = 0; id < Net.MachineIdLimit; id++)
            if (Net.IsMachineAlive(id) && Net.GetMachineInfo(id).Hp < Net.MaxHp(Net.GetMachineInfo(id).Type))
                _w.Submit(0, new Repair(id));
        // The commands are applied by the next tick; rejections (no items, not damaged, cell taken) are expected and ignored.
    }

    private int _wallsBuilt;

    private static readonly string[] DefenceOrder = { "core:turret_calibration", "core:reinforced_plating", "core:rapid_loader", "core:field_repair", "core:ship_armor", "core:ship_cannon", "core:overclock", "core:alloy_refinement", "core:nanite_repair" };
    private static readonly string[] ShipOrder = { "core:turret_calibration", "core:reinforced_plating", "core:ship_armor", "core:ship_cannon", "core:rapid_loader", "core:field_repair", "core:overclock", "core:alloy_refinement", "core:nanite_repair" };
    private string[] UpgradeOrder => Path == UpgradePath.Ship ? ShipOrder : DefenceOrder;

    /// <summary>Keeps the lab busy with the next defence upgrade once Logistics is done.</summary>
    private void ResearchUpgrades()
    {
        if (!Keeping || Net.CurrentResearch >= 0 || !Net.IsResearched(_c.ResearchIndex("core:logistics"))) return;
        var queue = new List<string>();
        if (Expand) { queue.Add("core:carbon_drilling"); queue.Add("core:jump_drive"); }
        queue.AddRange(UpgradeOrder.Take(Upgrades));
        foreach (var id in queue)
        {
            int node = _c.ResearchIndex(id);
            if (Net.CanResearch(node)) { _w.Submit(0, new SetResearch(node)); return; }
        }
    }

    private void Steer((int X, int Y, int Z) target)
    {
        var p = Net.GetPlayerInfo(0);
        static int Axis(int diffUnits) => Math.Abs(diffUnits) > Units.CellUnits / 2 ? Math.Sign(diffUnits) : 0;
        var intent = (Axis(target.X * Units.CellUnits - p.X), Axis(target.Y * Units.CellUnits - p.Y), Axis(target.Z * Units.CellUnits - p.Z));
        if (intent == _intent) return;
        _intent = intent;
        Do(new SetMovement(intent.Item1, intent.Item2, intent.Item3));
    }

    /// <summary>
    /// A sensible player does not let raw ore pile up: ore is only useful as fuel for the ship refinery, so mining pauses
    /// once a small stock is in the hold (or the hold is nearly full) and resumes when the stock is used up.
    /// </summary>
    private void ManageMining()
    {
        if (_expanding) return;
        if (_ironDeposit < 0 || _step == 0) return;
        int total = Net.GetPlayerInfo(0).HoldTotal;
        int ore = Net.HoldCount(0, Item("core:iron_ore"));
        if (_mining && (ore >= OreStock || total >= PlayerRules.HoldCapacity - 8)) { Do(new StopMining()); _mining = false; }
        else if (!_mining && ore <= OreStock / 3 && total <= PlayerRules.HoldCapacity - 12 && Net.InMineRange(0, _ironDeposit)) { Do(new StartMining(_ironDeposit)); _mining = true; }
    }

    /// <summary>How much raw ore the player keeps in the hold before pausing hand mining.</summary>
    public int OreStock { get; init; } = 10;

    private int CountTurrets()
    {
        int n = 0;
        for (int id = 0; id < Net.MachineIdLimit; id++)
            if (Net.IsMachineAlive(id) && _c.Machines[Net.GetMachineInfo(id).Type].Role == MachineRole.Turret) n++;
        return n;
    }

    private void Record()
    {
        double now = _w.Tick / (double)Units.TicksPerSecond;
        _objectives.Update(Net, 0);
        foreach (var s in _objectives.States)
            if (s.Done) _report.Milestones.TryAdd(s.Objective.ToString(), now);
        foreach (var r in _c.Research)
            if (Net.IsResearched(r.Index)) _report.Milestones.TryAdd("Researched " + r.Id, now);
        if (Net.ThreatActive)
        {
            var t = Net.GetSystemThreat(0);
            if (t.WarningRemaining >= 0 && _report.FirstWaveWarning is null) _report.FirstWaveWarning = now;
            if (Net.WavesSpawned > 0 && _report.FirstWave is null) _report.FirstWave = now;
            while (_report.WaveLog.Count < Net.WavesSpawned)
                _report.WaveLog.Add(new WaveEntry(_report.WaveLog.Count, now, Net.EnemyCount, Net.MachinesLost, Net.ShipsLost, CountTurrets()));
        }
        if (Net.GetPlayerInfo(0).HoldTotal >= PlayerRules.HoldCapacity) _report.HoldFullSeconds += 1;
        if (Net.MachinesLost > 0 && _report.FirstLossWave is null) _report.FirstLossWave = (int)Net.WavesSpawned;
    }

    /// <summary>Plays until the script is done, Logistics is researched and every guided step is ticked, or the time limit is reached.</summary>
    public BalanceReport Run(int maxSeconds = 3600)
    {
        if (_steps.Count == 0) BuildScript(); // built here, not in the constructor, so initializer options such as Turrets apply
        foreach (var id in Granted.Split(',', StringSplitOptions.RemoveEmptyEntries)) Net.GrantResearch(_c.ResearchIndex(id.Trim()));
        int logistics = _c.ResearchIndex("core:logistics");
        for (int sec = 0; sec < maxSeconds; sec++)
        {
            if (_cooldown > 0) _cooldown--;
            else if (_step < _steps.Count)
            {
                if (_steps[_step]())
                {
                    _step++;
                    _cooldown = (int)Math.Round(ActionDelaySeconds);
                }
                else if (_step > 0) _report.WaitingForItemsSeconds += 1;
            }
            ManageMining();
            Maintain();
            Expansion();
            _w.Step(Units.TicksPerSecond);
            Record();
            if (Trace is not null && sec % 30 == 0)
            {
                var hold = string.Join(" ", _c.Items.Select(i => $"{i.Id.Split(':')[1]}={Net.HoldCount(0, i.Index)}").Where(s => !s.EndsWith("=0")));
                Trace.WriteLine($"{sec / 60}:{sec % 60:00} step={_step}/{_steps.Count} ship=({Net.GetPlayerInfo(0).X / Units.CellUnits},{Net.GetPlayerInfo(0).Y / Units.CellUnits},{Net.GetPlayerInfo(0).Z / Units.CellUnits}) hold[{Net.GetPlayerInfo(0).HoldTotal}] {hold} machines={Net.MachineCount} lost={Net.MachinesLost} waves={Net.WavesSpawned} delivered={Net.DepotDelivered} iron_left={(_ironDeposit >= 0 ? Net.DepositRemaining(_ironDeposit) : -1)}");
            }
            if (StopAtFirstLoss && _report.FirstLossWave is not null) break;
            if (_step >= _steps.Count && Net.IsResearched(logistics) && (_objectives.Current is null || (_objectives.Current == Objective.Expand && !Expand)))
            {
                if (!_report.Finished) { _report.Finished = true; _finishedAt = sec; }
                if (sec - _finishedAt >= ObserveAfterSeconds) break;
            }
        }
        _report.Seconds = _w.Tick / (double)Units.TicksPerSecond;
        _report.MachinesLost = Net.MachinesLost;
        _report.Waves = Net.WavesSpawned;
        if (Expand && _remoteDeposit >= 0) { _report.RemoteDelivered = RemoteDelivered; _report.RemoteIronLeft = Net.DepositRemaining(_remoteDeposit); }
        _report.WallsLost = Net.WallsLost;
        _report.EnemiesKilled = Net.EnemiesKilled;
        if (!_report.Finished) _report.Stuck = $"script step {_step}/{_steps.Count}, next objective {_objectives.Current}";
        _w.ValidateInvariants();
        return _report;
    }

    public static string FormatWaves(BalanceReport r)
    {
        var lines = new List<string> { "| Wave | Time | Enemies | Machines lost so far | Ships lost so far | Turrets |", "| --- | --- | --- | --- | --- | --- |" };
        foreach (var w in r.WaveLog)
            lines.Add($"| {w.Index + 1} | {(int)w.Seconds / 60}:{(int)w.Seconds % 60:00} | {w.Enemies} | {w.MachinesLost} | {w.ShipsLost} | {w.Turrets} |");
        return string.Join("\n", lines);
    }

    public static string Format(BalanceReport r)
    {
        static string T(double s) => $"{(int)s / 60}:{(int)s % 60:00}";
        var lines = new List<string> { "| Milestone | Time |", "| --- | --- |" };
        foreach (var (k, v) in r.Milestones.OrderBy(m => m.Value)) lines.Add($"| {k} | {T(v)} |");
        if (r.FirstWaveWarning is double w) lines.Add($"| First wave warning | {T(w)} |");
        if (r.FirstWave is double f) lines.Add($"| First wave arrives | {T(f)} |");
        lines.Add($"| Hold full (total) | {r.HoldFullSeconds:F0} s |");
        lines.Add($"| Waiting for items (total) | {r.WaitingForItemsSeconds:F0} s |");
        lines.Add($"| Waves seen / enemies killed | {r.Waves} / {r.EnemiesKilled} |");
        lines.Add($"| First machine lost during wave | {(r.FirstLossWave is int fl ? fl.ToString() : "none")} |");
        lines.Add($"| Walls lost | {r.WallsLost} |");
        foreach (var n in r.Notes) lines.Add($"| Expansion | {n} |");
        if (r.RemoteIronLeft >= 0) lines.Add($"| Remote deposit | {r.RemoteIronLeft} ore left at the end, {r.RemoteDelivered} items delivered by depots since |");
        lines.Add($"| Machines lost | {r.MachinesLost} |");
        lines.Add($"| Run length | {T(r.Seconds)}{(r.Finished ? "" : " (not finished: " + r.Stuck + ")")} |");
        return string.Join("\n", lines);
    }
}
