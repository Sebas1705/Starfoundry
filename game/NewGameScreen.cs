using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using Godot;
using Starfoundry.Sim;
using Starfoundry.Ui;

namespace Starfoundry.Game;

/// <summary>What the player picked: the scenario, its rules (null keeps the scenario's own) and the seed (0 is random).</summary>
public sealed record NewGameChoice(string ScenarioId, WorldRules? Rules, ulong Seed);

/// <summary>
/// The New Game screen, in the style of <see cref="MenuScreen"/>: three tabs. Campaign (the standard game with a difficulty preset and a
/// seed), Story (the five levels, locked until the one before is completed, with best time and stars) and Sandbox (a custom rules form with
/// named presets). Arrows, Enter and Esc work as well as the mouse; PageUp and PageDown change tab.
/// </summary>
public partial class NewGameScreen : Control
{
    public enum Tab { Campaign, Story, Sandbox }

    private static readonly Color PanelBg = new(0.05f, 0.07f, 0.11f, 0.94f);
    private static readonly Color PanelEdge = new(0.35f, 0.45f, 0.65f, 0.8f);
    private static readonly Color Muted = new(0.65f, 0.72f, 0.85f);
    private static readonly Color Gold = new(1f, 0.85f, 0.4f);
    private static readonly Color Good = new(0.55f, 0.95f, 0.65f);

    private readonly ContentDb _content;
    private readonly ScenarioCatalog _catalog;
    private VBoxContainer _body = null!;
    private VBoxContainer _tabContent = null!;
    private Button _start = null!;
    private Label _status = null!;
    private readonly Button[] _tabButtons = new Button[3];

    private Tab _tab = Tab.Campaign;
    private string _difficulty = "normal";
    private string _campaignSeed = "";
    private string _storySelected = "";
    private static SandboxSettings _sandbox = new();
    private static string _sandboxSeed = "";
    private static string _presetName = "";

    public event Action<NewGameChoice>? Started;
    public event Action? Cancelled;

    public NewGameScreen(ContentDb content, ScenarioCatalog catalog, Tab tab = Tab.Campaign)
    {
        _content = content;
        _catalog = catalog;
        _tab = tab;
        var story = ScenarioKinds.Story(catalog);
        var progress = ScenarioStore.Progress;
        var ids = story.Select(s => s.Id).ToList();
        // Start on the first level that is open and not completed (the next one to play).
        _storySelected = story.FirstOrDefault(s => progress.IsUnlocked(ids, s.Id) && !progress.IsCompleted(s.Id))?.Id ?? story.FirstOrDefault()?.Id ?? "";
    }

    public override void _Ready()
    {
        Theme = UiTheme.Create();
        SetAnchorsAndOffsetsPreset(LayoutPreset.FullRect);
        MouseFilter = MouseFilterEnum.Stop;
        var centre = new CenterContainer { MouseFilter = MouseFilterEnum.Ignore };
        centre.SetAnchorsAndOffsetsPreset(LayoutPreset.FullRect);
        AddChild(centre);
        var panel = new PanelContainer { CustomMinimumSize = new Vector2(1120, 0) };
        panel.AddThemeStyleboxOverride("panel", UiKit.Box(PanelBg, PanelEdge, 8, 14f));
        centre.AddChild(panel);
        _body = new VBoxContainer();
        _body.AddThemeConstantOverride("separation", 8);
        panel.AddChild(_body);
        Build();
    }

    public void ShowTab(Tab tab)
    {
        _tab = tab;
        Build();
    }

    // ---- frame -----------------------------------------------------------------------------------

    private void Build()
    {
        foreach (var child in _body.GetChildren()) { _body.RemoveChild(child); child.QueueFree(); }
        var title = UiKit.Text(30, Colors.White);
        title.Text = Loc.T("ng.title");
        title.HorizontalAlignment = HorizontalAlignment.Center;
        _body.AddChild(title);

        var tabs = new HBoxContainer { Alignment = BoxContainer.AlignmentMode.Center };
        tabs.AddThemeConstantOverride("separation", 8);
        var group = new ButtonGroup();
        foreach (Tab t in Enum.GetValues<Tab>())
        {
            var tab = t;
            var b = UiKit.Button(Loc.T("ng.tab." + tab.ToString().ToLowerInvariant()), () => { if (_tab != tab) { _tab = tab; Build(); } }, 17, 200);
            b.ToggleMode = true;
            b.ButtonGroup = group;
            b.ButtonPressed = tab == _tab;
            b.CustomMinimumSize = new Vector2(200, 38);
            b.Name = "Tab" + tab;
            _tabButtons[(int)tab] = b;
            tabs.AddChild(b);
        }
        _body.AddChild(tabs);

        _tabContent = new VBoxContainer { CustomMinimumSize = new Vector2(0, 420) };
        _tabContent.AddThemeConstantOverride("separation", 8);
        _body.AddChild(_tabContent);
        switch (_tab)
        {
            case Tab.Story: BuildStory(); break;
            case Tab.Sandbox: BuildSandbox(); break;
            default: BuildCampaign(); break;
        }

        _status = UiKit.Text(13, Good);
        _status.HorizontalAlignment = HorizontalAlignment.Center;
        _body.AddChild(_status);

        var buttons = new HBoxContainer { Alignment = BoxContainer.AlignmentMode.Center };
        buttons.AddThemeConstantOverride("separation", 12);
        var back = UiKit.Button(Loc.T("menu.back"), () => Cancelled?.Invoke(), 17, 200);
        back.Name = "BackButton";
        back.CustomMinimumSize = new Vector2(200, 40);
        _start = UiKit.Button(Loc.T("ng.start"), StartPressed, 17, 260);
        _start.Name = "StartButton";
        _start.CustomMinimumSize = new Vector2(260, 40);
        buttons.AddChild(back);
        buttons.AddChild(_start);
        _body.AddChild(buttons);
        var hint = UiKit.Text(11, new Color(0.5f, 0.56f, 0.68f));
        hint.Text = Loc.T("ng.hint");
        hint.HorizontalAlignment = HorizontalAlignment.Center;
        _body.AddChild(hint);
        UpdateStart();
        CallDeferred(MethodName.FocusStart);
    }

    private void FocusStart()
    {
        if (!IsInsideTree()) return;
        if (_start is { Disabled: false }) _start.GrabFocus();
        else _tabButtons[(int)_tab]?.GrabFocus();
    }

    private void UpdateStart()
    {
        if (_start is null) return;
        _start.Disabled = _tab == Tab.Story && (_storySelected.Length == 0 || !StoryUnlocked(_storySelected));
    }

    public override void _UnhandledInput(InputEvent e)
    {
        if (!Visible || e is not InputEventKey { Pressed: true, Echo: false } k) return;
        if (k.Keycode == Key.Escape) { GetViewport().SetInputAsHandled(); Cancelled?.Invoke(); }
        else if (k.Keycode is Key.Pageup or Key.Pagedown)
        {
            GetViewport().SetInputAsHandled();
            int n = ((int)_tab + (k.Keycode == Key.Pagedown ? 1 : 2)) % 3;
            _tab = (Tab)n;
            Build();
        }
    }

    private void StartPressed()
    {
        switch (_tab)
        {
            case Tab.Campaign:
                Started?.Invoke(new NewGameChoice(ScenarioKinds.CampaignId, (WorldRules.Preset(_difficulty) ?? WorldRules.Normal), ParseSeed(_campaignSeed)));
                break;
            case Tab.Story:
                if (_storySelected.Length > 0 && StoryUnlocked(_storySelected)) Started?.Invoke(new NewGameChoice(_storySelected, null, 0));
                break;
            default:
                var s = _sandbox with { Seed = ParseSeed(_sandboxSeed) };
                Started?.Invoke(new NewGameChoice(s.ScenarioId, s.ToRules(_content), s.Clamped().Seed));
                break;
        }
    }

    private static ulong ParseSeed(string text) =>
        ulong.TryParse(text.Trim(), NumberStyles.None, CultureInfo.InvariantCulture, out ulong v) && v is >= 1 and <= 999_999_999UL ? v : 0;

    private LineEdit SeedField(string value, Action<string> changed, out Button random)
    {
        var edit = new LineEdit { Text = value, PlaceholderText = Loc.T("ng.seed.random"), MaxLength = 9, CustomMinimumSize = new Vector2(180, 34), Name = "SeedField" };
        edit.AddThemeFontSizeOverride("font_size", 15);
        edit.TextChanged += t =>
        {
            string digits = new(t.Where(char.IsAsciiDigit).ToArray());
            if (digits != t) { edit.Text = digits; edit.CaretColumn = digits.Length; }
            changed(digits);
        };
        var field = edit;
        random = UiKit.Button(Loc.T("ng.seed.randomButton"), () => { field.Text = ""; changed(""); }, 14);
        random.CustomMinimumSize = new Vector2(120, 34);
        return edit;
    }

    private void AddSeedRow(Control parent, string value, Action<string> changed)
    {
        var row = new HBoxContainer();
        row.AddThemeConstantOverride("separation", 8);
        var label = UiKit.Text(15, Muted);
        label.Text = Loc.T("ng.seed");
        row.AddChild(label);
        row.AddChild(SeedField(value, changed, out var random));
        row.AddChild(random);
        var note = UiKit.Text(12, Muted);
        note.Text = Loc.T("ng.seed.note");
        row.AddChild(note);
        parent.AddChild(row);
    }

    // ---- campaign --------------------------------------------------------------------------------

    private void BuildCampaign()
    {
        var def = _catalog.Find(ScenarioKinds.CampaignId);
        var intro = UiKit.Text(15, Muted);
        intro.Text = def is null ? Loc.T("ng.noScenarios") : def.Description(Loc.Lang == Language.Es);
        intro.AutowrapMode = TextServer.AutowrapMode.WordSmart;
        intro.CustomMinimumSize = new Vector2(1000, 0);
        _tabContent.AddChild(intro);

        var row = new HBoxContainer { Alignment = BoxContainer.AlignmentMode.Center };
        row.AddThemeConstantOverride("separation", 8);
        var group = new ButtonGroup();
        foreach (var (id, _) in WorldRules.Presets)
        {
            string pid = id;
            var b = UiKit.Button(Loc.T("ng.diff." + pid + ".name"), () => { _difficulty = pid; Build(); }, 17, 200);
            b.ToggleMode = true;
            b.ButtonGroup = group;
            b.ButtonPressed = pid == _difficulty;
            b.CustomMinimumSize = new Vector2(200, 40);
            b.Name = "Diff_" + pid;
            row.AddChild(b);
        }
        _tabContent.AddChild(row);

        var rules = WorldRules.Preset(_difficulty) ?? WorldRules.Normal;
        var desc = UiKit.Text(16, Gold);
        desc.Text = Loc.T("ng.diff." + _difficulty + ".desc");
        desc.AutowrapMode = TextServer.AutowrapMode.WordSmart;
        desc.HorizontalAlignment = HorizontalAlignment.Center;
        desc.CustomMinimumSize = new Vector2(1000, 0);
        _tabContent.AddChild(desc);

        var grid = new GridContainer { Columns = 4 };
        grid.AddThemeConstantOverride("h_separation", 40);
        grid.AddThemeConstantOverride("v_separation", 6);
        void Stat(string key, string value)
        {
            var k = UiKit.Text(15, Muted);
            k.Text = Loc.T(key);
            k.CustomMinimumSize = new Vector2(250, 0);
            var v = UiKit.Text(15, Colors.White);
            v.Text = value;
            v.CustomMinimumSize = new Vector2(80, 0);
            v.HorizontalAlignment = HorizontalAlignment.Right;
            grid.AddChild(k);
            grid.AddChild(v);
        }
        Stat("ng.stat.waves", rules.GoalWaves.ToString(Loc.Culture));
        Stat("ng.stat.damage", Percent(rules.EnemyDamagePercent));
        Stat("ng.stat.grace", rules.FirstWaveGraceSeconds < 0 ? Loc.T("ng.stat.contentGrace") : GoalsView.Clock(rules.FirstWaveGraceSeconds));
        Stat("ng.stat.hp", Percent(rules.EnemyHpPercent));
        Stat("ng.stat.richness", Percent(rules.DepositRichnessPercent));
        Stat("ng.stat.size", Percent(rules.WaveSizePercent));
        Stat("ng.stat.power", Percent(rules.PowerOutputPercent));
        Stat("ng.stat.interval", Percent(rules.WaveIntervalPercent));
        var centre = new CenterContainer();
        centre.AddChild(grid);
        _tabContent.AddChild(centre);

        var goals = UiKit.Text(14, Muted);
        goals.Text = Loc.T("ng.campaign.goals", rules.GoalWaves);
        goals.AutowrapMode = TextServer.AutowrapMode.WordSmart;
        goals.HorizontalAlignment = HorizontalAlignment.Center;
        goals.CustomMinimumSize = new Vector2(1000, 0);
        _tabContent.AddChild(goals);

        _tabContent.AddChild(new Control { CustomMinimumSize = new Vector2(0, 4) });
        var seedHolder = new CenterContainer();
        var seedBox = new VBoxContainer();
        seedHolder.AddChild(seedBox);
        AddSeedRow(seedBox, _campaignSeed, t => _campaignSeed = t);
        _tabContent.AddChild(seedHolder);
    }

    private static string Percent(int v) => v.ToString(Loc.Culture) + " %";

    // ---- story -----------------------------------------------------------------------------------

    private bool StoryUnlocked(string id) =>
        ScenarioStore.Progress.IsUnlocked(ScenarioKinds.Story(_catalog).Select(s => s.Id).ToList(), id);

    private void BuildStory()
    {
        var story = ScenarioKinds.Story(_catalog);
        if (story.Count == 0)
        {
            var none = UiKit.Text(16, Muted);
            none.Text = Loc.T("ng.noScenarios");
            _tabContent.AddChild(none);
            return;
        }
        var list = new VBoxContainer { SizeFlagsHorizontal = SizeFlags.ExpandFill };
        list.AddThemeConstantOverride("separation", 6);
        var hinted = new ScrollWithHint(list) { CustomMinimumSize = new Vector2(0, 446) }; // the arrow at the edge says there are more levels below
        hinted.Scroll.Name = "StoryScroll";
        _tabContent.AddChild(hinted);

        bool es = Loc.Lang == Language.Es;
        var ids = story.Select(s => s.Id).ToList();
        var progress = ScenarioStore.Progress;
        int number = 0;
        foreach (var def in story)
        {
            number++;
            string id = def.Id;
            bool unlocked = progress.IsUnlocked(ids, id);
            var record = progress.Get(id);
            bool selected = id == _storySelected;
            var card = UiKit.Button("", () =>
            {
                if (_storySelected == id && StoryUnlocked(id)) StartPressed();
                else { _storySelected = id; Build(); }
            }, 14);
            card.Name = "Level_" + id;
            card.CustomMinimumSize = new Vector2(1060, 82);
            card.Alignment = HorizontalAlignment.Left;
            var normal = UiKit.Box(selected ? new Color(0.13f, 0.2f, 0.32f, 0.97f) : new Color(0.08f, 0.1f, 0.16f, 0.95f), selected ? Gold : PanelEdge, 6, 10f);
            card.AddThemeStyleboxOverride("normal", normal);
            card.AddThemeStyleboxOverride("hover", UiKit.Box(new Color(0.16f, 0.22f, 0.34f, 0.97f), selected ? Gold : new Color(0.35f, 0.65f, 1f), 6, 10f));
            card.AddThemeStyleboxOverride("pressed", normal);
            card.AddThemeStyleboxOverride("hover_pressed", normal);

            var h = new HBoxContainer { MouseFilter = MouseFilterEnum.Ignore };
            h.AddThemeConstantOverride("separation", 14);
            h.SetAnchorsAndOffsetsPreset(LayoutPreset.FullRect, LayoutPresetMode.Minsize, 10);
            card.AddChild(h);

            var num = UiKit.Text(34, unlocked ? Gold : new Color(0.45f, 0.48f, 0.55f));
            num.Text = number.ToString(CultureInfo.InvariantCulture);
            num.CustomMinimumSize = new Vector2(36, 0);
            num.VerticalAlignment = VerticalAlignment.Center;
            h.AddChild(num);

            var mid = new VBoxContainer { SizeFlagsHorizontal = SizeFlags.ExpandFill, MouseFilter = MouseFilterEnum.Ignore };
            mid.AddThemeConstantOverride("separation", 2);
            var name = UiKit.Text(18, unlocked ? Colors.White : new Color(0.55f, 0.58f, 0.65f));
            name.Text = def.Name(es);
            mid.AddChild(name);
            // The description and the goals wrap to two lines each at a size that fits, and the card is as tall as they need (no more "..." cut).
            const float midWidth = 770f;
            var text = new FitLabel(2, 13, 11) { FitWidth = midWidth };
            text.AddThemeColorOverride("font_color", Muted);
            text.SetFit(def.Description(es));
            mid.AddChild(text);
            var goals = new FitLabel(2, 12, 11) { FitWidth = midWidth };
            goals.AddThemeColorOverride("font_color", new Color(0.75f, 0.85f, 1f));
            goals.SetFit(Loc.T("ng.story.goals") + " " + string.Join("  -  ", def.Primary.Select(g => es ? g.TextEs : g.TextEn)));
            mid.AddChild(goals);
            card.CustomMinimumSize = new Vector2(1060, Math.Max(82f, 20f + 24f + 4f + text.CustomMinimumSize.Y + goals.CustomMinimumSize.Y));
            h.AddChild(mid);

            var right = new VBoxContainer { CustomMinimumSize = new Vector2(190, 0), MouseFilter = MouseFilterEnum.Ignore };
            right.AddThemeConstantOverride("separation", 2);
            var diff = UiKit.Text(13, Muted);
            diff.Text = Loc.T("ng.story.difficulty", Loc.T("ng.diff." + (def.Rules.PresetId() ?? def.Preset) + ".name"));
            diff.HorizontalAlignment = HorizontalAlignment.Right;
            right.AddChild(diff);
            var st = UiKit.Text(14, unlocked ? (record is { Completed: true } ? Good : Colors.White) : new Color(1f, 0.55f, 0.45f));
            st.HorizontalAlignment = HorizontalAlignment.Right;
            st.Text = !unlocked ? Loc.T("ng.story.locked") : record is { Completed: true } ? Loc.T("ng.story.completed") : Loc.T("ng.story.open");
            right.AddChild(st);
            if (record is { Completed: true })
            {
                var stars = new StarRow { Stars = record.BestStars, Max = 3, CustomMinimumSize = new Vector2(66, 22), StarSize = 16 };
                var starLine = new HBoxContainer { Alignment = BoxContainer.AlignmentMode.End, MouseFilter = MouseFilterEnum.Ignore };
                starLine.AddThemeConstantOverride("separation", 8);
                starLine.AddChild(stars);
                var best = UiKit.Text(13, Muted);
                best.Text = Loc.T("ng.story.best", PlayLog.Format(record.BestSeconds));
                best.HorizontalAlignment = HorizontalAlignment.Right;
                starLine.AddChild(best);
                right.AddChild(starLine);
            }
            h.AddChild(right);
            list.AddChild(card);
        }
        var hint = UiKit.Text(12, Muted);
        hint.Text = Loc.T("ng.story.hint");
        _tabContent.AddChild(hint);
    }

    // ---- sandbox ---------------------------------------------------------------------------------

    private void BuildSandbox()
    {
        var intro = UiKit.Text(14, Muted);
        intro.Text = Loc.T("ng.sandbox.intro");
        intro.AutowrapMode = TextServer.AutowrapMode.WordSmart;
        intro.CustomMinimumSize = new Vector2(1060, 0);
        _tabContent.AddChild(intro);

        var cols = new HBoxContainer();
        cols.AddThemeConstantOverride("separation", 36);
        _tabContent.AddChild(cols);
        var left = new VBoxContainer { CustomMinimumSize = new Vector2(520, 0) };
        left.AddThemeConstantOverride("separation", 5);
        var right = new VBoxContainer { CustomMinimumSize = new Vector2(520, 0) };
        right.AddThemeConstantOverride("separation", 5);
        cols.AddChild(left);
        cols.AddChild(right);

        // Left: switches and the rule sliders.
        Check(left, "ng.sb.free", _sandbox.FreeBuild, on => _sandbox = _sandbox with { FreeBuild = on }, "FreeCheck", "ng.sb.free.tip");
        Check(left, "ng.sb.threats", _sandbox.Threats, on => _sandbox = _sandbox with { Threats = on }, "ThreatsCheck", "ng.sb.threats.tip");
        Slider(left, "ng.sb.damage", _sandbox.EnemyDamagePercent, WorldRules.MinPercent, WorldRules.MaxPercent, 5, v => Percent((int)v), v => _sandbox = _sandbox with { EnemyDamagePercent = (int)v }, "DamageSlider");
        Slider(left, "ng.sb.hp", _sandbox.EnemyHpPercent, WorldRules.MinPercent, WorldRules.MaxPercent, 5, v => Percent((int)v), v => _sandbox = _sandbox with { EnemyHpPercent = (int)v }, "HpSlider");
        Slider(left, "ng.sb.size", _sandbox.WaveSizePercent, WorldRules.MinPercent, WorldRules.MaxPercent, 5, v => Percent((int)v), v => _sandbox = _sandbox with { WaveSizePercent = (int)v }, "SizeSlider");
        Slider(left, "ng.sb.interval", _sandbox.WaveIntervalPercent, WorldRules.MinPercent, WorldRules.MaxPercent, 5, v => Percent((int)v), v => _sandbox = _sandbox with { WaveIntervalPercent = (int)v }, "IntervalSlider");
        Slider(left, "ng.sb.grace", _sandbox.FirstWaveGraceSeconds < 0 ? -30 : _sandbox.FirstWaveGraceSeconds, -30, WorldRules.MaxGraceSeconds, 30,
            v => v < 0 ? Loc.T("ng.stat.contentGrace") : GoalsView.Clock((int)v), v => _sandbox = _sandbox with { FirstWaveGraceSeconds = v < 0 ? WorldRules.ContentGrace : (int)v }, "GraceSlider");
        Slider(left, "ng.sb.richness", _sandbox.DepositRichnessPercent, WorldRules.MinRichness, WorldRules.MaxRichness, 5, v => Percent((int)v), v => _sandbox = _sandbox with { DepositRichnessPercent = (int)v }, "RichnessSlider");
        Slider(left, "ng.sb.power", _sandbox.PowerOutputPercent, WorldRules.MinPercent, WorldRules.MaxPercent, 5, v => Percent((int)v), v => _sandbox = _sandbox with { PowerOutputPercent = (int)v }, "PowerSlider");
        Slider(left, "ng.sb.goal", _sandbox.GoalWaves, WorldRules.MinGoalWaves, WorldRules.MaxGoalWaves, 1, v => ((int)v).ToString(Loc.Culture), v => _sandbox = _sandbox with { GoalWaves = (int)v }, "GoalSlider");

        // Right: start type, seed, starting items, presets.
        var startLabel = UiKit.Text(15, Gold);
        startLabel.Text = Loc.T("ng.sb.start");
        right.AddChild(startLabel);
        var startRow = new HBoxContainer();
        startRow.AddThemeConstantOverride("separation", 6);
        var group = new ButtonGroup();
        foreach (var (start, key) in new[] { (SandboxStart.Ship, "ng.sb.start.ship"), (SandboxStart.ShipAndStation, "ng.sb.start.station"), (SandboxStart.ShipAndSiege, "ng.sb.start.siege") })
        {
            var s = start;
            var b = UiKit.Button(Loc.T(key), () => _sandbox = _sandbox with { Start = s }, 13, 160);
            b.ToggleMode = true;
            b.ButtonGroup = group;
            b.ButtonPressed = _sandbox.Start == s;
            b.CustomMinimumSize = new Vector2(166, 34);
            b.Name = "Start_" + s;
            startRow.AddChild(b);
        }
        right.AddChild(startRow);
        AddSeedRow(right, _sandboxSeed, t => _sandboxSeed = t);

        var itemsLabel = UiKit.Text(15, Gold);
        itemsLabel.Text = Loc.T("ng.sb.items");
        right.AddChild(itemsLabel);
        var grid = new GridContainer { Columns = 4 };
        grid.AddThemeConstantOverride("h_separation", 10);
        grid.AddThemeConstantOverride("v_separation", 4);
        foreach (string itemId in SandboxSettings.OfferedItems.Where(i => _content.Items.Any(d => d.Id == i)))
        {
            string id = itemId;
            var name = UiKit.Text(13, Colors.White);
            name.Text = Loc.Name("item", id);
            name.CustomMinimumSize = new Vector2(112, 0);
            var spin = new SpinBox { MinValue = 0, MaxValue = SandboxSettings.MaxCount, Step = 10, Value = _sandbox.Items.FirstOrDefault(i => i.Item == id)?.Count ?? 0, CustomMinimumSize = new Vector2(112, 0), Name = "Item_" + id.Replace(':', '_') };
            spin.GetLineEdit().AddThemeFontSizeOverride("font_size", 13);
            spin.ValueChanged += v =>
            {
                var items = _sandbox.Items.Where(i => i.Item != id).ToList();
                if (v >= 1) items.Add(new SandboxItem(id, (int)v));
                _sandbox = _sandbox with { Items = items };
            };
            grid.AddChild(name);
            grid.AddChild(spin);
        }
        right.AddChild(grid);

        BuildPresetRow(right);
    }

    private void BuildPresetRow(Control parent)
    {
        var label = UiKit.Text(15, Gold);
        label.Text = Loc.T("ng.sb.presets");
        parent.AddChild(label);
        var presets = ScenarioStore.Presets;
        var row = new HBoxContainer();
        row.AddThemeConstantOverride("separation", 6);
        var pick = new OptionButton { CustomMinimumSize = new Vector2(200, 34), Name = "PresetPick" };
        pick.AddThemeFontSizeOverride("font_size", 14);
        foreach (var p in presets.All) pick.AddItem(p.Name);
        if (presets.All.Count == 0) { pick.AddItem(Loc.T("ng.sb.noPresets")); pick.Disabled = true; }
        row.AddChild(pick);
        var load = UiKit.Button(Loc.T("ng.sb.load"), () =>
        {
            if (presets.All.Count == 0 || pick.Selected < 0) return;
            var p = presets.All[pick.Selected];
            _sandbox = p.Settings;
            _sandboxSeed = p.Settings.Seed == 0 ? "" : p.Settings.Seed.ToString(CultureInfo.InvariantCulture);
            _presetName = p.Name;
            Build();
            _status.Text = Loc.T("ng.sb.loaded", p.Name);
        }, 14);
        load.Name = "LoadPreset";
        load.Disabled = presets.All.Count == 0;
        var del = UiKit.Button(Loc.T("ng.sb.delete"), () =>
        {
            if (presets.All.Count == 0 || pick.Selected < 0) return;
            string name = presets.All[pick.Selected].Name;
            presets.Remove(name);
            ScenarioStore.SavePresets();
            Build();
            _status.Text = Loc.T("ng.sb.deleted", name);
        }, 14);
        del.Name = "DeletePreset";
        del.Disabled = presets.All.Count == 0;
        row.AddChild(load);
        row.AddChild(del);
        parent.AddChild(row);

        var saveRow = new HBoxContainer();
        saveRow.AddThemeConstantOverride("separation", 6);
        var nameEdit = new LineEdit { Text = _presetName, PlaceholderText = Loc.T("ng.sb.presetName"), MaxLength = SandboxPresets.MaxNameLength, CustomMinimumSize = new Vector2(200, 34), Name = "PresetName" };
        nameEdit.AddThemeFontSizeOverride("font_size", 14);
        nameEdit.TextChanged += t => _presetName = t;
        var save = UiKit.Button(Loc.T("ng.sb.save"), () =>
        {
            var settings = _sandbox with { Seed = ParseSeed(_sandboxSeed) };
            if (SandboxPresets.CleanName(_presetName) is not { } clean) { _status.Text = Loc.T("ng.sb.badName"); return; }
            if (!presets.Save(clean, settings)) { _status.Text = Loc.T("ng.sb.full"); return; }
            ScenarioStore.SavePresets();
            _presetName = clean;
            Build();
            _status.Text = Loc.T("ng.sb.saved", clean);
        }, 14);
        save.Name = "SavePreset";
        saveRow.AddChild(nameEdit);
        saveRow.AddChild(save);
        parent.AddChild(saveRow);
    }

    // ---- form controls ---------------------------------------------------------------------------

    private void Check(Control parent, string textKey, bool value, Action<bool> changed, string name, string tipKey)
    {
        string text = Loc.T(textKey);
        string Label(bool on) => (on ? "[x]  " : "[  ]  ") + text;
        var check = UiKit.Button(Label(value), () => { }, 14);
        check.Name = name;
        check.ToggleMode = true;
        check.ButtonPressed = value;
        check.Alignment = HorizontalAlignment.Left;
        check.TooltipText = Loc.T(tipKey);
        check.Toggled += on => { check.Text = Label(on); changed(on); };
        parent.AddChild(check);
    }

    private void Slider(Control parent, string textKey, double value, double min, double max, double step, Func<double, string> format, Action<double> changed, string name)
    {
        var row = new HBoxContainer();
        row.AddThemeConstantOverride("separation", 10);
        var label = UiKit.Text(14);
        label.CustomMinimumSize = new Vector2(250, 0);
        string Text(double v) => Loc.T(textKey) + ": " + format(v);
        label.Text = Text(value);
        row.AddChild(label);
        var slider = new HSlider { Name = name, MinValue = min, MaxValue = max, Step = step, Value = value, FocusMode = FocusModeEnum.All, SizeFlagsHorizontal = SizeFlags.ExpandFill, CustomMinimumSize = new Vector2(220, 24) };
        slider.AddThemeStyleboxOverride("slider", UiKit.Box(new Color(0.22f, 0.27f, 0.38f), null, 3, 3f));
        slider.AddThemeStyleboxOverride("grabber_area", UiKit.Box(new Color(0.35f, 0.65f, 1f), null, 3, 3f));
        slider.AddThemeStyleboxOverride("grabber_area_highlight", UiKit.Box(new Color(0.55f, 0.8f, 1f), null, 3, 3f));
        slider.ValueChanged += v => { label.Text = Text(v); changed(v); };
        row.AddChild(slider);
        parent.AddChild(row);
    }
}

/// <summary>A row of stars drawn as polygons (the default font has no star glyph).</summary>
public partial class StarRow : Control
{
    public int Stars { get; set; }
    public int Max { get; set; } = 3;
    public float StarSize { get; set; } = 18f;
    public HorizontalAlignment Align { get; set; } = HorizontalAlignment.Left;

    public StarRow() { MouseFilter = MouseFilterEnum.Ignore; }

    public override void _Draw()
    {
        float step = StarSize + 4f;
        float total = Max * step - 4f;
        float x0 = Align == HorizontalAlignment.Right ? Math.Max(0, GetRect().Size.X - total) : Align == HorizontalAlignment.Center ? Math.Max(0, (GetRect().Size.X - total) / 2f) : 0f;
        for (int i = 0; i < Max; i++)
        {
            var c = new Vector2(x0 + i * step + StarSize / 2f, StarSize / 2f + 1f);
            var pts = new Vector2[10];
            for (int k = 0; k < 10; k++)
            {
                float a = -MathF.PI / 2f + k * MathF.PI / 5f;
                float r = (k % 2 == 0 ? StarSize / 2f : StarSize / 4.3f);
                pts[k] = c + new Vector2(MathF.Cos(a), MathF.Sin(a)) * r;
            }
            bool on = i < Stars;
            DrawColoredPolygon(pts, on ? new Color(1f, 0.82f, 0.25f) : new Color(0.2f, 0.23f, 0.3f));
            DrawPolyline(pts.Append(pts[0]).ToArray(), on ? new Color(1f, 0.93f, 0.6f) : new Color(0.4f, 0.45f, 0.55f), 1.2f);
        }
    }
}
