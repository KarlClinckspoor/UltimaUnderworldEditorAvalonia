using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Numerics;
using UltimateEditor.Games;
using UltimateEditor.Uw1;

namespace UwEditor.Core
{
    /// <summary>One object's collision box, as the game builds it (motion/COLLIDE.C obj_coll_check).</summary>
    public sealed class Hitbox
    {
        public int Index;
        public int Item;
        public bool Creature;
        /// <summary>The middle, in fine units (8 a tile) from the level's corner.</summary>
        public int FineX, FineY;
        /// <summary>COMOBJ's radius in fine units (4: the whole tile).</summary>
        public int Radius;
        /// <summary>Bottom and top in z units (8 a floor step); a height of 0 counts as 1.</summary>
        public int Bottom, Top;
        public int TileX, TileY;
        /// <summary>It overlaps another box (Hitboxes.Overlaps).</summary>
        public bool Overlapping;

        /// <summary>The box's corners in fine units: the whole tile for radius 4, else the middle ± the radius (less one for a creature when a creature is the other one).</summary>
        public void Bounds(bool shrink, out int x0, out int y0, out int x1, out int y1)
        {
            if (Radius == 4) { x0 = TileX * 8; y0 = TileY * 8; x1 = x0 + 7; y1 = y0 + 7; return; }
            int r = shrink && Creature && Radius > 0 ? Radius - 1 : Radius;
            x0 = FineX - r; x1 = FineX + r; y0 = FineY - r; y1 = FineY + r;
        }
    }

    /// <summary>Two boxes that overlap.</summary>
    public sealed class HitboxOverlap
    {
        public Hitbox A, B;
        public override string ToString()
        {
            return string.Format("#{0} at ({1}, {2}) and #{3} at ({4}, {5})", A.Index, A.TileX, A.TileY, B.Index, B.TileX, B.TileY);
        }
    }

    /// <summary>
    /// The objects' collision boxes and where they overlap. The game's boxes are squares that do
    /// not turn (motion/COLLIDE.C): the middle ± COMOBJ's radius in fine units (radius 4 is the
    /// whole tile), from the object's z up by COMOBJ's height (0 counts as 1). Only these collide:
    /// creatures, and static objects with a height whose Touch flag is set (ObjectCheck skips flat
    /// statics; do_objhit lets movers through the others). A moving creature counts its own radius
    /// and another creature's one smaller, and the bounds are inclusive, so two creatures of radius
    /// 2 touch within 2 fine units of each other. Overlaps: a creature and anything it collides with
    /// (it is blocked, or stuck when it starts inside: nothing pushes objects apart), or two
    /// blocking statics whose boxes share space.
    /// </summary>
    public static class Hitboxes
    {
        const int Record = 11, Header = 2;

        /// <summary>COMOBJ.DAT from DATA, or null.</summary>
        public static byte[] ReadComObj(GameSession s)
        {
            try { string p = GameProfile.Find(s.DataFolder, "COMOBJ.DAT"); return p == null ? null : File.ReadAllBytes(p); }
            catch (Exception) { return null; }
        }

        static int Field(byte[] com, int item, string name)
        {
            int at = Header + Record * item;
            if (com == null || at + Record > com.Length) return 0;
            return Uw1Schemas.ComObj[name].Get(com, at);
        }

        /// <summary>The boxes of the objects lying on the tiles that collide (creatures, and statics with a height and Touch).</summary>
        public static List<Hitbox> Of(LevelModel m, byte[] comobj)
        {
            var list = new List<Hitbox>();
            if (m == null || comobj == null) return list;
            foreach (var o in m.Objects)
            {
                if (o.Free || !o.OnTile || o.TileX < 0 || o.Item >= 0x180) continue;
                int height = Field(comobj, o.Item, "Height");
                bool creature = o.IsCritter;
                if (!creature && (height == 0 || Field(comobj, o.Item, "Touch") == 0)) continue;
                list.Add(new Hitbox
                {
                    Index = o.Index, Item = o.Item, Creature = creature, TileX = o.TileX, TileY = o.TileY,
                    FineX = o.TileX * 8 + o.FineX, FineY = o.TileY * 8 + o.FineY,
                    Radius = Field(comobj, o.Item, "Radius"), Bottom = o.Z, Top = o.Z + Math.Max(1, height),
                });
            }
            return list;
        }

        /// <summary>Whether two boxes overlap by the game's rule (see the class).</summary>
        public static bool Overlap(Hitbox a, Hitbox b)
        {
            if (a.Bottom >= b.Top || b.Bottom >= a.Top) return false;
            int ax0, ay0, ax1, ay1, bx0, by0, bx1, by1;
            if (a.Creature || b.Creature)
            {
                // as a moving creature sees it: inclusive bounds, creatures one smaller
                a.Bounds(true, out ax0, out ay0, out ax1, out ay1);
                b.Bounds(true, out bx0, out by0, out bx1, out by1);
                return ax1 >= bx0 && bx1 >= ax0 && ay1 >= by0 && by1 >= ay0;
            }
            // two statics never move into each other: only boxes that share space count
            a.Bounds(false, out ax0, out ay0, out ax1, out ay1);
            b.Bounds(false, out bx0, out by0, out bx1, out by1);
            return ax1 > bx0 && bx1 > ax0 && ay1 > by0 && by1 > ay0;
        }

        /// <summary>Every overlapping pair; marks the boxes that overlap.</summary>
        public static List<HitboxOverlap> Overlaps(List<Hitbox> boxes)
        {
            var res = new List<HitboxOverlap>();
            foreach (var b in boxes) b.Overlapping = false;
            var sorted = boxes.OrderBy(b => b.FineX).ToList();
            for (int i = 0; i < sorted.Count; i++)
                for (int j = i + 1; j < sorted.Count; j++)
                {
                    if (sorted[j].FineX - sorted[i].FineX > 16) break;        // radius 4 reaches a tile at most
                    if (!Overlap(sorted[i], sorted[j])) continue;
                    sorted[i].Overlapping = sorted[j].Overlapping = true;
                    var a = sorted[i].Index < sorted[j].Index ? sorted[i] : sorted[j];
                    res.Add(new HitboxOverlap { A = a, B = a == sorted[i] ? sorted[j] : sorted[i] });
                }
            return res.OrderBy(o => o.A.Index).ThenBy(o => o.B.Index).ToList();
        }

        /// <summary>"#235 mountainman at (37, 7) overlaps #921 door at (37, 7)".</summary>
        public static string Describe(GameSession s, HitboxOverlap o)
        {
            return string.Format("#{0} {1} at ({2}, {3}) overlaps #{4} {5} at ({6}, {7}).", o.A.Index, s.Names.Get(o.A.Item), o.A.TileX, o.A.TileY, o.B.Index, s.Names.Get(o.B.Item), o.B.TileX, o.B.TileY);
        }

        /// <summary>
        /// The overlaps with a creature that the edited level has and the level as it was did not
        /// (by the two objects' indexes): what saving it would add.
        /// </summary>
        public static List<HitboxOverlap> NewCreatureOverlaps(LevelModel before, LevelModel after, byte[] comobj)
        {
            Func<LevelModel, List<HitboxOverlap>> of = m => m == null || !m.Present ? new List<HitboxOverlap>() : Overlaps(Of(m, comobj)).Where(o => o.A.Creature || o.B.Creature).ToList();
            var old = new HashSet<(int, int)>(of(before).Select(o => (o.A.Index, o.B.Index)));
            return of(after).Where(o => !old.Contains((o.A.Index, o.B.Index))).ToList();
        }

        public static readonly Vector4 StaticColour = new Vector4(1f, 0.72f, 0.12f, 1f);
        public static readonly Vector4 CreatureColour = new Vector4(1f, 0.25f, 0.95f, 1f);
        public static readonly Vector4 OverlapColour = new Vector4(1f, 0.08f, 0.08f, 1f);

        static Vector4 ColourOf(Hitbox b) { return b.Overlapping ? OverlapColour : b.Creature ? CreatureColour : StaticColour; }

        /// <summary>The box's eight corners in the view's space (x east, y up, z north, in tiles).</summary>
        static Vector3[] Corners(Hitbox b)
        {
            int x0, y0, x1, y1;
            b.Bounds(false, out x0, out y0, out x1, out y1);
            // a fine unit's middle is at +0.5; the box reaches the outer edges of its end units
            float fx0 = x0 / 8f, fx1 = (x1 + 1) / 8f, fy0 = y0 / 8f, fy1 = (y1 + 1) / 8f;
            float z0 = b.Bottom / 32f, z1 = b.Top / 32f;
            return new[]
            {
                new Vector3(fx0, z0, fy0), new Vector3(fx1, z0, fy0), new Vector3(fx1, z0, fy1), new Vector3(fx0, z0, fy1),
                new Vector3(fx0, z1, fy0), new Vector3(fx1, z1, fy0), new Vector3(fx1, z1, fy1), new Vector3(fx0, z1, fy1),
            };
        }

        static readonly int[] EdgePairs = { 0, 1, 1, 2, 2, 3, 3, 0, 4, 5, 5, 6, 6, 7, 7, 4, 0, 4, 1, 5, 2, 6, 3, 7 };
        static readonly int[] FaceQuads = { 0, 1, 2, 3, 4, 5, 6, 7, 0, 1, 5, 4, 1, 2, 6, 5, 2, 3, 7, 6, 3, 0, 4, 7 };

        /// <summary>The boxes' twelve edges each, as marker lines (x, y, z, r, g, b, a per point).</summary>
        public static void AddEdges(List<float> lines, IEnumerable<Hitbox> boxes)
        {
            foreach (var b in boxes)
            {
                var c = Corners(b);
                var col = ColourOf(b);
                for (int k = 0; k < EdgePairs.Length; k += 2) LevelScene.Line(lines, c[EdgePairs[k]], c[EdgePairs[k + 1]], col);
            }
        }

        /// <summary>The boxes' six faces each, see-through, as marker triangles.</summary>
        public static void AddFaces(List<float> tris, IEnumerable<Hitbox> boxes, float alpha = 0.38f)
        {
            foreach (var b in boxes)
            {
                var c = Corners(b);
                var col = ColourOf(b);
                col.W = alpha;
                for (int k = 0; k < FaceQuads.Length; k += 4)
                {
                    Vertex(tris, c[FaceQuads[k]], col); Vertex(tris, c[FaceQuads[k + 1]], col); Vertex(tris, c[FaceQuads[k + 2]], col);
                    Vertex(tris, c[FaceQuads[k]], col); Vertex(tris, c[FaceQuads[k + 2]], col); Vertex(tris, c[FaceQuads[k + 3]], col);
                }
            }
        }

        static void Vertex(List<float> f, Vector3 p, Vector4 c)
        {
            f.Add(p.X); f.Add(p.Y); f.Add(p.Z); f.Add(c.X); f.Add(c.Y); f.Add(c.Z); f.Add(c.W);
        }
    }
}
