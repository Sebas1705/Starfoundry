using System.Numerics;
using System.Text.Json;

namespace Starfoundry.Ui;

/// <summary>
/// A screen of the console on its deck. <c>X</c> and <c>Z</c> are the position of its centre on the deck, <c>Width</c> and <c>Depth</c> its size (deck cells,
/// before the ship's squeeze), <c>PxW</c> and <c>PxH</c> the size of the texture drawn into it and <c>TiltDegrees</c> how far it is turned up from the deck
/// towards the pilot's eye about its near edge (0 lies flat on the deck).
/// </summary>
public readonly record struct ScreenRect(float X, float Z, float Width, float Depth, int PxW, int PxH, float TiltDegrees);

/// <summary>
/// Where the displays of one cockpit style sit on its console, in the local frame of the cockpit module (content data: <c>content/cockpit_layouts.json</c>).
/// The console deck is a plate tilted <see cref="DeckTiltDegrees"/> about X with its origin at <see cref="DeckOrigin"/>; a ship squeezes the whole console by
/// <see cref="Squeeze"/> about <see cref="SqueezeAboutX"/> (the same numbers as the console model, <c>ModuleModels.Cockpit</c>). The three screens are the
/// <see cref="CockpitScreen"/> slots, left to right. A style with another console (a wide desk with one big navigation screen) is another entry of the file.
/// </summary>
public sealed record CockpitLayout(
    string Id, Vector3 DeckOrigin, float DeckTiltDegrees, Vector3 Squeeze, float SqueezeAboutX,
    float SurfaceHeight, float BezelHeight, float BezelMargin, IReadOnlyList<ScreenRect> Screens)
{
    public ScreenRect Of(CockpitScreen screen) => Screens[(int)screen];

    /// <summary>The area of a screen on the deck, in square deck cells.</summary>
    public float AreaOf(CockpitScreen screen) => Of(screen).Width * Of(screen).Depth;

    /// <summary>
    /// How tall, in pixels of a view <paramref name="viewHeightPx"/> high with a vertical field of view of <paramref name="fovDegrees"/>, a glyph that is
    /// <paramref name="fontPx"/> texture pixels high appears on <paramref name="screen"/> when the eye at <paramref name="eyeModule"/> (module-local) looks at the
    /// middle of it. The screen's width as seen from the eye over its texture width is the scale; the foreshortening of a tilted screen only shrinks its height,
    /// not its width, so this is the legibility of a line of text along the screen.
    /// </summary>
    public float TextPixels(CockpitScreen screen, float fontPx, Vector3 eyeModule, float viewHeightPx, float fovDegrees)
    {
        var d = Of(screen);
        var (left, right, _, _) = Corners(screen);
        var centre = (left + right) * 0.5f;
        float dist = Math.Max(0.1f, (centre - eyeModule).Length());
        var axis = Vector3.Normalize(right - left);
        var toEye = Vector3.Normalize(eyeModule - centre);
        float across = (right - left).Length() * MathF.Sqrt(Math.Max(0.0001f, 1f - MathF.Pow(Vector3.Dot(axis, -toEye), 2f))); // the width as seen from the eye
        float focal = viewHeightPx * 0.5f / MathF.Tan(fovDegrees * MathF.PI / 360f);
        float scale = across / dist * focal / d.PxW;
        return fontPx * scale;
    }

    /// <summary>The middle of the left and right edges and of the top and bottom edges of a screen, in module coordinates.</summary>
    public (Vector3 Left, Vector3 Right, Vector3 Top, Vector3 Bottom) Corners(CockpitScreen screen)
    {
        var d = Of(screen);
        var m = ScreenToModule(d);
        Vector3 P(float x, float y) => Vector3.Transform(new Vector3(x, y, 0f), m);
        return (P(-d.Width / 2f, 0f), P(d.Width / 2f, 0f), P(0f, d.Depth / 2f), P(0f, -d.Depth / 2f));
    }

    /// <summary>The deck frame in the cockpit module's frame: the tilt, then the squeeze about the middle of the module.</summary>
    public Matrix4x4 DeckToModule
    {
        get
        {
            var rot = Matrix4x4.CreateRotationX(DeckTiltDegrees * MathF.PI / 180f);
            var scale = Matrix4x4.CreateScale(Squeeze);
            var origin = Squeeze * DeckOrigin + new Vector3(SqueezeAboutX * (1f - Squeeze.X), 0f, 0f);
            return rot * scale * Matrix4x4.CreateTranslation(origin); // row-vector convention: rotate first, then scale, then move
        }
    }

    /// <summary>
    /// The frame of a screen quad on the module: X to the pilot's right, Y up the screen (away from the pilot), Z out of the screen. The tilt turns the screen
    /// up from the deck about its near edge.
    /// </summary>
    public Matrix4x4 ScreenToModule(ScreenRect d)
    {
        // quad frame -> deck frame: quad Y (up the screen) is deck -Z, quad Z (out of the screen) is deck +Y
        var basis = new Matrix4x4(
            1f, 0f, 0f, 0f,
            0f, 0f, -1f, 0f,
            0f, 1f, 0f, 0f,
            0f, 0f, 0f, 1f);
        float tilt = d.TiltDegrees * MathF.PI / 180f;
        // pivot about the near edge, which is Depth/2 below the quad centre (the pilot is towards -Y in quad coordinates)
        var toEdge = Matrix4x4.CreateTranslation(0f, d.Depth / 2f, 0f);
        var turn = Matrix4x4.CreateRotationX(tilt); // raises the far edge: the screen leans towards the pilot
        var fromEdge = Matrix4x4.CreateTranslation(0f, -d.Depth / 2f, 0f);
        var local = toEdge * turn * fromEdge; // identity when the tilt is 0
        var place = Matrix4x4.CreateTranslation(d.X, SurfaceHeight, d.Z);
        return local * basis * place * DeckToModule;
    }

    /// <summary>The frame of the dark bezel box behind a screen (it follows the same tilt, a little below the surface).</summary>
    public Matrix4x4 BezelToModule(ScreenRect d)
    {
        float tilt = d.TiltDegrees * MathF.PI / 180f;
        var pivot = Matrix4x4.CreateTranslation(0f, 0f, -d.Depth / 2f); // deck coordinates: the pilot is at +Z
        var turn = Matrix4x4.CreateRotationX(tilt);
        var back = Matrix4x4.CreateTranslation(0f, 0f, d.Depth / 2f);
        var local = pivot * turn * back;
        var place = Matrix4x4.CreateTranslation(d.X, BezelHeight, d.Z);
        return local * place * DeckToModule;
    }
}

/// <summary>The cockpit layouts of the content, by id. Engine-free.</summary>
public sealed class CockpitLayouts
{
    public const string StarterId = "starter";

    private static CockpitLayout? _starter;
    private readonly Dictionary<string, CockpitLayout> _byId;

    private CockpitLayouts(Dictionary<string, CockpitLayout> byId) => _byId = byId;

    public IReadOnlyCollection<string> Ids => _byId.Keys;

    /// <summary>The layout of a cockpit style; the starter console when the style is not listed.</summary>
    public CockpitLayout Get(string? id) => id is not null && _byId.TryGetValue(id, out var l) ? l : _byId[StarterId];

    public static CockpitLayouts Default { get; } = new(new Dictionary<string, CockpitLayout> { [StarterId] = BuiltInStarter });

    /// <summary>The starter console as the game has it when the content file is missing (the same numbers as <c>content/cockpit_layouts.json</c>).</summary>
    public static CockpitLayout BuiltInStarter => _starter ??= new(
        StarterId, new Vector3(4f, 1.02f, 2.55f), 18f, new Vector3(0.72f, 0.78f, 1f), 4f, 0.13f, 0.112f, 0.06f,
        new[]
        {
            new ScreenRect(-1.79f, -0.01f, 0.95f, 0.9f, 310, 400, 14f),
            new ScreenRect(0f, -0.03f, 2.3f, 1.1f, 612, 400, 22f),
            new ScreenRect(1.79f, -0.01f, 0.95f, 0.9f, 310, 400, 14f),
        });

    public static CockpitLayouts FromJson(string json)
    {
        try
        {
            using var doc = JsonDocument.Parse(json, new JsonDocumentOptions { CommentHandling = JsonCommentHandling.Skip, AllowTrailingCommas = true });
            if (!doc.RootElement.TryGetProperty("layouts", out var layouts) || layouts.ValueKind != JsonValueKind.Object) throw new InvalidDataException("cockpit_layouts.json needs a layouts object");
            var map = new Dictionary<string, CockpitLayout>();
            foreach (var p in layouts.EnumerateObject()) map[p.Name] = ReadLayout(p.Name, p.Value);
            if (!map.ContainsKey(StarterId)) throw new InvalidDataException("cockpit_layouts.json needs a layout called \"starter\"");
            return new CockpitLayouts(map);
        }
        catch (JsonException e) { throw new InvalidDataException("cockpit_layouts.json is not valid JSON: " + e.Message); }
    }

    private static CockpitLayout ReadLayout(string id, JsonElement e)
    {
        string where = "layout " + id;
        var screens = new ScreenRect[3];
        if (!e.TryGetProperty("screens", out var sc) || sc.ValueKind != JsonValueKind.Object) throw new InvalidDataException(where + " needs screens");
        string[] slots = { "left", "center", "right" };
        for (int i = 0; i < 3; i++)
        {
            if (!sc.TryGetProperty(slots[i], out var s)) throw new InvalidDataException($"{where}: the {slots[i]} screen is missing");
            var r = new ScreenRect(Num(s, "x", where), Num(s, "z", where), Num(s, "width", where), Num(s, "depth", where), (int)Num(s, "px_w", where), (int)Num(s, "px_h", where), s.TryGetProperty("tilt_degrees", out _) ? Num(s, "tilt_degrees", where) : 0f);
            if (r.Width <= 0.05f || r.Depth <= 0.05f || r.Width > 8f || r.Depth > 4f) throw new InvalidDataException($"{where}: the {slots[i]} screen has an unusable size");
            if (r.PxW < 64 || r.PxH < 64 || r.PxW > 2048 || r.PxH > 2048) throw new InvalidDataException($"{where}: the {slots[i]} texture size must be between 64 and 2048 pixels");
            if (r.TiltDegrees < -10f || r.TiltDegrees > 75f) throw new InvalidDataException($"{where}: the {slots[i]} tilt must be between -10 and 75 degrees");
            screens[i] = r;
        }
        return new CockpitLayout(id, Vec(e, "deck_origin", where), Num(e, "deck_tilt_degrees", where), Vec(e, "squeeze", where), Num(e, "squeeze_about_x", where),
            Num(e, "surface_height", where), Num(e, "bezel_height", where), Num(e, "bezel_margin", where), screens);
    }

    private static float Num(JsonElement obj, string name, string where)
    {
        if (!obj.TryGetProperty(name, out var e) || e.ValueKind != JsonValueKind.Number || !e.TryGetSingle(out float v) || !float.IsFinite(v)) throw new InvalidDataException($"{where}: {name} must be a number");
        return v;
    }

    private static Vector3 Vec(JsonElement obj, string name, string where)
    {
        if (!obj.TryGetProperty(name, out var e) || e.ValueKind != JsonValueKind.Array || e.GetArrayLength() != 3) throw new InvalidDataException($"{where}: {name} must be three numbers");
        var a = e.EnumerateArray().Select(x => x.ValueKind == JsonValueKind.Number && x.TryGetSingle(out float f) && float.IsFinite(f) ? f : throw new InvalidDataException($"{where}: {name} must be numbers")).ToArray();
        return new Vector3(a[0], a[1], a[2]);
    }
}
