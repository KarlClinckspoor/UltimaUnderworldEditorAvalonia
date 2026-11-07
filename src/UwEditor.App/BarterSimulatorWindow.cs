using System;
using System.Collections.Generic;
using System.Linq;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using UwEditor.Core;
using UwEditor.Core.Balance;
using UwEditor.Core.Conversations;
using UwEditor.Core.Editing;

namespace UwEditor.App
{
    /// <summary>
    /// Tools ▸ Balance ▸ Barter simulator: a trader (a creature of the levels, or any creature
    /// class) and two sides of items, worked out as the game's trading does (Barter): what the
    /// trader thinks each side is worth, whether it accepts, the player's own remark, and the
    /// coins that settle the trade.
    /// </summary>
    public sealed class BarterSimulatorWindow : Window
    {
        /// <summary>A trader to choose: a creature of a level (exact temper) or a creature class.</summary>
        public sealed class TraderChoice
        {
            public string Label;
            public int Class;
            /// <summary>The creature's object index on its level, or −1 for a class.</summary>
            public int ObjectIndex = -1;
            public int Level;
            public int Slot;
            public override string ToString() { return Label; }
        }

        /// <summary>The kinds of items the item list offers.</summary>
        static readonly Tuple<string, ItemCategory>[] Kinds =
        {
            Tuple.Create("Weapons", ItemCategory.MeleeWeapon | ItemCategory.Launcher),
            Tuple.Create("Ammunition", ItemCategory.Ammunition),
            Tuple.Create("Armour and shields", ItemCategory.Armour | ItemCategory.Shield | ItemCategory.Crown),
            Tuple.Create("Rings", ItemCategory.Ring),
            Tuple.Create("Treasure", ItemCategory.Treasure),
            Tuple.Create("Potions", ItemCategory.Potion),
            Tuple.Create("Food", ItemCategory.Food),
            Tuple.Create("Lights", ItemCategory.Light),
            Tuple.Create("Wands", ItemCategory.Wand),
            Tuple.Create("Books and scrolls", ItemCategory.Book | ItemCategory.Scroll),
            Tuple.Create("Containers", ItemCategory.Container),
            Tuple.Create("Keys", ItemCategory.Key),
            Tuple.Create("Runestones", ItemCategory.Runestone),
            Tuple.Create("Everything that can be picked up", ItemCategory.All),
        };

        readonly MainWindow main;
        readonly GameSession session;
        readonly ConversationLibrary lib;
        readonly PlayerStats stats;
        readonly List<BarterItem> playerSide = new List<BarterItem>(), traderSide = new List<BarterItem>();
        readonly List<TraderChoice> traders = new List<TraderChoice>();
        readonly ComboBox traderBox = new ComboBox { HorizontalAlignment = HorizontalAlignment.Stretch };
        readonly TextBlock traderText = new TextBlock { TextWrapping = TextWrapping.Wrap, FontSize = 12 };
        readonly ComboBox kindBox = new ComboBox { Width = 220 };
        readonly ComboBox itemBox = new ComboBox { Width = 300 };
        readonly StackPanel playerList = new StackPanel { Spacing = 4 }, traderList = new StackPanel { Spacing = 4 };
        readonly TextBlock playerTotal = new TextBlock { FontWeight = FontWeight.SemiBold }, traderTotal = new TextBlock { FontWeight = FontWeight.SemiBold };
        readonly TextBlock result = new TextBlock { TextWrapping = TextWrapping.Wrap, FontSize = 13 };
        readonly TextBlock verdict = new TextBlock { FontSize = 18, FontWeight = FontWeight.SemiBold };
        readonly StatSlider coinQuality;
        readonly RadioButton toGreed = new RadioButton { Content = "What this trader accepts", GroupName = "settle", IsChecked = true };
        readonly RadioButton toEqual = new RadioButton { Content = "Equal worth", GroupName = "settle" };
        readonly List<TextBlock> playerWorths = new List<TextBlock>(), traderWorths = new List<TextBlock>();
        readonly Dictionary<int, BarterLikes> likesCache = new Dictionary<int, BarterLikes>();
        Func<int, LevelModel> levels;
        ObjectTables own;
        BarterLikes likes;
        BarterTemper temper;
        List<TraderClass> classes;

        public BarterSimulatorWindow(MainWindow main)
        {
            this.main = main;
            session = main.ViewModel.Session;
            Title = "Barter simulator";
            Width = 1280; Height = 840; MinWidth = 960; MinHeight = 560;
            WindowStartupLocation = WindowStartupLocation.CenterOwner;
            stats = PlayerStats.From(session.Player);
            levels = BalanceUi.Levels(main);
            try { lib = main.Conversations(); } catch (Exception) { lib = null; }
            classes = Barter.Classes(session, Tables());
            FillTraders();

            // left: the trader, the player, the coins
            var left = new StackPanel { Spacing = 4, Width = 330, Margin = new Thickness(0, 0, 18, 0) };
            left.Children.Add(BalanceUi.Heading("TRADER"));
            traderBox.ItemsSource = traders;
            traderBox.SelectionChanged += (o, e) => { TraderChanged(); };
            left.Children.Add(traderBox);
            var useSel = new Button { Content = "Use the selected creature" };
            ToolTip.SetTip(useSel, "The creature selected in the main window, when it is one.");
            useSel.Click += (o, e) => UseSelectedCreature();
            left.Children.Add(useSel);
            left.Children.Add(traderText);
            var panel = new PlayerStatsPanel(session, stats, new[] { PlayerStats.Charm, PlayerStats.Appraise }, false);
            // Charm changes the trader's figures, and their text with them
            panel.Changed += () => { if (traderBox.SelectedItem is TraderChoice) TemperChanged(); else Recompute(); };
            panel.Margin = new Thickness(0, 10, 0, 0);
            left.Children.Add(panel);
            left.Children.Add(BalanceUi.Heading("COINS"));
            coinQuality = new StatSlider("Coin quality", 0, 63, Barter.DefaultCoinQuality(session, levels),
                session.IsUw2 ? "UW2 counts every gold coin at quality 63, whatever its own." : "UW1 values a coin by its own quality: the default is the quality the levels' coins have most.");
            coinQuality.IsEnabled = !session.IsUw2;
            coinQuality.ValueChanged += v => Recompute();
            left.Children.Add(coinQuality);
            left.Children.Add(BalanceUi.Note(session.IsUw2
                ? "UW2 counts every coin as quality 63: a coin is worth about its value (3)."
                : "UW1 counts a coin at its own quality: at 40 a coin is worth about 1.9. More than about 270 coins of quality 40 in one stack pass the game's 16-bit limit and are valued wrongly."));
            left.Children.Add(BalanceUi.Heading("BALANCE TRADE AIMS AT"));
            left.Children.Add(toGreed); left.Children.Add(toEqual);
            ToolTip.SetTip(toGreed, "Coins until the trader just accepts (the player pays), or the most it still pays back (the player gives more).");
            ToolTip.SetTip(toEqual, "Coins until both sides are worth the same to the trader, whatever its greed.");

            // middle: adding items
            var kinds = Kinds.Select(k => k.Item1).ToList();
            kindBox.ItemsSource = kinds;
            kindBox.SelectionChanged += (o, e) => FillItems();
            var addP = new Button { Content = "Add to the player's side" };
            var addT = new Button { Content = "Add to the trader's side" };
            addP.Click += (o, e) => AddChosen(true);
            addT.Click += (o, e) => AddChosen(false);
            var selP = new Button { Content = "Selected object → player" };
            var selT = new Button { Content = "Selected object → trader" };
            ToolTip.SetTip(selP, "The object selected in the main window, with its quality and stack size.");
            ToolTip.SetTip(selT, "The object selected in the main window, with its quality and stack size.");
            selP.Click += (o, e) => AddSelected(true);
            selT.Click += (o, e) => AddSelected(false);
            var addRow = new WrapPanel { Orientation = Orientation.Horizontal };
            foreach (var c in new Control[] { kindBox, itemBox, addP, addT, selP, selT }) { c.Margin = new Thickness(0, 0, 8, 6); addRow.Children.Add(c); }

            var sides = new Grid { ColumnDefinitions = new ColumnDefinitions("*,16,*") };
            sides.Children.Add(Side("THE PLAYER GIVES", playerTotal, playerList));
            var ts = Side("THE TRADER GIVES", traderTotal, traderList);
            Grid.SetColumn(ts, 2);
            sides.Children.Add(ts);

            var balance = new Button { Content = "Balance trade", FontWeight = FontWeight.SemiBold, Padding = new Thickness(14, 6) };
            ToolTip.SetTip(balance, "Adds gold coins (0xA0) at the coin quality: to the player's side when the trader would refuse, to the trader's when the player gives more.");
            balance.Click += (o, e) => Balance();
            var clear = new Button { Content = "Clear both sides" };
            clear.Click += (o, e) => { playerSide.Clear(); traderSide.Clear(); Rebuild(); };
            var swap = new Button { Content = "Swap sides" };
            swap.Click += (o, e) => { var a = playerSide.ToList(); playerSide.Clear(); playerSide.AddRange(traderSide); traderSide.Clear(); traderSide.AddRange(a); Rebuild(); };
            var buttons = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8, Margin = new Thickness(0, 8, 0, 0) };
            buttons.Children.Add(balance); buttons.Children.Add(clear); buttons.Children.Add(swap);
            var bottom = new StackPanel { Spacing = 6, Margin = new Thickness(0, 10, 0, 0) };
            bottom.Children.Add(verdict);
            bottom.Children.Add(result);
            bottom.Children.Add(buttons);

            var middle = new DockPanel();
            DockPanel.SetDock(addRow, Dock.Top);
            DockPanel.SetDock(bottom, Dock.Bottom);
            middle.Children.Add(addRow);
            middle.Children.Add(bottom);
            middle.Children.Add(sides);

            var grid = new Grid { ColumnDefinitions = new ColumnDefinitions("Auto,*") };
            grid.Children.Add(new ScrollViewer { Content = left, Margin = new Thickness(0, 0, 16, 0) });
            Grid.SetColumn(middle, 1);
            grid.Children.Add(middle);
            Content = new Border { Padding = new Thickness(14), Child = grid };
            ManualWindow.Attach(this, "Barter simulator");
            Activated += (o, e) => { own = null; classes = Barter.Classes(session, Tables()); TemperChanged(); };

            kindBox.SelectedIndex = 0;
            traderBox.SelectedIndex = traders.Count > 0 ? 0 : -1;
            UseSelectedCreature(quiet: true);
            Rebuild();
        }

        static Control Side(string title, TextBlock total, StackPanel list)
        {
            var d = new DockPanel();
            var head = new StackPanel { Spacing = 2 };
            head.Children.Add(BalanceUi.Heading(title));
            head.Children.Add(total);
            DockPanel.SetDock(head, Dock.Top);
            d.Children.Add(head);
            d.Children.Add(new ScrollViewer { Content = list, Margin = new Thickness(0, 6, 0, 0) });
            return d;
        }

        ObjectTables Tables() { return BalanceUi.Tables(main, ref own); }
        int ComValue(int item) { return Tables().Get("Common properties", item, "Value"); }

        public IReadOnlyList<BarterItem> PlayerSide { get { return playerSide; } }
        public IReadOnlyList<BarterItem> TraderSide { get { return traderSide; } }
        public IReadOnlyList<TraderChoice> Traders { get { return traders; } }
        public ComboBox TraderBox { get { return traderBox; } }
        public BarterTemper Temper { get { return temper; } }
        public BarterLikes Likes { get { return likes; } }
        public PlayerStats Stats { get { return stats; } }
        public BarterResult Result { get; private set; }
        public string ResultText { get { return verdict.Text + "\n" + result.Text; } }
        public StatSlider CoinQuality { get { return coinQuality; } }
        public bool EqualWorth { get { return toEqual.IsChecked == true; } set { toEqual.IsChecked = value; toGreed.IsChecked = !value; } }

        /// <summary>The creatures of the levels that have a conversation of their own, then every creature class.</summary>
        void FillTraders()
        {
            foreach (var slot in session.LevelSlots().Where(x => x.Present))
            {
                LevelModel m;
                try { m = levels(slot.Level); } catch (Exception) { continue; }
                if (m == null) continue;
                foreach (var o in m.Objects)
                {
                    if (o == null || o.Free || !o.IsCritter || !o.IsMobile || o.Index == 0 || o.Record[0x1A] == 0) continue;
                    string npc = ObjectTexts.NpcName(session, o);
                    string where = session.IsUw2 && !string.IsNullOrWhiteSpace(slot.Name) ? slot.Name : "Level " + slot.Level;
                    traders.Add(new TraderChoice
                    {
                        Label = string.Format("{0}: {1} ({2}, #{3})", where, npc ?? "whoami " + o.Record[0x1A], session.Names.Get(o.Item), o.Index),
                        Class = o.Item & 0x3F, ObjectIndex = o.Index, Level = slot.Level, Slot = Barter.ConversationSlot(o),
                    });
                }
            }
            for (int c = 0; c < 64; c++)
                traders.Add(new TraderChoice { Label = string.Format("Any {0} (creature 0x{1:X2})", session.Names.Get(0x40 + c), 0x40 + c), Class = c, Slot = 0x100 + c });
        }

        /// <summary>The creature selected in the main window, when it is one; false otherwise.</summary>
        public bool UseSelectedCreature(bool quiet = false)
        {
            var vm = main.ViewModel;
            int sel = vm.SelectedObject;
            var lev = vm.Level;
            if (lev == null || sel <= 0 || sel >= lev.Objects.Length || !lev.Objects[sel].IsCritter)
            {
                if (!quiet) traderText.Text = "The selection is not a creature. " + traderText.Text;
                return false;
            }
            var o = lev.Objects[sel];
            int i = traders.FindIndex(t => t.ObjectIndex == sel && t.Level == lev.Level);
            if (i < 0)
            {
                traders.Insert(0, new TraderChoice
                {
                    Label = string.Format("Level {0}: {1} (#{2})", lev.Level, session.Names.Get(o.Item), o.Index),
                    Class = o.Item & 0x3F, ObjectIndex = o.Index, Level = lev.Level, Slot = Barter.ConversationSlot(o),
                });
                traderBox.ItemsSource = null; traderBox.ItemsSource = traders;
                i = 0;
            }
            traderBox.SelectedIndex = i;
            return true;
        }

        void TraderChanged()
        {
            var t = traderBox.SelectedItem as TraderChoice;
            if (t == null) return;
            if (!likesCache.TryGetValue(t.Slot, out likes)) likesCache[t.Slot] = likes = Barter.LikesOf(lib, t.Slot);
            TemperChanged();
        }

        void TemperChanged()
        {
            var t = traderBox.SelectedItem as TraderChoice;
            if (t == null) return;
            var c = classes[t.Class];
            temper = Barter.Temper(c, t.ObjectIndex, stats[PlayerStats.Charm]);
            var ci = System.Globalization.CultureInfo.InvariantCulture;
            var lines = new List<string>();
            lines.Add(string.Format("Creature figures: haggle {0}, patience {1}, shrewd {2}, level {3} (OBJECTS.DAT).", c.Haggle, c.Patience, c.Shrewd, c.Level));
            if (temper.Exact)
                lines.Add(string.Format("Wants {0}% more than it gives (greed), takes {1} bad offers (patience), may misjudge a value by up to {2}%. Drawn for object #{3} as the game draws them; Charm included.", temper.Greed, temper.Patience, temper.Assess, t.ObjectIndex));
            else
                lines.Add(string.Format("A creature of this class wants {0}% to {1}% more than it gives (greed), takes {2} to {3} bad offers, and may misjudge a value by {4}% to {5}%; each one draws its own figures from its object index. The middle figures are used: greed {6}%.", temper.GreedMin, temper.GreedMax, temper.PatienceMin, temper.PatienceMax, temper.AssessMin, temper.AssessMax, temper.Greed));
            if (likes == null) lines.Add(string.Format("Its conversation (slot {0}) sets no likes or dislikes: every item counts at its plain worth.", t.Slot));
            else
            {
                if (likes.Likes.Count > 0) lines.Add("Likes (× 1.5): " + string.Join(", ", likes.Likes.Select(e => BarterLikes.Describe(e, session.Names))) + ".");
                if (likes.Dislikes.Count > 0) lines.Add("Dislikes (worth nothing, never taken): " + string.Join(", ", likes.Dislikes.Select(e => BarterLikes.Describe(e, session.Names))) + ".");
                lines.Add(string.Format("From conversation slot {0}{1}.", t.Slot, likes.Calls > 1 ? " (it sets them in " + likes.Calls + " places; the first is shown)" : ""));
            }
            traderText.Text = string.Join("\n", lines);
            Recompute();
        }

        void FillItems()
        {
            int k = Math.Max(0, kindBox.SelectedIndex);
            var cat = Kinds[k].Item2;
            var t = Tables();
            var list = new List<string>();
            itemIds.Clear();
            for (int item = 0; item < 0x140; item++)
            {
                string raw = session.Strings.Get(4, item);
                if (string.IsNullOrWhiteSpace(raw)) continue;
                if (cat != ItemCategory.All && (ItemCategories.Of(item, session.Kind) & cat) == 0) continue;
                if (t.Get("Common properties", item, "Pickable") == 0) continue;
                itemIds.Add(item);
                list.Add(string.Format("{0} (0x{1:X3}) · value {2}", session.Names.Get(item), item, t.Get("Common properties", item, "Value")));
            }
            itemBox.ItemsSource = list;
            itemBox.SelectedIndex = list.Count > 0 ? 0 : -1;
        }
        readonly List<int> itemIds = new List<int>();

        void AddChosen(bool toPlayer)
        {
            int i = itemBox.SelectedIndex;
            if (i < 0 || i >= itemIds.Count) return;
            Add(new BarterItem { Item = itemIds[i] }, toPlayer);
        }

        /// <summary>Puts an item on a side.</summary>
        public void Add(BarterItem b, bool toPlayer)
        {
            (toPlayer ? playerSide : traderSide).Add(b);
            Rebuild();
        }

        /// <summary>The object selected in the main window, on a side; false when nothing (or no item) is selected.</summary>
        public bool AddSelected(bool toPlayer)
        {
            var vm = main.ViewModel;
            int sel = vm.SelectedObject;
            var lev = vm.Level;
            if (lev == null || sel <= 0 || sel >= lev.Objects.Length) { result.Text = "Select an object in the main window first."; return false; }
            var o = lev.Objects[sel];
            if (o.Item >= 0x140 || o.IsCritter) { result.Text = "The selected object is not an item that can be traded."; return false; }
            Add(new BarterItem { Item = o.Item, Quantity = WorldObjects.Quantity(o), Quality = o.Quality, From = string.Format("level {0}, #{1}", lev.Level, o.Index) }, toPlayer);
            return true;
        }

        /// <summary>The rows of both sides, made again.</summary>
        void Rebuild()
        {
            Fill(playerList, playerSide, playerWorths);
            Fill(traderList, traderSide, traderWorths);
            Recompute();
        }

        void Fill(StackPanel list, List<BarterItem> side, List<TextBlock> worths)
        {
            list.Children.Clear();
            worths.Clear();
            for (int i = 0; i < side.Count; i++)
            {
                var b = side[i];
                var row = new Border { Padding = new Thickness(6), CornerRadius = new CornerRadius(4), BorderThickness = new Thickness(1) };
                row.Bind(Border.BorderBrushProperty, row.GetResourceObservable("Line"));
                var g = new Grid { ColumnDefinitions = new ColumnDefinitions("36,*,Auto"), RowDefinitions = new RowDefinitions("Auto,Auto,Auto") };
                var icon = new Image { Source = main.ViewModel.IconOf(b.Item), Width = 32, Height = 32, VerticalAlignment = VerticalAlignment.Top };
                RenderOptions.SetBitmapInterpolationMode(icon, Avalonia.Media.Imaging.BitmapInterpolationMode.None);
                Grid.SetRowSpan(icon, 3);
                g.Children.Add(icon);
                var name = new TextBlock { Text = string.Format("{0} (0x{1:X3}){2}", session.Names.Get(b.Item), b.Item, b.From != null ? " · from " + b.From : ""), FontWeight = FontWeight.SemiBold, TextTrimming = TextTrimming.CharacterEllipsis };
                var worth = new TextBlock { FontSize = 12, Opacity = 0.8 };
                worths.Add(worth);
                var top = new StackPanel { Spacing = 0 };
                top.Children.Add(name); top.Children.Add(worth);
                Grid.SetColumn(top, 1);
                g.Children.Add(top);
                var qty = new StatSlider("Quantity", 1, 511, b.Quantity, "The stack's size (a quantity object's link, below 0x200). Value × quantity is worked in 16 bits.", 64);
                qty.ValueChanged += v => { b.Quantity = v; Recompute(); };
                var qual = new StatSlider("Quality", 0, 63, b.Quality, "0-63: worth × quality / 64 (at least 1; 0 at quality 0).", 64);
                qual.ValueChanged += v => { b.Quality = v; Recompute(); };
                Grid.SetColumn(qty, 1); Grid.SetRow(qty, 1);
                Grid.SetColumn(qual, 1); Grid.SetRow(qual, 2);
                g.Children.Add(qty); g.Children.Add(qual);
                var tools = new StackPanel { Spacing = 4, Margin = new Thickness(6, 0, 0, 0) };
                var remove = new Button { Content = "✕", Padding = new Thickness(8, 2) };
                ToolTip.SetTip(remove, "Take it off this side.");
                remove.Click += (o, e) => { side.Remove(b); Rebuild(); };
                var edit = new Button { Content = "Value…", Padding = new Thickness(8, 2) };
                ToolTip.SetTip(edit, "Opens Object tables on the item's common properties, where its Value (and everything else) can be changed. The simulator follows the edits, saved or not, when it is brought up again.");
                int item = b.Item;
                edit.Click += (o, e) => { var w = main.ShowObjectTables(); if (w == null) return; w.ShowTable("Common properties"); w.SelectRow("Common properties", item); };
                tools.Children.Add(remove); tools.Children.Add(edit);
                Grid.SetColumn(tools, 2); Grid.SetRowSpan(tools, 3);
                g.Children.Add(tools);
                row.Child = g;
                list.Children.Add(row);
            }
            if (side.Count == 0) list.Children.Add(BalanceUi.Note("Nothing yet: choose an item above and add it, or add the object selected in the main window."));
        }

        /// <summary>Works the trade out again.</summary>
        public void Recompute()
        {
            if (main.ViewModel.Session != session) { Close(); return; }
            if (temper == null) return;
            var t = traderBox.SelectedItem as TraderChoice;
            if (t != null) temper = Barter.Temper(classes[t.Class], t.ObjectIndex, stats[PlayerStats.Charm]);
            var r = Barter.Offer(session.Kind, playerSide, traderSide, ComValue, likes, temper, stats[PlayerStats.Appraise], session.Strings);
            Result = r;
            for (int i = 0; i < playerWorths.Count && i < r.PlayerItems.Count; i++) playerWorths[i].Text = WorthText(playerSide[i], r.PlayerItems[i], true);
            for (int i = 0; i < traderWorths.Count && i < r.TraderItems.Count; i++) traderWorths[i].Text = WorthText(traderSide[i], r.TraderItems[i], false);
            playerTotal.Text = string.Format("Worth {0} to the trader", r.PlayerWorth);
            traderTotal.Text = string.Format("Worth {0} to the trader", r.TraderWorth);
            if (playerSide.Count == 0 && traderSide.Count == 0) { verdict.Text = "No trade yet"; result.Text = ""; return; }
            verdict.Text = r.Accepted ? "The trader accepts" : "The trader refuses";
            verdict.Foreground = r.Accepted ? Brushes.SeaGreen : Brushes.IndianRed;
            var lines = new List<string>();
            lines.Add(string.Format("It values the player's side at {0} and its own at {1}: an evaluation of {2}% against its greed of {3}%{4}.",
                r.PlayerWorth, r.TraderWorth, r.Evaluation, temper.Greed, r.TraderWorth > 0 ? "" : " (its side is worth nothing, which counts as 100%)"));
            int a = temper.Assess;
            if (a > 0)
                lines.Add(string.Format("Its estimate of each item may be off by up to {0}%{5}: from {1}% ({2}) when it goes against the player to {3}% ({4}) when it goes for the player. The error is the same each time for the same object.",
                    a, r.WorstEvaluation, r.AcceptedAtWorst ? "accepts" : "refuses", r.BestEvaluation, r.AcceptedAtBest ? "accepts" : "refuses", temper.Exact ? "" : " (the class's middle figure)"));
            if (r.Judgement != null)
                lines.Add(string.Format("The player's own remark: \"{0}\" (without its blur; with Appraise {1} the player's estimate may be off by up to {2}%).", r.Judgement.Trim(), stats[PlayerStats.Appraise], Barter.AppraiseError(stats[PlayerStats.Appraise])));
            lines.AddRange(r.Notes);
            result.Text = string.Join("\n", lines);
        }

        string WorthText(BarterItem b, int w, bool player)
        {
            int cv = ComValue(b.Item);
            if (cv == 0) return "value 0: worth nothing" + (player ? "; the trader leaves it with the player" : "; a trader never offers it");
            if (player && likes != null)
            {
                int d = likes.Of(b.Item, cv);
                if (d < 0) return string.Format("value {0}: disliked, worth nothing; the trader leaves it with the player", cv);
                if (d > 0) return string.Format("value {0}: liked, × 1.5: worth {1}", cv, w);
            }
            return string.Format("value {0}: worth {1}", cv, w);
        }

        /// <summary>Balance trade: coins on the side that needs them.</summary>
        public void Balance()
        {
            bool toPlayer; string note;
            int q = session.IsUw2 ? 63 : coinQuality.Value;
            var coins = Barter.Settle(session.Kind, playerSide, traderSide, ComValue, likes, temper, q, EqualWorth, out toPlayer, out note);
            if (coins.Count > 0) { (toPlayer ? playerSide : traderSide).AddRange(coins); Rebuild(); }
            if (note != null) result.Text = note + (result.Text.Length > 0 ? "\n" + result.Text : "");
            else if (coins.Count > 0) result.Text = string.Format("Added {0} gold coins to the {1}'s side.\n", coins.Sum(c => c.Quantity), toPlayer ? "player" : "trader") + result.Text;
        }
    }
}
