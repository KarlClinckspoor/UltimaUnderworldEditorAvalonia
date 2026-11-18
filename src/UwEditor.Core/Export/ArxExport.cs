using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Security.Cryptography;
using System.Text;
using UltimateEditor.Games;
using UwEditor.Core.Editing;
using UwEditor.Core.Randomizer;

namespace UwEditor.Core.Export
{
    /// <summary>What one export wrote: the levels and, for each, its summary counts.</summary>
    public sealed class ArxExportResult
    {
        public readonly List<int> Levels = new List<int>();
        /// <summary>Each level's summary, as written in its file.</summary>
        public readonly SortedDictionary<int, JObj> Summaries = new SortedDictionary<int, JObj>();
        /// <summary>Every file written, by its path in the output folder ('/' between folders), with its size.</summary>
        public readonly SortedDictionary<string, long> Files = new SortedDictionary<string, long>(StringComparer.Ordinal);
        public long TotalBytes { get { return Files.Values.Sum(); } }
    }

    /// <summary>
    /// The export of Ultima Underworld 1 levels for ArxStructor (docs/ARX-EXPORT.md): one folder of
    /// JSON and PNG files holding each level's tiles, its surfaces as the 3D view builds them
    /// (<see cref="LevelGeometry.Build"/>, with the resolved texture of every triangle), its objects
    /// with the editor's reading of doors, traps, level links and lights, and the textures, object
    /// pictures, object classes and strings they use. The same game gives the same files byte for
    /// byte: keys and lists in a fixed order, invariant numbers, LF line ends, UTF-8 without a byte
    /// order mark, and no time stamps. The game files are only read.
    /// </summary>
    public static partial class ArxExport
    {
        public const string Format = "uw-arx-export";
        /// <summary>Raised when a field changes meaning.</summary>
        public const int FormatVersion = 1;

        static readonly UTF8Encoding Utf8 = new UTF8Encoding(false);

        /// <summary>The state of one export: the game, the output folder and what was written and read.</summary>
        sealed class Ctx
        {
            public GameSession S;
            public string Out;
            public ObjectTables Tables;
            public TerrainTable Terrain;
            public byte[] ComObj;
            public readonly ArxExportResult Result = new ArxExportResult();
            /// <summary>The SHA-256 of each file written, by its path.</summary>
            public readonly SortedDictionary<string, string> Hashes = new SortedDictionary<string, string>(StringComparer.Ordinal);
            /// <summary>The game files read, by their path under the game folder.</summary>
            public readonly SortedDictionary<string, string> Sources = new SortedDictionary<string, string>(StringComparer.Ordinal);
            /// <summary>The PNGs written for each texture set (walls, floors, doors), by number.</summary>
            public readonly Dictionary<string, HashSet<int>> Textures = new Dictionary<string, HashSet<int>>();
        }

        /// <summary>
        /// Writes the export of a UW1 game into a folder (made if missing; files already there with
        /// the same names are replaced, others are left). level: only that level (1-9), or null for
        /// every level present. Throws when the game is not UW1, the level does not exist, or a file
        /// cannot be read or written.
        /// </summary>
        public static ArxExportResult Run(GameSession s, string outFolder, int? level = null)
        {
            if (s == null) throw new ArgumentNullException(nameof(s));
            if (string.IsNullOrWhiteSpace(outFolder)) throw new ArgumentException("No output folder given.");
            if (s.IsUw2) throw new NotSupportedException("The ArxStructor export reads Ultima Underworld 1 only; this is Ultima Underworld II (docs/ARX-EXPORT.md, question 10).");
            var present = s.LevelSlots().Where(l => l.Present).Select(l => l.Level).ToList();
            List<int> levels;
            if (level.HasValue)
            {
                if (!present.Contains(level.Value)) throw new ArgumentException(string.Format("There is no level {0} in this game (levels {1}).", level.Value, string.Join(", ", present)));
                levels = new List<int> { level.Value };
            }
            else levels = present;
            if (levels.Count == 0) throw new InvalidDataException("LEV.ARK holds no level.");

            var c = new Ctx { S = s, Out = Path.GetFullPath(outFolder) };
            Directory.CreateDirectory(c.Out);
            c.Tables = ObjectTables.Load(s);
            c.Terrain = CreatureTerrain.TryLoad(s);
            c.ComObj = c.Tables.ComObjBytes;
            foreach (var name in new[] { "LEV.ARK", "OBJECTS.DAT", "COMOBJ.DAT", "TERRAIN.DAT", "STRINGS.PAK", "PALS.DAT", "ALLPALS.DAT", "W64.TR", "F32.TR", "DOORS.GR", "OBJECTS.GR" })
                Source(c, name == "LEV.ARK" ? s.LevArkPath : GameProfile.Find(s.DataFolder, name));
            if (s.ExePath != null) Source(c, s.ExePath);

            WriteTextures(c);
            WriteSprites(c);
            WriteClasses(c);
            WriteStrings(c);
            foreach (int l in levels)
            {
                var m = s.LoadLevel(l);
                var doc = LevelDocument(c, m);
                c.Result.Summaries[l] = (JObj)doc["summary"];
                WriteText(c, "levels/level" + l + ".json", ArxJson.Write(doc));
                c.Result.Levels.Add(l);
            }
            WriteText(c, "manifest.json", ArxJson.Write(Manifest(c)), false);
            return c.Result;
        }

        static void Source(Ctx c, string path)
        {
            if (path == null || !File.Exists(path)) return;
            string rel = Path.GetRelativePath(c.S.GameFolder, path).Replace('\\', '/');
            c.Sources[rel] = path;
        }

        static string Sha256(byte[] b)
        {
            using (var sha = SHA256.Create())
                return string.Concat(sha.ComputeHash(b).Select(x => x.ToString("x2")));
        }

        static void WriteText(Ctx c, string rel, string text, bool hash = true) { WriteBytes(c, rel, Utf8.GetBytes(text), hash); }

        static void WriteBytes(Ctx c, string rel, byte[] bytes, bool hash = true)
        {
            string full = Path.Combine(c.Out, rel.Replace('/', Path.DirectorySeparatorChar));
            Directory.CreateDirectory(Path.GetDirectoryName(full));
            File.WriteAllBytes(full, bytes);
            c.Result.Files[rel] = bytes.LongLength;
            if (hash) c.Hashes[rel] = Sha256(bytes);
        }

        /// <summary>The editor's version without the commit, so two builds of one version write the same manifest.</summary>
        static string EditorVersion()
        {
            var asm = typeof(ArxExport).Assembly;
            string v = asm.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion ?? asm.GetName().Version?.ToString() ?? "unknown";
            int plus = v.IndexOf('+');
            return plus > 0 ? v.Substring(0, plus) : v;
        }

        static JObj Manifest(Ctx c)
        {
            var sources = new List<object>();
            foreach (var kv in c.Sources)
            {
                var b = File.ReadAllBytes(kv.Value);
                sources.Add(new JLine(new JObj().Add("name", kv.Key).Add("size", b.LongLength).Add("sha256", Sha256(b))));
            }
            var files = new List<object>();
            foreach (var kv in c.Hashes)
                files.Add(new JLine(new JObj().Add("path", kv.Key).Add("size", c.Result.Files[kv.Key]).Add("sha256", kv.Value)));
            return new JObj()
                .Add("format", Format)
                .Add("formatVersion", FormatVersion)
                .Add("game", "uw1")
                .Add("editorVersion", EditorVersion())
                .Add("levels", c.Result.Levels.Cast<object>().ToList())
                .Add("units", Units())
                .Add("doorModels", DoorModels(c))
                .Add("sourceFiles", sources)
                .Add("files", files);
        }

        /// <summary>The conventions, as numbers (docs/ARX-EXPORT.md "Conventions").</summary>
        static JObj Units()
        {
            return new JObj()
                .Add("grid", new JObj().Add("width", LevelModel.Size).Add("height", LevelModel.Size).Add("xRuns", "east").Add("yRuns", "north")
                    .Add("tileOrder", "y then x: index = y * 64 + x"))
                .Add("meshAxes", new JObj().Add("x", "east").Add("y", "up").Add("z", "north").Add("unit", "tile")
                    .Add("handedness", "left-handed (x east, y up, z north), drawn unmirrored"))
                .Add("tileSize", 1.0)
                .Add("floorStep", (double)LevelGeometry.Step)
                .Add("floorHeights", new List<object> { 0, 15 })
                .Add("ceilingHeight", LevelGeometry.CeilingHeight)
                .Add("ceilingHeightTiles", (double)LevelGeometry.Ceiling)
                .Add("slopeRise", 1)
                .Add("objectFine", new JObj().Add("steps", 8).Add("worldInTile", "(fine + 0.5) / 8"))
                .Add("objectZ", new JObj().Add("unitsPerStep", 8).Add("unitsPerTile", 32).Add("worldZ", "z / 32"))
                .Add("heading", new JObj().Add("steps", 8).Add("degreesPerStep", 45).Add("zero", "north").Add("turns", "clockwise seen from above")
                    .Add("degrees", "heading * 45"))
                .Add("winding", new JObj()
                    .Add("frontFacesOpenSpace", true)
                    .Add("seenFromFront", "counter-clockwise in the game's unmirrored view (x east to the right, z north away, y up)")
                    .Add("crossProduct", "cross(v1 - v0, v2 - v0) computed on (x, y, z) as plain numbers points AWAY from the open space (into the rock, down under a floor, up above a ceiling); negate it for the front normal"))
                .Add("wallUv", new JObj().Add("u", "0 to 1 across the tile edge, the texture's left column on the left of a viewer standing in the open tile")
                    .Add("v", "tiles down from the top edge of the face: 0 at the top, the face's mean height at the bottom; the texture is one tile high and repeats"))
                .Add("floorUv", new JObj().Add("u", "world x (repeats every tile; the texture's left column on the tile's west edge)")
                    .Add("v", "floor: world y, the texture's top row on the tile's south edge; ceiling: -(y + 1) + (1 - northing), the top row on the north edge; repeats every tile"))
                .Add("textureV", "v grows downward in the picture: v = 0 is the top row of the PNG");
        }

        /// <summary>The extents of UW.EXE's door models (Uw1ExeModels: frame 1, door 0xE, portcullis 0xC, secret door 0xF) in tiles: x across the opening, y towards the wall (north at heading 0), z up.</summary>
        static JObj DoorModels(Ctx c)
        {
            var exe = c.S.ExeModels;
            if (exe == null) return null;
            var o = new JObj();
            foreach (var pair in new[] { Tuple.Create("frame", 1), Tuple.Create("door", 0x0E), Tuple.Create("portcullis", 0x0C), Tuple.Create("secretDoor", 0x0F) })
            {
                var m = pair.Item2 < exe.Models.Count ? exe.Models[pair.Item2] : null;
                var pts = m == null ? new List<float[]>() : m.Vertices.Where(v => v != null).ToList();
                if (pts.Count == 0) { o.Add(pair.Item1, null); continue; }
                o.Add(pair.Item1, new JLine(new JObj().Add("model", pair.Item2)
                    .Add("minX", pts.Min(p => p[0])).Add("maxX", pts.Max(p => p[0]))
                    .Add("minY", pts.Min(p => p[1])).Add("maxY", pts.Max(p => p[1]))
                    .Add("minZ", pts.Min(p => p[2])).Add("maxZ", pts.Max(p => p[2]))));
            }
            return o;
        }

        static string Hex(byte[] b) { return string.Concat(b.Select(x => x.ToString("x2"))); }

        static List<object> Ints(IEnumerable<int> v) { return v.Cast<object>().ToList(); }
    }
}
