namespace Starfoundry.Ui;

/// <summary>Which command-line flags turn a run into a scripted one (fixed seed, no New Game choice). Tool flags do not.</summary>
public static class LaunchFlags
{
    /// <summary>False for flags that only configure the session: <c>--menu-shot=</c>, <c>--agent</c>, <c>--ui-lang=</c> and <c>--content=</c>.</summary>
    public static bool IsScripted(string arg) =>
        !arg.StartsWith("--menu-shot=", System.StringComparison.Ordinal)
        && arg != "--agent"
        && !arg.StartsWith("--ui-lang=", System.StringComparison.Ordinal)
        && !arg.StartsWith(ContentLocator.ContentFlag, System.StringComparison.Ordinal);

    /// <summary>The debug flag that starts the client view at a far origin: <c>--view-origin=x,y,z</c> or <c>--view-origin x,y,z</c> (cells; ADR 0053).</summary>
    public const string ViewOriginFlag = "--view-origin";

    /// <summary>The headless self-check of the floating origin: it forces rebases and asserts the view stays near the origin and moves by the exact delta.</summary>
    public const string ViewOriginCheckFlag = "--view-origin-check";

    /// <summary>
    /// Reads <see cref="ViewOriginFlag"/> from the arguments: three whole numbers of cells after an equals sign or in the next argument. False when the flag is absent or malformed.
    /// </summary>
    public static bool TryViewOrigin(System.Collections.Generic.IReadOnlyList<string> args, out (long X, long Y, long Z) cells)
    {
        cells = default;
        for (int i = 0; i < args.Count; i++)
        {
            string a = args[i];
            string? value = a.StartsWith(ViewOriginFlag + "=", System.StringComparison.Ordinal) ? a[(ViewOriginFlag.Length + 1)..]
                : a == ViewOriginFlag && i + 1 < args.Count ? args[i + 1] : null;
            if (value is null) continue;
            var parts = value.Split(',');
            if (parts.Length == 3
                && long.TryParse(parts[0].Trim(), System.Globalization.NumberStyles.AllowLeadingSign, System.Globalization.CultureInfo.InvariantCulture, out long x)
                && long.TryParse(parts[1].Trim(), System.Globalization.NumberStyles.AllowLeadingSign, System.Globalization.CultureInfo.InvariantCulture, out long y)
                && long.TryParse(parts[2].Trim(), System.Globalization.NumberStyles.AllowLeadingSign, System.Globalization.CultureInfo.InvariantCulture, out long z))
            {
                cells = (x, y, z);
                return true;
            }
            return false;
        }
        return false;
    }
}
