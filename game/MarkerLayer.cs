using System;
using System.Collections.Generic;
using Godot;
using Starfoundry.Ui;

namespace Starfoundry.Game;

/// <summary>
/// Draws the directional markers of the first-person HUD: a diamond with a label over a target in view, an arrow with a label
/// on the screen edge when it is off to a side or behind. The positions come from <see cref="Markers.Place"/>.
/// </summary>
public partial class MarkerLayer : Control
{
    public readonly record struct Item(string Text, Color Color, MarkerPlacement Place);

    private const int FontSize = 13;
    private Item[] _items = Array.Empty<Item>();

    public MarkerLayer()
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
        var shadow = new Color(0f, 0f, 0f, 0.75f);
        foreach (var item in _items)
        {
            var p = item.Place;
            var at = new Vector2(p.X, p.Y);
            float w = font.GetStringSize(item.Text, HorizontalAlignment.Left, -1, FontSize).X;
            Vector2 textPos;
            if (p.OnScreen)
            {
                DrawPolygon(Diamond(at, 8f), new[] { shadow });
                DrawPolygon(Diamond(at, 6f), new[] { item.Color });
                textPos = new Vector2(at.X - w / 2f, at.Y + 24f);
            }
            else
            {
                // Screen y grows down, the placement angle is counter-clockwise with up positive.
                var d = new Vector2(MathF.Cos(p.AngleRadians), -MathF.Sin(p.AngleRadians));
                var n = new Vector2(-d.Y, d.X);
                DrawPolygon(new[] { at + d * 13f, at - d * 7f + n * 9f, at - d * 7f - n * 9f }, new[] { shadow });
                DrawPolygon(new[] { at + d * 10f, at - d * 5f + n * 6.5f, at - d * 5f - n * 6.5f }, new[] { item.Color });
                textPos = new Vector2(at.X - d.X * (w / 2f + 18f) - w / 2f, at.Y - d.Y * 20f + 5f);
            }
            textPos.X = Math.Clamp(textPos.X, 4f, Math.Max(4f, Size.X - w - 4f));
            textPos.Y = Math.Clamp(textPos.Y, FontSize + 2f, Math.Max(FontSize + 2f, Size.Y - 6f));
            DrawStringOutline(font, textPos, item.Text, HorizontalAlignment.Left, -1, FontSize, 4, shadow);
            DrawString(font, textPos, item.Text, HorizontalAlignment.Left, -1, FontSize, item.Color);
        }
    }

    private static Vector2[] Diamond(Vector2 c, float r) => new[] { c + new Vector2(0, -r), c + new Vector2(r, 0), c + new Vector2(0, r), c + new Vector2(-r, 0) };
}
