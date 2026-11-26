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
    public void OtherFlagsDo(string flag) => Assert.True(LaunchFlags.IsScripted(flag));
}
