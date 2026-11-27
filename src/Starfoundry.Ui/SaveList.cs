using System.Globalization;
using Starfoundry.Sim;

namespace Starfoundry.Ui;

/// <summary>Whether a save can be loaded by this game: it can unless the content it was recorded with (or its command log format) is not the one this game has.</summary>
public enum SaveCompat { Ok, OtherContent, UnknownScenario, NewerFormat }

public static class SaveCompatibility
{
    /// <summary>
    /// Judges a save from its header alone. <paramref name="expectedHash"/> gives the content hash a replay of a game of that scenario needs
    /// (null scenario: the plain content; null result: the scenario is not in this game); <paramref name="currentLogVersion"/> is the newest log format this game reads.
    /// </summary>
    public static SaveCompat Check(SaveMeta meta, Func<string?, ulong?> expectedHash, int currentLogVersion)
    {
        if (meta.LogVersion > currentLogVersion) return SaveCompat.NewerFormat;
        if (expectedHash(meta.ScenarioId) is not { } hash) return SaveCompat.UnknownScenario;
        return meta.ContentHash == hash ? SaveCompat.Ok : SaveCompat.OtherContent;
    }

    /// <summary>The <c>saves.problem.*</c> text key of a save that cannot be loaded.</summary>
    public static string Key(SaveCompat c) => c switch
    {
        SaveCompat.OtherContent => "saves.problem.content",
        SaveCompat.UnknownScenario => "saves.problem.scenario",
        SaveCompat.NewerFormat => "saves.problem.newer",
        _ => "",
    };

    /// <summary>The <c>saves.problem.*</c> text key of a file that could not be read.</summary>
    public static string Key(SaveProblem p) => p switch
    {
        SaveProblem.NewerFormat => "saves.problem.newer",
        SaveProblem.Truncated => "saves.problem.truncated",
        SaveProblem.NotASave => "saves.problem.notasave",
        _ => "saves.problem.corrupt",
    };
}

/// <summary>How many checkpoints of a game are kept, and which ones go when a new one is written.</summary>
public static class SaveRotation
{
    /// <summary>The checkpoints kept per game: the newest ones. Older ones rotate out as new ones are written.</summary>
    public const int CheckpointsKept = 6;

    /// <summary>
    /// The ids of the checkpoints of <paramref name="gameId"/> beyond the newest <paramref name="keep"/>. Only readable, automatic saves are candidates: manual,
    /// quick and victory saves, other games' checkpoints and files that cannot be read are never listed.
    /// </summary>
    public static IReadOnlyList<string> ToPrune(IEnumerable<SaveEntry> entries, string gameId, int keep) =>
        entries.Where(e => e.Meta is { Kind: SaveKind.Auto } m && m.GameId == gameId)
            .OrderByDescending(e => e.When).ThenByDescending(e => e.Id, StringComparer.Ordinal)
            .Skip(Math.Max(0, keep)).Select(e => e.Id).ToList();
}

public enum SaveFilter { All, Mine, Checkpoints }
public enum SaveSort { Newest, Oldest, Name }

/// <summary>The list on the save screen: which saves show (all, the player's own, the checkpoints, optionally only this game's) and in which order.</summary>
public static class SaveList
{
    public static IReadOnlyList<SaveEntry> Apply(IEnumerable<SaveEntry> entries, SaveFilter filter, SaveSort sort, string? onlyGameId = null)
    {
        IEnumerable<SaveEntry> q = entries.Where(e =>
        {
            if (e.Meta is not { } m) return filter == SaveFilter.All && onlyGameId is null; // an unreadable file has no game: shown only in the unfiltered list
            if (onlyGameId is not null && m.GameId != onlyGameId) return false;
            return filter switch
            {
                SaveFilter.Mine => m.Kind != SaveKind.Auto,
                SaveFilter.Checkpoints => m.Kind == SaveKind.Auto,
                _ => true,
            };
        });
        q = sort switch
        {
            SaveSort.Oldest => q.OrderBy(e => e.When).ThenBy(e => e.Id, StringComparer.Ordinal),
            SaveSort.Name => q.OrderBy(e => e.Meta?.Name ?? e.Id, StringComparer.CurrentCultureIgnoreCase).ThenByDescending(e => e.When),
            _ => q.OrderByDescending(e => e.When).ThenBy(e => e.Id, StringComparer.Ordinal),
        };
        return q.ToList();
    }

    /// <summary>
    /// The save a Continue button loads: the one last used if it still exists and can be loaded, else the newest loadable one. Null when there is none.
    /// </summary>
    public static SaveEntry? ContinueTarget(IEnumerable<SaveEntry> entries, string? lastId, Func<SaveMeta, bool> loadable)
    {
        var usable = entries.Where(e => e.Meta is not null && loadable(e.Meta)).ToList();
        return usable.FirstOrDefault(e => e.Id == lastId) ?? usable.OrderByDescending(e => e.When).ThenBy(e => e.Id, StringComparer.Ordinal).FirstOrDefault();
    }

    /// <summary>
    /// The save the end screen's "reload last checkpoint" loads: the newest loadable save of the same game (any kind; the quick save counts), or, when that game has none, the newest loadable save.
    /// </summary>
    public static SaveEntry? LastCheckpoint(IEnumerable<SaveEntry> entries, string gameId, Func<SaveMeta, bool> loadable)
    {
        var usable = entries.Where(e => e.Meta is not null && loadable(e.Meta)).OrderByDescending(e => e.When).ThenBy(e => e.Id, StringComparer.Ordinal).ToList();
        return usable.FirstOrDefault(e => e.Meta!.GameId == gameId && e.Meta.Kind != SaveKind.Victory) ?? usable.FirstOrDefault(e => e.Meta!.Kind != SaveKind.Victory);
    }
}

/// <summary>Plain-text pieces of a save row, engine-free so they are tested: the date, the play time and the facts line.</summary>
public static class SaveText
{
    /// <summary>The save time in the machine's local time zone: <c>2026-11-28 14:05</c>.</summary>
    public static string Date(long unixSeconds)
    {
        if (unixSeconds <= 0) return "-";
        var local = TimeZoneInfo.ConvertTimeFromUtc(DateTimeOffset.FromUnixTimeSeconds(unixSeconds).UtcDateTime, TimeZoneInfo.Local);
        return local.ToString("yyyy-MM-dd HH:mm", CultureInfo.InvariantCulture);
    }

    /// <summary>h:mm:ss over an hour, m:ss under it.</summary>
    public static string Clock(double seconds)
    {
        int s = (int)Math.Max(0, seconds);
        return s >= 3600 ? $"{s / 3600}:{s / 60 % 60:00}:{s % 60:00}" : $"{s / 60}:{s % 60:00}";
    }

    /// <summary>The game time of a save (ticks at 30 per second).</summary>
    public static string GameClock(long ticks) => Clock(ticks / (double)Units.TicksPerSecond);

    /// <summary>The name shown for a save: the name it has, or (quick, checkpoint, victory saves carry none, so the text follows the language) its kind and, for a checkpoint, why it was taken.</summary>
    public static string Title(SaveMeta m)
    {
        if (m.Name.Length > 0) return m.Name;
        string kind = Loc.T("saves.title." + m.Kind.ToString().ToLowerInvariant());
        return m.Kind == SaveKind.Auto && Loc.Has("saves.reason." + m.Reason) ? kind + " - " + Loc.T("saves.reason." + m.Reason) : kind;
    }

    /// <summary>The difficulty of a save in words: a preset's name, "custom", or empty when there is none to say.</summary>
    public static string PresetName(SaveMeta m) => m.Preset.Length == 0 ? "" : m.Preset == "custom" ? Loc.T("saves.preset.custom") : Loc.Has("difficulty." + m.Preset) ? Loc.T("difficulty." + m.Preset) : m.Preset;

    /// <summary>What was being played: the scenario's name, or Campaign or Sandbox for a game without one.</summary>
    public static string GameName(SaveMeta m, string? scenarioName) => scenarioName is { Length: > 0 } ? scenarioName : Loc.T(m.Mode == 0 ? "saves.mode.sandbox" : "saves.mode.campaign");

    /// <summary>The one-line summary of a save: game and difficulty, seed, game time, wave and system.</summary>
    public static string Facts(SaveMeta m, string? scenarioName)
    {
        string preset = PresetName(m);
        string lead = GameName(m, scenarioName) + (preset.Length > 0 ? " (" + preset + ")" : "");
        return m.System.Length > 0
            ? Loc.T("saves.facts", lead, m.Seed, GameClock(m.Ticks), m.Wave, m.System)
            : Loc.T("saves.facts.noSystem", lead, m.Seed, GameClock(m.Ticks), m.Wave);
    }
}
