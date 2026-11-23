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
    public readonly record struct Item(string Text, Color Color, MarkerPlacement Place, int Priority = 1, string Icon = "", string ShortText = "");

    private const int FontSize = 13;
    private Item[] _items = Array.Empty<Item>();

    /// <summary>Labels stay left of this x (the build and research panels dock at the right and would hide them); 0 means the whole width.</summary>
    public float RightLimit;

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

    /// <summary>Distance (cells) beyond which a marker that is not top priority starts to fade, and where it bottoms out.</summary>
    private const float FadeNear = 160f, FadeFar = 700f, FadeFloor = 0.4f;
    private const float LineHeight = FontSize + 5f;

    public override void _Draw()
    {
        var font = ThemeDB.FallbackFont;
        var shadow = new Color(0f, 0f, 0f, 0.75f);
        // Labels are placed most important first (then nearest); each takes the first free slot, stacking below (then above) the
        // spot where it would sit, and when every slot is taken only its diamond or arrow is drawn.
        var order = new List<Item>(_items);
        order.Sort((a, b) => a.Priority != b.Priority ? b.Priority.CompareTo(a.Priority) : a.Place.Distance.CompareTo(b.Place.Distance));
        var taken = new List<Rect2>();
        foreach (var item in order)
        {
            var p = item.Place;
            var at = new Vector2(p.X, p.Y);
            float fade = item.Priority >= 3 ? 1f : 1f - (1f - FadeFloor) * Math.Clamp((p.Distance - FadeNear) / (FadeFar - FadeNear), 0f, 1f);
            var color = item.Color with { A = item.Color.A * fade };
            var sh = shadow with { A = shadow.A * fade };
            float w = font.GetStringSize(item.Text, HorizontalAlignment.Left, -1, FontSize).X;
            // The full name and details are for the important marks (priority 2 and up) and the near ones; the rest are an icon with its distance.
            string text = item.Priority >= 2 || item.ShortText.Length == 0 || p.Distance < NearLabel ? item.Text : item.ShortText;
            w = font.GetStringSize(text, HorizontalAlignment.Left, -1, FontSize).X;
            var candidates = new List<Vector2>();
            if (p.OnScreen)
            {
                if (item.Icon.Length > 0) Chip(at, item.Icon, color, sh);
                else
                {
                    DrawPolygon(Diamond(at, 8f), new[] { sh });
                    DrawPolygon(Diamond(at, 6f), new[] { color });
                }
                // The crosshair and the aim card beside it own the middle of the screen: a label that would land on them goes above
                // its diamond, and when that is still in the way it is left out (the diamond stays).
                var keepClear = new Rect2(Size.X / 2f - 60f, Size.Y / 2f - 34f, 540f, 190f);
                bool Hits(Vector2 pos) => new Rect2(pos.X - 2f, pos.Y - FontSize, w + 4f, FontSize + 6f).Intersects(keepClear);
                float x = at.X - w / 2f;
                foreach (float dy in new[] { 24f, 24f + LineHeight, 24f + 2 * LineHeight, -16f, -16f - LineHeight, -16f - 2 * LineHeight })
                    if (!Hits(new Vector2(x, at.Y + dy))) candidates.Add(new Vector2(x, at.Y + dy));
            }
            else
            {
                // Screen y grows down, the placement angle is counter-clockwise with up positive.
                var d = new Vector2(MathF.Cos(p.AngleRadians), -MathF.Sin(p.AngleRadians));
                var n = new Vector2(-d.Y, d.X);
                DrawPolygon(new[] { at + d * 13f, at - d * 7f + n * 9f, at - d * 7f - n * 9f }, new[] { sh });
                DrawPolygon(new[] { at + d * 10f, at - d * 5f + n * 6.5f, at - d * 5f - n * 6.5f }, new[] { color });
                if (item.Icon.Length > 0) Chip(at - d * 24f, item.Icon, color, sh);
                float chipPad = item.Icon.Length > 0 ? 24f : 0f; // the icon chip sits between the arrow and its label
                var basePos = new Vector2(at.X - d.X * (w / 2f + 18f + chipPad) - w / 2f, at.Y - d.Y * (20f + chipPad) + 5f);
                // Edge labels stack along the edge instead of sitting on top of each other.
                foreach (float k in new[] { 0f, 1f, -1f, 2f, -2f, 3f, -3f })
                    candidates.Add(basePos + new Vector2(0f, k * LineHeight));
            }
            Vector2? textPos = null;
            foreach (var c in candidates)
            {
                var pos = new Vector2(Math.Clamp(c.X, 4f, Math.Max(4f, (RightLimit > 0f ? Math.Min(RightLimit, Size.X) : Size.X) - w - 4f)), Math.Clamp(c.Y, FontSize + 2f, Math.Max(FontSize + 2f, Size.Y - 6f)));
                var rect = new Rect2(pos.X - 3f, pos.Y - FontSize, w + 6f, LineHeight - 1f);
                bool free = true;
                foreach (var t in taken) if (t.Intersects(rect)) { free = false; break; }
                if (!free) continue;
                taken.Add(rect);
                textPos = pos;
                break;
            }
            if (textPos is not { } tp) continue; // label left out, the mark stays
            DrawStringOutline(font, tp, text, HorizontalAlignment.Left, -1, FontSize, 4, sh);
            DrawString(font, tp, text, HorizontalAlignment.Left, -1, FontSize, color);
        }
    }

    /// <summary>The icon on a dark round chip with a ring in the mark's colour: readable over bright space.</summary>
    private void Chip(Vector2 at, string icon, Color color, Color shadow)
    {
        DrawCircle(at, 14f, shadow with { A = shadow.A * 0.8f });
        DrawArc(at, 13f, 0f, MathF.Tau, 28, color, 1.5f, true);
        DrawTextureRect(Icons.Get(icon, 18), new Rect2(at - new Vector2(9f, 9f), new Vector2(18f, 18f)), false, color);
    }

    private const float NearLabel = 60f;

    private static Vector2[] Diamond(Vector2 c, float r) => new[] { c + new Vector2(0, -r), c + new Vector2(r, 0), c + new Vector2(0, r), c + new Vector2(-r, 0) };
}
