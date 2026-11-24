using System.Numerics;
using Starfoundry.Sim;

namespace Starfoundry.Ui;

/// <summary>One stairwell: the shaft between the floor of an upper module and the floor of the module below it, in local cells of the structure.</summary>
public readonly record struct StairLink(float UpperY, float LowerY, float X0, float Z0, float X1, float Z1)
{
    public float CentreX => (X0 + X1) / 2;
    public float CentreZ => (Z0 + Z1) / 2;
}

/// <summary>What <see cref="Stairwell.Try"/> found: the body can climb, where it lands, and which way.</summary>
public readonly record struct StairMove(bool Up, Vector3 Destination, StairLink Link);

/// <summary>
/// Moving between decks (client only, engine-free). A module with a module right below it has a stairwell when both are of a kind that has room for
/// it (<see cref="Capable"/>): a square opening in the upper deck with a ladder down to the lower deck, in the same place for every kind
/// (<see cref="X0"/> to <see cref="Z1"/>, from the module's back-left corner). The body climbs by interacting while standing at the shaft: up from the
/// lower deck to the deck beside the opening, down from the upper deck to the floor under the ladder. A landing that is occupied (a machine or the
/// workbench standing on the spot) refuses the climb. The model draws the opening and ladder from the same constants, and the walking body keeps
/// the rule of <see cref="HullProfile"/> (the <see cref="HullProfile.StairDown"/> bit of the mask).
/// </summary>
public static class Stairwell
{
    /// <summary>The opening in the upper deck, in cells from the back-left corner of the module (at the front, clear of the furniture of every capable kind).</summary>
    public const float X0 = 3.2f, X1 = 4.8f, Z0 = 0.6f, Z1 = 2.0f;
    /// <summary>How far from the opening the body still reaches the ladder.</summary>
    public const float Reach = 0.6f;
    /// <summary>The distance behind the opening where the body steps off the ladder onto the upper deck.</summary>
    public const float LandingGap = 0.5f;

    /// <summary>The kinds with room for a stairwell at both ends: nothing of their furniture stands in the shaft. Ship hulls, cargo holds and fabrication bays, and a station's core.</summary>
    public static bool Capable(ModuleKind kind) => kind is ModuleKind.Hull or ModuleKind.Cargo or ModuleKind.Workshop or ModuleKind.Core;

    /// <summary>Every stairwell of a structure's volumes: a box whose shell carries <see cref="HullProfile.StairDown"/> and the box right below it.</summary>
    public static List<StairLink> Links(IReadOnlyList<BodyBox> volumes)
    {
        var list = new List<StairLink>();
        foreach (var b in volumes)
        {
            if (b.Shell is not { } sh || (sh.Mask & HullProfile.StairDown) == 0) continue;
            float lower = b.MinY - HullProfile.H;
            foreach (var o in volumes)
            {
                if (MathF.Abs(o.MinY - lower) > 0.01f || MathF.Abs(o.MinX - b.MinX) > 0.01f || MathF.Abs(o.MinZ - b.MinZ) > 0.01f) continue;
                list.Add(new StairLink(b.MinY, lower, b.MinX + X0, b.MinZ + Z0, b.MinX + X1, b.MinZ + Z1));
                break;
            }
        }
        return list;
    }

    private static bool Near(StairLink l, Vector3 p) => p.X > l.X0 - Reach && p.X < l.X1 + Reach && p.Z > l.Z0 - Reach && p.Z < l.Z1 + Reach;

    /// <summary>The ways up and down from where the body stands: null when there is no ladder within reach or the landing is occupied.</summary>
    public static StairMove? Try(Vector3 body, bool up, IReadOnlyList<BodyBox> volumes, IReadOnlyList<BodySolid>? solids = null)
    {
        foreach (var l in Links(volumes))
        {
            if (!Near(l, body)) continue;
            if (up && MathF.Abs(body.Y - l.LowerY) < 0.05f)
            {
                // onto the deck behind the opening (the ladder is on its rear edge), or any free spot round it
                foreach (var dest in Landings(l, l.UpperY, true))
                    if (BodyMotion.Contains(dest, volumes, solids)) return new StairMove(true, dest, l);
            }
            else if (!up && MathF.Abs(body.Y - l.UpperY) < 0.05f)
            {
                foreach (var dest in Landings(l, l.LowerY, false))
                    if (BodyMotion.Contains(dest, volumes, solids)) return new StairMove(false, dest, l);
            }
        }
        return null;
    }

    /// <summary>True when a ladder is within reach on this level, whichever way it goes (for the prompt).</summary>
    public static (bool Up, bool Down) Reachable(Vector3 body, IReadOnlyList<BodyBox> volumes)
    {
        bool up = false, down = false;
        foreach (var l in Links(volumes))
        {
            if (!Near(l, body)) continue;
            if (MathF.Abs(body.Y - l.LowerY) < 0.05f) up = true;
            if (MathF.Abs(body.Y - l.UpperY) < 0.05f) down = true;
        }
        return (up, down);
    }

    /// <summary>Candidate landing spots, best first: for the upper deck behind then beside the opening; for the lower deck under the ladder then round it.</summary>
    private static IEnumerable<Vector3> Landings(StairLink l, float y, bool onDeck)
    {
        float cx = l.CentreX, cz = l.CentreZ;
        if (onDeck)
        {
            float zr = l.Z1 + LandingGap, zf = l.Z0 - LandingGap;
            yield return new Vector3(cx, y, zr);
            yield return new Vector3(cx - 0.5f, y, zr);
            yield return new Vector3(cx + 0.5f, y, zr);
            yield return new Vector3(l.X1 + LandingGap, y, cz);
            yield return new Vector3(l.X0 - LandingGap, y, cz);
            yield return new Vector3(cx, y, zf);
        }
        else
        {
            yield return new Vector3(cx, y, l.Z1 - 0.4f);
            yield return new Vector3(cx, y, cz);
            yield return new Vector3(cx - 0.4f, y, cz);
            yield return new Vector3(cx + 0.4f, y, cz);
            yield return new Vector3(cx, y, l.Z1 + LandingGap);
        }
    }
}
