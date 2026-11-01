using System;
using UltimateEditor.Games;

namespace UwEditor.Core.Editing
{
    /// <summary>
    /// New and cleared levels. An empty slot of UW2's LEV.ARK has no block, and the game
    /// cannot enter it: Map_Load treats a block without the magic word as fatal (map/MAP.C,
    /// "Error code A003"). A new level is laid out as the game lays out an empty object store
    /// (obj/OBJECTS.C Map_ObjFix, ported from the Unity editor's LevArk.EmptyBlock): no tile
    /// lists, the free stacks holding every index (mobile 2-255 from 0x7300, top 253; static
    /// 256-1023 from 0x74FC, top 767), the active list empty, the magic word set; UW2's
    /// overlays and timers empty. That passes Obj_ListOkay: 254 free mobiles plus the player
    /// make 255, and 768 free statics.
    /// </summary>
    public static class NewLevels
    {
        /// <summary>The open room of a new level: 5 by 5 tiles around (32, 32).</summary>
        public const int RoomFrom = 30, RoomTo = 34;
        /// <summary>The room's floor height.</summary>
        public const int RoomHeight = 4;

        /// <summary>A level block with no objects: all rock, or with a small open room in the middle.</summary>
        public static byte[] EmptyBlock(GameKind kind, bool room)
        {
            var p = GameProfile.For(kind);
            var b = new byte[p.LevelBlockSize];
            ResetObjects(b, kind);
            if (room)
                for (int y = RoomFrom; y <= RoomTo; y++)
                    for (int x = RoomFrom; x <= RoomTo; x++)
                        PutTileWord0(b, x, y, 1 | RoomHeight << 4);   // open, floor texture 0
            return b;
        }

        /// <summary>
        /// Empties the object store of a block in place (Map_ObjFix): every tile list cleared,
        /// every record but the player's (1) zeroed, the free stacks full, the active list empty,
        /// UW2's overlays and timers cleared (they name objects), the magic word set.
        /// </summary>
        public static void ResetObjects(byte[] b, GameKind kind)
        {
            for (int t = 0; t < 64 * 64; t++)
            {
                int at = LevelBlock.TilesAt + t * 4 + 2;
                int w = b[at] | b[at + 1] << 8;
                w &= 0x3F;                                                // the wall texture stays
                b[at] = (byte)w; b[at + 1] = (byte)(w >> 8);
            }
            Array.Clear(b, LevelBlock.MobilesAt, LevelBlock.MobileSize);   // record 0, "none"
            Array.Clear(b, LevelBlock.ObjectOffset(2), LevelBlock.ObjectOffset(LevelBlock.Mobiles) - LevelBlock.ObjectOffset(2));
            Array.Clear(b, LevelBlock.StaticsAt, (LevelBlock.Objects - LevelBlock.Mobiles) * LevelBlock.StaticSize);
            // the player's record: off every list in a level file
            int pl = LevelBlock.ObjectOffset(1);
            b[pl + 4] = (byte)(b[pl + 4] & 0x3F); b[pl + 5] = 0;
            b[pl + 6] = (byte)(b[pl + 6] & 0x3F); b[pl + 7] = 0;
            for (int i = 2; i < LevelBlock.Objects; i++) Put16(b, LevelBlock.MobFreeAt + (i - 2) * 2, i);
            Array.Clear(b, LevelBlock.ActiveAt, LevelBlock.ActiveCountAt - LevelBlock.ActiveAt);
            Put16(b, LevelBlock.ActiveCountAt, 0);
            Put16(b, LevelBlock.MobTopAt, 0xFD);
            Put16(b, LevelBlock.StaticTopAt, 0x2FF);
            Put16(b, LevelBlock.MagicAt, LevelBlock.Magic);
            if (kind == GameKind.UW2 && b.Length >= LevelBlock.Uw2OverlaysAt + 0x200)
                Array.Clear(b, LevelBlock.Uw2OverlaysAt, 0x200);
        }

        /// <summary>Makes every tile solid rock (type 0, height 0, no flags), keeping the textures.</summary>
        public static void MakeAllSolid(byte[] b)
        {
            for (int y = 0; y < 64; y++)
                for (int x = 0; x < 64; x++)
                {
                    int at = LevelBlock.TilesAt + (y * 64 + x) * 4;
                    int w = b[at] | b[at + 1] << 8;
                    PutTileWord0(b, x, y, w & 0x3C00);                         // floor texture kept
                }
        }

        /// <summary>
        /// The texture map a new level gets: a copy of the first level of its world that has
        /// one, else the game's default, texture n for index n and doors 0-5 (map/TEXTMAPS.C
        /// init_txtlib). UW2 0x86 bytes: 64 words, 6 door bytes. UW1 0x7A bytes: 48 wall
        /// words, 10 floor words, 6 door bytes.
        /// </summary>
        public static byte[] TextureMap(GameSession s, int level, Func<int, byte[]> texOf = null)
        {
            var p = s.Profile;
            int world = p.WorldOf(level);
            for (int l = 1; l <= p.LevelSlots; l++)
            {
                if (l == level || p.WorldOf(l) != world) continue;
                var t = texOf != null ? texOf(l) : (s.LevArk.Has(p.TextureBlock(l)) ? s.LevArk.Read(p.TextureBlock(l)) : null);
                if (t != null && t.Length == p.TextureBlockSize) return (byte[])t.Clone();
            }
            return DefaultTextureMap(s.Kind);
        }

        public static byte[] DefaultTextureMap(GameKind kind)
        {
            var p = GameProfile.For(kind);
            var t = new byte[p.TextureBlockSize];
            if (kind == GameKind.UW2)
            {
                for (int i = 0; i < 64; i++) Put16(t, 2 * i, i);
                for (int i = 0; i < 6; i++) t[0x80 + i] = (byte)i;
            }
            else
            {
                for (int i = 0; i < 48; i++) Put16(t, 2 * i, i);
                for (int i = 0; i < 10; i++) Put16(t, 0x60 + 2 * i, i);
                for (int i = 0; i < 6; i++) t[0x74 + i] = (byte)i;
            }
            return t;
        }

        static void PutTileWord0(byte[] b, int x, int y, int w)
        {
            int at = LevelBlock.TilesAt + (y * 64 + x) * 4;
            b[at] = (byte)w; b[at + 1] = (byte)(w >> 8);
        }

        static void Put16(byte[] b, int at, int v) { b[at] = (byte)v; b[at + 1] = (byte)(v >> 8); }
    }
}
