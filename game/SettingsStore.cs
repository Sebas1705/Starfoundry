using System;
using System.Collections.Generic;
using Godot;
using Starfoundry.Sim;
using Starfoundry.Ui;

namespace Starfoundry.Game;

/// <summary>
/// The player's <see cref="GameSettings"/> for the running game: loaded once from <c>user://settings.cfg</c> (a Godot
/// <see cref="ConfigFile"/>), saved on every change and applied to the engine (language, master bus volume, fullscreen).
/// Mouse sensitivity, field of view and the vertical inversion are read from <see cref="Current"/> by the game each frame.
/// </summary>
public static class SettingsStore
{
    public const string Path = "user://settings.cfg";
    private const string Section = "settings";

    private static GameSettings? _current;

    /// <summary>Raised after a change has been stored and applied.</summary>
    public static event Action? Changed;

    public static GameSettings Current
    {
        get
        {
            EnsureLoaded();
            return _current!;
        }
    }

    /// <summary>Loads the file the first time (defaults from the OS locale when there is none) and applies it.</summary>
    public static void EnsureLoaded()
    {
        if (_current is not null) return;
        _current = Load();
        // A test flag, not stored: look at the half-size textures without touching the player's settings file.
        if (Array.IndexOf(OS.GetCmdlineUserArgs(), "--low-textures") >= 0) _current = _current with { LowTextures = true };
        Apply(_current);
    }

    private static GameSettings Load()
    {
        var defaults = GameSettings.Defaults(OS.GetLocale());
        var cfg = new ConfigFile();
        if (cfg.Load(Path) != Error.Ok || !cfg.HasSection(Section)) return defaults;
        var pairs = new List<KeyValuePair<string, string>>();
        foreach (string key in cfg.GetSectionKeys(Section))
            pairs.Add(new(key, cfg.GetValue(Section, key).AsString()));
        return GameSettings.FromPairs(pairs, defaults);
    }

    private static void Save(GameSettings s)
    {
        var cfg = new ConfigFile();
        foreach (var (key, value) in s.ToPairs()) cfg.SetValue(Section, key, value);
        Error e = cfg.Save(Path);
        if (e != Error.Ok) GD.PrintErr("SETTINGS save failed: " + e);
    }

    /// <summary>Stores, saves and applies new settings (clamped to their ranges).</summary>
    public static void Set(GameSettings settings)
    {
        EnsureLoaded();
        var s = settings.Clamped();
        if (s == _current) return;
        _current = s;
        Apply(s);
        Save(s);
        Changed?.Invoke();
    }

    /// <summary>The settings that reach the engine: language, master bus volume and the window mode.</summary>
    private static void Apply(GameSettings s)
    {
        Loc.Lang = s.Language;
        AudioServer.SetBusVolumeDb(0, s.MasterVolume <= 0.001f ? -80f : Mathf.LinearToDb(s.MasterVolume));
        AudioManager.ApplyBusVolumes(s);
        bool isFullscreen = DisplayServer.WindowGetMode() == DisplayServer.WindowMode.Fullscreen;
        if (s.Fullscreen != isFullscreen)
            DisplayServer.WindowSetMode(s.Fullscreen ? DisplayServer.WindowMode.Fullscreen : DisplayServer.WindowMode.Maximized);
    }
}

/// <summary>What the main menu asks the game scene to do; set before the scene changes, read once by <c>Play</c>.</summary>
public static class Launch
{
    public enum Intent { New, Continue }

    public static Intent Mode = Intent.New;

    /// <summary>
    /// <c>--agent</c>: an agent drives this window (see docs/prototypes/agent-play.md). The game then ignores the OS window focus
    /// (no focus: still flies and reads keys) and the main menu stays up. Nothing else changes.
    /// </summary>
    public static readonly bool Agent = Array.Exists(OS.GetCmdlineUserArgs(), a => a == "--agent");

    /// <summary>The save to load when <see cref="Mode"/> is <see cref="Intent.Continue"/> ("quicksave" or "autosave").</summary>
    public static string? ContinueSave;

    /// <summary>The scenario of a new game chosen on the New Game screen (null for the scripted runs and the legacy path).</summary>
    public static string? ScenarioId;

    /// <summary>The rules for the scenario (the campaign's difficulty, the sandbox form); null keeps the scenario's own.</summary>
    public static WorldRules? Rules;

    /// <summary>The galaxy seed for the new game; 0 means a random one (or the scenario's fixed seed).</summary>
    public static ulong Seed;

    /// <summary>Sets a new game of a scenario (and clears the continue request).</summary>
    public static void StartScenario(string id, WorldRules? rules, ulong seed)
    {
        Mode = Intent.New;
        ContinueSave = null;
        ScenarioId = id;
        Rules = rules;
        Seed = seed;
    }

    /// <summary>Forgets the chosen scenario (the next game takes the legacy path unless a scenario is set again).</summary>
    public static void ClearScenario()
    {
        ScenarioId = null;
        Rules = null;
        Seed = 0;
    }

    /// <summary><c>--menu-shot=path</c>: the menu shot continues in the game scene, which saves the pause overlay shots.</summary>
    public static string? MenuShotPath;

    public static string SavePath(string name) => $"user://saves/{name}.sfsave";

    /// <summary>The newest existing save slot ("quicksave" or "autosave"), or null.</summary>
    public static string? NewestSave()
    {
        return SaveSlots.Newest(name =>
        {
            string path = SavePath(name);
            return Godot.FileAccess.FileExists(path) ? Godot.FileAccess.GetModifiedTime(path) : null;
        });
    }
}
