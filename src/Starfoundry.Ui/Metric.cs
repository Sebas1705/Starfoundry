using System.Globalization;

namespace Starfoundry.Ui;

/// <summary>The physical quantities the interface can show (ADR 0052). The value is always in SI base units: m, m/s, m/s2, kg, m3, W, J.</summary>
public enum Quantity
{
    None,
    Length,
    Speed,
    Acceleration,
    Mass,
    Volume,
    Power,
    Energy,

    /// <summary>Mass per second, shown as kg/min or t/h.</summary>
    MassFlow,

    /// <summary>A number of items in a hold, shown with the nominal mass they stand for: "460 (about 12 t)".</summary>
    HoldItems,
}

/// <summary>
/// Metric display of physical quantities with SI auto-scaling and the locale's decimal separator (ADR 0052). Engine-free, pure and deterministic.
/// Rules: two significant digits below 100 (6.3, 12), whole numbers from 100 (130); a value that rounds up to the next unit moves to it (999.7 m is 1.0 km);
/// the mantissa stays below 1,000, so digits are never grouped; negative or non-finite values show as zero. Spanish uses a decimal comma, English a point
/// (<see cref="Loc.Culture"/>); the unit symbols are the same in both.
/// </summary>
public static class Metric
{
    /// <summary>Standard gravity, m/s2, for the "g" figure of an acceleration.</summary>
    public const double StandardGravity = 9.80665;

    private readonly record struct Step(double Min, double Divisor, string Unit);

    private static readonly Step[] LengthSteps = { new(0, 1, "m"), new(1e3, 1e3, "km"), new(1e6, 1e6, "Mm") };
    private static readonly Step[] SpeedSteps = { new(0, 1, "m/s"), new(1e3, 1e3, "km/s") };
    private static readonly Step[] MassSteps = { new(0, 1e-3, "g"), new(1, 1, "kg"), new(1e3, 1e3, "t"), new(1e6, 1e6, "kt"), new(1e9, 1e9, "Mt") };
    private static readonly Step[] VolumeSteps = { new(0, 1e-3, "L"), new(1, 1, "m³") };
    private static readonly Step[] PowerSteps = { new(0, 1, "W"), new(1e3, 1e3, "kW"), new(1e6, 1e6, "MW"), new(1e9, 1e9, "GW") };
    private static readonly Step[] EnergySteps = { new(0, 1, "J"), new(1e3, 1e3, "kJ"), new(1e6, 1e6, "MJ"), new(1e9, 1e9, "GJ"), new(1e12, 1e12, "TJ") };

    private static IFormatProvider Cult(IFormatProvider? c) => c ?? Loc.Culture;

    /// <summary>A length in metres: "45 m", "1.2 km", "4.0 Mm".</summary>
    public static string Length(double metres, IFormatProvider? culture = null) => Scale(metres, LengthSteps, "m", Cult(culture));

    /// <summary>A speed in metres per second: "6.0 m/s", "24 m/s", "1.5 km/s".</summary>
    public static string Speed(double metresPerSecond, IFormatProvider? culture = null) => Scale(metresPerSecond, SpeedSteps, "m/s", Cult(culture));

    /// <summary>A speed in kilometres per hour for a tooltip: "22 km/h".</summary>
    public static string SpeedKmh(double metresPerSecond, IFormatProvider? culture = null) => Number(Clean(metresPerSecond) * 3.6, Cult(culture)) + " km/h";

    /// <summary>An acceleration in m/s2 with its size in g: "5.0 m/s² (0.51 g)".</summary>
    public static string Acceleration(double metresPerSecondSquared, IFormatProvider? culture = null)
    {
        var c = Cult(culture);
        double a = Clean(metresPerSecondSquared);
        return Number(a, c) + " m/s² (" + Number(a / StandardGravity, c) + " g)";
    }

    /// <summary>A mass in kilograms: "500 g", "1.5 t", "305 kt".</summary>
    public static string Mass(double kilograms, IFormatProvider? culture = null) => Scale(kilograms, MassSteps, "kg", Cult(culture));

    /// <summary>A volume in cubic metres: "12 L", "3.4 m³".</summary>
    public static string Volume(double cubicMetres, IFormatProvider? culture = null) => Scale(cubicMetres, VolumeSteps, "m³", Cult(culture));

    /// <summary>A power in watts: "500 kW", "10 MW".</summary>
    public static string Power(double watts, IFormatProvider? culture = null) => Scale(watts, PowerSteps, "W", Cult(culture));

    /// <summary>Two powers in one unit, the unit of the larger: "10/4.0 MW" (production over demand).</summary>
    public static string PowerPair(double wattsA, double wattsB, IFormatProvider? culture = null)
    {
        var c = Cult(culture);
        double a = Clean(wattsA), b = Clean(wattsB), max = Math.Max(a, b);
        int i = PowerSteps.Length - 1;
        while (i > 0 && max < PowerSteps[i].Min) i--;
        double d = PowerSteps[i].Divisor;
        string Part(double w) { double r = Round(w / d); return r.ToString("F" + Decimals(r), c); }
        return Part(a) + "/" + Part(b) + " " + PowerSteps[i].Unit;
    }

    /// <summary>An energy in joules: "300 MJ", "1.8 GJ".</summary>
    public static string Energy(double joules, IFormatProvider? culture = null) => Scale(joules, EnergySteps, "J", Cult(culture));

    /// <summary>An energy in kilowatt-hours for a tooltip: "83 kWh".</summary>
    public static string EnergyKwh(double joules, IFormatProvider? culture = null) => Number(Clean(joules) / 3.6e6, Cult(culture)) + " kWh";

    /// <summary>A mass flow in kilograms per second: "kg/min" below one tonne a minute, then "t/h".</summary>
    public static string MassFlow(double kilogramsPerSecond, IFormatProvider? culture = null)
    {
        var c = Cult(culture);
        double perMinute = Clean(kilogramsPerSecond) * 60.0;
        if (Round(perMinute) < 1000.0) return Number(perMinute, c) + " kg/min";
        return Number(perMinute * 0.06, c) + " t/h";
    }

    /// <summary>The value of a quantity in SI base units as text; <see cref="Quantity.None"/> returns the plain number.</summary>
    public static string Format(Quantity quantity, double si, IFormatProvider? culture = null) => quantity switch
    {
        Quantity.Length => Length(si, culture),
        Quantity.Speed => Speed(si, culture),
        Quantity.Acceleration => Number(si, culture) + " m/s²", // short, for a table cell; Acceleration() adds the g figure
        Quantity.Mass => Mass(si, culture),
        Quantity.Volume => Volume(si, culture),
        Quantity.Power => Power(si, culture),
        Quantity.Energy => Energy(si, culture),
        Quantity.MassFlow => MassFlow(si, culture),
        Quantity.HoldItems => Number(Math.Round(Clean(si)), culture, whole: true) + " (≈" + Mass(Physical.NominalKg((long)Math.Round(Clean(si))), culture) + ")",
        _ => Number(si, Cult(culture)),
    };

    /// <summary>Splits a formatted quantity such as "1.2 km" into its number ("1.2") and its unit ("km"); a text without a space returns the whole text and no unit.</summary>
    public static (string Number, string Unit) Parts(string formatted)
    {
        int i = formatted.LastIndexOf(' ');
        return i < 0 ? (formatted, "") : (formatted[..i], formatted[(i + 1)..]);
    }

    /// <summary>The change between two values (a stats panel delta) with its sign: "+5.0 m/s", "-2.5 t".</summary>
    public static string Signed(Quantity quantity, double delta, IFormatProvider? culture = null)
    {
        if (!double.IsFinite(delta)) delta = 0;
        string text = quantity == Quantity.HoldItems ? Number(Math.Abs(delta), culture, whole: true) : Format(quantity, Math.Abs(delta), culture); // a hold change is a number of items
        return (delta < 0 ? "-" : "+") + text;
    }

    /// <summary>Digits for a bare number by the same rule (two significant digits below 100, whole above), with the locale's decimal separator.</summary>
    public static string Number(double value, IFormatProvider? culture = null, bool whole = false)
    {
        var c = Cult(culture);
        double v = Clean(value);
        if (whole) return Math.Round(v).ToString("F0", c);
        double r = Round(v);
        return r.ToString("F" + Decimals(r), c);
    }

    private static string Scale(double value, Step[] steps, string zeroUnit, IFormatProvider culture)
    {
        double v = Clean(value);
        if (v == 0) return "0 " + zeroUnit;
        int i = steps.Length - 1;
        while (i > 0 && v < steps[i].Min) i--;
        while (true)
        {
            double r = Round(v / steps[i].Divisor);
            if (i + 1 < steps.Length && r * steps[i].Divisor >= steps[i + 1].Min) { i++; continue; } // 999.7 m rounds to 1,000: say 1.0 km
            return r.ToString("F" + Decimals(r), culture) + " " + steps[i].Unit;
        }
    }

    private static double Clean(double v) => double.IsFinite(v) ? Math.Max(0.0, v) : 0.0;

    private static int Decimals(double x) => x <= 0 ? 1 : x >= 10 ? 0 : x >= 1 ? 1 : x >= 0.1 ? 2 : x >= 0.01 ? 3 : 4;

    /// <summary>Two significant digits below 100, whole numbers from 100.</summary>
    private static double Round(double x)
    {
        if (x >= 100) return Math.Round(x, MidpointRounding.AwayFromZero);
        if (x <= 0) return 0;
        return Math.Round(x, Decimals(x), MidpointRounding.AwayFromZero);
    }
}
