using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Controls.Templates;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using UltimateEditor.Uw1;
using UwEditor.Core;
using UwEditor.Core.Editing;

namespace UwEditor.App
{
    /// <summary>A row of a list of the game tables window: a picture, a label, and whether it changed.</summary>
    public sealed class TableRow
    {
        public int Index { get; set; }
        public Bitmap Picture { get; set; }
        public string Label { get; set; }
        public bool Changed { get; set; }
        public override string ToString() { return Label; }
    }

    /// <summary>
    /// Tools ▸ Game tables: the small tables of the game's DATA folder, one tab each: the
    /// terrain of each texture (TERRAIN.DAT), the classes' attributes and skills (SKILLS.DAT),
    /// the item combinations (CMB.DAT) and the light (SHADES.DAT, and UW2's DL.DAT with the
    /// squares' light flags). Changed values are marked; each tab saves its own file.
    /// </summary>
    public sealed class GameTablesWindow : Window, IShowsTextures
    {
        readonly MainWindow main;
        readonly GameSession session;
        readonly TabControl tabs = new TabControl();
        readonly TextBlock status = new TextBlock { FontSize = 12, Opacity = 0.8, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 8, 0, 0) };
        bool filling;

        public TerrainTable Terrain { get; private set; }
        public SkillsTable Skills { get; private set; }
        public CombinationTable Combinations { get; private set; }
        public ShadesTable Shades { get; private set; }
        public MinimumLightTable MinimumLight { get; private set; }
        IEnumerable<DataTable> Tables { get { return new DataTable[] { Terrain, Skills, Combinations, Shades, MinimumLight }; } }

        public GameTablesWindow(MainWindow main, GameSession s)
        {
            this.main = main;
            session = s;
            Title = "Game tables — " + s.Title;
            Width = 1060; Height = 760; MinWidth = 760; MinHeight = 520;
            WindowStartupLocation = WindowStartupLocation.CenterOwner;
            Terrain = TerrainTable.Load(s);
            Skills = SkillsTable.Load(s);
            Combinations = CombinationTable.Load(s);
            Shades = ShadesTable.Load(s);
            MinimumLight = MinimumLightTable.Load(s);

            tabs.Items.Add(new TabItem { Header = "Terrain", Content = TerrainTab() });
            tabs.Items.Add(new TabItem { Header = "Class skills", Content = SkillsTab() });
            tabs.Items.Add(new TabItem { Header = "Combinations", Content = CombinationsTab() });
            tabs.Items.Add(new TabItem { Header = "Light", Content = LightTab() });

            var head = new StackPanel { Spacing = 4, Margin = new Thickness(0, 0, 0, 8) };
            head.Children.Add(new TextBlock { Text = "Game tables", FontSize = 16, FontWeight = FontWeight.SemiBold });
            head.Children.Add(Note("Small tables in the DATA folder that apply to the whole game, saved games included. Each tab saves its own file, keeping the previous version as NAME.DAT.BKP and the first version as NAME.DAT.ORIG. Changed values are marked."));
            DockPanel.SetDock(head, Dock.Top);
            DockPanel.SetDock(status, Dock.Bottom);
            var dock = new DockPanel { Margin = new Thickness(16) };
            dock.Children.Add(head); dock.Children.Add(status); dock.Children.Add(tabs);
            Content = dock;
            ManualWindow.Attach(this, "Game tables");
            // another game opened: the tables are the old game's (closing asks about unsaved ones)
            Action<LevelModel> other = l => { if (main.ViewModel.Session != session) Close(); };
            main.ViewModel.LevelChanged += other;
            Closed += (o, e) => main.ViewModel.LevelChanged -= other;
        }

        /// <summary>The tab shown: 0 terrain, 1 class skills, 2 combinations, 3 light.</summary>
        public int Tab { get { return tabs.SelectedIndex; } set { tabs.SelectedIndex = value; } }
        public string Status { get { return status.Text; } }

        public bool HasChanges { get { return Tables.Any(t => t.HasChanges); } }

        /// <summary>Saves every table with changes; false when one could not be written.</summary>
        public bool SaveAll()
        {
            bool ok = true;
            foreach (var t in Tables.Where(t => t.HasChanges).ToList()) ok &= Save(t);
            return ok;
        }

        bool Save(DataTable t)
        {
            if (!t.Exists) { status.Text = t.FileName + " is not in the DATA folder."; return false; }
            try
            {
                t.Save();
            }
            catch (Exception e) { status.Text = t.FileName + " not saved: " + e.Message; return false; }
            if (t is TerrainTable) Automap.TerrainChanged();
            if (t is ShadesTable || t is MinimumLightTable) main.GameLightChanged();
            status.Text = t.FileName + " saved." + (t is ShadesTable || t is MinimumLightTable ? " The view's game light follows it." : "") + " The game reads it when it starts" + (t is MinimumLightTable ? " and each time a level is entered." : ".");
            RefreshAll();
            return true;
        }

        void Revert(DataTable t)
        {
            t.Revert();
            status.Text = t.FileName + ": back to the file as saved.";
            RefreshAll();
        }

        Action refreshTerrain, refreshSkills, refreshCombinations, refreshLight;
        /// <summary>A texture file was saved: the texture pictures again.</summary>
        public void RefreshTextures() { thumbs.Clear(); RefreshAll(); }

        void RefreshAll() { refreshTerrain?.Invoke(); refreshSkills?.Invoke(); refreshCombinations?.Invoke(); refreshLight?.Invoke(); }

        // ---- helpers

        static TextBlock Note(string t) { return new TextBlock { Text = t, FontSize = 12, Opacity = 0.75, TextWrapping = TextWrapping.Wrap }; }
        static TextBlock Label(string t, double w = 0) { var l = new TextBlock { Text = t, VerticalAlignment = VerticalAlignment.Center }; if (w > 0) l.Width = w; return l; }
        static StackPanel Row(params Control[] c) { var p = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8 }; foreach (var x in c) if (x != null) p.Children.Add(x); return p; }
        static Button B(string text, Action a) { var b = new Button { Content = text }; b.Click += (o, e) => a(); return b; }
        static NumericUpDown Num(int min, int max, double width = 90) { return new NumericUpDown { Minimum = min, Maximum = max, Increment = 1, FormatString = "0", Width = width, ParsingNumberStyle = System.Globalization.NumberStyles.Integer }; }

        /// <summary>Marks a control whose value differs from the file's.</summary>
        void Mark(TemplatedControl c, bool changed, string was = null)
        {
            if (changed) { c.BorderBrush = WidgetPaint.Brush(this, "Accent", Brushes.Orange); c.BorderThickness = new Thickness(2); c.FontWeight = FontWeight.SemiBold; }
            else { c.ClearValue(BorderBrushProperty); c.ClearValue(BorderThicknessProperty); c.ClearValue(FontWeightProperty); }
            ToolTip.SetTip(c, changed && was != null ? "Was " + was : null);
        }

        void MarkText(TextBlock t, bool changed)
        {
            if (changed) { t.Foreground = WidgetPaint.Brush(this, "Accent", Brushes.Orange); t.FontWeight = FontWeight.SemiBold; }
            else { t.ClearValue(TextBlock.ForegroundProperty); t.ClearValue(TextBlock.FontWeightProperty); }
        }

        Control Foot(DataTable t, Func<string> check = null)
        {
            var info = new TextBlock { FontSize = 12, Opacity = 0.75, VerticalAlignment = VerticalAlignment.Center, TextTrimming = TextTrimming.CharacterEllipsis };
            info.Text = t.Exists ? t.Path : t.FileName + " is not in the DATA folder.";
            var save = B("Save " + t.FileName, () => Save(t));
            save.FontWeight = FontWeight.SemiBold;
            save.IsEnabled = t.Exists;
            var revert = B("Revert", () => Revert(t));
            revert.IsEnabled = t.Exists;
            var p = Row(save, revert, check == null ? null : B("Check", () => { string m = check(); status.Text = string.IsNullOrEmpty(m) ? "No problems found." : m; }), info);
            p.Margin = new Thickness(0, 8, 0, 0);
            return p;
        }

        static ListBox RowList()
        {
            var l = new ListBox();
            l.Bind(ListBox.BackgroundProperty, l.GetResourceObservable("Panel"));
            l.ItemTemplate = new FuncDataTemplate<TableRow>((r, ns) =>
            {
                var sp = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8 };
                if (r?.Picture != null)
                {
                    var img = new Image { Source = r.Picture, Width = 32, Height = 32 };
                    RenderOptions.SetBitmapInterpolationMode(img, BitmapInterpolationMode.None);
                    sp.Children.Add(img);
                }
                var t = new TextBlock { Text = (r?.Changed == true ? "• " : "") + r?.Label, VerticalAlignment = VerticalAlignment.Center };
                if (r?.Changed == true) t.FontWeight = FontWeight.SemiBold;
                sp.Children.Add(t);
                return sp;
            });
            return l;
        }

        readonly Dictionary<int, Bitmap> thumbs = new Dictionary<int, Bitmap>();

        /// <summary>A texture's picture, 32 pixels (floor true: the floor file).</summary>
        Bitmap Thumb(int texture, bool floor)
        {
            int key = (floor ? 0x10000 : 0) + texture;
            Bitmap b;
            if (thumbs.TryGetValue(key, out b)) return b;
            var g = session.Graphics;
            var t = floor ? g.FloorTexture(texture) : g.WallTexture(texture);
            b = t == null ? null : MainViewModel.ToBitmap(g.TextureRgba(t, floor ? g.FloorEdge : g.WallEdge, 32));
            thumbs[key] = b;
            return b;
        }

        // ---- TERRAIN.DAT

        readonly ObservableCollection<TableRow> terrainRows = new ObservableCollection<TableRow>();
        readonly ListBox terrainList = RowList();
        bool terrainFloors;

        /// <summary>The terrain list's textures (UW1: walls or floors).</summary>
        public bool TerrainFloors { get { return terrainFloors; } }
        public ListBox TerrainList { get { return terrainList; } }

        Control TerrainTab()
        {
            var t = Terrain;
            bool uw2 = session.IsUw2;
            var title = new TextBlock { FontWeight = FontWeight.SemiBold, FontSize = 14 };
            var big = new Image { Width = 96, Height = 96 };
            RenderOptions.SetBitmapInterpolationMode(big, BitmapInterpolationMode.None);
            var was = new TextBlock { FontSize = 12, Opacity = 0.8, TextWrapping = TextWrapping.Wrap };
            var raw = Num(0, 0xFFFF, 120);
            TerrainWord = raw;
            var uw1Choice = new ComboBox { Width = 320 };
            var cls = new ComboBox { Width = 200, ItemsSource = TerrainTable.Uw2Classes.Select((n, i) => i + ": " + n).ToList() };
            var sub = new ComboBox { Width = 260 };
            var kind = new ComboBox { Width = 260, ItemsSource = Enumerable.Range(0, 8).Select(i => i + ": " + TerrainTable.Uw2KindName(i)).ToList() };
            var revertOne = new Button { Content = "Revert this texture" };
            List<int> choiceValues = new List<int>();

            Func<int> entry = () => terrainList.SelectedItem is TableRow r ? r.Index : -1;
            Func<int> texture = () => { int e = entry(); return e < 0 ? -1 : uw2 || !terrainFloors ? e : e - 0x100; };

            TableRow MakeRow(int tex)
            {
                int e = t.EntryOf(tex, terrainFloors);
                return new TableRow { Index = e, Picture = Thumb(tex, terrainFloors && !uw2), Label = string.Format("{0}: {1}", tex, TerrainTable.Describe(session.Kind, terrainFloors, t.Get(e))), Changed = t.IsChanged(e) };
            }

            Action fillEditor = () =>
            {
                int e = entry(), tex = texture();
                filling = true;
                try
                {
                    bool on = e >= 0;
                    raw.IsEnabled = uw1Choice.IsEnabled = cls.IsEnabled = sub.IsEnabled = kind.IsEnabled = revertOne.IsEnabled = on;
                    if (!on) { title.Text = "No texture selected"; big.Source = null; was.Text = ""; return; }
                    int v = t.Get(e);
                    title.Text = (uw2 ? "Texture " : terrainFloors ? "Floor texture " : "Wall texture ") + tex + (uw2 ? " (T64.TR)" : terrainFloors ? " (F32.TR)" : " (W64.TR)");
                    big.Source = Thumb(tex, terrainFloors && !uw2);
                    raw.Value = v;
                    Mark(raw, t.IsChanged(e), string.Format("0x{0:X4}", t.OriginalValue(e)));
                    was.Text = string.Format("Word {0} of TERRAIN.DAT (offset 0x{1:X3}): 0x{2:X4}, {3}.", e, 2 * e, v, TerrainTable.Describe(session.Kind, terrainFloors, v))
                        + (t.IsChanged(e) ? string.Format(" In the file: 0x{0:X4}, {1}.", t.OriginalValue(e), TerrainTable.Describe(session.Kind, terrainFloors, t.OriginalValue(e))) : "");
                    if (uw2)
                    {
                        int c = TerrainTable.Uw2Class(v);
                        cls.SelectedIndex = c;
                        sub.ItemsSource = Enumerable.Range(0, 8).Select(i => i + ": " + TerrainTable.Uw2SubName(c, i)).ToList();
                        sub.SelectedIndex = TerrainTable.Uw2Sub(v);
                        kind.SelectedIndex = TerrainTable.Uw2Kind(v);
                    }
                    else
                    {
                        var list = (terrainFloors ? TerrainTable.Uw1Floors : TerrainTable.Uw1Walls).ToList();
                        if (!list.Any(p => p.Key == v)) list.Add(new KeyValuePair<int, string>(v, string.Format("0x{0:X} (not a known type)", v)));
                        choiceValues = list.Select(p => p.Key).ToList();
                        uw1Choice.ItemsSource = list.Select(p => string.Format("0x{0:X2}: {1}", p.Key, p.Value)).ToList();
                        uw1Choice.SelectedIndex = choiceValues.IndexOf(v);
                    }
                }
                finally { filling = false; }
            };

            Action fillList = () =>
            {
                int keep = terrainList.SelectedIndex;
                filling = true;
                terrainRows.Clear();
                for (int i = 0; i < t.Textures(terrainFloors); i++) terrainRows.Add(MakeRow(i));
                terrainList.SelectedIndex = Math.Min(Math.Max(0, keep), terrainRows.Count - 1);
                filling = false;
                fillEditor();
            };

            Action<int> set = v =>
            {
                int e = entry();
                if (filling || e < 0) return;
                try { t.Set(e, v); }
                catch (Exception ex) { status.Text = ex.Message; return; }
                int i = terrainList.SelectedIndex;
                filling = true;
                terrainRows[i] = MakeRow(texture());
                terrainList.SelectedIndex = i;
                filling = false;
                fillEditor();
                status.Text = string.Format("Texture {0}: {1}. Save TERRAIN.DAT to keep it.", texture(), TerrainTable.Describe(session.Kind, terrainFloors, v));
            };

            terrainList.ItemsSource = terrainRows;
            terrainList.SelectionChanged += (o, e) => { if (!filling) fillEditor(); };
            raw.ValueChanged += (o, e) => { if (raw.Value.HasValue) set((int)raw.Value.Value); };
            uw1Choice.SelectionChanged += (o, e) => { if (uw1Choice.SelectedIndex >= 0 && uw1Choice.SelectedIndex < choiceValues.Count) set(choiceValues[uw1Choice.SelectedIndex]); };
            Action compose = () => { int e = entry(); if (e >= 0 && cls.SelectedIndex >= 0 && sub.SelectedIndex >= 0 && kind.SelectedIndex >= 0) set(TerrainTable.Uw2Compose(t.Get(e), cls.SelectedIndex, sub.SelectedIndex, kind.SelectedIndex)); };
            cls.SelectionChanged += (o, e) => compose();
            sub.SelectionChanged += (o, e) => compose();
            kind.SelectionChanged += (o, e) => compose();
            revertOne.Click += (o, e) => { int en = entry(); if (en >= 0) set(t.OriginalValue(en)); };
            refreshTerrain = fillList;

            var editor = new StackPanel { Spacing = 8, Margin = new Thickness(16, 0, 0, 0) };
            editor.Children.Add(Row(big, title));
            if (uw2)
            {
                editor.Children.Add(Row(Label("Class", 90), cls));
                editor.Children.Add(Row(Label("Current / ice", 90), sub));
                editor.Children.Add(Row(Label("Wall kind", 90), kind));
            }
            else editor.Children.Add(Row(Label("Type", 90), uw1Choice));
            editor.Children.Add(Row(Label("Word", 90), raw, revertOne));
            editor.Children.Add(was);
            editor.Children.Add(Note(uw2
                ? "Bits 6-7 are the class: water lets you swim and fish, lava burns, and ice is slippery (include/map.h TERR_CLASS). Bits 3-5 are, in water, a current that carries the swimmer and, on ice, the grip: 7 grips and 0-6 slide (motion/PHYSICS.C). Bits 0-2 mark special walls: 3 and 4 are drawn and mapped as stairs (3d/DRAWOBJ.C, ui/AUTOMAP.C), and 5 is a shaft you can look down (obj/LOOK.C). T64.TR has 264 textures; TERRAIN.DAT covers the first 256."
                : "Walls: the special walls the game looks for (the ankh shrines, the princess, windows and the textured door; include/map.h TERR_*). Floors: water (for swimming and fishing) and lava; the class is the word shifted right by 4 (critter/PATHFIND.C, motion/COLLIDE.C). The automap draws water and lava floors in their own colours."));

            var top = new StackPanel { Spacing = 6, Margin = new Thickness(0, 4, 0, 8) };
            if (!uw2)
            {
                var which = new ComboBox { ItemsSource = new[] { "Wall textures (W64.TR)", "Floor textures (F32.TR)" }, SelectedIndex = 0, Width = 240 };
                which.SelectionChanged += (o, e) => { terrainFloors = which.SelectedIndex == 1; terrainList.SelectedIndex = -1; fillList(); };
                top.Children.Add(Row(Label("Show"), which));
                WhichTerrain = which;
            }
            top.Children.Add(Note("TERRAIN.DAT gives each texture a terrain word. Choose a texture on the left, then its terrain."));

            var grid = new Grid { ColumnDefinitions = new ColumnDefinitions("320,*") };
            grid.Children.Add(terrainList);
            var scroll = new ScrollViewer { Content = editor };
            Grid.SetColumn(scroll, 1);
            grid.Children.Add(scroll);
            var dock = new DockPanel();
            DockPanel.SetDock(top, Dock.Top);
            var foot = Foot(t);
            DockPanel.SetDock(foot, Dock.Bottom);
            dock.Children.Add(top); dock.Children.Add(foot); dock.Children.Add(grid);
            if (t.Exists) fillList();
            return dock;
        }

        /// <summary>The terrain tab's box for the selected texture's word.</summary>
        public NumericUpDown TerrainWord { get; private set; }

        /// <summary>UW1: the walls or floors choice of the terrain tab.</summary>
        public ComboBox WhichTerrain { get; private set; }

        // ---- SKILLS.DAT

        int skillClass;
        /// <summary>The class shown in the class skills tab.</summary>
        public ComboBox SkillClass { get; private set; }

        Control SkillsTab()
        {
            var t = Skills;
            var panel = new StackPanel { Spacing = 10 };
            var classBox = new ComboBox { ItemsSource = Enumerable.Range(0, SkillsDat.Classes).Select(SkillsTable.ClassName).ToList(), SelectedIndex = 0, Width = 200 };
            SkillClass = classBox;
            string[] attrNames = { "Strength", "Dexterity", "Intelligence", "Bonus points" };
            var attrs = new NumericUpDown[4];
            var attrRow = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8 };
            for (int a = 0; a < 4; a++)
            {
                int aa = a;
                attrs[a] = Num(0, 255, 110);
                attrs[a].ValueChanged += (o, e) =>
                {
                    if (filling || !attrs[aa].Value.HasValue) return;
                    t.SetAttribute(skillClass, aa, (int)attrs[aa].Value.Value);
                    refreshSkills();
                };
                attrRow.Children.Add(Label(attrNames[a]));
                attrRow.Children.Add(attrs[a]);
            }
            var entryHeads = new TextBlock[SkillsDat.EntriesPerClass];
            var boxes = new CheckBox[SkillsDat.EntriesPerClass, SkillsDat.Skills];
            var entries = new StackPanel { Spacing = 10 };
            for (int k = 0; k < SkillsDat.EntriesPerClass; k++)
            {
                entryHeads[k] = new TextBlock { TextWrapping = TextWrapping.Wrap };
                var wrap = new WrapPanel();
                for (int sk = 0; sk < SkillsDat.Skills; sk++)
                {
                    int kk = k, ss = sk;
                    var c = new CheckBox { Content = t.Exists ? t.SkillName(sk) : PlayerSkill(sk), Width = 130 };
                    c.IsCheckedChanged += (o, e) =>
                    {
                        if (filling) return;
                        t.Toggle(skillClass, kk, ss, c.IsChecked == true);
                        refreshSkills();
                    };
                    boxes[k, sk] = c;
                    wrap.Children.Add(c);
                }
                var block = new StackPanel { Spacing = 2 };
                block.Children.Add(entryHeads[k]);
                block.Children.Add(wrap);
                entries.Children.Add(block);
            }
            var warnings = new TextBlock { TextWrapping = TextWrapping.Wrap, FontSize = 12 };
            warnings.Bind(TextBlock.ForegroundProperty, warnings.GetResourceObservable("Warn"));

            refreshSkills = () =>
            {
                if (!t.Exists) return;
                filling = true;
                try
                {
                    for (int a = 0; a < 4; a++)
                    {
                        attrs[a].Value = t.Attribute(skillClass, a);
                        Mark(attrs[a], t.AttributeChanged(skillClass, a), t.OriginalAttribute(skillClass, a).ToString());
                    }
                    for (int k = 0; k < SkillsDat.EntriesPerClass; k++)
                    {
                        var ch = t.Choice(skillClass, k);
                        entryHeads[k].Text = string.Format("Entry {0}: {1}{2}", k + 1, t.Explain(skillClass, k), t.ChoiceChanged(skillClass, k) ? " (changed)" : "");
                        MarkText(entryHeads[k], t.ChoiceChanged(skillClass, k));
                        for (int sk = 0; sk < SkillsDat.Skills; sk++) boxes[k, sk].IsChecked = ch.Contains(sk);
                    }
                    var w = t.Check();
                    warnings.Text = string.Join("\n", w);
                }
                finally { filling = false; }
            };
            classBox.SelectionChanged += (o, e) => { skillClass = Math.Max(0, classBox.SelectedIndex); refreshSkills(); };

            panel.Children.Add(Note("A new character gets the class's strength, dexterity and intelligence, then its bonus points, added 1-4 at a time to random attributes, none going above 30 (game/CHARGEN.C roll_stats). Then come the class's five entries, in order: an empty entry gives nothing, a single skill is given outright, and several skills make a menu to pick one from (set_sklmnu). Every pick adds to the skill. An empty entry also cancels the entries after it, because the game does not step past it."));
            panel.Children.Add(Row(Label("Class"), classBox));
            panel.Children.Add(attrRow);
            panel.Children.Add(entries);
            panel.Children.Add(warnings);
            var dock = new DockPanel();
            var foot = Foot(t, () => string.Join("\n", t.Check()));
            DockPanel.SetDock(foot, Dock.Bottom);
            dock.Children.Add(foot);
            dock.Children.Add(new ScrollViewer { Content = panel, Padding = new Thickness(0, 4, 12, 4) });
            refreshSkills();
            return dock;
        }

        static string PlayerSkill(int i) { return i < UltimateEditor.Games.PlayerDat.SkillNames.Length ? UltimateEditor.Games.PlayerDat.SkillNames[i] : "skill " + i; }

        // ---- CMB.DAT

        List<string> itemNames;
        /// <summary>The combinations tab's controls by rule: in use, first, first used up, second, second used up, output.</summary>
        public readonly List<Control[]> CombinationRows = new List<Control[]>();

        Control CombinationsTab()
        {
            var t = Combinations;
            itemNames = Enumerable.Range(0, 0x200).Select(i => string.Format("0x{0:X3} {1}", i, session.Names.Get(i))).ToList();
            var panel = new StackPanel { Spacing = 6 };
            panel.Children.Add(Note(session.IsUw2
                ? "Drop one item on the other in the inventory, in either order, and the rule's output is made. Neither item may be a stack or hold anything. The game takes the first rule that names both items; a source marked used up goes away (obj/COMBINE.C). Underworld II reads every rule; its unused ones are 0xFFFF."
                : "Drop one item on the other in the inventory, in either order, and the rule's output is made. Neither item may be a stack or hold anything. The game takes the first rule that names both items and has a source marked used up; a used-up source goes away (obj/COMBINE.C)."));
            var warnings = new TextBlock { TextWrapping = TextWrapping.Wrap, FontSize = 12 };
            warnings.Bind(TextBlock.ForegroundProperty, warnings.GetResourceObservable("Warn"));
            var marks = new List<TextBlock>();
            Action refresh = null;
            for (int r = 0; r < CmbDat.Rules; r++)
            {
                int rule = r;
                var used = new CheckBox { Content = "In use", Width = 90 };
                var items = new ComboBox[3];
                var upBoxes = new CheckBox[2];
                for (int w = 0; w < 3; w++)
                {
                    int ww = w;
                    items[w] = new ComboBox { ItemsSource = itemNames, Width = 210, MaxDropDownHeight = 420 };
                    items[w].SelectionChanged += (o, e) =>
                    {
                        if (filling || items[ww].SelectedIndex < 0) return;
                        t.SetItem(rule, ww, items[ww].SelectedIndex);
                        refresh();
                    };
                }
                for (int w = 0; w < 2; w++)
                {
                    int ww = w;
                    upBoxes[w] = new CheckBox { Content = "used up" };
                    upBoxes[w].IsCheckedChanged += (o, e) =>
                    {
                        if (filling) return;
                        t.SetUsedUp(rule, ww, upBoxes[ww].IsChecked == true);
                        refresh();
                    };
                }
                used.IsCheckedChanged += (o, e) =>
                {
                    if (filling) return;
                    if (used.IsChecked == true) t.MakeRule(rule, 0, 0, 0); else t.Clear(rule);
                    refresh();
                };
                var mark = new TextBlock { VerticalAlignment = VerticalAlignment.Center, FontSize = 12, Width = 60 };
                marks.Add(mark);
                CombinationRows.Add(new Control[] { used, items[0], upBoxes[0], items[1], upBoxes[1], items[2] });
                panel.Children.Add(Row(Label((rule + 1) + ".", 24), used, items[0], upBoxes[0], Label("+"), items[1], upBoxes[1], Label("→"), items[2], mark));
            }
            refresh = () =>
            {
                if (!t.Exists) return;
                filling = true;
                try
                {
                    for (int r = 0; r < CmbDat.Rules; r++)
                    {
                        var c = CombinationRows[r];
                        bool empty = t.IsEmpty(r);
                        ((CheckBox)c[0]).IsChecked = !empty;
                        for (int w = 0; w < 3; w++)
                        {
                            var box = (ComboBox)c[w == 0 ? 1 : w == 1 ? 3 : 5];
                            box.IsEnabled = !empty;
                            box.SelectedIndex = empty ? -1 : t.Item(r, w);
                            Mark(box, t.Changed(r, w), string.Format("0x{0:X4}", OriginalWord(t, r, w)));
                        }
                        for (int w = 0; w < 2; w++) { var u = (CheckBox)c[w == 0 ? 2 : 4]; u.IsEnabled = !empty; u.IsChecked = t.UsedUp(r, w); }
                        marks[r].Text = t.RuleChanged(r) ? "changed" : !empty && !t.Active(r) ? "ignored" : "";
                        MarkText(marks[r], t.RuleChanged(r));
                    }
                    warnings.Text = string.Join("\n", t.Check());
                }
                finally { filling = false; }
            };
            refreshCombinations = refresh;
            panel.Children.Add(warnings);
            var dock = new DockPanel();
            var foot = Foot(t, () => string.Join("\n", t.Check()));
            DockPanel.SetDock(foot, Dock.Bottom);
            dock.Children.Add(foot);
            dock.Children.Add(new ScrollViewer { Content = panel, Padding = new Thickness(0, 4, 12, 4), HorizontalScrollBarVisibility = ScrollBarVisibility.Auto });
            refresh();
            return dock;
        }

        static int OriginalWord(CombinationTable t, int rule, int which)
        {
            var b = t.OriginalBytes;
            int i = 2 * (rule * 3 + which);
            return b != null && i + 1 < b.Length ? b[i] | b[i + 1] << 8 : 0;
        }

        // ---- SHADES.DAT, DL.DAT and the light flags

        /// <summary>The light tab's SHADES.DAT boxes, [light level, column].</summary>
        public NumericUpDown[,] ShadeBoxes { get; private set; }
        /// <summary>UW2: the levels of DL.DAT.</summary>
        public ListBox MinimumLightList { get; private set; }

        Control LightTab()
        {
            var t = Shades;
            var panel = new StackPanel { Spacing = 8 };
            panel.Children.Add(Note("SHADES.DAT: each light level is a row. A square d squares away is drawn with shade min(14, max(0, isqrt(128 d²) × smooth_div / 64 + smooth_lowpass) + smooth_base), 0 brightest; beyond curvrad it is not drawn (map/LIGHTING.C, 3d/VIEW3D.C preset_grid). The swatches show the shade at distance 0-15. The Light box above the 3D view shows a light level."));
            var grid = new Grid { ColumnDefinitions = new ColumnDefinitions("200,100,100,100,100,100,100,*"), RowDefinitions = new RowDefinitions(string.Join(",", Enumerable.Repeat("Auto", 9))) };
            for (int c = 0; c < 6; c++)
            {
                var h = new TextBlock { Text = ShadesDat.Columns[c], FontWeight = FontWeight.SemiBold, FontSize = 12, Margin = new Thickness(0, 0, 0, 4) };
                ToolTip.SetTip(h, ShadesDat.ColumnHelp[c] + " Range here " + ShadesTable.Min[c] + "-" + ShadesTable.Max[c] + ".");
                Grid.SetColumn(h, c + 1);
                grid.Children.Add(h);
            }
            ShadeBoxes = new NumericUpDown[8, 6];
            var swatches = new StackPanel[8];
            Action refreshShades = null;
            for (int r = 0; r < 8; r++)
            {
                int row = r;
                var name = new TextBlock { Text = ShadesDat.LevelNames[r], VerticalAlignment = VerticalAlignment.Center, FontSize = 12 };
                Grid.SetRow(name, r + 1);
                grid.Children.Add(name);
                for (int c = 0; c < 6; c++)
                {
                    int col = c;
                    var n = Num(ShadesTable.Min[c], ShadesTable.Max[c], 92);
                    n.ShowButtonSpinner = false;                  // typed, or the arrow keys and the wheel
                    n.Margin = new Thickness(0, 2, 4, 2);
                    n.ValueChanged += (o, e) =>
                    {
                        if (filling || !n.Value.HasValue) return;
                        try { t.Set(row, col, (int)n.Value.Value); }
                        catch (Exception ex) { status.Text = ex.Message; return; }
                        refreshShades();
                    };
                    ShadeBoxes[r, c] = n;
                    Grid.SetRow(n, r + 1); Grid.SetColumn(n, c + 1);
                    grid.Children.Add(n);
                }
                swatches[r] = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 1, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(8, 0, 0, 0) };
                Grid.SetRow(swatches[r], r + 1); Grid.SetColumn(swatches[r], 7);
                grid.Children.Add(swatches[r]);
            }
            var shadeWarn = new TextBlock { TextWrapping = TextWrapping.Wrap, FontSize = 12 };
            shadeWarn.Bind(TextBlock.ForegroundProperty, shadeWarn.GetResourceObservable("Warn"));
            refreshShades = () =>
            {
                if (!t.Exists) return;
                filling = true;
                try
                {
                    var orig = t.OriginalBytes;
                    var warn = new List<string>();
                    var light = main.ViewModel.Light;
                    for (int r = 0; r < 8; r++)
                    {
                        for (int c = 0; c < 6; c++)
                        {
                            ShadeBoxes[r, c].Value = t.Get(r, c);
                            Mark(ShadeBoxes[r, c], t.IsChanged(r, c), ((short)(orig[r * 12 + c * 2] | orig[r * 12 + c * 2 + 1] << 8)).ToString());
                        }
                        swatches[r].Children.Clear();
                        var shades = t.Dat.ShadeByDistance(r);
                        for (int d = 0; d < 16; d++)
                        {
                            int sh = shades[d];
                            float f = sh >= 15 ? 0f : light != null ? (r == 5 ? light.MonoFactors[sh] : light.Factors[sh]) : 1f - sh / 15f;
                            byte g = (byte)Math.Clamp((int)(f * 210), 0, 255);
                            var sw = new Border { Width = 12, Height = 18, Background = new SolidColorBrush(Color.FromRgb(g, g, (byte)Math.Clamp(g * 0.85, 0, 255))) };
                            ToolTip.SetTip(sw, string.Format("Distance {0}: shade {1}", d, sh >= 15 ? "15 (not drawn)" : sh.ToString()));
                            swatches[r].Children.Add(sw);
                        }
                        string w = t.Dat.Check(r);
                        if (w.Length > 0) warn.Add(ShadesDat.LevelNames[r] + ": " + w.Trim());
                    }
                    shadeWarn.Text = string.Join("\n", warn);
                }
                finally { filling = false; }
            };
            panel.Children.Add(grid);
            panel.Children.Add(shadeWarn);
            panel.Children.Add(Foot(t, () => shadeWarn.Text));

            Action refreshDl = () => { };
            if (session.IsUw2) refreshDl = MinimumLightPart(panel);
            else panel.Children.Add(Note("Underworld has no DL.DAT: its light comes from what the player carries and the light spells. The squares' light flag (bit 8 of the tile) is kept but not used for light (UW1's motion/PHYSICS.C has no light check)."));
            refreshLight = () => { refreshShades(); refreshDl(); };
            refreshShades();
            refreshDl();
            return new ScrollViewer { Content = panel, Padding = new Thickness(0, 4, 12, 4) };
        }

        /// <summary>UW2's DL.DAT and the light flags of the chosen level, added to the light tab.</summary>
        Action MinimumLightPart(StackPanel panel)
        {
            var t = MinimumLight;
            var slots = session.LevelSlots();
            panel.Children.Add(new TextBlock { Text = "Minimum light of each level (DL.DAT)", FontWeight = FontWeight.SemiBold, Margin = new Thickness(0, 16, 0, 0) });
            panel.Children.Add(Note("A level can have a minimum light: the light is never lower than that, whatever the player carries. Each square's light flag says where it applies: a value of 10 or more applies it on squares without the flag, and 1-9 on squares with it (game/PLAYDATA.C load_dl, motion/PHYSICS.C change_GrSq). That is why the castle's corridors are dark: level 1 has 14, which means light 4 on unflagged squares, and the corridors are flagged. The game works out the light again whenever the player steps between squares whose flags differ."));
            var rows = new ObservableCollection<TableRow>();
            var list = RowList();
            list.Height = 260;
            list.ItemsSource = rows;
            MinimumLightList = list;
            var min = Num(0, 9, 90);
            var where = new ComboBox { ItemsSource = new[] { "on squares with the light flag (1-9)", "on squares without the flag (10-19)" }, Width = 300 };
            var explain = new TextBlock { TextWrapping = TextWrapping.Wrap, FontSize = 12 };
            var flags = new TextBlock { TextWrapping = TextWrapping.Wrap, FontSize = 12 };
            Func<int> level = () => list.SelectedItem is TableRow r ? r.Index : -1;
            Func<int, TableRow> make = l => new TableRow { Index = l, Label = string.Format("{0} — {1}", l - 1 < slots.Count ? slots[l - 1].Name : "Level " + l, t.Explain(l)), Changed = t.IsChanged(l) };
            Action fillEditor = () =>
            {
                int l = level();
                filling = true;
                try
                {
                    min.IsEnabled = where.IsEnabled = l > 0;
                    if (l <= 0) { explain.Text = flags.Text = ""; return; }
                    min.Value = t.Minimum(l);
                    where.SelectedIndex = t.OnUnflagged(l) ? 1 : 0;
                    Mark(min, t.IsChanged(l), (t.OriginalBytes[l - 1] % 10).ToString());
                    Mark(where, t.IsChanged(l));
                    explain.Text = string.Format("Level {0}, DL.DAT byte {1}: {2}.", l, t.Raw(l), t.Explain(l));
                    var m = main.ViewModel.Edits != null ? main.ViewModel.Edits.Model(l) : session.LoadLevel(l);
                    int open;
                    int n = m.Present ? LightFlags.Count(m, out open) : (open = 0);
                    flags.Text = m.Present ? string.Format("{0} of its {1} open squares have the light flag. Paint flags with the Paint tool (Light flag on / off); the buttons below change every open square at once (Edit ▸ Undo steps back).", n, open) : "The level slot is empty.";
                }
                finally { filling = false; }
            };
            Action fillList = () =>
            {
                int keep = list.SelectedIndex;
                filling = true;
                rows.Clear();
                for (int l = 1; l <= t.Count; l++) rows.Add(make(l));
                int cur = main.ViewModel.Level != null ? main.ViewModel.Level.Level - 1 : 0;
                list.SelectedIndex = keep >= 0 ? keep : Math.Min(cur, rows.Count - 1);
                filling = false;
                fillEditor();
            };
            Action set = () =>
            {
                int l = level();
                if (filling || l <= 0 || !min.Value.HasValue || where.SelectedIndex < 0) return;
                try { t.Set(l, (int)min.Value.Value, where.SelectedIndex == 1); }
                catch (Exception ex) { status.Text = ex.Message; return; }
                int i = list.SelectedIndex;
                filling = true; rows[i] = make(l); list.SelectedIndex = i; filling = false;
                fillEditor();
                status.Text = string.Format("Level {0}: {1}. Save DL.DAT to keep it.", l, t.Explain(l));
            };
            list.SelectionChanged += (o, e) => { if (!filling) fillEditor(); };
            min.ValueChanged += (o, e) => set();
            where.SelectionChanged += (o, e) => set();
            Action<bool> all = on =>
            {
                int l = level();
                var vm = main.ViewModel;
                if (l <= 0 || vm.Edits == null || !vm.Edits.Model(l).Present) return;
                try { LightFlags.SetAll(vm.Edits.For(l), on); }
                catch (Exception ex) { status.Text = ex.Message; return; }
                status.Text = string.Format("Level {0}: light flag {1} every open square. Edit ▸ Undo steps back; Save levels keeps it.", l, on ? "set on" : "cleared on");
                fillEditor();
            };
            panel.Children.Add(list);
            panel.Children.Add(Row(Label("Minimum light", 110), min, where));
            panel.Children.Add(explain);
            panel.Children.Add(flags);
            panel.Children.Add(Row(B("Flag every open square", () => all(true)), B("Clear every flag", () => all(false))));
            panel.Children.Add(Foot(t));
            if (t.Exists) fillList();
            return () => { if (t.Exists) fillList(); };
        }
    }
}
