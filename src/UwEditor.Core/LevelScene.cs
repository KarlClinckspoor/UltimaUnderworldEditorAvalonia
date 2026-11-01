using System.Linq;
using System;
using System.Collections.Generic;
using System.Numerics;

namespace UwEditor.Core
{
    /// <summary>Which parts of the level the 3D view shows.</summary>
    public sealed class SceneOptions
    {
        public bool Ceiling;
        public bool Objects = true;
        public bool Invisible;
        public bool TrapsAndTriggers;
        /// <summary>Doors, furniture, levers and writings as the game's 3D models, not pictures.</summary>
        public bool Models = true;
        /// <summary>Yellow lines every <see cref="GridStep"/> tiles.</summary>
        public bool Grid = true;
        public int GridStep = 5;
    }

    /// <summary>
    /// Everything the 3D view draws for one level, built without GL: the surfaces, the texture
    /// layers they use (64×64 each: the level's walls, then its 16 floor slots), the object
    /// sprites and the marker lines.
    /// </summary>
    public sealed class LevelScene
    {
        public const int LayerEdge = 64;
        public LevelModel Level { get; private set; }
        public LevelMesh Mesh { get; private set; }
        public List<RgbaImage> Layers { get; private set; }
        public List<SpriteInstance> Sprites { get; private set; }
        public SpriteAtlas Atlas { get; private set; }
        /// <summary>The objects drawn as the built-in 3D models (empty when they are off).</summary>
        public ModelMesh Models { get; private set; }

        public static LevelScene Build(LevelModel level, SpriteAtlas atlas, SceneOptions opt)
        {
            var s = new LevelScene { Level = level, Atlas = atlas };
            s.Mesh = LevelGeometry.Build(level, opt.Ceiling);
            s.Layers = BuildLayers(level);
            s.RebuildObjects(opt);
            return s;
        }

        /// <summary>
        /// The same level after an edit, reusing what the edit left alone: the textures when the
        /// texture map is the same, the objects' models and sprites when no object record changed and
        /// no tile holding an object did (a door is drawn with its tile's wall and ceiling). Painting
        /// tiles then rebuilds only the level's mesh.
        /// </summary>
        public static LevelScene Update(LevelScene previous, LevelModel level, SceneOptions opt)
        {
            if (previous == null || previous.Level == null || previous.Level.Level != level.Level || previous.Level.Session != level.Session) return Build(level, previous?.Atlas, opt);
            var s = new LevelScene { Level = level, Atlas = previous.Atlas };
            s.Mesh = LevelGeometry.Build(level, opt.Ceiling);
            bool sameTextures = previous.Level.WallMap.SequenceEqual(level.WallMap) && Enumerable.Range(0, LevelGeometry.FloorLayers).All(i => previous.Level.FloorTextureNumber(i) == level.FloorTextureNumber(i));
            s.Layers = sameTextures ? previous.Layers : BuildLayers(level);
            if (SameObjects(previous.Level, level)) { s.Models = previous.Models; s.Sprites = previous.Sprites; s.ObjectsReused = true; }
            else s.RebuildObjects(opt);
            return s;
        }

        /// <summary>Whether the last Update kept the objects (the view then needs only the mesh).</summary>
        public bool ObjectsReused { get; private set; }
        /// <summary>Whether the textures are the previous scene's.</summary>
        public bool LayersReused(LevelScene previous) { return previous != null && ReferenceEquals(previous.Layers, Layers); }

        /// <summary>No object record changed, and no tile holding an object.</summary>
        public static bool SameObjects(LevelModel a, LevelModel b)
        {
            var da = a.Block.Data; var db = b.Block.Data;
            int from = UltimateEditor.Games.LevelBlock.ObjectOffset(0), to = UltimateEditor.Games.LevelBlock.ObjectOffset(1023) + UltimateEditor.Games.LevelBlock.StaticSize;
            if (da.Length < to || db.Length < to || !da.AsSpan(from, to - from).SequenceEqual(db.AsSpan(from, to - from))) return false;
            for (int y = 0; y < 64; y++)
                for (int x = 0; x < 64; x++)
                {
                    int at = UltimateEditor.Games.LevelBlock.TilesAt + (y * 64 + x) * 4;
                    if ((da[at + 2] | da[at + 3] << 8) >> 6 == 0) continue;           // no objects on it
                    if (da[at] != db[at] || da[at + 1] != db[at + 1] || da[at + 2] != db[at + 2] || da[at + 3] != db[at + 3]) return false;
                }
            return true;
        }

        /// <summary>The objects as models and sprites, after a change of what is shown.</summary>
        public void RebuildObjects(SceneOptions opt)
        {
            Models = opt.Objects && opt.Models ? ObjectModels.Build(Level, opt.Invisible) : new ModelMesh();
            Sprites = opt.Objects && Atlas != null
                ? ObjectSprites.Build(Level, Atlas, new SpriteFilter { Invisible = opt.Invisible, TrapsAndTriggers = opt.TrapsAndTriggers, Skip = Models.Objects })
                : new List<SpriteInstance>();
        }

        static List<RgbaImage> BuildLayers(LevelModel level)
        {
            var g = level.Session.Graphics;
            var list = new List<RgbaImage>();
            foreach (int n in level.WallMap)
            {
                var t = g.WallTexture(n);
                list.Add(t == null ? Missing() : g.TextureRgba(t, g.WallEdge, LayerEdge));
            }
            for (int i = 0; i < LevelGeometry.FloorLayers; i++)
            {
                int n = level.FloorTextureNumber(i);
                var t = n < 0 ? null : g.FloorTexture(n);
                list.Add(t == null ? Missing() : g.TextureRgba(t, g.FloorEdge, LayerEdge));
            }
            list.Add(Missing());
            return list;
        }

        /// <summary>A grey checkerboard for an index the level's texture map does not cover.</summary>
        static RgbaImage Missing()
        {
            var img = new RgbaImage(LayerEdge, LayerEdge);
            for (int y = 0; y < LayerEdge; y++)
                for (int x = 0; x < LayerEdge; x++)
                {
                    byte v = (byte)((((x >> 3) + (y >> 3)) & 1) == 0 ? 90 : 60);
                    int k = (y * LayerEdge + x) * 4;
                    img.Pixels[k] = v; img.Pixels[k + 1] = v; img.Pixels[k + 2] = (byte)(v + 10); img.Pixels[k + 3] = 255;
                }
            return img;
        }

        // ---- marker lines: (x, y, z, r, g, b, a) per point, two points a line

        public static void Line(List<float> f, Vector3 a, Vector3 b, Vector4 c)
        {
            f.Add(a.X); f.Add(a.Y); f.Add(a.Z); f.Add(c.X); f.Add(c.Y); f.Add(c.Z); f.Add(c.W);
            f.Add(b.X); f.Add(b.Y); f.Add(b.Z); f.Add(c.X); f.Add(c.Y); f.Add(c.Z); f.Add(c.W);
        }

        /// <summary>The outline of a tile's floor, a little above it.</summary>
        public void TileOutline(List<float> f, int x, int y, Vector4 colour)
        {
            if (x < 0 || y < 0 || x >= 64 || y >= 64) return;
            var t = Level.Tiles[x, y];
            int[] h = LevelGeometry.CornerHeights(t);
            float lift = 0.02f;
            var c = new[] { new Vector2(0, 0), new Vector2(1, 0), new Vector2(1, 1), new Vector2(0, 1) };
            int[] poly = t.IsSolid ? new[] { 0, 1, 2, 3 } : LevelGeometry.FloorCorners(t.Type);
            for (int i = 0; i < poly.Length; i++)
            {
                int a = poly[i], b = poly[(i + 1) % poly.Length];
                Line(f, new Vector3(x + c[a].X, h[a] * LevelGeometry.Step + lift, y + c[a].Y), new Vector3(x + c[b].X, h[b] * LevelGeometry.Step + lift, y + c[b].Y), colour);
            }
        }

        /// <summary>A cross on the floor with a post and a facing arrow, for the start or the player.</summary>
        public static void StartMarker(List<float> f, Vector3 p, float facingDegrees, Vector4 colour)
        {
            float r = 0.35f;
            Line(f, p + new Vector3(-r, 0.01f, 0), p + new Vector3(r, 0.01f, 0), colour);
            Line(f, p + new Vector3(0, 0.01f, -r), p + new Vector3(0, 0.01f, r), colour);
            Line(f, p, p + new Vector3(0, 0.6f, 0), colour);
            double a = facingDegrees * Math.PI / 180;
            var dir = new Vector3((float)Math.Sin(a), 0, (float)Math.Cos(a));
            var side = new Vector3(dir.Z, 0, -dir.X);
            var tip = p + new Vector3(0, 0.3f, 0) + dir * 0.5f;
            var root = p + new Vector3(0, 0.3f, 0);
            Line(f, root, tip, colour);
            Line(f, tip, tip - dir * 0.15f + side * 0.1f, colour);
            Line(f, tip, tip - dir * 0.15f - side * 0.1f, colour);
        }
    }
}
