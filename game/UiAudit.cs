using System;
using System.Collections.Generic;
using System.Linq;
using Godot;

namespace Starfoundry.Game;

/// <summary>
/// The text-fit audit behind <c>--ui-audit</c>: walks the visible controls of a scene tree and reports text that does not fit (a label wider or
/// taller than its box, a button text wider than the button, lines hidden by a line limit), controls that leave their parent or the viewport,
/// text panels that overlap each other and HUD cards that touch the screen edge. A control that is scrolled out of its scroll container is not a
/// problem; a cut-off text that has the full text as a tooltip is reported as <c>ELLIPSIS</c> (accepted), anything else as an error.
/// </summary>
public static class UiAudit
{
    public sealed record Finding(string Kind, string Where, string Text, string Detail, bool Error)
    {
        public override string ToString() => $"{(Error ? "FAIL" : "note")} {Kind} {Where} \"{Text}\" {Detail}";
    }

    /// <summary>Distance a HUD card keeps from the screen edge (logical pixels).</summary>
    public const float SafeMargin = 4f;

    /// <summary>Meta flag of a modal overlay (the help page): it is meant to cover the HUD, so overlapping it is not a finding.</summary>
    public const string ModalMeta = "audit_modal";

    public static List<Finding> Run(Node root, Rect2 viewport)
    {
        var found = new List<Finding>();
        foreach (var top in root.GetChildren().OfType<Node>()) Walk(top, viewport, found, null, false);
        return found.GroupBy(f => (f.Kind, f.Where, f.Text)).Select(g => g.First()).ToList();
    }

    private static void Walk(Node node, Rect2 viewport, List<Finding> found, Rect2? clip, bool parentBad)
    {
        if (node is CanvasItem { Visible: false }) return;
        if (node is Window) return;
        Rect2? childClip = clip;
        bool bad = parentBad;
        if (node is Control c)
        {
            var rect = c.GetGlobalRect();
            if (c.Size.X > 1f && c.Size.Y > 1f && (clip is null || clip.Value.Intersects(rect)) && c.Modulate.A > 0.05f && c.SelfModulate.A > 0.05f)
            {
                bad = Check(c, rect, viewport, clip, found) || parentBad;
                if (c.ClipContents || c is ScrollContainer) childClip = clip is { } o ? o.Intersection(rect) : rect;
            }
            else return; // scrolled away or collapsed: nothing to check inside
        }
        foreach (var child in node.GetChildren()) Walk(child, viewport, found, childClip, bad);
    }

    private static string Path(Control c)
    {
        var parts = new List<string>();
        Node? n = c;
        for (int i = 0; i < 4 && n is not null && n is not Window; i++, n = n.GetParent())
            parts.Add(n.Name.ToString().StartsWith('@') ? n.GetType().Name : n.Name.ToString());
        parts.Reverse();
        return string.Join("/", parts);
    }

    private static string Snip(string t) => (t.Replace('\n', ' ').Length > 40 ? t.Replace('\n', ' ')[..40] + "..." : t.Replace('\n', ' '));

    private static bool HasTooltip(Control c)
    {
        for (Node? n = c; n is Control k; n = n.GetParent()) if (k.TooltipText.Length > 0) return true;
        return false;
    }

    private static bool InScroll(Control c)
    {
        for (Node? n = c.GetParent(); n is not null; n = n.GetParent()) if (n is ScrollContainer) return true;
        return false;
    }

    /// <summary>Checks one control; true when it was reported as an error (the controls inside it are then not reported again).</summary>
    private static bool Check(Control c, Rect2 rect, Rect2 viewport, Rect2? clip, List<Finding> found)
    {
        bool error = false;
        void Add(string kind, string text, string detail, bool isError = true)
        {
            found.Add(new Finding(kind, Path(c), Snip(text), detail, isError));
            error |= isError;
        }

        // ---- text that does not fit its own box ----
        if (c is Label l && l.Text.Length > 0 && l.GetVisibleCharacters() != 0)
        {
            var font = l.GetThemeFont("font") ?? UiFit.DefaultFont;
            int size = l.GetThemeFontSize("font_size");
            if (l.AutowrapMode == TextServer.AutowrapMode.Off)
            {
                float w = l.Text.Split((char)10).Max(line => font.GetStringSize(line, l.HorizontalAlignment, -1, size).X);
                bool cuts = l.ClipText || l.TextOverrunBehavior != TextServer.OverrunBehavior.NoTrimming;
                if (w > l.Size.X + 0.75f)
                {
                    if (cuts) Add(HasTooltip(l) ? "ELLIPSIS" : "CLIPPED", l.Text, $"text {w:0} > box {l.Size.X:0}", !HasTooltip(l));
                    else Add("OVERFLOW", l.Text, $"text {w:0} > box {l.Size.X:0}");
                }
            }
            else
            {
                int lines = l.GetLineCount();
                float need = lines * (font.GetHeight(size) + UiFit.LineSpacing);
                if (l.MaxLinesVisible > 0 && lines > l.MaxLinesVisible)
                    Add(HasTooltip(l) ? "ELLIPSIS" : "LINES_HIDDEN", l.Text, $"{lines} lines > {l.MaxLinesVisible}", !HasTooltip(l));
                else if (need - UiFit.LineSpacing > l.Size.Y + 1.5f && !InScroll(l) && !(l is FitLabel))
                    Add("TALL", l.Text, $"{lines} lines need {need:0} > box {l.Size.Y:0}");
                else if (l is FitLabel { Truncated: true })
                    Add("ELLIPSIS", l.Text, $"min size {size}", !HasTooltip(l));
                // a word wider than the box is broken in the middle
                float widest = l.Text.Split(' ', '\n').Select(word => font.GetStringSize(word, HorizontalAlignment.Left, -1, size).X).DefaultIfEmpty(0f).Max();
                if (widest > l.Size.X + 0.75f) Add("WORD_BROKEN", l.Text, $"word {widest:0} > box {l.Size.X:0}");
            }
        }
        else if (c is Button b && b.Text.Length > 0)
        {
            var font = b.GetThemeFont("font") ?? UiFit.DefaultFont;
            int size = b.GetThemeFontSize("font_size");
            float iconW = b.Icon is { } ic ? Math.Min(ic.GetWidth(), b.GetThemeConstant("icon_max_width") is > 0 and var m ? m : 999) + b.GetThemeConstant("h_separation") : 0f;
            float inner = b.Size.X - (b.GetThemeStylebox("normal")?.GetMinimumSize().X ?? 0f);
            float w = font.GetStringSize(b.Text, HorizontalAlignment.Left, -1, size).X + iconW;
            if (w > inner + 0.75f)
            {
                bool cuts = b.ClipText || b.TextOverrunBehavior != TextServer.OverrunBehavior.NoTrimming;
                if (cuts) Add(HasTooltip(b) ? "ELLIPSIS" : "CLIPPED", b.Text, $"text {w:0} > inner {inner:0}", !HasTooltip(b));
                else Add("OVERFLOW", b.Text, $"text {w:0} > inner {inner:0}");
            }
        }

        // ---- a control that does not fit its parent or the screen ----
        if (c.GetParent() is Control p && p is not ScrollContainer && c.TopLevel == false && !InScrollContent(c))
        {
            var pr = p.GetGlobalRect();
            if (p is Container && (rect.Position.X < pr.Position.X - 1.5f || rect.End.X > pr.End.X + 1.5f || rect.Position.Y < pr.Position.Y - 1.5f || rect.End.Y > pr.End.Y + 1.5f)
                && !p.ClipContents && Total(c) > 0)
                Add("LEAVES_PARENT", FirstText(c), $"{Fmt(rect)} outside {Fmt(pr)}");
        }
        if (!InScrollContent(c) && c.GetParent() is not ScrollContainer && Total(c) > 0 && !viewport.Grow(1.5f).Encloses(rect))
            Add("OFFSCREEN", FirstText(c), $"{Fmt(rect)} outside viewport {Fmt(viewport)}");

        // ---- text panels that overlap each other (siblings that nothing lays out) ----
        if (c.GetParent() is Control q && q is not Container && c.GetParent()!.GetChildCount() > 1)
        {
            foreach (var other in q.GetChildren().OfType<Control>())
            {
                if (other == c || other.HasMeta(ModalMeta) || c.HasMeta(ModalMeta) || !other.Visible || other.GetIndex() < c.GetIndex() || other.Size.X < 2 || !IsPanelLike(c) || !IsPanelLike(other) || Total(c) == 0 || Total(other) == 0) continue;
                var inter = rect.Intersection(other.GetGlobalRect());
                if (inter.Size.X > 3f && inter.Size.Y > 3f) Add("OVERLAP", FirstText(c), $"with \"{Snip(FirstText(other))}\" {inter.Size.X:0}x{inter.Size.Y:0}");
            }
        }
        // ---- a HUD card keeps the safe margin from the screen edge ----
        if (c is PanelContainer && c.GetParent() is Control top && top is not Container && top.GetParent() is not Container && c.Size.X < viewport.Size.X * 0.95f && Total(c) > 0)
        {
            float dl = rect.Position.X - viewport.Position.X, dt = rect.Position.Y - viewport.Position.Y, dr = viewport.End.X - rect.End.X, db = viewport.End.Y - rect.End.Y;
            if (Math.Min(Math.Min(dl, dt), Math.Min(dr, db)) < SafeMargin) Add("MARGIN", FirstText(c), $"{Fmt(rect)} within {SafeMargin} px of the edge", true);
        }
        return error;
    }

    private static bool InScrollContent(Control c)
    {
        for (Node? n = c.GetParent(); n is not null; n = n.GetParent()) if (n is ScrollContainer) return true;
        return false;
    }

    private static bool IsPanelLike(Control c) => c is PanelContainer or Label or Button or HBoxContainer or VBoxContainer or MarginContainer;

    /// <summary>Number of visible text-bearing controls inside (including itself): purely graphic controls are never reported.</summary>
    private static int Total(Control c) => Texts(c).Count();

    private static IEnumerable<string> Texts(Node n)
    {
        if (n is CanvasItem { Visible: false }) yield break;
        if (n is Label l && l.Text.Length > 0) yield return l.Text;
        else if (n is Button b && b.Text.Length > 0) yield return b.Text;
        foreach (var ch in n.GetChildren()) foreach (var t in Texts(ch)) yield return t;
    }

    private static string FirstText(Control c) => Texts(c).FirstOrDefault() ?? c.GetType().Name;

    private static string Fmt(Rect2 r) => $"[{r.Position.X:0},{r.Position.Y:0} {r.Size.X:0}x{r.Size.Y:0}]";

    /// <summary>Prints the findings as <c>UIAUDIT</c> lines and returns the number of errors.</summary>
    public static int Report(string context, List<Finding> findings)
    {
        foreach (var f in findings.OrderByDescending(f => f.Error)) GD.Print($"UIAUDIT {context} {f}");
        return findings.Count(f => f.Error);
    }
}
