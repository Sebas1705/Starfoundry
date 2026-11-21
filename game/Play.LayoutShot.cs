using System.Linq;
using Godot;
using Starfoundry.Sim;
using Starfoundry.Ui;

namespace Starfoundry.Game;

/// <summary>
/// <c>--layout-shot=path</c>: the structures campaign with the HUD busy (three long toasts, the steps panel, the hold above the mining
/// limit) rendered at 1280x720 (<c>path.720.png</c>) and 1920x1080 (<c>path.1080.png</c>), then again with the details block and the star
/// map open (<c>path.720.full.png</c>, <c>path.1080.full.png</c>). <c>--ui-lang=es</c> renders them in Spanish. Seed fixed, nothing is saved.
/// A way to check, by looking, that nothing is clipped or overlapping at the common window sizes.
/// </summary>
public partial class Play
{
    private string? _layoutShotPath;
    private int _layoutFrame;

    private void LayoutShotSetup(string[] args)
    {
        _wantCapture = false;
        if (args.Contains("--ui-lang=es")) Loc.Lang = Language.Es;
        if (args.Contains("--ui-lang=en")) Loc.Lang = Language.En;
        if (!_structuresOn || ShipStruct < 0) return;
        // Enough items for a hold above the mining limit (the pause line) and a few more rows of the hold readout.
        Net.GiveItems(Player, _content.ItemIndex("core:iron_ingot"), 14);
        Net.GiveItems(Player, _content.ItemIndex("core:carbon"), 4);
        Net.GiveItems(Player, _content.ItemIndex("core:ice"), 6);
        _world.Step(30);
    }

    private static void SetWindowSize(Window w, int width, int height)
    {
        w.Mode = Window.ModeEnum.Windowed;
        w.Size = new Vector2I(width, height);
    }

    private void Toasts()
    {
        ToastLogistics(L("toast.welcome"), 60.0);
        ToastLogistics(L("toast.miningPaused"), 60.0);
        ToastProduction(L("learn.noReactor"), 60.0);
    }

    private void LayoutShotFrame()
    {
        if (_layoutShotPath is not { } path) return;
        _layoutFrame++;
        _wantCapture = false;
        var win = GetWindow();
        switch (_layoutFrame)
        {
            case 3: SetWindowSize(win, 1280, 720); break;
            case 20: Toasts(); break;
            case 60:
                GD.Print($"LAYOUT 720 viewport={GetViewport().GetVisibleRect().Size} window={win.Size}");
                Save(path + ".720.png");
                break;
            case 61: _detailsOpen = true; _mapOpen = true; SelectBuild(0); break; // the belt tool has the longest hint
            case 90: Save(path + ".720.full.png"); break;
            case 91: _detailsOpen = false; _mapOpen = false; _tool = ToolMode.Pointer; SetWindowSize(win, 1920, 1080); break;
            case 140:
                GD.Print($"LAYOUT 1080 viewport={GetViewport().GetVisibleRect().Size} window={win.Size}");
                Save(path + ".1080.png");
                break;
            case 141: _detailsOpen = true; _mapOpen = true; break;
            case 170: Save(path + ".1080.full.png"); break;
            case 171: GetTree().Quit(); break;
        }
    }
}
