using Starfoundry.Sim;
using Xunit;

namespace Starfoundry.Sim.Tests;

internal static class TestContent
{
    public static readonly ContentDb Core =
        ContentDb.FromFile(Path.Combine(AppContext.BaseDirectory, "content", "core.json"));
}

public class ContentTests
{
    private static readonly string Base = System.Text.RegularExpressions.Regex.Replace("""
        {"schema":1,"items":[{"id":"t:a"},{"id":"t:b"}],"machines":[{"id":"t:m"}],
         "recipes":[{"id":"t:r","machine":"t:m","inputs":[{"item":"t:a","count":1}],
                     "outputs":[{"item":"t:b","count":1}],"time_s":1}]}
        """, @"\s+", "");

    [Fact]
    public void CoreContentLoadsWithExpectedShape()
    {
        var c = TestContent.Core;
        Assert.Equal(4, c.Items.Count);
        Assert.Equal(3, c.Machines.Count);
        Assert.Equal(4, c.Recipes.Count);

        var smelt = c.Recipes[c.RecipeIndex("core:smelt_iron")];
        Assert.Equal(c.MachineIndex("core:smelter"), smelt.Machine);
        Assert.Equal(60, smelt.TimeTicks); // 2 seconds at 30 ticks per second
        Assert.Equal(new ItemStack(c.ItemIndex("core:iron_ore"), 1), Assert.Single(smelt.Inputs));
        Assert.Empty(c.Recipes[c.RecipeIndex("core:mine_iron")].Inputs);
        Assert.Throws<KeyNotFoundException>(() => c.ItemIndex("core:nothing"));
    }

    [Fact]
    public void HashIdentifiesContentButIgnoresFormatting()
    {
        var a = ContentDb.FromJson(Base);
        var b = ContentDb.FromJson(Base.Replace(",", ",\n  ").Replace("{", "{\n"));
        var c = ContentDb.FromJson(Base.Replace("\"time_s\":1", "\"time_s\":2"));
        var d = ContentDb.FromJson(Base.Replace("\"t:b\"", "\"t:c\""));

        Assert.Equal(a.Hash, b.Hash);
        Assert.NotEqual(a.Hash, c.Hash);
        Assert.NotEqual(a.Hash, d.Hash);
    }

    [Theory]
    [InlineData("0.05", 2)]   // 1.5 ticks rounds away from zero
    [InlineData("0.04", 1)]   // 1.2 ticks
    [InlineData("1.5", 45)]
    [InlineData("600", 18000)]
    public void SecondsConvertToWholeTicks(string seconds, int ticks)
    {
        var c = ContentDb.FromJson(Base.Replace("\"time_s\":1", $"\"time_s\":{seconds}"));
        Assert.Equal(ticks, c.Recipes[0].TimeTicks);
    }

    [Theory]
    [InlineData("{\"id\":\"t:b\"}", "{\"id\":\"t:a\"}", "Duplicate item")]
    [InlineData("\"machine\":\"t:m\"", "\"machine\":\"t:x\"", "unknown machine")]
    [InlineData("\"item\":\"t:a\"", "\"item\":\"t:zz\"", "unknown item")]
    [InlineData("\"count\":1}],\"outputs\"", "\"count\":0}],\"outputs\"", "count")]
    [InlineData("\"count\":1}],\"outputs\"", "\"count\":65}],\"outputs\"", "count")]
    [InlineData("\"time_s\":1", "\"time_s\":0", "time_s")]
    [InlineData("\"time_s\":1", "\"time_s\":-1", "time_s")]
    [InlineData("\"time_s\":1", "\"time_s\":0.01", "time_s")]
    [InlineData("\"time_s\":1", "\"time_s\":601", "time_s")]
    [InlineData("\"time_s\":1", "\"time_s\":\"1\"", "time_s")]
    [InlineData("\"outputs\":[{\"item\":\"t:b\",\"count\":1}]", "\"outputs\":[]", "output")]
    [InlineData("\"id\":\"t:r\"", "\"id\":\"T:r\"", "Invalid recipe id")]
    [InlineData("\"id\":\"t:r\"", "\"id\":\"nocolon\"", "Invalid recipe id")]
    [InlineData("\"id\":\"t:r\"", "\"id\":\"a:b:c\"", "Invalid recipe id")]
    [InlineData("\"id\":\"t:r\"", "\"id\":\":x\"", "Invalid recipe id")]
    [InlineData("\"id\":\"t:r\"", "\"id\":\"x:\"", "Invalid recipe id")]
    [InlineData("\"schema\":1,", "\"schema\":1,\"extra\":2,", "Unknown property")]
    [InlineData("\"schema\":1", "\"schema\":2", "schema")]
    [InlineData("\"inputs\":[{\"item\":\"t:a\",\"count\":1}]",
        "\"inputs\":[{\"item\":\"t:a\",\"count\":1},{\"item\":\"t:a\",\"count\":1}]", "twice")]
    public void InvalidContentIsRejectedWithAClearMessage(string find, string replace, string messagePart)
    {
        Assert.Contains(find, Base);
        var e = Assert.Throws<InvalidDataException>(() => ContentDb.FromJson(Base.Replace(find, replace)));
        Assert.Contains(messagePart, e.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void NonJsonAndStructuralErrorsAreRejected()
    {
        Assert.Throws<InvalidDataException>(() => ContentDb.FromJson("not json"));
        Assert.Throws<InvalidDataException>(() => ContentDb.FromJson("[]"));
        Assert.Throws<InvalidDataException>(() => ContentDb.FromJson("{\"schema\":1}"));
        Assert.Throws<InvalidDataException>(() => ContentDb.FromJson(Base.Replace("\"items\":[{\"id\":\"t:a\"},{\"id\":\"t:b\"}]", "\"items\":[]")));

        // Too many inputs in one recipe.
        string five = string.Join(",", Enumerable.Range(0, 5).Select(i => $"{{\"item\":\"t:i{i}\",\"count\":1}}"));
        string items = string.Join(",", Enumerable.Range(0, 5).Select(i => $"{{\"id\":\"t:i{i}\"}}"));
        string json = $"{{\"schema\":1,\"items\":[{items}],\"machines\":[{{\"id\":\"t:m\"}}],\"recipes\":[" +
                      $"{{\"id\":\"t:r\",\"machine\":\"t:m\",\"inputs\":[{five}],\"outputs\":[{{\"item\":\"t:i0\",\"count\":1}}],\"time_s\":1}}]}}";
        Assert.Throws<InvalidDataException>(() => ContentDb.FromJson(json));

        // More items than the buffers allow.
        string many = string.Join(",", Enumerable.Range(0, ContentDb.MaxItems + 1).Select(i => $"{{\"id\":\"t:i{i}\"}}"));
        Assert.Throws<InvalidDataException>(() =>
            ContentDb.FromJson($"{{\"schema\":1,\"items\":[{many}],\"machines\":[],\"recipes\":[]}}"));
    }
}
