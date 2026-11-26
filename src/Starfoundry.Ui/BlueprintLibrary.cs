using System.Globalization;
using System.Text;
using Starfoundry.Sim;

namespace Starfoundry.Ui;

/// <summary>Where saved designs live: one text file per design. The game backs it with <c>user://blueprints</c>; tests use <see cref="MemoryBlueprintStore"/>. Never touches the command log or the save game.</summary>
public interface IBlueprintStore
{
    IReadOnlyList<string> Files();
    string? Read(string file);
    bool Write(string file, string text);
    bool Delete(string file);
}

/// <summary>An in-memory store (tests, and a fallback when the user directory cannot be written).</summary>
public sealed class MemoryBlueprintStore : IBlueprintStore
{
    private readonly SortedDictionary<string, string> _files = new(StringComparer.Ordinal);
    public IReadOnlyList<string> Files() => _files.Keys.ToList();
    public string? Read(string file) => _files.TryGetValue(file, out var t) ? t : null;
    public bool Write(string file, string text) { _files[file] = text; return true; }
    public bool Delete(string file) => _files.Remove(file);
}

/// <summary>One saved design as the editor stores it: the structure it is for, its name and variant, whether it is pinned as a preset, the modules and (stations) the interior.</summary>
public sealed record DesignData(string Structure, string Name, string Variant, bool Preset,
    IReadOnlyList<((int Mx, int My, int Mz) Cell, ModuleKind Kind, int Rotation)> Modules, BlueprintInterior Interior);

/// <summary>A row of the design list.</summary>
public sealed record DesignInfo(string File, string Structure, string Name, string Variant, bool Preset, int Modules, int Parts);

/// <summary>
/// Saved blueprints for ships and stations (spec E1 and phase 5 of the blueprint editor): several named designs per structure kind, with variants (the same design
/// in another shape), pinned presets, rename, duplicate and delete. Text format version 2 (version 1, <see cref="BlueprintFormat"/>, still reads as a ship or station
/// design without an interior). Engine-free; stored in user data only, so the simulation, the command log and the save game never see it.
/// </summary>
public static class BlueprintDesignFormat
{
    public const string Header = "starfoundry-blueprint 2";
    public const int MaxName = 40;

    public static string Clean(string text, int max = MaxName)
    {
        var sb = new StringBuilder();
        foreach (char c in text) if (!char.IsControl(c)) sb.Append(c);
        var s = sb.ToString().Trim();
        return s.Length > max ? s[..max].TrimEnd() : s;
    }

    private static string I(int v) => v.ToString(CultureInfo.InvariantCulture);

    public static string Serialize(DesignData d, ContentDb content)
    {
        var sb = new StringBuilder();
        sb.Append(Header).Append('\n');
        sb.Append("structure=").Append(d.Structure).Append('\n');
        sb.Append("name=").Append(Clean(d.Name)).Append('\n');
        if (Clean(d.Variant).Length > 0) sb.Append("variant=").Append(Clean(d.Variant)).Append('\n');
        if (d.Preset) sb.Append("preset=1\n");
        foreach (var (c, kind, rot) in d.Modules.OrderBy(m => m.Cell))
            sb.Append("m=").Append(I(c.Mx)).Append(',').Append(I(c.My)).Append(',').Append(I(c.Mz)).Append(',').Append(ModuleKindIds.Id(kind)).Append(',').Append(I(rot)).Append('\n');
        var it = d.Interior;
        foreach (var m in it.Machines)
            sb.Append("machine=").Append(I(m.X)).Append(',').Append(I(m.Y)).Append(',').Append(I(m.Z)).Append(',').Append(content.Machines[m.Type].Id).Append(',').Append(m.Recipe < 0 ? "-" : content.Recipes[m.Recipe].Id).Append('\n');
        foreach (var b in it.Belts)
            sb.Append("belt=").Append(I(b.X)).Append(',').Append(I(b.Y)).Append(',').Append(I(b.Z)).Append(',').Append(b.Dir).Append(',').Append(I(b.Length)).Append(',').Append(b.Tier).Append('\n');
        foreach (var l in it.Links)
        {
            (int X, int Y, int Z) F, T;
            switch (l.Kind)
            {
                case BlueprintLinkKind.BeltToBelt: F = Cell(it.Belts[l.From]); T = Cell(it.Belts[l.To]); break;
                case BlueprintLinkKind.BeltToMachine: F = Cell(it.Belts[l.From]); T = Cell(it.Machines[l.To]); break;
                default: F = Cell(it.Machines[l.From]); T = Cell(it.Belts[l.To]); break;
            }
            sb.Append("link=").Append((int)l.Kind).Append(',').Append(I(F.X)).Append(',').Append(I(F.Y)).Append(',').Append(I(F.Z)).Append(',').Append(I(T.X)).Append(',').Append(I(T.Y)).Append(',').Append(I(T.Z)).Append('\n');
        }
        return sb.ToString();
    }

    private static (int, int, int) Cell(BlueprintBelt b) => (b.X, b.Y, b.Z);
    private static (int, int, int) Cell(BlueprintMachine m) => (m.X, m.Y, m.Z);

    private static bool Int(string s, out int v) => int.TryParse(s, NumberStyles.Integer, CultureInfo.InvariantCulture, out v);

    private static ModuleKind? KindOf(string id) => Enum.GetValues<ModuleKind>().Cast<ModuleKind?>().FirstOrDefault(k => ModuleKindIds.Id(k!.Value) == id);

    /// <summary>Reads a version 2 design, or a version 1 blueprint (a design with no interior). Null for anything that is not valid: a wrong header, a bad number, an unknown id, a duplicate cell, too many parts.</summary>
    public static DesignData? Parse(string text, ContentDb content)
    {
        var lines = text.Replace("\r", "").Split('\n', StringSplitOptions.RemoveEmptyEntries);
        if (lines.Length == 0) return null;
        if (lines[0].Trim() == BlueprintFormat.Header)
        {
            var v1 = BlueprintFormat.Parse(text);
            if (v1 is null) return null;
            return new DesignData("ship", Clean(v1.Value.Name), "", false, v1.Value.Parts, BlueprintInterior.Empty);
        }
        if (lines[0].Trim() != Header) return null;
        string structure = "ship", name = "", variant = "";
        bool preset = false;
        var modules = new List<((int, int, int), ModuleKind, int)>();
        var seen = new HashSet<(int, int, int)>();
        var machines = new List<BlueprintMachine>();
        var belts = new List<BlueprintBelt>();
        var rawLinks = new List<(int Kind, (int, int, int) F, (int, int, int) T)>();
        var machineCells = new HashSet<(int, int, int)>();
        var beltCells = new HashSet<(int, int, int)>();
        foreach (var raw in lines.Skip(1))
        {
            int eq = raw.IndexOf('=');
            if (eq < 0) return null;
            string key = raw[..eq].Trim(), val = raw[(eq + 1)..].Trim();
            var f = val.Split(',');
            switch (key)
            {
                case "structure": if (val is not ("ship" or "station")) return null; structure = val; break;
                case "name": name = Clean(val); break;
                case "variant": variant = Clean(val); break;
                case "preset": preset = val == "1"; break;
                case "m":
                {
                    if (f.Length != 5 || !Int(f[0], out int x) || !Int(f[1], out int y) || !Int(f[2], out int z) || !Int(f[4], out int rot)) return null;
                    var kind = KindOf(f[3]);
                    if (kind is null || rot is < 0 or > 3 || !StructureGrid.IsValidLocalModule(x, y, z) || !seen.Add((x, y, z))) return null;
                    modules.Add(((x, y, z), kind.Value, rot));
                    if (modules.Count > DesignerRules.MaxModules) return null;
                    break;
                }
                case "machine":
                {
                    if (f.Length != 5 || !Int(f[0], out int x) || !Int(f[1], out int y) || !Int(f[2], out int z)) return null;
                    int type = content.Machines.Select((m, i) => (m, i)).FirstOrDefault(p => p.m.Id == f[3]) is { m: not null } mt ? mt.i : -1;
                    if (type < 0 || !machineCells.Add((x, y, z))) return null;
                    int recipe = -1;
                    if (f[4] != "-") { recipe = content.Recipes.Select((r, i) => (r, i)).FirstOrDefault(p => p.r.Id == f[4]) is { r: not null } rt ? rt.i : -1; if (recipe < 0) return null; }
                    machines.Add(new BlueprintMachine(x, y, z, type, recipe));
                    break;
                }
                case "belt":
                {
                    if (f.Length != 6 || !Int(f[0], out int x) || !Int(f[1], out int y) || !Int(f[2], out int z) || !Int(f[4], out int len)) return null;
                    if (!Enum.TryParse<Dir>(f[3], out var dir) || !Enum.IsDefined(dir) || !Enum.TryParse<BeltTier>(f[5], out var tier) || !Enum.IsDefined(tier) || len < 1 || len > SimWorld.MaxSegmentCells) return null;
                    if (!beltCells.Add((x, y, z))) return null;
                    belts.Add(new BlueprintBelt(x, y, z, dir, len, tier));
                    break;
                }
                case "link":
                {
                    if (f.Length != 7 || !Int(f[0], out int k) || k is < 0 or > 2) return null;
                    var n = new int[6];
                    for (int i = 0; i < 6; i++) if (!Int(f[i + 1], out n[i])) return null;
                    rawLinks.Add((k, (n[0], n[1], n[2]), (n[3], n[4], n[5])));
                    break;
                }
                default: return null;
            }
            if (machines.Count + belts.Count > BlueprintInterior.MaxParts || rawLinks.Count > BlueprintInterior.MaxLinks) return null;
        }
        if (name.Length == 0) return null;
        var mi = new Dictionary<(int, int, int), int>();
        for (int i = 0; i < machines.Count; i++) mi[(machines[i].X, machines[i].Y, machines[i].Z)] = i;
        var bi = new Dictionary<(int, int, int), int>();
        for (int i = 0; i < belts.Count; i++) bi[(belts[i].X, belts[i].Y, belts[i].Z)] = i;
        var links = new List<BlueprintLink>();
        foreach (var (k, from, to) in rawLinks)
        {
            int a, b;
            switch ((BlueprintLinkKind)k)
            {
                case BlueprintLinkKind.BeltToBelt: if (!bi.TryGetValue(from, out a) || !bi.TryGetValue(to, out b)) return null; break;
                case BlueprintLinkKind.BeltToMachine: if (!bi.TryGetValue(from, out a) || !mi.TryGetValue(to, out b)) return null; break;
                default: if (!mi.TryGetValue(from, out a) || !bi.TryGetValue(to, out b)) return null; break;
            }
            links.Add(new BlueprintLink((BlueprintLinkKind)k, a, b));
        }
        return new DesignData(structure, name, variant, preset,
            modules.Select(p => (((int Mx, int My, int Mz))p.Item1, p.Item2, p.Item3)).ToList(),
            new BlueprintInterior(machines.ToArray(), belts.ToArray(), links.ToArray()));
    }
}

/// <summary>The list of saved designs of one store: save (a new design, another variant, or an overwrite), load, rename, duplicate as a variant, pin as preset and delete.</summary>
public sealed class BlueprintLibrary
{
    public const int MaxDesigns = 64;

    private readonly IBlueprintStore _store;
    private readonly ContentDb _content;

    public BlueprintLibrary(IBlueprintStore store, ContentDb content) { _store = store; _content = content; }

    /// <summary>The designs of a structure kind ("ship" or "station"): presets first, then by name and variant.</summary>
    public IReadOnlyList<DesignInfo> List(string structure)
    {
        var list = new List<DesignInfo>();
        foreach (var f in _store.Files())
        {
            if (!f.EndsWith(".bp", StringComparison.Ordinal)) continue;
            var text = _store.Read(f);
            var d = text is null ? null : BlueprintDesignFormat.Parse(text, _content);
            if (d is null || d.Structure != structure) continue;
            list.Add(new DesignInfo(f, d.Structure, d.Name, d.Variant, d.Preset, d.Modules.Count, d.Interior.Machines.Length + d.Interior.Belts.Length));
        }
        return list.OrderByDescending(i => i.Preset).ThenBy(i => i.Name, StringComparer.OrdinalIgnoreCase).ThenBy(i => i.Variant, StringComparer.OrdinalIgnoreCase).ToList();
    }

    public DesignData? Load(string file) => _store.Read(file) is { } t ? BlueprintDesignFormat.Parse(t, _content) : null;

    private static string Slug(string s)
    {
        var sb = new StringBuilder();
        foreach (char c in s.ToLowerInvariant()) sb.Append(char.IsAsciiLetterOrDigit(c) ? c : '_');
        var r = sb.ToString().Trim('_');
        return r.Length == 0 ? "design" : r;
    }

    /// <summary>The file a name and variant live in: stable for the same pair, so saving again overwrites (the one place a design is replaced).</summary>
    public static string FileFor(string structure, string name, string variant) => $"{structure}_{Slug(name)}{(variant.Length > 0 ? "__" + Slug(variant) : "")}.bp";

    /// <summary>Saves a design. Returns its file, or null when the name is empty, the store refuses it, or the structure already holds <see cref="MaxDesigns"/> designs and this one is new.</summary>
    public string? Save(DesignData d)
    {
        var name = BlueprintDesignFormat.Clean(d.Name);
        if (name.Length == 0) return null;
        var data = d with { Name = name, Variant = BlueprintDesignFormat.Clean(d.Variant) };
        string file = FileFor(d.Structure, data.Name, data.Variant);
        if (_store.Read(file) is null && List(d.Structure).Count >= MaxDesigns) return null;
        return _store.Write(file, BlueprintDesignFormat.Serialize(data, _content)) ? file : null;
    }

    public bool Delete(string file) => _store.Read(file) is not null && _store.Delete(file);

    /// <summary>Renames a design (its name; the variant stays). Refuses an empty name or one already taken by another design.</summary>
    public string? Rename(string file, string newName)
    {
        var d = Load(file);
        var name = BlueprintDesignFormat.Clean(newName);
        if (d is null || name.Length == 0) return null;
        string target = FileFor(d.Structure, name, d.Variant);
        if (target != file && _store.Read(target) is not null) return null;
        if (_store.Write(target, BlueprintDesignFormat.Serialize(d with { Name = name }, _content)) is false) return null;
        if (target != file) _store.Delete(file);
        return target;
    }

    /// <summary>Copies a design as another variant of the same name (for example "long range"); refuses an empty variant or one that exists.</summary>
    public string? Duplicate(string file, string variant)
    {
        var d = Load(file);
        var v = BlueprintDesignFormat.Clean(variant);
        if (d is null || v.Length == 0) return null;
        string target = FileFor(d.Structure, d.Name, v);
        if (_store.Read(target) is not null || List(d.Structure).Count >= MaxDesigns) return null;
        return _store.Write(target, BlueprintDesignFormat.Serialize(d with { Variant = v, Preset = false }, _content)) ? target : null;
    }

    public bool SetPreset(string file, bool preset)
    {
        var d = Load(file);
        return d is not null && _store.Write(file, BlueprintDesignFormat.Serialize(d with { Preset = preset }, _content));
    }
}
