using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using Godot;
using Starfoundry.Sim;
using Starfoundry.Ui;

namespace Starfoundry.Game;

/// <summary>
/// <c>--hud-sweep=dir [--hud-levels=min,std,det] [--window=WxH]</c>: puts the played game in every HUD context (flying, mining, walking, building,
/// combat, docked), saves a picture with the HUD and one without it, and measures how much of the screen the HUD covers: the share of pixels the
/// overlay changed, in total and in the centre 30 percent (a 30 by 30 percent box around the crosshair). One line per context and level
/// (<c>HUDCOVER ctx=flying level=std size=1280x720 total=3.1% centre=0.2% groups=6</c>) and a <c>coverage.csv</c> in the folder. Nothing is saved.
/// It is how <c>docs/prototypes/hud-information-study.md</c> was measured.
/// </summary>
public partial class Play
{
    private string? _hudSweepDir;
    private int _sweepFrame, _sweepCtx, _sweepLevel, _sweepPhase;
    private readonly List<string> _sweepRows = new();
    private Image? _sweepWith;
    private long _sweepDockTick;

    private static readonly string[] SweepContexts = { "flying", "mining", "walking", "building", "combat", "docked" };

    private void HudSweepSetup(string[] args)
    {
        _wantCapture = false;
        _hudSweepDir = args.First(a => a.StartsWith("--hud-sweep="))["--hud-sweep=".Length..];
        System.IO.Directory.CreateDirectory(_hudSweepDir);
        string levels = args.FirstOrDefault(a => a.StartsWith("--hud-levels="))?["--hud-levels=".Length..] ?? "std";
        _sweepLevels = levels.Split(',', StringSplitOptions.RemoveEmptyEntries);
        _sweepRows.Add("context,level,size,total_percent,centre_percent,groups");
    }

    private string[] _sweepLevels = { "std" };

    /// <summary>Back to the plain seated state between two contexts.</summary>
    private void SweepReset()
    {
        _tool = ToolMode.Pointer;
        _detailsOpen = false;
        _mapOpen = false;
        _dockAssist = false;
        if (_laserOn) SetLaser(false);
        if (Net.LaserFiring(Player)) Send(new StopMining());
        if (!_seated) { _body = StandSpot; Sit(); }
        _yaw = MathF.PI; _pitch = -0.35f;
        _paused = false;
    }

    private void SweepSetup(string ctx)
    {
        SweepReset();
        long cu = Units.CellUnits;
        int ship = ShipStruct;
        switch (ctx)
        {
            case "mining":
            {
                int iron = _content.ItemIndex("core:iron_ore");
                int dep = Enumerable.Range(0, Net.DepositCount).Where(d => Net.DepositItem(d) == iron && Net.IsDepositRevealed(d)).DefaultIfEmpty(-1).First();
                if (dep >= 0)
                {
                    var (x, y, z) = Net.DepositPosition(dep);
                    Net.SetStructureOrigin(ship, (long)(x - 8) * cu, (long)(y - 5) * cu, (long)(z + 12) * cu);
                    _yaw = 0f; _pitch = 0f;
                    _selectedDeposit = dep;
                    Send(new StartMining(dep));
                }
                break;
            }
            case "walking":
                StandUp();
                _body = StandSpot;
                _yaw = MathF.PI; _pitch = -0.1f;
                break;
            case "building":
                StandUp();
                _body = StandSpot;
                SelectBuild(Math.Max(0, _palette.FindIndex(b => !b.IsBelt && !b.IsModule && !b.IsStation && b.Machine == _content.MachineIndex("core:smelter"))));
                break;
            case "combat":
                Net.ScheduleWave(0);
                _world.Step(Units.TicksPerSecond * 20);
                _yaw = 0f; _pitch = 0f;
                break;
            case "docked":
            {
                var o = Net.GetStructure(ship);
                int st = Net.CreateStructure(Player, StructureKind.Station, o.OriginX - (8 + 22) * cu, o.OriginY, o.OriginZ);
                Net.AddStructureModuleUnchecked(st, 0, 0, 0, ModuleKind.Core);
                Net.AddStructureModuleUnchecked(st, 0, 0, 1, ModuleKind.Workshop);
                Net.AddStructureModuleUnchecked(st, -1, 0, 0, ModuleKind.Dock);
                Net.AddStructureModuleUnchecked(st, 0, 0, -1, ModuleKind.Reactor);
                Net.GiveStock(st, _content.FuelItem, 8);
                _yaw = MathF.PI * 0.5f; _pitch = -0.05f;
                _dockAssist = true;
                _sweepDockTick = Net.Tick;
                break;
            }
        }
    }

    private int SweepGroups(string ctx) => HudSweepGroups(ctx);

    /// <summary>Per frame (called from <see cref="HudShotTick"/>): the sweep's state machine. Contexts take about sixty frames each.</summary>
    private void HudSweepFrame()
    {
        if (_hudSweepDir is null) return;
        _sweepFrame++;
        _wantCapture = false;
        if (_sweepFrame == 3) { SetWindowSize(GetWindow(), GetWindow().Size.X, GetWindow().Size.Y); }
        if (_sweepFrame < 20) return;
        if (_sweepCtx >= SweepContexts.Length)
        {
            System.IO.File.WriteAllLines(System.IO.Path.Combine(_hudSweepDir, "coverage.csv"), _sweepRows);
            GD.Print("HUDCOVER done");
            GetTree().Quit();
            _hudSweepDir = null;
            return;
        }
        string ctx = SweepContexts[_sweepCtx];
        string level = _sweepLevels[_sweepLevel];
        switch (_sweepPhase)
        {
            case 0:
                if (_sweepLevel == 0) SweepSetup(ctx);
                ApplySweepLevel(level);
                _sweepFrame = 20; _sweepPhase = 1;
                break;
            case 1:
                if (ctx == "docked" && !IsDockedNow && Net.Tick < _sweepDockTick + 30 * 90) { _sweepFrame = 20; break; } // wait for the assisted approach
                if (ctx == "docked" && !IsDockedNow) { GD.Print("HUDCOVER docked: the approach did not dock; skipped"); _sweepCtx++; _sweepLevel = 0; _sweepPhase = 0; _sweepFrame = 20; break; }
                if (_sweepFrame < 45) break;
                _sweepWith = GetViewport().GetTexture().GetImage();
                _layer.Visible = false;
                _sweepFrame = 20; _sweepPhase = 2;
                break;
            case 2:
                if (_sweepFrame < 22) break;
                {
                    var without = GetViewport().GetTexture().GetImage();
                    _layer.Visible = true;
                    var size = _sweepWith!.GetSize();
                    var (total, centre) = Coverage(_sweepWith, without);
                    string tag = $"{ctx}.{level}";
                    _sweepWith.SavePng(System.IO.Path.Combine(_hudSweepDir, $"{tag}.png"));
                    var inv = System.Globalization.CultureInfo.InvariantCulture;
                    string line = string.Create(inv, $"ctx={ctx} level={level} size={size.X}x{size.Y} total={total:F2}% centre={centre:F2}% groups={SweepGroups(ctx)}");
                    SweepElements(ctx, level, size);
                    GD.Print("HUDCOVER " + line);
                    _sweepRows.Add(string.Create(inv, $"{ctx},{level},{size.X}x{size.Y},{total:F2},{centre:F2},{SweepGroups(ctx)}"));
                    _sweepWith = null;
                    _sweepPhase = 0; _sweepFrame = 20;
                    if (++_sweepLevel >= _sweepLevels.Length) { _sweepLevel = 0; _sweepCtx++; }
                }
                break;
        }
    }

    /// <summary>The share of pixels (percent) the overlay changed, over the whole picture and over its centre 30 by 30 percent.</summary>
    private static (double Total, double Centre) Coverage(Image with, Image without)
    {
        int w = Math.Min(with.GetWidth(), without.GetWidth()), h = Math.Min(with.GetHeight(), without.GetHeight());
        int cx0 = (int)(w * 0.35f), cx1 = (int)(w * 0.65f), cy0 = (int)(h * 0.35f), cy1 = (int)(h * 0.65f);
        long diff = 0, cdiff = 0;
        var a = with.GetData();
        var b = without.GetData();
        int bpp = a.Length / Math.Max(1, with.GetWidth() * with.GetHeight());
        for (int y = 0; y < h; y++)
            for (int x = 0; x < w; x++)
            {
                int ia = (y * with.GetWidth() + x) * bpp, ib = (y * without.GetWidth() + x) * bpp;
                int d = Math.Abs(a[ia] - b[ib]) + Math.Abs(a[ia + 1] - b[ib + 1]) + Math.Abs(a[ia + 2] - b[ib + 2]);
                if (d <= 24) continue;
                diff++;
                if (x >= cx0 && x < cx1 && y >= cy0 && y < cy1) cdiff++;
            }
        return (100.0 * diff / (w * (double)h), 100.0 * cdiff / ((cx1 - cx0) * (double)(cy1 - cy0)));
    }

    /// <summary>Prints the area (percent of the screen) of every HUD card that is showing, so the study can say which one costs what.</summary>
    private void SweepElements(string ctx, string level, Vector2I size)
    {
        var inv = System.Globalization.CultureInfo.InvariantCulture;
        double screen = size.X * (double)size.Y;
        foreach (var (name, c) in SweepControls())
        {
            if (!c.Visible || c.Modulate.A < 0.2f) continue;
            var r = c.GetGlobalRect();
            if (r.Size.X < 2 || r.Size.Y < 2) continue;
            var centre = new Rect2(size.X * 0.35f, size.Y * 0.35f, size.X * 0.3f, size.Y * 0.3f);
            bool hitsCentre = r.Intersects(centre);
            GD.Print(string.Create(inv, $"HUDELEM ctx={ctx} level={level} name={name} area={100.0 * r.Size.X * r.Size.Y / screen:F2}% rect={r.Position.X:0},{r.Position.Y:0} {r.Size.X:0}x{r.Size.Y:0} centre={hitsCentre}"));
            _sweepRows.Add(string.Create(inv, $"#elem,{ctx},{level},{name},{100.0 * r.Size.X * r.Size.Y / screen:F2},{hitsCentre}"));
        }
    }

    private IEnumerable<(string, Control)> SweepControls()
    {
        yield return ("ship-card", _statusPanel);
        yield return ("flight-readout", _flightPanel);
        yield return ("bottom-strip", _toolbar);
        yield return ("key-panel", _keyPanel);
        yield return ("toasts", _toastBox);
        yield return ("hint", _modeHint);
        yield return ("steps-panel", _objectivesPanel);
        yield return ("target-card", _targetPanel);
        yield return ("aim-tooltip", _tooltipPanel);
        yield return ("wave-banner", _waveBanner);
        foreach (var (name, c) in SweepExtraControls()) yield return (name, c);
    }

    private IEnumerable<(string, Control)> SweepExtraControls() { yield break; }

    // Hooked up when the detail levels exist (Play.HudDetail.cs); the baseline sweep has only the one HUD.
    partial void ApplySweepLevelHook(string level);
    private void ApplySweepLevel(string level) => ApplySweepLevelHook(level);
    private int HudSweepGroups(string ctx) => 0;
}
