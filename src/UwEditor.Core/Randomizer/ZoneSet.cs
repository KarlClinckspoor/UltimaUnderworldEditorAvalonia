using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace UwEditor.Core.Randomizer
{
    /// <summary>A zone: a part of the world reachable without crossing a requirement.</summary>
    public class Zone
    {
        public int Id;
        public string Name = "";
        public string Levels = "";      // free text, e.g. "L1-L6"; the tile maps are the truth
        public string Status = "";      // done, unfinished, unpainted, unassigned
        public string Notes = "";
        public string Color = "";       // "#RRGGBB"; empty: the Glasbey colour for the id
        /// <summary>Where its label sits in the zone graph window; null: not placed yet.</summary>
        public double? GraphX, GraphY;
        public override string ToString() { return Id + " " + Name; }
    }

    /// <summary>A way from one zone to another, one-way or both, with what it needs.</summary>
    public class ZoneLink
    {
        public int Id;
        public int From;
        public int To;
        public string Kind = "walk";    // walk door portcullis secret doorman drop climb teleport lever stairs jump other
        /// <summary>The requirement expression (Requirement's language): terms joined with &amp; and |.</summary>
        public string Requires = "free";
        public bool TwoWay;
        public string Notes = "";
        public string Confidence = "certain";   // certain, todo
        public int Level = 0;           // optional anchor: level and tile of the passage, 0 = none
        public int X = -1, Y = -1;

        public static readonly string[] Kinds = { "walk", "door", "portcullis", "secret", "doorman", "drop", "climb", "teleport", "lever", "stairs", "jump", "other" };

        public override string ToString()
        {
            return string.Format("{0} {1} {2} [{3}] {4}", From, TwoWay ? "<->" : "->", To, Kind, Requires);
        }

        /// <summary>The requirement as a list of terms that must all hold; a term may be an alternative ("key:5 | pick:3").</summary>
        public List<string> RequirementList
        {
            get { return SplitAnd(Requires); }
            set { Requires = JoinAnd(value); }
        }

        /// <summary>Splits an expression at its top-level '&amp;' (outer parentheses of a term removed).</summary>
        public static List<string> SplitAnd(string expr)
        {
            var list = new List<string>();
            string e = (expr ?? "").Trim();
            if (e.Length == 0 || e == "free") return list;
            int depth = 0, start = 0;
            for (int i = 0; i <= e.Length; i++)
            {
                if (i == e.Length || (e[i] == '&' && depth == 0))
                {
                    string t = e.Substring(start, i - start).Trim();
                    while (t.StartsWith("(") && t.EndsWith(")") && Balanced(t.Substring(1, t.Length - 2))) t = t.Substring(1, t.Length - 2).Trim();
                    if (t.Length > 0 && t != "free") list.Add(t);
                    start = i + 1;
                }
                else if (e[i] == '(') depth++;
                else if (e[i] == ')') depth--;
            }
            return list;
        }

        static bool Balanced(string s)
        {
            int d = 0;
            foreach (char c in s) { if (c == '(') d++; else if (c == ')') { d--; if (d < 0) return false; } }
            return d == 0;
        }

        public static string JoinAnd(IEnumerable<string> terms)
        {
            var t = terms.Select(x => (x ?? "").Trim()).Where(x => x.Length > 0 && x != "free").Select(x => x.Contains("|") ? "(" + x + ")" : x).ToList();
            return t.Count == 0 ? "free" : string.Join(" & ", t);
        }
    }

    /// <summary>What a zone gives: a key, an item, a flag, an NPC to talk to.</summary>
    public class Placement
    {
        public int Zone;
        public string Grants = "";  // one token: key:5, item:medallion, flag:gazer_dead, npc:eb, talisman:cup
        public string Requires = "";    // optional: granted only when this holds too
        public string Notes = "";
        public string Source = "";  // "" (hand), "levark" (found in the levels)
    }

    /// <summary>
    /// The zoning of a game for the randomizer: a zone id for every tile of every level (0 rock,
    /// 1 open but not given a zone yet, 2-255 zones), the zones, the links between them with their
    /// requirements, and what each zone grants. Saved as one JSON file (the Unity editor's
    /// "uw1-zones" format, version 1, read and written by both editors; UW2 files have 80 levels).
    /// </summary>
    public class ZoneSet
    {
        public const int Side = 64;
        public const byte Solid = 0;
        public const byte Unassigned = 1;
        public const int MaxZone = 255;

        public string Game = "UW1";
        public string Source = "";
        public int StartZone = 2;
        public int GoalZone = 0;
        public byte[][] Tiles;
        public List<Zone> Zones = new List<Zone>();
        public List<ZoneLink> Links = new List<ZoneLink>();
        public List<Placement> Placements = new List<Placement>();
        public Dictionary<string, string> OpenSources = new Dictionary<string, string>();
        /// <summary>Objects marked by hand as key items (true) or as not worth listing (false), by "level:index".</summary>
        public Dictionary<string, bool> Importance = new Dictionary<string, bool>();

        public ZoneSet(int levels) { Tiles = new byte[levels][]; }
        public int Levels { get { return Tiles.Length; } }

        public Zone GetZone(int id) { return Zones.Find(z => z.Id == id); }

        public Zone EnsureZone(int id)
        {
            var z = GetZone(id);
            if (z == null)
            {
                z = new Zone { Id = id, Name = id == Unassigned ? "Open, not assigned yet" : "Zone " + id };
                Zones.Add(z);
                Zones.Sort((a, b) => a.Id.CompareTo(b.Id));
            }
            return z;
        }

        public int NextZoneId()
        {
            for (int i = 2; i <= MaxZone; i++) if (GetZone(i) == null && !Used(i)) return i;
            return -1;
        }

        bool Used(int id)
        {
            foreach (var t in Tiles) if (t != null) foreach (var b in t) if (b == id) return true;
            return false;
        }

        public int NextLinkId()
        {
            int m = 0;
            foreach (var l in Links) m = Math.Max(m, l.Id);
            return m + 1;
        }

        public byte ZoneAt(int level1, int x, int y)
        {
            if (level1 < 1 || level1 > Levels) return 0;
            var t = Tiles[level1 - 1];
            if (t == null || x < 0 || y < 0 || x >= Side || y >= Side) return 0;
            return t[y * Side + x];
        }

        public void SetZone(int level1, int x, int y, byte zone)
        {
            if (level1 < 1 || level1 > Levels || x < 0 || y < 0 || x >= Side || y >= Side) return;
            if (Tiles[level1 - 1] == null) Tiles[level1 - 1] = new byte[Side * Side];
            Tiles[level1 - 1][y * Side + x] = zone;
        }

        /// <summary>The zone of the nearest zoned tile within a few squares (an object in a wall, a lever), or 0.</summary>
        public int NearestZone(int level1, int x, int y, int radius = 2)
        {
            int z = ZoneAt(level1, x, y);
            if (z > Unassigned) return z;
            for (int r = 1; r <= radius; r++)
                for (int dy = -r; dy <= r; dy++)
                    for (int dx = -r; dx <= r; dx++)
                    {
                        if (Math.Max(Math.Abs(dx), Math.Abs(dy)) != r) continue;
                        int k = ZoneAt(level1, x + dx, y + dy);
                        if (k > Unassigned) return k;
                    }
            return z;
        }

        /// <summary>Tile counts of each zone id on a level.</summary>
        public Dictionary<int, int> CountsOnLevel(int level1)
        {
            var d = new Dictionary<int, int>();
            var t = level1 >= 1 && level1 <= Levels ? Tiles[level1 - 1] : null;
            if (t == null) return d;
            foreach (var b in t) { int c; d.TryGetValue(b, out c); d[b] = c + 1; }
            return d;
        }

        public int TileCount(int zone)
        {
            int n = 0;
            foreach (var t in Tiles) if (t != null) foreach (var b in t) if (b == zone) n++;
            return n;
        }

        /// <summary>Levels (1-based) a zone has tiles on.</summary>
        public List<int> LevelsOf(int zone)
        {
            var r = new List<int>();
            for (int l = 0; l < Levels; l++)
            {
                if (Tiles[l] == null) continue;
                foreach (var b in Tiles[l]) if (b == zone) { r.Add(l + 1); break; }
            }
            return r;
        }

        /// <summary>
        /// Flood fill from (x, y) over the connected tiles (4-neighbour) that have the same id as
        /// that tile, with a new id. Rock is never filled. Returns the number of tiles changed.
        /// </summary>
        public int Fill(int level1, int x, int y, byte zone)
        {
            var t = level1 >= 1 && level1 <= Levels ? Tiles[level1 - 1] : null;
            if (t == null || x < 0 || y < 0 || x >= Side || y >= Side) return 0;
            byte from = t[y * Side + x];
            if (from == zone || from == Solid) return 0;
            int n = 0;
            var stack = new Stack<int>();
            stack.Push(y * Side + x);
            while (stack.Count > 0)
            {
                int k = stack.Pop();
                if (t[k] != from) continue;
                t[k] = zone;
                n++;
                int kx = k % Side, ky = k / Side;
                if (kx > 0) stack.Push(k - 1);
                if (kx < Side - 1) stack.Push(k + 1);
                if (ky > 0) stack.Push(k - Side);
                if (ky < Side - 1) stack.Push(k + Side);
            }
            return n;
        }

        /// <summary>Fills with zone the tiles the movement rules reach from (x, y) within its zone (<see cref="ZoneFlood.Reach"/>).</summary>
        public int Fill(int level1, int x, int y, byte zone, ZoneFlood rules)
        {
            var t = level1 >= 1 && level1 <= Levels ? Tiles[level1 - 1] : null;
            if (t == null || x < 0 || y < 0 || x >= Side || y >= Side) return 0;
            byte from = t[y * Side + x];
            if (from == zone || from == Solid) return 0;
            var reach = rules.Reach(t, x, y);
            foreach (int k in reach) t[k] = zone;
            return reach.Count;
        }

        /// <summary>Removes a zone: its tiles become unassigned, its links and placements go.</summary>
        public void Remove(int id)
        {
            foreach (var t in Tiles) if (t != null) for (int i = 0; i < t.Length; i++) if (t[i] == id) t[i] = Unassigned;
            Links.RemoveAll(k => k.From == id || k.To == id);
            Placements.RemoveAll(p => p.Zone == id);
            Zones.RemoveAll(z => z.Id == id);
        }

        /// <summary>Marks every open tile of a level that has no zone with Unassigned and every solid one with Solid.</summary>
        public void InitLevel(int level1, Func<int, int, bool> isOpen)
        {
            if (Tiles[level1 - 1] == null) Tiles[level1 - 1] = new byte[Side * Side];
            var t = Tiles[level1 - 1];
            for (int y = 0; y < Side; y++)
                for (int x = 0; x < Side; x++)
                {
                    int k = y * Side + x;
                    if (!isOpen(x, y)) t[k] = Solid;
                    else if (t[k] == Solid) t[k] = Unassigned;
                }
        }

        // ---- colours

        public static uint DefaultColor(int id)
        {
            if (id == Unassigned) return 0xFFFF00FFu;
            if (id == Solid) return 0xFF000000u;
            var g = Glasbey.Colors;
            return g[(id - 2 + g.Length) % g.Length];
        }

        /// <summary>The zone's colour as 0xAARRGGBB: its own, or the Glasbey colour for its id.</summary>
        public uint ColorOf(int id)
        {
            var z = GetZone(id);
            uint c;
            if (z != null && TryParseColor(z.Color, out c)) return c;
            return DefaultColor(id);
        }

        public static bool TryParseColor(string hex, out uint argb)
        {
            argb = 0;
            if (string.IsNullOrEmpty(hex)) return false;
            string h = hex.TrimStart('#');
            uint v;
            if (h.Length != 6 || !uint.TryParse(h, System.Globalization.NumberStyles.HexNumber, null, out v)) return false;
            argb = 0xFF000000u | v;
            return true;
        }

        public static string ColorString(uint argb) { return "#" + (argb & 0xFFFFFF).ToString("X6"); }

        // ---- the file

        public static string DefaultFileName(bool uw2) { return uw2 ? "uw2-zones.json" : "uw1-zones.json"; }

        public static ZoneSet Load(string path, int levels) { return FromJson(File.ReadAllText(path, Encoding.UTF8), levels); }

        public void Save(string path) { File.WriteAllText(path, ToJson(), new UTF8Encoding(false)); }

        static string S(JsonNode n, string k) { var v = n?[k]; return v == null ? "" : v.GetValueKind() == JsonValueKind.String ? (string)v : v.ToJsonString(); }
        static int I(JsonNode n, string k, int d) { var v = n?[k]; if (v == null) return d; try { return (int)v; } catch (Exception) { int r; return int.TryParse(v.ToString(), out r) ? r : d; } }
        static bool B(JsonNode n, string k) { var v = n?[k]; try { return v != null && (bool)v; } catch (Exception) { return false; } }
        static double? D(JsonNode n, string k) { var v = n?[k]; try { return v == null ? (double?)null : (double)v; } catch (Exception) { return null; } }

        /// <summary>Reads a zone file; levels: how many levels the game has (9 for UW1, 80 for UW2), or 0 to take the file's.</summary>
        public static ZoneSet FromJson(string json, int levels = 0)
        {
            var root = JsonNode.Parse(json) as JsonObject;
            string format = S(root, "format");
            if (root == null || (format != "uw1-zones" && format != "uw-zones")) throw new InvalidDataException("Not a zone file (format \"uw1-zones\" or \"uw-zones\").");
            var tl = root["tiles"]?["levels"] as JsonArray;
            int n = levels > 0 ? levels : Math.Max(9, tl?.Count ?? 9);
            var zs = new ZoneSet(n)
            {
                Game = S(root, "game").Length > 0 ? S(root, "game") : "UW1",
                Source = S(root, "source"),
                StartZone = I(root["start"], "zone", 2),
                GoalZone = I(root["goal"], "zone", 0),
            };
            foreach (var z in (root["zones"] as JsonArray) ?? new JsonArray())
                zs.Zones.Add(new Zone
                {
                    Id = I(z, "id", 0), Name = S(z, "name"), Levels = S(z, "levels"), Status = S(z, "status"), Notes = S(z, "notes"), Color = S(z, "color"),
                    GraphX = D(z?["graph"], "x"), GraphY = D(z?["graph"], "y"),
                });
            zs.Zones.Sort((a, b) => a.Id.CompareTo(b.Id));
            foreach (var k in (root["links"] as JsonArray) ?? new JsonArray())
                zs.Links.Add(new ZoneLink
                {
                    Id = I(k, "id", 0), From = I(k, "from", 0), To = I(k, "to", 0), Kind = S(k, "kind").Length > 0 ? S(k, "kind") : "walk",
                    Requires = S(k, "requires").Length > 0 ? S(k, "requires") : "free", TwoWay = B(k, "twoWay"), Notes = S(k, "notes"),
                    Confidence = S(k, "confidence").Length > 0 ? S(k, "confidence") : "certain", Level = I(k, "level", 0), X = I(k, "x", -1), Y = I(k, "y", -1),
                });
            foreach (var p in (root["placements"] as JsonArray) ?? new JsonArray())
                zs.Placements.Add(new Placement { Zone = I(p, "zone", 0), Grants = S(p, "grants"), Requires = S(p, "requires"), Notes = S(p, "notes"), Source = S(p, "source") });
            if (root["openSources"] is JsonObject os) foreach (var kv in os) zs.OpenSources[kv.Key] = kv.Value == null ? "" : kv.Value.ToString();
            if (root["importance"] is JsonObject im) foreach (var kv in im) { try { zs.Importance[kv.Key] = (bool)kv.Value; } catch (Exception) { } }
            if (tl != null)
                for (int l = 0; l < tl.Count && l < n; l++)
                {
                    if (!(tl[l] is JsonArray rows)) continue;
                    var t = new byte[Side * Side];
                    for (int y = 0; y < Side && y < rows.Count; y++)
                    {
                        string row = (string)rows[y] ?? "";
                        row = row.Replace(" ", "");
                        for (int x = 0; x < Side && x * 2 + 1 < row.Length; x++)
                            t[y * Side + x] = Convert.ToByte(row.Substring(x * 2, 2), 16);
                    }
                    zs.Tiles[l] = t;
                }
            return zs;
        }

        public string ToJson()
        {
            var root = new JsonObject
            {
                ["format"] = "uw1-zones", ["version"] = 1, ["game"] = Game, ["source"] = Source,
                ["start"] = new JsonObject { ["zone"] = StartZone },
                ["goal"] = new JsonObject { ["zone"] = GoalZone },
                ["requirementLanguage"] = new JsonArray(Requirement.LanguageHelp.Select(h => (JsonNode)h).ToArray()),
            };
            var zones = new JsonArray();
            foreach (var z in Zones)
            {
                var d = new JsonObject { ["id"] = z.Id, ["name"] = z.Name, ["levels"] = z.Levels, ["status"] = z.Status, ["notes"] = z.Notes };
                if (!string.IsNullOrEmpty(z.Color)) d["color"] = z.Color;
                if (z.GraphX.HasValue && z.GraphY.HasValue) d["graph"] = new JsonObject { ["x"] = Math.Round(z.GraphX.Value, 1), ["y"] = Math.Round(z.GraphY.Value, 1) };
                zones.Add(d);
            }
            root["zones"] = zones;
            var links = new JsonArray();
            foreach (var k in Links)
            {
                var d = new JsonObject
                {
                    ["id"] = k.Id, ["from"] = k.From, ["to"] = k.To, ["kind"] = k.Kind, ["requires"] = k.Requires,
                    ["twoWay"] = k.TwoWay, ["notes"] = k.Notes, ["confidence"] = k.Confidence,
                };
                if (k.Level > 0) { d["level"] = k.Level; d["x"] = k.X; d["y"] = k.Y; }
                links.Add(d);
            }
            root["links"] = links;
            var places = new JsonArray();
            foreach (var p in Placements)
            {
                var d = new JsonObject { ["zone"] = p.Zone, ["grants"] = p.Grants };
                if (!string.IsNullOrEmpty(p.Requires)) d["requires"] = p.Requires;
                d["notes"] = p.Notes;
                if (!string.IsNullOrEmpty(p.Source)) d["source"] = p.Source;
                places.Add(d);
            }
            root["placements"] = places;
            var open = new JsonObject();
            foreach (var kv in OpenSources) open[kv.Key] = kv.Value;
            root["openSources"] = open;
            if (Importance.Count > 0)
            {
                var im = new JsonObject();
                foreach (var kv in Importance.OrderBy(k => k.Key)) im[kv.Key] = kv.Value;
                root["importance"] = im;
            }
            var levels = new JsonArray();
            for (int l = 0; l < Levels; l++)
            {
                if (Tiles[l] == null) { levels.Add(null); continue; }
                var rows = new JsonArray();
                for (int y = 0; y < Side; y++)
                {
                    var sb = new StringBuilder(Side * 2);
                    for (int x = 0; x < Side; x++) sb.Append(Tiles[l][y * Side + x].ToString("X2"));
                    rows.Add(sb.ToString());
                }
                levels.Add(rows);
            }
            root["tiles"] = new JsonObject
            {
                ["encoding"] = "per level: 64 rows (y = 0..63), each 64 zone ids as two hex digits (x = 0..63); 00 solid, 01 open and unassigned",
                ["levels"] = levels,
            };
            return root.ToJsonString(new JsonSerializerOptions { WriteIndented = true, Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping });
        }

        // ---- pictures: one pixel a tile, the pixel's value the zone id

        /// <summary>
        /// Reads one level's zone ids from a 64x64 picture: an indexed PNG or 8-bit BMP (the index is
        /// the zone id), or a grey PNG (the grey value). The picture is the map with north up, as
        /// Gimp writes the maps: its bottom row is y = 0. Returns an error text, or null.
        /// </summary>
        public string ImportPicture(int level1, byte[] file)
        {
            byte[] t;
            if (file.Length > 8 && file[0] == 0x89 && file[1] == (byte)'P')
            {
                UwEditor.Core.Graphics.PngImage img;
                try { img = UwEditor.Core.Graphics.Png.Decode(file); } catch (Exception ex) { return "Cannot read the PNG: " + ex.Message; }
                if (img.Width != Side || img.Height != Side) return string.Format("The picture is {0}x{1}, not 64x64.", img.Width, img.Height);
                t = new byte[Side * Side];
                for (int p = 0; p < t.Length; p++)
                {
                    int i = (Side - 1 - p / Side) * Side + p % Side;   // picture row 0 is y = 63
                    if (img.Indices != null) t[i] = (byte)img.Indices[p];
                    else
                    {
                        byte r = img.Rgba[p * 4], g = img.Rgba[p * 4 + 1], b = img.Rgba[p * 4 + 2];
                        if (r != g || g != b) return "The PNG has colours: save it as an indexed or a greyscale image, the index or grey value being the zone id.";
                        t[i] = r;
                    }
                }
            }
            else if (file.Length >= 54 && file[0] == 'B' && file[1] == 'M')
            {
                int off = BitConverter.ToInt32(file, 10), w = BitConverter.ToInt32(file, 18), h = BitConverter.ToInt32(file, 22), bpp = BitConverter.ToUInt16(file, 28);
                if (w != Side || Math.Abs(h) != Side) return string.Format("The picture is {0}x{1}, not 64x64.", w, h);
                if (bpp != 8) return "Not an 8-bit indexed BMP (" + bpp + " bits per pixel).";
                if (off < 54 || (long)off + Side * Side > file.Length) return "The BMP is cut short: its pixels are not all there.";
                t = new byte[Side * Side];
                for (int r = 0; r < Side; r++)
                {
                    int y = h > 0 ? r : Side - 1 - r;
                    Array.Copy(file, off + r * Side, t, y * Side, Side);
                }
            }
            else return "Not a PNG or BMP file.";
            Tiles[level1 - 1] = t;
            foreach (var b in t) if (b > Unassigned) EnsureZone(b);
            return null;
        }

        /// <summary>One level as an indexed PNG whose palette is the zones' colours (index = zone id), for painting elsewhere.</summary>
        public byte[] ExportPicture(int level1)
        {
            var t = Tiles[level1 - 1] ?? new byte[Side * Side];
            var pal = new uint[256];
            for (int i = 0; i < 256; i++) pal[i] = ColorOf(i);
            var idx = new int[Side * Side];
            for (int p = 0; p < idx.Length; p++) idx[p] = t[(Side - 1 - p / Side) * Side + p % Side];
            return UwEditor.Core.Graphics.Png.EncodeIndexed(Side, Side, idx, pal);
        }
    }
}
