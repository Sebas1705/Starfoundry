namespace Starfoundry.Ui;

/// <summary>What the player is doing, as far as the key panel cares. The first context whose condition holds is shown (see <see cref="KeyHints.Contexts"/>).</summary>
public enum HintContext
{
    Pause,
    Help,
    BuildMenu,
    Research,
    Inventory,
    StarMap,
    Computer,
    Console,
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

/// <summary>The weapon group in hand (ADR 0032): the fire button mines with the lasers or shoots with the guns.</summary>
public enum WeaponGroup
{
    Laser,
    Guns,
}

/// <summary>What the interact key (F) does right now; the game works it out from the aim, the dock and the seat.</summary>
public enum HintInteract
{
    None,
    Sit,
    SitFar,
    Stand,
    Climb,
    Dock,
    DockAssist,
    DockCancel,
    Undock,
    Machine,
    Reactor,
    Shipyard,
    Console,
    Bench,
    Drones,
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
    /// <summary>The personal inventory panel is open (ADR 0035).</summary>
    public bool InventoryOpen { get; init; }
    public bool MapOpen { get; init; }
    public bool InfoOpen { get; init; }
    /// <summary>The ship computer (Tab) is open on a tab without a context of its own (info, designer, settings, systems).</summary>
    public bool ComputerOpen { get; init; }
    /// <summary>What F does at this moment.</summary>
    public HintInteract Interact { get; init; }

    /// <summary>The pilot looks at one cockpit display (F at the console): its keys replace the flying ones.</summary>
    public bool ConsoleFocus { get; init; }
    /// <summary>The ship has cockpit displays to look at (a seat at a console).</summary>
    public bool HasConsole { get; init; }
    /// <summary>Flight assist (Z): the ship damps its rotation when the pilot stops turning. Off means free rotation.</summary>
    public bool FlightAssist { get; init; } = true;
    /// <summary>The ship has rotation dynamics (the inertia setting is not off), so the flight assist matters.</summary>
    public bool HasTurnInertia { get; init; }
    /// <summary>How much the HUD shows (H cycles it).</summary>
    public HudDetail HudDetail { get; init; } = HudDetail.Standard;

    // body
    public bool CanWalk { get; init; }
    public bool Standing { get; init; }
    public bool NearSeat { get; init; }
    public bool StructuresOn { get; init; }

    /// <summary>True under the floor-building rule (ADR 0031): machines and belts go on the floor, so there is no layer to change.</summary>
    public bool FloorBuilding { get; init; }
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
    /// <summary>The mining laser is the weapon in hand (V switches).</summary>
    public bool Mining { get; init; }
    /// <summary>The laser is firing: the simulation is mining the deposit under the crosshair.</summary>
    public bool LaserFiring { get; init; }
    public bool Refining { get; init; }
    /// <summary>The ship fits weapons as hardpoint modules (ADR 0032): the weapon rows follow the group in hand and the modules the ship has.</summary>
    public bool Hardpoints { get; init; }
    public bool HasLaserModule { get; init; } = true;
    public bool HasGunModule { get; init; } = true;
    public WeaponGroup WeaponGroup { get; init; }
    /// <summary>Docked at one of the player's stations that has a shipyard: the Designer tab of the ship computer is available.</summary>
    public bool ShipyardDocked { get; init; }
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

    // docking (ADR 0029): rows and guidance appear only near a free port of one of the player's stations, or while docked
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

    /// <summary>The most rows the panel shows at once (the threat group included): flying and walking, and with a build tool in hand.</summary>
    public const int MaxRowsFlying = 8, MaxRowsBuild = 10;

    /// <summary>Which rows give way first when a context has more rows than fit (the first listed goes first); never an alert or a lit row.</summary>
    private static readonly string[] DropOrder = { "fireGuns", "mineFire", "hudDetail", "assist", "scan", "roll", "run", "dockGuide" };

    private sealed record Context(HintContext Id, Func<KeyState, bool> When, string TitleKey, Func<KeyState, object[]> TitleArgs, Func<KeyState, IEnumerable<KeyRow>> Rows, bool ThreatGroup);

    private static readonly object[] NoArgs = Array.Empty<object>();

    /// <summary>The contexts in priority order: panels, then the tool in hand, then the aim, then the body.</summary>
    private static readonly Context[] Contexts =
    {
        new(HintContext.Pause, s => s.PauseOpen, "key.ctx.pause", _ => NoArgs, PauseRows, false),
        new(HintContext.Help, s => s.HelpOpen, "key.ctx.help", _ => NoArgs, HelpRows, false),
        new(HintContext.BuildMenu, s => s.BuildOpen, "key.ctx.build", _ => NoArgs, BuildMenuRows, false),
        new(HintContext.Research, s => s.ResearchOpen, "key.ctx.research", _ => NoArgs, ResearchRows, false),
        new(HintContext.Inventory, s => s.InventoryOpen, "key.ctx.inventory", _ => NoArgs, InventoryRows, false),
        new(HintContext.StarMap, s => s.MapOpen && s.HasGalaxy, "key.ctx.map", _ => NoArgs, StarMapRows, false),
        new(HintContext.Computer, s => s.ComputerOpen, "key.ctx.computer", _ => NoArgs, ComputerRows, false),
        new(HintContext.Console, s => s.ConsoleFocus, "key.ctx.console", _ => NoArgs, ConsoleRows, false),
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
            rows.RemoveAll(r => r.Id is "weapons");
            groups.Add(new KeyGroup("key.ctx.alert", NoArgs, ThreatRows(s).ToList()));
        }
        int max = context.Id == HintContext.Tool ? MaxRowsBuild : MaxRowsFlying;
        if (context.Id is HintContext.Tool or HintContext.Walking or HintContext.Flying or HintContext.AimMachine or HintContext.AimSegment or HintContext.AimDeposit or HintContext.AimModule)
            foreach (var id in DropOrder)
            {
                if (rows.Count + groups.Sum(g => g.Rows.Count) <= max) break;
                int at = rows.FindIndex(r => r.Id == id && r.State is KeyRowState.Normal or KeyRowState.Info or KeyRowState.Disabled);
                if (at >= 0) rows.RemoveAt(at);
            }
        groups.Add(new KeyGroup(context.TitleKey, context.TitleArgs(s), rows));
        return groups;
    }

    // ---- row helpers ---------------------------------------------------------------------------------

    /// <summary>A row for an action of <see cref="InputActions"/>: the caps and the key a click presses come from the table.</summary>
    private static KeyRow Act(string id, string action, string label, KeyRowState state = KeyRowState.Normal, object[]? args = null) =>
        new(id, new[] { InputActions.PrimaryCap(action) }, "key." + label, state, state == KeyRowState.Info ? null : InputActions.Primary(action), null, args);

    /// <summary>A row that only tells (all the caps of an action, no click).</summary>
    private static KeyRow Tell(string id, string action, string label, KeyRowState state = KeyRowState.Info, object[]? args = null) =>
        new(id, InputActions.Caps(action), "key." + label, state, null, null, args);

    /// <summary>A row whose action is unavailable, with the reason shown on hover.</summary>
    private static KeyRow Off(string id, string action, string label, string reasonKey, object[]? reasonArgs = null) =>
        new(id, new[] { InputActions.PrimaryCap(action) }, "key." + label, KeyRowState.Disabled, null, reasonKey, null, reasonArgs);

    /// <summary>A row with a mouse cap ("@click", "@rclick", "@hold") that no key triggers.</summary>
    private static KeyRow Mouse(string id, string cap, string label, KeyRowState state = KeyRowState.Normal) =>
        new(id, new[] { cap }, "key." + label, state);

    /// <summary>A row with a literal cap (arrows, Intro, page keys) that the panels read themselves.</summary>
    private static KeyRow Fixed(string id, string cap, string label, KeyRowState state = KeyRowState.Info) =>
        new(id, new[] { cap }, "key." + label, state);

    private static KeyRowState Lit(bool on) => on ? KeyRowState.Active : KeyRowState.Normal;

    private static KeyRow BuildRow(KeyState s, string label = "build") => Act("build", "openBuild", label, Lit(s.BuildOpen));

    private static KeyRow QuickRow() => new("quick", InputActions.Caps("hotbar"), "key.quick");

    /// <summary>The computer key, with the direct keys in its label.</summary>
    private static KeyRow ComputerRow(KeyState s) => Act("computer", "computer", "computer", Lit(s.ComputerOpen));

    /// <summary>The rows every ship computer tab shares: the tab keys and the ways out.</summary>
    private static IEnumerable<KeyRow> ComputerTail()
    {
        yield return Tell("tabs", "tabs", "tabs");
        yield return Act("close", "computer", "close", KeyRowState.Active);
        yield return Act("esc", "cancel", "close");
    }

    // ---- the contexts --------------------------------------------------------------------------------

    /// <summary>The weapon in hand and the click that fires it (ADR 0036): the row to switch it, then what the click does with it.</summary>
    private static IEnumerable<KeyRow> WeaponRows(KeyState s)
    {
        bool hasLaser = !s.Hardpoints || s.HasLaserModule, hasGun = !s.Hardpoints || s.HasGunModule; // without hardpoints (older worlds) the ship has both, implicit
        if (!hasLaser && !hasGun) { yield return Off("weapons", "weaponNext", "weaponLaser", "key.why.noGun"); yield break; }
        bool guns = s.WeaponGroup == WeaponGroup.Guns && hasGun;
        yield return Act("weapons", "weaponNext", guns ? "weaponGuns" : "weaponLaser", Lit(guns));
        if (guns) yield return Mouse("fireGuns", "@hold", "fireGuns", s.Armed ? KeyRowState.Active : KeyRowState.Info);
        else if (hasLaser) yield return Mouse("mineFire", "@hold", "mineFire", s.LaserFiring ? KeyRowState.Active : KeyRowState.Info);
    }

    /// <summary>What F does right now, as one row: sit, stand, dock, undock, open what the crosshair is on.</summary>
    private static KeyRow InteractRow(KeyState s) => s.Interact switch
    {
        HintInteract.Sit => Act("interact", "interact", "sit"),
        HintInteract.SitFar => Off("interact", "interact", "sit", "key.why.farSeat"),
        HintInteract.Stand => Act("interact", "interact", "stand"),
        HintInteract.Climb => Act("interact", "interact", "climb"),
        HintInteract.Dock => Act("interact", "interact", "dock"),
        HintInteract.DockAssist => Act("interact", "interact", "dockAssist"),
        HintInteract.DockCancel => Act("interact", "interact", "dockCancel", KeyRowState.Active),
        HintInteract.Undock => Act("interact", "interact", "undock"),
        HintInteract.Machine => Act("interact", "interact", "interactMachine"),
        HintInteract.Reactor => Act("interact", "interact", "interactReactor"),
        HintInteract.Shipyard => Act("interact", "interact", "interactShipyard"),
        HintInteract.Console => Act("interact", "interact", "console"),
        HintInteract.Bench => Act("interact", "interact", "refine", Lit(s.Refining)),
        HintInteract.Drones => Act("interact", "interact", "drones"),
        _ => Act("interact", "interact", "interact"),
    };

    private static IEnumerable<KeyRow> FlyingRows(KeyState s)
    {
        yield return InteractRow(s);
        foreach (var row in DockRows(s)) yield return row;
        yield return ComputerRow(s);
        foreach (var row in WeaponRows(s)) yield return row;
        if (s.Dock != DockHint.Docked)
        {
            yield return s.ScanReady ? Act("scan", "scan", "scan") : Off("scan", "scan", "scan", "key.why.scanWait");
            yield return Tell("roll", "roll", "roll"); // free flight: roll the ship about its own axis
            if (s.HasTurnInertia) yield return Act("assist", "assist", s.FlightAssist ? "assistOn" : "assistOff", Lit(!s.FlightAssist)); // flight assist: damp the rotation, or let it run free
        }
        yield return HudRow(s);
        yield return Act("help", "help", "help");
    }

    /// <summary>H: the HUD detail level in force, as one row (the label names the level; pressing it moves to the next).</summary>
    private static KeyRow HudRow(KeyState s) =>
        Act("hudDetail", "hudDetail", s.HudDetail switch { HudDetail.Minimal => "hudMinimal", HudDetail.Detailed => "hudDetailed", _ => "hudStandard" }, Lit(s.HudDetail != HudDetail.Standard));

    /// <summary>The pilot looks at a cockpit display: click presses a button, the arrows change screen and page.</summary>
    private static IEnumerable<KeyRow> ConsoleRows(KeyState s)
    {
        yield return Mouse("press", "@click", "consolePress", KeyRowState.Info);
        yield return Fixed("nav", "@arrows", "consoleNav");
        yield return HudRow(s);
        yield return Act("leave", "interact", "consoleLeave", KeyRowState.Active);
        yield return Act("esc", "cancel", "consoleLeave");
    }

    /// <summary>The docking rows, only by context: near a free port the distance and speed guidance (F starts the approach); docked the two transfer keys and the stock.</summary>
    private static IEnumerable<KeyRow> DockRows(KeyState s)
    {
        if (s.Dock == DockHint.Near)
            yield return new KeyRow("dockGuide", Array.Empty<string>(), "key.dockGuide", s.DockSpeedPercent > 100 && s.DockDistanceCells <= Starfoundry.Sim.DockRules.RangeCells + 8 ? KeyRowState.Alert : KeyRowState.Info, null, null, new object[] { s.DockDistanceCells, s.DockSpeedPercent });
        else if (s.Dock == DockHint.Docked)
        {
            yield return Act("unload", "unload", "unload");
            yield return Act("load", "load", "load");
            yield return new KeyRow("stock", Array.Empty<string>(), "key.stock", KeyRowState.Info, null, null, new object[] { s.StockUsed, s.StockCapacity });
        }
    }

    /// <summary>(b) Standing and walking inside the ship.</summary>
    private static IEnumerable<KeyRow> WalkingRows(KeyState s)
    {
        yield return Tell("walk", "move", "walk");
        yield return Tell("run", "run", "run");
        yield return InteractRow(s);
        foreach (var row in DockRows(s)) yield return row;
        yield return ComputerRow(s);
        yield return HudRow(s);
        yield return Act("help", "help", "help");
    }

    /// <summary>(c) A build tool is in hand.</summary>
    private static IEnumerable<KeyRow> ToolRows(KeyState s)
    {
        switch (s.Tool)
        {
            case HintTool.Belt:
                yield return s.BeltAnchored ? Mouse("place", "@click", "beltEnd", KeyRowState.Active) : Mouse("place", "@click", "beltStart");
                yield return Act("rotate", "rotate", "rotate");
                break;
            case HintTool.Module:
                yield return Mouse("place", "@click", "place");
                if (s.StructuresOn) yield return Act("kind", "kind", "kind");
                break;
            default:
                yield return Mouse("place", "@click", "place");
                break;
        }
        if (s.Tool is HintTool.Machine or HintTool.Belt && !s.FloorBuilding || s.Tool == HintTool.Module && s.StructuresOn) yield return Tell("layer", "layer", "layer");
        if (s.Tool == HintTool.Machine && s.ToolHasRecipes) yield return Act("recipe", "recipe", "recipe");
        if (s.Tool == HintTool.Module) yield return Mouse("demolish", "@rclick", "removeModule");
        else if (s.Tool != HintTool.Station) yield return Mouse("demolish", "@rclick", s.BeltAnchored ? "cancelLine" : "demolish");
        yield return QuickRow();
        yield return BuildRow(s, "menu");
        yield return Act("cancel", "cancel", "cancel");
    }

    /// <summary>(d) Aiming at a machine.</summary>
    private static IEnumerable<KeyRow> AimMachineRows(KeyState s)
    {
        yield return InteractRow(s);
        yield return Mouse("inspect", "@click", "inspect");
        yield return Mouse("demolish", "@rclick", "demolish");
        yield return ComputerRow(s);
    }

    private static IEnumerable<KeyRow> AimSegmentRows(KeyState s)
    {
        yield return Mouse("demolish", "@rclick", "demolish");
        yield return ComputerRow(s);
    }

    /// <summary>(d) Aiming at a deposit.</summary>
    private static IEnumerable<KeyRow> AimDepositRows(KeyState s)
    {
        yield return Mouse("choose", "@click", "choose", KeyRowState.Normal);
        if (s.Hardpoints && !s.HasLaserModule) yield return Off("weapons", "weaponNext", "takeLaser", "key.why.noLaser");
        else if (s.WeaponGroup == WeaponGroup.Laser) yield return Mouse("mineFire", "@hold", "mineFire", s.LaserFiring ? KeyRowState.Active : KeyRowState.Info);
        else yield return Act("weapons", "weaponNext", "takeLaser");
        if (s.DroneBayBuilt) yield return Act("drones", "interact", "drones");
        else if (s.StructuresOn) yield return Off("drones", "interact", "drones", s.DroneBayLocked ? "key.why.droneLocked" : "key.why.noBay");
        yield return ComputerRow(s);
    }

    /// <summary>(d) Aiming at a damaged module: its hit points.</summary>
    private static IEnumerable<KeyRow> AimModuleRows(KeyState s)
    {
        yield return new KeyRow("hp", Array.Empty<string>(), "key.hp", KeyRowState.Info, null, null, new object[] { s.AimHp, s.AimMaxHp });
        if (s.Interact is HintInteract.Reactor or HintInteract.Shipyard) yield return InteractRow(s);
        yield return ComputerRow(s);
    }

    /// <summary>(e) The star map tab.</summary>
    private static IEnumerable<KeyRow> StarMapRows(KeyState s)
    {
        yield return Tell("choose", "mapChoose", "choose");
        yield return new KeyRow("rotate", new[] { "@drag" }, "key.mapRotate", KeyRowState.Info); // the 3D map: drag or Left and Right to rotate, the wheel zooms, Home resets
        string jump = s.JumpCost.Length > 0 ? "jumpCost" : "jump";
        object[]? args = s.JumpCost.Length > 0 ? new object[] { s.JumpCost } : null;
        string cap = InputActions.PrimaryCap("mapJump"), key = InputActions.Primary("mapJump");
        if (!s.JumpUnlocked) yield return new KeyRow("jump", new[] { cap }, "key.jump", KeyRowState.Disabled, null, "key.why.jumpLocked");
        else if (!s.SystemChosen) yield return new KeyRow("jump", new[] { cap }, "key." + jump, KeyRowState.Disabled, null, "key.why.noSystem", args);
        else if (!s.JumpAffordable) yield return new KeyRow("jump", new[] { cap }, "key." + jump, KeyRowState.Alert, key, "key.why.jumpCost", args);
        else yield return new KeyRow("jump", new[] { cap }, "key." + jump, KeyRowState.Normal, key, null, args);
        foreach (var row in ComputerTail()) yield return row;
    }

    /// <summary>(f) The research tab.</summary>
    private static IEnumerable<KeyRow> ResearchRows(KeyState s)
    {
        yield return Fixed("choose", "@arrows", "choose");
        yield return Fixed("start", "@enter", "researchStart");
        foreach (var row in ComputerTail()) yield return row;
    }

    /// <summary>The inventory tab (ADR 0035): how to move things, and the key that closes the computer.</summary>
    private static IEnumerable<KeyRow> InventoryRows(KeyState s)
    {
        yield return Mouse("invDrag", "@click", "invDrag", KeyRowState.Info);
        yield return Fixed("invQuick", "@shift", "invQuick");
        yield return Fixed("invKeys", "@enter", "invKeys");
        foreach (var row in ComputerTail()) yield return row;
    }

    /// <summary>(f) The build tab.</summary>
    private static IEnumerable<KeyRow> BuildMenuRows(KeyState s)
    {
        yield return Fixed("choose", "@arrows", "choose");
        yield return Fixed("pick", "@enter", "pickTool");
        yield return new KeyRow("tab", new[] { "PgUp", "PgDn" }, "key.tab", KeyRowState.Info);
        foreach (var row in ComputerTail()) yield return row;
    }

    /// <summary>The other tabs of the ship computer (info, designer, settings, systems).</summary>
    private static IEnumerable<KeyRow> ComputerRows(KeyState s) => ComputerTail();

    private static IEnumerable<KeyRow> HelpRows(KeyState s)
    {
        yield return Act("close", "help", "close", KeyRowState.Active);
        yield return Act("esc", "cancel", "close");
    }

    private static IEnumerable<KeyRow> PauseRows(KeyState s)
    {
        yield return Fixed("choose", "@arrows", "choose");
        yield return Fixed("pick", "@enter", "accept");
        yield return Act("resume", "cancel", "resume");
    }

    /// <summary>(g) A wave is on its way or enemies are alive: the weapons on top, highlighted.</summary>
    private static IEnumerable<KeyRow> ThreatRows(KeyState s)
    {
        bool inHand = s.WeaponGroup == WeaponGroup.Guns;
        yield return !s.Hardpoints || s.HasGunModule ? Act("weapons", "weaponNext", inHand ? "weaponGuns" : "weaponLaser", inHand ? KeyRowState.Active : KeyRowState.Alert) : Off("weapons", "weaponNext", "weaponLaser", "key.why.noGun");
    }
}
