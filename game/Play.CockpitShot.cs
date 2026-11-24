using System;
using System.Linq;
using Godot;
using Starfoundry.Sim;
using Starfoundry.Ui;

namespace Starfoundry.Game;

/// <summary>
/// <c>--cockpit-shot=path</c> (with <c>--cockpit-size=1920x1080</c>, default 1280x720, and <c>--ui-lang=en|es</c>): the structures campaign, seated, with the settings file left alone.
/// It saves <c>path.seat.png</c> (the view from the seat down at the console, full HUD), <c>path.seat.cockpit.png</c> and <c>path.seat.minimal.png</c> (the other HUD modes),
/// then each page on the centre display in focus (<c>path.flight.png</c>, <c>.nav.png</c>, <c>.systems.png</c>, <c>.weapons.png</c>), the left and right displays in focus
/// (<c>.left.png</c>, <c>.right.png</c>), the three of them seen together (<c>.console.png</c>) and the cockpit-only HUD standing (<c>.standing.png</c>). Between them it presses a
/// button and clicks a row with the crosshair, and prints what changed (<c>COCKPITSHOT ...</c>).
/// </summary>
public partial class Play
{
    private string? _cockpitShotPath;
    private int _cockpitShotFrame;

    private bool CockpitShotActive => _cockpitShotPath is not null;

    private void CockpitShotSetup(string[] args)
    {
        _wantCapture = false;
        if (args.Contains("--ui-lang=es")) Loc.Lang = Language.Es;
        if (args.Contains("--ui-lang=en")) Loc.Lang = Language.En;
        if (!_structuresOn || ShipStruct < 0) return;
        Net.GiveItems(Player, _content.ItemIndex("core:iron_ingot"), 14);
        Net.GiveItems(Player, _content.ItemIndex("core:hydrogen"), 9);
        _world.Step(30);
    }

    private void CockpitShotFrame()
    {
        if (_cockpitShotPath is not { } path) return;
        _cockpitShotFrame++;
        _wantCapture = false;
        var win = GetWindow();
        var args = OS.GetCmdlineUserArgs();
        var sizeArg = args.FirstOrDefault(a => a.StartsWith("--cockpit-size="))?["--cockpit-size=".Length..].Split('x');
        void Shot(string name)
        {
            GetViewport().GetTexture().GetImage().SavePng(path + "." + name + ".png");
            GD.Print($"COCKPITSHOT saved {path}.{name}.png {GetViewport().GetVisibleRect().Size} fov={_camera.Fov:F1} hud={CurrentHudMode} focus={_focus?.ToString() ?? "-"} aim={(_cockpitAim is { } a ? a.Screen + "@" + (int)a.Pixel.X + "," + (int)a.Pixel.Y : "-")} pages={_cockpit?.Pages.PageOn(CockpitScreen.Left)}/{_cockpit?.Pages.PageOn(CockpitScreen.Center)}/{_cockpit?.Pages.PageOn(CockpitScreen.Right)}");
        }
        void ShowAll()
        {
            _cockpit!.Pages.Show(CockpitScreen.Left, CockpitPage.Systems);
            _cockpit.Pages.Show(CockpitScreen.Center, CockpitPage.Flight);
            _cockpit.Pages.Show(CockpitScreen.Right, CockpitPage.Nav);
        }
        switch (_cockpitShotFrame)
        {
            case 3:
                if (sizeArg is { Length: 2 } && int.TryParse(sizeArg[0], out int w) && int.TryParse(sizeArg[1], out int h)) SetWindowSize(win, w, h);
                else SetWindowSize(win, 1280, 720);
                break;
            case 12:
                _hudModeShot = HudMode.Full;
                _yaw = 0f; _pitch = -0.5f; // from the seat, down at the console
                _focus = null;
                if (RevealedDeposits() is { Count: > 0 } deps) _selectedDeposit = deps[0];
                break;
            case 55: GD.Print($"COCKPITSHOT attached={ConsoleReady} module={Seat.Module} deposits={RevealedDeposits().Count}"); Shot("seat"); break;
            case 56: _hudModeShot = HudMode.Cockpit; break;
            case 80: Shot("seat.cockpit"); break;
            case 81: _hudModeShot = HudMode.Minimal; break;
            case 105: Shot("seat.minimal"); break;
            case 106: _hudModeShot = HudMode.Full; SetFocus(CockpitScreen.Center); _cockpit?.Pages.Show(CockpitScreen.Center, CockpitPage.Flight); break;
            case 150: Shot("flight"); _cockpit!.Pages.Show(CockpitScreen.Center, CockpitPage.Nav); break;
            case 175: Shot("nav"); _cockpit!.Pages.Show(CockpitScreen.Center, CockpitPage.Systems); break;
            case 200: Shot("systems"); _cockpit!.Pages.Show(CockpitScreen.Center, CockpitPage.Weapons); break;
            case 225: Shot("weapons"); ShowAll(); SetFocus(CockpitScreen.Left); break;
            case 255: Shot("left"); SetFocus(CockpitScreen.Right); break;
            case 285: Shot("right"); SetFocus(null); _yaw = 0f; _pitch = -0.62f; break;
            case 320: Shot("console"); break;
            case 321: ShowAll(); SetFocus(CockpitScreen.Center); _cockpit!.Pages.Show(CockpitScreen.Center, CockpitPage.Nav); break;
            case 360:
            {
                // A click with the crosshair: the centre of the nav display is on a row of the list; pressing selects that deposit.
                int before = _selectedDeposit;
                var press = new InputEventMouseButton { ButtonIndex = MouseButton.Left, Pressed = true };
                bool used = CockpitMouse(press);
                CockpitMouse(new InputEventMouseButton { ButtonIndex = MouseButton.Left, Pressed = false });
                GD.Print($"COCKPITSHOT click used={used} aim={(_cockpitAim is { } a ? a.Screen + "@" + (int)a.Pixel.X + "," + (int)a.Pixel.Y : "-")} deposit {before} -> {_selectedDeposit}");
                // A press on a tab button: the weapons tab of the centre display.
                var tab = _cockpit!.View(CockpitScreen.Center).Buttons.FirstOrDefault(b => b.Action == CockpitAction.ShowPage && b.Arg == (int)CockpitPage.Weapons);
                CockpitPress(CockpitScreen.Center, tab);
                GD.Print($"COCKPITSHOT tab center={_cockpit.Pages.PageOn(CockpitScreen.Center)} right={_cockpit.Pages.PageOn(CockpitScreen.Right)} hidden={_cockpit.Pages.Hidden}");
                CockpitPress(CockpitScreen.Center, new CockpitButton(0, 0, 1, 1, CockpitAction.CycleHudMode));
                GD.Print($"COCKPITSHOT hud mode after the button: {CurrentHudMode}");
                break;
            }
            case 361: _hudModeShot = HudMode.Cockpit; SetFocus(null); _cockpit?.Pages.Show(CockpitScreen.Center, CockpitPage.Flight); break;
            case 362: ToggleSeat(); _yaw = 0f; _pitch = -0.45f; break; // stand up: the overlay for walking stays in cockpit mode
            case 420: Shot("standing"); break;
            case 421: GetTree().Quit(); break;
        }
    }
}
