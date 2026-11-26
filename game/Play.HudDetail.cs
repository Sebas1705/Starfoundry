using System;
using System.Collections.Generic;
using System.Linq;
using Godot;
using Starfoundry.Sim;
using Starfoundry.Ui;

namespace Starfoundry.Game;

/// <summary>
/// The three HUD detail levels (minimal, standard, detailed; see <c>docs/prototypes/hud-information-study.md</c>). The rules are <see cref="HudDetails"/>
/// (engine-free, tested): this file resolves the context each frame, adds the rows the levels ask for (power risk, damaged modules, shield) to the ship
/// card, owns the small card under it (the target, the weapons, the objective) and refreshes both at most 15 times a second.
/// </summary>
public partial class Play
{
    /// <summary>A screenshot run pins its level (the settings file is never touched by a shot).</summary>
    private HudDetail? _hudDetailShot;

    private HudDetail CurrentHudDetail => _hudDetailShot ?? SettingsStore.Current.HudDetail;

    private HudContext _hudCtx = HudContext.Flying;
    private HudGroup _hudGroups = HudDetails.Groups(HudDetails.Default, HudContext.Flying);
    private HudRow _hudRows = HudDetails.Rows(HudDetails.Default, HudContext.Flying);

    /// <summary>The refresh of the HUD cards: at most 15 times a second, at once when something that changes what they show changes.</summary>
    private readonly RefreshGate _hudGate = new(15f);
    private bool _hudDue = true;
    private string _hudSignature = "";

    private readonly IncomeTracker _income = new();
    private readonly ClosingSpeed _closing = new();
    private int _closingEnemy = -2;

    private IconCount _riskRow = null!, _modsRow = null!, _shieldRow = null!;
    private PanelContainer _teleCard = null!;
    private IconCount _tgtRow = null!, _tgtClose = null!, _wpnRow = null!, _objRow = null!;
    private GaugeRow _tgtHull = null!;
    private GaugeBar _objBar = null!;
    private Label _teleTitle = null!;
    private float _hudAccum;
    private bool _teleHas;

    /// <summary>The groups that show now: the level's groups, with the HUD mode's families taken away.</summary>
    private bool GroupOn(HudGroup g) => _hudGroups.HasFlag(g);

    /// <summary>True when the HUD mode and the level both allow the world markers.</summary>
    private bool MarkersWanted => HudLayersNow.Markers && GroupOn(HudGroup.Markers);

    private const float RowWidth = 250f;

    /// <summary>The extra rows of the ship card and the card under it. Called once from <c>BuildUi</c>.</summary>
    private void BuildHudDetailUi(VBoxContainer statusBox)
    {
        _riskRow = new IconCount("status:warning", 18, UiTheme.Small) { Visible = false };
        _modsRow = new IconCount("status:health", 18, UiTheme.Small) { Visible = false };
        _shieldRow = new IconCount("status:shield", 18, UiTheme.Small) { Visible = false };
        foreach (var r in new[] { _riskRow, _modsRow, _shieldRow }) { r.LimitWidth(RowWidth); statusBox.AddChild(r); }

        var box = new VBoxContainer { MouseFilter = Control.MouseFilterEnum.Ignore };
        box.AddThemeConstantOverride("separation", 4);
        _teleTitle = MakeText(UiTheme.Caption, HudMuted);
        box.AddChild(UiTheme.Header("tool:target", _teleTitle));
        _tgtRow = new IconCount("status:enemy", 18, UiTheme.Small) { Visible = false };
        _tgtClose = new IconCount("status:speed", 18, UiTheme.Small) { Visible = false };
        _tgtHull = new GaugeRow("status:enemy", 96f, 60) { Visible = false };
        _wpnRow = new IconCount("tool:weapons", 18, UiTheme.Small) { Visible = false };
        _objRow = new IconCount("status:objective", 18, UiTheme.Small) { Visible = false };
        _objBar = new GaugeBar(RowWidth, 5f) { Visible = false };
        foreach (var r in new[] { _tgtRow, _tgtClose, _wpnRow, _objRow }) r.LimitWidth(RowWidth);
        foreach (Control c in new Control[] { _tgtRow, _tgtClose, _tgtHull, _wpnRow, _objRow, _objBar }) box.AddChild(c);
        _teleCard = MakePanel(box);
        _teleCard.MouseFilter = Control.MouseFilterEnum.Ignore;
        _teleCard.Visible = false;
    }

    /// <summary>Per frame, first thing: what the player is doing, which groups and rows the level shows for it, and whether the cards refresh this frame.</summary>
    private void ResolveHudContext(float dt)
    {
        bool building = _tool == ToolMode.Build;
        bool mining = _laserOn && Net.HasPlayer(Player) && Net.LaserFiring(Player);
        _hudCtx = HudDetails.ContextOf(Standing, building, IsDockedNow, ThreatNowHud(), mining);
        var detail = CurrentHudDetail;
        _hudGroups = HudDetails.Groups(detail, _hudCtx);
        _hudRows = HudDetails.Rows(detail, _hudCtx);

        // The cards refresh at most 15 times a second; a change of what they would show refreshes them at once.
        string sig = $"{detail}|{_hudCtx}|{CurrentHudMode}|{_detailsOpen}|{_tool}|{Loc.Lang}|{_mapOpen}|{_selectedDeposit}|{ResearchOpen}|{BuildOpen}|{_helpOpen}|{_objectivesOpen}";
        bool changed = sig != _hudSignature;
        _hudSignature = sig;
        _hudDue = _hudGate.Due(dt, changed || _hudSnap || _uiAudit);
    }

    /// <summary>F3 with Shift (and the button on the displays): the next detail level, saved in the settings.</summary>
    private void CycleHudDetail()
    {
        var next = HudDetails.Next(CurrentHudDetail);
        if (_hudDetailShot is not null) _hudDetailShot = next;
        else SettingsStore.Set(SettingsStore.Current with { HudDetail = next });
        Toast(L("toast.hudDetail", L("hud.detail." + HudDetails.Id(next))), 3.0);
        _hudGate.Invalidate();
    }

    // ---- the ship card's extra rows ---------------------------------------------------------------------------

    /// <summary>The rows the ship card shows even where the level has no vitals group (a threat brings the card up in any context).</summary>
    private HudRow VitalRows => _hudRows.HasFlag(HudRow.Hull) ? _hudRows : HudDetails.Rows(HudDetail.Standard, HudContext.Flying);

    private void UpdateShipExtras()
    {
        int ship = ShipStruct;
        bool structures = _structuresOn && Net.StructureRules && !_demo && ship >= 0 && Net.HasPlayer(Player);
        var rows = VitalRows;

        _riskRow.Visible = structures && rows.HasFlag(HudRow.PowerRisk);
        if (_riskRow.Visible)
        {
            var st = Net.PowerStatus(ship);
            long left = PowerFormat.TimeLeftTicks(st, BurnInterval);
            bool reactor = HasReactor(ship);
            if (reactor && st.SatisfactionPermille < 1000)
                _riskRow.SetText("status:warning", L("hud.row.powerRisk.demand"), BandColor(PowerFormat.Band(st.SatisfactionPermille)), L("hud.row.powerRisk.demand"));
            else if (reactor && left > 0 && left < PowerFormat.LowFuelSeconds * Units.TicksPerSecond * 2L)
                _riskRow.SetText("status:warning", L("hud.row.powerRisk.fuel", PowerFormat.Clock(left)), PowerAmber, PowerFormat.FuelLine(st, BurnInterval));
            else
                _riskRow.SetText("status:power", L("hud.row.powerOk"), UiTheme.Good, PowerFormat.FuelLine(st, BurnInterval));
        }

        _modsRow.Visible = structures && Net.ThreatActive && rows.HasFlag(HudRow.ModulesDamaged);
        if (_modsRow.Visible)
        {
            int total = 0, damaged = 0;
            foreach (var (key, kind) in Net.GetStructure(ship).Modules)
            {
                total++;
                int max = Net.ModuleMaxHp(kind);
                if (max > 0 && Net.ModuleHp(ship, key.Mx, key.My, key.Mz) < max) damaged++;
            }
            if (damaged == 0) _modsRow.SetText("status:health", L("hud.row.modulesOk"), UiTheme.Good);
            else _modsRow.SetText("status:health", L("hud.row.modulesDamaged", damaged, total), damaged * 3 > total ? BadColor : WornColor);
        }

        int shield = structures && rows.HasFlag(HudRow.Shield) ? Net.ShieldAbsorbPercent(ship) : 0;
        _shieldRow.Visible = shield > 0;
        if (shield > 0) _shieldRow.SetText("status:shield", L("hud.row.shield", shield), UiTheme.Accent);
    }

    // ---- the card under the ship card: target, weapons, objective -------------------------------------------------

    private EnemyInfo? NearestEnemy(Vector3 ship, int system, out float distance)
    {
        EnemyInfo? best = null;
        float bestD2 = float.MaxValue;
        foreach (var en in Net.Enemies())
        {
            if (system >= 0 && en.System != system) continue;
            float d2 = (new Vector3(en.X, en.Y, en.Z) / CellUnits - ship).LengthSquared();
            if (d2 < bestD2) { bestD2 = d2; best = en; }
        }
        distance = MathF.Sqrt(bestD2 == float.MaxValue ? 0f : bestD2);
        return best;
    }

    /// <summary>The target, weapons and objective rows. Refreshed 15 times a second; the closing speed samples at that rate.</summary>
    private void UpdateTelemetry(float step)
    {
        _teleTitle.Text = L("hud.target.title");
        var rows = _hudRows;
        bool any = false;
        bool hud = Net.HasPlayer(Player) && !_demo && !Overview;
        var ship = ShipPosition(0f);

        // the target: the nearest enemy when there is one, else the deposit being mined or chosen
        _tgtRow.Visible = _tgtClose.Visible = _tgtHull.Visible = false;
        if (hud && GroupOn(HudGroup.Target))
        {
            int system = Net.CurrentSystem(Player);
            float dist = 0f;
            var enemy = Net.ThreatActive ? NearestEnemy(ship, system, out dist) : null;
            if (enemy is { } e)
            {
                var def = _content.Enemies[e.Type];
                if (e.Id != _closingEnemy) { _closing.Reset(); _closingEnemy = e.Id; }
                _closing.Update(dist, step);
                string name = Loc.Name("enemy", def.Id);
                _tgtRow.Visible = true;
                _tgtRow.SetText(IconCatalog.ForEnemy(def.Id), L("hud.target.enemy", name, dist.ToString("F0", Loc.Culture)), EnemyMarkerColor, name);
                if (rows.HasFlag(HudRow.ClosingSpeed))
                {
                    float c = _closing.Value;
                    _tgtClose.Visible = true;
                    string text = MathF.Abs(c) < 0.3f ? L("hud.target.steady") : c > 0f ? L("hud.target.closing", c.ToString("F1", Loc.Culture)) : L("hud.target.opening", (-c).ToString("F1", Loc.Culture));
                    _tgtClose.SetText("status:speed", text, c > 0.3f ? WornColor : UiTheme.Muted);
                }
                if (rows.HasFlag(HudRow.TargetHull))
                {
                    float f = Math.Clamp(e.Hp / (float)Math.Max(1, def.Hp), 0f, 1f);
                    _tgtHull.Visible = true;
                    _tgtHull.Set("status:hull", f, L("hud.target.hull", (int)MathF.Round(f * 100f)), HpColor(f), $"{e.Hp}/{def.Hp}");
                }
                any = true;
            }
            else
            {
                _closingEnemy = -2;
                int dep = Net.LaserFiring(Player) ? Net.MiningDeposit(Player) : _selectedDeposit;
                if (Net.IsDeposit(dep) && Net.IsDepositRevealed(dep) && Net.DepositRemaining(dep) > 0)
                {
                    var q = Net.DepositPosition(dep);
                    float dd = (new Vector3(q.X, q.Y, q.Z) - ship).Length();
                    string item = ItemName(Net.DepositItem(dep));
                    _tgtRow.Visible = true;
                    string text = Net.LaserFiring(Player) && Net.InMineRange(Player, dep)
                        ? L("hud.target.mining", item, Net.DepositRemaining(dep))
                        : L("hud.target.deposit", item, Net.DepositRemaining(dep), dd.ToString("F0", Loc.Culture));
                    _tgtRow.SetText(Icons.ItemId(_content.Items[Net.DepositItem(dep)].Id), text, UiTheme.Gold, DensityText(dep));
                    any = true;
                }
            }
        }

        // the weapons: which group is in hand and whether it is armed (the sim does not expose the cooldown: see the study)
        _wpnRow.Visible = hud && GroupOn(HudGroup.Weapons) && rows.HasFlag(HudRow.WeaponState);
        if (_wpnRow.Visible)
        {
            var info = Net.GetPlayerInfo(Player);
            bool guns = HardpointsOn ? _weaponGroup == WeaponGroup.Guns : true;
            string group = L(guns ? "hud.weapons.guns" : "hud.weapons.laser");
            bool armed = guns ? info.Armed : _laserOn;
            _wpnRow.SetText(guns ? "module:gun" : "tool:mine", L(armed ? "hud.weapons.armed" : "hud.weapons.safe", group), armed ? UiTheme.Good : UiTheme.Muted);
            any = true;
        }

        // the objective: the step in progress and how many are done
        _objRow.Visible = _objBar.Visible = hud && GuidedSteps && GroupOn(HudGroup.Objective) && rows.HasFlag(HudRow.ObjectiveProgress) && _objectives.Current is not null;
        if (_objRow.Visible)
        {
            var states = _objectives.States;
            var cur = states.First(s => s.Objective == _objectives.Current);
            string title = L(cur.TitleKey);
            _objRow.SetText(IconCatalog.ForObjective(cur.Objective), L("hud.obj.step", _objectives.DoneCount + 1, states.Count, title), UiTheme.Gold, L(cur.HintKey));
            _objBar.Set(_objectives.DoneCount / (float)Math.Max(1, states.Count), UiTheme.Gold);
            any = true;
        }
        _teleHas = any;
    }

    /// <summary>Under the ship card, never over the centre of the view.</summary>
    private void LayoutTelemetry()
    {
        _teleCard.ResetSize();
        float top = _statusPanel.Visible ? _statusPanel.Position.Y + _statusPanel.Size.Y + 8f : 10f;
        _teleCard.Position = new Vector2(12f, top);
    }

    // ---- the hold's forecast ------------------------------------------------------------------------------------

    /// <summary>The text after the hold total at this level: how long until it is full, and at the detailed level the income per minute. Empty when nothing is growing.</summary>
    private (string Text, string Tip) HoldNote(int used, int capacity)
    {
        double now = Time.GetTicksMsec() / 1000.0;
        _income.Add((float)now, used);
        if (!_hudRows.HasFlag(HudRow.HoldForecast)) return ("", "");
        float seconds = _income.SecondsToFull(used, capacity);
        if (seconds < 0f || seconds > 3600f) return ("", "");
        string clock = PowerFormat.Clock((long)(seconds * Units.TicksPerSecond));
        string text = L("hud.hold.forecast", clock);
        string income = ((int)MathF.Round(_income.PerMinute)).ToString(Loc.Culture);
        if (_hudRows.HasFlag(HudRow.IncomePerMinute)) text += "  " + L("hud.hold.income", income);
        return (text, L("hud.hold.incomeTip", income, clock));
    }
}
