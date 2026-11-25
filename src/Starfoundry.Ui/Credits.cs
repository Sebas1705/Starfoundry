using System.Globalization;
using System.Text;

namespace Starfoundry.Ui;

/// <summary>One row of ASSETS.md: a bundled third-party file with its source, licence and author (ADR 0037).</summary>
public sealed record AssetRow(string File, string Pack, string Source, string Licence, string Author, string Attribution, string Date, long Bytes);

/// <summary>One line of the credits page: a pack (a font family or a sound pack) with its author and licence.</summary>
public sealed record CreditEntry(string Pack, string Author, string Licence, string Source, int Files);

/// <summary>
/// The credits page data. ASSETS.md is the source of truth; <c>game/assets/credits.tsv</c> is generated from it (a test regenerates it and
/// fails when the committed file differs; run the tests with <c>STARFOUNDRY_UPDATE_CREDITS=1</c> to rewrite it). The game reads the
/// small file because markdown files are not exported.
/// </summary>
public static class Credits
{
    public const string ProjectLicence = "MIT";
    public const string ProjectCopyright = "Copyright (c) 2026 Sebas1705";

    /// <summary>Third-party libraries that ship with the game (name, licence, source).</summary>
    public static readonly IReadOnlyList<(string Name, string Licence, string Source)> Libraries = new[]
    {
        ("Godot Engine 4.7 (.NET)", "MIT", "https://godotengine.org"),
        (".NET runtime", "MIT", "https://dotnet.microsoft.com"),
    };

    /// <summary>Reads the file rows of ASSETS.md: table lines whose first cell is a backticked path.</summary>
    public static List<AssetRow> ParseAssetsMd(string markdown)
    {
        var rows = new List<AssetRow>();
        foreach (string raw in markdown.Split('\n'))
        {
            string line = raw.Trim();
            if (!line.StartsWith("| `", StringComparison.Ordinal)) continue;
            var c = line.Trim('|').Split('|').Select(x => x.Trim()).ToArray();
            if (c.Length < 8) continue;
            long.TryParse(c[7], NumberStyles.Integer, CultureInfo.InvariantCulture, out long bytes);
            rows.Add(new AssetRow(c[0].Trim('`'), c[1], c[2], c[3], c[4], c[5], c[6], bytes));
        }
        return rows;
    }

    /// <summary>Groups the rows by pack, in file order, and writes the tab separated data file (one line per pack).</summary>
    public static string ToDataFile(IEnumerable<AssetRow> rows)
    {
        var sb = new StringBuilder();
        foreach (var g in rows.GroupBy(r => r.Pack))
        {
            var first = g.First();
            sb.Append(g.Key).Append('\t').Append(first.Author).Append('\t').Append(first.Licence).Append('\t').Append(first.Source).Append('\t')
              .Append(g.Count().ToString(CultureInfo.InvariantCulture)).Append('\n');
        }
        return sb.ToString();
    }

    public static List<CreditEntry> ParseDataFile(string text)
    {
        var list = new List<CreditEntry>();
        foreach (string line in text.Split('\n'))
        {
            var c = line.TrimEnd('\r').Split('\t');
            if (c.Length < 5) continue;
            list.Add(new CreditEntry(c[0], c[1], c[2], c[3], int.TryParse(c[4], out int n) ? n : 0));
        }
        return list;
    }
}
