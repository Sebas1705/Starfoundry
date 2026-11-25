using Starfoundry.Ui;
using Xunit;

namespace Starfoundry.Sim.Tests;

/// <summary>ADR 0037: every bundled font and audio file has a row in ASSETS.md, every row has its file, every pack has its licence text.</summary>
public class AssetsManifestTests
{
    private static readonly string[] AssetExtensions = { ".ttf", ".otf", ".woff", ".woff2", ".ogg", ".wav", ".mp3", ".flac" };

    private static string Root()
    {
        string? dir = AppContext.BaseDirectory;
        while (dir is not null && !File.Exists(Path.Combine(dir, "ASSETS.md"))) dir = Path.GetDirectoryName(dir);
        return dir ?? throw new InvalidOperationException("ASSETS.md not found above " + AppContext.BaseDirectory);
    }

    private static string Rel(string root, string path) => Path.GetRelativePath(root, path).Replace('\\', '/');

    private static List<string> BundledFiles(string root) =>
        new[] { "fonts", "audio" }
            .Select(d => Path.Combine(root, "game", "assets", d))
            .Where(Directory.Exists)
            .SelectMany(d => Directory.EnumerateFiles(d, "*", SearchOption.AllDirectories))
            .Where(f => AssetExtensions.Contains(Path.GetExtension(f).ToLowerInvariant()))
            .Select(f => Rel(root, f)).OrderBy(f => f, StringComparer.Ordinal).ToList();

    private static List<AssetRow> Rows(string root) => Credits.ParseAssetsMd(File.ReadAllText(Path.Combine(root, "ASSETS.md")));

    [Fact]
    public void EveryBundledFileHasARow_AndEveryRowHasItsFile()
    {
        string root = Root();
        var rows = Rows(root);
        Assert.Empty(BundledFiles(root).Except(rows.Select(r => r.File)));
        Assert.Empty(rows.Select(r => r.File).Where(f => !File.Exists(Path.Combine(root, f))));
        Assert.Equal(rows.Count, rows.Select(r => r.File).Distinct().Count());
        foreach (var r in rows)
        {
            Assert.False(string.IsNullOrWhiteSpace(r.Source) || string.IsNullOrWhiteSpace(r.Licence) || string.IsNullOrWhiteSpace(r.Author)
                || string.IsNullOrWhiteSpace(r.Attribution) || string.IsNullOrWhiteSpace(r.Date), r.File + " has an empty column");
            Assert.StartsWith("http", r.Source);
            Assert.Equal(new FileInfo(Path.Combine(root, r.File)).Length, r.Bytes);
        }
    }

    [Fact]
    public void EveryRowUsesAnAcceptedLicence()
    {
        string[] accepted = { "CC0", "SIL OFL 1.1", "MIT", "Apache-2.0", "ISC", "CC-BY 4.0" };
        foreach (var r in Rows(Root())) Assert.Contains(r.Licence, accepted);
    }

    [Fact]
    public void EveryPackKeepsItsLicenceText()
    {
        string root = Root();
        string assets = Path.Combine(root, "game", "assets");
        foreach (string rel in BundledFiles(root))
        {
            string? dir = Path.GetDirectoryName(Path.Combine(root, rel));
            bool found = false;
            // the licence sits in the file's folder or in a parent below game/assets/<fonts|audio>
            while (dir is not null && Path.GetRelativePath(assets, dir).Split('\\', '/').Length >= 2)
            {
                found = Directory.EnumerateFiles(dir).Any(f =>
                {
                    string n = Path.GetFileName(f).ToLowerInvariant();
                    return n.StartsWith("ofl") || n.StartsWith("license") || n.StartsWith("licence") || n.StartsWith("copying");
                });
                if (found) break;
                dir = Path.GetDirectoryName(dir);
            }
            Assert.True(found, rel + " has no licence text next to it");
        }
    }

    [Fact]
    public void CreditsDataFileMatchesAssetsMd()
    {
        string root = Root();
        string expected = Credits.ToDataFile(Rows(root));
        string path = Path.Combine(root, "game", "assets", "credits.tsv");
        if (Environment.GetEnvironmentVariable("STARFOUNDRY_UPDATE_CREDITS") == "1") File.WriteAllText(path, expected);
        Assert.Equal(expected, File.ReadAllText(path).Replace("\r\n", "\n"));
    }

    [Fact]
    public void SfxMapOnlyNamesFilesThatExist()
    {
        string root = Root();
        var map = SfxMap.Parse(File.ReadAllText(Path.Combine(root, "game", "assets", "audio", "sfx_map.json")));
        foreach (var (id, e) in map.Entries)
        {
            Assert.True(Enum.TryParse<Sfx>(id, out _), "unknown sound id " + id);
            foreach (string f in e.Files) Assert.True(File.Exists(Path.Combine(root, "game", "assets", "audio", f)), "missing " + f);
        }
    }

    [Fact]
    public void SfxMapParsesEntries_AndSurvivesGarbage()
    {
        var map = SfxMap.Parse("{\"UiClick\":{\"files\":[\"a.ogg\",\"b.ogg\"],\"gainDb\":-4,\"pitchJitter\":0.03},\"Bad\":1}");
        Assert.True(map.TryGet(Sfx.UiClick, out var e));
        Assert.Equal(2, e.Files.Count);
        Assert.Equal(-4f, e.GainDb);
        Assert.False(map.TryGet(Sfx.Explosion, out _));
        Assert.Empty(SfxMap.Parse("not json").Entries);
    }
}
