using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Numerics;
using System.Security.Cryptography;
using System.Text.Json;
using UwEditor.Core;
using UwEditor.Core.Export;
using Xunit;
using Xunit.Abstractions;

namespace UwEditor.Core.Tests
{
    /// <summary>Two exports of UW1's level 1 into temporary folders, shared by the tests and removed after them.</summary>
    public sealed class ArxLevel1Fixture : IDisposable
    {
        public readonly GameSession Session;
        public readonly string First, Second;
        public readonly ArxExportResult Result;

        public ArxLevel1Fixture()
        {
            string g = GameData.Game("UW");
            if (g == null) return;
            Session = GameSession.Open(g);
            First = Path.Combine(Path.GetTempPath(), "uwarx-" + Guid.NewGuid().ToString("N"));
            Second = Path.Combine(Path.GetTempPath(), "uwarx-" + Guid.NewGuid().ToString("N"));
            Result = ArxExport.Run(Session, First, 1);
            ArxExport.Run(GameSession.Open(g), Second, 1);
        }

        public void Dispose()
        {
            foreach (var d in new[] { First, Second })
                if (d != null && Directory.Exists(d)) Directory.Delete(d, true);
        }
    }

    /// <summary>The ArxStructor export (Export/ArxExport, docs/ARX-EXPORT.md) of UW1's level 1, and a run over all nine levels.</summary>
    public class ArxExportTests : IClassFixture<ArxLevel1Fixture>
    {
        readonly ArxLevel1Fixture fx;
        readonly ITestOutputHelper output;

        public ArxExportTests(ArxLevel1Fixture fx, ITestOutputHelper output) { this.fx = fx; this.output = output; }

        JsonDocument Level1() { return JsonDocument.Parse(File.ReadAllBytes(Path.Combine(fx.First, "levels", "level1.json"))); }

        static List<string> FilesUnder(string root)
        {
            return Directory.GetFiles(root, "*", SearchOption.AllDirectories)
                .Select(f => Path.GetRelativePath(root, f).Replace('\\', '/')).OrderBy(f => f, StringComparer.Ordinal).ToList();
        }

        [Fact]
        public void Two_exports_of_level_1_are_byte_for_byte_the_same()
        {
            if (fx.Session == null) return;
            var a = FilesUnder(fx.First);
            var b = FilesUnder(fx.Second);
            Assert.Equal(a, b);
            Assert.Contains("manifest.json", a);
            Assert.Contains("levels/level1.json", a);
            Assert.Contains("textures/textures.json", a);
            Assert.Contains("objects/classes.json", a);
            Assert.Contains("strings.json", a);
            foreach (var f in a)
                Assert.True(File.ReadAllBytes(Path.Combine(fx.First, f)).AsSpan().SequenceEqual(File.ReadAllBytes(Path.Combine(fx.Second, f))), f + " differs between two runs");
            // UTF-8 without a byte order mark and LF only
            foreach (var f in a.Where(x => x.EndsWith(".json")))
            {
                var bytes = File.ReadAllBytes(Path.Combine(fx.First, f));
                Assert.False(bytes.Length >= 3 && bytes[0] == 0xEF && bytes[1] == 0xBB && bytes[2] == 0xBF, f + " starts with a byte order mark");
                Assert.DoesNotContain((byte)'\r', bytes);
            }
        }

        [Fact]
        public void Level_1_has_4096_tiles_ordered_by_y_then_x()
        {
            if (fx.Session == null) return;
            using var doc = Level1();
            var tiles = doc.RootElement.GetProperty("tiles").EnumerateArray().ToList();
            Assert.Equal(4096, tiles.Count);
            var m = fx.Session.LoadLevel(1);
            for (int i = 0; i < tiles.Count; i++)
            {
                int x = tiles[i].GetProperty("x").GetInt32(), y = tiles[i].GetProperty("y").GetInt32();
                Assert.Equal(i % 64, x);
                Assert.Equal(i / 64, y);
                var t = m.Tiles[x, y];
                Assert.Equal(t.Type.ToString(), tiles[i].GetProperty("type").GetString());
                Assert.Equal(t.Height, tiles[i].GetProperty("height").GetInt32());
                Assert.Equal(Convert.ToHexString(t.Record).ToLowerInvariant(), tiles[i].GetProperty("record").GetString());
            }
        }

        [Fact]
        public void Summary_agrees_with_a_recount_of_tiles_and_mesh()
        {
            if (fx.Session == null) return;
            using var doc = Level1();
            var root = doc.RootElement;
            var tiles = root.GetProperty("tiles").EnumerateArray().ToList();
            var tris = root.GetProperty("mesh").GetProperty("triangles").EnumerateArray().ToList();
            var sum = root.GetProperty("summary");
            var open = tiles.Where(t => t.GetProperty("type").GetString() != "Solid").ToList();
            Assert.Equal(open.Count, sum.GetProperty("openTiles").GetInt32());
            Assert.Equal(4096 - open.Count, sum.GetProperty("solidTiles").GetInt32());
            foreach (var p in sum.GetProperty("tilesByType").EnumerateObject())
                Assert.Equal(tiles.Count(t => t.GetProperty("type").GetString() == p.Name), p.Value.GetInt32());
            var tc = sum.GetProperty("triangles");
            foreach (var kind in new[] { "floor", "wall", "ceiling" })
                Assert.Equal(tris.Count(t => t.GetProperty("kind").GetString() == kind), tc.GetProperty(kind).GetInt32());
            Assert.Equal(tris.Count, tc.GetProperty("total").GetInt32());
            Assert.Equal(tris.Count * 3, root.GetProperty("mesh").GetProperty("vertices").GetArrayLength());
            Assert.Equal(open.Min(t => t.GetProperty("height").GetInt32()), sum.GetProperty("lowestFloor").GetInt32());
            Assert.Equal(open.Max(t => t.GetProperty("height").GetInt32()), sum.GetProperty("highestFloor").GetInt32());
            // the same mesh the 3D view draws
            var mesh = LevelGeometry.Build(fx.Session.LoadLevel(1), true);
            Assert.Equal(mesh.TriangleCount, tris.Count);
            output.WriteLine("level 1: {0} open tiles, {1} triangles ({2} floor, {3} wall, {4} ceiling)", open.Count, tris.Count,
                tc.GetProperty("floor").GetInt32(), tc.GetProperty("wall").GetInt32(), tc.GetProperty("ceiling").GetInt32());
        }

        [Fact]
        public void Every_triangle_has_its_tile_kind_edge_and_a_texture_that_was_written()
        {
            if (fx.Session == null) return;
            using var doc = Level1();
            var root = doc.RootElement;
            var tris = root.GetProperty("mesh").GetProperty("triangles").EnumerateArray().ToList();
            Assert.NotEmpty(tris);
            int prevTile = -1;
            for (int i = 0; i < tris.Count; i++)
            {
                var t = tris[i];
                var v = t.GetProperty("v").EnumerateArray().Select(x => x.GetInt32()).ToArray();
                Assert.Equal(new[] { 3 * i, 3 * i + 1, 3 * i + 2 }, v);
                var tile = t.GetProperty("tile").EnumerateArray().Select(x => x.GetInt32()).ToArray();
                Assert.InRange(tile[0], 0, 63);
                Assert.InRange(tile[1], 0, 63);
                int key = tile[1] * 64 + tile[0];
                Assert.True(key >= prevTile, "triangles go tile by tile, y then x");
                prevTile = key;
                string kind = t.GetProperty("kind").GetString();
                Assert.Contains(kind, new[] { "floor", "wall", "ceiling" });
                if (kind == "wall") Assert.Contains(t.GetProperty("edge").GetString(), new[] { "W", "E", "S", "N", "diagonal" });
                else Assert.False(t.TryGetProperty("edge", out _));
                var tex = t.GetProperty("texture");
                Assert.Equal(JsonValueKind.Object, tex.ValueKind);
                string set = tex.GetProperty("set").GetString();
                Assert.Equal(kind == "wall" ? "walls" : "floors", set);
                string png = Path.Combine(fx.First, "textures", set, tex.GetProperty("number").GetInt32().ToString("D3") + ".png");
                Assert.True(File.Exists(png), png);
            }
            Assert.Equal(0, root.GetProperty("summary").GetProperty("trianglesWithoutTexture").GetInt32());
        }

        [Fact]
        public void Winding_faces_the_open_space_floors_up_ceilings_down_walls_into_their_tile()
        {
            if (fx.Session == null) return;
            using var doc = Level1();
            var root = doc.RootElement;
            var verts = root.GetProperty("mesh").GetProperty("vertices").EnumerateArray()
                .Select(v => { var a = v.EnumerateArray().Select(x => x.GetSingle()).ToArray(); return new Vector3(a[0], a[1], a[2]); }).ToList();
            var types = root.GetProperty("tiles").EnumerateArray().Select(t => t.GetProperty("type").GetString()).ToList();
            int checkedWalls = 0, degenerate = 0;
            foreach (var t in root.GetProperty("mesh").GetProperty("triangles").EnumerateArray())
            {
                var v = t.GetProperty("v").EnumerateArray().Select(x => verts[x.GetInt32()]).ToArray();
                // the export's rule: cross(v1 - v0, v2 - v0) points away from the open space, so the front is its negation
                var front = -Vector3.Cross(v[1] - v[0], v[2] - v[0]);
                if (front.LengthSquared() < 1e-10f) { degenerate++; continue; }
                var tile = t.GetProperty("tile").EnumerateArray().Select(x => x.GetInt32()).ToArray();
                string kind = t.GetProperty("kind").GetString();
                if (kind == "floor") { Assert.True(front.Y > 0, "floor faces up"); continue; }
                if (kind == "ceiling") { Assert.True(front.Y < 0, "ceiling faces down"); continue; }
                Vector3 into;
                switch (t.GetProperty("edge").GetString())
                {
                    case "W": into = new Vector3(1, 0, 0); Assert.All(v, p => Assert.Equal(tile[0], p.X)); break;
                    case "E": into = new Vector3(-1, 0, 0); Assert.All(v, p => Assert.Equal(tile[0] + 1, p.X)); break;
                    case "S": into = new Vector3(0, 0, 1); Assert.All(v, p => Assert.Equal(tile[1], p.Z)); break;
                    case "N": into = new Vector3(0, 0, -1); Assert.All(v, p => Assert.Equal(tile[1] + 1, p.Z)); break;
                    default:
                        // towards the open half the diagonal tile's name gives (x east, z north)
                        string type = types[tile[1] * 64 + tile[0]];
                        into = type == "OpenSE" ? new Vector3(1, 0, -1) : type == "OpenSW" ? new Vector3(-1, 0, -1) : type == "OpenNE" ? new Vector3(1, 0, 1) : new Vector3(-1, 0, 1);
                        Assert.StartsWith("Open", type);
                        Assert.NotEqual("Open", type);
                        break;
                }
                Assert.True(Vector3.Dot(front, into) > 0, string.Format("the {0} wall of ({1}, {2}), {3}, faces its open tile", t.GetProperty("edge").GetString(), tile[0], tile[1], types[tile[1] * 64 + tile[0]]));
                Assert.True(Math.Abs(front.Y) < 1e-4f, "walls stand upright");
                checkedWalls++;
            }
            Assert.True(checkedWalls > 1000);
            output.WriteLine("{0} walls checked, {1} triangles of no area skipped", checkedWalls, degenerate);
        }

        [Fact]
        public void Objects_leave_out_the_free_ones_and_children_and_parents_agree()
        {
            if (fx.Session == null) return;
            using var doc = Level1();
            var m = fx.Session.LoadLevel(1);
            var objs = doc.RootElement.GetProperty("objects").EnumerateArray().ToList();
            Assert.Equal(m.Objects.Count(o => !o.Free), objs.Count);
            var byIndex = objs.ToDictionary(o => o.GetProperty("index").GetInt32());
            foreach (var o in objs)
            {
                int i = o.GetProperty("index").GetInt32();
                Assert.False(m.Objects[i].Free);
                Assert.Equal(m.Objects[i].IsMobile ? 27 : 8, o.GetProperty("record").GetString().Length / 2);
                int parent = o.GetProperty("parent").GetInt32();
                if (parent >= 0)
                {
                    Assert.True(byIndex.ContainsKey(parent), "#" + i + "'s parent #" + parent + " is exported");
                    Assert.Contains(i, byIndex[parent].GetProperty("children").EnumerateArray().Select(x => x.GetInt32()));
                    Assert.False(o.GetProperty("onTile").GetBoolean());
                }
                foreach (var c in o.GetProperty("children").EnumerateArray().Select(x => x.GetInt32()))
                    Assert.Equal(i, byIndex[c].GetProperty("parent").GetInt32());
            }
            // the readings refer to exported objects
            foreach (var d in doc.RootElement.GetProperty("doors").EnumerateArray()) Assert.True(byIndex.ContainsKey(d.GetProperty("index").GetInt32()));
            foreach (var t in doc.RootElement.GetProperty("triggers").EnumerateArray())
            {
                Assert.True(byIndex.ContainsKey(t.GetProperty("index").GetInt32()));
                foreach (var st in t.GetProperty("chain").EnumerateArray()) Assert.True(byIndex.ContainsKey(st.GetProperty("index").GetInt32()));
            }
            Assert.NotEmpty(doc.RootElement.GetProperty("links").EnumerateArray());
            Assert.Equal(JsonValueKind.Object, doc.RootElement.GetProperty("playerStart").ValueKind);
        }

        [Fact]
        public void Manifest_hashes_and_sizes_match_the_files()
        {
            if (fx.Session == null) return;
            using var man = JsonDocument.Parse(File.ReadAllBytes(Path.Combine(fx.First, "manifest.json")));
            var root = man.RootElement;
            Assert.Equal("uw-arx-export", root.GetProperty("format").GetString());
            Assert.Equal(1, root.GetProperty("formatVersion").GetInt32());
            Assert.Equal("uw1", root.GetProperty("game").GetString());
            Assert.Equal(new[] { 1 }, root.GetProperty("levels").EnumerateArray().Select(x => x.GetInt32()));
            var listed = new List<string>();
            foreach (var f in root.GetProperty("files").EnumerateArray())
            {
                string path = f.GetProperty("path").GetString();
                listed.Add(path);
                var bytes = File.ReadAllBytes(Path.Combine(fx.First, path));
                Assert.Equal(f.GetProperty("size").GetInt64(), bytes.LongLength);
                Assert.Equal(f.GetProperty("sha256").GetString(), Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant());
            }
            Assert.Equal(FilesUnder(fx.First).Where(f => f != "manifest.json"), listed);
            var sources = root.GetProperty("sourceFiles").EnumerateArray().Select(s => s.GetProperty("name").GetString()).ToList();
            Assert.Contains(sources, s => s.EndsWith("LEV.ARK", StringComparison.OrdinalIgnoreCase));
            Assert.Contains(sources, s => s.EndsWith("W64.TR", StringComparison.OrdinalIgnoreCase));
        }

        [Fact]
        public void All_nine_levels_export_with_sane_counts()
        {
            if (fx.Session == null) return;
            string dir = Path.Combine(Path.GetTempPath(), "uwarx-" + Guid.NewGuid().ToString("N"));
            try
            {
                var r = ArxExport.Run(fx.Session, dir);
                Assert.Equal(Enumerable.Range(1, 9), r.Levels);
                output.WriteLine("{0} files, {1:N0} bytes", r.Files.Count, r.TotalBytes);
                var fines = new Dictionary<string, int>();
                foreach (int l in r.Levels)
                {
                    using var doc = JsonDocument.Parse(File.ReadAllBytes(Path.Combine(dir, "levels", "level" + l + ".json")));
                    var s = doc.RootElement.GetProperty("summary");
                    var tri = s.GetProperty("triangles");
                    int open = s.GetProperty("openTiles").GetInt32(), floor = tri.GetProperty("floor").GetInt32(), wall = tri.GetProperty("wall").GetInt32(), ceiling = tri.GetProperty("ceiling").GetInt32();
                    Assert.InRange(open, 100, 4096);
                    Assert.Equal(4096, open + s.GetProperty("solidTiles").GetInt32());
                    Assert.Equal(floor, ceiling);
                    Assert.InRange(floor, open, 2 * open);
                    Assert.True(wall > 0);
                    Assert.Equal(floor + wall + ceiling, tri.GetProperty("total").GetInt32());
                    Assert.Equal(0, s.GetProperty("trianglesWithoutTexture").GetInt32());
                    Assert.InRange(s.GetProperty("lowestFloor").GetInt32(), 0, 15);
                    Assert.InRange(s.GetProperty("highestFloor").GetInt32(), s.GetProperty("lowestFloor").GetInt32(), 15);
                    Assert.InRange(s.GetProperty("objects").GetInt32(), 1, 1022);
                    var root = doc.RootElement;
                    foreach (var d in root.GetProperty("doors").EnumerateArray())
                    {
                        string k = string.Format("fine ({0}, {1}) {2}", d.GetProperty("fineX").GetInt32(), d.GetProperty("fineY").GetInt32(), d.GetProperty("plane").GetString());
                        fines[k] = fines.TryGetValue(k, out int n) ? n + 1 : 1;
                    }
                    output.WriteLine("level {0}: {1} open, {2} triangles ({3} floor, {4} wall, {5} ceiling), floors {6}-{7}, {8} objects, {9} doors, {10} links, {11} triggers, {12} tile changes, {13} standable, {14:N0} bytes",
                        l, open, floor + wall + ceiling, floor, wall, ceiling, s.GetProperty("lowestFloor").GetInt32(), s.GetProperty("highestFloor").GetInt32(), s.GetProperty("objects").GetInt32(),
                        root.GetProperty("doors").GetArrayLength(), root.GetProperty("links").GetArrayLength(), root.GetProperty("triggers").GetArrayLength(),
                        root.GetProperty("tileChanges").GetArrayLength(), root.GetProperty("standable").GetArrayLength(), r.Files["levels/level" + l + ".json"]);
                }
                foreach (var kv in fines.OrderByDescending(k => k.Value)) output.WriteLine("doors at {0}: {1}", kv.Key, kv.Value);
            }
            finally
            {
                if (Directory.Exists(dir)) Directory.Delete(dir, true);
            }
        }
    }
}
