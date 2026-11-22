namespace Starfoundry.Sim;

/// <summary>Fixed rules of ship attitude (ADR 0030). Part of the simulation, so changing them changes states.</summary>
public static class AttitudeRules
{
    /// <summary>Fixed-point scale of the quaternion components and of the rotation matrix: 1.0 is 16384 (2 to the 14th).</summary>
    public const int Scale = 16384;

    /// <summary>The shift that divides by <see cref="Scale"/>.</summary>
    public const int Shift = 14;

    /// <summary>
    /// A quaternion is accepted when its squared length differs from <c>Scale * Scale</c> by at most this much (a relative error of 2.4e-4,
    /// which is about twice what rounding the four components of a unit quaternion to this scale can cause).
    /// </summary>
    public const long NormTolerance = 4L * Scale + 4;

    /// <summary>
    /// A ship docks only when its attitude is within about 15 degrees of the identity (the port axes of a station): the <c>W</c> of the
    /// quaternion is at least cos(7.5 degrees) times the scale.
    /// </summary>
    public const int DockMinW = 16244;

    /// <summary>The pilot seat's eye, from the origin of the cockpit module, in units: the point the ship turns about (4, 2.7 and 6.6 cells).</summary>
    public const int PivotX = 960, PivotY = 648, PivotZ = 1584;
}

/// <summary>
/// The attitude of a ship structure: a unit quaternion in fixed point (<see cref="AttitudeRules.Scale"/> is 1.0), in the canonical sign
/// (<c>W</c> is not negative). The identity is (Scale, 0, 0, 0) and is what every structure has unless a <see cref="SetAttitude"/> says otherwise.
/// Integer arithmetic only, so every machine turns a ship into exactly the same cells. A structure turns about the pilot seat; see
/// <see cref="BeltNetwork.StructureToWorld"/>. Always build one with <see cref="Identity"/>, <see cref="TryCreate"/> or <see cref="Of"/>:
/// <c>default</c> is not a valid attitude.
/// </summary>
public readonly struct Attitude : IEquatable<Attitude>
{
    public int W { get; }
    public int X { get; }
    public int Y { get; }
    public int Z { get; }

    // The rotation matrix at the same scale (row-major), computed once.
    private readonly int _m00, _m01, _m02, _m10, _m11, _m12, _m20, _m21, _m22;

    private Attitude(int w, int x, int y, int z)
    {
        W = w; X = x; Y = y; Z = z;
        long s = AttitudeRules.Scale;
        long xx = (long)x * x, yy = (long)y * y, zz = (long)z * z, xy = (long)x * y, xz = (long)x * z, yz = (long)y * z, wx = (long)w * x, wy = (long)w * y, wz = (long)w * z;
        static int Div(long n) => (int)((n + AttitudeRules.Scale / 2) >> AttitudeRules.Shift);
        _m00 = Div(s * s - 2 * (yy + zz)); _m01 = Div(2 * (xy - wz)); _m02 = Div(2 * (xz + wy));
        _m10 = Div(2 * (xy + wz)); _m11 = Div(s * s - 2 * (xx + zz)); _m12 = Div(2 * (yz - wx));
        _m20 = Div(2 * (xz - wy)); _m21 = Div(2 * (yz + wx)); _m22 = Div(s * s - 2 * (xx + yy));
    }

    /// <summary>The attitude with no turn.</summary>
    public static Attitude Identity { get; } = new(AttitudeRules.Scale, 0, 0, 0);

    /// <summary>True for the identity: nothing is turned and every position is the axis-aligned one.</summary>
    public bool IsIdentity => W == AttitudeRules.Scale && X == 0 && Y == 0 && Z == 0;

    /// <summary>Like <see cref="TryCreate"/>, but throws for components that are not a unit quaternion.</summary>
    public static Attitude Of(int w, int x, int y, int z) =>
        TryCreate(w, x, y, z, out var a) ? a : throw new ArgumentException("The quaternion is not a unit quaternion within the tolerance.");

    /// <summary>
    /// Builds an attitude from the four components at <see cref="AttitudeRules.Scale"/>. False when a component is beyond the scale or the
    /// squared length is not the scale squared within <see cref="AttitudeRules.NormTolerance"/>. A quaternion and its negative are the same
    /// turn: the stored one has a non-negative <c>W</c> (or, when <c>W</c> is zero, a positive first non-zero component).
    /// </summary>
    public static bool TryCreate(int w, int x, int y, int z, out Attitude attitude)
    {
        attitude = Identity;
        int max = AttitudeRules.Scale;
        if (w < -max || w > max || x < -max || x > max || y < -max || y > max || z < -max || z > max) return false;
        long n2 = (long)w * w + (long)x * x + (long)y * y + (long)z * z;
        long full = (long)max * max;
        if (n2 < full - AttitudeRules.NormTolerance || n2 > full + AttitudeRules.NormTolerance) return false;
        bool flip = w < 0 || (w == 0 && (x < 0 || (x == 0 && (y < 0 || (y == 0 && z < 0)))));
        if (flip) { w = -w; x = -x; y = -y; z = -z; }
        attitude = w == max && x == 0 && y == 0 && z == 0 ? Identity : new Attitude(w, x, y, z);
        return true;
    }

    /// <summary>True when this attitude is close enough to the identity for a ship to dock at an axis-aligned port (<see cref="AttitudeRules.DockMinW"/>).</summary>
    public bool IsDockAligned => W >= AttitudeRules.DockMinW;

    /// <summary>Turns a vector (any unit) by this attitude: rounded to the nearest unit, the same on every machine.</summary>
    public (long X, long Y, long Z) Rotate(long x, long y, long z)
    {
        if (IsIdentity) return (x, y, z);
        static long Sh(long n) => (n + AttitudeRules.Scale / 2) >> AttitudeRules.Shift;
        return (Sh(_m00 * x + _m01 * y + _m02 * z), Sh(_m10 * x + _m11 * y + _m12 * z), Sh(_m20 * x + _m21 * y + _m22 * z));
    }

    /// <summary>The inverse turn (the transpose of the matrix): from the world frame back into the structure's frame.</summary>
    public (long X, long Y, long Z) RotateInverse(long x, long y, long z)
    {
        if (IsIdentity) return (x, y, z);
        static long Sh(long n) => (n + AttitudeRules.Scale / 2) >> AttitudeRules.Shift;
        return (Sh(_m00 * x + _m10 * y + _m20 * z), Sh(_m01 * x + _m11 * y + _m21 * z), Sh(_m02 * x + _m12 * y + _m22 * z));
    }

    /// <summary>One element of the rotation matrix (row and column in 0..2) at <see cref="AttitudeRules.Scale"/>.</summary>
    public int Element(int row, int column) => (row * 3 + column) switch
    {
        0 => _m00, 1 => _m01, 2 => _m02, 3 => _m10, 4 => _m11, 5 => _m12, 6 => _m20, 7 => _m21, 8 => _m22,
        _ => throw new ArgumentOutOfRangeException(nameof(row)),
    };

    public bool Equals(Attitude other) => W == other.W && X == other.X && Y == other.Y && Z == other.Z;

    public override bool Equals(object? obj) => obj is Attitude a && Equals(a);

    public override int GetHashCode() => HashCode.Combine(W, X, Y, Z);

    public static bool operator ==(Attitude a, Attitude b) => a.Equals(b);

    public static bool operator !=(Attitude a, Attitude b) => !a.Equals(b);

    public override string ToString() => IsIdentity ? "Attitude(identity)" : $"Attitude({W}, {X}, {Y}, {Z})";
}
