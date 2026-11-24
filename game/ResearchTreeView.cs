using System;
using System.Collections.Generic;
using System.Linq;
using Godot;
using Starfoundry.Sim;
using Starfoundry.Ui;

namespace Starfoundry.Game;

/// <summary>
/// One research node of the tree: a round token with the node's icon in its field's colour, a progress ring, a padlock when locked and a tick
/// when done. It is a button, so the arrows move the focus between nodes and a click or Enter reaches the panel; the focus and the selection are
/// drawn as rings.
/// </summary>
public partial class ResearchNode : Button
{
    public const float Diameter = 46f;

    public ResearchEntry Entry { get; set; } = null!;
    public bool IsSelected { get; set; }
    private bool _hover;

    public ResearchNode()
    {
        CustomMinimumSize = new Vector2(Diameter, Diameter);
        Flat = true;
        FocusMode = FocusModeEnum.All;
        var empty = new StyleBoxEmpty();
        foreach (var s in new[] { "normal", "hover", "pressed", "hover_pressed", "focus", "disabled" }) AddThemeStyleboxOverride(s, empty);
        MouseEntered += () => { _hover = true; QueueRedraw(); };
        MouseExited += () => { _hover = false; QueueRedraw(); };
        FocusEntered += QueueRedraw;
        FocusExited += QueueRedraw;
    }

    public override void _Draw()
    {
        var e = Entry;
        if (e is null) return;
        var field = Icons.FieldTint(e.Field);
        bool locked = e.State == ResearchState.Locked, done = e.State == ResearchState.Done, active = e.State == ResearchState.Researching;
        var c = new Vector2(Diameter, Diameter) / 2f;
        float r = Diameter / 2f - 1.5f;

        var fill = done ? field.Darkened(0.78f) with { A = 0.95f } : new Color(0.06f, 0.08f, 0.13f, 0.96f);
        if (_hover || HasFocus()) fill = fill.Lightened(0.08f);
        DrawCircle(c, r, fill);
        var ring = locked ? new Color(0.45f, 0.5f, 0.62f, 0.45f) : done ? field with { A = 0.9f } : active ? UiTheme.Gold : field with { A = 0.85f };
        DrawArc(c, r, 0f, MathF.Tau, 56, ring with { A = ring.A * (done ? 1f : 0.55f) }, 2f, true);
        if (e.Progress > 0.001 && !done)
            DrawArc(c, r, -MathF.PI / 2f, -MathF.PI / 2f + MathF.Tau * (float)e.Progress, 56, active ? UiTheme.Gold : field, 3.5f, true);

        var tint = locked ? new Color(0.5f, 0.55f, 0.66f) : field;
        DrawTextureRect(Icons.Get(IconCatalog.ForResearch(e.Id), 26), new Rect2(c - new Vector2(13f, 13f), new Vector2(26f, 26f)), false, tint);

        if (locked) Badge(new Vector2(Diameter - 8f, Diameter - 8f), "status:locked", UiTheme.Warn);
        else if (done) Badge(new Vector2(Diameter - 8f, Diameter - 8f), "status:check", UiTheme.Good);
        if (e.Repeatable && e.Level > 0 && !done)
        {
            var font = ThemeDB.FallbackFont;
            DrawString(font, new Vector2(1f, Diameter - 3f), e.Level.ToString(), HorizontalAlignment.Left, -1, 11, UiTheme.Text);
        }

        if (IsSelected) DrawArc(c, r + 3f, 0f, MathF.Tau, 56, new Color(1f, 1f, 1f, 0.55f), 1.5f, true);
        if (HasFocus()) DrawArc(c, r + 3.5f, 0f, MathF.Tau, 56, UiTheme.Gold, 2.5f, true);
    }

    private void Badge(Vector2 at, string icon, Color tint)
    {
        DrawCircle(at, 8f, new Color(0.03f, 0.04f, 0.07f, 0.95f));
        DrawArc(at, 7.5f, 0f, MathF.Tau, 20, tint with { A = 0.9f }, 1.2f, true);
        DrawTextureRect(Icons.Get(icon, 11), new Rect2(at - new Vector2(5.5f, 5.5f), new Vector2(11f, 11f)), false, tint);
    }
}

/// <summary>
/// The research tree: a band per science field with the nodes in columns by depth (<see cref="ResearchTreeLayout"/>) and a line from each node to
/// what it requires (bright once that is done). It sizes itself to its content so a scroll container around it scrolls both ways.
/// </summary>
public partial class ResearchTreeView : Control
{
    private const float Pitch = 58f, RowPitch = 52f, HeaderHeight = 22f, BandGap = 6f, Pad = 8f; // rows are closer than columns so the four-row basic band fits the panel

    private readonly Dictionary<int, ResearchNode> _nodes = new();
    private List<TreeSlot> _slots = new();
    private IReadOnlyList<ResearchEntry> _entries = Array.Empty<ResearchEntry>();
    private readonly List<(float Y, ResearchField Field)> _bands = new();
    private string _signature = "";
    private int _selected = -1;

    /// <summary>A node took the focus or the mouse.</summary>
    public event Action<int>? NodeFocused;

    /// <summary>A node was clicked.</summary>
    public event Action<int>? NodeClicked;

    public ResearchTreeView()
    {
        MouseFilter = MouseFilterEnum.Pass;
    }

    public ResearchNode? Node(int index) => _nodes.GetValueOrDefault(index);

    public IEnumerable<int> Indices => _nodes.Keys;

    /// <summary>Shows the entries: rebuilds the layout when the node set or the language changed, otherwise only refreshes states and progress.</summary>
    public void Set(IReadOnlyList<ResearchEntry> entries)
    {
        _entries = entries;
        string signature = string.Join(",", entries.Select(e => e.Index)) + Loc.Lang;
        if (signature != _signature)
        {
            _signature = signature;
            Rebuild();
        }
        foreach (var e in entries)
        {
            if (!_nodes.TryGetValue(e.Index, out var n)) continue;
            n.Entry = e;
            n.IsSelected = e.Index == _selected;
            n.TooltipText = ResearchView.Name(e) + "\n" + ResearchView.StateName(e.State) + (e.State == ResearchState.Locked ? "  -  " + ResearchView.LockedReason(e) : "");
            n.QueueRedraw();
        }
        QueueRedraw();
    }

    public void SetSelected(int index)
    {
        _selected = index;
        foreach (var (i, n) in _nodes) { n.IsSelected = i == index; n.QueueRedraw(); }
    }

    private void Rebuild()
    {
        foreach (var n in _nodes.Values) { RemoveChild(n); n.QueueFree(); }
        _nodes.Clear();
        _bands.Clear();
        _slots = ResearchTreeLayout.Layout(_entries).ToList();
        var rows = ResearchTreeLayout.BandRows(_slots);
        float y = Pad;
        var bandTop = new List<float>();
        for (int b = 0; b < rows.Count; b++)
        {
            bandTop.Add(y);
            var field = _slots.First(s => s.Band == b).Field;
            _bands.Add((y, field));
            y += HeaderHeight + rows[b] * RowPitch + BandGap;
        }
        foreach (var s in _slots)
        {
            var entry = _entries.First(e => e.Index == s.Index);
            var node = new ResearchNode { Entry = entry, Position = new Vector2(Pad + s.Column * Pitch, bandTop[s.Band] + HeaderHeight + s.Row * RowPitch + 3f), Size = new Vector2(ResearchNode.Diameter, ResearchNode.Diameter) };
            int index = s.Index;
            node.FocusEntered += () => NodeFocused?.Invoke(index);
            node.MouseEntered += () => NodeFocused?.Invoke(index);
            node.Pressed += () => NodeClicked?.Invoke(index);
            AddChild(node);
            _nodes[s.Index] = node;
        }
        CustomMinimumSize = new Vector2(Pad * 2 + ResearchTreeLayout.Columns(_slots) * Pitch, y);
    }

    public override void _Draw()
    {
        var font = ThemeDB.FallbackFont;
        var byIndex = _entries.ToDictionary(e => e.Index);
        float width = Math.Max(Size.X, CustomMinimumSize.X);
        foreach (var (top, field) in _bands)
        {
            var tint = Icons.FieldTint(field);
            DrawTextureRect(Icons.Get(IconCatalog.ForField(field), 16), new Rect2(Pad, top + 3f, 16f, 16f), false, tint);
            DrawString(font, new Vector2(Pad + 22f, top + 16f), ResearchView.FieldName(field).ToUpperInvariant(), HorizontalAlignment.Left, -1, 12, tint);
            DrawLine(new Vector2(Pad, top + HeaderHeight - 2f), new Vector2(width - Pad, top + HeaderHeight - 2f), tint with { A = 0.25f }, 1f);
        }
        var slotOf = _slots.ToDictionary(s => s.Index);
        foreach (var s in _slots)
        {
            if (!byIndex.TryGetValue(s.Index, out var e) || !_nodes.TryGetValue(s.Index, out var to)) continue;
            foreach (var req in e.Requires)
            {
                if (!_nodes.TryGetValue(req.Index, out var from)) continue;
                var tint = Icons.FieldTint(byIndex.TryGetValue(req.Index, out var re) ? re.Field : e.Field);
                var a = from.Position + new Vector2(ResearchNode.Diameter, ResearchNode.Diameter / 2f);
                var b = to.Position + new Vector2(0f, ResearchNode.Diameter / 2f);
                float mid = b.X - (Pitch - ResearchNode.Diameter) / 2f;
                var colour = req.Done ? tint with { A = 0.85f } : tint with { A = 0.28f };
                DrawPolyline(new[] { a, new Vector2(mid, a.Y), new Vector2(mid, b.Y), b }, colour, req.Done ? 2.2f : 1.5f, true);
            }
        }
    }
}
