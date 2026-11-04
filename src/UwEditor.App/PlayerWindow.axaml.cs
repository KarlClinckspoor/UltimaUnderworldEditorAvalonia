using System;
using System.IO;
using System.Linq;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Media.Imaging;
using UwEditor.Core;
using UwEditor.Core.Graphics;
using UwEditor.Core.Player;

namespace UwEditor.App
{
    /// <summary>
    /// Tools ▸ Player: a saved game's character — name, class, sex, hand, portrait, attributes,
    /// condition, experience, skills — and inventory, with the checks the game makes (UW2 calls
    /// attributes over 64 cheating) and the values play cannot reach.
    /// </summary>
    public partial class PlayerWindow : Window
    {
        public PlayerViewModel Model { get; private set; }

        public PlayerWindow() { InitializeComponent(); }

        public PlayerWindow(PlayerSheet sheet, GameSession session, Func<int, Bitmap> icon)
        {
            InitializeComponent();
            ManualWindow.Attach(this, "Player and inventory");
            PictureFile heads = null;
            Palettes pals = null;
            try
            {
                string h = UltimateEditor.Games.GameProfile.Find(session.DataFolder, "HEADS.GR");
                if (h != null) { heads = PictureFile.Load(h, session.Kind); pals = Palettes.Load(session.DataFolder); }
            }
            catch (Exception) { heads = null; }
            Func<int, Bitmap> portrait = n => heads == null || n >= heads.Count ? null : Pictures.ToBitmap(heads.Get(n, pals, 0));
            Model = new PlayerViewModel(sheet, session, icon, portrait);
            iconOf = icon;
            DataContext = Model;
            WireDragging();
            Title ="Player — " + sheet.Name + " — " + session.Title;
            TitleText.Text = "Player: " + sheet.Name;
            FileText.Text = sheet.Path ?? "";
            Model.Status = string.Format("{0}, level {1} {2}, carrying {3} objects. Changes are kept until you save.", sheet.Name, sheet.Level, PlayerSheet.ClassNames[sheet.Class], sheet.Inventory.All().Count());
            AddHandler(KeyDownEvent, (s, e) =>
            {
                if ((e.KeyModifiers.HasFlag(KeyModifiers.Control) || e.KeyModifiers.HasFlag(KeyModifiers.Meta)) && e.Key == Key.S) { Save(); e.Handled = true; }
            }, RoutingStrategies.Tunnel);
        }

        /// <summary>Shows a tab: 0 the character, 1 the inventory.</summary>
        public void ShowTab(int i) { Tabs.SelectedIndex = i; }

        void OnRecompute(object sender, RoutedEventArgs e) { Model.Recompute(); }
        void OnDelete(object sender, RoutedEventArgs e) { Model.Delete(); }
        void OnAdd(object sender, RoutedEventArgs e) { Model.Add(); }
        void OnSave(object sender, RoutedEventArgs e) { Save(); }

        Func<int, Bitmap> iconOf;

        async void OnAddDetailed(object sender, RoutedEventArgs e)
        {
            Func<string> where = () => "It goes to " + Model.AddTargetWhere + " (chosen under Add to in the inventory tab).";
            var w = await AddObjectWindow.Ask(this, Model.Session, iconOf, true, where(), keepAdding: spec => { Model.AddSpec(spec); return Model.Status + " " + where(); });
            if (w.Result != null) Model.AddSpec(w.Result);
        }

        // ---- dragging in the inventory list: press on an object, move, release on another line

        ItemNode dragged, dropOnto;
        Avalonia.Point pressedAt;
        bool dragging;

        void WireDragging()
        {
            ItemTree.AddHandler(PointerPressedEvent, (s, e) =>
            {
                dragged = e.GetCurrentPoint(ItemTree).Properties.IsLeftButtonPressed ? NodeAt(e.Source) : null;
                pressedAt = e.GetPosition(ItemTree);
                dragging = false;
            }, RoutingStrategies.Tunnel | RoutingStrategies.Bubble, true);
            ItemTree.AddHandler(PointerMovedEvent, (s, e) =>
            {
                if (dragged == null || dragged.Item == null || !e.GetCurrentPoint(ItemTree).Properties.IsLeftButtonPressed) return;
                var p = e.GetPosition(ItemTree);
                if (!dragging && Math.Abs(p.X - pressedAt.X) + Math.Abs(p.Y - pressedAt.Y) < 6) return;
                dragging = true;
                var onto = NodeAt(ItemTree.InputHitTest(p));
                SetDropOnto(Model.CanDrop(dragged, onto) ? onto : null);
                ItemTree.Cursor = new Cursor(dropOnto != null ? StandardCursorType.DragMove : StandardCursorType.No);
            }, RoutingStrategies.Tunnel | RoutingStrategies.Bubble, true);
            ItemTree.AddHandler(PointerReleasedEvent, (s, e) =>
            {
                var from = dragging ? dragged : null;
                var onto = dropOnto;
                dragged = null; dragging = false;
                SetDropOnto(null);
                ItemTree.Cursor = Cursor.Default;
                if (from != null && onto != null) Model.Drop(from, onto);
            }, RoutingStrategies.Tunnel | RoutingStrategies.Bubble, true);
            ItemTree.AddHandler(KeyDownEvent, (s, e) =>
            {
                if (e.Key == Key.Delete && Model.HasItem) { Model.Delete(); e.Handled = true; }
            });
        }

        void SetDropOnto(ItemNode n)
        {
            if (dropOnto == n) return;
            if (dropOnto != null) dropOnto.IsDropTarget = false;
            dropOnto = n;
            if (n != null) n.IsDropTarget = true;
        }

        static ItemNode NodeAt(object source)
        {
            for (var v = source as Avalonia.Visual; v != null; v = Avalonia.VisualTree.VisualExtensions.GetVisualParent(v))
                if (v is TreeViewItem tvi) return tvi.DataContext as ItemNode;
            return null;
        }

        public bool Save()
        {
            try
            {
                Model.Sheet.Save();
                Model.BuildTree();
                Model.Check();
                Model.Status = "Saved " + Model.Sheet.Path + " (the old file is kept as PLAYER.DAT.BKP).";
                return true;
            }
            catch (Exception ex) { Model.Status = "Not saved: " + ex.Message; return false; }
        }
    }
}
