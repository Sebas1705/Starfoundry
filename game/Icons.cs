using System;
using System.Collections.Generic;
using Godot;
using Starfoundry.Ui;

namespace Starfoundry.Game;

/// <summary>
/// The icon set as textures. The drawings are the vector glyphs of <see cref="IconGlyphs"/>, rasterised on demand with Godot's SVG loader at
/// the pixel size the screen needs (the logical size times the window's scale, at least 2) and cached, so an icon is crisp at 720p and at
/// 1080p and above. Icons are white: tint them with <see cref="TintOf"/> or any colour. An id nobody registered gets the generic glyph of its
/// kind and one log line. No external art: everything is code.
/// </summary>
public static class Icons
{
    private static readonly Dictionary<(string Glyph, int Pixels), ImageTexture> Cache = new();
    private static bool _hooked;

    /// <summary>Pixels per logical pixel when rasterising: 2 up to a 2560 wide window, 3 above (the canvas stretches with the window).</summary>
    public static int PixelScale
    {
        get
        {
            int w = DisplayServer.WindowGetSize().X;
            return w <= 0 ? 2 : Math.Max(2, (int)Math.Ceiling(w / 1280.0));
        }
    }

    private static void Hook()
    {
        if (_hooked) return;
        _hooked = true;
        IconCatalog.FellBack += id => GD.Print($"ICON no icon registered for '{id}', using the generic one of its kind");
    }

    /// <summary>The white texture of an icon, drawn for a logical size of <paramref name="size"/> pixels.</summary>
    public static Texture2D Get(string id, int size = 24)
    {
        Hook();
        var def = IconCatalog.Resolve(id, out _);
        int px = Math.Clamp(size * PixelScale, 8, 512);
        if (Cache.TryGetValue((def.Glyph, px), out var tex)) return tex;
        string svg = IconGlyphs.Document(def.Glyph, px);
        var img = new Image();
        var err = img.LoadSvgFromString(svg);
        if (err != Error.Ok) GD.PrintErr($"ICON could not rasterise {def.Glyph}: {err}");
        tex = ImageTexture.CreateFromImage(img);
        Cache[(def.Glyph, px)] = tex;
        return tex;
    }

    /// <summary>The material or field colour of an icon, or <paramref name="fallback"/> (white by default) when the caller colours it.</summary>
    public static Color TintOf(string id, Color? fallback = null)
    {
        var def = IconCatalog.Resolve(id, out _);
        if (def.Tint == 0) return fallback ?? Colors.White;
        return new Color(((def.Tint >> 16) & 0xFF) / 255f, ((def.Tint >> 8) & 0xFF) / 255f, (def.Tint & 0xFF) / 255f);
    }

    /// <summary>The icon's name in the current language (tooltips).</summary>
    public static string Name(string id) => IconCatalog.Name(id);

    public static string ItemId(string contentId) => IconCatalog.ForItem(contentId);

    /// <summary>The tint of a research node's icon (the colour of its field).</summary>
    public static Color FieldTint(Sim.ResearchField field)
    {
        uint t = IconCatalog.ResearchTint(field);
        return new Color(((t >> 16) & 0xFF) / 255f, ((t >> 8) & 0xFF) / 255f, (t & 0xFF) / 255f);
    }
}
