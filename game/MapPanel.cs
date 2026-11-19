using System;
using Godot;
using Starfoundry.Sim;

namespace Starfoundry.Game;

/// <summary>A flat star map: known systems as dots, lanes between known systems as lines, with the ship's system and the selection marked.</summary>
public partial class MapPanel : Control
{
    private Galaxy? _galaxy;
    private Func<int, bool> _known = _ => false;
    private Func<int, bool> _visited = _ => false;
    private int _current = -1;
    private int _selected = -1;

    public MapPanel()
    {
        CustomMinimumSize = new Vector2(320, 320);
        Size = CustomMinimumSize;
    }

    public void Show(Galaxy galaxy, Func<int, bool> known, Func<int, bool> visited, int current, int selected)
    {
        _galaxy = galaxy;
        _known = known;
        _visited = visited;
        _current = current;
        _selected = selected;
        QueueRedraw();
    }

    public override void _Draw()
    {
        DrawRect(new Rect2(Vector2.Zero, Size), new Color(0.02f, 0.03f, 0.06f, 0.82f));
        DrawRect(new Rect2(Vector2.Zero, Size), new Color(0.4f, 0.5f, 0.7f, 0.6f), filled: false, width: 1f);
        if (_galaxy is null) return;

        float extent = 0;
        foreach (var s in _galaxy.Systems) extent = Mathf.Max(extent, Mathf.Max(Math.Abs(s.X), Math.Abs(s.Z)));
        extent = Mathf.Max(extent, 1f) * 1.15f;
        Vector2 ToPanel(StarSystem s) => Size / 2f + new Vector2(s.X, s.Z) / extent * (Size / 2f);

        foreach (var s in _galaxy.Systems)
        {
            if (!_known(s.Id)) continue;
            foreach (int other in s.Lanes)
            {
                if (other < s.Id || !_known(other)) continue;
                bool traveled = _visited(s.Id) || _visited(other);
                DrawLine(ToPanel(s), ToPanel(_galaxy.Systems[other]), traveled ? new Color(0.5f, 0.7f, 1f, 0.8f) : new Color(0.4f, 0.4f, 0.5f, 0.35f), 1.5f);
            }
        }

        foreach (var s in _galaxy.Systems)
        {
            if (!_known(s.Id)) continue;
            var p = ToPanel(s);
            var color = s.Id == 0 ? new Color(0.4f, 1f, 0.6f) : _visited(s.Id) ? new Color(0.95f, 0.85f, 0.4f) : new Color(0.6f, 0.65f, 0.8f);
            DrawCircle(p, _visited(s.Id) ? 5f : 3.5f, color);
            if (s.Id == _current) DrawArc(p, 9f, 0, Mathf.Tau, 24, Colors.White, 1.5f);
            if (s.Id == _selected) DrawArc(p, 12f, 0, Mathf.Tau, 24, new Color(1f, 0.5f, 0.2f), 2f);
        }
    }
}
