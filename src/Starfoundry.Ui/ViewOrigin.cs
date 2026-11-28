using Starfoundry.Sim;

namespace Starfoundry.Ui;

/// <summary>
/// The floating origin of the client view (world scale stage 1, ADR 0053). The simulation keeps integer units
/// (1/240 m, <see cref="Units.CellUnits"/> per cell); the renderer works in float32, whose spacing grows with the distance
/// from the origin. So the view is drawn relative to an origin that follows the ship: a value is subtracted in 64-bit and
/// only then converted to float, which makes the view value exact for anything near the origin. The simulation never sees this.
/// </summary>
public sealed class ViewOrigin
{
    /// <summary>The origin only ever sits on a multiple of this many cells, so a rebase moves nodes by whole, float-exact amounts.</summary>
    public const long SnapCells = 1000;

    /// <summary>The ship may drift this far (cells) from the origin before the view rebases.</summary>
    public const long RebaseCells = 20000;

    private readonly long _threshold;

    /// <param name="rebaseCells">Distance (cells) that triggers a rebase; tests use a smaller one.</param>
    public ViewOrigin(long rebaseCells = RebaseCells) { _threshold = rebaseCells; }

    /// <summary>The origin, in cells, on each axis.</summary>
    public (long X, long Y, long Z) Cells { get; private set; }

    /// <summary>The origin in simulation units (1/240 m).</summary>
    public (long X, long Y, long Z) OriginUnits => (Cells.X * Sim.Units.CellUnits, Cells.Y * Sim.Units.CellUnits, Cells.Z * Sim.Units.CellUnits);

    /// <summary>The nearest multiple of <see cref="SnapCells"/> (ties away from zero, symmetric around 0).</summary>
    public static long Snap(long cells)
    {
        long half = SnapCells / 2;
        return cells >= 0 ? (cells + half) / SnapCells * SnapCells : -((-cells + half) / SnapCells * SnapCells);
    }

    /// <summary>Sets the origin directly (a debug start or a loaded session); every axis is snapped.</summary>
    public void Set(long xCells, long yCells, long zCells) => Cells = (Snap(xCells), Snap(yCells), Snap(zCells));

    /// <summary>View coordinate of one axis value in simulation units: the 64-bit difference is converted to float last.</summary>
    public static float ToView(long units, long originCells) => (float)(units - originCells * Sim.Units.CellUnits) / Sim.Units.CellUnits; // the same float arithmetic as the old `new Vector3(units) / CellUnits` when the origin is 0

    public float ToViewX(long units) => ToView(units, Cells.X);
    public float ToViewY(long units) => ToView(units, Cells.Y);
    public float ToViewZ(long units) => ToView(units, Cells.Z);

    /// <summary>View coordinate of a value already in whole cells.</summary>
    public float CellsToView(long cells, long originCells) => cells - originCells;

    /// <summary>The inverse of <see cref="ToView(long, long)"/>: a view coordinate back to simulation units, rounded to the nearest unit.</summary>
    public static long ToUnits(float view, long originCells) => (long)MathF.Round(view * Sim.Units.CellUnits) + originCells * Sim.Units.CellUnits;

    /// <summary>Like <see cref="ToUnits(float, long)"/> but truncating the view part toward zero, as the old <c>(int)(v * CellUnits)</c> casts of the aim code did (so the view at origin 0 sends the same command values as before).</summary>
    public static long ToUnitsTrunc(float view, long originCells) => (long)(view * Sim.Units.CellUnits) + originCells * Sim.Units.CellUnits;

    public long ToUnitsX(float view) => ToUnits(view, Cells.X);
    public long ToUnitsY(float view) => ToUnits(view, Cells.Y);
    public long ToUnitsZ(float view) => ToUnits(view, Cells.Z);

    /// <summary>True when the ship (simulation units) is more than the threshold from the origin on any axis.</summary>
    public bool NeedsRebase(long shipX, long shipY, long shipZ)
    {
        long t = _threshold * Sim.Units.CellUnits;
        return Math.Abs(shipX - Cells.X * Sim.Units.CellUnits) > t
            || Math.Abs(shipY - Cells.Y * Sim.Units.CellUnits) > t
            || Math.Abs(shipZ - Cells.Z * Sim.Units.CellUnits) > t;
    }

    /// <summary>
    /// Moves the origin to the snapped ship position (given in cells) and returns the exact integer shift, in cells, of the origin
    /// (new minus old). Every node placed in view space must move by the negative of it.
    /// </summary>
    public (long X, long Y, long Z) RebaseTo(long shipXCells, long shipYCells, long shipZCells)
    {
        var old = Cells;
        Cells = (Snap(shipXCells), Snap(shipYCells), Snap(shipZCells));
        return (Cells.X - old.X, Cells.Y - old.Y, Cells.Z - old.Z);
    }
}
