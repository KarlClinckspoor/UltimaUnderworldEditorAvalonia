using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using UltimateEditor.Games;

namespace UwEditor.Core.Conversations
{
    /// <summary>What a name belongs to: a function or label (by code address), a global (by memory address), or a function's local or argument (by frame offset).</summary>
    public enum NameKind { Function, Label, Global, Local }

    public struct NameTarget : IEquatable<NameTarget>
    {
        public NameKind Kind;
        /// <summary>The code address of a function or label, the memory address of a global, the function's start for a local.</summary>
        public int Address;
        /// <summary>For a local: its frame offset (1 and up the locals, -2 and down the arguments).</summary>
        public int Offset;

        public static NameTarget Function(int a) { return new NameTarget { Kind = NameKind.Function, Address = a }; }
        public static NameTarget Label(int a) { return new NameTarget { Kind = NameKind.Label, Address = a }; }
        public static NameTarget Global(int a) { return new NameTarget { Kind = NameKind.Global, Address = a }; }
        public static NameTarget Local(int function, int offset) { return new NameTarget { Kind = NameKind.Local, Address = function, Offset = offset }; }

        public bool Equals(NameTarget o) { return Kind == o.Kind && Address == o.Address && Offset == o.Offset; }
        public override bool Equals(object o) { return o is NameTarget t && Equals(t); }
        public override int GetHashCode() { return ((int)Kind * 397 ^ Address) * 397 ^ Offset; }
        public override string ToString()
        {
            switch (Kind)
            {
                case NameKind.Function: return "function at " + Address.ToString("X4");
                case NameKind.Label: return "label at " + Address.ToString("X4");
                case NameKind.Global: return "global at " + Address;
                default: return (Offset < 0 ? "argument " : "local ") + Offset + " of the function at " + Address.ToString("X4");
            }
        }
    }

    /// <summary>
    /// The names one conversation is shown with: the names file's, where it has one, else the
    /// placeholders the decompiler always used (func_XXXX, LXXXX, globalN, localN, argN).
    /// Function and label names are unique in the conversation (the assembler's labels are
    /// one namespace); a local's only in its function.
    /// </summary>
    public sealed class ConversationNames
    {
        internal readonly Dictionary<int, string> Functions = new Dictionary<int, string>();
        internal readonly Dictionary<int, string> Labels = new Dictionary<int, string>();
        internal readonly Dictionary<int, string> Globals = new Dictionary<int, string>();
        internal readonly Dictionary<int, Dictionary<int, string>> Locals = new Dictionary<int, Dictionary<int, string>>();
        /// <summary>The conversation's layout (null for the placeholders only).</summary>
        public BablLayout Layout { get; internal set; }
        /// <summary>The SHA-256 of the conversation's block as it is now.</summary>
        public string Sha256 { get; internal set; }
        /// <summary>A names file had this exact block (not only matching function bodies).</summary>
        public bool Exact { get; internal set; }
        /// <summary>How many functions got their names by their body, the block being another.</summary>
        public int Carried { get; internal set; }

        public static readonly ConversationNames None = new ConversationNames();

        public static string FunctionPlaceholder(int a) { return a == 0 ? "main" : "func_" + a.ToString("X4"); }
        public static string LabelPlaceholder(int a) { return "L" + a.ToString("X4"); }
        public static string GlobalPlaceholder(int a) { return "global" + a; }
        public static string LocalPlaceholder(int n)
        {
            if (n > 0) return "local" + n;
            // the caller's arguments: bp - 1 the return address, bp - 2 the last argument pushed
            return n <= -2 ? "arg" + (-n - 1) : "frame[" + n + "]";
        }

        public string Function(int a) { string n; return Functions.TryGetValue(a, out n) ? n : FunctionPlaceholder(a); }
        public string Label(int a) { string n; return Labels.TryGetValue(a, out n) ? n : LabelPlaceholder(a); }
        public string Global(int a) { string n; return Globals.TryGetValue(a, out n) ? n : GlobalPlaceholder(a); }
        public string Local(int function, int offset)
        {
            Dictionary<int, string> d; string n;
            return Locals.TryGetValue(function, out d) && d.TryGetValue(offset, out n) ? n : LocalPlaceholder(offset);
        }

        public string NameOf(NameTarget t)
        {
            switch (t.Kind)
            {
                case NameKind.Function: return Function(t.Address);
                case NameKind.Label: return Label(t.Address);
                case NameKind.Global: return Global(t.Address);
                default: return Local(t.Address, t.Offset);
            }
        }

        /// <summary>Whether the name shown is a names file's (not a placeholder).</summary>
        public bool IsNamed(NameTarget t)
        {
            switch (t.Kind)
            {
                case NameKind.Function: return Functions.ContainsKey(t.Address);
                case NameKind.Label: return Labels.ContainsKey(t.Address);
                case NameKind.Global: return Globals.ContainsKey(t.Address);
                default: return Locals.TryGetValue(t.Address, out var d) && d.ContainsKey(t.Offset);
            }
        }

        public int NamedFunctions { get { return Functions.Count; } }
        public int NamedLabels { get { return Labels.Count; } }
        public int NamedGlobals { get { return Globals.Count; } }

        static readonly Regex placeholder = new Regex(@"^(?:func_([0-9A-F]{4})|L([0-9A-F]{4})|global(\d+)|local(\d+)|arg(\d+))$");

        /// <summary>
        /// What a word of the Dialogue or Script text names, read in the function that starts
        /// at function (for its locals), or null. Imported variables and built-ins are not ours to name.
        /// </summary>
        public NameTarget? Find(Conversation c, string word, int function)
        {
            if (string.IsNullOrEmpty(word)) return null;
            if (word == "main") return NameTarget.Function(0);
            Dictionary<int, string> loc;
            if (Locals.TryGetValue(function, out loc))
                foreach (var kv in loc) if (kv.Value == word) return NameTarget.Local(function, kv.Key);
            foreach (var kv in Functions) if (kv.Value == word) return NameTarget.Function(kv.Key);
            foreach (var kv in Labels) if (kv.Value == word) return NameTarget.Label(kv.Key);
            foreach (var kv in Globals) if (kv.Value == word) return NameTarget.Global(kv.Key);
            var m = placeholder.Match(word);
            if (!m.Success) return null;
            var lay = Layout ?? BablLayout.Of(c);
            if (m.Groups[1].Success)
            {
                int a = int.Parse(m.Groups[1].Value, NumberStyles.HexNumber);
                return lay.Functions.Contains(a) && !Functions.ContainsKey(a) ? NameTarget.Function(a) : (NameTarget?)null;
            }
            if (m.Groups[2].Success)
            {
                int a = int.Parse(m.Groups[2].Value, NumberStyles.HexNumber);
                return lay.Labels.Contains(a) && !Labels.ContainsKey(a) ? NameTarget.Label(a) : (NameTarget?)null;
            }
            if (m.Groups[3].Success)
            {
                int a = int.Parse(m.Groups[3].Value, CultureInfo.InvariantCulture);
                return a < c.Variables && c.VariableAt(a) == null && !Globals.ContainsKey(a) ? NameTarget.Global(a) : (NameTarget?)null;
            }
            int off = m.Groups[4].Success ? int.Parse(m.Groups[4].Value, CultureInfo.InvariantCulture) : -1 - int.Parse(m.Groups[5].Value, CultureInfo.InvariantCulture);
            var t = NameTarget.Local(function, off);
            return IsNamed(t) ? (NameTarget?)null : t;
        }

        static readonly Regex identifier = new Regex(@"^[A-Za-z_][A-Za-z0-9_]*$");

        /// <summary>Words a name may not be: the readable view's own, and the assembler's directives and opcodes.</summary>
        static readonly HashSet<string> reserved = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            "if", "goto", "return", "end", "exit", "say", "respond", "not", "and", "or", "strcmp", "result", "bp", "mem", "frame", "main", "word",
            // the conversation language's own words (docs/conversation-language.md)
            "else", "while", "do", "break", "continue", "menu", "fmenu", "switch", "case", "default", "int", "true", "false", "streq", "ptr", "save_reg",
        };

        /// <summary>Why a name cannot be given to t in conversation c, or null when it can. An empty name, or the placeholder, clears the name.</summary>
        public string Check(Conversation c, NameTarget t, string name)
        {
            if (string.IsNullOrEmpty(name)) return null;
            if (t.Kind == NameKind.Function && t.Address == 0) return "main is the conversation's start and keeps its name.";
            if (t.Kind == NameKind.Global && c.VariableAt(t.Address) != null) return "This variable is imported by name from the game (" + c.VariableAt(t.Address).Name + "); its name is part of the conversation.";
            if (name == PlaceholderOf(t)) return null;
            if (name.Length > 48) return "Keep it to 48 characters.";
            if (!identifier.IsMatch(name)) return "A name is letters, digits and _, and does not start with a digit.";
            if (reserved.Contains(name)) return "\"" + name + "\" is a word the views use themselves.";
            BablOp op;
            if (BablOps.TryParse(name, out op)) return "\"" + name + "\" is an instruction of the script.";
            if (placeholder.IsMatch(name) || name.StartsWith("func_", StringComparison.Ordinal)) return "Names like " + name + " are kept for things without a name.";
            foreach (var im in c.Imports) if (im.Name == name) return name + " is " + (im.IsFunction ? "a built-in function" : "an imported variable") + " of this conversation.";
            foreach (var kv in All(c))
                if (kv.Value == name && !kv.Key.Equals(t) && (kv.Key.Kind != NameKind.Local || t.Kind != NameKind.Local || kv.Key.Address == t.Address))
                    return "The " + kv.Key + " is already called " + name + ".";
            return null;
        }

        public static string PlaceholderOf(NameTarget t)
        {
            switch (t.Kind)
            {
                case NameKind.Function: return FunctionPlaceholder(t.Address);
                case NameKind.Label: return LabelPlaceholder(t.Address);
                case NameKind.Global: return GlobalPlaceholder(t.Address);
                default: return LocalPlaceholder(t.Offset);
            }
        }

        /// <summary>Every name the conversation shows that is not a placeholder, with what it names.</summary>
        public IEnumerable<KeyValuePair<NameTarget, string>> All(Conversation c)
        {
            foreach (var kv in Functions) yield return new KeyValuePair<NameTarget, string>(NameTarget.Function(kv.Key), kv.Value);
            foreach (var kv in Labels) yield return new KeyValuePair<NameTarget, string>(NameTarget.Label(kv.Key), kv.Value);
            foreach (var kv in Globals) yield return new KeyValuePair<NameTarget, string>(NameTarget.Global(kv.Key), kv.Value);
            foreach (var f in Locals)
                foreach (var kv in f.Value) yield return new KeyValuePair<NameTarget, string>(NameTarget.Local(f.Key, kv.Key), kv.Value);
        }
    }

    /// <summary>One function's names in a names file: its own, its labels' (counted from its start) and its locals' (by frame offset).</summary>
    public sealed class FunctionNames
    {
        public string Name;
        /// <summary>BablLayout.Body: how the function is found again when the block is another.</summary>
        public string Body;
        public readonly SortedDictionary<int, string> Labels = new SortedDictionary<int, string>();
        public readonly SortedDictionary<int, string> Locals = new SortedDictionary<int, string>();
        /// <summary>Free text kept with the entry (what a library function does).</summary>
        public string Note;

        public bool Empty { get { return Name == null && Labels.Count == 0 && Locals.Count == 0 && Note == null; } }

        public FunctionNames Clone()
        {
            var f = new FunctionNames { Name = Name, Body = Body, Note = Note };
            foreach (var kv in Labels) f.Labels[kv.Key] = kv.Value;
            foreach (var kv in Locals) f.Locals[kv.Key] = kv.Value;
            return f;
        }
    }

    /// <summary>A slot's names in a names file, for the block whose SHA-256 it has.</summary>
    public sealed class SlotNames
    {
        /// <summary>Who talks through the slot: a comment for whoever edits the file.</summary>
        public string Talker;
        public string Sha256;
        /// <summary>By the function's start address in that block.</summary>
        public readonly SortedDictionary<int, FunctionNames> Functions = new SortedDictionary<int, FunctionNames>();
        /// <summary>Private globals, by memory address.</summary>
        public readonly SortedDictionary<int, string> Globals = new SortedDictionary<int, string>();

        public bool Empty { get { return Functions.Count == 0 && Globals.Count == 0; } }
    }

    /// <summary>
    /// A names file (docs/conversations/names-uw1.json and names-uw2.json, shipped; and the
    /// user's renames in the settings folder): JSON meant to be edited by hand.
    /// "library" names the compiler's shared functions by their body, wherever they are;
    /// "slots" names each conversation's functions (by hex address), their labels (by hex
    /// address) and locals (by frame offset), and its globals (by memory address), for the
    /// block with that "sha256".
    /// </summary>
    public sealed class NameFile
    {
        public string Game = "UW1";
        public string Notes;
        public readonly SortedDictionary<string, FunctionNames> Library = new SortedDictionary<string, FunctionNames>(StringComparer.Ordinal);
        public readonly SortedDictionary<int, SlotNames> Slots = new SortedDictionary<int, SlotNames>();

        public static NameFile Parse(string json)
        {
            var f = new NameFile();
            var root = JsonNode.Parse(json, documentOptions: new JsonDocumentOptions { CommentHandling = JsonCommentHandling.Skip, AllowTrailingCommas = true }) as JsonObject;
            if (root == null) throw new InvalidDataException("A names file is a JSON object.");
            f.Game = Str(root["game"]) ?? f.Game;
            f.Notes = Str(root["notes"]);
            if (root["library"] is JsonObject lib)
                foreach (var kv in lib)
                {
                    var fn = Function(kv.Value, 0, true);
                    fn.Body = kv.Key;
                    f.Library[kv.Key] = fn;
                }
            if (root["slots"] is JsonObject slots)
                foreach (var kv in slots)
                {
                    int slot;
                    if (!int.TryParse(kv.Key, NumberStyles.Integer, CultureInfo.InvariantCulture, out slot) || !(kv.Value is JsonObject so)) continue;
                    var s = new SlotNames { Talker = Str(so["talker"]), Sha256 = Str(so["sha256"]) };
                    if (so["functions"] is JsonObject fs)
                        foreach (var fk in fs)
                        {
                            int a;
                            if (!TryHex(fk.Key, out a)) continue;
                            s.Functions[a] = Function(fk.Value, a, false);
                        }
                    if (so["globals"] is JsonObject gs)
                        foreach (var gk in gs)
                        {
                            int a;
                            string n = Str(gk.Value);
                            if (n != null && int.TryParse(gk.Key, NumberStyles.Integer, CultureInfo.InvariantCulture, out a)) s.Globals[a] = n;
                        }
                    f.Slots[slot] = s;
                }
            return f;
        }

        /// <summary>A function entry: a bare string is just its name. Labels are hex addresses, or "+hex" from the function's start (the library's).</summary>
        static FunctionNames Function(JsonNode n, int start, bool library)
        {
            var f = new FunctionNames();
            if (n is JsonValue) { f.Name = Str(n); return f; }
            var o = n as JsonObject;
            if (o == null) return f;
            f.Name = Str(o["name"]);
            f.Body = Str(o["body"]);
            f.Note = Str(o["note"]);
            if (o["labels"] is JsonObject ls)
                foreach (var kv in ls)
                {
                    string name = Str(kv.Value);
                    if (name == null) continue;
                    int a;
                    if (kv.Key.StartsWith("+") && TryHex(kv.Key.Substring(1), out a)) f.Labels[a] = name;
                    else if (!library && TryHex(kv.Key, out a)) f.Labels[a - start] = name;
                }
            if (o["locals"] is JsonObject lo)
                foreach (var kv in lo)
                {
                    int off;
                    string name = Str(kv.Value);
                    if (name != null && int.TryParse(kv.Key, NumberStyles.AllowLeadingSign, CultureInfo.InvariantCulture, out off)) f.Locals[off] = name;
                }
            return f;
        }

        static bool TryHex(string s, out int v)
        {
            if (s.StartsWith("0x", StringComparison.OrdinalIgnoreCase)) s = s.Substring(2);
            return int.TryParse(s, NumberStyles.HexNumber, CultureInfo.InvariantCulture, out v);
        }

        static string Str(JsonNode n)
        {
            if (n is JsonValue v && v.TryGetValue(out string s)) return s;
            return null;
        }

        public string ToJson()
        {
            var root = new JsonObject { ["game"] = Game };
            if (Notes != null) root["notes"] = Notes;
            var lib = new JsonObject();
            foreach (var kv in Library) lib[kv.Key] = Write(kv.Value, 0, true);
            root["library"] = lib;
            var slots = new JsonObject();
            foreach (var kv in Slots)
            {
                if (kv.Value.Empty) continue;
                var so = new JsonObject();
                if (kv.Value.Talker != null) so["talker"] = kv.Value.Talker;
                so["sha256"] = kv.Value.Sha256;
                if (kv.Value.Globals.Count > 0)
                {
                    var g = new JsonObject();
                    foreach (var gk in kv.Value.Globals) g[gk.Key.ToString(CultureInfo.InvariantCulture)] = gk.Value;
                    so["globals"] = g;
                }
                var fs = new JsonObject();
                foreach (var fk in kv.Value.Functions) if (!fk.Value.Empty) fs[fk.Key.ToString("X4")] = Write(fk.Value, fk.Key, false);
                so["functions"] = fs;
                slots[kv.Key.ToString(CultureInfo.InvariantCulture)] = so;
            }
            root["slots"] = slots;
            string s = root.ToJsonString(new JsonSerializerOptions { WriteIndented = true, Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping });
            return Compact(s.Replace("\r\n", "\n")) + "\n";
        }

        static JsonNode Write(FunctionNames f, int start, bool library)
        {
            var o = new JsonObject();
            if (f.Name != null) o["name"] = f.Name;
            if (f.Note != null) o["note"] = f.Note;
            if (!library && f.Body != null) o["body"] = f.Body;
            if (f.Labels.Count > 0)
            {
                var l = new JsonObject();
                foreach (var kv in f.Labels) l[library ? "+" + kv.Key.ToString("X4") : (start + kv.Key).ToString("X4")] = kv.Value;
                o["labels"] = l;
            }
            if (f.Locals.Count > 0)
            {
                var l = new JsonObject();
                foreach (var kv in f.Locals) l[kv.Key.ToString(CultureInfo.InvariantCulture)] = kv.Value;
                o["locals"] = l;
            }
            return o;
        }

        /// <summary>Puts each small object (a function's labels, say) on one line, so the file reads a function a few lines.</summary>
        static string Compact(string json)
        {
            var lines = json.Split('\n');
            var o = new StringBuilder();
            for (int i = 0; i < lines.Length; i++)
            {
                string l = lines[i];
                if (l.EndsWith("{"))
                {
                    // the object up to its closing brace, when it holds only "key": "value" pairs
                    int j = i + 1;
                    var parts = new List<string>();
                    string indent = l.Substring(0, l.Length - l.TrimStart().Length);
                    bool flat = true;
                    for (; j < lines.Length; j++)
                    {
                        string t = lines[j].Trim();
                        if (t == "}" || t == "},") break;
                        if (t.EndsWith("{") || t.EndsWith("[")) { flat = false; break; }
                        parts.Add(t.TrimEnd(','));
                    }
                    if (flat && j < lines.Length && parts.Count > 0 && parts.Count <= 8 && parts.Sum(p => p.Length) < 160)
                    {
                        o.Append(l).Append(' ').Append(string.Join(", ", parts)).Append(' ').Append(lines[j].Trim()).Append('\n');
                        i = j;
                        continue;
                    }
                }
                o.Append(l).Append('\n');
            }
            return o.ToString().TrimEnd('\n');
        }
    }

    /// <summary>
    /// The names for one game's conversations: the shipped names file (embedded from
    /// docs/conversations/names-uw1.json or names-uw2.json) under the user's renames (a file
    /// in the settings folder), which come first. A slot's names apply as they are when its
    /// block is the one they were made for (the same SHA-256); for another block (the
    /// conversation was edited, or another release) they follow each function by its body
    /// (BablLayout.Body), and globals by their address. The library's names follow bodies in
    /// every slot.
    /// </summary>
    public sealed class NameBook
    {
        public GameKind Kind { get; private set; }
        public NameFile Shipped { get; private set; }
        public NameFile User { get; private set; }
        /// <summary>Where the user's renames are kept (null: not kept).</summary>
        public string UserPath { get; private set; }
        /// <summary>A problem reading a names file, or null.</summary>
        public string LoadError { get; private set; }

        public static string ShippedResource(GameKind kind) { return kind == GameKind.UW2 ? "names-uw2.json" : "names-uw1.json"; }

        /// <summary>The user's renames for a game, in a settings folder.</summary>
        public static string UserFile(string folder, GameKind kind)
        {
            return Path.Combine(folder, kind == GameKind.UW2 ? "conversation-names-uw2.json" : "conversation-names-uw1.json");
        }

        public static NameFile LoadShipped(GameKind kind)
        {
            var asm = typeof(NameBook).Assembly;
            using (var st = asm.GetManifestResourceStream(ShippedResource(kind)))
            {
                if (st == null) return new NameFile { Game = kind == GameKind.UW2 ? "UW2" : "UW1" };
                using (var r = new StreamReader(st)) return NameFile.Parse(r.ReadToEnd());
            }
        }

        public static NameBook Load(GameKind kind, string userPath, NameFile shipped = null)
        {
            var b = new NameBook { Kind = kind, UserPath = userPath };
            try { b.Shipped = shipped ?? LoadShipped(kind); }
            catch (Exception ex) { b.Shipped = new NameFile(); b.LoadError = ShippedResource(kind) + ": " + ex.Message; }
            b.User = new NameFile { Game = b.Shipped.Game, Notes = "Renames made in the editor; they come before the shipped names." };
            if (userPath != null && File.Exists(userPath))
            {
                try { b.User = NameFile.Parse(File.ReadAllText(userPath)); }
                catch (Exception ex) { b.LoadError = userPath + ": " + ex.Message; }
            }
            return b;
        }

        /// <summary>The SHA-256 of a conversation's block, as CNV.ARK holds it (uncompressed).</summary>
        public static string BlockHash(Conversation c) { return BablLayout.Hash(c.ToBytes()); }

        public ConversationNames Resolve(Conversation c)
        {
            var lay = BablLayout.Of(c);
            var r = new ConversationNames { Layout = lay, Sha256 = BlockHash(c) };
            var sources = new List<SlotNames>();
            foreach (var file in new[] { User, Shipped })
            {
                SlotNames s;
                if (file != null && file.Slots.TryGetValue(c.Slot, out s)) sources.Add(s);
            }
            // per function: the entries that name it, first first
            var entries = new Dictionary<int, List<FunctionNames>>();
            foreach (int f in lay.Functions) entries[f] = new List<FunctionNames>();
            Dictionary<int, string> bodies = null;
            Func<int, string> body = f =>
            {
                if (bodies == null) bodies = new Dictionary<int, string>();
                string b;
                if (!bodies.TryGetValue(f, out b)) bodies[f] = b = lay.Body(f);
                return b;
            };
            foreach (var s in sources)
            {
                if (s.Sha256 == r.Sha256)
                {
                    r.Exact = true;
                    foreach (var kv in s.Functions) if (entries.ContainsKey(kv.Key)) entries[kv.Key].Add(kv.Value);
                }
                else
                {
                    foreach (var kv in Match(s, lay, body))
                    {
                        if (entries[kv.Key].Count == 0 && kv.Value.Name != null) r.Carried++;
                        entries[kv.Key].Add(kv.Value);
                    }
                }
            }
            foreach (var file in new[] { User, Shipped })
            {
                if (file == null || file.Library.Count == 0) continue;
                foreach (int f in lay.Functions)
                {
                    FunctionNames fn;
                    if (file.Library.TryGetValue(body(f), out fn)) entries[f].Add(fn);
                }
            }

            var taken = new HashSet<string>(StringComparer.Ordinal) { "main" };
            foreach (var im in c.Imports) taken.Add(im.Name);
            Func<string, string> unique = n =>
            {
                string u = n;
                for (int k = 2; taken.Contains(u); k++) u = n + "_" + k;
                taken.Add(u);
                return u;
            };
            foreach (int f in lay.Functions)
            {
                if (f == 0) continue;
                var name = entries[f].Select(e => e.Name).FirstOrDefault(n => n != null);
                if (name != null) r.Functions[f] = unique(name);
            }
            foreach (int a in lay.Labels.OrderBy(x => x))
            {
                int f = lay.FunctionOf(a);
                string name = null;
                foreach (var e in entries[f]) if (e.Labels.TryGetValue(a - f, out name)) break;
                if (name != null) r.Labels[a] = unique(name);
            }
            foreach (int f in lay.Functions)
            {
                Dictionary<int, string> loc = null;
                var used = new HashSet<string>(StringComparer.Ordinal);
                foreach (var e in entries[f])
                    foreach (var kv in e.Locals)
                    {
                        if (loc != null && loc.ContainsKey(kv.Key)) continue;
                        if (loc == null) loc = new Dictionary<int, string>();
                        string n = kv.Value;
                        for (int k = 2; used.Contains(n) || taken.Contains(n); k++) n = kv.Value + "_" + k;
                        used.Add(n);
                        loc[kv.Key] = n;
                    }
                if (loc != null) r.Locals[f] = loc;
            }
            foreach (var s in sources)
                foreach (var kv in s.Globals)
                {
                    if (kv.Key < 0 || kv.Key >= c.Variables || c.VariableAt(kv.Key) != null || r.Globals.ContainsKey(kv.Key)) continue;
                    r.Globals[kv.Key] = unique(kv.Value);
                }
            return r;
        }

        /// <summary>
        /// A slot's entries matched to the functions of another block by body: the k-th function
        /// with a body gets the k-th entry with it. Entries without a body cannot follow.
        /// </summary>
        static List<KeyValuePair<int, FunctionNames>> Match(SlotNames s, BablLayout lay, Func<int, string> body)
        {
            var byBody = new Dictionary<string, Queue<FunctionNames>>(StringComparer.Ordinal);
            foreach (var kv in s.Functions)
            {
                if (kv.Value.Body == null) continue;
                Queue<FunctionNames> q;
                if (!byBody.TryGetValue(kv.Value.Body, out q)) byBody[kv.Value.Body] = q = new Queue<FunctionNames>();
                q.Enqueue(kv.Value);
            }
            var list = new List<KeyValuePair<int, FunctionNames>>();
            if (byBody.Count == 0) return list;
            foreach (int f in lay.Functions)
            {
                Queue<FunctionNames> q;
                if (byBody.TryGetValue(body(f), out q) && q.Count > 0) list.Add(new KeyValuePair<int, FunctionNames>(f, q.Dequeue()));
            }
            // main is main, whatever it became
            if (!list.Any(kv => kv.Key == 0) && s.Functions.TryGetValue(0, out var m) && m.Body == null) list.Add(new KeyValuePair<int, FunctionNames>(0, m));
            return list;
        }

        /// <summary>
        /// Gives t a new name in conversation c, in the user's renames (saved at once when they
        /// are kept in a file). An empty name, or the placeholder, drops the user's name, so the
        /// shipped one (if any) shows again. Check the name with ConversationNames.Check first.
        /// </summary>
        public void Rename(Conversation c, NameTarget t, string name)
        {
            var lay = BablLayout.Of(c);
            string sha = BlockHash(c);
            SlotNames s;
            if (!User.Slots.TryGetValue(c.Slot, out s)) User.Slots[c.Slot] = s = new SlotNames();
            if (s.Sha256 != sha)
            {
                // the user's names follow the functions into this block before the new one is added
                var moved = new SlotNames { Talker = s.Talker, Sha256 = sha };
                if (s.Sha256 == null) { foreach (var kv in s.Functions) moved.Functions[kv.Key] = kv.Value; }
                else foreach (var kv in Match(s, lay, lay.Body)) moved.Functions[kv.Key] = kv.Value.Clone();
                foreach (var kv in s.Globals) moved.Globals[kv.Key] = kv.Value;
                User.Slots[c.Slot] = s = moved;
            }
            if (string.IsNullOrEmpty(name) || name == ConversationNames.PlaceholderOf(t)) name = null;
            if (t.Kind == NameKind.Global)
            {
                if (name == null) s.Globals.Remove(t.Address); else s.Globals[t.Address] = name;
            }
            else
            {
                int f = t.Kind == NameKind.Function ? t.Address : t.Kind == NameKind.Local ? t.Address : lay.FunctionOf(t.Address);
                FunctionNames fn;
                if (!s.Functions.TryGetValue(f, out fn)) s.Functions[f] = fn = new FunctionNames();
                fn.Body = lay.Body(f);
                if (t.Kind == NameKind.Function) fn.Name = name;
                else if (t.Kind == NameKind.Label) { if (name == null) fn.Labels.Remove(t.Address - f); else fn.Labels[t.Address - f] = name; }
                else { if (name == null) fn.Locals.Remove(t.Offset); else fn.Locals[t.Offset] = name; }
                if (fn.Empty) s.Functions.Remove(f);
            }
            if (s.Empty) User.Slots.Remove(c.Slot);
            SaveUser();
        }

        public void SaveUser()
        {
            if (UserPath == null) return;
            Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(UserPath)));
            string tmp = UserPath + ".tmp";
            File.WriteAllText(tmp, User.ToJson());
            File.Move(tmp, UserPath, true);
        }

        /// <summary>
        /// The shipped names with the user's renames folded in, in the shipped file's format.
        /// A rename made in an edited block goes to the shipped block's function with the same
        /// body, found in the shipped file's entries or, given original (a slot's block as the
        /// game has it), in that block when it is the shipped one; one with no such function
        /// (new code) is left out.
        /// </summary>
        public NameFile Export(Func<int, Conversation> original = null)
        {
            var o = NameFile.Parse(Shipped.ToJson());
            foreach (var kv in User.Library) o.Library[kv.Key] = kv.Value.Clone();
            foreach (var us in User.Slots)
            {
                SlotNames ss;
                if (!o.Slots.TryGetValue(us.Key, out ss) || ss.Sha256 == null || ss.Sha256 == us.Value.Sha256)
                {
                    if (ss == null) o.Slots[us.Key] = ss = new SlotNames { Sha256 = us.Value.Sha256, Talker = us.Value.Talker };
                    foreach (var kv in us.Value.Functions) Overlay(ss, kv.Key, kv.Value);
                }
                else
                {
                    // by body onto the shipped block's functions
                    var where = new Dictionary<string, Queue<int>>(StringComparer.Ordinal);
                    Conversation oc = null;
                    try { oc = original == null ? null : original(us.Key); } catch (Exception) { }
                    if (oc != null && BlockHash(oc) == ss.Sha256)
                    {
                        var ol = BablLayout.Of(oc);
                        foreach (int f in ol.Functions)
                        {
                            string b = ol.Body(f);
                            if (!where.ContainsKey(b)) where[b] = new Queue<int>();
                            where[b].Enqueue(f);
                        }
                    }
                    else
                        foreach (var kv in ss.Functions)
                            if (kv.Value.Body != null)
                            {
                                if (!where.ContainsKey(kv.Value.Body)) where[kv.Value.Body] = new Queue<int>();
                                where[kv.Value.Body].Enqueue(kv.Key);
                            }
                    foreach (var kv in us.Value.Functions)
                    {
                        Queue<int> q;
                        if (kv.Key == 0 && kv.Value.Body == null) Overlay(ss, 0, kv.Value);
                        else if (kv.Value.Body != null && where.TryGetValue(kv.Value.Body, out q) && q.Count > 0) Overlay(ss, q.Dequeue(), kv.Value);
                    }
                }
                foreach (var g in us.Value.Globals) ss.Globals[g.Key] = g.Value;
            }
            return o;
        }

        static void Overlay(SlotNames s, int at, FunctionNames u)
        {
            FunctionNames fn;
            if (!s.Functions.TryGetValue(at, out fn)) s.Functions[at] = fn = new FunctionNames { Body = u.Body };
            if (u.Name != null) fn.Name = u.Name;
            if (u.Note != null) fn.Note = u.Note;
            if (fn.Body == null) fn.Body = u.Body;
            foreach (var kv in u.Labels) fn.Labels[kv.Key] = kv.Value;
            foreach (var kv in u.Locals) fn.Locals[kv.Key] = kv.Value;
        }
    }
}
