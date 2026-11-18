using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;

namespace UwEditor.Core.Export
{
    /// <summary>A JSON object whose keys keep the order they were added in, so the output never depends on hashing.</summary>
    public sealed class JObj : List<KeyValuePair<string, object>>
    {
        public JObj Add(string key, object value)
        {
            Add(new KeyValuePair<string, object>(key, value));
            return this;
        }

        public object this[string key]
        {
            get
            {
                foreach (var kv in this) if (kv.Key == key) return kv.Value;
                return null;
            }
        }
    }

    /// <summary>A value written on one line, however deep it is (a tile, a vertex, a triangle), so that big lists stay one item per line.</summary>
    public sealed class JLine
    {
        public readonly object Value;
        public JLine(object value) { Value = value; }
    }

    /// <summary>
    /// The export's JSON writer. It writes the same text for the same values on every machine:
    /// keys in the order given, numbers in the invariant culture with the shortest form that reads
    /// back the same, LF line ends, two spaces an indent, non-ASCII letters as UTF-8. Values:
    /// null, bool, int, long, float, double, string, <see cref="JObj"/>, any IEnumerable&lt;object&gt;
    /// (an array) and <see cref="JLine"/>.
    /// </summary>
    public static class ArxJson
    {
        public static string Write(object value)
        {
            var sb = new StringBuilder();
            Value(sb, value, 0, false);
            sb.Append('\n');
            return sb.ToString();
        }

        static void Value(StringBuilder sb, object v, int depth, bool inline)
        {
            switch (v)
            {
                case null: sb.Append("null"); return;
                case JLine l: Value(sb, l.Value, depth, true); return;
                case bool b: sb.Append(b ? "true" : "false"); return;
                case string s: Str(sb, s); return;
                case int i: sb.Append(i.ToString(CultureInfo.InvariantCulture)); return;
                case long n: sb.Append(n.ToString(CultureInfo.InvariantCulture)); return;
                case byte by: sb.Append(by.ToString(CultureInfo.InvariantCulture)); return;
                case float f: Num(sb, f); return;
                case double d: Num(sb, d); return;
                case JObj o: Obj(sb, o, depth, inline); return;
                case System.Collections.IEnumerable e: Arr(sb, e, depth, inline); return;
            }
            throw new ArgumentException("The export cannot write a " + v.GetType().Name + ".");
        }

        static void Num(StringBuilder sb, float f)
        {
            if (float.IsNaN(f) || float.IsInfinity(f)) { sb.Append("null"); return; }
            if (f == 0f) f = 0f;   // no "-0"
            sb.Append(f.ToString("R", CultureInfo.InvariantCulture));
        }

        static void Num(StringBuilder sb, double d)
        {
            if (double.IsNaN(d) || double.IsInfinity(d)) { sb.Append("null"); return; }
            if (d == 0d) d = 0d;
            sb.Append(d.ToString("R", CultureInfo.InvariantCulture));
        }

        static void Indent(StringBuilder sb, int depth) { sb.Append('\n').Append(' ', depth * 2); }

        static void Obj(StringBuilder sb, JObj o, int depth, bool inline)
        {
            if (o.Count == 0) { sb.Append("{}"); return; }
            sb.Append('{');
            for (int i = 0; i < o.Count; i++)
            {
                if (i > 0) sb.Append(inline ? ", " : ",");
                if (!inline) Indent(sb, depth + 1);
                Str(sb, o[i].Key);
                sb.Append(": ");
                Value(sb, o[i].Value, depth + 1, inline);
            }
            if (!inline) Indent(sb, depth);
            sb.Append('}');
        }

        static void Arr(StringBuilder sb, System.Collections.IEnumerable e, int depth, bool inline)
        {
            var items = new List<object>();
            foreach (var x in e) items.Add(x);
            if (items.Count == 0) { sb.Append("[]"); return; }
            // a list of plain numbers or strings goes on one line: texture maps, children, a vertex
            bool flat = inline || items.TrueForAll(x => !(x is JObj) && !(x is JLine) && (x is string || !(x is System.Collections.IEnumerable)));
            sb.Append('[');
            for (int i = 0; i < items.Count; i++)
            {
                if (i > 0) sb.Append(flat ? ", " : ",");
                if (!flat) Indent(sb, depth + 1);
                Value(sb, items[i], depth + 1, flat);
            }
            if (!flat) Indent(sb, depth);
            sb.Append(']');
        }

        static void Str(StringBuilder sb, string s)
        {
            sb.Append('"');
            foreach (char c in s)
            {
                switch (c)
                {
                    case '"': sb.Append("\\\""); break;
                    case '\\': sb.Append("\\\\"); break;
                    case '\n': sb.Append("\\n"); break;
                    case '\r': sb.Append("\\r"); break;
                    case '\t': sb.Append("\\t"); break;
                    default:
                        if (c < 0x20) sb.Append("\\u").Append(((int)c).ToString("x4", CultureInfo.InvariantCulture));
                        else sb.Append(c);
                        break;
                }
            }
            sb.Append('"');
        }

        /// <summary>A name of the editor's ("Goal target", "HP", "Bit 0D.11") as a JSON key ("goalTarget", "hp", "bit0D11"): the first word in small letters, the others joined on with a capital.</summary>
        public static string Key(string name)
        {
            var sb = new StringBuilder();
            bool firstWord = true, wordStart = true;
            foreach (char c in name ?? "")
            {
                if (!char.IsLetterOrDigit(c))
                {
                    if (sb.Length > 0) firstWord = false;
                    wordStart = true;
                    continue;
                }
                if (firstWord) sb.Append(char.ToLowerInvariant(c));
                else sb.Append(wordStart ? char.ToUpperInvariant(c) : c);
                wordStart = false;
            }
            return sb.ToString();
        }
    }
}
