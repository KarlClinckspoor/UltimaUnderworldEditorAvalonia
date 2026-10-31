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
using UwEditor.Core.Randomizer;

namespace UwEditor.App
{
    /// <summary>A zone in the zones window's list.</summary>
    public sealed class ZoneRow
    {
        public int Id { get; set; }
        public string Label { get; set; }
        public IBrush Swatch { get; set; }
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
        readonly CheckBox fillBox = new CheckBox { Content = "A click fills the area (4-neighbour)" };
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
        bool filling;

        public ZoningState Zoning { get { return z; } }

        public ZonesWindow(MainWindow main, ZoningState zoning)
        {
            this.main = main;
            z = zoning;
            Title = "Zones (randomizer, work in progress) — " + zoning.Session.Title;
            Width = 1000; Height = 720; MinWidth = 760; MinHeight = 500;
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
                Text = "Work in progress. A zone is a part of the world that can be walked without meeting a requirement. Every tile has a zone id: 0 rock, 1 open but not in a zone yet (magenta), 2-255 the zones. The links between zones and what they need are drawn in the zone graph.",
            });
            var files = Row(B("New", () => Confirm(() => z.NewFile())), B("Open…", OpenFile), B("Save", () => SaveFile(false)), B("Save as…", () => SaveFile(true)),
                z.Session.IsUw2 ? null : B("Load the UW1 example", () => Confirm(() => z.LoadExample())), B("Import pictures…", ImportPictures), B("Export pictures…", ExportPictures),
                B("Zone graph…", () => main.ShowZoneGraph()));
            head.Children.Add(files);
            head.Children.Add(fileText);
            DockPanel.SetDock(head, Dock.Top);
            dock.Children.Add(head);
            DockPanel.SetDock(status, Dock.Bottom);
            status.Margin = new Thickness(0, 8, 0, 0);
            dock.Children.Add(status);

            // left: the zone
            var left = new StackPanel { Spacing = 8, Margin = new Thickness(0, 10, 16, 0) };
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
            left.Children.Add(H("PAINTING"));
            left.Children.Add(Row(paintButton, fillBox));
            left.Children.Add(Row(B("Fill the area under the selected tile as zone", FillSelected), fillZone));
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

            var cols = new Grid { ColumnDefinitions = new ColumnDefinitions("420,*") };
            cols.Children.Add(leftScroll);
            Grid.SetColumn(right, 1);
            cols.Children.Add(right);
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
            rows.Clear();
            foreach (var zone in z.Set.Zones.Where(x => x.Id > ZoneSet.Unassigned))
            {
                int n;
                counts.TryGetValue(zone.Id, out n);
                rows.Add(new ZoneRow { Id = zone.Id, Label = string.Format("{0}  {1}  ({2} tiles{3})", zone.Id, zone.Name, n, zone.Id == z.Set.StartZone ? ", start" : zone.Id == z.Set.GoalZone ? ", goal" : ""), Swatch = Brush(zone.Id) });
            }
            zoneBox.SelectedItem = rows.FirstOrDefault(r => r.Id == z.CurrentZone);
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
            status.Text = n == 0 ? "Nothing to fill there (rock, or already that zone)." : n + " tiles filled.";
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
            if (ask)
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
