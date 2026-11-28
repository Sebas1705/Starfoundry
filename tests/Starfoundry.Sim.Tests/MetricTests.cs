using System.Globalization;
using Starfoundry.Sim;
using Starfoundry.Ui;
using Xunit;

namespace Starfoundry.Sim.Tests;

/// <summary>The metric display of physical quantities (ADR 0052): SI auto-scaling, rounding, locales, and the units of the simulation.</summary>
public class MetricTests
{
    private static readonly CultureInfo En = CultureInfo.GetCultureInfo("en-US");
    private static readonly CultureInfo Es = CultureInfo.GetCultureInfo("es-ES");

    [Theory]
    [InlineData(0, "0 m")]
    [InlineData(0.45, "0.45 m")]
    [InlineData(1, "1.0 m")]
    [InlineData(6.25, "6.3 m")]
    [InlineData(12.4, "12 m")]
    [InlineData(99.6, "100 m")]
    [InlineData(130.4, "130 m")]
    [InlineData(999.4, "999 m")]
    [InlineData(999.7, "1.0 km")]
    [InlineData(1800, "1.8 km")]
    [InlineData(12500, "13 km")]
    [InlineData(4_000_000, "4.0 Mm")]
    public void LengthScalesToTheRightUnitWithTwoSignificantDigits(double metres, string expected) => Assert.Equal(expected, Metric.Length(metres, En));

    [Fact]
    public void SpanishUsesADecimalCommaAndEnglishAPoint()
    {
        Assert.Equal("1,8 km", Metric.Length(1800, Es));
        Assert.Equal("1.8 km", Metric.Length(1800, En));
        Assert.Equal("6,3 m/s", Metric.Speed(6.25, Es));
        Assert.Equal("1,5 t", Metric.Mass(1500, Es));
        Assert.Equal("1.5 t", Metric.Mass(1500, En));
    }

    [Theory]
    [InlineData(0.5, "500 g")]
    [InlineData(2.5, "2.5 kg")]
    [InlineData(36, "36 kg")]
    [InlineData(999.7, "1.0 t")]
    [InlineData(1500, "1.5 t")]
    [InlineData(12_000, "12 t")]
    [InlineData(305_000_000, "305 kt")]
    [InlineData(2_000_000_000, "2.0 Mt")]
    public void MassScalesFromGramsToMegatonnes(double kg, string expected) => Assert.Equal(expected, Metric.Mass(kg, En));

    [Theory]
    [InlineData(0.012, "12 L")]
    [InlineData(0.0025, "2.5 L")]
    [InlineData(3.4, "3.4 m³")]
    public void VolumeUsesLitresBelowACubicMetre(double m3, string expected) => Assert.Equal(expected, Metric.Volume(m3, En));

    [Theory]
    [InlineData(100_000, "100 kW")]
    [InlineData(500_000, "500 kW")]
    [InlineData(10_000_000, "10 MW")]
    [InlineData(25_000_000, "25 MW")]
    [InlineData(1_800_000, "1.8 MW")]
    [InlineData(3e9, "3.0 GW")]
    public void PowerScalesFromWattsToGigawatts(double watts, string expected) => Assert.Equal(expected, Metric.Power(watts, En));

    [Fact]
    public void EnergyAndFuelMatchTheReactor()
    {
        // one hydrogen burns for 30 s in a 10 MW reactor: 300 MJ, which is 83 kWh
        double joules = Physical.Watts(100) * 900 / Units.TicksPerSecond;
        Assert.Equal(300e6, joules, 3);
        Assert.Equal("300 MJ", Metric.Energy(joules, En));
        Assert.Equal("83 kWh", Metric.EnergyKwh(joules, En));
        Assert.Equal("1.8 GJ", Metric.Energy(6 * joules, En));
    }

    [Fact]
    public void SpeedAccelerationAndFlowReadNaturally()
    {
        Assert.Equal("6.0 m/s", Metric.Speed(Physical.MetresPerSecondOfUnitsPerTick(PlayerRules.MoveUnitsPerTick), En));
        Assert.Equal("24 m/s", Metric.Speed(24, En));
        Assert.Equal("1.5 km/s", Metric.Speed(1500, En));
        Assert.Equal("22 km/h", Metric.SpeedKmh(6, En));
        Assert.Equal("5.0 m/s² (0.51 g)", Metric.Acceleration(5, En));
        Assert.Equal("600 kg/min", Metric.MassFlow(10, En));
        Assert.Equal("108 t/h", Metric.MassFlow(30, En));
        Assert.Equal("0.60 kg/min", Metric.MassFlow(0.01, En));
    }

    [Fact]
    public void PairsShareTheUnitOfTheLarger()
    {
        Assert.Equal("10/4.0 MW", Metric.PowerPair(10e6, 4e6, En));
        Assert.Equal("400/900 kW", Metric.PowerPair(400e3, 900e3, En));
        Assert.Equal("0.0/0.0 W", Metric.PowerPair(0, 0, En));
    }

    [Fact]
    public void NegativeAndNonFiniteValuesShowAsZeroAndSignedKeepsTheSign()
    {
        Assert.Equal("0 m", Metric.Length(-5, En));
        Assert.Equal("0 kg", Metric.Mass(double.NaN, En));
        Assert.Equal("0 m/s", Metric.Speed(double.PositiveInfinity, En));
        Assert.Equal("+1.5 t", Metric.Signed(Quantity.Mass, 1500, En));
        Assert.Equal("-2.5 kg", Metric.Signed(Quantity.Mass, -2.5, En));
        Assert.Equal("1.5", Metric.Number(1.5, En));
        Assert.Equal("0.0", Metric.Number(-0.0, En));
    }

    [Fact]
    public void PartsSplitTheNumberFromTheUnit()
    {
        Assert.Equal(("1.2", "km"), Metric.Parts("1.2 km"));
        Assert.Equal(("5.0", "m/s²"), Metric.Parts("5.0 m/s²"));
        Assert.Equal(("--", ""), Metric.Parts("--"));
    }

    [Fact]
    public void TheSimulationUnitsConvertToMetres()
    {
        Assert.Equal(1.0, Physical.Metres(1));
        Assert.Equal(1.0, Physical.MetresOfUnits(Units.CellUnits), 9);
        // 48 units per tick is 6 cells per second, which is 6 m/s; 12 units per tick (the docking limit) is 1.5 m/s
        Assert.Equal(6.0, Physical.MetresPerSecondOfUnitsPerTick(PlayerRules.MoveUnitsPerTick), 9);
        Assert.Equal(1.5, Physical.MetresPerSecondOfUnitsPerTick(DockRules.MaxSpeedUnitsPerTick), 9);
        Assert.Equal(18.75, Physical.MetresPerSecondOfUnitsPerTick(FeelRules.ProjectileSpeedUnits), 9);
        Assert.Equal(10_000_000.0, Physical.Watts(100), 6);
    }

    [Fact]
    public void ModuleSizeIsEightByFourByEightMetres()
    {
        Assert.Equal(8, StationGrid.ModuleX);
        Assert.Equal(4, StationGrid.ModuleY);
        Assert.Equal(8, StationGrid.ModuleZ);
        Assert.Equal(1.0, Physical.Table.CellMetres); // the body, the doors and the modules were drawn at one cell per metre
        Assert.Equal(1.7, BodyMotion.EyeHeight, 6);
    }

    [Fact]
    public void RocksHaveARealisticRadiusAndMass()
    {
        // the reference rock (1000 thousandths) is 1.3 m in radius, a sphere of 2,700 kg/m3: about 25 t
        Assert.Equal(1.3, Physical.RockRadiusMetres(1000), 6);
        Assert.InRange(Physical.RockKg(Physical.RockRadiusMetres(1000)), 24_000, 26_000);
        Assert.Equal(1.3 * 2, Physical.RockRadiusMetres(8000), 6);
    }

    [Fact]
    public void TheHoldStatRowShowsItemsAndTheNominalMass()
    {
        Assert.Equal("460 (≈12 t)", Metric.Format(Quantity.HoldItems, 460, En));
        Assert.Equal("60 (≈1.5 t)", Metric.Format(Quantity.HoldItems, 60, En));
    }
}
