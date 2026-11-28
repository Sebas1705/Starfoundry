using Starfoundry.Sim;
using Starfoundry.Ui;
using Xunit;

namespace Starfoundry.Sim.Tests;

public class ViewOriginTests
{
    private const long Cu = Units.CellUnits;

    [Fact]
    public void StartsAtZeroAndMatchesTheOldConversion()
    {
        var o = new ViewOrigin();
        Assert.Equal((0L, 0L, 0L), o.Cells);
        foreach (long u in new long[] { 0, 1, 239, 240, -7, 123456, -987654, 2_000_000_000 })
            Assert.Equal((float)(int)u / Units.CellUnits, o.ToViewX(u));
    }

    [Theory]
    [InlineData(0L)]
    [InlineData(100_000L)]        // 100 km
    [InlineData(10_000_000L)]     // 10,000 km
    [InlineData(1_000_000_000L)]  // 1e9 m
    public void ViewValuesNearTheShipAreExactWhateverTheDistance(long shipCells)
    {
        var o = new ViewOrigin();
        long snapped = ViewOrigin.Snap(shipCells);
        o.Set(shipCells, 0, 0);
        Assert.Equal(snapped, o.Cells.X);
        // A rock 37.5 cells from the ship: with the origin on the ship the view value is exact in float; without, it is not.
        long shipUnits = shipCells * Cu;
        long rock = shipUnits + 37 * Cu + 120;
        float view = o.ToViewX(rock);
        double truth = (rock - snapped * Cu) / (double)Cu;
        Assert.Equal(truth, view, 3);
        Assert.True(MathF.Abs(view) < 20_001f);
        // The shimmer test: two points a quarter of a metre apart stay distinct and ordered.
        float a = o.ToViewX(rock), b = o.ToViewX(rock + Cu / 4);
        Assert.Equal(0.25f, b - a, 3);
    }

    [Fact]
    public void WithoutAnOriginFarValuesLoseTheirPrecision()
    {
        // The reason for the whole class: at 1e9 m a float cannot tell two points a metre apart.
        float far = 1_000_000_000f, near = 1_000_000_001f;
        Assert.True(near - far is 0f or >= 64f);
    }

    [Theory]
    [InlineData(0L, 0L)]
    [InlineData(499L, 0L)]
    [InlineData(500L, 1000L)]
    [InlineData(1499L, 1000L)]
    [InlineData(1500L, 2000L)]
    [InlineData(-499L, 0L)]
    [InlineData(-500L, -1000L)]
    [InlineData(-1_000_000_123L, -1_000_000_000L)]
    public void SnapGoesToAMultipleOfOneThousand(long cells, long expected)
    {
        Assert.Equal(expected, ViewOrigin.Snap(cells));
        Assert.Equal(0, ViewOrigin.Snap(cells) % ViewOrigin.SnapCells);
    }

    [Fact]
    public void RebaseOnlyWhenTheShipIsFarEnough()
    {
        var o = new ViewOrigin(1000);
        Assert.False(o.NeedsRebase(1000 * Cu, 0, 0));
        Assert.True(o.NeedsRebase(1000 * Cu + 1, 0, 0));
        Assert.True(o.NeedsRebase(0, -1001 * Cu, 0));
        Assert.True(o.NeedsRebase(0, 0, 5000 * Cu));
        Assert.Equal(20000, ViewOrigin.RebaseCells);
        Assert.False(new ViewOrigin().NeedsRebase(20000 * Cu, 0, 0));
        Assert.True(new ViewOrigin().NeedsRebase(20001 * Cu, 0, 0));
    }

    [Fact]
    public void RebaseReturnsTheExactIntegerDelta()
    {
        var o = new ViewOrigin(1000);
        var d1 = o.RebaseTo(4_321, -2_600, 0);
        Assert.Equal((4000L, -3000L, 0L), d1);
        Assert.Equal((4000L, -3000L, 0L), o.Cells);
        var d2 = o.RebaseTo(5_100, -3_000, 9_999_999);
        Assert.Equal((1000L, 0L, 10_000_000L), d2);
        Assert.Equal((5000L, -3000L, 10_000_000L), o.Cells);
        Assert.Equal((5000L * Cu, -3000L * Cu, 10_000_000L * Cu), o.OriginUnits);
    }

    [Fact]
    public void ARebaseShiftsViewValuesByExactlyTheDelta()
    {
        var o = new ViewOrigin(1000);
        o.Set(0, 0, 0);
        long rock = (7_340L * Cu) + 61;
        float before = o.ToViewX(rock);
        var d = o.RebaseTo(7_300, 0, 0);
        float after = o.ToViewX(rock);
        Assert.Equal(7000L, d.X);
        Assert.Equal(before - d.X, after, 2); // the old value carried float32 rounding of its own
    }

    [Theory]
    [InlineData(0L, 12345L)]
    [InlineData(100_000L, -777L)]
    [InlineData(10_000_000L, 240L * 9)]
    [InlineData(1_000_000_000L, 5L)]
    public void ToUnitsRoundTrips(long originCells, long offsetUnits)
    {
        var o = new ViewOrigin();
        o.Set(originCells, originCells, 0);
        long units = o.Cells.X * Cu + offsetUnits;
        Assert.Equal(units, o.ToUnitsX(o.ToViewX(units)));
        Assert.Equal(o.Cells.Y * Cu - offsetUnits, o.ToUnitsY(o.ToViewY(o.Cells.Y * Cu - offsetUnits)));
    }
}
