using System;
using System.Collections.Generic;
using Godot;
using Starfoundry.Sim;

namespace Starfoundry.Game;

/// <summary>
/// The star map, in three dimensions: known systems as dots at their real X, Y, Z, lanes between them, the ship's system, the selected
/// destination and the way the ship's nose points. It is an orbit view of the galaxy that you can rotate and inspect (drag with the mouse,
/// Left and Right arrows, Home to reset, the wheel zooms) and click a system on to select it. The map has no horizon: the orbit turns about the
/// centre of the known systems, and the three axes drawn in the corner are the fixed galactic frame (the same ones the flight gizmo shows).
/// </summary>
public partial class MapPanel : Control
{
    private Galaxy? _galaxy;
    private Func<int, bool> _known = _ => false;
    private Func<int, bool> _visited = _ => false;
    private int _current = -1;
    private int _selected = -1;
    private System.Numerics.Vector3 _shipForward = -System.Numerics.Vector3.UnitZ;

    // The orbit view (radians) and the zoom; the defaults show the three axes apart from each other.
    private const float DefaultYaw = 0.55f, DefaultPitch = 0.45f;
    private float _yaw = DefaultYaw, _pitch = DefaultPitch, _zoom = 1f;
    private bool _dragging;
    private float _dragged;

    private readonly List<(int Id, Vector2 At)> _hits = new();

    /// <summary>Raised when a system dot is clicked.</summary>
    public event Action<int>? SystemPicked;

    public MapPanel()
    {
        CustomMinimumSize = new Vector2(320, 320);
        Size = CustomMinimumSize;
        MouseFilter = MouseFilterEnum.Stop;
        ClipContents = true;
    }

    public void Show(Galaxy galaxy, Func<int, bool> known, Func<int, bool> visited, int current, int selected, System.Numerics.Vector3 shipForward)
    {
        _galaxy = galaxy;
        _known = known;
        _visited = visited;
        _current = current;
        _selected = selected;
        _shipForward = shipForward;
        QueueRedraw();
    }

    /// <summary>Turns the orbit view (used by the arrow keys); the pitch is limited so the view never flips over the pole of the orbit itself.</summary>
    public void Orbit(float yaw, float pitch)
    {
        _yaw += yaw;
        _pitch = Mathf.Clamp(_pitch + pitch, -1.45f, 1.45f);
        QueueRedraw();
    }

    public void ResetView()
    {
        _yaw = DefaultYaw;
        _pitch = DefaultPitch;
        _zoom = 1f;
        QueueRedraw();
    }

    public override void _GuiInput(InputEvent @event)
    {
        switch (@event)
        {
            case InputEventMouseButton { ButtonIndex: MouseButton.Left } mb:
                _dragging = mb.Pressed;
                if (mb.Pressed) _dragged = 0f;
                else if (_dragged < 4f && PickAt(mb.Position) is { } id) SystemPicked?.Invoke(id);
                AcceptEvent();
                break;
            case InputEventMouseButton { ButtonIndex: MouseButton.WheelUp, Pressed: true }:
                _zoom = Mathf.Clamp(_zoom * 1.12f, 0.5f, 4f); QueueRedraw(); AcceptEvent(); break;
            case InputEventMouseButton { ButtonIndex: MouseButton.WheelDown, Pressed: true }:
                _zoom = Mathf.Clamp(_zoom / 1.12f, 0.5f, 4f); QueueRedraw(); AcceptEvent(); break;
            case InputEventMouseMotion mm when _dragging:
                _dragged += mm.Relative.Length();
                Orbit(-mm.Relative.X * 0.012f, mm.Relative.Y * 0.012f);
                AcceptEvent();
                break;
        }
    }

    private int? PickAt(Vector2 at)
    {
        int? best = null;
        float bestD = 14f * 14f;
        foreach (var (id, p) in _hits)
        {
            float d = (p - at).LengthSquared();
            if (d < bestD) { bestD = d; best = id; }
        }
        return best;
    }

    /// <summary>The orbit rotation: yaw about the galactic Y axis, then pitch about the view's X axis.</summary>
    private Basis ViewBasis() => new Basis(Vector3.Right, _pitch) * new Basis(Vector3.Up, _yaw);

    public override void _Draw()
    {
        DrawRect(new Rect2(Vector2.Zero, Size), new Color(0.02f, 0.03f, 0.06f, 0.9f));
        DrawRect(new Rect2(Vector2.Zero, Size), new Color(0.4f, 0.5f, 0.7f, 0.6f), filled: false, width: 1f);
        _hits.Clear();
        if (_galaxy is null) return;
        var font = ThemeDB.FallbackFont;
        var view = ViewBasis();

        // The centre and radius of what is known: the orbit turns about the centre, and the radius sets the scale.
        var centre = Vector3.Zero;
        int known = 0;
        foreach (var s in _galaxy.Systems)
        {
            if (!_known(s.Id)) continue;
            centre += new Vector3(s.X, s.Y, s.Z);
            known++;
        }
        if (known > 0) centre /= known;
        float radius = 1f;
        foreach (var s in _galaxy.Systems)
            if (_known(s.Id)) radius = Mathf.Max(radius, (new Vector3(s.X, s.Y, s.Z) - centre).Length());
        radius *= 1.15f;
        float scale = Mathf.Min(Size.X, Size.Y) / 2f / radius * _zoom;

        // A system's place on the panel and its depth (positive = toward the viewer), with a mild perspective for the depth.
        (Vector2 At, float Depth, float Size) Project(Vector3 galactic)
        {
            var v = view * (galactic - centre);
            float depth = v.Z / radius; // about -1 to 1
            float k = 1f + depth * 0.22f;
            return (Size / 2f + new Vector2(v.X, -v.Y) * scale * k, depth, k);
        }

        // Lanes first, fainter when they are far from the viewer.
        foreach (var s in _galaxy.Systems)
        {
            if (!_known(s.Id)) continue;
            foreach (int other in s.Lanes)
            {
                if (other < s.Id || !_known(other)) continue;
                var o = _galaxy.Systems[other];
                var a = Project(new Vector3(s.X, s.Y, s.Z));
                var b = Project(new Vector3(o.X, o.Y, o.Z));
                float fade = Mathf.Clamp(0.65f - (a.Depth + b.Depth) * 0.25f, 0.25f, 1f);
                bool traveled = _visited(s.Id) || _visited(other);
                DrawLine(a.At, b.At, traveled ? new Color(0.5f, 0.7f, 1f, 0.8f * fade) : new Color(0.4f, 0.4f, 0.5f, 0.35f * fade), 1.5f, true);
            }
        }

        // The route to the selected destination and the ship's nose, both from the ship's system.
        if (_current >= 0 && _current < _galaxy.Systems.Count)
        {
            var cur = _galaxy.Systems[_current];
            var from = Project(new Vector3(cur.X, cur.Y, cur.Z));
            if (_selected >= 0 && _selected != _current && _selected < _galaxy.Systems.Count && _known(_selected))
            {
                var sel = _galaxy.Systems[_selected];
                DrawDashedLine(from.At, Project(new Vector3(sel.X, sel.Y, sel.Z)).At, new Color(1f, 0.55f, 0.25f, 0.9f), 1.5f, 5f);
            }
            var nose = Project(new Vector3(cur.X, cur.Y, cur.Z) + new Vector3(_shipForward.X, _shipForward.Y, _shipForward.Z) * radius * 0.35f).At;
            DrawLine(from.At, nose, new Color(0.45f, 0.9f, 1f, 0.95f), 2f, true);
            var back = (from.At - nose).Normalized();
            var side = new Vector2(-back.Y, back.X);
            DrawPolygon(new[] { nose, nose + back * 9f + side * 4.5f, nose + back * 9f - side * 4.5f }, new[] { new Color(0.45f, 0.9f, 1f) });
        }

        // Systems, far ones first so the near ones draw over them.
        var order = new List<(int Id, Vector2 At, float Depth, float K)>();
        foreach (var s in _galaxy.Systems)
        {
            if (!_known(s.Id)) continue;
            var p = Project(new Vector3(s.X, s.Y, s.Z));
            order.Add((s.Id, p.At, p.Depth, p.Size));
        }
        order.Sort((x, y) => x.Depth.CompareTo(y.Depth));
        foreach (var (id, at, depth, k) in order)
        {
            var s = _galaxy.Systems[id];
            var color = id == 0 ? new Color(0.4f, 1f, 0.6f) : _visited(id) ? new Color(0.95f, 0.85f, 0.4f) : new Color(0.6f, 0.65f, 0.8f);
            color = color.Lerp(new Color(0.02f, 0.03f, 0.06f), Mathf.Clamp(0.35f - depth * 0.3f, 0f, 0.6f)); // far systems sink into the dark
            DrawCircle(at, (_visited(id) ? 5f : 3.5f) * k, color);
            if (id == _current) DrawArc(at, 9f * k, 0, Mathf.Tau, 24, Colors.White, 1.5f);
            if (id == _selected) DrawArc(at, 12f * k, 0, Mathf.Tau, 24, new Color(1f, 0.5f, 0.2f), 2f);
            _hits.Add((id, at));
        }

        // Names: the ship's system and the chosen one always; the rest only where there is room, near ones first, fainter with depth.
        var taken = new List<Rect2>();
        float NameWidth(string t, int size) => font.GetStringSize(t, HorizontalAlignment.Left, -1, size).X;
        var labelOrder = new List<(int Id, Vector2 At, float Depth, float K)>(order);
        labelOrder.Sort((x, y) =>
        {
            int Rank(int id) => id == _selected ? 0 : id == _current ? 1 : 2;
            return Rank(x.Id) != Rank(y.Id) ? Rank(x.Id).CompareTo(Rank(y.Id)) : y.Depth.CompareTo(x.Depth);
        });
        foreach (var (id, at, depth, k) in labelOrder)
        {
            bool key = id == _current || id == _selected;
            int fontSize = key ? 12 : 11;
            string name = _galaxy.Systems[id].Name;
            float w = NameWidth(name, fontSize);
            var pos = at + new Vector2(13f * k, -9f * k);
            if (pos.X + w > Size.X - 4f) pos.X = at.X - 13f * k - w; // flip to the left near the right edge
            var rect = new Rect2(pos.X - 2f, pos.Y - fontSize, w + 4f, fontSize + 4f);
            bool free = true;
            foreach (var t in taken) if (t.Intersects(rect)) { free = false; break; }
            if (!free && !key) continue;
            taken.Add(rect);
            float a = key ? 1f : Mathf.Clamp(0.85f - depth * 0.35f, 0.4f, 0.9f);
            var c = id == _selected ? new Color(1f, 0.7f, 0.4f) : id == _current ? Colors.White : new Color(0.75f, 0.8f, 0.95f, a);
            DrawString(font, pos + new Vector2(1f, 1f), name, HorizontalAlignment.Left, -1, fontSize, new Color(0f, 0f, 0f, 0.7f * a));
            DrawString(font, pos, name, HorizontalAlignment.Left, -1, fontSize, c);
        }

        // The fixed galactic frame in the corner: the same axes as the flight gizmo, turned with the orbit.
        var corner = new Vector2(30f, Size.Y - 30f);
        string[] names = { "X", "Y", "Z" };
        Color[] cols = { new(1f, 0.45f, 0.4f), new(0.55f, 0.95f, 0.5f), new(0.5f, 0.7f, 1f) };
        Vector3[] axes = { Vector3.Right, Vector3.Up, Vector3.Back };
        for (int i = 0; i < 3; i++)
        {
            var v = view * axes[i];
            var tip = corner + new Vector2(v.X, -v.Y) * 20f;
            var c = cols[i] with { A = v.Z < 0f ? 0.5f : 1f };
            DrawLine(corner, tip, c, 2f, true);
            DrawString(font, tip + new Vector2(3f, 4f), names[i], HorizontalAlignment.Left, -1, 10, c);
        }
    }
}
