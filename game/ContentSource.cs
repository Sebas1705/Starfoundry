using System;
using System.IO;
using Godot;
using Starfoundry.Sim;
using Starfoundry.Ui;

namespace Starfoundry.Game;

/// <summary>
/// Finds and loads <c>core.json</c> for the editor, a repository checkout and an exported build (see <see cref="ContentLocator"/>),
/// and shows a friendly error screen when it cannot.
/// </summary>
public static class ContentSource
{
    public const string ResourcePath = "res://content/core.json";

    public static ContentLookup Locate(string[] userArgs)
    {
        string? exeDir = null;
        try { exeDir = Path.GetDirectoryName(OS.GetExecutablePath()); } catch (Exception) { /* no executable path */ }
        string repo = ProjectSettings.GlobalizePath("res://../content/core.json");
        return ContentLocator.Resolve(ContentLocator.ExplicitPath(userArgs), ResourcePath, exeDir, repo, Godot.FileAccess.FileExists);
    }

    /// <summary>Loads the content; on failure <paramref name="error"/> is a short English reason and <paramref name="lookup"/> says where it looked.</summary>
    public static ContentDb? Load(string[] userArgs, out ContentLookup lookup, out string? error)
    {
        lookup = Locate(userArgs);
        error = null;
        if (lookup.Path is null) return null;
        try
        {
            string json = Godot.FileAccess.GetFileAsString(lookup.Path);
            if (string.IsNullOrEmpty(json)) throw new InvalidDataException("The file is empty or cannot be read.");
            return ContentDb.FromJson(json);
        }
        catch (Exception e)
        {
            error = e.Message;
            GD.PrintErr($"Could not load content from {lookup.Path}: {e.Message}");
            return null;
        }
    }

    /// <summary>Covers the screen with the error message (Spanish or English by the saved language) and a Quit button.</summary>
    public static void ShowError(Node host, ContentLookup lookup, string? error)
    {
        GD.PrintErr("Content not available.\n" + lookup.TriedText + (error is null ? "" : "\n" + error));
        SettingsStore.EnsureLoaded();
        Input.MouseMode = Input.MouseModeEnum.Visible;

        var layer = new CanvasLayer { Layer = 100 };
        var bg = new ColorRect { Color = new Color(0.04f, 0.05f, 0.08f) };
        bg.SetAnchorsPreset(Control.LayoutPreset.FullRect);
        layer.AddChild(bg);

        var center = new CenterContainer();
        center.SetAnchorsPreset(Control.LayoutPreset.FullRect);
        layer.AddChild(center);

        var box = new VBoxContainer { CustomMinimumSize = new Vector2(640, 0) };
        box.AddThemeConstantOverride("separation", 14);
        center.AddChild(box);

        box.AddChild(MakeLabel(Loc.T("content.error.title"), 30, new Color(1f, 0.8f, 0.4f)));
        box.AddChild(MakeLabel(Loc.T("content.error.body"), 18, Colors.White));
        if (error is not null) box.AddChild(MakeLabel(Loc.T("content.error.invalid") + "\n" + error, 16, new Color(1f, 0.6f, 0.6f)));
        box.AddChild(MakeLabel(Loc.T("content.error.tried") + "\n" + lookup.TriedText, 14, new Color(0.7f, 0.75f, 0.8f)));

        var quit = new Button { Text = Loc.T("content.error.quit"), CustomMinimumSize = new Vector2(160, 44) };
        quit.Pressed += () => host.GetTree().Quit(1);
        box.AddChild(quit);
        host.AddChild(layer);
        quit.GrabFocus();
    }

    private static Label MakeLabel(string text, int size, Color color)
    {
        var l = new Label { Text = text, AutowrapMode = TextServer.AutowrapMode.WordSmart };
        l.AddThemeFontSizeOverride("font_size", size);
        l.AddThemeColorOverride("font_color", color);
        return l;
    }
}

/// <summary>
/// The client-side content files that sit beside <c>core.json</c> and that the simulation never reads: <c>ship_handling.json</c> (turn dynamics) and
/// <c>cockpit_layouts.json</c> (where the console screens are). A missing or invalid file is reported once and the built-in defaults are used, so the
/// game always starts.
/// </summary>
public static class ClientContent
{
    private static string? _loadedFor;
    private static ShipHandling _handling = ShipHandling.Default;
    private static CockpitLayouts _layouts = CockpitLayouts.Default;
    private static PhysicalTable _physical = PhysicalTable.Default;

    public static ShipHandling Handling => _handling;
    public static CockpitLayouts Layouts => _layouts;
    public static PhysicalTable PhysicalUnits => _physical;

    /// <summary>Reads the sibling files of the content file at <paramref name="corePath"/> (once per path).</summary>
    public static void Load(string? corePath)
    {
        if (corePath is null || _loadedFor == corePath) return;
        _loadedFor = corePath;
        int slash = Math.Max(corePath.LastIndexOf('/'), corePath.LastIndexOf((char)92));
        string dir = slash >= 0 ? corePath[..slash] : ".";
        _handling = Read(dir + "/ship_handling.json", ShipHandling.FromJson, ShipHandling.Default);
        _layouts = Read(dir + "/cockpit_layouts.json", CockpitLayouts.FromJson, CockpitLayouts.Default);
        _physical = Read(dir + "/physical_units.json", PhysicalTable.FromJson, PhysicalTable.Default);
        Starfoundry.Ui.Physical.Table = _physical;
    }

    private static T Read<T>(string path, Func<string, T> parse, T fallback)
    {
        try
        {
            if (!Godot.FileAccess.FileExists(path)) { GD.PrintErr($"CONTENT {path} not found, using the built-in defaults"); return fallback; }
            return parse(Godot.FileAccess.GetFileAsString(path));
        }
        catch (Exception e)
        {
            GD.PrintErr($"CONTENT {path} is not valid ({e.Message}), using the built-in defaults");
            return fallback;
        }
    }
}
