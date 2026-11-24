namespace Starfoundry.Ui;

/// <summary>
/// How much of the outside HUD is drawn (F3 cycles it, the choice is saved in the settings): <see cref="Full"/> is the overlay of old,
/// <see cref="Cockpit"/> hides the cards, markers and gauges while the pilot sits (the cockpit displays carry that information) and
/// <see cref="Minimal"/> keeps only the crosshair, the warnings and the notifications.
/// </summary>
public enum HudMode
{
    Full,
    Cockpit,
    Minimal,
}

/// <summary>Which families of the outside overlay a <see cref="HudMode"/> shows in a situation. The crosshair and the warnings are always on.</summary>
/// <param name="Cards">The ship card (hull, power, fuel, hold) and the status panels.</param>
/// <param name="Markers">The world markers (deposits, enemy, ports, gate) and their arrows.</param>
/// <param name="FlightReadout">The speed and the galactic-frame gizmo at the top centre.</param>
/// <param name="KeyPanel">The contextual key panel at the bottom right.</param>
/// <param name="Toolbar">The bottom strip with the hold and the tool in hand (a build tool in hand always shows it).</param>
/// <param name="Toasts">The notification column.</param>
public readonly record struct HudLayers(bool Cards, bool Markers, bool FlightReadout, bool KeyPanel, bool Toolbar, bool Toasts);

/// <summary>The rules of <see cref="HudMode"/>, engine-free.</summary>
public static class HudModes
{
    public const HudMode Default = HudMode.Full;

    /// <summary>The mode after this one (F3): full, cockpit, minimal, full...</summary>
    public static HudMode Next(HudMode mode) => mode switch
    {
        HudMode.Full => HudMode.Cockpit,
        HudMode.Cockpit => HudMode.Minimal,
        _ => HudMode.Full,
    };

    /// <summary>The id stored in the settings file.</summary>
    public static string Id(HudMode mode) => mode switch
    {
        HudMode.Cockpit => "cockpit",
        HudMode.Minimal => "minimal",
        _ => "full",
    };

    /// <summary>Reads an id (any case); false for anything unknown.</summary>
    public static bool TryParse(string? id, out HudMode mode)
    {
        switch ((id ?? "").Trim().ToLowerInvariant())
        {
            case "full": mode = HudMode.Full; return true;
            case "cockpit": mode = HudMode.Cockpit; return true;
            case "minimal": mode = HudMode.Minimal; return true;
            default: mode = Default; return false;
        }
    }

    /// <summary>
    /// What a mode shows. Sitting at the controls is where the cockpit displays take over; on foot (walking, building) the overlay stays,
    /// because there are no displays to read there: <see cref="HudMode.Cockpit"/> keeps all of it and <see cref="HudMode.Minimal"/> drops the key panel and the markers.
    /// </summary>
    public static HudLayers Layers(HudMode mode, bool seated) => (mode, seated) switch
    {
        (HudMode.Full, _) => new HudLayers(true, true, true, true, true, true),
        (HudMode.Cockpit, false) => new HudLayers(true, true, true, true, true, true),
        (HudMode.Cockpit, true) => new HudLayers(false, false, false, true, false, true),
        (HudMode.Minimal, false) => new HudLayers(true, false, false, false, true, true),
        _ => new HudLayers(false, false, false, false, false, true),
    };
}
