using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using Avalonia;
using Avalonia.Media.Imaging;
using Avalonia.Platform;
using UwEditor.Core;

namespace UwEditor.App
{
    public abstract class Observable : INotifyPropertyChanged
    {
        public event PropertyChangedEventHandler PropertyChanged;

        protected bool Set<T>(ref T field, T value, [CallerMemberName] string name = null)
        {
            if (EqualityComparer<T>.Default.Equals(field, value)) return false;
            field = value;
            Raise(name);
            return true;
        }

        protected void Raise([CallerMemberName] string name = null)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
        }
    }

    /// <summary>A line of the object list: a tile, an object on it, or an object inside another.</summary>
    public sealed class TreeNode : Observable
    {
        public string Header { get; set; }
        public ObservableCollection<TreeNode> Children { get; } = new ObservableCollection<TreeNode>();
        public int ObjectIndex = -1, TileX = -1, TileY = -1;
        public TreeNode Parent;
        bool expanded;
        public bool IsExpanded { get { return expanded; } set { Set(ref expanded, value); } }
    }

    /// <summary>The window's state. The window's code connects it to the 3D view.</summary>
    public sealed class MainViewModel : Observable
    {
        GameSession session;
        List<LevelSlot> slots = new List<LevelSlot>();
        List<LevelSlot> levelsInWorld = new List<LevelSlot>();
        List<string> worlds = new List<string>();
        int worldIndex = -1;
        LevelSlot selectedSlot;
        LevelModel level;
        string filter = "";
        TreeNode selectedNode;
        int selectedObject = -1, selectedTileX = -1, selectedTileY = -1;
        bool suppressSlot;

        public SceneOptions Options { get; } = new SceneOptions();
        public ObservableCollection<TreeNode> Tree { get; } = new ObservableCollection<TreeNode>();
        public ObservableCollection<PropertyRow> ObjectRows { get; } = new ObservableCollection<PropertyRow>();
        public ObservableCollection<PropertyRow> TileRows { get; } = new ObservableCollection<PropertyRow>();
        public ObservableCollection<string> Recent { get; } = new ObservableCollection<string>();

        /// <summary>The level to show changed (null when none).</summary>
        public event Action<LevelModel> LevelChanged;
        /// <summary>The selection changed (from the list or the view).</summary>
        public event Action SelectionChanged;
        /// <summary>What the view shows changed (ceiling, objects...).</summary>
        public event Action<bool> OptionsChanged;

        public GameSession Session { get { return session; } }
        public LevelModel Level { get { return level; } }
        public bool HasGame { get { return session != null; } }
        public bool NoGame { get { return session == null; } }
        public bool IsUw2 { get { return session != null && session.IsUw2; } }
        public string GameTitle { get { return session == null ? "No game open" : session.Title; } }
        public string GameFolder { get { return session == null ? "" : session.SaveFolder ?? session.DataFolder; } }

        public List<string> Worlds { get { return worlds; } }
        public List<LevelSlot> LevelsInWorld { get { return levelsInWorld; } }

        public int WorldIndex
        {
            get { return worldIndex; }
            set
            {
                if (!Set(ref worldIndex, value) || session == null || value < 0) return;
                levelsInWorld = slots.Where(s => s.World == value).ToList();
                Raise(nameof(LevelsInWorld));
                if (!suppressSlot) SelectedSlot = levelsInWorld.FirstOrDefault(s => s.Present) ?? levelsInWorld.FirstOrDefault();
            }
        }

        public LevelSlot SelectedSlot
        {
            get { return selectedSlot; }
            set
            {
                if (value == null || !Set(ref selectedSlot, value)) return;
                LoadLevel(value.Level);
            }
        }

        public string LevelSummary
        {
            get
            {
                if (level == null) return "";
                if (!level.Present) return "This slot holds no level.";
                string world = IsUw2 ? session.Profile.LevelName(level.Level) + "\n" : "";
                string note = session.Profile.SlotNote(level.Level);
                return string.Format("{0}{1} open tiles, {2} objects{3}{4}", world, level.OpenTileCount, level.UsedObjectCount,
                    level.CheckOk ? "" : "\nObject lists fail the game's check", note == null ? "" : "\nSlot " + note);
            }
        }

        public string Filter
        {
            get { return filter; }
            set { if (Set(ref filter, value ?? "")) BuildTree(); }
        }

        public TreeNode SelectedNode
        {
            get { return selectedNode; }
            set
            {
                if (!Set(ref selectedNode, value) || value == null) return;
                if (value.ObjectIndex >= 0) Select(value.ObjectIndex, value.TileX, value.TileY, false);
                else if (value.TileX >= 0) Select(-1, value.TileX, value.TileY, false);
            }
        }

        public int SelectedObject { get { return selectedObject; } }
        public int SelectedTileX { get { return selectedTileX; } }
        public int SelectedTileY { get { return selectedTileY; } }

        string objectTitle = "Nothing selected", objectSubtitle = "Click an object in the view or the list.", tileTitle = "", hoverText = "", startText = "", viewText = "";
        Bitmap wallThumb, floorThumb;
        public string ObjectTitle { get { return objectTitle; } set { Set(ref objectTitle, value); } }
        public string ObjectSubtitle { get { return objectSubtitle; } set { Set(ref objectSubtitle, value); } }
        public string TileTitle { get { return tileTitle; } set { Set(ref tileTitle, value); } }
        public bool HasObject { get { return selectedObject >= 0; } }
        public bool HasTile { get { return selectedTileX >= 0; } }
        public Bitmap WallThumb { get { return wallThumb; } set { Set(ref wallThumb, value); } }
        public Bitmap FloorThumb { get { return floorThumb; } set { Set(ref floorThumb, value); } }
        public Bitmap ObjectThumb { get { return objectThumb; } set { Set(ref objectThumb, value); } }
        Bitmap objectThumb;
        public string HoverText { get { return hoverText; } set { Set(ref hoverText, value); } }
        public string StartText { get { return startText; } set { Set(ref startText, value); } }
        public string ViewText { get { return viewText; } set { Set(ref viewText, value); } }

        // ---- view options (View menu and the Show box)

        public bool ShowCeiling { get { return Options.Ceiling; } set { if (Options.Ceiling == value) return; Options.Ceiling = value; Raise(); OptionsChanged?.Invoke(true); } }
        public bool ShowObjects { get { return Options.Objects; } set { if (Options.Objects == value) return; Options.Objects = value; Raise(); OptionsChanged?.Invoke(false); } }
        public bool ShowInvisible { get { return Options.Invisible; } set { if (Options.Invisible == value) return; Options.Invisible = value; Raise(); OptionsChanged?.Invoke(false); } }
        public bool ShowTraps { get { return Options.TrapsAndTriggers; } set { if (Options.TrapsAndTriggers == value) return; Options.TrapsAndTriggers = value; Raise(); OptionsChanged?.Invoke(false); } }
        bool darkness;
        public bool Darkness { get { return darkness; } set { if (Set(ref darkness, value)) OptionsChanged?.Invoke(false); } }

        // ---- opening

        public void Open(GameSession s)
        {
            session = s;
            slots = s.LevelSlots();
            worlds = new List<string>();
            for (int w = 0; w < s.Profile.WorldCount; w++)
            {
                int used = slots.Count(x => x.World == w && x.Present), all = slots.Count(x => x.World == w);
                worlds.Add(s.IsUw2 ? string.Format("{0} ({1} of {2})", s.Profile.WorldName(w), used, all) : "Levels");
            }
            foreach (var n in new[] { nameof(Session), nameof(HasGame), nameof(NoGame), nameof(IsUw2), nameof(GameTitle), nameof(GameFolder), nameof(Worlds) }) Raise(n);
            StartText = s.Start.Description;
            var startSlot = slots.FirstOrDefault(x => x.Level == s.Start.Level) ?? slots.FirstOrDefault(x => x.Present);
            suppressSlot = true;
            worldIndex = -1;
            WorldIndex = startSlot != null ? startSlot.World : 0;
            suppressSlot = false;
            selectedSlot = null;
            SelectedSlot = startSlot;
        }

        public void LoadLevel(int l)
        {
            if (session == null) return;
            level = session.LoadLevel(l);
            var slot = slots.FirstOrDefault(x => x.Level == l);
            if (slot != null && slot != selectedSlot)
            {
                if (slot.World != worldIndex) { suppressSlot = true; WorldIndex = slot.World; suppressSlot = false; }
                selectedSlot = slot;
                Raise(nameof(SelectedSlot));
            }
            Raise(nameof(Level));
            Raise(nameof(LevelSummary));
            ClearSelection();
            BuildTree();
            LevelChanged?.Invoke(level);
        }

        public void StepLevel(int delta)
        {
            if (session == null || level == null) return;
            // the next slot holding a level
            for (int l = level.Level + delta; l >= 1 && l <= session.Profile.LevelSlots; l += delta)
                if (slots[l - 1].Present) { LoadLevel(l); return; }
        }

        // ---- the object list

        void BuildTree()
        {
            Tree.Clear();
            if (level == null) return;
            string f = filter.Trim().ToLowerInvariant();
            for (int y = 63; y >= 0; y--)
                for (int x = 0; x < 64; x++)
                {
                    var objs = level.TileObjects(x, y);
                    if (objs.Count == 0) continue;
                    var tileNode = new TreeNode { Header = string.Format("({0}, {1})  {2}", x, y, Describe(level.Tiles[x, y], objs.Count)), TileX = x, TileY = y };
                    foreach (var o in objs) AddObject(tileNode, o, f);
                    if (tileNode.Children.Count > 0) Tree.Add(tileNode);
                    if (f.Length > 0) tileNode.IsExpanded = true;
                }
        }

        static string Describe(TileInfo t, int n) { return n == 1 ? "1 object" : n + " objects"; }

        bool AddObject(TreeNode parent, ObjectInfo o, string f)
        {
            var node = new TreeNode { Header = Label(o), ObjectIndex = o.Index, TileX = o.TileX, TileY = o.TileY, Parent = parent };
            bool childMatches = false;
            foreach (int c in o.Children) childMatches |= AddObject(node, level.Objects[c], f);
            bool matches = f.Length == 0 || Matches(o, f) || childMatches;
            if (matches) parent.Children.Add(node);
            if (childMatches && f.Length > 0) node.IsExpanded = true;
            return matches;
        }

        static string Label(ObjectInfo o)
        {
            string flags = (o.Invisible ? " · invisible" : "");
            return string.Format("{0}  #{1}{2}", o.Name, o.Index, flags);
        }

        static bool Matches(ObjectInfo o, string f)
        {
            if (o.Name.ToLowerInvariant().Contains(f)) return true;
            string hex = f.StartsWith("0x") ? f.Substring(2) : f;
            int v;
            if (int.TryParse(hex, System.Globalization.NumberStyles.HexNumber, null, out v) && f.StartsWith("0x") && v == o.Item) return true;
            if (int.TryParse(f.TrimStart('#'), out v) && v == o.Index) return true;
            return false;
        }

        TreeNode FindNode(IEnumerable<TreeNode> nodes, int obj)
        {
            foreach (var n in nodes)
            {
                if (n.ObjectIndex == obj) return n;
                var c = FindNode(n.Children, obj);
                if (c != null) return c;
            }
            return null;
        }

        TreeNode FindTileNode(int x, int y)
        {
            return Tree.FirstOrDefault(n => n.TileX == x && n.TileY == y && n.ObjectIndex < 0);
        }

        // ---- selection

        public void ClearSelection()
        {
            selectedObject = selectedTileX = selectedTileY = -1;
            selectedNode = null;
            Raise(nameof(SelectedNode));
            ObjectRows.Clear(); TileRows.Clear();
            ObjectTitle = "Nothing selected";
            ObjectSubtitle = "Click an object in the view or the list.";
            ObjectThumb = null;
            TileTitle = ""; WallThumb = FloorThumb = null;
            Raise(nameof(HasObject)); Raise(nameof(HasTile));
            SelectionChanged?.Invoke();
        }

        /// <summary>Selects an object (or none, -1) and a tile; fromView also selects it in the list.</summary>
        public void Select(int obj, int tx, int ty, bool fromView)
        {
            if (level == null) return;
            selectedObject = obj;
            if (obj >= 0 && level.Objects[obj].TileX >= 0) { tx = level.Objects[obj].TileX; ty = level.Objects[obj].TileY; }
            selectedTileX = tx; selectedTileY = ty;
            ObjectRows.Clear();
            if (obj >= 0)
            {
                var o = level.Objects[obj];
                ObjectTitle = o.Name;
                ObjectSubtitle = string.Format("#{0} · item 0x{1:X3} · {2}", o.Index, o.Item, o.IsMobile ? "mobile" : "static");
                foreach (var r in PropertyRows.ForObject(level, o)) ObjectRows.Add(r);
                var pics = session.Graphics.ObjectPictures;
                ObjectThumb = o.Item < pics.Count && pics[o.Item] != null ? ToBitmap(pics[o.Item]) : null;
            }
            else
            {
                ObjectTitle = "No object";
                ObjectSubtitle = tx >= 0 ? "A tile is selected; its objects are in the list." : "Click an object in the view or the list.";
                ObjectThumb = null;
            }
            TileRows.Clear();
            if (tx >= 0)
            {
                var t = level.Tiles[tx, ty];
                TileTitle = string.Format("({0}, {1}) · {2} · height {3}", tx, ty, TileInfo.TypeName(t.Type), t.Height);
                foreach (var r in PropertyRows.ForTile(level, t)) TileRows.Add(r);
                var g = session.Graphics;
                var wt = g.WallTexture(level.WallTextureNumber(t.WallIndex));
                var ft = g.FloorTexture(level.FloorTextureNumber(t.FloorIndex));
                WallThumb = wt == null ? null : ToBitmap(g.TextureRgba(wt, g.WallEdge, 64));
                FloorThumb = ft == null ? null : ToBitmap(g.TextureRgba(ft, g.FloorEdge, 64));
            }
            else { TileTitle = ""; WallThumb = FloorThumb = null; }
            Raise(nameof(HasObject)); Raise(nameof(HasTile));
            if (fromView)
            {
                var node = obj >= 0 ? FindNode(Tree, obj) : FindTileNode(tx, ty);
                for (var p = node?.Parent; p != null; p = p.Parent) p.IsExpanded = true;
                if (node != null && node.Parent == null && obj >= 0) node.IsExpanded = true;
                selectedNode = node;
                Raise(nameof(SelectedNode));
            }
            SelectionChanged?.Invoke();
        }

        public static Bitmap ToBitmap(RgbaImage img)
        {
            var bmp = new WriteableBitmap(new PixelSize(img.Width, img.Height), new Vector(96, 96), PixelFormats.Rgba8888, AlphaFormat.Unpremul);
            using (var fb = bmp.Lock())
            {
                for (int y = 0; y < img.Height; y++)
                    Marshal.Copy(img.Pixels, y * img.Width * 4, fb.Address + y * fb.RowBytes, img.Width * 4);
            }
            return bmp;
        }
    }
}
