using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace UwEditor.Core.Randomizer
{
    /// <summary>
    /// A place a fact names: a tile of a level, or an object of a level (its tile), or a special
    /// word ("planted": wherever the player planted the silver sapling, see the warp kind).
    /// </summary>
    public sealed class FactSpot
    {
        public int Level;
        public int X = -1, Y = -1;
        public int Object = -1;
        /// <summary>A word in place of a place: "planted" (or "back").</summary>
        public string Special = "";

        public bool IsPlanted { get { return Special == "planted" || Special == "back"; } }
        public bool HasTile { get { return Level > 0 && X >= 0 && Y >= 0 && X < 64 && Y < 64; } }
        public bool HasObject { get { return Level > 0 && Object >= 0; } }
        public bool Valid { get { return IsPlanted || HasTile || HasObject; } }

        public override string ToString()
        {
            if (Special.Length > 0) return Special;
            if (HasObject) return string.Format("L{0} #{1}", Level, Object);
            return string.Format("L{0} ({1}, {2})", Level, X, Y);
        }
    }

    /// <summary>One hand-written fact of a facts file (docs/facts/uw1-*.json).</summary>
    public sealed class Fact
    {
        public string Id = "";
        /// <summary>craft, trade, flag, nomana, walls, hidden; Phase C: link, warp, goal, free, needs.</summary>
        public string Kind = "";
        /// <summary>craft, flag: the token granted.</summary>
        public string Grants = "";
        /// <summary>trade: the token whose placements get the requirement.</summary>
        public string Token = "";
        public string Requires = "";
        /// <summary>nomana: the token that lifts it.</summary>
        public string Until = "";
        public int Level;
        /// <summary>flag: the object whose zone grants it; hidden: the look trigger.</summary>
        public int Object = -1, Trigger = -1;
        /// <summary>A tile given at the top level (goal, free, flag): x and y, or -1.</summary>
        public int X = -1, Y = -1;
        /// <summary>walls: the rectangle x0, y0, x1, y1 (inclusive).</summary>
        public int[] Rect;
        /// <summary>walls: which tiles of the rectangle (damage: move triggers whose chain hurts the player).</summary>
        public string Tiles = "";
        public string Notes = "";
        public string Source = "";
        /// <summary>certain, probable, todo (optional).</summary>
        public string Confidence = "";
        /// <summary>The file it was read from (uw1-facts.json, uw1-quests.json ...).</summary>
        public string File = "";

        // ---- Phase C

        /// <summary>link: where it starts and ends; warp: where it goes (To); goal: where the goal is (To, or the top-level level and tile or object).</summary>
        public FactSpot From, To;
        /// <summary>link: both ways.</summary>
        public bool TwoWay;
        /// <summary>link, warp: the option (without "option:") the fact needs; off: the link takes Otherwise, or does not exist.</summary>
        public string Option = "";
        /// <summary>link: the requirement when the option is off; empty: then there is no link.</summary>
        public string Otherwise = "";
        /// <summary>link: the links' kind (default "other").</summary>
        public string LinkKind = "";
        /// <summary>link: the other links between the two zones are removed (the fact is the only way between them).</summary>
        public bool Replace;
        /// <summary>warp: only from the zones of this level (0: from every zone).</summary>
        public int FromLevel;
        /// <summary>free: the objects (levers) and triggers whose chains count as done once their zone is reached, and the flags granted there.</summary>
        public readonly List<int> Objects = new List<int>();
        public readonly List<int> Triggers = new List<int>();
        public readonly List<string> Flags = new List<string>();

        public bool InRect(int x, int y) { return Rect != null && Rect.Length == 4 && x >= Rect[0] && x <= Rect[2] && y >= Rect[1] && y <= Rect[3]; }
        public override string ToString() { return Id + " (" + Kind + ")"; }

        /// <summary>The place the fact names at its top level (level with x and y, or with object), or null.</summary>
        public FactSpot TopSpot
        {
            get
            {
                if (Level <= 0) return null;
                if (Object >= 0) return new FactSpot { Level = Level, Object = Object };
                if (X >= 0 && Y >= 0) return new FactSpot { Level = Level, X = X, Y = Y };
                return null;
            }
        }
    }

    /// <summary>
    /// The facts files: ability sources that are not plain items (the dragon skin boots), flags set
    /// by using an item somewhere (the orb), areas with special rules (the damaging maze), notes on
    /// the hidden gates, and (Phase C) links, warps, the goal, and chains that count as done. A
    /// small JSON format meant to be edited by hand ("uw1-facts"). Every docs/facts/uw1-*.json is
    /// embedded under its own file name and read by <see cref="Shipped"/>, uw1-facts.json first.
    /// </summary>
    public sealed class FactSet
    {
        public string Game = "UW1";
        public string Notes = "";
        public readonly List<Fact> Facts = new List<Fact>();
        /// <summary>The files read (uw1-facts.json, uw1-quests.json ...).</summary>
        public readonly List<string> Files = new List<string>();

        /// <summary>The option names the facts may test (option:NAME), as the automatic zones' options set them.</summary>
        public static readonly string[] KnownOptions =
        {
            "magic", "single_use", "lockpick", "pick_retry", "search_retry", "damage_walls", "ring_needs_lava", "kill_npcs",
            "cut_levels", "gap_jumps", "diagonal_jumps", "glitches",
        };

        public IEnumerable<Fact> OfKind(string kind) { return Facts.Where(f => f.Kind == kind); }

        /// <summary>
        /// The extra conditions the facts give a mechanism (kind "needs" by its flag or its level
        /// and trigger, or a trade whose token is a mechanism flag): the mechanism goes off only
        /// when they hold too. "free" when none.
        /// </summary>
        public string MechanismNeeds(int level, int trigger, string flag)
        {
            string fl = Requirement.Normalize(flag);
            var terms = Facts.Where(f => (f.Kind == "needs" && ((f.Token.Length > 0 && Requirement.Normalize(f.Token) == fl) || (f.Token.Length == 0 && f.Level == level && f.Trigger == trigger)))
                || (f.Kind == "trade" && Requirement.Normalize(f.Token) == fl)).Select(f => f.Requires).Where(r => !string.IsNullOrWhiteSpace(r)).ToList();
            return ZoneLink.JoinAnd(terms);
        }

        /// <summary>The tokens the facts grant (craft, flag, free flags).</summary>
        public HashSet<string> Granted()
        {
            var h = new HashSet<string>();
            foreach (var f in Facts)
            {
                if ((f.Kind == "craft" || f.Kind == "flag") && f.Grants.Length > 0) h.Add(Requirement.Normalize(f.Grants));
                if (f.Kind == "free") foreach (var t in f.Flags) h.Add(Requirement.Normalize(t));
            }
            return h;
        }

        public static FactSet Load(string path)
        {
            var set = FromJson(File.ReadAllText(path), Path.GetFileName(path));
            return set;
        }

        /// <summary>The facts shipped with the editor (every embedded uw1-*.json; only UW1 has any), or an empty set.</summary>
        public static FactSet Shipped(bool uw2)
        {
            if (uw2) return new FactSet { Game = "UW2" };
            var asm = typeof(FactSet).Assembly;
            var names = asm.GetManifestResourceNames().Where(IsFactsName).OrderBy(n => n == "uw1-facts.json" ? 0 : 1).ThenBy(n => n, StringComparer.Ordinal).ToList();
            var all = new FactSet();
            foreach (var name in names)
            {
                using (var st = asm.GetManifestResourceStream(name))
                {
                    if (st == null) continue;
                    FactSet one;
                    using (var r = new StreamReader(st))
                    {
                        try { one = FromJson(r.ReadToEnd(), name); }
                        catch (Exception ex) { all.LoadErrors.Add(name + ": " + ex.Message); continue; }
                    }
                    all.Add(one);
                }
            }
            return all;
        }

        /// <summary>A shipped facts file name: uw1-NAME.json.</summary>
        public static bool IsFactsName(string name)
        {
            return name.StartsWith("uw1-", StringComparison.Ordinal) && name.EndsWith(".json", StringComparison.Ordinal) && name.IndexOf('/') < 0 && !name.Contains("zones");
        }

        /// <summary>Files that could not be read (reported by <see cref="Check"/>).</summary>
        public readonly List<string> LoadErrors = new List<string>();

        /// <summary>Adds another file's facts (their notes are kept per fact; the first file's notes and game stay).</summary>
        public void Add(FactSet other)
        {
            if (Files.Count == 0) { Game = other.Game.Length > 0 ? other.Game : Game; Notes = other.Notes; }
            Files.AddRange(other.Files);
            Facts.AddRange(other.Facts);
            LoadErrors.AddRange(other.LoadErrors);
        }

        static string S(JsonNode n, string k)
        {
            var v = n?[k];
            if (v == null) return "";
            return v.GetValueKind() == JsonValueKind.String ? (string)v : v.ToJsonString();
        }

        static int Int(JsonNode v, int dflt)
        {
            if (v == null) return dflt;
            if (v.GetValueKind() == JsonValueKind.Number) return (int)v.AsValue().GetValue<double>();
            if (v.GetValueKind() != JsonValueKind.String) return dflt;
            int x;
            string t = ((string)v).Trim();
            if (t.StartsWith("#")) t = t.Substring(1);
            if (t.StartsWith("0x", StringComparison.OrdinalIgnoreCase) && int.TryParse(t.Substring(2), System.Globalization.NumberStyles.HexNumber, null, out x)) return x;
            return int.TryParse(t, out x) ? x : dflt;
        }

        static int I(JsonNode n, string k, int dflt) { return Int(n?[k], dflt); }

        static bool B(JsonNode n, string k)
        {
            var v = n?[k];
            if (v == null) return false;
            if (v.GetValueKind() == JsonValueKind.True) return true;
            if (v.GetValueKind() == JsonValueKind.String) { string t = ((string)v).Trim().ToLowerInvariant(); return t == "true" || t == "yes" || t == "1"; }
            return false;
        }

        /// <summary>A place: {"level": L, "x": X, "y": Y} or {"level": L, "object": N}, [L, X, Y], or a word ("planted").</summary>
        static FactSpot Spot(JsonNode v, int level)
        {
            if (v == null) return null;
            var kind = v.GetValueKind();
            if (kind == JsonValueKind.String) return new FactSpot { Level = level, Special = ((string)v).Trim().ToLowerInvariant() };
            if (kind == JsonValueKind.Number) return new FactSpot { Level = Int(v, 0) };
            if (v is JsonArray a)
            {
                var sp = new FactSpot();
                if (a.Count == 3) { sp.Level = Int(a[0], 0); sp.X = Int(a[1], -1); sp.Y = Int(a[2], -1); }
                else if (a.Count == 2) { sp.Level = Int(a[0], 0); sp.Object = Int(a[1], -1); }
                return sp;
            }
            if (v is JsonObject o)
            {
                var sp = new FactSpot { Level = I(o, "level", level), X = I(o, "x", -1), Y = I(o, "y", -1), Object = I(o, "object", -1) };
                var tile = o["tile"] as JsonArray;
                if (tile != null && tile.Count == 2) { sp.X = Int(tile[0], -1); sp.Y = Int(tile[1], -1); }
                return sp;
            }
            return null;
        }

        static void Ints(JsonNode n, string one, string many, List<int> into)
        {
            int v = I(n, one, -1);
            if (v >= 0) into.Add(v);
            if (n?[many] is JsonArray a) foreach (var x in a) { int k = Int(x, -1); if (k >= 0) into.Add(k); }
        }

        static string Opt(string o)
        {
            o = (o ?? "").Trim().ToLowerInvariant();
            return o.StartsWith("option:") ? o.Substring(7) : o;
        }

        public static FactSet FromJson(string json, string file = "")
        {
            var root = JsonNode.Parse(json, documentOptions: new JsonDocumentOptions { CommentHandling = JsonCommentHandling.Skip, AllowTrailingCommas = true });
            string format = S(root, "format");
            if (format.Length > 0 && !format.EndsWith("-facts")) throw new FormatException("Not a facts file (format '" + format + "').");
            var set = new FactSet { Game = S(root, "game"), Notes = S(root, "notes") };
            if (file.Length > 0) set.Files.Add(file);
            var arr = root?["facts"] as JsonArray;
            if (arr == null) return set;
            foreach (var n in arr)
            {
                var f = new Fact
                {
                    Id = S(n, "id"), Kind = S(n, "kind").Trim().ToLowerInvariant(), Grants = S(n, "grants"), Token = S(n, "token"), Requires = S(n, "requires"), Until = S(n, "until"),
                    Level = I(n, "level", 0), Object = I(n, "object", -1), Trigger = I(n, "trigger", -1), X = I(n, "x", -1), Y = I(n, "y", -1),
                    Tiles = S(n, "tiles"), Notes = S(n, "notes"), Source = S(n, "source"), Confidence = S(n, "confidence"), File = file,
                    TwoWay = B(n, "twoWay") || B(n, "twoway") || B(n, "two_way"), Option = Opt(S(n, "option")), Otherwise = S(n, "otherwise"),
                    LinkKind = S(n, "linkKind"), Replace = B(n, "replace"), FromLevel = I(n, "fromLevel", 0),
                };
                var rect = n?["rect"] as JsonArray;
                if (rect != null) f.Rect = rect.Select(x => Int(x, 0)).ToArray();
                var tile = n?["tile"] as JsonArray;
                if (tile != null && tile.Count == 2) { f.X = Int(tile[0], -1); f.Y = Int(tile[1], -1); }
                f.From = Spot(n?["from"], f.Level);
                f.To = Spot(n?["to"] ?? n?["at"], f.Level);
                if (f.Kind == "warp")
                {
                    // a warp's own level (with a destination given apart) says where it is made from
                    if (f.FromLevel == 0 && f.To != null && n?["level"] != null && n?["object"] == null && n?["x"] == null) f.FromLevel = f.Level;
                    if (f.From != null && f.From.Level > 0 && f.FromLevel == 0 && !f.From.HasTile && !f.From.HasObject) f.FromLevel = f.From.Level;
                }
                if (f.Kind == "goal" && f.To == null) f.To = f.TopSpot;
                if (f.Kind == "free")
                {
                    Ints(n, "object", "objects", f.Objects);
                    Ints(n, "trigger", "triggers", f.Triggers);
                    if (S(n, "flag").Length > 0) f.Flags.Add(S(n, "flag"));
                    // "token" names the flag the chain's condition tested: with objects or triggers it only documents it, alone it is a flag to grant
                    if (f.Token.Length > 0 && f.Objects.Count + f.Triggers.Count == 0) f.Flags.Add(f.Token);
                    if (n?["flags"] is JsonArray fl) foreach (var x in fl) f.Flags.Add(x.GetValueKind() == JsonValueKind.String ? (string)x : x.ToJsonString());
                    for (int i = 0; i < f.Flags.Count; i++) { string t = f.Flags[i].Trim(); f.Flags[i] = t.Contains(":") ? t : "flag:" + t; }
                }
                set.Facts.Add(f);
            }
            return set;
        }

        /// <summary>The problems a hand edit may have made: unknown kinds, unreadable requirements, missing fields, ids used twice.</summary>
        public List<string> Check()
        {
            var list = new List<string>(LoadErrors);
            foreach (var g in Facts.GroupBy(f => f.Id).Where(g => g.Key.Length > 0 && g.Count() > 1))
                list.Add(g.Key + ": the id is used " + g.Count() + " times (" + string.Join(", ", g.Select(f => f.File).Distinct()) + ").");
            foreach (var f in Facts)
            {
                Action<string> req = r => { string e; if (Requirement.TryParse(r, out e) == null) list.Add(f.Id + ": " + e); };
                Action<string> option = o => { if (o.Length > 0 && !KnownOptions.Contains(o)) list.Add(f.Id + ": unknown option '" + o + "' (known: " + string.Join(", ", KnownOptions) + ")."); };
                switch (f.Kind)
                {
                    case "craft":
                    case "flag":
                        if (f.Grants.Length == 0) list.Add(f.Id + ": no 'grants'.");
                        req(f.Requires);
                        if (f.Kind == "flag" && (f.Level <= 0 || (f.Object < 0 && (f.X < 0 || f.Y < 0)))) list.Add(f.Id + ": a flag needs 'level' and 'object' (or 'x' and 'y').");
                        break;
                    case "trade":
                        if (f.Token.Length == 0) list.Add(f.Id + ": no 'token'.");
                        req(f.Requires);
                        break;
                    case "nomana":
                        if (f.Level <= 0) list.Add(f.Id + ": no 'level'.");
                        break;
                    case "walls":
                        if (f.Level <= 0 || f.Rect == null || f.Rect.Length != 4) list.Add(f.Id + ": walls need 'level' and 'rect' [x0, y0, x1, y1].");
                        break;
                    case "hidden":
                        if (f.Level <= 0 || f.Trigger < 0) list.Add(f.Id + ": a hidden gate needs 'level' and 'trigger'.");
                        break;
                    case "link":
                        if (f.From == null || !(f.From.HasTile || f.From.HasObject)) list.Add(f.Id + ": a link needs 'from' {level, x, y} or {level, object}.");
                        if (f.To == null || !(f.To.HasTile || f.To.HasObject)) list.Add(f.Id + ": a link needs 'to' {level, x, y} or {level, object}.");
                        req(f.Requires);
                        if (f.Otherwise.Length > 0) req(f.Otherwise);
                        option(f.Option);
                        break;
                    case "warp":
                        if (f.To == null || !f.To.Valid) list.Add(f.Id + ": a warp needs 'to' {level, x, y}, {level, object} or \"planted\".");
                        req(f.Requires);
                        option(f.Option);
                        break;
                    case "goal":
                        if (f.To == null || !(f.To.HasTile || f.To.HasObject)) list.Add(f.Id + ": a goal needs where it is: 'level' with 'x' and 'y' or 'object' (or 'at').");
                        req(f.Requires);
                        break;
                    case "needs":
                        if (f.Token.Length == 0 && (f.Level <= 0 || f.Trigger < 0)) list.Add(f.Id + ": 'needs' names a mechanism by 'token' (its flag) or by 'level' and 'trigger'.");
                        req(f.Requires);
                        break;
                    case "free":
                        if (f.Objects.Count + f.Triggers.Count + f.Flags.Count == 0) list.Add(f.Id + ": 'free' names nothing (objects, triggers or flags).");
                        if ((f.Objects.Count + f.Triggers.Count > 0) && f.Level <= 0) list.Add(f.Id + ": objects and triggers need 'level'.");
                        foreach (var t in f.Flags) req(t);
                        break;
                    default:
                        list.Add(f.Id + ": unknown kind '" + f.Kind + "'.");
                        break;
                }
            }
            return list;
        }
    }
}
