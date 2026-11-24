namespace Starfoundry.Ui;

/// <summary>A page a cockpit display can show.</summary>
public enum CockpitPage
{
    Flight,
    Nav,
    Systems,
    Weapons,
}

/// <summary>One of the three screens of the console, from the pilot's left to right.</summary>
public enum CockpitScreen
{
    Left,
    Center,
    Right,
}

/// <summary>What pressing a button on a cockpit display does. <see cref="CockpitButton.Arg"/> carries the page or the row.</summary>
public enum CockpitAction
{
    None,
    /// <summary>Show page <c>Arg</c> (a <see cref="CockpitPage"/>) on the screen the button is on.</summary>
    ShowPage,
    ToggleWeaponGroup,
    ToggleMining,
    ToggleArmed,
    ToggleAutopilot,
    ToggleDockAssist,
    CycleHudMode,
    PrevTarget,
    NextTarget,
    /// <summary>Select the target in row <c>Arg</c> of the list.</summary>
    SelectTarget,
    Scan,
    LeaveFocus,
    /// <summary>Flight assist on or off (F8).</summary>
    ToggleFlightAssist,
    /// <summary>Pick the map or list target in row <c>Arg</c> of the map list (any kind).</summary>
    PickTarget,
    /// <summary>The map zoom buttons: <c>Arg</c> 1 zooms in, -1 out; 0 resets the view.</summary>
    MapZoom,
}

/// <summary>A clickable rectangle of a display, in the pixels of that display's texture.</summary>
public readonly record struct CockpitButton(float X, float Y, float W, float H, CockpitAction Action, int Arg = 0)
{
    public bool Contains(float x, float y) => x >= X && y >= Y && x < X + W && y < Y + H;
}

/// <summary>
/// Which page each of the three screens shows. There are four pages and three screens, so one page is always off; showing a page that is on
/// another screen swaps the two, showing the hidden one replaces what the screen had. Engine-free.
/// </summary>
public sealed class CockpitPages
{
    public const int ScreenCount = 3;
    public const int PageCount = 4;

    private readonly CockpitPage[] _on = { CockpitPage.Systems, CockpitPage.Flight, CockpitPage.Nav };

    public CockpitPage PageOn(CockpitScreen screen) => _on[(int)screen];

    /// <summary>The page that no screen shows right now.</summary>
    public CockpitPage Hidden
    {
        get
        {
            for (int p = 0; p < PageCount; p++)
                if (Array.IndexOf(_on, (CockpitPage)p) < 0) return (CockpitPage)p;
            return CockpitPage.Weapons; // unreachable while the invariant holds
        }
    }

    /// <summary>The screen that shows a page, or null when it is the hidden one.</summary>
    public CockpitScreen? ScreenOf(CockpitPage page)
    {
        int i = Array.IndexOf(_on, page);
        return i < 0 ? null : (CockpitScreen)i;
    }

    /// <summary>Shows <paramref name="page"/> on <paramref name="screen"/>: the screen that had it takes the page this one had.</summary>
    public void Show(CockpitScreen screen, CockpitPage page)
    {
        if (!Enum.IsDefined(page)) return;
        int s = (int)screen;
        int other = Array.IndexOf(_on, page);
        if (other == s) return;
        if (other >= 0) _on[other] = _on[s];
        _on[s] = page;
    }

    /// <summary>The next (or, with a negative step, previous) page in order on a screen, wrapping round; the swap rule of <see cref="Show"/> applies.</summary>
    public void Cycle(CockpitScreen screen, int step)
    {
        if (step == 0) return;
        int next = (((int)PageOn(screen) + Math.Sign(step)) % PageCount + PageCount) % PageCount;
        Show(screen, (CockpitPage)next);
    }

    /// <summary>The screen to the right (or left, with a negative step) of another one, stopping at the ends.</summary>
    public static CockpitScreen Step(CockpitScreen from, int step) =>
        (CockpitScreen)Math.Clamp((int)from + Math.Sign(step), 0, ScreenCount - 1);
}

/// <summary>What the nav page lists.</summary>
public enum NavKind
{
    Deposit,
    Enemy,
    Port,
    Gate,
}

/// <summary>One row of the navigation list. <see cref="Id"/> is the game's id of the thing (a deposit or a system) or -1.</summary>
public sealed record NavTarget(NavKind Kind, int Id, string Name, float Distance, float TurnDegrees, string Icon, bool Selected = false, System.Numerics.Vector3 Local = default)
{
    /// <summary>Deposits are the targets the previous and next buttons step through and the laser and the autopilot use.</summary>
    public bool Selectable => Kind == NavKind.Deposit;

    /// <summary>Every kind of row can be picked as the navigation target (the big numbers and the route line follow the pick): deposits, threats, ports and the gate.</summary>
    public bool Pickable => true;

}

/// <summary>Builds and steps through the list of the navigation display.</summary>
public static class NavList
{
    /// <summary>
    /// The rows to show: threats first, then the selected deposit, then everything else by distance, cut to <paramref name="max"/> rows. The selected
    /// deposit is always kept, even when it is far. Ports and the gate keep their place after the selection.
    /// </summary>
    public static IReadOnlyList<NavTarget> Build(IEnumerable<NavTarget> all, int max)
    {
        var list = all.ToList();
        static int Rank(NavTarget t) => t.Kind == NavKind.Enemy ? 0 : t.Selected ? 1 : t.Kind == NavKind.Gate ? 2 : t.Kind == NavKind.Port ? 3 : 4;
        list = list.OrderBy(Rank).ThenBy(t => t.Distance).ToList();
        if (max <= 0) return Array.Empty<NavTarget>();
        if (list.Count <= max) return list;
        var cut = list.Take(max).ToList();
        if (list.FirstOrDefault(t => t.Selected) is { } sel && !cut.Contains(sel)) cut[^1] = sel;
        return cut;
    }

    /// <summary>The id of the deposit <paramref name="step"/> places after the selected one among the selectable rows, wrapping round; the first one when none is selected, -1 when there are none.</summary>
    public static int Step(IReadOnlyList<NavTarget> rows, int selectedId, int step)
    {
        var ids = rows.Where(r => r.Selectable).Select(r => r.Id).ToList();
        if (ids.Count == 0) return -1;
        int at = ids.IndexOf(selectedId);
        if (at < 0) return step >= 0 ? ids[0] : ids[^1];
        return ids[((at + Math.Sign(step)) % ids.Count + ids.Count) % ids.Count];
    }
}

/// <summary>Where the ship's velocity points, as the indicator of the flight display draws it: a dot in a disc whose centre is the nose.</summary>
/// <param name="X">Right of the centre, -1 to 1.</param>
/// <param name="Y">Above the centre, -1 to 1 (up is positive).</param>
/// <param name="Behind">The ship is drifting backwards (more than 90 degrees from the nose): the dot sits on the rim, drawn hollow.</param>
/// <param name="Still">Too slow to say.</param>
public readonly record struct VelocityMarker(float X, float Y, bool Behind, bool Still);

/// <summary>Display maths of the cockpit, engine-free.</summary>
public static class CockpitMath
{
    /// <summary>
    /// The velocity in the ship's own axes (right, up, forward) as a point on the indicator: the distance from the centre grows with the angle from
    /// the nose (90 degrees reaches the rim), the direction is the way the velocity leans (right and up). It does not use any world "up".
    /// </summary>
    public static VelocityMarker Velocity(float right, float up, float forward, float stillBelow = 0.05f)
    {
        float speed = MathF.Sqrt(right * right + up * up + forward * forward);
        if (!float.IsFinite(speed) || speed < stillBelow) return new VelocityMarker(0f, 0f, false, true);
        float angle = MathF.Acos(Math.Clamp(forward / speed, -1f, 1f)); // 0 ahead, pi behind
        float r = Math.Min(1f, angle / (MathF.PI / 2f));
        float lateral = MathF.Sqrt(right * right + up * up);
        if (lateral < 1e-6f) return new VelocityMarker(0f, 0f, forward < 0f, false);
        return new VelocityMarker(right / lateral * r, up / lateral * r, forward < 0f, false);
    }

    /// <summary>A value over its maximum, clamped to 0..1 (0 when the maximum is not positive).</summary>
    public static float Fraction(float value, float max) => max > 0f && float.IsFinite(value) ? Math.Clamp(value / max, 0f, 1f) : 0f;

    /// <summary>The state colour of a gauge from its fraction: good, worn below a third... the same bands as the HUD bars.</summary>
    public static HealthBand Band(float fraction) => HudFormat.Band((int)MathF.Round(Math.Clamp(fraction, 0f, 1f) * 1000f), 1000);

    /// <summary>The speed as shown: one decimal below 100, whole above, never negative, no "-0.0".</summary>
    public static string Speed(float cellsPerSecond, IFormatProvider culture)
    {
        float v = float.IsFinite(cellsPerSecond) ? Math.Max(0f, cellsPerSecond) : 0f;
        return v < 99.95f ? v.ToString("F1", culture) : v.ToString("F0", culture);
    }

    /// <summary>A distance in cells as shown on the nav list: whole cells, "1.2k" from a thousand.</summary>
    public static string Distance(float cells, IFormatProvider culture)
    {
        float v = float.IsFinite(cells) ? Math.Max(0f, cells) : 0f;
        return v < 1000f ? v.ToString("F0", culture) : (v / 1000f).ToString("F1", culture) + "k";
    }


    /// <summary>How bright the screens are for a power satisfaction in per mille: full at 1000 and over, dimming towards 0.45 at nothing, and 0.2 when the ship has no power at all (they stay readable, only dimmer).</summary>
    public static float PowerDim(int powerPermille, bool hasPower) => !hasPower ? 0.2f : 0.45f + 0.55f * Fraction(powerPermille, 1000f);
    /// <summary>The scanline brightness of row <paramref name="y"/> of a display: every fourth row is a little darker (subtle, so small text under it stays crisp).</summary>
    public static float Scanline(int y) => y % 4 == 0 ? 0.92f : 1f;
}

/// <summary>The data the flight display shows, with its fractions worked out.</summary>
public readonly record struct FlightDisplayData(
    float Speed, float MaxSpeed, float Thrust, bool Boosting, bool BoostAvailable, float Target,
    int Hull, int MaxHull, int PowerProduction, int PowerDemand, int PowerPermille,
    int Hydrogen, long FuelTicksLeft, int HoldUsed, int HoldCapacity)
{
    public float SpeedFraction => CockpitMath.Fraction(Speed, MaxSpeed);
    /// <summary>Where the demanded speed sits on the speed bar (0 to 1): the mark the gauge shows against the actual speed.</summary>
    public float TargetFraction => CockpitMath.Fraction(Target, MaxSpeed);
    public float HullFraction => CockpitMath.Fraction(Hull, MaxHull);
    public float PowerFraction => CockpitMath.Fraction(PowerPermille, 1000f);
    public float HoldFraction => CockpitMath.Fraction(HoldUsed, HoldCapacity);
    public bool FuelLow => FuelTicksLeft <= 0 ? Hydrogen <= 0 : FuelTicksLeft < PowerFormat.LowFuelSeconds * Starfoundry.Sim.Units.TicksPerSecond;
}

/// <summary>The data the systems display shows: the modules of the ship and what is in the hold.</summary>
public readonly record struct SystemsDisplayData(int Modules, int DamagedModules, int Hull, int MaxHull, int HoldUsed, int HoldCapacity, int Wave, bool ThreatNow)
{
    public float HealthyFraction => Modules <= 0 ? 0f : CockpitMath.Fraction(Modules - DamagedModules, Modules);
}

/// <summary>The data the weapons display shows.</summary>
public readonly record struct WeaponsDisplayData(bool Hardpoints, bool HasLaser, bool HasGun, WeaponGroup Group, bool Armed, bool Mining, bool LaserFiring, bool ThreatActive, int Enemies)
{
    /// <summary>The group that the fire button uses, or null when the ship has nothing to fire.</summary>
    public WeaponGroup? ActiveGroup => !Hardpoints ? (Armed ? WeaponGroup.Guns : WeaponGroup.Laser) : Group == WeaponGroup.Guns && HasGun ? WeaponGroup.Guns : HasLaser ? WeaponGroup.Laser : null;
}
