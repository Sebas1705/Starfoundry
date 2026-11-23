using System.Numerics;
using Starfoundry.Sim;

namespace Starfoundry.Ui;

/// <summary>
/// The shell of one module as the walking body sees it: whether it is a ship hull or a station box, the height of its roof, whether it
/// carries a tapered nose or tail, and its neighbour mask (the <see cref="HullProfile"/> bits). Built by <see cref="HullProfile.ShellOf"/>.
/// </summary>
public readonly record struct HullShell(bool Ship, float RoofY, bool Nose, bool Tail, bool OpenRear, bool Kerb, int Mask);

/// <summary>
/// The single source of truth for the cross-section of a module shell. The model builder (<c>game/ModuleModels.cs</c>) draws its ship
/// hull from <see cref="Octagon"/> (the floor and ceiling chamfers, the nose and tail tapers) and its stations from the constants below, and
/// the walking body collides against the very same numbers (<see cref="Clear"/>), so the diagonal strip between floor and wall is solid
/// for the body exactly where it is drawn. Engine-free; all lengths are in cells of the module box (8 x 4 x 8, local y up from the floor).
/// </summary>
public static class HullProfile
{
    // ---- neighbour and docking bits of a module's mask (the same values the model builder uses) ------------------------

    public const int Left = 1, Right = 2, Front = 4, Rear = 8, Up = 16, Down = 32;
    public const int PortLeft = 64, PortRight = 128, PortFront = 256, PortRear = 512;
    public const int DockedLeft = 1024, DockedRight = 2048, DockedFront = 4096, DockedRear = 8192;

    // ---- shared dimensions ---------------------------------------------------------------------------------------------

    public const float W = StationGrid.ModuleX, H = StationGrid.ModuleY, D = StationGrid.ModuleZ;
    /// <summary>Height of the underside of the floor plating: the section of a ship hull starts here.</summary>
    public const float FloorY = 0.08f;
    /// <summary>Top of the deck plating, the height the soles of the body touch.</summary>
    public const float DeckTop = 0.086f;
    /// <summary>Distance of the outer face of a ship hull from the module box.</summary>
    public const float HullEdge = 0.05f;
    /// <summary>A ship hull is lined with an inner face this far inside the outer one (the face the player actually sees from inside).</summary>
    public const float LinerInset = 0.1f;
    /// <summary>Longest chamfer of a ship section, and the share of the section height it takes before that cap.</summary>
    public const float MaxChamfer = 1.0f, ChamferShare = 0.3f;

    /// <summary>The top of a full-height shell, the lower roof of a shell that carries something on its roof, and the roofs of the small modules.</summary>
    public const float Roof = 3.88f, LowRoof = 3.0f, ShieldRoof = 1.2f, SensorRoof = 2.2f, MiningRoof = 1.5f, GunRoof = 1.2f, ShipyardRoof = 3.2f;

    /// <summary>The nose of a ship cockpit runs from a tip of scale <see cref="NoseTipScale"/> at z = <see cref="NoseStart"/> to the full section at <see cref="NoseEnd"/>.</summary>
    public const float NoseStart = 0.05f, NoseEnd = 3.4f, NoseTipScale = 0.28f;
    /// <summary>The tail of a ship engine runs from the full section at <see cref="TailStart"/> to scale <see cref="TailScale"/> at <see cref="TailEnd"/>.</summary>
    public const float TailStart = 5.0f, TailEnd = 6.1f, TailScale = 0.8f;
    /// <summary>Pivot of a section scale, as a share of the section height above the floor: a nose tip scales about the floor, the rest about 0.35.</summary>
    public const float DefaultPivot = 0.35f;

    /// <summary>A station shell stands this far inside the module box and is <see cref="StationWall"/> thick.</summary>
    public const float StationFace = 0.14f, StationWall = 0.2f;
    /// <summary>Half the width of a doorway and the half width of a ship's airlock, and the depth of the door frame round a joint.</summary>
    public const float DoorHalf = 1.8f, ShipAirlockHalf = DoorHalf - 0.18f;
    /// <summary>How far a frame protrudes into a module: a joint frame (own side), a joint frame (the neighbour's side), a ship airlock, a station airlock.</summary>
    public const float JointOwn = 0.37f, JointNeighbour = 0.09f, ShipAirlockDepth = StationFace + 0.34f, StationAirlockDepth = StationFace + 0.5f;
    /// <summary>The raised kerb along both walls of a defence deck, measured from the wall (the parapet block is 0.5 wide centred 0.3 in).</summary>
    public const float DefenceKerb = 0.55f;
    /// <summary>The height of the body's head above the floor, a little over the eye.</summary>
    public const float BodyHeight = 1.8f;

    /// <summary>The roof height of a module kind (cells above the module floor).</summary>
    public static float RoofOf(ModuleKind kind, bool ship) => kind switch
    {
        ModuleKind.Shield => ShieldRoof,
        ModuleKind.Sensor => SensorRoof,
        ModuleKind.Reactor or ModuleKind.ReactorMk2 or ModuleKind.Core => LowRoof,
        ModuleKind.Cockpit => ship ? Roof : LowRoof,
        ModuleKind.MiningLaser or ModuleKind.MiningLaserMk2 => MiningRoof,
        ModuleKind.Gun or ModuleKind.GunMk2 => GunRoof,
        ModuleKind.Shipyard => ShipyardRoof,
        _ => Roof,
    };

    /// <summary>The shell of a module of <paramref name="kind"/> with the given neighbour mask.</summary>
    public static HullShell ShellOf(ModuleKind kind, bool ship, int mask) => new(
        ship, RoofOf(kind, ship),
        Nose: ship && kind == ModuleKind.Cockpit && (mask & Front) == 0,
        Tail: ship && kind == ModuleKind.Engine && (mask & Rear) == 0,
        OpenRear: kind == ModuleKind.DroneBay,
        Kerb: kind == ModuleKind.Defence,
        Mask: mask);

    // ---- the ship section ------------------------------------------------------------------------------------------------

    /// <summary>
    /// The cross-section of a ship hull in the XY plane (counter-clockwise from the bottom edge: bottom, lower right chamfer, right wall,
    /// upper right chamfer, roof, upper left chamfer, left wall, lower left chamfer), scaled about a point <paramref name="pivotFrac"/> of the
    /// way up the section. A side that touches another module (<paramref name="mask"/>) is squared off, so no chamfer is drawn there.
    /// </summary>
    public static Vector2[] Octagon(float roofY, float scale, int mask = 0, float pivotFrac = DefaultPivot)
    {
        float x0 = HullEdge, x1 = W - HullEdge, y0 = FloorY, ch = MathF.Min(MaxChamfer, (roofY - y0) * ChamferShare);
        float bl = (mask & (Left | Down)) != 0 ? 0f : ch, br = (mask & (Right | Down)) != 0 ? 0f : ch;
        float tl = (mask & (Left | Up)) != 0 ? 0f : ch, tr = (mask & (Right | Up)) != 0 ? 0f : ch;
        var p = new[]
        {
            new Vector2(x0 + bl, y0), new Vector2(x1 - br, y0), new Vector2(x1, y0 + br), new Vector2(x1, roofY - tr),
            new Vector2(x1 - tr, roofY), new Vector2(x0 + tl, roofY), new Vector2(x0, roofY - tl), new Vector2(x0, y0 + bl),
        };
        var pivot = new Vector2(W / 2, y0 + (roofY - y0) * pivotFrac);
        for (int i = 0; i < p.Length; i++) p[i] = pivot + (p[i] - pivot) * scale;
        return p;
    }

    /// <summary>The scale of the nose section at depth z (0.28 at the tip, 1 at <see cref="NoseEnd"/>).</summary>
    public static float NoseScale(float z) => NoseTipScale + (1f - NoseTipScale) * (z - NoseStart) / (NoseEnd - NoseStart);

    /// <summary>The vertices of a section moved <see cref="LinerInset"/> towards its centre: the inner face the player sees (the model's liner).</summary>
    public static Vector2[] Liner(Vector2[] section)
    {
        var c = Vector2.Zero;
        foreach (var p in section) c += p;
        c /= section.Length;
        var r = new Vector2[section.Length];
        for (int i = 0; i < r.Length; i++)
        {
            var d = c - section[i];
            r[i] = d.LengthSquared() < 1e-12f ? section[i] : section[i] + Vector2.Normalize(d) * LinerInset;
        }
        return r;
    }

    /// <summary>The cached liner sections of a ship shell: the nose tip, the full section, the tail end.</summary>
    private sealed record Geometry(Vector2[] Tip, Vector2[] Full, Vector2[] TailEnd, float A, float B);

    private static readonly Dictionary<HullShell, Geometry> Cache = new();

    private static Geometry GeometryOf(in HullShell s)
    {
        lock (Cache)
        {
            if (Cache.TryGetValue(s, out var g)) return g;
            var full = Liner(Octagon(s.RoofY, 1f, s.Mask));
            g = new Geometry(
                s.Nose ? Liner(Octagon(s.RoofY, NoseTipScale, s.Mask, 0f)) : full,
                full,
                s.Tail ? Liner(Octagon(s.RoofY, TailScale, s.Mask)) : full,
                s.Nose ? NoseEnd : NoseStart, s.Tail ? TailStart : D - NoseStart);
            Cache[s] = g;
            return g;
        }
    }

    /// <summary>
    /// The inner section (the liner polygon) of a ship shell at depth <paramref name="z"/>, or null where the hull is closed off (inside an
    /// end cap or beyond the tail). Between the tip and the full section, and between the full section and the tail end, the model lofts
    /// linearly, so the section is the linear blend of the two.
    /// </summary>
    public static Vector2[]? SectionAt(in HullShell s, float z)
    {
        var g = GeometryOf(s);
        float capIn = NoseStart + LinerInset;
        if (z < (s.Nose || (s.Mask & Front) == 0 ? capIn : 0f)) return null; // the front bulkhead (or the nose tip)
        if (s.Tail ? z > TailEnd - LinerInset : (s.Mask & Rear) == 0 && !s.OpenRear && z > D - capIn) return null; // the rear bulkhead (or the tail end)
        if (s.Nose && z < g.A) return Blend(g.Tip, g.Full, (z - NoseStart) / (NoseEnd - NoseStart));
        if (s.Tail && z > g.B) return Blend(g.Full, g.TailEnd, (z - TailStart) / (TailEnd - TailStart));
        return g.Full;
    }

    private static Vector2[] Blend(Vector2[] a, Vector2[] b, float t)
    {
        t = Math.Clamp(t, 0f, 1f);
        var r = new Vector2[a.Length];
        for (int i = 0; i < r.Length; i++) r[i] = Vector2.Lerp(a[i], b[i], t);
        return r;
    }

    /// <summary>
    /// True when the point (x, y) of the section is inside the polygon of a hull whose neighbour sides are open. The bottom edge is not tested
    /// (the deck covers it) and the sides that touch another module have no wall, chamfer or roof there.
    /// </summary>
    public static bool InsideSection(Vector2[] liner, int mask, float x, float y)
    {
        for (int i = 0; i < liner.Length; i++)
        {
            if (i == 0) continue;
            if (i == 2 && (mask & Right) != 0) continue;
            if (i == 6 && (mask & Left) != 0) continue;
            if (i == 4 && (mask & Up) != 0) continue;
            var a = liner[i]; var b = liner[(i + 1) % liner.Length];
            var e = b - a;
            if (e.LengthSquared() < 1e-6f) continue; // a squared-off corner has no chamfer face
            // Counter-clockwise polygon: the inside is to the left of every edge.
            if (e.X * (y - a.Y) - e.Y * (x - a.X) < -1e-4f) return false;
        }
        return true;
    }

    // ---- doorways, airlocks and station walls ------------------------------------------------------------------------------

    /// <summary>One side of a module: how deep a solid frame or wall reaches into it and how far the opening in it extends each way from the middle (negative: solid across).</summary>
    private readonly record struct SideZone(float Depth, float Half);

    /// <summary>The solid zone along a side of a module; <paramref name="far"/> is the Right/Rear side (the one a joint frame is drawn on).</summary>
    private static SideZone Zone(in HullShell s, int neighbour, int docked, bool far)
    {
        bool open = (s.Mask & neighbour) != 0 || (!s.Ship && s.OpenRear && neighbour == Rear);
        if ((s.Mask & docked) != 0) return s.Ship ? new(ShipAirlockDepth, ShipAirlockHalf) : new(StationAirlockDepth, DoorHalf);
        if (!s.Ship)
            return open ? new(far ? JointOwn : StationFace + StationWall, DoorHalf) : new(StationFace + StationWall, -1f);
        return open ? new(far ? JointOwn : JointNeighbour, DoorHalf) : new(0f, -1f);
    }

    private static bool Blocked(in SideZone z, float along) => z.Depth > 0f && (z.Half < 0f || MathF.Abs(along - W / 2) > z.Half);

    /// <summary>
    /// True when a point of the body at (x, z) of the module, with the soles at <paramref name="feet"/> and the head at <paramref name="head"/>
    /// (local heights), is not inside the module's solid parts: the ship's inner hull with its floor and ceiling chamfers, nose and tail, the
    /// bulkheads, the door frames and airlocks, a station's walls and roof, and the kerb of a defence deck.
    /// </summary>
    public static bool Clear(in HullShell s, float x, float z, float feet, float head)
    {
        if (x < Depth(s, Left, DockedLeft, false) && Blocked(Zone(s, Left, DockedLeft, false), z)) return false;
        if (x > W - Depth(s, Right, DockedRight, true) && Blocked(Zone(s, Right, DockedRight, true), z)) return false;
        if (z < Depth(s, Front, DockedFront, false) && Blocked(Zone(s, Front, DockedFront, false), x)) return false;
        if (z > D - Depth(s, Rear, DockedRear, true) && Blocked(Zone(s, Rear, DockedRear, true), x)) return false;
        if (s.Kerb && (x < DefenceKerb || x > W - DefenceKerb)) return false;
        if (!s.Ship)
        {
            // a station's roof slab is a wall thickness thick; a module with another above has no roof of its own
            return (s.Mask & Up) != 0 || head <= s.RoofY - StationWall;
        }
        var liner = SectionAt(s, z);
        if (liner is null) return false;
        return InsideSection(liner, s.Mask, x, feet) && InsideSection(liner, s.Mask, x, head);
    }

    private static float Depth(in HullShell s, int neighbour, int docked, bool far) => Zone(s, neighbour, docked, far).Depth;
}
