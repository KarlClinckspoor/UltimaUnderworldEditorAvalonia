using System;
using System.Collections.Generic;
using System.Numerics;

namespace UwEditor.Core
{
    /// <summary>A vertex of the level mesh: position (x east, y up, z north, in tiles), texture
    /// coordinates (repeating), texture layer and a brightness.</summary>
    public struct LevelVertex
    {
        public float X, Y, Z, U, V, Layer, Shade, Tile;
        public const int Floats = 8;

        public LevelVertex(Vector3 p, float u, float v, float layer, float shade)
        {
            X = p.X; Y = p.Y; Z = p.Z; U = u; V = v; Layer = layer; Shade = shade; Tile = 0;
        }

        public Vector3 Position { get { return new Vector3(X, Y, Z); } }
    }

    /// <summary>The level's surfaces as triangles, with the tile each triangle belongs to.</summary>
    public sealed class LevelMesh
    {
        public readonly List<LevelVertex> Vertices = new List<LevelVertex>();
        /// <summary>For each triangle (three vertices in a row), the tile x + 64 y.</summary>
        public readonly List<int> TriangleTile = new List<int>();
        /// <summary>For each triangle, what it is: 0 floor, 1 wall, 2 ceiling.</summary>
        public readonly List<byte> TriangleKind = new List<byte>();
        public int WallLayers;
        /// <summary>The layer after the walls and the 16 floor slots: a checkerboard for indexes the texture map lacks.</summary>
        public int MissingLayer { get { return WallLayers + LevelGeometry.FloorLayers; } }

        public int TriangleCount { get { return TriangleTile.Count; } }

        public void Tri(LevelVertex a, LevelVertex b, LevelVertex c, int tile, byte kind)
        {
            a.Tile = b.Tile = c.Tile = tile;
            Vertices.Add(a); Vertices.Add(b); Vertices.Add(c);
            TriangleTile.Add(tile);
            TriangleKind.Add(kind);
        }

        public float[] ToArray()
        {
            var f = new float[Vertices.Count * LevelVertex.Floats];
            int k = 0;
            foreach (var v in Vertices)
            {
                f[k++] = v.X; f[k++] = v.Y; f[k++] = v.Z; f[k++] = v.U; f[k++] = v.V; f[k++] = v.Layer; f[k++] = v.Shade; f[k++] = v.Tile;
            }
            return f;
        }
    }

    /// <summary>
    /// The level as the game builds it, in tiles: x east, z north, y up (port of the Unity
    /// editor's WalkGeometry). A floor step is a quarter of a tile (the renderer's 0x40 units
    /// against 0x100 a tile) and the ceiling is height 16 (3d/GRIDDB.C grdb_elem). Slopes rise
    /// one step across the tile; a diagonal tile is open in the half its name gives, with a
    /// wall along the diagonal. A wall is drawn on the open side: up to the neighbour's floor,
    /// or to the ceiling against rock.
    /// Texture layers: wall index n is layer n, floor index n is layer <see cref="LevelMesh.WallLayers"/> + n,
    /// then one 'missing' layer.
    /// </summary>
    public static class LevelGeometry
    {
        public const float Step = 0.25f;
        public const int CeilingHeight = 16;
        public const float Ceiling = CeilingHeight * Step;
        public const int FloorLayers = 16;

        // corners: 0 SW, 1 SE, 2 NE, 3 NW
        static readonly Vector2[] CornerXY = { new Vector2(0, 0), new Vector2(1, 0), new Vector2(1, 1), new Vector2(0, 1) };

        public static int[] CornerHeights(TileInfo t)
        {
            int h = t.Height;
            switch (t.Type)
            {
                case TileType.SlopeUpN: return new[] { h, h, h + 1, h + 1 };
                case TileType.SlopeUpS: return new[] { h + 1, h + 1, h, h };
                case TileType.SlopeUpE: return new[] { h, h + 1, h + 1, h };
                case TileType.SlopeUpW: return new[] { h + 1, h, h, h + 1 };
                default: return new[] { h, h, h, h };
            }
        }

        // edges: W, E, S, N as (corner a, corner b, dx, dy, neighbour's corner for a, for b)
        static readonly int[][] Edges =
        {
            new[] { 0, 3, -1, 0, 1, 2 },
            new[] { 1, 2, 1, 0, 0, 3 },
            new[] { 0, 1, 0, -1, 3, 2 },
            new[] { 3, 2, 0, 1, 0, 1 },
        };

        /// <summary>Whether the tile is open along edge e (0 W, 1 E, 2 S, 3 N).</summary>
        public static bool EdgeOpen(TileType type, int e)
        {
            switch (type)
            {
                case TileType.Solid: return false;
                case TileType.OpenSE: return e == 1 || e == 2;
                case TileType.OpenSW: return e == 0 || e == 2;
                case TileType.OpenNE: return e == 1 || e == 3;
                case TileType.OpenNW: return e == 0 || e == 3;
                default: return true;
            }
        }

        public static int[] FloorCorners(TileType type)
        {
            switch (type)
            {
                case TileType.OpenSE: return new[] { 0, 1, 2 };
                case TileType.OpenSW: return new[] { 0, 1, 3 };
                case TileType.OpenNE: return new[] { 3, 1, 2 };
                case TileType.OpenNW: return new[] { 0, 2, 3 };
                default: return new[] { 0, 1, 2, 3 };
            }
        }

        static Vector3 P(int x, int y, int corner, float height)
        {
            var c = CornerXY[corner];
            return new Vector3(x + c.X, height, y + c.Y);
        }

        /// <summary>Ceilings carry a negative shade, so the shader can leave them out when the eye is above them.</summary>
        const float FloorShade = 1f, CeilShade = -0.7f, WallNS = 1f, WallEW = 0.8f, WallDiag = 0.9f;

        public static LevelMesh Build(LevelModel level, bool ceiling)
        {
            var mesh = new LevelMesh { WallLayers = level.WallMap.Length };
            var tiles = level.Tiles;
            int w = LevelModel.Size, d = LevelModel.Size;
            ceiling &= level.HasCeiling;
            int ceilLayer = mesh.WallLayers + Math.Min(FloorLayers - 1, level.CeilingFloorIndex);
            for (int x = 0; x < w; x++)
                for (int y = 0; y < d; y++)
                {
                    TileInfo t = tiles[x, y];
                    if (t.Type == TileType.Solid) continue;
                    int tile = x + 64 * y;
                    int[] h = CornerHeights(t);
                    int floorLayer = mesh.WallLayers + Math.Min(FloorLayers - 1, t.FloorIndex);
                    int[] poly = FloorCorners(t.Type);
                    for (int i = 1; i + 1 < poly.Length; i++)
                    {
                        int a = poly[0], b = poly[i], c = poly[i + 1];
                        mesh.Tri(FloorV(x, y, a, h[a] * Step, floorLayer, FloorShade), FloorV(x, y, b, h[b] * Step, floorLayer, FloorShade),
                            FloorV(x, y, c, h[c] * Step, floorLayer, FloorShade), tile, 0);
                        if (ceiling)
                            mesh.Tri(FloorV(x, y, a, Ceiling, ceilLayer, CeilShade), FloorV(x, y, c, Ceiling, ceilLayer, CeilShade),
                                FloorV(x, y, b, Ceiling, ceilLayer, CeilShade), tile, 2);
                    }
                    // a wall index past the level's list (UW1 has 48, the field 6 bits) gets the 'missing' layer
                    int wallLayer = t.WallIndex < mesh.WallLayers ? t.WallIndex : mesh.MissingLayer;
                    // the diagonal wall
                    int da = -1, db = -1;
                    if (t.Type == TileType.OpenSE || t.Type == TileType.OpenNW) { da = 0; db = 2; }
                    else if (t.Type == TileType.OpenSW || t.Type == TileType.OpenNE) { da = 3; db = 1; }
                    if (da >= 0)
                        WallQuad(mesh, tile, x, y, da, db, h[da] * Step, h[db] * Step, Ceiling, Ceiling, 0f, 1f, wallLayer, WallDiag);
                    for (int e = 0; e < 4; e++)
                    {
                        if (!EdgeOpen(t.Type, e)) continue;
                        var ed = Edges[e];
                        int a = ed[0], b = ed[1], nx = x + ed[2], ny = y + ed[3];
                        float botA = h[a] * Step, botB = h[b] * Step, topA, topB;
                        TileInfo n = nx >= 0 && ny >= 0 && nx < w && ny < d ? tiles[nx, ny] : null;
                        int opposite = e ^ 1;
                        if (n == null || !EdgeOpen(n.Type, opposite))
                            topA = topB = Ceiling;
                        else
                        {
                            int[] nh = CornerHeights(n);
                            topA = Math.Max(botA, nh[ed[4]] * Step);
                            topB = Math.Max(botB, nh[ed[5]] * Step);
                            if (topA <= botA && topB <= botB) continue;
                        }
                        // texture u runs with the world axis along the edge, so walls line up
                        float u0 = e < 2 ? y + CornerXY[a].Y : x + CornerXY[a].X;
                        float u1 = e < 2 ? y + CornerXY[b].Y : x + CornerXY[b].X;
                        WallQuad(mesh, tile, x, y, a, b, botA, botB, topA, topB, u0, u1, wallLayer, e < 2 ? WallEW : WallNS);
                    }
                }
            return mesh;
        }

        /// <summary>A floor or ceiling vertex: the texture's top row to the north.</summary>
        static LevelVertex FloorV(int x, int y, int corner, float height, int layer, float shade)
        {
            var c = CornerXY[corner];
            return new LevelVertex(P(x, y, corner, height), x + c.X, -(y + c.Y), layer, shade);
        }

        /// <summary>
        /// A wall from corner a to corner b, bottom to top at each end (in tiles). Its texture is
        /// one tile high, top row up, and starts at the face's top edge as the game's does (3d/GRIDDB.C
        /// txtwal and 3d/TMAPOPS.ASM do_compact_tmap: v is 0 at both top corners and the face's
        /// height at both bottom ones), so a face that ends on a floor not a whole tile below its
        /// top shows the texture cut at the bottom. A sloped face takes its two ends' mean height.
        /// </summary>
        static void WallQuad(LevelMesh m, int tile, int x, int y, int a, int b, float botA, float botB, float topA, float topB, float u0, float u1, int layer, float shade)
        {
            float h = ((topA - botA) + (topB - botB)) / 2;
            var v0 = new LevelVertex(P(x, y, a, botA), u0, h, layer, shade);
            var v1 = new LevelVertex(P(x, y, b, botB), u1, h, layer, shade);
            var v2 = new LevelVertex(P(x, y, b, topB), u1, 0, layer, shade);
            var v3 = new LevelVertex(P(x, y, a, topA), u0, 0, layer, shade);
            m.Tri(v0, v1, v2, tile, 1);
            m.Tri(v0, v2, v3, tile, 1);
        }
    }
}
