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
}
