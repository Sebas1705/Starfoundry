using Starfoundry.Sim;
using Starfoundry.Ui;
using Xunit;

namespace Starfoundry.Sim.Tests;

public class RejectTextsTests
{
    [Fact]
    public void Every_reject_has_a_text()
    {
        var table = Loc.All.ToDictionary(p => p.Key, p => p.Value);
        foreach (var r in Enum.GetValues<Reject>())
        {
            if (r == Reject.None) { Assert.Null(RejectTexts.Key(r)); continue; }
            string? key = RejectTexts.Key(r);
            Assert.True(key is not null, $"{r} has no text key");
            Assert.True(table.TryGetValue(key!, out var text), $"{r}: key {key} is not in Loc");
            Assert.False(string.IsNullOrWhiteSpace(text.Es), $"{r} has no Spanish text");
            Assert.False(string.IsNullOrWhiteSpace(text.En), $"{r} has no English text");
            Assert.DoesNotContain(r.ToString(), text.En);
        }
    }
}
