using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UwEditor.Core;

namespace UwEditor.App
{
    /// <summary>The main window's side of View ▸ Collision edges and Collision faces, and the warning on save about new overlaps.</summary>
    public partial class MainWindow
    {
        byte[] comobj;
        string comobjPath;
        DateTime comobjTime;

        /// <summary>DATA's COMOBJ.DAT, read again when the file changed (Tools ▸ Object tables saves it).</summary>
        byte[] ComObj()
        {
            var s = vm.Session;
            if (s == null) return null;
            string p = UltimateEditor.Games.GameProfile.Find(s.DataFolder, "COMOBJ.DAT");
            if (p == null) return null;
            DateTime t;
            try { t = File.GetLastWriteTimeUtc(p); } catch (Exception) { return comobj; }
            if (comobj == null || p != comobjPath || t != comobjTime)
            {
                comobj = Hitboxes.ReadComObj(s);
                comobjPath = p; comobjTime = t;
            }
            return comobj;
        }

        /// <summary>The level's collision boxes as lines (edges) and see-through triangles (faces), red where two overlap.</summary>
        void AddHitboxes(List<float> lines, List<float> tris)
        {
            if (!vm.ShowCollisionEdges && !vm.ShowCollisionFaces) return;
            var level = vm.Level;
            var com = ComObj();
            if (level == null || com == null || tris == null) return;
            var boxes = Hitboxes.Of(level, com);
            Hitboxes.Overlaps(boxes);
            if (vm.ShowCollisionEdges) Hitboxes.AddEdges(lines, boxes);
            if (vm.ShowCollisionFaces) Hitboxes.AddFaces(tris, boxes);
        }

        /// <summary>Before the levels are written: the overlaps with a creature each changed level would add to what is on disk.</summary>
        List<string> NewOverlapWarnings()
        {
            var list = new List<string>();
            var s = vm.Session;
            var com = ComObj();
            if (s == null || vm.Edits == null || com == null) return list;
            foreach (int level in vm.Edits.DirtyLevels.ToList())
            {
                LevelModel before = null;
                try { before = s.LoadLevel(level); } catch (Exception) { }
                foreach (var o in Hitboxes.NewCreatureOverlaps(before, vm.Edits.Model(level), com))
                    list.Add("Level " + level + ": " + Hitboxes.Describe(s, o));
            }
            return list;
        }

        /// <summary>After a save: says which new overlaps were written.</summary>
        void WarnOverlaps(List<string> warnings)
        {
            if (warnings == null || warnings.Count == 0) return;
            string text = "Saved, but these collision boxes now overlap (a creature that starts inside another box may be stuck: nothing in the game pushes them apart):\n\n"
                + string.Join("\n", warnings.Take(20)) + (warnings.Count > 20 ? "\n… and " + (warnings.Count - 20) + " more." : "")
                + "\n\nView ▸ Collision edges shows the boxes; Level ▸ Fixes lists every overlap.";
            _ = MessageWindow.Show(this, "Overlapping hitboxes", text);
        }
    }
}
