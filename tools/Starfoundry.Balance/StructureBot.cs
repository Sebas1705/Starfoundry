using Starfoundry.Sim;
using Starfoundry.Ui;

namespace Starfoundry.Balance;

/// <summary>What one structure-campaign run measured. Times are simulated seconds since the start of the run.</summary>
public sealed class StructureBotReport
{
    /// <summary>First time each milestone happened (seconds), in the order they happened.</summary>
    public List<(string Name, double Seconds)> Timeline { get; } = new();

    /// <summary>Every point where the bot could not do something (rejected command, unaffordable for too long, timeout), with the time.</summary>
    public List<string> Stuck { get; } = new();

    public OutcomeKind Outcome { get; set; } = OutcomeKind.Playing;
    public OutcomeReason Reason { get; set; } = OutcomeReason.None;
    public double Seconds { get; set; }
    public int WavesSurvived { get; set; }
    public long MachinesLost { get; set; }
    public long ModulesLost { get; set; }
    public long MinedByHand { get; set; }
    public long MinedByDrones { get; set; }
    public string End { get; set; } = "";

    /// <summary>Seconds (counted once a second) in which a structure with a reactor had no hydrogen to burn and ran on its emergency output.</summary>
    public int EmergencySeconds { get; set; }

    /// <summary>Turrets standing, and modules and machines lost so far, when the win was reached (how hard the waves were on this defence).</summary>
    public int TurretsAtWin { get; set; }
    public long ModulesLostAtWin { get; set; }
    public long MachinesLostAtWin { get; set; }

    public bool Has(string milestone) => Timeline.Any(t => t.Name == milestone);

    public double At(string milestone) => Timeline.First(t => t.Name == milestone).Seconds;

    public static string Clock(double s) => $"{(int)s / 60}:{(int)s % 60:00}";

    public string Format()
    {
        var lines = new List<string> { "| Minute | Milestone |", "| --- | --- |" };
        foreach (var (n, s) in Timeline) lines.Add($"| {Clock(s)} | {n} |");
        lines.Add("");
        lines.Add($"Emergency power: {EmergencySeconds} s.");
        if (TurretsAtWin > 0) lines.Add($"At the win: {TurretsAtWin} turrets standing, {ModulesLostAtWin} modules and {MachinesLostAtWin} machines lost so far.");
        lines.Add($"Flying: {StructureBot.FlightSeconds:F0} s of game time.");
        lines.Add($"Outcome: {Outcome} ({Reason}) after {Clock(Seconds)}; waves survived {WavesSurvived}; modules lost {ModulesLost}; machines lost {MachinesLost}; mined by hand {MinedByHand}, by drones {MinedByDrones}.");
        if (End.Length > 0) lines.Add("End: " + End);
        if (Stuck.Count > 0)
        {
            lines.Add("");
            lines.Add("Stuck points:");
            foreach (var s in Stuck) lines.Add("- " + s);
        }
        return string.Join("\n", lines);
    }
}

/// <summary>Raised inside the bot's own loop when the game is decided (won or lost) or the time limit is reached.</summary>
internal sealed class GameEnded : Exception
{
    public GameEnded(string why) : base(why) { }
}

/// <summary>
/// A scripted player for the structure campaign (ships, stations, power, drones, structure defence). It uses only sim commands (never
/// <c>GiveItems</c>) and plays like a patient human: <see cref="ActionDelaySeconds"/> of game time pass after every placement.
/// The plan is a list of named goals (see <c>StructureBot.Goals.cs</c>); each one waits for the items it needs while a once-a-second
/// maintenance step keeps hand mining, the fuel stock and the record going. Layouts live in <c>StructureBot.Layouts.cs</c>.
/// </summary>
public sealed partial class StructureBot
{
    private const int Tps = Units.TicksPerSecond;
    private const int Cell = Units.CellUnits;

    private readonly ContentDb _c;
    private readonly SimWorld _w;
    private readonly StructureBotReport _report = new();
    private readonly HashSet<string> _seen = new();
    private int _maxSeconds;

    /// <summary>Game seconds that pass after every placement or choice, standing in for aiming and thinking.</summary>
    public double ActionDelaySeconds { get; init; } = 6;

    /// <summary>Waves to survive for the win (the game's <see cref="GoalSettings"/> default).</summary>
    public int GoalWaves { get => _goalWaves ?? Net.Rules.GoalWaves; init => _goalWaves = value; }
    private int? _goalWaves;

    /// <summary>When set, goal changes, notable events and a status line every 30 simulated seconds are written here.</summary>
    public TextWriter? Trace { get; init; }

    /// <summary>Keep playing after the win (until the plan is done, the ship is lost or the time is up) to see how far research gets.</summary>
    public bool ContinueAfterVictory { get; init; }

    private const string VictoryMilestone = "VICTORY: waves survived with a station standing";

    /// <summary>Waves can be switched off to study the economy alone.</summary>
    public bool Threats { get; init; } = true;

    public StructureBot(ContentDb content, ulong seed = 20261121UL, bool threats = true, WorldRules? rules = null)
    {
        _c = content;
        Threats = threats;
        _w = SimWorld.CreateGalaxy(content, GameMode.Campaign, seed, threats, stationRules: false, structures: true, rules: rules);
        _w.Network.EnsurePlayer(0);
        _ship = Net.ShipStructure(0);
    }

    /// <summary>Plays a story level: the world is made by <see cref="SimWorld.CreateScenario(ContentDb, ScenarioDef, ulong, WorldRules?)"/> and the
    /// game is decided by the scenario's goals (<see cref="ScenarioProgress"/>). Only the levels listed in <c>StructureBot.Scenario.cs</c> have a plan.</summary>
    public StructureBot(ContentDb content, ScenarioDef scenario, ulong seed = 0)
    {
        _c = content;
        _scenario = scenario;
        _progress = new ScenarioProgress(content, scenario);
        _w = SimWorld.CreateScenario(content, scenario, seed);
        _w.Network.EnsurePlayer(0);
        _ship = Net.ShipStructure(0);
        Threats = _w.Network.ThreatActive;
    }

    private readonly ScenarioDef? _scenario;
    private readonly ScenarioProgress? _progress;

    /// <summary>The world the bot plays in, for inspecting or saving it after a run.</summary>
    public SimWorld World => _w;

    private BeltNetwork Net => _w.Network;
    private int Item(string id) => _c.ItemIndex(id);
    private int Machine(string id) => _c.MachineIndex(id);
    private int Recipe(string id) => _c.RecipeIndex(id);
    private double Now => _w.Tick / (double)Tps;
    private int NowSeconds => (int)Now;

    private int _ship = -1;
    private int _station = -1;

    // ---- logging ---------------------------------------------------------------------------------------

    private void Say(string text) => Trace?.WriteLine($"[{StructureBotReport.Clock(Now)}] {text}");

    private void Milestone(string name)
    {
        if (!_seen.Add(name)) return;
        _report.Timeline.Add((name, Now));
        Say("MILESTONE " + name);
    }

    private void Stuck(string text)
    {
        string line = $"{StructureBotReport.Clock(Now)} {text}";
        if (_report.Stuck.Contains(line)) return;
        _report.Stuck.Add(line);
        Say("STUCK " + text);
    }

    private string _goal = "start";

    private void Goal(string name)
    {
        _goal = name;
        Say("GOAL " + name);
    }

    // ---- time -----------------------------------------------------------------------------------------

    /// <summary>Lets <paramref name="seconds"/> of game time pass, running the maintenance step every second.</summary>
    private void Wait(double seconds)
    {
        int ticksLeft = (int)Math.Round(seconds * Tps);
        while (ticksLeft > 0)
        {
            int n = Math.Min(Tps, ticksLeft);
            _w.Step(n);
            ticksLeft -= n;
            EverySecond();
        }
    }

    /// <summary>Waits until the condition holds (checked every second). Returns false after <paramref name="maxSeconds"/>.</summary>
    private bool WaitUntil(Func<bool> condition, int maxSeconds)
    {
        for (int i = 0; i < maxSeconds; i++)
        {
            if (condition()) return true;
            Wait(1);
        }
        return condition();
    }

    private CommandResult Cmd(Command c)
    {
        _w.Submit(0, c);
        _w.Step();
        return _w.LastResults.Last();
    }

    /// <summary>A command that costs the player a moment: the game clock moves on afterwards.</summary>
    private CommandResult Act(Command c)
    {
        var r = Cmd(c);
        Wait(ActionDelaySeconds);
        return r;
    }

    private bool Failed(CommandResult r, string what)
    {
        if (r.Ok) return false;
        Stuck($"{what} was rejected: {r.Reject}");
        return true;
    }

    // ---- once a second --------------------------------------------------------------------------------

    private bool _inMaintenance;
    private int _lastHydrogen = -1;
    private int _mineTarget = -1;
    private int _lastWave;

    /// <summary>How much raw iron ore the player keeps in the hold before pausing hand mining.</summary>
    public int OreStock { get; init; } = 40;
    private int _lastWarningWave = -1;

    private void EverySecond()
    {
        Record();
        if (Trace is not null && NowSeconds % 60 == 0) StatusLine();
        var outcome = _progress is null ? GameOutcome.Evaluate(Net, 0, new GoalSettings(GoalWaves)) : ScenarioOutcome();
        if (outcome.Kind == OutcomeKind.Won && !_seen.Contains(VictoryMilestone))
        {
            _report.Outcome = OutcomeKind.Won;
            _report.Reason = outcome.Reason;
            for (int id = 0; id < Net.MachineIdLimit; id++)
            {
                if (!Net.IsMachineAlive(id)) continue;
                var role = _c.Machines[Net.GetMachineInfo(id).Type].Role;
                if (role == MachineRole.Turret) _report.TurretsAtWin++;
            }
            _report.ModulesLostAtWin = Net.ModulesLost;
            _report.MachinesLostAtWin = Net.MachinesLost;
            Milestone(VictoryMilestone);
        }
        if (outcome.Kind == OutcomeKind.Lost)
        {
            _report.Outcome = OutcomeKind.Lost;
            _report.Reason = outcome.Reason;
            Milestone("DEFEAT: ship lost");
            throw new GameEnded("Lost");
        }
        if (outcome.Kind == OutcomeKind.Won && !ContinueAfterVictory) throw new GameEnded("Won");
        if (NowSeconds >= _maxSeconds) throw new GameEnded("time limit");
        if (_inMaintenance) return;
        _inMaintenance = true;
        try { Maintain(); }
        finally { _inMaintenance = false; }
    }

    /// <summary>Hand mining keeps going, fuel is watched, a lost station is noted.</summary>
    private void Maintain()
    {
        if (_flying > 0) return;
        // A build trip took the ship away from its deposit: go back to it.
        if (_mineTarget >= 0 && !Net.InMineRange(0, _mineTarget) && Net.CanFly(0) && Net.DepositRemaining(_mineTarget) > 0)
        {
            var (dx, dy, dz) = DepositCentre(_mineTarget);
            FlyTo(dx, dy, dz, 3L * Cell);
        }
        // A sensible player does not let raw ore pile up in the hold: hand mining of the home iron pauses at a stock the lines use soon.
        int ore = Have("core:iron_ore");
        if (_mineTarget == _homeDeposit && _homeDeposit >= 0 && !_orePaused && ore >= OreStock) { Cmd(new StopMining()); _orePaused = true; }
        else if (_orePaused && ore <= OreStock / 3) _orePaused = false;
        bool paused = _orePaused && _mineTarget == _homeDeposit;
        if (_mineTarget >= 0 && !paused && Net.InMineRange(0, _mineTarget) && Net.MiningDeposit(0) != _mineTarget && Net.DepositRemaining(_mineTarget) > 0)
            Cmd(new StartMining(_mineTarget));
        HoldHygiene();
        ManageStock();
        ManageDrones();
        if (_iceLoopBuilt && !_refuelling && _gatherDepth == 0 && Hydrogen < 12 && Net.CurrentSystem(0) == 0 && (Net.EnemyCount == 0 || Hydrogen < 6)) Refuel();
        if (_station >= 0 && !Net.Structures().Any(s => s.Id == _station))
        {
            Stuck("the station was destroyed");
            _station = -1;
        }
    }

    /// <summary>Debug listing of every machine (for finding what starves).</summary>
    private void DumpMachines()
    {
        for (int id = 0; id < Net.MachineIdLimit; id++)
        {
            if (!Net.IsMachineAlive(id)) continue;
            var m = Net.GetMachineInfo(id);
            Trace!.WriteLine($"   machine {id} {_c.Machines[m.Type].Id} st{m.Structure} ({m.X},{m.Y},{m.Z}) recipe {(m.Recipe >= 0 ? _c.Recipes[m.Recipe].Id : "-")} crafting {m.Crafting} in {m.InputItems} out {m.OutputItems}");
        }
    }

    private void StatusLine()
    {
        if (NowSeconds == 40 * 60 && Environment.GetEnvironmentVariable("SF_DUMP") == "1") DumpMachines();
        var p = Net.GetPlayerInfo(0);
        var hold = string.Join(" ", _c.Items.Select(i => $"{i.Id.Split(':')[1]}={Net.HoldCount(0, i.Index)}").Where(s => !s.EndsWith("=0")));
        var power = Net.ShipPower(0);
        string st = _station >= 0 && Net.StructureExists(_station) ? $" station={Net.PowerStatus(_station).SatisfactionPermille}" : "";
        if (Net.CurrentResearch >= 0)
            st += " research " + _c.Research[Net.CurrentResearch].Id.Replace("core:", "") + " " + string.Join("/", Net.ResearchCost(Net.CurrentResearch).Select(c => $"{Net.ResearchProgress(Net.CurrentResearch, c.Item)}of{c.Count}"));
        if (_lab >= 0 && Net.IsMachineAlive(_lab)) st += $" lab[in {Net.GetMachineInfo(_lab).InputItems}]";
        if (_dronesOnIron || _dronesOnIce) st += $" drones[iron {(_station >= 0 && Net.StructureExists(_station) ? Net.AssignedDeposit(_station) : -9)} paused={_dronesPaused} ice {Net.AssignedDeposit(_ship)} paused={_icePaused}] mining={Net.MiningDeposit(0)} target={_mineTarget} orePaused={_orePaused} sys={Net.CurrentSystem(0)}";
        Trace!.WriteLine($"[{StructureBotReport.Clock(Now)}] {_goal} | ship=({p.X / Cell},{p.Y / Cell},{p.Z / Cell}) hold[{p.HoldTotal}/{Net.HoldCapacity(0)}] {hold} | power {power.Production}/{power.Demand} sat={power.SatisfactionPermille}{st} | "
            + $"machines={Net.MachineCount} lost={Net.MachinesLost} waves={Net.WavesSpawned} enemies={Net.EnemyCount} res={Net.ResearchedCount}");
    }

    /// <summary>Notes the first time things happen. Every check is a read-only look at the world.</summary>
    private long _lastModulesLost, _lastMachinesLost;

    private string ModuleList(int structure) => Net.StructureExists(structure) && structure != 0
        ? string.Join(" ", Net.GetStructure(structure).Modules.Select(m => $"{m.Value.ToString().ToLowerInvariant()}{m.Key}:{Net.ModuleHp(structure, m.Key.Mx, m.Key.My, m.Key.Mz)}"))
        : "gone";

    private void Record()
    {
        if (Net.ModulesLost != _lastModulesLost || Net.MachinesLost != _lastMachinesLost)
        {
            Say($"LOSS modules lost {_lastModulesLost}->{Net.ModulesLost}, machines lost {_lastMachinesLost}->{Net.MachinesLost}, ship hp {Net.StructureHpFraction(_ship)} permille, enemies {Net.EnemyCount}; ship modules [{ModuleList(_ship)}]; station [{(_station >= 0 ? ModuleList(_station) : "none")}]");
            _lastModulesLost = Net.ModulesLost; _lastMachinesLost = Net.MachinesLost;
        }
        if (Net.HoldCount(0, Item("core:iron_ingot")) > 0) Milestone("First iron ingot in the hold");
        if (Net.HoldCount(0, Item("core:iron_plate")) > 0) Milestone("First iron plate in the hold");
        int hydrogen = Net.HoldCount(0, Item("core:hydrogen"));
        if (_lastHydrogen >= 0 && hydrogen > _lastHydrogen) Milestone("Hydrogen loop: first hydrogen made from ice");
        _lastHydrogen = hydrogen;
        foreach (var s in Net.Structures())
        {
            if (s.Owner != 0 || s.Kind != StructureKind.Station) continue;
            Milestone("Station founded");
            var ps = Net.PowerStatus(s.Id);
            if (ps.Burning && ps.SatisfactionPermille >= 1000 && Net.MachineCount > 0 && StationHasMachine(s.Id)) Milestone("Station powered with machines running");
        }
        foreach (var r in _c.Research)
        {
            if (Net.IsResearched(r.Index) && !r.Id.Contains("never")) Milestone("Researched " + r.Id.Replace("core:", ""));
        }
        if (Net.ResearchedCount > 0) Milestone("First research finished");
        for (int id = 0; id < Net.MachineIdLimit; id++)
        {
            if (!Net.IsMachineAlive(id)) continue;
            var m = Net.GetMachineInfo(id);
            var def = _c.Machines[m.Type];
            if (def.Role == MachineRole.Lab) Milestone("Laboratory built");
            if (def.Role == MachineRole.Turret)
            {
                Milestone("First turret built");
                if (Net.MachineInCount(id, Item("core:ammo")) > 0) Milestone("First turret loaded with ammo");
            }
            if (def.Role == MachineRole.Repairer) Milestone("First repair bay built");
            if (def.Role == MachineRole.Crafter && def.Id == "core:hydrogen_cracker") Milestone("Hydrogen cracker built");
        }
        foreach (var s in Net.Structures())
        {
            if (s.Owner != 0) continue;
            if (s.Modules.Values.Any(k => k == ModuleKind.Defence)) Milestone("First defence module built");
            if (s.Modules.Values.Any(k => k == ModuleKind.DroneBay)) Milestone("First drone bay built");
            if (Net.AssignedDeposit(s.Id) >= 0) Milestone("Drones mining a deposit");
        }
        if (Net.ThreatActive)
        {
            var t = Net.GetSystemThreat(0);
            if (t.WarningRemaining >= 0 && _lastWarningWave != (int)Net.WavesSpawned)
            {
                _lastWarningWave = (int)Net.WavesSpawned;
                Milestone($"Wave {Net.WavesSpawned + 1} warning");
            }
            int survived = GameOutcome.WavesSurvived(Net);
            while (_lastWave < survived) Milestone($"Wave {++_lastWave} survived");
        }
        bool noFuel = false;
        foreach (var s in Net.Structures())
            if (s.Owner == 0 && s.Modules.Values.Any(k => k == ModuleKind.Reactor) && !Net.PowerStatus(s.Id).Burning) noFuel = true;
        if (noFuel) { _report.EmergencySeconds++; Milestone("Emergency power: a reactor has no hydrogen to burn"); }
        if (Net.MinedByDrones > 0) Milestone("Drones delivered ore");
        if (Net.CurrentSystem(0) != 0 && Net.CurrentSystem(0) >= 0) Milestone($"Arrived in system {Net.CurrentSystem(0)}");
    }

    private bool StationHasMachine(int structure)
    {
        for (int id = 0; id < Net.MachineIdLimit; id++)
            if (Net.IsMachineAlive(id) && Net.GetMachineInfo(id).Structure == structure) return true;
        return false;
    }

    // ---- running ----------------------------------------------------------------------------------------

    /// <summary>Plays until the game is decided or the time limit (simulated seconds) is reached.</summary>
    public StructureBotReport Run(int maxSeconds = 7200)
    {
        _maxSeconds = maxSeconds;
        try
        {
            if (_progress is null) PlayCampaign(); else PlayScenario();
            _report.End = "the plan ran out before the game was decided";
            // Keep the base alive until the game is decided.
            Goal("hold the line until the waves are done");
            while (true) Wait(1);
        }
        catch (GameEnded e)
        {
            _report.End = e.Message;
        }
        _report.Seconds = Now;
        _report.WavesSurvived = GameOutcome.WavesSurvived(Net);
        _report.MachinesLost = Net.MachinesLost;
        _report.ModulesLost = Net.ModulesLost;
        _report.MinedByHand = Net.MinedByHand;
        _report.MinedByDrones = Net.MinedByDrones;
        if (_report.Outcome == OutcomeKind.Playing && _report.End == "time limit") Stuck("the time limit was reached before the game was decided");
        _w.ValidateInvariants();
        return _report;
    }
}
