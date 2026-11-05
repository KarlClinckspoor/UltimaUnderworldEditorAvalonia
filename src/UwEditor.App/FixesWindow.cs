using System;
using System.Collections.Generic;
using System.Linq;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;
using UwEditor.Core;
using UwEditor.Core.Editing;

namespace UwEditor.App
{
    /// <summary>
    /// Level ▸ Fixes: the game's own consistency rules checked on the levels as edited
    /// (Core's LevelFixes: the object lists, the free stacks, the active list, traps and their
    /// links, texts...), each finding with a description, a go-to and, where there is one, a fix.
    /// Every fix and every Fix all is one Edit ▸ Undo step of its level.
    /// </summary>
    public sealed class FixesWindow : Window
    {
        readonly MainWindow main;
        readonly GameSession session;
        readonly ComboBox scope = new ComboBox { ItemsSource = new[] { "This level", "Every level" }, SelectedIndex = 0, Width = 160 };
        readonly CheckBox warnings = new CheckBox { Content = "Fix all includes warnings", IsChecked = false };
        readonly ListBox checks = new ListBox { Width = 270 };
        readonly ListBox findings = new ListBox();
        readonly TextBlock description = new TextBlock { TextWrapping = TextWrapping.Wrap, FontSize = 12, Opacity = 0.8, Margin = new Thickness(0, 0, 0, 6) };
        readonly TextBlock status = new TextBlock { TextWrapping = TextWrapping.Wrap, FontSize = 12, Opacity = 0.8, VerticalAlignment = VerticalAlignment.Center };
        readonly Button fixButton = new Button { Content = "Fix", IsEnabled = false };
        readonly Button fixKind = new Button { Content = "Fix all of this kind", IsEnabled = false };
        readonly Button goButton = new Button { Content = "Go to", IsEnabled = false };
        readonly Dictionary<int, List<LevelFinding>> found = new Dictionary<int, List<LevelFinding>>();
        FixContext context;
        bool fixing;

        /// <summary>One row of the check list: a check (or all of them) with its count.</summary>
        sealed class CheckRow
        {
            public FixCheck Check;
            public int Count;
            public override string ToString() { return (Check == null ? "Everything found" : Check.Title) + (Count > 0 ? " (" + Count + ")" : ""); }
        }

        /// <summary>One row of the findings list.</summary>
        sealed class FindingRow
        {
            public LevelFinding F;
            public string Level;
            public override string ToString()
            {
                string sev = F.Severity == FixSeverity.ListError ? "list error" : F.Severity == FixSeverity.Problem ? "problem" : "warning";
                return Level + " · " + sev + " · " + F.Text + (F.CanFix ? "   [" + F.FixText + "]" : "");
            }
        }

        public FixesWindow(MainWindow main)
        {
            this.main = main;
            session = main.ViewModel.Session;
            Title = "Fixes";
            Width = 1100; Height = 720; MinWidth = 760; MinHeight = 460;
            WindowStartupLocation = WindowStartupLocation.CenterOwner;

            var check = new Button { Content = "Check", FontWeight = FontWeight.SemiBold };
            check.Click += (o, e) => Run();
            var fixAll = new Button { Content = "Fix all" };
            ToolTip.SetTip(fixAll, "Fix every finding that has a fix, in the levels checked (one Edit ▸ Undo step per level).");
            fixAll.Click += (o, e) => FixAll();
            var cleaner = new Button { Content = "Item cleaner…" };
            cleaner.Click += (o, e) => main.ShowItemCleaner();
            scope.SelectionChanged += (o, e) => Run();

            var top = new StackPanel { Spacing = 6, Margin = new Thickness(0, 0, 0, 8) };
            top.Children.Add(new TextBlock { Text = "Fixes", FontSize = 16, FontWeight = FontWeight.SemiBold });
            top.Children.Add(new TextBlock
            {
                TextWrapping = TextWrapping.Wrap, FontSize = 12, Opacity = 0.75,
                Text = "Checks what the game itself relies on, in the levels as edited: that every object is either free or reached exactly once from one tile (Obj_ListOkay), " +
                       "the free stacks, the creatures on the active list, traps and their links, and texts. List errors fail the game's own check, problems make the game misbehave, " +
                       "and warnings may be intentional. Double-click a finding to go to it. Each fix is one Edit ▸ Undo step.",
            });
            var bar = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8 };
            bar.Children.Add(scope); bar.Children.Add(check); bar.Children.Add(fixAll); bar.Children.Add(warnings); bar.Children.Add(cleaner);
            top.Children.Add(bar);
            DockPanel.SetDock(top, Dock.Top);

            checks.SelectionChanged += (o, e) => ShowFindings();
            DockPanel.SetDock(checks, Dock.Left);
            checks.Margin = new Thickness(0, 0, 12, 0);

            findings.SelectionChanged += (o, e) => Selected();
            findings.DoubleTapped += (o, e) => GoToSelected();
            findings.KeyDown += (o, e) => { if (e.Key == Key.Enter) { GoToSelected(); e.Handled = true; } };
            goButton.Click += (o, e) => GoToSelected();
            fixButton.Click += (o, e) => FixSelected();
            fixKind.Click += (o, e) => FixKind();
            var buttons = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8, Margin = new Thickness(0, 8, 0, 0) };
            buttons.Children.Add(goButton); buttons.Children.Add(fixButton); buttons.Children.Add(fixKind); buttons.Children.Add(status);
            var right = new DockPanel();
            DockPanel.SetDock(description, Dock.Top);
            DockPanel.SetDock(buttons, Dock.Bottom);
            right.Children.Add(description); right.Children.Add(buttons); right.Children.Add(findings);

            var dock = new DockPanel { Margin = new Thickness(16) };
            dock.Children.Add(top); dock.Children.Add(checks); dock.Children.Add(right);
            Content = dock;
            ManualWindow.Attach(this, "Fixes");

            main.ViewModel.Edits.Changed += EditsChanged;
            Closed += (o, e) => main.ViewModel.Edits.Changed -= EditsChanged;
            Run();
        }

        /// <summary>The game the window works on.</summary>
        public GameSession Session { get { return session; } }

        /// <summary>A level changed elsewhere (an edit, an undo): its findings are made again.</summary>
        void EditsChanged(LevelEditor e)
        {
            if (fixing || !found.ContainsKey(e.Level)) return;
            found[e.Level] = LevelFixes.Find(e, context);
            Fill();
        }

        /// <summary>The levels the scope names.</summary>
        IEnumerable<int> Levels
        {
            get
            {
                if (scope.SelectedIndex == 0) { var l = main.ViewModel.Level; return l != null && l.Present ? new[] { l.Level } : new int[0]; }
                return session.LevelSlots().Where(s => s.Present).Select(s => s.Level);
            }
        }

        /// <summary>The level's block as edited (the editor's when there is one, else as loaded).</summary>
        byte[] DataOf(int level)
        {
            var e = main.ViewModel.Edits.Existing(level);
            return e != null ? e.Data : main.ViewModel.Edits.Model(level).Block.Data;
        }

        string LevelName(int level)
        {
            return session.IsUw2 ? session.Profile.LevelName(level) : "Level " + level;
        }

        /// <summary>The Check button: every level in scope checked again.</summary>
        public void Run()
        {
            found.Clear();
            context = FixContext.Build(session, main.ViewModel.Edits.Model);
            foreach (int l in Levels) found[l] = LevelFixes.Find(session, l, DataOf(l), context);
            Fill();
        }

        /// <summary>Everything found, in the levels checked.</summary>
        public List<LevelFinding> Findings { get { return found.Values.SelectMany(x => x).ToList(); } }

        void Fill()
        {
            var keep = (checks.SelectedItem as CheckRow)?.Check;
            var all = Findings;
            var rows = new List<CheckRow> { new CheckRow { Check = null, Count = all.Count } };
            foreach (var c in LevelFixes.Checks) rows.Add(new CheckRow { Check = c, Count = all.Count(f => f.Check == c) });
            checks.ItemsSource = rows;
            checks.SelectedItem = rows.FirstOrDefault(r => r.Check == keep && (r.Check == null || r.Count > 0)) ?? rows[0];
            int levels = found.Count, errors = all.Count(f => f.Severity == FixSeverity.ListError), fixable = all.Count(f => f.CanFix);
            status.Text = all.Count == 0
                ? (levels == 1 ? "Nothing found in this level." : "Nothing found in " + levels + " levels.")
                : string.Format("{0} found in {1} level{2} ({3} list error{4}); {5} with a fix.", all.Count, levels, levels == 1 ? "" : "s", errors, errors == 1 ? "" : "s", fixable);
            ShowFindings();
        }

        void ShowFindings()
        {
            var row = checks.SelectedItem as CheckRow;
            var c = row?.Check;
            description.Text = c == null ? "Pick a check on the left to read what it looks for." : c.Description;
            var list = Findings.Where(f => c == null || f.Check == c).OrderBy(f => f.Level).ThenBy(f => f.Severity).ThenBy(f => f.Object)
                .Select(f => new FindingRow { F = f, Level = LevelName(f.Level) }).ToList();
            findings.ItemsSource = list;
            fixKind.IsEnabled = list.Any(r => r.F.CanFix);
            fixKind.Content = c == null ? "Fix all listed" : "Fix all of this kind";
            Selected();
        }

        void Selected()
        {
            var r = findings.SelectedItem as FindingRow;
            fixButton.IsEnabled = r != null && r.F.CanFix;
            goButton.IsEnabled = r != null && (r.F.Object > 0 || r.F.X >= 0);
            if (r != null && checks.SelectedItem is CheckRow cr && cr.Check == null) description.Text = r.F.Check.Title + ": " + r.F.Check.Description;
        }

        /// <summary>Opens the finding's level and selects its object (or tile).</summary>
        public void GoTo(LevelFinding f)
        {
            if (f == null) return;
            var m = main.ViewModel.Edits.Model(f.Level);
            if (f.Object > 0 && f.Object < m.Objects.Length && !m.Objects[f.Object].Free && m.Objects[f.Object].TileX >= 0) main.GoToObject(f.Level, f.Object);
            else if (f.X >= 0 && f.Y >= 0) main.GoToTile(f.Level, f.X, f.Y);
        }

        void GoToSelected() { GoTo((findings.SelectedItem as FindingRow)?.F); }

        /// <summary>One finding's fix, one undoable step; the level is checked again.</summary>
        public bool Fix(LevelFinding f)
        {
            if (f == null || !f.CanFix) return false;
            bool done;
            fixing = true;
            try { done = LevelFixes.Fix(main.ViewModel.Edits.For(f.Level), f); }
            catch (Exception ex) { status.Text = "The fix failed: " + ex.Message; return false; }
            finally { fixing = false; }
            Recheck(f.Level);
            if (done) status.Text = "Fixed: " + f.Check.Title.ToLowerInvariant() + ". " + status.Text;
            return done;
        }

        void FixSelected() { Fix((findings.SelectedItem as FindingRow)?.F); }

        void Recheck(int level)
        {
            found[level] = LevelFixes.Find(session, level, DataOf(level), context);
            Fill();
        }

        /// <summary>The Fix all button: in each level checked, every finding with a fix (warnings when ticked), one step a level.</summary>
        public int FixAll()
        {
            int n = 0, levels = 0;
            bool warn = warnings.IsChecked == true;
            fixing = true;
            try
            {
                foreach (var kv in found.ToList())
                {
                    if (!kv.Value.Any(f => f.CanFix && (warn || f.Severity != FixSeverity.Warning))) continue;
                    int k = LevelFixes.FixAll(main.ViewModel.Edits.For(kv.Key), warn, context);
                    if (k > 0) { n += k; levels++; }
                }
            }
            catch (Exception ex) { status.Text = "Fix all failed: " + ex.Message; return n; }
            finally { fixing = false; }
            foreach (int l in found.Keys.ToList()) found[l] = LevelFixes.Find(session, l, DataOf(l), context);
            Fill();
            status.Text = n == 0 ? "Nothing to fix. " + status.Text : string.Format("{0} fixed in {1} level{2} (one Edit ▸ Undo step each). {3}", n, levels, levels == 1 ? "" : "s", status.Text);
            return n;
        }

        /// <summary>Every listed finding of the selected check, each level as one step.</summary>
        void FixKind()
        {
            var list = ((findings.ItemsSource as IEnumerable<FindingRow>) ?? Enumerable.Empty<FindingRow>()).Select(r => r.F).Where(f => f.CanFix).ToList();
            fixing = true;
            int n = 0;
            try
            {
                foreach (var g in list.GroupBy(f => f.Level))
                    n += LevelFixes.FixMany(main.ViewModel.Edits.For(g.Key), g, "Fix: " + g.First().Check.Title.ToLowerInvariant() + " (" + g.Count() + ")");
            }
            catch (Exception ex) { status.Text = "The fix failed: " + ex.Message; }
            finally { fixing = false; }
            foreach (int l in list.Select(f => f.Level).Distinct()) found[l] = LevelFixes.Find(session, l, DataOf(l), context);
            Fill();
            status.Text = n + " fixed. " + status.Text;
        }
    }
}
