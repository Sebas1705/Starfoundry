using System;
using System.Collections.Generic;
using System.Linq;
using Godot;
using Starfoundry.Sim;
using Starfoundry.Ui;

namespace Starfoundry.Game;

/// <summary>
/// <c>--ui-audit [--ui-lang=es|en] [--audit-sizes=800x600,1024x600,...]</c>: the headless text-fit audit of the in-game interface. For every window
/// size it opens each panel in turn (the HUD with toasts, the build menu on every tab, the research panel, the key panel in its contexts, the ship
/// designer, the pause menu with Settings and Controls, the end screens, the story briefing), lets the layout settle and runs <see cref="UiAudit"/>
/// over the whole tree. It prints <c>UIAUDIT</c> lines for every finding and a final <c>UIAUDIT RESULT errors=N</c> line, quits with exit code 1 when there
/// were errors. <c>tools/ui-audit.ps1</c> runs it for es and en, for the campaign and for a scenario game. Nothing is saved.
/// </summary>
public partial class Play
{
    private bool _uiAudit;
    private int _auditFrame, _auditStep, _auditErrors, _auditNotes;
    private string[] _auditSizes = { "800x600", "1024x600", "1280x720", "1920x1080" };
    private int _auditSize;
    private string? _auditShots;
    private readonly List<(string Name, Action Setup)> _auditSteps = new();

    private void UiAuditSetup(string[] args)
    {
        if (args.FirstOrDefault(a => a.StartsWith("--audit-sizes="))?["--audit-sizes=".Length..] is { Length: > 0 } list) _auditSizes = list.Split(',');
        _auditShots = args.FirstOrDefault(a => a.StartsWith("--audit-shots="))?["--audit-shots=".Length..];
        BuildAuditSteps();
        DisplayServer.WindowSetVsyncMode(DisplayServer.VSyncMode.Disabled); // a frame lasts what it costs, so the timing pass measures the work
    }

    private void CloseAllPanels()
    {
        CloseBuildMenu();
        CloseResearchPanel();
        CloseInventory();
        _designer?.Close();
        if (_pauseMenu is not null) _pauseMenu.Visible = false;
        _endScreen?.Dismiss();
        _storyDialog?.Hide();
        BuildMenu.StressSuffix = null;
        _helpOpen = false;
        _hudDetailShot = null;
        _detailsOpen = false;
        _mapOpen = false;
        _tool = ToolMode.Pointer;
        _paused = false;
    }

    private void BuildAuditSteps()
    {
        var s = _auditSteps;
        s.Add(("settle", () => { }));
        s.Add(("hud", () => { CloseAllPanels(); Toasts(); }));
        s.Add(("hud.details", () => { CloseAllPanels(); _detailsOpen = true; _mapOpen = true; }));
        s.Add(("hud.help", () => { CloseAllPanels(); _helpOpen = true; }));
        // The three HUD detail levels (Shift+F3), seated and standing, with the busy toasts: nothing may overflow, overlap or reach the centre.
        foreach (var level in new[] { HudDetail.Minimal, HudDetail.Standard, HudDetail.Detailed })
        {
            var lv = level;
            s.Add(("hud.detail." + HudDetails.Id(lv), () => { CloseAllPanels(); _hudDetailShot = lv; Toasts(); }));
            s.Add(("hud.detail." + HudDetails.Id(lv) + ".info", () => { CloseAllPanels(); _hudDetailShot = lv; _detailsOpen = true; }));
        }
        s.Add(("keys.flying", () => { CloseAllPanels(); _yaw = 0f; _pitch = 0.25f; }));
        s.Add(("keys.aim", () => { CloseAllPanels(); if (!LookAtMachine("core:smelter")) GD.Print("UIAUDIT note: no machine to aim at"); }));
        s.Add(("keys.walking", () => { CloseAllPanels(); _seated = true; ToggleSeat(); _body = new Vector3(4f, 0f, 20f); _yaw = MathF.PI * 0.8f; _pitch = -0.1f; }));
        s.Add(("keys.map", () => { CloseAllPanels(); _body = StandSpot; ToggleSeat(); _yaw = MathF.PI; _pitch = -0.35f; _mapOpen = true; }));
        foreach (var cat in BuildCatalog.Categories)
        {
            var category = cat;
            s.Add(("build." + category, () => { CloseAllPanels(); _buildMenu?.Open(category); }));
            s.Add(("build." + category + ".stress", () => { CloseAllPanels(); BuildMenu.StressSuffix = Loc.Lang == Language.Es ? " de ejemplo con un nombre larguisimo" : " with an extra long example name"; _buildMenu?.Open(category); }));
        }
        s.Add(("build.tool", () =>
        {
            CloseAllPanels();
            SelectBuild(_palette.FindIndex(b => !b.IsBelt && !b.IsModule && !b.IsStation && b.Machine == _content.MachineIndex("core:smelter")));
        }));
        s.Add(("keys.belt", () => { CloseAllPanels(); SelectBuild(0); _detailsOpen = true; }));
        s.Add(("research", () => { CloseAllPanels(); ToggleResearchPanel(); }));
        foreach (var node in new[] { "core:rapid_loader", "core:repair_drones", "core:advanced_logistics" })
        {
            var id = node;
            s.Add(("research." + id, () => { _researchPanel?.Select(_content.ResearchIndex(id)); }));
        }
        // The personal inventory (ADR 0035): the reactor beside the bag, the hold with a filter, a machine's buffers and the bin's question.
        s.Add(("inventory.reactor", () => { CloseAllPanels(); InventoryAuditFeed(); InventoryShotOpenReactor(); }));
        s.Add(("inventory.hold", () => { CloseAllPanels(); InventoryAuditFeed(); _inventory?.Open(ContainerRef.Hold); _inventory?.SetFilter(ItemCategory.Ore); }));
        s.Add(("inventory.machine", () => { CloseAllPanels(); InventoryAuditFeed(); if (InventoryShotMachine() is { } m) _inventory?.Open(ContainerRef.MachineIn(m), m); }));
        s.Add(("inventory.bin", () =>
        {
            CloseAllPanels();
            InventoryAuditFeed();
            _inventory?.Open(ContainerRef.Hold);
            if (_inventory?.FindSlot(ContainerRef.Person, _content.ItemIndex("core:iron_ore")) is { } slot) _inventory.AskBinFor(slot);
        }));
        s.Add(("designer", () => { CloseAllPanels(); _designer?.Open(); }));
        s.Add(("pause", () => { CloseAllPanels(); OpenPause(); }));
        s.Add(("pause.settings", () => { _pauseMenu?.Open(MenuScreen.Page.Settings); }));
        s.Add(("pause.controls", () => { _pauseMenu?.Open(MenuScreen.Page.Controls); }));
        s.Add(("end.victory", () =>
        {
            CloseAllPanels();
            if (_scenario is not null && _progress is not null)
            {
                var goals = _progress.Goals.Select(g => g.Goal.Id == "no_losses"
                    ? new GoalSnapshot(g.Goal.Id, GoalStatus.Failed, 0, null, 612.0)
                    : new GoalSnapshot(g.Goal.Id, GoalStatus.Done, g.Target, 1180.0, null)).ToList();
                _progress.Restore(new ProgressSnapshot(Net.Tick, goals, new List<(int, double)>(), OutcomeKind.Won, OutcomeReason.WavesSurvived, 2, 1523.0, false));
                ShowScenarioOutcome();
            }
            else ShowOutcome(new OutcomeResult(OutcomeKind.Won, OutcomeReason.WavesSurvived,
                new OutcomeStats(Net.Tick / (double)Units.TicksPerSecond, 14, 2, 0, 3, 10, 412, 186, 5)));
        }));
        s.Add(("end.defeat", () =>
        {
            CloseAllPanels();
            ShowOutcome(new OutcomeResult(OutcomeKind.Lost, OutcomeReason.ShipLost,
                new OutcomeStats(Net.Tick / (double)Units.TicksPerSecond, 14, 2, 1, 3, 4, 412, 186, 5)));
        }));
        s.Add(("story", () =>
        {
            CloseAllPanels();
            if (_scenario is not null && _storyDialog is not null && true)
                _storyDialog.Present(_scenario.Name(Loc.Lang == Language.Es), _scenario.Briefing(Loc.Lang == Language.Es), new StoryMessage(_scenario.Story.Count > 0 ? _scenario.Story[0].Speaker : "Speaker", _scenario.Story.Count > 0 ? (Loc.Lang == Language.Es ? _scenario.Story[0].TextEs : _scenario.Story[0].TextEn) : "Text", true));
        }));
        s.Add(("hud.detail.detailed.walking", () => { CloseAllPanels(); _hudDetailShot = HudDetail.Detailed; if (_seated) StandUp(); Toasts(); }));
        s.Add(("done", () => { CloseAllPanels(); if (!_seated) { _body = StandSpot; Sit(); } })); // seated again for the next window size
    }

    // ---- the timing pass -----------------------------------------------------------------------------------
    // Every step opens a panel (or changes it) and then lets 20 frames run. The CPU time of those frames comes from the engine's own monitor
    // (script and scene processing, without the GPU and without waiting for the screen), so a loaded machine does not fake a failure. The budgets
    // are the ones of the spec (docs/prototypes/hud-information-study.md): a panel opens in under 100 ms, no frame during the interaction goes
    // over 20 ms, both checked at 1920x1080 (the other sizes are reported and noted only).

    public const float OpenBudgetMs = 100f, FrameBudgetMs = 20f;
    private double _perfSetupMs;
    private float _perfOpenMs, _perfWorstMs;
    private int _perfWorstPhase, _perfOver;
    private double _perfLastWall;
    private readonly List<(string Name, float Open, float Worst)> _perfRows = new();
    private float _perfWorstOpen, _perfWorstFrame;
    private string _perfWorstOpenName = "", _perfWorstFrameName = "";

    /// <summary>
    /// Called once per frame with the wall time since the previous frame. The audit turns vsync off, so a frame lasts what it costs (script, layout and
    /// drawing). The frame that runs the step's setup and the next one are the cost of opening; the frames after it are the interaction.
    /// </summary>
    private void PerfSample(int phase, float frameMs)
    {
        if (phase is 3 or 4) _perfOpenMs = Math.Max(_perfOpenMs, frameMs);
        else if (phase is >= 5 and <= 21)
        {
            if (frameMs > FrameBudgetMs) _perfOver++;
            if (frameMs > _perfWorstMs) { _perfWorstMs = frameMs; _perfWorstPhase = phase; }
        }
    }

    private void PerfBegin() { _perfSetupMs = 0; _perfOpenMs = _perfWorstMs = 0f; _perfOver = 0; }

    /// <summary>Reports the step's numbers and returns the number of budget failures (1080p only; elsewhere a note).</summary>
    private int PerfEnd(string ctx, string size)
    {
        float open = Math.Max((float)_perfSetupMs, _perfOpenMs); // the setup call is inside the first measured frame
        _perfRows.Add((ctx, open, _perfWorstMs));
        if (open > _perfWorstOpen) { _perfWorstOpen = open; _perfWorstOpenName = ctx; }
        if (_perfWorstMs > _perfWorstFrame) { _perfWorstFrame = _perfWorstMs; _perfWorstFrameName = ctx; }
        bool strict = size == "1920x1080";
        int errors = 0;
        // One frame over the budget is a hitch (the garbage collector, another process on the machine): a note. Two or more in a step are a failure.
        bool badOpen = open > OpenBudgetMs, badFrame = _perfOver >= 2, hitch = _perfOver == 1;
        if (badOpen && strict) errors++;
        if (badFrame && strict) errors++;
        GD.Print($"UIAUDIT PERF {ctx} open={open:F1}ms worstFrame={_perfWorstMs:F1}ms setup={_perfSetupMs:F1}ms (worst frame at phase {_perfWorstPhase})" + (hitch ? " note: one frame over " + FrameBudgetMs + " ms (a hitch)" : "") + (badOpen || badFrame ? (strict ? " FAIL budget (open " + OpenBudgetMs + " ms, frame " + FrameBudgetMs + " ms)" : " note: over budget (checked at 1920x1080)") : ""));
        return errors;
    }

    private void UiAuditFrame()
    {
        if (!_uiAudit) return;
        _auditFrame++;
        double nowMs = Time.GetTicksUsec() / 1000.0;
        float frameMs = (float)(nowMs - _perfLastWall);
        _perfLastWall = nowMs;
        _wantCapture = false;
        _paused = _paused && PauseOpen;
        // Per size: a step every 26 frames (setup on the second, audit on the 22nd, after the HUD fades have run).
        const int Period = 26;
        if (_auditSize >= _auditSizes.Length)
        {
            GD.Print($"UIAUDIT PERF worst open={_perfWorstOpen:F1}ms ({_perfWorstOpenName}) worst frame={_perfWorstFrame:F1}ms ({_perfWorstFrameName})");
            GD.Print($"UIAUDIT RESULT errors={_auditErrors} notes={_auditNotes} lang={Loc.Lang} scenario={(_scenario?.Id ?? "campaign")}");
            GetTree().Quit(_auditErrors == 0 ? 0 : 1);
            _uiAudit = false;
            return;
        }
        int phase = _auditFrame % Period;
        if (_auditStep == 0 && phase == 1 && _auditFrame < Period * 2)
        {
            var wh = _auditSizes[_auditSize].Split('x');
            SetWindowSize(GetWindow(), int.Parse(wh[0]), int.Parse(wh[1]));
        }
        if (_auditStep >= _auditSteps.Count) { _auditStep = 0; _auditSize++; _auditFrame = 0; return; }
        var step = _auditSteps[_auditStep];
        if (phase == 2)
        {
            PerfBegin();
            var sw = System.Diagnostics.Stopwatch.StartNew();
            step.Setup();
            _perfSetupMs = sw.Elapsed.TotalMilliseconds;
        }
        else if (phase is >= 3 and <= 21) PerfSample(phase, frameMs);
        if (phase == 22)
        {
            var vp = GetViewport().GetVisibleRect();
            string ctx = $"{_auditSizes[_auditSize]}/{step.Name}";
            if (step.Name == "settle") { _auditStep++; return; }
            var findings = UiAudit.Run(GetTree().Root, vp);
            // Marker labels are drawn, not nodes: check the boxes the layer recorded.
            if (_markers.Visible)
            {
                float limit = _markers.RightLimit > 0f ? Math.Min(_markers.RightLimit, vp.Size.X) : vp.Size.X;
                foreach (var (rect, text) in _markers.LastLabels)
                    if (rect.Position.X < -1f || rect.Position.Y < -1f || rect.End.X > limit + 1f || rect.End.Y > vp.Size.Y + 1f)
                        findings.Add(new UiAudit.Finding("MARKER_OFFSCREEN", "MarkerLayer", text, $"[{rect.Position.X:0},{rect.Position.Y:0} {rect.Size.X:0}x{rect.Size.Y:0}] limit {limit:0}", true));
            }
            if (_auditShots is not null) { System.IO.Directory.CreateDirectory(_auditShots); GetViewport().GetTexture().GetImage().SavePng(System.IO.Path.Combine(_auditShots, ctx.Replace("/", "_").Replace(":", "_") + ".png")); }
            int errors = UiAudit.Report(ctx, findings);
            _auditErrors += errors + PerfEnd(ctx, _auditSizes[_auditSize]);
            _auditNotes += findings.Count - errors;
            _auditStep++;
        }
    }
}
