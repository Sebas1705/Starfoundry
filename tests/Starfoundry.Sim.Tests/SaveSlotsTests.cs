using Starfoundry.Sim;
using Starfoundry.Ui;
using Xunit;

namespace Starfoundry.Sim.Tests;

public class SaveSlotsTests
{
    private static Func<string, ulong?> Times(ulong? quick, ulong? auto) =>
        name => name == SaveSlots.Quick ? quick : name == SaveSlots.Auto ? auto : null;

    [Fact]
    public void Newest_picks_the_most_recent_slot()
    {
        Assert.Equal(SaveSlots.Auto, SaveSlots.Newest(Times(100, 200)));
        Assert.Equal(SaveSlots.Quick, SaveSlots.Newest(Times(300, 200)));
    }

    [Fact]
    public void Newest_handles_missing_slots_and_ties()
    {
        Assert.Null(SaveSlots.Newest(Times(null, null)));
        Assert.Equal(SaveSlots.Auto, SaveSlots.Newest(Times(null, 5)));
        Assert.Equal(SaveSlots.Quick, SaveSlots.Newest(Times(5, null)));
        Assert.Equal(SaveSlots.Quick, SaveSlots.Newest(Times(7, 7)));
    }

    [Fact]
    public void Names_for_atomic_writes_and_rotation()
    {
        Assert.Equal("quicksave.tmp", SaveSlots.TempName("quicksave"));
        Assert.Equal("quicksave.bak", SaveSlots.PreviousName("quicksave"));
        Assert.Equal(SaveSlots.AutoPrevious, SaveSlots.PreviousName(SaveSlots.Auto));
        Assert.DoesNotContain(SaveSlots.AutoPrevious, SaveSlots.Loadable);
    }

    [Fact]
    public void Load_failures_map_to_localised_reasons()
    {
        Assert.Equal("toast.loadFailed.content", SaveSlots.LoadFailureKey(new InvalidDataException("The save was made with other game content and cannot be loaded.")));
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
        foreach (var k in new[] { "toast.loadFailed.notFound", "toast.loadFailed.content", "toast.loadFailed.corrupt" })
            Assert.DoesNotContain("⟦", Loc.T(k));
    }
}
