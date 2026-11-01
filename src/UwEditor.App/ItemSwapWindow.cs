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
    /// <summary>The swap window's settings, kept per game.</summary>
    public sealed class SwapSettings
    {
        public int Seed { get; set; } = 1;
        public bool AnyKind { get; set; }
        public bool IncludeInventories { get; set; } = true;
        public bool Weapons { get; set; } = true;
        public bool Armour { get; set; } = true;
        public bool Food { get; set; } = true;
        public bool Lights { get; set; } = true;
        public bool Tools { get; set; } = true;
        public bool Runes { get; set; } = true;
        public bool Wands { get; set; } = true;
        public bool Scrolls { get; set; } = true;
        public bool Potions { get; set; } = true;
        public bool Rings { get; set; } = true;
    }

    /// <summary>
    /// Tools ▸ Swap items: the existing objects of the ticked kinds trade places, within their
    /// kind or across all the ticked kinds. Nothing is made or removed and no weights apply.
    /// </summary>
    public sealed class ItemSwapWindow : Window
    {
        readonly MainWindow main;
        readonly GameSession session;
        readonly SwapSettings set;
        readonly List<IgnoredObject> ignored;
        readonly TextBox report = new TextBox { IsReadOnly = true, AcceptsReturn = true, TextWrapping = TextWrapping.NoWrap, FontFamily = new FontFamily("Cascadia Mono,Consolas,DejaVu Sans Mono,monospace"), FontSize = 12 };
        readonly NumericUpDown seed = new NumericUpDown { Minimum = 0, Maximum = int.MaxValue, Increment = 1, FormatString = "0", Width = 150 };
        readonly Button undoButton = new Button { Content = "Undo swap", IsEnabled = false };
        readonly List<ShuffleResult> applied = new List<ShuffleResult>();

        public ItemSwapWindow(MainWindow main, GameSession s)
        {
            this.main = main;
            session = s;
            Title = "Swap items";
            Width = 900; Height = 840; MinWidth = 640; MinHeight = 480;
            WindowStartupLocation = WindowStartupLocation.CenterOwner;
            set = Load();
            ignored = IgnoreStore.Load(s, Level);

            seed.Value = set.Seed;
            seed.ValueChanged += (o, e) => set.Seed = (int)(seed.Value ?? 1);
            var newSeed = new Button { Content = "New seed" };
            newSeed.Click += (o, e) => seed.Value = new Random().Next(1, int.MaxValue);
            var within = new RadioButton { Content = "Within each kind (a sword stays a weapon)", GroupName = "swapmode", IsChecked = !set.AnyKind };
            var across = new RadioButton { Content = "Across all the ticked kinds (a sword may land where a torch was)", GroupName = "swapmode", IsChecked = set.AnyKind };
            across.IsCheckedChanged += (o, e) => set.AnyKind = across.IsChecked == true;

            var top = new StackPanel { Spacing = 6, Margin = new Thickness(0, 0, 0, 8) };
            top.Children.Add(new TextBlock { Text = "Swap items", FontSize = 16, FontWeight = FontWeight.SemiBold });
            top.Children.Add(new TextBlock
            {
                TextWrapping = TextWrapping.Wrap, FontSize = 12, Opacity = 0.75,
                Text = "The existing objects trade places whole: item, quality, enchantment, quantity, owner. Nothing is made or removed, so every item keeps its count. " +
                       "Charged wands swap among themselves with their spells and charges (the charges live in an object inside the wand). Caliburn" + (s.IsUw2 ? ", the jewelled dagger" : "") +
                       ", talismans, quest items and the ignored objects stay. To change what items are, use Tools ▸ Shuffle item properties.",
            });
            var row = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8 };
            row.Children.Add(new TextBlock { Text = "Seed", VerticalAlignment = VerticalAlignment.Center });
            row.Children.Add(seed); row.Children.Add(newSeed);
            row.Children.Add(Check("Include creatures' inventories", set.IncludeInventories, x => set.IncludeInventories = x));
            top.Children.Add(row);
            top.Children.Add(within); top.Children.Add(across);

            var kinds = new WrapPanel { Margin = new Thickness(0, 6) };
            void K(string text, bool v, Action<bool> a) { var c = Check(text, v, a); c.Width = 160; kinds.Children.Add(c); }
            K("Weapons", set.Weapons, x => set.Weapons = x);
            K("Armour", set.Armour, x => set.Armour = x);
            K("Food", set.Food, x => set.Food = x);
            K("Lights", set.Lights, x => set.Lights = x);
            K("Tools", set.Tools, x => set.Tools = x);
            K("Runestones", set.Runes, x => set.Runes = x);
            K("Wands", set.Wands, x => set.Wands = x);
            K("Spell scrolls", set.Scrolls, x => set.Scrolls = x);
            K("Potions", set.Potions, x => set.Potions = x);
            K("Rings", set.Rings, x => set.Rings = x);
            top.Children.Add(new TextBlock { Text = "Kinds taking part", FontWeight = FontWeight.SemiBold, Margin = new Thickness(0, 6, 0, 0) });
            top.Children.Add(kinds);
            DockPanel.SetDock(top, Dock.Top);

            var preview = new Button { Content = "Preview" };
            preview.Click += (o, e) => report.Text = "Preview — nothing changed yet.\n\n" + Run().Report;
            var swap = new Button { Content = "Swap", FontWeight = FontWeight.SemiBold };
            swap.Click += (o, e) => Swap();
            undoButton.Click += (o, e) => UndoAll();
            var buttons = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8, Margin = new Thickness(0, 8, 0, 6) };
            buttons.Children.Add(preview); buttons.Children.Add(swap); buttons.Children.Add(undoButton);
            buttons.Children.Add(new TextBlock { Text = "Each swap is also one Edit ▸ Undo step per level; nothing is written to the game until you save.", VerticalAlignment = VerticalAlignment.Center, Opacity = 0.7, FontSize = 12 });
            var foot = new DockPanel { Height = 140 };
            DockPanel.SetDock(buttons, Dock.Top);
            foot.Children.Add(buttons); foot.Children.Add(report);
            DockPanel.SetDock(foot, Dock.Bottom);

            var ign = new IgnoreListPanel(main, s, Level, ignored);
            var ignHead = new TextBlock { Text = "Ignored objects (" + ignored.Count + ")", FontWeight = FontWeight.SemiBold, Margin = new Thickness(0, 4, 0, 0) };
            ign.Changed += () => ignHead.Text = "Ignored objects (" + ignored.Count + ")";
            var mid = new DockPanel();
            DockPanel.SetDock(ignHead, Dock.Top);
            mid.Children.Add(ignHead); mid.Children.Add(ign);

            var dock = new DockPanel { Margin = new Thickness(16) };
            dock.Children.Add(top); dock.Children.Add(foot); dock.Children.Add(mid);
            Content = dock;
            ManualWindow.Attach(this, "Swapping items");
            Closed += (o, e) => Save();
        }

        static CheckBox Check(string text, bool value, Action<bool> a)
        {
            var c = new CheckBox { Content = text, IsChecked = value };
            c.IsCheckedChanged += (o, e) => a(c.IsChecked == true);
            return c;
        }

        LevelModel Level(int l) { return main.ViewModel.Edits.Model(l); }

        string PathOf() { return Path.Combine(Settings.Folder, "item-swap-" + (session.IsUw2 ? "uw2" : "uw1") + ".json"); }

        SwapSettings Load()
        {
            try { if (File.Exists(PathOf())) return JsonSerializer.Deserialize<SwapSettings>(File.ReadAllText(PathOf())) ?? new SwapSettings(); } catch (Exception) { }
            return new SwapSettings { Seed = Environment.TickCount & 0x7FFFFFFF };
        }

        void Save()
        {
            try { Directory.CreateDirectory(Settings.Folder); File.WriteAllText(PathOf(), JsonSerializer.Serialize(set, new JsonSerializerOptions { WriteIndented = true })); } catch (Exception) { }
        }

        /// <summary>The swap as shuffle options: the mode, the kinds, the ignored objects.</summary>
        public ShuffleOptions Options()
        {
            var o = ShuffleOptions.Defaults(session);
            o.Seed = set.Seed;
            o.Mode = set.AnyKind ? "swap-any" : "swap-kind";
            o.IncludeInventories = set.IncludeInventories;
            o.ShuffleWeapons = set.Weapons; o.ShuffleArmour = set.Armour; o.ShuffleFood = set.Food; o.ShuffleLights = set.Lights; o.SwapTools = set.Tools;
            o.ShuffleRunes = set.Runes; o.ShuffleWands = set.Wands; o.ShuffleScrolls = set.Scrolls; o.ShufflePotions = set.Potions; o.ShuffleRings = set.Rings;
            o.Ignored = ignored;
            return o;
        }

        public SwapSettings Settings_ { get { return set; } }
        public bool CanUndoSwap { get { return undoButton.IsEnabled; } }

        ShuffleResult Run() { return ItemShuffle.Run(session, Level, Options()); }

        /// <summary>The Swap button.</summary>
        public void Swap()
        {
            var r = Run();
            if (r.Changed == 0) { report.Text = "Nothing to change.\n\n" + r.Report; return; }
            ItemShuffle.Apply(r, main.ViewModel.Edits, set.Seed);
            applied.Add(r);
            undoButton.IsEnabled = true;
            Save();
            report.Text = "Swapped. " + (applied.Count > 1 ? applied.Count + " swaps so far; Undo swap puts back all of them.\n\n" : "Undo swap puts everything back.\n\n") + r.Report;
            main.ShuffleApplied();
        }

        /// <summary>The Undo swap button: every swap from this window, put back.</summary>
        public void UndoAll()
        {
            int n = 0;
            var skipped = new SortedSet<int>();
            for (int i = applied.Count - 1; i >= 0; i--) { skipped.UnionWith(ItemShuffle.Revert(applied[i], main.ViewModel.Edits)); n += applied[i].Changed; }
            report.Text = string.Format("Undone: {0} swap(s), {1} object changes put back as they were.", applied.Count, n)
                + (skipped.Count == 0 ? "" : "\n\nLeft as they are, because they were edited since: level" + (skipped.Count == 1 ? " " : "s ") + string.Join(", ", skipped) + ". Edit ▸ Undo steps back there.");
            applied.Clear();
            undoButton.IsEnabled = false;
            main.ShuffleApplied();
        }
    }
}
