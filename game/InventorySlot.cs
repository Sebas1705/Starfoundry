using System;
using Godot;
using Starfoundry.Sim;
using Starfoundry.Ui;

namespace Starfoundry.Game;

/// <summary>
/// One slot of the inventory panel (ADR 0035): a tile with an item icon and a count badge. It draws its own hover, focus ring, filter dimming and
/// drop-target highlight, reports presses, drags and releases to the panel (<see cref="InventoryPanel"/>), and answers the keyboard: Enter picks up or drops,
/// Shift+Enter moves to the other panel, Delete throws away. The tooltip is set by the panel.
/// </summary>
public partial class InventorySlot : Control
{
    private static readonly Color TileBg = new(0.1f, 0.13f, 0.2f, 0.95f), TileEdge = new(0.35f, 0.45f, 0.65f, 0.7f);

    public ContainerRef Container;

    /// <summary>The slot number a move names: the grid index in the bag, the item index in a hold, stock or machine buffer, 0 for the reactor, -1 for an empty slot of a list.</summary>
    public int Slot = -1;

    public int Item = -1, Count, StackSize;
    public string ItemId = "";
    public string Tip = "";
    public bool IsBin;

    /// <summary>The filter hides this slot's item: it is drawn dim.</summary>
    public bool Dim;

    /// <summary>The slot is picked up (keyboard) or being dragged: drawn faint.</summary>
    public bool Lifted;

    /// <summary>A drag is over this slot.</summary>
    public bool DropTarget;

    /// <summary>The slot is the one selected for the detail line.</summary>
    public bool Selected;

    private readonly InventoryPanel _panel;
    private bool _hover;
    private readonly int _pixels;

    public bool Empty => Count <= 0 || Item < 0;

    public InventorySlot(InventoryPanel panel, ContainerRef container, int slot, int pixels)
    {
        _panel = panel;
        Container = container;
        Slot = slot;
        _pixels = pixels;
        Name = $"Slot_{container.Kind}_{container.Id}_{slot}"; // stable handle for agents (docs/prototypes/agent-play.md)
        CustomMinimumSize = new Vector2(pixels, pixels);
        FocusMode = FocusModeEnum.All;
        MouseFilter = MouseFilterEnum.Stop;
        ClipContents = true;
    }

    /// <summary>Fills the slot; redraws only when something changed. Returns true when it did.</summary>
    public bool Set(int item, int count, int stackSize, string itemId, string tip, bool dim)
    {
        if (item == Item && count == Count && stackSize == StackSize && itemId == ItemId && tip == Tip && dim == Dim) return false;
        Item = item; Count = count; StackSize = stackSize; ItemId = itemId; Tip = tip; Dim = dim;
        QueueRedraw();
        return true;
    }

    public void SetFlags(bool lifted, bool dropTarget, bool selected)
    {
        if (lifted == Lifted && dropTarget == DropTarget && selected == Selected) return;
        Lifted = lifted; DropTarget = dropTarget; Selected = selected;
        QueueRedraw();
    }

    public override string _GetTooltip(Vector2 atPosition) => Tip;

    public override void _Notification(int what)
    {
        if (what == NotificationMouseEnter) { _hover = true; QueueRedraw(); }
        else if (what == NotificationMouseExit) { _hover = false; QueueRedraw(); }
        else if (what == NotificationFocusEnter || what == NotificationFocusExit) QueueRedraw();
    }

    public override void _Draw()
    {
        var rect = new Rect2(Vector2.Zero, Size);
        bool hot = _hover || DropTarget;
        var box = UiTheme.Box(DropTarget ? new Color(0.16f, 0.3f, 0.2f, 0.97f) : hot ? new Color(0.16f, 0.22f, 0.34f, 0.97f) : IsBin ? new Color(0.16f, 0.09f, 0.1f, 0.95f) : TileBg,
            DropTarget ? UiTheme.Good : Selected ? UiTheme.Gold : hot ? UiTheme.Accent : IsBin ? new Color(0.7f, 0.35f, 0.35f, 0.7f) : TileEdge, 5, 2f, Selected || DropTarget ? 2 : 1);
        DrawStyleBox(box, rect);
        float alpha = Lifted ? 0.3f : Dim ? 0.3f : 1f;
        if (IsBin)
        {
            int ip = (int)(Size.X * 0.56f);
            DrawTextureRect(Icons.Get("tool:bin", ip), new Rect2((Size.X - ip) / 2f, (Size.Y - ip) / 2f, ip, ip), false, new Color(1f, 0.55f, 0.5f, hot ? 1f : 0.75f));
        }
        else if (!Empty && ItemId.Length > 0)
        {
            string icon = Icons.ItemId(ItemId);
            int ip = (int)(Size.X * 0.62f);
            var tint = Icons.TintOf(icon);
            DrawTextureRect(Icons.Get(icon, ip), new Rect2((Size.X - ip) / 2f, (Size.Y - ip) / 2f - 2f, ip, ip), false, tint with { A = alpha });
            if (Count > 1 || StackSize > 1)
            {
                var font = UiFit.DefaultFont;
                int fs = Size.X < 44f ? UiTheme.Caption : UiTheme.Small;
                string text = Count.ToString(Loc.Culture);
                var white = new Color(1f, 1f, 1f, alpha);
                DrawStringOutline(font, new Vector2(2f, Size.Y - 4f), text, HorizontalAlignment.Right, Size.X - 5f, fs, 4, new Color(0f, 0f, 0f, 0.85f * alpha));
                DrawString(font, new Vector2(2f, Size.Y - 4f), text, HorizontalAlignment.Right, Size.X - 5f, fs, Count >= StackSize && StackSize > 0 ? UiTheme.Gold with { A = alpha } : white);
            }
        }
        if (HasFocus())
        {
            var ring = UiTheme.FocusRing();
            ring.BorderColor = Colors.White;
            DrawStyleBox(ring, rect);
        }
    }

    // ---- input -------------------------------------------------------------------------------------------

    private MouseButton _down = MouseButton.None;
    private Vector2 _downAt;

    public override void _GuiInput(InputEvent ev)
    {
        switch (ev)
        {
            case InputEventMouseButton { ButtonIndex: MouseButton.Left or MouseButton.Right } mb:
                if (mb.Pressed)
                {
                    _down = mb.ButtonIndex;
                    _downAt = mb.Position;
                    GrabFocus();
                    _panel.SlotPressed(this, mb.ButtonIndex == MouseButton.Right);
                    AcceptEvent();
                }
                else if (_down == mb.ButtonIndex)
                {
                    _down = MouseButton.None;
                    _panel.SlotReleased(this, mb.ButtonIndex == MouseButton.Right, mb.ShiftPressed, mb.DoubleClick, GetGlobalMousePosition());
                    AcceptEvent();
                }
                break;
            case InputEventMouseMotion mm when _down != MouseButton.None:
                if (!_panel.Dragging && mm.Position.DistanceTo(_downAt) > 6f) _panel.BeginDrag(this, _down == MouseButton.Right);
                AcceptEvent();
                break;
            case InputEventKey { Pressed: true, Echo: false } k:
                if (k.Keycode is Key.Enter or Key.KpEnter or Key.Space)
                {
                    _panel.SlotActivated(this, k.ShiftPressed, k.CtrlPressed);
                    AcceptEvent();
                }
                else if (k.Keycode == Key.Delete)
                {
                    _panel.SlotBinRequested(this);
                    AcceptEvent();
                }
                break;
        }
    }
}

/// <summary>The icon and count that follow the pointer while a stack is dragged or picked up.</summary>
public partial class DragGhost : Control
{
    private string _icon = "";
    private int _count;
    private int _pixels = 40;

    public DragGhost()
    {
        MouseFilter = MouseFilterEnum.Ignore;
        ZIndex = 100;
        Visible = false;
        TopLevel = true;
    }

    public void Show(string iconId, int count, int pixels)
    {
        _icon = iconId; _count = count; _pixels = pixels;
        Size = new Vector2(pixels, pixels);
        Visible = true;
        QueueRedraw();
    }

    public override void _Process(double delta)
    {
        if (Visible) GlobalPosition = GetGlobalMousePosition() - new Vector2(_pixels, _pixels) / 2f;
    }

    public override void _Draw()
    {
        if (_icon.Length == 0) return;
        DrawStyleBox(UiTheme.Box(new Color(0.12f, 0.2f, 0.34f, 0.85f), UiTheme.Gold, 5, 2f, 2), new Rect2(Vector2.Zero, Size));
        int ip = (int)(_pixels * 0.62f);
        DrawTextureRect(Icons.Get(_icon, ip), new Rect2((_pixels - ip) / 2f, (_pixels - ip) / 2f - 2f, ip, ip), false, Icons.TintOf(_icon));
        if (_count > 1)
        {
            string text = _count.ToString(Loc.Culture);
            DrawStringOutline(UiFit.DefaultFont, new Vector2(2f, _pixels - 4f), text, HorizontalAlignment.Right, _pixels - 5f, UiTheme.Small, 4, new Color(0f, 0f, 0f, 0.85f));
            DrawString(UiFit.DefaultFont, new Vector2(2f, _pixels - 4f), text, HorizontalAlignment.Right, _pixels - 5f, UiTheme.Small, Colors.White);
        }
    }
}
