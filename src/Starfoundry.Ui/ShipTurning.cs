using System.Globalization;
using System.Numerics;
using System.Text.Json;
using Starfoundry.Sim;

namespace Starfoundry.Ui;

/// <summary>
/// How much the ship's turn lags the pilot (the setting "Ship inertia"). <see cref="Off"/> keeps the old short easing of the mouse (about 40 ms, see
/// ADR 0033), which is the accessible choice; <see cref="Light"/> and <see cref="Full"/> run the rotation dynamics of <see cref="TurnController"/>, light
/// with a lighter, quicker ship.
/// </summary>
public enum ShipInertia { Off, Light, Full }

public static class ShipInertias
{
    public const ShipInertia Default = ShipInertia.Full;

    public static string Id(ShipInertia mode) => mode switch { ShipInertia.Off => "off", ShipInertia.Light => "light", _ => "full" };

    public static bool TryParse(string? text, out ShipInertia mode)
    {
        switch ((text ?? "").Trim().ToLowerInvariant())
        {
            case "off": mode = ShipInertia.Off; return true;
            case "light": mode = ShipInertia.Light; return true;
            case "full": mode = ShipInertia.Full; return true;
            default: mode = Default; return false;
        }
    }

    public static ShipInertia Next(ShipInertia mode) => mode switch { ShipInertia.Off => ShipInertia.Light, ShipInertia.Light => ShipInertia.Full, _ => ShipInertia.Off };
}

/// <summary>The turning limits of one ship: the fastest it turns about an axis and how quickly it gets there (degrees).</summary>
public readonly record struct TurnStats(float RateDegPerSecond, float AccelDegPerSecondSquared)
{
    public float RateRad => RateDegPerSecond * MathF.PI / 180f;
    public float AccelRad => AccelDegPerSecondSquared * MathF.PI / 180f;

    /// <summary>Time to turn through <paramref name="degrees"/> from rest to rest with the best profile (speed up, cruise, slow down).</summary>
    public float TimeToTurn(float degrees)
    {
        float rate = Math.Max(0.01f, RateDegPerSecond), accel = Math.Max(0.01f, AccelDegPerSecondSquared);
        float ramp = rate * rate / accel; // the angle spent speeding up and slowing down together
        return degrees >= ramp ? degrees / rate + rate / accel : 2f * MathF.Sqrt(degrees / accel);
    }
}

/// <summary>
/// The handling of ships as content data (<c>content/ship_handling.json</c>): the base turn rate and acceleration of the starter ship, the mass of every
/// module kind, how the research of the engines raises the two numbers and what the Light setting adds. Heavier ships than the starter turn slower, lighter
/// ones faster (bounded), and the numbers are tuned in this one file. Engine-free.
/// </summary>
public sealed class ShipHandling
{
    public float TurnRate { get; init; } = 90f;
    public float TurnAccel { get; init; } = 160f;
    /// <summary>Mass of each module kind (id in snake case); a kind that is not listed weighs 1.</summary>
    public IReadOnlyDictionary<string, float> Mass { get; init; } = new Dictionary<string, float>();
    /// <summary>The modules of the ship that has the base numbers (its mass is the reference).</summary>
    public IReadOnlyList<string> ReferenceModules { get; init; } = new[] { "cockpit", "hull", "cargo", "reactor", "engine", "mining_laser", "gun" };
    /// <summary>The acceleration factor of a ship is the reference mass over its own, kept between these (percent); the rate factor is the square root.</summary>
    public int MinFactorPercent { get; init; } = 30;
    public int MaxFactorPercent { get; init; } = 150;
    /// <summary>How much of the engine research bonus (top speed, acceleration) also raises the turn rate and the turn acceleration, in percent.</summary>
    public int ResearchRateSharePercent { get; init; } = 50;
    public int ResearchAccelSharePercent { get; init; } = 50;
    /// <summary>What the Light setting does to the numbers (percent).</summary>
    public int LightRatePercent { get; init; } = 135;
    public int LightAccelPercent { get; init; } = 220;

    public static readonly ShipHandling Default = new()
    {
        Mass = new Dictionary<string, float>
        {
            ["core"] = 3f, ["cockpit"] = 1f, ["engine"] = 1.2f, ["reactor"] = 1.3f, ["cargo"] = 1.4f, ["workshop"] = 1.5f, ["defence"] = 1.5f, ["hull"] = 0.8f,
            ["drone_bay"] = 1f, ["shield"] = 0.8f, ["sensor"] = 0.4f, ["reactor_mk2"] = 1.6f, ["dock"] = 0.8f, ["mining_laser"] = 0.6f, ["mining_laser_mk2"] = 0.8f,
            ["gun"] = 0.6f, ["gun_mk2"] = 0.8f, ["shipyard"] = 2f,
        },
    };

    public float MassOf(string kind) => Mass.TryGetValue(kind, out float m) && float.IsFinite(m) && m > 0f ? m : 1f;

    public float MassOf(ModuleKind kind) => MassOf(ModuleKindIds.Id(kind));

    /// <summary>The mass of a ship: the sum of the mass of its modules.</summary>
    public float MassOf(IEnumerable<ModuleKind> modules) => modules.Sum(MassOf);

    public float ReferenceMass => ReferenceModules.Sum(MassOf);

    /// <summary>
    /// The turning limits of a ship made of <paramref name="modules"/>, with the engine research bonuses (percent, <c>BonusKind.EngineSpeed</c> and
    /// <c>BonusKind.EngineAccel</c>) and the inertia setting. <see cref="ShipInertia.Off"/> gives the same numbers as Full (the setting then does not use them).
    /// </summary>
    public TurnStats StatsFor(IEnumerable<ModuleKind> modules, int engineSpeedBonusPercent = 0, int engineAccelBonusPercent = 0, ShipInertia inertia = ShipInertia.Full)
    {
        float mass = Math.Max(0.1f, MassOf(modules));
        float accelFactor = Math.Clamp(ReferenceMass / mass, MinFactorPercent / 100f, MaxFactorPercent / 100f);
        float rateFactor = MathF.Sqrt(accelFactor);
        float rate = TurnRate * rateFactor * (1f + ResearchRateSharePercent * Math.Max(0, engineSpeedBonusPercent) / 10000f);
        float accel = TurnAccel * accelFactor * (1f + ResearchAccelSharePercent * Math.Max(0, engineAccelBonusPercent) / 10000f);
        if (inertia == ShipInertia.Light)
        {
            rate *= LightRatePercent / 100f;
            accel *= LightAccelPercent / 100f;
        }
        return new TurnStats(rate, accel);
    }

    /// <summary>Reads <c>ship_handling.json</c>; throws <see cref="InvalidDataException"/> with a short reason when it is not valid.</summary>
    public static ShipHandling FromJson(string json)
    {
        try
        {
            using var doc = JsonDocument.Parse(json, new JsonDocumentOptions { CommentHandling = JsonCommentHandling.Skip, AllowTrailingCommas = true });
            var root = doc.RootElement;
            var mass = new Dictionary<string, float>(Default.Mass);
            if (root.TryGetProperty("mass", out var m) && m.ValueKind == JsonValueKind.Object)
                foreach (var p in m.EnumerateObject()) mass[p.Name] = Number(p.Value, 1f, p.Name);
            var reference = Default.ReferenceModules;
            if (root.TryGetProperty("reference_modules", out var r) && r.ValueKind == JsonValueKind.Array)
                reference = r.EnumerateArray().Select(e => e.GetString() ?? "").Where(s => s.Length > 0).ToList();
            var research = root.TryGetProperty("research", out var rs) && rs.ValueKind == JsonValueKind.Object ? rs : default;
            var light = root.TryGetProperty("light", out var l) && l.ValueKind == JsonValueKind.Object ? l : default;
            var h = new ShipHandling
            {
                TurnRate = Positive(root, "turn_rate", Default.TurnRate),
                TurnAccel = Positive(root, "turn_accel", Default.TurnAccel),
                Mass = mass,
                ReferenceModules = reference,
                MinFactorPercent = (int)Positive(root, "min_factor_percent", Default.MinFactorPercent),
                MaxFactorPercent = (int)Positive(root, "max_factor_percent", Default.MaxFactorPercent),
                ResearchRateSharePercent = research.ValueKind == JsonValueKind.Object ? (int)NonNegative(research, "turn_rate_share_percent", Default.ResearchRateSharePercent) : Default.ResearchRateSharePercent,
                ResearchAccelSharePercent = research.ValueKind == JsonValueKind.Object ? (int)NonNegative(research, "turn_accel_share_percent", Default.ResearchAccelSharePercent) : Default.ResearchAccelSharePercent,
                LightRatePercent = light.ValueKind == JsonValueKind.Object ? (int)Positive(light, "rate_percent", Default.LightRatePercent) : Default.LightRatePercent,
                LightAccelPercent = light.ValueKind == JsonValueKind.Object ? (int)Positive(light, "accel_percent", Default.LightAccelPercent) : Default.LightAccelPercent,
            };
            if (h.MinFactorPercent > h.MaxFactorPercent) throw new InvalidDataException("min_factor_percent is larger than max_factor_percent");
            return h;
        }
        catch (JsonException e) { throw new InvalidDataException("ship_handling.json is not valid JSON: " + e.Message); }
    }

    private static float Number(JsonElement e, float fallback, string what)
    {
        if (e.ValueKind != JsonValueKind.Number || !e.TryGetSingle(out float v) || !float.IsFinite(v) || v <= 0f) throw new InvalidDataException($"ship_handling.json: {what} must be a positive number");
        return v;
    }

    private static float Positive(JsonElement obj, string name, float fallback) => obj.TryGetProperty(name, out var e) ? Number(e, fallback, name) : fallback;

    private static float NonNegative(JsonElement obj, string name, float fallback)
    {
        if (!obj.TryGetProperty(name, out var e)) return fallback;
        if (e.ValueKind != JsonValueKind.Number || !e.TryGetSingle(out float v) || !float.IsFinite(v) || v < 0f) throw new InvalidDataException($"ship_handling.json: {name} must be a number of zero or more");
        return v;
    }
}

/// <summary>
/// The rotation dynamics of the ship, client side (the replacement of the 40 ms mouse easing; ADR 0030 is untouched: the attitude it produces is still
/// quantised and throttled before it is sent). The pilot gives two kinds of input: the mouse adds a turn to <see cref="Pending"/> (pitch and yaw, radians)
/// and the roll keys give a roll direction. The controller works out a <em>desired angular rate</em> for each axis from them (the fastest rate that still
/// lets the ship stop on the pending turn with its maximum angular acceleration, never more than the turn rate), and the real angular velocity
/// <see cref="Omega"/> approaches it with at most that acceleration. With flight assist on, no input means a desired rate of zero, so the ship damps back
/// to rest; with it off, the ship keeps rotating until the pilot counters it. The attitude is integrated with the mean of the old and new rate, so the
/// result does not depend on the frame rate to first order, and is renormalised every step.
/// </summary>
public sealed class TurnController
{
    /// <summary>Margin of the stopping profile: the ship plans to brake with this share of its acceleration, so it can keep to the plan.</summary>
    public const float BrakeShare = 0.8f;
    /// <summary>The pending turn never exceeds this many seconds of the turn rate: a fast flick of the mouse cannot queue a spin that runs on for seconds.</summary>
    public const float MaxPendingSeconds = 0.6f;
    /// <summary>With flight assist off, a pending turn becomes a change of angular velocity of this many radians per second per radian turned.</summary>
    public const float FreeImpulseGain = 4f;
    /// <summary>Under this pending turn and this rate the ship stops dead (avoids a tremble of a few hundredths of a degree around the target).</summary>
    private const float SnapAngle = 0.0006f, SnapRate = 0.06f;
    private const float Rest = 0.0004f; // radians per second under which the ship counts as still

    /// <summary>Turn still to be made about the ship's own axes: X pitch up, Y yaw right, Z unused (the roll is rate-driven), radians.</summary>
    public Vector3 Pending { get; private set; }

    /// <summary>The angular velocity about the ship's own axes in radians per second: X pitch up, Y yaw right, Z roll right.</summary>
    public Vector3 Omega { get; private set; }

    public void Reset()
    {
        Pending = Vector3.Zero;
        Omega = Vector3.Zero;
    }

    /// <summary>Adds a mouse turn (radians): yaw to the right and pitch up.</summary>
    public void AddLook(float yawRight, float pitchUp, TurnStats stats)
    {
        if (!float.IsFinite(yawRight) || !float.IsFinite(pitchUp)) return;
        float cap = stats.RateRad * MaxPendingSeconds;
        Pending = new Vector3(Math.Clamp(Pending.X + pitchUp, -cap, cap), Math.Clamp(Pending.Y + yawRight, -cap, cap), 0f);
    }

    /// <summary>Drops the turn that is still pending (the pilot let go of the controls, or the controls changed hands).</summary>
    public void ClearPending() => Pending = Vector3.Zero;

    /// <summary>
    /// Advances the rotation by <paramref name="dt"/> seconds and returns the new attitude. <paramref name="roll"/> is -1 to 1 (the roll keys), positive banks
    /// to the right. <paramref name="assist"/> is the flight assist.
    /// </summary>
    public Quaternion Step(Quaternion q, float dt, float roll, TurnStats stats, bool assist)
    {
        if (!(dt > 0f) || !float.IsFinite(dt)) return q;
        dt = Math.Min(dt, 0.1f); // a hitch must not turn the ship through a large angle in one step
        float rate = Math.Max(1e-3f, stats.RateRad), accel = Math.Max(1e-3f, stats.AccelRad);
        roll = float.IsFinite(roll) ? Math.Clamp(roll, -1f, 1f) : 0f;

        var old = Omega;
        Vector3 next;
        if (assist)
        {
            next = new Vector3(Approach(old.X, Desired(Pending.X, rate, accel), accel, dt), Approach(old.Y, Desired(Pending.Y, rate, accel), accel, dt), Approach(old.Z, roll * rate, accel, dt));
            // Settle: close to the target and nearly still on an axis, stop dead.
            if (Math.Abs(Pending.X) < SnapAngle && Math.Abs(next.X) < SnapRate) { next.X = 0f; Pending = new Vector3(0f, Pending.Y, 0f); }
            if (Math.Abs(Pending.Y) < SnapAngle && Math.Abs(next.Y) < SnapRate) { next.Y = 0f; Pending = new Vector3(Pending.X, 0f, 0f); }
            if (roll == 0f && Math.Abs(next.Z) < Rest) next.Z = 0f;
            Omega = next;
            var turned = (old + next) * 0.5f * dt; // the angle this step: pitch up, yaw right, roll right
            Pending = new Vector3(Spend(Pending.X, turned.X), Spend(Pending.Y, turned.Y), 0f); // the pending turn is spent by what was turned about its axes
            return turned == Vector3.Zero ? q : FlightOrientation.Rotate(q, turned.X, turned.Y, turned.Z);
        }
        else
        {
            // Free rotation: the mouse turn and the roll keys are pushes on the angular velocity, nothing brings it back to rest.
            float push = accel * dt;
            float dvx = Math.Clamp(Pending.X * FreeImpulseGain, -push, push), dvy = Math.Clamp(Pending.Y * FreeImpulseGain, -push, push);
            Pending = new Vector3(Pending.X - dvx / FreeImpulseGain, Pending.Y - dvy / FreeImpulseGain, 0f);
            next = new Vector3(Math.Clamp(old.X + dvx, -rate, rate), Math.Clamp(old.Y + dvy, -rate, rate), Math.Clamp(old.Z + roll * push, -rate, rate));
            Omega = next;
            var turned = (old + next) * 0.5f * dt;
            return turned == Vector3.Zero ? q : FlightOrientation.Rotate(q, turned.X, turned.Y, turned.Z);
        }
    }

    /// <summary>The rate the ship should have now to arrive on <paramref name="pending"/> and stop: bounded by the turn rate and by a braking profile.</summary>
    private static float Desired(float pending, float rate, float accel)
    {
        float a = Math.Abs(pending);
        if (a < 1e-6f) return 0f;
        float v = Math.Min(rate, MathF.Sqrt(2f * accel * BrakeShare * a));
        return pending > 0f ? v : -v;
    }

    private static float Approach(float v, float target, float accel, float dt)
    {
        float step = accel * dt;
        float d = target - v;
        return Math.Abs(d) <= step ? target : v + MathF.Sign(d) * step;
    }


    private static float Spend(float pending, float turned)
    {
        if (pending == 0f) return 0f;
        float left = pending - turned;
        if (Math.Sign(left) != Math.Sign(pending) || Math.Abs(left) < 1e-6f) return 0f; // crossing zero ends the turn
        return left;
    }

    /// <summary>The angular velocity in degrees per second, for the display.</summary>
    public Vector3 OmegaDegrees => Omega * (180f / MathF.PI);
}

/// <summary>The setting of the flight assist and what it does to the numbers shown, so a test and the display agree.</summary>
public static class FlightAssistText
{
    public static string Rate(float degreesPerSecond) => Math.Abs(degreesPerSecond) < 0.5f ? "0" : degreesPerSecond.ToString("+0;-0", CultureInfo.InvariantCulture);
}
