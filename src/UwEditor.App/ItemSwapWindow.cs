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
    /// <summary>The swap window's settings, kept per game (item-swap-uw1.json / -uw2.json), apart from the shuffle's.</summary>
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
        /// <summary>Skulls, bones and piles of bones (Garamon's bones never).</summary>
        public bool Bones { get; set; }
        /// <summary>The other scenery that can be picked up.</summary>
        public bool Scenery { get; set; }
        /// <summary>An item put in a spot takes the spot's owner (ShuffleOptions.SpotOwnership).</summary>
        public bool SpotOwnership { get; set; } = true;
        /// <summary>"game", "world", "level" or "near" (ShuffleOptions.SwapScope).</summary>
        public string Scope { get; set; } = "game";
        public int Range { get; set; } = 1;
        public bool SwapEnchantments { get; set; }
        public bool EnchantWeapons { get; set; } = true;
        public bool EnchantArmour { get; set; } = true;
        public bool EnchantScrolls { get; set; } = true;
        /// <summary>The starting equipment stays where it is.</summary>
        public bool KeepStart { get; set; } = true;
        /// <summary>The starting equipment; null until filled with the game's (ItemShuffle.StartingEquipment).</summary>
        public List<IgnoredObject> Start { get; set; }
        /// <summary>The swap's own ignore list; null until filled from the shuffle's list the first time.</summary>
        public List<IgnoredObject> Ignored { get; set; }
    }

    /// <summary>
    /// Item swap: the existing objects of the ticked kinds trade places, within their kind or
    /// across all the ticked kinds, within a level, a world, ±N levels or the whole game.
    /// Nothing is made or removed and no weights apply. Its settings, ignore list and starting
    /// equipment are its own.
    /// </summary>
    public sealed class ItemSwapWindow : Window
    {
        readonly MainWindow main;
        readonly GameSession session;
        readonly SwapSettings set;
        readonly TextBox report = ItemToolLayout.TextArea(false);
        readonly TextBox formula = ItemToolLayout.TextArea(true);
        readonly NumericUpDown seed = new NumericUpDown { Minimum = 0, Maximum = int.MaxValue, Increment = 1, FormatString = "0", Width = 150 };
        readonly Button undoButton = new Button { Content = "Undo swap", IsEnabled = false };
        readonly List<ShuffleResult> applied = new List<ShuffleResult>();
        readonly TabControl tabs = new TabControl();

        public ItemSwapWindow(MainWindow main, GameSession s)
        {
            this.main = main;
            session = s;
            Title = "Item swap";
            Width = 940; Height = 880; MinWidth = 660; MinHeight = 520;
            WindowStartupLocation = WindowStartupLocation.CenterOwner;
            set = Load();
            if (set.Ignored == null) set.Ignored = IgnoreStore.Load(s, Level).Select(x => new IgnoredObject { Level = x.Level, Index = x.Index, Label = x.Label }).ToList();
            if (set.Start == null) set.Start = ItemShuffle.StartingEquipment(s, Level);

            var header = new StackPanel { Spacing = 4, Margin = new Thickness(0, 0, 0, 6) };
            header.Children.Add(new TextBlock { Text = "Item swap", FontSize = 16, FontWeight = FontWeight.SemiBold });
            header.Children.Add(new TextBlock
            {
                TextWrapping = TextWrapping.Wrap, FontSize = 12, Opacity = 0.75,
                Text = "The existing objects trade places whole: item, quality, enchantment, quantity and (unless the spot keeps its own) owner. Nothing is made or removed, so every item keeps its count. " +
                       "Charged wands swap among themselves with their spells and charges. Caliburn" + (s.IsUw2 ? ", the jewelled dagger" : "") +
                       ", talismans, quest items, Garamon's bones, the ignored objects and the starting equipment stay. The same seed gives the same result.",
            });

            tabs.Items.Add(new TabItem { Header = "Settings", Content = new ScrollViewer { Content = SettingsTab(), Padding = new Thickness(4, 8, 16, 8) } });
            var ignTab = new TabItem { Header = "Ignored (" + set.Ignored.Count + ")" };
            ignTab.Content = new IgnoreListPanel(main, s, set.Ignored, ItemShuffle.Candidates(s, Level, true), () => { Save(); ignTab.Header = "Ignored (" + set.Ignored.Count + ")"; },
                "The item swap leaves these objects alone (a wand's spell object goes with its wand). This list is the swap's own; it started as a copy of the shuffle's. Wands enchanted directly are ignored at first: Ultima Underworld's bullfrog wand, Underworld II's slightly worn wand of Telekinesis and wand of Light.",
                "Objects the swap touches", "Ignored", "Ignore →", "← Stop ignoring", "Ignore the object selected in the editor", null);
            tabs.Items.Add(ignTab);
            var startTab = new TabItem { Header = "Starting equipment (" + set.Start.Count + ")" };
            Action startChanged = () => { Save(); startTab.Header = "Starting equipment (" + set.Start.Count + ")"; };
            Action buildStart = null;
            buildStart = () =>
            {
                var panel = new DockPanel();
                var top = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 10, Margin = new Thickness(4, 6, 4, 0) };
                top.Children.Add(Check("Keep the starting equipment where it is", set.KeepStart, x => set.KeepStart = x));
                var reset = new Button { Content = "The game's list" };
                ToolTip.SetTip(reset, "Sets the list to the objects found on the start tiles of level 1 now");
                reset.Click += (o, e) => { set.Start.Clear(); set.Start.AddRange(ItemShuffle.StartingEquipment(session, Level)); startChanged(); buildStart(); };
                top.Children.Add(reset);
                DockPanel.SetDock(top, Dock.Top);
                panel.Children.Add(top);
                panel.Children.Add(new IgnoreListPanel(main, s, set.Start, ItemShuffle.Candidates(s, Level, true), startChanged,
                    s.IsUw2
                        ? "What the Avatar starts with on level 1: in the Avatar's quarters (the start square is (19, 48)) the sack, leather vest and boots on (20, 49), and the closet behind the secret door at (19, 50): the pack, the rune bag and runestones, the sling, a hand axe and a flute. Only what the swap could move is listed. While ticked, these stay, and nothing is put in their place."
                        : "What the Avatar starts with on level 1: the sack on (33, 3), beside the start square (32, 2), with a dagger, bread, a fish, a torch and an apple (its map is not something the swap moves). While ticked, these stay, and nothing is put in their place.",
                    "Objects the swap touches", "Starting equipment", "Add →", "← Remove", "Add the object selected in the editor", null));
                startTab.Content = panel;
            };
            buildStart();
            tabs.Items.Add(startTab);

            var preview = new Button { Content = "Preview" };
            preview.Click += (o, e) => report.Text = "Preview — nothing changed yet.\n\n" + Run().Report;
            var swap = new Button { Content = "Swap", FontWeight = FontWeight.SemiBold };
            swap.Click += (o, e) => Swap();
            undoButton.Click += (o, e) => UndoAll();
            var buttons = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8, Margin = new Thickness(0, 8, 0, 6) };
            buttons.Children.Add(preview); buttons.Children.Add(swap); buttons.Children.Add(undoButton);
            buttons.Children.Add(new TextBlock { Text = "Each swap is also one Edit ▸ Undo step per level; nothing is written to the game until you save.", VerticalAlignment = VerticalAlignment.Center, Opacity = 0.7, FontSize = 12, TextWrapping = TextWrapping.Wrap });

            ItemToolLayout.Build(this, header, tabs, buttons, formula, report, 470, () => ItemShuffle.SwapFormula(session, Options()));
            ManualWindow.Attach(this, "Swapping items");
            Closed += (o, e) => Save();
        }

        Control SettingsTab()
        {
            var p = new StackPanel { Spacing = 6 };
            seed.Value = set.Seed;
            seed.ValueChanged += (o, e) => set.Seed = (int)(seed.Value ?? 1);
            var newSeed = new Button { Content = "New seed" };
            newSeed.Click += (o, e) => seed.Value = new Random().Next(1, int.MaxValue);
            var row = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8 };
            row.Children.Add(new TextBlock { Text = "Seed", VerticalAlignment = VerticalAlignment.Center });
            row.Children.Add(seed); row.Children.Add(newSeed);
            row.Children.Add(Check("Include creatures' inventories", set.IncludeInventories, x => set.IncludeInventories = x));
            p.Children.Add(row);

            p.Children.Add(Heading("What trades with what"));
            var within = new RadioButton { Content = "Within each kind (a sword stays a weapon)", GroupName = "swapmode", IsChecked = !set.AnyKind };
            var across = new RadioButton { Content = "Across all the ticked kinds (a sword may land where a torch was)", GroupName = "swapmode", IsChecked = set.AnyKind };
            across.IsCheckedChanged += (o, e) => set.AnyKind = across.IsChecked == true;
            p.Children.Add(within); p.Children.Add(across);

            p.Children.Add(Heading("Where objects may go"));
            var range = new NumericUpDown { Minimum = 1, Maximum = session.IsUw2 ? 7 : 8, Increment = 1, FormatString = "0", Width = 110, Value = Math.Max(1, set.Range) };
            range.ValueChanged += (o, e) => set.Range = (int)(range.Value ?? 1);
            Func<string, string, RadioButton> scope = (text, value) =>
            {
                bool on = set.Scope == value || (!session.IsUw2 && value == "game" && set.Scope == "world");
                var r = new RadioButton { Content = text, GroupName = "swapscope", IsChecked = on };
                r.IsCheckedChanged += (o, e) => { if (r.IsChecked == true) set.Scope = value; };
                return r;
            };
            p.Children.Add(scope("Within each level", "level"));
            var near = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8 };
            near.Children.Add(scope(session.IsUw2 ? "Within ± this many levels, in the same world:" : "Within ± this many levels:", "near"));
            near.Children.Add(range);
            p.Children.Add(near);
            if (session.IsUw2)
            {
                p.Children.Add(scope("Within each world (Britannia, the Prison Tower, Killorn Keep…)", "world"));
                p.Children.Add(scope("Across all the worlds", "game"));
            }
            else p.Children.Add(scope("Within the world: Ultima Underworld has one, so every level", "game"));

            p.Children.Add(Heading("Options"));
            p.Children.Add(Check("An item put in a spot takes the spot's owner (food belonging to a rat, replaced by a lantern, gives a lantern belonging to the rat)", set.SpotOwnership, x => set.SpotOwnership = x));
            p.Children.Add(Note("Only where the owner field means \"belongs to\" on both items (the game's can-own bit); keys, bones and scrolls keep theirs. Off: every item carries its own owner."));

            var kinds = new WrapPanel { Margin = new Thickness(0, 2) };
            void K(string text, bool v, Action<bool> a) { var c = Check(text, v, a); c.Width = 190; kinds.Children.Add(c); }
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
            K("Bones and skulls", set.Bones, x => set.Bones = x);
            K("Other scenery", set.Scenery, x => set.Scenery = x);
            p.Children.Add(Heading("Kinds taking part"));
            p.Children.Add(kinds);
            p.Children.Add(Note("Bones and skulls: the skulls, bones and piles of bones you can pick up; Garamon's bones never move. Other scenery: broken weapons, pieces of wood, debris, plants and the like that can be picked up."));

            p.Children.Add(Heading("Enchantments"));
            p.Children.Add(Check("After the swap, swap the enchantments among objects of the same class", set.SwapEnchantments, x => set.SwapEnchantments = x));
            var cls = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 12, Margin = new Thickness(24, 0, 0, 0) };
            cls.Children.Add(Check("weapons (melee and launchers apart)", set.EnchantWeapons, x => set.EnchantWeapons = x));
            cls.Children.Add(Check("armour", set.EnchantArmour, x => set.EnchantArmour = x));
            cls.Children.Add(Check("spell scrolls", set.EnchantScrolls, x => set.EnchantScrolls = x));
            p.Children.Add(cls);
            p.Children.Add(Note("Every weapon, piece of armour or spell scroll the swap may touch takes part, whether or not its kind is ticked above, plain ones included: an enchantment may move to an object that had none. The scope above applies."));
            return p;
        }

        static TextBlock Heading(string t) { return new TextBlock { Text = t, FontWeight = FontWeight.SemiBold, Margin = new Thickness(0, 8, 0, 0) }; }
        static TextBlock Note(string t) { return new TextBlock { Text = t, TextWrapping = TextWrapping.Wrap, FontSize = 12, Opacity = 0.7 }; }

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

        /// <summary>The swap as shuffle options: the mode, the scope, the kinds, the owners, the enchantments, the ignored objects and the starting equipment.</summary>
        public ShuffleOptions Options()
        {
            var o = ShuffleOptions.Defaults(session);
            o.Seed = set.Seed;
            o.Mode = set.AnyKind ? "swap-any" : "swap-kind";
            o.IncludeInventories = set.IncludeInventories;
            o.ShuffleWeapons = set.Weapons; o.ShuffleArmour = set.Armour; o.ShuffleFood = set.Food; o.ShuffleLights = set.Lights; o.SwapTools = set.Tools;
            o.ShuffleRunes = set.Runes; o.ShuffleWands = set.Wands; o.ShuffleScrolls = set.Scrolls; o.ShufflePotions = set.Potions; o.ShuffleRings = set.Rings;
            o.SwapBones = set.Bones; o.SwapScenery = set.Scenery;
            o.SpotOwnership = set.SpotOwnership;
            o.SwapScope = string.IsNullOrEmpty(set.Scope) ? "game" : set.Scope;
            o.SwapRange = Math.Max(1, set.Range);
            o.SwapEnchantments = set.SwapEnchantments; o.EnchantWeapons = set.EnchantWeapons; o.EnchantArmour = set.EnchantArmour; o.EnchantScrolls = set.EnchantScrolls;
            o.Ignored = set.Ignored;
            o.Kept = set.KeepStart ? set.Start : null;
            return o;
        }

        public SwapSettings Settings_ { get { return set; } }
        public bool CanUndoSwap { get { return undoButton.IsEnabled; } }
        public string ReportText { get { return report.Text; } }
        public string FormulaText { get { return formula.Text; } }
        /// <summary>The formula written again now (it also follows the settings by itself).</summary>
        public void RefreshFormula() { formula.Text = ItemShuffle.SwapFormula(session, Options()); }
        public double SettingsHeight { get { return tabs.Bounds.Height; } }
        public double ReportHeight { get { return report.Bounds.Height; } }
        public void ShowTab(int i) { tabs.SelectedIndex = i; }

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
            main.RandomizerApplied("Items swapped; Edit ▸ Undo (per level) or the item swap window's Undo swap puts them back.");
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
            main.RandomizerApplied("Item swaps undone.");
        }
    }
}
