using System;
using System.Linq;
using Godot;
using Starfoundry.Sim;
using Starfoundry.Ui;

namespace Starfoundry.Game;

/// <summary>
/// Power and fuel in the HUD and the world: the readout in the status panel, the fuel warnings, the machines without power and
/// the ice marker of the guided step. The numbers come from <c>BeltNetwork.PowerStatus</c>; the maths is in <see cref="PowerFormat"/>.
/// </summary>
public partial class Play
{
    private VBoxContainer _powerBox = null!;
    private GaugeRow _powerRow = null!;
    private IconCount _fuelRow = null!;
    private Label _targetPower = null!;
    private readonly FuelWatch _fuelWatch = new();

    private static readonly Color PowerAmber = new(1f, 0.74f, 0.25f);

    private int BurnInterval => _content.Structures?.BurnIntervalTicks(ModuleKind.Reactor) ?? PowerFormat.DefaultBurnIntervalTicks;

    private bool HasReactor(int structure) => Net.GetStructure(structure).Modules.Values.Any(ModuleKindIds.IsReactor);

    private static Color BandColor(PowerBand band) => band switch
    {
        PowerBand.Full => GoodColor,
        PowerBand.Strained => PowerAmber,
        _ => BadColor,
    };

    private void BuildPowerBox(Control parent)
    {
        _powerBox = new VBoxContainer { Visible = false, MouseFilter = Control.MouseFilterEnum.Ignore };
        _powerBox.AddThemeConstantOverride("separation", 2);
        _powerRow = new GaugeRow("status:power");
        _fuelRow = new IconCount("item:core:hydrogen");
        _powerBox.AddChild(_powerRow);
        _powerBox.AddChild(_fuelRow);
        parent.AddChild(_powerBox);
    }

    /// <summary>The power readout of the ship in the status panel, and the fuel warnings. Hidden outside the structures campaign.</summary>
    private void UpdatePowerHud()
    {
        int ship = ShipStruct;
        _powerBox.Visible = !_demo && ship >= 0 && Net.HasPlayer(Player);
        if (!_powerBox.Visible) return;

        var st = Net.PowerStatus(ship);
        var band = PowerFormat.Band(st.SatisfactionPermille);
        var colour = BandColor(band);
        bool reactor = HasReactor(ship);
        bool powered = reactor || st.Production > 0;
        _powerRow.Set("status:power", st.SatisfactionPermille / 1000f, powered ? Metric.PowerPair(Physical.Watts(st.Production), Physical.Watts(st.Demand), Loc.Culture) : "-",
            powered ? colour : BadColor,
            powered ? L("hud.power", Physical.Pow(st.Production, Loc.Culture), Physical.Pow(st.Demand, Loc.Culture), PowerFormat.Percent(st.SatisfactionPermille)) : L("hud.power.noReactor"));
        var vital = VitalRows;
        _powerRow.Visible = vital.HasFlag(HudRow.PowerGauge) || band != PowerBand.Full || !powered; // the minimal level shows the gauge only when power is short
        _fuelRow.Visible = vital.HasFlag(HudRow.FuelTime);
        long left = PowerFormat.TimeLeftTicks(st, BurnInterval);
        var fuelColour = left < PowerFormat.LowFuelSeconds * Units.TicksPerSecond ? colour == GoodColor ? PowerAmber : colour : UiTheme.Text;
        _fuelRow.SetText("item:core:hydrogen", left <= 0 ? $"{st.HydrogenInHold}  -" : $"{st.HydrogenInHold}  ·  {PowerFormat.Clock(left)}", left <= 0 ? BadColor : fuelColour, PowerFormat.FuelLine(st, BurnInterval));

        switch (_fuelWatch.Update(st, reactor, BurnInterval))
        {
            case PowerWarning.LowFuel:
            case PowerWarning.LastSeconds:
                ToastProduction(L("toast.powerLow", PowerFormat.Clock(PowerFormat.TimeLeftTicks(st, BurnInterval))), 10.0);
                break;
            case PowerWarning.Out:
                ToastProduction(L("toast.powerOut"), 12.0);
                break;
        }
    }

    /// <summary>Hands the power state of every structure to the module lights (engine glow, reactor core, amber lamps).</summary>
    private void UpdateStructurePower()
    {
        foreach (var s in Net.Structures())
        {
            var st = Net.PowerStatus(s.Id);
            _stationView.SetPower(s.Id, st.SatisfactionPermille, st.Burning);
        }
    }

    private readonly System.Collections.Generic.Dictionary<int, int> _satCache = new();

    /// <summary>Satisfaction per mille of a structure, read once per frame for all the machine models.</summary>
    private int SatisfactionOf(int structure)
    {
        if (!_structuresOn || structure == 0) return PowerRules.FullSatisfaction;
        if (!_satCache.TryGetValue(structure, out int v)) _satCache[structure] = v = Net.PowerStatus(structure).SatisfactionPermille;
        return v;
    }

    /// <summary>True for machines that draw power (the others, like walls and drills, never show as unpowered).</summary>
    private bool NeedsPower(int machineType) => _structuresOn && _content.Machines[machineType].PowerUse > 0;

    /// <summary>One line about the power of a structure: "Station: power 12/100 (55%) - hydrogen 4 - fuel 2:10".</summary>
    private string StructurePowerLine(int structure, string name)
    {
        var st = Net.PowerStatus(structure);
        if (!HasReactor(structure)) return $"{name}: {L("hud.power.noReactor")}";
        return L("hud.power.station", name, Physical.Pow(st.Production, Loc.Culture), Physical.Pow(st.Demand, Loc.Culture), PowerFormat.Percent(st.SatisfactionPermille), PowerFormat.FuelLine(st, BurnInterval));
    }

    /// <summary>While the guided step is making hydrogen: a marker on the nearest known ice deposit.</summary>
    private void IceMarker(Action<string, Color, Vector3, float> add)
    {
        if (_objectives.Current != Starfoundry.Ui.Objective.MakeFuel) return;
        int ice = _content.ItemIndex("core:ice");
        if (ice < 0) return;
        var ship = ShipPosition(0f);
        int best = -1;
        float bestD = float.MaxValue;
        for (int d = 0; d < Net.DepositCount; d++)
        {
            if (Net.DepositItem(d) != ice || !Net.IsDepositRevealed(d) || Net.DepositRemaining(d) <= 0) continue;
            var p = Net.DepositPosition(d);
            float dist = (new Vector3(p.X, p.Y, p.Z) - ship).LengthSquared();
            if (dist < bestD) { bestD = dist; best = d; }
        }
        if (best < 0) return;
        var q = Net.DepositPosition(best);
        add(L("mark.ice"), new Color(0.55f, 0.9f, 1f), new Vector3(q.X, q.Y, q.Z), 6f);
    }
}
