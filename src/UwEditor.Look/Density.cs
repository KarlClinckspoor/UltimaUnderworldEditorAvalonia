using System;
using System.Linq;
using System.Runtime.CompilerServices;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Data;
using Avalonia.Diagnostics;
using Avalonia.Interactivity;
using Avalonia.Layout;
using Avalonia.Markup.Xaml.Styling;
using Avalonia.Styling;
using Avalonia.VisualTree;

namespace UwEditor.Look
{
    /// <summary>
    /// View ▸ Layout: Comfortable (the Fluent look) or Compact (classic): Compact.axaml's
    /// tighter sizes and borders, and the white space the windows set themselves (margins,
    /// spacing, padding given in the windows' own code or XAML) halved. Font sizes stay. Every
    /// control is adjusted when it loads, so windows and panels made later follow; switching
    /// back puts the original values back.
    /// </summary>
    public static class Density
    {
        public const string Comfortable = "Comfortable", Compact = "Compact";
        /// <summary>How much of the windows' own white space is kept in Compact.</summary>
        public const double Factor = 0.5;

        static StyleInclude sheet;
        static bool hooked;
        public static bool IsCompact { get; private set; }

        sealed class Original
        {
            public Thickness? Margin, Padding;
            public double? Spacing, ItemSpacing, LineSpacing, RowSpacing, ColumnSpacing;
            public Thickness? MarginSet, PaddingSet;
            public double? SpacingSet, ItemSpacingSet, LineSpacingSet, RowSpacingSet, ColumnSpacingSet;
        }
        static readonly ConditionalWeakTable<Control, Original> originals = new ConditionalWeakTable<Control, Original>();

        /// <summary>Switches every window to the layout given (Comfortable or Compact).</summary>
        public static void Set(string name)
        {
            bool compact = string.Equals(name, Compact, StringComparison.OrdinalIgnoreCase);
            var app = Application.Current;
            if (app == null) return;
            if (!hooked)
            {
                hooked = true;
                Control.LoadedEvent.AddClassHandler<Control>((c, e) =>
                {
                    if (c is TopLevel top) Remember(top);
                    if (IsCompact) Tighten(c);
                }, RoutingStrategies.Direct);
            }
            // the sheet must be in this application's styles (a new Application, as each headless
            // test gets, starts without it although the flag says compact)
            bool present = sheet != null && app.Styles.Contains(sheet);
            if (compact == IsCompact && present == compact) return;
            IsCompact = compact;
            if (compact)
            {
                // a fresh sheet each time: one taken out of the application's styles keeps its owner
                // and cannot be added again (Compact, Comfortable, Compact threw)
                if (sheet == null || !app.Styles.Contains(sheet))
                {
                    sheet = new StyleInclude(new Uri("avares://UwEditor.Look/")) { Source = new Uri("avares://UwEditor.Look/Compact.axaml") };
                    app.Styles.Add(sheet);
                }
            }
            else if (sheet != null) { app.Styles.Remove(sheet); sheet = null; }
            foreach (var w in Windows())
                foreach (var c in w.GetSelfAndVisualDescendants().OfType<Control>())
                    if (compact) Tighten(c); else Restore(c);
        }

        static readonly System.Collections.Generic.List<WeakReference<TopLevel>> tops = new System.Collections.Generic.List<WeakReference<TopLevel>>();

        static void Remember(TopLevel t)
        {
            tops.RemoveAll(r => !r.TryGetTarget(out var x) || x == t);
            tops.Add(new WeakReference<TopLevel>(t));
        }

        /// <summary>The windows open now: the desktop's list, and every window loaded since the first Set.</summary>
        static System.Collections.Generic.IEnumerable<TopLevel> Windows()
        {
            var all = new System.Collections.Generic.List<TopLevel>();
            var life = Application.Current?.ApplicationLifetime as Avalonia.Controls.ApplicationLifetimes.IClassicDesktopStyleApplicationLifetime;
            if (life != null) all.AddRange(life.Windows);
            foreach (var r in tops) if (r.TryGetTarget(out var t) && !all.Contains(t)) all.Add(t);
            return all;
        }

        /// <summary>A value the control's own code or XAML set (not a style's, a template's or a binding's).</summary>
        static bool Own(Control c, AvaloniaProperty p)
        {
            var d = c.GetDiagnostic(p);
            if (d.Priority != BindingPriority.LocalValue) return false;
            return d.Diagnostic == null || d.Diagnostic.IndexOf("inding", StringComparison.Ordinal) < 0;
        }

        static Thickness Half(Thickness t) { return new Thickness(H(t.Left), H(t.Top), H(t.Right), H(t.Bottom)); }
        static double H(double v) { return v <= 2 ? v : Math.Round(Math.Max(2, v * Factor)); }

        /// <summary>Halves the control's own margin, padding and spacing (once; the originals are kept).</summary>
        public static void Tighten(Control c)
        {
            if (c == null || c.TemplatedParent != null) return;      // inside a control's template: the theme's business
            Original o;
            if (originals.TryGetValue(c, out o)) return;
            o = new Original();
            bool any = false;
            if (Own(c, Layoutable.MarginProperty) && c.Margin != default(Thickness)) { o.Margin = c.Margin; c.Margin = Half(c.Margin); o.MarginSet = c.Margin; any = true; }
            var pad = PaddingOf(c);
            if (pad != null && Own(c, pad))
            {
                var t = (Thickness)c.GetValue(pad);
                if (t != default(Thickness)) { o.Padding = t; var n = Half(t); c.SetValue(pad, n); o.PaddingSet = n; any = true; }
            }
            if (c is StackPanel sp && Own(c, StackPanel.SpacingProperty) && sp.Spacing > 0) { o.Spacing = sp.Spacing; sp.Spacing = H(sp.Spacing); o.SpacingSet = sp.Spacing; any = true; }
            if (c is WrapPanel wp)
            {
                if (Own(c, WrapPanel.ItemSpacingProperty) && wp.ItemSpacing > 0) { o.ItemSpacing = wp.ItemSpacing; wp.ItemSpacing = H(wp.ItemSpacing); o.ItemSpacingSet = wp.ItemSpacing; any = true; }
                if (Own(c, WrapPanel.LineSpacingProperty) && wp.LineSpacing > 0) { o.LineSpacing = wp.LineSpacing; wp.LineSpacing = H(wp.LineSpacing); o.LineSpacingSet = wp.LineSpacing; any = true; }
            }
            if (c is Grid g)
            {
                if (Own(c, Grid.RowSpacingProperty) && g.RowSpacing > 0) { o.RowSpacing = g.RowSpacing; g.RowSpacing = H(g.RowSpacing); o.RowSpacingSet = g.RowSpacing; any = true; }
                if (Own(c, Grid.ColumnSpacingProperty) && g.ColumnSpacing > 0) { o.ColumnSpacing = g.ColumnSpacing; g.ColumnSpacing = H(g.ColumnSpacing); o.ColumnSpacingSet = g.ColumnSpacing; any = true; }
            }
            originals.AddOrUpdate(c, any ? o : new Original());
        }

        /// <summary>Puts back what Tighten changed, where nothing else changed it since.</summary>
        public static void Restore(Control c)
        {
            Original o;
            if (c == null || !originals.TryGetValue(c, out o)) return;
            originals.Remove(c);
            if (o.Margin.HasValue && c.Margin == o.MarginSet) c.Margin = o.Margin.Value;
            var pad = PaddingOf(c);
            if (pad != null && o.Padding.HasValue && (Thickness)c.GetValue(pad) == o.PaddingSet) c.SetValue(pad, o.Padding.Value);
            if (c is StackPanel sp && o.Spacing.HasValue && sp.Spacing == o.SpacingSet) sp.Spacing = o.Spacing.Value;
            if (c is WrapPanel wp)
            {
                if (o.ItemSpacing.HasValue && wp.ItemSpacing == o.ItemSpacingSet) wp.ItemSpacing = o.ItemSpacing.Value;
                if (o.LineSpacing.HasValue && wp.LineSpacing == o.LineSpacingSet) wp.LineSpacing = o.LineSpacing.Value;
            }
            if (c is Grid g)
            {
                if (o.RowSpacing.HasValue && g.RowSpacing == o.RowSpacingSet) g.RowSpacing = o.RowSpacing.Value;
                if (o.ColumnSpacing.HasValue && g.ColumnSpacing == o.ColumnSpacingSet) g.ColumnSpacing = o.ColumnSpacing.Value;
            }
        }

        static AvaloniaProperty PaddingOf(Control c)
        {
            if (c is Border) return Border.PaddingProperty;
            if (c is Decorator) return Decorator.PaddingProperty;
            if (c is TextBlock) return TextBlock.PaddingProperty;
            if (c is Avalonia.Controls.Primitives.TemplatedControl) return Avalonia.Controls.Primitives.TemplatedControl.PaddingProperty;
            return null;
        }
    }
}
