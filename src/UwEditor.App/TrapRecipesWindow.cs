using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using UwEditor.Core;
using UwEditor.Core.Editing;

namespace UwEditor.App
{
    /// <summary>What the trap recipes window has chosen: the recipe, the squares and objects, and the settings.</summary>
    public sealed class RecipeForm
    {
        /// <summary>The recipe, an index into TrapRecipes.Recipes.</summary>
        public int Recipe;
        /// <summary>The squares and objects chosen, by input (door, lever, wall, from, to, backFrom, backTo, plate, object, createAt, trapDoor, delete).</summary>
        public readonly Dictionary<string, PickedPlace> Places = new Dictionary<string, PickedPlace>();
        // lever
        public bool NewLever = true;
        public int LeverItem = 0x175;
        public WallSide Side = WallSide.Auto;
        public DoorAction LeverAction = DoorAction.Toggle;
        public bool LeverUnlock;
        // teleport
        public bool Back;
        // plate
        public bool Pressure;
        // object
        public ObjectEvent When = ObjectEvent.PickUp;
        // the trap
        public RecipeTrap Kind = RecipeTrap.Message;
        public int Damage = 5;
        public bool Poison;
        public string Text = "";
        public int Variable = 1;
        public bool QuestBit;
        public int Operation = 2;
        public int Value = 1;
        public int CreateItem = 0x43;
        public bool Hostile = true;
        // wandering monsters
        public int SpawnItem = -1;
        public double SpawnChance = 30;
        public DoorAction DoorAction = DoorAction.Open;
        public bool Unlock;
        // the trigger
        public bool Reusable = true;
        public bool Creatures;

        public PickedPlace Place(string key) { PickedPlace p; return Places.TryGetValue(key, out p) ? p : null; }
    }

    /// <summary>
    /// Edit ▸ Trap recipes: a list of ready-made trigger and trap chains (a lever for a door,
    /// teleports, a pressure plate, a trigger in an object). Squares and objects are chosen by
    /// clicking in the main view or from its selection; Make builds the chain in one undo step
    /// (TrapRecipes) and selects the new trigger.
    /// </summary>
    public sealed class TrapRecipesWindow : Window
    {
        readonly MainWindow main;
        readonly GameSession session;
        readonly ListBox list = new ListBox { Width = 200 };
        readonly StackPanel body = new StackPanel { Spacing = 8 };
        readonly TextBlock status = new TextBlock { TextWrapping = TextWrapping.Wrap, FontSize = 12, Margin = new Thickness(0, 6, 0, 0) };
        readonly List<Action> refreshPlaces = new List<Action>();

        public RecipeForm Form { get; private set; } = new RecipeForm();
        /// <summary>The last line of the status: what was made, or why not.</summary>
        public string Status { get { return status.Text; } }
        /// <summary>The last recipe made, or null.</summary>
        public RecipeResult LastResult { get; private set; }

        public TrapRecipesWindow(MainWindow main)
        {
            this.main = main;
            session = main.ViewModel.Session;
            Title = "Trap recipes";
            Width = 860; Height = 680; MinWidth = 640; MinHeight = 480;
            WindowStartupLocation = WindowStartupLocation.CenterOwner;
            Form.CreateItem = 0x43;

            list.ItemsSource = TrapRecipes.Recipes.Select(r => r.Name).ToList();
            list.SelectedIndex = 0;
            list.SelectionChanged += (s, e) => { if (list.SelectedIndex >= 0) { Form.Recipe = list.SelectedIndex; Build(); } };

            var make = new Button { Content = "Make", FontWeight = FontWeight.SemiBold };
            make.Click += (s, e) => Make();
            var foot = new StackPanel { Spacing = 4, Margin = new Thickness(0, 8, 0, 0) };
            var buttons = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 10 };
            buttons.Children.Add(make);
            buttons.Children.Add(new TextBlock { Text = "One Edit ▸ Undo step; nothing is written to the game until you save.", VerticalAlignment = VerticalAlignment.Center, Opacity = 0.7, FontSize = 12 });
            foot.Children.Add(buttons);
            foot.Children.Add(status);
            DockPanel.SetDock(foot, Dock.Bottom);

            var right = new DockPanel { Margin = new Thickness(12, 0, 0, 0) };
            right.Children.Add(foot);
            right.Children.Add(new ScrollViewer { Content = body, Padding = new Thickness(0, 0, 12, 0) });
            DockPanel.SetDock(list, Dock.Left);
            var dock = new DockPanel { Margin = new Thickness(16) };
            dock.Children.Add(list);
            dock.Children.Add(right);
            Content = dock;
            ManualWindow.Attach(this, "Trap recipes");
            Build();
        }

        /// <summary>Shows a recipe (an index into TrapRecipes.Recipes).</summary>
        public void ShowRecipe(int index) { list.SelectedIndex = index; }

        // ---- the form

        static TextBlock Label(string t, double w = 140) { return new TextBlock { Text = t, Width = w, VerticalAlignment = VerticalAlignment.Center }; }
        static TextBlock Note(string t) { return new TextBlock { Text = t, FontSize = 12, Opacity = 0.7, TextWrapping = TextWrapping.Wrap, MaxWidth = 560 }; }
        static StackPanel Row(params Control[] c) { var p = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8 }; foreach (var x in c) p.Children.Add(x); return p; }

        static ComboBox Combo(IEnumerable<string> items, int index, Action<int> set, double width = 260)
        {
            var c = new ComboBox { ItemsSource = items.ToList(), SelectedIndex = index, Width = width };
            c.SelectionChanged += (s, e) => { if (c.SelectedIndex >= 0) set(c.SelectedIndex); };
            return c;
        }

        static CheckBox Check(string text, bool value, Action<bool> set)
        {
            var c = new CheckBox { Content = text, IsChecked = value };
            c.IsCheckedChanged += (s, e) => set(c.IsChecked == true);
            return c;
        }

        static NumericUpDown Number(int value, int min, int max, Action<int> set)
        {
            var n = new NumericUpDown { Minimum = min, Maximum = max, Increment = 1, FormatString = "0", Value = value, Width = 130 };
            n.ValueChanged += (s, e) => set((int)(n.Value ?? min));
            return n;
        }

        string Describe(PickedPlace p)
        {
            if (p == null) return "(not chosen)";
            if (p.Object < 0) return p.ToString();
            var m = main.ViewModel.Edits.Model(p.Level);
            var o = m.Objects[p.Object];
            return string.Format("#{0} {1} on ({2}, {3}), level {4}", o.Index, o.Name, o.TileX, o.TileY, p.Level);
        }

        /// <summary>An input chosen in the view: what it is now, Pick in view, Use the selection.</summary>
        Control PlaceRow(string key, string label, bool needObject, string help)
        {
            var value = new TextBlock { VerticalAlignment = VerticalAlignment.Center, MinWidth = 220 };
            Action show = () => value.Text = Describe(Form.Place(key));
            show();
            refreshPlaces.Add(show);
            Action<PickedPlace> take = p =>
            {
                if (p == null) return;
                if (needObject && p.Object < 0) { SetStatus("Choose an object for " + label.ToLowerInvariant() + ", not just a square."); return; }
                Form.Places[key] = needObject ? p : new PickedPlace { Level = p.Level, X = p.X, Y = p.Y };
                show();
                SetStatus(label + ": " + Describe(Form.Places[key]) + ".");
            };
            var pick = new Button { Content = "Pick in view" };
            ToolTip.SetTip(pick, needObject ? "Then click the object in the main view." : "Then click the square in the main view (another level: change level there first).");
            pick.Click += (s, e) =>
            {
                SetStatus(needObject ? "Click the object in the main view." : "Click the square in the main view.");
                main.RequestPlacePick(label + ": click " + (needObject ? "the object" : "the square") + " in the view", p => { take(p); Activate(); });
            };
            var sel = new Button { Content = "Use selection" };
            sel.Click += (s, e) =>
            {
                var p = main.SelectedPlace();
                if (p == null) SetStatus("Nothing is selected in the main view.");
                else take(p);
            };
            var panel = new StackPanel { Spacing = 2 };
            panel.Children.Add(Row(Label(label), value, pick, sel));
            if (!string.IsNullOrEmpty(help)) panel.Children.Add(new Border { Margin = new Thickness(148, 0, 0, 0), Child = Note(help) });
            return panel;
        }

        void Build()
        {
            body.Children.Clear();
            refreshPlaces.Clear();
            var info = TrapRecipes.Recipes[Math.Max(0, Math.Min(TrapRecipes.Recipes.Length - 1, Form.Recipe))];
            body.Children.Add(new TextBlock { Text = info.Name, FontSize = 16, FontWeight = FontWeight.SemiBold });
            body.Children.Add(new TextBlock { Text = info.Explanation, TextWrapping = TextWrapping.Wrap, FontSize = 12, Opacity = 0.8 });
            switch (Form.Recipe)
            {
                case 0: BuildLever(); break;
                case 1: BuildTeleport(); break;
                case 2: BuildPlate(); break;
                case 3: BuildObject(); break;
                case 4: BuildSpawner(); return;     // no trigger
            }
            body.Children.Add(Row(Label("Trigger"), Check("Works every time (otherwise once, then the chain is deleted)", Form.Reusable, v => Form.Reusable = v)));
            body.Children.Add(Row(Label(""), Check("Creatures set it off too", Form.Creatures, v => Form.Creatures = v)));
        }

        static readonly string[] DoorActions = { "Open", "Close", "Toggle (open or close)" };

        void BuildLever()
        {
            body.Children.Add(PlaceRow("door", "Door", true, "A door (closed, open or a portcullis) lying on an open tile."));
            var leverPanel = new StackPanel { Spacing = 8 };
            Action fill = null;
            fill = () =>
            {
                leverPanel.Children.Clear();
                if (Form.NewLever)
                {
                    leverPanel.Children.Add(PlaceRow("wall", "Lever's tile", false, "The open tile the new lever is put on, against one of its walls."));
                    var items = TrapRecipes.LeverItems();
                    leverPanel.Children.Add(Row(Label("Lever"), Combo(items.Select(i => string.Format("0x{0:X3} {1}", i, session.Names.Get(i))), Math.Max(0, items.IndexOf(Form.LeverItem)), i => Form.LeverItem = items[i])));
                    leverPanel.Children.Add(Row(Label("On the wall"), Combo(new[] { "First wall found (north, east, south, west)", "North (+y)", "East (+x)", "South (−y)", "West (−x)" }, (int)Form.Side, i => Form.Side = (WallSide)i)));
                }
                else leverPanel.Children.Add(PlaceRow("lever", "Lever", true, "A lever, switch, button or pull chain (0x170-0x17F) that holds no trigger yet."));
            };
            body.Children.Add(Row(Label("The lever"), Combo(new[] { "A new one on a wall", "One already in the level" }, Form.NewLever ? 0 : 1, i => { Form.NewLever = i == 0; fill(); })));
            fill();
            body.Children.Add(leverPanel);
            body.Children.Add(Row(Label("It does"), Combo(DoorActions, (int)Form.LeverAction - 1, i => Form.LeverAction = (DoorAction)(i + 1))));
            body.Children.Add(Row(Label(""), Check("Unlock the door when it runs", Form.LeverUnlock, v => Form.LeverUnlock = v)));
            body.Children.Add(Note(session.IsUw2
                ? "Underworld II's door trap leaves the lock alone unless told to remove it."
                : "Ultima Underworld's door trap always takes the door's lock off and puts on the lock it carries: the recipe gives it a copy of the door's own lock, unless you choose to unlock."));
        }

        void BuildTeleport()
        {
            body.Children.Add(PlaceRow("from", "Step on", false, "The square that sends the player away (a move trigger and a teleport trap go there)."));
            body.Children.Add(PlaceRow("to", "Arrive at", false, "Where he arrives. For another level, change level in the main window, then pick."));
            var back = new StackPanel { Spacing = 8 };
            Action fill = () =>
            {
                back.Children.Clear();
                if (!Form.Back) return;
                back.Children.Add(PlaceRow("backFrom", "Back: step on", false, "A square on the arrival level, not the arrival square itself (he would be sent straight back)."));
                back.Children.Add(PlaceRow("backTo", "Back: arrive at", false, "A square on the first level, not the first square."));
            };
            body.Children.Add(Row(Label(""), Check("And a way back", Form.Back, v => { Form.Back = v; fill(); })));
            fill();
            body.Children.Add(back);
        }

        void BuildPlate()
        {
            body.Children.Add(PlaceRow("plate", "The plate", false, "The square stepped on."));
            if (session.IsUw2)
                body.Children.Add(Row(Label("Trigger"), Combo(new[] { "Move trigger (walked into)", "Pressure plate (weight comes onto it)" }, Form.Pressure ? 1 : 0, i => Form.Pressure = i == 1)));
            BuildAction(false);
        }

        void BuildObject()
        {
            body.Children.Add(PlaceRow("object", "Object", true, "A single object in the level (not a stack, a creature or an enchanted item) that holds no trigger yet."));
            body.Children.Add(Row(Label("When"), Combo(new[] { "Picked up", "Used", "Looked at" }, (int)Form.When, i => Form.When = (ObjectEvent)i)));
            BuildAction(true);
        }

        int SpawnItem() { return Form.SpawnItem >= 0 ? Form.SpawnItem : session.IsUw2 ? Triggers.Adventurer : 0x43; }

        void BuildSpawner()
        {
            body.Children.Add(PlaceRow("spawn", "Spawner's square", false, "The open square the creatures appear on. The game does not run it while the player is within 8 squares, so it is not seen to happen."));
            var name = new TextBlock { VerticalAlignment = VerticalAlignment.Center, MinWidth = 200 };
            Action showName = () =>
            {
                int i = SpawnItem();
                name.Text = session.IsUw2 && i == Triggers.Adventurer ? "a random monster (adventurer)" : session.Names.Get(i) ?? "?";
            };
            var item = new TextBox { Text = "0x" + SpawnItem().ToString("X3"), Width = 90 };
            item.TextChanged += (s, e) =>
            {
                int v;
                if (MainViewModel.TryParse(item.Text, out v) && v >= 0x40 && v < 0x80) Form.SpawnItem = v;
                showName();
            };
            showName();
            body.Children.Add(Row(Label("Creature (item id)"), item, name));
            if (session.IsUw2)
            {
                var random = new Button { Content = "Random monster (0x07F)" };
                random.Click += (s, e) => { item.Text = "0x07F"; };
                body.Children.Add(Row(Label(""), random));
                body.Children.Add(Note("The random monster is chosen each run: creatures 0x40 + row × 16 + column, with rows up to (the level's place in its world × 3 + the castle plot's progress) / 9 and columns up to the player's level + the castle plot's progress + 1, at most 16. A strong creature is kept only with chance 1 in its strength. So deeper levels of a world, the castle plot moving on and a stronger player all bring stronger monsters."));
            }
            var pct = new TextBlock { VerticalAlignment = VerticalAlignment.Center };
            Action showPct = () => { int q = Triggers.SpawnQuality(Form.SpawnChance); pct.Text = string.Format("% each run (quality {0}: {1:0.#} %)", q, Triggers.SpawnChance(q)); };
            var chance = new NumericUpDown { Minimum = 2, Maximum = 100, Increment = 5, FormatString = "0", Value = (decimal)Form.SpawnChance, Width = 130 };
            chance.ValueChanged += (s, e) => { Form.SpawnChance = (double)(chance.Value ?? 30); showPct(); };
            showPct();
            body.Children.Add(Row(Label("Chance"), chance, pct));
            body.Children.Add(Row(Label(""), Check("Hostile", Form.Hostile, v => Form.Hostile = v)));
            body.Children.Add(Note("The shipped spawners make their creature 5 to 100 % of the runs; half of them between 20 and 50 %. A creature made goes away when the player sleeps, and none is made while one it made is within 4 squares."));
        }

        static readonly string[] Kinds = { "Damage", "Show a message", "Set a variable", "Create an object or creature", "Work a door", "Remove an object" };

        void BuildAction(bool inObject)
        {
            var sub = new StackPanel { Spacing = 8 };
            Action fill = null;
            fill = () =>
            {
                sub.Children.Clear();
                switch (Form.Kind)
                {
                    case RecipeTrap.Damage:
                        sub.Children.Add(Row(Label("Damage"), Number(Form.Damage, 1, 63, v => Form.Damage = v), Check("Poison instead (strength 1-15, the player only)", Form.Poison, v => Form.Poison = v)));
                        break;
                    case RecipeTrap.Message:
                    {
                        var t = new TextBox { Text = Form.Text, AcceptsReturn = true, TextWrapping = TextWrapping.Wrap, Height = 70, Width = 420 };
                        t.TextChanged += (s, e) => Form.Text = t.Text ?? "";
                        sub.Children.Add(Row(Label("Message"), t));
                        sub.Children.Add(Note("Added to STRINGS.PAK block 9 (the text traps' messages). Save it in Tools ▸ Strings: Save levels does not write it. Undo removes the trap, not the string."));
                        break;
                    }
                    case RecipeTrap.SetVariable:
                        if (!session.IsUw2) sub.Children.Add(Row(Label(""), Check("A quest bit (0-31) instead of a game variable (1-63)", Form.QuestBit, v => Form.QuestBit = v)));
                        sub.Children.Add(Row(Label("Variable"), Number(Form.Variable, 0, session.IsUw2 ? 0x19F : 63, v => Form.Variable = v),
                            Note(session.IsUw2 ? "0-255 variables, 256-383 quest flags 0-127, 384-399 quests 128-143, 400-415 X clocks." : "A game variable 1-63, or with the box ticked a quest bit 0-31.")));
                        sub.Children.Add(Row(Label("Operation"), Combo(new[] { "Add", "Subtract", "Set to", "And", "Or", "Xor", "Shift left" }, Math.Min(6, Form.Operation), i => Form.Operation = i, 160),
                            Label("value", 50), Number(Form.Value, 0, session.IsUw2 ? 4095 : 255, v => Form.Value = v)));
                        sub.Children.Add(Note(session.IsUw2 ? "A quest flag is set to (value > 0) whatever the operation, except subtract (unchanged) and xor (toggles)." : "A quest bit is set, cleared by subtract, toggled by xor. A variable keeps 6 bits."));
                        break;
                    case RecipeTrap.CreateObject:
                    {
                        var name = new TextBlock { VerticalAlignment = VerticalAlignment.Center, MinWidth = 200 };
                        Action showName = () => name.Text = Form.CreateItem >= 0 && Form.CreateItem < 0x200 ? session.Names.Get(Form.CreateItem) ?? "?" : "?";
                        var item = new TextBox { Text = "0x" + Form.CreateItem.ToString("X3"), Width = 90 };
                        item.TextChanged += (s, e) =>
                        {
                            int v;
                            if (MainViewModel.TryParse(item.Text, out v) && v >= 0 && v < 0x180) Form.CreateItem = v;
                            showName();
                        };
                        showName();
                        sub.Children.Add(Row(Label("Object (item id)"), item, name));
                        sub.Children.Add(Row(Label(""), Check("A creature is hostile", Form.Hostile, v => Form.Hostile = v)));
                        sub.Children.Add(PlaceRow("createAt", "Appears on", false, "Leave it unchosen for the trigger's own square."));
                        sub.Children.Add(Note("The object is a template kept inside the trap; each time the trap runs, a copy appears. A creature is not made while another created one is within four squares."));
                        break;
                    }
                    case RecipeTrap.Door:
                        sub.Children.Add(PlaceRow("trapDoor", "Door", true, null));
                        sub.Children.Add(Row(Label("It does"), Combo(DoorActions, (int)Form.DoorAction - 1, i => Form.DoorAction = (DoorAction)(i + 1)), Check("Unlock it", Form.Unlock, v => Form.Unlock = v)));
                        break;
                    case RecipeTrap.DeleteObject:
                        sub.Children.Add(PlaceRow("delete", "Remove", true, "An object lying on its tile, not a creature. The trigger then works once."));
                        break;
                }
            };
            body.Children.Add(Row(Label(inObject ? "Then" : "It does"), Combo(Kinds, (int)Form.Kind, i => { Form.Kind = (RecipeTrap)i; fill(); })));
            fill();
            body.Children.Add(sub);
        }

        void SetStatus(string s) { status.Text = s; }

        // ---- making it

        PickedPlace Need(string key, string what, int level = -1, bool obj = false)
        {
            var p = Form.Place(key);
            if (p == null) throw new InvalidOperationException("Choose " + what + ".");
            if (obj && p.Object < 0) throw new InvalidOperationException("Choose an object for " + what + ".");
            if (level >= 0 && p.Level != level) throw new InvalidOperationException(char.ToUpper(what[0]) + what.Substring(1) + " is on level " + p.Level + "; it must be on level " + level + ".");
            return p;
        }

        LevelEditor Editor(int level)
        {
            var m = main.ViewModel.Edits.Model(level);
            if (!m.Present) throw new InvalidOperationException("Level " + level + " is an empty slot.");
            return main.ViewModel.Edits.For(level);
        }

        TrapAction TrapActionFor(int level)
        {
            var a = new TrapAction
            {
                Kind = Form.Kind, Damage = Form.Damage, Poison = Form.Poison, Text = Form.Text, Variable = Form.Variable, QuestBit = Form.QuestBit && !session.IsUw2,
                Operation = Form.Operation, Value = Form.Value, DoorAction = Form.DoorAction, Unlock = Form.Unlock,
            };
            switch (Form.Kind)
            {
                case RecipeTrap.CreateObject:
                {
                    a.Template = ObjectSpec.Default(Form.CreateItem, session.Kind);
                    if (a.Template.IsMobile && Form.Hostile) a.Template.Attitude = 0;
                    var at = Form.Place("createAt");
                    if (at != null)
                    {
                        if (at.Level != level) throw new InvalidOperationException("Where the object appears must be on level " + level + ".");
                        a.X = at.X; a.Y = at.Y;
                    }
                    break;
                }
                case RecipeTrap.Door: a.Door = Need("trapDoor", "the door", level, true).Object; break;
                case RecipeTrap.DeleteObject: a.Delete = Need("delete", "the object to remove", level, true).Object; break;
            }
            return a;
        }

        /// <summary>Makes the chosen recipe; null (and the reason in Status) when it cannot be made.</summary>
        public RecipeResult Make()
        {
            main.CancelPlacePick();
            var t = new TriggerOptions { Reusable = Form.Reusable, Creatures = Form.Creatures };
            RecipeResult r;
            try
            {
                switch (Form.Recipe)
                {
                    case 0:
                    {
                        var door = Need("door", "the door", -1, true);
                        var o = new LeverDoorOptions { Door = door.Object, Action = Form.LeverAction, Unlock = Form.LeverUnlock, Trigger = t, LeverItem = Form.LeverItem, Side = Form.Side };
                        if (Form.NewLever) { var w = Need("wall", "the lever's tile", door.Level); o.WallX = w.X; o.WallY = w.Y; }
                        else o.Lever = Need("lever", "the lever", door.Level, true).Object;
                        r = TrapRecipes.LeverDoor(Editor(door.Level), o);
                        break;
                    }
                    case 1:
                    {
                        var from = Need("from", "the square to step on");
                        var to = Need("to", "where the player arrives");
                        var o = new TeleportOptions { FromX = from.X, FromY = from.Y, ToX = to.X, ToY = to.Y, Back = Form.Back, Trigger = t };
                        if (Form.Back)
                        {
                            var bf = Need("backFrom", "the square of the way back", to.Level);
                            var bt = Need("backTo", "where the way back arrives", from.Level);
                            o.BackFromX = bf.X; o.BackFromY = bf.Y; o.BackToX = bt.X; o.BackToY = bt.Y;
                        }
                        r = TrapRecipes.Teleport(Editor(from.Level), Editor(to.Level), o);
                        break;
                    }
                    case 2:
                    {
                        var plate = Need("plate", "the plate's square");
                        var a = TrapActionFor(plate.Level);
                        r = TrapRecipes.Plate(Editor(plate.Level), plate.X, plate.Y, Form.Pressure && session.IsUw2, a, t, a.Kind == RecipeTrap.Message ? main.Strings() : null);
                        break;
                    }
                    case 4:
                    {
                        var at = Need("spawn", "the spawner's square");
                        r = TrapRecipes.Spawner(Editor(at.Level), new SpawnerOptions { X = at.X, Y = at.Y, Item = SpawnItem(), Chance = Form.SpawnChance, Hostile = Form.Hostile });
                        break;
                    }
                    default:
                    {
                        var obj = Need("object", "the object", -1, true);
                        var a = TrapActionFor(obj.Level);
                        r = TrapRecipes.ObjectTrigger(Editor(obj.Level), obj.Object, Form.When, a, t, a.Kind == RecipeTrap.Message ? main.Strings() : null);
                        break;
                    }
                }
            }
            catch (Exception ex)
            {
                SetStatus(ex.Message);
                return null;
            }
            LastResult = r;
            string text = r.Summary + (r.Back != null ? " " + r.Back.Summary : "");
            if (r.StringIndex >= 0 && Form.Kind == RecipeTrap.Message) text += " The message is string " + r.StringIndex + " of block 9: save it in Tools ▸ Strings.";
            SetStatus(text);
            main.ViewModel.EditStatus = r.Summary;
            main.SelectMade(r.Level, r.Trigger >= 0 ? r.Trigger : r.Trap);
            foreach (var f in refreshPlaces) f();
            return r;
        }
    }
}
