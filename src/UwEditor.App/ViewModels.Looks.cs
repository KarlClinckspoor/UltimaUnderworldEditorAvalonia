using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using UwEditor.Core;

namespace UwEditor.App
{
    /// <summary>
    /// The object list and the properties panel after an edit: when only fields of objects changed
    /// (no object added, deleted or moved to another list) the lines of the changed objects are
    /// updated where they are, and the panel's rows are replaced only where their values changed,
    /// so the list keeps what is open and the panel does not rebuild for every value typed.
    /// </summary>
    public sealed partial class MainViewModel
    {
        /// <summary>The list's line of each object shown in it.</summary>
        readonly Dictionary<int, TreeNode> nodeOf = new Dictionary<int, TreeNode>();
        ObjectLook objectLook = ObjectLook.Plain;
        bool freshRows;

        /// <summary>The colour and underline of the selected object's name in the properties panel.</summary>
        public ObjectLook ObjectLook { get { return objectLook; } private set { Set(ref objectLook, value ?? ObjectLook.Plain); } }

        /// <summary>How many times the list was built from nothing (tests).</summary>
        public int TreeBuilds { get; private set; }

        /// <summary>The list's line of an object, or null when it is not shown.</summary>
        public TreeNode NodeOf(int obj) { TreeNode n; return nodeOf.TryGetValue(obj, out n) ? n : null; }

        /// <summary>Puts rows into a panel's collection: the same rows as before (the same object or tile, the same fields) are replaced only where they differ.</summary>
        void SetRows(ObservableCollection<PropertyRow> to, List<PropertyRow> rows)
        {
            bool same = !freshRows && to.Count == rows.Count;
            for (int i = 0; same && i < rows.Count; i++)
            {
                var a = to[i]; var b = rows[i];
                same = a.Name == b.Name && a.Field == b.Field && a.Target == b.Target && a.TargetIndex == b.TargetIndex && a.IsHeader == b.IsHeader;
            }
            if (!same)
            {
                to.Clear();
                foreach (var r in rows) to.Add(r);
                return;
            }
            for (int i = 0; i < rows.Count; i++)
            {
                var a = to[i]; var b = rows[i];
                if (a.Value != b.Value || a.Hex != b.Hex || a.Tip != b.Tip || a.Certainty != b.Certainty || !SameChoices(a.Choices, b.Choices)) to[i] = b;
            }
        }

        static bool SameChoices(List<UwEditor.Core.Editing.Choice> a, List<UwEditor.Core.Editing.Choice> b)
        {
            if (a == null || b == null) return a == b;
            if (a.Count != b.Count) return false;
            for (int i = 0; i < a.Count; i++) if (a[i].Value != b[i].Value || a[i].Label != b[i].Label) return false;
            return true;
        }

        /// <summary>Selects again with every row of the panel made anew (a refused value shows the old one again).</summary>
        void ReselectFresh()
        {
            freshRows = true;
            try { Select(selectedObject, selectedTileX, selectedTileY, false); }
            finally { freshRows = false; }
        }

        /// <summary>
        /// After an edit of the level shown: updates the list's lines of the objects whose records
        /// changed, if the list would come out the same otherwise (every object in the same list
        /// at the same place, the filter matching the same ones). False when it must be built again.
        /// </summary>
        bool UpdateTreeInPlace(LevelModel before)
        {
            if (before == null || level == null || before.Level != level.Level || before.Objects.Length != level.Objects.Length) return false;
            var changed = new List<int>();
            for (int i = 0; i < level.Objects.Length; i++)
            {
                var a = before.Objects[i]; var b = level.Objects[i];
                if (a.Free != b.Free || a.TileX != b.TileX || a.TileY != b.TileY || a.Parent != b.Parent || !a.Children.SequenceEqual(b.Children)) return false;
                if (b.Free) continue;
                if (a.Name != b.Name || !a.Record.AsSpan().SequenceEqual(b.Record)) changed.Add(i);
            }
            var q = CurrentQuery();
            if (!q.IsEmpty)
                foreach (int i in changed)
                    if (q.Matches(session, before.Objects[i]) != q.Matches(session, level.Objects[i])) return false;
            foreach (int i in changed)
            {
                var node = NodeOf(i);
                if (node == null) continue;
                var o = level.Objects[i];
                node.Header = Label(o, node.Parent == null);
                node.Icon = IconOf(o.Item);
                node.Look = ObjectLook.Of(o);
            }
            return true;
        }
    }
}
