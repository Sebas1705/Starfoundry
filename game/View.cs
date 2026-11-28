using Godot;
using Starfoundry.Sim;
using Starfoundry.Ui;

namespace Starfoundry.Game;

/// <summary>
/// The floating origin of the client view (ADR 0053, world scale stage 1). The simulation keeps integer units (1/240 m, <see cref="Units.CellUnits"/> per cell)
/// and knows nothing about this class. Every place that turns a simulation position into a Godot <see cref="Vector3"/> for the world goes through here, so the value
/// is the 64-bit difference to the view origin converted to float only at the end; every place that turns a Godot world vector back into simulation units
/// (aim, tractor, build tool) goes through <see cref="ToUnits"/>. While the origin is (0, 0, 0) every value is bit-for-bit what the old
/// <c>View.Cells(x, y, z)</c> produced.
/// <para>
/// Rebase rule (Play.RebaseView): a node placed in world space that is NOT rewritten from the simulation every frame joins the group <see cref="WorldGroup"/>
/// and is shifted by the exact integer delta; a cached world vector field is shifted by the same delta; everything else (nodes rewritten every frame, the camera-relative
/// sky, dust and glow, screen-projected markers) is left alone.
/// </para>
/// </summary>
public static class View
{
    /// <summary>Group of the long-lived world-space nodes that a rebase shifts (sparks, bursts, shot beams, deposit models, ...).</summary>
    public const string WorldGroup = "world_root_space";

    public static ViewOrigin Origin { get; } = new();

    /// <summary>Counts the origin changes, so a cache of view values knows when it is stale.</summary>
    public static int Version { get; private set; }

    public static long OriginX => Origin.Cells.X;
    public static long OriginY => Origin.Cells.Y;
    public static long OriginZ => Origin.Cells.Z;

    /// <summary>The origin in cells as a vector (exact for the multiples of 1,000 the origin snaps to, up to far beyond a home sector).</summary>
    public static Vector3 OriginVector => new(Origin.Cells.X, Origin.Cells.Y, Origin.Cells.Z);

    public static void Set(long xCells, long yCells, long zCells) { Origin.Set(xCells, yCells, zCells); Version++; }

    /// <summary>View position (in cells) of a point given in simulation units.</summary>
    public static Vector3 Cells(long x, long y, long z) => new(Origin.ToViewX(x), Origin.ToViewY(y), Origin.ToViewZ(z));

    /// <summary>A point in simulation units relative to the origin, still in units (to add a sub-unit offset before dividing by <see cref="Units.CellUnits"/>).</summary>
    public static Vector3 Rel(long x, long y, long z) => new((float)(x - OriginX * Units.CellUnits), (float)(y - OriginY * Units.CellUnits), (float)(z - OriginZ * Units.CellUnits));

    /// <summary>View position of a point given in whole world cells.</summary>
    public static Vector3 FromCells(long x, long y, long z) => new(x - OriginX, y - OriginY, z - OriginZ);

    /// <summary>The same point as a cell value that may carry a fraction (a position of a structure's frame, a cell centre): the whole part is exact.</summary>
    public static Vector3 FromCells(float x, float y, float z) => new(x - OriginX, y - OriginY, z - OriginZ);

    /// <summary>Simulation units of a view vector (rounded to the nearest unit).</summary>
    public static (long X, long Y, long Z) ToUnits(Vector3 v) => (Origin.ToUnitsX(v.X), Origin.ToUnitsY(v.Y), Origin.ToUnitsZ(v.Z));

    /// <summary>Simulation units of a view vector, truncating like the old <c>(int)(v * CellUnits)</c> casts.</summary>
    public static (long X, long Y, long Z) ToUnitsTrunc(Vector3 v) =>
        (ViewOrigin.ToUnitsTrunc(v.X, OriginX), ViewOrigin.ToUnitsTrunc(v.Y, OriginY), ViewOrigin.ToUnitsTrunc(v.Z, OriginZ));

    /// <summary>Moves the origin to the ship (simulation units) when it has drifted too far; returns the exact integer shift of the origin in cells, or null when nothing changed.</summary>
    public static (long X, long Y, long Z)? RebaseIfNeeded(long shipX, long shipY, long shipZ)
    {
        if (!Origin.NeedsRebase(shipX, shipY, shipZ)) return null;
        return Rebase(shipX, shipY, shipZ);
    }

    /// <summary>Moves the origin to the snapped position of the ship (simulation units) whatever the distance; returns the shift of the origin in cells.</summary>
    public static (long X, long Y, long Z) Rebase(long shipX, long shipY, long shipZ)
    {
        var d = Origin.RebaseTo(shipX / Units.CellUnits, shipY / Units.CellUnits, shipZ / Units.CellUnits);
        Version++;
        return d;
    }

    /// <summary>Shifts a world-space node by the origin's shift (the nodes move the other way).</summary>
    public static void Shift(Node3D node, (long X, long Y, long Z) originShift) =>
        node.Position -= new Vector3(originShift.X, originShift.Y, originShift.Z);

    public static Vector3 Shift(Vector3 cached, (long X, long Y, long Z) originShift) =>
        cached - new Vector3(originShift.X, originShift.Y, originShift.Z);
}
