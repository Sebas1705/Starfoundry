using System;
using System.Collections.Generic;
using System.Linq;
using Godot;
using Starfoundry.Sim;
using Starfoundry.Ui;

namespace Starfoundry.Game;

/// <summary>
/// The scenario side of the player's files: the scenario catalog (<c>content/scenarios</c> next to <c>core.json</c>), the progress record
/// (<c>user://progress.cfg</c>), the sandbox presets (<c>user://sandbox_presets.cfg</c>) and the goals sidecar of a save (<c>&lt;slot&gt;.goals</c>).
/// All parsing and the file formats are engine-free (<c>Starfoundry.Ui</c>); this class only reads and writes the files.
/// </summary>
public static class ScenarioStore
{
    public const string ProgressPath = "user://progress.cfg";
    public const string PresetsPath = "user://sandbox_presets.cfg";

    private static ContentDb? _catalogFor;
    private static ScenarioCatalog? _catalog;
    private static ProgressRecord? _progress;
    private static SandboxPresets? _presets;

    /// <summary>The scenarios of the content (empty, with an error printed, when the folder is missing or a file is invalid).</summary>
    public static ScenarioCatalog Catalog(ContentDb content, string? contentPath)
    {
        if (_catalog is not null && ReferenceEquals(_catalogFor, content)) return _catalog;
        _catalogFor = content;
        _catalog = ScenarioCatalog.Empty;
        if (contentPath is null) return _catalog;
        try
        {
            int slash = Math.Max(contentPath.LastIndexOf('/'), contentPath.LastIndexOf('\\'));
            string dir = (slash >= 0 ? contentPath[..slash] : ".") + "/scenarios";
            var files = new List<(string Name, string Json)>();
            foreach (string name in Godot.DirAccess.GetFilesAt(dir).Where(f => f.EndsWith(".json", StringComparison.Ordinal)).OrderBy(f => f, StringComparer.Ordinal))
                files.Add((name, Godot.FileAccess.GetFileAsString(dir + "/" + name)));
            if (files.Count == 0) GD.PrintErr("SCENARIOS none found in " + dir);
            else _catalog = ScenarioCatalog.FromJson(content, files);
        }
        catch (Exception e) { GD.PrintErr("SCENARIOS could not be loaded: " + e.Message); }
        return _catalog;
    }

    // ---- progress ----------------------------------------------------------------------------

    public static ProgressRecord Progress => _progress ??= ProgressRecord.Parse(ReadText(ProgressPath));

    public static void SaveProgress() => WriteText(ProgressPath, Progress.Serialize());

    // ---- sandbox presets ---------------------------------------------------------------------

    public static SandboxPresets Presets => _presets ??= SandboxPresets.Parse(ReadText(PresetsPath));

    public static void SavePresets() => WriteText(PresetsPath, Presets.Serialize());

    // ---- goals sidecar -----------------------------------------------------------------------

    public static string SidecarPath(string slot) => $"user://saves/{slot}.goals";

    public static void WriteSidecar(string slot, ProgressSnapshot snapshot) => WriteText(SidecarPath(slot), ProgressSnapshotText.Serialize(snapshot));

    /// <summary>The goals saved with a slot, or null when there is none, it is unreadable or it belongs to another save (a different tick).</summary>
    public static ProgressSnapshot? ReadSidecar(string slot, long saveTick)
    {
        var snap = ProgressSnapshotText.Parse(ReadText(SidecarPath(slot)));
        return snap is not null && snap.Tick == saveTick ? snap : null;
    }

    // ---- files -------------------------------------------------------------------------------

    private static string? ReadText(string path)
    {
        if (!Godot.FileAccess.FileExists(path)) return null;
        string text = Godot.FileAccess.GetFileAsString(path);
        return text;
    }

    private static void WriteText(string path, string text)
    {
        try
        {
            int slash = path.LastIndexOf('/');
            if (slash > "user://".Length) DirAccess.MakeDirRecursiveAbsolute(ProjectSettings.GlobalizePath(path[..slash]));
            using var f = Godot.FileAccess.Open(path, Godot.FileAccess.ModeFlags.Write);
            if (f is null) throw new System.IO.IOException(Godot.FileAccess.GetOpenError().ToString());
            f.StoreString(text);
        }
        catch (Exception e) { GD.PrintErr($"FILE could not write {path}: {e.Message}"); }
    }
}
