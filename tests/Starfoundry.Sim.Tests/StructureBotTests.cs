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
public class StructureBotTests
{
    private const ulong Seed = 20261121UL;
    private const double Minute = 60;

    private readonly ITestOutputHelper _out;
    public StructureBotTests(ITestOutputHelper output) => _out = output;

    private static StructureBotReport Play(ulong seed = Seed, double delay = 6, bool continueAfterWin = false, int minutes = 120, bool threats = true) =>
        new StructureBot(TestContent.Core, seed, threats) { ActionDelaySeconds = delay, ContinueAfterVictory = continueAfterWin }.Run(minutes * 60);

    private static double Min(StructureBotReport r, string milestone) => r.At(milestone) / Minute;

    [Fact]
    public void Bot_reaches_each_milestone()
    {
        var r = Play();
        _out.WriteLine(r.Format());
        // Each milestone: its measured minute (seed 20261121, 6 s per action) and a generous bound.
        var bounds = new (string Name, double MaxMinutes)[]
        {
            ("Hand mining the home iron deposit", 1),         // measured 0:01
            ("First iron ingot in the hold", 1),              // 0:10
            ("First iron plate in the hold", 2),              // 0:19
            ("Station founded", 5),                           // 1:48
            ("Hydrogen cracker built", 12),                   // 5:21
            ("Hydrogen loop: first hydrogen made from ice", 15), // 6:05
            ("First defence module built", 20),               // 8:59
            ("First turret built", 22),                       // 10:01
            ("First turret loaded with ammo", 24),            // 10:15
            ("Station powered with machines running", 25),    // 12:38
            ("Laboratory built", 30),                         // 16:26
            ("First repair bay built", 30),                   // 16:50
            ("First research finished", 40),                  // 20:03
            ("Wave 1 warning", 30),                           // 15:26
            ("Wave 1 survived", 32),                          // 16:08
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
    public void Bot_wins_the_campaign()
    {
        var r = Play();
        _out.WriteLine(r.Format());
        Assert.Equal(OutcomeKind.Won, r.Outcome);
        Assert.Equal(OutcomeReason.WavesSurvived, r.Reason);
        Assert.True(r.WavesSurvived >= GoalSettings.DefaultWaves, $"only {r.WavesSurvived} waves survived");
        Assert.InRange(r.Seconds / Minute, 38, 60); // measured 42 to 44 on five seeds: the waves and the jump drive research decide
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
        Assert.InRange(r.Seconds / Minute, 38, 60); // measured 42 to 44 on five seeds: the waves and the jump drive research decide
    }

    [Theory]
    [InlineData(0.0)]
    [InlineData(30.0)]
    public void Bot_wins_at_any_pace(double delay)
    {
        var r = Play(delay: delay);
        Assert.True(r.Outcome == OutcomeKind.Won, $"delay {delay}: " + r.Format());
        // The wave clock decides, not the player: even a player 30 s per action behind wins within the same hour or so.
        Assert.InRange(r.Seconds / Minute, 38, 90); // 0 s: 44, 30 s: 77
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
            "Scanned another system", "Researched titanium_smelting", "Researched nickel_smelting",
        })
            Assert.True(r.Has(name), $"'{name}' never happened");
        Assert.True(Min(r, "Researched jump_drive") <= 60, $"the jump drive came at minute {Min(r, "Researched jump_drive"):F1}");
        Assert.True(Min(r, "Researched nickel_smelting") <= 100, $"nickel smelting came at minute {Min(r, "Researched nickel_smelting"):F1}");
    }

    [Fact]
    public void Bot_researches_the_whole_tree_with_drones_when_left_alone()
    {
        // No waves: the economy alone. Measured (seed 20261121): the last of the 17 nodes at about minute 93, the first drone at 30.
        var r = Play(continueAfterWin: true, minutes: 120, threats: false);
        _out.WriteLine(r.Format());
        // The electronics branch (silicon, circuits, shields, sensors, Mk2 power) is not part of the bot's plan.
        var planned = TestContent.Core.Research.Where(n => n.Field == ResearchField.Basic && !n.Id.StartsWith("core:silicon_") && n.Id is not ("core:electronics" or "core:shielding" or "core:sensors" or "core:advanced_power")).ToList();
        foreach (var node in planned)
            Assert.True(r.Has("Researched " + node.Id.Replace("core:", "")), $"'{node.Id}' was never researched");
        double last = planned.Max(n => Min(r, "Researched " + n.Id.Replace("core:", "")));
        Assert.True(last <= 110, $"the last node came at minute {last:F1}");
        Assert.True(Min(r, "First drone bay built") <= 40, $"the first drone bay came at minute {Min(r, "First drone bay built"):F1}");
        Assert.True(r.MinedByDrones > 2000, $"drones mined only {r.MinedByDrones}");
        // Hydrogen holds: the ice deposit of home (1500 to 2000) lasts, so no reactor ran dry for more than a few minutes in two hours.
        Assert.True(r.EmergencySeconds < 600, $"{r.EmergencySeconds} s on emergency power");
    }

    [Fact]
    public void Bot_in_science_mode_builds_the_three_chains_and_researches_the_whole_extended_tree()
    {
        // Left alone (no waves) on rich deposits: the tier needs more ore than the home system holds. Measured on seed 20261121:
        // complete at about minute 345 (see docs/prototypes/research.md); the bound is generous.
        var rules = new WorldRules { DepositRichnessPercent = 400 };
        var bot = new StructureBot(TestContent.Core, Seed, threats: false, rules: rules) { ContinueAfterVictory = true, Science = true };
        var r = bot.Run(500 * 60);
        _out.WriteLine(r.Format());
        foreach (var node in TestContent.Core.Research.Where(n => n.Field != ResearchField.Basic))
            Assert.True(r.Has("Researched " + node.Id.Replace("core:", "")), $"'{node.Id}' was never researched");
        Assert.True(r.Has("Science tier complete"));
        Assert.True(r.At("Science tier complete") / Minute <= 480, "the science tier took too long");
        Assert.True(bot.World.Network.ResearchItemsUsed > 0);
    }

    [Fact]
    public void Default_bot_path_does_not_touch_the_science_tier()
    {
        var r = Play(continueAfterWin: true, minutes: 100, threats: false);
        Assert.DoesNotContain(r.Timeline, t => t.Name.StartsWith("Researched ") && TestContent.Core.Research.Any(n => n.Field != ResearchField.Basic && t.Name == "Researched " + n.Id.Replace("core:", "")));
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
