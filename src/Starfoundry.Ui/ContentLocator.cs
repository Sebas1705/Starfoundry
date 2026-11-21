namespace Starfoundry.Ui;

/// <summary>Result of looking for the content file: the first location that exists, plus every location that was tried, in order.</summary>
public sealed record ContentLookup(string? Path, IReadOnlyList<string> Tried)
{
    public bool Found => Path is not null;

    /// <summary>A plain English list of the tried locations, one per line (for logs and as the detail of the error screen).</summary>
    public string TriedText => string.Join('\n', Tried.Select(t => "- " + t));
}

/// <summary>
/// Engine-free resolver for <c>core.json</c>, so the same code finds the content in the editor, from a repository checkout and from an
/// exported build. Locations are opaque strings (they may be <c>res://</c> paths); the caller says whether one exists.
/// Order: an explicit <c>--content=path</c> (when given it is the only candidate, so a typo is reported instead of hidden), then the
/// project resource <c>res://content/core.json</c> (included in an export), then <c>&lt;exe dir&gt;/content/core.json</c>, then the
/// repository layout <c>res://../content/core.json</c>.
/// </summary>
public static class ContentLocator
{
    public const string FileName = "core.json";
    public const string ContentFlag = "--content=";

    /// <summary>The value of the first <c>--content=path</c> argument, or null when absent or empty.</summary>
    public static string? ExplicitPath(IEnumerable<string> args)
    {
        string? value = args.FirstOrDefault(a => a.StartsWith(ContentFlag, StringComparison.Ordinal))?[ContentFlag.Length..].Trim().Trim('"');
        return string.IsNullOrEmpty(value) ? null : value;
    }

    /// <summary>Joins a directory and the content file name with forward slashes (works for disk paths and <c>res://</c>).</summary>
    public static string InDir(string dir) => dir.TrimEnd('/', '\\').Replace('\\', '/') + "/" + FileName;

    /// <param name="explicitPath">The <c>--content=</c> value, or null.</param>
    /// <param name="resourcePath">The in-project resource (<c>res://content/core.json</c>), or null when there is none.</param>
    /// <param name="exeDir">The folder of the executable (the file is looked up in its <c>content</c> subfolder), or null.</param>
    /// <param name="repoPath">The repository layout path (the game folder's sibling <c>content/core.json</c>), or null.</param>
    /// <param name="exists">Whether a location exists.</param>
    public static ContentLookup Resolve(string? explicitPath, string? resourcePath, string? exeDir, string? repoPath, Func<string, bool> exists)
    {
        var tried = new List<string>();
        if (!string.IsNullOrEmpty(explicitPath))
        {
            tried.Add(explicitPath);
            return new ContentLookup(exists(explicitPath) ? explicitPath : null, tried);
        }

        var candidates = new List<string>();
        if (!string.IsNullOrEmpty(resourcePath)) candidates.Add(resourcePath);
        if (!string.IsNullOrEmpty(exeDir)) candidates.Add(InDir(exeDir.TrimEnd('/', '\\') + "/content"));
        if (!string.IsNullOrEmpty(repoPath)) candidates.Add(repoPath);

        foreach (string c in candidates)
        {
            if (tried.Contains(c)) continue;
            tried.Add(c);
            if (exists(c)) return new ContentLookup(c, tried);
        }
        return new ContentLookup(null, tried);
    }
}
