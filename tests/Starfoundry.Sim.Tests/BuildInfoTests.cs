using Starfoundry.Ui;
using Xunit;

namespace Starfoundry.Sim.Tests;

/// <summary>The VERSION file is the single source of the product version (docs/release.md).</summary>
public class BuildInfoTests
{
    private static string Root()
    {
        string? dir = AppContext.BaseDirectory;
        while (dir is not null && !File.Exists(Path.Combine(dir, "VERSION"))) dir = Path.GetDirectoryName(dir);
        return dir ?? throw new InvalidOperationException("VERSION not found above " + AppContext.BaseDirectory);
    }

    [Fact]
    public void TheVersionFileIsASemanticVersion()
    {
        string v = File.ReadAllText(Path.Combine(Root(), "VERSION")).Trim();
        Assert.Matches(@"^\d+\.\d+\.\d+(-[0-9A-Za-z.-]+)?$", v);
    }

    [Fact]
    public void TheBuiltVersionIsTheVersionFile()
    {
        Assert.Equal(File.ReadAllText(Path.Combine(Root(), "VERSION")).Trim(), BuildInfo.Version);
        Assert.Equal("Starfoundry " + BuildInfo.Version, BuildInfo.WindowTitle);
    }

    [Theory]
    [InlineData("0.1.0+abc123", "0.1.0")]
    [InlineData("1.2.3-rc.1", "1.2.3-rc.1")]
    [InlineData("1.0.0", "1.0.0")]
    public void CleanDropsBuildMetadata(string raw, string expected) => Assert.Equal(expected, BuildInfo.Clean(raw));
}
