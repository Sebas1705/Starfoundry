using Starfoundry.Balance;
using Starfoundry.Ui;
using Xunit;
using Xunit.Abstractions;

namespace Starfoundry.Sim.Tests;

/// <summary>
/// A scripted player plays the structure campaign from the first tick to the win with sim commands only (<c>StructureBot</c>, see
/// docs/prototypes/structure-bot.md for the timeline and every place where it got stuck). The bounds are generous: they catch a content
/// or rule change that makes a stage impossible or absurdly slow, not small shifts.
/// </summary>
[Trait("Tier", "Slow")]
public class StructureBotTests
{
    private const ulong Seed = 20261121UL;
    private const double Minute = 60;

    private readonly ITestOutputHelper _out;
    public StructureBotTests(ITestOutputHelper output) => _out = output;

    private static StructureBotReport Play(ulong seed = Seed, double delay = 6, bool continueAfterWin = false, int minutes = 120, bool threats = true) =>
        new StructureBot(TestContent.CoreFloor, seed, threats) { ActionDelaySeconds = delay, ContinueAfterVictory = continueAfterWin }.Run(minutes * 60);

    private static double Min(StructureBotReport r, string milestone) => r.At(milestone) / Minute;

    [Fact]
    public void Bot_reaches_each_milestone()
    {
        var r = Play();
        _out.WriteLine(r.Format());
        // Each milestone: its measured minute (seed 20261121, 6 s per action) and a generous bound.
        var bounds = new (string Name, double MaxMinutes)[]
        {
            ("Hand mining the home iron deposit", 1),         // measured 0:07
            ("First iron ingot in the hold", 1),              // 0:17
            ("First iron plate in the hold", 2),              // 1:05
            ("Station founded", 8),                           // 4:58 (the workbench makes the core's ingots and plates, 8 s a craft, ADR 0039)
            ("Factory running: first delivery from a depot", 18), // 14:11 (reactor, then the ingot line on the core floor)
            ("Hydrogen cracker built", 22),                   // 17:58
            ("Hydrogen loop: first hydrogen made from ice", 24), // 19:30
            ("Station powered with machines running", 25),    // 19:31
            ("Fabrication bay built", 26),                     // 20:3x (the bay of the station defence)
            ("First defence module built", 26),               // 20:51
            ("First turret built", 27),                       // 21:22
            ("First turret loaded with ammo", 28),            // 21:40
            ("First repair bay built", 30),                   // 22:31
            ("Laboratory built", 32),                         // 24:57
            ("First research finished", 36),                  // 28:07
            ("Wave 1 warning", 27),                           // 22:38 (the alert meter fills with the factory noise)
            ("Wave 1 survived", 29),                          // 23:24
        };
        foreach (var (name, max) in bounds)
        {
            Assert.True(r.Has(name), $"milestone '{name}' never happened");
            Assert.True(Min(r, name) <= max, $"'{name}' at minute {Min(r, name):F1}, expected by minute {max}");
        }
        // The first wave must not arrive before the player could have built anything (the grace period of the structure rules).
        Assert.True(Min(r, "Wave 1 warning") >= 8, $"the first wave warning came at minute {Min(r, "Wave 1 warning"):F1}");
        Assert.True(Min(r, "First turret built") < Min(r, "Wave 1 warning"), "the first turret has to stand before the first wave warning");
    }

    [Fact]
    public void Relaxed_wins_before_Normal_and_inside_its_band()
    {
        // After ADR 0033 the solid deposit rocks pushed the station away from the mining spot and Relaxed came out slower than Normal (55:35); the bot now keeps the trips short.
        const string win = "VICTORY: waves survived with a station standing";
        double Run(string preset)
        {
            var r = new StructureBot(TestContent.CoreFloor, Seed, threats: true, rules: WorldRules.Preset(preset)!) { ActionDelaySeconds = 6 }.Run(120 * 60);
            Assert.True(r.Has(win), $"{preset}: no win");
            return Min(r, win);
        }
        double relaxed = Run("relaxed"), normal = Run("normal");
        _out.WriteLine($"relaxed {relaxed:F1}, normal {normal:F1}");
        Assert.True(relaxed < normal, $"Relaxed {relaxed:F1} must win before Normal {normal:F1}");
        Assert.InRange(relaxed, 40, 52);
        Assert.InRange(normal, 45, 55);
    }

    [Fact]
    public void Bot_wins_the_campaign()
    {
        var r = Play();
        _out.WriteLine(r.Format());
        Assert.Equal(OutcomeKind.Won, r.Outcome);
        Assert.Equal(OutcomeReason.WavesSurvived, r.Reason);
        Assert.True(r.WavesSurvived >= GoalSettings.DefaultWaves, $"only {r.WavesSurvived} waves survived");
        Assert.InRange(r.Seconds / Minute, 38, 60); // measured 48:47 (66:08 after ADR 0031 with the 2 s smelter; 42 to 44 before it): the wave clock decides, the research keeps up
        // The waves come on their own clock: about three minutes apart once the base is loud.
        Assert.True(r.Has("Wave 10 survived"));
        // The win needs the research path: the jump drive was researched and a second system visited before it.
        Assert.True(r.Has("Researched jump_drive"), "won without the jump drive");
        Assert.True(r.Timeline.Any(t => t.Name.StartsWith("Arrived in system")), "won without leaving home");
        Assert.True(Min(r, "Researched jump_drive") <= Min(r, "VICTORY: waves survived with a station standing"));
        Assert.True(Min(r, "Researched drone_automation") < Min(r, "Researched jump_drive"), "automation comes before the jump drive");
        Assert.True(r.Has("First drone bay built") && r.Has("Drones delivered ore"));
    }

    [Theory]
    [InlineData(1UL)]
    [InlineData(2UL)]
    [InlineData(20261120UL)]
    public void Bot_wins_other_galaxies_too(ulong seed)
    {
        var r = Play(seed);
        Assert.True(r.Outcome == OutcomeKind.Won, "seed " + seed + ": " + r.Format());
        Assert.InRange(r.Seconds / Minute, 38, 60); // measured 46:38 (seed 1), 47:36 (2), 48:59 (20261120): the wave clock decides
    }

    [Theory]
    [InlineData(0.0)]
    [InlineData(12.0)]
    public void Bot_wins_at_any_pace(double delay)
    {
        var r = Play(delay: delay);
        Assert.True(r.Outcome == OutcomeKind.Won, $"delay {delay}: " + r.Format());
        // The wave clock decides, not the player: even a player 30 s per action behind wins within the same hour or so.
        Assert.InRange(r.Seconds / Minute, 38, 105); // 0 s: 48:10, 15 s: 59:22, 30 s: 88:16
    }

    [Fact]
    public void Bot_scans_jumps_and_researches_new_metals_after_the_win()
    {
        // The waves keep coming after the win, so this run measures how far research gets in the hour the base can hold out.
        var r = Play(continueAfterWin: true, minutes: 110);
        _out.WriteLine(r.Format());
        Assert.True(r.Has("VICTORY: waves survived with a station standing"), "no win");
        foreach (var name in new[]
        {
            "Researched logistics", "Researched carbon_drilling", "Researched jump_drive",
            "Scanned another system", "Researched titanium_smelting",
        })
            Assert.True(r.Has(name), $"'{name}' never happened");
        Assert.True(Min(r, "Researched jump_drive") <= 60, $"the jump drive came at minute {Min(r, "Researched jump_drive"):F1}"); // measured 46:13
        // ADR 0039: the ship carries no turrets, so the waves that keep coming after the win take it at about minute 61 (titanium smelting is at 59), before nickel smelting.
    }

    [Fact]
    public void Bot_researches_the_whole_tree_with_drones_when_left_alone()
    {
        // No waves: the economy alone. Flight with inertia, the deposit rocks and the planned approach poses make the bot's trips about 15 percent slower than before ADR 0033
        // (the last of the 17 nodes, the beam collimator, was done at minute 119 and is now at about 132 with the same iron), and the 12000 iron of the home deposit is gone by about minute 126:
        // the home deposits hold 125 percent here so the plan can finish (the win and the other tests are not affected). The two engine nodes of ADR 0033 (plates and carbon) are left to the science mode.
        var r = new StructureBot(TestContent.CoreFloor, Seed, threats: false, rules: new WorldRules { DepositRichnessPercent = 250 }) { ContinueAfterVictory = true }.Run(150 * 60); // 250: the laser and the drones are 2.5 times faster (ADR 0036), so the same ore is gone in 2.5 times less time
        _out.WriteLine(r.Format());
        // The electronics branch (silicon, circuits, shields, sensors, Mk2 power) is only part of the science mode (see the next test).
        var planned = TestContent.CoreFloor.Research.Where(n => n.Field == ResearchField.Basic && !n.Id.StartsWith("core:silicon_") && n.Id is not ("core:electronics" or "core:shielding" or "core:sensors" or "core:advanced_power" or "core:ion_thrusters" or "core:thrust_vectoring" or "core:composites" or "core:cryogenics" or "core:resonance" or "core:heavy_industry" or "core:large_structures" or "core:cruise_drive")).ToList();
        foreach (var node in planned)
            Assert.True(r.Has("Researched " + node.Id.Replace("core:", "")), $"'{node.Id}' was never researched");
        double last = planned.Max(n => Min(r, "Researched " + n.Id.Replace("core:", "")));
        Assert.True(last <= 140, $"the last node came at minute {last:F1}");
        Assert.True(Min(r, "First drone bay built") <= 45, $"the first drone bay came at minute {Min(r, "First drone bay built"):F1}");
        Assert.True(r.MinedByDrones > 2000, $"drones mined only {r.MinedByDrones}");
        // Hydrogen holds: the ice deposit of home (1500 to 2000) lasts, so no reactor ran dry for more than a few minutes in two hours.
        Assert.True(r.EmergencySeconds < 900, $"{r.EmergencySeconds} s on emergency power"); // ADR 0039: the 20 hydrogen of the start run out at about minute 9 and the cracker comes with the station factory at about 18: about 620 s of that is the opening
    }

    [Fact]
    public void Bot_in_science_mode_builds_the_three_chains_and_researches_the_whole_extended_tree()
    {
        // Left alone (no waves) on rich deposits: the tier needs more ore than the home system holds. Measured on seed 20261121: complete at about
        // minute 378 (345 on the stacked layout of ADR 0026: the wings are now one or two fabrication bays of the station each, ADR 0031); the bound is generous.
        var rules = new WorldRules { DepositRichnessPercent = 400 };
        var bot = new StructureBot(TestContent.CoreFloor, Seed, threats: false, rules: rules) { ContinueAfterVictory = true, Science = true };
        var r = bot.Run(520 * 60);
        _out.WriteLine(r.Format());
        foreach (var node in TestContent.CoreFloor.Research.Where(n => n.Field != ResearchField.Basic))
            Assert.True(r.Has("Researched " + node.Id.Replace("core:", "")), $"'{node.Id}' was never researched");
        foreach (var name in new[] { "silicon_processing", "electronics", "shielding", "sensors", "advanced_power", "advanced_plating" })
            Assert.True(r.Has("Researched " + name), $"'{name}' was never researched");
        Assert.True(r.Has("Science tier complete"));
        Assert.True(r.At("Science tier complete") / Minute <= 480, "the science tier took too long");
        Assert.True(bot.World.Network.ResearchItemsUsed > 0);
    }

    [Fact]
    public void Default_bot_path_does_not_touch_the_science_tier()
    {
        var r = Play(continueAfterWin: true, minutes: 100, threats: false);
        Assert.DoesNotContain(r.Timeline, t => t.Name.StartsWith("Researched ") && TestContent.CoreFloor.Research.Any(n => n.Field != ResearchField.Basic && t.Name == "Researched " + n.Id.Replace("core:", "")));
    }

    [Fact]
    public void Same_seed_same_timeline()
    {
        var a = Play(minutes: 30);
        var b = Play(minutes: 30);
        Assert.Equal(a.Timeline, b.Timeline);
        Assert.Equal(a.Stuck, b.Stuck);
    }

    [Fact]
    public void Bot_uses_commands_only_and_never_gives_itself_items()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !Directory.Exists(Path.Combine(dir.FullName, "tools", "Starfoundry.Balance"))) dir = dir.Parent;
        Assert.NotNull(dir);
        var files = Directory.GetFiles(Path.Combine(dir!.FullName, "tools", "Starfoundry.Balance"), "StructureBot*.cs");
        Assert.NotEmpty(files);
        foreach (var f in files)
        {
            var text = File.ReadAllText(f);
            Assert.DoesNotContain(".GiveItems(", text);
            Assert.DoesNotContain("AddStructureModuleUnchecked", text);
            Assert.DoesNotContain("GrantResearch", text);
            Assert.DoesNotContain("ScheduleWave", text);
        }
    }
}
