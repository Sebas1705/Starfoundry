using Godot;
using Starfoundry.Ui;

namespace Starfoundry.Game;

/// <summary>
/// A small orientation cue for free flight: the three axes of the fixed galactic frame (X, Y, Z of the galaxy coordinates, the same ones the
/// star map uses) as the ship sees them. It is not a horizon and not an "up": it never moves by itself, and it is read against the ship's
/// attitude. An axis pointing away from the pilot is drawn faint, one pointing at the pilot is drawn bright; the tip of an axis shows
/// where that galactic direction lies on the screen. The maths is <see cref="NavFrame.Project"/>.
/// </summary>
public partial class NavGizmo : Control
{
    private static readonly Color[] AxisColors = { new(1f, 0.45f, 0.4f), new(0.55f, 0.95f, 0.5f), new(0.5f, 0.7f, 1f) };
    private GizmoAxis[] _axes = System.Array.Empty<GizmoAxis>();

    public NavGizmo()
    {
        CustomMinimumSize = new Vector2(66f, 66f);
        MouseFilter = MouseFilterEnum.Ignore;
        TooltipText = "";
    }

    public void Set(GizmoAxis[] axes)
    {
        _axes = axes;
        QueueRedraw();
    }

    public override void _Draw()
    {
        if (_axes.Length < 3) return; // not set yet
        var c = Size / 2f;
        float r = Mathf.Min(Size.X, Size.Y) / 2f - 9f;
        DrawArc(c, r, 0f, Mathf.Tau, 40, new Color(0.5f, 0.6f, 0.8f, 0.28f), 1f);
        DrawCircle(c, 1.6f, new Color(0.8f, 0.9f, 1f, 0.7f));
        var font = ThemeDB.FallbackFont;
        // Far axes first so the near ones are drawn over them.
        var order = new System.Collections.Generic.List<int> { 0, 1, 2 };
        order.Sort((a, b) => _axes[b].Depth.CompareTo(_axes[a].Depth));
        foreach (int i in order)
        {
            if (i >= _axes.Length) continue;
            var a = _axes[i];
            var tip = c + new Vector2(a.X, -a.Y) * r;
            float alpha = a.Receding ? 0.45f : 1f;
            var col = AxisColors[i] with { A = alpha };
            DrawLine(c, tip, col, a.Receding ? 1.5f : 2.5f, true);
            DrawCircle(tip, a.Receding ? 2.4f : 3.6f, col);
            DrawString(font, tip + new Vector2(5f, 4f), a.Label, HorizontalAlignment.Left, -1, 11, col);
        }
    }
}
