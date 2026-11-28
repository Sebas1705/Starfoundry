using Starfoundry.Ui;
using Xunit;

namespace Starfoundry.Sim.Tests;

public class LaunchFlagsTests
{
    [Theory]
    [InlineData("--agent")]
    [InlineData("--ui-lang=en")]
    [InlineData("--menu-shot=a.png")]
    [InlineData("--content=dir")]
    public void SessionFlagsDoNotScriptTheRun(string flag) => Assert.False(LaunchFlags.IsScripted(flag));

    [Theory]
    [InlineData("--scenario=06_salvage_run")]
    [InlineData("--seed=5")]
    [InlineData("--demo")]
    [InlineData("--view-origin=100000,0,-5")]
    [InlineData("--view-origin-check")]
    public void OtherFlagsDo(string flag) => Assert.True(LaunchFlags.IsScripted(flag));

    [Theory]
    [InlineData("--view-origin=100000,0,-5", 100000L, 0L, -5L)]
    [InlineData("--view-origin= 1 , 2 , 3 ", 1L, 2L, 3L)]
    [InlineData("--view-origin=10000000000,0,0", 10000000000L, 0L, 0L)]
    public void ViewOriginReadsThreeCellsAfterAnEqualsSign(string flag, long x, long y, long z)
    {
        Assert.True(LaunchFlags.TryViewOrigin(new[] { "--demo", flag }, out var cells));
        Assert.Equal((x, y, z), cells);
    }

    [Fact]
    public void ViewOriginReadsTheNextArgumentToo()
    {
        Assert.True(LaunchFlags.TryViewOrigin(new[] { "--view-origin", "-20000,5,7" }, out var cells));
        Assert.Equal((-20000L, 5L, 7L), cells);
    }

    [Theory]
    [InlineData("--demo")]
    [InlineData("--view-origin=1,2")]
    [InlineData("--view-origin=1,2,x")]
    [InlineData("--view-origin=1.5,2,3")]
    [InlineData("--view-origin=")]
    [InlineData("--view-origin-check")]
    public void ViewOriginIgnoresAbsentOrMalformedValues(string flag) => Assert.False(LaunchFlags.TryViewOrigin(new[] { flag }, out _));

    [Fact]
    public void ViewOriginWithoutAValueIsAbsent() => Assert.False(LaunchFlags.TryViewOrigin(new[] { "--view-origin" }, out _));
}
