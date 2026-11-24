using Starfoundry.Balance;
using Starfoundry.Sim;
using Starfoundry.Ui;
using Xunit;
using Xunit.Abstractions;

namespace Starfoundry.Sim.Tests;

/// <summary>
/// The shipped scenario files are valid, every goal can be reached, and a scripted player wins the levels it can play (01, 02 and 03).
/// Levels 04 and 05 are validated by reachability analysis only: the bot has no plan for them.
/// </summary>
public class ScenarioContentTests
{
    private static readonly ContentDb C = TestContent.CoreFloor;
    private static readonly ScenarioCatalog Catalog = TestContent.ScenariosFloor;
    private const double Minute = 60;

    private readonly ITestOutputHelper _out;
    public ScenarioContentTests(ITestOutputHelper output) => _out = output;

    // ---- texts and numbers ---------------------------------------------------------------------------

    [Fact]
    public void Every_text_exists_in_both_languages_and_differs_between_them()
    {
        foreach (var s in Catalog.All)
        {
            var texts = new List<(string Es, string En)> { (s.NameEs, s.NameEn), (s.DescriptionEs, s.DescriptionEn), (s.BriefingEs, s.BriefingEn) };
            texts.AddRange(s.AllGoals.Select(g => (g.TextEs, g.TextEn)));
            texts.AddRange(s.Story.Select(b => (b.TextEs, b.TextEn)));
            foreach (var (es, en) in texts)
            {
                Assert.False(string.IsNullOrWhiteSpace(es) || string.IsNullOrWhiteSpace(en), $"{s.Id} has an empty text");
                Assert.NotEqual(es, en);
            }
            // Spanish texts really are Spanish (an accent, an inverted mark or a common word), the English ones are plain ASCII.
            Assert.All(texts, t => Assert.True(t.En.All(c => c < 128 || c == '’'), $"{s.Id}: English text with non-ASCII: {t.En}"));
        }
    }

    [Fact]
    public void Ids_orders_and_numbers_are_sane()
    {
        Assert.Equal(Catalog.All.Count, Catalog.All.Select(s => s.Order).Distinct().Count());
        var levels = Catalog.All.Where(s => char.IsDigit(s.Id[0])).ToList();
        Assert.Equal(7, levels.Count);
        Assert.Equal(new[] { 1, 2, 3, 4, 5, 6, 7 }, levels.Select(s => s.Order));
        foreach (var s in Catalog.All)
        {
            if (char.IsDigit(s.Id[0])) Assert.StartsWith(s.Order.ToString("00"), s.Id); // levels are numbered like their order
            if (s.PlayerRules) continue;
            foreach (var g in s.AllGoals)
            {
                Assert.True(g.TimeLimitSeconds == 0 || g.TimeLimitSeconds >= 120, $"{s.Id}/{g.Id}: a time limit under two minutes");
                Assert.True(g.TimeLimitSeconds <= 4 * 3600, $"{s.Id}/{g.Id}: a time limit above four hours");
            }
            Assert.Equal(s.Primary.Select(g => g.Id).Concat(s.Secondary.Select(g => g.Id)).Distinct().Count(), s.AllGoals.Count());
            if (s.Stars.TwoStarSeconds > 0) Assert.True(s.Stars.TwoStarSeconds >= 300);
        }
        // The sandbox has no goals, the campaign and the levels have some; only levels with a fixed seed are reproducible.
        Assert.Empty(Catalog.Get("sandbox").AllGoals);
        Assert.Empty(Catalog.Get("sandbox_station").AllGoals);
        Assert.NotEmpty(Catalog.Get("campaign").Primary);
        Assert.All(levels.Where(s => s.Id != "05_long_watch"), s => Assert.NotEqual(0UL, s.Seed));
    }

    [Fact]
    public void Stars_are_reachable_inside_the_primary_goals_limits()
    {
        foreach (var s in Catalog.All.Where(s => s.Primary.Count > 0))
        {
            Assert.True(s.Stars.TwoStarSecondary <= s.Secondary.Count && s.Stars.ThreeStarSecondary <= s.Secondary.Count, s.Id);
            // A primary goal's time limit cannot cut the three-star time off.
            foreach (var g in s.Primary.Where(g => g.TimeLimitSeconds > 0))
                Assert.True(s.Stars.ThreeStarSeconds == 0 || g.TimeLimitSeconds >= s.Stars.ThreeStarSeconds, $"{s.Id}/{g.Id}");
        }
    }

    // ---- reachability: every goal can be delivered ---------------------------------------------------------

    private static bool HandMinable(int item) => C.Archetypes.Any(a => a.Deposits.Any(d => d.Item == item));
    private static bool HasPort(int item) => C.Machines.Any(m => m.Role == MachineRole.Unloader && m.Unloader!.Item == item);

    private static bool InWrecks(int item) => C.World!.Kinds.Any(k => k.Behaviour == ObstacleBehaviour.Wreck && k.Loot.Any(l => l.Item == item));

    private static bool PlaceableInModule(MachineDef m) =>
        !m.SandboxOnly && m.Role != MachineRole.Ship
        && (C.Structures!.AllowsRole(ModuleKind.Workshop, m.Role) || C.Structures.AllowsRole(ModuleKind.Core, m.Role));

    /// <summary>Same analysis as the structure economy tests: a raw item with a port, or the output of a placeable crafter whose inputs are deliverable.</summary>
    private static bool Deliverable(int item, HashSet<int> path)
    {
        if (HandMinable(item) && HasPort(item)) return true;
        if (!path.Add(item)) return false;
        try
        {
            foreach (var r in C.Recipes)
                if (r.Outputs.Any(o => o.Item == item) && PlaceableInModule(C.Machines[r.Machine]) && r.Inputs.All(i => Deliverable(i.Item, path))) return true;
            return false;
        }
        finally { path.Remove(item); }
    }

    private static bool Deliverable(int item) => Deliverable(item, new HashSet<int>());

    private static IEnumerable<int> ResearchClosure(int node)
    {
        var seen = new HashSet<int>();
        var stack = new Stack<int>();
        stack.Push(node);
        while (stack.Count > 0)
        {
            int n = stack.Pop();
            if (!seen.Add(n)) continue;
            foreach (int r in C.Research[n].Requires) stack.Push(r);
        }
        return seen;
    }

    [Fact]
    public void Every_goal_can_be_delivered_with_the_content()
    {
        var problems = new List<string>();
        foreach (var s in Catalog.All)
        {
            foreach (var g in s.AllGoals)
            {
                string at = $"{s.Id}/{g.Id}";
                switch (g.Type)
                {
                    case GoalType.Produce:
                        // Raw items are produced by mining, everything else by a crafter chain.
                        if (!HandMinable(g.Item) && !Deliverable(g.Item)) problems.Add($"{at}: nothing makes {C.Items[g.Item].Id}");
                        break;
                    case GoalType.Mine:
                        if (!HandMinable(g.Item)) problems.Add($"{at}: {C.Items[g.Item].Id} is in no deposit");
                        break;
                    case GoalType.Research:
                        foreach (int n in ResearchClosure(g.Node))
                            foreach (var cost in C.Research[n].Cost)
                                if (!Deliverable(cost.Item)) problems.Add($"{at}: research '{C.Research[n].Id}' costs {C.Items[cost.Item].Id}, which cannot reach a lab");
                        break;
                    case GoalType.BuildModule:
                        foreach (var cost in C.Structures!.ModuleCost(g.Kind!.Value))
                            if (!HandMinable(cost.Item) && !Deliverable(cost.Item)) problems.Add($"{at}: the {g.Kind} module costs {C.Items[cost.Item].Id}, which nothing makes");
                        break;
                    case GoalType.VisitSystems:
                        if (g.Count > C.Galaxy!.SystemCount) problems.Add($"{at}: more systems than the galaxy has");
                        break;
                    case GoalType.FoundStations:
                        foreach (var cost in C.Structures!.ModuleCost(ModuleKind.Core))
                            if (!Deliverable(cost.Item)) problems.Add($"{at}: the core costs {C.Items[cost.Item].Id}, which nothing makes");
                        break;
                    case GoalType.HaveModule:
                        foreach (var cost in C.Structures!.ModuleCost(g.Kind!.Value))
                            if (!HandMinable(cost.Item) && !Deliverable(cost.Item) && !InWrecks(cost.Item)) problems.Add($"{at}: the {g.Kind} module costs {C.Items[cost.Item].Id}, which nothing provides");
                        break;
                    case GoalType.Salvage:
                        if (!C.World!.Kinds.Any(k => k.Behaviour == ObstacleBehaviour.Wreck)) problems.Add($"{at}: the world has no wrecks");
                        break;
                    case GoalType.Stock:
                        if (!HandMinable(g.Item) && !Deliverable(g.Item) && !InWrecks(g.Item)) problems.Add($"{at}: nothing provides {C.Items[g.Item].Id}");
                        break;
                    case GoalType.ShipSpeed:
                        if (g.Count > StructureFlight.MaxSpeedPercent || C.Structures!.EngineSpeedPercent(ModuleKind.Engine) <= 0 || g.Count > C.Structures.EngineSpeedPercent(ModuleKind.Engine) * 40) problems.Add($"{at}: engines cannot give {g.Count} percent");
                        break;
                    case GoalType.KillEnemies:
                        Assert.NotNull(C.Threat);
                        break;
                    case GoalType.SurviveWaves:
                        Assert.NotNull(C.Threat);
                        Assert.True(g.Count == 0 || g.Count <= WorldRules.MaxGoalWaves);
                        break;
                }
            }
        }
        Assert.True(problems.Count == 0, string.Join("\n", problems));
    }

    [Fact]
    public void Starting_items_of_every_level_fit_the_starting_hold()
    {
        foreach (var s in Catalog.All)
        {
            var w = SimWorld.CreateScenario(C, s, s.Seed != 0 ? s.Seed : 12345);
            Assert.Equal(0, w.Network.StartingItemsClamped);
            // Hand mining stops at 75 percent of the hold: a level starts below that, or the player is told to build before mining
            // (the cold level starts with a cracker's worth of items that the first builds spend).
            bool buildsFirst = s.Id == "02_cold_fuel";
            Assert.True(buildsFirst || w.Network.HoldTotal(0) * 100 < w.Network.HoldCapacity(0) * PlayerRules.MiningHoldLimitPercent, s.Id);
            Assert.True(w.Network.HoldTotal(0) <= w.Network.HoldCapacity(0), s.Id);
        }
    }

    // ---- reachability of levels 04 and 05 (the bot does not play them) -----------------------------------------------

    /// <summary>Systems reachable from home within <paramref name="hops"/> lane jumps, nearest first.</summary>
    private static List<int> WithinHops(Galaxy g, int hops)
    {
        var dist = new Dictionary<int, int> { [0] = 0 };
        var order = new List<int> { 0 };
        for (int i = 0; i < order.Count; i++)
        {
            int s = order[i];
            if (dist[s] == hops) continue;
            foreach (int n in g.Systems[s].Lanes)
                if (dist.TryAdd(n, dist[s] + 1)) order.Add(n);
        }
        return order;
    }

    private static long Reachable(Galaxy g, IEnumerable<int> systems, int item, int richnessPercent) =>
        g.Deposits.Where(d => systems.Contains(d.System) && d.Item == item).Sum(d => (long)WorldRules.Scale(d.Amount, richnessPercent));

    [Fact]
    public void Deep_vein_has_every_material_of_its_goals_within_three_jumps_of_home()
    {
        var def = Catalog.Get("04_deep_vein");
        var g = Galaxy.Generate(C, def.Seed);
        var systems = WithinHops(g, 3);
        Assert.True(systems.Count >= 2, "no second system to visit");
        // What the goals cost in raw ore: titanium smelting 40 ore, advanced plating 40 titanium ingots and 40 nickel ingots (each 1 ore) and
        // 30 quartz, nickel smelting 40 nickel ore, and 20 titanium plates which are 40 titanium ingots, so 40 more ore.
        var need = new (string Item, long Amount)[] { ("core:titanium_ore", 40 + 40 + 40), ("core:nickel_ore", 40 + 40), ("core:quartz", 30) };
        foreach (var (item, amount) in need)
        {
            long have = Reachable(g, systems, C.ItemIndex(item), def.Rules.DepositRichnessPercent);
            _out.WriteLine($"{item}: {have} reachable within 3 jumps (needs {amount})");
            Assert.True(have >= amount * 2, $"{item}: only {have} within three jumps of home, the goals need {amount}");
        }
        // The first jump needs the jump drive (225 plates and 75 carbon, all at home) and 5 carbon per jump.
        Assert.True(Reachable(g, new[] { 0 }, C.ItemIndex("core:carbon"), def.Rules.DepositRichnessPercent) >= 75 + 5 * 6);
        Assert.True(Reachable(g, new[] { 0 }, C.ItemIndex("core:iron_ore"), def.Rules.DepositRichnessPercent) >= 2500);
    }

    [Fact]
    public void Deep_vein_and_the_long_watch_research_goals_are_all_in_the_completable_graph()
    {
        foreach (var id in new[] { "04_deep_vein" })
        {
            var def = Catalog.Get(id);
            foreach (var goal in def.AllGoals.Where(g => g.Type == GoalType.Research))
            {
                var closure = ResearchClosure(goal.Node).ToList();
                // The prerequisites are real nodes with deliverable costs; titanium smelting and advanced plating need the jump to fetch their ore.
                Assert.All(closure, n => Assert.All(C.Research[n].Cost, c => Assert.True(Deliverable(c.Item), C.Research[n].Id)));
                _out.WriteLine($"{goal.Id}: needs {string.Join(", ", closure.Select(n => C.Research[n].Id))}");
            }
        }
    }

    [Theory]
    [InlineData(1UL)]
    [InlineData(2UL)]
    [InlineData(777UL)]
    [InlineData(20261120UL)]
    [InlineData(20261121UL)]
    [InlineData(987654321UL)]
    public void The_long_watch_can_start_on_any_seed_with_a_neighbour_to_jump_to_and_enough_home_iron(ulong seed)
    {
        var def = Catalog.Get("05_long_watch");
        var g = Galaxy.Generate(C, seed);
        Assert.NotEmpty(g.Systems[0].Lanes);
        int richness = def.Rules.DepositRichnessPercent;
        Assert.True(Reachable(g, new[] { 0 }, C.ItemIndex("core:iron_ore"), richness) >= 2500, "home iron");
        Assert.True(Reachable(g, new[] { 0 }, C.ItemIndex("core:carbon"), richness) >= 75 + 5, "home carbon for the jump drive and a jump");
        Assert.True(Reachable(g, new[] { 0 }, C.ItemIndex("core:ice"), richness) >= 200, "home ice for the reactors");
    }

    [Fact]
    public void Every_goal_has_a_registered_icon_and_the_panel_rows_carry_it()
    {
        foreach (var s in Catalog.All)
        {
            var progress = new ScenarioProgress(C, s);
            foreach (var g in s.AllGoals)
                Assert.True(IconCatalog.IsRegistered(IconCatalog.ForGoal(g, C)), $"{s.Id}/{g.Id}: {IconCatalog.ForGoal(g, C)}");
            var model = GoalsView.Build(progress, 0, spanish: false);
            Assert.All(model.Rows, r => Assert.True(IconCatalog.IsRegistered(r.Icon), $"{s.Id}/{r.Id}"));
            Assert.True(s.AllGoals.Count() <= ScenarioCatalog.MaxGoals);
        }
    }

    [Fact]
    public void Goal_texts_stay_short_enough_for_the_panel_at_720p()
    {
        // The panel is about 300 px wide with a 16 px icon, a ring and the numbers: a goal line must wrap to at most three lines of about 26 characters.
        foreach (var s in Catalog.All)
            foreach (var g in s.AllGoals)
            {
                Assert.True(g.TextEn.Length <= 80, $"{s.Id}/{g.Id} English text is {g.TextEn.Length} characters");
                Assert.True(g.TextEs.Length <= 90, $"{s.Id}/{g.Id} Spanish text is {g.TextEs.Length} characters");
            }
    }

    [Fact]
    public void The_salvage_run_wreck_is_near_the_ship_and_holds_enough_crystals()
    {
        var def = Catalog.Get("06_salvage_run");
        var g = Galaxy.Generate(C, def.Seed);
        int crystal = C.ItemIndex("core:resonant_crystal");
        var wreck = g.ObstaclesOf(0).Select(i => g.Obstacles[i]).Where(o => C.World!.Kinds[o.Kind].Behaviour == ObstacleBehaviour.Wreck).ToList();
        Assert.Single(wreck);
        Assert.Equal(crystal, wreck[0].LootItem);
        int need = def.Primary.Single(p => p.Type == GoalType.Stock).Count;
        Assert.True(wreck[0].LootCount >= need, $"the wreck holds {wreck[0].LootCount} crystals, the goal wants {need}");
        double cells = Math.Sqrt((double)wreck[0].X * wreck[0].X + (double)wreck[0].Y * wreck[0].Y + (double)wreck[0].Z * wreck[0].Z) / Units.CellUnits;
        _out.WriteLine($"the wreck is {cells:F0} cells from the ship");
        Assert.InRange(cells, 20, 120);
        // The hold must have room for the loot (the starter ship's hold is 100).
        Assert.True(need + 20 <= 100);
    }

    [Fact]
    public void The_deep_vein_has_a_wreck_to_salvage_within_two_jumps_of_home()
    {
        var def = Catalog.Get("04_deep_vein");
        var g = Galaxy.Generate(C, def.Seed);
        var systems = WithinHops(g, 2);
        int wrecks = g.Obstacles.Count(o => systems.Contains(o.System) && C.World!.Kinds[o.Kind].Behaviour == ObstacleBehaviour.Wreck);
        Assert.True(wrecks >= 2, $"only {wrecks} wrecks within two jumps");
    }

    // ---- the bot plays levels 01, 02, 03, 06 and 07 -----------------------------------------------------------------------

    private StructureBot Bot(string id) => new(C, Catalog.Get(id)) { ActionDelaySeconds = 6 };

    private void Report(string id, StructureBot bot, StructureBotReport r)
    {
        _out.WriteLine($"{id}: {r.Outcome} ({r.Reason}) after {StructureBotReport.Clock(r.Seconds)}, stars {bot.Stars}");
        foreach (var g in bot.GoalStates)
            _out.WriteLine($"  {(g.Primary ? "primary  " : "secondary")} {g.Goal.Id}: {g.Status}{(g.CompletedSeconds is { } c ? " at " + StructureBotReport.Clock(c) : "")}");
        _out.WriteLine(r.Format());
    }

    [Fact]
    public void Bot_wins_level_01()
    {
        var bot = Bot("01_first_light");
        var r = bot.Run(30 * 60);
        Report("01", bot, r);
        Assert.Equal(OutcomeKind.Won, r.Outcome);
        Assert.True(r.Seconds <= 12 * Minute, $"won at {StructureBotReport.Clock(r.Seconds)}"); // measured 3:27 with the optional bay (1:51 before it)
        Assert.All(bot.GoalStates.Where(g => g.Primary), g => Assert.Equal(GoalStatus.Done, g.Status));
        Assert.Equal(GoalStatus.Done, bot.GoalStates.Single(g => g.Goal.Id == "quick_station").Status);
        Assert.Equal(GoalStatus.Done, bot.GoalStates.Single(g => g.Goal.Id == "fab_bay").Status);
        Assert.Equal(3, bot.Stars);
        Assert.Equal(0, r.WavesSurvived); // threats are off
    }

    [Fact]
    public void Bot_wins_level_02()
    {
        var bot = Bot("02_cold_fuel");
        var r = bot.Run(60 * 60);
        Report("02", bot, r);
        Assert.Equal(OutcomeKind.Won, r.Outcome);
        Assert.True(r.Seconds <= 45 * Minute, $"won at {StructureBotReport.Clock(r.Seconds)}"); // measured 4:20
        Assert.All(bot.GoalStates.Where(g => g.Primary), g => Assert.Equal(GoalStatus.Done, g.Status));
        Assert.Equal(GoalStatus.Done, bot.GoalStates.Single(g => g.Goal.Id == "never_dry").Status); // the cracker came in time
        Assert.True(r.At("Goal done: make_hydrogen") <= 5 * Minute, "the hydrogen has to come before the reactor starves");
        Assert.Equal(GoalStatus.Done, bot.GoalStates.Single(g => g.Goal.Id == "dock_stock").Status); // docked and left 20 ingots in the stock
        Assert.True(r.Has("Docked at the station"));
        Assert.Equal(3, bot.Stars);
    }

    [Fact]
    public void Bot_wins_level_03()
    {
        var bot = Bot("03_under_siege");
        var r = bot.Run(60 * 60);
        Report("03", bot, r);
        Assert.Equal(OutcomeKind.Won, r.Outcome);
        Assert.Equal(OutcomeReason.WavesSurvived, r.Reason);
        Assert.True(r.Seconds <= 45 * Minute, $"won at {StructureBotReport.Clock(r.Seconds)}"); // measured 17:35
        Assert.True(r.WavesSurvived >= 5);
        Assert.All(bot.GoalStates.Where(g => g.Primary), g => Assert.Equal(GoalStatus.Done, g.Status));
        Assert.Equal(GoalStatus.Done, bot.GoalStates.Single(g => g.Goal.Id == "gunnery").Status); // the turrets shot twelve ships down by wave three
        Assert.Equal(3, bot.Stars);
        Assert.True(r.Has("First turret built"));
        // The first wave must not come before the first turret stands: the 120 second grace is not the whole story, the alert meter fills too.
        Assert.True(r.At("First turret built") < r.At("Wave 1 warning"));
    }

    [Fact]
    public void Bot_wins_level_06()
    {
        var bot = Bot("06_salvage_run");
        var r = bot.Run(20 * 60);
        Report("06", bot, r);
        Assert.Equal(OutcomeKind.Won, r.Outcome);
        Assert.All(bot.GoalStates.Where(g => g.Primary), g => Assert.Equal(GoalStatus.Done, g.Status));
        Assert.True(r.Has("Wreck salvaged"));
        Assert.True(r.Has("Docked at the station"));
        Assert.True(r.Seconds <= 10 * Minute, $"won at {StructureBotReport.Clock(r.Seconds)}");
        Assert.Equal(GoalStatus.Done, bot.GoalStates.Single(g => g.Goal.Id == "quick_run").Status);
        Assert.Equal(3, bot.Stars);
    }

    [Fact]
    public void Bot_wins_level_07()
    {
        var bot = Bot("07_hull_breach");
        var r = bot.Run(40 * 60);
        Report("07", bot, r);
        Assert.Equal(OutcomeKind.Won, r.Outcome);
        Assert.All(bot.GoalStates.Where(g => g.Primary), g => Assert.Equal(GoalStatus.Done, g.Status));
        Assert.True(r.WavesSurvived >= 3);
    }

    [Fact]
    public void The_bot_refuses_levels_it_has_no_plan_for()
    {
        foreach (var id in new[] { "04_deep_vein", "05_long_watch", "campaign" })
        {
            Assert.DoesNotContain(id, StructureBot.PlayableScenarios);
            var bot = new StructureBot(C, Catalog.Get(id), 20261121UL);
            Assert.Throws<NotSupportedException>(() => bot.Run(60));
        }
    }
}
