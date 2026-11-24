using System.Globalization;
using System.Numerics;
using System.Text;
using System.Text.Json;
using Starfoundry.Sim;

namespace Starfoundry.Ui;

/// <summary>
/// The engine-free numbers of the starter cockpit: the console deck, the three display rectangles, the shell section the glazing sits in and the solid
/// footprints. The game's <c>CockpitScreenLayout</c> reads its constants from here, and <see cref="ToJson"/> writes them to <c>tools/blender/shell.json</c>
/// so the Blender generator (<c>tools/blender/cockpit.py</c>) builds the cockpit detail on exactly the same dimensions. A test fails when the file is stale.
/// All lengths are cells of the 8 x 4 x 8 module box (X right, Y up, Z back; the front of a ship is z = 0).
/// </summary>
public static class CockpitShell
{
    /// <summary>The deck of the console (a plate tilted about X) in the module's frame before the squeeze.</summary>
    public const float DeckOriginX = 4f, DeckOriginY = 1.02f, DeckOriginZ = 2.55f, DeckTiltDegrees = 18f;
    /// <summary>The squeeze a ship applies to the whole console, about the middle of the module (X only), and the X it keeps fixed.</summary>
    public const float SqueezeX = 0.72f, SqueezeY = 0.78f, SqueezeZ = 1f, SqueezeAboutX = 4f;
    /// <summary>Height of the lit screen surface and of the bezel box above the deck plate, and the bezel margin round a screen, in deck cells.</summary>
    public const float SurfaceHeight = 0.13f, BezelHeight = 0.112f, BezelMargin = 0.06f;

    /// <summary>A screen is a rectangle on the deck: centre (<see cref="X"/>, <see cref="Z"/>) on the deck, size in deck cells before the squeeze, and the pixels of its texture.</summary>
    public readonly record struct ScreenRect(CockpitScreen Screen, float X, float Z, float Width, float Depth, int PxW, int PxH);

    public static readonly ScreenRect[] Screens =
    {
        new(CockpitScreen.Left, -1.72f, -0.01f, 1.25f, 0.74f, 496, 400),
        new(CockpitScreen.Center, 0f, -0.02f, 1.9f, 0.78f, 720, 400),
        new(CockpitScreen.Right, 1.72f, -0.01f, 1.25f, 0.74f, 496, 400),
    };

    /// <summary>
    /// The mask of the starter ship's cockpit as <see cref="HullProfile.MaskOf"/> computes it for the modules of <c>starter_ship</c> in <c>content/core.json</c>
    /// (cockpit, hull, cargo, reactor, engine in a row, a mining laser in front, a gun on top): a pod in front (flat windscreen), a module behind and above.
    /// </summary>
    public static int StarterMask()
    {
        var parts = new Dictionary<(int, int, int), ModuleKind>
        {
            [(0, 0, 0)] = ModuleKind.Cockpit, [(0, 0, 1)] = ModuleKind.Hull, [(0, 0, 2)] = ModuleKind.Cargo, [(0, 0, 3)] = ModuleKind.Reactor,
            [(0, 0, 4)] = ModuleKind.Engine, [(0, 0, -1)] = ModuleKind.MiningLaser, [(0, 1, 0)] = ModuleKind.Gun,
        };
        return HullProfile.MaskOf((x, y, z) => parts.TryGetValue((x, y, z), out var k) ? k : null, 0, 0, 0);
    }

    /// <summary>The bits of a mask that decide the cockpit's shell: the neighbour bits and the pod bits (the skin bits of the neighbours change nothing here).</summary>
    public const int ShellBits = HullProfile.Left | HullProfile.Right | HullProfile.Front | HullProfile.Rear | HullProfile.Up | HullProfile.Down | HullProfile.PodFront | HullProfile.PodRear | (3 << HullProfile.SkinShift);

    /// <summary>True when a ship cockpit with this mask has the shell of the starter cockpit (the Blender detail is made for that shell).</summary>
    public static bool IsStarterShell(int mask) => (mask & ShellBits) == (StarterMask() & ShellBits);

    private static Vector3 Rot(Vector3 p)
    {
        float a = DeckTiltDegrees * MathF.PI / 180f, c = MathF.Cos(a), s = MathF.Sin(a);
        return new Vector3(p.X, p.Y * c - p.Z * s, p.Y * s + p.Z * c);
    }

    /// <summary>A point of the deck frame (X right, Y out of the deck, Z towards the pilot) in the module's frame: the tilt, then the squeeze about the middle of the module.</summary>
    public static Vector3 DeckPoint(Vector3 p)
    {
        var r = Rot(p);
        return new Vector3(r.X * SqueezeX + DeckOriginX * SqueezeX + SqueezeAboutX * (1f - SqueezeX), r.Y * SqueezeY + DeckOriginY * SqueezeY, r.Z * SqueezeZ + DeckOriginZ * SqueezeZ);
    }

    // ---- the JSON --------------------------------------------------------------------------------------------------------------

    private static decimal R(float v) => decimal.Round((decimal)v, 4) / 1.0000000000000000000000000000m;

    private static void Vec(Utf8JsonWriter w, string name, Vector3 v)
    {
        w.WriteStartArray(name);
        w.WriteNumberValue(R(v.X)); w.WriteNumberValue(R(v.Y)); w.WriteNumberValue(R(v.Z));
        w.WriteEndArray();
    }

    private static void Pts(Utf8JsonWriter w, string name, IEnumerable<System.Numerics.Vector2> pts)
    {
        w.WriteStartArray(name);
        foreach (var p in pts) { w.WriteStartArray(); w.WriteNumberValue(R(p.X)); w.WriteNumberValue(R(p.Y)); w.WriteEndArray(); }
        w.WriteEndArray();
    }

    private static void Box(Utf8JsonWriter w, string name, float x0, float z0, float x1, float z1)
    {
        w.WriteStartObject(name);
        w.WriteNumber("x0", R(x0)); w.WriteNumber("z0", R(z0)); w.WriteNumber("x1", R(x1)); w.WriteNumber("z1", R(z1));
        w.WriteEndObject();
    }

    /// <summary>The side window band of the starter cockpit, as <c>ModuleModels.Viewports</c> cuts it to the wall of the skin (a ship cockpit with a pod in front starts at z = 0.45).</summary>
    public static (float Z0, float Z1, float Y0, float Y1) SideWindow(int mask)
    {
        var ring = HullProfile.Octagon(HullProfile.Roof, 1f, mask);
        float lo = ring[2].Y + 0.12f, hi = ring[3].Y - 0.1f;
        return ((mask & HullProfile.Front) != 0 ? 0.45f : HullProfile.NoseEnd + 0.05f, 7.55f, MathF.Max(0f, lo), MathF.Min(3.9f, hi));
    }

    /// <summary>The text of <c>tools/blender/shell.json</c>: deterministic, LF line ends, four decimals.</summary>
    public static string ToJson()
    {
        int mask = StarterMask();
        var ring = HullProfile.Octagon(HullProfile.Roof, 1f, mask);
        var liner = HullProfile.Liner(ring, mask);
        var (wz0, wz1, wy0, wy1) = SideWindow(mask);
        using var ms = new MemoryStream();
        using (var w = new Utf8JsonWriter(ms, new JsonWriterOptions { Indented = true }))
        {
            w.WriteStartObject();
            w.WriteString("note", "Generated by CockpitShell.ToJson (src/Starfoundry.Ui/CockpitShell.cs). Do not edit by hand: set STARFOUNDRY_UPDATE_SHELL=1 and run the BlenderPipelineTests to rewrite it.");
            w.WriteNumber("schema", 1);
            w.WriteString("axes", "Godot module frame: X right, Y up, Z back (the front of a ship is z = 0); one unit is one cell");
            Vec(w, "module", new Vector3(HullProfile.W, HullProfile.H, HullProfile.D));

            w.WriteStartObject("shell");
            w.WriteNumber("mask", mask);
            w.WriteNumber("shellBits", ShellBits);
            w.WriteString("skin", "wide");
            w.WriteNumber("lowerChamfer", R(HullProfile.ChamfersOf(HullProfile.SkinWide, 0f).Low));
            w.WriteNumber("upperChamfer", R(HullProfile.ChamfersOf(HullProfile.SkinWide, 0f).Up));
            w.WriteNumber("floorY", R(HullProfile.FloorY));
            w.WriteNumber("deckTop", R(HullProfile.DeckTop));
            w.WriteNumber("hullEdge", R(HullProfile.HullEdge));
            w.WriteNumber("linerInset", R(HullProfile.LinerInset));
            w.WriteNumber("roof", R(HullProfile.Roof));
            w.WriteNumber("noseEnd", R(HullProfile.NoseEnd));
            Pts(w, "section", ring);
            Pts(w, "liner", liner);
            w.WriteEndObject();

            w.WriteStartObject("windscreen");
            w.WriteNumber("z", R(0.06f));
            w.WriteString("note", "one flat pane over the whole section (ring), two posts at x 1.9 and 6.1 and a header just under the roof");
            w.WriteEndObject();

            w.WriteStartObject("sideWindows");
            w.WriteNumber("z0", R(wz0)); w.WriteNumber("z1", R(wz1)); w.WriteNumber("y0", R(wy0)); w.WriteNumber("y1", R(wy1));
            w.WriteNumber("wallThickness", R(0.12f));
            w.WriteNumber("leftWallMidX", R(0.05f + 0.06f));
            w.WriteNumber("rightWallMidX", R(HullProfile.W - 0.05f - 0.06f));
            w.WriteNumber("frameWidth", R(0.09f));
            w.WriteNumber("mullionEvery", R(2.0f));
            w.WriteEndObject();

            w.WriteStartObject("seat");
            Vec(w, "offset", SeatLocator.SeatOffset);
            Vec(w, "eye", SeatLocator.EyeOffset);
            Vec(w, "stand", SeatLocator.StandOffset);
            Box(w, "footprint", HullProfile.SeatX0, HullProfile.SeatZ0, HullProfile.SeatX1, HullProfile.SeatZ1);
            w.WriteEndObject();

            w.WriteStartObject("console");
            Box(w, "footprint", HullProfile.ConsoleX0, HullProfile.ConsoleZ0, HullProfile.ConsoleX1, HullProfile.ConsoleZ1);
            w.WriteStartObject("deck");
            Vec(w, "origin", new Vector3(DeckOriginX, DeckOriginY, DeckOriginZ));
            w.WriteNumber("tiltDegrees", R(DeckTiltDegrees));
            Vec(w, "squeeze", new Vector3(SqueezeX, SqueezeY, SqueezeZ));
            w.WriteNumber("squeezeAboutX", R(SqueezeAboutX));
            w.WriteNumber("surfaceHeight", R(SurfaceHeight));
            w.WriteNumber("bezelHeight", R(BezelHeight));
            w.WriteNumber("bezelMargin", R(BezelMargin));
            Vec(w, "deckToModuleOrigin", DeckPoint(Vector3.Zero));
            Vec(w, "deckToModuleX", DeckPoint(Vector3.UnitX) - DeckPoint(Vector3.Zero));
            Vec(w, "deckToModuleY", DeckPoint(Vector3.UnitY) - DeckPoint(Vector3.Zero));
            Vec(w, "deckToModuleZ", DeckPoint(Vector3.UnitZ) - DeckPoint(Vector3.Zero));
            w.WriteEndObject();
            w.WriteStartArray("screens");
            foreach (var s in Screens)
            {
                w.WriteStartObject();
                w.WriteString("name", s.Screen.ToString().ToLowerInvariant());
                w.WriteNumber("x", R(s.X)); w.WriteNumber("z", R(s.Z)); w.WriteNumber("width", R(s.Width)); w.WriteNumber("depth", R(s.Depth));
                w.WriteNumber("pxW", s.PxW); w.WriteNumber("pxH", s.PxH);
                w.WriteStartArray("cornersModule");
                foreach (var (sx, sz) in new[] { (-1f, -1f), (1f, -1f), (1f, 1f), (-1f, 1f) })
                {
                    var p = DeckPoint(new Vector3(s.X + sx * s.Width / 2f, SurfaceHeight, s.Z + sz * s.Depth / 2f));
                    w.WriteStartArray(); w.WriteNumberValue(R(p.X)); w.WriteNumberValue(R(p.Y)); w.WriteNumberValue(R(p.Z)); w.WriteEndArray();
                }
                w.WriteEndArray();
                w.WriteEndObject();
            }
            w.WriteEndArray();
            w.WriteEndObject();
            w.WriteEndObject();
        }
        return Encoding.UTF8.GetString(ms.ToArray()).Replace("\r\n", "\n") + "\n";
    }

    /// <summary>The file this is written to, relative to the repository root.</summary>
    public const string JsonPath = "tools/blender/shell.json";
}
