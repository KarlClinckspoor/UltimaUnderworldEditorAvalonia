using System;
using System.Collections.Generic;
using System.Linq;

namespace UwEditor.Core.Export
{
    public static partial class ArxExport
    {
        static readonly string[] EdgeNames = { "W", "E", "S", "N", "diagonal" };
        static readonly string[] KindNames = { "floor", "wall", "ceiling" };

        /// <summary>levels/levelN.json: the level's tiles, texture map, mesh and summary (stage 1), and its objects and their readings (stage 2).</summary>
        static JObj LevelDocument(Ctx c, LevelModel m)
        {
            var mesh = LevelGeometry.Build(m, true);
            var tiles = Tiles(m);
            var meshDoc = Mesh(c, m, mesh, out int unresolved, out int[] perKind);
            var doc = new JObj()
                .Add("level", m.Level)
                .Add("tiles", tiles)
                .Add("textureMap", TextureMap(m))
                .Add("mesh", meshDoc)
                .Add("summary", Summary(c, m, mesh, perKind, unresolved));
            AddObjects(c, m, doc);
            return doc;
        }

        /// <summary>All 4096 tiles, y then x, as read (LevelModel.ReadTiles) with the texture numbers their indexes resolve to.</summary>
        static List<object> Tiles(LevelModel m)
        {
            var list = new List<object>();
            for (int y = 0; y < LevelModel.Size; y++)
                for (int x = 0; x < LevelModel.Size; x++)
                {
                    var t = m.Tiles[x, y];
                    int ft = m.FloorTextureNumber(t.FloorIndex), wt = m.WallTextureNumber(t.WallIndex);
                    list.Add(new JLine(new JObj()
                        .Add("x", t.X).Add("y", t.Y).Add("type", t.Type.ToString()).Add("height", t.Height)
                        .Add("floorIndex", t.FloorIndex).Add("wallIndex", t.WallIndex)
                        .Add("floorTexture", ft >= 0 ? (object)ft : null).Add("wallTexture", wt >= 0 ? (object)wt : null)
                        .Add("light", t.Light).Add("noMagic", t.NoMagic).Add("doorBit", t.DoorBit)
                        .Add("firstObject", t.FirstObject).Add("record", Hex(t.Record))));
                }
            return list;
        }

        static JObj TextureMap(LevelModel m)
        {
            int ci = m.CeilingFloorIndex;
            return new JObj()
                .Add("walls", Ints(m.WallMap))
                .Add("floors", Ints(m.FloorMap))
                .Add("doors", Ints(m.DoorMap))
                .Add("ceilingFloorIndex", ci)
                .Add("ceilingTexture", m.FloorTextureNumber(ci) >= 0 ? (object)m.FloorTextureNumber(ci) : null)
                .Add("raw", m.TextureBytes == null ? null : Hex(m.TextureBytes));
        }

        /// <summary>
        /// The surfaces as <see cref="LevelGeometry.Build"/> makes them for the 3D view (with the
        /// ceiling), not welded: each triangle has its own three vertices, numbered 3i to 3i + 2. The
        /// triangles go tile by tile, y then x, and within a tile in the order Build makes them. The
        /// texture is resolved from the vertex's layer through the level's maps: a floor or ceiling
        /// layer is WallLayers + the floor index (FloorMap), a wall layer the wall index (WallMap),
        /// and the 'missing' layer of an index the map lacks resolves to null.
        /// </summary>
        static JObj Mesh(Ctx c, LevelModel m, LevelMesh mesh, out int unresolved, out int[] perKind)
        {
            unresolved = 0;
            perKind = new int[3];
            int n = mesh.TriangleCount;
            // TriangleTile is x + 64 y, so its order is y then x; Build goes x then y
            var order = Enumerable.Range(0, n).OrderBy(i => mesh.TriangleTile[i]).ThenBy(i => i).ToList();
            var vertices = new List<object>(n * 3);
            var triangles = new List<object>(n);
            foreach (int i in order)
            {
                int k = triangles.Count;
                int kind = mesh.TriangleKind[i];
                int tile = mesh.TriangleTile[i];
                // Build puts a wall's corners in the order of its edge (west to east, south to north), so the
                // walls on the E and S edges, and the diagonal walls of OpenNE and OpenNW tiles, are wound
                // the other way round from the floors and the other walls; the 3D view draws both sides
                // (GlRenderer turns face culling off), so it never shows. The export turns those round,
                // so that every triangle's front faces the open space.
                var tt = m.Tiles[tile & 63, tile >> 6].Type;
                int edge = mesh.TriangleEdge[i];
                bool turn = kind == 1 && (edge == 1 || edge == 2 || (edge == 4 && (tt == TileType.OpenNE || tt == TileType.OpenNW)));
                foreach (int j in turn ? new[] { 0, 2, 1 } : new[] { 0, 1, 2 })
                {
                    var v = mesh.Vertices[3 * i + j];
                    vertices.Add(new List<object> { v.X, v.Y, v.Z, v.U, v.V });
                }
                perKind[kind]++;
                int layer = (int)mesh.Vertices[3 * i].Layer;
                JObj tex = null;
                if (kind == 1)
                {
                    int number = layer < mesh.WallLayers ? m.WallTextureNumber(layer) : -1;
                    if (number >= 0 && Has(c, "walls", number)) tex = new JObj().Add("set", "walls").Add("number", number);
                }
                else
                {
                    int index = layer - mesh.WallLayers;
                    int number = index >= 0 && index < LevelGeometry.FloorLayers ? m.FloorTextureNumber(index) : -1;
                    if (number >= 0 && Has(c, "floors", number)) tex = new JObj().Add("set", "floors").Add("number", number);
                }
                if (tex == null) unresolved++;
                var t = new JObj()
                    .Add("v", new List<object> { 3 * k, 3 * k + 1, 3 * k + 2 })
                    .Add("tile", new List<object> { tile & 63, tile >> 6 })
                    .Add("kind", KindNames[kind])
                    .Add("texture", tex);
                if (kind == 1) t.Add("edge", EdgeNames[mesh.TriangleEdge[i]]);
                triangles.Add(new JLine(t));
            }
            return new JObj()
                .Add("vertexFields", new List<object> { "x", "y", "z", "u", "v" })
                .Add("vertices", vertices.Select(v => (object)new JLine(v)).ToList())
                .Add("triangles", triangles);
        }

        static bool Has(Ctx c, string set, int number)
        {
            HashSet<int> h;
            return c.Textures.TryGetValue(set, out h) && h.Contains(number);
        }

        /// <summary>Counts to check a reading against: tiles by type, triangles by kind, floor heights of the open tiles.</summary>
        static JObj Summary(Ctx c, LevelModel m, LevelMesh mesh, int[] perKind, int unresolved)
        {
            var byType = new JObj();
            foreach (TileType tt in Enum.GetValues(typeof(TileType)))
            {
                int k = 0;
                foreach (var t in m.Tiles) if (t.Type == tt) k++;
                byType.Add(tt.ToString(), k);
            }
            var open = m.Tiles.Cast<TileInfo>().Where(t => !t.IsSolid).ToList();
            int objects = m.Objects.Count(o => !o.Free);
            return new JObj()
                .Add("openTiles", open.Count)
                .Add("solidTiles", 4096 - open.Count)
                .Add("tilesByType", byType)
                .Add("triangles", new JObj().Add("floor", perKind[0]).Add("wall", perKind[1]).Add("ceiling", perKind[2]).Add("total", mesh.TriangleCount))
                .Add("vertices", mesh.TriangleCount * 3)
                .Add("trianglesWithoutTexture", unresolved)
                .Add("lowestFloor", open.Count == 0 ? null : (object)open.Min(t => t.Height))
                .Add("highestFloor", open.Count == 0 ? null : (object)open.Max(t => t.Height))
                .Add("objects", objects)
                .Add("mobileObjects", m.Objects.Count(o => !o.Free && o.IsMobile))
                .Add("staticObjects", m.Objects.Count(o => !o.Free && !o.IsMobile));
        }
    }
}
