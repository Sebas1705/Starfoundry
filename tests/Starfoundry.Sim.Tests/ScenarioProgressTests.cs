using Starfoundry.Sim;
using Starfoundry.Ui;
using Xunit;

namespace Starfoundry.Sim.Tests;

/// <summary>The goals engine: goals done and failed with their times, story beats fired once, the outcome, and the stars (scripted sim worlds).</summary>
public class ScenarioProgressTests
{
    private const int Tps = Units.TicksPerSecond;
    private const ulong Seed = 20261121UL;
    private static readonly ContentDb C = TestContent.Core;
    private static readonly int Ore = C.ItemIndex("core:iron_ore");
    private static readonly int Hydrogen = C.ItemIndex("core:hydrogen");
    private static readonly int Ammo = C.ItemIndex("core:ammo");
    private static readonly int Turret = C.MachineIndex("core:turret");

    private const string Head =
        "{\"schema\":1,\"id\":\"t\",\"order\":1,\"name_es\":\"t\",\"name_en\":\"t\",\"description_es\":\"t\",\"description_en\":\"t\",\"briefing_es\":\"t\",\"briefing_en\":\"t\"";

    private static string Goal(string id, string type, string fields = "", string limit = "") =>
        $"{{\"id\":\"{id}\",\"type\":\"{type}\"{fields}{limit},\"text_es\":\"es {id}\",\"text_en\":\"en {id}\"}}";

    private static ScenarioDef Def(string[] primary, string[]? secondary = null, string? story = null, string? stars = null, string? rules = null) =>
        ScenarioCatalog.Parse(C, Head + (rules is null ? "" : ",\"rules\":" + rules) + ",\"goals\":{\"primary\":[" + string.Join(",", primary) + "],\"secondary\":["
            + string.Join(",", secondary ?? Array.Empty<string>()) + "]}" + (story is null ? "" : ",\"story\":" + story) + (stars is null ? "" : ",\"stars\":" + stars) + "}");

    private static string Beat(string trigger, string text = "x") => $"{{\"trigger\":\"{trigger}\",\"text_es\":\"es {text}\",\"text_en\":\"en {text}\"}}";

    private static SimWorld World(bool threats = false) =>
        SimWorld.CreateGalaxy(TestContent.CoreWithoutStarterShip, GameMode.Sandbox, Seed, threats: threats, stationRules: false, structures: true);

    private static void Ok(SimWorld w, Command c)
    {
        w.Submit(0, c);
        w.Step();
        var r = w.LastResults.Single();
        Assert.True(r.Ok, $"{c} was rejected: {r.Reject}");
    }

    private static int Place(SimWorld w, Command c)
    {
        w.Submit(0, c);
        w.Step();
        var r = w.LastResults.Single();
        Assert.True(r.Ok, $"{c} was rejected: {r.Reject}");
        return r.CreatedMachines.Count > 0 ? r.CreatedMachines[0] : r.Created.Count > 0 ? r.Created[0] : -1;
    }

    /// <summary>A powered ship with a cockpit, workshop, defence module, engine and two ammo-fed turrets.</summary>
    private static (SimWorld W, int Ship) ArmedShip(bool threats = false)
    {
        var w = World(threats);
        w.Network.EnsurePlayer(0);
        int s = w.Network.CreateStructure(0, StructureKind.Ship, 0, 0, 0);
        w.Network.AddStructureModuleUnchecked(s, 0, 0, 0, ModuleKind.Cockpit);
        w.Network.AddStructureModuleUnchecked(s, 1, 0, 0, ModuleKind.Workshop);
        w.Network.AddStructureModuleUnchecked(s, 2, 0, 0, ModuleKind.Defence);
        w.Network.AddStructureModuleUnchecked(s, 3, 0, 0, ModuleKind.Engine);
        w.Network.AddStructureModuleUnchecked(s, 20, 0, 0, ModuleKind.Reactor);
        w.Network.AddStructureModuleUnchecked(s, 4, 0, 0, ModuleKind.Cargo);
        w.Network.AddStructureModuleUnchecked(s, 5, 0, 0, ModuleKind.Cargo);
        w.Network.GiveItems(0, Hydrogen, 60);
        for (int i = 0; i < 2; i++)
        {
            int belt = Place(w, new PlaceSegmentIn(s, 12, 1, i, Dir.PosX, 4, BeltTier.Mk2));
            Ok(w, new SetSource(belt, new ushort[] { (ushort)Ammo }));
            int turret = Place(w, new PlaceMachineIn(s, 16, 1, i, Turret, -1));
            Ok(w, new ConnectToMachine(belt, turret));
        }
        return (w, s);
    }

    private static void ClearWaves(SimWorld w, int count)
    {
        for (int wave = 0; wave < count; wave++)
        {
            w.Network.ScheduleWave(0);
            for (int i = 0; i < 20_000 && (w.Network.EnemyCount > 0 || w.Network.WavesSpawned <= wave); i++) w.Step();
            Assert.Equal(0, w.Network.EnemyCount);
        }
    }

    /// <summary>Forces one more wave and steps until it has been cleared.</summary>
    private static void ClearNextWave(SimWorld w)
    {
        long before = w.Network.WavesSpawned;
        w.Network.ScheduleWave(0);
        for (int i = 0; i < 20_000 && (w.Network.EnemyCount > 0 || w.Network.WavesSpawned <= before); i++) w.Step();
        Assert.Equal(0, w.Network.EnemyCount);
        Assert.Equal(before + 1, w.Network.WavesSpawned);
    }

    private static int Floor(int units) => (int)Math.Floor(units / (double)Units.CellUnits);

    private static void FoundStation(SimWorld w)
    {
        var p = w.Network.GetPlayerInfo(0);
        Ok(w, new FoundStation(Floor(p.X), Floor(p.Y), Floor(p.Z) - 8));
    }

    // ---- goals --------------------------------------------------------------------------------------

    [Fact]
    public void A_fresh_world_has_every_goal_pending_and_the_game_playing()
    {
        var (w, _) = ArmedShip();
        var p = new ScenarioProgress(C, Def(new[] { Goal("a", "found_stations", ",\"count\":1"), Goal("b", "research", ",\"node\":\"core:logistics\"") },
            new[] { Goal("c", "keep_alive", ",\"kind\":\"core\"") }));
        Assert.Empty(p.Update(w.Network, 0)); // no story
        Assert.Equal(OutcomeKind.Playing, p.Outcome);
        Assert.Equal(OutcomeReason.None, p.Reason);
        Assert.Equal(0, p.Stars);
        Assert.All(p.Goals, g => Assert.Equal(GoalStatus.Pending, g.Status));
        Assert.Equal(new[] { true, true, false }, p.Goals.Select(g => g.Primary));
        Assert.Equal(2, p.Primary.Count());
        Assert.Single(p.Secondary);
        Assert.Null(p.DecidedSeconds);
        Assert.Equal("en a", p.Find("a")!.Text(false));
        Assert.Equal("es a", p.Find("a")!.Text(true));
        Assert.Equal("0/1", p.Find("a")!.ProgressText);
        Assert.Equal("", p.Find("b")!.ProgressText);
        Assert.Null(p.Find("zzz"));
        Assert.Equal((0, 1), p.SecondaryCount);
    }

    [Fact]
    public void Each_goal_type_completes_from_its_own_counter_and_records_when()
    {
        var (w, s) = ArmedShip();
        var def = Def(new[]
        {
            Goal("research", "research", ",\"node\":\"core:logistics\""),
            Goal("station", "found_stations", ",\"count\":1"),
            Goal("module", "build_module", ",\"kind\":\"reactor\",\"count\":1"),
            Goal("mine", "mine", ",\"item\":\"core:iron_ore\",\"count\":3"),
            Goal("make", "produce", ",\"item\":\"core:iron_ore\",\"count\":3"),
            Goal("visit", "visit_systems", ",\"count\":2"),
        });
        var p = new ScenarioProgress(C, def);
        p.Update(w.Network, 0);
        Assert.Equal(new[] { 0L, 0L, 0L, 0L, 0L, 1L }, p.Goals.Select(g => g.Current));

        w.Network.GrantResearch(C.ResearchIndex("core:logistics"));
        w.Step(Tps);
        p.Update(w.Network, 0);
        Assert.Equal(GoalStatus.Done, p.Find("research")!.Status);
        Assert.InRange(p.Find("research")!.CompletedSeconds!.Value, 1, 3);

        FoundStation(w);
        int station = w.Network.Structures().Single(x => x.Kind == StructureKind.Station).Id;
        Ok(w, new PlaceModuleIn(station, 1, 0, 0, ModuleKind.Reactor));
        p.Update(w.Network, 0);
        Assert.Equal(GoalStatus.Done, p.Find("station")!.Status);
        Assert.Equal(GoalStatus.Done, p.Find("module")!.Status);
        Assert.Equal(OutcomeKind.Playing, p.Outcome);

        // Drones: a bay and a deposit give the mined and produced counts.
        w.Network.AddStructureModuleUnchecked(s, 10, 0, 0, ModuleKind.DroneBay);
        int dep = w.Network.AddDeposit(Ore, 1000, 0, 0, 0);
        Ok(w, new AssignDrones(s, dep));
        w.Step(2 * 36);
        p.Update(w.Network, 0);
        Assert.InRange(p.Find("mine")!.Current, 1, 2);
        Assert.Equal(GoalStatus.Pending, p.Find("mine")!.Status);
        w.Step(3 * 36);
        p.Update(w.Network, 0);
        Assert.Equal(GoalStatus.Done, p.Find("mine")!.Status);
        Assert.Equal(GoalStatus.Done, p.Find("make")!.Status);
        Assert.Equal("3/3", p.Find("mine")!.ProgressText);

        // A jump visits a second system (the sandbox world jumps along a lane for free).
        Ok(w, new Jump(w.Network.Galaxy!.Systems[0].Lanes[0]));
        w.Step(ExplorationRules.JumpChargeTicks);
        p.Update(w.Network, 0);
        Assert.Equal(GoalStatus.Done, p.Find("visit")!.Status);
        Assert.Equal(OutcomeKind.Won, p.Outcome);
        Assert.Equal(OutcomeReason.GoalsDone, p.Reason);
        Assert.NotNull(p.DecidedSeconds);
    }

    [Fact]
    public void A_goal_that_is_done_stays_done_and_its_time_does_not_move()
    {
        var (w, _) = ArmedShip();
        var p = new ScenarioProgress(C, Def(new[] { Goal("station", "found_stations", ",\"count\":1"), Goal("never", "research", ",\"node\":\"core:logistics\"") }));
        FoundStation(w);
        p.Update(w.Network, 0);
        double at = p.Find("station")!.CompletedSeconds!.Value;
        int station = w.Network.Structures().Single(x => x.Kind == StructureKind.Station).Id;
        w.Network.HitModule(station, 0, 0, 0, 99_999); // the station falls: the goal stays done
        w.Step(Tps * 3);
        p.Update(w.Network, 0);
        Assert.Equal(GoalStatus.Done, p.Find("station")!.Status);
        Assert.Equal(at, p.Find("station")!.CompletedSeconds);
        Assert.Equal(1, p.Find("station")!.Current);
    }

    // ---- surviving waves ----------------------------------------------------------------------------

    [Fact]
    public void Surviving_waves_uses_the_goal_or_the_rules_goal_waves()
    {
        var (w, _) = ArmedShip(threats: true);
        w.Step(Tps);
        var explicitGoal = new ScenarioProgress(C, Def(new[] { Goal("w", "survive_waves", ",\"count\":2") }));
        var fromRules = new ScenarioProgress(C, Def(new[] { Goal("w", "survive_waves") }));
        ClearNextWave(w);
        explicitGoal.Update(w.Network, 0); fromRules.Update(w.Network, 0);
        Assert.Equal((1L, 2L), (explicitGoal.Find("w")!.Current, explicitGoal.Find("w")!.Target));
        Assert.Equal((1L, (long)w.Network.Rules.GoalWaves), (fromRules.Find("w")!.Current, fromRules.Find("w")!.Target));
        Assert.Equal(OutcomeKind.Playing, explicitGoal.Outcome);
        ClearNextWave(w);
        explicitGoal.Update(w.Network, 0);
        Assert.Equal(OutcomeKind.Won, explicitGoal.Outcome);
        Assert.Equal(OutcomeReason.WavesSurvived, explicitGoal.Reason);
        Assert.Equal(GoalStatus.Pending, fromRules.Find("w")!.Status);
    }

    [Fact]
    public void The_campaign_scenario_matches_the_standard_outcome_rule_for_the_waves_and_the_station()
    {
        var def = TestContent.Scenarios.Get("campaign");
        var (w, _) = ArmedShip(threats: true);
        w.Step(Tps);
        ClearWaves(w, 2);
        var rules = new WorldRules { GoalWaves = 2 };
        // The standard rule needs the waves and a station; the scenario also asks for a jump, so it is a step stricter.
        Assert.Equal(OutcomeKind.Playing, GameOutcome.Evaluate(w.Network, 0, rules).Kind);
        FoundStation(w);
        Assert.Equal(OutcomeKind.Playing, GameOutcome.Evaluate(w.Network, 0, rules).Kind); // the standard rule also needs a completed jump now
        var progress = new ScenarioProgress(C, def);
        progress.Update(w.Network, 0);
        Assert.Equal(GoalStatus.Pending, progress.Find("jump")!.Status);
        Assert.Equal(GoalStatus.Done, progress.Find("found_station")!.Status);
        Assert.Equal(OutcomeKind.Playing, progress.Outcome);
        Ok(w, new Jump(w.Network.Galaxy!.Systems[0].Lanes[0]));
        w.Step(ExplorationRules.JumpChargeTicks);
        progress.Update(w.Network, 0);
        Assert.Equal(2L, progress.Find("jump")!.Current);
        Assert.Equal(OutcomeKind.Won, GameOutcome.Evaluate(w.Network, 0, rules).Kind); // waves, station and jump: the same rule as the scenario
        // The campaign asks for the rules' goal waves (10 on default rules), which this world has not cleared.
        Assert.Equal(10L, progress.Find("hold_the_line")!.Target);
        Assert.Equal(OutcomeKind.Playing, progress.Outcome);
    }

    // ---- failing and losing -------------------------------------------------------------------------

    [Fact]
    public void A_primary_time_limit_that_passes_loses_and_a_secondary_one_only_fails()
    {
        var (w, _) = ArmedShip();
        var def = Def(new[] { Goal("late", "research", ",\"node\":\"core:logistics\"", ",\"time_limit_seconds\":60") },
            new[] { Goal("soon", "found_stations", ",\"count\":1", ",\"time_limit_seconds\":30") },
            story: "[" + Beat("fail:soon", "slow") + "," + Beat("lose", "lost") + "]");
        var p = new ScenarioProgress(C, def);
        p.Update(w.Network, 0);
        w.Step(Tps * 31);
        var fired = p.Update(w.Network, 0);
        Assert.Equal(GoalStatus.Failed, p.Find("soon")!.Status);
        Assert.InRange(p.Find("soon")!.FailedSeconds!.Value, 30, 32);
        Assert.Equal(new[] { "fail:soon" }, fired.Select(e => e.Beat.Trigger));
        Assert.Equal(OutcomeKind.Playing, p.Outcome);
        Assert.Equal(30, p.Find("soon")!.TimeLimitSeconds);

        w.Step(Tps * 30);
        fired = p.Update(w.Network, 0);
        Assert.Equal(GoalStatus.Failed, p.Find("late")!.Status);
        Assert.Equal((OutcomeKind.Lost, OutcomeReason.GoalFailed), (p.Outcome, p.Reason));
        Assert.Equal(new[] { "lose" }, fired.Select(e => e.Beat.Trigger));
        Assert.Equal(0, p.Stars);
    }

    [Fact]
    public void A_goal_met_inside_its_time_limit_is_done_and_one_met_late_is_failed()
    {
        var (w, _) = ArmedShip();
        var p = new ScenarioProgress(C, Def(new[] { Goal("fast", "found_stations", ",\"count\":1", ",\"time_limit_seconds\":60") }));
        w.Step(Tps * 5);
        FoundStation(w);
        p.Update(w.Network, 0);
        Assert.Equal(GoalStatus.Done, p.Find("fast")!.Status);

        var (w2, _) = ArmedShip();
        var late = new ScenarioProgress(C, Def(new[] { Goal("fast", "found_stations", ",\"count\":1", ",\"time_limit_seconds\":60") }));
        w2.Step(Tps * 70);
        FoundStation(w2);
        late.Update(w2.Network, 0);
        Assert.Equal(GoalStatus.Failed, late.Find("fast")!.Status);
        Assert.Equal(OutcomeKind.Lost, late.Outcome);
    }

    [Fact]
    public void Losing_a_kept_module_loses_the_game_when_the_goal_is_primary_and_only_fails_when_secondary()
    {
        var (w, s) = ArmedShip();
        var def = Def(new[] { Goal("found", "found_stations", ",\"count\":1"), Goal("keep", "keep_alive", ",\"kind\":\"core\"") },
            new[] { Goal("any", "keep_alive") });
        var p = new ScenarioProgress(C, def);
        p.Update(w.Network, 0);
        Assert.Equal((1L, 1L), (p.Find("keep")!.Current, p.Find("keep")!.Target));
        Assert.Equal(GoalStatus.Pending, p.Find("keep")!.Status);

        // Losing a module of another kind fails only the any-module goal.
        w.Network.HitModule(s, 3, 0, 0, 99_999); // the engine
        p.Update(w.Network, 0);
        Assert.Equal(GoalStatus.Failed, p.Find("any")!.Status);
        Assert.Equal(0, p.Find("any")!.Current);
        Assert.Equal(GoalStatus.Pending, p.Find("keep")!.Status);
        Assert.Equal(OutcomeKind.Playing, p.Outcome);

        FoundStation(w);
        int station = w.Network.Structures().Single(x => x.Kind == StructureKind.Station).Id;
        w.Network.HitModule(station, 0, 0, 0, 99_999);
        p.Update(w.Network, 0);
        Assert.Equal(GoalStatus.Failed, p.Find("keep")!.Status);
        Assert.Equal((OutcomeKind.Lost, OutcomeReason.GoalFailed), (p.Outcome, p.Reason));
    }

    [Fact]
    public void Losing_the_ship_loses_the_game_whatever_the_goals_say()
    {
        var (w, s) = ArmedShip();
        var p = new ScenarioProgress(C, Def(new[] { Goal("found", "found_stations", ",\"count\":1") }, story: "[" + Beat("lose") + "]"));
        w.Network.HitModule(s, 0, 0, 0, 99_999);
        var fired = p.Update(w.Network, 0);
        Assert.Equal((OutcomeKind.Lost, OutcomeReason.ShipLost), (p.Outcome, p.Reason));
        Assert.Single(fired);
        // And ship loss beats a goal that was met in the same moment.
        var (w2, s2) = ArmedShip();
        var p2 = new ScenarioProgress(C, Def(new[] { Goal("found", "found_stations", ",\"count\":1") }));
        FoundStation(w2);
        w2.Network.HitModule(s2, 0, 0, 0, 99_999);
        p2.Update(w2.Network, 0);
        Assert.Equal(OutcomeKind.Lost, p2.Outcome);
    }

    [Fact]
    public void Running_out_of_fuel_fails_a_keep_fuel_goal_and_a_full_tank_passes_it()
    {
        var (w, _) = ArmedShip();
        var p = new ScenarioProgress(C, Def(new[] { Goal("found", "found_stations", ",\"count\":1") }, new[] { Goal("fuel", "keep_fuel") }));
        w.Step(Tps * 20);
        p.Update(w.Network, 0);
        Assert.Equal(GoalStatus.Pending, p.Find("fuel")!.Status);
        Ok(w, new DumpItems(Hydrogen, 0));
        w.Step(Tps * 60);
        p.Update(w.Network, 0);
        Assert.Equal(GoalStatus.Failed, p.Find("fuel")!.Status);
        Assert.Equal(OutcomeKind.Playing, p.Outcome);

        var (w2, _) = ArmedShip();
        var ok = new ScenarioProgress(C, Def(new[] { Goal("found", "found_stations", ",\"count\":1") }, new[] { Goal("fuel", "keep_fuel") }));
        FoundStation(w2);
        ok.Update(w2.Network, 0);
        Assert.Equal(OutcomeKind.Won, ok.Outcome);
        Assert.Equal(GoalStatus.Done, ok.Find("fuel")!.Status); // a condition that held to the end is done
        Assert.Equal((1, 1), ok.SecondaryCount);
    }

    [Fact]
    public void The_outcome_is_final_once_decided()
    {
        var (w, s) = ArmedShip();
        var p = new ScenarioProgress(C, Def(new[] { Goal("found", "found_stations", ",\"count\":1") }, new[] { Goal("keep", "keep_alive") }));
        FoundStation(w);
        p.Update(w.Network, 0);
        Assert.Equal(OutcomeKind.Won, p.Outcome);
        double decided = p.DecidedSeconds!.Value;
        w.Step(Tps * 5);
        w.Network.HitModule(s, 3, 0, 0, 99_999);
        w.Network.HitModule(s, 0, 0, 0, 99_999); // even the ship's fall changes nothing
        p.Update(w.Network, 0);
        Assert.Equal(OutcomeKind.Won, p.Outcome);
        Assert.Equal(decided, p.DecidedSeconds);
        Assert.Equal(GoalStatus.Done, p.Find("keep")!.Status);
    }

    // ---- story --------------------------------------------------------------------------------------

    [Fact]
    public void Story_beats_fire_once_in_order_with_their_text_and_speaker()
    {
        var (w, _) = ArmedShip();
        var def = Def(new[] { Goal("found", "found_stations", ",\"count\":1") },
            story: "[" + Beat("win", "won") + "," + Beat("goal:found", "found") + "," + Beat("time:10", "ten") + "," + Beat("start", "hello") + "," + Beat("time:10", "again") + "]");
        var p = new ScenarioProgress(C, def);
        var first = p.Update(w.Network, 0);
        Assert.Equal(new[] { "start" }, first.Select(e => e.Beat.Trigger));
        Assert.Equal("ORCA", first[0].Beat.Speaker);
        Assert.Equal("en hello", first[0].Text(false));
        Assert.Equal("es hello", first[0].Text(true));
        Assert.Empty(p.Update(w.Network, 0)); // nothing fires twice

        w.Step(Tps * 11);
        Assert.Equal(new[] { "time:10", "time:10" }, p.Update(w.Network, 0).Select(e => e.Beat.Trigger));
        FoundStation(w);
        var last = p.Update(w.Network, 0);
        Assert.Equal(new[] { "goal:found", "win" }, last.Select(e => e.Beat.Trigger)); // a goal beat comes before the win beat of the same update
        Assert.Equal(5, p.Story.Count);
        Assert.Empty(p.Update(w.Network, 0));
        Assert.All(p.Story, e => Assert.True(e.Seconds >= 0));
    }

    [Fact]
    public void A_time_beat_fires_even_after_the_game_is_decided_and_the_lose_beat_only_on_a_loss()
    {
        var (w, _) = ArmedShip();
        var def = Def(new[] { Goal("found", "found_stations", ",\"count\":1") }, story: "[" + Beat("time:20") + "," + Beat("lose") + "," + Beat("win") + "]");
        var p = new ScenarioProgress(C, def);
        FoundStation(w);
        Assert.Equal(new[] { "win" }, p.Update(w.Network, 0).Select(e => e.Beat.Trigger));
        w.Step(Tps * 21);
        Assert.Equal(new[] { "time:20" }, p.Update(w.Network, 0).Select(e => e.Beat.Trigger));
        Assert.DoesNotContain(p.Story, e => e.Beat.Trigger == "lose");
    }

    // ---- stars --------------------------------------------------------------------------------------

    private static ScenarioProgress Won(string stars, int seconds, bool secondaryDone, out ScenarioProgress p2)
    {
        var (w, _) = ArmedShip();
        var def = Def(new[] { Goal("found", "found_stations", ",\"count\":1") }, new[] { Goal("extra", "research", ",\"node\":\"core:logistics\"") }, stars: stars);
        var p = new ScenarioProgress(C, def);
        w.Step(Tps * seconds);
        if (secondaryDone) w.Network.GrantResearch(C.ResearchIndex("core:logistics"));
        p.Update(w.Network, 0);
        FoundStation(w);
        p.Update(w.Network, 0);
        p2 = p;
        return p;
    }

    [Theory]
    [InlineData("{}", 100, true, 3)] // no bounds: every secondary goal done gives the top stars
    [InlineData("{}", 100, false, 1)]
    [InlineData("{\"two_star_seconds\":120,\"three_star_seconds\":60}", 100, true, 2)]
    [InlineData("{\"two_star_seconds\":120,\"three_star_seconds\":60}", 40, true, 3)]
    [InlineData("{\"two_star_seconds\":120,\"three_star_seconds\":60}", 40, false, 2)] // three stars want every secondary goal by default
    [InlineData("{\"two_star_seconds\":120,\"three_star_seconds\":60,\"three_star_secondary\":0}", 40, false, 3)]
    [InlineData("{\"two_star_seconds\":120,\"three_star_seconds\":60}", 200, true, 1)]
    [InlineData("{\"two_star_seconds\":120,\"two_star_secondary\":1,\"three_star_seconds\":1}", 100, false, 1)]
    [InlineData("{\"two_star_seconds\":120,\"two_star_secondary\":1,\"three_star_seconds\":1}", 100, true, 2)]
    [InlineData("{\"three_star_seconds\":0,\"three_star_secondary\":-1}", 100, true, 3)] // no time bound: the secondary goals decide
    [InlineData("{\"three_star_seconds\":0,\"three_star_secondary\":-1}", 100, false, 1)]
    public void Stars_follow_the_time_bounds_and_the_secondary_goals(string stars, int seconds, bool secondaryDone, int expected)
    {
        var p = Won(stars, seconds, secondaryDone, out _);
        Assert.Equal(OutcomeKind.Won, p.Outcome);
        Assert.Equal(expected, p.Stars);
    }

    // ---- the shipped levels, scripted -----------------------------------------------------------------

    [Fact]
    public void Level_one_counts_ore_taken_out_of_deposits_by_any_means()
    {
        var def = TestContent.Scenarios.Get("01_first_light");
        var w = SimWorld.CreateScenario(C, def);
        var p = new ScenarioProgress(C, def);
        Assert.Equal(new[] { "start" }, p.Update(w.Network, 0).Select(e => e.Beat.Trigger));
        // A drone bay stands in for hand mining here: the goal counts ore taken out of deposits however it is done.
        int ship = w.Network.ShipStructure(0);
        w.Network.AddStructureModuleUnchecked(ship, 10, 0, 0, ModuleKind.DroneBay);
        int dep = w.Network.AddDeposit(Ore, 5000, 0, 0, 0);
        Ok(w, new AssignDrones(ship, dep));
        w.Step(Tps * 60); // one drone bay at density 100 mines 50 ore a minute: enough for the checks, short of the goal
        p.Update(w.Network, 0);
        Assert.True(p.Find("mine_iron")!.Current >= 3);
        Assert.Equal(GoalStatus.Pending, p.Find("mine_iron")!.Status);
        Assert.Equal(OutcomeKind.Playing, p.Outcome);
    }

    [Fact]
    public void Level_three_is_lost_when_the_core_falls_and_the_lose_beat_speaks()
    {
        var def = TestContent.Scenarios.Get("03_under_siege");
        var w = SimWorld.CreateScenario(C, def);
        var p = new ScenarioProgress(C, def);
        p.Update(w.Network, 0);
        var station = w.Network.Structures().Single(x => x.Kind == StructureKind.Station).Id;
        w.Network.HitModule(station, 0, 0, 0, 99_999);
        var fired = p.Update(w.Network, 0);
        Assert.Equal((OutcomeKind.Lost, OutcomeReason.GoalFailed), (p.Outcome, p.Reason));
        Assert.Contains(fired, e => e.Beat.Trigger == "lose");
        Assert.Equal(GoalStatus.Failed, p.Find("keep_core")!.Status);
        Assert.Equal(GoalStatus.Failed, p.Find("no_losses")!.Status);
    }
}
