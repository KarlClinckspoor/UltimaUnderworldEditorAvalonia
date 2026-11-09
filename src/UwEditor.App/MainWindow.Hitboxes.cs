using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UwEditor.Core;

namespace UwEditor.App
{
    /// <summary>The main window's side of View ▸ Collision edges, Collision faces and Light flags, and the warning on save about new overlaps.</summary>
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

        /// <summary>The light flag's colour: a faint blue.</summary>
        static readonly System.Numerics.Vector4 LightFlagColour = new System.Numerics.Vector4(0.35f, 0.62f, 1f, 0.34f);

        /// <summary>View ▸ Light flags: a faint blue sheet over the floor of every open square with the light flag, as the zones are drawn (Markers.TileSheet, following slopes).</summary>
        void AddLightFlagOverlay(List<float> tris)
        {
            var level = vm.Level;
            if (!vm.ShowLightFlags || level == null || tris == null) return;
            const float up = 0.03f;                      // a hair above the zones' sheet, so both show
            for (int y = 0; y < 64; y++)
                for (int x = 0; x < 64; x++)
                {
                    var t = level.Tiles[x, y];
                    if (t.IsSolid || (t.Light & 1) == 0) continue;
                    Markers.TileSheet(tris, x, y,
                        (float)level.FloorAt(x, y, 0, 0) + up, (float)level.FloorAt(x, y, 1, 0) + up,
                        (float)level.FloorAt(x, y, 0, 1) + up, (float)level.FloorAt(x, y, 1, 1) + up, LightFlagColour);
                }
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
