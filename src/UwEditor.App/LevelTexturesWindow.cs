using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using UwEditor.Core;
using UwEditor.Core.Editing;

namespace UwEditor.App
{
    /// <summary>
    /// Level ▸ Level textures: the current level's texture map, which wall, floor and door
    /// textures its tiles and doors can use (UW1 48 walls, 10 floors; UW2 64 textures, the first
    /// 16 also the floors; 6 doors). Choosing another texture for a slot is one undoable level
    /// edit, saved with the levels; the 3D view follows. The window follows the level shown.
    /// A click on a texture only marks it: Use it (Enter, or a second click on it) puts it in the
    /// slot, Cancel (Esc) drops it, so a stray click changes nothing. Undo beside the status takes
    /// the last change back.
    /// </summary>
    public sealed class LevelTexturesWindow : Window, IShowsTextures
    {
        readonly MainWindow main;
        readonly GameSession session;
        readonly TextBlock title = new TextBlock { FontSize = 16, FontWeight = FontWeight.SemiBold };
        readonly TextBlock status = new TextBlock { FontSize = 12, Opacity = 0.8, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 8, 0, 0) };
        readonly ComboBox kindBox = new ComboBox { Width = 260 };
        readonly ObservableCollection<TableRow> rows = new ObservableCollection<TableRow>();
        readonly ListBox slots = new ListBox();
        readonly WrapPanel picker = new WrapPanel();
        readonly TextBlock pickTitle = new TextBlock { FontWeight = FontWeight.SemiBold, TextWrapping = TextWrapping.Wrap };
        readonly List<TextureMap.Kind> kinds = new List<TextureMap.Kind>();
        readonly Dictionary<int, Bitmap> thumbs = new Dictionary<int, Bitmap>();
        readonly List<ToggleButton> pickButtons = new List<ToggleButton>();
        readonly TextBlock pendingText = new TextBlock { TextWrapping = TextWrapping.Wrap, VerticalAlignment = VerticalAlignment.Center, FontWeight = FontWeight.SemiBold };
        readonly Border pendingBar = new Border { Padding = new Thickness(8, 6), CornerRadius = new CornerRadius(4), BorderThickness = new Thickness(1), Margin = new Thickness(0, 0, 0, 8), IsVisible = false };
        readonly Button undoButton = new Button { Content = "Undo", Padding = new Thickness(10, 2), IsVisible = false, VerticalAlignment = VerticalAlignment.Center };
        TerrainTable terrain;
        LevelModel level;
        TextureMap.Kind kind;
        bool filling, choosing;
        /// <summary>The texture marked for the selected slot, not yet used (−1: none).</summary>
        int pending = -1;
        /// <summary>The undo step of this window's last change, and its level: Undo here shows while it is the next to undo.</summary>
        object ownStep;
        int ownStepLevel = -1;

        public LevelTexturesWindow(MainWindow main)
        {
            this.main = main;
            session = main.ViewModel.Session;
            Title = "Level textures";
            Width = 1080; Height = 760; MinWidth = 760; MinHeight = 480;
            WindowStartupLocation = WindowStartupLocation.CenterOwner;
            try { terrain = TerrainTable.Load(session); } catch (Exception) { terrain = null; }

            if (session.IsUw2) { kinds.Add(TextureMap.Kind.Wall); kinds.Add(TextureMap.Kind.Door); kindBox.ItemsSource = new[] { "Wall, floor and ceiling textures (T64.TR)", "Door textures (DOORS.GR)" }; }
            else { kinds.Add(TextureMap.Kind.Wall); kinds.Add(TextureMap.Kind.Floor); kinds.Add(TextureMap.Kind.Door); kindBox.ItemsSource = new[] { "Wall textures (W64.TR)", "Floor/ceiling textures (F32.TR)", "Door textures (DOORS.GR)" }; }
            kindBox.SelectedIndex = 0;
            kindBox.SelectionChanged += (o, e) => { kind = kinds[Math.Max(0, kindBox.SelectedIndex)]; pending = -1; slots.SelectedIndex = -1; Fill(); };

            slots.Bind(ListBox.BackgroundProperty, slots.GetResourceObservable("Panel"));
            slots.ItemsSource = rows;
            slots.ItemTemplate = new Avalonia.Controls.Templates.FuncDataTemplate<TableRow>((r, ns) =>
            {
                var sp = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8 };
                var img = new Image { Source = r?.Picture, Width = 40, Height = 40 };
                RenderOptions.SetBitmapInterpolationMode(img, BitmapInterpolationMode.None);
                sp.Children.Add(img);
                sp.Children.Add(new TextBlock { Text = r?.Label, VerticalAlignment = VerticalAlignment.Center, TextWrapping = TextWrapping.Wrap, MaxWidth = 300 });
                return sp;
            });
            slots.SelectionChanged += (o, e) => { if (!filling) { pending = -1; FillPicker(); } };

            var head = new StackPanel { Spacing = 4, Margin = new Thickness(0, 0, 0, 8) };
            head.Children.Add(title);
            head.Children.Add(new TextBlock
            {
                FontSize = 12, Opacity = 0.75, TextWrapping = TextWrapping.Wrap,
                Text = session.IsUw2
                    ? "A tile names its wall texture by a slot 0-63 and its floor by a slot 0-15 of this list; the ceiling is slot 15. Doors use the six door slots. Pick a slot, then click the texture it should show and Use it (or click it again). Each change is one Edit ▸ Undo step and is saved with the levels."
                    : "A tile names its wall texture by a slot 0-47 and its floor by a slot 0-9; the ceiling is floor slot 9. Doors use the six door slots. Pick a slot, then click the texture it should show and Use it (or click it again). Each change is one Edit ▸ Undo step and is saved with the levels.",
            });
            head.Children.Add(Row(new TextBlock { Text = "Show", VerticalAlignment = VerticalAlignment.Center }, kindBox));
            DockPanel.SetDock(head, Dock.Top);
            var foot = new DockPanel { Margin = new Thickness(0, 8, 0, 0) };
            status.Margin = new Thickness(0);
            status.VerticalAlignment = VerticalAlignment.Center;
            DockPanel.SetDock(undoButton, Dock.Right);
            ToolTip.SetTip(undoButton, "Takes the last texture change back (as Edit ▸ Undo).");
            undoButton.Click += (o, e) => UndoLast();
            foot.Children.Add(undoButton);
            foot.Children.Add(status);
            DockPanel.SetDock(foot, Dock.Bottom);

            var right = new DockPanel { Margin = new Thickness(16, 0, 0, 0) };
            pickTitle.Margin = new Thickness(0, 0, 0, 6);
            DockPanel.SetDock(pickTitle, Dock.Top);
            right.Children.Add(pickTitle);
            var use = new Button { Content = "Use it", FontWeight = FontWeight.SemiBold, Padding = new Thickness(12, 3), Margin = new Thickness(10, 0, 0, 0) };
            var cancel = new Button { Content = "Cancel", Padding = new Thickness(10, 3), Margin = new Thickness(6, 0, 0, 0) };
            ToolTip.SetTip(use, "Puts the marked texture in the slot (Enter, or a second click on the texture).");
            ToolTip.SetTip(cancel, "Leaves the slot as it is (Esc).");
            use.Click += (o, e) => UsePending();
            cancel.Click += (o, e) => CancelPending();
            var bar = new DockPanel();
            DockPanel.SetDock(cancel, Dock.Right); DockPanel.SetDock(use, Dock.Right);
            bar.Children.Add(cancel); bar.Children.Add(use); bar.Children.Add(pendingText);
            pendingBar.Child = bar;
            pendingBar.Bind(Border.BorderBrushProperty, pendingBar.GetResourceObservable("Accent"));
            pendingBar.Bind(Border.BackgroundProperty, pendingBar.GetResourceObservable("PanelAlt"));
            DockPanel.SetDock(pendingBar, Dock.Top);
            right.Children.Add(pendingBar);
            right.Children.Add(new ScrollViewer { Content = picker });

            var grid = new Grid { ColumnDefinitions = new ColumnDefinitions("380,*") };
            grid.Children.Add(slots);
            Grid.SetColumn(right, 1);
            grid.Children.Add(right);
            var dock = new DockPanel { Margin = new Thickness(16) };
            dock.Children.Add(head); dock.Children.Add(foot); dock.Children.Add(grid);
            Content = dock;
            ManualWindow.Attach(this, "Level textures");
            // Enter and Esc act on a marked texture before a focused button sees them
            AddHandler(KeyDownEvent, (o, e) =>
            {
                if (pending < 0) return;
                if (e.Key == Avalonia.Input.Key.Enter) { UsePending(); e.Handled = true; }
                else if (e.Key == Avalonia.Input.Key.Escape) { CancelPending(); e.Handled = true; }
            }, Avalonia.Interactivity.RoutingStrategies.Tunnel);

            main.ViewModel.LevelChanged += OnLevel;
            main.ViewModel.LevelEdited += OnLevelEdited;
            Closed += (o, e) => { main.ViewModel.LevelChanged -= OnLevel; main.ViewModel.LevelEdited -= OnLevelEdited; };
            OnLevel(main.ViewModel.Level);
        }

        static StackPanel Row(params Control[] c) { var p = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8 }; foreach (var x in c) p.Children.Add(x); return p; }

        /// <summary>The level shown.</summary>
        public LevelModel Level { get { return level; } }
        public ListBox Slots { get { return slots; } }
        public ComboBox KindBox { get { return kindBox; } }
        public string Status { get { return status.Text; } }
        /// <summary>The picker's buttons, by texture number.</summary>
        public IReadOnlyList<ToggleButton> PickButtons { get { return pickButtons; } }
        /// <summary>The texture marked for the selected slot and not used yet, or −1.</summary>
        public int PendingTexture { get { return pending; } }
        public string PendingText { get { return pendingBar.IsVisible ? pendingText.Text : null; } }
        public bool CanUndo { get { return undoButton.IsVisible; } }

        void OnLevel(LevelModel l)
        {
            if (main.ViewModel.Session != session) { Close(); return; }      // another game was opened
            level = l;
            pending = -1;
            undoButton.IsVisible = OwnStepOnTop();
            Fill();
        }

        /// <summary>The level was edited (or an edit undone): the slots again, the selection kept.</summary>
        void OnLevelEdited()
        {
            var l = main.ViewModel.Level;
            if (l == null || level == null || l.Level != level.Level) return;
            level = l;
            if (!choosing) pending = -1;
            undoButton.IsVisible = OwnStepOnTop();      // after an edit or undo from elsewhere, Undo here would take back something else
            Fill();
            main.ViewModel.RefreshSlots();          // the paint brush's slot pictures
        }

        /// <summary>A texture file was saved: the slots and the picker drawn again.</summary>
        public void RefreshTextures()
        {
            thumbs.Clear();
            pickerKind = null;      // the picker's buttons are made again
            Fill();
        }

        Bitmap Thumb(TextureMap.Kind k, int texture, int edge = 40)
        {
            int key = (int)k << 16 | texture;
            Bitmap b;
            if (thumbs.TryGetValue(key, out b)) return b;
            var g = session.Graphics;
            RgbaImage img = null;
            if (k == TextureMap.Kind.Door) img = texture >= 0 && texture < g.Doors.Count && g.Doors[texture] != null ? Fit(g.Doors[texture], edge) : null;
            else
            {
                var t = k == TextureMap.Kind.Floor ? g.FloorTexture(texture) : g.WallTexture(texture);
                if (t != null) img = g.TextureRgba(t, k == TextureMap.Kind.Floor ? g.FloorEdge : g.WallEdge, edge);
            }
            b = img == null ? null : MainViewModel.ToBitmap(img);
            thumbs[key] = b;
            return b;
        }

        /// <summary>A door picture scaled into a square (nearest pixel, the aspect kept).</summary>
        static RgbaImage Fit(RgbaImage p, int edge)
        {
            var o = new RgbaImage(edge, edge);
            double s = Math.Max(p.Width, p.Height) / (double)edge;
            int w = (int)(p.Width / s), h = (int)(p.Height / s), x0 = (edge - w) / 2, y0 = (edge - h) / 2;
            for (int y = 0; y < h; y++)
                for (int x = 0; x < w; x++)
                {
                    int sx = Math.Min(p.Width - 1, (int)(x * s)), sy = Math.Min(p.Height - 1, (int)(y * s));
                    Array.Copy(p.Pixels, (sy * p.Width + sx) * 4, o.Pixels, ((y0 + y) * edge + x0 + x) * 4, 4);
                }
            return o;
        }

        string TerrainOf(TextureMap.Kind k, int texture)
        {
            if (terrain == null || !terrain.Exists || k == TextureMap.Kind.Door) return null;
            bool floor = k == TextureMap.Kind.Floor;
            if (texture >= terrain.Textures(floor)) return null;
            int v = terrain.Get(terrain.EntryOf(texture, floor));
            return v == 0 ? null : TerrainTable.Describe(session.Kind, floor, v);
        }

        string SlotLabel(int slot, int tex)
        {
            string name = kind == TextureMap.Kind.Door ? "Door " + slot : kind == TextureMap.Kind.Floor ? "Floor " + slot : session.IsUw2 && slot < 16 ? "Wall/floor " + slot : "Wall " + slot;
            var parts = new List<string> { name + ": texture " + tex };
            int ceiling = level.CeilingFloorIndex;
            if (kind == TextureMap.Kind.Floor && slot == ceiling || session.IsUw2 && kind == TextureMap.Kind.Wall && slot == ceiling) parts.Add("the ceiling");
            if (kind != TextureMap.Kind.Door)
            {
                int n = TextureMap.Uses(level, kind, slot);
                parts.Add(n == 0 ? "not used" : n + (n == 1 ? " tile" : " tiles"));
            }
            string terr = TerrainOf(kind, tex);
            if (terr != null) parts.Add(terr);
            return string.Join(" · ", parts);
        }

        void Fill()
        {
            int keep = slots.SelectedIndex;
            filling = true;
            rows.Clear();
            if (level != null)
            {
                title.Text = string.Format("Textures of level {0}", level.Level) + (level.Present ? "" : " (empty slot)");
                int n = TextureMap.Slots(session, kind);
                for (int i = 0; i < n; i++)
                {
                    int tex = TextureMap.Get(session, level, kind, i);
                    rows.Add(new TableRow { Index = i, Picture = Thumb(kind, tex), Label = SlotLabel(i, tex) });
                }
                slots.SelectedIndex = Math.Min(Math.Max(0, keep), rows.Count - 1);
            }
            else title.Text = "No level";
            filling = false;
            FillPicker();
        }

        TextureMap.Kind? pickerKind;
        readonly List<TextBlock> pickLabels = new List<TextBlock>();

        /// <summary>The kind's textures as buttons, made once for each kind.</summary>
        void BuildPicker()
        {
            picker.Children.Clear();
            pickButtons.Clear();
            pickLabels.Clear();
            pickerKind = kind;
            int n = TextureMap.Choices(session, kind);
            for (int t = 0; t < n; t++)
            {
                int tex = t;
                var img = new Image { Source = Thumb(kind, tex, 48), Width = 48, Height = 48 };
                RenderOptions.SetBitmapInterpolationMode(img, BitmapInterpolationMode.None);
                var label = new TextBlock { Text = tex.ToString(), FontSize = 11, HorizontalAlignment = HorizontalAlignment.Center };
                var sp = new StackPanel { Spacing = 2 };
                sp.Children.Add(img);
                sp.Children.Add(label);
                var b = new ToggleButton { Content = sp, Padding = new Thickness(3), Margin = new Thickness(2) };
                b.Click += (o, e) => Pick(tex);
                pickButtons.Add(b);
                pickLabels.Add(label);
                picker.Children.Add(b);
            }
        }

        void FillPicker()
        {
            int slot = slots.SelectedItem is TableRow r ? r.Index : -1;
            if (level == null || slot < 0) { pickTitle.Text = "Choose a slot on the left."; picker.IsVisible = false; pendingBar.IsVisible = false; return; }
            if (pickerKind != kind) BuildPicker();
            picker.IsVisible = true;
            int cur = TextureMap.Get(session, level, kind, slot);
            if (pending == cur) pending = -1;
            string kname = kind == TextureMap.Kind.Door ? "Door" : kind == TextureMap.Kind.Floor ? "Floor" : "Wall";
            pickTitle.Text = string.Format("{0} slot {1} shows texture {2}. Click another texture to mark it, then Use it ({3} to choose from; • marks those already in the level's list).", kname, slot, cur, pickButtons.Count);
            pendingBar.IsVisible = pending >= 0;
            if (pending >= 0)
            {
                string uses = "";
                if (kind != TextureMap.Kind.Door)
                {
                    int n = TextureMap.Uses(level, kind, slot);
                    uses = n == 0 ? " No tile uses the slot." : string.Format(" {0} {1} change.", n, n == 1 ? "tile" : "tiles");
                }
                pendingText.Text = string.Format("{0} slot {1}: texture {2} → {3}.{4}", kname, slot, cur, pending, uses);
            }
            var used = new HashSet<int>(Enumerable.Range(0, TextureMap.Slots(session, kind)).Select(i => TextureMap.Get(session, level, kind, i)));
            for (int t = 0; t < pickButtons.Count; t++)
            {
                pickButtons[t].IsChecked = t == cur;
                if (t == pending)
                {
                    pickButtons[t].Bind(TemplatedControl.BorderBrushProperty, pickButtons[t].GetResourceObservable("Accent"));
                    pickButtons[t].BorderThickness = new Thickness(3);
                }
                else { pickButtons[t].ClearValue(TemplatedControl.BorderBrushProperty); pickButtons[t].ClearValue(TemplatedControl.BorderThicknessProperty); }
                pickLabels[t].Text = t + (used.Contains(t) ? " •" : "");
                string terr = TerrainOf(kind, t);
                ToolTip.SetTip(pickButtons[t], "Texture " + t + (terr != null ? ": " + terr : "") + (used.Contains(t) ? " (already in this level's list)" : ""));
            }
        }

        /// <summary>
        /// A click on a texture: marks it for the selected slot. A click on the texture already
        /// marked uses it (so a double-click does too); a click on the slot's own texture drops the mark.
        /// </summary>
        public void Pick(int texture)
        {
            int slot = slots.SelectedItem is TableRow r ? r.Index : -1;
            if (level == null || slot < 0) return;
            if (texture == pending) { UsePending(); return; }
            pending = texture == TextureMap.Get(session, level, kind, slot) ? -1 : texture;
            FillPicker();
        }

        /// <summary>Use it: puts the marked texture in the slot. False when none is marked or it failed.</summary>
        public bool UsePending()
        {
            if (pending < 0) return false;
            int t = pending;
            pending = -1;
            return Choose(t);
        }

        /// <summary>Cancel: drops the marked texture.</summary>
        public void CancelPending()
        {
            if (pending < 0) return;
            pending = -1;
            FillPicker();
        }

        /// <summary>Undo beside the status: takes the last texture change back.</summary>
        public void UndoLast()
        {
            if (!OwnStepOnTop()) { undoButton.IsVisible = false; return; }
            undoButton.IsVisible = false;
            ownStep = null;
            choosing = true;
            try { main.Undo(); }
            finally { choosing = false; }
            status.Text = "The last texture change was undone.";
        }

        /// <summary>This window's last change is the next step Edit ▸ Undo takes back.</summary>
        bool OwnStepOnTop()
        {
            if (ownStep == null || level == null || ownStepLevel != level.Level) return false;
            var e = main.ViewModel.Edits?.Existing(ownStepLevel);
            return e != null && ReferenceEquals(e.UndoTop, ownStep);
        }

        /// <summary>Points the selected slot at a texture at once (one undoable level edit).</summary>
        public bool Choose(int texture)
        {
            int slot = slots.SelectedItem is TableRow r ? r.Index : -1;
            var vm = main.ViewModel;
            if (level == null || slot < 0) return false;
            if (!level.Present || vm.Edits == null) { status.Text = "The level slot is empty: there is nothing to texture."; FillPicker(); return false; }
            pending = -1;
            choosing = true;
            try
            {
                try { TextureMap.Set(vm.Edits.For(level.Level), kind, slot, texture); }
                catch (Exception e) { status.Text = e.Message; FillPicker(); return false; }
                vm.FlushRefresh();
            }
            finally { choosing = false; }
            ownStep = vm.Edits.Existing(level.Level)?.UndoTop;
            ownStepLevel = level.Level;
            undoButton.IsVisible = OwnStepOnTop();
            status.Text = string.Format("Slot {0} now shows texture {1}. Edit ▸ Undo reverts it; Save levels keeps it.", slot, texture);
            vm.EditStatus = status.Text;
            if (!ReferenceEquals(level, vm.Level)) { level = vm.Edits.Model(level.Level); Fill(); }   // the shown level refreshes itself (LevelEdited)
            return true;
        }
    }
}
