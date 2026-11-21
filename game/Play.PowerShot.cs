using System;
using System.Linq;
using Godot;
using Starfoundry.Sim;
using Starfoundry.Ui;

namespace Starfoundry.Game;

/// <summary>
/// <c>--power-shot=path</c>: a scripted run of the structures campaign that saves the power HUD and the ship's lights in four states,
/// each as a cockpit image and an outside image (<c>.outside.png</c>): full power (<c>path</c>), amber (extra labs push the demand above
/// the reactor, <c>path.amber.png</c>), the two-minute fuel warning (<c>path.fuelwarn.png</c>, cockpit only) and power out
/// (<c>path.lowpower.png</c>, the fuel is run out by stepping the clock). Options: --power-labs=N (labs added for the amber state,
/// default 5: demand 128 against 100 produced), --shot-ticks=N (ticks stepped before the first image). Quits after the last image.
/// </summary>
public partial class Play
{
    private string? _powerShotPath;
    private int _psPhase;
    private long _psTick0;
    private int _psSaveIn;
    private string? _psSaveTo;

    private void PsSave(string path)
    {
        _psSaveTo = path;
        _psSaveIn = 12; // frames, so the interface and the lights have settled
    }

    /// <summary>Per frame: saves the pending image when its countdown ends.</summary>
    private void PowerShotFrame()
    {
        if (_psSaveIn <= 0 || --_psSaveIn != 0 || _psSaveTo is null) return;
        GetViewport().GetTexture().GetImage().SavePng(_psSaveTo);
        GD.Print("POWER SHOT saved " + _psSaveTo);
        _psSaveTo = null;
    }

    private bool PsSaving => _psSaveTo is not null;

    private void PsStepUntil(Func<bool> done, int max)
    {
        for (int i = 0; i < max && !done(); i++) _world.Step();
    }

    /// <summary>Called before every simulation tick of a <c>--power-shot</c> run.</summary>
    private void PowerShotThink()
    {
        if (ShipStruct < 0 || PsSaving) return;
        string p = _powerShotPath!;
        switch (_psPhase)
        {
            case 0: // optional warm-up ticks, then the full-power cockpit image
            {
                int warm = ShotArg(OS.GetCmdlineUserArgs(), "--shot-ticks") is { } t && int.TryParse(t, out int n) ? n : 0;
                PsStepUntil(() => false, warm);
                _psTick0 = _world.Tick;
                _psPhase = 1;
                break;
            }
            case 1:
                if (_world.Tick - _psTick0 < 90) break;
                PsSave(p);
                _psPhase = 2;
                break;
            case 2:
                _shotOutside = true;
                PsSave(p + ".outside.png");
                _psPhase = 3;
                break;
            case 3: // more labs than the reactor can feed
            {
                _shotOutside = false;
                var args = OS.GetCmdlineUserArgs();
                int labs = ShotArg(args, "--power-labs") is { } l && int.TryParse(l, out int ln) ? ln : 5;
                int lab = _content.MachineIndex("core:research_lab");
                for (int i = 0; i < labs; i++) Net.AddMachine(i, 1, 13, lab, -1, Player, ShipStruct);
                _psTick0 = _world.Tick;
                _psPhase = 4;
                break;
            }
            case 4:
                if (_world.Tick - _psTick0 < 90) break;
                PsSave(p + ".amber.png");
                _psPhase = 5;
                break;
            case 5:
                _shotOutside = true;
                PsSave(p + ".amber.outside.png");
                _psPhase = 6;
                break;
            case 6: // run the clock to the two-minute warning
                _shotOutside = false;
                PsStepUntil(() => PowerFormat.TimeLeftTicks(Net.ShipPower(Player), BurnInterval) <= 100 * Units.TicksPerSecond, 40_000);
                _psTick0 = _world.Tick;
                _psPhase = 7;
                break;
            case 7:
                if (_world.Tick - _psTick0 < 40) break;
                PsSave(p + ".fuelwarn.png");
                _psPhase = 8;
                break;
            case 8: // run the fuel out
                PsStepUntil(() => Net.ShipPower(Player).SatisfactionPermille == 0, 40_000);
                _psTick0 = _world.Tick;
                _psPhase = 9;
                break;
            case 9:
                if (_world.Tick - _psTick0 < 40) break;
                PsSave(p + ".lowpower.png");
                _psPhase = 10;
                break;
            case 10:
                _shotOutside = true;
                PsSave(p + ".lowpower.outside.png");
                _psPhase = 11;
                break;
            case 11:
                GetTree().Quit();
                _psPhase = 12;
                break;
        }
    }
}
