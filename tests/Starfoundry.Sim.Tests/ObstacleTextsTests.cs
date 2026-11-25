using Starfoundry.Sim;
using Starfoundry.Ui;
using Xunit;

namespace Starfoundry.Sim.Tests;

/// <summary>ADR 0028: every obstacle kind and star type of the content has a Spanish and an English name, and the boost row only shows when boost exists.</summary>
public class ObstacleTextsTests
{
    [Fact]
    public void Every_obstacle_kind_and_star_type_has_names_in_both_languages()
    {
        var world = TestContent.Core.World!;
        foreach (var id in world.Kinds.Select(k => ("obstacle", k.Id)).Concat(world.StarTypes.Select(s => ("star", s.Id))))
        {
            string key = id.Item1 + "." + id.Id;
            Assert.True(Loc.Has(key), key);
            var (es, en) = Loc.All.First(p => p.Key == key).Value;
            Assert.False(string.IsNullOrWhiteSpace(es));
            Assert.False(string.IsNullOrWhiteSpace(en));
            Assert.NotEqual(es, en);
        }
    }

    [Fact]
    public void The_boost_row_appears_only_with_boost_and_lights_up_while_boosting()
    {
        // The key panel has no boost row any more (it is full); the key is in the registry and the F1 help.
        Assert.DoesNotContain(KeyHints.For(new KeyState { BoostAvailable = true, Boosting = true }).SelectMany(g => g.Rows), r => r.Id == "boost");
        var boost = InputActions.Get("boost");
        Assert.Contains("Shift", boost.Keys);
        Assert.True(boost.Contexts.HasFlag(InputContext.Flying));
        Assert.True(Loc.Has(boost.NameKey));
    }
}
