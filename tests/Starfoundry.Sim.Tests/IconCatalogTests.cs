using System.Xml.Linq;
using Starfoundry.Ui;
using Xunit;

namespace Starfoundry.Sim.Tests;

public class IconCatalogTests
{
    private static readonly ContentDb C = TestContent.Core;

    private static void Registered(string id) => Assert.True(IconCatalog.IsRegistered(id), $"no icon registered for {id}");

    [Fact]
    public void EveryItemMachineAndResearchNodeHasAnExplicitIcon()
    {
        foreach (var i in C.Items) Registered(IconCatalog.ForItem(i.Id));
        foreach (var m in C.Machines) Registered(IconCatalog.ForMachine(m.Id));
        foreach (var r in C.Research) Registered(IconCatalog.ForResearch(r.Id));
        foreach (var e in C.Enemies) Registered(IconCatalog.ForEnemy(e.Id));
        foreach (var a in C.Archetypes) Registered("arch:" + a.Id);
        if (C.World is { } w)
        {
            foreach (var s in w.StarTypes) Registered("star:" + s.Id);
            foreach (var k in w.Kinds) Registered("obstacle:" + k.Id);
        }
    }

    [Fact]
    public void EveryModuleKindBeltAndFieldHasAnIcon()
    {
        foreach (var k in Enum.GetValues<ModuleKind>()) Registered(IconCatalog.ForModule(k));
        foreach (var t in Enum.GetValues<BeltTier>()) Registered(IconCatalog.ForBelt(t));
        foreach (var f in ResearchView.Fields) Registered(IconCatalog.ForField(f));
        foreach (var c in BuildCatalog.Categories) Registered(IconCatalog.ForBuildTab(c));
    }

    [Fact]
    public void EveryKeyPanelRowHasAnIconOrAnExplicitNone()
    {
        string[] rows = { "boost", "build", "cancel", "choose", "close", "demolish", "dock", "dockGuide", "drones", "esc", "found", "help", "hp", "info", "inspect", "jump",
            "kind", "lang", "layer", "load", "look", "map", "mine", "mineFire", "module", "pick", "place", "quick", "recipe", "refine", "repair", "research", "resume",
            "roll", "rotate", "run", "scan", "seat", "start", "stock", "tab", "undock", "unload", "walk", "weapons", "designer", "fireGuns", "inventory", "interact", "computer", "tabs", "hudDetail", "assist", "invDrag", "invQuick", "invKeys", "invClose" };
        foreach (var id in rows)
        {
            var icon = IconCatalog.ForKeyRow(id);
            Assert.NotNull(icon);
            Registered(icon!);
        }
        Assert.Null(IconCatalog.ForKeyRow("no-such-row"));
    }

    [Fact]
    public void EveryRegisteredIconHasAGlyphAndAnEsAndEnName()
    {
        foreach (var id in IconCatalog.All)
        {
            var def = IconCatalog.Resolve(id, out bool fallback);
            Assert.False(fallback);
            Assert.True(IconGlyphs.Has(def.Glyph), id);
            Assert.True(IconCatalog.HasName(id), $"{id} has no name text");
            foreach (var lang in new[] { Language.Es, Language.En })
            {
                var before = Loc.Lang;
                Loc.Lang = lang;
                try { Assert.DoesNotContain("⟦", IconCatalog.Name(id)); Assert.NotEqual("", IconCatalog.Name(id)); }
                finally { Loc.Lang = before; }
            }
        }
    }

    [Fact]
    public void UnknownIdsFallBackGracefullyAndAreReportedOnce()
    {
        var reported = new List<string>();
        void Handler(string id) => reported.Add(id);
        IconCatalog.FellBack += Handler;
        try
        {
            var a = IconCatalog.Resolve("item:mod:unobtainium", out bool fb1);
            IconCatalog.Resolve("item:mod:unobtainium", out bool fb2);
            Assert.True(fb1 && fb2);
            Assert.True(IconGlyphs.Has(a.Glyph));
            Assert.Single(reported, "item:mod:unobtainium");
            Assert.Equal("generic", IconCatalog.Resolve("weird", out _).Glyph);
            Assert.Equal("hostile", IconCatalog.Resolve("enemy:mod:x", out _).Glyph);
        }
        finally { IconCatalog.FellBack -= Handler; }
    }

    [Fact]
    public void EveryGlyphIsWellFormedSvgOnTheGrid()
    {
        foreach (var g in IconGlyphs.All)
        {
            var doc = XDocument.Parse(IconGlyphs.Document(g, 48));
            Assert.Equal("svg", doc.Root!.Name.LocalName);
            Assert.Equal("0 0 24 24", doc.Root.Attribute("viewBox")!.Value);
            Assert.DoesNotContain("{F}", doc.ToString());
            Assert.DoesNotContain("{S}", doc.ToString());
            Assert.True(doc.Root.Elements().Any(), g);
        }
    }

    [Fact]
    public void ResearchNodesOfOneFieldShareItsTint()
    {
        Assert.Equal(IconCatalog.Physics, IconCatalog.ResearchTint(ResearchField.Physics));
        Assert.NotEqual(IconCatalog.ResearchTint(ResearchField.Biology), IconCatalog.ResearchTint(ResearchField.Engineering));
    }
}

public class ResearchTreeLayoutTests
{
    private static IReadOnlyList<ResearchEntry> Entries()
    {
        var c = TestContent.Core;
        var net = new BeltNetwork(c);
        return ResearchView.Build(c, net, 0);
    }

    [Fact]
    public void EveryNodeGetsOneSlotAndNoTwoShareACell()
    {
        var entries = Entries();
        var slots = ResearchTreeLayout.Layout(entries);
        Assert.Equal(entries.Count, slots.Count);
        Assert.Equal(entries.Count, slots.Select(s => s.Index).Distinct().Count());
        Assert.Equal(slots.Count, slots.Select(s => (s.Band, s.Column, s.Row)).Distinct().Count());
    }

    [Fact]
    public void ANodeSitsRightOfEverythingItRequires()
    {
        var entries = Entries();
        var slots = ResearchTreeLayout.Layout(entries).ToDictionary(s => s.Index);
        foreach (var e in entries)
            foreach (var r in e.Requires)
                Assert.True(slots[e.Index].Column > slots[r.Index].Column, $"{e.Id} is not right of {r.Id}");
    }

    [Fact]
    public void BandsFollowTheFieldOrder()
    {
        var slots = ResearchTreeLayout.Layout(Entries());
        var order = ResearchView.Fields.ToList();
        foreach (var s in slots) Assert.Equal(order.Where(f => slots.Any(x => x.Field == f)).ToList().IndexOf(s.Field), s.Band);
        Assert.True(ResearchTreeLayout.Columns(slots) >= 2);
    }
}

public class ObjectiveIconTests
{
    [Fact]
    public void EveryGuidedStepHasARegisteredIcon()
    {
        foreach (var o in Enum.GetValues<Objective>())
            Assert.True(IconCatalog.IsRegistered(IconCatalog.ForObjective(o)), o.ToString());
    }
}

public class IconLiteralTests
{
    private static string? GameFolder()
    {
        for (var dir = new DirectoryInfo(AppContext.BaseDirectory); dir is not null; dir = dir.Parent)
        {
            string game = Path.Combine(dir.FullName, "game");
            if (File.Exists(Path.Combine(game, "Play.cs"))) return game;
        }
        return null;
    }

    /// <summary>A literal icon id typed in the game code that nobody registered would fall back to a generic glyph at run time (and log an ICON line): catch it here.</summary>
    [Fact]
    public void EveryIconIdWrittenInTheGameSourcesIsRegistered()
    {
        string? game = GameFolder();
        if (game is null) return; // built outside the repository: nothing to scan
        var pattern = new System.Text.RegularExpressions.Regex("\"((?:tool|status|machine|module|item|field|research|enemy|belt|obstacle|star|arch):[A-Za-z0-9_:]+)\"");
        var missing = new List<string>();
        foreach (var file in Directory.EnumerateFiles(game, "*.cs").Where(f => Path.GetFileName(f) != "IconGallery.cs")) // the gallery shows unknown ids on purpose
            foreach (System.Text.RegularExpressions.Match m in pattern.Matches(File.ReadAllText(file)))
                if (!IconCatalog.IsRegistered(m.Groups[1].Value)) missing.Add($"{Path.GetFileName(file)}: {m.Groups[1].Value}");
        Assert.True(missing.Count == 0, "unregistered icon ids: " + string.Join(", ", missing.Distinct()));
    }

    [Fact]
    public void TheToastCategoriesHaveIcons()
    {
        foreach (var id in new[] { "tool:refine", "tool:belt", "status:alert", "tool:info" }) Assert.True(IconCatalog.IsRegistered(id), id);
    }
}
