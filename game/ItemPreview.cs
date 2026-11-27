using System.Collections.Generic;
using Godot;

namespace Starfoundry.Game;

/// <summary>
/// A small turning 3D view of an item (a little pile of its own model), used in the inventory tooltip so the item can be judged in the round, in
/// its material. It has its own world; the piles are built the first time an item is asked for and kept.
/// </summary>
public partial class ItemPreview : SubViewportContainer
{
    private readonly SubViewport _view;
    private readonly Node3D _pivot = new() { Name = "Pivot" };
    private readonly Camera3D _camera = new() { Fov = 34f };
    private readonly Dictionary<string, Node3D> _piles = new();
    private string? _shown;

    public ItemPreview(int size = 96)
    {
        Stretch = true;
        CustomMinimumSize = new Vector2(size, size);
        MouseFilter = MouseFilterEnum.Ignore;
        _view = new SubViewport { OwnWorld3D = true, TransparentBg = true, Msaa3D = Viewport.Msaa.Msaa2X, Size = new Vector2I(size, size), RenderTargetUpdateMode = SubViewport.UpdateMode.WhenVisible };
        AddChild(_view);
        _view.AddChild(new WorldEnvironment
        {
            Environment = new Godot.Environment
            {
                BackgroundMode = Godot.Environment.BGMode.Color,
                BackgroundColor = new Color(0f, 0f, 0f, 0f),
                AmbientLightSource = Godot.Environment.AmbientSource.Color,
                AmbientLightColor = new Color(0.55f, 0.6f, 0.75f),
                AmbientLightEnergy = 0.9f,
            },
        });
        _view.AddChild(new DirectionalLight3D { LightEnergy = 1.5f, RotationDegrees = new Vector3(-40, 35, 0) });
        _view.AddChild(_pivot);
        _camera.Transform = new Transform3D(Basis.Identity, new Vector3(0f, 2.3f, 4.7f)).LookingAt(new Vector3(0f, 0.25f, 0f), Vector3.Up);
        _view.AddChild(_camera);
    }

    /// <summary>Shows the item with this content id (a pile of four).</summary>
    public void Show(string itemId)
    {
        if (_shown == itemId) return;
        if (_shown is not null && _piles.TryGetValue(_shown, out var old)) old.Visible = false;
        if (!_piles.TryGetValue(itemId, out var pile))
        {
            pile = ItemPile.Build(itemId, 11, 4, 1f);
            _pivot.AddChild(pile);
            _piles[itemId] = pile;
        }
        pile.Visible = true;
        _shown = itemId;
    }

    public override void _Process(double delta)
    {
        if (_shown is not null && IsVisibleInTree()) _pivot.RotateY((float)delta * 0.8f);
    }
}
