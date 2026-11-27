using System.Globalization;
using Starfoundry.Sim;

namespace Starfoundry.Ui;

public enum ValidationSeverity { Info, Warning, Error }

/// <summary>
/// One line of the validation list: how bad it is, a stable code, the icon, the text (a <see cref="Loc"/> key with its arguments, readable in both languages)
/// and the parts it is about, so a click can frame them in the 3D view. Errors block Apply; warnings and information do not.
/// </summary>
public sealed record ValidationEntry(ValidationSeverity Severity, string Code, string Icon, string TextKey, object[] Args, IReadOnlyList<(int Mx, int My, int Mz)> Cells)
{
    public bool Blocks => Severity == ValidationSeverity.Error;

    /// <summary>The cell a click frames first, or null when the entry is about the whole ship.</summary>
    public (int Mx, int My, int Mz)? Frame => Cells.Count > 0 ? Cells[0] : null;

    /// <summary>The text in a language (the entry is built once and shown in whichever the player has set).</summary>
    public string Text(Language lang)
    {
        var pair = Loc.All.FirstOrDefault(p => p.Key == TextKey).Value;
        string template = lang == Language.Es ? pair.Es : pair.En;
        if (string.IsNullOrEmpty(template)) return "⟦" + TextKey + "⟧";
        return string.Format(CultureInfo.GetCultureInfo(lang == Language.Es ? "es-ES" : "en-US"), template, Args);
    }

    public string Text() => Text(Loc.Lang);
}

/// <summary>The validation list of a draft: the entries and whether Apply is allowed.</summary>
public sealed class BlueprintValidation
{
    public required IReadOnlyList<ValidationEntry> Entries { get; init; }

    public bool HasErrors => Entries.Any(e => e.Blocks);

    public IEnumerable<ValidationEntry> Errors => Entries.Where(e => e.Severity == ValidationSeverity.Error);

    public IEnumerable<ValidationEntry> Warnings => Entries.Where(e => e.Severity == ValidationSeverity.Warning);

    /// <summary>The cells that every error is about, for the invalid (red hatch) ghosts.</summary>
    public IReadOnlyList<(int Mx, int My, int Mz)> InvalidCells => Errors.SelectMany(e => e.Cells).Distinct().ToList();

    /// <summary>The icon of an entry by its severity and code.</summary>
    public static string IconFor(ValidationSeverity severity, string code) => code switch
    {
        "noCockpit" => "module:cockpit",
        "noEngine" => "module:engine",
        "noReactor" => "module:reactor",
        "noCargo" => "module:cargo",
        "noCore" => "module:core",
        "noDock" => "module:dock",
        "orphans" or "offFloor" => "status:cross",
        "beltEnd" or "beltStart" => "belt:mk1",
        "power" => "status:power",
        "port" or "portBlocked" => "status:port",
        "view" => "tool:look",
        "noLaser" => "tool:mine",
        "turn" => "tool:rotate",
        "items" => "status:cargo",
        _ => severity switch { ValidationSeverity.Error => "status:cross", ValidationSeverity.Warning => "status:warning", _ => "tool:info" },
    };

    /// <summary>
    /// Checks a draft. The local checks cover what the player can see at once (a missing essential module, a part cut off from the rest, the docked port, power, the
    /// view from the cockpit, handling); <paramref name="report"/> is the simulation's verdict, whose refusals the local checks cannot know (overlap with the
    /// station, room in the hold, research, items) are added as errors, so the list never says "fine" about a draft the simulation refuses.
    /// </summary>
    /// <param name="port">The cell of the ship's docked port, when the ship is docked (its module must stay and its face must stay free).</param>
    /// <param name="portFace">The cell in front of the port's face.</param>
    public static BlueprintValidation Check(BlueprintDraft draft, ContentDb content, DesignReport? report, ShipStats after, ShipStats before, (int Mx, int My, int Mz)? port = null, (int Mx, int My, int Mz)? portFace = null, bool requireDock = true, StructureKind kind = StructureKind.Ship, InteriorDraft? interior = null)
    {
        var list = new List<ValidationEntry>();
        void Add(ValidationSeverity sev, string code, string key, IReadOnlyList<(int, int, int)>? cells = null, params object[] args) =>
            list.Add(new ValidationEntry(sev, code, IconFor(sev, code), key, args, (cells ?? Array.Empty<(int, int, int)>()).Select(c => (c.Item1, c.Item2, c.Item3)).ToList()));

        var mods = draft.Modules;
        bool station = kind == StructureKind.Station;
        if (mods.Count == 0) Add(ValidationSeverity.Error, "empty", "bp.val.empty");
        if (mods.Count > draft.Max) Add(ValidationSeverity.Error, "tooMany", "bp.val.tooMany", null, mods.Count, draft.Max);
        if (mods.Count > 0)
        {
            if (station)
            {
                // The core anchors a station (ADR 0043): it stays where and what it is.
                var core = draft.Original.Where(kv => kv.Value == ModuleKind.Core).Select(kv => ((int Mx, int My, int Mz)?)kv.Key).FirstOrDefault();
                if (core is { } c && (!mods.TryGetValue(c, out var ck) || ck != ModuleKind.Core)) Add(ValidationSeverity.Error, "noCore", "bp.val.noCore", new[] { (c.Mx, c.My, c.Mz) });
            }
            else
            {
                if (!mods.Values.Contains(ModuleKind.Cockpit)) Add(ValidationSeverity.Error, "noCockpit", "bp.val.noCockpit");
                if (!mods.Values.Contains(ModuleKind.Engine)) Add(ValidationSeverity.Error, "noEngine", "bp.val.noEngine");
                if (!mods.Values.Any(ModuleKindIds.IsReactor)) Add(ValidationSeverity.Error, "noReactor", "bp.val.noReactor");
                if (!mods.Values.Contains(ModuleKind.Cargo)) Add(ValidationSeverity.Error, "noCargo", "bp.val.noCargo");
            }
            if (draft.UsesPieces) { if (requireDock && !draft.Pieces.Any()) Add(ValidationSeverity.Error, "noDock", station ? "bp.val.noDockStation" : "bp.val.noDock"); } // ADR 0047: a structure docks through a docking collar
            else if (requireDock && !mods.Values.Contains(ModuleKind.Dock)) Add(ValidationSeverity.Error, "noDock", station ? "bp.val.noDockStation" : "bp.val.noDock"); // ADR 0041: a structure docks only through a dock module
            var stray = Disconnected(mods, port ?? (station ? draft.Original.Where(kv => kv.Value == ModuleKind.Core).Select(kv => ((int, int, int)?)kv.Key).FirstOrDefault() : null));
            if (stray.Count > 0) Add(ValidationSeverity.Error, "disconnected", station ? "bp.val.disconnectedStation" : "bp.val.disconnected", stray.Select(c => (c.Mx, c.My, c.Mz)).ToList(), stray.Count);
        }
        if (port is { } p)
        {
            if (!mods.TryGetValue(p, out var pk) || !draft.Original.TryGetValue(p, out var ok) || pk != ok)
                Add(ValidationSeverity.Error, "port", "bp.val.port", new[] { (p.Mx, p.My, p.Mz) });
            else if (draft.UsesPieces && portFace is { } pf0 && DirOf(p, pf0) is { } pdir && !draft.Pieces.Any(c => c.Mx == p.Mx && c.My == p.My && c.Mz == p.Mz && c.Face == pdir))
                Add(ValidationSeverity.Error, "port", "bp.val.port", new[] { (p.Mx, p.My, p.Mz) }); // the docked collar has to stay
            else if (portFace is { } f && mods.ContainsKey(f))
                Add(ValidationSeverity.Error, "portBlocked", "bp.val.portBlocked", new[] { (f.Mx, f.My, f.Mz), (p.Mx, p.My, p.Mz) });
        }

        // The simulation's own refusals that the checks above do not cover.
        if (report is { Ok: false } r)
        {
            bool covered = r.Reject switch
            {
                Reject.NoCockpitOrEngine => list.Any(e => e.Code is "noCockpit" or "noEngine"),
                Reject.LastEssentialModule => list.Any(e => e.Code is "noReactor" or "noCargo"),
                Reject.NoDockModule => list.Any(e => e.Code == "noDock"),
                Reject.NotAdjacent => list.Any(e => e.Code == "disconnected"),
                Reject.InvalidArgument => list.Any(e => e.Code is "empty" or "tooMany"),
                Reject.ModuleOccupied => list.Any(e => e.Code is "port" or "portBlocked" or "noCore"),
                Reject.OutsideStation => list.Any(e => e.Code == "orphans"),
                Reject.NotOnFloor => list.Any(e => e.Code == "offFloor"),
                _ => false,
            };
            if (!covered)
            {
                string key = RejectTexts.Key(r.Reject) ?? "toast.reject.invalidArgument";
                var cells = r.Reject == Reject.ModuleOccupied && port is { } pc ? new[] { (pc.Mx, pc.My, pc.Mz) } : null;
                Add(ValidationSeverity.Error, r.Reject == Reject.NotEnoughItems ? "items" : "sim", key, cells);
            }
        }

        if (interior is not null && station)
        {
            // Parts must stand in a module that is still there, on its floor; belts should lead somewhere (ADR 0044).
            var orphanModules = new List<(int, int, int)>();
            foreach (var m in interior.Machines) { var k = StationGrid.ModuleOfCell(m.X, m.Y, m.Z); if (!mods.ContainsKey(k) && !orphanModules.Contains(k)) orphanModules.Add(k); }
            foreach (var b in interior.Belts) foreach (var c in InteriorDraft.BeltCells(b)) { var k = StationGrid.ModuleOfCell(c.X, c.Y, c.Z); if (!mods.ContainsKey(k) && !orphanModules.Contains(k)) orphanModules.Add(k); }
            if (orphanModules.Count > 0) Add(ValidationSeverity.Error, "orphans", "bp.val.orphans", orphanModules, orphanModules.Count);
            var offFloor = interior.Machines.Where(m => !StationGrid.IsFloorLayer(m.Y)).Select(m => StationGrid.ModuleOfCell(m.X, m.Y, m.Z)).Concat(interior.Belts.Where(b => !StationGrid.IsFloorLayer(b.Y)).Select(b => StationGrid.ModuleOfCell(b.X, b.Y, b.Z))).Distinct().ToList();
            if (offFloor.Count > 0 && !interior.FreeBuilding) Add(ValidationSeverity.Error, "offFloor", "bp.val.offFloor", offFloor, offFloor.Count);
            var (dead, unfed) = interior.BeltGaps();
            if (dead.Count > 0) Add(ValidationSeverity.Warning, "beltEnd", "bp.val.beltEnd", dead.Select(c => StationGrid.ModuleOfCell(c.X, c.Y, c.Z)).Distinct().ToList(), dead.Count);
            if (unfed.Count > 0) Add(ValidationSeverity.Warning, "beltStart", "bp.val.beltStart", unfed.Select(c => StationGrid.ModuleOfCell(c.X, c.Y, c.Z)).Distinct().ToList(), unfed.Count);
        }
        if (after.PowerBalance < 0)
            Add(ValidationSeverity.Warning, "power", "bp.val.power", mods.Where(kv => ModuleKindIds.IsReactor(kv.Value)).Select(kv => (kv.Key.Mx, kv.Key.My, kv.Key.Mz)).ToList(), -after.PowerBalance);
        if (!station)
        {
        // Only what the draft adds: the starter ship's own gun deck over the cockpit is a known, glazed case that would warn forever.
        var already = DesignWarnings.ViewBlockers(draft.Original).Select(b => (b.Cockpit, b.Attachment, b.Why)).ToHashSet();
        var blockers = DesignWarnings.ViewBlockers(mods).Where(b => !already.Contains((b.Cockpit, b.Attachment, b.Why))).ToList();
        if (blockers.Count > 0)
            Add(ValidationSeverity.Warning, "view", "bp.val.view", blockers.Select(b => (b.Attachment.Item1, b.Attachment.Item2, b.Attachment.Item3)).Distinct().ToList(), blockers.Count);
        if (mods.Count > 0 && after.Lasers == 0 && before.Lasers > 0)
            Add(ValidationSeverity.Warning, "noLaser", "bp.val.noLaser");
        if (before.Turn.RateDegPerSecond > 0f && after.Turn.RateDegPerSecond < before.Turn.RateDegPerSecond * 0.75f && mods.Count > 0)
            Add(ValidationSeverity.Warning, "turn", "bp.val.turn", null, (int)Math.Round(100f * (1f - after.Turn.RateDegPerSecond / before.Turn.RateDegPerSecond)));
        }

        if (!draft.Dirty && !(interior?.Dirty ?? false)) Add(ValidationSeverity.Info, "unchanged", "bp.val.unchanged");
        else if (!list.Any(e => e.Blocks)) Add(ValidationSeverity.Info, "valid", "bp.val.valid");

        // Errors first, then warnings, then information; the order inside a group is the order the checks ran.
        var ordered = list.Select((e, i) => (e, i)).OrderByDescending(x => (int)x.e.Severity).ThenBy(x => x.i).Select(x => x.e).ToList();
        return new BlueprintValidation { Entries = ordered };
    }

    /// <summary>
    /// The modules that are not part of the main piece: the piece that holds the docked port when there is one, else the piece with the cockpit, else the biggest
    /// (the same six-face adjacency the simulation uses). Empty when the draft is one piece.
    /// </summary>
    public static List<(int Mx, int My, int Mz)> Disconnected(IReadOnlyDictionary<(int Mx, int My, int Mz), ModuleKind> mods, (int Mx, int My, int Mz)? port = null)
    {
        var pieces = new List<List<(int Mx, int My, int Mz)>>();
        var seen = new HashSet<(int, int, int)>();
        foreach (var start in mods.Keys)
        {
            if (!seen.Add((start.Mx, start.My, start.Mz))) continue;
            var piece = new List<(int Mx, int My, int Mz)> { start };
            var stack = new Stack<(int Mx, int My, int Mz)>();
            stack.Push(start);
            while (stack.Count > 0)
            {
                var c = stack.Pop();
                foreach (var (dx, dy, dz) in Steps)
                {
                    var n = (c.Mx + dx, c.My + dy, c.Mz + dz);
                    if (mods.ContainsKey(n) && seen.Add((n.Item1, n.Item2, n.Item3))) { piece.Add(n); stack.Push(n); }
                }
            }
            pieces.Add(piece);
        }
        if (pieces.Count <= 1) return new();
        var main = (port is { } pp ? pieces.FirstOrDefault(pc => pc.Contains(pp)) : null)
            ?? pieces.FirstOrDefault(pc => pc.Any(c => mods[c] == ModuleKind.Cockpit))
            ?? pieces.OrderByDescending(pc => pc.Count).First();
        return pieces.Where(pc => !ReferenceEquals(pc, main)).SelectMany(pc => pc).OrderBy(c => c).ToList();
    }

    private static Dir? DirOf((int Mx, int My, int Mz) from, (int Mx, int My, int Mz) to) => (to.Mx - from.Mx, to.Mz - from.Mz) switch
    {
        (1, 0) => Dir.PosX, (-1, 0) => Dir.NegX, (0, 1) => Dir.PosZ, (0, -1) => Dir.NegZ, _ => null,
    };

    private static readonly (int, int, int)[] Steps = { (1, 0, 0), (-1, 0, 0), (0, 1, 0), (0, -1, 0), (0, 0, 1), (0, 0, -1) };
}
