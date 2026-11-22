using System.Globalization;
using System.Text;
using Starfoundry.Sim;

namespace Starfoundry.Ui;

/// <summary>What the New Game screen offers a scenario as.</summary>
public enum ScenarioKind { Story, Campaign, Sandbox, Custom }

/// <summary>Classifies and orders scenarios for the menus. Story levels are the numbered ones (order below 100); the open-ended ones have no goals.</summary>
public static class ScenarioKinds
{
    public const string CampaignId = "campaign";

    public static ScenarioKind Of(ScenarioDef s) =>
        s.Id == CampaignId ? ScenarioKind.Campaign
        : s.IsOpenEnded ? (s.SandboxMode ? ScenarioKind.Sandbox : ScenarioKind.Custom)
        : s.Order < 100 && !s.PlayerRules ? ScenarioKind.Story
        : ScenarioKind.Campaign;

    /// <summary>The story levels in play order.</summary>
    public static IReadOnlyList<ScenarioDef> Story(ScenarioCatalog catalog) =>
        catalog.All.Where(s => Of(s) == ScenarioKind.Story).ToList();

    /// <summary>The level after this one, or null (not a story level, or the last).</summary>
    public static ScenarioDef? NextStory(ScenarioCatalog catalog, ScenarioDef current)
    {
        var story = Story(catalog);
        int i = -1;
        for (int k = 0; k < story.Count; k++) if (story[k].Id == current.Id) i = k;
        return i >= 0 && i + 1 < story.Count ? story[i + 1] : null;
    }

    /// <summary>The scenario that a sandbox or custom game starts from: free build picks the sandbox mode (everything unlocked and free), otherwise costs and research apply.</summary>
    public static string OpenEndedId(bool freeBuild, SandboxStart start) => (freeBuild ? "sandbox" : "custom") + start switch
    {
        SandboxStart.ShipAndStation => "_station",
        SandboxStart.ShipAndSiege => "_siege",
        _ => "",
    };
}

/// <summary>The best result of one scenario.</summary>
public sealed record ScenarioRecord(bool Completed, double BestSeconds, int BestStars);

/// <summary>What recording a win changed.</summary>
public sealed record RecordResult(bool FirstCompletion, bool NewBestTime, bool NewBestStars, double? PreviousBestSeconds, int PreviousBestStars);

/// <summary>
/// The player's progress through the scenarios: per scenario whether it was completed, the best time and the best stars. Stored in
/// <c>user://progress.cfg</c> in the text form of <see cref="CfgText"/>. A story level is unlocked when the previous one was completed
/// (the first is always open). Engine-free.
/// </summary>
public sealed class ProgressRecord
{
    private readonly SortedDictionary<string, ScenarioRecord> _records = new(StringComparer.Ordinal);

    public IReadOnlyDictionary<string, ScenarioRecord> Records => _records;

    public ScenarioRecord? Get(string id) => _records.GetValueOrDefault(id);

    public bool IsCompleted(string id) => _records.TryGetValue(id, out var r) && r.Completed;

    /// <summary>Records a win: the best time is the lowest, the best stars the highest. Stars are clamped to 1..3.</summary>
    public RecordResult RecordWin(string id, int stars, double seconds)
    {
        stars = Math.Clamp(stars, 1, 3);
        if (!double.IsFinite(seconds) || seconds < 0) seconds = 0;
        var old = Get(id);
        bool first = old is null || !old.Completed;
        bool betterTime = first || seconds < old!.BestSeconds;
        bool betterStars = first || stars > old!.BestStars;
        _records[id] = new ScenarioRecord(true, betterTime ? seconds : old!.BestSeconds, betterStars ? stars : old!.BestStars);
        return new RecordResult(first, betterTime && !first, betterStars && !first, first ? null : old!.BestSeconds, first ? 0 : old!.BestStars);
    }

    /// <summary>Story levels: the first is always open, each other needs the one before completed.</summary>
    public bool IsUnlocked(IReadOnlyList<string> storyOrder, string id)
    {
        int i = -1;
        for (int k = 0; k < storyOrder.Count; k++) if (storyOrder[k] == id) i = k;
        return i <= 0 || IsCompleted(storyOrder[i - 1]);
    }

    public int TotalStars => _records.Values.Sum(r => r.BestStars);

    // ---- text form ---------------------------------------------------------------------------

    public string Serialize()
    {
        var sb = new StringBuilder("; Starfoundry progress: best result per scenario\n");
        foreach (var (id, r) in _records)
        {
            sb.Append('[').Append(id).Append("]\n");
            sb.Append("completed=").Append(r.Completed ? "true" : "false").Append('\n');
            sb.Append("best_seconds=").Append(r.BestSeconds.ToString("0.##", CultureInfo.InvariantCulture)).Append('\n');
            sb.Append("best_stars=").Append(r.BestStars.ToString(CultureInfo.InvariantCulture)).Append("\n\n");
        }
        return sb.ToString();
    }

    /// <summary>Reads the text form; unknown sections, bad values and missing keys are skipped or defaulted.</summary>
    public static ProgressRecord Parse(string? text)
    {
        var p = new ProgressRecord();
        foreach (var section in CfgText.Parse(text))
        {
            if (section.Name.Length == 0 || section.Name.Length > 80) continue;
            bool completed = bool.TryParse(section.Get("completed"), out bool c) && c;
            double seconds = double.TryParse(section.Get("best_seconds"), NumberStyles.Float, CultureInfo.InvariantCulture, out double s) && double.IsFinite(s) && s >= 0 ? s : 0;
            int stars = int.TryParse(section.Get("best_stars"), NumberStyles.Integer, CultureInfo.InvariantCulture, out int st) ? Math.Clamp(st, 0, 3) : 0;
            if (!completed) continue; // only wins are worth keeping
            p._records[section.Name] = new ScenarioRecord(true, seconds, Math.Max(1, stars));
        }
        return p;
    }
}
