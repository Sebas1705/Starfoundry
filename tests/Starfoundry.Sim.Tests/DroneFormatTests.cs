using Starfoundry.Ui;
using Xunit;

namespace Starfoundry.Sim.Tests;

public class DroneFormatTests
{
    private static DroneStatus S(int bays, bool working, int deposit, int rate = 0, bool waiting = false, bool power = true) =>
        new(bays, working, deposit, rate, waiting, power);

    [Fact]
    public void State_follows_bays_assignment_power_and_hold()
    {
        Assert.Equal(DroneState.NoBays, DroneFormat.State(S(0, false, -1)));
        Assert.Equal(DroneState.Idle, DroneFormat.State(S(1, false, -1)));
        Assert.Equal(DroneState.Working, DroneFormat.State(S(1, true, 3, 20)));
        Assert.Equal(DroneState.NoPower, DroneFormat.State(S(1, false, 3, power: false)));
        Assert.Equal(DroneState.HoldFull, DroneFormat.State(S(1, false, 3, waiting: true)));
    }

    [Theory]
    [InlineData(1, 2)]
    [InlineData(2, 4)]
    [InlineData(3, 6)]
    [InlineData(5, 6)]
    public void Visible_drones_are_two_per_bay_up_to_six(int bays, int expected) =>
        Assert.Equal(expected, DroneFormat.VisibleDrones(S(bays, true, 0, 20)));

    [Fact]
    public void No_drones_fly_when_idle_or_waiting()
    {
        Assert.Equal(0, DroneFormat.VisibleDrones(S(3, false, -1)));
        Assert.Equal(0, DroneFormat.VisibleDrones(S(3, false, 2, waiting: true)));
        Assert.Equal(0, DroneFormat.VisibleDrones(S(0, false, -1)));
    }

    [Fact]
    public void Line_is_empty_without_bays_and_names_the_rate_in_both_languages()
    {
        var old = Loc.Lang;
        try
        {
            Assert.Equal("", DroneFormat.Line("Ship", S(0, false, -1), "Iron"));
            Loc.Lang = Language.En;
            string en = DroneFormat.Line("Ship", S(2, true, 1, 40), "Iron");
            Assert.Contains("2 bay", en);
            Assert.Contains("40/min", en);
            Loc.Lang = Language.Es;
            Assert.Contains("40/min", DroneFormat.Line("Nave", S(2, true, 1, 40), "Hierro"));
            Assert.Contains("Nave", DroneFormat.Line("Nave", S(1, false, -1), "x"));
        }
        finally { Loc.Lang = old; }
    }
}
