using System.Globalization;
using System.Text;

namespace Starfoundry.Ui;

/// <summary>Names and file ids of save slots, engine-free: what a typed name becomes, which ids are safe file names, and how clashes are resolved.</summary>
public static class SaveNaming
{
    public const int MaxIdLength = 64;
    public const string QuickId = "quick";

    /// <summary>A typed name cleaned for storing: control characters out, runs of spaces collapsed, trimmed, cut to <see cref="SaveMeta.MaxNameLength"/>.</summary>
    public static string CleanName(string? name)
    {
        var sb = new StringBuilder();
        bool space = false;
        foreach (char c in name ?? "")
        {
            if (char.IsWhiteSpace(c)) { space = sb.Length > 0; continue; }
            if (char.IsControl(c)) continue;
            if (space) { sb.Append(' '); space = false; }
            sb.Append(c);
        }
        string s = sb.ToString();
        if (s.Length > SaveMeta.MaxNameLength) s = s[..SaveMeta.MaxNameLength].TrimEnd();
        return s;
    }

    /// <summary>The file-safe form of a name: lower case ASCII letters, digits and single dashes (accents dropped), at most 40 characters, "save" when nothing is left.</summary>
    public static string Slug(string? name)
    {
        var sb = new StringBuilder();
        foreach (char c in (name ?? "").Normalize(NormalizationForm.FormD))
        {
            if (CharUnicodeInfo.GetUnicodeCategory(c) == UnicodeCategory.NonSpacingMark) continue;
            char l = char.ToLowerInvariant(c);
            if (l is >= 'a' and <= 'z' or >= '0' and <= '9') sb.Append(l);
            else if (sb.Length > 0 && sb[^1] != '-') sb.Append('-');
        }
        string s = sb.ToString().Trim('-');
        if (s.Length > 40) s = s[..40].Trim('-');
        return s.Length == 0 ? "save" : s;
    }

    /// <summary>True for an id that is safe as a file name inside the saves folder: <c>[a-z0-9._-]</c>, not starting with a dot, short.</summary>
    public static bool IsValidId(string? id)
    {
        if (string.IsNullOrEmpty(id) || id.Length > MaxIdLength || id[0] == '.') return false;
        foreach (char c in id) if (!(c is >= 'a' and <= 'z' or >= '0' and <= '9' or '-' or '_' or '.')) return false;
        return !id.Contains("..", StringComparison.Ordinal);
    }

    /// <summary>The first of <paramref name="baseId"/>, <c>baseId-2</c>, <c>baseId-3</c>... that does not exist.</summary>
    public static string UniqueId(Func<string, bool> exists, string baseId)
    {
        string id = baseId;
        for (int n = 2; exists(id); n++) id = $"{baseId}-{n}";
        return id;
    }

    /// <summary>The id of a checkpoint: the time it was taken and why (<c>auto-1767225600-dock</c>).</summary>
    public static string AutoId(long unixSeconds, string reason) => $"auto-{unixSeconds.ToString(CultureInfo.InvariantCulture)}-{Slug(reason)}";

    /// <summary>The id of the victory save of a game.</summary>
    public static string VictoryId(string gameId) => "victory-" + Slug(gameId);

    /// <summary><paramref name="desired"/> if no existing name equals it (ignoring case), else <c>desired (2)</c>, <c>desired (3)</c>...; the result still fits <see cref="SaveMeta.MaxNameLength"/>.</summary>
    public static string UniqueName(IEnumerable<string> existing, string desired)
    {
        var taken = new HashSet<string>(existing, StringComparer.OrdinalIgnoreCase);
        string name = CleanName(desired);
        if (name.Length == 0) name = "Save";
        if (!taken.Contains(name)) return name;
        for (int n = 2; ; n++)
        {
            string suffix = $" ({n})";
            string candidate = (name.Length + suffix.Length > SaveMeta.MaxNameLength ? name[..(SaveMeta.MaxNameLength - suffix.Length)].TrimEnd() : name) + suffix;
            if (!taken.Contains(candidate)) return candidate;
        }
    }

    /// <summary>A proposed name for a new manual save: <c>Campaign - 12:34</c> (the scenario's name and the game time).</summary>
    public static string SuggestName(string scenarioName, double gameSeconds)
    {
        string time = $"{(int)gameSeconds / 60}:{(int)gameSeconds % 60:00}";
        string baseName = CleanName(scenarioName);
        if (baseName.Length == 0) baseName = "Save";
        string suffix = " - " + time;
        if (baseName.Length + suffix.Length > SaveMeta.MaxNameLength) baseName = baseName[..(SaveMeta.MaxNameLength - suffix.Length)].TrimEnd();
        return baseName + suffix;
    }

    /// <summary>The id a legacy file name maps to when it is wrapped into a slot (<c>quicksave</c> becomes <see cref="QuickId"/>).</summary>
    public static string LegacyId(string stem) => stem == "quicksave" ? QuickId : Slug(stem);
}
