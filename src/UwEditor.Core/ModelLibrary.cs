using System;
using System.Collections.Generic;
using System.Linq;
using UltimateEditor.Uw1;

namespace UwEditor.Core
{
    /// <summary>A built-in 3D model with what the editor knows about it: its byte budget in the executable and the items drawn with it.</summary>
    public sealed class ModelEntry
    {
        public Uw1ExeModels.Model Model;
        public int Index { get { return Model.Index; } }
        /// <summary>Bytes from its start to the next model's start (its room for a replacement), or -1 for the last.</summary>
        public int Budget = -1;
        /// <summary>Other table entries that point at the same bytes.</summary>
        public readonly List<int> SharedWith = new List<int>();
        public readonly List<int> Items = new List<int>();
        public int VertexCount { get { return Model.Vertices.Count(v => v != null); } }
        public int FaceCount { get { return Model.Faces.Count; } }
        public string Label
        {
            get
            {
                string s = string.Format("{0:X2}  {1}", Index, Model.Name);
                if (Model.Error != null) s += "  (cannot be read)";
                else if (FaceCount == 0) s += "  (empty)";
                return s;
            }
        }
        public override string ToString() { return Label; }
    }

    /// <summary>
    /// The 32 models of UW.EXE / UW2.EXE (Uw1ExeModels): bytecode for the renderer's model
    /// interpreter, packed one after another behind a table of 16-bit offsets. The space a
    /// model may take in place is the distance to the next model's start.
    /// </summary>
    public static class ModelLibrary
    {
        public static List<ModelEntry> Of(GameSession s)
        {
            var list = new List<ModelEntry>();
            var exe = s.ExeModels;
            if (exe == null) return list;
            var starts = exe.Models.Select(m => m.FileOffset).Distinct().OrderBy(x => x).ToList();
            foreach (var m in exe.Models)
            {
                var e = new ModelEntry { Model = m };
                int k = starts.IndexOf(m.FileOffset);
                if (k >= 0 && k + 1 < starts.Count) e.Budget = (int)(starts[k + 1] - m.FileOffset);
                e.SharedWith.AddRange(exe.Models.Where(o => o != m && o.FileOffset == m.FileOffset).Select(o => o.Index));
                list.Add(e);
            }
            for (int item = 0; item < 0x200; item++)
            {
                int model;
                var kind = ObjectModels.Classify(item, s.RenderTypes, s.IsUw2, out model);
                if (model >= 0 && model < list.Count && kind != ObjectModels.Kind.Sprite) list[model].Items.Add(item);
            }
            return list;
        }

        /// <summary>The model as Wavefront OBJ (y up) with a material per palette colour, and the matching MTL text.</summary>
        public static string ToObj(Uw1ExeModels.Model m, uint[] palette, string mtlName, out string mtl)
        {
            var sb = new System.Text.StringBuilder();
            var ms = new System.Text.StringBuilder();
            var inv = System.Globalization.CultureInfo.InvariantCulture;
            sb.AppendLine("# Ultima Underworld built-in model " + m.Index + ": " + m.Name);
            sb.AppendLine("# units: 1.0 = one tile; exported by Underworld Editor");
            if (mtlName != null) sb.AppendLine("mtllib " + mtlName);
            sb.AppendLine("o " + m.Name.Replace(' ', '_').Replace("(", "").Replace(")", ""));
            var map = new Dictionary<int, int>();
            for (int i = 0; i < m.Vertices.Count; i++)
            {
                var v = m.Vertices[i];
                if (v == null) continue;
                map[i] = map.Count + 1;
                sb.AppendFormat(inv, "v {0} {1} {2}\n", v[0], v[2], -v[1]);
            }
            var used = new HashSet<string>();
            string current = null;
            foreach (var f in m.Faces)
            {
                string mat = f.Textured ? "textured" : f.Color >= 0 ? "pal" + f.Color : "unknown";
                if (mat != current) { sb.Append("usemtl ").AppendLine(mat); current = mat; }
                if (used.Add(mat))
                {
                    float r = 0.6f, g = 0.6f, b = 0.6f;
                    if (!f.Textured && f.Color >= 0 && palette != null)
                    {
                        uint c = palette[f.Color];
                        r = (c >> 16 & 0xFF) / 255f; g = (c >> 8 & 0xFF) / 255f; b = (c & 0xFF) / 255f;
                    }
                    ms.AppendFormat(inv, "newmtl {0}\nKd {1:0.###} {2:0.###} {3:0.###}\n\n", mat, r, g, b);
                }
                var polys = f.Convex || f.Triangles == null ? new[] { f.Vertices } : f.Triangles;
                foreach (var poly in polys)
                {
                    if (poly.Any(v => !map.ContainsKey(v))) continue;
                    sb.Append('f');
                    // (x, y, z) z up becomes (x, z, -y) y up: a rotation, so the winding stays
                    foreach (int v in poly) sb.Append(' ').Append(map[v]);
                    sb.Append('\n');
                }
            }
            mtl = ms.ToString();
            return sb.ToString();
        }
    }
}
