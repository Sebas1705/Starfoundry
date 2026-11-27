using Starfoundry.Sim;
using Starfoundry.Ui;
using Xunit;

namespace Starfoundry.Sim.Tests;

public class SaveSlotsTests
{
    [Fact]
    public void Load_failures_map_to_localised_reasons()
    {
        Assert.Equal("toast.loadFailed.content", SaveSlots.LoadFailureKey(new InvalidDataException("The save was made with other game content and cannot be loaded.")));
        Assert.Equal("toast.loadFailed.scenario", SaveSlots.LoadFailureKey(new InvalidDataException("The save belongs to the scenario 'x', which this game does not have.")));
        Assert.Equal("toast.loadFailed.newer", SaveSlots.LoadFailureKey(new InvalidDataException("Unsupported log version 99.")));
        Assert.Equal("toast.loadFailed.newer", SaveSlots.LoadFailureKey(new SaveFileException(SaveProblem.NewerFormat, "newer")));
        Assert.Equal("toast.loadFailed.corrupt", SaveSlots.LoadFailureKey(new SaveFileException(SaveProblem.Corrupt, "bad")));
        Assert.Equal("toast.loadFailed.corrupt", SaveSlots.LoadFailureKey(new InvalidDataException("Not a save file.")));
        Assert.Equal("toast.loadFailed.corrupt", SaveSlots.LoadFailureKey(new EndOfStreamException()));
        Assert.Equal("toast.loadFailed.notFound", SaveSlots.LoadFailureKey(new FileNotFoundException()));
    }

    [Fact]
    public void A_garbage_file_reads_as_corrupt_and_every_reason_has_text()
    {
        var ex = Record.Exception(() => SaveGame.Read(new MemoryStream(new byte[64]), TestContent.Core, out _));
        Assert.NotNull(ex);
        Assert.Equal("toast.loadFailed.corrupt", SaveSlots.LoadFailureKey(ex!));
        foreach (var k in new[] { "toast.loadFailed.notFound", "toast.loadFailed.content", "toast.loadFailed.corrupt", "toast.loadFailed.newer", "toast.loadFailed.scenario" })
            Assert.DoesNotContain("⟦", Loc.T(k));
    }
}
