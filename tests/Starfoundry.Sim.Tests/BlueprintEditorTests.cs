using System.Numerics;
using Starfoundry.Sim;
using Starfoundry.Ui;
using Xunit;

namespace Starfoundry.Sim.Tests;

/// <summary>The engine-free model of the blueprint editor (docs/superpowers/specs/2026-11-26-blueprint-editor-design.md, section 3): the draft, its history, the ghost diff, the cost, the stats, the validation list, the camera and the session.</summary>
public class BlueprintEditorTests
{
    private const int Cell = Units.CellUnits;
    private const ulong Seed = 20261126UL;
    private static readonly ContentDb C = TestContent.Core;
    private static readonly ShipHandling Handling = ShipHandling.Default;

    /// <summary>The starter ship docked at a station with a shipyard (the same set-up as the designer tests).</summary>
    private static SimWorld Yard(GameMode mode, out int ship, out int station)
    {
        var w = SimWorld.CreateGalaxy(C, mode, Seed, threats: false, stationRules: false, structures: true);
        ship = w.Network.ShipStructure(0);
        station = w.Network.CreateStructure(0, StructureKind.Station, -18L * Cell, 0, 0);
        w.Network.AddStructureModuleUnchecked(station, 0, 0, 0, ModuleKind.Core);
        w.Network.AddStructureModuleUnchecked(station, 0, 0, 1, ModuleKind.Shipyard);
        w.Submit(0, new DockAt(station, 0, 0, 0, Dir.PosX));
        w.Step();
        Assert.Equal(Reject.None, w.LastResults.Single().Reject);
        return w;
    }

    private static BlueprintSession Open(SimWorld w)
    {
        var s = new BlueprintSession(C, Handling, () => w, 0);
        s.Open();
        return s;
    }

    private static (int, int, int) First(BlueprintDraft d, ModuleKind kind) => d.Modules.First(kv => kv.Value == kind).Key;

    private static string Snapshot(BlueprintDraft d) => string.Join(";", d.Modules.Select(kv => $"{kv.Key}{(int)kv.Value}r{d.RotationOf(kv.Key)}"));

    /// <summary>A random edit of the draft: add next to a module, remove one, move one, upgrade, rotate or mirror a few.</summary>
    private static void RandomEdit(BlueprintDraft d, Random rng)
    {
        var cat = ShipDesignEditor.Catalogue(C);
        var cells = d.Modules.Keys.ToList();
        var pick = cells[rng.Next(cells.Count)];
        var steps = new[] { (1, 0, 0), (-1, 0, 0), (0, 1, 0), (0, -1, 0), (0, 0, 1), (0, 0, -1) };
        var (dx, dy, dz) = steps[rng.Next(steps.Length)];
        var near = (pick.Mx + dx, pick.My + dy, pick.Mz + dz);
        switch (rng.Next(7))
        {
            case 0: case 1: d.Add(near, cat[rng.Next(cat.Count)], C, rng.Next(4)); break;
            case 2: d.Remove(pick); break;
            case 3: d.Move(pick, near); break;
            case 4: d.Upgrade(pick, C); break;
            case 5: d.Rotate(new[] { pick }, 1 + rng.Next(3)); break;
            default: d.Mirror(new[] { pick }, GridAxis.X); break;
        }
    }

    // ---- the draft and its history ------------------------------------------------------------------------------

    [Fact]
    public void Undo_walks_back_to_the_start_and_redo_forward_to_the_end()
    {
        var w = Yard(GameMode.Sandbox, out _, out _);
        for (int seed = 1; seed <= 12; seed++)
        {
            var s = Open(w);
            var d = s.Draft;
            string start = Snapshot(d);
            var rng = new Random(seed);
            var states = new List<string> { start };
            for (int i = 0; i < 25; i++)
            {
                string before = Snapshot(d);
                int depth = d.UndoDepth;
                RandomEdit(d, rng);
                if (d.UndoDepth != depth) states.Add(Snapshot(d)); // an edit that changed something is one step
                else Assert.Equal(before, Snapshot(d)); // a refused edit leaves everything as it was
            }
            string end = Snapshot(d);
            Assert.Equal(states.Count - 1, d.UndoDepth);
            for (int i = states.Count - 2; i >= 0; i--)
            {
                Assert.True(d.Undo());
                Assert.Equal(states[i], Snapshot(d));
            }
            Assert.False(d.CanUndo);
            Assert.Equal(start, Snapshot(d));
            Assert.False(d.Dirty);
            for (int i = 1; i < states.Count; i++)
            {
                Assert.True(d.Redo());
                Assert.Equal(states[i], Snapshot(d));
            }
            Assert.False(d.CanRedo);
            Assert.Equal(end, Snapshot(d));
        }
    }

    [Fact]
    public void A_new_edit_clears_redo_and_revert_is_one_undoable_step()
    {
        var w = Yard(GameMode.Sandbox, out _, out _);
        var d = Open(w).Draft;
        var nose = First(d, ModuleKind.Cockpit);
        Assert.Equal(DesignEditResult.Ok, d.Add((nose.Item1 + 1, nose.Item2 + 1, nose.Item3), ModuleKind.Hull, C));
        Assert.True(d.Undo());
        Assert.True(d.CanRedo);
        Assert.Equal(DesignEditResult.Ok, d.Add((nose.Item1 - 1, nose.Item2 + 1, nose.Item3), ModuleKind.Hull, C));
        Assert.False(d.CanRedo);
        string edited = Snapshot(d);
        d.Revert();
        Assert.False(d.Dirty);
        Assert.True(d.Undo());
        Assert.Equal(edited, Snapshot(d));
    }

    [Fact]
    public void Edits_refuse_what_the_grid_and_the_catalogue_refuse_and_change_nothing()
    {
        var w = Yard(GameMode.Sandbox, out _, out _);
        var d = Open(w).Draft;
        var cockpit = First(d, ModuleKind.Cockpit);
        int depth = d.UndoDepth;
        Assert.Equal(DesignEditResult.Occupied, d.Add(cockpit, ModuleKind.Hull, C));
        Assert.Equal(DesignEditResult.KindNotAllowed, d.Add((50, 50, 50), ModuleKind.Core, C));
        Assert.Equal(DesignEditResult.OutOfBounds, d.Add((100000, 0, 0), ModuleKind.Hull, C));
        Assert.Equal(DesignEditResult.Empty, d.Remove((40, 0, 0)));
        Assert.Equal(DesignEditResult.SameCell, d.Move(cockpit, cockpit));
        Assert.Equal(depth, d.UndoDepth);
        Assert.False(d.Dirty);
    }

    [Fact]
    public void A_group_moves_by_an_offset_only_when_every_target_is_free()
    {
        var w = Yard(GameMode.Sandbox, out _, out _);
        var d = Open(w).Draft;
        var all = d.Modules.Keys.ToList();
        Assert.True(d.MoveMany(all, (0, 0, 0)).Done == 0);
        Assert.Equal(all.Count, d.MoveMany(all, (2, 0, 0)).Done); // the whole ship slides: targets vacated by the move count as free
        Assert.Equal(all.Count, d.Count);
        string moved = Snapshot(d);
        var one = d.Modules.Keys.First();
        var other = d.Modules.Keys.Last();
        var delta = (other.Mx - one.Mx, other.My - one.My, other.Mz - one.Mz);
        Assert.Equal(0, d.MoveMany(new[] { one }, delta).Done); // lands on a taken cell
        Assert.Equal(moved, Snapshot(d));
    }

    [Fact]
    public void Mirror_adds_the_reflection_and_skips_taken_cells_and_paste_places_the_clipboard()
    {
        var w = Yard(GameMode.Sandbox, out _, out _);
        var d = Open(w).Draft;
        var cockpit = First(d, ModuleKind.Cockpit);
        var wing = (cockpit.Item1 + 2, cockpit.Item2, cockpit.Item3);
        Assert.Equal(DesignEditResult.Ok, d.Add(wing, ModuleKind.Hull, C));
        var r = d.Mirror(new[] { wing }, GridAxis.X, 2 * cockpit.Item1);
        Assert.Equal(new BulkResult(1, 0), r);
        Assert.Equal(ModuleKind.Hull, d.Modules[(cockpit.Item1 - 2, cockpit.Item2, cockpit.Item3)]);
        Assert.Equal(new BulkResult(0, 1), d.Mirror(new[] { wing }, GridAxis.X, 2 * cockpit.Item1)); // the mirror cell is taken now
        Assert.Equal(2, d.Copy(new[] { wing, (cockpit.Item1 - 2, cockpit.Item2, cockpit.Item3) }));
        int before = d.Count;
        var pasted = d.Paste((cockpit.Item1 - 2, cockpit.Item2 + 3, cockpit.Item3), C);
        Assert.Equal(2, pasted.Done);
        Assert.Equal(before + 2, d.Count);
        Assert.Equal(new BulkResult(0, 2), d.Paste((cockpit.Item1 - 2, cockpit.Item2 + 3, cockpit.Item3), C)); // the same place is taken now
        Assert.True(d.Undo());
        Assert.Equal(before, d.Count); // the refused paste was no step: undo takes back the first one
    }

    [Fact]
    public void Snap_maps_a_point_to_the_module_grid()
    {
        Assert.Equal((0, 0, 0), BlueprintDraft.SnapToCell(0.1, 0.1, 0.1));
        Assert.Equal((1, 1, 1), BlueprintDraft.SnapToCell(StationGrid.ModuleX + 0.5, StationGrid.ModuleY, StationGrid.ModuleZ * 1.99));
        Assert.Equal((-1, -1, -1), BlueprintDraft.SnapToCell(-0.01, -0.01, -StationGrid.ModuleZ));
    }

    // ---- the diff against the structure, and the price ------------------------------------------------------------

    private static List<DesignModule> RandomDraft(SimWorld w, int ship, int seed, out BlueprintDraft draft)
    {
        draft = new BlueprintDraft();
        draft.Load(w.Network.GetStructure(ship).Modules);
        var rng = new Random(seed);
        int edits = 1 + rng.Next(8);
        for (int i = 0; i < edits; i++) RandomEdit(draft, rng);
        return draft.ToModules().ToList();
    }

    [Fact]
    public void The_diff_counts_match_the_simulations_for_random_drafts()
    {
        var w = Yard(GameMode.Sandbox, out int ship, out _);
        int checkedDrafts = 0, withMoves = 0;
        for (int seed = 1; seed <= 300; seed++)
        {
            RandomDraft(w, ship, seed, out var d);
            var report = d.Check(w, 0);
            if (report.Reject is not (Reject.None or Reject.NotEnoughItems)) continue; // the counts only exist when the draft got as far as the price
            var diff = BlueprintDiff.Compute(d.Original, d.Modules);
            Assert.Equal(report.Added, diff.AddedTotal);
            Assert.Equal(report.Removed, diff.RemovedTotal);
            Assert.Equal(report.Moved, diff.Moved.Count);
            foreach (var m in diff.Moved)
            {
                Assert.Equal(m.Kind, d.Original[m.From]);
                Assert.Equal(m.Kind, d.Modules[m.To]);
            }
            Assert.Equal(d.Modules.Count, diff.Unchanged.Count + diff.Added.Count + diff.Moved.Count);
            checkedDrafts++;
            if (diff.Moved.Count > 0) withMoves++;
        }
        Assert.True(checkedDrafts > 60, $"only {checkedDrafts} drafts reached the price");
        Assert.True(withMoves > 0, "no draft moved a module");
    }

    [Fact]
    public void The_diff_names_each_ghost_and_pairs_a_move_by_distance()
    {
        var original = new Dictionary<(int, int, int), ModuleKind> { [(0, 0, 0)] = ModuleKind.Cockpit, [(0, 0, 1)] = ModuleKind.Hull, [(5, 0, 0)] = ModuleKind.Hull, [(1, 0, 0)] = ModuleKind.Gun };
        var draft = new Dictionary<(int, int, int), ModuleKind> { [(0, 0, 0)] = ModuleKind.Cockpit, [(0, 0, 2)] = ModuleKind.Hull, [(5, 0, 0)] = ModuleKind.Hull, [(1, 0, 0)] = ModuleKind.GunMk2, [(9, 0, 0)] = ModuleKind.Cargo };
        var diff = BlueprintDiff.Compute(original, draft, new[] { (9, 0, 0) });
        Assert.Equal(new GhostMove((0, 0, 1), (0, 0, 2), ModuleKind.Hull), Assert.Single(diff.Moved));
        Assert.Equal(GhostState.Moved, diff.StateAt((0, 0, 2)));
        Assert.Equal(GhostState.Invalid, diff.StateAt((9, 0, 0))); // an invalid cell wins over added
        Assert.Contains(diff.Removed, r => r.Cell == (1, 0, 0) && r.Kind == ModuleKind.Gun); // an upgrade is a removal and an addition on one cell
        Assert.Contains(diff.Added, a => a.Cell == (1, 0, 0) && a.Kind == ModuleKind.GunMk2);
        Assert.Equal(GhostState.Unchanged, diff.StateAt((5, 0, 0)));
        Assert.False(diff.IsEmpty);
        Assert.True(BlueprintDiff.Compute(original, original).IsEmpty);
    }

    [Fact]
    public void The_cost_by_item_equals_the_simulations_for_random_drafts()
    {
        var w = Yard(GameMode.Campaign, out int ship, out int station);
        foreach (var item in C.Items) w.Network.GiveStock(station, item.Index, 400); // the stock pays (a full hold would make every change a NoRoom)
        int compared = 0, priced = 0, refunded = 0;
        var rejects = new Dictionary<Reject, int>();
        for (int seed = 1; seed <= 400; seed++)
        {
            RandomDraft(w, ship, seed, out var d);
            var report = d.Check(w, 0);
            rejects[report.Reject] = rejects.GetValueOrDefault(report.Reject) + 1;
            if (report.Reject != Reject.None) continue;
            var diff = BlueprintDiff.Compute(d.Original, d.Modules);
            var cost = BlueprintCost.Compute(diff, C, pays: true, item => w.Network.HoldCount(0, item) + w.Network.StockCount(station, item));
            Assert.Equal(report.Cost, cost.ToPay);
            Assert.Equal(report.Refund, cost.ToRefund);
            Assert.True(cost.CanPay);
            compared++;
            if (cost.ToPay.Length > 0) priced++;
            if (cost.ToRefund.Length > 0) refunded++;
        }
        Assert.True(compared > 40, $"only {compared} drafts were valid: " + string.Join(", ", rejects.Select(p => p.Key + "=" + p.Value)));
        Assert.True(priced > 0 && refunded > 0, "the drafts never exercised both price and refund");
    }

    [Fact]
    public void Missing_items_agree_with_NotEnoughItems_and_a_free_world_costs_nothing()
    {
        var poor = Yard(GameMode.Campaign, out int ship, out int station);
        var d = new BlueprintDraft();
        d.Load(poor.Network.GetStructure(ship).Modules);
        Assert.Equal(DesignEditResult.Ok, d.Add((1, 1, 0), ModuleKind.MiningLaser, C));
        var report = d.Check(poor, 0);
        Assert.Equal(Reject.NotEnoughItems, report.Reject);
        var cost = BlueprintCost.Compute(BlueprintDiff.Compute(d.Original, d.Modules), C, true, i => poor.Network.HoldCount(0, i) + poor.Network.StockCount(station, i));
        Assert.False(cost.CanPay);
        Assert.NotEmpty(cost.Missing);
        Assert.All(cost.Missing, l => Assert.Equal(l.Net - l.Available, l.Missing));
        var free = BlueprintCost.Compute(BlueprintDiff.Compute(d.Original, d.Modules), C, false, _ => 0);
        Assert.Empty(free.ToPay);
        Assert.Empty(free.Lines);
        Assert.True(free.CanPay);
    }

    // ---- stats ---------------------------------------------------------------------------------------------------

    [Fact]
    public void Stats_follow_the_modules_a_heavier_ship_turns_slower_and_a_reactor_adds_power()
    {
        var w = Yard(GameMode.Sandbox, out int ship, out _);
        var mods = w.Network.GetStructure(ship).Modules.Values.ToList();
        var bonus = new StatBonuses();
        var now = ShipStats.Compute(mods, C, Handling, bonus);
        Assert.True(now.Mass > 0f && now.Engines >= 1 && now.HoldCapacity > 0 && now.Lasers >= 1);
        Assert.Equal(now.PowerOutput - now.PowerUse, now.PowerBalance);
        var heavy = ShipStats.Compute(mods.Concat(Enumerable.Repeat(ModuleKind.Cargo, 8)), C, Handling, bonus);
        Assert.True(heavy.Mass > now.Mass);
        Assert.True(heavy.Turn.RateDegPerSecond < now.Turn.RateDegPerSecond);
        Assert.True(heavy.HoldCapacity > now.HoldCapacity);
        var more = ShipStats.Compute(mods.Concat(new[] { ModuleKind.Reactor }), C, Handling, bonus);
        Assert.True(more.PowerBalance > now.PowerBalance);
        var research = ShipStats.Compute(mods, C, Handling, new StatBonuses(EngineSpeedPercent: 25, HoldPercent: 10));
        Assert.True(research.HoldCapacity > now.HoldCapacity);
        var rows = StatRows.Build(heavy, now);
        Assert.Equal(-1, rows.First(r => r.LabelKey == "bp.stat.turn").Sign);
        Assert.Equal(1, rows.First(r => r.LabelKey == "bp.stat.cargo").Sign);
        Assert.All(rows, r => Assert.True(Loc.Has(r.LabelKey)));
    }

    // ---- validation ----------------------------------------------------------------------------------------------

    [Fact]
    public void The_unchanged_ship_has_no_errors_and_says_so_in_both_languages()
    {
        var w = Yard(GameMode.Sandbox, out _, out _);
        var s = Open(w);
        Assert.False(s.Validation.HasErrors);
        var info = Assert.Single(s.Validation.Entries.Where(e => e.Code == "unchanged"));
        Assert.Equal("Sin cambios", info.Text(Language.Es));
        Assert.Equal("No changes", info.Text(Language.En));
        Assert.False(s.CanApply); // nothing to apply
    }

    [Fact]
    public void Validation_lists_what_is_wrong_with_icon_severity_and_the_part_to_frame()
    {
        var w = Yard(GameMode.Sandbox, out _, out _);
        var s = Open(w);
        var d = s.Draft;
        var cockpit = First(d, ModuleKind.Cockpit);
        d.Remove(cockpit);
        var e = Assert.Single(s.Validation.Entries.Where(x => x.Code == "noCockpit"));
        Assert.Equal(ValidationSeverity.Error, e.Severity);
        Assert.Equal("module:cockpit", e.Icon);
        Assert.True(IconCatalog.IsRegistered(e.Icon));
        Assert.True(s.Validation.HasErrors && !s.CanApply);
        Assert.Equal(ValidationSeverity.Error, s.Validation.Entries[0].Severity); // errors come first
        d.Undo();

        // A module far away is cut off: the entry names it so a click can frame it, and the diff marks it invalid.
        var far = (cockpit.Item1 + 9, cockpit.Item2, cockpit.Item3);
        Assert.Equal(DesignEditResult.Ok, d.Add(far, ModuleKind.Hull, C));
        var cut = Assert.Single(s.Validation.Entries.Where(x => x.Code == "disconnected"));
        Assert.Equal(far, cut.Frame);
        Assert.Contains(far, s.Validation.InvalidCells);
        Assert.Equal(GhostState.Invalid, s.Diff.StateAt(far));
        Assert.True(IconCatalog.IsRegistered(cut.Icon));
        s.Frame(cut);
        Assert.Equal(BlueprintCamera.CellCentre(far).X, s.Camera.Target.X, 3);
        Assert.Contains(far, s.Selection);
        Assert.False(s.CanApply);
        d.Undo();

        // Power: a big draw without a reactor to match is a warning, not an error.
        Assert.False(s.Validation.HasErrors);
        for (int i = 0; i < 12; i++) d.Add((cockpit.Item1 + 1, cockpit.Item2 + 1 + i, cockpit.Item3 + 2), ModuleKind.MiningLaserMk2, C);
        Assert.True(s.Stats.PowerBalance < 0, "twelve Mk2 lasers should overdraw the starter reactor");
        var power = Assert.Single(s.Validation.Entries.Where(x => x.Code == "power"));
        Assert.Equal(ValidationSeverity.Warning, power.Severity);
        Assert.Equal("status:power", power.Icon);
    }

    [Fact]
    public void Every_validation_text_exists_in_both_languages_and_warnings_do_not_block()
    {
        var table = Loc.All.ToDictionary(p => p.Key, p => p.Value);
        foreach (var key in new[] { "bp.val.empty", "bp.val.tooMany", "bp.val.noCockpit", "bp.val.noEngine", "bp.val.noReactor", "bp.val.noCargo", "bp.val.disconnected", "bp.val.port", "bp.val.portBlocked", "bp.val.power", "bp.val.view", "bp.val.noLaser", "bp.val.turn", "bp.val.unchanged", "bp.val.valid" })
        {
            Assert.True(table.TryGetValue(key, out var t), key);
            Assert.False(string.IsNullOrWhiteSpace(t.Es) || string.IsNullOrWhiteSpace(t.En), key);
        }
        var w = Yard(GameMode.Sandbox, out _, out _);
        var s = Open(w);
        var d = s.Draft;
        // Remove every mining laser: a warning only (the ship can still be applied).
        foreach (var k in d.Modules.Where(kv => ModuleKindIds.IsMiningLaser(kv.Value)).Select(kv => kv.Key).ToList()) d.Remove(k);
        var warn = s.Validation.Entries.FirstOrDefault(x => x.Code == "noLaser");
        Assert.NotNull(warn);
        Assert.Equal(ValidationSeverity.Warning, warn!.Severity);
        Assert.False(warn.Blocks);
        Assert.Equal("tool:mine", warn.Icon);
        Assert.False(s.Validation.HasErrors);
        Assert.DoesNotContain("⟦", warn.Text(Language.Es));
        Assert.DoesNotContain("⟦", warn.Text(Language.En));
    }

    [Fact]
    public void A_draft_the_simulation_refuses_never_reads_as_valid()
    {
        var w = Yard(GameMode.Campaign, out int ship, out _);
        var s = Open(w);
        Assert.Equal(DesignEditResult.Ok, s.Draft.Add((1, 1, 0), ModuleKind.MiningLaser, C));
        Assert.Equal(Reject.NotEnoughItems, s.Report.Reject);
        var e = Assert.Single(s.Validation.Entries.Where(x => x.Code == "items"));
        Assert.Equal(ValidationSeverity.Error, e.Severity);
        Assert.False(s.CanApply);
        Assert.False(s.Cost.CanPay);
        // Removing a module that holds the docked port is refused by the simulation and by the list.
        s.Draft.Undo();
        var port = s.DockPort()!.Value.Port;
        s.Draft.Remove(port);
        Assert.Contains(s.Validation.Entries, x => x.Code == "port" && x.Frame == port);
        Assert.False(s.CanApply);
        _ = ship;
    }

    // ---- the camera -----------------------------------------------------------------------------------------------

    [Fact]
    public void The_camera_orbits_pans_and_zooms_within_its_limits()
    {
        var cam = new BlueprintCamera();
        cam.Frame((0, 0, 0), (2, 1, 2));
        var target = cam.Target;
        Assert.True(Vector3.Distance(cam.Position, target) > 0f);
        cam.Orbit(100f, 100f);
        Assert.InRange(cam.Pitch, BlueprintCamera.MinPitch, BlueprintCamera.MaxPitch);
        Assert.InRange(cam.Yaw, 0f, MathF.PI * 2f);
        cam.Zoom(1000f);
        Assert.Equal(BlueprintCamera.MinDistance, cam.Distance);
        cam.Zoom(-1000f);
        Assert.Equal(BlueprintCamera.MaxDistance, cam.Distance);
        var before = cam.Target;
        cam.Pan(0.1f, 0f);
        Assert.NotEqual(before, cam.Target);
        Assert.Equal(0f, Vector3.Dot(cam.Target - before, cam.Back), 3); // panning stays in the screen plane
        float d1 = cam.Distance; cam.Zoom(2f);
        Assert.True(cam.Distance < d1);
    }

    [Fact]
    public void The_top_view_looks_down_with_the_bow_at_the_top_and_returns_to_the_orbit()
    {
        var cam = new BlueprintCamera();
        cam.Orbit(0.3f, 0.2f);
        float yaw = cam.Yaw, pitch = cam.Pitch;
        cam.SetTop();
        Assert.Equal(PlanView.Top, cam.View);
        Assert.Equal(Vector3.UnitY, cam.Back);
        Assert.Equal(-Vector3.UnitZ, cam.ScreenUp);
        Assert.Equal(Vector3.UnitX, cam.Right);
        cam.SetOrbit();
        Assert.Equal((yaw, pitch), (cam.Yaw, cam.Pitch));
        cam.CycleView(2);
        cam.CycleView(2);
        Assert.Equal(PlanView.Section, cam.View);
        Assert.Equal(2, cam.SectionLayer);
        Assert.True(cam.LayerVisible(2) && !cam.LayerVisible(3)); // the section cuts the decks above
        cam.CycleView(2);
        Assert.Equal(PlanView.Orbit, cam.View);
        Assert.True(cam.LayerVisible(9));
    }

    [Fact]
    public void The_ray_through_the_screen_centre_hits_the_target_and_projection_round_trips()
    {
        var cam = new BlueprintCamera();
        cam.Frame((0, 0, 0), (0, 0, 0));
        var ray = cam.RayAt(400f, 300f, 800f, 600f);
        Assert.True(Vector3.Distance(Vector3.Normalize(cam.Target - cam.Position), ray.Direction) < 1e-4f);
        var p = cam.Project(cam.Target, 800f, 600f);
        Assert.NotNull(p);
        Assert.Equal(400f, p!.Value.X, 1);
        Assert.Equal(300f, p.Value.Y, 1);
        var off = cam.Target + cam.Right * 3f;
        var q = cam.Project(off, 800f, 600f)!.Value;
        Assert.True(q.X > 400f);
        var back = cam.RayAt(q.X, q.Y, 800f, 600f);
        Assert.True(Vector3.Distance(back.Direction, Vector3.Normalize(off - cam.Position)) < 1e-3f);
        Assert.Null(cam.Project(cam.Position + cam.Back * 5f, 800f, 600f)); // behind the camera
    }

    [Fact]
    public void Picking_hits_the_nearest_module_names_the_free_cell_beside_the_face_and_falls_back_to_the_plane()
    {
        var mods = new Dictionary<(int Mx, int My, int Mz), ModuleKind> { [(0, 0, 0)] = ModuleKind.Cockpit, [(1, 0, 0)] = ModuleKind.Hull };
        var cam = new BlueprintCamera();
        cam.Frame((0, 0, 0), (1, 0, 0));
        // A ray from above the second module straight down.
        var top = cam.Pick(new PlanRay(new Vector3(StationGrid.ModuleX * 1.5f, 50f, StationGrid.ModuleZ * 0.5f), -Vector3.UnitY), mods);
        Assert.Equal((1, 0, 0), top.Module);
        Assert.Equal((1, 1, 0), top.Place); // the face hit is the roof: a module goes on top
        // A ray from the side at the end of the row: the place is the free cell beyond the last module.
        var side = cam.Pick(new PlanRay(new Vector3(60f, StationGrid.ModuleY * 0.5f, StationGrid.ModuleZ * 0.5f), -Vector3.UnitX), mods);
        Assert.Equal((1, 0, 0), side.Module);
        Assert.Equal((2, 0, 0), side.Place);
        // The face between two modules is not a free place.
        var between = cam.Pick(new PlanRay(new Vector3(StationGrid.ModuleX * 0.5f, StationGrid.ModuleY * 0.5f, 60f), -Vector3.UnitZ), mods);
        Assert.Equal((0, 0, 0), between.Module);
        Assert.Equal((0, 0, 1), between.Place);
        // Nothing hit: the plane through the target gives the empty cell.
        var empty = cam.Pick(new PlanRay(new Vector3(StationGrid.ModuleX * 5.5f, 50f, StationGrid.ModuleZ * 0.5f), -Vector3.UnitY), mods);
        Assert.Null(empty.Module);
        Assert.NotNull(empty.Place);
        // In the section view the decks above the cut are not clickable.
        mods[(0, 1, 0)] = ModuleKind.Gun;
        cam.SetSection(0);
        var cut = cam.Pick(new PlanRay(new Vector3(StationGrid.ModuleX * 0.5f, 80f, StationGrid.ModuleZ * 0.5f), -Vector3.UnitY), mods);
        Assert.Equal((0, 0, 0), cut.Module);
    }

    // ---- the saved blueprint, the session and the registry ---------------------------------------------------------

    [Fact]
    public void A_blueprint_round_trips_and_a_bad_file_is_refused()
    {
        var w = Yard(GameMode.Sandbox, out _, out _);
        var d = Open(w).Draft;
        d.Rotate(new[] { First(d, ModuleKind.Engine) }, 3);
        string text = BlueprintFormat.Serialize(d, "Mining skiff");
        var parsed = BlueprintFormat.Parse(text);
        Assert.NotNull(parsed);
        Assert.Equal("Mining skiff", parsed!.Value.Name);
        var other = new BlueprintDraft();
        other.Load(Array.Empty<KeyValuePair<(int, int, int), ModuleKind>>());
        Assert.Equal(d.Count, other.ReplaceWith(parsed.Value.Parts).Done);
        Assert.Equal(Snapshot(d), Snapshot(other));
        Assert.Null(BlueprintFormat.Parse("nonsense"));
        Assert.Null(BlueprintFormat.Parse(BlueprintFormat.Header + "\n0,0,0,not_a_kind,0\n"));
        Assert.Null(BlueprintFormat.Parse(BlueprintFormat.Header + "\n0,0,0,hull,0\n0,0,0,hull,0\n"));
        Assert.Null(BlueprintFormat.Parse(BlueprintFormat.Header + "\n0,0,0,hull,9\n"));
        Assert.Null(BlueprintFormat.Parse(BlueprintFormat.Header + "\n0,0,x,hull,0\n"));
    }

    [Fact]
    public void The_session_tools_edit_the_draft_and_apply_sends_one_command_the_simulation_accepts()
    {
        var w = Yard(GameMode.Sandbox, out int ship, out _);
        var s = Open(w);
        var cockpit = First(s.Draft, ModuleKind.Cockpit);
        var steps = new[] { (1, 0, 0), (-1, 0, 0), (0, 1, 0), (0, 0, 1), (0, 0, -1) };
        var face = s.DockPort()!.Value.Face;
        var spots = s.Draft.Modules.Keys.SelectMany(k => steps.Select(t => (k.Mx + t.Item1, k.My + t.Item2, k.Mz + t.Item3)))
            .Where(c => s.Draft.CanAddAt(c) && c != face).Distinct().OrderByDescending(c => c.Item2).ThenBy(c => c).ToList();
        var free = spots[0]; // a free cell on the roof, touching the ship
        Assert.Null(s.Click(free)); // the Select tool on an empty cell does nothing
        s.SetTool(BlueprintTool.Place);
        Assert.Equal("designer.hint", s.Click(free)); // Place without a kind says how
        s.PickKind(ModuleKind.Hull);
        Assert.Equal(BlueprintTool.Place, s.Tool);
        Assert.Null(s.Click(free));
        Assert.Null(s.Click(free)); // a click on an occupied cell with the Place tool selects it
        Assert.Single(s.Selection);
        Assert.True(s.Draft.Dirty);
        Assert.Equal(1, s.Diff.Added.Count);
        s.SetTool(BlueprintTool.Move);
        Assert.Null(s.Click(free));
        Assert.Equal(free, s.Carrying);
        var target = spots.First(c => c != free && ShipDesignEditor.Touches(s.Draft.Original, c)); // touches the real ship, so the move stays one piece
        Assert.Null(s.Click(target));
        Assert.Null(s.Carrying);
        Assert.Contains(target, s.Selection);
        Assert.False(s.Draft.Modules.ContainsKey(free));
        s.SetTool(BlueprintTool.Delete);
        Assert.Null(s.Click(target));
        Assert.False(s.Draft.Dirty);
        s.Draft.Undo();
        s.SetTool(BlueprintTool.Select);
        s.Select(target, additive: false);
        Assert.Equal(1, s.Selection.Count);
        Assert.True(s.CanApply, string.Join(", ", s.Validation.Entries.Select(e => e.Code)));
        var cmd = s.ToCommand();
        Assert.Equal(s.Draft.ToModules(), cmd.Modules);
        w.Submit(0, cmd);
        w.Step();
        Assert.Equal(Reject.None, w.LastResults.Single().Reject);
        Assert.Equal(s.Draft.Count, w.Network.GetStructure(ship).Modules.Count);
        s.Open();
        Assert.False(s.Dirty);
        Assert.False(s.CanApply);
        w.ValidateInvariants();
    }

    [Fact]
    public void The_editor_controls_live_in_their_own_context_without_collisions_and_in_the_help()
    {
        var bp = InputActions.All.Where(a => (a.Contexts & InputContext.Blueprint) != 0).ToList();
        Assert.True(bp.Count >= 20);
        Assert.All(bp.Where(a => a.Id.StartsWith("bp")), a => Assert.Equal(InputContext.Blueprint, a.Contexts)); // only the editor reads them
        Assert.Contains(InputActions.ActiveSets(), s => s == InputContext.Blueprint);
        Assert.Equal(InputContext.Blueprint, InputActions.Active(false, false, false, false, blueprint: true));
        Assert.Equal("bpUndo", InputActions.Find("@ctrlz", InputContext.Blueprint)!.Id);
        Assert.Equal("bpRedo", InputActions.Find("@ctrly", InputContext.Blueprint)!.Id);
        Assert.Equal("bpApply", InputActions.Find("Enter", InputContext.Blueprint)!.Id);
        Assert.Null(InputActions.Find("Enter", InputContext.Flying)); // the editor keys do not exist in flight
        Assert.Null(InputActions.Find("Tab", InputContext.Blueprint)); // and the ship computer key is not one of the editor's
        foreach (var lang in new[] { Language.Es, Language.En })
        {
            var old = Loc.Lang;
            Loc.Lang = lang;
            try
            {
                string help = InputActions.HelpText();
                Assert.Contains(Loc.T("help.group.blueprint"), help);
                foreach (var a in bp.Where(a => a.Id.StartsWith("bp"))) Assert.Contains(Loc.T(a.NameKey), help);
                Assert.DoesNotContain("⟦", help);
            }
            finally { Loc.Lang = old; }
        }
    }

    [Fact]
    public void The_classic_designer_setting_round_trips_and_defaults_to_the_new_editor()
    {
        Assert.False(GameSettings.Defaults().ClassicDesigner);
        var on = GameSettings.Defaults() with { ClassicDesigner = true };
        Assert.True(GameSettings.FromPairs(on.ToPairs(), GameSettings.Defaults()).ClassicDesigner);
    }
}
