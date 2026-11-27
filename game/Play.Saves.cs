using System;
using System.IO;
using System.Linq;
using Godot;
using Starfoundry.Sim;
using Starfoundry.Ui;

namespace Starfoundry.Game;

/// <summary>
/// Saving and loading in the game scene (ADR 0049). A save is a slot file of the saves folder: the replayable command log plus what the Save/Load screen shows
/// (name, kind, game, difficulty, seed, game time, wave, system, play time, date, a screenshot) and the goals sidecar of a scenario game. Four kinds:
/// <b>manual</b> (named by the player, on the Save/Load screen), <b>quick</b> (F5 writes its own slot, F9 loads it), <b>checkpoint</b> (automatic, at a moment
/// worth coming back to, a ring of the newest few per game: see <see cref="CheckpointWatcher"/>) and <b>victory</b>. Loading any of them replays the log
/// and compares the checksum, as before. A lost world never writes a checkpoint, so the end screen can always return to the last one.
/// </summary>
public partial class Play
{
    private GameMode _gameMode = GameMode.Campaign;
    private bool _threatsOn = true;
    private bool _saveTest;
    private bool _noSaves; // --no-saves: nothing is written (agent runs that must not leave files)
    private string _gameId = "";
    private double _playSeconds;
    private readonly CheckpointWatcher _checkpoints = new();
    private byte[]? _cleanThumb;
    private CanvasLayer? _savesLayer;
    private SaveLoadScreen? _savesScreen;
    private SavesReturn _savesReturn;
    private bool _savesSetupDone;

    private enum SavesReturn { Game, Pause, End }

    private bool SavesOpen => _savesScreen is { Visible: true };

    private static double RealNow => Time.GetTicksMsec() / 1000.0;

    private static string NewGameId() => Random.Shared.NextInt64(1, long.MaxValue).ToString("x", System.Globalization.CultureInfo.InvariantCulture);

    // ---- setup -----------------------------------------------------------------------------------------------------------

    /// <summary>
    /// Chooses the saves folder before anything reads a save: the player's own, or a throwaway one for scripted runs (shots, audits, benchmarks), which must
    /// never add files to the player's saves or change which slot Continue picks. <c>--saves-dir=path</c> forces a folder (the checks use it).
    /// </summary>
    private void SavesFolderSetup(string[] args)
    {
        if (_savesSetupDone) return;
        _savesSetupDone = true;
        _noSaves = args.Contains("--no-saves");
        if (args.FirstOrDefault(a => a.StartsWith("--saves-dir="))?["--saves-dir=".Length..] is { Length: > 0 } dir) SaveStore.UseFolder(dir);
        else if (args.Any(a => LaunchFlags.IsScripted(a) && !a.StartsWith("--continue=", StringComparison.Ordinal))) SaveStore.UseFolder(SaveStore.ScriptedPath);
        else SaveStore.Prepare();
        _gameId = NewGameId();
        _checkpoints.Reset(RealNow);
    }

    // ---- writing ---------------------------------------------------------------------------------------------------------

    private SaveParts BuildSaveParts(SaveKind kind, string name, string reason, byte[]? thumbnail)
    {
        var ms = new MemoryStream();
        SaveGame.Write(ms, _world, _content, _gameMode, _galaxySeed, _threatsOn, Player, _stationRules, _structuresOn,
            _scenario is not null || !Net.Rules.IsDefault ? Net.Rules : null, _scenario);
        byte[] payload = ms.ToArray();
        // The goals engine is recomputed from the world on load, so when each goal was done travels beside the save.
        string goals = _progress is not null ? ProgressSnapshotText.Serialize(_progress.Export(_world.Tick, _continueEndless)) : "";
        int system = Net.CurrentSystem(Player);
        string systemName = Net.Galaxy is { } g && system >= 0 && system < g.Systems.Count ? g.Systems[system].Name : "";
        string preset = _scenario is { PlayerRules: false } ? "" : Net.Rules.PresetId() ?? "custom";
        var meta = new SaveMeta
        {
            Name = SaveNaming.CleanName(name),
            Kind = kind,
            Reason = reason,
            GameId = _gameId,
            ScenarioId = _scenario?.Id,
            Preset = preset,
            Mode = (byte)_gameMode,
            Seed = _galaxySeed,
            Ticks = _world.Tick,
            Wave = Net.WavesSpawned,
            System = systemName,
            PlaySeconds = _playSeconds,
            SavedAt = SaveStore.Now,
            ContentHash = SaveFile.ContentHashOf(payload),
            LogVersion = SaveFile.LogVersionOf(payload),
        };
        return new SaveParts(meta, thumbnail ?? Array.Empty<byte>(), goals, payload);
    }

    /// <summary>
    /// Writes a save and returns its id (null when nothing was written). <paramref name="id"/> overwrites that slot (the previous contents are kept as <c>.bak</c>);
    /// without it a manual save gets a new file, the quick save its own slot, a checkpoint a new one in the ring and a victory the game's victory slot.
    /// </summary>
    private string? WriteSave(SaveKind kind, string name, string reason, string? id = null, bool silent = false)
    {
        if (_demo || _noSaves) return null;
        if (kind == SaveKind.Auto && Net.StructureRules && Net.ShipLost(Player)) return null; // a lost world never becomes a checkpoint
        try
        {
            var folder = SaveStore.Folder;
            var parts = BuildSaveParts(kind, name, reason, _cleanThumb ?? SaveStore.CaptureThumbnail(GetViewport()));
            id ??= kind switch
            {
                SaveKind.Quick => SaveNaming.QuickId,
                SaveKind.Victory => SaveNaming.VictoryId(_gameId),
                SaveKind.Auto => SaveNaming.UniqueId(folder.Exists, SaveNaming.AutoId(parts.Meta.SavedAt, reason)),
                _ => SaveNaming.UniqueId(folder.Exists, SaveNaming.Slug(name)),
            };
            folder.Write(id, parts);
            if (kind == SaveKind.Auto) folder.PruneCheckpoints(_gameId, SaveRotation.CheckpointsKept);
            _checkpoints.Wrote(RealNow);
            SaveStore.Remember(id);
            if (!silent)
            {
                string clock = PlayLog.Format(Net.Tick / (double)Units.TicksPerSecond);
                Toast(kind == SaveKind.Quick ? L("toast.quickSaved", clock) : L("toast.saved.named", SaveText.Title(parts.Meta), clock));
            }
            return id;
        }
        catch (Exception e)
        {
            GD.PrintErr("SAVE failed: " + e.Message);
            Toast(L("toast.saveFailed", e.Message));
            return null;
        }
    }

    private void QuickSave() => WriteSave(SaveKind.Quick, "", SaveReasons.QuickSave, silent: false);

    /// <summary>An automatic save at a moment worth coming back to (see <see cref="CheckpointWatcher"/>).</summary>
    private void Checkpoint(string reason)
    {
        if (WriteSave(SaveKind.Auto, "", reason, silent: true) is not null) Toast(L("toast.checkpoint", L("saves.reason." + reason)), 3.5);
    }

    /// <summary>Per frame: counts the real play time and takes a checkpoint when the watcher says so (a wave about to arrive, a station, a research, docking, every five minutes).</summary>
    private void CheckpointTick(float dt)
    {
        if (_demo || _noSaves) return;
        if (!_paused) _playSeconds += dt;
        if (EndOpen || PauseOpen || SavesOpen || StoryOpen || _uiAudit) return;
        if (Net.StructureRules && Net.ShipLost(Player)) return; // a lost world never replaces a checkpoint
        int system = Math.Max(0, Net.CurrentSystem(Player));
        bool warning = Net.ThreatActive && Net.GetSystemThreat(system).WarningRemaining > 0;
        var sample = new CheckpointSample(IsDockedNow, warning, system, (int)Net.StationsFounded, Net.ResearchedCount);
        if (_checkpoints.Observe(sample, RealNow) is { } reason) Checkpoint(reason);
    }

    /// <summary>The checkpoint before a jump: what the player would come back to if the other system turns out badly.</summary>
    private void CheckpointBeforeJump()
    {
        if (!_demo && !_noSaves && _checkpoints.Request(RealNow)) Checkpoint(SaveReasons.Jump);
    }

    // ---- reading ---------------------------------------------------------------------------------------------------------

    private SaveCompat CompatOf(SaveMeta meta) => SaveStore.Check(meta, _content, _scenarios);

    private string? ScenarioNameOf(SaveMeta meta) => meta.ScenarioId is { } id && _scenarios.Find(id) is { } s ? s.Name(Loc.Lang == Language.Es) : meta.ScenarioId;

    private void QuickLoad()
    {
        if (!SaveStore.Folder.Exists(SaveNaming.QuickId)) { Toast(L("toast.noQuick")); return; }
        LoadSave(SaveNaming.QuickId);
    }

    private bool LoadSave(string id) => LoadSave(id, out _);

    /// <summary>
    /// Loads a slot by replaying it (the game freezes for a second or two). A save that this game cannot replay (other content, a newer format, a damaged file) is
    /// refused with its reason and the file is left exactly as it was. A save of another galaxy cannot be loaded into this scene (its deposits and sky are built
    /// from the seed): the scene is reopened on it.
    /// </summary>
    private bool LoadSave(string id, out string? failure)
    {
        failure = null;
        if (_demo) return false;
        SaveParts parts;
        try { parts = SaveStore.Folder.Read(id); }
        catch (Exception e)
        {
            failure = L(SaveSlots.LoadFailureKey(e));
            GD.PrintErr($"LOAD failed ({id}): {e.Message}");
            Toast(L("toast.loadFailed", failure));
            return false;
        }
        var compat = CompatOf(parts.Meta);
        if (compat != SaveCompat.Ok)
        {
            failure = L(SaveCompatibility.Key(compat));
            GD.PrintErr($"LOAD refused ({id}): {compat}");
            Toast(L("toast.loadFailed", L(compat == SaveCompat.OtherContent ? "toast.loadFailed.content" : compat == SaveCompat.NewerFormat ? "toast.loadFailed.newer" : "toast.loadFailed.scenario")));
            return false;
        }
        try
        {
            var peek = SaveGame.Peek(new MemoryStream(parts.Payload));
            if (peek.GalaxySeed != _galaxySeed || peek.ScenarioId != _scenario?.Id)
            {
                FinishPlayLog();
                Launch.Mode = Launch.Intent.Continue;
                Launch.ContinueSave = id;
                Launch.MenuShotPath = null;
                GetTree().CallDeferred(SceneTree.MethodName.ReloadCurrentScene);
                return true;
            }
            var world = SaveGame.Read(new MemoryStream(parts.Payload), _content, out var info, _scenarios);
            _world = world;
            var snapshot = _scenario is null ? null : ProgressSnapshotText.Parse(parts.Goals);
            _loadedSnapshot = snapshot is not null && snapshot.Tick == info.Ticks ? snapshot : null;
            _gameMode = info.Mode;
            _threatsOn = info.Threats;
            // The save decides the rules, whatever the running game started with.
            bool structures = info.Structures && _content.StarterShip is not null;
            bool rules = info.StationRules && !structures; // structures win over the station rules
            bool rulesChanged = _stationRules != rules || _structuresOn != structures;
            _stationRules = rules;
            _structuresOn = structures;
            if (rulesChanged) RebuildPalette();
            _stationView.Invalidate();
            _gameId = parts.Meta.GameId.Length > 0 ? parts.Meta.GameId : NewGameId();
            _playSeconds = parts.Meta.PlaySeconds;
            ResetAfterLoad();
            _checkpoints.Reset(RealNow);
            SaveStore.Remember(id);
            Toast(L("toast.loaded", PlayLog.Format(info.Ticks / (double)Units.TicksPerSecond)));
            return true;
        }
        catch (Exception e)
        {
            GD.PrintErr($"LOAD failed ({id}): {e.Message}");
            failure = L(SaveSlots.LoadFailureKey(e));
            Toast(L("toast.loadFailed", failure));
            return false;
        }
    }

    // ---- the Save/Load screen --------------------------------------------------------------------------------------------

    private string SuggestedSaveName()
    {
        string game = _scenario is { } s ? s.Name(Loc.Lang == Language.Es) : L(_gameMode == GameMode.Sandbox ? "saves.mode.sandbox" : "saves.mode.campaign");
        string suggestion = SaveNaming.SuggestName(game, Net.Tick / (double)Units.TicksPerSecond);
        return SaveNaming.UniqueName(SaveStore.Folder.List().Where(e => e.Meta is not null).Select(e => e.Meta!.Name), suggestion);
    }

    /// <summary>Opens the Save/Load screen over the pause menu, the game or the end screen. The screenshot of a save made there is the game as it was before the screen opened.</summary>
    private void OpenSaves(SavesReturn from)
    {
        if (_demo || _noSaves || SavesOpen) return;
        if (from == SavesReturn.Pause && _pauseMenu is not null) _pauseMenu.Visible = false;
        else if (from == SavesReturn.Game) { _pausedBefore = _paused; _paused = true; _wantCapture = false; }
        _cleanThumb = from == SavesReturn.Game ? SaveStore.CaptureThumbnail(GetViewport()) : _cleanThumb;
        _savesReturn = from;
        var ctx = new SaveLoadScreen.Context(SaveStore.Folder, ScenarioNameOf, CompatOf, _gameId, from == SavesReturn.End ? null : SuggestedSaveName());
        var screen = _savesScreen = new SaveLoadScreen(ctx);
        screen.LoadRequested += OnSavesLoad;
        screen.SaveRequested += OnSavesSave;
        screen.CloseRequested += CloseSaves;
        _savesLayer ??= new CanvasLayer { Layer = 22, Name = "SavesLayer" };
        if (_savesLayer.GetParent() is null) AddChild(_savesLayer);
        _savesLayer.AddChild(screen);
        if (SaveStore.Migrated is { Wrapped: > 0 } m && !_migratedShown) { _migratedShown = true; screen.Refresh(L("saves.status.migrated", m.Wrapped)); }
    }

    private bool _migratedShown;

    private void FreeSaves()
    {
        if (_savesScreen is null) return;
        _savesScreen.Visible = false;
        _savesScreen.QueueFree();
        _savesScreen = null;
    }

    private void CloseSaves()
    {
        FreeSaves();
        _cleanThumb = null;
        switch (_savesReturn)
        {
            case SavesReturn.Pause: _pauseMenu?.Open(MenuScreen.Page.Main); break;
            case SavesReturn.Game: _paused = _pausedBefore; _wantCapture = true; break;
        }
    }

    private void OnSavesLoad(string id)
    {
        if (!LoadSave(id, out string? why)) { _savesScreen?.Refresh(L("toast.loadFailed", why ?? ""), error: true); return; }
        FreeSaves();
        _cleanThumb = null;
        if (_pauseMenu is not null) _pauseMenu.Visible = false;
        _paused = false;
        _wantCapture = true;
    }

    private void OnSavesSave(string name, string? overwriteId)
    {
        string? id;
        if (overwriteId is not null && SaveStore.Folder.Find(overwriteId)?.Meta is { } old)
        {
            string reason = old.Kind switch { SaveKind.Quick => SaveReasons.QuickSave, SaveKind.Victory => SaveReasons.Victory, _ => SaveReasons.Manual };
            id = WriteSave(old.Kind, old.Name, reason, overwriteId, silent: true);
            name = old.Name.Length > 0 ? old.Name : SaveText.Title(old);
        }
        else id = WriteSave(SaveKind.Manual, name, SaveReasons.Manual, silent: true);
        if (id is null) _savesScreen?.Refresh(L("saves.status.failed", L("pause.saveFailed")), error: true);
        else _savesScreen?.Refresh(L("saves.status.saved", name), id);
    }

    // ---- the end screen -------------------------------------------------------------------------------------------------

    /// <summary>The save "reload last checkpoint" goes back to: the newest loadable save of this game (the quick save counts), else the newest loadable one.</summary>
    private SaveEntry? LastCheckpoint() => SaveList.LastCheckpoint(SaveStore.Folder.List(), _gameId, m => CompatOf(m) == SaveCompat.Ok);

    private void LoadLastAfterDefeat()
    {
        if (LastCheckpoint() is not { } last || !LoadSave(last.Id)) return;
        _endScreen?.Dismiss();
        _paused = false;
        _wantCapture = true;
    }
}
