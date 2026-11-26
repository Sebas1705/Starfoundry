using System;
using System.Collections.Generic;
using Godot;
using Starfoundry.Ui;

namespace Starfoundry.Game;

/// <summary>
/// The tab strip of the ship computer: one icon button per tab (with the tab number and, when there is room, the name), the tab keys at the
/// ends and a close button. It only reports clicks; <see cref="Play"/> decides what each tab shows.
/// </summary>
public partial class ShipComputerBar : PanelContainer
{
    public const float Height = 58f;

    public event Action<ComputerTab>? TabChosen;
    public event Action? CloseRequested;

    private readonly Dictionary<ComputerTab, Button> _buttons = new();
    private HBoxContainer _row = null!;
    private Button _close = null!;
    private float _width;
    private string _signature = "";

    public override void _Ready()
    {
        Theme = UiTheme.Create();
        AddThemeStyleboxOverride("panel", UiTheme.Box(UiTheme.Solid, UiTheme.EdgeStrong, 8, 5f));
        MouseFilter = MouseFilterEnum.Stop;
        _row = new HBoxContainer();
        _row.AddThemeConstantOverride("separation", 4);
        AddChild(_row);
        foreach (var tab in ShipComputerTabs.All)
        {
            var t = tab;
            var b = UiKit.Button("", () => TabChosen?.Invoke(t), 13);
            b.ToggleMode = true;
            b.ExpandIcon = false;
            b.IconAlignment = HorizontalAlignment.Left;
            b.SizeFlagsHorizontal = SizeFlags.ExpandFill;
            b.FocusMode = FocusModeEnum.None; // Q and E (or a click) change tab; the pages keep the keyboard focus
            b.Name = "Tab" + t;
            b.AddThemeConstantOverride("icon_max_width", 20);
            b.AddThemeColorOverride("icon_normal_color", UiTheme.Muted);
            b.AddThemeColorOverride("icon_pressed_color", Colors.White);
            b.AddThemeColorOverride("icon_hover_color", Colors.White);
            b.AddThemeColorOverride("icon_hover_pressed_color", Colors.White);
            _buttons[t] = b;
            _row.AddChild(b);
        }
        _close = UiKit.Button("", () => CloseRequested?.Invoke(), 13);
        _close.Icon = Icons.Get("tool:close", 18);
        _close.ExpandIcon = false;
        _close.FocusMode = FocusModeEnum.None;
        _close.Name = "CloseComputer";
        _close.CustomMinimumSize = new Vector2(44f, 0f);
        _row.AddChild(_close);
    }

    /// <summary>Puts the strip in a rectangle and refreshes the buttons (the active tab, the tabs that are off, the language).</summary>
    public void Set(float x, float y, float width, ComputerTab? active, ComputerAvailability availability)
    {
        if (_row is null) return;
        string sig = $"{width}|{active}|{availability}|{Loc.Lang}";
        Position = new Vector2(x, y);
        if (sig == _signature) return;
        _signature = sig;
        _width = width;
        float inner = width - 10f - 4f * ShipComputerTabs.All.Count - 56f; // margins, gaps and the close button
        float per = inner / ShipComputerTabs.All.Count;
        // The names show when each tab has room for its number, icon and a short name; otherwise the icon and the number stay and the name is the tooltip.
        bool names = per >= 96f;
        foreach (var tab in ShipComputerTabs.All)
        {
            var b = _buttons[tab];
            int number = (int)tab + 1;
            string name = Loc.T(ShipComputerTabs.NameKey(tab));
            b.Icon = Icons.Get(ShipComputerTabs.Icon(tab), 20);
            b.CustomMinimumSize = new Vector2(Math.Max(34f, per), 0);
            b.Disabled = !availability.Has(tab);
            b.SetPressedNoSignal(active == tab);
            b.TooltipText = number + "  " + name + (ShipComputerTabs.WhyOff(tab, availability) is { } why ? "\n" + Loc.T(why) : "");
            b.Text = names ? number + " " + name : number.ToString();
            b.ClipText = true; // the width of a tab never depends on its text, so no tab moves when another one is chosen
            if (names) UiFit.ShrinkButton(b, number + " " + name, per, 13, 9, iconWidth: 24f);
        }
        _close.TooltipText = Loc.T("computer.close") + "  (Tab)";
        // After the buttons: the strip may only shrink once their minimum widths have, or it would keep the old width.
        _row.ResetSize();
        CustomMinimumSize = new Vector2(width, Height);
        ResetSize();
        Size = new Vector2(width, Height);
    }
}
