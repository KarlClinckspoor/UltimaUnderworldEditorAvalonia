using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UltimateEditor.Games;
using UwEditor.Core.Editing;
using UwEditor.Core.Graphics;

namespace UwEditor.Core.Export
{
    public static partial class ArxExport
    {
        /// <summary>
        /// textures/walls, floors and doors: every texture of W64.TR, F32.TR and DOORS.GR as an RGBA
        /// PNG at its own size, with textures.json (terrain, palette cycling, transparency) and the
        /// palette. Walls and floors are drawn through palette 0 with no transparent colour
        /// (GameGraphics.TextureRgba); DOORS.GR pictures as the game draws .GR pictures, palette
        /// index 0 transparent (GameGraphics.ToRgba).
        /// </summary>
        static void WriteTextures(Ctx c)
        {
            var g = c.S.Graphics;
            var cycles = PaletteCycles.InGame(false);
            var list = new List<object>();
            for (int n = 0; n < g.WallTextures.Count; n++)
                list.Add(Texture(c, "walls", n, g.WallEdge, g.WallEdge, Ints(g.WallTextures[n]), false, cycles));
            for (int n = 0; n < g.FloorTextures.Count; n++)
                list.Add(Texture(c, "floors", n, g.FloorEdge, g.FloorEdge, Ints(g.FloorTextures[n]), false, cycles));
            var doors = ReadGr(c, "DOORS.GR");
            for (int n = 0; n < doors.Count; n++)
                if (doors[n] != null) list.Add(Texture(c, "doors", n, doors[n].Width, doors[n].Height, doors[n].Pixels, true, cycles));
            var palette = new List<object>();
            foreach (uint p in g.Palette) palette.Add(new List<object> { (int)(p >> 16 & 0xFF), (int)(p >> 8 & 0xFF), (int)(p & 0xFF) });
            var cycleList = cycles.Select(cy => (object)new JLine(new JObj().Add("first", cy.First).Add("count", cy.Count)
                .Add("direction", cy.Up ? "up: each entry takes the colour of the one before it" : "down: each entry takes the colour of the one after it")
                .Add("stepSeconds", cy.Period))).ToList();
            var doc = new JObj()
                .Add("palette", palette)
                .Add("paletteSource", "PALS.DAT palette 0 (the 3D view's), 6-bit values scaled as value * 255 / 63 rounded down (GameGraphics.Load)")
                .Add("paletteCycles", cycleList)
                .Add("paletteCyclesSource", "Graphics/PaletteCycles.cs InGame: gfx/COLCYCLE.C cycle_colors, rotate_bank; one step every 64 ticks of the 1/256 s clock")
                .Add("textures", list);
            WriteText(c, "textures/textures.json", ArxJson.Write(doc));
        }

        static List<int> Ints(byte[] b) { return b.Select(x => (int)x).ToList(); }

        /// <summary>One texture: its PNG (and its palette-cycle frames) and its entry in textures.json.</summary>
        static JLine Texture(Ctx c, string set, int number, int w, int h, IList<int> idx, bool zeroTransparent, IReadOnlyList<PaletteCycle> cycles)
        {
            var pal = c.S.Graphics.Palette;
            string name = number.ToString("D3");
            string file = "textures/" + set + "/" + name + ".png";
            bool transparent = zeroTransparent && idx.Take(w * h).Any(i => i == 0);
            WriteBytes(c, file, Png.EncodeRgba(w, h, Rgba(idx, w, h, pal, zeroTransparent, null, 0)));
            HashSet<int> written;
            if (!c.Textures.TryGetValue(set, out written)) c.Textures[set] = written = new HashSet<int>();
            written.Add(number);

            // the cycles whose entries the picture uses: their counts' least common multiple is how many different pictures it shows
            var used = cycles.Where(cy => idx.Take(w * h).Any(i => cy.Contains(i) && !(zeroTransparent && i == 0))).ToList();
            object animated = false;
            if (used.Count > 0)
            {
                int frames = 1;
                foreach (var cy in used) frames = Lcm(frames, cy.Count);
                var files = new List<object> { file };
                for (int k = 1; k < frames; k++)
                {
                    string f = "textures/" + set + "/" + name + "_f" + k + ".png";
                    WriteBytes(c, f, Png.EncodeRgba(w, h, Rgba(idx, w, h, pal, zeroTransparent, used, k)));
                    files.Add(f);
                }
                int cycled = idx.Take(w * h).Count(i => used.Exists(cy => cy.Contains(i)) && !(zeroTransparent && i == 0));
                animated = new JObj().Add("frames", frames).Add("frameSeconds", used[0].Period).Add("cycledPixels", cycled).Add("files", files)
                    .Add("paletteRanges", used.Select(cy => (object)string.Format("0x{0:X2}-0x{1:X2}", cy.First, cy.Last)).ToList());
            }
            var e = new JObj().Add("set", set).Add("number", number).Add("file", file).Add("width", w).Add("height", h);
            AddTerrain(c, e, set, number);
            e.Add("animated", animated);
            e.Add("transparent", transparent);
            e.Add("transparentIndex", zeroTransparent ? (object)0 : null);
            return new JLine(e);
        }

        /// <summary>RGBA pixels; with cycles, as they show after steps steps of palette cycling (PaletteCycle.SourceAfter).</summary>
        static byte[] Rgba(IList<int> idx, int w, int h, uint[] pal, bool zeroTransparent, List<PaletteCycle> cycles, long steps)
        {
            var px = new byte[w * h * 4];
            for (int k = 0; k < w * h && k < idx.Count; k++)
            {
                int i = idx[k];
                if (zeroTransparent && i == 0) continue;   // all four bytes 0, as GameGraphics.ToRgba
                if (cycles != null)
                {
                    var cy = PaletteCycles.Find(cycles, i);
                    if (cy != null) i = cy.SourceAfter(i, steps);
                }
                uint p = i >= 0 && i < pal.Length ? pal[i] : 0xFF000000u;
                px[k * 4] = (byte)(p >> 16); px[k * 4 + 1] = (byte)(p >> 8); px[k * 4 + 2] = (byte)p; px[k * 4 + 3] = 0xFF;
            }
            return px;
        }

        static int Lcm(int a, int b) { int x = a, y = b; while (y != 0) { int t = x % y; x = y; y = t; } return a / x * b; }

        /// <summary>
        /// The texture's TERRAIN.DAT word and what it means (Editing/GameTables.cs TerrainTable): a
        /// wall's type (TerrainTable.Uw1Walls), a floor's class, the word shifted right 4 (0 normal, 1 water,
        /// 2 lava, 3 ice; Randomizer/CreatureTerrain.cs FloorClass). Doors have none.
        /// </summary>
        static void AddTerrain(Ctx c, JObj e, string set, int number)
        {
            if (set == "doors" || c.Terrain == null) { e.Add("terrain", null).Add("terrainWord", null); return; }
            bool floor = set == "floors";
            if (number >= c.Terrain.Textures(floor)) { e.Add("terrain", null).Add("terrainWord", null); return; }
            int word = c.Terrain.Get(c.Terrain.EntryOf(number, floor));
            string name;
            if (floor) name = FloorClassNames[(word >> 4) & 3];
            else
            {
                name = null;
                foreach (var kv in TerrainTable.Uw1Walls) if (kv.Key == word) name = kv.Value;
                name = name ?? string.Format("unknown 0x{0:X2}", word);
            }
            e.Add("terrain", name).Add("terrainWord", word);
        }

        static readonly string[] FloorClassNames = { "normal", "water", "lava", "ice" };

        /// <summary>The pictures of a .GR file of DATA with their palette indices (4-bit ones through ALLPALS.DAT, as GameGraphics.ToRgba); null for an empty one.</summary>
        static List<GrFile.Picture> ReadGr(Ctx c, string name)
        {
            var list = new List<GrFile.Picture>();
            string path = GameProfile.Find(c.S.DataFolder, name);
            if (path == null) return list;
            byte[] file = File.ReadAllBytes(path);
            string ap = GameProfile.Find(c.S.DataFolder, "ALLPALS.DAT");
            byte[] aux = ap != null ? File.ReadAllBytes(ap) : new byte[0];
            int[] o = GrFile.Offsets(file);
            for (int i = 0; i + 1 < o.Length; i++)
            {
                GrFile.Picture p;
                try { p = GrFile.Decode(file, o, i); } catch (FormatException) { p = null; }
                if (p != null && p.Type != 4)
                    for (int k = 0; k < p.Pixels.Length; k++)
                    {
                        int at = (p.Aux & 31) * 16 + (p.Pixels[k] & 15);
                        p.Pixels[k] = at < aux.Length ? aux[at] : 0;
                    }
                list.Add(p);
            }
            return list;
        }

        /// <summary>objects/sprites/NNN.png: OBJECTS.GR's picture of each item, as the 3D view draws it (GameGraphics.ObjectPictures, index 0 transparent).</summary>
        static void WriteSprites(Ctx c)
        {
            var pics = c.S.Graphics.ObjectPictures;
            for (int i = 0; i < pics.Count && i < 512; i++)
            {
                var p = pics[i];
                if (p == null || p.Width == 0 || p.Height == 0) continue;
                WriteBytes(c, "objects/sprites/" + i.ToString("D3") + ".png", Png.EncodeRgba(p.Width, p.Height, p.Pixels));
            }
        }

        /// <summary>strings.json: the strings window's export (StringsEditor.ExportJson), with LF line ends.</summary>
        static void WriteStrings(Ctx c)
        {
            string json = StringsEditor.Open(c.S).ExportJson().Replace("\r\n", "\n");
            if (!json.EndsWith("\n")) json += "\n";
            WriteText(c, "strings.json", json);
        }

        /// <summary>
        /// objects/classes.json: for each item 0-511 its name, category (ItemCategories), how the 3D
        /// view draws it (ObjectModels.Classify), and every field of each object table that covers it
        /// (Editing/ObjectTables.cs Layout: OBJECTS.DAT's class tables and COMOBJ.DAT), under the
        /// editor's field names in camelCase.
        /// </summary>
        static void WriteClasses(Ctx c)
        {
            var s = c.S;
            var items = new List<object>();
            for (int item = 0; item < 512; item++)
            {
                int model;
                var drawn = ObjectModels.Classify(item, s.RenderTypes, false, out model);
                var tables = new JObj();
                foreach (var t in ObjectTables.Layout)
                {
                    if (item < t.FirstItem || item >= t.FirstItem + t.Count) continue;
                    tables.Add(ArxJson.Key(t.Name), Fields(t.Schema, c.Tables.Bytes(t), t.OffsetOf(item - t.FirstItem)));
                }
                var cat = ItemCategories.Of(item, GameKind.UW1);
                items.Add(new JLine(new JObj()
                    .Add("item", item)
                    .Add("hex", "0x" + item.ToString("X3"))
                    .Add("name", s.Names.Get(item))
                    .Add("category", cat.ToString())
                    .Add("drawnAs", drawn.ToString())
                    .Add("model", model >= 0 ? (object)model : null)
                    .Add("sprite", s.Graphics.ObjectPictures.Count > item && s.Graphics.ObjectPictures[item] != null ? "objects/sprites/" + item.ToString("D3") + ".png" : null)
                    .Add("tables", tables)));
            }
            var doc = new JObj()
                .Add("categories", "Editing: ItemCategories.Of (include/items.h class ranges): " + string.Join(", ", Enum.GetValues(typeof(ItemCategory)).Cast<ItemCategory>().Where(x => x != ItemCategory.None && IsSingleBit((long)x)).Select(x => x.ToString())))
                .Add("drawnAs", "ObjectModels.Classify (3d/DRAWOBJ.C): Sprite, Critter, Model (a built-in 3D model), Door, TmapLever, TmapWall, None (traps and triggers, not drawn)")
                .Add("tablesSource", "Editing/ObjectTables.cs Layout: OBJECTS.DAT class tables and COMOBJ.DAT, field names from Uw1Schemas / ObjectTables in camelCase")
                .Add("classes", items);
            WriteText(c, "objects/classes.json", ArxJson.Write(doc));
        }

        static bool IsSingleBit(long v) { return v != 0 && (v & (v - 1)) == 0; }

        /// <summary>Every field of a record, by its camelCase name.</summary>
        static JObj Fields(UltimateEditor.Uw1.RecordSchema schema, byte[] data, int at)
        {
            var o = new JObj();
            foreach (var f in schema.Fields)
            {
                string key = ArxJson.Key(f.Name);
                if (o.Exists(kv => kv.Key == key)) throw new InvalidOperationException("Two fields of " + schema.Name + " give the key " + key + ".");
                o.Add(key, at + f.Offset + f.ByteSpan <= data.Length ? (object)f.Get(data, at) : null);
            }
            return o;
        }
    }
}
