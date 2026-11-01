using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using UwEditor.Core;
using UwEditor.Core.Randomizer;

namespace UwEditor.App
{
    /// <summary>The objects the item shuffle and the item swap leave alone, one list per game, kept in the settings folder.</summary>
    public static class IgnoreStore
    {
        static string PathOf(GameSession s) { return Path.Combine(Settings.Folder, "item-ignore-" + (s.IsUw2 ? "uw2" : "uw1") + ".json"); }

        /// <summary>The saved list, or the defaults (the wands enchanted directly) the first time.</summary>
        public static List<IgnoredObject> Load(GameSession s, Func<int, LevelModel> level)
        {
            try
            {
                string p = PathOf(s);
                if (File.Exists(p)) { var l = JsonSerializer.Deserialize<List<IgnoredObject>>(File.ReadAllText(p)); if (l != null) return l; }
            }
            catch (Exception) { }
            return ItemShuffle.DefaultIgnored(s, level);
        }

        public static void Save(GameSession s, List<IgnoredObject> list)
        {
            try { Directory.CreateDirectory(Settings.Folder); File.WriteAllText(PathOf(s), JsonSerializer.Serialize(list, new JsonSerializerOptions { WriteIndented = true })); } catch (Exception) { }
        }
    }

    /// <summary>Picks the objects to ignore: everything the tools touch on the left (with a filter), the ignored ones on the right.</summary>
    public sealed class IgnoreListPanel : Grid
    {
        readonly MainWindow main;
        readonly GameSession session;
        readonly List<IgnoredObject> list;
        readonly List<IgnoredObject> all;
        readonly TextBox filter = new TextBox { Watermark = "Filter: a name, a spell, L4…", Width = 300 };
        readonly ListBox left = new ListBox(), right = new ListBox();
        readonly TextBlock status = new TextBlock { FontSize = 12, Opacity = 0.75, VerticalAlignment = VerticalAlignment.Center };
        /// <summary>The list changed (it is saved already).</summary>
        public event Action Changed;
        public int Count { get { return list.Count; } }

        public IgnoreListPanel(MainWindow main, GameSession s, Func<int, LevelModel> level, List<IgnoredObject> list)
        {
            this.main = main;
            session = s;
            this.list = list;
            all = ItemShuffle.Candidates(s, level);
            left.Bind(ListBox.BackgroundProperty, left.GetResourceObservable("Panel"));
            right.Bind(ListBox.BackgroundProperty, right.GetResourceObservable("Panel"));
            filter.TextChanged += (o, e) => Refresh();
            var addB = new Button { Content = "Ignore →" };
            addB.Click += (o, e) => Add(left.SelectedItem as IgnoredObject);
            left.DoubleTapped += (o, e) => Add(left.SelectedItem as IgnoredObject);
            var remB = new Button { Content = "← Stop ignoring" };
            remB.Click += (o, e) =>
            {
                if (right.SelectedItem is IgnoredObject c) { list.RemoveAll(x => x.Level == c.Level && x.Index == c.Index); Save(); }
            };
            var selB = new Button { Content = "Ignore the object selected in the editor" };
            selB.Click += (o, e) =>
            {
                var vm = main.ViewModel;
                if (vm.Level == null || vm.SelectedObject < 0) { status.Text = "Select an object in the editor first."; return; }
                var m = vm.Edits.Model(vm.Level.Level);
                var ob = m.Objects[vm.SelectedObject];
                Add(new IgnoredObject { Level = vm.Level.Level, Index = ob.Index, Label = ItemShuffle.Describe(session, m, ob) });
            };
            var goB = new Button { Content = "Go to" };
            goB.Click += (o, e) => { var c = (right.SelectedItem ?? left.SelectedItem) as IgnoredObject; if (c != null) main.GoToObject(c.Level, c.Index); };

            ColumnDefinitions = new ColumnDefinitions("*,Auto,*");
            RowDefinitions = new RowDefinitions("Auto,Auto,*,Auto");
            var intro = new TextBlock
            {
                TextWrapping = TextWrapping.Wrap, FontSize = 12, Opacity = 0.7, Margin = new Thickness(0, 2, 0, 6),
                Text = "The item shuffle and the item swap both leave these objects alone (a wand's spell object goes with its wand); the list is shared. Wands enchanted directly are ignored at first: Ultima Underworld's bullfrog wand, Underworld II's slightly worn wand of Telekinesis and wand of Light.",
            };
            SetColumnSpan(intro, 3); Children.Add(intro);
            var lh = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8, Margin = new Thickness(0, 4) };
            lh.Children.Add(new TextBlock { Text = "Objects the tools touch", FontWeight = FontWeight.SemiBold, VerticalAlignment = VerticalAlignment.Center });
            lh.Children.Add(filter);
            SetRow(lh, 1); Children.Add(lh);
            var rh = new TextBlock { Text = "Ignored", FontWeight = FontWeight.SemiBold, Margin = new Thickness(0, 8) };
            SetRow(rh, 1); SetColumn(rh, 2); Children.Add(rh);
            SetRow(left, 2); Children.Add(left);
            var mid = new StackPanel { Spacing = 8, Margin = new Thickness(10, 0), VerticalAlignment = VerticalAlignment.Center };
            mid.Children.Add(addB); mid.Children.Add(remB); mid.Children.Add(goB);
            SetRow(mid, 2); SetColumn(mid, 1); Children.Add(mid);
            SetRow(right, 2); SetColumn(right, 2); Children.Add(right);
            var foot = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 10, Margin = new Thickness(0, 6, 0, 0) };
            foot.Children.Add(selB); foot.Children.Add(status);
            SetRow(foot, 3); SetColumnSpan(foot, 3); Children.Add(foot);
            Margin = new Thickness(4, 8);
            Refresh();
        }

        void Add(IgnoredObject c)
        {
            if (c == null || list.Any(x => x.Level == c.Level && x.Index == c.Index)) return;
            list.Add(c);
            status.Text = "Ignored: " + c.Label;
            Save();
        }

        void Save()
        {
            IgnoreStore.Save(session, list);
            Refresh();
            Changed?.Invoke();
        }

        void Refresh()
        {
            string f = (filter.Text ?? "").Trim();
            var ign = new HashSet<long>(list.Select(x => (long)x.Level << 32 | (uint)x.Index));
            left.ItemsSource = all.Where(c => !ign.Contains((long)c.Level << 32 | (uint)c.Index) && (f.Length == 0 || c.Label.IndexOf(f, StringComparison.OrdinalIgnoreCase) >= 0)).ToList();
            right.ItemsSource = list.ToList();
        }
    }
}
