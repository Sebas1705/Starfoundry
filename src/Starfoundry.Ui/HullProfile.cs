using System.Numerics;
using Starfoundry.Sim;

namespace Starfoundry.Ui;

/// <summary>The solid furniture a module carries that the walking body cannot pass through (besides its shell).</summary>
public enum Fixture { None, Cockpit, Vessel, VesselMk2 }

/// <summary>
/// The shell of one module as the walking body sees it: whether it is a ship hull or a station box, the height of its roof, whether it
/// carries a tapered nose or tail, and its neighbour mask (the <see cref="HullProfile"/> bits). Built by <see cref="HullProfile.ShellOf"/>.
/// </summary>
public readonly record struct HullShell(bool Ship, float RoofY, bool Nose, bool Tail, bool OpenRear, bool Kerb, int Mask, Fixture Fixture = Fixture.None, bool Pod = false);

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
    /// <summary>
    /// The neighbour on this side is a pod (a hardpoint, a shield or a sensor): a low outbuilding with no walkable interior, so the joint has no
    /// doorway. A cockpit with a pod in front closes its nose with a flat windscreen; every other kind closes the joint with a bulkhead.
    /// </summary>
    public const int PodFront = 16384, PodRear = 32768;

    /// <summary>True for the kinds that hang on the outside of a ship (see <see cref="PodFront"/>): the hardpoints, the shield and the sensor.</summary>
    public static bool IsPod(ModuleKind kind) =>
        ModuleKindIds.IsHardpoint(kind) || kind is ModuleKind.Shield or ModuleKind.Sensor;

    /// <summary>
    /// The neighbour mask of the module at (mx, my, mz) given the kind (or null) of the module at any coordinate: the six neighbour bits plus
    /// <see cref="PodFront"/> and <see cref="PodRear"/>. The front of a ship is -Z. The one mask the model builder and the walking body share.
    /// </summary>
    public static int MaskOf(Func<int, int, int, ModuleKind?> kindAt, int mx, int my, int mz)
    {
        int Bit(int dx, int dy, int dz, int bit, int pod = 0) =>
            kindAt(mx + dx, my + dy, mz + dz) is { } k ? bit | (pod != 0 && IsPod(k) ? pod : 0) : 0;
        // the four lateral and vertical neighbour bits of a module, packed as L, R, U, D: how a neighbour along Z squares its own section
        int Lrud(int x, int y, int z) =>
            (kindAt(x - 1, y, z) is null ? 0 : 1) | (kindAt(x + 1, y, z) is null ? 0 : 2) | (kindAt(x, y + 1, z) is null ? 0 : 4) | (kindAt(x, y - 1, z) is null ? 0 : 8);
        int NbFront = kindAt(mx, my, mz - 1) is { } fk && !IsPod(fk) ? Lrud(mx, my, mz - 1) << FrontNbShift : 0;
        int NbRear = kindAt(mx, my, mz + 1) is { } rk && !IsPod(rk) ? Lrud(mx, my, mz + 1) << RearNbShift : 0;
        return Bit(-1, 0, 0, Left) | Bit(1, 0, 0, Right) | Bit(0, 0, -1, Front, PodFront) | Bit(0, 0, 1, Rear, PodRear) | Bit(0, 1, 0, Up) | Bit(0, -1, 0, Down) | NbFront | NbRear;
    }

    /// <summary>
    /// Where the section bits of the front and the rear neighbour sit in a mask: its left, right, up and down neighbours packed as 1, 2, 4, 8. A module
    /// squares the corners of its section where it touches something, so two modules along Z can meet with different sections; the model closes the
    /// difference with a plate (see <see cref="NeighbourRing"/>) and needs to know how the module on the other side is shaped.
    /// </summary>
    public const int FrontNbShift = 16, RearNbShift = 20;

    /// <summary>The section of the module in front of (<paramref name="front"/>) or behind this one at the joint, from the packed bits of <see cref="MaskOf"/>; null when that side has no body module to join.</summary>
    public static Vector2[]? NeighbourRing(int mask, bool front, float roofY)
    {
        if ((mask & (front ? Front : Rear)) == 0 || (mask & (front ? PodFront : PodRear)) != 0) return null;
        int p = (mask >> (front ? FrontNbShift : RearNbShift)) & 15;
        int m = ((p & 1) != 0 ? Left : 0) | ((p & 2) != 0 ? Right : 0) | ((p & 4) != 0 ? Up : 0) | ((p & 8) != 0 ? Down : 0);
        return Octagon(roofY, 1f, m);
    }

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

    // ---- the cockpit furniture: solid for the walking body -------------------------------------------------------------

    /// <summary>The footprint of a ship cockpit's console (a low desk across the front) and of the pilot seat with its armrests, from the module's back-left corner.</summary>
    public const float ConsoleX0 = 2.1f, ConsoleX1 = 5.9f, ConsoleZ0 = 1.95f, ConsoleZ1 = 3.1f;
    public const float SeatX0 = 3.1f, SeatX1 = 4.9f, SeatZ0 = 3.9f, SeatZ1 = 5.5f;
    /// <summary>The radius of the containment vessel standing in the middle of a ship reactor and of a mark 2 reactor.</summary>
    public const float VesselRadius = 1.5f, VesselMk2Radius = 1.8f;

    private static bool InBlock(float x, float z, float x0, float x1, float z0, float z1) => x > x0 && x < x1 && z > z0 && z < z1;

    /// <summary>
    /// The roof height of a module kind (cells above the module floor). Every kind that makes up the body of a ship (hull, cargo, workshop,
    /// reactors, cockpit, engine, defence, drone bay, dock) shares the full <see cref="Roof"/>, so the sections line up at every joint; only the
    /// pods (hardpoints, shield, sensor) and the station-only kinds are lower.
    /// </summary>
    public static float RoofOf(ModuleKind kind, bool ship) => kind switch
    {
        ModuleKind.Shield => ShieldRoof,
        ModuleKind.Sensor => SensorRoof,
        ModuleKind.Reactor or ModuleKind.ReactorMk2 => ship ? Roof : LowRoof,
        ModuleKind.Core => LowRoof,
        ModuleKind.Cockpit => ship ? Roof : LowRoof,
        ModuleKind.MiningLaser or ModuleKind.MiningLaserMk2 => MiningRoof,
        ModuleKind.Gun or ModuleKind.GunMk2 => GunRoof,
        ModuleKind.Shipyard => ShipyardRoof,
        _ => Roof,
    };

    /// <summary>
    /// A nose or a tail narrows the section towards its tip. A module that touches another one at its side or below cannot narrow: the section at
    /// that joint has to stay level with its neighbour's, so it ends blunt (a flat windscreen for a cockpit, a flat end for an engine). A module with
    /// something on top still narrows, below a roof that stays flat.
    /// </summary>
    public static bool Tapers(int mask) => (mask & (Left | Right | Down)) == 0;

    /// <summary>The shell of a module of <paramref name="kind"/> with the given neighbour mask.</summary>
    public static HullShell ShellOf(ModuleKind kind, bool ship, int mask) => new(
        ship, RoofOf(kind, ship),
        Nose: ship && kind == ModuleKind.Cockpit && (mask & Front) == 0 && Tapers(mask),
        Tail: ship && kind == ModuleKind.Engine && (mask & Rear) == 0 && Tapers(mask),
        OpenRear: kind == ModuleKind.DroneBay,
        Kerb: kind == ModuleKind.Defence,
        Mask: mask,
        Fixture: !ship ? Fixture.None : kind == ModuleKind.Cockpit ? Fixture.Cockpit : kind == ModuleKind.Reactor ? Fixture.Vessel : kind == ModuleKind.ReactorMk2 ? Fixture.VesselMk2 : Fixture.None,
        Pod: ship && IsPod(kind));

    // ---- the ship section ------------------------------------------------------------------------------------------------

    /// <summary>
    /// The cross-section of a ship hull in the XY plane (counter-clockwise from the bottom edge: bottom, lower right chamfer, right wall,
    /// upper right chamfer, roof, upper left chamfer, left wall, lower left chamfer), scaled about a point <paramref name="pivotFrac"/> of the
    /// way up the section. A side that touches another module (<paramref name="mask"/>) is squared off, so no chamfer is drawn there.
    /// </summary>
    public static Vector2[] Octagon(float roofY, float scale, int mask = 0, float pivotFrac = DefaultPivot)
    {
        // a side that touches another module has no wall: the hull runs out to the module boundary there, so the two hulls meet with no slit
        float x0 = (mask & Left) != 0 ? 0f : HullEdge, x1 = (mask & Right) != 0 ? W : W - HullEdge, y0 = (mask & Down) != 0 ? Roof - H : FloorY; // a module above another starts where the lower one ends
        float ch = MathF.Min(MaxChamfer, (roofY - FloorY) * ChamferShare);
        float bl = (mask & (Left | Down)) != 0 ? 0f : ch, br = (mask & (Right | Down)) != 0 ? 0f : ch;
        float tl = (mask & (Left | Up)) != 0 ? 0f : ch, tr = (mask & (Right | Up)) != 0 ? 0f : ch;
        var p = new[]
        {
            new Vector2(x0 + bl, y0), new Vector2(x1 - br, y0), new Vector2(x1, y0 + br), new Vector2(x1, roofY - tr),
            new Vector2(x1 - tr, roofY), new Vector2(x0 + tl, roofY), new Vector2(x0, roofY - tl), new Vector2(x0, y0 + bl),
        };
        var pivot = new Vector2(W / 2, y0 + (roofY - y0) * pivotFrac);
        for (int i = 0; i < p.Length; i++)
        {
            var o = p[i];
            p[i] = pivot + (o - pivot) * scale;
            // a taper never pulls a side away from the module it touches: that side stays on the joint plane
            if ((mask & Left) != 0 && o.X <= x0 + 1e-4f) p[i].X = x0;
            if ((mask & Right) != 0 && o.X >= x1 - 1e-4f) p[i].X = x1;
            if ((mask & Up) != 0 && o.Y >= roofY - 1e-4f) p[i].Y = roofY;
            if ((mask & Down) != 0 && o.Y <= y0 + 1e-4f) p[i].Y = y0;
        }
        return p;
    }

    /// <summary>The scale of the nose section at depth z (0.28 at the tip, 1 at <see cref="NoseEnd"/>).</summary>
    public static float NoseScale(float z) => NoseTipScale + (1f - NoseTipScale) * (z - NoseStart) / (NoseEnd - NoseStart);

    /// <summary>
    /// The vertices of a section moved <see cref="LinerInset"/> towards its centre: the inner face the player sees (the model's liner). A vertex that
    /// sits on a side that is open to a neighbouring module (<paramref name="mask"/>: left, right, up, down) keeps its place along that axis, so the
    /// liners of the two modules meet at the joint instead of leaving a slit open to the sky.
    /// </summary>
    public static Vector2[] Liner(Vector2[] section, int mask = 0)
    {
        var c = Vector2.Zero;
        foreach (var p in section) c += p;
        c /= section.Length;
        var r = new Vector2[section.Length];
        float top = float.MinValue;
        foreach (var p in section) top = MathF.Max(top, p.Y);
        for (int i = 0; i < r.Length; i++) r[i] = LinerPoint(section[i], c, mask, top);
        return r;
    }

    /// <summary>One vertex of a section's liner (see <see cref="Liner"/>).</summary>
    public static Vector2 LinerPoint(Vector2 p, Vector2 centre, int mask, float top)
    {
        var d = centre - p;
        if (d.LengthSquared() < 1e-12f) return p;
        var shift = Vector2.Normalize(d) * LinerInset;
        if (((mask & Left) != 0 && p.X <= 0.001f) || ((mask & Right) != 0 && p.X >= W - 0.001f)) shift.X = 0f;
        if (((mask & Down) != 0 && p.Y <= Roof - H + 0.001f) || ((mask & Up) != 0 && p.Y >= top - 0.001f)) shift.Y = 0f;
        return p + shift;
    }

    /// <summary>The cached liner sections of a ship shell: the nose tip, the full section, the tail end.</summary>
    private sealed record Geometry(Vector2[] Tip, Vector2[] Full, Vector2[] TailEnd, float A, float B);

    private static readonly Dictionary<HullShell, Geometry> Cache = new();

    private static Geometry GeometryOf(in HullShell s)
    {
        lock (Cache)
        {
            if (Cache.TryGetValue(s, out var g)) return g;
            var full = Liner(Octagon(s.RoofY, 1f, s.Mask), s.Mask);
            g = new Geometry(
                s.Nose ? Liner(Octagon(s.RoofY, NoseTipScale, s.Mask, 0f), s.Mask) : full,
                full,
                s.Tail ? Liner(Octagon(s.RoofY, TailScale, s.Mask), s.Mask) : full,
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
        if (z < (s.Nose || !FrontOpen(s) ? capIn : 0f)) return null; // the front bulkhead (or the nose tip)
        if (s.Tail ? z > TailEnd - LinerInset : !RearOpen(s) && !s.OpenRear && z > D - capIn) return null; // the rear bulkhead (or the tail end)
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
        bool open = ((s.Mask & neighbour) != 0 && !(s.Ship && IsPodSide(s, neighbour))) || (!s.Ship && s.OpenRear && neighbour == Rear);
        if ((s.Mask & docked) != 0) return s.Ship ? new(ShipAirlockDepth, ShipAirlockHalf) : new(StationAirlockDepth, DoorHalf);
        if (!s.Ship)
            return open ? new(far ? JointOwn : StationFace + StationWall, DoorHalf) : new(StationFace + StationWall, -1f);
        return open ? new(far ? JointOwn : JointNeighbour, DoorHalf) : new(0f, -1f);
    }

    /// <summary>True when the neighbour on this side of a ship shell is a pod (see <see cref="PodFront"/>): the joint is closed, there is no doorway.</summary>
    private static bool IsPodSide(in HullShell s, int neighbour) =>
        s.Pod ? neighbour is Front or Rear : neighbour == Front ? (s.Mask & PodFront) != 0 : neighbour == Rear && (s.Mask & PodRear) != 0;

    /// <summary>The front (rear) of a shell is open to the next module: it touches one that is not a pod.</summary>
    private static bool FrontOpen(in HullShell s) => (s.Mask & Front) != 0 && (!s.Ship || ((s.Mask & PodFront) == 0 && !s.Pod));
    private static bool RearOpen(in HullShell s) => (s.Mask & Rear) != 0 && (!s.Ship || ((s.Mask & PodRear) == 0 && !s.Pod));

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
        if (s.Fixture == Fixture.Cockpit && (InBlock(x, z, ConsoleX0, ConsoleX1, ConsoleZ0, ConsoleZ1) || InBlock(x, z, SeatX0, SeatX1, SeatZ0, SeatZ1))) return false;
        if (s.Fixture is Fixture.Vessel or Fixture.VesselMk2)
        {
            float r = s.Fixture == Fixture.Vessel ? VesselRadius : VesselMk2Radius;
            if ((x - W / 2) * (x - W / 2) + (z - D / 2) * (z - D / 2) < r * r) return false;
        }
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
