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

        readonly Action save;
        readonly Func<IgnoredObject, string> reject;

        /// <summary>The item tools' shared ignore list.</summary>
        public IgnoreListPanel(MainWindow main, GameSession s, Func<int, LevelModel> level, List<IgnoredObject> list)
            : this(main, s, list, ItemShuffle.Candidates(s, level), () => IgnoreStore.Save(s, list),
                  "The item shuffle and the item swap both leave these objects alone (a wand's spell object goes with its wand); the list is shared. Wands enchanted directly are ignored at first: Ultima Underworld's bullfrog wand, Underworld II's slightly worn wand of Telekinesis and wand of Light.",
                  "Objects the tools touch", "Ignored", "Ignore →", "← Stop ignoring", "Ignore the object selected in the editor", null)
        {
        }

        /// <summary>A pick list: candidates on the left, the chosen on the right. save runs after every change; reject says why a selected object cannot be added (or null).</summary>
        public IgnoreListPanel(MainWindow main, GameSession s, List<IgnoredObject> list, List<IgnoredObject> candidates, Action save,
            string introText, string leftTitle, string rightTitle, string addText, string removeText, string selectedText, Func<IgnoredObject, string> reject)
        {
            this.main = main;
            session = s;
            this.list = list;
            this.save = save;
            this.reject = reject;
            all = candidates;
            left.Bind(ListBox.BackgroundProperty, left.GetResourceObservable("Panel"));
            right.Bind(ListBox.BackgroundProperty, right.GetResourceObservable("Panel"));
            filter.TextChanged += (o, e) => Refresh();
            var addB = new Button { Content = addText };
            addB.Click += (o, e) => Add(left.SelectedItem as IgnoredObject);
            left.DoubleTapped += (o, e) => Add(left.SelectedItem as IgnoredObject);
            var remB = new Button { Content = removeText };
            remB.Click += (o, e) =>
            {
                if (right.SelectedItem is IgnoredObject c) { list.RemoveAll(x => x.Level == c.Level && x.Index == c.Index); Save(); }
            };
            var selB = new Button { Content = selectedText };
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
                Text = introText,
            };
            SetColumnSpan(intro, 3); Children.Add(intro);
            var lh = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8, Margin = new Thickness(0, 4) };
            lh.Children.Add(new TextBlock { Text = leftTitle, FontWeight = FontWeight.SemiBold, VerticalAlignment = VerticalAlignment.Center });
            lh.Children.Add(filter);
            SetRow(lh, 1); Children.Add(lh);
            var rh = new TextBlock { Text = rightTitle, FontWeight = FontWeight.SemiBold, Margin = new Thickness(0, 8) };
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
            string why = reject?.Invoke(c);
            if (why != null) { status.Text = why; return; }
            var known = all.FirstOrDefault(x => x.Level == c.Level && x.Index == c.Index);
            if (known != null) c = known;
            list.Add(c);
            status.Text = "Added: " + c.Label;
            Save();
        }

        void Save()
        {
            save();
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
