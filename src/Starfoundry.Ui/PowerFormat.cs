using System.Globalization;
using Starfoundry.Sim;

namespace Starfoundry.Ui;

/// <summary>How well a structure is powered, for colouring the power bar: green, amber or red.</summary>
public enum PowerBand
{
    /// <summary>Satisfaction 1000 per mille or more: every machine has all its power.</summary>
    Full,

    /// <summary>500 to 999: machines run slower.</summary>
    Strained,

    /// <summary>Below 500: machines stop (turrets stop firing), the ship barely moves.</summary>
    Critical,
}

/// <summary>What the fuel-warning logic decided this frame.</summary>
public enum PowerWarning
{
    None,

    /// <summary>The fuel left dropped below two minutes.</summary>
    LowFuel,

    /// <summary>The fuel left dropped below thirty seconds.</summary>
    LastSeconds,

    /// <summary>The power hit zero.</summary>
    Out,
}

/// <summary>Engine-free helpers of the power readout of the HUD: fuel time, colour band and text.</summary>
public static class PowerFormat
{
    /// <summary>Ticks one hydrogen keeps a reactor burning in the shipped content (<c>burn_interval_ticks</c>); the game passes the content's own value.</summary>
    public const int DefaultBurnIntervalTicks = 900;

    /// <summary>Fuel left below this many seconds raises the first warning.</summary>
    public const int LowFuelSeconds = 120;

    /// <summary>Fuel left below this many seconds raises the last warning.</summary>
    public const int LastSecondsLimit = 30;

    /// <summary>Ticks of power left: the hydrogen in the hold times the burn interval, plus what the reactor still has burning.</summary>
    public static long TimeLeftTicks(PowerStatus status, int burnIntervalTicks = DefaultBurnIntervalTicks) =>
        (long)Math.Max(0, status.HydrogenInHold) * Math.Max(0, burnIntervalTicks) + Math.Max(0, status.BurnRemainingTicks);

    /// <summary>Green at 1000 per mille or more, amber from 500 to 999, red below 500.</summary>
    public static PowerBand Band(int satisfactionPermille) =>
        satisfactionPermille >= PowerRules.FullSatisfaction ? PowerBand.Full
        : satisfactionPermille >= PowerRules.TurretMinSatisfaction ? PowerBand.Strained
        : PowerBand.Critical;

    /// <summary>Ticks as <c>m:ss</c>, rounded up to whole seconds so the last tick still reads 0:01 and only no fuel reads 0:00.</summary>
    public static string Clock(long ticks)
    {
        long seconds = (Math.Max(0, ticks) + Units.TicksPerSecond - 1) / Units.TicksPerSecond;
        return string.Create(CultureInfo.InvariantCulture, $"{seconds / 60}:{seconds % 60:00}");
    }

    /// <summary>Whole percent of satisfaction (per mille divided by ten, rounded down).</summary>
    public static int Percent(int satisfactionPermille) => Math.Clamp(satisfactionPermille, 0, 1000) / 10;

    /// <summary>The fuel line: hydrogen count and the time it lasts, or the no-fuel text when nothing is left or burning.</summary>
    public static string FuelLine(PowerStatus status, int burnIntervalTicks = DefaultBurnIntervalTicks)
    {
        long left = TimeLeftTicks(status, burnIntervalTicks);
        return left <= 0
            ? Loc.T("hud.power.noFuel")
            : Loc.T("hud.power.fuel", status.HydrogenInHold, Clock(left));
    }
}

/// <summary>
/// Decides when to warn about the fuel of the ship: once when the time left drops below two minutes, once below thirty seconds and
/// once when the power hits zero. Each warning arms again when the situation recovers (hydrogen made). The first reading counts too,
/// so a game loaded with half a minute of fuel warns at once, but only with the most urgent warning.
/// </summary>
public sealed class FuelWatch
{
    private bool _firedLow, _firedLast, _firedOut;

    /// <summary>Feeds one reading and returns the warning to show now, if any. <paramref name="hasReactor"/> false (no reactor: no fuel is burnt) never warns about fuel.</summary>
    public PowerWarning Update(PowerStatus status, bool hasReactor, int burnIntervalTicks = PowerFormat.DefaultBurnIntervalTicks)
    {
        if (!hasReactor)
        {
            _firedLow = _firedLast = _firedOut = false;
            return PowerWarning.None;
        }
        long left = PowerFormat.TimeLeftTicks(status, burnIntervalTicks);
        long lowTicks = (long)PowerFormat.LowFuelSeconds * Units.TicksPerSecond, lastTicks = (long)PowerFormat.LastSecondsLimit * Units.TicksPerSecond;
        bool out_ = status.SatisfactionPermille == 0;

        if (left >= lowTicks) _firedLow = false;
        if (left >= lastTicks) _firedLast = false;
        if (!out_) _firedOut = false;

        if (out_)
        {
            _firedLow = _firedLast = true; // nothing left to say about the fuel once the power is out
            if (_firedOut) return PowerWarning.None;
            _firedOut = true;
            return PowerWarning.Out;
        }
        if (left < lastTicks)
        {
            bool fire = !_firedLast;
            _firedLow = _firedLast = true;
            return fire ? PowerWarning.LastSeconds : PowerWarning.None;
        }
        if (left < lowTicks)
        {
            bool fire = !_firedLow;
            _firedLow = true;
            return fire ? PowerWarning.LowFuel : PowerWarning.None;
        }
        return PowerWarning.None;
    }
}
