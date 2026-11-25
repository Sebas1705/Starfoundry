using System;
using System.IO;
using System.Linq;
using Godot;
using Starfoundry.Sim;
using Starfoundry.Ui;

namespace Starfoundry.Game;

/// <summary>
/// The start screen (the project's main scene): Continue, New game, Settings, Controls and Quit over a slowly turning sky.
/// Any command-line flag (demos, screenshots, the stress test, <c>--play</c>...) skips it and starts the game scene directly,
/// so every scripted run behaves as before. <c>--menu-shot=path</c> saves the menu, the settings page and, in the game scene,
/// the pause overlay, and quits.
/// </summary>
public partial class MainMenu : Control
{
    private const string GameScene = "res://Play.tscn";

    private Camera3D? _camera;
    private MenuScreen _menu = null!;
    private string? _menuShotPath;
    private int _frame;
    private double _time;

    public override void _Ready()
    {
        var args = OS.GetCmdlineUserArgs();
        UiFx.Install(this); // hover, press and click feedback for every button of every screen
        if (AudioTest.Handle(GetTree(), args)) return;
        AudioManager.Enabled = !AudioFlags.IsSilent(args);
        _menuShotPath = args.FirstOrDefault(a => a.StartsWith("--menu-shot="))?["--menu-shot=".Length..];
        // Any other flag means a scripted run or a screenshot: no menu.
        // --content=path only says where core.json is; it does not make the run scripted.
        _newGameShotPath = args.FirstOrDefault(a => a.StartsWith("--newgame-shot="))?["--newgame-shot=".Length..];
        if (args.Any(a => !a.StartsWith("--menu-shot=") && !a.StartsWith("--newgame-shot=") && a != "--ui-audit-menu" && !a.StartsWith("--audit-sizes=") && !a.StartsWith("--audit-shots=") && !a.StartsWith(ContentLocator.ContentFlag) && !a.StartsWith("--ui-lang=") && a != "--agent"))
        {
            Launch.Mode = Launch.Intent.New;
            CallDeferred(MethodName.StartGame);
            return;
        }

        SettingsStore.EnsureLoaded();
        if (args.Contains("--ui-lang=es")) Loc.Lang = Language.Es;
        if (args.Contains("--ui-lang=en")) Loc.Lang = Language.En;
        _content = ContentSource.Load(args, out var contentLookup, out string? contentError);
        if (_content is null)
        {
            ContentSource.ShowError(this, contentLookup, contentError);
            SetProcess(false);
            return;
        }
        _contentPath = contentLookup.Path;
        AudioManager.Create(this); // UI sounds in the menu (clicks, slider previews)
        Input.MouseMode = Input.MouseModeEnum.Visible;
        BuildBackground();

        _menu = new MenuScreen("menu.title", dim: false);
        string? save = Launch.NewestSave();
        _menu.AddEntry(() => Loc.T("menu.continue"), () => { Launch.Mode = Launch.Intent.Continue; Launch.ContinueSave = save; StartGame(); },
            () => save is not null, () => save is null ? Loc.T("menu.noSave") : SaveDetail(save));
        _menu.AddEntry(() => Loc.T("menu.new"), () => OpenNewGame());
        _menu.AddEntry(() => Loc.T("menu.settings"), () => _menu.Open(MenuScreen.Page.Settings));
        _menu.AddEntry(() => Loc.T("menu.controls"), () => _menu.Open(MenuScreen.Page.Controls));
        _menu.AddEntry(() => Loc.T("menu.quit"), () => GetTree().Quit());
        _menu.SetAnchorsPreset(LayoutPreset.FullRect);
        AddChild(_menu);
        if (args.Contains("--ui-audit-menu")) MenuAuditSetup(args);
    }

    private ContentDb? _content;
    private string? _contentPath;
    private string? _newGameShotPath;
    private NewGameScreen? _newGame;

    /// <summary>Opens the New Game screen over the menu; its Start button sets the <see cref="Launch"/> choice and starts the game scene.</summary>
    private void OpenNewGame(NewGameScreen.Tab tab = NewGameScreen.Tab.Campaign)
    {
        if (_content is null || _newGame is not null) return;
        var catalog = ScenarioStore.Catalog(_content, _contentPath);
        var screen = _newGame = new NewGameScreen(_content, catalog, tab);
        screen.Started += choice =>
        {
            Launch.StartScenario(choice.ScenarioId, choice.Rules, choice.Seed);
            StartGame();
        };
        screen.Cancelled += CloseNewGame;
        _menu.Visible = false;
        AddChild(screen);
    }

    private void CloseNewGame()
    {
        if (_newGame is null) return;
        _newGame.QueueFree();
        _newGame = null;
        _menu.Visible = true;
        _menu.Open(MenuScreen.Page.Main);
    }

    private void StartGame()
    {
        Launch.MenuShotPath = _menuShotPath;
        GetTree().ChangeSceneToFile(GameScene);
    }

    /// <summary>"Quick save - play time 12:34" for the newest save, or an empty string when the file cannot be read.</summary>
    private static string SaveDetail(string slot)
    {
        try
        {
            byte[] bytes = Godot.FileAccess.GetFileAsBytes(Launch.SavePath(slot));
            var info = SaveGame.Peek(new MemoryStream(bytes));
            return Loc.T("menu.continueInfo", Loc.T("menu.save." + slot), PlayLog.Format(info.Ticks / (double)Units.TicksPerSecond));
        }
        catch (Exception) { return ""; }
    }

    private void BuildBackground()
    {
        // The same procedural sky as the game, seen by a camera that turns slowly.
        var world = new Node3D();
        AddChild(world);
        Ambience.Build(world, Ambience.SystemSeed(20261120UL, 0));
        _camera = new Camera3D { Far = 500f, Fov = 70f };
        world.AddChild(_camera);
    }

    public override void _Process(double delta)
    {
        _time += delta;
        if (_camera is not null) _camera.Rotation = new Vector3(0.12f * MathF.Sin((float)_time * 0.13f), (float)_time * 0.05f, 0f);
        if (_menuAudit) { MenuAuditFrame(); return; }
        if (_newGameShotPath is not null) { NewGameShotFrame(); return; }
        if (_menuShotPath is null) return;
        _frame++;
        if (_frame == 45) Save(_menuShotPath);
        if (_frame == 46) _menu.Open(MenuScreen.Page.Settings);
        if (_frame == 60)
        {
            Save(_menuShotPath + ".settings.png");
            _menu.Open(MenuScreen.Page.Controls);
        }
        if (_frame == 90)
        {
            Save(_menuShotPath + ".controls.png");
            _menu.Open(MenuScreen.Page.Main);
            StartGame(); // the game scene saves the pause overlay and quits
        }
    }

    /// <summary><c>--newgame-shot=path</c>: saves the three tabs of the New Game screen (<c>path.campaign.png</c>, <c>path.story.png</c>, <c>path.sandbox.png</c>) and quits.</summary>
    private void NewGameShotFrame()
    {
        string path = _newGameShotPath!;
        _frame++;
        if (_frame == 10) OpenNewGame(NewGameScreen.Tab.Campaign);
        if (_frame == 40) { Save(path + ".campaign.png"); _newGame?.ShowTab(NewGameScreen.Tab.Story); }
        if (_frame == 70) { Save(path + ".story.png"); _newGame?.ShowTab(NewGameScreen.Tab.Sandbox); }
        if (_frame == 100) { Save(path + ".sandbox.png"); GetTree().Quit(); }
    }

    private void Save(string path) => GetViewport().GetTexture().GetImage().SavePng(path);
}
