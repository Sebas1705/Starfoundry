using System.Globalization;

namespace Starfoundry.Ui;

/// <summary>
/// The player's settings, independent of the engine: defaults, clamping and a simple <c>key=value</c> text form (one pair per
/// line) that the game stores in <c>user://settings.cfg</c>. Unknown keys and malformed values are ignored, so an old, edited or
/// damaged file never stops the game from starting.
/// </summary>
public sealed record GameSettings
{
    public const float MinMouseSensitivity = 0.5f, MaxMouseSensitivity = 2f;
    public const float MinFov = 70f, MaxFov = 100f;
    public const float DefaultFov = 80f;

    public Language Language { get; init; } = Language.Es;

    /// <summary>A multiplier of the base mouse sensitivity: 0.5 (slow) to 2 (fast).</summary>
    public float MouseSensitivity { get; init; } = 1f;

    /// <summary>Vertical field of view of the first-person camera, in degrees.</summary>
    public float Fov { get; init; } = DefaultFov;

    /// <summary>Master volume, 0 (silent) to 1, applied to the Master audio bus.</summary>
    public float MasterVolume { get; init; } = 0.8f;

    public const int DefaultSfxVolume = 80, DefaultAmbienceVolume = 60, DefaultUiVolume = 80;

    /// <summary>Volume of the Sfx bus (machines, mining, building, combat), 0 to 100.</summary>
    public int SfxVolume { get; init; } = DefaultSfxVolume;

    /// <summary>Volume of the Ambience bus (ship hum, engine, reactor and the music), 0 to 100.</summary>
    public int AmbienceVolume { get; init; } = DefaultAmbienceVolume;

    /// <summary>Volume of the Ui bus (clicks, toasts, jingles, warning beeps), 0 to 100.</summary>
    public int UiVolume { get; init; } = DefaultUiVolume;

    public bool InvertMouseY { get; init; }

    /// <summary>How much of the outside HUD is drawn (F3 cycles it; see <see cref="Starfoundry.Ui.HudMode"/>).</summary>
    public HudMode HudMode { get; init; } = HudModes.Default;

    /// <summary>Piloting turns the ship a little behind the mouse (a short angular inertia) and eases the roll keys; off, the ship follows the mouse at once.</summary>
    public bool SmoothLook { get; init; } = true;

    public bool Fullscreen { get; init; }

    /// <summary>Low texture quality: the procedural textures are generated at half their size (read when the game starts, so a change applies on the next launch).</summary>
    public bool LowTextures { get; init; }

    /// <summary>Debug option (docs/prototypes/blender-pipeline.md): the starter cockpit's console, seat, frames and glass come from the Blender-authored glTF instead of the procedural meshes. The <c>--blender-cockpit</c> flag turns it on for one run.</summary>
    public bool BlenderCockpit { get; init; }

    /// <summary>Legacy: the goal-waves slider of older versions. It is still parsed and written so an old settings file round-trips, but no screen shows it and no game reads it: the difficulty preset or scenario (<c>WorldRules.GoalWaves</c>) decides the goal.</summary>
    public int GoalWaves { get; init; } = GoalSettings.DefaultWaves;

    /// <summary>The defaults for a system locale: Spanish for "es*" locales, English for any other known one, Spanish when there is none.</summary>
    public static GameSettings Defaults(string? locale = null) =>
        new() { Language = string.IsNullOrWhiteSpace(locale) ? Language.Es : Loc.FromLocale(locale) };

    /// <summary>The same settings with every value forced into its valid range (NaN and infinity fall back to the default).</summary>
    public GameSettings Clamped() => this with
    {
        Language = Enum.IsDefined(Language) ? Language : Language.Es,
        MouseSensitivity = Clamp(MouseSensitivity, MinMouseSensitivity, MaxMouseSensitivity, 1f),
        Fov = Clamp(Fov, MinFov, MaxFov, DefaultFov),
        HudMode = Enum.IsDefined(HudMode) ? HudMode : HudModes.Default,
        MasterVolume = Clamp(MasterVolume, 0f, 1f, 0.8f),
        SfxVolume = Math.Clamp(SfxVolume, 0, 100),
        AmbienceVolume = Math.Clamp(AmbienceVolume, 0, 100),
        UiVolume = Math.Clamp(UiVolume, 0, 100),
        GoalWaves = Math.Clamp(GoalWaves, GoalSettings.MinWaves, GoalSettings.MaxWaves),
    };

    private static float Clamp(float v, float min, float max, float fallback) =>
        float.IsFinite(v) ? Math.Clamp(v, min, max) : fallback;

    /// <summary>The settings as ordered key/value pairs (invariant culture), for a file format that stores them one by one.</summary>
    public IReadOnlyList<KeyValuePair<string, string>> ToPairs()
    {
        var s = Clamped();
        return new[]
        {
            new KeyValuePair<string, string>("language", s.Language == Language.Es ? "es" : "en"),
            new KeyValuePair<string, string>("mouse_sensitivity", s.MouseSensitivity.ToString("0.###", CultureInfo.InvariantCulture)),
            new KeyValuePair<string, string>("fov", s.Fov.ToString("0.#", CultureInfo.InvariantCulture)),
            new KeyValuePair<string, string>("master_volume", s.MasterVolume.ToString("0.###", CultureInfo.InvariantCulture)),
            new KeyValuePair<string, string>("sfx_volume", s.SfxVolume.ToString(CultureInfo.InvariantCulture)),
            new KeyValuePair<string, string>("ambience_volume", s.AmbienceVolume.ToString(CultureInfo.InvariantCulture)),
            new KeyValuePair<string, string>("ui_volume", s.UiVolume.ToString(CultureInfo.InvariantCulture)),
            new KeyValuePair<string, string>("invert_mouse_y", s.InvertMouseY ? "true" : "false"),
            new KeyValuePair<string, string>("hud_mode", HudModes.Id(s.HudMode)),
            new KeyValuePair<string, string>("smooth_look", s.SmoothLook ? "true" : "false"),
            new KeyValuePair<string, string>("fullscreen", s.Fullscreen ? "true" : "false"),
            new KeyValuePair<string, string>("low_textures", s.LowTextures ? "true" : "false"),
            new KeyValuePair<string, string>("blender_cockpit", s.BlenderCockpit ? "true" : "false"),
            new KeyValuePair<string, string>("goal_waves", s.GoalWaves.ToString(CultureInfo.InvariantCulture)),
        };
    }

    /// <summary>One <c>key=value</c> pair per line.</summary>
    public string Serialize() => string.Join('\n', ToPairs().Select(p => p.Key + "=" + p.Value)) + "\n";

    /// <summary>Builds settings from pairs on top of <paramref name="baseline"/>: unknown keys and bad values are skipped, the rest is clamped.</summary>
    public static GameSettings FromPairs(IEnumerable<KeyValuePair<string, string>> pairs, GameSettings baseline)
    {
        var s = baseline.Clamped();
        foreach (var (rawKey, rawValue) in pairs)
        {
            string key = rawKey.Trim().ToLowerInvariant();
            string value = rawValue.Trim().Trim('"');
            switch (key)
            {
                case "language":
                    if (value.Equals("es", StringComparison.OrdinalIgnoreCase)) s = s with { Language = Language.Es };
                    else if (value.Equals("en", StringComparison.OrdinalIgnoreCase)) s = s with { Language = Language.En };
                    break;
                case "mouse_sensitivity" when TryFloat(value, out float m): s = s with { MouseSensitivity = m }; break;
                case "fov" when TryFloat(value, out float f): s = s with { Fov = f }; break;
                case "master_volume" when TryFloat(value, out float v): s = s with { MasterVolume = v }; break;
                case "sfx_volume" when int.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out int sv): s = s with { SfxVolume = sv }; break;
                case "ambience_volume" when int.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out int av): s = s with { AmbienceVolume = av }; break;
                case "ui_volume" when int.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out int uv): s = s with { UiVolume = uv }; break;
                case "invert_mouse_y" when bool.TryParse(value, out bool i): s = s with { InvertMouseY = i }; break;
                case "hud_mode" when HudModes.TryParse(value, out var hm): s = s with { HudMode = hm }; break;
                case "smooth_look" when bool.TryParse(value, out bool sl): s = s with { SmoothLook = sl }; break;
                case "fullscreen" when bool.TryParse(value, out bool fs): s = s with { Fullscreen = fs }; break;
                case "low_textures" when bool.TryParse(value, out bool lt): s = s with { LowTextures = lt }; break;
                case "blender_cockpit" when bool.TryParse(value, out bool bc): s = s with { BlenderCockpit = bc }; break;
                case "goal_waves" when int.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out int gw): s = s with { GoalWaves = gw }; break;
            }
        }
        return s.Clamped();
    }

    /// <summary>Parses the text form; lines that are not <c>key=value</c> (blank lines, <c>[sections]</c>, comments, garbage) are ignored.</summary>
    public static GameSettings Parse(string? text, GameSettings? baseline = null)
    {
        var pairs = new List<KeyValuePair<string, string>>();
        foreach (string raw in (text ?? "").Split('\n'))
        {
            string line = raw.Trim();
            if (line.Length == 0 || line[0] is '[' or ';' or '#') continue;
            int eq = line.IndexOf('=');
            if (eq <= 0) continue;
            pairs.Add(new(line[..eq], line[(eq + 1)..]));
        }
        return FromPairs(pairs, baseline ?? Defaults());
    }

    private static bool TryFloat(string s, out float value) =>
        float.TryParse(s, NumberStyles.Float, CultureInfo.InvariantCulture, out value);
}
