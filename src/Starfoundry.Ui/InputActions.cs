using System.Text;

namespace Starfoundry.Ui;

/// <summary>Where an action applies. An action may apply in several contexts; two actions of the same active set must never share a key.</summary>
[Flags]
public enum InputContext
{
    None = 0,
    /// <summary>Everything except the pause and help overlays: the direct keys, the ship computer key, quick save and load.</summary>
    Global = 1,
    /// <summary>Seated, nothing in hand.</summary>
    Flying = 2,
    /// <summary>Standing and walking inside the ship.</summary>
    Walking = 4,
    /// <summary>A build tool is in hand.</summary>
    Build = 8,
    /// <summary>The ship computer (Tab) is open.</summary>
    Computer = 16,
    /// <summary>Docked to a station port (on top of flying or walking).</summary>
    Docked = 32,
    /// <summary>The pause menu and the help overlay.</summary>
    Menu = 64,
}

/// <summary>The section of the F1 help an action is listed under.</summary>
public enum HelpGroup { Ship, Weapons, Build, Computer, System }

/// <summary>
/// One thing the player can do with a key. <see cref="Keys"/> are Godot <c>Key</c> enum names ("B", "F5", "Tab", "Key1", "Escape") or a
/// fixed pseudo key starting with "@" (a mouse input: "@click", "@rclick", "@wheel") that cannot be rebound. The name and the one-line hint
/// are Loc entries <c>act.{Id}.name</c> and <c>act.{Id}.hint</c>; the icon is a <c>tool:</c> icon id of <see cref="IconCatalog"/>.
/// </summary>
public sealed record InputAction(string Id, string[] Keys, InputContext Contexts, HelpGroup Group, string Icon)
{
    public string NameKey => "act." + Id + ".name";
    public string HintKey => "act." + Id + ".hint";
}

/// <summary>
/// The single table of every key binding (docs/superpowers/specs/2026-11-25-controls-redesign-design.md). The key handler resolves a key
/// to an action id through <see cref="Find"/>, and the key panel caps and the F1 help are generated from it, so none of them can disagree.
/// Engine-free. Overrides (a future rebinding screen) replace the keys of an action without touching the table.
/// </summary>
public static class InputActions
{
    private const InputContext Fly = InputContext.Flying, Walk = InputContext.Walking, Tool = InputContext.Build, Pc = InputContext.Computer, Dock = InputContext.Docked, Any = InputContext.Global;
    private const InputContext Body = Fly | Walk | Tool;

    public static readonly IReadOnlyList<InputAction> All = new InputAction[]
    {
        // ---- the ship ----
        new("move", new[] { "W", "A", "S", "D" }, Body, HelpGroup.Ship, "tool:walk"),
        new("climb", new[] { "Space", "C" }, Fly | Tool, HelpGroup.Ship, "tool:boost"),
        new("boost", new[] { "Shift" }, Fly, HelpGroup.Ship, "tool:boost"),
        new("run", new[] { "Shift" }, Walk, HelpGroup.Ship, "tool:run"),
        new("look", new[] { "Alt" }, Fly, HelpGroup.Ship, "tool:look"),
        new("roll", new[] { "Q", "E" }, Fly, HelpGroup.Ship, "tool:rotate"),
        new("interact", new[] { "F" }, Body, HelpGroup.Ship, "tool:seat"),
        new("assist", new[] { "Z" }, Fly | Tool, HelpGroup.Ship, "tool:assist"),
        new("scan", new[] { "K" }, Body, HelpGroup.Ship, "tool:scan"),
        new("unload", new[] { "Comma" }, Dock, HelpGroup.Ship, "tool:unload"),
        new("load", new[] { "Period" }, Dock, HelpGroup.Ship, "tool:store"),
        // ---- weapons ----
        new("fire", new[] { "@click" }, Fly, HelpGroup.Weapons, "tool:fireGuns"),
        new("weaponNext", new[] { "V" }, Body, HelpGroup.Weapons, "tool:weapons"),
        new("weaponPick", new[] { "Key1", "Key2", "Key3", "Key4" }, Fly, HelpGroup.Weapons, "tool:weapons"),
        new("weaponWheel", new[] { "@wheel" }, Fly, HelpGroup.Weapons, "tool:weapons"),
        // ---- building (a tool in hand) ----
        new("place", new[] { "@click" }, Tool, HelpGroup.Build, "tool:place"),
        new("demolish", new[] { "@rclick" }, Tool, HelpGroup.Build, "tool:demolish"),
        new("hotbar", new[] { "Key1", "Key2", "Key3", "Key4", "Key5", "Key6", "Key7", "Key8", "Key9", "Key0" }, Tool, HelpGroup.Build, "tool:quick"),
        new("hotbarWheel", new[] { "@wheel" }, Tool, HelpGroup.Build, "tool:quick"),
        new("rotate", new[] { "R" }, Tool, HelpGroup.Build, "tool:rotate"),
        new("recipe", new[] { "Y" }, Tool, HelpGroup.Build, "tool:recipe"),
        new("kind", new[] { "N" }, Tool, HelpGroup.Build, "tool:kind"),
        new("layer", new[] { "Q", "E" }, Tool, HelpGroup.Build, "tool:layer"),
        // ---- the ship computer ----
        new("computer", new[] { "Tab" }, Any, HelpGroup.Computer, "tool:computer"),
        new("openBuild", new[] { "B" }, Any, HelpGroup.Computer, "tool:build"),
        new("openInventory", new[] { "I" }, Any, HelpGroup.Computer, "tool:inventory"),
        new("openMap", new[] { "M" }, Any, HelpGroup.Computer, "tool:map"),
        new("tabs", new[] { "Q", "E" }, Pc, HelpGroup.Computer, "tool:tab"),
        new("tabPick", new[] { "Key1", "Key2", "Key3", "Key4", "Key5", "Key6", "Key7", "Key8" }, Pc, HelpGroup.Computer, "tool:tab"),
        new("mapChoose", new[] { "Up", "Down" }, Pc, HelpGroup.Computer, "tool:pick"),
        new("mapTurn", new[] { "Left", "Right" }, Pc, HelpGroup.Computer, "tool:rotate"),
        new("mapJump", new[] { "Enter" }, Pc, HelpGroup.Computer, "tool:jump"),
        new("mapReset", new[] { "Home" }, Pc, HelpGroup.Computer, "tool:map"),
        new("mapZoom", new[] { "@wheel" }, Pc, HelpGroup.Computer, "tool:map"),
        // ---- the system ----
        new("hudDetail", new[] { "H" }, Any, HelpGroup.System, "tool:info"),
        new("help", new[] { "F1" }, Any | InputContext.Menu, HelpGroup.System, "tool:help"),
        new("quickSave", new[] { "F5" }, Any, HelpGroup.System, "tool:save"),
        new("quickLoad", new[] { "F9" }, Any, HelpGroup.System, "tool:load"),
        new("fullscreen", new[] { "F11" }, Any, HelpGroup.System, "tool:fullscreen"),
        new("cancel", new[] { "Escape" }, Any | InputContext.Menu, HelpGroup.System, "tool:cancel"),
    };

    private static readonly Dictionary<string, InputAction> ById = All.ToDictionary(a => a.Id);
    private static IReadOnlyDictionary<string, string[]> _overrides = new Dictionary<string, string[]>();

    public static InputAction Get(string id) => ById[id];

    public static bool Has(string id) => ById.ContainsKey(id);

    /// <summary>The keys of an action: the player's override when there is one, else the default.</summary>
    public static string[] KeysOf(string id) => _overrides.TryGetValue(id, out var keys) && keys.Length > 0 ? keys : ById[id].Keys;

    /// <summary>The first key of an action, the one the key panel shows and a click on its row presses.</summary>
    public static string Primary(string id) => KeysOf(id)[0];

    /// <summary>The cap text of the first key of an action (see <see cref="Cap"/>).</summary>
    public static string PrimaryCap(string id) => Cap(Primary(id));

    /// <summary>Replaces the keys of some actions (the stub of the rebinding screen); an unknown action is ignored.</summary>
    public static void SetOverrides(IReadOnlyDictionary<string, string[]>? overrides) =>
        _overrides = overrides is null ? new Dictionary<string, string[]>() : overrides.Where(p => ById.ContainsKey(p.Key)).ToDictionary(p => p.Key, p => p.Value);

    /// <summary>Parses the settings value of the overrides: <c>action=Key,Key;action=Key</c>.</summary>
    public static Dictionary<string, string[]> ParseOverrides(string? text)
    {
        var map = new Dictionary<string, string[]>();
        foreach (var part in (text ?? "").Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            int eq = part.IndexOf('=');
            if (eq <= 0) continue;
            var keys = part[(eq + 1)..].Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
            if (keys.Length > 0) map[part[..eq].Trim()] = keys;
        }
        return map;
    }

    public static string FormatOverrides(IReadOnlyDictionary<string, string[]> map) =>
        string.Join(';', map.OrderBy(p => p.Key, StringComparer.Ordinal).Select(p => p.Key + "=" + string.Join(',', p.Value)));

    /// <summary>The action a key triggers in the active contexts, or null. A fixed pseudo key ("@wheel") is looked up the same way.</summary>
    public static InputAction? Find(string key, InputContext active)
    {
        foreach (var a in All)
            if ((a.Contexts & active) != 0 && KeysOf(a.Id).Contains(key)) return a;
        return null;
    }

    /// <summary>The contexts that are active together for what the player is doing.</summary>
    public static InputContext Active(bool computerOpen, bool buildTool, bool standing, bool docked)
    {
        var c = InputContext.Global;
        if (computerOpen) return c | InputContext.Computer;
        c |= buildTool ? InputContext.Build : standing ? InputContext.Walking : InputContext.Flying;
        if (docked) c |= InputContext.Docked;
        return c;
    }

    /// <summary>Every set of contexts <see cref="Active"/> can produce, for the conflict test.</summary>
    public static IEnumerable<InputContext> ActiveSets()
    {
        foreach (bool docked in new[] { false, true })
        {
            yield return Active(false, false, false, docked);
            yield return Active(false, false, true, docked);
            yield return Active(false, true, false, docked);
        }
        yield return Active(true, false, false, false);
        yield return InputContext.Menu;
    }

    // ---- how a key is written -----------------------------------------------------------------------

    /// <summary>The text of a key cap: a Godot key name as the player knows it, or an "@name" cap that the UI looks up as <c>key.cap.name</c>.</summary>
    public static string Cap(string key) => key switch
    {
        "Escape" => "Esc",
        "Comma" => ",",
        "Period" => ".",
        "Space" => "@space",
        "Shift" => "@shift",
        "Enter" => "@enter",
        "Up" => "↑",
        "Down" => "↓",
        "Left" => "←",
        "Right" => "→",
        _ when IsDigitKey(key) => key[3].ToString(),
        _ => key,
    };

    private static bool IsDigitKey(string key) => key.StartsWith("Key") && key.Length == 4 && char.IsDigit(key[3]);

    /// <summary>The caps of every key of an action, with a run of digits written as a range ("1-4").</summary>
    public static string[] Caps(string id)
    {
        var keys = KeysOf(id);
        return keys.Length > 2 && keys.All(IsDigitKey) ? new[] { Cap(keys[0]) + "-" + Cap(keys[^1]) } : keys.Select(Cap).ToArray();
    }

    private static string CapText(string cap) => cap.StartsWith('@') ? Loc.T("key.cap." + cap[1..]) : cap;

    /// <summary>The F1 help: the intro and mouse text (not key bindings), then every action by group, generated from the table in the current language.</summary>
    public static string HelpText(int lineWidth = 116)
    {
        var sb = new StringBuilder();
        sb.Append(Loc.T("help.title")).Append("\n\n").Append(Loc.T("help.intro")).Append('\n');
        foreach (var group in Enum.GetValues<HelpGroup>())
        {
            sb.Append('\n').Append(Loc.T("help.group." + group.ToString().ToLowerInvariant())).Append('\n');
            var line = new StringBuilder("  ");
            foreach (var a in All.Where(a => a.Group == group))
            {
                string entry = string.Join(' ', Caps(a.Id).Select(CapText)) + "  " + Loc.T(a.NameKey);
                if (line.Length > 2 && line.Length + entry.Length > lineWidth) { sb.Append(line.ToString().TrimEnd()).Append('\n'); line.Clear().Append("  "); }
                line.Append(entry).Append("    ");
            }
            if (line.Length > 2) sb.Append(line.ToString().TrimEnd()).Append('\n');
        }
        return sb.ToString();
    }
}
