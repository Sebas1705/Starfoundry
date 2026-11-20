using System;
using Godot;

namespace Starfoundry.Game;

/// <summary>The small ring at the screen centre that shows where the ship's aim is; red while the placement there is invalid.</summary>
public partial class Crosshair : Control
{
    public static readonly Color Normal = new(1f, 1f, 1f, 0.85f);
    public static readonly Color Invalid = new(1f, 0.25f, 0.2f, 0.95f);

    private Color _tint = Normal;

    public Color Tint
    {
        get => _tint;
        set { if (_tint != value) { _tint = value; QueueRedraw(); } }
    }

    public Crosshair()
    {
        CustomMinimumSize = new Vector2(28, 28);
        Size = CustomMinimumSize;
        MouseFilter = MouseFilterEnum.Ignore;
    }

    public override void _Draw()
    {
        var c = Size / 2f;
        DrawArc(c, 9f, 0f, MathF.Tau, 32, new Color(0f, 0f, 0f, 0.6f), 4f, true);
        DrawArc(c, 9f, 0f, MathF.Tau, 32, _tint, 2f, true);
        DrawCircle(c, 1.8f, _tint);
    }
}
