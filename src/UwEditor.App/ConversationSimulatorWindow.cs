using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Globalization;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Templates;
using Avalonia.Data;
using Avalonia.Data.Converters;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;
using UltimateEditor.Games;
using UwEditor.Core;
using UwEditor.Core.Conversations;

namespace UwEditor.App
{
    /// <summary>A line of the simulated conversation.</summary>
    public sealed class TranscriptRow
    {
        public string Who { get; set; }
        public string Text { get; set; }
        public IBrush Brush { get; set; }
        public FontStyle Style { get; set; }
        public SimOutput Output { get; set; }
    }

    /// <summary>A value the simulator shows and lets change: a word of script memory, a quest, a variable, a skill.</summary>
    public sealed class SimVarRow : Observable
    {
        string value = "", error = "";
        int last = int.MinValue;
        public string Kind { get; set; }
        public string Name { get; set; }
        public string Tip { get; set; }
        public Func<int> Get;
        /// <summary>Sets the value; returns an error, or null.</summary>
        public Func<int, string> Put;
        public string Value
        {
            get { return value; }
            set
            {
                if (!Set(ref this.value, value ?? "")) return;
                int v;
                if (!int.TryParse(this.value.Trim(), NumberStyles.Integer, CultureInfo.InvariantCulture, out v)) { Error = "Enter a number."; return; }
                string e = Put == null ? "This value is read-only." : Put(v);
                Error = e ?? "";
                if (e == null) last = v;
            }
        }
        public string Error { get { return error; } set { if (Set(ref error, value ?? "")) Raise(nameof(HasError)); } }
        public bool HasError { get { return !string.IsNullOrEmpty(error); } }
        public bool Changed { get; private set; }

        /// <summary>Shows the current value when it changed since the last look (a row being typed in keeps its text otherwise).</summary>
        public void Refresh()
        {
            int v = Get();
            Changed = last != int.MinValue && v != last;
            if (v == last) return;
            last = v;
            value = v.ToString(CultureInfo.InvariantCulture);
            error = "";
            Raise(nameof(Value)); Raise(nameof(Error)); Raise(nameof(HasError));
        }
    }

    /// <summary>
    /// Conversations ▸ Simulate: runs the shown conversation as the game would (ConversationSimulator):
    /// the NPC's lines, the player's menu choices as buttons, a box for babl_ask, Step, Run and
    /// Reset, a debug log, the variables with their current values (editable), and the start-up
    /// values of what only the game knows, filled in from the open game. Nothing is written to
    /// the game's files.
    /// </summary>
    public sealed class ConversationSimulatorWindow : Window
    {
        readonly ConversationWindow cw;
        Conversation conv;
        int slot = -1;
        SimWorld start;
        string npcText = "", tradeText = "";
        ConversationSimulator sim;
        DecompileResult decompiled;
        int shownOutput;
        SimLog lastLog;

        readonly TextBlock header = new TextBlock { FontSize = 15, FontWeight = FontWeight.SemiBold, TextWrapping = TextWrapping.Wrap };
        readonly TextBlock stateText = new TextBlock { FontSize = 12, TextWrapping = TextWrapping.Wrap, VerticalAlignment = VerticalAlignment.Center };
        readonly ObservableCollection<TranscriptRow> transcript = new ObservableCollection<TranscriptRow>();
        readonly ListBox transcriptList = new ListBox();
        readonly StackPanel choices = new StackPanel { Spacing = 4 };
        readonly TextBox askBox = new TextBox { PlaceholderText = "What the player types", MaxLength = 0x32 };
        readonly Button askButton = new Button { Content = "Answer" };
        readonly DockPanel askPanel = new DockPanel { IsVisible = false };
        readonly ObservableCollection<SimVarRow> vars = new ObservableCollection<SimVarRow>();
        readonly ObservableCollection<SimVarRow> startVars = new ObservableCollection<SimVarRow>();
        readonly ObservableCollection<string> log = new ObservableCollection<string>();
        readonly ListBox logList = new ListBox();
        readonly CheckBox allValues = new CheckBox { Content = "Show every quest, variable and skill, not only those this conversation uses" };
        readonly CheckBox logInstructions = new CheckBox { Content = "Log every instruction", IsChecked = true };
        readonly CheckBox follow = new CheckBox { Content = "Follow in the script" };
        readonly CheckBox runOn = new CheckBox { Content = "Run on after a choice", IsChecked = true };
        readonly TextBox stopAt = new TextBox { Width = 120, PlaceholderText = "e.g. 02E2, 0352" };
        readonly NumericUpDown limit = new NumericUpDown { Minimum = 100, Maximum = 10000000, Value = 200000, Increment = 10000, FormatString = "0", Width = 160 };
        readonly ComboBox talker = new ComboBox { MinWidth = 320 };
        readonly TextBox playerName = new TextBox { Width = 200 };
        readonly CheckBox female = new CheckBox { Content = "Female (sex picks the second of two strings)" };
        readonly NumericUpDown seed = new NumericUpDown { Minimum = 0, Maximum = int.MaxValue, Value = 1, FormatString = "0", Width = 130 };
        readonly CheckBox fixedRandom = new CheckBox { Content = "random(n) always gives" };
        readonly NumericUpDown fixedValue = new NumericUpDown { Minimum = 1, Maximum = 32767, Value = 1, FormatString = "0", Width = 100 };
        readonly CheckBox offers = new CheckBox { Content = "do_offer: the talker accepts", IsChecked = true };
        readonly CheckBox demands = new CheckBox { Content = "do_demand: the talker gives in", IsChecked = true };
        readonly TextBox npcInventory = Mono();
        readonly TextBox playerTrade = Mono();
        readonly TabControl tabs = new TabControl();
        List<ConversationUser> users = new List<ConversationUser>();
        bool filling;

        public ConversationSimulator Simulator { get { return sim; } }
        public SimWorld StartWorld { get { return start; } }
        public ObservableCollection<TranscriptRow> Transcript { get { return transcript; } }
        public ObservableCollection<SimVarRow> Variables { get { return vars; } }
        public ObservableCollection<SimVarRow> StartValues { get { return startVars; } }
        public ObservableCollection<string> LogLines { get { return log; } }
        /// <summary>The choice buttons shown now.</summary>
        public IEnumerable<Button> ChoiceButtons { get { return choices.Children.OfType<Button>(); } }
        public TextBox AskBox { get { return askBox; } }

        static TextBox Mono()
        {
            return new TextBox { AcceptsReturn = true, TextWrapping = TextWrapping.NoWrap, FontFamily = new FontFamily("Cascadia Mono,Consolas,DejaVu Sans Mono,monospace"), FontSize = 12, MinHeight = 90 };
        }

        public ConversationSimulatorWindow(ConversationWindow owner)
        {
            cw = owner;
            Title = "Conversation simulator";
            Width = 1180; Height = 820; MinWidth = 760; MinHeight = 520;
            WindowStartupLocation = WindowStartupLocation.CenterOwner;
            Content = Build();
            ManualWindow.Attach(this, "Conversation simulator");
            cw.CurrentChanged += OnCurrentChanged;
            Closed += (o, e) => cw.CurrentChanged -= OnCurrentChanged;
            AddHandler(KeyDownEvent, OnKey, Avalonia.Interactivity.RoutingStrategies.Tunnel);
            Load();
        }

        void OnKey(object sender, KeyEventArgs e)
        {
            if (e.Key == Key.F10) { Step(); e.Handled = true; }
            else if (e.Key == Key.F5) { Run(); e.Handled = true; }
            else if (e.Key >= Key.D1 && e.Key <= Key.D9 && sim != null && sim.State == SimState.WaitingMenu && !(FocusManager?.GetFocusedElement() is TextBox))
            {
                Choose(e.Key - Key.D1 + 1);
                e.Handled = true;
            }
        }

        Control Build()
        {
            var reset = new Button { Content = "Reset" };
            ToolTip.SetTip(reset, "Start again from the start-up values.");
            reset.Click += (o, e) => Reset();
            var step = new Button { Content = "Step" };
            ToolTip.SetTip(step, "Run one instruction (F10).");
            step.Click += (o, e) => Step();
            var run = new Button { Content = "Run", FontWeight = FontWeight.SemiBold };
            ToolTip.SetTip(run, "Run until a menu, a question, the end, a stop address or the instruction limit (F5).");
            run.Click += (o, e) => Run();
            logInstructions.IsCheckedChanged += (o, e) => { if (sim != null) sim.LogInstructions = logInstructions.IsChecked == true; };
            var bar = new WrapPanel { Orientation = Orientation.Horizontal };
            foreach (var c in new Control[] { reset, step, run, Label("Stop at"), stopAt, Label("Limit"), limit, logInstructions, follow, runOn })
            {
                c.Margin = new Thickness(0, 2, 10, 2);
                c.VerticalAlignment = VerticalAlignment.Center;
                bar.Children.Add(c);
            }
            ToolTip.SetTip(stopAt, "The code addresses (in hex, as the script shows them) where Run stops.");
            ToolTip.SetTip(limit, "Run stops after this many instructions, in case of a loop that waits for nothing.");

            transcriptList.ItemsSource = transcript;
            transcriptList.Bind(ListBox.BackgroundProperty, transcriptList.GetResourceObservable("Bg"));
            transcriptList.ItemTemplate = new FuncDataTemplate<TranscriptRow>((r, ns) =>
            {
                var g = new Grid { ColumnDefinitions = new ColumnDefinitions("70,*") };
                var who = new TextBlock { FontSize = 11, Opacity = 0.7, Margin = new Thickness(0, 2, 6, 0) };
                who.Bind(TextBlock.TextProperty, new Binding("Who"));
                var t = new TextBlock { TextWrapping = TextWrapping.Wrap, FontSize = 13 };
                t.Bind(TextBlock.TextProperty, new Binding("Text"));
                t.Bind(TextBlock.ForegroundProperty, new Binding("Brush"));
                t.Bind(TextBlock.FontStyleProperty, new Binding("Style"));
                Grid.SetColumn(t, 1);
                g.Children.Add(who); g.Children.Add(t);
                return g;
            }, true);

            askButton.Click += (o, e) => Answer(askBox.Text);
            askBox.KeyDown += (o, e) => { if (e.Key == Key.Enter) { Answer(askBox.Text); e.Handled = true; } };
            DockPanel.SetDock(askButton, Dock.Right);
            askButton.Margin = new Thickness(6, 0, 0, 0);
            askPanel.Children.Add(askButton);
            askPanel.Children.Add(askBox);

            var bottom = new StackPanel { Spacing = 6, Margin = new Thickness(0, 8, 0, 0) };
            bottom.Children.Add(stateText);
            bottom.Children.Add(choices);
            bottom.Children.Add(askPanel);
            var left = new DockPanel();
            DockPanel.SetDock(bottom, Dock.Bottom);
            left.Children.Add(bottom);
            left.Children.Add(transcriptList);

            tabs.Items.Add(new TabItem { Header = "Variables", Content = VariablesTab() });
            tabs.Items.Add(new TabItem { Header = "Start-up", Content = StartTab() });
            tabs.Items.Add(new TabItem { Header = "Log", Content = LogTab() });

            var grid = new Grid { ColumnDefinitions = new ColumnDefinitions("*,4,460") };
            var split = new GridSplitter { ResizeDirection = GridResizeDirection.Columns };
            Grid.SetColumn(split, 1); Grid.SetColumn(tabs, 2);
            grid.Children.Add(left); grid.Children.Add(split); grid.Children.Add(tabs);

            var top = new StackPanel { Spacing = 4, Margin = new Thickness(0, 0, 0, 8) };
            top.Children.Add(header);
            top.Children.Add(new TextBlock
            {
                FontSize = 12, Opacity = 0.75, TextWrapping = TextWrapping.Wrap,
                Text = "Runs the conversation as the game does: the NPC's lines, your choices, the variables. What only the game knows (the player, quests, inventories, random rolls) comes from the start-up values. Nothing is written to the game.",
            });
            top.Children.Add(bar);
            DockPanel.SetDock(top, Dock.Top);
            var dock = new DockPanel { Margin = new Thickness(14) };
            dock.Children.Add(top);
            dock.Children.Add(grid);
            return dock;
        }

        static TextBlock Label(string t) { return new TextBlock { Text = t, VerticalAlignment = VerticalAlignment.Center }; }

        Control VarList(ObservableCollection<SimVarRow> rows)
        {
            return new ScrollViewer { Content = VarItems(rows), Padding = new Thickness(0, 0, 12, 0) };
        }

        static Control VarItems(ObservableCollection<SimVarRow> rows)
        {
            var list = new ItemsControl { ItemsSource = rows };
            list.ItemTemplate = new FuncDataTemplate<SimVarRow>((r, ns) =>
            {
                var g = new Grid { ColumnDefinitions = new ColumnDefinitions("110,*,90"), Margin = new Thickness(0, 1) };
                var k = new TextBlock { FontSize = 11, Opacity = 0.7, VerticalAlignment = VerticalAlignment.Center };
                k.Bind(TextBlock.TextProperty, new Binding("Kind"));
                var n = new TextBlock { FontSize = 12, VerticalAlignment = VerticalAlignment.Center, TextTrimming = TextTrimming.CharacterEllipsis };
                n.Bind(TextBlock.TextProperty, new Binding("Name"));
                n.Bind(ToolTip.TipProperty, new Binding("Tip"));
                var v = new TextBox { FontSize = 12, MinHeight = 24, Padding = new Thickness(4, 1) };
                v.Bind(TextBox.TextProperty, new Binding("Value") { Mode = BindingMode.TwoWay });
                v.Bind(ToolTip.TipProperty, new Binding("Error"));
                v.Bind(TextBox.ForegroundProperty, new Binding("HasError") { Converter = new FuncValueConverter<bool, IBrush>(b => b ? ColourScheme.Pick("#E07060", "#B03A2A") : ColourScheme.Pick("#DCD8CF", "#24221E")) });
                Grid.SetColumn(n, 1); Grid.SetColumn(v, 2);
                g.Children.Add(k); g.Children.Add(n); g.Children.Add(v);
                return g;
            }, true);
            return list;
        }

        Control VariablesTab()
        {
            allValues.IsCheckedChanged += (o, e) => { BuildRows(); };
            var top = new StackPanel { Spacing = 4, Margin = new Thickness(0, 6, 0, 6) };
            top.Children.Add(new TextBlock { FontSize = 12, Opacity = 0.75, TextWrapping = TextWrapping.Wrap, Text = "The current values: script memory first (the imported variables and the conversation's private globals), then the game values it reads and writes. A change takes effect from the next instruction." });
            top.Children.Add(allValues);
            var d = new DockPanel();
            DockPanel.SetDock(top, Dock.Top);
            d.Children.Add(top);
            d.Children.Add(VarList(vars));
            return d;
        }

        Control StartTab()
        {
            talker.SelectionChanged += (o, e) => { if (!filling) { start = null; Load(true); } };
            var p = new StackPanel { Spacing = 6, Margin = new Thickness(0, 6, 12, 6) };
            p.Children.Add(new TextBlock { FontSize = 12, Opacity = 0.75, TextWrapping = TextWrapping.Wrap, Text = "What the game would know when the conversation starts, filled in from the open game (a saved game's PLAYER.DAT and BGLOBALS.DAT, the talker's level object). Reset starts again with these." });
            p.Children.Add(Row("Talker", talker));
            p.Children.Add(Row("Player", playerName, female));
            p.Children.Add(Row("Random", Label("seed"), seed));
            p.Children.Add(Row("", fixedRandom, fixedValue));
            p.Children.Add(Row("Trading", offers));
            p.Children.Add(Row("", demands));
            var apply = new Button { Content = "Reset with these" };
            apply.Click += (o, e) => Reset();
            p.Children.Add(Row("", apply));
            p.Children.Add(Section("The talker's inventory"));
            p.Children.Add(Note("One object per line: the item number (decimal, or hex with 0x), then x followed by a quantity and q followed by a quality; # starts a comment."));
            p.Children.Add(npcInventory);
            p.Children.Add(Section("The player's trade slots"));
            p.Children.Add(Note("The items the player has put up for trade (" + "up to four in UW1, six in UW2" + "); \"selected\" marks the ones offered, as clicking a slot does."));
            p.Children.Add(playerTrade);
            p.Children.Add(Section("Values at the start"));
            p.Children.Add(Note("The talker's and the player's values the game copies in, and the game's values this conversation uses."));
            p.Children.Add(VarItems(startVars));
            return new ScrollViewer { Content = p };
        }

        static Control Row(string label, params Control[] c)
        {
            var s = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8 };
            s.Children.Add(new TextBlock { Text = label, Width = 70, VerticalAlignment = VerticalAlignment.Center });
            foreach (var x in c) { x.VerticalAlignment = VerticalAlignment.Center; s.Children.Add(x); }
            return s;
        }

        static TextBlock Section(string t) { return new TextBlock { Text = t, FontWeight = FontWeight.SemiBold, Margin = new Thickness(0, 8, 0, 0) }; }
        static TextBlock Note(string t) { return new TextBlock { Text = t, FontSize = 12, Opacity = 0.7, TextWrapping = TextWrapping.Wrap }; }

        Control LogTab()
        {
            logList.ItemsSource = log;
            logList.FontFamily = new FontFamily("Cascadia Mono,Consolas,DejaVu Sans Mono,monospace");
            logList.FontSize = 11;
            var clear = new Button { Content = "Clear" };
            clear.Click += (o, e) => log.Clear();
            var top = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8, Margin = new Thickness(0, 6, 0, 6) };
            top.Children.Add(clear);
            top.Children.Add(Note("Instructions, built-in calls with their arguments and results, and writes to memory."));
            var d = new DockPanel();
            DockPanel.SetDock(top, Dock.Top);
            d.Children.Add(top); d.Children.Add(logList);
            return d;
        }

        // ----- the conversation -----

        void OnCurrentChanged() { Load(); }

        GameSession Session { get { return cw.Library.Session; } }

        /// <summary>Takes the conversation the editor shows (the world filled in again for another slot) and resets.</summary>
        void Load(bool keepTalkerChoice = false)
        {
            conv = cw.Current;
            if (conv == null) { header.Text = "No conversation"; sim = null; Refresh(); return; }
            string name = cw.Library.Name(conv.Slot);
            header.Text = (name ?? "No name") + "  ·  conversation " + conv.Slot;
            Title = "Conversation simulator — " + (name ?? "conversation " + conv.Slot);
            decompiled = cw.Library.Decompile(conv);
            if (conv.Slot != slot || start == null)
            {
                bool same = conv.Slot == slot;
                slot = conv.Slot;
                ConversationUser who = null;
                if (keepTalkerChoice && same && talker.SelectedIndex > 0 && talker.SelectedIndex - 1 < users.Count) who = users[talker.SelectedIndex - 1];
                else
                {
                    users = cw.UsersOf(slot);
                    who = users.FirstOrDefault();
                    filling = true;
                    talker.ItemsSource = new[] { "None: default values" }.Concat(users.Select(u => u.ToString())).ToList();
                    talker.SelectedIndex = who == null ? 0 : 1;
                    filling = false;
                }
                start = SimWorld.FromGame(Session, slot, who);
                FillStart();
            }
            Reset();
        }

        void FillStart()
        {
            filling = true;
            playerName.Text = start.PlayerName;
            female.IsChecked = start.PlayerFemale;
            seed.Value = start.RandomSeed;
            fixedRandom.IsChecked = start.FixedRandom.HasValue;
            fixedValue.Value = start.FixedRandom ?? 1;
            offers.IsChecked = start.OffersAccepted;
            demands.IsChecked = start.DemandsGivenIn;
            npcText = ObjectsText(start, start.NpcInventory.Select(i => Tuple.Create(i, false)));
            npcInventory.Text = npcText;
            tradeText = ObjectsText(start, Enumerable.Range(0, start.TradeSlots).Where(i => start.PlayerSlots[i] != 0).Select(i => Tuple.Create(start.PlayerSlots[i], start.PlayerSelected[i])));
            playerTrade.Text = tradeText;
            filling = false;
            BuildStartRows();
        }

        static string ObjectsText(SimWorld w, IEnumerable<Tuple<int, bool>> objects)
        {
            var sb = new StringBuilder();
            foreach (var t in objects)
            {
                var o = w.Get(t.Item1);
                if (o == null) continue;
                sb.AppendFormat(CultureInfo.InvariantCulture, "0x{0:X3}{1} q{2}{3}  # {4}\n", o.Item, o.Count > 1 ? " x" + o.Count : "", o.Quality, t.Item2 ? " selected" : "", w.ItemName(o.Item));
            }
            return sb.ToString();
        }

        /// <summary>Parses an inventory box: (item, quantity, quality, selected) a line. Returns an error, or null.</summary>
        static string ParseObjects(string text, List<Tuple<int, int, int, bool>> into)
        {
            int ln = 0;
            foreach (var raw in (text ?? "").Replace("\r", "").Split('\n'))
            {
                ln++;
                string s = raw;
                int hash = s.IndexOf('#');
                if (hash >= 0) s = s.Substring(0, hash);
                var parts = s.Split(new[] { ' ', '\t', ',' }, StringSplitOptions.RemoveEmptyEntries);
                if (parts.Length == 0) continue;
                int item, qty = 1, q = 40;
                bool sel = false;
                if (!Number(parts[0], out item) || item < 0 || item >= 0x200) return "Line " + ln + ": '" + parts[0] + "' is not an item number (0-511).";
                foreach (var p in parts.Skip(1))
                {
                    string l = p.ToLowerInvariant();
                    if (l == "selected" || l == "*") sel = true;
                    else if (l.StartsWith("x") && int.TryParse(l.Substring(1), out qty) && qty >= 1 && qty < 0x200) { }
                    else if (l.StartsWith("q") && int.TryParse(l.Substring(1), out q) && q >= 0 && q < 64) { }
                    else return "Line " + ln + ": '" + p + "' is not x<quantity>, q<quality> or selected.";
                }
                into.Add(Tuple.Create(item, qty, q, sel));
            }
            return null;
        }

        static bool Number(string s, out int v)
        {
            if (s.StartsWith("0x", StringComparison.OrdinalIgnoreCase)) return int.TryParse(s.Substring(2), NumberStyles.HexNumber, CultureInfo.InvariantCulture, out v);
            return int.TryParse(s, NumberStyles.Integer, CultureInfo.InvariantCulture, out v);
        }

        /// <summary>The start-up world with the panel's values in it. Returns an error, or null.</summary>
        string ApplyStart()
        {
            if (start == null) return null;
            start.PlayerName = string.IsNullOrEmpty(playerName.Text) ? "Avatar" : playerName.Text;
            start.PlayerFemale = female.IsChecked == true;
            start.RandomSeed = (int)(seed.Value ?? 1);
            start.FixedRandom = fixedRandom.IsChecked == true ? (int?)(int)(fixedValue.Value ?? 1) : null;
            start.OffersAccepted = offers.IsChecked == true;
            start.DemandsGivenIn = demands.IsChecked == true;
            if (npcInventory.Text != npcText)
            {
                var l = new List<Tuple<int, int, int, bool>>();
                string err = ParseObjects(npcInventory.Text, l);
                if (err != null) return "The talker's inventory: " + err;
                foreach (int i in start.NpcInventory) start.Objects.Remove(i);
                start.NpcInventory.Clear();
                foreach (var t in l) { var o = start.Add(t.Item1, t.Item2, t.Item3); o.Value = SimWorld.ItemValue(Session, o.Item); start.NpcInventory.Add(o.Index); }
                npcText = npcInventory.Text = ObjectsText(start, start.NpcInventory.Select(i => Tuple.Create(i, false)));
            }
            if (playerTrade.Text != tradeText)
            {
                var l = new List<Tuple<int, int, int, bool>>();
                string err = ParseObjects(playerTrade.Text, l);
                if (err != null) return "The player's trade slots: " + err;
                if (l.Count > start.TradeSlots) return "The player has only " + start.TradeSlots + " trade slots.";
                for (int i = 0; i < start.TradeSlots; i++) { if (start.PlayerSlots[i] != 0) start.Objects.Remove(start.PlayerSlots[i]); start.PlayerSlots[i] = 0; start.PlayerSelected[i] = false; }
                for (int i = 0; i < l.Count; i++)
                {
                    var o = start.Add(l[i].Item1, l[i].Item2, l[i].Item3);
                    o.Value = SimWorld.ItemValue(Session, o.Item);
                    start.PlayerSlots[i] = o.Index;
                    start.PlayerSelected[i] = l[i].Item4;
                }
                tradeText = playerTrade.Text = ObjectsText(start, Enumerable.Range(0, start.TradeSlots).Where(i => start.PlayerSlots[i] != 0).Select(i => Tuple.Create(start.PlayerSlots[i], start.PlayerSelected[i])));
            }
            return null;
        }

        /// <summary>Starts the conversation again from the start-up values (stopped before its first instruction).</summary>
        public void Reset()
        {
            if (conv == null) { sim = null; Refresh(); return; }
            if (users.Count == 0 && cw.UsersOf(slot).Count > 0)
            {
                // the levels were still being read when the window opened
                users = cw.UsersOf(slot);
                filling = true;
                talker.ItemsSource = new[] { "None: default values" }.Concat(users.Select(u => u.ToString())).ToList();
                talker.SelectedIndex = 0;
                filling = false;
            }
            string err = ApplyStart();
            if (err != null) { stateText.Text = err; tabs.SelectedIndex = 1; return; }
            conv = cw.Current ?? conv;
            sim = new ConversationSimulator(conv, cw.CurrentStrings, start, (b, i) => Session.Strings.Get(b, i), SimWorld.SavedGlobals(Session, conv.Slot));
            sim.LogInstructions = logInstructions.IsChecked == true;
            transcript.Clear();
            log.Clear();
            shownOutput = 0;
            lastLog = null;
            BuildRows();
            Refresh();
            if (sim.State == SimState.Running)
                stateText.Text = "Ready. Run (F5) runs to the first menu; Step (F10) runs one instruction." + (cw.ScriptPending ? " The script has unapplied edits, so this runs the applied code." : "");
        }

        public void Step()
        {
            if (sim == null) return;
            sim.Step();
            Refresh();
        }

        /// <summary>Runs until the machine stops (a menu, babl_ask, the end, an error, a stop address, the limit).</summary>
        public void Run()
        {
            if (sim == null) return;
            sim.Breakpoints.Clear();
            foreach (var p in (stopAt.Text ?? "").Split(new[] { ' ', ',', ';' }, StringSplitOptions.RemoveEmptyEntries))
            {
                int a;
                string t = p.StartsWith("0x", StringComparison.OrdinalIgnoreCase) ? p.Substring(2) : p;
                if (int.TryParse(t, NumberStyles.HexNumber, CultureInfo.InvariantCulture, out a)) sim.Breakpoints.Add(a);
            }
            sim.Run((int)(limit.Value ?? 200000));
            Refresh();
        }

        /// <summary>Picks menu choice n (from 1), then runs on if that is ticked.</summary>
        public void Choose(int n)
        {
            if (sim == null || sim.State != SimState.WaitingMenu) return;
            string err = sim.Choose(n);
            if (err != null) { stateText.Text = err; return; }
            if (runOn.IsChecked == true) Run(); else Refresh();
        }

        public void Answer(string text)
        {
            if (sim == null || sim.State != SimState.WaitingAsk) return;
            sim.Answer(text ?? "");
            askBox.Text = "";
            if (runOn.IsChecked == true) Run(); else Refresh();
        }

        static readonly Regex Escapes = new Regex(@"\\[0-9P]");
        static string Clean(string s) { return Escapes.Replace(s ?? "", "").Trim(); }

        void Refresh()
        {
            choices.Children.Clear();
            askPanel.IsVisible = false;
            if (sim == null) { stateText.Text = "Choose a conversation in the conversation editor."; return; }
            for (; shownOutput < sim.Output.Count; shownOutput++)
            {
                var o = sim.Output[shownOutput];
                var row = new TranscriptRow { Output = o, Text = Clean(o.Text), Style = FontStyle.Normal };
                switch (o.Kind)
                {
                    case SimOutputKind.Say: row.Who = "NPC"; row.Brush = ColourScheme.Pick("#E8E2D4", "#2B2A26"); break;
                    case SimOutputKind.Respond: row.Who = "Player"; row.Brush = ColourScheme.Pick("#C9934C", "#8A5A1C"); break;
                    case SimOutputKind.Choice: row.Who = "You"; row.Brush = ColourScheme.Pick("#7FB6E0", "#2F6DA8"); break;
                    case SimOutputKind.Answer: row.Who = "You type"; row.Brush = ColourScheme.Pick("#7FB6E0", "#2F6DA8"); row.Style = FontStyle.Italic; break;
                    case SimOutputKind.Print: row.Who = ""; row.Brush = ColourScheme.Pick("#A9B1A0", "#4A5544"); row.Style = FontStyle.Italic; break;
                    default: row.Who = ""; row.Brush = ColourScheme.Pick("#8E8B85", "#6E6A62"); break;
                }
                transcript.Add(row);
            }
            if (transcript.Count > 0) transcriptList.ScrollIntoView(transcript[transcript.Count - 1]);
            switch (sim.State)
            {
                case SimState.WaitingMenu:
                    stateText.Text = "Choose an answer (or press its number).";
                    for (int i = 0; i < sim.Menu.Texts.Count; i++)
                    {
                        int n = i + 1;
                        var b = new Button
                        {
                            Content = new TextBlock { Text = n + ". " + Clean(sim.Menu.Texts[i]), TextWrapping = TextWrapping.Wrap },
                            HorizontalAlignment = HorizontalAlignment.Stretch, HorizontalContentAlignment = HorizontalAlignment.Left,
                            Tag = n,
                        };
                        b.Click += (o, e) => Choose(n);
                        choices.Children.Add(b);
                    }
                    break;
                case SimState.WaitingAsk:
                    stateText.Text = "The script asks the player to type something (babl_ask).";
                    askPanel.IsVisible = true;
                    askBox.Focus();
                    break;
                case SimState.Ended:
                    stateText.Text = "The conversation is over (" + sim.Steps + " instructions). " + string.Join(" ", sim.EndReport);
                    break;
                case SimState.Error:
                    stateText.Text = "Stopped: " + sim.Error;
                    break;
                default:
                    stateText.Text = string.Format("At {0:X4} after {1} instructions (sp {2}, bp {3}, result {4}).", sim.Pc, sim.Steps, sim.Sp, sim.Bp, sim.Reg);
                    break;
            }
            foreach (var r in vars) r.Refresh();
            SyncLog();
            if (follow.IsChecked == true && sim.State != SimState.Ended) cw.ShowAddress(sim.State == SimState.Running ? sim.Pc : sim.Menu != null ? sim.Menu.Address : sim.Pc, false);
        }

        void SyncLog()
        {
            int from = 0;
            if (lastLog != null)
            {
                int at = sim.Log.LastIndexOf(lastLog);
                if (at < 0) log.Clear(); else from = at + 1;
            }
            for (int i = from; i < sim.Log.Count; i++) log.Add(sim.Log[i].ToString());
            if (sim.Log.Count > 0) lastLog = sim.Log[sim.Log.Count - 1];
            while (log.Count > 5000) log.RemoveAt(0);
            if (log.Count > 0 && tabs.SelectedIndex == 2) logList.ScrollIntoView(log[log.Count - 1]);
        }

        // ----- the value rows -----

        void BuildRows()
        {
            vars.Clear();
            if (sim == null) return;
            var s = sim;
            for (int a = 0; a < s.Variables; a++)
            {
                int addr = a;
                var im = s.Conversation.VariableAt(a);
                bool bound = im != null && ImportedVariables.IsBound(im.Name, s.Kind);
                var info = bound ? ImportedVariables.Find(im.Name) : null;
                vars.Add(new SimVarRow
                {
                    Kind = bound ? "Talker/player" : "Private " + a,
                    Name = s.MemoryName(a),
                    Tip = bound ? "Copied in by the game: " + info.Description + (info.WrittenBack ? "; copied back at the end." : ".") : "The conversation's own variable at address " + a + ", kept in BGLOBALS.DAT.",
                    Get = () => s.GetMem(addr),
                    Put = v => s.SetMemory(addr, v),
                });
            }
            foreach (var r in GameRows(s.World, allValues.IsChecked == true)) vars.Add(r);
            foreach (var r in vars) r.Refresh();
        }

        void BuildStartRows()
        {
            startVars.Clear();
            if (start == null || conv == null) return;
            var w = start;
            foreach (var iv in ImportedVariables.All)
            {
                if (conv.VariableNamed(iv.Name) == null || iv.Name == "play_name" || iv.Name == "play_sex") continue;
                string name = iv.Name;
                startVars.Add(new SimVarRow
                {
                    Kind = "Talker/player", Name = name, Tip = iv.Description,
                    Get = () => { int v; return w.Imported.TryGetValue(name, out v) ? v : 0; },
                    Put = v => { w.Imported[name] = v; return null; },
                });
            }
            foreach (var r in GameRows(w, false)) startVars.Add(r);
            foreach (var r in startVars) r.Refresh();
        }

        /// <summary>Rows for the game's values: the quests, variables, clocks and skills this conversation names (or all of them).</summary>
        List<SimVarRow> GameRows(SimWorld w, bool all)
        {
            var rows = new List<SimVarRow>();
            bool uw2 = w.Uw2;
            int flags = w.QuestFlags, bytes = w.QuestBytes.Length;
            var used = decompiled == null ? new List<GlobalUse>() : decompiled.Globals;
            Func<GlobalKind, IEnumerable<int>, IEnumerable<int>> pick = (k, every) =>
                all ? every : used.Where(u => u.Kind == k).Select(u => u.Number).Distinct().OrderBy(x => x);
            foreach (int q in pick(GlobalKind.Quest, Enumerable.Range(0, flags + bytes + 1)))
            {
                int n = q;
                if (n < flags)
                    rows.Add(new SimVarRow { Kind = "Quest", Name = "quest " + n + " (flag)", Tip = "get_quest/set_quest " + n, Get = () => w.QuestFlag(n), Put = v => { if (v < 0 || v > 1) return "A flag is 0 or 1."; w.SetQuestFlag(n, v); return null; } });
                else if (n < flags + bytes)
                    rows.Add(new SimVarRow { Kind = "Quest", Name = "quest " + n + " (byte)", Tip = "get_quest/set_quest " + n, Get = () => w.QuestBytes[n - flags], Put = v => { if (v < 0 || v > 255) return "Must be 0 to 255."; w.QuestBytes[n - flags] = v; return null; } });
                else
                    rows.Add(new SimVarRow { Kind = "Quest", Name = "quest " + n + "+ (" + (uw2 ? "record byte 0xF6" : "talismans left") + ")", Tip = "What get_quest returns above " + (n - 1), Get = () => w.QuestTop, Put = v => { if (v < 0 || v > 255) return "Must be 0 to 255."; w.QuestTop = v; return null; } });
            }
            foreach (int x in pick(GlobalKind.GameVariable, Enumerable.Range(0, w.GameVariables.Length)))
            {
                int n = x;
                if (n < 0 || n >= w.GameVariables.Length) continue;
                rows.Add(new SimVarRow { Kind = "Game variable", Name = "variable " + n, Tip = "x_traps " + n, Get = () => w.GameVariables[n], Put = v => { if (v < 0 || v > (uw2 ? 0xFF : 0x3F)) return "Must be 0 to " + (uw2 ? 255 : 63) + "."; w.GameVariables[n] = v; return null; } });
            }
            if (uw2)
                foreach (int x in pick(GlobalKind.XClock, Enumerable.Range(0, 16)))
                {
                    int n = x;
                    if (n < 0 || n >= 16) continue;
                    rows.Add(new SimVarRow { Kind = "X clock", Name = "clock " + n, Tip = "x_clock " + n, Get = () => w.XClocks[n], Put = v => { if (v < 0 || v > 255) return "Must be 0 to 255."; w.XClocks[n] = v; return null; } });
                }
            foreach (int x in pick(GlobalKind.Skill, Enumerable.Range(0, 20)))
            {
                int n = x;
                if (n < 0 || n >= 20) continue;
                rows.Add(new SimVarRow { Kind = "Skill", Name = n < PlayerDat.SkillNames.Length ? PlayerDat.SkillNames[n] : "skill " + n, Tip = "x_skills " + n, Get = () => w.Skills[n], Put = v => { if (v < 0 || v > 30) return "Must be 0 to 30."; w.Skills[n] = v; return null; } });
            }
            return rows;
        }
    }
}
