using System;
using System.Collections.Generic;
using Godot;
using Starfoundry.Sim;
using Starfoundry.Ui;

namespace Starfoundry.Game;

/// <summary>
/// The context rules of the HUD. By default the screen holds the crosshair, the key panel, the toasts and a small alert when a wave comes;
/// everything else shows up because of what the player does:
/// seated or flying -> the ship card (hull, power and fuel, hold, flight readout, markers); standing -> nothing but what is aimed at;
/// aiming at a machine, belt, deposit or module -> that object's card; a build tool in hand -> its cost and hint; <c>H</c> -> the info
/// panels (full status, goals and steps, research, threat forecast). Cards fade in and out instead of popping.
/// </summary>
public partial class Play
{
    private static readonly Color HudMuted = new(0.62f, 0.7f, 0.84f);
    private static readonly Color EnemyMarkerColor = new(1f, 0.35f, 0.3f);

    private Label _statusTitle = null!;
    private GaugeRow _hullBox = null!;
    private float _hudDt = 1f / 60f;
    private readonly Dictionary<Control, float> _hudFade = new();

    /// <summary>The headless screenshot flags render frames as fast as they can, so a fade would be caught half way: they snap instead.</summary>
    private readonly bool _hudSnap = Array.Exists(OS.GetCmdlineUserArgs(), a => a.Contains("-shot") || a.StartsWith("--hud-sweep="));

    /// <summary>True while the player sits in the pilot seat (or the world has no walking): the ship's data is what the HUD is about.</summary>
    private bool ShipHudContext => !Standing;

    /// <summary>
    /// Keeps a card out of the bottom text lines (the notifications and the tool hint, which sit under the view's centre): a card that would
    /// run over them is lifted above them instead. Called after the toasts and the hint are laid out.
    /// </summary>
    private Vector2 AvoidBottomText(Vector2 pos, Vector2 size, float gap = 8f)
    {
        float top = float.MaxValue;
        bool toasts = false;
        foreach (var l in _toastLabels) toasts |= l.Visible;
        var box = new Rect2(_toastBox.Position, _toastBox.Size);
        if (toasts && new Rect2(pos, size).Intersects(box)) top = box.Position.Y;
        if (_modeHint.Text.Length > 0)
        {
            var hint = new Rect2(_modeHint.Position, _modeHint.Size);
            if (new Rect2(pos, size).Intersects(hint)) top = Math.Min(top, hint.Position.Y);
        }
        if (top == float.MaxValue) return pos;
        return new Vector2(pos.X, Math.Max(8f, top - size.Y - gap));
    }

    /// <summary>The title and the hull row at the head of the ship card.</summary>
    private void BuildShipCardHead(VBoxContainer box)
    {
        _statusTitle = MakeText(UiTheme.Caption, HudMuted);
        box.AddChild(UiTheme.Header("status:hull", _statusTitle));
        _hullBox = new GaugeRow("status:hull") { Visible = false };
        box.AddChild(_hullBox);
    }

    /// <summary>The hull of the ship as a text and a bar (worlds with threats only: without them nothing can hurt it).</summary>
    private void UpdateHullRow(PlayerInfo p)
    {
        _hullBox.Visible = Net.ThreatActive && !_demo;
        if (!_hullBox.Visible) return;
        int max = Math.Max(1, Net.ShipMaxHull);
        float f = Math.Clamp(p.Hull / (float)max, 0f, 1f);
        if (p.RespawnRemaining > 0)
        {
            string text = L("hud.rebuilding", (p.RespawnRemaining / (float)Units.TicksPerSecond).ToString("F0", Loc.Culture));
            _hullBox.Set("status:hull", 0f, $"{(p.RespawnRemaining / (float)Units.TicksPerSecond):F0} s", ToastThreatColor, text);
        }
        else _hullBox.Set("status:hull", f, $"{p.Hull}/{max}", HpColor(f), L("hud.ess.hull", p.Hull, max));
    }

    /// <summary>A wave is on its way or enemies are alive in the current system.</summary>
    private bool ThreatNowHud()
    {
        if (!Net.ThreatActive || !Net.HasPlayer(Player)) return false;
        var th = Net.GetSystemThreat(Math.Max(0, Net.CurrentSystem(Player)));
        return th.WarningRemaining >= 0 || th.Enemies > 0;
    }

    /// <summary>
    /// Fades a card toward shown or hidden. The control's own <c>Visible</c> (set earlier in the frame by whatever fills it) says whether it has
    /// anything to show; <paramref name="want"/> says whether the context asks for it. A card with nothing left keeps its last text while it fades out.
    /// </summary>
    private void Fade(Control c, bool want, bool hasContent = true, bool instantOut = false)
    {
        float a = _hudFade.GetValueOrDefault(c, 0f);
        if (!hasContent && instantOut) a = 0f; // a card whose text is wiped each frame (the aim tooltip) cannot fade out
        float target = want && hasContent ? 1f : 0f;
        float step = _hudSnap ? 1f : _hudDt * (target > a ? 8f : 5f);
        a = a < target ? Math.Min(target, a + step) : Math.Max(target, a - step);
        _hudFade[c] = a;
        c.Modulate = new Color(1f, 1f, 1f, a);
        c.Visible = a > 0.01f;
    }

    /// <summary>Decides, once the layout is done, which cards the context asks for and fades them.</summary>
    private void ApplyHudContext(Vector2 size)
    {
        if (_demo) return; // the scripted overview keeps its own simple readout
        bool hud = Net.HasPlayer(Player);
        bool building = _tool == ToolMode.Build;
        bool aiming = AimActive && !building && (_hover.Kind is HoverKind.Machine or HoverKind.Segment or HoverKind.Deposit or HoverKind.Enemy || _modulePanel.Visible);
        bool threat = ThreatNowHud();
        bool info = _detailsOpen;

        var layers = HudLayersNow; // the HUD mode (F3): seated, the cockpit-only and minimal modes leave the cards to the cockpit displays
        bool shipCard = hud && (info || (layers.Cards && (threat || (ShipHudContext && !building && !aiming))));
        _statusTitle.Text = L(info ? "hud.card.info" : "hud.card.ship").ToUpperInvariant();
        _holdItems.Visible = shipCard;
        _toolLabel.Visible = building;

        bool statusContent = _statusPanel.Visible;
        Fade(_statusPanel, shipCard, statusContent);
        bool flightClear = !shipCard || !new Rect2(_flightPanel.Position, _flightPanel.Size).Intersects(new Rect2(_statusPanel.Position, _statusPanel.Size));
        Fade(_flightPanel, hud && layers.FlightReadout && ShipHudContext && !building && !aiming && flightClear, _flightPanel.Visible);
        Fade(_toolbar, shipCard || building, true);
        Fade(_objectivesPanel, hud && info, _objectivesPanel.Visible && (_objectivesText.Visible || _goalsBox.Visible || _goalLabel.Visible));
        Fade(_targetPanel, true, _targetPanel.Visible);
        Fade(_tooltipPanel, true, _tooltipPanel.Visible, instantOut: true);
        // The build hint snaps; the one-line step and laser hints fade in and out like the cards.
        if (building) { _hudFade[_modeHint] = 1f; _modeHint.Modulate = Colors.White; _modeHint.Visible = _modeHint.Text.Length > 0; }
        else Fade(_modeHint, !_mapOpen && !ResearchOpen && !BuildOpen && hud, _modeHint.Text.Length > 0);
    }
}
