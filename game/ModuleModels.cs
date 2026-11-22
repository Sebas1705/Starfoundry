using System;
using Godot;
using Starfoundry.Sim;

namespace Starfoundry.Game;

/// <summary>
/// Procedural models for structure modules. A module is an 8 x 4 x 8 cell box (X width, Y height, Z depth, one cell = one
/// world unit) and the model's local origin is the box's minimum corner, so it is placed at (mx * 8, my * 4, mz * 8).
/// Everything fits inside the box. The front of a ship is -Z (cockpit), the back is +Z (engines).
///
/// The <c>ship</c> flag chooses the outer shell language: a ship is streamlined (white octagonal plating, a tapered nose at
/// the cockpit, a tapered tail at the engines, blue trim), a station is boxy (grey slab panels, a heavy corner frame, docking
/// collars on the four sides, orange trim). The shells are single-sided: they show from outside and vanish when the camera
/// is inside the module, so the player looks through the hull at the stars and the deck; the frame beams and the deck stay
/// visible from inside. The workshop and the defence deck have no roof, so what stands on them stays in view.
///
/// Parts reuse <see cref="Models"/>' meshes cache, materials and mesh builder, so every module of a kind shares its meshes.
/// Node names the game can drive: "GlowEngine" (flame of an engine nozzle, pulsed by <see cref="Models.Animate"/>, a flame
/// along local +X which is world +Z), "Ring" (the core's gold ring, spun by Animate), "Turret0".. with "Head" and "Head/Barrel"
/// children (<see cref="Models.Aim"/>), "GlowCore" (the reactor),
/// "GlowLamp" (the drone bay's status light), "GlowDoor" (its launch door).
/// </summary>
public static partial class ModuleModels
{
    public const float W = StationGrid.ModuleX;
    public const float H = StationGrid.ModuleY;
    public const float D = StationGrid.ModuleZ;

    private const float Roof = 3.88f;     // top of a full-height shell
    private const float StationFace = 0.14f; // a station shell stands this far inside the module box, between the frame beams
    private const float LowRoof = 3.0f;  // top of a shell that carries something on its roof (cupola, reactor drum, core pillar)

    private static readonly Color Dark = new(0.12f, 0.14f, 0.19f);
    private static readonly Color DeckCol = new(0.19f, 0.21f, 0.27f);
    private static readonly Color Steel = new(0.46f, 0.50f, 0.58f);
    private static readonly Color Panel = new(0.72f, 0.76f, 0.83f);
    private static readonly Color White = new(0.90f, 0.93f, 0.98f);
    private static readonly Color Slab = new(0.46f, 0.49f, 0.57f);
    private static readonly Color SlabLt = new(0.58f, 0.62f, 0.70f);
    private static readonly Color Yellow = new(0.98f, 0.86f, 0.18f);
    private static readonly Color Gold = new(1f, 0.78f, 0.30f);
    private static readonly Color Crate = new(0.62f, 0.40f, 0.22f);
    private static readonly Color CrateDk = new(0.40f, 0.27f, 0.17f);
    private static readonly Color Green = new(0.30f, 0.95f, 0.50f);
    private static readonly Color Amber = new(1f, 0.70f, 0.18f);

    private static readonly Color[] ShipAccents = { new(0.24f, 0.48f, 0.94f), new(0.15f, 0.72f, 0.75f), new(0.97f, 0.42f, 0.07f) };
    private static readonly Color[] StationAccents = { new(0.97f, 0.42f, 0.07f), new(0.15f, 0.72f, 0.75f), new(0.64f, 0.34f, 0.90f) };

    private readonly record struct Cx(Node3D R, string K, bool Ship, Color Acc, int V, ModuleKind Kind, int M, Color Role)
    {
        public bool Has(int bit) => (M & bit) != 0;
    }

    /// <summary>Neighbour bits of a module: which of its six sides touch another module of the same structure.</summary>
    public const int Left = 1, Right = 2, Front = 4, Rear = 8, Up = 16, Down = 32;

    /// <summary>The neighbour mask of the module at (mx, my, mz) in a set of module coordinates; the front of a ship is -Z.</summary>
    public static int NeighbourMask(Func<int, int, int, bool> has, int mx, int my, int mz) =>
        (has(mx - 1, my, mz) ? Left : 0) | (has(mx + 1, my, mz) ? Right : 0) | (has(mx, my, mz - 1) ? Front : 0)
        | (has(mx, my, mz + 1) ? Rear : 0) | (has(mx, my + 1, mz) ? Up : 0) | (has(mx, my - 1, mz) ? Down : 0);

    /// <summary>The colour that marks a module's role on its hull band, its door frames and its floor markings.</summary>
    public static Color RoleColour(ModuleKind kind) => kind switch
    {
        ModuleKind.Cockpit => new Color(0.30f, 0.80f, 1f),
        ModuleKind.Engine => new Color(1f, 0.38f, 0.10f),
        ModuleKind.Reactor => new Color(0.30f, 0.95f, 0.50f),
        ModuleKind.ReactorMk2 => new Color(0.40f, 0.90f, 1f),
        ModuleKind.Cargo => new Color(0.80f, 0.58f, 0.30f),
        ModuleKind.Workshop => new Color(0.98f, 0.80f, 0.15f),
        ModuleKind.Defence => new Color(0.90f, 0.22f, 0.22f),
        ModuleKind.DroneBay => new Color(0.80f, 0.45f, 0.95f),
        ModuleKind.Shield => new Color(0.30f, 0.60f, 1f),
        ModuleKind.Sensor => new Color(0.20f, 0.85f, 0.78f),
        ModuleKind.Core => new Color(1f, 0.78f, 0.30f),
        _ => new Color(0.58f, 0.64f, 0.76f),
    };

    /// <summary>
    /// A model of one module of <paramref name="kind"/>; <paramref name="variant"/> (0..2) picks the trim colour and the clutter layout.
    /// <paramref name="neighbours"/> (see <see cref="NeighbourMask"/>) says which sides touch another module: those sides get a doorway
    /// instead of a wall, exposed ends of a ship get a bulkhead, and the exposed sides of a station get docking collars, solar arrays and masts.
    /// </summary>
    public static Node3D Build(ModuleKind kind, bool ship, int variant = 0, int neighbours = 0)
    {
        variant = ((variant % 3) + 3) % 3;
        var r = new Node3D { Name = $"{kind}{(ship ? "Ship" : "Station")}" };
        var c = new Cx(r, $"mod.{(ship ? "s" : "t")}{variant}.{kind}.n{neighbours}", ship, (ship ? ShipAccents : StationAccents)[variant], variant, kind, neighbours, RoleColour(kind));
        Deck(c);
        switch (kind)
        {
            case ModuleKind.Cockpit: Cockpit(c); break;
            case ModuleKind.Engine: Engine(c); break;
            case ModuleKind.Reactor: Reactor(c); break;
            case ModuleKind.Cargo: Cargo(c); break;
            case ModuleKind.Workshop: Workshop(c); break;
            case ModuleKind.Defence: Defence(c); break;
            case ModuleKind.Core: Core(c); break;
            case ModuleKind.DroneBay: DroneBay(c); break;
            case ModuleKind.Shield: Shield(c); break;
            case ModuleKind.Sensor: Sensor(c); break;
            case ModuleKind.ReactorMk2: ReactorMk2(c); break;
            default: HullModule(c); break;
        }
        Joints(c);
        Exposed(c);
        Details(c);
        return r;
    }

    // ---- shared parts ---------------------------------------------------------------------------------

    /// <summary>
    /// The floor of one module: a plated deck that exists only inside the module, with a marking style per kind in the role colour
    /// (a hazard ring under a reactor, cargo lanes, a gold inlay round the core...), and a lit sill across every doorway.
    /// </summary>
    private static void Deck(Cx c) => Models.Add(c.R, "Deck", $"mod.deck.{c.K}", Models.Body(), mb =>
    {
        const float Top = 0.086f;
        var role = c.Role;
        var dim = new Color(role.R * 0.55f, role.G * 0.55f, role.B * 0.55f);
        // A deck with a module below it has a stairwell: a square opening at the rear right with a coaming and a ladder going down.
        bool hole = c.Has(Down) && c.Kind is ModuleKind.Hull or ModuleKind.Cargo or ModuleKind.Workshop or ModuleKind.DroneBay
            or ModuleKind.Cockpit or ModuleKind.Engine or ModuleKind.Defence;
        const float Hx0 = 5.4f, Hx1 = 7.0f, Hz0 = 1.0f, Hz1 = 2.6f;
        void Blk(Vector3 ctr, Vector3 size)
        {
            if (!hole) { mb.Block(ctr, size); return; }
            float x0 = ctr.X - size.X / 2, x1 = ctr.X + size.X / 2, z0 = ctr.Z - size.Z / 2, z1 = ctr.Z + size.Z / 2;
            if (x1 <= Hx0 || x0 >= Hx1 || z1 <= Hz0 || z0 >= Hz1) { mb.Block(ctr, size); return; }
            void Piece(float ax0, float ax1, float az0, float az1)
            {
                if (ax1 - ax0 < 0.004f || az1 - az0 < 0.004f) return;
                mb.Block(new((ax0 + ax1) / 2, ctr.Y, (az0 + az1) / 2), new(ax1 - ax0, size.Y, az1 - az0));
            }
            float cx0 = MathF.Max(x0, Hx0), cx1 = MathF.Min(x1, Hx1);
            Piece(x0, MathF.Min(x1, Hx0), z0, z1);
            Piece(MathF.Max(x0, Hx1), x1, z0, z1);
            Piece(cx0, cx1, z0, MathF.Min(z1, Hz0));
            Piece(cx0, cx1, MathF.Max(z0, Hz1), z1);
        }
        mb.At(default); mb.C(Dark); Blk(new(4, 0.04f, 4), new(7.9f, 0.08f, 7.9f));
        mb.C(DeckCol); Blk(new(4, 0.05f, 4), new(7.3f, 0.07f, 7.3f));
        // plate seams every two cells, in a slightly darker tone, so the floor reads as hard-surface plating
        mb.C(new Color(0.14f, 0.16f, 0.21f));
        for (int i = 1; i < 4; i++)
        {
            Blk(new(i * 2f, Top - 0.004f, 4), new(0.05f, 0.01f, 7.3f));
            Blk(new(4, Top - 0.004f, i * 2f), new(7.3f, 0.01f, 0.05f));
        }
        // the border line: the structure's trim colour on a ship's or a station's deck
        mb.C(c.Acc);
        foreach (float s in new[] { -1f, 1f })
        {
            Blk(new(4 + s * 3.5f, Top, 4), new(0.1f, 0.012f, 7.1f));
            Blk(new(4, Top, 4 + s * 3.5f), new(7.1f, 0.012f, 0.1f));
        }
        switch (c.Kind)
        {
            case ModuleKind.Cockpit:
                mb.C(dim);
                foreach (float x in new[] { 2.3f, 5.7f }) Blk(new(x, Top, 4), new(0.08f, 0.012f, 6.4f));
                mb.C(role); Blk(new(4, Top, 4), new(0.12f, 0.014f, 6.4f));
                break;
            case ModuleKind.Engine:
                mb.C(Dark);
                for (int i = 0; i < 6; i++) Blk(new(4, Top, 0.9f + i * 0.9f), new(5.6f, 0.014f, 0.32f));
                mb.C(dim);
                foreach (float x in new[] { 1.0f, 7.0f }) Blk(new(x, Top, 4), new(0.16f, 0.014f, 7f));
                break;
            case ModuleKind.Reactor:
            case ModuleKind.ReactorMk2:
                mb.C(role).Prism(new(4, 0.07f, 4), 3.0f, 3.0f, 0.02f, 24);
                mb.C(DeckCol).Prism(new(4, 0.075f, 4), 2.8f, 2.8f, 0.02f, 24);
                mb.C(dim).Prism(new(4, 0.08f, 4), 2.1f, 2.1f, 0.02f, 24);
                mb.C(DeckCol).Prism(new(4, 0.085f, 4), 1.95f, 1.95f, 0.02f, 24);
                break;
            case ModuleKind.Cargo:
                mb.C(role);
                foreach (float x in new[] { 2.4f, 5.6f }) Blk(new(x, Top, 4), new(0.12f, 0.014f, 7f));
                for (int i = 0; i < 4; i++) Blk(new(4, Top, 1.2f + i * 1.9f), new(3.1f, 0.014f, 0.1f));
                break;
            case ModuleKind.Workshop:
                mb.C(role);
                foreach (float x in new[] { 2.2f, 5.8f }) Blk(new(x, Top, 4), new(0.18f, 0.014f, 7f));
                mb.C(Dark);
                for (int i = 0; i < 7; i++) Blk(new(4, Top, 0.9f + i), new(3.4f, 0.014f, 0.3f));
                break;
            case ModuleKind.Defence:
                mb.C(role);
                for (int i = 0; i < 4; i++)
                    foreach (float s in new[] { -1f, 1f })
                        Blk(new(4 + s * 3.15f, Top, 1.0f + i * 2.0f), new(0.5f, 0.014f, 0.5f));
                mb.C(dim).Prism(new(4, 0.07f, 4), 1.4f, 1.4f, 0.02f, 8);
                break;
            case ModuleKind.DroneBay:
                mb.C(role);
                foreach (float x in new[] { 2.5f, 5.5f }) Blk(new(x, Top, 4), new(0.14f, 0.014f, 7f));
                mb.C(dim);
                for (int i = 0; i < 5; i++) Blk(new(4, Top, 1.0f + i * 1.5f), new(3.0f, 0.014f, 0.1f));
                break;
            case ModuleKind.Core:
                mb.C(role).Prism(new(4, 0.07f, 4), 3.1f, 3.1f, 0.02f, 8, 22.5f);
                mb.C(DeckCol).Prism(new(4, 0.075f, 4), 2.9f, 2.9f, 0.02f, 8, 22.5f);
                mb.C(Dark).Prism(new(4, 0.08f, 4), 2.3f, 2.3f, 0.02f, 8, 22.5f);
                mb.C(dim).Prism(new(4, 0.085f, 4), 2.1f, 2.1f, 0.02f, 8, 22.5f);
                mb.C(DeckCol).Prism(new(4, 0.09f, 4), 1.9f, 1.9f, 0.02f, 8, 22.5f);
                break;
            case ModuleKind.Shield:
            case ModuleKind.Sensor:
                mb.C(role).Prism(new(4, 0.07f, 4), 2.6f, 2.6f, 0.02f, 20);
                mb.C(DeckCol).Prism(new(4, 0.075f, 4), 2.45f, 2.45f, 0.02f, 20);
                break;
            default: // hull plating: rivet rows along the seams
                mb.C(new Color(0.30f, 0.33f, 0.40f));
                for (int i = 0; i < 4; i++)
                    for (int j = 0; j < 4; j++)
                        Blk(new(1 + i * 2f, Top, 1 + j * 2f), new(0.14f, 0.014f, 0.14f));
                break;
        }
        if (!hole) return;
        // The coaming round the stairwell, a dark shaft wall on two sides and a ladder on the rear edge going down to the deck below.
        const float Cw = 0.1f, Ch = 0.12f;
        float hcx = (Hx0 + Hx1) / 2, hcz = (Hz0 + Hz1) / 2, hw = Hx1 - Hx0, hd = Hz1 - Hz0;
        mb.C(Yellow);
        mb.Block(new(hcx, Top + Ch / 2, Hz0 - Cw / 2), new(hw + 2 * Cw, Ch, Cw));
        mb.Block(new(Hx0 - Cw / 2, Top + Ch / 2, hcz), new(Cw, Ch, hd));
        mb.Block(new(Hx1 + Cw / 2, Top + Ch / 2, hcz), new(Cw, Ch, hd));
        mb.Block(new(hcx, Top + Ch / 2, Hz1 + Cw / 2), new(hw + 2 * Cw, Ch, Cw));
        mb.C(Steel);
        foreach (float rx in new[] { hcx - 0.45f, hcx + 0.45f })
            mb.Block(new(rx, -1.6f, Hz1 - 0.12f), new(0.07f, 3.6f + 0.9f, 0.07f)); // rails stand a little over the deck as handholds
        for (int i = 0; i < 7; i++)
            mb.Block(new(hcx, -3.4f + i * 0.52f, Hz1 - 0.12f), new(0.9f, 0.05f, 0.05f));
    });

    /// <summary>The opening of a doorway in a wall, and the lit sill on the floor across it.</summary>
    private const float DoorHalf = 1.8f, DoorHeight = 2.7f, ShipDoorHeight = 3.1f;

    /// <summary>The cross-section of a ship hull in the XY plane, scaled about a point low in the section.</summary>
    private static Vector2[] Oct(float roofY, float scale, int mask = 0)
    {
        float x0 = 0.05f, x1 = W - 0.05f, y0 = 0.08f, ch = MathF.Min(1.0f, (roofY - y0) * 0.3f);
        // A side that touches another module is squared off (no chamfer), so two modules side by side or stacked meet flush.
        float bl = (mask & (Left | Down)) != 0 ? 0f : ch, br = (mask & (Right | Down)) != 0 ? 0f : ch;
        float tl = (mask & (Left | Up)) != 0 ? 0f : ch, tr = (mask & (Right | Up)) != 0 ? 0f : ch;
        var p = new[]
        {
            new Vector2(x0 + bl, y0), new Vector2(x1 - br, y0), new Vector2(x1, y0 + br), new Vector2(x1, roofY - tr),
            new Vector2(x1 - tr, roofY), new Vector2(x0 + tl, roofY), new Vector2(x0, roofY - tl), new Vector2(x0, y0 + bl),
        };
        var pivot = new Vector2(4f, y0 + (roofY - y0) * 0.35f);
        for (int i = 0; i < p.Length; i++) p[i] = pivot + (p[i] - pivot) * scale;
        return p;
    }

    /// <summary>A convex tube between two cross-sections at depths <paramref name="za"/> and <paramref name="zb"/>.</summary>
    private static void Loft(Models.Mb mb, Vector2[] a, float za, Vector2[] b, float zb, bool capA, bool capB, int skipEdges = 0)
    {
        var ca = Vector2.Zero; var cb = Vector2.Zero;
        foreach (var p in a) ca += p;
        foreach (var p in b) cb += p;
        ca /= a.Length; cb /= b.Length;
        var inside = new Vector3((ca.X + cb.X) / 2, (ca.Y + cb.Y) / 2, (za + zb) / 2);
        for (int i = 0; i < a.Length; i++)
        {
            int j = (i + 1) % a.Length;
            var a0 = new Vector3(a[i].X, a[i].Y, za); var a1 = new Vector3(a[j].X, a[j].Y, za);
            var b0 = new Vector3(b[i].X, b[i].Y, zb); var b1 = new Vector3(b[j].X, b[j].Y, zb);
            if (a0.DistanceTo(a1) < 1e-3f && b0.DistanceTo(b1) < 1e-3f) continue; // a squared-off corner has no chamfer face
            if ((skipEdges & (1 << i)) == 0) mb.Quad(a0, a1, b1, b0, inside);
            if (capA) mb.Tri(new(ca.X, ca.Y, za), a0, a1, inside);
            if (capB) mb.Tri(new(cb.X, cb.Y, zb), b0, b1, inside);
        }
    }

    /// <summary>
    /// The outer shell. Ship: an octagonal hull with optional tapered nose and tail, a spine and side stripe in the trim colour.
    /// Station: a slab box with docking collars on the four sides, and raised panels when <paramref name="detail"/>.
    /// </summary>
    private static void Skin(Cx c, float roofY, bool nose = false, bool tail = false, bool detail = false, bool collars = true, bool glassRoof = false)
    {
        string key = $"{c.K}.skin.{roofY}.{nose}.{tail}.{detail}.{collars}.{glassRoof}";
        Models.Add(c.R, "Skin", key, Models.Body(), mb =>
        {
            mb.At(default);
            if (c.Ship)
            {
                var full = Oct(roofY, 1f, c.M);
                int skip = (c.Has(Right) ? 1 << 2 : 0) | (c.Has(Left) ? 1 << 6 : 0) | (c.Has(Up) ? 1 << 4 : 0) | (c.Has(Down) ? 1 : 0);
                if (glassRoof) skip |= RoofEdges; // the roof is a separate glass part, see-through from inside
                float a = nose ? 3.4f : 0.05f, b = tail ? 5.0f : D - 0.05f;
                mb.C(White);
                // exposed ends of the tube are closed by a bulkhead; ends that touch another module stay open
                bool capFront = !nose && !c.Has(Front), capRear = !tail && !c.Has(Rear);
                if (nose) Loft(mb, Oct(roofY, 0.28f, c.M), 0.05f, full, a, true, false, skip);
                Loft(mb, full, a, full, b, capFront, capRear, skip);
                if (tail) Loft(mb, full, b, Oct(roofY, 0.8f, c.M), 6.1f, false, true, skip);
                float zc = (a + b) / 2, len = b - a;
                float spineTop = full[4].Y;
                if (!c.Has(Up) && !glassRoof) mb.C(c.Acc).Block(new(4, spineTop + 0.02f, zc), new(1.1f, 0.06f, len - 0.1f));
                mb.C(Dark).Block(new(4, 0.066f, 4), new(full[1].X - full[0].X - 0.4f, 0.03f, D - 0.4f));
                float mid = (full[2].Y + full[3].Y) / 2;
                // role band along each exposed flank, and a thin trim line under it
                foreach (float s in new[] { 0.01f, W - 0.01f })
                {
                    if (s < 1f ? c.Has(Left) : c.Has(Right)) continue;
                    mb.C(c.Role).Block(new(s, mid, zc), new(0.04f, 0.26f, len - 0.2f));
                    mb.C(c.Acc).Block(new(s, mid - 0.3f, zc), new(0.04f, 0.05f, len - 0.2f));
                }
                if (detail)
                {
                    mb.C(Panel);
                    if (!c.Has(Up))
                        for (int i = 1; i < 4; i++)
                            mb.Block(new(4, spineTop + 0.012f, i * 2f), new(full[4].X - full[5].X, 0.03f, 0.05f));
                    for (int i = 1; i < 4; i++)
                        foreach (float s in new[] { 0.035f, W - 0.035f })
                        {
                            if (s < 1f ? c.Has(Left) : c.Has(Right)) continue;
                            mb.Block(new(s, mid, i * 2f), new(0.04f, full[3].Y - full[2].Y, 0.05f));
                        }
                }
                // an exposed end of the tube carries a hatch in the role colour: the visible place where the next module would attach
                foreach (bool front in new[] { true, false })
                {
                    if (front ? (nose || c.Has(Front)) : (tail || c.Has(Rear))) continue;
                    float z = front ? 0.05f : D - 0.05f, o = front ? -0.03f : 0.03f;
                    mb.C(Panel).Block(new(4, mid - 0.2f, z + o), new(3.9f, 2.5f, 0.06f));
                    mb.C(Dark).Block(new(4, mid - 0.2f, z + 2 * o), new(3.4f, 2.0f, 0.04f));
                    mb.C(c.Role).Block(new(4, mid + 1.05f, z + 2 * o), new(3.4f, 0.1f, 0.05f));
                }
            }
            else
            {
                BoxShell(mb, c, roofY, detail, collars);
            }
        });
    }

    /// <summary>
    /// A station module's shell: four walls and a roof of grey slabs, each a thin single-sided plate. A side that touches another module has
    /// a doorway (jambs and a lintel) instead of a wall; the exposed sides carry a role-colour band and, when asked, a docking collar.
    /// </summary>
    private static void BoxShell(Models.Mb mb, Cx c, float roofY, bool detail, bool collars)
    {
        const float T = 0.2f;
        float si = StationFace, y0 = 0.08f, h = roofY - y0;
        // Walls: (axis 0 = a wall across X at the left/right, axis 1 = across Z at the front/rear), side 0 or 1
        for (int axis = 0; axis < 2; axis++)
            for (int side = 0; side < 2; side++)
            {
                int bit = axis == 0 ? (side == 0 ? Left : Right) : (side == 0 ? Front : Rear);
                bool open = c.Has(bit);
                float span = axis == 0 ? D - 2 * si : W - 2 * si;
                float pos = side == 0 ? si + T / 2 : (axis == 0 ? W : D) - si - T / 2;
                Vector3 P(float along, float y) => axis == 0 ? new(pos, y, along) : new(along, y, pos);
                Vector3 S(float len, float hh, float extra = 0f) => axis == 0 ? new(T + extra, hh, len) : new(len, hh, T + extra);
                mb.C(Slab);
                if (!open)
                {
                    mb.Block(P(4, y0 + h / 2), S(span, h));
                    mb.C(c.Role).Block(P(4, roofY * 0.5f), S(span - 0.9f, 0.2f, 0.04f));
                    mb.C(c.Acc).Block(P(4, roofY * 0.5f - 0.3f), S(span - 0.9f, 0.05f, 0.04f));
                    if (detail)
                    {
                        mb.C(SlabLt);
                        foreach (float sx in new[] { 2f, 6f })
                            mb.Block(P(sx, roofY * 0.5f + 0.1f), S(3.3f, roofY - 0.9f, 0.03f));
                    }
                }
                else
                {
                    // doorway: jambs either side of the opening and a lintel above it, with a role-colour frame
                    float jamb = (span - 2 * DoorHalf) / 2;
                    foreach (float s in new[] { -1f, 1f })
                        mb.Block(P(4 + s * (DoorHalf + jamb / 2), y0 + h / 2), S(jamb, h));
                    mb.Block(P(4, (y0 + DoorHeight + roofY) / 2), S(2 * DoorHalf, roofY - y0 - DoorHeight));
                }
            }
        if (!c.Has(Up))
        {
            mb.C(Slab).Block(new(4, roofY - T / 2, 4), new(W - 2 * si, T, D - 2 * si));
            mb.C(c.Acc).Block(new(4, roofY + 0.005f, 4), new(W - 2 * si - 0.6f, 0.02f, 0.14f));
            if (detail)
            {
                mb.C(SlabLt);
                foreach (float sx in new[] { 2f, 6f })
                    foreach (float sz in new[] { 2f, 6f })
                        mb.Block(new(sx, roofY + 0.012f, sz), new(3.3f, 0.025f, 3.3f));
            }
        }
        if (!collars) return;
        // Docking collars on the exposed lateral faces: an octagonal flange with a dark hatch (builder +Y turned to point outwards).
        (int bit, Vector3 pos, Vector3 rot)[] spots =
        {
            (Left, new(si, 1.9f, 4), Models.Deg(0, 0, 90)),
            (Right, new(W - si, 1.9f, 4), Models.Deg(0, 0, -90)),
            (Front, new(4, 1.9f, si), Models.Deg(-90, 0, 0)),
            (Rear, new(4, 1.9f, D - si), Models.Deg(90, 0, 0)),
        };
        foreach (var s in spots)
        {
            if (c.Has(s.bit)) continue;
            mb.At(s.pos, s.rot).C(Steel).Prism(default, 1.3f, 1.15f, 0.1f, 8, 22.5f);
            mb.C(Dark).Prism(new(0, 0.005f, 0), 0.95f, 0.95f, 0.11f, 8, 22.5f);
            mb.C(c.Role).Prism(new(0, 0.01f, 0), 0.4f, 0.4f, 0.115f, 8, 22.5f);
        }
        mb.At(default);
    }

    /// <summary>The load-bearing frame, visible from inside as well. Ship: eight longitudinal beams along the octagon and a ring at each end. Station: the twelve box edges with trim caps on the corners.</summary>
    private static void Frame(Cx c, float roofY, bool nose = false, bool tail = false)
    {
        Models.Add(c.R, "Frame", $"{c.K}.frame.{roofY}.{nose}.{tail}", Models.Body(), mb =>
        {
            mb.At(default);
            if (c.Ship)
            {
                // The frame follows the lofted hull: sections at (z, scale), so a nose or tail does not leave the frame floating in the air.
                var secs = new System.Collections.Generic.List<(float Z, float S)>();
                if (nose) { secs.Add((0.07f, 0.28f)); secs.Add((3.4f, 1f)); } else secs.Add((0.07f, 1f));
                if (tail) { secs.Add((5.0f, 1f)); secs.Add((6.1f, 0.8f)); } else secs.Add((D - 0.07f, 1f));
                mb.C(Panel);
                for (int s = 0; s + 1 < secs.Count; s++)
                {
                    var pa = Oct(roofY, secs[s].S, c.M); var pb = Oct(roofY, secs[s + 1].S, c.M);
                    for (int i = 0; i < pa.Length; i++)
                    {
                        if (i % 2 == 1 && pa[i].DistanceTo(pa[(i + 1) % pa.Length]) < 1e-3f) continue; // squared corner: one post only
                        mb.Beam(new(pa[i].X, pa[i].Y, secs[s].Z), new(pb[i].X, pb[i].Y, secs[s + 1].Z), 0.12f);
                    }
                }
                // Ring frames at both ends: a thicker role-colour ring where another module attaches (the visible seam), a plain one at an exposed end.
                for (int e = 0; e < 2; e++)
                {
                    var sec = e == 0 ? secs[0] : secs[^1];
                    bool joined = e == 0 ? c.Has(Front) : c.Has(Rear);
                    var o = Oct(roofY, sec.S, c.M);
                    mb.C(joined ? c.Role : Panel);
                    for (int i = 0; i < o.Length; i++)
                    {
                        int j = (i + 1) % o.Length;
                        if (o[i].DistanceTo(o[j]) < 1e-3f) continue;
                        mb.Beam(new(o[i].X, o[i].Y, sec.Z), new(o[j].X, o[j].Y, sec.Z), joined ? 0.17f : 0.12f);
                    }
                }
            }
            else
            {
                float t = 0.30f, x0 = t / 2, x1 = W - t / 2, y0 = 0.08f + t / 2, y1 = roofY - t / 2, z0 = t / 2, z1 = D - t / 2;
                mb.C(Dark);
                foreach (float y in new[] { y0, y1 })
                    foreach (float z in new[] { z0, z1 })
                        mb.Block(new((x0 + x1) / 2, y, z), new(x1 - x0 + t, t, t));
                foreach (float x in new[] { x0, x1 })
                    foreach (float z in new[] { z0, z1 })
                        mb.Block(new(x, (y0 + y1) / 2, z), new(t, y1 - y0 + t, t));
                foreach (float x in new[] { x0, x1 })
                    foreach (float y in new[] { y0, y1 })
                        mb.Block(new(x, y, (z0 + z1) / 2), new(t, t, z1 - z0 + t));
                mb.C(c.Acc);
                foreach (float x in new[] { x0, x1 })
                    foreach (float y in new[] { y0, y1 })
                        foreach (float z in new[] { z0, z1 })
                            mb.Block(new(x, y, z), new(t + 0.06f, t + 0.06f, t + 0.06f));
            }
        });
    }

    private static void Stripes(Models.Mb mb, float x0, float x1, float z, float w, float y, int n, Color a, Color b)
    {
        float k = w * 0.9f, s = (x1 - x0 - k) / n;
        for (int i = 0; i < n; i++)
        {
            float xa = x0 + i * s;
            mb.C(i % 2 == 0 ? a : b).Extrude(new[] { new Vector2(xa, z), new Vector2(xa + s, z), new Vector2(xa + s + k, z + w), new Vector2(xa + k, z + w) }, y, y + 0.03f);
        }
    }

    /// <summary>The upper half of an ellipsoid without a base cap, so from inside the module the glass is not seen at all.</summary>
    private static void Dome(Models.Mb mb, Vector3 c, Vector3 radii, int rings, int segs)
    {
        mb.At(default);
        Vector3 P(int i, int j)
        {
            float lat = Mathf.Pi / 2 * i / rings, lon = Mathf.Tau * j / segs;
            return c + new Vector3(MathF.Sin(lat) * MathF.Cos(lon) * radii.X, MathF.Cos(lat) * radii.Y, MathF.Sin(lat) * MathF.Sin(lon) * radii.Z);
        }
        for (int i = 0; i < rings; i++)
            for (int j = 0; j < segs; j++)
                mb.Quad(P(i, j), P(i, j + 1), P(i + 1, j + 1), P(i + 1, j), c);
    }

    private static void ZPrism(Models.Mb mb, float x, float y, float z0, float rb, float rt, float len, int sides, Color col)
    {
        // A prism along +Z: the builder's Y axis is rotated onto Z (local (x, y, z) becomes world (x, -z, y)).
        mb.At(default, Models.Deg(90, 0, 0)).C(col).Prism(new(x, z0, -y), rb, rt, len, sides);
        mb.At(default);
    }

    // ---- joints and exposed sides ------------------------------------------------------------------------

    private static readonly Color SolarCell = new(0.09f, 0.17f, 0.40f);
    private static readonly Color SolarLine = new(0.36f, 0.52f, 0.92f);

    /// <summary>
    /// Doorway frames where this module touches another one. Only the module with the lower coordinate on an axis (its +X and +Z sides)
    /// draws the frame, so a joint between two modules is drawn once, in the colour of the module that owns it.
    /// </summary>
    private static void Joints(Cx c)
    {
        foreach (bool lateral in new[] { true, false })
        {
            if (!c.Has(lateral ? Right : Rear)) continue;
            Models.Add(c.R, "Joint", $"{c.K}.joint.{lateral}", Models.Body(), mb =>
            {
                mb.At(default);
                float wall = lateral ? W - 0.14f : D - 0.14f; // the wall plane, a hair inside the module box
                float jh = c.Ship ? ShipDoorHeight : DoorHeight, w = DoorHalf; // a ship door is taller so the frame header clears the seated eye
                Vector3 P(float along, float y, float across = 0f) => lateral ? new(wall + across, y, along) : new(along, y, wall + across);
                Vector3 S(float len, float hh, float depth) => lateral ? new(depth, hh, len) : new(len, hh, depth);
                mb.C(c.Ship ? Panel : Steel);
                foreach (float s in new[] { -1f, 1f })
                    mb.Block(P(4 + s * (w + 0.15f), 0.08f + jh / 2), S(0.3f, jh, 0.46f));
                mb.Block(P(4, 0.08f + jh + 0.12f), S(2 * w + 0.6f, 0.24f, 0.46f));
                mb.C(c.Role).Block(P(4, 0.08f + jh + 0.27f), S(2 * w + 0.6f, 0.06f, 0.5f));
                mb.C(Dark).Block(P(4, 0.098f), S(2 * w, 0.01f, 0.5f)); // the sill
                mb.C(c.Role).Block(P(4 + w - 0.05f, 0.1f), S(0.1f, 0.012f, 0.5f));
                mb.Block(P(4 - w + 0.05f, 0.1f), S(0.1f, 0.012f, 0.5f));
            });
            Models.Add(c.R, "JointLight", $"{c.K}.jointlight.{lateral}", Models.Glow(c.Role, 1.6f), mb =>
            {
                mb.At(default);
                float wall = lateral ? W - 0.14f : D - 0.14f, y = 0.08f + (c.Ship ? ShipDoorHeight : DoorHeight) - 0.03f;
                float len = 2 * DoorHalf - 0.2f;
                mb.Block(lateral ? new(wall, y, 4) : new(4, y, wall), lateral ? new(0.2f, 0.05f, len) : new(len, 0.05f, 0.2f));
            });
        }
    }

    /// <summary>Windows, solar arrays, radiators, masts and fins on the sides of a module that touch nothing: what makes the outside of a bigger structure read as a ship or a station.</summary>
    private static void Exposed(Cx c)
    {
        if (c.Kind is ModuleKind.Workshop or ModuleKind.Defence or ModuleKind.DroneBay) { Beacon(c); return; } // open decks have no flank to decorate
        float roof = c.Kind switch
        {
            ModuleKind.Shield => 1.2f, ModuleKind.Sensor => 2.2f,
            ModuleKind.Reactor or ModuleKind.ReactorMk2 or ModuleKind.Core => c.Ship ? Roof : LowRoof,
            ModuleKind.Cockpit => c.Ship ? Roof : LowRoof,
            _ => Roof,
        };
        bool windowed = c.Kind is ModuleKind.Hull or ModuleKind.Cargo or ModuleKind.Cockpit or ModuleKind.Core or ModuleKind.Engine;
        if (windowed)
            Models.Add(c.R, "Windows", $"{c.K}.windows", Models.Glow(new Color(1f, 0.92f, 0.65f), 1.3f), mb =>
            {
                mb.At(default);
                float wy = c.Ship ? 2.25f : roof * 0.62f;
                foreach (float x in new[] { 0f, W })
                {
                    if (x == 0f ? c.Has(Left) : c.Has(Right)) continue;
                    float f = c.Ship ? 0.015f : StationFace + 0.0f, o = x == 0f ? f - 0.005f : W - f + 0.005f;
                    int n = c.Kind == ModuleKind.Cargo ? 3 : c.Kind == ModuleKind.Core ? 2 : 5;
                    for (int i = 0; i < n; i++)
                    {
                        float z = n == 5 ? 1.0f + i * 1.5f : 4f + (i - (n - 1) / 2f) * 2.2f;
                        if (c.Kind == ModuleKind.Engine && z > 5f) continue; // the tail tapers off
                        if (c.Ship && c.Kind == ModuleKind.Cockpit && z < 3.4f) continue; // the nose tapers
                        mb.Block(new(o, wy, z), new(0.05f, c.Ship ? 0.3f : 0.34f, c.Ship ? 0.62f : 0.9f));
                    }
                }
            });
        if (c.Ship) { ShipFins(c); NavLights(c); } else StationRigging(c, roof);
        Beacon(c);
    }

    /// <summary>A small light on top of a module that has nothing above it: a lit roof, so the structure shows in the dark.</summary>
    private static void Beacon(Cx c)
    {
        if (c.Has(Up) || c.Kind != ModuleKind.Hull && c.Kind != ModuleKind.Core) return;
        float y = c.Kind == ModuleKind.Core ? 3.0f : Roof;
        if (c.Ship && c.Kind == ModuleKind.Hull) return; // the ship's roof carries its spine stripe instead
        Models.Add(c.R, "GlowBlinkBeacon", $"{c.K}.beacon", Models.Glow(c.Role, 2.2f), mb =>
            mb.At(default).Block(new(c.Ship ? 4f : 6.9f, y + (c.Kind == ModuleKind.Core ? 0.01f : 0.2f), c.Ship ? 4f : 1.1f), new(0.2f, 0.2f, 0.2f)));
    }

    /// <summary>Port (red) and starboard (green) running lights on the flanks of a ship cockpit and engine, pulsing with the other glows.</summary>
    private static void NavLights(Cx c)
    {
        if (c.Kind != ModuleKind.Cockpit && c.Kind != ModuleKind.Engine) return;
        foreach (bool port in new[] { true, false })
        {
            if (port ? c.Has(Left) : c.Has(Right)) continue;
            var col = port ? new Color(1f, 0.15f, 0.12f) : new Color(0.2f, 1f, 0.35f);
            float x = port ? -0.02f : W + 0.02f, z = c.Kind == ModuleKind.Cockpit ? 3.9f : 1.0f;
            Models.Add(c.R, "GlowBlinkNav", $"{c.K}.nav.{port}", Models.Glow(col, 3.2f), mb =>
                mb.At(default).Block(new(x, 2.55f, z), new(0.12f, 0.12f, 0.12f)));
        }
    }

    private static void ShipFins(Cx c)
    {
        if (c.Kind != ModuleKind.Hull && c.Kind != ModuleKind.Cargo && c.Kind != ModuleKind.Reactor) return;
        Models.Add(c.R, "Fins", $"{c.K}.fins", Models.Body(), mb =>
        {
            mb.At(default);
            foreach (float s in new[] { -1f, 1f })
            {
                if (s < 0 ? c.Has(Left) : c.Has(Right)) continue;
                float x0 = s < 0 ? 0.05f : W - 0.05f, x1 = s < 0 ? -1.9f : W + 1.9f, y = c.Kind == ModuleKind.Hull ? 1.35f : 1.0f;
                // a swept stub wing: a plate in the trim colour with a role-colour tip
                mb.C(White).Extrude(new[] { new Vector2(x0, 1.2f), new Vector2(x1, 4.9f), new Vector2(x1, 6.0f), new Vector2(x0, 6.6f) }, y, y + 0.14f);
                mb.C(c.Acc).Extrude(new[] { new Vector2(x1 - s * 0.02f, 4.9f), new Vector2(x1, 4.9f), new Vector2(x1, 6.0f), new Vector2(x1 - s * 0.02f, 6.0f) }, y - 0.02f, y + 0.16f);
                mb.C(c.Role).Block(new(x1, y + 0.07f, 5.45f), new(0.16f, 0.2f, 1.2f));
            }
        });
    }

    private static void StationRigging(Cx c, float roof)
    {
        // Solar arrays on exposed flanks of hull and cargo modules: a boom with two panels.
        if (c.Kind is ModuleKind.Hull or ModuleKind.Cargo or ModuleKind.Reactor or ModuleKind.ReactorMk2)
            Models.Add(c.R, "Rigging", $"{c.K}.rigging", Models.Body(), mb =>
            {
                mb.At(default);
                foreach (float s in new[] { -1f, 1f })
                {
                    if (s < 0 ? c.Has(Left) : c.Has(Right)) continue;
                    float xw = s < 0 ? StationFace : W - StationFace, dir = s;
                    float y = roof * 0.55f;
                    if (c.Kind is ModuleKind.Reactor or ModuleKind.ReactorMk2)
                    {
                        // radiator fins: dark plates with a hot edge, standing out of the flank
                        for (int i = 0; i < 3; i++)
                        {
                            float z = 1.6f + i * 2.4f;
                            mb.C(Slab).Block(new(xw + dir * 0.7f, y, z), new(1.4f, roof * 0.55f, 0.1f));
                            mb.C(c.Role).Block(new(xw + dir * 1.4f, y, z), new(0.06f, roof * 0.55f, 0.14f));
                        }
                        continue;
                    }
                    mb.C(Steel).Beam(new(xw, y, 4f), new(xw + dir * 1.6f, y, 4f), 0.16f);
                    mb.Block(new(xw + dir * 1.6f, y, 4f), new(0.14f, 0.14f, 6.4f));
                    foreach (float z in new[] { 1.9f, 6.1f })
                    {
                        mb.C(SolarCell).Block(new(xw + dir * 1.6f, y, z), new(0.08f, 2.6f, 3.4f));
                        mb.C(SolarLine);
                        for (int i = -1; i <= 1; i++) mb.Block(new(xw + dir * 1.6f + dir * 0.045f, y + i * 0.85f, z), new(0.02f, 0.05f, 3.4f));
                        for (int i = -1; i <= 1; i++) mb.Block(new(xw + dir * 1.6f + dir * 0.045f, y, z + i * 1.1f), new(0.02f, 2.6f, 0.05f));
                    }
                }
            });
        // An antenna dish on the roof of a hull or cargo module with nothing above it.
        if (c.Kind is ModuleKind.Hull or ModuleKind.Cargo && !c.Has(Up))
            Models.Add(c.R, "Mast", $"{c.K}.antenna", Models.Body(), mb =>
            {
                mb.At(default).C(Steel).Prism(new(1.4f, roof, 1.4f), 0.22f, 0.16f, 1.2f, 8);
                mb.C(c.Role).Prism(new(1.4f, roof + 1.2f, 1.4f), 0.2f, 0.2f, 0.06f, 8);
                mb.At(new(1.4f, roof + 1.45f, 1.4f), Models.Deg(0, 0, -50)).C(Panel).Prism(new(0, 0, 0), 0.1f, 0.9f, 0.45f, 14);
                mb.C(Dark).Prism(new(0, 0.44f, 0), 0.75f, 0.75f, 0.03f, 14);
                mb.At(default);
            });
    }

    // ---- kinds -------------------------------------------------------------------------------------------

    /// <summary>The three upper edges of the hull section (both chamfers and the top): a cockpit has glass there instead of a solid roof.</summary>
    private const int RoofEdges = (1 << 3) | (1 << 4) | (1 << 5);

    private static void Cockpit(Cx c)
    {
        float roof = c.Ship ? Roof : LowRoof;
        bool nose = c.Ship && !c.Has(Front);
        Skin(c, roof, nose: nose, glassRoof: c.Ship && !c.Has(Up));
        if (c.Ship && !c.Has(Up))
            Models.Add(c.R, "RoofGlass", $"{c.K}.roofglass.{roof}.{nose}", Models.GlassMat(), mb =>
            {
                mb.At(default);
                var full = Oct(roof, 1f, c.M);
                float a = nose ? 3.4f : 0.05f, b = D - 0.05f;
                int only = ~RoofEdges & 0xFF; // skip every edge except the roof ones
                if (nose) Loft(mb, Oct(roof, 0.28f, c.M), 0.05f, full, a, false, false, only);
                Loft(mb, full, a, full, b, false, false, only);
            });
        Frame(c, roof, nose: nose);
        if (c.Ship)
            Models.Add(c.R, "Canopy", $"{c.K}.canopy", Models.GlassMat(), mb => Dome(mb, new(4, 2.9f, 2.9f), new(1.5f, 1.1f, 2.1f), 4, 10));
        else
            Models.Add(c.R, "Canopy", $"{c.K}.canopy", Models.GlassMat(), mb => Dome(mb, new(4, 3.05f, 4), new(3.4f, 0.85f, 3.4f), 3, 12));

        Models.Add(c.R, "Seat", $"{c.K}.seat", Models.Body(), mb =>
        {
            mb.At(default).C(Dark).Block(new(4, 0.4f, 4.6f), new(0.5f, 0.7f, 0.5f));
            mb.C(Steel).Block(new(4, 0.82f, 4.6f), new(1.3f, 0.3f, 1.3f));
            mb.C(c.Acc).Block(new(4, 1.55f, 5.25f), new(1.3f, 1.6f, 0.26f));
            mb.C(Steel).Block(new(4, 2.5f, 5.3f), new(0.8f, 0.45f, 0.2f));
            foreach (float s in new[] { -1f, 1f })
                mb.C(Dark).Block(new(4 + s * 0.78f, 1.15f, 4.6f), new(0.14f, 0.2f, 1.0f));
            mb.C(Dark).Beam(new(3.4f, 0.1f, 3.5f), new(3.4f, 1.2f, 3.5f), 0.1f);
            mb.C(Steel).Block(new(3.4f, 1.28f, 3.5f), new(0.28f, 0.14f, 0.28f));
        });
        Models.Add(c.R, "Console", $"{c.K}.console", Models.Body(), mb =>
        {
            mb.At(default).C(Dark).Block(new(4, 0.55f, 2.25f), new(3.6f, 1.0f, 1.0f), 1f, 0.45f);
            mb.C(Steel).Block(new(4, 1.08f, 2.25f), new(3.7f, 0.08f, 0.55f));
        });
        Models.Add(c.R, "GlowDash", $"{c.K}.dash", Models.Glow(new Color(0.3f, 0.8f, 1f), 2.2f), mb =>
        {
            mb.At(default).Block(new(4, 1.14f, 2.2f), new(2.2f, 0.04f, 0.38f));
            foreach (float s in new[] { -1f, 1f }) mb.Block(new(4 + s * 1.4f, 1.14f, 2.2f), new(0.6f, 0.04f, 0.38f));
        });
    }

    private static void Engine(Cx c)
    {
        bool tail = c.Ship && !c.Has(Rear);
        Skin(c, Roof, tail: tail);
        Frame(c, Roof, tail: tail);
        Models.Add(c.R, "Machinery", $"{c.K}.machinery", Models.Body(), mb =>
        {
            mb.At(default).C(Dark).Block(new(4, 0.8f, 2.4f), new(5.2f, 1.4f, 3.2f));
            mb.C(c.Acc).Block(new(4, 1.52f, 2.4f), new(5.3f, 0.1f, 3.3f));
            mb.C(Steel);
            foreach (float s in new[] { -1f, 1f })
            {
                mb.Beam(new(4 + s * 1.5f, 1.5f, 3.9f), new(4 + s * 1.5f, 1.5f, 5.3f), 0.35f);
                mb.Beam(new(4 + s * 1.5f, 0.3f, 3.9f), new(4 + s * 1.5f, 0.3f, 5.3f), 0.35f);
            }
        });
        Models.Add(c.R, "GlowVent", $"{c.K}.vent", Models.Glow(new Color(1f, 0.45f, 0.12f), 1.8f), mb =>
        {
            mb.At(default);
            foreach (float s in new[] { -1f, 1f })
                for (int i = 0; i < 3; i++)
                    mb.Block(new(4 + s * 2.62f, 0.85f, 1.4f + i * 1.0f), new(0.06f, 0.9f, 0.45f));
        });
        foreach (float x in new[] { 2.7f, 5.3f })
            Nozzle(c, x, 1.95f);
    }

    private static void Nozzle(Cx c, float x, float y)
    {
        Models.Add(c.R, "Nozzle", $"{c.K}.nozzle", Models.Body(), mb =>
        {
            ZPrism(mb, x, y, 5.1f, 1.05f, 1.05f, 0.5f, 10, Panel);
            ZPrism(mb, x, y, 5.6f, 0.8f, 1.3f, 1.4f, 10, new Color(0.20f, 0.22f, 0.28f));
            ZPrism(mb, x, y, 6.95f, 1.36f, 1.36f, 0.1f, 10, Steel);
            ZPrism(mb, x, y, 6.98f, 1.1f, 1.1f, 0.06f, 10, new Color(0.75f, 0.26f, 0.08f));
            mb.C(c.Acc);
            for (int i = 0; i < 4; i++)
            {
                float a = Mathf.Tau * i / 4 + 0.4f;
                mb.Beam(new(x + MathF.Cos(a) * 1.0f, y + MathF.Sin(a) * 1.0f, 5.2f), new(x + MathF.Cos(a) * 1.25f, y + MathF.Sin(a) * 1.25f, 6.4f), 0.14f);
            }
        });
        // The flame lies along local +X and the node is turned so that +X points to world +Z: Animate scales it along X.
        Models.Add(c.R, "GlowEngine", "mod.flame", Models.Glow(new Color(0.25f, 0.65f, 1f), 3.5f), mb =>
        {
            mb.At(default, Models.Deg(0, 0, -90)).Prism(default, 1.05f, 0f, 1.0f, 10);
        }, new Vector3(x, y, 7.0f), new Vector3(0, -90, 0));
        // a white-hot core inside the flame, and a long faint trail behind it (both scaled with the thrust level by Animate; the trail only shows while thrusting)
        Models.Add(c.R, "GlowEngineCore", "mod.flame.core", Models.Glow(new Color(0.85f, 0.97f, 1f), 5f), mb =>
        {
            mb.At(default, Models.Deg(0, 0, -90)).Prism(default, 0.55f, 0f, 0.8f, 8);
        }, new Vector3(x, y, 7.0f), new Vector3(0, -90, 0));
        Models.Add(c.R, "GlowEngineTrail", "mod.flame.trail", Models.GlowAlpha(new Color(0.35f, 0.7f, 1f), 0.9f, 0.08f), mb =>
        {
            mb.At(default, Models.Deg(0, 0, -90)).Prism(new(0, 0.8f, 0), 0.9f, 0.05f, 6.5f, 12);
        }, new Vector3(x, y, 7.0f), new Vector3(0, -90, 0));
    }

    private static void Reactor(Cx c)
    {
        Skin(c, LowRoof);
        Frame(c, LowRoof);
        Models.Add(c.R, "Containment", $"{c.K}.containment", Models.Body(), mb =>
        {
            mb.At(default).C(Steel).Prism(new(4, 0.08f, 4), 1.5f, 1.3f, 0.35f, 10);
            foreach (float y in new[] { 1.0f, 2.2f, 3.2f })
                mb.C(y > 3 ? c.Acc : Steel).Torus(new(4, y, 4), 1.0f, 0.14f, 10, 4);
            // four coolant pipes with elbows into the vessel
            foreach (float sx in new[] { -1f, 1f })
                foreach (float sz in new[] { -1f, 1f })
                {
                    float px = 4 + sx * 2.8f, pz = 4 + sz * 2.8f;
                    mb.C(Panel).Prism(new(px, 0.08f, pz), 0.26f, 0.26f, 2.7f, 8);
                    mb.C(c.Acc).Prism(new(px, 0.9f, pz), 0.3f, 0.3f, 0.14f, 8);
                    mb.C(c.Acc).Prism(new(px, 1.9f, pz), 0.3f, 0.3f, 0.14f, 8);
                    mb.C(Steel).Beam(new(px, 0.5f, pz), new(4 + sx * 0.9f, 0.5f, 4 + sz * 0.9f), 0.2f);
                }
        });
        Models.Add(c.R, "GlowCore", $"{c.K}.core", Models.Glow(Green, 2.4f), mb =>
            mb.At(default).Prism(new(4, 0.4f, 4), 0.85f, 0.85f, 3.45f, 10));
        // vents on the hull sides tell the reactor from outside
        Models.Add(c.R, "GlowVent", $"{c.K}.vent", Models.Glow(Green, 1.4f), mb =>
        {
            mb.At(default);
            foreach (float s in new[] { (c.Ship ? 0.05f : StationFace) - 0.02f, W - (c.Ship ? 0.05f : StationFace) + 0.02f })
                for (int i = 0; i < 3; i++)
                    mb.Block(new(s, 1.55f, 1.6f + i * 2.4f), new(0.05f, 0.9f, 0.7f));
        });
    }

    private static void Cargo(Cx c)
    {
        Skin(c, Roof, detail: false);
        Frame(c, Roof);
        Models.Add(c.R, "Hatch", $"{c.K}.hatch", Models.Body(), mb =>
        {
            mb.At(default).C(Dark).Block(new(4, Roof + 0.04f, 4), new(3.0f, 0.05f, 3.0f));
            mb.C(Yellow);
            foreach (float s in new[] { -1f, 1f })
            {
                mb.Block(new(4 + s * 1.5f, Roof + 0.05f, 4), new(0.2f, 0.07f, 3.2f));
                mb.Block(new(4, Roof + 0.05f, 4 + s * 1.5f), new(3.2f, 0.07f, 0.2f));
            }
            mb.C(Steel).Block(new(4, Roof + 0.08f, 4), new(0.5f, 0.05f, 0.5f));
            // side cargo doors with hazard bars
            foreach (float x in new[] { 0.0f, W })
            {
                float f = c.Ship ? 0.05f : StationFace;
                float o = x == 0 ? f - 0.02f : W - f + 0.02f;
                mb.C(Dark).Block(new(o, 1.95f, 4), new(0.06f, 1.5f, 3.4f));
                mb.C(Yellow);
                for (int i = 0; i < 4; i++) mb.Block(new(o + (x == 0 ? -0.02f : 0.02f), 1.95f, 2.7f + i * 0.87f), new(0.05f, 1.5f, 0.12f));
            }
        });
        Models.Add(c.R, "Cargo", $"{c.K}.cargo", Models.Body(), mb =>
        {
            mb.At(default);
            // a three-level rack along the left wall
            mb.C(Steel);
            for (int i = 0; i < 5; i++) foreach (float s in new[] { 0.35f, 1.55f }) mb.Beam(new(s, 0.1f, 0.5f + i * 1.75f), new(s, 3.1f, 0.5f + i * 1.75f), 0.1f);
            foreach (float y in new[] { 0.9f, 1.9f, 2.9f })
                mb.C(c.Acc).Block(new(0.95f, y, 4), new(1.4f, 0.07f, 7.3f));
            // boxes on the shelves, layout by variant
            for (int lvl = 0; lvl < 3; lvl++)
                for (int i = 0; i < 6; i++)
                {
                    if ((i * 7 + lvl * 3 + c.V) % 5 == 0) continue;
                    float h = 0.45f + ((i + lvl + c.V) % 3) * 0.15f;
                    mb.C(((i + lvl) & 1) == 0 ? Crate : CrateDk).Block(new(0.95f, 0.935f + lvl + h / 2, 0.8f + i * 1.2f), new(0.95f, h, 0.8f));
                }
            // stacked crates against the right wall
            float[] zs = { 1.3f, 2.7f, 4.1f };
            for (int i = 0; i < zs.Length; i++)
            {
                int stack = 1 + (i + c.V) % 3;
                for (int s = 0; s < stack; s++)
                {
                    mb.C((i + s) % 2 == 0 ? Crate : CrateDk).Block(new(6.7f, 0.6f + s * 1.15f, zs[i]), new(1.2f, 1.1f, 1.2f));
                    mb.C(Yellow).Block(new(6.7f, 0.6f + s * 1.15f, zs[i]), new(1.24f, 0.2f, 1.24f));
                }
            }
            mb.C(CrateDk).Block(new(5.5f, 0.55f, 6.5f), new(2.2f, 1.0f, 1.2f));
            mb.C(c.Acc).Block(new(5.5f, 1.06f, 6.5f), new(2.24f, 0.06f, 1.24f));
        });
    }

    private static void Workshop(Cx c)
    {
        Frame(c, Roof);
        Models.Add(c.R, "Rails", $"{c.K}.rails", Models.Body(), mb =>
        {
            mb.At(default).C(Steel);
            foreach (float x in new[] { 1.2f, W - 1.2f })
                mb.Block(new(x, 0.12f, 4), new(0.14f, 0.07f, 7.6f));
            mb.C(Dark);
            for (int i = 0; i < 8; i++) mb.Block(new(4, 0.1f, 0.45f + i * 1.01f), new(W - 2.2f, 0.04f, 0.16f));
            Stripes(mb, 0.4f, W - 0.4f, 0.12f, 0.28f, 0.088f, 9, Yellow, Dark);
            Stripes(mb, 0.4f, W - 0.4f, D - 0.4f, 0.28f, 0.088f, 9, Yellow, Dark);
        });
        Models.Add(c.R, "Truss", $"{c.K}.truss", Models.Body(), mb =>
        {
            mb.At(default).C(c.Ship ? Panel : Steel);
            // two lattice girders along Z with a gantry rail across them
            foreach (float x in new[] { 1.8f, W - 1.8f })
            {
                float yt = Roof - 0.1f, yb = Roof - 0.65f;
                mb.Beam(new(x, yt, 0.2f), new(x, yt, D - 0.2f), 0.14f);
                mb.Beam(new(x, yb, 0.2f), new(x, yb, D - 0.2f), 0.14f);
                for (int i = 0; i < 7; i++)
                {
                    float z0 = 0.2f + i * (D - 0.4f) / 7, z1 = 0.2f + (i + 1) * (D - 0.4f) / 7;
                    mb.Beam(new(x, i % 2 == 0 ? yb : yt, z0), new(x, i % 2 == 0 ? yt : yb, z1), 0.09f);
                }
            }
            mb.C(c.Acc);
            foreach (float z in new[] { 0.3f, D - 0.3f })
                mb.Block(new(4, Roof - 0.1f, z), new(W - 3.4f, 0.16f, 0.16f));
            mb.C(Dark).Block(new(4, Roof - 0.7f, 4), new(W - 3.4f, 0.16f, 0.2f));
            // the trolley and a hook hang from the gantry
            mb.C(Yellow).Block(new(4.6f, Roof - 0.85f, 4), new(0.7f, 0.2f, 0.5f));
            mb.C(Steel).Beam(new(4.6f, Roof - 0.95f, 4), new(4.6f, 2.7f, 4), 0.05f);
            mb.C(Yellow).Block(new(4.6f, 2.62f, 4), new(0.2f, 0.18f, 0.2f));
        });
        Models.Add(c.R, "GlowLamp", $"{c.K}.lamps", Models.Glow(new Color(1f, 0.93f, 0.75f), 1.6f), mb =>
        {
            mb.At(default);
            foreach (float x in new[] { 1.8f, W - 1.8f })
                foreach (float z in new[] { 1.6f, 4.0f, 6.4f })
                    mb.Block(new(x, Roof - 0.78f, z), new(0.4f, 0.06f, 0.4f));
        });
        if (c.Ship)
            Models.Add(c.R, "Skirt", $"{c.K}.skirt", Models.Body(), mb =>
            {
                mb.At(default).C(White);
                foreach (float x in new[] { 0.2f, W - 0.2f })
                    mb.Block(new(x, 0.4f, 4), new(0.3f, 0.7f, D - 0.2f), 0.4f, 1f);
                mb.C(c.Acc);
                foreach (float x in new[] { 0.1f, W - 0.1f })
                    mb.Block(new(x, 0.62f, 4), new(0.08f, 0.1f, D - 0.4f));
            });
        else
            Models.Add(c.R, "Kerb", $"{c.K}.kerb", Models.Body(), mb =>
            {
                mb.At(default).C(Slab);
                foreach (float x in new[] { 0.2f, W - 0.2f })
                    mb.Block(new(x, 0.35f, 4), new(0.3f, 0.55f, D - 0.2f), 0.7f, 1f);
            });
    }

    private static void Defence(Cx c)
    {
        Models.Add(c.R, "Parapet", $"{c.K}.parapet", Models.Body(), mb =>
        {
            mb.At(default);
            float top = c.Ship ? 0.3f : 0.75f;
            mb.C(c.Ship ? White : Slab);
            foreach (float x in new[] { 0.3f, W - 0.3f })
                mb.Block(new(x, 0.62f, 4), new(0.5f, 1.1f, D - 0.1f), top, 1f);
            mb.Block(new(4, 0.62f, D - 0.3f), new(W - 0.9f, 1.1f, 0.5f), 1f, top);
            mb.Block(new(4, 0.62f, 0.3f), new(W - 0.9f, 1.1f, 0.5f), 1f, top);
            mb.C(c.Acc);
            foreach (float x in new[] { 0.3f, W - 0.3f })
                mb.Block(new(x, 1.19f, 4), new(0.18f, 0.06f, D - 0.5f));
            if (!c.Ship)
            {
                mb.C(Steel);
                foreach (float x in new[] { 0.3f, W - 0.3f })
                    for (int i = 0; i < 4; i++) mb.Block(new(x, 1.3f, 0.9f + i * 2.07f), new(0.34f, 0.22f, 0.7f));
            }
            Stripes(mb, 1.0f, W - 1.0f, 1.0f, 0.45f, 0.088f, 8, Yellow, Dark);
            Stripes(mb, 1.0f, W - 1.0f, D - 1.45f, 0.45f, 0.088f, 8, Yellow, Dark);
        });
        Frame(c, Roof);
        var spots = c.Ship
            ? new[] { new Vector3(4, 0, 2.5f), new Vector3(4, 0, 5.5f) }
            : new[] { new Vector3(2.5f, 0, 2.5f), new Vector3(5.5f, 0, 2.5f), new Vector3(2.5f, 0, 5.5f), new Vector3(5.5f, 0, 5.5f) };
        for (int i = 0; i < spots.Length; i++) Turret(c, i, spots[i]);
    }

    private static void Turret(Cx c, int n, Vector3 pos)
    {
        var t = new Node3D { Name = $"Turret{n}", Position = pos };
        c.R.AddChild(t);
        Models.Add(t, "Mount", $"{c.K}.mount", Models.Body(), mb =>
        {
            mb.At(default).C(Dark).Prism(new(0, 0.07f, 0), 0.95f, 0.95f, 0.2f, 8);
            mb.C(Steel).Prism(new(0, 0.27f, 0), 0.8f, 0.55f, 0.35f, 8);
            mb.C(c.Acc).Prism(new(0, 0.26f, 0), 0.82f, 0.82f, 0.03f, 8);
        });
        var head = Models.Pivot(t, "Head", new(0, 0.9f, 0));
        head.RotationDegrees = new Vector3(0, 90, 0); // faces -Z (the ship's front) until something aims it
        Models.Add(head, "HeadMesh", $"{c.K}.head", Models.Body(), mb =>
        {
            mb.At(default).C(c.Ship ? White : Steel).Prism(new(0, -0.2f, 0), 0.62f, 0.5f, 0.45f, 8);
            mb.C(Dark).Sphere(new(0, 0.24f, 0), new(0.4f, 0.26f, 0.4f), 2, 8, dome: true);
            mb.C(c.Acc).Block(new(0.3f, 0.05f, 0), new(0.3f, 0.12f, 0.9f));
        });
        var barrel = Models.Pivot(head, "Barrel", new(0.25f, 0.08f, 0));
        Models.Add(barrel, "BarrelMesh", $"{c.K}.barrel", Models.Body(), mb =>
        {
            mb.At(default).C(Dark);
            foreach (float z in new[] { -0.17f, 0.17f })
            {
                mb.Block(new(0.8f, 0, z), new(1.6f, 0.15f, 0.15f));
                mb.C(Steel).Block(new(1.62f, 0, z), new(0.22f, 0.22f, 0.22f));
                mb.C(Dark);
            }
        });
        Models.Add(barrel, "GlowMuzzle", "mod.muzzle", Models.Glow(new Color(1f, 0.5f, 0.15f), 2f), mb =>
        {
            mb.At(default);
            foreach (float z in new[] { -0.17f, 0.17f }) mb.Block(new(1.75f, 0, z), new(0.04f, 0.1f, 0.1f));
        });
    }

    private static readonly Color Cyan = new(0.45f, 0.92f, 1f);
    private static readonly Color FieldBlue = new(0.30f, 0.62f, 1f);
    private static readonly Color RadarGreen = new(0.30f, 1f, 0.55f);

    /// <summary>
    /// A low plated base with an emitter pylon and a faint blue translucent field dome. Parts: "Ring" (the emitter halo, spun by
    /// <see cref="Models.Animate"/>), "GlowField" (the dome) and "GlowEmitter" (the pylon tip and prongs), which pulse with the power.
    /// </summary>
    private static void Shield(Cx c)
    {
        const float roof = 1.2f;
        Skin(c, roof, detail: true, collars: false);
        Frame(c, roof);
        Models.Add(c.R, "Pylon", $"{c.K}.pylon", Models.Body(), mb =>
        {
            mb.At(default).C(Dark).Prism(new(4, roof, 4), 1.5f, 1.2f, 0.2f, 8);
            mb.C(Steel).Prism(new(4, roof + 0.2f, 4), 0.6f, 0.38f, 1.4f, 8);
            mb.C(c.Acc).Prism(new(4, roof + 0.55f, 4), 0.66f, 0.66f, 0.1f, 8);
            // four emitter prongs leaning out of the base, each ending in a lamp
            for (int i = 0; i < 4; i++)
            {
                float a = Mathf.Tau * i / 4 + Mathf.Pi / 4;
                mb.C(c.Ship ? Panel : Steel).Beam(new(4 + MathF.Cos(a) * 2.4f, roof, 4 + MathF.Sin(a) * 2.4f), new(4 + MathF.Cos(a) * 0.9f, roof + 1.2f, 4 + MathF.Sin(a) * 0.9f), 0.16f);
            }
        });
        Models.Add(c.R, "GlowEmitter", $"{c.K}.emitter", Models.Glow(FieldBlue, 2.6f), mb =>
        {
            mb.At(default).Sphere(new(4, roof + 1.75f, 4), new(0.42f, 0.42f, 0.42f), 3, 10);
            for (int i = 0; i < 4; i++)
            {
                float a = Mathf.Tau * i / 4 + Mathf.Pi / 4;
                mb.Sphere(new(4 + MathF.Cos(a) * 2.4f, roof + 0.12f, 4 + MathF.Sin(a) * 2.4f), new(0.2f, 0.2f, 0.2f), 2, 6);
            }
        });
        Models.Add(c.R, "Ring", $"{c.K}.halo", Models.Glow(Cyan, 1.8f), mb =>
        {
            mb.At(default, Models.Deg(18, 0, 0)).Torus(default, 1.0f, 0.06f, 16, 4);
            mb.At(default, Models.Deg(0, 0, 18)).Torus(default, 0.8f, 0.05f, 14, 4);
        }, new Vector3(4, roof + 1.75f, 4));
        Models.Add(c.R, "GlowField", $"{c.K}.field", Models.GlowAlpha(FieldBlue, 0.9f, 0.16f), mb => Dome(mb, new(4, roof, 4), new(3.7f, 2.7f, 3.7f), 6, 20));
    }

    /// <summary>
    /// A sensor tower: a low plated base, a mast and a dish that turns with a translucent green radar sweep. Parts: "Dish" (the
    /// turning pivot with the bowl and the sweep), "GlowSweep" (the sweep wedge) and "GlowLamp" (the mast light).
    /// </summary>
    private static void Sensor(Cx c)
    {
        const float roof = 2.2f;
        Skin(c, roof, detail: true, collars: false);
        Frame(c, roof);
        Models.Add(c.R, "Mast", $"{c.K}.mast", Models.Body(), mb =>
        {
            mb.At(default).C(Dark).Prism(new(4, roof, 4), 1.2f, 1.0f, 0.14f, 8);
            mb.C(Steel).Prism(new(4, roof + 0.1f, 4), 0.28f, 0.2f, 0.62f, 8);
            mb.C(c.Acc).Prism(new(4, roof + 0.32f, 4), 0.34f, 0.34f, 0.07f, 8);
        });
        var dish = Models.Pivot(c.R, "Dish", new(4, roof + 0.78f, 4));
        Models.Add(dish, "Bowl", $"{c.K}.bowl", Models.Body(), mb =>
        {
            // a dish facing +X and tilted up: a cone that widens towards the front, a dark inner face, a feed horn on three struts
            mb.At(new(0, 0.28f, 0), Models.Deg(0, 0, -52)).C(c.Ship ? White : Panel).Prism(new(0, 0, 0), 0.3f, 1.75f, 0.6f, 16);
            mb.C(c.Acc).Prism(new(0, 0.6f, 0), 1.75f, 1.75f, 0.05f, 16);
            mb.C(Dark).Prism(new(0, 0.6f, 0), 1.5f, 1.5f, 0.07f, 16);
            mb.C(Steel);
            foreach (float a in new[] { 0f, 2.09f, 4.19f })
                mb.Beam(new(MathF.Cos(a) * 1.6f, 0.62f, MathF.Sin(a) * 1.6f), new(0, 1.5f, 0), 0.06f);
            mb.C(c.Acc).Sphere(new(0, 1.55f, 0), new(0.14f, 0.14f, 0.14f), 2, 6);
        });
        // the sweep lies flat just above the roof and turns with the dish; its wedge opens along +X
        Models.Add(dish, "GlowSweep", $"{c.K}.sweep", Models.GlowAlpha(RadarGreen, 1.6f, 0.22f), mb =>
        {
            var pts = new System.Collections.Generic.List<Vector2> { new(0.6f, 0f) };
            for (int i = 0; i <= 6; i++)
            {
                float a = Mathf.DegToRad(-24f + 48f * i / 6);
                pts.Add(new Vector2(MathF.Cos(a) * 3.6f, MathF.Sin(a) * 3.6f));
            }
            mb.At(default).Extrude(pts.ToArray(), -0.74f, -0.72f);
        });
        Models.Add(c.R, "GlowLamp", $"{c.K}.lamp", Models.Glow(RadarGreen, 2.4f), mb =>
            mb.At(default).Sphere(new(4, roof + 0.7f, 5.6f), new(0.12f, 0.12f, 0.12f), 2, 6));
        // a row of status lights along the side of the base
        Models.Add(c.R, "GlowDash", $"{c.K}.status", Models.Glow(RadarGreen, 1.6f), mb =>
        {
            mb.At(default);
            foreach (float s in new[] { 1.0f, W - 1.0f })
                for (int i = 0; i < 4; i++) mb.Block(new(s, 2.24f, 1.4f + i * 1.7f), new(0.12f, 0.05f, 0.5f));
        });
    }

    /// <summary>
    /// A bigger reactor: a wide vessel with a taller cyan core inside two crossed rings. Parts: "GlowCore" (breathes while burning, dim red
    /// when dark, like the reactor), "Ring" (the two crossed rings, spun while burning) and "GlowVent" (hull vents).
    /// </summary>
    private static void ReactorMk2(Cx c)
    {
        Skin(c, LowRoof);
        Frame(c, LowRoof);
        Models.Add(c.R, "Containment", $"{c.K}.containment", Models.Body(), mb =>
        {
            mb.At(default).C(Steel).Prism(new(4, 0.08f, 4), 1.95f, 1.7f, 0.4f, 12);
            mb.C(c.Acc).Prism(new(4, 0.46f, 4), 1.98f, 1.98f, 0.06f, 12);
            foreach (float y in new[] { 1.0f, 3.0f })
                mb.C(y > 2 ? c.Acc : Steel).Torus(new(4, y, 4), 1.35f, 0.16f, 12, 4);
            // six heavy coolant pillars
            for (int i = 0; i < 6; i++)
            {
                float a = Mathf.Tau * i / 6 + 0.3f;
                float px = 4 + MathF.Cos(a) * 3.0f, pz = 4 + MathF.Sin(a) * 3.0f;
                mb.C(Panel).Prism(new(px, 0.08f, pz), 0.3f, 0.3f, 2.75f, 8);
                mb.C(c.Acc).Prism(new(px, 1.2f, pz), 0.34f, 0.34f, 0.14f, 8);
                mb.C(Steel).Beam(new(px, 0.5f, pz), new(4 + MathF.Cos(a) * 1.3f, 0.5f, 4 + MathF.Sin(a) * 1.3f), 0.22f);
            }
        });
        Models.Add(c.R, "GlowCore", $"{c.K}.core", Models.Glow(Cyan, 1.5f), mb =>
            mb.At(default).Prism(new(4, 0.46f, 4), 1.0f, 1.0f, 3.4f, 12));
        Models.Add(c.R, "Ring", $"{c.K}.rings", Models.Glow(Gold, 1.8f), mb =>
        {
            mb.At(default, Models.Deg(26, 0, 0)).Torus(default, 1.5f, 0.09f, 18, 4);
            mb.At(default, Models.Deg(0, 0, -26)).Torus(default, 1.35f, 0.08f, 18, 4);
        }, new Vector3(4, 3.3f, 4));
        Models.Add(c.R, "GlowVent", $"{c.K}.vent", Models.Glow(Cyan, 1.5f), mb =>
        {
            mb.At(default);
            foreach (float s in new[] { (c.Ship ? 0.05f : StationFace) - 0.02f, W - (c.Ship ? 0.05f : StationFace) + 0.02f })
                for (int i = 0; i < 3; i++)
                    mb.Block(new(s, 1.55f, 1.6f + i * 2.4f), new(0.05f, 0.9f, 0.7f));
        });
    }

    private static void HullModule(Cx c)
    {
        Skin(c, Roof, detail: true);
        Frame(c, Roof);
    }

    /// <summary>
    /// A small quad-rotor drone craft about a cell long, nose towards -Z so <c>Basis.LookingAt</c> flies it nose first. Used in the bay's
    /// cradles and by the game's flock of working drones. Parts: "Hull" (body, arms, rotor discs) and "GlowDrone" (nose lamp and thruster).
    /// </summary>
    public static Node3D DroneCraft(string name = "Drone")
    {
        var n = new Node3D { Name = name };
        Models.Add(n, "Hull", "mod.drone.hull", Models.Body(), mb =>
        {
            mb.At(default).C(White).Block(new(0, 0, 0), new(0.46f, 0.2f, 0.84f), 0.7f, 0.5f);
            mb.C(Dark).Block(new(0, 0.12f, 0.08f), new(0.3f, 0.07f, 0.38f));
            mb.C(Steel).Block(new(0, -0.14f, -0.3f), new(0.12f, 0.12f, 0.3f)); // the mining tool under the nose
            foreach (float sx in new[] { -1f, 1f })
                foreach (float sz in new[] { -1f, 1f })
                {
                    mb.C(Steel).Beam(new(sx * 0.18f, 0.02f, sz * 0.22f), new(sx * 0.52f, 0.06f, sz * 0.36f), 0.07f);
                    mb.C(Dark).Prism(new(sx * 0.52f, 0.06f, sz * 0.36f), 0.2f, 0.2f, 0.035f, 8);
                    mb.C(Amber).Prism(new(sx * 0.52f, 0.1f, sz * 0.36f), 0.05f, 0.05f, 0.05f, 6);
                }
        });
        Models.Add(n, "GlowDrone", "mod.drone.glow", Models.Glow(Amber, 2.6f), mb =>
        {
            mb.At(default).Block(new(0, 0.02f, -0.43f), new(0.22f, 0.1f, 0.05f)); // nose lamp
            mb.Block(new(0, 0, 0.43f), new(0.2f, 0.09f, 0.05f)); // thruster
        });
        return n;
    }

    /// <summary>
    /// An open hangar: low side walls, a taller back wall (+Z) with a glowing launch door, two launch rails, three cradles with a drone
    /// each (one cradle empty in variant 1: a drone is out), a gantry over them and an amber status light on a mast.
    /// </summary>
    private static void DroneBay(Cx c)
    {
        float wall = c.Ship ? 0.25f : StationFace + 0.1f;
        Frame(c, Roof);
        Models.Add(c.R, "Hangar", $"{c.K}.hangar", Models.Body(), mb =>
        {
            mb.At(default).C(c.Ship ? White : Slab);
            foreach (float x in new[] { wall, W - wall })
                mb.Block(new(x, 0.9f, 4), new(0.3f, 1.7f, D - 0.4f));
            mb.Block(new(4, 1.55f, D - wall - 0.1f), new(W - 0.4f, 3.0f, 0.3f));
            mb.C(c.Acc);
            foreach (float x in new[] { wall, W - wall })
                mb.Block(new(x, 1.78f, 4), new(0.34f, 0.07f, D - 0.5f));
            mb.Block(new(4, 3.08f, D - wall - 0.1f), new(W - 0.5f, 0.07f, 0.34f));
            mb.C(Dark).Block(new(4, 1.6f, D - wall - 0.27f), new(3.9f, 2.9f, 0.08f)); // the door frame, proud of the wall
            // launch rails and sleepers
            mb.C(Steel);
            foreach (float x in new[] { 2.5f, W - 2.5f })
                mb.Block(new(x, 0.17f, 3.7f), new(0.2f, 0.16f, 6.4f));
            mb.C(Dark);
            for (int i = 0; i < 7; i++) mb.Block(new(4, 0.1f, 0.7f + i * 0.9f), new(W - 4.6f, 0.06f, 0.2f));
            Stripes(mb, 1.0f, W - 1.0f, D - 1.4f, 0.4f, 0.088f, 8, Yellow, Dark);
            // three cradles
            mb.C(Steel);
            foreach (float z in new[] { 1.5f, 3.3f, 5.1f })
            {
                mb.Block(new(4, 0.3f, z), new(1.7f, 0.18f, 1.1f));
                foreach (float s in new[] { -1f, 1f }) mb.Block(new(4 + s * 0.75f, 0.45f, z), new(0.1f, 0.36f, 1.1f));
            }
            // a gantry over the cradles
            mb.C(c.Ship ? Panel : Steel);
            mb.Block(new(4, Roof - 0.2f, 3.3f), new(W - 1.2f, 0.14f, 0.16f));
            foreach (float x in new[] { 0.8f, W - 0.8f }) mb.Beam(new(x, 1.7f, 3.3f), new(x, Roof - 0.2f, 3.3f), 0.12f);
        });
        Models.Add(c.R, "GlowDoor", $"{c.K}.door", Models.Glow(Amber, 1.5f), mb =>
            mb.At(default).Block(new(4, 1.45f, D - wall - 0.33f), new(3.3f, 2.6f, 0.03f)));
        Models.Add(c.R, "DoorSlats", $"{c.K}.slats", Models.Body(), mb =>
        {
            mb.At(default).C(Dark);
            for (int i = 0; i < 5; i++) mb.Block(new(4, 0.45f + i * 0.52f, D - wall - 0.35f), new(3.4f, 0.07f, 0.03f));
        });
        Models.Add(c.R, "Mast", $"{c.K}.mast", Models.Body(), mb =>
        {
            mb.At(default).C(Steel).Prism(new(W - 0.9f, 0.08f, 1.0f), 0.14f, 0.1f, 1.6f, 8);
            mb.C(Dark).Prism(new(W - 0.9f, 1.62f, 1.0f), 0.2f, 0.2f, 0.1f, 8);
        });
        Models.Add(c.R, "GlowLamp", $"{c.K}.lamp", Models.Glow(Amber, 2.4f), mb =>
            mb.At(default).Sphere(new(W - 0.9f, 1.85f, 1.0f), new(0.17f, 0.17f, 0.17f), 3, 8));
        float[] zs = { 1.5f, 3.3f, 5.1f };
        for (int i = 0; i < zs.Length; i++)
        {
            if (c.V == 1 && i == 1) continue;
            var d = DroneCraft($"Drone{i}");
            d.Position = new Vector3(4, 0.63f, zs[i]);
            d.RotationDegrees = new Vector3(0, 180, 0); // nose towards +Z, the door
            c.R.AddChild(d);
        }
    }

    private static void Core(Cx c)
    {
        Skin(c, LowRoof, detail: false);
        Frame(c, LowRoof);
        Models.Add(c.R, "Pillar", $"{c.K}.pillar", Models.Body(), mb =>
        {
            mb.At(default).C(Dark).Prism(new(4, 0.08f, 4), 2.0f, 1.7f, 0.22f, 8);
            mb.C(Steel).Prism(new(4, 0.3f, 4), 1.1f, 0.8f, 0.5f, 8);
            mb.C(Panel).Prism(new(4, 0.8f, 4), 0.55f, 0.5f, 2.6f, 8);
            mb.C(Gold);
            foreach (float y in new[] { 1.1f, 2.0f }) mb.Prism(new(4, y, 4), 0.6f, 0.6f, 0.1f, 8);
            for (int i = 0; i < 4; i++)
            {
                float a = Mathf.Tau * i / 4 + Mathf.Pi / 4;
                mb.C(c.Ship ? Steel : Slab).Beam(new(4 + MathF.Cos(a) * 2.6f, 0.1f, 4 + MathF.Sin(a) * 2.6f), new(4 + MathF.Cos(a) * 0.6f, 1.2f, 4 + MathF.Sin(a) * 0.6f), 0.2f);
            }
        });
        var ring = Models.Add(c.R, "Ring", "mod.core.ring", Models.Glow(Gold, 1.6f), mb =>
        {
            mb.At(default).Torus(default, 1.25f, 0.1f, 14, 4);
            mb.Torus(new(0, 0.28f, 0), 0.8f, 0.07f, 12, 4);
        }, new Vector3(4, 3.3f, 4));
        ring.RotationDegrees = new Vector3(8, 0, 4);
        Models.Add(c.R, "CoreMarker", "mod.core.marker", Models.Glow(Gold, 2.6f), mb =>
            mb.At(default).Sphere(new(4, 3.58f, 4), new(0.34f, 0.34f, 0.34f), 2, 4));
    }
}
