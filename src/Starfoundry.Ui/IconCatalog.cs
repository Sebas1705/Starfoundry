using Starfoundry.Sim;

namespace Starfoundry.Ui;

/// <summary>Where an icon id comes from, as far as fallbacks and names care.</summary>
public enum IconKind { Item, Machine, Module, Belt, Research, Field, Enemy, Obstacle, Star, Arch, Tool, Status }

/// <summary>
/// The registry of the icon set. An icon id is <c>kind:id</c>: <c>item:core:iron_ore</c>, <c>machine:core:smelter</c>, <c>module:drone_bay</c>,
/// <c>belt:mk2</c>, <c>research:core:logistics</c>, <c>field:physics</c>, <c>enemy:core:scout</c>, <c>obstacle:core:ion_cloud</c>,
/// <c>star:core:pulsar</c>, <c>arch:core:ice_field</c>, <c>tool:build</c> and <c>status:power</c>. Each id maps to one glyph of
/// <see cref="IconGlyphs"/> and a tint (the material's colour, the field's colour or none). An id nobody registered (new content, a mod)
/// falls back to a generic glyph of its kind and is reported once by <see cref="Missing"/>, so the screens never break. Pure data,
/// tested without the engine; the game draws it in <c>Icons</c>.
/// </summary>
public static class IconCatalog
{
    /// <summary>Packed 0xRRGGBB tint of an icon, or 0 when the caller colours it (status and tool icons take the colour of their state).</summary>
    public readonly record struct Def(string Glyph, uint Tint);

    private static readonly Dictionary<string, Def> Map = new();
    private static readonly HashSet<string> Reported = new();

    /// <summary>Called with an id that fell back to a generic glyph (the game logs it).</summary>
    public static event Action<string>? FellBack;

    // ---- tints ----
    public const uint Iron = 0xE59A6C, Carbon = 0xB4BCCB, Ice = 0x86E0FF, Titanium = 0xC3CCF2, Nickel = 0x86E6B2, Quartz = 0xF5AEDB,
        Hydrogen = 0x93BDFF, Silicon = 0xCDBFFF, Circuit = 0x72E6A4, Alloy = 0xDDB9FF, Cell = 0xFFCB5C, Ammo = 0xFFD580, Biomass = 0x93E57D,
        Fiber = 0x8D9AB8, Composite = 0xB6C2E0, Coolant = 0x7FE9FF, Resonant = 0xC48CFF, Lens = 0xE3C4FF;
    public const uint Basic = 0xB5C4E0, Engineering = 0xFFB35C, Biology = 0x7FE08A, Physics = 0x7FB4FF;
    public const uint Hostile = 0xFF7468;

    static IconCatalog()
    {
        // Materials: the glyph is the form (ore, ingot, plate...), the tint the material.
        Add("item:core:iron_ore", "ore", Iron); Add("item:core:iron_ingot", "ingot", Iron); Add("item:core:iron_plate", "plate", Iron);
        Add("item:core:titanium_ore", "ore", Titanium); Add("item:core:titanium_ingot", "ingot", Titanium); Add("item:core:titanium_plate", "plate", Titanium);
        Add("item:core:nickel_ore", "ore", Nickel); Add("item:core:nickel_ingot", "ingot", Nickel); Add("item:core:nickel_plate", "plate", Nickel);
        Add("item:core:carbon", "lump", Carbon); Add("item:core:ice", "gem", Ice); Add("item:core:quartz", "crystal", Quartz);
        Add("item:core:ammo", "ammo", Ammo); Add("item:core:hydrogen", "drop", Hydrogen); Add("item:core:silicon", "wafer", Silicon);
        Add("item:core:circuit", "chip", Circuit); Add("item:core:titanium_alloy", "alloy", Alloy); Add("item:core:fuel_cell", "cell", Cell);
        Add("item:core:science_engineering", "gear", Engineering); Add("item:core:science_biology", "leaf", Biology);
        Add("item:core:science_physics", "atom", Physics); Add("item:core:biomass", "sprout", Biomass);
        // The optional chains: composites, coolant and the salvaged resonant crystal (docs/prototypes/materials.md).
        Add("item:core:carbon_fiber", "ingot", Fiber); Add("item:core:composite_plate", "plate", Composite); Add("item:core:coolant", "drop", Coolant);
        Add("item:core:resonant_crystal", "crystal", Resonant); Add("item:core:focus_lens", "gem", Lens);

        // Modules.
        Add("module:core", "core", 0); Add("module:cockpit", "cockpit", 0); Add("module:engine", "engine", 0); Add("module:reactor", "reactor", 0);
        Add("module:cargo", "cargo", 0); Add("module:workshop", "gear", 0); Add("module:defence", "turret", 0); Add("module:hull", "hull", 0);
        Add("module:drone_bay", "drone", 0); Add("module:shield", "shield", 0); Add("module:sensor", "dish", 0); Add("module:reactor_mk2", "reactor2", 0);
        Add("module:dock", "dock", 0);
        Add("module:mining_laser", "laser", 0); Add("module:mining_laser_mk2", "laser2", 0); Add("module:gun", "gun", 0); Add("module:gun_mk2", "gun2", 0); Add("module:shipyard", "shipyard", 0);

        // Machines. The ports take the colour of the ore they unload, so the five of them tell apart at a glance.
        Add("machine:core:extractor", "laser", 0); Add("machine:core:drill", "drill", 0); Add("machine:core:smelter", "furnace", 0);
        Add("machine:core:micro_refinery", "furnace", 0); Add("machine:core:assembler", "arm", 0); Add("machine:core:research_lab", "flask", 0);
        Add("machine:core:depot", "depot", 0); Add("machine:core:turret", "turret", 0); Add("machine:core:wall", "wall", 0);
        Add("machine:core:repair_bay", "repairBay", 0); Add("machine:core:unloader", "port", Ammo); Add("machine:core:ore_unloader", "port", Iron);
        Add("machine:core:titanium_ore_unloader", "port", Titanium); Add("machine:core:nickel_ore_unloader", "port", Nickel);
        Add("machine:core:ice_unloader", "port", Ice); Add("machine:core:quartz_unloader", "port", Quartz); Add("machine:core:carbon_unloader", "port", Carbon);
        Add("machine:core:hydrogen_cracker", "molecule", Hydrogen); Add("machine:core:engineering_bench", "bench", Engineering);
        Add("machine:core:physics_lab", "atom", Physics); Add("machine:core:greenhouse", "greenhouse", Biology);
        Add("machine:core:crystal_unloader", "port", Resonant);

        // Belts.
        Add("belt:mk1", "belt1", 0); Add("belt:mk2", "belt2", 0); Add("belt:mk3", "belt3", 0);

        // Science fields (also the colour of every research node of the field).
        Add("field:basic", "plateStack", Basic); Add("field:engineering", "gear", Engineering); Add("field:biology", "leaf", Biology); Add("field:physics", "atom", Physics);

        // Research nodes: a glyph that says what the node is about; the tint comes from the node's field (see ResearchTint).
        foreach (var (id, glyph) in ResearchGlyphs) Add("research:core:" + id, glyph, 0);

        // Enemies, obstacles, stars and system archetypes.
        Add("enemy:core:scout", "scout", Hostile); Add("enemy:core:striker", "striker", Hostile); Add("enemy:core:brute", "brute", Hostile); Add("enemy:core:bulwark", "bulwark", Hostile);
        Add("obstacle:core:asteroid_small", "asteroid", Carbon); Add("obstacle:core:asteroid_big", "asteroid", Carbon); Add("obstacle:core:floating_mine", "mine", Hostile);
        Add("obstacle:core:unstable_crystal", "crystal", Quartz); Add("obstacle:core:ion_cloud", "cloud", Hydrogen); Add("obstacle:core:gravity_cloud", "cloud", Silicon);
        Add("obstacle:core:derelict_wreck", "wreck", Carbon); Add("obstacle:core:drifting_debris", "debris", Carbon);
        Add("star:core:yellow_dwarf", "sun", 0xFFE08A); Add("star:core:red_dwarf", "sun", 0xFF8A70); Add("star:core:orange_star", "sun", 0xFFB070);
        Add("star:core:blue_giant", "sun", 0x8FC0FF); Add("star:core:binary_star", "binary", 0xFFD9A0); Add("star:core:pulsar", "pulsar", 0xC7B5FF);
        Add("arch:core:home", "station", 0); Add("arch:core:dense_belt", "asteroid", Carbon); Add("arch:core:sparse_belt", "asteroid", Carbon);
        Add("arch:core:carbon_rich", "lump", Carbon); Add("arch:core:nickel_field", "ore", Nickel); Add("arch:core:ice_field", "gem", Ice);
        Add("arch:core:titanium_vein", "ore", Titanium); Add("arch:core:quartz_geode", "crystal", Quartz);

        // Tools and actions (key panel rows, buttons): coloured by their state, so no tint.
        foreach (var (id, glyph) in ToolGlyphs) Add("tool:" + id, glyph, 0);
        foreach (var (id, glyph) in InventoryToolGlyphs) Add("tool:" + id, glyph, 0);
        foreach (var (id, glyph) in StatusGlyphs) Add("status:" + id, glyph, 0);
    }

    private static void Add(string id, string glyph, uint tint)
    {
        if (!IconGlyphs.Has(glyph)) throw new InvalidOperationException($"icon {id} uses the unknown glyph {glyph}");
        Map[id] = new Def(glyph, tint);
    }

    /// <summary>Tool and action icons by id (the part after <c>tool:</c>).</summary>
    public static readonly (string Id, string Glyph)[] ToolGlyphs =
    {
        ("build", "build"), ("demolish", "trash"), ("mine", "laser"), ("weapons", "crosshair"), ("scan", "radar"), ("map", "starmap"),
        ("research", "flask"), ("undock", "undock"), ("dock", "dock"), ("jump", "jump"), ("repair", "wrench"), ("refine", "furnace"),
        ("seat", "seat"), ("walk", "walk"), ("look", "eye"), ("help", "help"), ("language", "globe"), ("boost", "bolt"), ("pause", "pause"),
        ("settings", "sliders"), ("save", "save"), ("load", "load"), ("rotate", "rotate"), ("layer", "layers"), ("unload", "unload"),
        ("store", "upload"), ("cancel", "cross"), ("close", "cross"), ("found", "station"), ("pick", "cursor"), ("inspect", "magnifier"),
        ("recipe", "cycle"), ("kind", "tag"), ("tab", "tabs"), ("start", "play"), ("info", "info"), ("stock", "depot"), ("module", "cargo"),
        ("drones", "drone"), ("place", "build"), ("quick", "bolt"), ("run", "bolt"), ("hp", "heart"), ("menu", "tabs"), ("resume", "play"),
        ("hold", "cargo"), ("designer", "shipyard"), ("fireGuns", "gun"), ("belt", "belt"), ("machine", "arm"), ("target", "crosshair"), ("add", "plus"),
        ("assist", "gauge"), ("computer", "tabs"), ("fullscreen", "layers"), ("systems", "gauge"),
        ("move", "arm"), ("mirror", "tabs"), ("copy", "depot"), ("paste", "upload"), ("undo", "rotate"), ("redo", "cycle"),
    };

    /// <summary>The icons of the personal inventory panel (ADR 0035), by id (the part after <c>tool:</c>).</summary>
    public static readonly (string Id, string Glyph)[] InventoryToolGlyphs =
    {
        ("inventory", "backpack"), ("sort", "sort"), ("bin", "trash"), ("invAll", "tabs"), ("invOre", "ore"), ("invParts", "plate"), ("invFuel", "drop"), ("invAmmo", "ammo"),
    };

    /// <summary>Status icons by id (the part after <c>status:</c>).</summary>
    public static readonly (string Id, string Glyph)[] StatusGlyphs =
    {
        ("power", "bolt"), ("fuel", "drop"), ("hull", "ship"), ("shield", "shield"), ("speed", "gauge"), ("cargo", "cargo"), ("warning", "warning"),
        ("alert", "alarm"), ("objective", "flag"), ("enemy", "hostile"), ("station", "station"), ("port", "dock"), ("gate", "gate"),
        ("deposit", "ore"), ("check", "check"), ("cross", "cross"), ("locked", "lock"), ("time", "clock"), ("wave", "waves"), ("threat", "crosshair"),
        ("system", "star"), ("health", "heart"), ("signature", "radar"), ("research", "flask"), ("shieldOk", "shieldCheck"), ("drone", "drone"),
    };

    private static readonly (string Id, string Glyph)[] ResearchGlyphs =
    {
        ("logistics", "belt"), ("carbon_drilling", "drill"), ("advanced_logistics", "belt3"), ("jump_drive", "jump"), ("turret_calibration", "turret"),
        ("reinforced_plating", "hull"), ("rapid_loader", "unload"), ("field_repair", "wrench"), ("ship_armor", "ship"), ("ship_cannon", "crosshair"),
        ("overclock", "bolt"), ("alloy_refinement", "alloy"), ("nanite_repair", "repairBay"), ("titanium_smelting", "furnace"), ("nickel_smelting", "furnace"),
        ("advanced_plating", "plateStack"), ("drone_automation", "drone"), ("silicon_processing", "wafer"), ("electronics", "chip"), ("shielding", "shield"),
        ("sensors", "dish"), ("advanced_power", "reactor2"), ("structural_plating", "hull"), ("alloy_hulls", "alloy"), ("repair_drones", "drone"),
        ("automated_turrets", "turret"), ("cargo_expansion", "cargo"), ("algae_fuel", "drop"), ("closed_loop", "cycle"), ("medical_bay", "repairBay"),
        ("bio_sensors", "dish"), ("symbiotic_drones", "drone"), ("field_theory", "atom"), ("efficient_reactors", "reactor"), ("long_range_jump", "jump"),
        ("targeting_lasers", "laser"), ("quantum_sensors", "radar"), ("laser_focusing", "laser"), ("beam_collimator", "crosshair"),
        ("composites", "plateStack"), ("cryogenics", "drop"), ("resonance", "crystal"),
        ("ion_thrusters", "engine"), ("thrust_vectoring", "gauge"), ("plasma_drive", "bolt"),
    };

    /// <summary>Every registered id, for the gallery and the tests.</summary>
    public static IEnumerable<string> All => Map.Keys;

    public static bool IsRegistered(string id) => Map.ContainsKey(id);

    /// <summary>The icon id of a content id (for example <c>core:iron_ore</c> of kind <see cref="IconKind.Item"/>).</summary>
    public static string Id(IconKind kind, string contentId) => kind.ToString().ToLowerInvariant() switch
    {
        "item" => "item:" + contentId,
        _ => kind.ToString().ToLowerInvariant() + ":" + contentId,
    };

    public static string ForItem(string contentId) => "item:" + contentId;
    public static string ForMachine(string contentId) => "machine:" + contentId;
    public static string ForModule(ModuleKind kind) => "module:" + ModuleKindIds.Id(kind);
    public static string ForResearch(string contentId) => "research:" + contentId;
    public static string ForField(ResearchField field) => "field:" + field.ToString().ToLowerInvariant();
    public static string ForBelt(BeltTier tier) => "belt:mk" + ((int)tier + 1);
    public static string ForEnemy(string contentId) => "enemy:" + contentId;
    public static string ForBuildTab(BuildCategory c) => c switch
    {
        BuildCategory.Logistics => "belt:mk1",
        BuildCategory.Production => "machine:core:smelter",
        BuildCategory.Defence => "status:shield",
        _ => "module:hull",
    };

    /// <summary>The icon of a guided step (the checklist strip of the steps panel).</summary>
    public static string ForObjective(Objective o) => o switch
    {
        Objective.Fly => "status:speed", Objective.Mine => "tool:mine", Objective.Refine => "tool:refine", Objective.Drill => "machine:core:drill",
        Objective.Smelter => "machine:core:smelter", Objective.Depot => "machine:core:depot", Objective.Assembler => "machine:core:assembler",
        Objective.Lab => "machine:core:research_lab", Objective.Research => "tool:research", Objective.Scan => "tool:scan", Objective.Defend => "tool:weapons",
        Objective.Expand => "tool:found", Objective.Core => "module:core", Objective.CargoPort => "machine:core:ore_unloader",
        Objective.WatchFactory => "item:core:iron_plate", Objective.FoundStation => "tool:found", Objective.MakeFuel => "item:core:hydrogen",
        Objective.AddModule => "tool:module", Objective.Automate => "tool:drones", Objective.FabBay => "module:workshop",
        Objective.Dock => "tool:dock", Objective.Salvage => "obstacle:core:derelict_wreck", Objective.Upgrade => "module:shipyard", _ => "status:objective",
    };

    /// <summary>The icon of a goal of a scenario, from what it asks for: the item, the module, the research node or a status symbol.</summary>
    public static string ForGoal(GoalDef g, ContentDb content) => g.Type switch
    {
        GoalType.SurviveWaves => "status:wave",
        GoalType.Research => ForResearch(content.Research[g.Node].Id),
        GoalType.Produce or GoalType.Mine or GoalType.Stock => ForItem(content.Items[g.Item].Id),
        GoalType.VisitSystems => "status:system",
        GoalType.FoundStations => "tool:found",
        GoalType.BuildModule or GoalType.HaveModule => ForModule(g.Kind!.Value),
        GoalType.KeepAlive => g.Kind is { } k ? ForModule(k) : "status:hull",
        GoalType.KeepFuel => "status:fuel",
        GoalType.Salvage => "obstacle:core:derelict_wreck",
        GoalType.Dock => "tool:dock",
        GoalType.ShipSpeed => "status:speed",
        GoalType.KillEnemies => "status:enemy",
        _ => "status:objective",
    };

    /// <summary>The icon of a key panel row (by the row's id), or null when the row has none.</summary>
    public static string? ForKeyRow(string rowId) => rowId switch
    {
        "boost" => "tool:boost", "build" => "tool:build", "cancel" => "tool:cancel", "choose" => "tool:pick", "close" => "tool:close",
        "demolish" => "tool:demolish", "dock" => "tool:dock", "dockGuide" => "tool:dock", "drones" => "tool:drones", "esc" => "tool:cancel",
        "found" => "tool:found", "help" => "tool:help", "hp" => "tool:hp", "info" => "tool:info", "inspect" => "tool:inspect", "jump" => "tool:jump",
        "kind" => "tool:kind", "lang" => "tool:language", "layer" => "tool:layer", "load" => "tool:store", "look" => "tool:look", "map" => "tool:map",
        "mine" => "tool:mine", "mineFire" => "tool:mine", "module" => "tool:module", "pick" => "tool:pick", "place" => "tool:place",
        "quick" => "tool:quick", "recipe" => "tool:recipe", "refine" => "tool:refine", "repair" => "tool:repair", "research" => "tool:research",
        "resume" => "tool:resume", "roll" => "tool:rotate", "rotate" => "tool:rotate", "run" => "tool:run", "scan" => "tool:scan", "seat" => "tool:seat",
        "start" => "tool:start", "stock" => "tool:stock", "tab" => "tool:tab", "undock" => "tool:undock", "unload" => "tool:unload",
        "inventory" => "tool:inventory", "invDrag" => "tool:pick", "invQuick" => "tool:quick", "invKeys" => "tool:pick", "invClose" => "tool:close",
        "walk" => "tool:walk", "weapons" => "tool:weapons", "designer" => "tool:designer", "fireGuns" => "tool:fireGuns",
        "console" => "tool:look", "assist" => "tool:assist", "hudDetail" => "tool:info", "interact" => "tool:seat", "computer" => "tool:computer", "tabs" => "tool:tab", "press" => "tool:pick", "leave" => "tool:close", "nav" => "tool:pick",
        _ => null,
    };

    /// <summary>
    /// The definition of an id. An unregistered id gets the generic glyph of its kind (research nodes: their field's glyph) and is reported
    /// once through <see cref="FellBack"/>; <paramref name="fallback"/> says so.
    /// </summary>
    public static Def Resolve(string id, out bool fallback)
    {
        if (Map.TryGetValue(id, out var def)) { fallback = false; return def; }
        fallback = true;
        bool first;
        lock (Reported) first = Reported.Add(id);
        if (first) FellBack?.Invoke(id);
        int colon = id.IndexOf(':');
        string kind = colon > 0 ? id[..colon] : "";
        return kind switch
        {
            "item" => new Def("ore", 0),
            "machine" => new Def("arm", 0),
            "module" => new Def("cargo", 0),
            "research" => new Def("flask", 0),
            "enemy" => new Def("hostile", Hostile),
            "obstacle" => new Def("asteroid", 0),
            _ => new Def("generic", 0),
        };
    }

    /// <summary>The tint of a research node's icon: its field's colour.</summary>
    public static uint ResearchTint(ResearchField field) => field switch
    {
        ResearchField.Engineering => Engineering,
        ResearchField.Biology => Biology,
        ResearchField.Physics => Physics,
        _ => Basic,
    };

    /// <summary>The name shown for an icon (tooltips, the gallery), in the current language.</summary>
    public static string Name(string id)
    {
        int colon = id.IndexOf(':');
        if (colon < 0) return id;
        string kind = id[..colon], rest = id[(colon + 1)..];
        switch (kind)
        {
            case "item": case "machine": case "research": case "enemy": case "obstacle": case "star": case "arch":
                return Loc.Name(kind, rest);
            case "module": return Loc.Has("module." + rest.Replace("_", "")) ? Loc.T("module." + rest.Replace("_", "")) : rest.Replace('_', ' ');
            case "belt": return Loc.T("ui.belt", rest[2..]);
            case "field": return Loc.T("research.field." + rest);
            default: return Loc.Has("icon." + id) ? Loc.T("icon." + id) : rest;
        }
    }

    /// <summary>Whether <see cref="Name"/> has real text for the id (a Loc entry), used by the tests.</summary>
    public static bool HasName(string id)
    {
        int colon = id.IndexOf(':');
        if (colon < 0) return false;
        string kind = id[..colon], rest = id[(colon + 1)..];
        return kind switch
        {
            "item" or "machine" or "research" or "enemy" or "obstacle" or "star" or "arch" => Loc.Has(kind + "." + rest),
            "module" => Loc.Has("module." + rest.Replace("_", "")),
            "belt" => true,
            "field" => Loc.Has("research.field." + rest),
            _ => Loc.Has("icon." + id),
        };
    }
}
