using Starfoundry.Ui;
using Xunit;

namespace Starfoundry.Sim.Tests;

/// <summary>Key rebinding (controls redesign phase 4): conflicts per context, swap, reset, reserved keys, settings.cfg round trip and tolerant loading. Engine-free; the editor is never live here (one live test is in InputActionsTests).</summary>
public class KeyBindingsTests
{
    private static KeyBindingEditor Fresh() => new();

    [Fact]
    public void DefaultBindingsHaveNoConflicts()
    {
        var ed = Fresh();
        foreach (var a in InputActions.All)
            for (int i = 0; i < a.Keys.Length; i++)
                Assert.Empty(KeyRules.ConflictsOf(new KeySlot(a.Id, i), a.Keys[i], ed.KeysOf));
    }

    [Fact]
    public void EveryDefaultKeyOfARebindableActionIsAKnownAssignableKey()
    {
        foreach (var a in InputActions.All.Where(KeyRules.IsRebindable))
            foreach (var k in a.Keys) Assert.True(KeyRules.IsAssignable(k), $"{a.Id}: {k}");
    }

    [Fact]
    public void AFreeKeyIsAssigned()
    {
        var ed = Fresh();
        Assert.Equal(AssignKind.Ok, ed.Assign("scan", 0, "J").Kind);
        Assert.Equal("J", ed.KeyAt("scan", 0));
        Assert.True(ed.IsCustom("scan"));
        Assert.Equal(AssignKind.NoChange, ed.Preview("scan", 0, "J").Kind);
    }

    [Fact]
    public void AKeyOfAnActionActiveAtTheSameTimeIsAConflictAndNothingChanges()
    {
        var ed = Fresh();
        var r = ed.Assign("scan", 0, "F"); // interact: flying, walking and building, together with scan
        Assert.Equal(AssignKind.Conflict, r.Kind);
        Assert.Equal(new KeySlot("interact", 0), Assert.Single(r.Conflicts));
        Assert.Equal("K", ed.KeyAt("scan", 0));
        Assert.False(ed.IsCustom("scan"));
    }

    [Fact]
    public void GlobalAndDockedActionsCollideWithFlyingOnes()
    {
        var ed = Fresh();
        Assert.Equal(AssignKind.Conflict, ed.Preview("scan", 0, "B").Kind);      // openBuild is global
        Assert.Equal(AssignKind.Conflict, ed.Preview("scan", 0, "Comma").Kind);  // unload is docked, which is on top of flying
    }

    [Fact]
    public void ActionsThatCanNeverBeActiveTogetherMayShareAKey()
    {
        var ed = Fresh();
        Assert.Equal(AssignKind.Ok, ed.Preview("run", 0, "Space").Kind);        // run is walking only, climb is flying and building
        Assert.Equal(AssignKind.Conflict, ed.Preview("look", 0, "Shift").Kind); // look and boost are both flying
        Assert.Equal(AssignKind.Ok, ed.Preview("mapReset", 0, "Z").Kind);       // mapReset is the computer only, assist is flying and building
    }

    [Fact]
    public void SwapExchangesTheTwoKeys()
    {
        var ed = Fresh();
        Assert.True(ed.Preview("scan", 0, "F").CanSwap);
        Assert.True(ed.Swap("scan", 0, "F"));
        Assert.Equal("F", ed.KeyAt("scan", 0));
        Assert.Equal("K", ed.KeyAt("interact", 0));
        Assert.Equal(2, ed.Overrides.Count);
        foreach (var a in InputActions.All)
            for (int i = 0; i < a.Keys.Length; i++)
                Assert.Empty(KeyRules.ConflictsOf(new KeySlot(a.Id, i), ed.KeyAt(a.Id, i), ed.KeysOf));
    }

    [Fact]
    public void SwapInsideOneActionExchangesItsSlots()
    {
        var ed = Fresh();
        Assert.True(ed.Preview("roll", 0, "E").CanSwap);
        Assert.True(ed.Swap("roll", 0, "E"));
        Assert.Equal(new[] { "E", "Q" }, ed.KeysOf("roll"));
    }

    [Fact]
    public void AKeyHeldByManyActionsCannotBeSwapped()
    {
        var ed = Fresh();
        var r = ed.Preview("scan", 0, "Key1"); // weapon pick, hotbar and tab pick
        Assert.Equal(AssignKind.Conflict, r.Kind);
        Assert.True(r.Conflicts.Count > 1);
        Assert.False(r.CanSwap);
        Assert.False(ed.Swap("scan", 0, "Key1"));
    }

    [Fact]
    public void ReservedKeysAndLockedActionsAreRefused()
    {
        var ed = Fresh();
        foreach (var k in new[] { "Escape", "F1", "Tab" }) Assert.Equal(AssignKind.Reserved, ed.Preview("scan", 0, k).Kind);
        foreach (var id in new[] { "cancel", "help", "computer", "fire", "place", "weaponPick", "hotbar", "tabPick", "weaponWheel" })
            Assert.Equal(AssignKind.Locked, ed.Preview(id, 0, "J").Kind);
        Assert.Equal(AssignKind.Invalid, ed.Preview("scan", 0, "Ctrl").Kind);
        Assert.Equal(AssignKind.Invalid, ed.Preview("scan", 0, "@click").Kind);
        Assert.Equal(AssignKind.Invalid, ed.Preview("scan", 0, "Nope").Kind);
        Assert.Equal(AssignKind.Invalid, ed.Preview("nope", 0, "J").Kind);
        Assert.Equal(AssignKind.Invalid, ed.Preview("scan", 3, "J").Kind);
        Assert.Empty(ed.Overrides);
    }

    [Fact]
    public void OnlyOptionalActionsCanBeCleared()
    {
        var ed = Fresh();
        Assert.False(ed.Clear("move", 0));
        Assert.False(ed.Clear("interact", 0));
        Assert.True(ed.Clear("scan", 0));
        Assert.Equal(KeyRules.Unbound, ed.KeyAt("scan", 0));
        Assert.False(ed.Clear("scan", 0));
        Assert.Equal(AssignKind.Ok, ed.Preview("assist", 0, "K").Kind); // K is free again
    }

    [Fact]
    public void ResetGoesBackToTheDefaults()
    {
        var ed = Fresh();
        ed.Assign("scan", 0, "J");
        ed.Assign("assist", 0, "X");
        ed.Reset("scan");
        Assert.Equal("K", ed.KeyAt("scan", 0));
        Assert.Equal("X", ed.KeyAt("assist", 0));
        ed.ResetAll();
        Assert.Empty(ed.Overrides);
    }

    [Fact]
    public void ResetNeverLeavesACollision()
    {
        var ed = Fresh();
        ed.Assign("scan", 0, "J");
        ed.Assign("assist", 0, "K"); // takes the default key of scan
        ed.Reset("scan");            // scan wants K back: assist gives it up
        Assert.Equal("K", ed.KeyAt("scan", 0));
        Assert.Equal("Z", ed.KeyAt("assist", 0));
    }

    [Fact]
    public void ChangedIsRaisedOncePerEdit()
    {
        var ed = Fresh();
        int n = 0;
        ed.Changed += () => n++;
        ed.Assign("scan", 0, "J");
        ed.Swap("assist", 0, "J");
        ed.ResetAll();
        Assert.Equal(3, n);
    }

    [Fact]
    public void OverridesRoundTripThroughSettingsPairs()
    {
        var ed = Fresh();
        ed.Assign("scan", 0, "J");
        ed.Swap("roll", 0, "E");
        ed.Clear("assist", 0);
        var pairs = ed.ToPairs();
        Assert.Contains(new KeyValuePair<string, string>("bind.scan", "J"), pairs);
        Assert.Contains(new KeyValuePair<string, string>("bind.roll", "E,Q"), pairs);
        Assert.Contains(new KeyValuePair<string, string>("bind.assist", "None"), pairs);
        var back = new KeyBindingEditor(KeyBindingEditor.ParsePairs(pairs));
        Assert.Equal(ed.ToPairs(), back.ToPairs());
        var settings = GameSettings.Parse(new GameSettings().Serialize() + string.Join('\n', pairs.Select(p => p.Key + "=" + p.Value)));
        Assert.Equal("J", settings.Bindings["scan"][0]);
        Assert.Contains("bind.scan=J", settings.Serialize());
        Assert.Equal(settings.Serialize(), GameSettings.Parse(settings.Serialize()).Serialize());
    }

    [Fact]
    public void DefaultSettingsWriteNoBindings()
    {
        Assert.DoesNotContain("bind.", new GameSettings().Serialize());
        Assert.Empty(GameSettings.Parse("language=en\nmouse_sensitivity=1.5\n").Bindings);
    }

    [Fact]
    public void LoadingIgnoresWhatItDoesNotKnowWithoutCrashing()
    {
        var text = "bind.nope=J\nbind.scan=Nonsense\nbind.assist=Tab\nbind.cancel=J\nbind.fire=J\nbind.computer=J\nbind.interact=\nbind.move=X\nbind.look=Ctrl\nbind.weaponPick=J\nbind.=x\nbind.hudDetail=U,V,W\nbind.recipe=none";
        var s = GameSettings.Parse(text);
        Assert.Equal(new[] { "X", "A", "S", "D" }, s.Bindings["move"]);
        foreach (var id in new[] { "nope", "scan", "assist", "cancel", "fire", "computer", "interact", "look", "weaponPick", "recipe" }) Assert.False(s.Bindings.ContainsKey(id), id);
        Assert.Equal(new[] { "U" }, s.Bindings["hudDetail"]); // keys beyond the slots are dropped
    }

    [Fact]
    public void ALoadedFileWithACollisionFallsBackToTheDefaultsOfTheActionsInvolved()
    {
        var s = GameSettings.Parse("bind.scan=F\nbind.assist=Z");
        Assert.False(s.Bindings.ContainsKey("scan")); // F is the key of interact
        var ok = GameSettings.Parse("bind.scan=F\nbind.interact=K"); // a saved swap is fine
        Assert.Equal("F", ok.Bindings["scan"][0]);
        Assert.Equal("K", ok.Bindings["interact"][0]);
    }

    [Fact]
    public void ActionIdsInTheFileAreCaseInsensitiveAndKeysTolerateSpacesAndQuotes()
    {
        var s = GameSettings.Parse("bind.SCAN=\" J \"");
        Assert.Equal("J", s.Bindings["scan"][0]);
    }

    [Fact]
    public void EveryActionIdIsAValidFileKey()
    {
        foreach (var a in InputActions.All) Assert.Matches("^[A-Za-z]+$", a.Id);
    }

    [Fact]
    public void EveryRebindableActionHasNamesForItsSlots()
    {
        foreach (var a in InputActions.All.Where(a => KeyRules.IsRebindable(a) && a.Keys.Length == 2))
            for (int i = 0; i < 2; i++) Assert.True(Loc.Has($"act.{a.Id}.slot.{i}"), $"{a.Id} slot {i}");
        for (int i = 0; i < 4; i++) Assert.True(Loc.Has($"act.move.slot.{i}"));
    }

    [Fact]
    public void ARunOfDigitsIsNotRebindableAndCapsAreNamed()
    {
        Assert.False(KeyRules.IsRebindable(InputActions.Get("hotbar")));
        Assert.True(KeyRules.IsRebindable(InputActions.Get("roll")));
        Assert.Equal("–", InputActions.Cap(KeyRules.Unbound));
        Assert.Equal("-", InputActions.Cap("Minus"));
    }
}
