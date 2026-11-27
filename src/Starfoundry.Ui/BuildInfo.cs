using System.Reflection;

namespace Starfoundry.Ui;

/// <summary>
/// The product version shown in the main menu, the Credits page and the window title. The single source is the <c>VERSION</c> file at
/// the repository root: <c>Directory.Build.props</c> copies it into the assembly version at build time and this class reads it back.
/// </summary>
public static class BuildInfo
{
    public const string ProductName = "Starfoundry";

    /// <summary>The semantic version (for example <c>0.1.0</c> or <c>0.2.0-rc.1</c>), without any build metadata.</summary>
    public static string Version { get; } = Clean(typeof(BuildInfo).Assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion
        ?? typeof(BuildInfo).Assembly.GetName().Version?.ToString(3) ?? "0.0.0");

    /// <summary>The text of the window title: product name and version.</summary>
    public static string WindowTitle => $"{ProductName} {Version}";

    /// <summary>Drops the <c>+commit</c> build metadata the SDK appends to the informational version.</summary>
    public static string Clean(string raw)
    {
        int plus = raw.IndexOf('+');
        return (plus >= 0 ? raw[..plus] : raw).Trim();
    }
}
