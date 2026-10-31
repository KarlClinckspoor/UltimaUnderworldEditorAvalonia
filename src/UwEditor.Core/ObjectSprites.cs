using System;
using System.Collections.Generic;
using System.Numerics;

namespace UwEditor.Core
{
    /// <summary>A packed picture: where it is in the atlas, in pixels.</summary>
    public struct AtlasRect
    {
        public int X, Y, W, H;
    }

    /// <summary>All the object pictures packed into one RGBA image, in rows (shelf packing).</summary>
    public sealed class SpriteAtlas
    {
        public RgbaImage Image;
        public AtlasRect[] Rects;

        public static SpriteAtlas Build(IList<RgbaImage> pictures, int width = 512)
        {
            var a = new SpriteAtlas { Rects = new AtlasRect[pictures.Count] };
            int x = 1, y = 1, rowH = 0;
            for (int i = 0; i < pictures.Count; i++)
            {
                var p = pictures[i];
                if (p == null) continue;
                if (x + p.Width + 1 > width) { x = 1; y += rowH + 1; rowH = 0; }
                a.Rects[i] = new AtlasRect { X = x, Y = y, W = p.Width, H = p.Height };
                x += p.Width + 1;
                rowH = Math.Max(rowH, p.Height);
            }
            int height = 1;
            while (height < y + rowH + 1) height *= 2;
            a.Image = new RgbaImage(width, height);
            for (int i = 0; i < pictures.Count; i++)
            {
                var p = pictures[i];
                if (p == null) continue;
                var r = a.Rects[i];
                for (int row = 0; row < p.Height; row++)
                    Array.Copy(p.Pixels, row * p.Width * 4, a.Image.Pixels, ((r.Y + row) * width + r.X) * 4, p.Width * 4);
            }
            return a;
        }
    }

    /// <summary>One object drawn as a picture that always faces the camera, its middle half its
    /// height above the object's point.</summary>
    public struct SpriteInstance
    {
        public int Object;
        public Vector3 Base;
        public Vector3 Centre { get { return Base + new Vector3(0, Height / 2, 0); } }
        public float Width, Height;
        public AtlasRect Rect;
        public float Alpha;
    }

    public sealed class SpriteFilter
    {
        public bool Invisible, TrapsAndTriggers;
        /// <summary>Objects drawn some other way (as models).</summary>
        public HashSet<int> Skip;
    }

    /// <summary>
    /// The level's objects as sprites, for now: every object's OBJECTS.GR picture (picture
    /// number = item id), 64 pixels a tile, critters scaled to 0.6 tiles high (the walk view's
    /// sizes). Built-in models, doors and critter animations come later.
    /// </summary>
    public static class ObjectSprites
    {
        public const float TilesPerPixel = 1f / 64f, CritterHeight = 0.6f;

        public static List<SpriteInstance> Build(LevelModel level, SpriteAtlas atlas, SpriteFilter filter)
        {
            var list = new List<SpriteInstance>();
            foreach (var o in level.Objects)
            {
                if (!o.OnTile || o.Free) continue;
                if (filter.Skip != null && filter.Skip.Contains(o.Index)) continue;
                if (o.Invisible && !filter.Invisible) continue;
                if (o.IsTrapOrTrigger && !filter.TrapsAndTriggers) continue;
                if (o.Item < 0 || o.Item >= atlas.Rects.Length || atlas.Rects[o.Item].W == 0) continue;
                var r = atlas.Rects[o.Item];
                float scale = o.IsCritter ? CritterHeight / Math.Max(1, r.H) : TilesPerPixel;
                list.Add(new SpriteInstance
                {
                    Object = o.Index,
                    Base = new Vector3((float)o.WorldX, (float)o.WorldZ, (float)o.WorldY),
                    Width = r.W * scale,
                    Height = r.H * scale,
                    Rect = r,
                    Alpha = o.Invisible || o.IsTrapOrTrigger ? 0.55f : 1f,
                });
            }
            return list;
        }
    }
}
