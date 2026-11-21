using System;
using System.Collections.Generic;
using Godot;

namespace Starfoundry.Game;

/// <summary>Draws floating hit-point bars over modules (screen positions come from the game's camera projection).</summary>
public partial class ModuleBarLayer : Control
{
    public readonly record struct Item(Vector2 At, string Text, float Fraction, Color Color);

    private Item[] _items = Array.Empty<Item>();

    public ModuleBarLayer()
    {
        MouseFilter = MouseFilterEnum.Ignore;
        SetAnchorsPreset(LayoutPreset.FullRect);
    }

    public void Set(List<Item> items)
    {
        _items = items.ToArray();
        QueueRedraw();
    }

    public override void _Draw()
    {
        var font = ThemeDB.FallbackFont;
        foreach (var it in _items)
        {
            const float w = 92f, h = 7f;
            var rect = new Rect2(it.At.X - w / 2f, it.At.Y, w, h);
            DrawRect(rect.Grow(2f), new Color(0f, 0f, 0f, 0.7f));
            DrawRect(rect, new Color(0.14f, 0.16f, 0.22f));
            DrawRect(new Rect2(rect.Position, new Vector2(w * Math.Clamp(it.Fraction, 0f, 1f), h)), it.Color);
            float tw = font.GetStringSize(it.Text, HorizontalAlignment.Left, -1, 12).X;
            var pos = new Vector2(it.At.X - tw / 2f, it.At.Y - 5f);
            DrawStringOutline(font, pos, it.Text, HorizontalAlignment.Left, -1, 12, 4, new Color(0f, 0f, 0f, 0.8f));
            DrawString(font, pos, it.Text, HorizontalAlignment.Left, -1, 12, it.Color);
        }
    }
}
