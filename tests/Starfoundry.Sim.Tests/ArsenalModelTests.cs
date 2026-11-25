using Starfoundry.Ui;
using Xunit;

namespace Starfoundry.Sim.Tests;

public class ArsenalModelTests
{
    private static ArsenalModel Full()
    {
        var m = new ArsenalModel();
        m.Fit(new[] { new FittedWeapon(ArsenalKind.Gun, 2, 1), new FittedWeapon(ArsenalKind.Laser, 1, 1), new FittedWeapon(ArsenalKind.Laser, 1, 1), new FittedWeapon(ArsenalKind.Laser, 2, 1), new FittedWeapon(ArsenalKind.Gun, 1, 1) });
        return m;
    }

    private static void InBoth(Action check)
    {
        foreach (var lang in new[] { Language.En, Language.Es }) { Loc.Lang = lang; check(); }
        Loc.Lang = Language.En;
    }

    [Fact]
    public void SlotsAreLasersThenGunsMk1BeforeMk2WithMergedCounts()
    {
        var m = Full();
        Assert.Equal(new[] { (ArsenalKind.Laser, 1, 2), (ArsenalKind.Laser, 2, 1), (ArsenalKind.Gun, 1, 1), (ArsenalKind.Gun, 2, 1) }, m.Slots.Select(s => (s.Kind, s.Mark, s.Count)).ToArray());
        Assert.Equal(0, m.Selected);
        Assert.Equal(WeaponGroup.Laser, m.Group);
        Assert.Equal("module:mining_laser", m.Slots[0].Icon);
        Assert.Equal("module:gun_mk2", m.Slots[3].Icon);
    }

    [Fact]
    public void EmptyArsenalHasNoSelectionAndNothingToStepTo()
    {
        var m = new ArsenalModel();
        Assert.True(m.IsEmpty);
        Assert.Equal(-1, m.Selected);
        Assert.Null(m.Group);
        Assert.False(m.Step(1));
        Assert.False(m.Select(0));
        Assert.Equal(-1, m.IndexOfKey("Key1"));
        Assert.False(ArsenalVisibility.Allowed(new ArsenalContext(true, false, true, false, HudMode.Full, HudDetail.Standard), false));
    }

    [Fact]
    public void StepWrapsBothWays()
    {
        var m = Full();
        Assert.True(m.Step(-1));
        Assert.Equal(3, m.Selected);
        Assert.True(m.Step(1));
        Assert.Equal(0, m.Selected);
        var single = new ArsenalModel();
        single.Fit(new[] { new FittedWeapon(ArsenalKind.Laser, 1, 1) });
        Assert.False(single.Step(1));
    }

    [Fact]
    public void NumberKeysMapToSlotsAndIgnoreTheRest()
    {
        var m = Full();
        Assert.Equal(0, m.IndexOfKey("Key1"));
        Assert.Equal(3, m.IndexOfKey("Key4"));
        Assert.Equal(-1, m.IndexOfKey("Key5")); // no fifth slot
        Assert.Equal(-1, m.IndexOfKey("Key0"));
        Assert.Equal(-1, m.IndexOfKey("V"));
        Assert.True(m.Select(m.IndexOfKey("Key3")));
        Assert.Equal(WeaponGroup.Guns, m.Group);
        Assert.False(m.Select(2)); // already in hand
    }

    [Fact]
    public void RefitKeepsTheSlotInHandWhileItsKindIsFitted()
    {
        var m = Full();
        m.Select(3); // Mk2 gun
        m.Fit(new[] { new FittedWeapon(ArsenalKind.Laser, 1, 1), new FittedWeapon(ArsenalKind.Gun, 1, 1) });
        Assert.Equal(WeaponGroup.Guns, m.Group); // the Mk2 is gone: the gun slot stays in hand
        Assert.Equal(1, m.Selected);
        m.Fit(new[] { new FittedWeapon(ArsenalKind.Laser, 1, 1) });
        Assert.Equal(WeaponGroup.Laser, m.Group);
        m.Fit(Array.Empty<FittedWeapon>());
        Assert.Equal(-1, m.Selected);
    }

    [Fact]
    public void SelectGroupFollowsAnOutsideChange()
    {
        var m = Full();
        Assert.True(m.SelectGroup(WeaponGroup.Guns));
        Assert.Equal(2, m.Selected);
        Assert.False(m.SelectGroup(WeaponGroup.Guns));
        m.Select(3);
        Assert.False(m.SelectGroup(WeaponGroup.Guns)); // the Mk2 gun already is a gun slot
        Assert.Equal(3, m.Selected);
    }

    [Fact]
    public void StateComesFromWhatTheKindIsDoing()
    {
        var m = Full();
        Assert.Equal(SlotState.Ready, m.StateOf(0));
        m.SetLive(ArsenalKind.Laser, new ArsenalLive(Firing: true));
        Assert.Equal(SlotState.Firing, m.StateOf(1));
        Assert.Equal(SlotState.Ready, m.StateOf(2));
        Assert.True(m.AnyFiring);
        m.SetLive(ArsenalKind.Gun, new ArsenalLive(Dry: true, Firing: true));
        Assert.Equal(SlotState.Dry, m.StateOf(2));
        Assert.Equal(0f, m.RingOf(2));
        m.SetLive(ArsenalKind.Gun, new ArsenalLive(PowerLostPermille: 1000));
        Assert.Equal(SlotState.NoPower, m.StateOf(3));
        m.SetLive(ArsenalKind.Gun, new ArsenalLive(PowerLostPermille: 400));
        Assert.Equal(SlotState.LowPower, m.StateOf(3));
        m.SetLive(ArsenalKind.Gun, new ArsenalLive(Cooldown: 0.25f));
        Assert.Equal(SlotState.Cooling, m.StateOf(2));
        Assert.Equal(0.75f, m.RingOf(2), 3);
        m.SetLive(ArsenalKind.Laser, new ArsenalLive(Blocked: true));
        Assert.Equal(SlotState.Blocked, m.StateOf(0));
    }

    [Fact]
    public void DetailShowsTheMiningReadoutAndTheAmmoInBothLanguages()
    {
        var m = Full();
        m.SetLive(ArsenalKind.Laser, new ArsenalLive(Firing: true, HasDeposit: true, MineTenths: 15, DepositLeft: 240));
        m.SetLive(ArsenalKind.Gun, new ArsenalLive(UsesAmmo: true, Ammo: 12));
        InBoth(() =>
        {
            Assert.Contains("1.5/s", m.Detail(0).Replace(',', '.'));
            Assert.Contains("240", m.Detail(0));
            Assert.Contains("12", m.Detail(2));
            Assert.Contains("x2", m.Tooltip(0));
        });
        m.SetLive(ArsenalKind.Laser, new ArsenalLive());
        Assert.Equal("no deposit", m.Detail(0));
    }

    [Fact]
    public void CursorEasesToTheSelectedSlotAndSettles()
    {
        var m = Full();
        m.Select(3);
        float last = m.Cursor;
        for (int i = 0; i < 4; i++) { m.Animate(1f / 60f); Assert.InRange(m.Cursor, last, 3f); last = m.Cursor; }
        Assert.True(m.Cursor < 3f);
        for (int i = 0; i < 60; i++) m.Animate(1f / 60f);
        Assert.Equal(3f, m.Cursor);
    }

    private static ArsenalContext Calm(HudMode mode = HudMode.Full, HudDetail d = HudDetail.Standard) => new(true, false, false, false, mode, d);

    [Fact]
    public void BarFadesAfterFourCalmSecondsAndComesBackOnUseOrThreat()
    {
        var v = new ArsenalVisibility();
        for (int i = 0; i < 60; i++) v.Tick(1f / 60f, Calm(), true, false);
        Assert.Equal(1f, v.Alpha, 2);
        for (int i = 0; i < 60 * 2; i++) v.Tick(1f / 60f, Calm(), true, false);
        Assert.True(v.Alpha > 0.9f); // 4 s are not over yet
        for (int i = 0; i < 60 * 5; i++) v.Tick(1f / 60f, Calm(), true, false);
        Assert.Equal(0f, v.Alpha, 2);
        v.Touch();
        for (int i = 0; i < 30; i++) v.Tick(1f / 60f, Calm(), true, false);
        Assert.True(v.Alpha > 0.9f);
        for (int i = 0; i < 60 * 6; i++) v.Tick(1f / 60f, Calm(), true, false);
        Assert.Equal(0f, v.Alpha, 2);
        for (int i = 0; i < 60 * 6; i++) v.Tick(1f / 60f, Calm() with { ThreatNear = true }, true, false);
        Assert.Equal(1f, v.Alpha, 2); // a threat keeps it up for as long as it lasts
    }

    [Fact]
    public void BarStaysOffWhereTheContextSaysSo()
    {
        Assert.True(ArsenalVisibility.Allowed(Calm(), true));
        Assert.False(ArsenalVisibility.Allowed(Calm() with { Seated = false }, true)); // on foot
        Assert.False(ArsenalVisibility.Allowed(Calm() with { BuildTool = true }, true)); // the number keys pick build entries
        Assert.False(ArsenalVisibility.Allowed(Calm(HudMode.Cockpit), true)); // the cockpit weapons screen shows it
        Assert.False(ArsenalVisibility.Allowed(Calm(HudMode.Minimal), true));
        var v = new ArsenalVisibility();
        v.Tick(0.1f, Calm(d: HudDetail.Minimal), true, false);
        Assert.True(v.Compact);
        v.Tick(0.1f, Calm(), true, false);
        Assert.False(v.Compact);
    }

    [Fact]
    public void TheComputerKeepsTheBarUp()
    {
        var v = new ArsenalVisibility();
        for (int i = 0; i < 60 * 10; i++) v.Tick(1f / 60f, Calm() with { ComputerOpen = true }, true, false);
        Assert.Equal(1f, v.Alpha, 2);
    }

    [Fact]
    public void RegistryDescribesTheArsenalKeys()
    {
        Assert.Equal(9, InputActions.Get("weaponPick").Keys.Length);
        Assert.Equal(new[] { "1-9" }, InputActions.Caps("weaponPick"));
        InBoth(() =>
        {
            foreach (var id in new[] { "weaponPick", "weaponWheel", "weaponNext" })
            {
                Assert.False(string.IsNullOrWhiteSpace(Loc.T(InputActions.Get(id).HintKey)));
                Assert.DoesNotContain("act.", Loc.T(InputActions.Get(id).HintKey));
            }
            Assert.Contains("arsenal", Loc.T("act.weaponPick.hint"), StringComparison.OrdinalIgnoreCase);
        });
    }

    [Fact]
    public void EverySlotHasAnIconAndTextsInBothLanguages()
    {
        InBoth(() =>
        {
            foreach (var kind in new[] { ArsenalKind.Laser, ArsenalKind.Gun })
                foreach (int mark in new[] { 1, 2 })
                {
                    var slot = new ArsenalSlot(kind, mark, 1);
                    Assert.True(IconCatalog.IsRegistered(slot.Icon), slot.Icon);
                    Assert.NotEqual(slot.NameKey, Loc.T(slot.NameKey));
                }
            foreach (var state in Enum.GetValues<SlotState>()) Assert.NotEqual(ArsenalModel.StateKey(state), Loc.T(ArsenalModel.StateKey(state)));
        });
        Loc.Lang = Language.Es;
        Assert.Contains("astillero", Loc.T("arsenal.empty"));
        Loc.Lang = Language.En;
        Assert.Contains("shipyard", Loc.T("arsenal.empty"));
    }
}
