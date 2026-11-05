using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;
using UwEditor.Core.Randomizer;

namespace UwEditor.App
{
    /// <summary>
    /// One link of a zoning, to edit: its zones and direction, kind, sureness, the list of
    /// requirements (all must hold; | within a line for "or"), filled from the doors when asked,
    /// notes, a go-to for its square, and delete. Used by the Zones window.
    /// </summary>
    public sealed class LinkEditorPanel : StackPanel
    {
        readonly ZoningState z;
        readonly ZoneLink k;
        readonly Action<string> status;

        /// <param name="reshow">Called when the panel should be built again (the link's ends changed).</param>
        /// <param name="delete">Removes the link.</param>
        public LinkEditorPanel(MainWindow main, ZoningState zoning, ZoneLink link, Action<string> status, Action reshow, Action delete)
        {
            z = zoning; k = link; this.status = status ?? (s => { });
            Spacing = 6;
            var zones = z.Set.Zones.Where(x => x.Id > 1).Select(x => x.Id + "  " + x.Name).ToList();
            var ids = z.Set.Zones.Where(x => x.Id > 1).Select(x => x.Id).ToList();
            var from = new ComboBox { ItemsSource = zones, SelectedIndex = ids.IndexOf(k.From), HorizontalAlignment = HorizontalAlignment.Stretch };
            var to = new ComboBox { ItemsSource = zones, SelectedIndex = ids.IndexOf(k.To), HorizontalAlignment = HorizontalAlignment.Stretch };
            from.SelectionChanged += (s, e) => { if (from.SelectedIndex >= 0 && ids[from.SelectedIndex] != k.From) { k.From = ids[from.SelectedIndex]; z.LinkChanged(); } };
            to.SelectionChanged += (s, e) => { if (to.SelectedIndex >= 0 && ids[to.SelectedIndex] != k.To) { k.To = ids[to.SelectedIndex]; z.LinkChanged(); } };
            Children.Add(new TextBlock { Text = "From", FontSize = 12 });
            Children.Add(from);
            Children.Add(new TextBlock { Text = "To", FontSize = 12 });
            Children.Add(to);
            var two = new CheckBox { Content = "Two-way (also from the second zone back to the first)", IsChecked = k.TwoWay };
            two.IsCheckedChanged += (s, e) => { k.TwoWay = two.IsChecked == true; z.LinkChanged(); };
            Children.Add(two);
            Children.Add(Btn("Swap the direction", () => { int t = k.From; k.From = k.To; k.To = t; z.LinkChanged(); reshow?.Invoke(); }));
            var kind = new ComboBox { ItemsSource = ZoneLink.Kinds, SelectedItem = ZoneLink.Kinds.Contains(k.Kind) ? k.Kind : "other", HorizontalAlignment = HorizontalAlignment.Stretch };
            kind.SelectionChanged += (s, e) => { var v = kind.SelectedItem as string ?? "walk"; if (v != k.Kind) { k.Kind = v; z.LinkChanged(); } };
            Children.Add(new TextBlock { Text = "Kind", FontSize = 12 });
            Children.Add(kind);
            var conf = new ComboBox { ItemsSource = new[] { "certain", "todo" }, SelectedItem = k.Confidence == "todo" ? "todo" : "certain", HorizontalAlignment = HorizontalAlignment.Stretch };
            conf.SelectionChanged += (s, e) => { var v = conf.SelectedItem as string ?? "certain"; if (v != k.Confidence) { k.Confidence = v; z.LinkChanged(); } };
            Children.Add(new TextBlock { Text = "Confidence", FontSize = 12 });
            Children.Add(conf);

            Children.Add(new TextBlock { Classes = { "h" }, Text = "REQUIREMENTS (ALL OF THEM)" });
            var terms = new ObservableCollection<string>(k.RequirementList);
            var list = new ListBox { ItemsSource = terms, MinHeight = 60, MaxHeight = 180 };
            list.Bind(ListBox.BackgroundProperty, list.GetResourceObservable("PanelAlt"));
            var check = new TextBlock { FontSize = 11, TextWrapping = TextWrapping.Wrap };
            Action<bool> apply = changed =>
            {
                if (changed) k.RequirementList = terms.ToList();
                string err;
                Requirement.TryParse(k.Requires, out err);
                check.Text = err == null ? (terms.Count == 0 ? "Free: nothing is needed." : "As an expression: " + k.Requires) : "Not understood: " + err;
                check.Bind(TextBlock.ForegroundProperty, check.GetResourceObservable(err == null ? "Muted" : "Error"));
                if (changed) z.LinkChanged();
            };
            var term = new AutoCompleteBox { Watermark = "key:5, pick:3, bash, item:orb, spell:levitate, zone:12, talismans:8 … (| for or)", ItemsSource = Suggestions(), FilterMode = AutoCompleteFilterMode.Contains, MinimumPrefixLength = 1 };
            Action addTerm = () => { var t = (term.Text ?? "").Trim(); if (t.Length > 0) { terms.Add(t); term.Text = ""; apply(true); } };
            var add = Btn("Add", addTerm);
            term.KeyDown += (s, e) => { if (e.Key == Key.Enter) { addTerm(); e.Handled = true; } };
            var remove = Btn("Remove", () => { if (list.SelectedItem is string t) { terms.Remove(t); apply(true); } });
            var suggest = Btn("From the doors", () =>
            {
                List<DoorPassage> used;
                string r = ZoneDoors.Suggest(z.Doors, k.From, k.To, out used);
                if (r == null) { this.status("No door joins these two zones; the way may be a walk, a drop, a lever or a conversation."); return; }
                terms.Clear();
                foreach (var t in ZoneLink.SplitAnd(r)) terms.Add(t);
                k.Notes = string.Join("; ", used.Select(u => string.Format("L{0} ({1}, {2}) {3}: {4}", u.Level, u.X, u.Y, u.Kind, u.Notes)));
                apply(true);
                reshow?.Invoke();
                this.status(used.Count + " door(s) between the zones.");
            });
            Children.Add(list);
            var addRow = new DockPanel();
            DockPanel.SetDock(add, Dock.Right);
            add.Margin = new Thickness(6, 0, 0, 0);
            addRow.Children.Add(add);
            addRow.Children.Add(term);
            Children.Add(addRow);
            var r2 = new WrapPanel();
            r2.Children.Add(remove); r2.Children.Add(suggest);
            foreach (Control c in r2.Children) c.Margin = new Thickness(0, 0, 6, 0);
            Children.Add(r2);
            Children.Add(check);
            Children.Add(new TextBlock
            {
                FontSize = 11, Opacity = 0.7, TextWrapping = TextWrapping.Wrap,
                Text = "Every line must hold; a line with | holds when any of its parts does. key:N is a key with owner N; pick:Z is picking a lock of difficulty Z; bash is breaking the door; item:, flag: and npc: are things a zone grants; spell:NAME is a spell; zone:N is reaching zone N (for a lever there); talismans:N is having N talismans.",
            });
            Children.Add(new TextBlock { Classes = { "h" }, Text = "NOTES" });
            var notes = new TextBox { Text = k.Notes, AcceptsReturn = true, TextWrapping = TextWrapping.Wrap, MinHeight = 60 };
            notes.LostFocus += (s, e) => { if (notes.Text != k.Notes) { k.Notes = notes.Text ?? ""; z.LinkChanged(); } };
            Children.Add(notes);
            var foot = new WrapPanel();
            if (k.Level > 0) foot.Children.Add(Btn(string.Format("Go to level {0} ({1}, {2})", k.Level, k.X, k.Y), () => main.GoToTile(k.Level, k.X, k.Y)));
            foot.Children.Add(Btn("Delete the link", () => delete?.Invoke()));
            foreach (Control c in foot.Children) c.Margin = new Thickness(0, 0, 6, 0);
            Children.Add(foot);
            apply(false);
        }

        public ZoneLink Link { get { return k; } }

        static Button Btn(string t, Action a) { var b = new Button { Content = t }; b.Click += (s, e) => a(); return b; }

        /// <summary>Terms for the requirement box: what the zones grant, the doors' keys, the spells.</summary>
        List<string> Suggestions()
        {
            var s = new SortedSet<string>(StringComparer.OrdinalIgnoreCase) { "bash", "free", "spell:levitate", "spell:waterwalk", "spell:jump", "spell:reveal", "spell:gate_travel", "talismans:8" };
            foreach (var p in z.Set.Placements) if (!string.IsNullOrEmpty(p.Grants)) s.Add(Requirement.Normalize(p.Grants));
            foreach (var l in z.Set.Links) foreach (var t in ZoneLink.SplitAnd(l.Requires)) foreach (var part in t.Split('|')) s.Add(part.Trim());
            try { foreach (var i in z.Items) s.Add(i.Token); } catch (Exception) { }
            foreach (var zone in z.Set.Zones.Where(x => x.Id > 1)) s.Add("zone:" + zone.Id);
            return s.ToList();
        }
    }
}
