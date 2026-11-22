using System;
using System.Collections.Generic;
using System.Linq;
using Godot;
using Starfoundry.Sim;
using Starfoundry.Ui;

namespace Starfoundry.Game;

/// <summary>
/// The end of a played game: <see cref="GameOutcome"/> is checked every frame, and a win or a loss pauses the simulation and shows
/// <see cref="EndScreen"/>. Victory writes an extra <c>victory</c> save and offers to keep playing; defeat keeps the autosave and the
/// quick save (a lost world is never autosaved over them) and offers to load them. <c>--end-shot=path</c> renders both overlays.
/// </summary>
public partial class Play
{
    private EndScreen? _endScreen;
    private bool _outcomeShown;
    private bool _continueEndless;

    private bool EndOpen => _endScreen is { Visible: true };

    private void BuildEndScreen()
    {
        var layer = new CanvasLayer { Layer = 21 };
        AddChild(layer);
        _endScreen = new EndScreen();
        var root = new Control { MouseFilter = Control.MouseFilterEnum.Ignore };
        root.SetAnchorsAndOffsetsPreset(Control.LayoutPreset.FullRect);
        layer.AddChild(root);
        root.AddChild(_endScreen);
    }

    /// <summary>Per frame: refreshes the goal from the settings and looks for a win or a loss.</summary>
    private void UpdateOutcome()
    {
        if (_demo || _endScreen is null) return;
        if (_progress is not null) { UpdateScenarioOutcome(); return; }
        _goal = _endShotGoal ?? GoalSettings.From(SettingsStore.Current);
        if (_outcomeShown || _gameMode != GameMode.Campaign || !Net.ThreatActive || _defenceShotPath is not null || !Net.HasPlayer(Player)) return;
        var r = GameOutcome.Evaluate(Net, Player, _goal);
        if (r.Kind == OutcomeKind.Playing) return;
        if (r.Kind == OutcomeKind.Won && _continueEndless) return;
        ShowOutcome(r);
    }

    private void ShowOutcome(OutcomeResult r)
    {
        if (_endScreen is null) return;
        _outcomeShown = true;
        _paused = true;
        _wantCapture = false;
        LogOutcome(r);
        bool victory = r.Kind == OutcomeKind.Won;
        string note;
        var choices = new List<EndScreen.Choice>();
        if (victory)
        {
            bool saved = SaveGameToDisk("victory", silent: true);
            note = saved ? L("end.victory.note") : "";
            choices.Add(new EndScreen.Choice(L("end.continue"), ContinueEndless));
            choices.Add(new EndScreen.Choice(L("end.mainMenu"), () => { SaveGameToDisk("autosave", silent: true); GoToMainMenu(); }));
            choices.Add(new EndScreen.Choice(L("end.quit"), () => { FinishPlayLog(); GetTree().Quit(); }));
        }
        else
        {
            string? last = Launch.NewestSave();
            note = L(last is null ? "end.defeat.noSave" : "end.defeat.note");
            choices.Add(new EndScreen.Choice(L("end.loadLast"), () => LoadLastAfterDefeat(), last is not null));
            choices.Add(new EndScreen.Choice(L("end.newGame"), NewGameFromEnd));
            choices.Add(new EndScreen.Choice(L("end.mainMenu"), GoToMainMenu)); // no autosave: the lost world must not replace the good one
        }
        _endScreen.Present(r, victory, _goal.WavesToWin, note, choices, _galaxySeed);
    }

    private void ContinueEndless()
    {
        _continueEndless = true;
        _outcomeShown = false;
        _endScreen?.Dismiss();
        _paused = false;
        _wantCapture = true;
    }

    private void LoadLastAfterDefeat()
    {
        string? last = Launch.NewestSave();
        if (last is null || !LoadGameFromDisk(last)) return;
        _endScreen?.Dismiss();
        _paused = false;
        _wantCapture = true;
    }

    private void NewGameFromEnd()
    {
        FinishPlayLog();
        Launch.Mode = Launch.Intent.New;
        Launch.ContinueSave = null;
        Launch.MenuShotPath = null;
        GetTree().ReloadCurrentScene();
    }

    private void GoToMainMenu()
    {
        FinishPlayLog();
        Launch.MenuShotPath = null;
        GetTree().ChangeSceneToFile("res://MainMenu.tscn");
    }

    private void LogOutcome(OutcomeResult r)
    {
        if (_playLog is null) return;
        var s = r.Stats;
        string text = r.Kind == OutcomeKind.Won
            ? $"victory: {s.WavesSurvived} waves survived with a station"
            : "defeat: the ship was lost";
        text += $" (galaxy seed {_galaxySeed}, modules built {s.ModulesBuilt}, stations {s.StationsFounded} founded and {s.StationsLost} lost, mined {s.MinedByHand} by hand and {s.MinedByDrones} by drones, " +
                $"modules lost {s.ModulesLost}, machines lost {s.MachinesLost})";
        _logFile?.StoreLine(PlayLog.Line(_playLog.Note(Net, "Outcome", text)));
        _logFile?.Flush();
    }

    // ---- --end-shot -------------------------------------------------------------------------------------------------

    private string? _endShotPath;
    private GoalSettings? _endShotGoal;
    private int _endFrame;

    /// <summary>
    /// <c>--end-shot=path</c>: a structures campaign world where the outcome is forced. The victory overlay (stats made up for the
    /// picture, the win rule itself is covered by tests) is saved as <c>path.victory.png</c>; then the cockpit is destroyed for real and
    /// the defeat overlay goes through the normal check and is saved as <c>path.defeat.png</c>.
    /// </summary>
    private void EndShotSetup(string[] args)
    {
        _wantCapture = false;
        _shotPhase = 3; // the structures-shot script stays out of it
        _endShotGoal = new GoalSettings(10);
        if (args.Contains("--ui-lang=es")) Loc.Lang = Language.Es;
        if (args.Contains("--ui-lang=en")) Loc.Lang = Language.En;
        if (_scenario is not null)
        {
            // Level 3 already has its station: only some time on the clock, short of the first wave.
            _briefingShown = true;
            _world.Step(Units.TicksPerSecond * 60);
            return;
        }
        int ship = ShipStruct;
        if (ship < 0) return;
        var o = ShipPosition(0f);
        long cu = Units.CellUnits;
        int st = Net.CreateStructure(Player, StructureKind.Station, ((long)o.X + 30) * cu, (long)o.Y * cu, (long)o.Z * cu);
        foreach (var (x, k) in new[] { (0, ModuleKind.Core), (1, ModuleKind.Workshop), (2, ModuleKind.Defence) }) Net.AddStructureModuleUnchecked(st, x, 0, 0, k);
        _world.Step(Units.TicksPerSecond * 60 * 17 + 23 * Units.TicksPerSecond); // some time on the clock
    }

    private void EndShotFrame()
    {
        if (_endShotPath is null) return;
        _endFrame++;
        if (_scenario is not null && _progress is not null)
        {
            ScenarioEndShotFrame();
            return;
        }
        if (_endFrame == 20)
            ShowOutcome(new OutcomeResult(OutcomeKind.Won, OutcomeReason.WavesSurvived,
                new OutcomeStats(Net.Tick / (double)Units.TicksPerSecond, 14, 2, 0, 3, 10, 412, 186, 5)));
        if (_endFrame == 50) Save(_endShotPath + ".victory.png");
        if (_endFrame == 51)
        {
            ContinueEndless();
            var s = Net.ShipStructure(Player);
            foreach (var (k, kind) in Net.GetStructure(s).Modules.Where(m => m.Value == ModuleKind.Cockpit).Select(m => (m.Key, m.Value)).ToList())
                ShotHit(s, k.Mx, k.My, k.Mz, 9999);
            _world.Step();
        }
        if (_endFrame == 80) Save(_endShotPath + ".defeat.png");
        if (_endFrame == 81)
        {
            // The victory save made by the overlay is only a by-product of the picture: do not leave it in the player's folder.
            string victory = ProjectSettings.GlobalizePath(SavePath("victory"));
            if (System.IO.File.Exists(victory)) { GD.Print("SHOT victory save was written, removing it"); System.IO.File.Delete(victory); }
            GetTree().Quit();
        }
    }

    /// <summary>Scenario version of the end shots: a made-up win with two stars (the bonus goal failed), then a real defeat.</summary>
    private void ScenarioEndShotFrame()
    {
        string path = _endShotPath!;
        if (_endFrame == 20)
        {
            var goals = _progress!.Goals.Select(g => g.Goal.Id == "no_losses"
                ? new GoalSnapshot(g.Goal.Id, GoalStatus.Failed, 0, null, 612.0)
                : new GoalSnapshot(g.Goal.Id, GoalStatus.Done, g.Target, 1180.0, null)).ToList();
            _progress.Restore(new ProgressSnapshot(Net.Tick, goals, new List<(int, double)>(), OutcomeKind.Won, OutcomeReason.WavesSurvived, 2, 1523.0, false));
            ShowScenarioOutcome();
        }
        if (_endFrame == 50) Save(path + ".victory.png");
        if (_endFrame == 51)
        {
            ContinueEndless();
            _progress = new ScenarioProgress(_content, _scenario!);
            _briefingShown = true;
            RebuildGoalRows();
            var s = Net.ShipStructure(Player);
            foreach (var (k, kind) in Net.GetStructure(s).Modules.Where(m => m.Value == ModuleKind.Cockpit).Select(m => (m.Key, m.Value)).ToList())
                ShotHit(s, k.Mx, k.My, k.Mz, 9999);
            _world.Step();
        }
        if (_endFrame == 80) Save(path + ".defeat.png");
        if (_endFrame == 81) GetTree().Quit();
    }

    private void Save(string path)
    {
        GetViewport().GetTexture().GetImage().SavePng(path);
        GD.Print("SHOT saved " + path);
    }
}
