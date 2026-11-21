using Starfoundry.Ui;
using Xunit;

namespace Starfoundry.Sim.Tests;

public class PowerFormatTests
{
    private static PowerStatus S(int sat, int hydrogen, int burn, int production = 100, int demand = 28) =>
        new(production, demand, sat, burn, hydrogen, production > 0);

    [Fact]
    public void TimeLeft_counts_hold_hydrogen_and_the_running_burn()
    {
        Assert.Equal(20 * 900 + 400, PowerFormat.TimeLeftTicks(S(1000, 20, 400)));
        Assert.Equal(0, PowerFormat.TimeLeftTicks(S(0, 0, 0, 0)));
        Assert.Equal(3 * 600 + 10, PowerFormat.TimeLeftTicks(S(1000, 3, 10), 600));
    }

    [Theory]
    [InlineData(1000, PowerBand.Full)]
    [InlineData(1200, PowerBand.Full)]
    [InlineData(999, PowerBand.Strained)]
    [InlineData(500, PowerBand.Strained)]
    [InlineData(499, PowerBand.Critical)]
    [InlineData(0, PowerBand.Critical)]
    public void Band_splits_at_full_and_half(int sat, PowerBand expected) => Assert.Equal(expected, PowerFormat.Band(sat));

    [Theory]
    [InlineData(0, "0:00")]
    [InlineData(1, "0:01")]
    [InlineData(30, "0:01")]
    [InlineData(31, "0:02")]
    [InlineData(1800, "1:00")]
    [InlineData(18000, "10:00")]
    [InlineData(18000 + 450, "10:15")]
    public void Clock_is_minutes_and_seconds_rounded_up(long ticks, string text) => Assert.Equal(text, PowerFormat.Clock(ticks));

    [Fact]
    public void FuelLine_says_no_fuel_at_zero_in_both_languages()
    {
        var old = Loc.Lang;
        try
        {
            Loc.Lang = Language.En;
            Assert.Equal("Hydrogen 0  -  no fuel", PowerFormat.FuelLine(S(0, 0, 0, 0)));
            Assert.Equal("Hydrogen 20  -  fuel 10:00", PowerFormat.FuelLine(S(1000, 20, 0)));
            Loc.Lang = Language.Es;
            Assert.Contains("sin combustible", PowerFormat.FuelLine(S(0, 0, 0, 0)));
        }
        finally { Loc.Lang = old; }
    }

    [Fact]
    public void FuelWatch_warns_once_per_threshold_and_rearms()
    {
        var w2 = new FuelWatch();
        Assert.Equal(PowerWarning.None, w2.Update(S(1000, 10, 0), true)); // 300 s: fine
        Assert.Equal(PowerWarning.LowFuel, w2.Update(S(1000, 3, 0), true)); // 90 s
        Assert.Equal(PowerWarning.None, w2.Update(S(1000, 3, 0), true));
        Assert.Equal(PowerWarning.None, w2.Update(S(1000, 2, 800), true));
        Assert.Equal(PowerWarning.LastSeconds, w2.Update(S(1000, 0, 800), true));    // 800 ticks = 26.7 s
        Assert.Equal(PowerWarning.None, w2.Update(S(1000, 0, 700), true));
        Assert.Equal(PowerWarning.Out, w2.Update(S(0, 0, 0, 0), true));
        Assert.Equal(PowerWarning.None, w2.Update(S(0, 0, 0, 0), true));
        // Hydrogen made: everything arms again.
        Assert.Equal(PowerWarning.None, w2.Update(S(1000, 10, 0), true));
        Assert.Equal(PowerWarning.LowFuel, w2.Update(S(1000, 2, 0), true));
    }

    [Fact]
    public void FuelWatch_first_reading_gives_only_the_most_urgent_warning()
    {
        var w = new FuelWatch();
        Assert.Equal(PowerWarning.LastSeconds, w.Update(S(1000, 0, 300), true));
        Assert.Equal(PowerWarning.None, w.Update(S(1000, 0, 200), true));
    }

    [Fact]
    public void FuelWatch_ignores_ships_without_a_reactor()
    {
        var w = new FuelWatch();
        Assert.Equal(PowerWarning.None, w.Update(S(0, 5, 0, 0), false));
    }
}
