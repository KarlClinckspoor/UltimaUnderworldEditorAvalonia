using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Controls.Templates;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Platform.Storage;
using UwEditor.Core;
using UwEditor.Core.Editing;
using UwEditor.Core.Randomizer;

namespace UwEditor.App
{
    /// <summary>A zone in the zones window's list.</summary>
    public sealed class ZoneRow
    {
        public int Id { get; set; }
        public string Label { get; set; }
        public IBrush Swatch { get; set; }
        public uint Color { get; set; }
        public override string ToString() { return Label; }
    }

    /// <summary>
    /// Tools ▸ Randomizer ▸ Zones (work in progress): the zone being painted, adding, renaming,
    /// removing and colouring zones, painting them in the 3D view or filling an area, and the
    /// key items each zone holds.
    /// </summary>
    public sealed class ZonesWindow : Window
    {
        readonly MainWindow main;
        readonly ZoningState z;
        readonly ComboBox zoneBox = new ComboBox { HorizontalAlignment = HorizontalAlignment.Stretch, MaxDropDownHeight = 520 };
        readonly TextBox nameBox = new TextBox { PlaceholderText = "Name" };
        readonly TextBox colorBox = new TextBox { Width = 100, PlaceholderText = "#RRGGBB" };
        readonly Border swatch = new Border { Width = 26, Height = 26, CornerRadius = new CornerRadius(4) };
        readonly ToggleButton paintButton = new ToggleButton { Content = "Paint in the view" };
        readonly CheckBox fillBox = new CheckBox { Content = "A click fills the area" };
        readonly NumericUpDown climbBox = new NumericUpDown { Minimum = 0, Maximum = 15, Increment = 1, FormatString = "0", Width = 110 };
        readonly CheckBox overlayBox = new CheckBox { Content = "Show zone colours in the view", IsChecked = true };
        readonly NumericUpDown fillZone = new NumericUpDown { Minimum = 2, Maximum = 255, Increment = 1, FormatString = "0", Width = 110 };
        readonly TextBox notesBox = new TextBox { AcceptsReturn = true, TextWrapping = TextWrapping.Wrap, MinHeight = 70, MaxHeight = 160, PlaceholderText = "Notes about this zone" };
        readonly TextBlock levelInfo = new TextBlock { FontSize = 12, TextWrapping = TextWrapping.Wrap };
        readonly TextBlock fileText = new TextBlock { FontSize = 12, Opacity = 0.75, TextTrimming = TextTrimming.CharacterEllipsis };
        readonly TextBlock status = new TextBlock { FontSize = 12, Opacity = 0.75, TextWrapping = TextWrapping.Wrap };
        readonly TextBlock itemsTitle = new TextBlock { FontWeight = FontWeight.SemiBold };
        readonly ListBox itemList = new ListBox();
        readonly TextBlock grantsText = new TextBlock { FontSize = 12, TextWrapping = TextWrapping.Wrap, Opacity = 0.85 };
        readonly ObservableCollection<ZoneRow> rows = new ObservableCollection<ZoneRow>();
        readonly CheckBox splitBox = new CheckBox { Content = "Split multi-level zones by level for the solver (each level its own zone, joined by free two-way links)" };
        readonly TextBlock splitText = new TextBlock { FontSize = 11, Opacity = 0.7, TextWrapping = TextWrapping.Wrap };
        bool filling;

        public CheckBox SplitByLevelBox { get { return splitBox; } }

        // ---- start and goal
        readonly TextBlock startGoalText = new TextBlock { FontSize = 11, Opacity = 0.8, TextWrapping = TextWrapping.Wrap };
        // ---- links
        readonly TextBlock linksTitle = new TextBlock { FontWeight = FontWeight.SemiBold };
        readonly ListBox linkList = new ListBox { MaxHeight = 200 };
        readonly ComboBox linkTo = new ComboBox { Width = 210, MaxDropDownHeight = 420 };
        readonly CheckBox linkTwoWay = new CheckBox { Content = "Two-way", IsChecked = true };
        readonly ContentControl linkHost = new ContentControl();
        List<ZoneLink> shownLinks = new List<ZoneLink>();
        List<int> toIds = new List<int>();
        ZoneLink selectedLink;
        bool fillingLinks;
        // ---- the solve
        readonly TextBlock solveText = new TextBlock { FontSize = 12, TextWrapping = TextWrapping.Wrap, VerticalAlignment = VerticalAlignment.Center, MaxWidth = 1000 };

        public IReadOnlyList<ZoneRow> Rows { get { return rows; } }
        /// <summary>The three columns and the splitters between them (for the tests).</summary>
        public Grid Splitters { get; private set; }
        public ComboBox ZoneBox { get { return zoneBox; } }
        public string NameText { get { return nameBox.Text; } }
        public string StartGoalText { get { return startGoalText.Text; } }

        /// <summary>The last Check if solvable, or null.</summary>
        public TimelineResult LastCheck { get; private set; }
        public ZoneLink SelectedLink { get { return selectedLink; } }

        Func<int, LevelModel> Levels { get { var edits = main.ViewModel.Edits; return l => edits != null ? edits.Model(l) : z.Session.LoadLevel(l); } }

        /// <summary>The zone of the new-game start square (UW.EXE, as the editor's start tool sets it), else the zoning's start zone.</summary>
        int StartZone()
        {
            var st = z.Session.NewGameStart;
            if (st != null) { int v = z.Set.ZoneAt(st.Level, (int)st.X, (int)st.Y); if (v > ZoneSet.Unassigned) return v; }
            return z.Set.StartZone;
        }

        List<EndTrigger> Ends()
        {
            try { return EndGame.Find(z.Session, Levels); }
            catch (Exception) { return new List<EndTrigger>(); }
        }

        void RefreshStartGoal()
        {
            var st = z.Session.NewGameStart;
            string start = st == null ? "Start: unknown." : string.Format("Start: level {0} ({1}, {2}), {3}.", st.Level, (int)st.X, (int)st.Y, ZoneText(z.Set.ZoneAt(st.Level, (int)st.X, (int)st.Y)));
            var ends = Ends();
            string goal = z.Session.IsUw2 ? "Goal: not known for UW2." :
                "Goal: reaching level 9, or a square that ends the game: " + (ends.Count == 0 ? "none." : string.Join("; ", ends.Select(e => e + " " + ZoneText(z.Set.ZoneAt(e.Level, e.X, e.Y)))) + ".");
            startGoalText.Text = start + " " + goal + " Mark as start and Mark as goal use the square selected in the view.";
        }

        string ZoneText(int id) { return id > ZoneSet.Unassigned ? "zone " + z.Label(id) : "in no zone yet"; }

        /// <summary>Mark as start: the new-game start to the selected square (level 1), saved in the executable, and the zoning's start zone with it.</summary>
        public bool MarkStart()
        {
            var vm = main.ViewModel;
            if (vm.Level == null || vm.SelectedTileX < 0) { status.Text = "Select a square in the view first (on level 1)."; return false; }
            int level = vm.Level.Level, x = vm.SelectedTileX, y = vm.SelectedTileY;
            bool ok = main.MoveMarker("start", level, x, y);
            status.Text = vm.EditStatus;
            if (!ok) return false;
            int zone = z.Set.ZoneAt(1, x, y);
            if (zone > ZoneSet.Unassigned && z.Set.StartZone != zone) { z.Set.StartZone = zone; z.LinkChanged(); }
            Refresh();
            return true;
        }

        /// <summary>Mark as goal: a square that ends the game (move trigger and hack trap 0x3F) on the selected square; level 9's stays.</summary>
        public bool MarkGoal()
        {
            var vm = main.ViewModel;
            if (vm.Level == null || vm.SelectedTileX < 0 || vm.Edits == null) { status.Text = "Select a square in the view first."; return false; }
            try
            {
                var r = TrapRecipes.EndOfGame(vm.Edits.For(vm.Level.Level), vm.SelectedTileX, vm.SelectedTileY);
                vm.EditStatus = r.Summary;
                main.SelectMade(r.Level, r.Trigger);
                status.Text = r.Summary + " Edit ▸ Undo takes it away; save the level to keep it.";
                Refresh();
                return true;
            }
            catch (Exception ex) { status.Text = ex.Message; return false; }
        }

        // ---- links

        Control LinksColumn()
        {
            var col = new StackPanel { Spacing = 6, Margin = new Thickness(8, 10, 0, 0) };
            col.Children.Add(linksTitle);
            col.Children.Add(new TextBlock { FontSize = 11, Opacity = 0.7, TextWrapping = TextWrapping.Wrap, Text = "The ways from and to this zone, and what each needs. Click one to edit it; the zone graph shows them as you go. A new link takes its requirement from the doors between the zones when there are any." });
            linkList.Bind(ListBox.BackgroundProperty, linkList.GetResourceObservable("Panel"));
            linkList.SelectionChanged += (s, e) =>
            {
                if (fillingLinks || linkList.SelectedIndex < 0 || linkList.SelectedIndex >= shownLinks.Count) return;
                selectedLink = shownLinks[linkList.SelectedIndex];
                ShowLinkEditor();
            };
            col.Children.Add(linkList);
            col.Children.Add(Row(new TextBlock { Text = "New link to", VerticalAlignment = VerticalAlignment.Center }, linkTo, linkTwoWay, B("Add link", () => AddLink())));
            col.Children.Add(H("THE LINK"));
            col.Children.Add(linkHost);
            return new ScrollViewer { Content = col, HorizontalScrollBarVisibility = Avalonia.Controls.Primitives.ScrollBarVisibility.Disabled };
        }

        string LinkText(ZoneLink k) { return string.Format("{0} {1} {2}: {3}", z.Label(k.From), k.TwoWay ? "↔" : "→", z.Label(k.To), k.Requires); }

        /// <summary>The links of the current zone; with rebuild, the zones to link to and the editor of the selected link too.</summary>
        void RefreshLinks(bool rebuild)
        {
            int cur = z.CurrentZone;
            if (selectedLink != null && !z.Set.Links.Contains(selectedLink)) { selectedLink = null; rebuild = true; }
            shownLinks = z.Set.Links.Where(k => k.From == cur || k.To == cur).ToList();
            linksTitle.Text = string.Format("Links of zone {0} ({1})", cur, shownLinks.Count);
            fillingLinks = true;
            linkList.ItemsSource = shownLinks.Select(LinkText).ToList();
            linkList.SelectedIndex = selectedLink != null ? shownLinks.IndexOf(selectedLink) : -1;
            if (rebuild)
            {
                toIds = z.Set.Zones.Where(x => x.Id > ZoneSet.Unassigned && x.Id != cur).Select(x => x.Id).ToList();
                int keep = linkTo.SelectedIndex >= 0 && linkTo.SelectedIndex < toIds.Count ? toIds[linkTo.SelectedIndex] : -1;
                linkTo.ItemsSource = toIds.Select(id => z.Label(id)).ToList();
                linkTo.SelectedIndex = toIds.IndexOf(keep);
            }
            fillingLinks = false;
            if (rebuild)
            {
                if (selectedLink != null && selectedLink.From != cur && selectedLink.To != cur) selectedLink = null;
                ShowLinkEditor();
            }
        }

        void ShowLinkEditor()
        {
            linkHost.Content = selectedLink == null
                ? (Control)new TextBlock { FontSize = 12, Opacity = 0.7, TextWrapping = TextWrapping.Wrap, Text = "No link chosen." }
                : new LinkEditorPanel(main, z, selectedLink, t => status.Text = t, () => RefreshLinks(true), DeleteSelectedLink);
        }

        /// <summary>Adds a link from the current zone to the zone chosen (two-way when ticked), its requirement from the doors between them.</summary>
        public ZoneLink AddLink(int to = -1)
        {
            if (to < 0) to = linkTo.SelectedIndex >= 0 && linkTo.SelectedIndex < toIds.Count ? toIds[linkTo.SelectedIndex] : -1;
            if (to < 0) { status.Text = "Choose the zone the link goes to."; return null; }
            try
            {
                var k = z.Connect(z.CurrentZone, to, linkTwoWay.IsChecked == true);
                selectedLink = k;
                RefreshLinks(true);
                status.Text = string.Format("Linked {0} {1} {2}: requires {3}.", z.Label(k.From), k.TwoWay ? "<->" : "->", z.Label(k.To), k.Requires);
                return k;
            }
            catch (Exception ex) { status.Text = ex.Message; return null; }
        }

        void DeleteSelectedLink()
        {
            if (selectedLink == null) return;
            var k = selectedLink;
            selectedLink = null;
            z.RemoveLink(k);
            RefreshLinks(true);
            status.Text = "Link removed.";
        }

        public void SelectLink(ZoneLink k) { selectedLink = k; RefreshLinks(true); }

        // ---- the solve

        /// <summary>Check if solvable: the solve over time on this zoning, as Apply uses it, with the main option sets. Returns the report.</summary>
        public string CheckSolvable()
        {
            TimelineResult r;
            string text = main.SolveZoning(z, out r);
            LastCheck = r;
            string verdict = MainWindow.Verdict(r);
            solveText.Text = verdict;
            return verdict + Environment.NewLine + Environment.NewLine + text;
        }

        async System.Threading.Tasks.Task CheckSolvableAsync()
        {
            solveText.Text = "Checking (the solve over time, with the main option sets)…";
            await System.Threading.Tasks.Task.Delay(30);   // let the line show before the solve holds the window
            string text;
            try { text = CheckSolvable(); }
            catch (Exception ex) { solveText.Text = "The solver stopped: " + ex.Message; return; }
            await MessageWindow.ShowReport(this, "Is the zoning solvable?", text);
        }

        /// <summary>Says which zones lie on several levels (what the split option splits).</summary>
        void RefreshSplit()
        {
            var multi = z.Set.Zones.Where(x => x.Id > ZoneSet.Unassigned).Select(x => new { x.Id, Levels = z.Set.LevelsOf(x.Id) }).Where(x => x.Levels.Count > 1).ToList();
            splitText.Text = multi.Count == 0 ? "No zone lies on several levels." :
                string.Format("{0} zone(s) on several levels{1}: {2}.", multi.Count, splitBox.IsChecked == true ? ", split for the solver" : "",
                    string.Join("; ", multi.Take(8).Select(x => x.Id + " (L" + string.Join(", L", x.Levels) + ")")) + (multi.Count > 8 ? " …" : ""));
        }

        public ZoningState Zoning { get { return z; } }

        public ZonesWindow(MainWindow main, ZoningState zoning)
        {
            this.main = main;
            z = zoning;
            Title = "Zones (randomizer, work in progress) — " + zoning.Session.Title;
            Width = 1400; Height = 760; MinWidth = 1000; MinHeight = 520;
            WindowStartupLocation = WindowStartupLocation.CenterOwner;

            zoneBox.ItemsSource = rows;
            zoneBox.ItemTemplate = new FuncDataTemplate<ZoneRow>((r, ns) =>
            {
                var sp = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8 };
                sp.Children.Add(new Border { Width = 16, Height = 16, CornerRadius = new CornerRadius(3), Background = r?.Swatch });
                sp.Children.Add(new TextBlock { Text = r?.Label, VerticalAlignment = VerticalAlignment.Center });
                return sp;
            });
            zoneBox.SelectionChanged += (s, e) =>
            {
                if (filling || !(zoneBox.SelectedItem is ZoneRow r)) return;
                z.CurrentZone = r.Id;
            };
            itemList.Bind(ListBox.BackgroundProperty, itemList.GetResourceObservable("Panel"));
            itemList.DoubleTapped += (s, e) => GoToItem();
            notesBox.LostFocus += (s, e) => z.SetNotes(z.CurrentZone, notesBox.Text);
            paintButton.IsCheckedChanged += (s, e) => { z.Painting = paintButton.IsChecked == true; main.ZonePaintingChanged(); };
            fillBox.IsCheckedChanged += (s, e) => z.FillMode = fillBox.IsChecked == true;
            overlayBox.IsCheckedChanged += (s, e) => { z.ShowOverlay = overlayBox.IsChecked == true; main.ZonesChanged("tiles"); };

            var dock = new DockPanel { Margin = new Thickness(16) };
            // file row
            var head = new StackPanel { Spacing = 6 };
            head.Children.Add(new TextBlock { Text = "Zones", FontSize = 16, FontWeight = FontWeight.SemiBold });
            head.Children.Add(new TextBlock
            {
                FontSize = 12, Opacity = 0.75, TextWrapping = TextWrapping.Wrap,
                Text = "Work in progress. A zone is a part of the world that can be walked without meeting a requirement. Every tile has a zone id: 0 rock, 1 open but not in a zone yet (magenta), 2-255 the zones. The links between zones and what they need are made on the right; the zone graph shows them.",
            });
            var files = Row(B("New", () => Confirm(() => z.NewFile())), B("Open…", OpenFile), B("Save", () => SaveFile(false)), B("Save as…", () => SaveFile(true)),
                // Import pictures… / Export pictures… (ImportPictures, ExportPictures) are kept in the code but no longer offered
                z.Session.IsUw2 ? null : B("Load the UW1 example", () => Confirm(() => z.LoadExample())),
                B("Zone graph…", () => main.ShowZoneGraph()), AutozoneButton());
            head.Children.Add(files);
            head.Children.Add(fileText);
            splitBox.IsChecked = z.SplitByLevel;
            splitBox.IsCheckedChanged += (s, e) => { main.ZoneSplitByLevel = splitBox.IsChecked == true; RefreshSplit(); };
            ToolTip.SetTip(splitBox, "For the solver only (Apply, Check the logic): each zone painted on several levels becomes one zone a level, the parts joined in level order by free two-way links, as a hand zoning takes the way between levels as given. The zoning here is not changed.");
            head.Children.Add(splitBox);
            head.Children.Add(splitText);
            var solveButton = B("Check if solvable…", async () => await CheckSolvableAsync());
            ToolTip.SetTip(solveButton, "The solve over time on this zoning, as Apply uses it (unsaved changes and the split option included), with the Apply window's options when it is open (else the Automatic zones window's, else the defaults), and with the main option sets.");
            head.Children.Add(Row(solveButton, solveText));
            DockPanel.SetDock(head, Dock.Top);
            dock.Children.Add(head);
            DockPanel.SetDock(status, Dock.Bottom);
            status.Margin = new Thickness(0, 8, 0, 0);
            dock.Children.Add(status);

            // left: the zone
            var left = new StackPanel { Spacing = 8, Margin = new Thickness(0, 10, 8, 0) };
            left.Children.Add(H("CURRENT ZONE"));
            left.Children.Add(zoneBox);
            left.Children.Add(Row(B("Add zone", AddZone), B("Remove zone", RemoveZone)));
            var nameRow = new DockPanel();
            var rename = B("Rename", () => { z.Rename(z.CurrentZone, nameBox.Text); status.Text = "Renamed."; });
            DockPanel.SetDock(rename, Dock.Right);
            rename.Margin = new Thickness(6, 0, 0, 0);
            nameRow.Children.Add(rename);
            nameRow.Children.Add(nameBox);
            left.Children.Add(nameRow);
            left.Children.Add(Row(swatch, colorBox, B("Set colour", () => Try(() => z.SetColor(z.CurrentZone, colorBox.Text))), B("Glasbey default", () => z.SetColor(z.CurrentZone, ""))));
            var markStart = B("Mark as start", () => MarkStart());
            ToolTip.SetTip(markStart, "Moves the new-game start to the square selected in the view (level 1 only), as the editor's start tool does: saved in UW.EXE at once, the original kept as UW.EXE.ORIG.");
            var markGoal = B("Mark as goal", () => MarkGoal());
            ToolTip.SetTip(markGoal, "Puts a square that ends the game on the square selected in the view: a move trigger and hack trap 0x3F, as on level 9 (52, 51), which stays. One undo step (Edit ▸ Undo).");
            left.Children.Add(Row(markStart, markGoal));
            left.Children.Add(startGoalText);
            left.Children.Add(H("PAINTING"));
            left.Children.Add(Row(paintButton, fillBox));
            left.Children.Add(Row(B("Fill the area under the selected tile as zone", FillSelected), fillZone));
            climbBox.Value = main.ZoneClimb;
            climbBox.ValueChanged += (s, e) => main.ZoneClimb = (int)(climbBox.Value ?? ZoneFlood.DefaultMaxClimb);
            left.Children.Add(Row(new TextBlock { Text = "Highest climb (floor steps)", VerticalAlignment = Avalonia.Layout.VerticalAlignment.Center }, climbBox));
            left.Children.Add(new TextBlock
            {
                FontSize = 11, Opacity = 0.7, TextWrapping = TextWrapping.Wrap,
                Text = "A fill moves left, right, up and down through the tiles of the start tile's zone. Walls, the wall side of diagonal tiles and tiles with a door (any kind, open or closed: they stay out) stop it, and so does going up more than the highest climb; going down is always fine, so a fill from above a drop reaches below it but not the other way.",
            });
            left.Children.Add(Row(B("Undo painting", () => { z.Undo(); status.Text = "Undone."; }), overlayBox));
            left.Children.Add(levelInfo);
            left.Children.Add(H("NOTES"));
            left.Children.Add(notesBox);
            var leftScroll = new ScrollViewer { Content = left, HorizontalScrollBarVisibility = Avalonia.Controls.Primitives.ScrollBarVisibility.Disabled };

            // right: key items
            var right = new DockPanel { Margin = new Thickness(0, 10, 0, 0) };
            var rhead = new StackPanel { Spacing = 4 };
            rhead.Children.Add(itemsTitle);
            rhead.Children.Add(new TextBlock { FontSize = 11, Opacity = 0.7, TextWrapping = TextWrapping.Wrap, Text = "Keys, talismans, quest items, runestones, creatures that talk and those whose death the game checks; plants, bridges and furniture are left out. Double-click to go there." });
            DockPanel.SetDock(rhead, Dock.Top);
            right.Children.Add(rhead);
            var rfoot = new StackPanel { Spacing = 6, Margin = new Thickness(0, 8, 0, 0) };
            rfoot.Children.Add(Row(B("Go to", GoToItem), B("Mark the selected object as key", () => MarkSelected(true)), B("Not a key item", () => MarkSelected(false)), B("Unmark", () => MarkSelected(null))));
            rfoot.Children.Add(Row(B("Add them to the zone's grants", AddGrants)));
            rfoot.Children.Add(grantsText);
            DockPanel.SetDock(rfoot, Dock.Bottom);
            right.Children.Add(rfoot);
            right.Children.Add(itemList);

            // three columns, each resizable by dragging the splitters between them
            var cols = new Grid();
            cols.ColumnDefinitions.Add(new ColumnDefinition(380, GridUnitType.Pixel) { MinWidth = 260 });
            cols.ColumnDefinitions.Add(new ColumnDefinition(8, GridUnitType.Pixel));
            cols.ColumnDefinitions.Add(new ColumnDefinition(1, GridUnitType.Star) { MinWidth = 220 });
            cols.ColumnDefinitions.Add(new ColumnDefinition(8, GridUnitType.Pixel));
            cols.ColumnDefinitions.Add(new ColumnDefinition(1, GridUnitType.Star) { MinWidth = 300 });
            cols.Children.Add(leftScroll);
            Grid.SetColumn(right, 2);
            cols.Children.Add(right);
            var links = LinksColumn();
            Grid.SetColumn(links, 4);
            cols.Children.Add(links);
            foreach (int c in new[] { 1, 3 })
            {
                var split = new GridSplitter { ResizeDirection = GridResizeDirection.Columns, Background = Brushes.Transparent, Width = 8 };
                ToolTip.SetTip(split, "Drag to widen or narrow the columns.");
                Grid.SetColumn(split, c);
                cols.Children.Add(split);
            }
            Splitters = cols;
            dock.Children.Add(cols);
            Content = dock;

            z.Changed += OnChanged;
            Closed += (s, e) => { z.Changed -= OnChanged; z.Painting = false; main.ZonePaintingChanged(); };
            ManualWindow.Attach(this, "Randomizer");
            AddHandler(KeyDownEvent, (s, e) =>
            {
                if (e.Key == Key.S && (e.KeyModifiers & KeyModifiers.Control) != 0) { SaveFile(false); e.Handled = true; }
            }, Avalonia.Interactivity.RoutingStrategies.Tunnel);
            Refresh();
        }

        static TextBlock H(string t) { return new TextBlock { Classes = { "h" }, Text = t }; }

        Button AutozoneButton()
        {
            var b = B("Autozone…", () => main.ShowAutoZones());
            ToolTip.SetTip(b, "Opens the Automatic zones window: Build and Use puts automatic zones here, in place of this zoning.");
            return b;
        }

        static Button B(string text, Action a)
        {
            var b = new Button { Content = text };
            b.Click += (s, e) => a();
            return b;
        }

        static WrapPanel Row(params Control[] cs)
        {
            var w = new WrapPanel();
            foreach (var c in cs)
            {
                if (c == null) continue;
                c.Margin = new Thickness(0, 0, 6, 4);
                c.VerticalAlignment = VerticalAlignment.Center;
                w.Children.Add(c);
            }
            return w;
        }

        void Try(Action a)
        {
            try { a(); } catch (Exception ex) { status.Text = ex.Message; }
        }

        async void Confirm(Action a)
        {
            if (z.Dirty)
            {
                string r = await MessageWindow.Ask(this, "Unsaved zoning", "The zoning has unsaved changes. Replace it anyway?", "Replace", "Cancel");
                if (r != "Replace") return;
            }
            Try(a);
        }

        void OnChanged(string what)
        {
            if (what == "notes") return;
            // a link edited: the list's lines only, so the link being edited keeps its controls
            if (what == "links") { RefreshLinks(false); RefreshStartGoal(); return; }
            Refresh();
        }

        IBrush Brush(int id)
        {
            uint c = z.Set.ColorOf(id);
            return new SolidColorBrush(Color.FromArgb(255, (byte)(c >> 16), (byte)(c >> 8), (byte)c));
        }

        /// <summary>Shows the zoning as it now is.</summary>
        public void Refresh()
        {
            filling = true;
            var counts = new Dictionary<int, int>();
            foreach (var t in z.Set.Tiles) if (t != null) foreach (var b in t) { int c; counts.TryGetValue(b, out c); counts[b] = c + 1; }
            int startZone = StartZone();
            var goalZones = new HashSet<int>(Ends().Select(e => (int)z.Set.ZoneAt(e.Level, e.X, e.Y)).Where(v => v > ZoneSet.Unassigned));
            var fresh = new List<ZoneRow>();
            foreach (var zone in z.Set.Zones.Where(x => x.Id > ZoneSet.Unassigned))
            {
                int n;
                counts.TryGetValue(zone.Id, out n);
                fresh.Add(new ZoneRow { Id = zone.Id, Label = string.Format("{0}  {1}  ({2} tiles{3}{4})", zone.Id, zone.Name, n, zone.Id == startZone ? ", start" : "", goalZones.Contains(zone.Id) ? ", ends the game" : ""), Color = z.Set.ColorOf(zone.Id), Swatch = Brush(zone.Id) });
            }
            // the list is rebuilt only when it changed: rebuilding it while a zone is being picked from it leaves the box empty
            bool same = rows.Count == fresh.Count && rows.Zip(fresh, (a, b) => a.Id == b.Id && a.Label == b.Label && a.Color == b.Color).All(x => x);
            if (!same) { rows.Clear(); foreach (var r in fresh) rows.Add(r); }
            var want = rows.FirstOrDefault(r => r.Id == z.CurrentZone);
            if (!ReferenceEquals(zoneBox.SelectedItem, want)) zoneBox.SelectedItem = want;
            filling = false;
            var cur = z.Set.GetZone(z.CurrentZone);
            nameBox.Text = cur?.Name ?? "";
            colorBox.Text = ZoneSet.ColorString(z.Set.ColorOf(z.CurrentZone));
            swatch.Background = Brush(z.CurrentZone);
            notesBox.Text = cur?.Notes ?? "";
            fillZone.Value = z.CurrentZone;
            fileText.Text = (z.FileExists ? z.Path : z.Path + " (not saved yet)") + (z.Dirty ? " · unsaved changes" : "");
            var lv = main.ViewModel.Level;
            if (lv != null)
            {
                int un = z.Unassigned(lv.Level);
                levelInfo.Text = z.Set.Tiles[lv.Level - 1] == null
                    ? string.Format("Level {0} has no zone map yet: painting starts one from its tiles.", lv.Level)
                    : string.Format("Level {0}: {1} open tile{2} not in a zone (magenta).", lv.Level, un, un == 1 ? "" : "s");
            }
            RefreshItems();
            RefreshSplit();
            RefreshLinks(true);
            RefreshStartGoal();
        }

        void RefreshItems()
        {
            List<KeyItem> items;
            try { items = z.Items.Where(i => i.Zone == z.CurrentZone).OrderBy(i => i.Level).ThenBy(i => i.Kind).ThenBy(i => i.Index).ToList(); }
            catch (Exception ex) { items = new List<KeyItem>(); status.Text = "Could not read the levels: " + ex.Message; }
            itemsTitle.Text = string.Format("Key items in zone {0} ({1})", z.CurrentZone, items.Count);
            itemList.ItemsSource = items;
            var grants = z.Set.Placements.Where(p => p.Zone == z.CurrentZone).Select(p => p.Grants + (string.IsNullOrEmpty(p.Requires) ? "" : " (if " + p.Requires + ")")).ToList();
            grantsText.Text = grants.Count == 0 ? "The zone grants nothing yet." : "The zone grants: " + string.Join(", ", grants);
        }

        void AddZone()
        {
            Try(() =>
            {
                var zone = z.AddZone(nameBox.Text != null && nameBox.Text.Trim().Length > 0 && z.Set.GetZone(z.CurrentZone)?.Name != nameBox.Text ? nameBox.Text : null);
                status.Text = "Zone " + zone.Id + " added; it is the zone painted now.";
            });
        }

        async void RemoveZone()
        {
            var cur = z.Set.GetZone(z.CurrentZone);
            if (cur == null) return;
            int tiles = z.Set.TileCount(cur.Id), links = z.Set.Links.Count(k => k.From == cur.Id || k.To == cur.Id);
            string r = await MessageWindow.Ask(this, "Remove a zone", string.Format("Remove zone {0} \"{1}\"? Its {2} tiles become unassigned, and its {3} links and its grants go.", cur.Id, cur.Name, tiles, links), "Remove", "Cancel");
            if (r != "Remove") return;
            z.RemoveZone(cur.Id);
            status.Text = "Zone " + cur.Id + " removed (Undo painting brings the tiles back, not the links).";
        }

        void FillSelected()
        {
            var vm = main.ViewModel;
            if (vm.Level == null || vm.SelectedTileX < 0) { status.Text = "Select a tile in the view first."; return; }
            int n = z.FillArea(vm.Level.Level, vm.SelectedTileX, vm.SelectedTileY, (int)(fillZone.Value ?? z.CurrentZone));
            status.Text = n == 0 ? "Nothing to fill there (rock, a door tile, or already that zone)." : n + " tiles filled.";
        }

        void GoToItem()
        {
            if (itemList.SelectedItem is KeyItem k) main.GoToObject(k.Level, k.Index);
        }

        void MarkSelected(bool? important)
        {
            var vm = main.ViewModel;
            if (vm.Level == null || vm.SelectedObject < 0)
            {
                if (itemList.SelectedItem is KeyItem k && important != true) { z.Mark(k.Level, k.Index, important); status.Text = "Marked."; }
                else status.Text = "Select an object in the view or the list first.";
                return;
            }
            z.Mark(vm.Level.Level, vm.SelectedObject, important);
            status.Text = string.Format("#{0} {1}.", vm.SelectedObject, important == true ? "is a key item now" : important == false ? "is left out of the key items" : "is unmarked");
        }

        void AddGrants()
        {
            var items = z.Items.Where(i => i.Zone == z.CurrentZone).ToList();
            var add = KeyItems.SuggestPlacements(z.Set, items);
            z.Set.Placements.AddRange(add);
            z.LinkChanged();
            status.Text = add.Count + " grant(s) added to zone " + z.CurrentZone + ".";
        }

        async void OpenFile()
        {
            var f = await StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
            {
                Title = "Open a zone file", AllowMultiple = false,
                FileTypeFilter = new[] { new FilePickerFileType("Zone file") { Patterns = new[] { "*.json" } } },
            });
            var p = f.FirstOrDefault()?.TryGetLocalPath();
            if (p != null) Try(() => { z.Open(p); status.Text = "Opened " + p + "."; });
        }

        async void SaveFile(bool ask)
        {
            string path = z.Path;
            if (ask || z.AskForName)
            {
                var f = await StorageProvider.SaveFilePickerAsync(new FilePickerSaveOptions
                {
                    Title = "Save the zone file", SuggestedFileName = Path.GetFileName(z.Path), DefaultExtension = "json",
                    FileTypeChoices = new[] { new FilePickerFileType("Zone file") { Patterns = new[] { "*.json" } } },
                });
                path = f?.TryGetLocalPath();
                if (path == null) return;
            }
            Try(() => { z.Save(path); status.Text = "Saved " + path + "."; });
        }

        async void ImportPictures()
        {
            var fs = await StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
            {
                Title = "Zone maps: one 64x64 picture a level, named with the level (map0.png is level 1)", AllowMultiple = true,
                FileTypeFilter = new[] { new FilePickerFileType("Indexed pictures") { Patterns = new[] { "*.png", "*.bmp" } } },
            });
            var said = new List<string>();
            z.Snapshot();
            foreach (var f in fs)
            {
                var p = f.TryGetLocalPath();
                if (p == null) continue;
                int level = LevelOfFile(Path.GetFileNameWithoutExtension(p));
                if (level < 1 || level > z.Set.Levels) { said.Add(Path.GetFileName(p) + ": no level number in the name"); continue; }
                string err = z.Set.ImportPicture(level, File.ReadAllBytes(p));
                said.Add(Path.GetFileName(p) + (err == null ? " → level " + level : ": " + err));
            }
            z.LinkChanged();
            main.ZonesChanged("tiles");
            status.Text = string.Join("; ", said);
        }

        /// <summary>"map0" is level 1 (Gimp's maps count from 0), "level3" or "L3" level 3.</summary>
        public static int LevelOfFile(string name)
        {
            var digits = new string(name.Reverse().TakeWhile(char.IsDigit).Reverse().ToArray());
            int n;
            if (!int.TryParse(digits, out n)) return -1;
            return name.StartsWith("map", StringComparison.OrdinalIgnoreCase) ? n + 1 : n;
        }

        async void ExportPictures()
        {
            var f = await StorageProvider.OpenFolderPickerAsync(new FolderPickerOpenOptions { Title = "A folder for map0.png, map1.png …" });
            var p = f.FirstOrDefault()?.TryGetLocalPath();
            if (p == null) return;
            int n = 0;
            for (int l = 1; l <= z.Set.Levels; l++)
            {
                if (z.Set.Tiles[l - 1] == null) continue;
                File.WriteAllBytes(Path.Combine(p, "map" + (l - 1) + ".png"), z.Set.ExportPicture(l));
                n++;
            }
            status.Text = n + " maps written to " + p + " (indexed PNG: the index is the zone id, the palette the zone colours).";
        }
    }
}
