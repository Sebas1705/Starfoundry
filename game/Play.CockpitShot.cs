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
        if (args.Contains("--cockpit-bench")) { CockpitBenchFrame(win, args); return; }
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
                _autoFrameBlock = true; // the seat shots show the screens unframed
                _yaw = 0f; _pitch = -0.5f; // from the seat, down at the console
                _focus = null;
                if (RevealedDeposits() is { Count: > 0 } deps) _selectedDeposit = deps[0];
                break;
            case 55: GD.Print($"COCKPITSHOT attached={ConsoleReady} module={Seat.Module} deposits={RevealedDeposits().Count}"); Shot("seat"); break;
            case 56: _hudModeShot = HudMode.Cockpit; break;
            case 57: _hudModeShot = HudMode.Full; _cockpit!.Pages.Show(CockpitScreen.Center, CockpitPage.Nav); break;
            case 66: Shot("seat.nav"); NavLegibility("seat"); _cockpit!.Capture(CockpitScreen.Center).SavePng(path + ".tex.navcompact.png"); _cockpit.Capture(CockpitScreen.Right).SavePng(path + ".tex.right.png"); _cockpit!.Pages.Show(CockpitScreen.Center, CockpitPage.Flight); _hudModeShot = HudMode.Cockpit; break;
            case 80: Shot("seat.cockpit"); break;
            case 81: _hudModeShot = HudMode.Minimal; break;
            case 105: Shot("seat.minimal"); break;
            case 106: _hudModeShot = HudMode.Full; SetFocus(CockpitScreen.Center); _cockpit?.Pages.Show(CockpitScreen.Center, CockpitPage.Flight); break;
            case 150: Shot("flight"); foreach (var sc in new[] { CockpitScreen.Left, CockpitScreen.Center, CockpitScreen.Right }) _cockpit!.Capture(sc).SavePng(path + ".tex." + sc + ".png"); _cockpit!.Pages.Show(CockpitScreen.Center, CockpitPage.Nav); break;
            case 175: Shot("nav"); NavLegibility("focused"); _cockpit!.Pages.Show(CockpitScreen.Center, CockpitPage.Systems); break;
            case 200: Shot("systems"); _cockpit!.Pages.Show(CockpitScreen.Center, CockpitPage.Weapons); break;
            case 225: Shot("weapons"); ShowAll(); SetFocus(CockpitScreen.Left); break;
            case 255: Shot("left"); SetFocus(CockpitScreen.Right); break;
            case 285: Shot("right"); SetFocus(null); _yaw = 0f; _pitch = -0.62f; break;
            case 295: Shot("console"); GD.Print($"COCKPITSHOT console frame: autoframe={_autoFrame.Framed?.ToString() ?? "-"} aim={_cockpitAim?.Screen.ToString() ?? "-"}"); break;
            case 296: _autoFrameBlock = false; _pitch = -0.5f; break; // from here the crosshair rests on the centre screen and the auto-frame may act
            case 400: Shot("autoframe"); GD.Print($"COCKPITSHOT after looking at a screen for 0.4 s: autoframe={_autoFrame.Framed?.ToString() ?? "-"} fov={_camera.Fov:F1}"); break;
            case 421: ShowAll(); SetFocus(CockpitScreen.Center); _cockpit!.Pages.Show(CockpitScreen.Center, CockpitPage.Nav); break;
            case 440: CockpitLook(new Vector2(260f, 150f)); break; // the mouse moves over the focused display
            case 445: _mapDrag = true; CockpitLook(new Vector2(90f, -40f)); _mapDrag = false; _navMap.Zoom(1); GD.Print($"COCKPITSHOT map yaw={_navMap.Yaw:F2} tilt={_navMap.Tilt:F2} range={_navMap.Range:F0} auto={_navMap.Auto}"); break;
            case 450: Shot("nav.rotated"); _navMap.Reset(); break;
            case 455: Shot("pan"); CockpitLook(new Vector2(-260f, -150f)); break;
            case 460:
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
            case 461: // every warning state at once, from made-up data: low hull, strained power, low fuel, full hold, a wave, boost, a port, a gate and an enemy
                _syntheticSnapshot = SyntheticAlertSnapshot();
                ShowAll(); SetFocus(CockpitScreen.Center); _cockpit!.Pages.Show(CockpitScreen.Center, CockpitPage.Flight);
                break;
            case 500: Shot("alert.flight"); _cockpit!.Pages.Show(CockpitScreen.Center, CockpitPage.Systems); break;
            case 525: Shot("alert.systems"); _cockpit!.Pages.Show(CockpitScreen.Center, CockpitPage.Weapons); break;
            case 550: Shot("alert.weapons"); _cockpit!.Pages.Show(CockpitScreen.Center, CockpitPage.Nav); break;
            case 575: Shot("alert.nav"); break;
            case 600: _syntheticSnapshot = null; _hudModeShot = HudMode.Cockpit; SetFocus(null); _cockpit?.Pages.Show(CockpitScreen.Center, CockpitPage.Flight); break;
            case 601: ToggleSeat(); _yaw = 0f; _pitch = -0.45f; break; // stand up: the overlay for walking stays in cockpit mode
            case 660: Shot("standing"); break;
            case 661: GetTree().Quit(); break;
        }
    }

    private readonly System.Collections.Generic.List<double> _benchWall = new(), _benchCpu = new(), _benchGpu = new();
    private ulong _benchLast;

    /// <summary>
    /// <c>--cockpit-shot=path --cockpit-bench [--cockpit-size=WxH] [--blender-cockpit]</c>: instead of the screenshots, seats the pilot looking at the console, switches vertical sync off,
    /// skips 70 frames while the scene builds and times 300: average, 95th percentile and worst frame time, the render thread's CPU and GPU time and the draw calls, objects and
    /// primitives in the frame (<c>COCKPITBENCH ...</c>), then quits. Used to compare the procedural cockpit with the Blender one (docs/prototypes/blender-pipeline.md).
    /// </summary>
    private void CockpitBenchFrame(Window win, string[] args)
    {
        var sizeArg = args.FirstOrDefault(a => a.StartsWith("--cockpit-size="))?["--cockpit-size=".Length..].Split('x');
        switch (_cockpitShotFrame)
        {
            case 3:
                if (sizeArg is { Length: 2 } && int.TryParse(sizeArg[0], out int w) && int.TryParse(sizeArg[1], out int h)) SetWindowSize(win, w, h);
                else SetWindowSize(win, 1280, 720);
                DisplayServer.WindowSetVsyncMode(DisplayServer.VSyncMode.Disabled);
                RenderingServer.ViewportSetMeasureRenderTime(GetViewport().GetViewportRid(), true);
                break;
            case 12: _hudModeShot = HudMode.Full; _yaw = 0f; _pitch = -0.5f; _focus = null; break;
        }
        ulong now = Time.GetTicksUsec();
        if (_cockpitShotFrame > 70)
        {
            _benchWall.Add((now - _benchLast) / 1000.0);
            var rid = GetViewport().GetViewportRid();
            _benchCpu.Add(RenderingServer.ViewportGetMeasuredRenderTimeCpu(rid));
            _benchGpu.Add(RenderingServer.ViewportGetMeasuredRenderTimeGpu(rid));
        }
        _benchLast = now;
        if (_benchWall.Count < 300) return;
        var sorted = _benchWall.OrderBy(x => x).ToList();
        GD.Print($"COCKPITBENCH blender={BlenderCockpit.Enabled} size={GetViewport().GetVisibleRect().Size} frames={sorted.Count} avg_ms={sorted.Average():F3} p95_ms={sorted[(int)(sorted.Count * 0.95)]:F3} worst_ms={sorted[^1]:F3} render_cpu_ms={_benchCpu.Average():F3} render_gpu_ms={_benchGpu.Average():F3} draw_calls={Performance.GetMonitor(Performance.Monitor.RenderTotalDrawCallsInFrame)} objects={Performance.GetMonitor(Performance.Monitor.RenderTotalObjectsInFrame)} primitives={Performance.GetMonitor(Performance.Monitor.RenderTotalPrimitivesInFrame)} video_mem_mb={Performance.GetMonitor(Performance.Monitor.RenderVideoMemUsed) / 1048576.0:F0}");
        GetTree().Quit();
    }

    private CockpitSnapshot? _syntheticSnapshot;

    /// <summary>Made-up data in every warning state, to look at how the displays draw trouble (the screenshot run only).</summary>
    private CockpitSnapshot SyntheticAlertSnapshot()
    {
        var axes = NavFrame.Project(new CameraBasis(new Vec3(0.5f, 0.3f, -0.8f), new Vec3(0.8f, 0f, 0.6f), new Vec3(-0.2f, 0.95f, 0.1f), 1.4f));
        return new CockpitSnapshot
        {
            Online = true,
            HasReactor = true,
            HudMode = HudMode.Cockpit,
            Flight = new FlightDisplayData(18.4f, 24f, 0.8f, true, true, 20f, 22, 100, 31, 40, 420, 2, 60 * 25, 100, 100),
            Systems = new SystemsDisplayData(9, 3, 22, 100, 100, 100, 4, true),
            Weapons = new WeaponsDisplayData(true, true, true, WeaponGroup.Guns, true, false, true, true, 3, 12, 240, false, SyntheticArsenal()),
            Axes = axes,
            Velocity = CockpitMath.Velocity(0.4f, 0.2f, 5f),
            SystemName = "Kepler",
            Autopilot = true, DockNear = true, DockAssist = false, ScanReady = false, HasGalaxy = true, DockDistance = 12,
            Targets = SyntheticNav,
            MapTargets = SyntheticNav,
            PickKind = NavKind.Port,
            PickId = 0,
        };
    }
}

public partial class Play
{
    /// <summary>
    /// Prints how big the centre display is in the view and how tall its key glyphs are on the screen: the width of the quad in pixels, that over the window width,
    /// and the three big values of the nav page (target name, distance, speed) in pixels (<c>NAVSHOT</c>). The acceptance is at least 14 px unfocused at 720p.
    /// </summary>
    private void NavLegibility(string tag)
    {
        if (_cockpit?.Quad(CockpitScreen.Center) is not { } q) return;
        var def = _cockpit.Layout.Of(CockpitScreen.Center);
        Vector2 P(float x, float y) => _camera.UnprojectPosition(q.GlobalTransform * new Vector3(x, y, 0f));
        var l = P(-def.Width / 2f, 0f);
        var r = P(def.Width / 2f, 0f);
        var t = P(0f, def.Depth / 2f);
        var b = P(0f, -def.Depth / 2f);
        float widthPx = l.DistanceTo(r), heightPx = t.DistanceTo(b);
        var vp = GetViewport().GetVisibleRect().Size;
        float scale = widthPx / def.PxW;
        var (nameF, distF, speedF) = NavPage.KeyFonts(def.PxW);
        GD.Print($"NAVSHOT {tag} {vp.X:F0}x{vp.Y:F0}: screen {widthPx:F0} x {heightPx:F0} px ({widthPx / vp.X * 100f:F0} percent of the width), texture {def.PxW}x{def.PxH}, scale {scale:F3}, " +
                 $"key glyphs name {nameF * scale:F1} px, distance {distF * scale:F1} px, speed {speedF * scale:F1} px (font sizes {nameF}/{distF}/{speedF}); fov {_camera.Fov:F1}");
    }
}

public partial class Play
{
    /// <summary>Made-up nav targets of every kind, with positions in the ship's frame (right, up, ahead): a threat ahead and above, two deposits, a port beside and the gate's direction.</summary>
    private static readonly NavTarget[] SyntheticNav =
    {
        new(NavKind.Enemy, 1, "Striker", 38f, 12f, "enemy:core:striker", false, new System.Numerics.Vector3(8f, 10f, 36f)),
        new(NavKind.Deposit, 4, "Iron ore", 120f, 95f, "item:core:iron_ore", true, new System.Numerics.Vector3(110f, -20f, 40f)),
        new(NavKind.Gate, 2, "Gate: Vega", 640f, 140f, "status:gate", false, new System.Numerics.Vector3(-0.6f, 0.2f, 0.75f)),
        new(NavKind.Port, 0, "Port", 26f, 30f, "status:port", false, new System.Numerics.Vector3(-14f, -6f, 20f)),
        new(NavKind.Deposit, 7, "Ice", 1450f, 170f, "item:core:ice", false, new System.Numerics.Vector3(-900f, 300f, -1100f)),
    };

    /// <summary>Made-up arsenal of the cockpit shots: two Mk1 lasers, a Mk2 laser and a Mk2 gun, the guns in hand and firing.</summary>
    private static ArsenalModel SyntheticArsenal()
    {
        var a = new ArsenalModel();
        a.Fit(new[] { new FittedWeapon(ArsenalKind.Laser, 1, 2), new FittedWeapon(ArsenalKind.Laser, 2, 1), new FittedWeapon(ArsenalKind.Gun, 2, 1) });
        a.Select(2);
        a.SetLive(ArsenalKind.Laser, new ArsenalLive(HasDeposit: true, MineTenths: 12, DepositLeft: 240));
        a.SetLive(ArsenalKind.Gun, new ArsenalLive(Firing: true));
        return a;
    }
}
