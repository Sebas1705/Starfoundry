using Godot;

namespace Starfoundry.Game;

/// <summary>
/// The bundled fonts (see ASSETS.md and ADR 0037): Inter for body text, Rajdhani SemiBold for titles and headers, JetBrains Mono for
/// numeric readouts. They are read as raw bytes from <c>res://assets/fonts</c> (no import step, so a fresh checkout needs no editor
/// pass); when a file is missing the engine's default font is used and nothing throws.
/// </summary>
public static class UiFonts
{
    private static Font? _body, _display, _mono;
    private static bool _loaded;

    /// <summary>Body text (Inter), or null for the engine default.</summary>
    public static Font? Body { get { Load(); return _body; } }

    /// <summary>Titles and card headers (Rajdhani SemiBold), or null for the engine default.</summary>
    public static Font? Display { get { Load(); return _display; } }

    /// <summary>Numbers and gauges (JetBrains Mono), or null for the engine default.</summary>
    public static Font? Mono { get { Load(); return _mono; } }

    private static void Load()
    {
        if (_loaded) return;
        _loaded = true;
        _body = Read("res://assets/fonts/inter/Inter-Variable.ttf");
        _display = Read("res://assets/fonts/rajdhani/Rajdhani-SemiBold.ttf");
        _mono = Read("res://assets/fonts/jetbrains-mono/JetBrainsMono-Regular.ttf");
    }

    private static Font? Read(string path)
    {
        byte[] data = FileAccess.GetFileAsBytes(path);
        if (data == null || data.Length == 0) return null;
        var f = new FontFile { Data = data, Antialiasing = TextServer.FontAntialiasing.Gray, SubpixelPositioning = TextServer.SubpixelPositioning.Auto };
        f.GenerateMipmaps = false;
        return f;
    }

    /// <summary>Gives a label the display face (titles) when the font is present.</summary>
    public static T Title<T>(T c) where T : Control { if (Display is { } f) c.AddThemeFontOverride("font", f); return c; }

    /// <summary>Gives a label the monospaced face (numbers) when the font is present.</summary>
    public static T Num<T>(T c) where T : Control { if (Mono is { } f) c.AddThemeFontOverride("font", f); return c; }
}
