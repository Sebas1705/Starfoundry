using System.Collections.Generic;
using System.Linq;
using Godot;
using Starfoundry.Sim;
using Starfoundry.Ui;

namespace Starfoundry.Game;

public partial class MainMenu
{
    /// <summary>The flag of the release smoke test: print what a packaged build can see, then quit (<c>docs/release.md</c>).</summary>
    public const string SelfCheckFlag = "--self-check";

    /// <summary>
    /// <c>--self-check</c>: prints one <c>SELFCHECK</c> line per fact (version, content, scenarios, credits, fonts, models, user folder) and
    /// quits with code 0, or 1 when something a packaged build needs is missing. Used by <c>tools/release</c> against the exported and the
    /// installed game; it opens no window of its own and writes nothing to the user folder.
    /// </summary>
    private void RunSelfCheck(ContentDb? content, string? contentPath, string? contentError)
    {
        var failures = new List<string>();
        void Line(string key, string value) => GD.Print($"SELFCHECK {key}={value}");
        void Need(bool ok, string what) { if (!ok) failures.Add(what); }

        Line("version", BuildInfo.Version);
        Line("content", contentPath ?? "MISSING" + (contentError is null ? "" : " (" + contentError + ")"));
        Need(content is not null, "content/core.json was not found or is invalid");

        int scenarios = content is null ? 0 : ScenarioStore.Catalog(content, contentPath).All.Count;
        Line("scenarios", scenarios.ToString());
        Need(scenarios > 0, "no scenarios were found next to core.json");

        string tsv = FileAccess.FileExists("res://assets/credits.tsv") ? FileAccess.GetFileAsString("res://assets/credits.tsv") : "";
        var packs = Credits.ParseDataFile(tsv);
        Line("credits", string.Join("; ", packs.Select(p => p.Pack)));
        Need(packs.Count > 0, "assets/credits.tsv is missing or empty (the Credits page would be empty)");

        string[] fonts = { "inter/Inter-Variable.ttf", "rajdhani/Rajdhani-SemiBold.ttf", "jetbrains-mono/JetBrainsMono-Regular.ttf" };
        foreach (string f in fonts)
        {
            bool ok = ResourceLoader.Exists("res://assets/fonts/" + f);
            Line("font " + f, ok ? "ok" : "MISSING");
            Need(ok, "font " + f + " is missing");
        }
        foreach (string licence in new[] { "inter", "rajdhani", "jetbrains-mono" })
            Need(FileAccess.FileExists($"res://assets/fonts/{licence}/OFL.txt"), $"the licence text of {licence} is missing");

        bool model = ResourceLoader.Exists("res://assets/models/ship.glb");
        Line("model ship.glb", model ? "ok" : "MISSING");
        Need(model, "assets/models/ship.glb is missing");
        Line("scenes", ResourceLoader.Exists("res://Play.tscn") && ResourceLoader.Exists("res://MainMenu.tscn") ? "ok" : "MISSING");
        Need(ResourceLoader.Exists("res://Play.tscn"), "Play.tscn is missing");
        Line("userdir", OS.GetUserDataDir());

        foreach (string f in failures) GD.PrintErr("SELFCHECK FAIL " + f);
        GD.Print(failures.Count == 0 ? "SELFCHECK PASS" : "SELFCHECK FAIL");
        GetTree().Quit(failures.Count == 0 ? 0 : 1);
    }
}
