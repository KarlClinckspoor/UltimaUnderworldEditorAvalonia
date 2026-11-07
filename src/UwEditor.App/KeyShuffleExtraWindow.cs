using System;
using System.Collections.Generic;
using System.Linq;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using UwEditor.Core;
using UwEditor.Core.Randomizer;

namespace UwEditor.App
{
    /// <summary>
    /// The key item scatter's "Additional items to include in the shuffle": every kind of item the
    /// player can pick up that lies in the levels, by class, each with its count. A class's box
    /// ticks all its kinds. The choice is the set of item ids it edits in place.
    /// </summary>
    public sealed class KeyShuffleExtraWindow : Window
    {
        readonly HashSet<int> chosen;
        readonly List<ExtraItem> offered;
        readonly Dictionary<int, CheckBox> itemBoxes = new Dictionary<int, CheckBox>();
        readonly Dictionary<string, CheckBox> classBoxes = new Dictionary<string, CheckBox>();
        readonly TextBlock summary = new TextBlock { FontSize = 12, Opacity = 0.8, VerticalAlignment = VerticalAlignment.Center };
        bool filling;

        /// <summary>Raised whenever the choice changes.</summary>
        public event Action Changed;

        public KeyShuffleExtraWindow(GameSession s, Func<int, LevelModel> level, HashSet<int> chosen)
        {
            this.chosen = chosen;
            offered = ExtraItems.Offered(s, level);
            Title = "Additional items to include in the shuffle";
            Width = 860; Height = 760; MinWidth = 560; MinHeight = 420;
            WindowStartupLocation = WindowStartupLocation.CenterOwner;

            var list = new StackPanel { Spacing = 10 };
            foreach (var cls in ExtraItems.Classes)
            {
                string id = cls[0];
                var items = offered.Where(x => x.Class == id).ToList();
                if (items.Count == 0) continue;
                var head = new CheckBox { Content = cls[1] + " (" + items.Sum(x => x.Count) + ")", FontWeight = FontWeight.SemiBold, IsThreeState = true };
                head.IsCheckedChanged += (o, e) =>
                {
                    if (filling) return;
                    // a click on a partly ticked class ticks it all
                    bool on = head.IsChecked != false;
                    foreach (var x in items) { if (on) chosen.Add(x.Item); else chosen.Remove(x.Item); }
                    Refresh();
                };
                classBoxes[id] = head;
                var wrap = new WrapPanel { Margin = new Thickness(24, 2, 0, 0) };
                foreach (var x in items)
                {
                    var item = x.Item;
                    var box = new CheckBox { Content = string.Format("{0} ×{1}", x.Name, x.Count), Width = 250, FontSize = 12 };
                    ToolTip.SetTip(box, string.Format("Item 0x{0:X3}: {1} in the levels.", x.Item, x.Count));
                    box.IsCheckedChanged += (o, e) =>
                    {
                        if (filling) return;
                        if (box.IsChecked == true) chosen.Add(item); else chosen.Remove(item);
                        Refresh();
                    };
                    itemBoxes[item] = box;
                    wrap.Children.Add(box);
                }
                var group = new StackPanel();
                group.Children.Add(head);
                group.Children.Add(wrap);
                list.Children.Add(group);
            }

            var swap = new Button { Content = "The item swap's kinds" };
            ToolTip.SetTip(swap, "Ticks what the old \"Everything else as filler\" moved: weapons, armour, rings, wands, potions, food and lights.");
            swap.Click += (o, e) => { foreach (var x in offered.Where(x => ExtraItems.SwapClasses.Contains(x.Class))) chosen.Add(x.Item); Refresh(); };
            var all = new Button { Content = "All" };
            all.Click += (o, e) => { foreach (var x in offered) chosen.Add(x.Item); Refresh(); };
            var none = new Button { Content = "None" };
            none.Click += (o, e) => { chosen.Clear(); Refresh(); };
            var close = new Button { Content = "Close", FontWeight = FontWeight.SemiBold };
            close.Click += (o, e) => Close();

            var top = new StackPanel { Spacing = 6, Margin = new Thickness(0, 0, 0, 8) };
            top.Children.Add(new TextBlock { Text = "Additional items to include in the shuffle", FontSize = 16, FontWeight = FontWeight.SemiBold });
            top.Children.Add(new TextBlock
            {
                TextWrapping = TextWrapping.Wrap, FontSize = 12, Opacity = 0.75,
                Text = "The kinds ticked take part as filler: they trade places with the key items and with each other, and their places become places too. " +
                       "Key items (keys, runes, ability items, ingredients, plot items, talismans) move by their category above, whatever is ticked here. " +
                       "A container takes what it holds along, and only to a place on its own level. A container holding something the logic needs stays where it is, and what it holds takes part instead. " +
                       "Objects that stay for their own reasons (a trap names them, an NPC with a conversation carries them…) still stay; the log lists them.",
            });
            var buttons = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8 };
            buttons.Children.Add(swap); buttons.Children.Add(all); buttons.Children.Add(none); buttons.Children.Add(summary);
            top.Children.Add(buttons);
            var bottom = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right, Margin = new Thickness(0, 8, 0, 0) };
            bottom.Children.Add(close);

            var dock = new DockPanel { Margin = new Thickness(16) };
            DockPanel.SetDock(top, Dock.Top);
            DockPanel.SetDock(bottom, Dock.Bottom);
            dock.Children.Add(top);
            dock.Children.Add(bottom);
            dock.Children.Add(new ScrollViewer { Content = list });
            Content = dock;
            Refresh(false);
        }

        /// <summary>The kinds offered (for tests).</summary>
        public IReadOnlyList<ExtraItem> Offered { get { return offered; } }

        /// <summary>Ticks a class's kinds, as its box does.</summary>
        public void SetClass(string id, bool on)
        {
            foreach (var x in offered.Where(x => x.Class == id)) { if (on) chosen.Add(x.Item); else chosen.Remove(x.Item); }
            Refresh();
        }

        void Refresh(bool raise = true)
        {
            filling = true;
            foreach (var kv in itemBoxes) kv.Value.IsChecked = chosen.Contains(kv.Key);
            foreach (var kv in classBoxes)
            {
                var items = offered.Where(x => x.Class == kv.Key).ToList();
                int n = items.Count(x => chosen.Contains(x.Item));
                kv.Value.IsChecked = n == 0 ? false : n == items.Count ? true : (bool?)null;
            }
            filling = false;
            summary.Text = Summary(offered, chosen);
            if (raise) Changed?.Invoke();
        }

        /// <summary>"none", or how many kinds and objects are ticked.</summary>
        public static string Summary(IEnumerable<ExtraItem> offered, HashSet<int> chosen)
        {
            var on = offered.Where(x => chosen.Contains(x.Item)).ToList();
            return on.Count == 0 ? "none" : string.Format("{0} kind{1}, {2} object{3}", on.Count, on.Count == 1 ? "" : "s", on.Sum(x => x.Count), on.Sum(x => x.Count) == 1 ? "" : "s");
        }
    }
}
