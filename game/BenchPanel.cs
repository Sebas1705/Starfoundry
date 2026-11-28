using System;
using System.Collections.Generic;
using System.Linq;
using Godot;
using Starfoundry.Sim;
using Starfoundry.Ui;

namespace Starfoundry.Game;

/// <summary>
/// The ship workbench panel (ADR 0039): F on the workbench opens it in a station-first world. It lists the three recipes (what they take, make and how long a craft
/// lasts) with buttons that queue one craft or fill the queue, shows the queue (up to five waiting), the craft in progress with a progress bar, the input and output
/// slots (what the hold has of the inputs, what the craft makes) and says plainly that the work is manual and slow. The logic is in <see cref="BenchView"/>; this class is
/// the widgets. Layout: one column, 520 logical pixels wide at most and about 400 tall, which fits 1280x720 with room for the key panel.
/// </summary>
public partial class BenchPanel : PanelContainer
{
    private const float Width = 520f;
    private const int Slot = 44;

    private readonly Func<BeltNetwork> _net;
    private readonly Func<int> _player;
    private readonly ContentDb _content;
    private readonly Action<Command> _send;
    private readonly Func<bool> _stationFirst;

    private Label _title = null!, _slow = null!, _recipesHead = null!, _progressLabel = null!, _status = null!, _inLabel = null!, _outLabel = null!, _queueHead = null!, _queueEmpty = null!, _hint = null!;
    private GaugeBar _bar = null!;
    private Button _close = null!, _clear = null!;
    private readonly List<RecipeRow> _rows = new();
    private readonly InventorySlot[] _inSlots = new InventorySlot[2];
    private InventorySlot _outSlot = null!;
    private readonly InventorySlot[] _queueSlots = new InventorySlot[PlayerRules.BenchQueueMax];

    /// <summary>The player closed the panel (button, F or Esc handled by the game).</summary>
    public event Action? CloseRequested;

    private sealed class RecipeRow
    {
        public BenchRecipeRow Data = null!;
        public IconRect Icon = null!;
        public Label Name = null!, Time = null!, Arrow = null!;
        public readonly List<IconCount> Takes = new(), Makes = new();
        public Button One = null!, Five = null!;
    }

    public BenchPanel(Func<BeltNetwork> net, Func<int> player, ContentDb content, Action<Command> send, Func<bool> stationFirst)
    {
        _net = net;
        _player = player;
        _content = content;
        _send = send;
        _stationFirst = stationFirst;
        Name = "BenchPanel";
        Visible = false;
        MouseFilter = MouseFilterEnum.Stop;
        AddThemeStyleboxOverride("panel", UiTheme.Dock());
        SetMeta(UiAudit.ModalMeta, true);
        Build();
    }

    private static Label Text(int size, Color? colour = null, bool wrap = false)
    {
        var l = UiKit.Text(size, colour ?? UiTheme.Text);
        if (wrap) { l.AutowrapMode = TextServer.AutowrapMode.WordSmart; l.CustomMinimumSize = new Vector2(10, 0); l.SizeFlagsHorizontal = SizeFlags.ExpandFill; }
        return l;
    }

    private void Build()
    {
        var root = new VBoxContainer();
        root.AddThemeConstantOverride("separation", UiTheme.Gap);
        AddChild(root);

        var head = new HBoxContainer();
        head.AddThemeConstantOverride("separation", 8);
        head.AddChild(new IconRect("tool:refine", 26, UiTheme.Gold, tooltip: false));
        _title = Text(UiTheme.Title, Colors.White);
        _title.SizeFlagsHorizontal = SizeFlags.ExpandFill;
        _title.ClipText = true;
        _title.TextOverrunBehavior = TextServer.OverrunBehavior.TrimEllipsis;
        _title.CustomMinimumSize = new Vector2(10, 0);
        head.AddChild(_title);
        _close = UiKit.Button("", () => CloseRequested?.Invoke(), 13);
        _close.Icon = Icons.Get("tool:close", 16);
        _close.ExpandIcon = false;
        _close.FocusMode = FocusModeEnum.None;
        _close.AddThemeColorOverride("icon_normal_color", UiTheme.Muted);
        head.AddChild(_close);
        root.AddChild(head);

        _slow = Text(UiTheme.Body, UiTheme.Warn, wrap: true);
        root.AddChild(_slow);
        root.AddChild(UiTheme.Rule());

        _recipesHead = Text(UiTheme.Heading, UiTheme.Gold);
        root.AddChild(_recipesHead);
        foreach (var data in BenchView.Rows(_content, true, _ => 0)) root.AddChild(BuildRow(data));
        root.AddChild(UiTheme.Rule());

        // the craft in progress
        _progressLabel = Text(UiTheme.Body, wrap: true);
        root.AddChild(_progressLabel);
        _bar = new GaugeBar(120f, 10f);
        root.AddChild(_bar);
        _status = Text(UiTheme.Small, UiTheme.Muted, wrap: true);
        root.AddChild(_status);

        // input and output slots
        var slots = new HBoxContainer();
        slots.AddThemeConstantOverride("separation", 6);
        _inLabel = Text(UiTheme.Small, UiTheme.Muted);
        slots.AddChild(_inLabel);
        for (int i = 0; i < _inSlots.Length; i++) { _inSlots[i] = new InventorySlot(Slot); slots.AddChild(_inSlots[i]); }
        var gap = new Control { CustomMinimumSize = new Vector2(14, 0) };
        slots.AddChild(gap);
        _outLabel = Text(UiTheme.Small, UiTheme.Muted);
        slots.AddChild(_outLabel);
        _outSlot = new InventorySlot(Slot);
        slots.AddChild(_outSlot);
        root.AddChild(slots);

        // the queue
        var queueHead = new HBoxContainer();
        queueHead.AddThemeConstantOverride("separation", 8);
        _queueHead = Text(UiTheme.Heading, UiTheme.Gold);
        _queueHead.SizeFlagsHorizontal = SizeFlags.ExpandFill;
        queueHead.AddChild(_queueHead);
        _clear = UiKit.Button("", () => _send(BenchView.ClearCommand(_stationFirst())), 13);
        _clear.FocusMode = FocusModeEnum.None;
        queueHead.AddChild(_clear);
        root.AddChild(queueHead);
        var queue = new HBoxContainer();
        queue.AddThemeConstantOverride("separation", 6);
        for (int i = 0; i < _queueSlots.Length; i++) { _queueSlots[i] = new InventorySlot(Slot); queue.AddChild(_queueSlots[i]); }
        _queueEmpty = Text(UiTheme.Body, UiTheme.Dim);
        queue.AddChild(_queueEmpty);
        root.AddChild(queue);

        _hint = Text(UiTheme.Small, UiTheme.Dim);
        root.AddChild(_hint);
    }

    private Control BuildRow(BenchRecipeRow data)
    {
        var row = new RecipeRow { Data = data };
        var def = _content.Recipes[data.Recipe];
        var box = new HBoxContainer();
        box.AddThemeConstantOverride("separation", 8);
        row.Icon = new IconRect(Icons.ItemId(_content.Items[data.Outputs[0].Item].Id), 28, null, tooltip: false);
        box.AddChild(row.Icon);

        var mid = new VBoxContainer { SizeFlagsHorizontal = SizeFlags.ExpandFill };
        mid.AddThemeConstantOverride("separation", 2);
        row.Name = Text(UiTheme.Body, UiTheme.Text);
        row.Name.ClipText = true;
        row.Name.TextOverrunBehavior = TextServer.OverrunBehavior.TrimEllipsis;
        row.Name.CustomMinimumSize = new Vector2(10, 0);
        mid.AddChild(row.Name);
        var io = new HBoxContainer();
        io.AddThemeConstantOverride("separation", 4);
        foreach (var s in data.Inputs)
        {
            var c = new IconCount(Icons.ItemId(_content.Items[s.Item].Id), 18, UiTheme.Small);
            row.Takes.Add(c);
            io.AddChild(c);
        }
        row.Arrow = Text(UiTheme.Small, UiTheme.Dim);
        row.Arrow.Text = ">";
        io.AddChild(row.Arrow);
        foreach (var s in data.Outputs)
        {
            var c = new IconCount(Icons.ItemId(_content.Items[s.Item].Id), 18, UiTheme.Small);
            row.Makes.Add(c);
            io.AddChild(c);
        }
        mid.AddChild(io);
        box.AddChild(mid);

        row.Time = Text(UiTheme.Small, UiTheme.Muted);
        row.Time.HorizontalAlignment = HorizontalAlignment.Right;
        row.Time.CustomMinimumSize = new Vector2(40, 0);
        box.AddChild(row.Time);
        row.One = UiKit.Button(Loc.T("bench.panel.queue1"), () => Queue(row, 1), 13, 44);
        row.Five = UiKit.Button("", () => Queue(row, PlayerRules.BenchQueueMax), 13, 44);
        row.One.FocusMode = row.Five.FocusMode = FocusModeEnum.None;
        box.AddChild(row.One);
        box.AddChild(row.Five);
        _rows.Add(row);
        _ = def;
        return box;
    }

    private void Queue(RecipeRow row, int count)
    {
        var info = _net().GetPlayerInfo(_player());
        int add = BenchView.Addable(info.BenchQueue.Count, count);
        if (add <= 0) return;
        _send(BenchView.QueueCommand(_stationFirst(), row.Data.Recipe, add));
    }

    private static void SetText(Label l, string text)
    {
        if (l.Text != text) l.Text = text;
    }

    /// <summary>Places the panel centred on the screen, clear of the key panel's rows at the bottom; <paramref name="size"/> is the viewport.</summary>
    public void Layout(Vector2 size, float bottom)
    {
        float w = Math.Min(Width, size.X - 32f);
        CustomMinimumSize = new Vector2(w, 0);
        ResetSize();
        var min = GetCombinedMinimumSize();
        float h = Math.Min(min.Y, Math.Max(240f, bottom - 16f));
        Size = new Vector2(w, h);
        Position = new Vector2(Math.Max(8f, (size.X - w) / 2f), Math.Max(8f, Math.Min((bottom - h) / 2f, size.Y - h - 8f)));
    }

    public void Open()
    {
        Visible = true;
        Refresh();
        UiFx.Present(this);
    }

    public void Close() => Visible = false;

    /// <summary>Re-reads the world: queue, progress, slots and the buttons' states. A few times a second is plenty.</summary>
    public void Refresh()
    {
        if (!Visible) return;
        var net = _net();
        int player = _player();
        if (!net.HasPlayer(player)) return;
        bool sf = _stationFirst();
        var info = net.GetPlayerInfo(player);
        int queued = info.BenchQueue.Count;

        SetText(_title, Loc.T("bench.panel.title"));
        int seconds = _rows.Count > 0 ? _rows[0].Data.Seconds : 0;
        SetText(_slow, BenchView.SlowLabel(seconds));
        SetText(_recipesHead, Loc.T("bench.panel.recipes"));
        _close.TooltipText = Loc.T("bench.panel.close");

        foreach (var row in _rows)
        {
            var r = _content.Recipes[row.Data.Recipe];
            SetText(row.Name, Loc.T("bench." + row.Data.Id).ToUpperInvariantFirst());
            for (int i = 0; i < row.Data.Inputs.Length; i++)
            {
                var s = row.Data.Inputs[i];
                row.Takes[i].Set(Icons.ItemId(_content.Items[s.Item].Id), net.HoldCount(player, s.Item), s.Count);
            }
            for (int i = 0; i < row.Data.Outputs.Length; i++)
            {
                var s = row.Data.Outputs[i];
                row.Makes[i].Set(Icons.ItemId(_content.Items[s.Item].Id), s.Count);
            }
            SetText(row.Time, Loc.T("bench.panel.seconds", BenchView.CraftSeconds(r, sf)));
            int room = BenchView.Room(queued);
            string tip = room == 0 ? Loc.T("bench.panel.queueFull") : Loc.T("bench.panel.queueTip", PlayerRules.BenchQueueMax);
            row.One.Disabled = row.Five.Disabled = room == 0;
            row.One.TooltipText = row.Five.TooltipText = tip;
            row.Five.Text = Loc.T("bench.panel.queue5", PlayerRules.BenchQueueMax);
            if (!row.Data.CanStart && room > 0) row.One.TooltipText = row.Five.TooltipText = tip + "\n" + Loc.T("bench.panel.noInputs");
        }

        // The recipe the bench is on: the craft in progress, otherwise the next one waiting.
        int current = info.ShipCrafting ? info.ShipRecipe : queued > 0 ? info.BenchQueue[0] : -1;
        var cur = current >= 0 ? _content.Recipes[current] : null;
        bool hasInputs = cur is null || cur.Inputs.All(i => net.HoldCount(player, i.Item) >= i.Count);
        int delta = cur is null ? 0 : cur.Outputs.Sum(o => o.Count) - cur.Inputs.Sum(i => i.Count);
        bool room2 = cur is null || net.HoldTotal(player) + delta <= net.HoldCapacity(player);
        var state = BenchView.State(info.ShipCrafting, queued, hasInputs, room2);
        float progress = cur is null ? 0f : BenchView.Progress(info.ShipCrafting, info.ShipRemaining, BenchView.CraftTicks(cur, sf));
        SetText(_progressLabel, cur is null ? Loc.T("bench.panel.idle") : Loc.T("bench.panel.inProgress", Loc.T("bench." + cur.Id)));
        _bar.Set(progress, state is BenchState.WaitingInputs or BenchState.NoRoom ? UiTheme.Warn : UiTheme.Good);
        SetText(_status, cur is null ? "" : Loc.T(BenchView.StateKey(state)) + (info.ShipCrafting ? " " + (int)MathF.Round(progress * 100f) + " %" : ""));
        _status.AddThemeColorOverride("font_color", state is BenchState.WaitingInputs or BenchState.NoRoom ? UiTheme.Warn : UiTheme.Muted);

        SetText(_inLabel, Loc.T("bench.panel.inSlot"));
        SetText(_outLabel, Loc.T("bench.panel.outSlot"));
        for (int i = 0; i < _inSlots.Length; i++)
        {
            if (cur is not null && i < cur.Inputs.Length)
            {
                var s = cur.Inputs[i];
                var item = _content.Items[s.Item];
                int have = net.HoldCount(player, s.Item);
                _inSlots[i].Set(s.Item, have, 0, item.Id, $"{Loc.Name("item", item.Id)}: {Loc.T("bench.panel.hold", have)} ({s.Count})", dim: have < s.Count);
            }
            else _inSlots[i].Set(-1, 0, 0, "", "", false);
        }
        if (cur is not null)
        {
            var o = cur.Outputs[0];
            var item = _content.Items[o.Item];
            _outSlot.Set(o.Item, o.Count, 0, item.Id, $"{Loc.Name("item", item.Id)} x{o.Count}", false);
        }
        else _outSlot.Set(-1, 0, 0, "", "", false);

        SetText(_queueHead, Loc.T("bench.panel.queueTitle", queued, PlayerRules.BenchQueueMax));
        _clear.Text = Loc.T("bench.panel.clear");
        _clear.TooltipText = Loc.T("bench.panel.clearTip");
        _clear.Disabled = queued == 0 && !info.ShipCrafting;
        for (int i = 0; i < _queueSlots.Length; i++)
        {
            if (i < queued)
            {
                var r = _content.Recipes[info.BenchQueue[i]];
                var item = _content.Items[r.Outputs[0].Item];
                _queueSlots[i].Set(r.Outputs[0].Item, r.Outputs[0].Count, 0, item.Id, Loc.T("bench." + r.Id) + (i == 0 && !info.ShipCrafting ? "" : ""), false);
            }
            else _queueSlots[i].Set(-1, 0, 0, "", "", false);
        }
        SetText(_queueEmpty, queued == 0 ? Loc.T("bench.panel.queueEmpty") : "");
        SetText(_hint, Loc.T("bench.panel.hint"));
    }
}

internal static class BenchText
{
    /// <summary>Upper-cases the first letter ("ingots" becomes "Ingots").</summary>
    public static string ToUpperInvariantFirst(this string s) => s.Length == 0 ? s : char.ToUpperInvariant(s[0]) + s[1..];
}
