namespace Starfoundry.Ui;

/// <summary>What a notification is about; each category has its own colour in the HUD.</summary>
public enum ToastCategory
{
    Info,
    Production,
    Logistics,
    Threat,
}

/// <summary>A notification as shown: the text, how many times it repeated and its category.</summary>
public readonly record struct ToastView(string Text, int Count, ToastCategory Category)
{
    /// <summary>The text with a repeat count appended ("x3") when it was raised more than once.</summary>
    public string Display => Count > 1 ? $"{Text}  x{Count}" : Text;
}

/// <summary>
/// The notifications of the HUD: at most <see cref="MaxVisible"/> at a time (the newest), identical ones grouped into one
/// with a repeat count, each expiring on its own. The caller supplies the clock so the logic is testable. Engine-free.
/// </summary>
public sealed class ToastQueue
{
    public const int MaxVisible = 3;

    private sealed class Entry
    {
        public string Text = "";
        public ToastCategory Category;
        public int Count;
        public double Until;
        public long Order; // raising order: the newest are kept on screen
    }

    private readonly List<Entry> _entries = new();
    private long _order;

    /// <summary>Raises a notification. A live one with the same text and category is refreshed and counted instead of repeated.</summary>
    public void Add(string text, ToastCategory category, double now, double seconds)
    {
        foreach (var e in _entries)
        {
            if (e.Until < now || e.Category != category || e.Text != text) continue;
            e.Count++;
            e.Until = Math.Max(e.Until, now + seconds);
            e.Order = _order++;
            return;
        }
        _entries.Add(new Entry { Text = text, Category = category, Count = 1, Until = now + seconds, Order = _order++ });
    }

    /// <summary>The notifications to show at <paramref name="now"/>, oldest first, never more than <see cref="MaxVisible"/>.</summary>
    public IReadOnlyList<ToastView> Visible(double now)
    {
        _entries.RemoveAll(e => e.Until < now);
        return _entries.OrderByDescending(e => e.Order).Take(MaxVisible).Reverse()
            .Select(e => new ToastView(e.Text, e.Count, e.Category)).ToList();
    }

    public void Clear() => _entries.Clear();
}
