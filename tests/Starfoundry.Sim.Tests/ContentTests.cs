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
        Assert.Equal(7, c.Machines.Count);
        Assert.Equal(7, c.Recipes.Count);
        Assert.Equal(4, c.Research.Count);
        Assert.Equal(new GalaxyRules(12, 800, 2, 150), c.Galaxy);
        Assert.Equal(4, c.Archetypes.Count);
        Assert.Single(c.Archetypes, a => a.IsHome);
        Assert.Equal(new ItemStack(c.ItemIndex("core:carbon"), 5), Assert.Single(c.JumpCost));
        Assert.True(c.AbilityGated[(int)Ability.Jump]);
        Assert.Equal(new[] { Ability.Jump }, c.Research[c.ResearchIndex("core:jump_drive")].UnlockAbilities);

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

    private static readonly string Rich = System.Text.RegularExpressions.Regex.Replace("""
        {"schema":1,
         "items":[{"id":"t:a"},{"id":"t:b"}],
         "machines":[{"id":"t:m","cost":[{"item":"t:a","count":3}]},
                     {"id":"t:drill","role":"drill"},
                     {"id":"t:lab","role":"lab"},
                     {"id":"t:ship","role":"ship"}],
         "recipes":[{"id":"t:r","machine":"t:m","inputs":[{"item":"t:a","count":1}],"outputs":[{"item":"t:b","count":1}],"time_s":1},
                    {"id":"t:dr","machine":"t:drill","inputs":[],"outputs":[{"item":"t:a","count":1}],"time_s":1},
                    {"id":"t:sr","machine":"t:ship","inputs":[{"item":"t:a","count":1}],"outputs":[{"item":"t:b","count":1}],"time_s":1}],
         "belts":[{"id":"t:belt1","tier":"mk1","cost_per_cell":[{"item":"t:a","count":1}]},
                  {"id":"t:belt2","tier":"mk2","cost_per_cell":[]}],
         "research":[{"id":"t:n1","cost":[{"item":"t:b","count":5}],"requires":[],"unlocks":["t:belt2","t:dr"]},
                     {"id":"t:n2","cost":[{"item":"t:b","count":9}],"requires":["t:n1"],"unlocks":["t:m"]}]}
        """, @"\s+", "");

    [Fact]
    public void RolesCostsBeltsAndResearchLoadAndGateContent()
    {
        var c = ContentDb.FromJson(Rich);
        Assert.Equal(MachineRole.Drill, c.Machines[c.MachineIndex("t:drill")].Role);
        Assert.Equal(MachineRole.Lab, c.Machines[c.MachineIndex("t:lab")].Role);
        Assert.Equal(MachineRole.Ship, c.Machines[c.MachineIndex("t:ship")].Role);
        Assert.Equal(new ItemStack(c.ItemIndex("t:a"), 3), Assert.Single(c.Machines[c.MachineIndex("t:m")].Cost));
        Assert.Equal(BeltTier.Mk2, c.Belts[1]!.Tier);
        Assert.Null(c.Belts[2]);

        // Whatever a node unlocks is gated; everything else is open from the start.
        Assert.True(c.MachineGated[c.MachineIndex("t:m")]);
        Assert.False(c.MachineGated[c.MachineIndex("t:drill")]);
        Assert.True(c.RecipeGated[c.RecipeIndex("t:dr")]);
        Assert.False(c.RecipeGated[c.RecipeIndex("t:r")]);
        Assert.True(c.BeltGated[(int)BeltTier.Mk2]);
        Assert.False(c.BeltGated[(int)BeltTier.Mk1]);
        Assert.Equal(new[] { c.ResearchIndex("t:n1") }, c.Research[c.ResearchIndex("t:n2")].Requires);
    }

    [Theory]
    [InlineData("\"role\":\"drill\"", "\"role\":\"pilot\"", "role")]
    [InlineData("\"role\":\"drill\"", "\"role\":5", "role")]
    [InlineData("{\"id\":\"t:ship\",\"role\":\"ship\"}", "{\"id\":\"t:ship\",\"role\":\"ship\",\"cost\":[{\"item\":\"t:a\",\"count\":1}]}", "never built")]
    [InlineData("{\"id\":\"t:m\",\"cost\"", "{\"id\":\"t:m\",\"sandbox_only\":1,\"cost\"", "sandbox_only")]
    [InlineData("\"item\":\"t:a\",\"count\":3", "\"item\":\"t:zz\",\"count\":3", "unknown item")]
    [InlineData("\"machine\":\"t:drill\",\"inputs\":[]", "\"machine\":\"t:drill\",\"inputs\":[{\"item\":\"t:b\",\"count\":1}]", "Drill recipe")]
    [InlineData("\"machine\":\"t:drill\"", "\"machine\":\"t:lab\"", "lab")]
    [InlineData("\"tier\":\"mk2\"", "\"tier\":\"mk1\"", "twice")]
    [InlineData("\"tier\":\"mk2\"", "\"tier\":\"mk9\"", "tier")]
    [InlineData("\"requires\":[\"t:n1\"]", "\"requires\":[\"t:nope\"]", "unknown research")]
    [InlineData("\"requires\":[\"t:n1\"]", "\"requires\":[\"t:n2\"]", "itself")]
    [InlineData("\"requires\":[],\"unlocks\":[\"t:belt2\"", "\"requires\":[\"t:n2\"],\"unlocks\":[\"t:belt2\"", "cycle")]
    [InlineData("\"unlocks\":[\"t:m\"]", "\"unlocks\":[]", "unlocks nothing")]
    [InlineData("\"unlocks\":[\"t:m\"]", "\"unlocks\":[\"t:ghost\"]", "unknown id")]
    [InlineData("\"id\":\"t:belt2\"", "\"id\":\"t:m\"", "used twice")]
    [InlineData("\"cost\":[{\"item\":\"t:b\",\"count\":5}]", "\"cost\":[]", "at least one")]
    [InlineData("\"cost\":[{\"item\":\"t:b\",\"count\":5}]", "\"cost\":[{\"item\":\"t:b\",\"count\":10001}]", "count")]
    public void InvalidRichContentIsRejected(string find, string replace, string messagePart)
    {
        Assert.Contains(find, Rich);
        var e = Assert.Throws<InvalidDataException>(() => ContentDb.FromJson(Rich.Replace(find, replace)));
        Assert.Contains(messagePart, e.Message, StringComparison.OrdinalIgnoreCase);
    }

    private static readonly string GalaxyPart = System.Text.RegularExpressions.Regex.Replace("""
        ,"galaxy":{"system_count":6,"spacing_cells":800,"lanes_per_system":2,"system_radius_cells":150},
         "archetypes":[
           {"id":"t:home","home":true,"weight":0,"distance_cells":[5,10],"deposits":[{"item":"t:a","count":[1,1],"amount":[100,100]}]},
           {"id":"t:belt","weight":2,"distance_cells":[10,40],"deposits":[{"item":"t:a","count":[1,3],"amount":[50,200]}]}],
         "jump_cost":[{"item":"t:b","count":2}]}
        """, @"\s+", "");

    private static readonly string RichGalaxy = Rich[..^1] + GalaxyPart;

    [Fact]
    public void GalaxyArchetypesAndJumpCostLoad()
    {
        var c = ContentDb.FromJson(RichGalaxy);
        Assert.Equal(new GalaxyRules(6, 800, 2, 150), c.Galaxy);
        Assert.Equal(2, c.Archetypes.Count);
        Assert.True(c.Archetypes[0].IsHome);
        Assert.Equal(new DepositSpec(c.ItemIndex("t:a"), 1, 3, 50, 200), c.Archetypes[1].Deposits[0]);
        Assert.Equal(new ItemStack(c.ItemIndex("t:b"), 2), Assert.Single(c.JumpCost));
    }

    [Theory]
    [InlineData("\"system_count\":6", "\"system_count\":1", "system_count")]
    [InlineData("\"system_count\":6", "\"system_count\":65", "system_count")]
    [InlineData("\"system_count\":6", "\"system_count\":\"6\"", "system_count")]
    [InlineData("\"spacing_cells\":800", "\"spacing_cells\":99", "spacing_cells")]
    [InlineData("\"spacing_cells\":800", "\"spacing_cells\":2001", "spacing_cells")]
    [InlineData("\"lanes_per_system\":2", "\"lanes_per_system\":0", "lanes_per_system")]
    [InlineData("\"lanes_per_system\":2", "\"lanes_per_system\":5", "lanes_per_system")]
    [InlineData("\"system_radius_cells\":150", "\"system_radius_cells\":19", "system_radius_cells")]
    [InlineData("\"galaxy\":{", "\"galaxy\":{\"extra\":1,", "Unknown property")]
    [InlineData("\"home\":true,\"weight\":0", "\"home\":false,\"weight\":1", "exactly one home")]
    [InlineData("\"id\":\"t:belt\",\"weight\":2", "\"id\":\"t:belt\",\"home\":true,\"weight\":0", "exactly one home")]
    [InlineData("\"weight\":2", "\"weight\":0", "weight")]
    [InlineData("\"weight\":2", "\"weight\":-1", "weight")]
    [InlineData("\"distance_cells\":[5,10]", "\"distance_cells\":[0,10]", "distance_cells")]
    [InlineData("\"distance_cells\":[5,10]", "\"distance_cells\":[10,5]", "distance_cells")]
    [InlineData("\"distance_cells\":[10,40]", "\"distance_cells\":[10,200]", "radius")]
    [InlineData("\"distance_cells\":[10,40]", "\"distance_cells\":[10]", "distance_cells")]
    [InlineData("\"distance_cells\":[10,40]", "\"distance_cells\":[10,\"x\"]", "distance_cells")]
    [InlineData("\"count\":[1,3]", "\"count\":[1,17]", "count")]
    [InlineData("\"amount\":[50,200]", "\"amount\":[0,200]", "amount")]
    [InlineData("\"item\":\"t:a\",\"count\":[1,3]", "\"item\":\"t:zz\",\"count\":[1,3]", "unknown item")]
    [InlineData("\"deposits\":[{\"item\":\"t:a\",\"count\":[1,3],\"amount\":[50,200]}]", "\"deposits\":[]", "deposit specs")]
    [InlineData("\"id\":\"t:belt\"", "\"id\":\"t:m\"", "used twice")]
    [InlineData("\"jump_cost\":[{\"item\":\"t:b\"", "\"jump_cost\":[{\"item\":\"t:nope\"", "unknown item")]
    public void InvalidGalaxyContentIsRejected(string find, string replace, string messagePart)
    {
        Assert.Contains(find, RichGalaxy);
        var e = Assert.Throws<InvalidDataException>(() => ContentDb.FromJson(RichGalaxy.Replace(find, replace)));
        Assert.Contains(messagePart, e.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void ArchetypesNeedAGalaxyAndAbilitiesMustBeKnown()
    {
        string noGalaxy = RichGalaxy.Replace("\"galaxy\":{\"system_count\":6,\"spacing_cells\":800,\"lanes_per_system\":2,\"system_radius_cells\":150},", "");
        Assert.Throws<InvalidDataException>(() => ContentDb.FromJson(noGalaxy));

        string unknownAbility = Rich.Replace("\"unlocks\":[\"t:belt2\",\"t:dr\"]", "\"unlocks\":[\"t:belt2\"],\"abilities\":[\"fly\"]");
        Assert.Throws<InvalidDataException>(() => ContentDb.FromJson(unknownAbility));
        string twice = Rich.Replace("\"unlocks\":[\"t:belt2\",\"t:dr\"]", "\"unlocks\":[\"t:belt2\"],\"abilities\":[\"jump\",\"jump\"]");
        Assert.Throws<InvalidDataException>(() => ContentDb.FromJson(twice));

        // A node may unlock only an ability.
        string onlyAbility = Rich.Replace("\"unlocks\":[\"t:belt2\",\"t:dr\"]", "\"unlocks\":[],\"abilities\":[\"jump\"]");
        Assert.True(ContentDb.FromJson(onlyAbility).AbilityGated[(int)Ability.Jump]);
    }

    [Fact]
    public void GalaxyDataChangesTheHash()
    {
        ulong h = ContentDb.FromJson(RichGalaxy).Hash;
        Assert.NotEqual(h, ContentDb.FromJson(RichGalaxy.Replace("\"system_count\":6", "\"system_count\":7")).Hash);
        Assert.NotEqual(h, ContentDb.FromJson(RichGalaxy.Replace("\"amount\":[50,200]", "\"amount\":[50,201]")).Hash);
        Assert.NotEqual(h, ContentDb.FromJson(RichGalaxy.Replace("\"count\":2}]}", "\"count\":3}]}")).Hash);
    }

    [Fact]
    public void RichContentChangesTheHash()
    {
        ulong baseHash = ContentDb.FromJson(Rich).Hash;
        Assert.NotEqual(baseHash, ContentDb.FromJson(Rich.Replace("\"count\":9", "\"count\":8")).Hash);
        Assert.NotEqual(baseHash, ContentDb.FromJson(Rich.Replace("\"role\":\"ship\"", "\"role\":\"crafter\"")).Hash);
        Assert.NotEqual(baseHash, ContentDb.FromJson(Rich.Replace("\"count\":3", "\"count\":4")).Hash);
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
