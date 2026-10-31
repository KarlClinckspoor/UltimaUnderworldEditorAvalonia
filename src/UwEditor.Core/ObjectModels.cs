using System;
using System.Collections.Generic;
using System.Numerics;
using UltimateEditor.Uw1;

namespace UwEditor.Core
{
    /// <summary>A vertex of the object models: position, texture coordinates, texture layer
    /// (-1: a plain coloured face), colour and the object it belongs to.</summary>
    public struct ModelVertex
    {
        public Vector3 Position;
        public float U, V, Layer, R, G, B, Object;
        public const int Floats = 10;
    }

    /// <summary>The level's objects drawn as the built-in 3D models, one triangle list.</summary>
    public sealed class ModelMesh
    {
        public readonly List<ModelVertex> Vertices = new List<ModelVertex>();
        public readonly List<int> TriangleObject = new List<int>();
        public readonly List<int> TriangleTile = new List<int>();
        /// <summary>The textures the textured faces use, 64×64 each, by layer.</summary>
        public readonly List<RgbaImage> Layers = new List<RgbaImage>();
        /// <summary>The objects drawn here (the others are drawn as pictures).</summary>
        public readonly HashSet<int> Objects = new HashSet<int>();

        public int TriangleCount { get { return TriangleObject.Count; } }

        public float[] ToArray()
        {
            var f = new float[Vertices.Count * ModelVertex.Floats];
            int k = 0;
            foreach (var v in Vertices)
            {
                f[k++] = v.Position.X; f[k++] = v.Position.Y; f[k++] = v.Position.Z; f[k++] = v.U; f[k++] = v.V;
                f[k++] = v.Layer; f[k++] = v.R; f[k++] = v.G; f[k++] = v.B; f[k++] = v.Object;
            }
            return f;
        }
    }

    /// <summary>
    /// How the game draws each object in the 3D view (3d/DRAWOBJ.C do_obj, do_rect, do_door),
    /// from COMOBJ.DAT's render field: 0 a sprite, 1 a critter, 2 a built-in 3D model (or a
    /// door for item &amp; 0x3F below 0x10), 3 a texture-mapped model (levers and switches,
    /// 0x170-0x17F, take model 0x14 and a TMFLAT picture; the others model 0x16 and the wall
    /// texture named by their owner). Ported from the Unity editor's WalkObjects/WalkView.
    /// </summary>
    public static class ObjectModels
    {
        public enum Kind { None, Sprite, Critter, Model, Door, TmapLever, TmapWall }


        /// <summary>The model for each render-2 item, by item &amp; 0x3F less 0x10 (rect_sub).</summary>
        static readonly int[] RectSubUw1 =
        {
            0x03, 0x08, 0x08, 0x07, 0x07, 0x06, 0x05, 0x0B,
            0x18, 0x09, 0x17, 0x1B, 0x1C, 0x19, 0x1A, 0x04,
            0x0A, 0x10, 0x11, -1, 0x02, 0x13, 0x12, -1,
            -1, -1, -1, -1, -1, -1, -1, -1
        };
        static readonly int[] RectSubUw2 =
        {
            0x03, 0x08, 0x08, 0x07, 0x07, 0x06, 0x05, 0x0B,
            0x18, 0x09, 0x17, 0x1B, 0x1C, 0x19, 0x1A, 0x04,
            0x0A, 0x10, 0x11, 0x0D, 0x02, 0x13, 0x12, 0x1D,
            0x1E, 0x1F, -1, -1, -1, -1, -1, -1
        };

        /// <summary>UW1 rect_cols: flags, three colours (the third is, for a textured model, the
        /// first texture in bits 0-4 and the count less one in bits 5-7).</summary>
        static readonly byte[,] RectColsUw1 =
        {
            { 0x01, 0xEC, 0x00, 0x00 }, { 0x21, 0xEB, 0x00, 0x00 }, { 0x11, 0xEC, 0x00, 0x3E }, { 0x01, 0xE4, 0x00, 0x00 },
            { 0x02, 0xB6, 0xB0, 0x00 }, { 0x02, 0x64, 0x6C, 0x00 }, { 0x02, 0x64, 0x6C, 0x00 }, { 0x02, 0x64, 0x6C, 0x00 },
            { 0x42, 0xE8, 0xB8, 0x00 }, { 0x01, 0xE4, 0x00, 0x00 }, { 0x19, 0xE4, 0x00, 0x60 }, { 0x03, 0xA3, 0xA4, 0xA6 },
            { 0x01, 0x68, 0x00, 0x00 }, { 0x01, 0x68, 0x00, 0x00 }, { 0x11, 0xEC, 0x00, 0x00 }, { 0x21, 0xEC, 0x00, 0x00 },
            { 0x51, 0xB0, 0x00, 0xE4 }, { 0x51, 0xB0, 0x00, 0xEC }, { 0x11, 0xB0, 0x00, 0xF4 }, { 0x11, 0x6A, 0x00, 0x3C },
            { 0x51, 0xB0, 0x00, 0x00 }, { 0x11, 0xB0, 0x00, 0x00 }, { 0x21, 0xB0, 0x00, 0x00 }, { 0x83, 0x00, 0x02, 0x04 },
            { 0x02, 0xE4, 0x68, 0x00 }, { 0x02, 0xE6, 0x68, 0x00 }, { 0x01, 0xE4, 0x00, 0x00 }, { 0x02, 0xE4, 0x6A, 0x00 },
            { 0x03, 0xE6, 0x6A, 0x71 }, { 0x03, 0xE2, 0x62, 0xC4 }, { 0, 0, 0, 0 }, { 0, 0, 0, 0 },
        };
        /// <summary>UW2 rect_cols: flags, three colours, first texture, number of textures.</summary>
        static readonly byte[,] RectColsUw2 =
        {
            { 0x01, 0x8F, 0x00, 0x00, 0x00 }, { 0x21, 0x93, 0x00, 0x00, 0x00 }, { 0x11, 0x8F, 0x00, 0x1E, 0x02 }, { 0x01, 0x8C, 0x00, 0x00, 0x00 },
            { 0x02, 0x21, 0x1B, 0x00, 0x00 }, { 0x02, 0xC6, 0xCC, 0x00, 0x00 }, { 0x02, 0xC6, 0xCC, 0x00, 0x00 }, { 0x02, 0xC6, 0xCC, 0x00, 0x00 },
            { 0x43, 0x88, 0x23, 0x56, 0x00 }, { 0x01, 0x8C, 0x00, 0x00, 0x00 }, { 0x19, 0x8C, 0x00, 0x00, 0x04 }, { 0x03, 0xB9, 0xBA, 0xBC, 0x00 },
            { 0x01, 0xCA, 0x00, 0x00, 0x00 }, { 0x11, 0x8D, 0x00, 0x2A, 0x06 }, { 0x11, 0x8F, 0x00, 0x00, 0x00 }, { 0x21, 0x8F, 0x00, 0x00, 0x00 },
            { 0x51, 0x1B, 0x00, 0x04, 0x08 }, { 0x51, 0x1B, 0x00, 0x0C, 0x08 }, { 0x11, 0x1B, 0x00, 0x14, 0x08 }, { 0x11, 0xCB, 0x00, 0x1C, 0x02 },
            { 0x51, 0x1B, 0x00, 0x00, 0x00 }, { 0x11, 0x1B, 0x00, 0x00, 0x00 }, { 0x21, 0x1B, 0x00, 0x00, 0x00 }, { 0x83, 0x00, 0x02, 0x04, 0x00 },
            { 0x12, 0x8C, 0xCA, 0x20, 0x04 }, { 0x02, 0x8E, 0xCA, 0x00, 0x00 }, { 0x01, 0x8C, 0x00, 0x00, 0x00 }, { 0x02, 0x8C, 0xCB, 0x8E, 0x00 },
            { 0x11, 0x8F, 0x00, 0x26, 0x04 }, { 0x04, 0x31, 0xC6, 0x52, 0x4D }, { 0x02, 0xCC, 0x02, 0x00, 0x00 }, { 0x12, 0x15, 0x35, 0x24, 0x02 },
        };
        /// <summary>COMOBJ.DAT's render field for each item (11 bytes an item after a 2-byte
        /// header, byte 9 bits 0-1; include/object.h struct ComObj).</summary>
        public static int[] ReadRenderTypes(byte[] comobj)
        {
            var r = new int[512];
            for (int i = 0; i < 512; i++)
            {
                int at = 2 + i * 11 + 9;
                r[i] = comobj != null && at < comobj.Length ? comobj[at] & 3 : 0;
            }
            return r;
        }

        /// <summary>How an item is drawn; model is the built-in model for Model, the door's leaf, and the tmaps.</summary>
        public static Kind Classify(int item, int[] render, bool uw2, out int model)
        {
            model = -1;
            if (item >= 0x180 && item < 0x1C0) return Kind.None;   // traps and triggers are not drawn
            int type = render != null && item < render.Length ? render[item] : 0;
            switch (type)
            {
                case 1: return Kind.Critter;
                case 2:
                    int low = item & 0x3F;
                    if (low >> 4 == 0)
                    {
                        if (item < 0x140 || item >= 0x150) return Kind.None;   // the moving door animation (0x1CF) is skipped
                        // do_door: the leaf is model 0xE, 0xF for the secret door, 0xC for the portcullis
                        int door = item & 7;
                        model = door == 7 ? 0x0F : door == 6 ? 0x0C : 0x0E;
                        return Kind.Door;
                    }
                    int idx = low - 0x10;
                    int[] sub = uw2 ? RectSubUw2 : RectSubUw1;
                    if (idx < 0 || idx >= sub.Length || sub[idx] < 0) return Kind.None;
                    model = sub[idx];
                    return Kind.Model;
                case 3:
                    if (((item >> 4) & 3) == 3) { model = 0x14; return Kind.TmapLever; }
                    model = 0x16;
                    return Kind.TmapWall;
                default: return Kind.Sprite;
            }
        }

        /// <summary>
        /// The texture a model's textured faces take when the object does not name one (do_rect):
        /// a TMOBJ-range number (0-0x0F TMFLAT, 0x10-0x2F TMOBJ, then the doors), or a floor
        /// texture for the bridge (model 2) when its flags pass the model's texture count. -1: none.
        /// </summary>
        public static int ModelTexture(int model, int flags, bool uw2, out bool floorTexture)
        {
            floorTexture = false;
            if (model < 0 || model >= 32) return -1;
            if (uw2)
            {
                if ((RectColsUw2[model, 0] & 0x10) == 0) return -1;
                int ntex = RectColsUw2[model, 4], first = RectColsUw2[model, 3];
                if (ntex == 0) return -1;
                if (model == 2 && flags >= ntex) { floorTexture = true; return flags - ntex; }
                return first + 0x10 + flags % ntex;
            }
            if ((RectColsUw1[model, 0] & 0x10) == 0) return -1;
            int first1 = RectColsUw1[model, 3], count = (first1 >> 5) + 1;
            if (model == 2 && flags >= count) { floorTexture = true; return flags - count; }
            return (first1 & 0x1F) + 0x10 + flags % count;
        }

        // ---- building the level's models

        sealed class Builder
        {
            public LevelModel Level;
            public GameGraphics G;
            public Uw1ExeModels Exe;
            public ModelMesh Mesh = new ModelMesh();
            readonly Dictionary<string, int> layers = new Dictionary<string, int>();
            public bool Uw2;

            /// <summary>The layer of a texture: "tm:n" (the renderer's object textures: 0-0x0F
            /// TMFLAT, from 0x10 TMOBJ, then the level's six doors from 0x30 in UW1 and 0x40 in
            /// UW2: gfx/LOADGR.C first_tmobj, load_doors), "wall:n" and "floor:n" (the level's
            /// wall and floor lists). -1 when there is no such picture.</summary>
            public int Layer(string kind, int n)
            {
                string key = kind + ":" + n;
                int l;
                if (layers.TryGetValue(key, out l)) return l;
                RgbaImage img = null;
                if (kind == "wall")
                {
                    var t = G.WallTexture(Level.WallTextureNumber(n));
                    if (t != null) img = G.TextureRgba(t, G.WallEdge, 64);
                }
                else if (kind == "floor")
                {
                    var t = G.FloorTexture(Level.FloorTextureNumber(n));
                    if (t != null) img = G.TextureRgba(t, G.FloorEdge, 64);
                }
                else
                {
                    int doors = Uw2 ? 0x40 : 0x30;
                    RgbaImage p = null;
                    if (n < 0x10) p = At(G.TmFlat, n);
                    else if (n < doors) p = At(G.TmObj, n - 0x10);
                    else if (n - doors < 6) p = At(G.Doors, Level.DoorMap[n - doors]);
                    if (p != null) img = Resample(p, 64);
                }
                l = img == null ? -1 : Mesh.Layers.Count;
                if (img != null) Mesh.Layers.Add(img);
                layers[key] = l;
                return l;
            }

            static RgbaImage At(List<RgbaImage> list, int i) { return i >= 0 && i < list.Count ? list[i] : null; }

            public Uw1ExeModels.Model Model(int i)
            {
                if (Exe == null || i < 0 || i >= Exe.Models.Count) return null;
                var m = Exe.Models[i];
                return m != null && m.Faces.Count > 0 ? m : null;
            }

            /// <summary>
            /// The model's faces in tiles, turned clockwise by headingDegrees about origin
            /// (the model's x east, y north, which is towards the wall for wall objects, z up).
            /// Coloured faces take their palette colour; textured faces the layer, mapped once
            /// across each face's extent. topTo above 0: corners at height 1 (where the decoder
            /// stops the door frame and the pillar, which reach the ceiling in the game) are
            /// raised to that height above the origin.
            /// </summary>
            public void Add(Uw1ExeModels.Model m, Vector3 origin, float headingDegrees, int layer, int obj, int tile, float topTo = -1f)
            {
                if (m == null) return;
                double a = headingDegrees * Math.PI / 180;
                float cos = (float)Math.Cos(a), sin = (float)Math.Sin(a);
                Func<float[], Vector3> world = p =>
                {
                    float z = topTo > 0 && p[2] >= 0.999f ? topTo : p[2];
                    // clockwise from above: north (0, 1) turns to east (1, 0) at 90 degrees
                    return origin + new Vector3(p[0] * cos + p[1] * sin, z, -p[0] * sin + p[1] * cos);
                };
                foreach (var f in m.Faces)
                {
                    if (f.Triangles == null || f.Vertices == null || f.Vertices.Length < 3) continue;
                    bool ok = true;
                    foreach (int vi in f.Vertices)
                        if (vi < 0 || vi >= m.Vertices.Count || m.Vertices[vi] == null) { ok = false; break; }
                    if (!ok) continue;
                    int faceLayer = f.Textured ? layer : -1;
                    Vector3 col = Vector3.One;
                    if (faceLayer < 0)
                    {
                        uint c = f.Color >= 0 && f.Color < 256 ? G.Palette[f.Color] : 0xFF999999u;
                        col = new Vector3((c >> 16 & 0xFF) / 255f, (c >> 8 & 0xFF) / 255f, (c & 0xFF) / 255f);
                    }
                    // texture coordinates: the face's own plane, across its extent, image top up
                    Func<int, Vector2> uv = vi => Vector2.Zero;
                    if (faceLayer >= 0)
                    {
                        double[] n = Uw1ExeModels.Normal(m, f.Vertices);
                        bool flat = n != null && Math.Abs(n[2]) >= Math.Abs(n[0]) && Math.Abs(n[2]) >= Math.Abs(n[1]);
                        float ax = 1f, ay = 0f;
                        if (!flat && n != null)
                        {
                            double len = Math.Sqrt(n[0] * n[0] + n[1] * n[1]);
                            if (len > 1e-9) { ax = (float)(-n[1] / len); ay = (float)(n[0] / len); }
                        }
                        Func<float[], Vector2> raw = p => flat ? new Vector2(p[0], p[1]) : new Vector2(p[0] * ax + p[1] * ay, p[2]);
                        float umin = float.MaxValue, umax = float.MinValue, vmin = float.MaxValue, vmax = float.MinValue;
                        foreach (int vi in f.Vertices)
                        {
                            var q = raw(m.Vertices[vi]);
                            umin = Math.Min(umin, q.X); umax = Math.Max(umax, q.X); vmin = Math.Min(vmin, q.Y); vmax = Math.Max(vmax, q.Y);
                        }
                        float du = Math.Max(1e-5f, umax - umin), dv = Math.Max(1e-5f, vmax - vmin);
                        uv = vi => { var q = raw(m.Vertices[vi]); return new Vector2((q.X - umin) / du, 1f - (q.Y - vmin) / dv); };
                    }
                    foreach (var tri in f.Triangles)
                    {
                        if (tri == null || tri.Length < 3) continue;
                        var p0 = world(m.Vertices[tri[0]]); var p1 = world(m.Vertices[tri[1]]); var p2 = world(m.Vertices[tri[2]]);
                        // a little shading by the face's direction, so the shapes read
                        var nrm = Vector3.Cross(p1 - p0, p2 - p0);
                        float shade = nrm.LengthSquared() > 1e-12f ? 0.62f + 0.38f * Math.Abs(Vector3.Dot(Vector3.Normalize(nrm), LightDir)) : 0.8f;
                        var c = col * shade;
                        Vertex(p0, uv(tri[0]), faceLayer, c, obj);
                        Vertex(p1, uv(tri[1]), faceLayer, c, obj);
                        Vertex(p2, uv(tri[2]), faceLayer, c, obj);
                        Mesh.TriangleObject.Add(obj);
                        Mesh.TriangleTile.Add(tile);
                    }
                }
                Mesh.Objects.Add(obj);
            }

            void Vertex(Vector3 p, Vector2 uv, int layer, Vector3 c, int obj)
            {
                Mesh.Vertices.Add(new ModelVertex { Position = p, U = uv.X, V = uv.Y, Layer = layer, R = c.X, G = c.Y, B = c.Z, Object = obj });
            }
        }

        static readonly Vector3 LightDir = Vector3.Normalize(new Vector3(0.35f, 0.8f, 0.5f));

        /// <summary>A picture scaled (nearest) to a square texture layer.</summary>
        public static RgbaImage Resample(RgbaImage p, int edge)
        {
            var img = new RgbaImage(edge, edge);
            for (int y = 0; y < edge; y++)
                for (int x = 0; x < edge; x++)
                {
                    int sx = x * p.Width / edge, sy = y * p.Height / edge;
                    Array.Copy(p.Pixels, (sy * p.Width + sx) * 4, img.Pixels, (y * edge + x) * 4, 4);
                }
            return img;
        }

        /// <summary>A wall object's face lies on the wall: drawn a hair in front so the wall can't cover it.</summary>
        static Vector3 OffWall(Vector3 at, float headingDegrees)
        {
            double a = headingDegrees * Math.PI / 180;
            return at - new Vector3((float)Math.Sin(a), 0, (float)Math.Cos(a)) * 0.01f;
        }

        /// <summary>The objects on the level's tiles that the game draws as 3D models.</summary>
        public static ModelMesh Build(LevelModel level, bool invisible)
        {
            var s = level.Session;
            var b = new Builder { Level = level, G = s.Graphics, Exe = s.ExeModels, Uw2 = s.IsUw2 };
            if (b.Exe == null) return b.Mesh;
            foreach (var o in level.Objects)
            {
                if (!o.OnTile || o.Free || (o.Invisible && !invisible)) continue;
                int model;
                var kind = Classify(o.Item, s.RenderTypes, s.IsUw2, out model);
                if (kind == Kind.None || kind == Kind.Sprite || kind == Kind.Critter) continue;
                var at = new Vector3((float)o.WorldX, (float)o.WorldZ, (float)o.WorldY);
                float heading = o.Heading * 45f;
                int tile = o.TileX + 64 * o.TileY;
                var tileInfo = level.Tiles[o.TileX, o.TileY];
                int flags = (o.Record[0] | o.Record[1] << 8) >> 9 & 7;
                var m = b.Model(model);
                switch (kind)
                {
                    case Kind.Door:
                    {
                        // do_door: the frame (model 1) in the tile's wall texture, up to the
                        // ceiling, and the leaf hinged at x -0.1875 of the frame's opening.
                        // An open door (flags & 7) is drawn 0xC0 (3 steps) lower: opening raised
                        // the object by 24 (obj/USEITEMS.C OpenDoor). Its leaf turns by flags & 7
                        // sixteenths of a turn, one way or the other by the door-direction bit
                        // (bit 13). A portcullis (6) is model 0xC, raised where it stands; the
                        // secret door (7) is model 0xF in the wall texture; the others model 0xE
                        // in the level's door texture item & 7.
                        int door = o.Item & 7;
                        bool portcullis = door == 6;
                        var floorAt = at;
                        if (!portcullis && flags != 0) floorAt.Y -= 0.75f;
                        int wallLayer = b.Layer("wall", tileInfo.WallIndex);
                        b.Add(b.Model(1), floorAt, heading, wallLayer, o.Index, tile, LevelGeometry.Ceiling - floorAt.Y);
                        var leaf = m ?? b.Model(0x0E);
                        int leafLayer = door == 7 ? wallLayer : portcullis ? -1 : b.Layer("tm", (s.IsUw2 ? 0x40 : 0x30) + door);
                        double ha = heading * Math.PI / 180;
                        var hinge = (portcullis ? at : floorAt) + new Vector3(-0.1875f * (float)Math.Cos(ha), 0, 0.1875f * (float)Math.Sin(ha));
                        bool doorDir = (o.Record[1] & 0x20) != 0;
                        float leafHeading = portcullis ? heading : heading + flags * (doorDir ? 1 : -1) * 22.5f;
                        b.Add(leaf, hinge, leafHeading, leafLayer, o.Index, tile);
                        break;
                    }
                    case Kind.TmapLever:
                        if (m == null) continue;
                        b.Add(m, OffWall(at, heading), heading, b.Layer("tm", o.Item & 0xF), o.Index, tile);
                        break;
                    case Kind.TmapWall:
                        if (m == null) continue;
                        b.Add(m, OffWall(at, heading), heading, b.Layer("wall", o.Owner), o.Index, tile);
                        break;
                    case Kind.Model:
                    {
                        if (m == null) continue;
                        bool floorTex;
                        int tex = ModelTexture(model, flags, s.IsUw2, out floorTex);
                        int layer = tex < 0 ? -1 : b.Layer(floorTex ? "floor" : "tm", tex);
                        var p = at;
                        // writings, switches and UW2's paintings hang on walls
                        if (model == 0x0D || model == 0x10 || model == 0x11 || model == 0x12) p = OffWall(at, heading);
                        // the pillar, like the door frame, reaches the ceiling in the game
                        b.Add(m, p, heading, layer, o.Index, tile, model == 0x0A ? LevelGeometry.Ceiling - p.Y : -1f);
                        break;
                    }
                }
            }
            return b.Mesh;
        }
    }
}
