using System;
using Godot;
using Starfoundry.Ui;

namespace Starfoundry.Game;

/// <summary>
/// The arsenal bar of the HUD (docs/prototypes/arsenal.md): one slot per weapon kind and mark fitted, drawn from an <see cref="ArsenalModel"/>. Each slot has the icon inside a state ring,
/// the number key, the module count and a line of readout; the slot in hand has a marker that eases from slot to slot. With the mouse free a click picks a slot and hovering one names it.
/// </summary>
public partial class ArsenalBar : Control
{
    public const float SlotW = 92f, SlotH = 62f, Gap = 6f, Pad = 6f;
    private const float CompactW = 60f, CompactH = 50f;

    private readonly ArsenalModel _model;
    private int _hover = -1;

    /// <summary>The slot index a click picked.</summary>
    public Action<int>? Picked;

    /// <summary>Draw only the slot in hand (HUD detail level Minimal).</summary>
    public bool Compact { get; set; }

    public ArsenalBar(ArsenalModel model)
    {
        _model = model;
        Name = "ArsenalBar"; // slots are drawn, not nodes: agents pick one with the keys 1-9 or ArsenalBar.Picked
        MouseFilter = MouseFilterEnum.Ignore;
        FocusMode = FocusModeEnum.None;
    }

    public Vector2 PreferredSize()
    {
        if (_model.IsEmpty) return new Vector2(Math.Max(220f, 2f * Pad + 190f), 34f);
        if (Compact) return new Vector2(CompactW, CompactH);
        int n = _model.Slots.Count;
        return new Vector2(2f * Pad + n * SlotW + (n - 1) * Gap, SlotH + 2f * Pad);
    }

    private int SlotAt(Vector2 p)
    {
        if (_model.IsEmpty || Compact) return -1;
        for (int i = 0; i < _model.Slots.Count; i++)
            if (new Rect2(Pad + i * (SlotW + Gap), Pad, SlotW, SlotH).HasPoint(p)) return i;
        return -1;
    }

    public override string _GetTooltip(Vector2 atPosition)
    {
        int i = Compact ? _model.Selected : SlotAt(atPosition);
        return i >= 0 && i < _model.Slots.Count ? _model.Tooltip(i) : "";
    }

    public override void _GuiInput(InputEvent @event)
    {
        switch (@event)
        {
            case InputEventMouseMotion mm:
                int h = SlotAt(mm.Position);
                if (h != _hover) { _hover = h; QueueRedraw(); }
                break;
            case InputEventMouseButton { Pressed: true, ButtonIndex: MouseButton.Left } mb:
                int i = SlotAt(mb.Position);
                if (i >= 0) { Picked?.Invoke(i); AcceptEvent(); }
                break;
        }
    }

    public override void _Notification(int what)
    {
        if (what == NotificationMouseExit && _hover != -1) { _hover = -1; QueueRedraw(); }
    }

    private static readonly System.Collections.Generic.Dictionary<(Color, Color, int, int), StyleBoxFlat> Styles = new();

    /// <summary>The boxes of the bar, built once each: a few dozen colour combinations at most, so no allocation per frame.</summary>
    private static StyleBoxFlat Cached(Color bg, Color border, int radius, float margin, int width = 1) =>
        Styles.TryGetValue((bg, border, radius, width), out var s) ? s : Styles[(bg, border, radius, width)] = UiTheme.Box(bg, border, radius, margin, width);

    private static Color StateColor(SlotState s) => s switch
    {
        SlotState.Firing => UiTheme.Gold,
        SlotState.Cooling => UiTheme.Accent,
        SlotState.Blocked or SlotState.LowPower => new Color(1f, 0.72f, 0.3f),
        SlotState.NoPower or SlotState.Dry => UiTheme.Bad,
        _ => UiTheme.Good,
    };

    public override void _Draw()
    {
        var font = GetThemeDefaultFont();
        var all = new Rect2(Vector2.Zero, Size);
        DrawStyleBox(Cached(new Color(0.04f, 0.06f, 0.1f, 0.78f), new Color(0.35f, 0.45f, 0.65f, 0.55f), 8, 0f), all);
        if (_model.IsEmpty)
        {
            DrawText(font, Loc.T("arsenal.empty"), new Vector2(Pad + 4f, 22f), Size.X - 2f * Pad - 8f, 13, UiTheme.Dim);
            return;
        }
        if (Compact)
        {
            DrawSlot(font, Math.Max(0, _model.Selected), new Rect2(0, 0, CompactW, CompactH), true, false, true);
            return;
        }
        float cx = Pad + _model.Cursor * (SlotW + Gap);
        var marker = new Rect2(cx - 2f, Pad - 2f, SlotW + 4f, SlotH + 4f);
        DrawStyleBox(Cached(new Color(UiTheme.Gold, 0.14f), UiTheme.Gold, 7, 0f, 2), marker);
        for (int i = 0; i < _model.Slots.Count; i++)
            DrawSlot(font, i, new Rect2(Pad + i * (SlotW + Gap), Pad, SlotW, SlotH), i == _model.Selected, i == _hover, false);
    }

    private void DrawSlot(Font font, int i, Rect2 r, bool selected, bool hover, bool compact)
    {
        var slot = _model.Slots[i];
        var state = _model.StateOf(i);
        Color sc = StateColor(state);
        bool mate = !selected && _model.Selected >= 0 && _model.Slots[_model.Selected].Group == slot.Group; // acts together with the one in hand
        if (hover) DrawStyleBox(Cached(new Color(1f, 1f, 1f, 0.07f), new Color(UiTheme.Accent, 0.8f), 6, 0f), r);
        else if (!compact) DrawStyleBox(Cached(new Color(1f, 1f, 1f, mate ? 0.05f : 0.02f), new Color(1f, 1f, 1f, mate ? 0.35f : 0.12f), 6, 0f), r);

        // the icon inside the state ring
        var centre = new Vector2(r.Position.X + 26f, r.Position.Y + 24f);
        DrawArc(centre, 19f, 0f, Mathf.Tau, 40, new Color(1f, 1f, 1f, 0.12f), 2f);
        float fill = _model.RingOf(i);
        if (fill > 0.01f) DrawArc(centre, 19f, -Mathf.Pi / 2f, -Mathf.Pi / 2f + Mathf.Tau * fill, 40, new Color(sc, selected ? 1f : 0.7f), 2.5f);
        var tex = Icons.Get(slot.Icon, 26);
        DrawTextureRect(tex, new Rect2(centre - new Vector2(13f, 13f), new Vector2(26f, 26f)), false, selected ? Icons.TintOf(slot.Icon) : Icons.TintOf(slot.Icon) * new Color(1f, 1f, 1f, 0.75f));

        // the number key and the module count
        var cap = new Rect2(r.End.X - 22f, r.Position.Y + 6f, 16f, 16f);
        DrawStyleBox(Cached(new Color(0f, 0f, 0f, 0.4f), new Color(1f, 1f, 1f, selected ? 0.6f : 0.3f), 3, 0f), cap);
        DrawString(font, new Vector2(cap.Position.X, cap.Position.Y + 12.5f), ArsenalModel.KeyHint(i), HorizontalAlignment.Center, 16f, 12, selected ? UiTheme.Text : UiTheme.Muted);
        if (slot.Count > 1) DrawString(font, new Vector2(r.End.X - 40f, r.Position.Y + 38f), "x" + slot.Count, HorizontalAlignment.Right, 34f, 13, UiTheme.Text);
        if (compact) return;

        // the readout: the mining numbers or the volley state for the slot in hand, the state word for the others
        string line = selected ? _model.Detail(i) : Loc.T(ArsenalModel.StateKey(state));
        DrawText(font, line, new Vector2(r.Position.X + 6f, r.End.Y - 6f), r.Size.X - 10f, 11, selected ? sc : UiTheme.Dim);
    }

    private void DrawText(Font font, string text, Vector2 baseline, float width, int size, Color colour)
    {
        if (font.GetStringSize(text, HorizontalAlignment.Left, -1, size).X > width)
        {
            while (text.Length > 1 && font.GetStringSize(text + "…", HorizontalAlignment.Left, -1, size).X > width) text = text[..^1];
            text += "…";
        }
        DrawString(font, baseline, text, HorizontalAlignment.Left, -1, size, colour);
    }
}
