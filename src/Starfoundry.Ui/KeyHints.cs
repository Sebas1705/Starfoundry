namespace Starfoundry.Ui;

/// <summary>What the player is doing, as far as the key panel cares. The first context whose condition holds is shown (see <see cref="KeyHints.Contexts"/>).</summary>
public enum HintContext
{
    Pause,
    Help,
    BuildMenu,
    Research,
    StarMap,
    Tool,
    AimMachine,
    AimSegment,
    AimDeposit,
    AimModule,
    Walking,
    Flying,
}

/// <summary>Where the ship stands with respect to a station port: nothing to say, a free port close by (guidance shows) or docked.</summary>
public enum DockHint
{
    None,
    Near,
    Docked,
}

/// <summary>The build tool in hand, as the key panel distinguishes them.</summary>
public enum HintTool
{
    None,
    Machine,
    Belt,
    Module,
    Station,
}

/// <summary>What the crosshair is on (only meaningful while the pointer tool is in hand).</summary>
public enum HintAim
{
    None,
    Machine,
    Segment,
    Deposit,
    Module,
}

/// <summary>How a row is drawn: normal, highlighted (the tool or menu is active), dimmed (unavailable, with a reason), alert (a threat is on) or plain information without a key.</summary>
public enum KeyRowState
{
    Normal,
    Active,
    Disabled,
    Alert,
    Info,
}

/// <summary>
/// One row of the key panel. <see cref="Caps"/> are the key caps (a cap starting with "@" is the Loc key <c>key.cap.*</c>, anything else is
/// shown as it is), <see cref="LabelKey"/> the Loc key of the label, <see cref="ActionKey"/> the engine key that clicking the row presses
/// (a Godot <c>Key</c> name such as "B" or "Escape"; null when the row is not clickable) and <see cref="ReasonKey"/> the Loc key that says
/// why a disabled row is unavailable.
/// </summary>
public sealed record KeyRow(string Id, string[] Caps, string LabelKey, KeyRowState State = KeyRowState.Normal, string? ActionKey = null,
    string? ReasonKey = null, object[]? LabelArgs = null, object[]? ReasonArgs = null)
{
    public bool Clickable => ActionKey is not null && State != KeyRowState.Disabled && State != KeyRowState.Info;
}

/// <summary>A titled run of rows, for example the rows of the context ("Ship", "Building: smelter") or the threat group.</summary>
public sealed record KeyGroup(string TitleKey, object[] TitleArgs, IReadOnlyList<KeyRow> Rows);

/// <summary>Everything the game knows that decides the key panel. The game fills it each frame; every field has a neutral default.</summary>
public sealed record KeyState
{
    // panels
    public bool PauseOpen { get; init; }
    public bool HelpOpen { get; init; }
    public bool BuildOpen { get; init; }
    public bool ResearchOpen { get; init; }
    public bool MapOpen { get; init; }
    public bool InfoOpen { get; init; }

    // body
    public bool CanWalk { get; init; }
    public bool Standing { get; init; }
    public bool NearSeat { get; init; }
    public bool StructuresOn { get; init; }
    public bool ModuleToolAvailable { get; init; }

    // the tool in hand
    public HintTool Tool { get; init; }
    public string ToolName { get; init; } = "";
    public bool ToolHasRecipes { get; init; }
    public bool BeltAnchored { get; init; }

    // the aim
    public HintAim Aim { get; init; }
    public string AimName { get; init; } = "";
    public bool AimHasRecipes { get; init; }
    public bool AimDamaged { get; init; }
    public int AimHp { get; init; }
    public int AimMaxHp { get; init; }

    // the ship and the world
    public bool HasGalaxy { get; init; }
    public bool ThreatActive { get; init; }
    public bool ThreatNow { get; init; }
    public bool Armed { get; init; }
    /// <summary>The mining laser is the weapon in hand (M).</summary>
    public bool Mining { get; init; }
    /// <summary>The laser is firing: the simulation is mining the deposit under the crosshair.</summary>
    public bool LaserFiring { get; init; }
    public bool Refining { get; init; }
    /// <summary>The content has boost (Shift while flying, ADR 0028).</summary>
    public bool BoostAvailable { get; init; }
    public bool Boosting { get; init; }
    public bool ScanReady { get; init; } = true;
    public bool AnythingDamaged { get; init; }
    public bool DroneBayBuilt { get; init; }
    public bool DroneBayLocked { get; init; }
    public bool JumpUnlocked { get; init; }
    public bool JumpAffordable { get; init; } = true;
    public bool SystemChosen { get; init; }
    public string JumpCost { get; init; } = "";

    // docking (ADR 0028): rows and guidance appear only near a free port of one of the player's stations, or while docked
    public DockHint Dock { get; init; }
    /// <summary>Near a port: the ship is within docking range of it and slow enough, so the key docks at once.</summary>
    public bool DockReady { get; init; }
    /// <summary>The assisted approach is flying the ship to the port.</summary>
    public bool DockAssisting { get; init; }
    public int DockDistanceCells { get; init; }
    public int DockSpeedPercent { get; init; }
    /// <summary>Docked: items in the station stock and its capacity.</summary>
    public int StockUsed { get; init; }
    public int StockCapacity { get; init; }
}

/// <summary>
/// The contextual key panel of the HUD (bottom right): which keys to show for what the player is doing. Engine-free and data-driven:
/// <see cref="Contexts"/> lists the contexts in priority order, each with the condition under which it applies and the rows it shows, so
/// a new context is one more entry. <see cref="For"/> picks the first matching context and adds the threat group on top when a wave is coming.
/// </summary>
public static class KeyHints
{
    /// <summary>The keys that pick a build entry directly, as one row.</summary>
    public const string QuickCap = "1-0";

    private sealed record Context(HintContext Id, Func<KeyState, bool> When, string TitleKey, Func<KeyState, object[]> TitleArgs, Func<KeyState, IEnumerable<KeyRow>> Rows, bool ThreatGroup);

    private static readonly object[] NoArgs = Array.Empty<object>();

    /// <summary>The contexts in priority order: panels, then the tool in hand, then the aim, then the body.</summary>
    private static readonly Context[] Contexts =
    {
        new(HintContext.Pause, s => s.PauseOpen, "key.ctx.pause", _ => NoArgs, PauseRows, false),
        new(HintContext.Help, s => s.HelpOpen, "key.ctx.help", _ => NoArgs, HelpRows, false),
        new(HintContext.BuildMenu, s => s.BuildOpen, "key.ctx.build", _ => NoArgs, BuildMenuRows, false),
        new(HintContext.Research, s => s.ResearchOpen, "key.ctx.research", _ => NoArgs, ResearchRows, false),
        new(HintContext.StarMap, s => s.MapOpen && s.HasGalaxy, "key.ctx.map", _ => NoArgs, StarMapRows, false),
        new(HintContext.Tool, s => s.Tool != HintTool.None, "key.ctx.tool", s => new object[] { s.ToolName }, ToolRows, true),
        new(HintContext.AimMachine, s => s.Aim == HintAim.Machine, "key.ctx.aimMachine", s => new object[] { s.AimName }, AimMachineRows, true),
        new(HintContext.AimSegment, s => s.Aim == HintAim.Segment, "key.ctx.aimBelt", _ => NoArgs, AimSegmentRows, true),
        new(HintContext.AimDeposit, s => s.Aim == HintAim.Deposit, "key.ctx.aimDeposit", _ => NoArgs, AimDepositRows, true),
        new(HintContext.AimModule, s => s.Aim == HintAim.Module, "key.ctx.aimModule", s => new object[] { s.AimName }, AimModuleRows, true),
        new(HintContext.Walking, s => s.Standing, "key.ctx.walking", _ => NoArgs, WalkingRows, true),
        new(HintContext.Flying, _ => true, "key.ctx.flying", _ => NoArgs, FlyingRows, true),
    };

    /// <summary>The context that applies to a state.</summary>
    public static HintContext ContextOf(KeyState s) => Contexts.First(c => c.When(s)).Id;

    /// <summary>The ordered groups of rows for a state: the threat group first when a wave is coming, then the rows of the context.</summary>
    public static IReadOnlyList<KeyGroup> For(KeyState s)
    {
        var context = Contexts.First(c => c.When(s));
        var rows = context.Rows(s).ToList();
        var groups = new List<KeyGroup>();
        if (context.ThreatGroup && s.ThreatActive && s.ThreatNow)
        {
            rows.RemoveAll(r => r.Id is "weapons" or "repair");
            groups.Add(new KeyGroup("key.ctx.alert", NoArgs, ThreatRows(s).ToList()));
        }
        groups.Add(new KeyGroup(context.TitleKey, context.TitleArgs(s), rows));
        return groups;
    }

    // ---- row helpers ---------------------------------------------------------------------------------

    private static KeyRow Row(string id, string cap, string label, string? action = null, KeyRowState state = KeyRowState.Normal, string? reason = null, object[]? args = null) =>
        new(id, new[] { cap }, "key." + label, state, action, reason, args);

    private static KeyRow Row(string id, string[] caps, string label, string? action = null, KeyRowState state = KeyRowState.Normal, string? reason = null, object[]? args = null) =>
        new(id, caps, "key." + label, state, action, reason, args);

    private static KeyRow Off(string id, string cap, string label, string reasonKey, object[]? reasonArgs = null) =>
        new(id, new[] { cap }, "key." + label, KeyRowState.Disabled, null, reasonKey, null, reasonArgs);

    private static KeyRowState Lit(bool on) => on ? KeyRowState.Active : KeyRowState.Normal;

    private static KeyRow BuildRow(KeyState s, string label = "build") => Row("build", "B", label, "B", Lit(s.BuildOpen));

    private static KeyRow QuickRow() => Row("quick", QuickCap, "quick");

    // ---- the contexts --------------------------------------------------------------------------------

    /// <summary>(a) Seated and flying, nothing aimed at.</summary>
    private static IEnumerable<KeyRow> FlyingRows(KeyState s)
    {
        foreach (var row in DockRows(s)) yield return row;
        yield return BuildRow(s);
        yield return Row("research", "T", "research", "T", Lit(s.ResearchOpen));
        if (s.HasGalaxy) yield return Row("map", "G", "map", "G", Lit(s.MapOpen));
        yield return s.ScanReady ? Row("scan", "K", "scan", "K") : Off("scan", "K", "scan", "key.why.scanWait");
        yield return Row("mine", "M", s.Mining ? "mineStop" : "mine", "M", Lit(s.Mining));
        if (s.Mining) yield return Row("mineFire", "@hold", "mineFire", state: s.LaserFiring ? KeyRowState.Active : KeyRowState.Info);
        yield return Row("refine", "F", s.Refining ? "refineStop" : "refine", "F", Lit(s.Refining));
        yield return s.ThreatActive ? Row("weapons", "V", "weapons", "V", Lit(s.Armed)) : Off("weapons", "V", "weapons", "key.why.noThreats");
        if (s.StructuresOn) yield return Row("found", "I", "found", "I");
        if (s.ModuleToolAvailable) yield return Row("module", "N", "module", "N");
        if (s.BoostAvailable) yield return Row("boost", "Shift", "boost", state: s.Boosting ? KeyRowState.Active : KeyRowState.Info);
        if (s.CanWalk) yield return Row("seat", "E", "stand", "E");
        yield return Row("info", "H", "info", "H", Lit(s.InfoOpen));
        yield return Row("help", "F1", "help", "F1");
    }

    /// <summary>
    /// The docking rows, only by context: near a free port the dock key and the distance and speed guidance (the key starts the assisted
    /// approach unless the ship is already in range and slow enough); docked, the undock key, the two transfer keys and the stock.
    /// </summary>
    private static IEnumerable<KeyRow> DockRows(KeyState s)
    {
        if (s.Dock == DockHint.Near)
        {
            if (s.DockAssisting) yield return Row("dock", "U", "dockCancel", "U", KeyRowState.Active);
            else if (s.DockReady) yield return Row("dock", "U", "dock", "U");
            else yield return Row("dock", "U", "dockAssist", "U");
            yield return new KeyRow("dockGuide", Array.Empty<string>(), "key.dockGuide", s.DockSpeedPercent > 100 && s.DockDistanceCells <= Starfoundry.Sim.DockRules.RangeCells + 8 ? KeyRowState.Alert : KeyRowState.Info, null, null, new object[] { s.DockDistanceCells, s.DockSpeedPercent });
        }
        else if (s.Dock == DockHint.Docked)
        {
            yield return Row("undock", "U", "undock", "U");
            yield return Row("unload", ",", "unload", "Comma");
            yield return Row("load", ".", "load", "Period");
            yield return new KeyRow("stock", Array.Empty<string>(), "key.stock", KeyRowState.Info, null, null, new object[] { s.StockUsed, s.StockCapacity });
        }
    }

    /// <summary>(b) Standing and walking inside the ship.</summary>
    private static IEnumerable<KeyRow> WalkingRows(KeyState s)
    {
        yield return Row("walk", "WASD", "walk", state: KeyRowState.Info);
        yield return Row("run", "@shift", "run", state: KeyRowState.Info);
        yield return s.NearSeat ? Row("seat", "E", "sit", "E") : Off("seat", "E", "sit", "key.why.farSeat");
        foreach (var row in DockRows(s)) yield return row;
        yield return BuildRow(s);
        yield return Row("research", "T", "research", "T", Lit(s.ResearchOpen));
        if (s.StructuresOn) yield return Row("found", "I", "found", "I");
        if (s.ModuleToolAvailable) yield return Row("module", "N", "module", "N");
        yield return Row("info", "H", "info", "H", Lit(s.InfoOpen));
        yield return Row("help", "F1", "help", "F1");
    }

    /// <summary>(c) A build tool is in hand.</summary>
    private static IEnumerable<KeyRow> ToolRows(KeyState s)
    {
        switch (s.Tool)
        {
            case HintTool.Belt:
                yield return s.BeltAnchored ? Row("place", "@click", "beltEnd", state: KeyRowState.Active) : Row("place", "@click", "beltStart");
                yield return Row("rotate", "R", "rotate", "R");
                break;
            case HintTool.Module:
                yield return Row("place", "@click", "place");
                if (s.StructuresOn) yield return Row("kind", "N", "kind", "N");
                break;
            default:
                yield return Row("place", "@click", "place");
                break;
        }
        if (s.Tool is HintTool.Machine or HintTool.Belt) yield return Row("layer", new[] { "Z", "X" }, "layer", state: KeyRowState.Info);
        if (s.Tool == HintTool.Machine && s.ToolHasRecipes) yield return Row("recipe", "Y", "recipe", "Y");
        if (s.Tool == HintTool.Module) yield return Row("demolish", "@rclick", "removeModule");
        else if (s.Tool != HintTool.Station) yield return Row("demolish", "@rclick", s.BeltAnchored ? "cancelLine" : "demolish");
        yield return QuickRow();
        yield return BuildRow(s, "menu");
        yield return Row("cancel", "Esc", "cancel", "Escape");
    }

    /// <summary>(d) Aiming at a machine.</summary>
    private static IEnumerable<KeyRow> AimMachineRows(KeyState s)
    {
        if (s.AimHasRecipes) yield return Row("recipe", "Y", "recipe", "Y");
        yield return Row("inspect", "@click", "inspect");
        yield return Row("demolish", "@rclick", "demolish");
        yield return s.AimDamaged ? Row("repair", "@backspace", "repair", "Backspace") : Off("repair", "@backspace", "repair", "key.why.noDamage");
        yield return BuildRow(s);
    }

    private static IEnumerable<KeyRow> AimSegmentRows(KeyState s)
    {
        yield return Row("demolish", "@rclick", "demolish");
        yield return BuildRow(s);
    }

    /// <summary>(d) Aiming at a deposit.</summary>
    private static IEnumerable<KeyRow> AimDepositRows(KeyState s)
    {
        yield return Row("choose", "@click", "choose");
        yield return Row("mine", "M", s.Mining ? "mineStop" : "mine", "M", Lit(s.Mining));
        if (s.Mining) yield return Row("mineFire", "@hold", "mineFire", state: s.LaserFiring ? KeyRowState.Active : KeyRowState.Info);
        if (s.DroneBayBuilt) yield return Row("drones", "F7", "drones", "F7");
        else if (s.StructuresOn) yield return Off("drones", "F7", "drones", s.DroneBayLocked ? "key.why.droneLocked" : "key.why.noBay");
        yield return BuildRow(s);
    }

    /// <summary>(d) Aiming at a damaged module: its hit points.</summary>
    private static IEnumerable<KeyRow> AimModuleRows(KeyState s)
    {
        yield return new KeyRow("hp", Array.Empty<string>(), "key.hp", KeyRowState.Info, null, null, new object[] { s.AimHp, s.AimMaxHp });
        yield return BuildRow(s);
        if (s.ModuleToolAvailable) yield return Row("module", "N", "module", "N");
    }

    /// <summary>(e) The star map is open.</summary>
    private static IEnumerable<KeyRow> StarMapRows(KeyState s)
    {
        yield return Row("choose", "@arrows", "choose", state: KeyRowState.Info);
        string jump = s.JumpCost.Length > 0 ? "jumpCost" : "jump";
        object[]? args = s.JumpCost.Length > 0 ? new object[] { s.JumpCost } : null;
        if (!s.JumpUnlocked) yield return new KeyRow("jump", new[] { "J" }, "key.jump", KeyRowState.Disabled, null, "key.why.jumpLocked");
        else if (!s.SystemChosen) yield return new KeyRow("jump", new[] { "J" }, "key." + jump, KeyRowState.Disabled, null, "key.why.noSystem", args);
        else if (!s.JumpAffordable) yield return new KeyRow("jump", new[] { "J" }, "key." + jump, KeyRowState.Alert, "J", "key.why.jumpCost", args);
        else yield return new KeyRow("jump", new[] { "J" }, "key." + jump, KeyRowState.Normal, "J", null, args);
        yield return Row("close", "G", "close", "G", KeyRowState.Active);
    }

    /// <summary>(f) The research panel.</summary>
    private static IEnumerable<KeyRow> ResearchRows(KeyState s)
    {
        yield return Row("choose", "@arrows", "choose", state: KeyRowState.Info);
        yield return Row("start", "@enter", "researchStart", state: KeyRowState.Info);
        yield return Row("close", "T", "close", "T", KeyRowState.Active);
        yield return Row("esc", "Esc", "close", "Escape");
    }

    /// <summary>(f) The build menu.</summary>
    private static IEnumerable<KeyRow> BuildMenuRows(KeyState s)
    {
        yield return Row("choose", "@arrows", "choose", state: KeyRowState.Info);
        yield return Row("pick", "@enter", "pickTool", state: KeyRowState.Info);
        yield return Row("tab", new[] { "PgUp", "PgDn" }, "tab", state: KeyRowState.Info);
        yield return Row("close", "B", "close", "B", KeyRowState.Active);
        yield return Row("esc", "Esc", "close", "Escape");
    }

    private static IEnumerable<KeyRow> HelpRows(KeyState s)
    {
        yield return Row("lang", "L", "lang", "L");
        yield return Row("close", "F1", "close", "F1", KeyRowState.Active);
        yield return Row("esc", "Esc", "close", "Escape");
    }

    private static IEnumerable<KeyRow> PauseRows(KeyState s)
    {
        yield return Row("choose", "@arrows", "choose", state: KeyRowState.Info);
        yield return Row("pick", "@enter", "accept", state: KeyRowState.Info);
        yield return Row("resume", "Esc", "resume", "Escape");
    }

    /// <summary>(g) A wave is on its way or enemies are alive: the weapons and the repair on top, highlighted.</summary>
    private static IEnumerable<KeyRow> ThreatRows(KeyState s)
    {
        yield return Row("weapons", "V", "fire", "V", s.Armed ? KeyRowState.Active : KeyRowState.Alert);
        yield return s.AnythingDamaged ? Row("repair", "@backspace", "repair", "Backspace", KeyRowState.Alert) : Off("repair", "@backspace", "repair", "key.why.noDamage");
    }
}
