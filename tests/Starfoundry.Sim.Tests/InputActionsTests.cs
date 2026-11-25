using Starfoundry.Ui;
using Xunit;

namespace Starfoundry.Sim.Tests;

/// <summary>The key registry (controls redesign): no collisions, text and icons for every action, and the help generated from it. Engine-free.</summary>
public class InputActionsTests
{
    [Fact]
    public void NoTwoActionsShareAKeyInTheSameActiveContext()
    {
        foreach (var set in InputActions.ActiveSets())
        {
            var seen = new Dictionary<string, string>();
            foreach (var a in InputActions.All.Where(a => (a.Contexts & set) != 0))
                foreach (var key in a.Keys)
                {
                    Assert.False(seen.TryGetValue(key, out var other), $"{key} is bound to both {other} and {a.Id} in {set}");
                    seen[key] = a.Id;
                }
        }
    }

    [Fact]
    public void ActionIdsAreUniqueAndEveryActionHasAContext()
    {
        Assert.Equal(InputActions.All.Count, InputActions.All.Select(a => a.Id).Distinct().Count());
        foreach (var a in InputActions.All)
        {
            Assert.NotEqual(InputContext.None, a.Contexts);
            Assert.NotEmpty(a.Keys);
        }
    }

    [Fact]
    public void EveryActionHasANameAndAHintInBothLanguagesAndAnIcon()
    {
        var table = Loc.All.ToDictionary(p => p.Key, p => p.Value);
        foreach (var a in InputActions.All)
        {
            foreach (var key in new[] { a.NameKey, a.HintKey })
            {
                Assert.True(table.TryGetValue(key, out var text), $"{key} is missing");
                Assert.False(string.IsNullOrWhiteSpace(text.Es), key + " (es)");
                Assert.False(string.IsNullOrWhiteSpace(text.En), key + " (en)");
            }
            Assert.True(IconCatalog.All.Contains(a.Icon), $"{a.Id}: icon {a.Icon} is not registered");
        }
    }

    [Fact]
    public void TheHelpIsGeneratedAndListsEveryActionInBothLanguages()
    {
        var before = Loc.Lang;
        try
        {
            foreach (var lang in new[] { Language.Es, Language.En })
            {
                Loc.Lang = lang;
                string help = InputActions.HelpText();
                foreach (var a in InputActions.All) Assert.Contains(Loc.T(a.NameKey), help);
                Assert.DoesNotContain("⟦", help);
                Assert.True(help.Split('\n').Length <= 34, $"the help is {help.Split('\n').Length} lines");
            }
        }
        finally { Loc.Lang = before; }
    }

    [Fact]
    public void TheCoreKeysAreWhereTheSpecSaysTheyAre()
    {
        var fly = InputActions.Active(false, false, false, false);
        Assert.Equal("roll", InputActions.Find("Q", fly)!.Id);
        Assert.Equal("roll", InputActions.Find("E", fly)!.Id);
        Assert.Equal("interact", InputActions.Find("F", fly)!.Id);
        Assert.Equal("assist", InputActions.Find("Z", fly)!.Id);
        Assert.Equal("weaponNext", InputActions.Find("V", fly)!.Id);
        Assert.Equal("computer", InputActions.Find("Tab", fly)!.Id);
        Assert.Equal("quickSave", InputActions.Find("F5", fly)!.Id);
        var tool = InputActions.Active(false, true, false, false);
        Assert.Equal("layer", InputActions.Find("Q", tool)!.Id);
        Assert.Equal("hotbar", InputActions.Find("Key3", tool)!.Id);
        Assert.Equal("weaponPick", InputActions.Find("Key3", fly)!.Id);
        Assert.Equal("unload", InputActions.Find("Comma", InputActions.Active(false, false, false, true))!.Id);
        Assert.Null(InputActions.Find("Comma", fly));
    }

    [Fact]
    public void TheOldKeysAreGone()
    {
        foreach (var set in InputActions.ActiveSets())
            foreach (var key in new[] { "F2", "F3", "F4", "F6", "F7", "F8", "F10", "U", "G", "J", "T", "O", "P", "L", "Equal", "Minus", "Bracketleft", "Bracketright", "Backspace", "X" })
                Assert.Null(InputActions.Find(key, set));
    }

    [Fact]
    public void OverridesReplaceTheKeysOfAnAction()
    {
        try
        {
            InputActions.SetOverrides(new Dictionary<string, string[]> { ["scan"] = new[] { "J" }, ["nope"] = new[] { "X" } });
            Assert.Equal("J", InputActions.Primary("scan"));
            Assert.Equal("scan", InputActions.Find("J", InputActions.Active(false, false, false, false))!.Id);
            Assert.Null(InputActions.Find("K", InputActions.Active(false, false, false, false)));
        }
        finally { InputActions.SetOverrides(null); }
        Assert.Equal("K", InputActions.Primary("scan"));
        Assert.Null(InputActions.Find("None", InputActions.Active(false, false, false, false)));
    }

    [Fact]
    public void ALiveEditorFeedsTheHandlerTheCapsAndTheHelp()
    {
        var fly = InputActions.Active(false, false, false, false);
        var ed = new KeyBindingEditor(live: true);
        try
        {
            ed.Assign("scan", 0, "J");
            Assert.Equal("scan", InputActions.Find("J", fly)!.Id);
            Assert.Null(InputActions.Find("K", fly));
            Assert.Equal("J", InputActions.PrimaryCap("scan"));
            Assert.Contains("J  " + Loc.T("act.scan.name"), InputActions.HelpText());
            ed.Clear("assist", 0);
            Assert.Equal("–", InputActions.PrimaryCap("assist"));
            Assert.Null(InputActions.Find("None", fly));
            Assert.Equal(0, InputActions.SlotOf("roll", "Q"));
            ed.Swap("roll", 0, "E");
            Assert.Equal(1, InputActions.SlotOf("roll", "Q"));
        }
        finally { ed.ResetAll(); InputActions.SetOverrides(null); }
        Assert.Equal("K", InputActions.Primary("scan"));
    }

    [Fact]
    public void CapsWriteRangesAndSpecialKeys()
    {
        Assert.Equal(new[] { "1-9" }, InputActions.Caps("weaponPick"));
        Assert.Equal(new[] { "1-0" }, InputActions.Caps("hotbar"));
        Assert.Equal("Esc", InputActions.Cap("Escape"));
        Assert.Equal(",", InputActions.Cap("Comma"));
        Assert.Equal("@space", InputActions.Cap("Space"));
    }
}

public class ShipComputerTabsTests
{
    private static readonly ComputerAvailability Both = new(true, true), None = new(false, false);

    [Fact]
    public void EveryTabHasAnIconAndNamesInBothLanguages()
    {
        Assert.Equal(8, ShipComputerTabs.All.Count);
        var table = Loc.All.ToDictionary(p => p.Key, p => p.Value);
        foreach (var tab in ShipComputerTabs.All)
        {
            Assert.True(IconCatalog.All.Contains(ShipComputerTabs.Icon(tab)), tab.ToString());
            Assert.True(table.TryGetValue(ShipComputerTabs.NameKey(tab), out var t) && t.Es.Length > 0 && t.En.Length > 0, tab.ToString());
            if (ShipComputerTabs.WhyOff(tab, None) is { } why) Assert.True(table.ContainsKey(why), why);
        }
    }

    [Fact]
    public void NumbersAndStepsSkipTheTabsThatAreOff()
    {
        Assert.Equal(ComputerTab.Map, ShipComputerTabs.FromNumber(1, Both));
        Assert.Equal(ComputerTab.Systems, ShipComputerTabs.FromNumber(8, Both));
        Assert.Null(ShipComputerTabs.FromNumber(1, None));
        Assert.Null(ShipComputerTabs.FromNumber(6, None));
        Assert.Null(ShipComputerTabs.FromNumber(0, Both));
        Assert.Null(ShipComputerTabs.FromNumber(9, Both));
        Assert.Equal(ComputerTab.Research, ShipComputerTabs.Step(ComputerTab.Map, 1, Both));
        Assert.Equal(ComputerTab.Systems, ShipComputerTabs.Step(ComputerTab.Map, -1, Both));
        Assert.Equal(ComputerTab.Settings, ShipComputerTabs.Step(ComputerTab.Info, 1, None)); // the designer is off
        Assert.Equal(ComputerTab.Research, ShipComputerTabs.Step(ComputerTab.Systems, 1, None)); // so is the map
    }
}
