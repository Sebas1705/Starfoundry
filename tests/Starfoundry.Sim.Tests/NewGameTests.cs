using Starfoundry.Sim;
using Starfoundry.Ui;
using Xunit;

namespace Starfoundry.Sim.Tests;

/// <summary>The engine-free parts of the New Game screen, the goals panel and the end screens: file formats, progress, presets and the goals view.</summary>
public class NewGameTests
{
    private static readonly ContentDb C = TestContent.CoreScen;
    private static readonly ScenarioCatalog Catalog = TestContent.Scenarios;

    // ---- CfgText -----------------------------------------------------------------------------

    [Fact]
    public void Cfg_text_round_trips_quotes_and_skips_garbage()
    {
        string q = CfgText.Quote("a \"b\" \\ c\nd");
        Assert.Equal("a \"b\" \\ c\nd", CfgText.Unquote(q));
        var sections = CfgText.Parse("; comment\nstray=1\n[one]\nk = v\nbad line\n#c\n[two]\nname=" + q + "\n[broken\nx=1\n");
        Assert.Equal(new[] { "", "one", "two" }, sections.Select(s => s.Name).ToArray());
        Assert.Equal("v", sections[1].Get("k"));
        Assert.Equal("a \"b\" \\ c\nd", sections[2].Get("name"));
        Assert.Null(sections[1].Get("nope"));
        Assert.Empty(CfgText.Parse(null));
    }

    // ---- ProgressRecord ----------------------------------------------------------------------

    [Fact]
    public void A_win_records_the_best_time_and_the_best_stars_separately()
    {
        var p = new ProgressRecord();
        var first = p.RecordWin("01_first_light", 2, 600);
        Assert.True(first.FirstCompletion);
        Assert.False(first.NewBestTime); // the first completion is reported as such, not as a record
        var worse = p.RecordWin("01_first_light", 1, 700);
        Assert.False(worse.NewBestTime || worse.NewBestStars);
        Assert.Equal(600, p.Get("01_first_light")!.BestSeconds);
        var better = p.RecordWin("01_first_light", 3, 500);
        Assert.True(better.NewBestTime && better.NewBestStars);
        Assert.Equal(600, better.PreviousBestSeconds);
        Assert.Equal(2, better.PreviousBestStars);
        var r = p.Get("01_first_light")!;
        Assert.Equal((500.0, 3), (r.BestSeconds, r.BestStars));
        var clamped = new ProgressRecord();
        clamped.RecordWin("x", 9, double.NaN);
        Assert.Equal((0.0, 3), (clamped.Get("x")!.BestSeconds, clamped.Get("x")!.BestStars));
    }

    [Fact]
    public void Story_levels_unlock_in_order()
    {
        var ids = ScenarioKinds.Story(Catalog).Select(s => s.Id).ToList();
        Assert.Equal(new[] { "01_first_light", "02_cold_fuel", "03_under_siege", "04_deep_vein", "05_long_watch", "06_salvage_run", "07_hull_breach" }, ids);
        var p = new ProgressRecord();
        Assert.True(p.IsUnlocked(ids, ids[0]));
        Assert.False(p.IsUnlocked(ids, ids[1]));
        Assert.False(p.IsUnlocked(ids, ids[2]));
        p.RecordWin(ids[0], 1, 100);
        Assert.True(p.IsUnlocked(ids, ids[1]));
        Assert.False(p.IsUnlocked(ids, ids[2])); // completing level 1 does not open level 3
        Assert.True(p.IsUnlocked(ids, "not a level")); // not part of the chain: nothing to unlock
        Assert.Equal(ids[1], ScenarioKinds.NextStory(Catalog, Catalog.Get(ids[0]))!.Id);
        Assert.Null(ScenarioKinds.NextStory(Catalog, Catalog.Get(ids[6])));
        Assert.Null(ScenarioKinds.NextStory(Catalog, Catalog.Get("campaign")));
    }

    [Fact]
    public void Progress_text_round_trips_and_ignores_bad_entries()
    {
        var p = new ProgressRecord();
        p.RecordWin("01_first_light", 3, 111.5);
        p.RecordWin("campaign", 1, 2700);
        var back = ProgressRecord.Parse(p.Serialize());
        Assert.Equal(p.Records.Keys, back.Records.Keys);
        Assert.Equal(p.Get("01_first_light"), back.Get("01_first_light"));
        Assert.Equal(4, back.TotalStars);
        var rough = ProgressRecord.Parse("[a]\ncompleted=true\nbest_seconds=-5\nbest_stars=99\n[b]\ncompleted=false\nbest_stars=3\n[c]\ncompleted=maybe\n\n[]\ncompleted=true\n");
        Assert.Equal(new[] { "a" }, rough.Records.Keys.ToArray());
        Assert.Equal((0.0, 3), (rough.Get("a")!.BestSeconds, rough.Get("a")!.BestStars));
        Assert.Empty(ProgressRecord.Parse("garbage\n\u0000\u0001").Records);
        Assert.Empty(ProgressRecord.Parse(null).Records);
    }

    [Fact]
    public void Scenarios_are_classified_for_the_menus()
    {
        Assert.Equal(ScenarioKind.Campaign, ScenarioKinds.Of(Catalog.Get("campaign")));
        Assert.Equal(ScenarioKind.Sandbox, ScenarioKinds.Of(Catalog.Get("sandbox_siege")));
        Assert.Equal(ScenarioKind.Custom, ScenarioKinds.Of(Catalog.Get("custom")));
        Assert.All(Catalog.All.Where(s => char.IsDigit(s.Id[0])), s => Assert.Equal(ScenarioKind.Story, ScenarioKinds.Of(s)));
        // Every form choice names a scenario that exists.
        foreach (bool free in new[] { true, false })
            foreach (var start in Enum.GetValues<SandboxStart>())
                Assert.NotNull(Catalog.Find(ScenarioKinds.OpenEndedId(free, start)));
    }

    // ---- sandbox form and presets ------------------------------------------------------------

    [Fact]
    public void The_sandbox_form_becomes_valid_world_rules()
    {
        var s = new SandboxSettings
        {
            FreeBuild = false, Threats = true, EnemyDamagePercent = 5000, EnemyHpPercent = 1, WaveSizePercent = 150, WaveIntervalPercent = 90,
            FirstWaveGraceSeconds = 99999, DepositRichnessPercent = 250, PowerOutputPercent = 120, GoalWaves = 0,
            Items = new[] { new SandboxItem("core:iron_plate", 40), new SandboxItem("core:hydrogen", 12), new SandboxItem("core:nope", 5), new SandboxItem("core:carbon", 0) },
        };
        var rules = s.ToRules(C);
        Assert.Null(rules.Validate(C));
        Assert.Equal(WorldRules.MaxPercent, rules.EnemyDamagePercent);
        Assert.Equal(WorldRules.MinPercent, rules.EnemyHpPercent);
        Assert.Equal(WorldRules.MaxGraceSeconds, rules.FirstWaveGraceSeconds);
        Assert.Equal(WorldRules.MinGoalWaves, rules.GoalWaves);
        Assert.True(rules.ThreatsEnabled);
        Assert.False(rules.FreeBuild);
        Assert.Equal(2, rules.StartingItems.Count); // the unknown item and the zero count are dropped
        Assert.Equal((C.ItemIndex("core:iron_plate"), 40), (rules.StartingItems[0].Item, rules.StartingItems[0].Count));
        Assert.Equal("custom", s.ScenarioId);
        Assert.Equal("sandbox_siege", (s with { FreeBuild = true, Start = SandboxStart.ShipAndSiege }).ScenarioId);
    }

    [Fact]
    public void Every_offered_starting_item_exists_and_every_default_form_builds_a_world()
    {
        Assert.All(SandboxSettings.OfferedItems, id => Assert.Contains(C.Items, i => i.Id == id));
        foreach (bool free in new[] { true, false })
            foreach (var start in Enum.GetValues<SandboxStart>())
            {
                var s = new SandboxSettings { FreeBuild = free, Start = start, Threats = true, Items = new[] { new SandboxItem("core:iron_plate", 30) } };
                var world = SimWorld.CreateScenario(C, Catalog, s.ScenarioId, 12345, s.ToRules(C));
                Assert.Equal(free ? GameMode.Sandbox : GameMode.Campaign, world.Mode);
                Assert.Equal(free, world.Network.Rules.FreeBuild);
            }
    }

    [Fact]
    public void Presets_save_replace_remove_and_validate_names()
    {
        var presets = new SandboxPresets();
        Assert.True(presets.Save("  Hard siege ", new SandboxSettings { EnemyDamagePercent = 200 }));
        Assert.Equal("Hard siege", presets.All[0].Name);
        Assert.True(presets.Save("hard SIEGE", new SandboxSettings { EnemyDamagePercent = 150 })); // same name, other case: replaced
        Assert.Single(presets.All);
        Assert.Equal(150, presets.Find("Hard Siege")!.Settings.EnemyDamagePercent);
        Assert.False(presets.Save("", new SandboxSettings()));
        Assert.False(presets.Save(new string('x', SandboxPresets.MaxNameLength + 1), new SandboxSettings()));
        Assert.False(presets.Save("a\nb", new SandboxSettings()));
        Assert.True(presets.Remove("HARD SIEGE"));
        Assert.False(presets.Remove("nothing"));
        for (int i = 0; i < SandboxPresets.MaxPresets; i++) Assert.True(presets.Save("p" + i, new SandboxSettings()));
        Assert.False(presets.Save("one too many", new SandboxSettings()));
        Assert.True(presets.Save("p3", new SandboxSettings { GoalWaves = 4 })); // replacing still works when full
    }

    [Fact]
    public void Presets_round_trip_through_the_text_form_and_survive_damage()
    {
        var presets = new SandboxPresets();
        var form = new SandboxSettings
        {
            FreeBuild = false, Threats = true, EnemyDamagePercent = 140, EnemyHpPercent = 60, WaveSizePercent = 200, WaveIntervalPercent = 75,
            FirstWaveGraceSeconds = 90, DepositRichnessPercent = 300, PowerOutputPercent = 80, GoalWaves = 7, Start = SandboxStart.ShipAndSiege, Seed = 424242,
            Items = new[] { new SandboxItem("core:iron_plate", 100), new SandboxItem("core:hydrogen", 25) },
        };
        presets.Save("Mine \"quoted\"", form);
        presets.Save("Plain", new SandboxSettings());
        var back = SandboxPresets.Parse(presets.Serialize());
        Assert.Equal(new[] { "Mine \"quoted\"", "Plain" }, back.All.Select(p => p.Name).ToArray());
        var got = back.All[0].Settings;
        Assert.Equal(form.Clamped() with { Items = got.Items }, got);
        Assert.Equal(form.Items.Select(i => (i.Item, i.Count)), got.Items.Select(i => (i.Item, i.Count)));
        Assert.Equal(new SandboxSettings().ToRules(C), back.All[1].Settings.ToRules(C));

        // Hand-edited or damaged files: bad names and values are skipped or clamped, never fatal.
        var rough = SandboxPresets.Parse("[preset 1]\nname=\"\"\n[preset 2]\nname=\"ok\"\nenemy_damage=99999\nstart=weird\nseed=abc\nitems=\"core:iron_plate:5,junk,core:ice:x\"\n[other]\nname=\"no\"\n\u0000");
        Assert.Single(rough.All);
        Assert.Equal(WorldRules.MaxPercent, rough.All[0].Settings.EnemyDamagePercent);
        Assert.Equal(SandboxStart.Ship, rough.All[0].Settings.Start);
        Assert.Equal(0UL, rough.All[0].Settings.Seed);
        Assert.Single(rough.All[0].Settings.Items);
        Assert.Empty(SandboxPresets.Parse(null).All);
    }

    // ---- goals view --------------------------------------------------------------------------

    private static string Goal(string id, string type, string fields = "", string limit = "") =>
        $"{{\"id\":\"{id}\",\"type\":\"{type}\"{fields}{limit},\"text_es\":\"es {id}\",\"text_en\":\"en {id}\"}}";

    private static ScenarioDef Def(string[] primary, string[]? secondary = null, string? story = null) =>
        ScenarioCatalog.Parse(C, "{\"schema\":1,\"id\":\"t\",\"order\":1,\"name_es\":\"t\",\"name_en\":\"t\",\"description_es\":\"t\",\"description_en\":\"t\",\"briefing_es\":\"t\",\"briefing_en\":\"t\""
            + ",\"goals\":{\"primary\":[" + string.Join(",", primary) + "],\"secondary\":[" + string.Join(",", secondary ?? Array.Empty<string>()) + "]}"
            + (story is null ? "" : ",\"story\":" + story) + "}");

    private static SimWorld World() => SimWorld.CreateGalaxy(TestContent.CoreWithoutStarterShip, GameMode.Sandbox, 20261121UL, threats: false, stationRules: false, structures: true);

    [Fact]
    public void The_goals_view_lists_primary_then_secondary_with_language_countdowns_and_bars()
    {
        var def = Def(new[] { Goal("found", "found_stations", ",\"count\":2"), Goal("keep", "keep_alive") },
            new[] { Goal("fast", "found_stations", ",\"count\":1", ",\"time_limit_seconds\":600") });
        var w = World();
        w.Network.EnsurePlayer(0);
        var progress = new ScenarioProgress(C, def);
        progress.Update(w.Network, 0);

        var en = GoalsView.Build(progress, 545.2, spanish: false);
        Assert.Equal(new[] { "found", "keep", "fast" }, en.Rows.Select(r => r.Id).ToArray());
        Assert.Equal(new[] { true, true, false }, en.Rows.Select(r => r.Primary).ToArray());
        Assert.Equal("en found", en.Rows[0].Text);
        Assert.Equal("es found", GoalsView.Build(progress, 0, spanish: true).Rows[0].Text);
        Assert.Equal((0, 2, 0, 1), (en.PrimaryDone, en.PrimaryTotal, en.SecondaryDone, en.SecondaryTotal));
        Assert.True(en.Rows[0].ShowBar);
        Assert.False(en.Rows[1].ShowBar); // a keep goal only holds
        Assert.Equal("0/2", en.Rows[0].ProgressText);
        Assert.Equal(0, en.Rows[0].Fraction);
        Assert.Null(en.Rows[0].RemainingSeconds);
        Assert.Equal(55, en.Rows[2].RemainingSeconds); // 600 - 545.2, rounded up
        Assert.Equal("0:55", en.Rows[2].TimeText);
        Assert.True(en.Rows[2].Urgent);
        Assert.False(GoalsView.Build(progress, 100, false).Rows[2].Urgent);
        Assert.Equal("8:20", GoalsView.Build(progress, 100, false).Rows[2].TimeText);
        Assert.Equal(0, GoalsView.Build(progress, 9999, false).Rows[2].RemainingSeconds); // never negative
    }

    [Fact]
    public void Goal_rows_follow_done_and_failed_states()
    {
        var def = Def(new[] { Goal("found", "found_stations", ",\"count\":1") }, new[] { Goal("keep", "keep_alive") });
        var w = World();
        w.Network.EnsurePlayer(0);
        var progress = new ScenarioProgress(C, def);
        progress.Update(w.Network, 0);
        progress.Restore(new ProgressSnapshot(0, new[]
        {
            new GoalSnapshot("found", GoalStatus.Done, 1, 100, null), new GoalSnapshot("keep", GoalStatus.Failed, 0, null, 50),
        }, new List<(int, double)>(), OutcomeKind.Playing, OutcomeReason.None, 0, null, false));
        var model = GoalsView.Build(progress, 200, false);
        Assert.Equal(GoalStatus.Done, model.Rows[0].Status);
        Assert.Equal(1.0, model.Rows[0].Fraction);
        Assert.Equal(GoalStatus.Failed, model.Rows[1].Status);
        Assert.Null(model.Rows[1].RemainingSeconds); // a settled goal has no countdown
        Assert.Equal(1, model.PrimaryDone);
    }

    [Fact]
    public void Guided_steps_are_hidden_only_in_a_sandbox_without_goals()
    {
        Assert.False(GoalsView.ShowGuidedSteps(Catalog.Get("sandbox")));
        Assert.False(GoalsView.ShowGuidedSteps(Catalog.Get("sandbox_siege")));
        Assert.True(GoalsView.ShowGuidedSteps(Catalog.Get("custom")));
        Assert.True(GoalsView.ShowGuidedSteps(Catalog.Get("campaign")));
        Assert.True(GoalsView.ShowGuidedSteps(Catalog.Get("01_first_light")));
        Assert.True(GoalsView.ShowGuidedSteps(null));
    }

    [Fact]
    public void Story_messages_carry_the_speaker_and_the_language_and_mark_the_start()
    {
        var def = Def(new[] { Goal("a", "found_stations", ",\"count\":1") }, story: "[{\"trigger\":\"start\",\"speaker\":\"Ada\",\"text_es\":\"hola\",\"text_en\":\"hi\"},{\"trigger\":\"win\",\"text_es\":\"fin\",\"text_en\":\"end\"}]");
        var w = World();
        w.Network.EnsurePlayer(0);
        var fired = new ScenarioProgress(C, def).Update(w.Network, 0);
        var start = GoalsView.Message(fired[0], spanish: false);
        Assert.Equal(("Ada", "hi", true), (start.Speaker, start.Text, start.IsStart));
        Assert.Equal("hola", GoalsView.Message(fired[0], spanish: true).Text);
        var other = GoalsView.Message(new StoryEvent(def.Story[1], 10), false);
        Assert.Equal(("ORCA", "end", false), (other.Speaker, other.Text, other.IsStart));
    }

    [Theory]
    [InlineData(0, "0:00")]
    [InlineData(59, "0:59")]
    [InlineData(61, "1:01")]
    [InlineData(3600, "1:00:00")]
    [InlineData(-5, "0:00")]
    public void The_clock_formats_minutes_and_hours(int seconds, string expected) => Assert.Equal(expected, GoalsView.Clock(seconds));

    [Fact]
    public void Star_hints_say_what_the_next_star_needs()
    {
        Loc.Lang = Language.En;
        var siege = Catalog.Get("03_under_siege"); // 2 stars within 35:00, 3 within 25:00 with every secondary goal
        Assert.Equal("For star 2: finish within 35:00", GoalsView.StarHint(siege, 1));
        Assert.Equal("For star 3: finish within 25:00 and complete every bonus goal", GoalsView.StarHint(siege, 2));
        Assert.Equal("", GoalsView.StarHint(siege, 3));
        Assert.Equal("", GoalsView.StarHint(Catalog.Get("campaign"), 1)); // no star rules
        Assert.Equal("**-", GoalsView.StarsText(2));
    }

    // ---- the goals sidecar of a save ---------------------------------------------------------

    private static ScenarioDef StoryDef() => Def(new[] { Goal("found", "found_stations", ",\"count\":1"), Goal("hold", "keep_alive") }, new[] { Goal("bonus", "found_stations", ",\"count\":3") },
        "[{\"trigger\":\"start\",\"text_es\":\"a\",\"text_en\":\"a\"},{\"trigger\":\"goal:found\",\"text_es\":\"b\",\"text_en\":\"b\"},{\"trigger\":\"time:30\",\"text_es\":\"c\",\"text_en\":\"c\"}]");

    [Fact]
    public void The_sidecar_text_round_trips()
    {
        var snap = new ProgressSnapshot(12345, new[]
        {
            new GoalSnapshot("found", GoalStatus.Done, 1, 123.456789012345, null), new GoalSnapshot("hold", GoalStatus.Failed, 0, null, 77.5),
            new GoalSnapshot("bonus", GoalStatus.Pending, 2, null, null),
        }, new List<(int, double)> { (0, 0.05), (1, 123.456789012345) }, OutcomeKind.Won, OutcomeReason.GoalsDone, 2, 130.25, true);
        var back = ProgressSnapshotText.Parse(ProgressSnapshotText.Serialize(snap))!;
        Assert.Equal(snap.Tick, back.Tick);
        Assert.Equal(snap.Goals, back.Goals);
        Assert.Equal(snap.Beats, back.Beats);
        Assert.Equal((snap.Outcome, snap.Reason, snap.Stars, snap.DecidedSeconds, snap.KeepPlaying), (back.Outcome, back.Reason, back.Stars, back.DecidedSeconds, back.KeepPlaying));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("not a sidecar")]
    [InlineData("SFGOALS 2\ntick 1\n")]
    [InlineData("SFGOALS 1\n")]
    [InlineData("SFGOALS 1\ntick x\n")]
    [InlineData("SFGOALS 1\ntick 1\ngoal a 9 0 - -\n")]
    [InlineData("SFGOALS 1\ntick 1\ngoal a 1 0 -5 -\n")]
    [InlineData("SFGOALS 1\ntick 1\noutcome 7 0 0 - 0\n")]
    [InlineData("SFGOALS 1\ntick 1\nbeat 0 -\n")]
    [InlineData("SFGOALS 1\ntick 99999999999999999999\n")]
    public void A_damaged_sidecar_reads_as_nothing(string? text) => Assert.Null(ProgressSnapshotText.Parse(text));

    [Fact]
    public void Restoring_a_snapshot_keeps_goal_times_and_does_not_repeat_story_beats()
    {
        var def = StoryDef();
        var w = World();
        w.Network.EnsurePlayer(0);
        var first = new ScenarioProgress(C, def);
        Assert.Single(first.Update(w.Network, 0)); // the start beat

        // A world where the goal is not met: the tracker would be pending, so what the snapshot says must win.
        var snap = new ProgressSnapshot(w.Tick, new[]
        {
            new GoalSnapshot("found", GoalStatus.Done, 1, 12.5, null), new GoalSnapshot("hold", GoalStatus.Pending, 1, null, null),
            new GoalSnapshot("bonus", GoalStatus.Failed, 1, null, 20),
        }, new List<(int, double)> { (0, 0), (1, 12.5) }, OutcomeKind.Playing, OutcomeReason.None, 0, null, false);
        var loaded = new ScenarioProgress(C, def);
        loaded.Restore(snap);
        Assert.Empty(loaded.Update(w.Network, 0)); // neither the start beat nor the goal beat fire again
        Assert.Equal(GoalStatus.Done, loaded.Find("found")!.Status);
        Assert.Equal(12.5, loaded.Find("found")!.CompletedSeconds);
        Assert.Equal(GoalStatus.Failed, loaded.Find("bonus")!.Status);
        Assert.Equal(20, loaded.Find("bonus")!.FailedSeconds);
        Assert.Equal(2, loaded.Story.Count);
        Assert.Equal(OutcomeKind.Won, loaded.Outcome); // the done goal and a keep goal that never failed
    }

    [Fact]
    public void Export_then_restore_gives_the_same_state_and_a_decided_outcome_stays_decided()
    {
        var def = StoryDef();
        var w = World();
        w.Network.EnsurePlayer(0);
        var a = new ScenarioProgress(C, def);
        a.Update(w.Network, 0);
        var snap = a.Export(w.Tick, keepPlaying: true);
        Assert.True(snap.KeepPlaying);
        var b = new ScenarioProgress(C, def);
        b.Restore(ProgressSnapshotText.Parse(ProgressSnapshotText.Serialize(snap))!);
        Assert.Equal(a.Goals.Select(g => (g.Goal.Id, g.Status, g.Current)), b.Goals.Select(g => (g.Goal.Id, g.Status, g.Current)));
        Assert.Equal(a.Story.Count, b.Story.Count);

        // A won tracker stays won, with its stars and time, after a round trip.
        var won = new ProgressSnapshot(w.Tick, snap.Goals, snap.Beats, OutcomeKind.Won, OutcomeReason.GoalsDone, 3, 99.5, false);
        var c = new ScenarioProgress(C, def);
        c.Restore(won);
        c.Update(w.Network, 0);
        Assert.Equal((OutcomeKind.Won, 3, 99.5), (c.Outcome, c.Stars, c.DecidedSeconds));
    }

    [Fact]
    public void Restore_ignores_goals_and_beats_the_scenario_does_not_have()
    {
        var def = StoryDef();
        var p = new ScenarioProgress(C, def);
        p.Restore(new ProgressSnapshot(0, new[] { new GoalSnapshot("ghost", GoalStatus.Done, 1, 1, null), new GoalSnapshot("found", GoalStatus.Done, 1, 5, null) },
            new List<(int, double)> { (99, 1), (-1, 1), (1, 5), (1, 6) }, OutcomeKind.Playing, OutcomeReason.None, 0, null, false));
        Assert.Equal(GoalStatus.Done, p.Find("found")!.Status);
        Assert.Equal(GoalStatus.Pending, p.Find("hold")!.Status);
        Assert.Single(p.Story); // only beat 1, once
    }
}
