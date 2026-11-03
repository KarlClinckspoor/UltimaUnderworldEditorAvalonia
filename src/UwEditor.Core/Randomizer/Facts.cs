using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace UwEditor.Core.Randomizer
{
    /// <summary>One hand-written fact of the facts file (docs/facts/uw1-facts.json).</summary>
    public sealed class Fact
    {
        public string Id = "";
        /// <summary>craft, trade, flag, nomana, walls, hidden.</summary>
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
        /// <summary>walls: the rectangle x0, y0, x1, y1 (inclusive).</summary>
        public int[] Rect;
        /// <summary>walls: which tiles of the rectangle (damage: move triggers whose chain hurts the player).</summary>
        public string Tiles = "";
        public string Notes = "";
        public string Source = "";

        public bool InRect(int x, int y) { return Rect != null && Rect.Length == 4 && x >= Rect[0] && x <= Rect[2] && y >= Rect[1] && y <= Rect[3]; }
        public override string ToString() { return Id + " (" + Kind + ")"; }
    }

    /// <summary>
    /// The facts file: ability sources that are not plain items (the dragon skin boots), flags set
    /// by using an item somewhere (the orb), areas with special rules (the damaging maze), and
    /// notes on the hidden gates. A small JSON format meant to be edited by hand ("uw1-facts").
    /// The shipped UW1 file is embedded as "uw1-facts.json".
    /// </summary>
    public sealed class FactSet
    {
        public string Game = "UW1";
        public string Notes = "";
        public readonly List<Fact> Facts = new List<Fact>();

        public IEnumerable<Fact> OfKind(string kind) { return Facts.Where(f => f.Kind == kind); }

        public static FactSet Load(string path) { return FromJson(File.ReadAllText(path)); }

        /// <summary>The facts shipped with the editor (only UW1 has any), or an empty set.</summary>
        public static FactSet Shipped(bool uw2)
        {
            if (uw2) return new FactSet { Game = "UW2" };
            using (var st = typeof(FactSet).Assembly.GetManifestResourceStream("uw1-facts.json"))
            {
                if (st == null) return new FactSet();
                using (var r = new StreamReader(st)) return FromJson(r.ReadToEnd());
            }
        }

        static string S(JsonNode n, string k)
        {
            var v = n?[k];
            if (v == null) return "";
            return v.GetValueKind() == JsonValueKind.String ? (string)v : v.ToJsonString();
        }

        static int I(JsonNode n, string k, int dflt)
        {
            var v = n?[k];
            if (v == null) return dflt;
            if (v.GetValueKind() == JsonValueKind.Number) return (int)v;
            int x;
            string t = (string)v;
            if (t.StartsWith("0x", StringComparison.OrdinalIgnoreCase) && int.TryParse(t.Substring(2), System.Globalization.NumberStyles.HexNumber, null, out x)) return x;
            return int.TryParse(t, out x) ? x : dflt;
        }

        public static FactSet FromJson(string json)
        {
            var root = JsonNode.Parse(json, documentOptions: new JsonDocumentOptions { CommentHandling = JsonCommentHandling.Skip, AllowTrailingCommas = true });
            string format = S(root, "format");
            if (format.Length > 0 && !format.EndsWith("-facts")) throw new FormatException("Not a facts file (format '" + format + "').");
            var set = new FactSet { Game = S(root, "game"), Notes = S(root, "notes") };
            var arr = root?["facts"] as JsonArray;
            if (arr == null) return set;
            foreach (var n in arr)
            {
                var f = new Fact
                {
                    Id = S(n, "id"), Kind = S(n, "kind"), Grants = S(n, "grants"), Token = S(n, "token"), Requires = S(n, "requires"), Until = S(n, "until"),
                    Level = I(n, "level", 0), Object = I(n, "object", -1), Trigger = I(n, "trigger", -1), Tiles = S(n, "tiles"), Notes = S(n, "notes"), Source = S(n, "source"),
                };
                var rect = n?["rect"] as JsonArray;
                if (rect != null) f.Rect = rect.Select(x => (int)x).ToArray();
                set.Facts.Add(f);
            }
            return set;
        }

        /// <summary>The problems a hand edit may have made: unknown kinds, unreadable requirements, missing fields.</summary>
        public List<string> Check()
        {
            var list = new List<string>();
            foreach (var f in Facts)
            {
                string err;
                switch (f.Kind)
                {
                    case "craft":
                    case "flag":
                        if (f.Grants.Length == 0) list.Add(f.Id + ": no 'grants'.");
                        if (Requirement.TryParse(f.Requires, out err) == null) list.Add(f.Id + ": " + err);
                        if (f.Kind == "flag" && (f.Level <= 0 || f.Object < 0)) list.Add(f.Id + ": a flag needs 'level' and 'object'.");
                        break;
                    case "trade":
                        if (f.Token.Length == 0) list.Add(f.Id + ": no 'token'.");
                        if (Requirement.TryParse(f.Requires, out err) == null) list.Add(f.Id + ": " + err);
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
                    default:
                        list.Add(f.Id + ": unknown kind '" + f.Kind + "'.");
                        break;
                }
            }
            return list;
        }
    }
}
