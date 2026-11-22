using System.Numerics;

namespace Starfoundry.Ui;

/// <summary>
/// The free-flight attitude of the ship: a unit quaternion in world space, turned only about the ship's own axes. There is no world "up"
/// in it: no pitch limit, no horizon lock and no levelling, so the ship can fly upside down and nothing in the maths prefers an attitude.
/// The ship looks along its local -Z, with +X to its right and +Y over its head (the Godot camera convention). Engine-free.
/// </summary>
public static class FlightOrientation
{
    /// <summary>
    /// Turns <paramref name="q"/> about the ship's own axes. <paramref name="pitchUp"/> raises the nose, <paramref name="yawRight"/> turns it to
    /// the right and <paramref name="rollRight"/> banks to the right (the right wing goes down); all in radians. The result is renormalised,
    /// so thousands of small turns never drift away from a valid rotation.
    /// </summary>
    public static Quaternion Rotate(Quaternion q, float pitchUp, float yawRight, float rollRight)
    {
        // Local turns come first, then the current attitude: Concatenate(a, b) applies a, then b.
        var turn = Quaternion.CreateFromAxisAngle(Vector3.UnitX, pitchUp);
        turn = Quaternion.Concatenate(turn, Quaternion.CreateFromAxisAngle(Vector3.UnitY, -yawRight));
        turn = Quaternion.Concatenate(turn, Quaternion.CreateFromAxisAngle(Vector3.UnitZ, -rollRight));
        return Normalize(Quaternion.Concatenate(turn, q));
    }

    /// <summary>The quaternion scaled to unit length (the identity when it has collapsed to zero or is not a number).</summary>
    public static Quaternion Normalize(Quaternion q)
    {
        float n = MathF.Sqrt(q.X * q.X + q.Y * q.Y + q.Z * q.Z + q.W * q.W);
        if (!(n > 1e-6f) || float.IsInfinity(n)) return Quaternion.Identity;
        return new Quaternion(q.X / n, q.Y / n, q.Z / n, q.W / n);
    }

    /// <summary>Where the ship's nose points, in world space.</summary>
    public static Vec3 Forward(Quaternion q) => ToVec(Vector3.Transform(-Vector3.UnitZ, q));

    /// <summary>The ship's right-hand side, in world space.</summary>
    public static Vec3 Right(Quaternion q) => ToVec(Vector3.Transform(Vector3.UnitX, q));

    /// <summary>The direction over the pilot's head, in world space.</summary>
    public static Vec3 Up(Quaternion q) => ToVec(Vector3.Transform(Vector3.UnitY, q));

    /// <summary>The camera of a ship with this attitude whose pilot looks straight ahead.</summary>
    public static CameraBasis Camera(Quaternion q, float verticalFovRadians) => new(Forward(q), Right(q), Up(q), verticalFovRadians);

    /// <summary>
    /// Moves <paramref name="q"/> toward the identity attitude by <paramref name="amount"/> (0 to 1) along the shortest arc. Used only when the
    /// ship locks onto a docking port, which is axis-aligned; never during free flight.
    /// </summary>
    public static Quaternion Align(Quaternion q, float amount) => Normalize(Quaternion.Slerp(q, Quaternion.Identity, Math.Clamp(amount, 0f, 1f)));

    /// <summary>The angle in radians (0 to pi) between the ship's attitude and the identity attitude.</summary>
    public static float AngleFromIdentity(Quaternion q)
    {
        float w = Math.Clamp(MathF.Abs(Normalize(q).W), 0f, 1f);
        return 2f * MathF.Acos(w);
    }

    /// <summary>True when the three axes are unit length and perpendicular to each other within <paramref name="tolerance"/>.</summary>
    public static bool IsOrthonormal(Quaternion q, float tolerance = 1e-4f)
    {
        var f = Forward(q);
        var r = Right(q);
        var u = Up(q);
        return MathF.Abs(f.Length - 1f) < tolerance && MathF.Abs(r.Length - 1f) < tolerance && MathF.Abs(u.Length - 1f) < tolerance
            && MathF.Abs(f.Dot(r)) < tolerance && MathF.Abs(f.Dot(u)) < tolerance && MathF.Abs(r.Dot(u)) < tolerance;
    }

    private static Vec3 ToVec(Vector3 v) => new(v.X, v.Y, v.Z);
}
