using System;
using System.Collections.Generic;
using UltimateEditor.Games;
using UltimateEditor.Uw1;

namespace UwEditor.Core
{
    /// <summary>include/map.h enum TileType.</summary>
    public enum TileType
    {
        Solid = 0, Open = 1, OpenSE = 2, OpenSW = 3, OpenNE = 4, OpenNW = 5,
        SlopeUpN = 6, SlopeUpS = 7, SlopeUpE = 8, SlopeUpW = 9,
    }

    /// <summary>One tile, decoded through <see cref="Uw1Schemas.Tile"/> (both games share struct Tile).</summary>
    public sealed class TileInfo
    {
        public int X, Y;
        public TileType Type;
        /// <summary>Floor height in steps, 0-15; the ceiling is 16.</summary>
        public int Height;
        public int Light;
        /// <summary>Index into the level's floor list (UW1 10, UW2 the first 16 of its 64).</summary>
        public int FloorIndex;
        public bool NoMagic, DoorBit;
        /// <summary>Index into the level's wall list (UW1 48, UW2 64).</summary>
        public int WallIndex;
        public int FirstObject;
        /// <summary>The 4 record bytes.</summary>
        public byte[] Record;

        public bool IsSolid { get { return Type == TileType.Solid; } }

        public static string TypeName(TileType t)
        {
            switch (t)
            {
                case TileType.Solid: return "solid";
                case TileType.Open: return "open";
                case TileType.OpenSE: return "diagonal, open SE";
                case TileType.OpenSW: return "diagonal, open SW";
                case TileType.OpenNE: return "diagonal, open NE";
                case TileType.OpenNW: return "diagonal, open NW";
                case TileType.SlopeUpN: return "slope up to the N";
                case TileType.SlopeUpS: return "slope up to the S";
                case TileType.SlopeUpE: return "slope up to the E";
                case TileType.SlopeUpW: return "slope up to the W";
            }
            return t.ToString();
        }
    }

    /// <summary>One object record (8 bytes static, 0x1B mobile), with where it lies.</summary>
    public sealed class ObjectInfo
    {
        public int Index;
        public bool IsMobile { get { return Index < LevelBlock.Mobiles; } }
        public byte[] Record;
        public int Item, Z, Heading, FineX, FineY, Quality, Next, Owner, Link;
        public bool IsQuantity, Invisible, Enchanted;
        public string Name;
        /// <summary>On a free list (unused).</summary>
        public bool Free;
        /// <summary>The tile whose list it is on, directly or through a parent's link; -1, -1 if none.</summary>
        public int TileX = -1, TileY = -1;
        /// <summary>The object whose link list holds it (a container, a critter's inventory, a trigger's trap, a door's lock), or -1.</summary>
        public int Parent = -1;
        /// <summary>Lies directly on its tile's list (not inside anything).</summary>
        public bool OnTile { get { return TileX >= 0 && Parent < 0; } }
        /// <summary>The objects on its link list, when the link is an object list (not a quantity).</summary>
        public readonly List<int> Children = new List<int>();

        public bool IsTrapOrTrigger { get { return Item >= 0x180 && Item < 0x1C0; } }
        public bool IsCritter { get { return Item >= 0x40 && Item < 0x80; } }

        /// <summary>Position in tiles (x east, y north) and height in tiles: fine x and y are
        /// the middles of eighths of the tile; z has 8 units a floor step (a quarter tile).</summary>
        public double WorldX { get { return TileX + (FineX + 0.5) / 8.0; } }
        public double WorldY { get { return TileY + (FineY + 0.5) / 8.0; } }
        public double WorldZ { get { return Z / 32.0; } }

        public override string ToString() { return string.Format("#{0} {1}", Index, Name); }
    }

    /// <summary>A level of LEV.ARK: tiles, objects and texture lists (docs/FORMATS.md "LEV.ARK").</summary>
    public sealed class LevelModel
    {
        public const int Size = 64;
        public int Level { get; private set; }
        public GameSession Session { get; private set; }
        public LevelBlock Block { get; private set; }
        public bool Present { get; private set; }
        public readonly TileInfo[,] Tiles = new TileInfo[Size, Size];
        public readonly ObjectInfo[] Objects = new ObjectInfo[LevelBlock.Objects];
        /// <summary>Texture numbers in the wall / floor file for each index a tile uses.</summary>
        public int[] WallMap, FloorMap, DoorMap;
        public string CheckReport { get; private set; }
        public bool CheckOk { get; private set; }

        /// <summary>UW2's Ethereal Void, levels 65-72 but 68, has no ceiling (uw2 3d/GRIDDB.C).</summary>
        public bool HasCeiling
        {
            get { return !(Session.IsUw2 && (Level - 1) / 8 == 8 && Level != 68); }
        }

        /// <summary>The floor index the ceiling is drawn with: UW1 9, UW2 0xF (3d/GRIDDB.C grdb_elem, gr_ccall).</summary>
        public int CeilingFloorIndex { get { return Session.IsUw2 ? 0xF : 9; } }

        public static LevelModel Load(GameSession s, int level)
        {
            var m = new LevelModel { Level = level, Session = s };
            var p = s.Profile;
            byte[] data = s.LevArk.Has(p.LevelBlock(level)) ? s.LevArk.Read(p.LevelBlock(level)) : null;
            m.Present = data != null && data.Length >= LevelBlock.MagicAt + 2;
            if (!m.Present) data = new byte[p.LevelBlockSize];
            m.Block = new LevelBlock(data, s.Kind);
            m.Present &= m.Block.MagicOk;
            byte[] tex = s.LevArk.Has(p.TextureBlock(level)) ? s.LevArk.Read(p.TextureBlock(level)) : null;
            m.ReadTextureMap(tex);
            m.ReadTiles();
            m.ReadObjects();
            bool ok = false;
            m.CheckReport = m.Present ? m.Block.Check(out ok) : "empty slot";
            m.CheckOk = m.Present && ok;
            return m;
        }

        /// <summary>
        /// The texture map (docs/FORMATS.md "The texture map"). UW1, 0x7A bytes: 48 wall words
        /// (W64.TR), 10 floor words (F32.TR), 6 door bytes. UW2, 0x86 bytes: 64 words of T64.TR
        /// numbers, the first 16 doubling as the floors (a tile's floor index is 4 bits), then
        /// the 6 door bytes at 0x80. A missing block maps each index to itself (init_txtlib).
        /// </summary>
        void ReadTextureMap(byte[] tex)
        {
            Func<int, int> w = i => tex[2 * i] | tex[2 * i + 1] << 8;
            if (Session.IsUw2)
            {
                WallMap = new int[64];
                for (int i = 0; i < 64; i++) WallMap[i] = tex != null && tex.Length >= 2 * i + 2 ? w(i) : i;
                FloorMap = new int[16];
                for (int i = 0; i < 16; i++) FloorMap[i] = WallMap[i];
                DoorMap = new int[6];
                for (int i = 0; i < 6; i++) DoorMap[i] = tex != null && tex.Length > 0x80 + i ? tex[0x80 + i] : i;
                return;
            }
            WallMap = new int[48];
            FloorMap = new int[10];
            DoorMap = new int[6];
            bool okLen = tex != null && tex.Length >= 0x7A;
            for (int i = 0; i < 48; i++) WallMap[i] = okLen ? w(i) : i;
            for (int i = 0; i < 10; i++) FloorMap[i] = okLen ? w(48 + i) : i;
            for (int i = 0; i < 6; i++) DoorMap[i] = okLen ? tex[116 + i] : i;
        }

        /// <summary>The wall texture number in the texture file, or -1.</summary>
        public int WallTextureNumber(int index) { return index >= 0 && index < WallMap.Length ? WallMap[index] : -1; }

        /// <summary>The floor texture number in the texture file, or -1. A UW1 tile can name index 10-15, which the map lacks.</summary>
        public int FloorTextureNumber(int index) { return index >= 0 && index < FloorMap.Length ? FloorMap[index] : -1; }

        void ReadTiles()
        {
            var S = Uw1Schemas.Tile;
            FieldDef fType = S["Type"], fH = S["Floor height"], fL = S["Light"], fF = S["Floor texture"], fNm = S["No magic"],
                fD = S["Door"], fW = S["Wall texture"], fO = S["First object"];
            for (int y = 0; y < Size; y++)
                for (int x = 0; x < Size; x++)
                {
                    int at = LevelBlock.TilesAt + (y * Size + x) * 4;
                    var rec = new byte[4];
                    Array.Copy(Block.Data, at, rec, 0, 4);
                    Tiles[x, y] = new TileInfo
                    {
                        X = x, Y = y, Record = rec,
                        Type = (TileType)fType.Get(rec),
                        Height = fH.Get(rec),
                        Light = fL.Get(rec),
                        FloorIndex = fF.Get(rec),
                        NoMagic = fNm.Get(rec) != 0,
                        DoorBit = fD.Get(rec) != 0,
                        WallIndex = fW.Get(rec),
                        FirstObject = fO.Get(rec),
                    };
                    if ((int)Tiles[x, y].Type > 9) Tiles[x, y].Type = TileType.Solid;
                }
        }

        void ReadObjects()
        {
            var S = Uw1Schemas.StaticObject;
            FieldDef fId = S["Item id"], fZ = S["Z"], fH = S["Heading"], fX = S["Fine X"], fY = S["Fine Y"], fQ = S["Quality"],
                fN = S["Next"], fOw = S["Owner"], fL = S["Link"], fQt = S["Is quantity"], fInv = S["Invisible"], fEn = S["Enchanted"];
            int[] free, reached;
            Block.Counts(out free, out reached);
            for (int i = 0; i < LevelBlock.Objects; i++)
            {
                int size = i < LevelBlock.Mobiles ? LevelBlock.MobileSize : LevelBlock.StaticSize;
                var rec = new byte[size];
                Array.Copy(Block.Data, LevelBlock.ObjectOffset(i), rec, 0, size);
                var o = new ObjectInfo
                {
                    Index = i, Record = rec,
                    Item = fId.Get(rec), Z = fZ.Get(rec), Heading = fH.Get(rec), FineX = fX.Get(rec), FineY = fY.Get(rec),
                    Quality = fQ.Get(rec), Next = fN.Get(rec), Owner = fOw.Get(rec), Link = fL.Get(rec),
                    IsQuantity = fQt.Get(rec) != 0, Invisible = fInv.Get(rec) != 0, Enchanted = fEn.Get(rec) != 0,
                    Free = i < 2 || (free[i] > 0 && reached[i] == 0),
                };
                o.Name = Session.Names.Get(o.Item);
                Objects[i] = o;
            }
            // the tile lists: next links, and the link lists of non-quantity objects (Obj_ListOkay)
            var seen = new bool[LevelBlock.Objects];
            for (int y = 0; y < Size; y++)
                for (int x = 0; x < Size; x++)
                    WalkList(Tiles[x, y].FirstObject, x, y, -1, seen, 0);
        }

        void WalkList(int i, int x, int y, int parent, bool[] seen, int depth)
        {
            int guard = 0;
            while (i > 0 && i < LevelBlock.Objects && !seen[i] && guard++ < 1100)
            {
                seen[i] = true;
                var o = Objects[i];
                o.TileX = x; o.TileY = y; o.Parent = parent;
                if (parent >= 0) Objects[parent].Children.Add(i);
                if (!o.IsQuantity && o.Link > 0 && depth < 64) WalkList(o.Link, x, y, i, seen, depth + 1);
                i = o.Next;
            }
        }

        /// <summary>The objects lying directly on a tile, in list order.</summary>
        public List<ObjectInfo> TileObjects(int x, int y)
        {
            var list = new List<ObjectInfo>();
            int i = Tiles[x, y].FirstObject, guard = 0;
            while (i > 0 && i < LevelBlock.Objects && guard++ < 1100)
            {
                if (Objects[i].TileX != x || Objects[i].TileY != y || Objects[i].Parent >= 0) break;
                list.Add(Objects[i]);
                i = Objects[i].Next;
            }
            return list;
        }

        /// <summary>Floor height in tiles at a point inside a tile (fx, fy 0-1).</summary>
        public double FloorAt(int x, int y, double fx, double fy)
        {
            var t = Tiles[x, y];
            double h = t.Height;
            switch (t.Type)
            {
                case TileType.SlopeUpN: h += fy; break;
                case TileType.SlopeUpS: h += 1 - fy; break;
                case TileType.SlopeUpE: h += fx; break;
                case TileType.SlopeUpW: h += 1 - fx; break;
            }
            return h * LevelGeometry.Step;
        }

        /// <summary>The open tile nearest the middle of the level's open tiles, for a level the
        /// camera has no other place to start in; (32, 32) for an empty level.</summary>
        public void Centre(out int x, out int y)
        {
            double sx = 0, sy = 0; int n = 0;
            for (int ty = 0; ty < Size; ty++)
                for (int tx = 0; tx < Size; tx++)
                    if (!Tiles[tx, ty].IsSolid) { sx += tx + 0.5; sy += ty + 0.5; n++; }
            x = y = 32;
            if (n == 0) return;
            double cx = sx / n, cy = sy / n, best = double.MaxValue;
            for (int ty = 0; ty < Size; ty++)
                for (int tx = 0; tx < Size; tx++)
                {
                    if (Tiles[tx, ty].IsSolid) continue;
                    double d = (tx + 0.5 - cx) * (tx + 0.5 - cx) + (ty + 0.5 - cy) * (ty + 0.5 - cy);
                    if (d < best) { best = d; x = tx; y = ty; }
                }
        }

        public int OpenTileCount
        {
            get { int n = 0; foreach (var t in Tiles) if (!t.IsSolid) n++; return n; }
        }

        public int UsedObjectCount
        {
            get { int n = 0; foreach (var o in Objects) if (o.TileX >= 0) n++; return n; }
        }
    }
}
