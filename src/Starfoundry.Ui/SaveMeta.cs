using System.Globalization;
using System.Text;

namespace Starfoundry.Ui;

/// <summary>What a save is for: a named save the player made, the one quick-save slot, an automatic checkpoint, or the result of a won game.</summary>
public enum SaveKind { Manual, Quick, Auto, Victory }

/// <summary>Why a save exists (the <c>reason</c> of a checkpoint); manual saves carry <see cref="Manual"/>, quick saves <see cref="QuickSave"/>.</summary>
public static class SaveReasons
{
    public const string Timer = "timer", Dock = "dock", Wave = "wave", Jump = "jump", Station = "station", Research = "research", Exit = "exit",
        Manual = "manual", QuickSave = "quick", Victory = "victory", Migrated = "migrated", Copy = "copy";

    public static readonly string[] All = { Timer, Dock, Wave, Jump, Station, Research, Exit, Manual, QuickSave, Victory, Migrated, Copy };
}

/// <summary>
/// What a save file says about the game it holds, readable without replaying anything: the name, the kind, the game it belongs to, the
/// difficulty, seed, game time, wave, system, real play time and date, and the content the replay needs. Stored as <c>key=value</c> lines
/// (see <see cref="SaveMetaText"/>), so a newer game can add fields without breaking an older one.
/// </summary>
public sealed record SaveMeta
{
    public const int MaxNameLength = 48;

    public string Name { get; init; } = "";
    public SaveKind Kind { get; init; } = SaveKind.Manual;
    /// <summary>One of <see cref="SaveReasons"/> (or empty): why this save exists.</summary>
    public string Reason { get; init; } = SaveReasons.Manual;
    /// <summary>Identifies one playthrough: every save made while playing it (and every save loaded from it) carries the same id, so checkpoints rotate per game.</summary>
    public string GameId { get; init; } = "";
    public string? ScenarioId { get; init; }
    /// <summary>The difficulty preset id (<c>relaxed</c>, <c>normal</c>...), <c>custom</c> for other rules, or empty when unknown.</summary>
    public string Preset { get; init; } = "";
    public byte Mode { get; init; }
    public ulong Seed { get; init; }
    /// <summary>Game time in ticks.</summary>
    public long Ticks { get; init; }
    /// <summary>Waves spawned so far (the wave the game is at), 0 when none or unknown.</summary>
    public long Wave { get; init; }
    /// <summary>The star system the ship was in (its name), empty when unknown.</summary>
    public string System { get; init; } = "";
    /// <summary>Real time spent playing this game, in seconds, across loads.</summary>
    public double PlaySeconds { get; init; }
    /// <summary>When the save was written, Unix seconds UTC.</summary>
    public long SavedAt { get; init; }
    /// <summary>The content hash the log was recorded with (the scenario's header hash for a scenario game): the replay needs the same one.</summary>
    public ulong ContentHash { get; init; }
    /// <summary>The command log version inside the file.</summary>
    public int LogVersion { get; init; }
}

/// <summary>The <c>key=value</c> text form of <see cref="SaveMeta"/>. Unknown keys and bad values are ignored, so any text parses to something.</summary>
public static class SaveMetaText
{
    public const int FormatVersion = 1;

    public static string Serialize(SaveMeta m)
    {
        var inv = CultureInfo.InvariantCulture;
        var sb = new StringBuilder();
        void Put(string key, string value) => sb.Append(key).Append('=').Append(Escape(value)).Append('\n');
        Put("format", FormatVersion.ToString(inv));
        Put("name", m.Name);
        Put("kind", m.Kind.ToString().ToLowerInvariant());
        Put("reason", m.Reason);
        Put("game", m.GameId);
        if (m.ScenarioId is not null) Put("scenario", m.ScenarioId);
        Put("preset", m.Preset);
        Put("mode", m.Mode.ToString(inv));
        Put("seed", m.Seed.ToString(inv));
        Put("ticks", m.Ticks.ToString(inv));
        Put("wave", m.Wave.ToString(inv));
        Put("system", m.System);
        Put("play", m.PlaySeconds.ToString("R", inv));
        Put("saved", m.SavedAt.ToString(inv));
        Put("content", m.ContentHash.ToString("x16", inv));
        Put("log", m.LogVersion.ToString(inv));
        return sb.ToString();
    }

    public static SaveMeta Parse(string? text)
    {
        var inv = CultureInfo.InvariantCulture;
        var m = new SaveMeta();
        foreach (string line in (text ?? "").Split('\n'))
        {
            int eq = line.IndexOf('=');
            if (eq <= 0) continue;
            string key = line[..eq], value = Unescape(line[(eq + 1)..]);
            switch (key)
            {
                case "name": m = m with { Name = Clean(value) }; break;
                case "kind": m = m with { Kind = Enum.TryParse<SaveKind>(value, true, out var k) && Enum.IsDefined(k) ? k : SaveKind.Manual }; break;
                case "reason": m = m with { Reason = Clean(value) }; break;
                case "game": m = m with { GameId = Clean(value) }; break;
                case "scenario": m = m with { ScenarioId = value.Length == 0 ? null : Clean(value) }; break;
                case "preset": m = m with { Preset = Clean(value) }; break;
                case "mode": if (byte.TryParse(value, NumberStyles.None, inv, out byte mode)) m = m with { Mode = mode }; break;
                case "seed": if (ulong.TryParse(value, NumberStyles.None, inv, out ulong seed)) m = m with { Seed = seed }; break;
                case "ticks": if (long.TryParse(value, NumberStyles.None, inv, out long ticks)) m = m with { Ticks = ticks }; break;
                case "wave": if (long.TryParse(value, NumberStyles.None, inv, out long wave)) m = m with { Wave = wave }; break;
                case "system": m = m with { System = Clean(value) }; break;
                case "play": if (double.TryParse(value, NumberStyles.Float, inv, out double play) && double.IsFinite(play) && play >= 0) m = m with { PlaySeconds = play }; break;
                case "saved": if (long.TryParse(value, NumberStyles.None, inv, out long saved)) m = m with { SavedAt = saved }; break;
                case "content": if (ulong.TryParse(value, NumberStyles.AllowHexSpecifier, inv, out ulong hash)) m = m with { ContentHash = hash }; break;
                case "log": if (int.TryParse(value, NumberStyles.None, inv, out int log)) m = m with { LogVersion = log }; break;
            }
        }
        return m;
    }

    /// <summary>Control characters out and a sane length: what a damaged or edited file may not push into a list row.</summary>
    private static string Clean(string s)
    {
        var sb = new StringBuilder(s.Length);
        foreach (char c in s) if (!char.IsControl(c)) sb.Append(c);
        return sb.Length > SaveMeta.MaxNameLength * 2 ? sb.ToString(0, SaveMeta.MaxNameLength * 2) : sb.ToString();
    }

    private static string Escape(string s) => s.Replace("\\", "\\\\", StringComparison.Ordinal).Replace("\n", "\\n", StringComparison.Ordinal).Replace("\r", "\\r", StringComparison.Ordinal);

    private static string Unescape(string s)
    {
        if (s.IndexOf('\\') < 0) return s.TrimEnd('\r');
        var sb = new StringBuilder(s.Length);
        for (int i = 0; i < s.Length; i++)
        {
            if (s[i] == '\\' && i + 1 < s.Length)
            {
                char n = s[++i];
                sb.Append(n == 'n' ? '\n' : n == 'r' ? '\r' : n);
            }
            else sb.Append(s[i]);
        }
        return sb.ToString();
    }
}
