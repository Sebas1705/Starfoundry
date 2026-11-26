using System.Globalization;
using System.Text;
using Starfoundry.Sim;

namespace Starfoundry.Ui;

/// <summary>
/// The text format of a saved blueprint (the "save as blueprint" slot hook of the editor; the full slots, presets and variants come later). One header line
/// <c>starfoundry-blueprint 1</c>, an optional <c>name=...</c> line and one <c>mx,my,mz,kind,rotation</c> line per module, kind as its content id. Engine-free,
/// strict on read: a file that is not valid returns null instead of half a blueprint.
/// </summary>
public static class BlueprintFormat
{
    public const string Header = "starfoundry-blueprint 1";

    public static string Serialize(BlueprintDraft draft, string name)
    {
        var sb = new StringBuilder();
        sb.Append(Header).Append('\n');
        sb.Append("name=").Append(name.Replace('\n', ' ').Trim()).Append('\n');
        foreach (var (k, kind) in draft.Modules)
            sb.Append(k.Mx.ToString(CultureInfo.InvariantCulture)).Append(',').Append(k.My.ToString(CultureInfo.InvariantCulture)).Append(',').Append(k.Mz.ToString(CultureInfo.InvariantCulture))
              .Append(',').Append(ModuleKindIds.Id(kind)).Append(',').Append(draft.RotationOf(k).ToString(CultureInfo.InvariantCulture)).Append('\n');
        return sb.ToString();
    }

    /// <summary>Reads a blueprint: its name and parts, or null when the text is not a valid one (wrong header, bad number, unknown kind, duplicate cell, too many modules).</summary>
    public static (string Name, List<((int Mx, int My, int Mz) Cell, ModuleKind Kind, int Rotation)> Parts)? Parse(string text)
    {
        var lines = text.Replace("\r", "").Split('\n', StringSplitOptions.RemoveEmptyEntries);
        if (lines.Length == 0 || lines[0].Trim() != Header) return null;
        string name = "";
        var parts = new List<((int, int, int), ModuleKind, int)>();
        var seen = new HashSet<(int, int, int)>();
        foreach (var raw in lines.Skip(1))
        {
            var line = raw.Trim();
            if (line.StartsWith("name=", StringComparison.Ordinal)) { name = line[5..]; continue; }
            var f = line.Split(',');
            if (f.Length != 5) return null;
            if (!int.TryParse(f[0], NumberStyles.Integer, CultureInfo.InvariantCulture, out int x) || !int.TryParse(f[1], NumberStyles.Integer, CultureInfo.InvariantCulture, out int y)
                || !int.TryParse(f[2], NumberStyles.Integer, CultureInfo.InvariantCulture, out int z) || !int.TryParse(f[4], NumberStyles.Integer, CultureInfo.InvariantCulture, out int rot)) return null;
            var kind = Enum.GetValues<ModuleKind>().Cast<ModuleKind?>().FirstOrDefault(k => ModuleKindIds.Id(k!.Value) == f[3]);
            if (kind is null || rot is < 0 or > 3 || !StructureGrid.IsValidLocalModule(x, y, z) || !seen.Add((x, y, z))) return null;
            parts.Add(((x, y, z), kind.Value, rot));
            if (parts.Count > DesignerRules.MaxModules) return null;
        }
        return (name, parts.Select(p => (((int Mx, int My, int Mz))p.Item1, p.Item2, p.Item3)).ToList());
    }
}
