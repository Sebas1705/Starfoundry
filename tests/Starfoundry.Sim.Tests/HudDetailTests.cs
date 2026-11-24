using Starfoundry.Ui;
using Xunit;

namespace Starfoundry.Sim.Tests;

/// <summary>The HUD detail levels (minimal, standard, detailed) and the motion helpers behind the reactivity of the interface. Engine-free.</summary>
public class HudDetailTests
{
    private static readonly HudDetail[] Levels = { HudDetail.Minimal, HudDetail.Standard, HudDetail.Detailed };
    private static readonly HudContext[] Contexts = Enum.GetValues<HudContext>();

    // ---- the rules ------------------------------------------------------------------------------------

    [Fact]
    public void NoLevelShowsMoreGroupsThanTheCeiling()
    {
        foreach (var d in Levels)
            foreach (var c in Contexts)
                Assert.True(HudDetails.Count(HudDetails.Groups(d, c)) <= HudDetails.Ceiling, $"{d} {c}");
    }

    [Fact]
    public void EachLevelContainsTheOneBelowIt()
    {
        foreach (var c in Contexts)
        {
            var min = HudDetails.Groups(HudDetail.Minimal, c);
            var std = HudDetails.Groups(HudDetail.Standard, c);
            var det = HudDetails.Groups(HudDetail.Detailed, c);
            Assert.Equal(min, min & std);
            Assert.Equal(std, std & det);
            var rmin = HudDetails.Rows(HudDetail.Minimal, c);
            var rstd = HudDetails.Rows(HudDetail.Standard, c);
            var rdet = HudDetails.Rows(HudDetail.Detailed, c);
            Assert.Equal(rmin, rmin & rstd);
            Assert.Equal(rstd, rstd & rdet);
        }
    }

    [Fact]
    public void TheKeyPanelIsInEveryContextAtEveryLevel()
    {
        foreach (var d in Levels)
            foreach (var c in Contexts)
                Assert.True(HudDetails.Groups(d, c).HasFlag(HudGroup.Keys), $"{d} {c}");
    }

    [Fact]
    public void StandardIsRicherThanTheMinimalHudWhileFlying()
    {
        var std = HudDetails.Groups(HudDetail.Standard, HudContext.Flying);
        Assert.True(std.HasFlag(HudGroup.Target) && std.HasFlag(HudGroup.Hold) && std.HasFlag(HudGroup.Markers));
        Assert.True(HudDetails.Rows(HudDetail.Standard, HudContext.Flying).HasFlag(HudRow.HoldForecast));
        Assert.False(HudDetails.Rows(HudDetail.Standard, HudContext.Flying).HasFlag(HudRow.IncomePerMinute));
    }

    [Fact]
    public void OnlyTheDetailedLevelHasTheBigNumbers()
    {
        foreach (var c in Contexts)
        {
            Assert.False(HudDetails.Rows(HudDetail.Standard, c).HasFlag(HudRow.PowerRisk));
            Assert.False(HudDetails.Rows(HudDetail.Standard, c).HasFlag(HudRow.ModulesDamaged));
            Assert.False(HudDetails.Rows(HudDetail.Standard, c).HasFlag(HudRow.ObjectiveProgress));
        }
        var det = HudDetails.Rows(HudDetail.Detailed, HudContext.Flying);
        Assert.True(det.HasFlag(HudRow.PowerRisk) && det.HasFlag(HudRow.ModulesDamaged) && det.HasFlag(HudRow.IncomePerMinute)
            && det.HasFlag(HudRow.ObjectiveProgress) && det.HasFlag(HudRow.ThreeThreats));
    }

    [Fact]
    public void RowsBelongToGroupsThatShow()
    {
        foreach (var d in Levels)
            foreach (var c in Contexts)
            {
                var g = HudDetails.Groups(d, c);
                var r = HudDetails.Rows(d, c);
                if (r.HasFlag(HudRow.Hull) || r.HasFlag(HudRow.PowerRisk)) Assert.True(g.HasFlag(HudGroup.Vitals));
                if (r.HasFlag(HudRow.SpeedNumber)) Assert.True(g.HasFlag(HudGroup.Flight));
                if (r.HasFlag(HudRow.HoldForecast)) Assert.True(g.HasFlag(HudGroup.Hold));
                if (r.HasFlag(HudRow.TargetDistance)) Assert.True(g.HasFlag(HudGroup.Target));
                if (r.HasFlag(HudRow.WeaponState)) Assert.True(g.HasFlag(HudGroup.Weapons));
                if (r.HasFlag(HudRow.ObjectiveProgress)) Assert.True(g.HasFlag(HudGroup.Objective));
            }
    }

    [Fact]
    public void CombatHasTheTargetAndWeaponsWalkingHasNeither()
    {
        var combat = HudDetails.Groups(HudDetail.Standard, HudContext.Combat);
        Assert.True(combat.HasFlag(HudGroup.Target) && combat.HasFlag(HudGroup.Weapons));
        foreach (var d in Levels)
        {
            var walk = HudDetails.Groups(d, HudContext.Walking);
            Assert.False(walk.HasFlag(HudGroup.Flight) || walk.HasFlag(HudGroup.Target) || walk.HasFlag(HudGroup.Weapons));
        }
        Assert.True(HudDetails.Groups(HudDetail.Standard, HudContext.Building).HasFlag(HudGroup.Tool));
    }

    [Theory]
    [InlineData(false, true, true, true, true, HudContext.Building)]
    [InlineData(true, false, false, false, false, HudContext.Walking)]
    [InlineData(true, false, true, true, false, HudContext.Docked)]
    [InlineData(false, false, false, true, true, HudContext.Combat)]
    [InlineData(false, false, true, false, true, HudContext.Docked)]
    [InlineData(false, false, false, false, true, HudContext.Mining)]
    [InlineData(false, false, false, false, false, HudContext.Flying)]
    public void TheContextFollowsWhatThePlayerIsDoing(bool standing, bool building, bool docked, bool threat, bool mining, HudContext expected) =>
        Assert.Equal(expected, HudDetails.ContextOf(standing, building, docked, threat, mining));

    [Fact]
    public void ThreeArrowsOnlyWhenDetailed()
    {
        Assert.Equal(1, HudDetails.ThreatArrows(HudDetail.Minimal));
        Assert.Equal(1, HudDetails.ThreatArrows(HudDetail.Standard));
        Assert.Equal(3, HudDetails.ThreatArrows(HudDetail.Detailed));
    }

    // ---- the id, the cycle and the settings -----------------------------------------------------------------

    [Fact]
    public void TheLevelCyclesThroughThreeAndBack()
    {
        var d = HudDetail.Minimal;
        var seen = new List<HudDetail>();
        for (int i = 0; i < 6; i++) { seen.Add(d); d = HudDetails.Next(d); }
        Assert.Equal(new[] { HudDetail.Minimal, HudDetail.Standard, HudDetail.Detailed, HudDetail.Minimal, HudDetail.Standard, HudDetail.Detailed }, seen);
    }

    [Fact]
    public void IdsRoundTripAndUnknownFallsBackToStandard()
    {
        foreach (var d in Levels)
        {
            Assert.True(HudDetails.TryParse(HudDetails.Id(d).ToUpperInvariant(), out var back));
            Assert.Equal(d, back);
        }
        Assert.False(HudDetails.TryParse("banana", out var fallback));
        Assert.Equal(HudDetail.Standard, fallback);
        Assert.False(HudDetails.TryParse(null, out _));
    }

    [Fact]
    public void TheSettingsKeepTheLevelAndOldFilesGetStandard()
    {
        foreach (var d in Levels)
        {
            var loaded = GameSettings.Parse((GameSettings.Defaults() with { HudDetail = d }).Serialize());
            Assert.Equal(d, loaded.HudDetail);
        }
        Assert.Equal(HudDetail.Standard, GameSettings.Parse("language=en\nhud_mode=cockpit\n").HudDetail);
        Assert.Equal(HudDetail.Minimal, GameSettings.Parse("hud_detail=banana\n", GameSettings.Defaults() with { HudDetail = HudDetail.Minimal }).HudDetail);
        Assert.Equal(HudDetail.Standard, (GameSettings.Defaults() with { HudDetail = (HudDetail)77 }).Clamped().HudDetail);
    }

    [Fact]
    public void TheSoundSettingsDefaultOnAndRoundTrip()
    {
        var d = GameSettings.Defaults();
        Assert.True(d.UiClickSound && d.UiHoverSound);
        var off = GameSettings.Parse((d with { UiClickSound = false, UiHoverSound = false }).Serialize());
        Assert.False(off.UiClickSound);
        Assert.False(off.UiHoverSound);
        Assert.True(GameSettings.Parse("language=en\n").UiClickSound);
    }

    [Fact]
    public void EveryHudStringHasBothLanguages()
    {
        foreach (var key in new[] { "hud.detail.minimal", "hud.detail.standard", "hud.detail.detailed", "cp.detail", "toast.hudDetail", "settings.uiClick", "settings.uiHover",
            "hud.row.powerOk", "hud.row.modulesDamaged", "hud.target.closing", "hud.hold.forecast", "hud.obj.step", "hud.weapons.armed" })
        {
            Loc.Lang = Language.Es;
            string es = Loc.T(key, "1", "2", "3");
            Loc.Lang = Language.En;
            string en = Loc.T(key, "1", "2", "3");
            Assert.NotEqual(key, es);
            Assert.NotEqual(key, en);
            Assert.NotEqual(es, en);
        }
        Loc.Lang = Language.Es;
    }

    // ---- the key panel at the minimal level -----------------------------------------------------------------

    private static KeyRow R(string id, KeyRowState state = KeyRowState.Normal) => new(id, new[] { "X" }, "key." + id, state);

    [Fact]
    public void MinimalDropsTheLearnedRowsButKeepsAlertsAndLitRows()
    {
        var groups = new[]
        {
            new KeyGroup("key.ctx.alert", Array.Empty<object>(), new[] { R("weapons", KeyRowState.Alert) }),
            new KeyGroup("key.ctx.ship", Array.Empty<object>(), new[] { R("build"), R("research"), R("map", KeyRowState.Active), R("seat"), R("help"), R("hudmode"), R("roll", KeyRowState.Info) }),
            new KeyGroup("key.ctx.other", Array.Empty<object>(), new[] { R("build"), R("scan") }),
        };
        var trimmed = HudDetails.TrimKeys(groups, HudDetail.Minimal);
        Assert.Equal(2, trimmed.Count); // the group with only learned rows is gone
        Assert.Equal(new[] { "weapons" }, trimmed[0].Rows.Select(r => r.Id));
        Assert.Equal(new[] { "map", "seat", "help", "hudmode" }, trimmed[1].Rows.Select(r => r.Id));
        Assert.Same(groups, HudDetails.TrimKeys(groups, HudDetail.Standard));
        Assert.Same(groups, HudDetails.TrimKeys(groups, HudDetail.Detailed));
    }

    [Fact]
    public void MinimalKeepsHelpAndTheHudKeyInEveryRealContext()
    {
        var states = new[]
        {
            new KeyState { StructuresOn = true, CanWalk = true },
            new KeyState { StructuresOn = true, CanWalk = true, Standing = true },
            new KeyState { ThreatActive = true, ThreatNow = true },
        };
        foreach (var s in states)
        {
            var rows = HudDetails.TrimKeys(KeyHints.For(s), HudDetail.Minimal).SelectMany(g => g.Rows).Select(r => r.Id).ToList();
            Assert.Contains("help", rows);
            Assert.Contains("hudmode", rows);
            Assert.True(rows.Count <= HudDetails.TrimKeys(KeyHints.For(s), HudDetail.Standard).SelectMany(g => g.Rows).Count());
        }
    }

    // ---- easing and trackers ------------------------------------------------------------------------------

    [Fact]
    public void EasingCurvesStartAtZeroEndAtOneAndNeverGoBackwards()
    {
        foreach (Func<float, float> ease in new Func<float, float>[] { UiMotion.EaseOutCubic, UiMotion.EaseInOutCubic })
        {
            Assert.Equal(0f, ease(0f), 5);
            Assert.Equal(1f, ease(1f), 5);
            Assert.Equal(0f, ease(-3f), 5);
            Assert.Equal(1f, ease(9f), 5);
            float prev = 0f;
            for (int i = 1; i <= 100; i++)
            {
                float v = ease(i / 100f);
                Assert.InRange(v, prev - 1e-6f, 1.000001f);
                prev = v;
            }
        }
        Assert.True(UiMotion.EaseOutCubic(0.5f) > 0.5f); // out: fast start
        Assert.Equal(0.5f, UiMotion.EaseInOutCubic(0.5f), 5);
    }

    [Fact]
    public void DurationsAreInTheRequestedRange()
    {
        foreach (float d in new[] { UiMotion.Fast, UiMotion.Normal, UiMotion.Slow }) Assert.InRange(d, 0.0999f, 0.1601f);
        Assert.Equal(1f, UiMotion.Progress(5f, 0f));
        Assert.Equal(0.5f, UiMotion.Progress(0.05f, 0.1f), 5);
    }

    [Fact]
    public void ApproachDoesNotDependOnTheFrameRateAndNeverOvershoots()
    {
        float a = 0f, b = 0f;
        for (int i = 0; i < 60; i++) a = UiMotion.Approach(a, 10f, 1f / 60f, 8f);
        for (int i = 0; i < 240; i++) b = UiMotion.Approach(b, 10f, 1f / 240f, 8f);
        Assert.Equal(a, b, 3);
        Assert.InRange(a, 0f, 10f);
        Assert.Equal(3f, UiMotion.Approach(3f, 3f, 0.016f, 9f));
        Assert.Equal(2f, UiMotion.Approach(2f, 9f, 0f, 9f));
    }

    [Fact]
    public void ValueTweenShowsTheFirstValueAtOnceThenEases()
    {
        var t = new ValueTween();
        t.Set(5f);
        Assert.Equal(5f, t.Current);
        t.Set(10f);
        Assert.Equal(5f, t.Current);
        Assert.True(t.Step(0.016f));
        Assert.InRange(t.Current, 5f, 10f);
        for (int i = 0; i < 200; i++) t.Step(0.016f);
        Assert.Equal(10f, t.Current);
        Assert.True(t.Settled);
        Assert.False(t.Step(0.016f));
        t.Set(2f, instant: true);
        Assert.Equal(2f, t.Current);
    }

    [Fact]
    public void CounterTweenCountsUpWithoutStallingAndEndsOnTheTarget()
    {
        var c = new CounterTween();
        c.Set(0);
        c.Set(100);
        Assert.Equal(0, c.Value);
        int last = 0, steps = 0;
        while (c.Step(1f / 60f))
        {
            Assert.True(c.Value >= last);
            last = c.Value;
            Assert.True(++steps < 600, "it must settle");
        }
        Assert.Equal(100, c.Value);
        c.Set(97);
        while (c.Step(1f / 60f)) { }
        Assert.Equal(97, c.Value);
    }

    [Fact]
    public void RefreshGateRunsAtMostFifteenTimesASecondUnlessForced()
    {
        var gate = new RefreshGate(15f);
        int runs = 0;
        for (int i = 0; i < 120; i++) if (gate.Due(1f / 60f)) runs++; // two seconds at 60 frames per second
        Assert.InRange(runs, 29, 31);
        Assert.True(gate.Due(0.001f, force: true));
        gate.Invalidate();
        Assert.True(gate.Due(0.001f));
        Assert.True(new RefreshGate(15f).Due(0f)); // the first call is always due
    }

    [Fact]
    public void IncomeIsSilentUntilItHasWatchedAndIgnoresUnloading()
    {
        var t = new IncomeTracker();
        t.Add(0f, 10);
        t.Add(2f, 12);
        Assert.Equal(0f, t.PerMinute); // under four seconds of watching
        for (int s = 3; s <= 10; s++) t.Add(s, 10 + 2 * s);
        Assert.InRange(t.PerMinute, 110f, 125f); // two items a second, about 120 a minute
        Assert.InRange(t.SecondsToFull(50, 100), 24f, 27f);
        t.Add(11f, 0); // unloaded: not negative income
        Assert.True(t.PerMinute > 0f);
        Assert.Equal(-1f, t.SecondsToFull(100, 100));
        t.Reset();
        Assert.Equal(0f, t.PerMinute);
        Assert.Equal(-1f, t.SecondsToFull(0, 100));
    }

    [Fact]
    public void ClosingSpeedIsPositiveWhenTheDistanceShrinks()
    {
        var c = new ClosingSpeed();
        float d = 100f;
        for (int i = 0; i < 120; i++) { d -= 0.2f; c.Update(d, 0.016f); } // 12.5 cells a second
        Assert.InRange(c.Value, 11f, 13.5f);
        for (int i = 0; i < 240; i++) { d += 0.1f; c.Update(d, 0.016f); }
        Assert.True(c.Value < 0f);
        c.Update(d + 500f, 0.016f); // a new target far away: reset, not a spike
        Assert.Equal(0f, c.Value);
    }
}
