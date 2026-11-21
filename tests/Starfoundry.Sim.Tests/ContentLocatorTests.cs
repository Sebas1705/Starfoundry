using Starfoundry.Ui;
using Xunit;

namespace Starfoundry.Sim.Tests;

public class ContentLocatorTests
{
    private const string Res = "res://content/core.json";
    private const string Exe = "C:/game/content/core.json";
    private const string Repo = "C:/repo/content/core.json";

    private static Func<string, bool> Only(params string[] existing) => p => existing.Contains(p);

    [Fact]
    public void Explicit_path_wins_over_everything_else()
    {
        var r = ContentLocator.Resolve("D:/mine/core.json", Res, "C:/game", Repo, Only("D:/mine/core.json", Res, Exe, Repo));
        Assert.Equal("D:/mine/core.json", r.Path);
        Assert.True(r.Found);
    }

    [Fact]
    public void Missing_explicit_path_is_an_error_not_a_silent_fallback()
    {
        var r = ContentLocator.Resolve("D:/typo/core.json", Res, "C:/game", Repo, Only(Res, Exe, Repo));
        Assert.False(r.Found);
        Assert.Equal(new[] { "D:/typo/core.json" }, r.Tried);
    }

    [Fact]
    public void Order_is_resource_then_exe_folder_then_repo()
    {
        Assert.Equal(Res, ContentLocator.Resolve(null, Res, "C:/game", Repo, Only(Res, Exe, Repo)).Path);
        Assert.Equal(Exe, ContentLocator.Resolve(null, Res, "C:/game", Repo, Only(Exe, Repo)).Path);
        Assert.Equal(Repo, ContentLocator.Resolve(null, Res, "C:/game", Repo, Only(Repo)).Path);
    }

    [Fact]
    public void Missing_everywhere_lists_every_tried_location_in_order()
    {
        var r = ContentLocator.Resolve(null, Res, "C:\\game\\", Repo, Only());
        Assert.False(r.Found);
        Assert.Null(r.Path);
        Assert.Equal(new[] { Res, Exe, Repo }, r.Tried);
        Assert.Equal($"- {Res}\n- {Exe}\n- {Repo}", r.TriedText);
    }

    [Fact]
    public void Optional_candidates_are_skipped_and_duplicates_tried_once()
    {
        var r = ContentLocator.Resolve("", null, null, Repo, Only());
        Assert.Equal(new[] { Repo }, r.Tried);
        var d = ContentLocator.Resolve(null, Repo, null, Repo, Only());
        Assert.Single(d.Tried);
        Assert.Empty(ContentLocator.Resolve(null, null, null, null, Only()).Tried);
    }

    [Fact]
    public void Content_flag_is_parsed_from_the_arguments()
    {
        Assert.Equal("C:/x/core.json", ContentLocator.ExplicitPath(new[] { "--demo", "--content=C:/x/core.json" }));
        Assert.Equal("a b/core.json", ContentLocator.ExplicitPath(new[] { "--content=\"a b/core.json\"" }));
        Assert.Null(ContentLocator.ExplicitPath(new[] { "--demo", "--content=" }));
        Assert.Null(ContentLocator.ExplicitPath(Array.Empty<string>()));
    }

    [Fact]
    public void InDir_normalises_separators()
    {
        Assert.Equal("C:/a/b/core.json", ContentLocator.InDir("C:\\a\\b\\"));
        Assert.Equal("res://content/core.json", ContentLocator.InDir("res://content/"));
    }

    [Fact]
    public void Error_screen_text_exists_in_both_languages()
    {
        foreach (string key in new[] { "content.error.title", "content.error.body", "content.error.tried", "content.error.invalid", "content.error.quit" })
        {
            Assert.True(Loc.Has(key), key);
            var (es, en) = Loc.All.First(p => p.Key == key).Value;
            Assert.False(string.IsNullOrWhiteSpace(es));
            Assert.False(string.IsNullOrWhiteSpace(en));
        }
    }
}
