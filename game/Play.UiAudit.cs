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
    }

    private void CloseAllPanels()
    {
        CloseBuildMenu();
        CloseResearchPanel();
        _designer?.Close();
        if (_pauseMenu is not null) _pauseMenu.Visible = false;
        _endScreen?.Dismiss();
        _storyDialog?.Hide();
        BuildMenu.StressSuffix = null;
        _helpOpen = false;
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
        s.Add(("done", () => CloseAllPanels()));
    }

    private void UiAuditFrame()
    {
        if (!_uiAudit) return;
        _auditFrame++;
        _wantCapture = false;
        _paused = _paused && PauseOpen;
        // Per size: a step every 26 frames (setup on the second, audit on the 22nd, after the HUD fades have run).
        const int Period = 26;
        if (_auditSize >= _auditSizes.Length)
        {
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
        if (phase == 2) step.Setup();
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
            _auditErrors += errors;
            _auditNotes += findings.Count - errors;
            _auditStep++;
        }
    }
}
