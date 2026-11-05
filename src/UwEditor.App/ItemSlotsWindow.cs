using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using UwEditor.Core;
using UwEditor.Core.Conversations;
using UwEditor.Core.Editing;

namespace UwEditor.App
{
    /// <summary>
    /// Tools ▸ Item slots: every item id with what uses it (objects in the levels, conversations,
    /// the game's code, creature loot, combinations, quest items), to turn a slot nobody needs into
    /// a new item: its name and plural, its picture, its stats, and placing it.
    /// </summary>
    public sealed class ItemSlotsWindow : Window
    {
        readonly MainWindow main;
        readonly GameSession session;
        readonly ListBox list = new ListBox();
        readonly ListBox uses = new ListBox();
        readonly TextBox filter = new TextBox { Watermark = "Name or 0x id" };
        readonly CheckBox freeOnly = new CheckBox { Content = "Free slots only" };
        readonly TextBlock status = new TextBlock { FontSize = 12, Opacity = 0.8, TextWrapping = TextWrapping.Wrap };
        readonly TextBlock title = new TextBlock { FontSize = 16, FontWeight = FontWeight.SemiBold, TextWrapping = TextWrapping.Wrap };
        readonly TextBlock kind = new TextBlock { FontSize = 12, Opacity = 0.8, TextWrapping = TextWrapping.Wrap };
        readonly TextBlock verdict = new TextBlock { FontSize = 12, TextWrapping = TextWrapping.Wrap };
        readonly TextBlock stats = new TextBlock { FontSize = 12, TextWrapping = TextWrapping.Wrap, FontFamily = new FontFamily("monospace") };
        readonly Image picture = new Image { Width = 64, Height = 64 };
        readonly ComboBox article = new ComboBox { ItemsSource = new[] { "a", "an", "some", "(none)" }, Width = 90 };
        readonly TextBox nameBox = new TextBox { Width = 220, Watermark = "Name" };
        readonly TextBox pluralBox = new TextBox { Width = 220, Watermark = "Plural (leave empty for none)" };
        readonly StackPanel details = new StackPanel { Spacing = 8 };
        ItemUses.ConversationIndex conversations;
        ObjectTables tables;
        CombinationTable cmb;
        List<ItemUseReport> census = new List<ItemUseReport>();
        public ItemUseReport Selected { get; private set; }
        public Task Loading { get; private set; }

        public sealed class Row
        {
            public ItemUseReport Report { get; set; }
            public Bitmap Icon { get; set; }
            public string Text { get; set; }
            public string Summary { get; set; }
        }

        public ItemSlotsWindow(MainWindow main, int item)
        {
            this.main = main;
            session = main.ViewModel.Session;
            Title = "Item slots";
            Width = 1180; Height = 760; MinWidth = 800; MinHeight = 480;
            WindowStartupLocation = WindowStartupLocation.CenterOwner;
            RenderOptions.SetBitmapInterpolationMode(picture, BitmapInterpolationMode.None);
            list.ItemTemplate = new Avalonia.Controls.Templates.FuncDataTemplate<Row>((r, ns) =>
            {
                var p = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 6 };
                var img = new Image { Width = 20, Height = 20 };
                RenderOptions.SetBitmapInterpolationMode(img, BitmapInterpolationMode.None);
                img.Bind(Image.SourceProperty, new Avalonia.Data.Binding("Icon"));
                var t = new TextBlock { VerticalAlignment = VerticalAlignment.Center };
                t.Bind(TextBlock.TextProperty, new Avalonia.Data.Binding("Text"));
                var sm = new TextBlock { VerticalAlignment = VerticalAlignment.Center, Opacity = 0.65, FontSize = 12 };
                sm.Bind(TextBlock.TextProperty, new Avalonia.Data.Binding("Summary"));
                p.Children.Add(img); p.Children.Add(t); p.Children.Add(sm);
                return p;
            });
            list.SelectionChanged += (s, e) => { if (list.SelectedItem is Row r) ShowItem(r.Report.Item); };
            filter.TextChanged += (s, e) => Fill();
            freeOnly.IsCheckedChanged += (s, e) => Fill();
            uses.DoubleTapped += (s, e) => GoToUse();

            var left = new DockPanel { Width = 430, Margin = new Thickness(0, 0, 12, 0) };
            var lhead = new StackPanel { Spacing = 6, Margin = new Thickness(0, 0, 0, 6) };
            lhead.Children.Add(filter); lhead.Children.Add(freeOnly);
            DockPanel.SetDock(lhead, Dock.Top);
            left.Children.Add(lhead); left.Children.Add(list);

            var applyName = new Button { Content = "Set the name" };
            applyName.Click += (s, e) => SetName();
            var saveName = new Button { Content = "Save STRINGS.PAK" };
            saveName.Click += (s, e) => { var st = main.Strings(); if (st == null) return; try { st.Save(); status.Text = "STRINGS.PAK saved (the old file kept as .BKP)."; Refresh(); } catch (Exception ex) { status.Text = "Not saved: " + ex.Message; } };
            var editPic = new Button { Content = "Edit the picture…" };
            editPic.Click += (s, e) => { if (Selected != null) { var w = main.EditItemPicture(Selected.Item); if (w != null) w.Closed += (o, a) => ShowItem(Selected.Item); } };
            var editStats = new Button { Content = "Edit its stats (Object tables)…" };
            editStats.Click += (s, e) => OpenStats(false);
            var editCommon = new Button { Content = "Edit its common properties…" };
            editCommon.Click += (s, e) => OpenStats(true);
            var place = new Button { Content = "Place one on the selected tile" };
            place.Click += (s, e) => Place();

            var nameRow = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 6 };
            nameRow.Children.Add(article); nameRow.Children.Add(nameBox); nameRow.Children.Add(pluralBox); nameRow.Children.Add(applyName);
            var picRow = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 10 };
            var picBorder = new Border { Child = picture, Background = new SolidColorBrush(Color.Parse("#2A2A30")), Padding = new Thickness(4) };
            var picButtons = new StackPanel { Spacing = 6 };
            picButtons.Children.Add(editPic); picButtons.Children.Add(editStats); picButtons.Children.Add(editCommon); picButtons.Children.Add(place);
            picRow.Children.Add(picBorder); picRow.Children.Add(picButtons); picRow.Children.Add(stats);
            details.Children.Add(title);
            details.Children.Add(kind);
            details.Children.Add(verdict);
            details.Children.Add(new TextBlock { Text = "Name (STRINGS.PAK block 4: \"a_name&plural\")", FontWeight = FontWeight.SemiBold, Margin = new Thickness(0, 6, 0, 0) });
            details.Children.Add(nameRow);
            details.Children.Add(saveName);
            details.Children.Add(new TextBlock { Text = "Picture, stats and placing", FontWeight = FontWeight.SemiBold, Margin = new Thickness(0, 6, 0, 0) });
            details.Children.Add(picRow);
            details.Children.Add(new TextBlock { Text = "What uses it (double-click to go there)", FontWeight = FontWeight.SemiBold, Margin = new Thickness(0, 6, 0, 0) });
            var right = new DockPanel();
            DockPanel.SetDock(details, Dock.Top);
            right.Children.Add(details); right.Children.Add(uses);

            var head = new StackPanel { Spacing = 4, Margin = new Thickness(0, 0, 0, 10) };
            head.Children.Add(new TextBlock
            {
                TextWrapping = TextWrapping.Wrap, FontSize = 12, Opacity = 0.75,
                Text = "The game has 512 item ids and decides how each acts by its class (weapon, food, light…), so a new item is an existing id given a new name, picture and stats. Pick one nothing uses, or one whose uses you can change: objects placed in the levels can be moved or deleted; what a conversation or the game's code does with an id stays with that id.",
            });
            head.Children.Add(status);
            var dock = new DockPanel { Margin = new Thickness(14) };
            DockPanel.SetDock(head, Dock.Top); DockPanel.SetDock(left, Dock.Left);
            dock.Children.Add(head); dock.Children.Add(left); dock.Children.Add(right);
            Content = dock;
            ManualWindow.Attach(this, "Item slots");
            Loading = LoadAsync(item);
        }

        async Task LoadAsync(int item)
        {
            status.Text = "Reading the levels and every conversation…";
            var vm = main.ViewModel;
            var lib = main.Conversations();
            // the levels as edited are taken here, on the window's thread
            var levels = session.LevelSlots().Where(x => x.Present).ToDictionary(x => x.Level, x => vm.Edits != null ? vm.Edits.Model(x.Level) : session.LoadLevel(x.Level));
            try
            {
                await Task.Run(() =>
                {
                    conversations = ItemUses.IndexConversations(lib);
                    try { tables = ObjectTables.Load(session); } catch (Exception) { tables = null; }
                    try { cmb = CombinationTable.Load(session); } catch (Exception) { cmb = null; }
                    census = ItemUses.Census(session, l => levels[l], conversations, tables, cmb);
                });
            }
            catch (Exception ex) { status.Text = "Could not read everything: " + ex.Message; return; }
            status.Text = string.Format("{0} item ids; {1} free (nothing in the levels, conversations, code, loot or combinations uses them); {2} item calls read from the conversations.",
                census.Count, census.Count(r => r.Free), conversations.Calls.Count);
            Fill();
            ShowItem(item >= 0 ? item : census.FirstOrDefault(r => r.Free)?.Item ?? 0);
        }

        /// <summary>Reads the uses again (after placing or deleting objects, or saving strings).</summary>
        public void Refresh() { int keep = Selected?.Item ?? -1; Loading = LoadAsync(keep); }

        string NameOf(int item)
        {
            var st = main.Strings();
            var l = st?.Get(4);
            return l != null && item < l.Count ? l[item] : session.Strings.Get(4, item) ?? "";
        }

        void Fill()
        {
            string f = (filter.Text ?? "").Trim().ToLowerInvariant();
            var rows = new List<Row>();
            foreach (var r in census)
            {
                if (freeOnly.IsChecked == true && !r.Free) continue;
                string text = string.Format("0x{0:X3} {1}", r.Item, session.Names.Get(r.Item));
                if (f.Length > 0 && !text.ToLowerInvariant().Contains(f)) continue;
                rows.Add(new Row { Report = r, Icon = main.ViewModel.IconOf(r.Item), Text = text, Summary = "  " + r.Summary });
            }
            list.ItemsSource = rows;
            if (Selected != null) list.SelectedItem = rows.FirstOrDefault(x => x.Report.Item == Selected.Item);
        }

        /// <summary>Shows one item's slot.</summary>
        public void ShowItem(int item)
        {
            var r = census.FirstOrDefault(x => x.Item == item);
            if (r == null) return;
            Selected = r;
            title.Text = string.Format("0x{0:X3} {1}", item, session.Names.Get(item));
            kind.Text = "Class: " + ItemUses.ClassOf(item, session.IsUw2) + ". The game treats it as this kind of thing whatever its name.";
            verdict.Text = r.Free ? "Nothing uses this slot: it can become a new item of its class."
                : r.Bound ? "Careful: the game's code, a conversation or a quest uses this id. A new name and picture do not change what they do with it."
                : "Only objects, creature loot or combinations use it: move or delete those, or keep them as the new item.";
            verdict.Foreground = r.Free ? new SolidColorBrush(Color.Parse("#62C370")) : r.Bound ? new SolidColorBrush(Color.Parse("#FF8A65")) : new SolidColorBrush(Color.Parse("#FFC24A"));
            ParseName(NameOf(item));
            var f = session.Pictures.Get("OBJECTS.GR");
            var img = f != null && item < f.Count ? f.Get(item, session.Pictures.Palettes, f.DefaultPalette(item)) : null;
            picture.Source = img == null ? null : Pictures.ToBitmap(img);
            stats.Text = StatsText(item);
            uses.ItemsSource = r.Uses.Count == 0 ? new List<ItemUse> { new ItemUse { Kind = "", Text = "(nothing)" } } : r.Uses.OrderBy(u => u.Kind == "code" ? 0 : u.Kind == "named" ? 1 : u.Kind == "conversation" ? 2 : u.Kind == "creature" ? 3 : u.Kind == "combination" ? 4 : 5).ToList();
        }

        void ParseName(string s)
        {
            s = s ?? "";
            string art = "(none)", rest = s;
            int u = s.IndexOf('_');
            if (u >= 0) { art = s.Substring(0, u); rest = s.Substring(u + 1); if (art.Length == 0) art = "(none)"; }
            int amp = rest.IndexOf('&');
            nameBox.Text = amp >= 0 ? rest.Substring(0, amp) : rest;
            pluralBox.Text = amp >= 0 ? rest.Substring(amp + 1) : "";
            article.SelectedItem = new[] { "a", "an", "some" }.Contains(art) ? art : "(none)";
        }

        /// <summary>The name as STRINGS.PAK keeps it: article_name&amp;plural.</summary>
        public static string ComposeName(string art, string name, string plural)
        {
            art = art == "(none)" ? "" : art ?? "";
            string s = art + "_" + (name ?? "").Trim();
            if (!string.IsNullOrWhiteSpace(plural)) s += "&" + plural.Trim();
            return s;
        }

        /// <summary>Sets the selected item's name (unsaved, in STRINGS.PAK with the other string edits).</summary>
        public bool SetName(string art = null, string name = null, string plural = null)
        {
            if (Selected == null) return false;
            var st = main.Strings();
            if (st == null) return false;
            string text = ComposeName(art ?? article.SelectedItem as string, name ?? nameBox.Text, plural ?? pluralBox.Text);
            if ((name ?? nameBox.Text ?? "").Trim().Length == 0) { status.Text = "The name is empty."; return false; }
            try { st.Set(4, Selected.Item, text); }
            catch (Exception ex) { status.Text = "Not set: " + ex.Message; return false; }
            status.Text = string.Format("Item 0x{0:X3} is now \"{1}\" (not saved yet; use Save STRINGS.PAK or the strings window).", Selected.Item, text);
            return true;
        }

        string StatsText(int item)
        {
            if (tables == null) return "";
            var lines = new List<string>();
            foreach (var t in ObjectTables.Layout)
            {
                if (item < t.FirstItem || item >= t.FirstItem + t.Count) continue;
                int row = item - t.FirstItem;
                var fields = t.Schema.Fields.Take(t.IsComObj ? 6 : 8).Select(fd => fd.Name + " " + tables.Get(t, row, fd));
                lines.Add(t.Name + ": " + string.Join(", ", fields));
            }
            return string.Join("\n", lines);
        }

        void OpenStats(bool common)
        {
            if (Selected == null) return;
            var t = ObjectTables.Layout.FirstOrDefault(x => (common ? x.IsComObj : !x.IsComObj) && Selected.Item >= x.FirstItem && Selected.Item < x.FirstItem + x.Count);
            if (t == null) { status.Text = "OBJECTS.DAT has no table for this class (only its common properties)."; return; }
            var w = main.ShowObjectTables();
            if (w == null) return;
            Avalonia.Threading.Dispatcher.UIThread.Post(() => { try { w.SelectRow(t.Name, Selected.Item - t.FirstItem); } catch (Exception) { } });
        }

        void Place()
        {
            if (Selected == null) return;
            var vm = main.ViewModel;
            int x = vm.SelectedTileX, y = vm.SelectedTileY;
            if (x < 0) { status.Text = "Select a tile in the main window first."; return; }
            int made = -1;
            if (vm.Edit(e => made = e.AddObject(ObjectSpec.Default(Selected.Item, session.Kind), x, y), string.Format("Placed 0x{0:X3} on ({1}, {2}).", Selected.Item, x, y)))
            {
                vm.FlushRefresh();
                vm.Select(made, x, y, true);
                status.Text = vm.EditStatus;
                Refresh();
            }
            else status.Text = vm.EditStatus;
        }

        void GoToUse()
        {
            if (!(uses.SelectedItem is ItemUse u)) return;
            if (u.Kind == "level" && u.Level > 0)
            {
                if (u.TileX >= 0) main.GoToTile(u.Level, u.TileX, u.TileY); else main.ViewModel.LoadLevel(u.Level);
                main.ViewModel.FlushRefresh();
                main.ViewModel.Select(u.Index, -1, -1, true);
            }
            else if (u.Kind == "conversation" && u.Slot >= 0) main.ShowConversations(u.Slot);
        }
    }
}
