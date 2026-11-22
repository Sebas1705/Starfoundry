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
            "roll", "rotate", "run", "scan", "seat", "start", "stock", "tab", "undock", "unload", "walk", "weapons" };
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
