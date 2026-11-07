using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;
using UltimateEditor.Games;

namespace UwEditor.Core.Conversations
{
    public sealed class CompileOptions
    {
        /// <summary>Compile every function from its source, copying none (the proofs use it; the editor keeps unchanged functions as they were).</summary>
        public bool CompileAll;
        /// <summary>The accent table the source's strings are written with: a letter it has no code for is an error. Null: no check.</summary>
        public AccentTable Accents;
    }

    /// <summary>A string of the conversation's block: its number and text.</summary>
    public sealed class StringChange
    {
        public int Id;
        public string Text;
        public override string ToString() { return "#" + Id + " " + Decompiler.Quote(Text); }
    }

    /// <summary>Where a function of the source went in the compiled code, with the names its source gave its labels, locals and arguments.</summary>
    public sealed class PlacedFunction
    {
        public string Name;
        public int Address;
        /// <summary>Compiled from the source (false: copied from the old code as it was).</summary>
        public bool Compiled;
        /// <summary>The labels of the source (name: its address), for a compiled function.</summary>
        public readonly Dictionary<string, int> Labels = new Dictionary<string, int>(StringComparer.Ordinal);
        /// <summary>The locals and arguments of the source (frame offset: name, as NameTarget.Local counts them), for a compiled function.</summary>
        public readonly Dictionary<int, string> Locals = new Dictionary<int, string>();
    }

    /// <summary>What compiling a conversation's source gives: the new conversation and its strings, or the errors.</summary>
    public sealed class CompileResult
    {
        public readonly List<SourceError> Errors = new List<SourceError>();
        /// <summary>Things that compile but may not be meant (an argument count the games never use for a script function, say).</summary>
        public readonly List<SourceError> Warnings = new List<SourceError>();
        public bool Ok { get { return Errors.Count == 0; } }
        /// <summary>The compiled conversation (null when there are errors).</summary>
        public Conversation Conversation;
        /// <summary>The conversation's string block after compiling: the old strings, then the new ones.</summary>
        public List<string> Strings;
        /// <summary>Strings the source added to the end of the block.</summary>
        public readonly List<StringChange> AddedStrings = new List<StringChange>();
        /// <summary>Strings the old code used and the new code does not. They stay in the block: something else may still use them.</summary>
        public readonly List<StringChange> UnusedStrings = new List<StringChange>();
        public bool StringsChanged { get { return AddedStrings.Count > 0; } }
        /// <summary>The assembly the compiler wrote (Assembler.Assemble reads it into Conversation).</summary>
        public string Assembly;
        /// <summary>Functions copied from the old code because their source did not change, and functions compiled from the source.</summary>
        public readonly List<string> CopiedFunctions = new List<string>(), CompiledFunctions = new List<string>();
        /// <summary>Every function of the source in the compiled code, in order (NameBook.Adopt keeps the source's names for them).</summary>
        public readonly List<PlacedFunction> Placed = new List<PlacedFunction>();
    }

    /// <summary>
    /// The compiler from the conversation language (docs/conversation-language.md) to the
    /// conversation machine's code (conv/BABL.C babl_run), through the assembler
    /// (Assembly.cs). It codes each construct the way Origin's compiler did (the table in the
    /// language's "From the language to the code"), so a function decompiled and compiled
    /// back mostly gives the same bytes; and a function whose source did not change is copied
    /// from the old code as it was, so an edit changes only the functions edited.
    /// </summary>
    public static class ConversationCompiler
    {
        /// <summary>
        /// Compiles a conversation's source. original is the conversation as it is now (its
        /// header, imports and code: unchanged functions are copied from it), strings its
        /// string block, names the names its source was shown with (NameBook.Resolve).
        /// </summary>
        public static CompileResult Compile(Conversation original, IList<string> strings, GameKind kind, ConversationNames names, string source, CompileOptions options = null)
        {
            var job = new Job(original, strings, kind, names ?? ConversationNames.None, options ?? new CompileOptions());
            job.Run(source);
            return job.Result;
        }

        /// <summary>The biggest CNV.ARK block the game reads (ConversationArchive.MaxBlockBytes).</summary>
        public static int MaxBlockBytes(GameKind kind) { return ConversationArchive.MaxBlockBytesFor(kind); }

        /// <summary>The most choices a menu shows: the game keeps the shown choices' strings in bablOptionIds[10], from 1 (conv/CONVERSE.C).</summary>
        public const int MaxMenuChoices = 9;
        /// <summary>The menu's arrays are 21 words (Origin's compiler): 20 choices and the 0 after them.</summary>
        public const int MenuArray = 21;

        /// <summary>
        /// How many arguments each built-in takes: what the game reads (conv/CONVERSE.C,
        /// BABLHACK.C, BARTER.C, BABL.C: args[-1] to args[-n]), widened to the counts the
        /// games' own code passes (do_offer 5 to 7, babl_hack 1 to 4, a few calls with one
        /// argument more or less). Built-ins not listed are not checked.
        /// </summary>
        static readonly Dictionary<string, int[]> arity = new Dictionary<string, int[]>(StringComparer.Ordinal)
        {
            { "babl_menu", new[] { 1, 1 } }, { "babl_fmenu", new[] { 2, 2 } }, { "babl_ask", new[] { 0, 0 } },
            { "print", new[] { 1, 1 } }, { "pause", new[] { 0, 1 } }, { "sex", new[] { 2, 2 } }, { "show_inv", new[] { 2, 2 } },
            { "find_barter", new[] { 1, 1 } }, { "find_barter_total", new[] { 4, 4 } }, { "give_to_npc", new[] { 2, 2 } },
            { "give_ptr_npc", new[] { 2, 2 } }, { "do_inv_delete", new[] { 1, 1 } }, { "find_inv", new[] { 2, 2 } },
            { "take_from_npc", new[] { 1, 1 } }, { "take_id_from_npc", new[] { 1, 1 } }, { "do_inv_create", new[] { 1, 1 } },
            { "identify_inv", new[] { 4, 4 } }, { "count_inv", new[] { 1, 1 } }, { "check_inv_quality", new[] { 1, 1 } },
            { "set_inv_quality", new[] { 2, 2 } }, { "switch_pic", new[] { 1, 1 } }, { "setup_to_barter", new[] { 0, 0 } },
            { "do_offer", new[] { 5, 7 } }, { "do_demand", new[] { 2, 3 } }, { "do_decline", new[] { 0, 0 } },
            { "do_judgement", new[] { 0, 0 } }, { "end_barter", new[] { 0, 0 } }, { "set_likes_dislikes", new[] { 2, 2 } },
            { "give_all_stuff", new[] { 0, 0 } }, { "get_quest", new[] { 1, 2 } }, { "set_quest", new[] { 1, 2 } },
            { "x_skills", new[] { 2, 2 } }, { "x_traps", new[] { 2, 2 } }, { "x_clock", new[] { 2, 2 } }, { "x_exp", new[] { 1, 1 } },
            { "set_attitude", new[] { 1, 2 } }, { "set_race_attitude", new[] { 1, 3 } }, { "gronk_door", new[] { 3, 3 } },
            { "take_from_npc_inv", new[] { 1, 1 } }, { "add_to_npc_inv", new[] { 1, 1 } }, { "place_object", new[] { 3, 3 } },
            { "remove_talker", new[] { 0, 0 } }, { "transform_talker", new[] { 4, 4 } }, { "set_sequence", new[] { 3, 3 } },
            { "teleport_player", new[] { 3, 3 } }, { "teleport_talker", new[] { 2, 2 } }, { "x_obj_stuff", new[] { 9, 9 } },
            { "x_obj_pos", new[] { 5, 5 } }, { "babl_hack", new[] { 1, 4 } }, { "do_input_wait", new[] { 0, 0 } },
            { "compare", new[] { 2, 2 } }, { "random", new[] { 1, 1 } }, { "plural", new[] { 3, 3 } }, { "contains", new[] { 1, 2 } },
            { "append", new[] { 2, 2 } }, { "copy", new[] { 1, 1 } }, { "find", new[] { 2, 2 } }, { "length", new[] { 1, 1 } },
            { "val", new[] { 1, 1 } },
        };

        /// <summary>The argument counts a built-in takes (null: not checked).</summary>
        public static int[] Arity(string builtin) { int[] a; return arity.TryGetValue(builtin, out a) ? (int[])a.Clone() : null; }

        /// <summary>
        /// The choice Origin's compiler made where the language allows two codings, read from
        /// the old code: -n as PUSHI n NEG (all of UW1, some UW2 functions) or as PUSHI -n
        /// (most of UW2). It differs from function to function within a conversation, so a
        /// function takes its own old code's way, and a new one the conversation's.
        /// </summary>
        static bool PushesNegative(List<BablInstruction> code, int from, int to, bool otherwise)
        {
            int neg = 0, pushed = 0;
            for (int i = 0; i < code.Count; i++)
            {
                var ins = code[i];
                if (ins.Address < from || ins.Address >= to || !ins.Known || ins.Op != BablOp.PUSHI) continue;
                if (i + 1 < code.Count && code[i + 1].Known && code[i + 1].Op == BablOp.NEG) neg++;
                else if ((short)ins.Operand < 0 && (short)ins.Operand > -1000) pushed++;
            }
            return pushed == neg ? otherwise : pushed > neg;
        }

        // ----- one compilation -----

        enum SymKind { Global, Local, Arg, Builtin, Function }

        sealed class Sym
        {
            public SymKind Kind;
            public int Address;       // global's address, local's slot, argument's frame offset, built-in's number
            public int Size = 1;
            public bool Array;
            public BablImport Import;
            public string Name;
        }

        /// <summary>A function of the new source and how it is made.</summary>
        sealed class Unit : SNodeBase
        {
            public string Name;
            public SourceChunk Chunk;
            public SourceFunction F;
            public string Label;
            /// <summary>The old function copied as it was (its start in the old code), or -1.</summary>
            public int CopyFrom = -1;
            /// <summary>A compiled function's labels (source name: assembly label) and variables (frame offset: name).</summary>
            public Dictionary<string, string> Labels;
            public Dictionary<int, string> Locals;
        }

        sealed class Job
        {
            readonly Conversation orig;
            readonly List<string> strings;
            readonly int originalStrings;
            readonly GameKind kind;
            readonly ConversationNames names;
            readonly CompileOptions options;
            public readonly CompileResult Result = new CompileResult();
            readonly bool pushNegative;
            readonly Dictionary<string, int> startOf = new Dictionary<string, int>(StringComparer.Ordinal);

            readonly Dictionary<string, int> globalsByName = new Dictionary<string, int>(StringComparer.Ordinal);
            readonly Dictionary<string, Unit> units = new Dictionary<string, Unit>(StringComparer.Ordinal);
            readonly HashSet<int> usedStrings = new HashSet<int>();

            // the old code
            BablLayout lay;
            readonly Dictionary<int, string> origName = new Dictionary<int, string>();   // old function start → name
            readonly Dictionary<int, int> origEnd = new Dictionary<int, int>();
            /// <summary>Where the old code's jumps go (labels for the functions copied).</summary>
            HashSet<int> targets;

            // the assembly being written
            readonly StringBuilder asm = new StringBuilder();
            int pc;
            readonly Dictionary<string, int> labelAt = new Dictionary<string, int>(StringComparer.Ordinal);
            readonly List<Tuple<string, int, string, SNodeBase>> jumps = new List<Tuple<string, int, string, SNodeBase>>();  // op, at, label, where
            int gen;

            public Job(Conversation original, IList<string> strings, GameKind kind, ConversationNames names, CompileOptions options)
            {
                orig = original;
                this.strings = strings == null ? new List<string>() : new List<string>(strings);
                originalStrings = this.strings.Count;
                this.kind = kind;
                this.names = names;
                this.options = options;
                pushNegative = PushesNegative(BablCode.Decode(original.Code), 0, original.Code.Length, false);
            }

            void Error(int line, int col, string message) { Result.Errors.Add(new SourceError(line, col, message)); }
            void Error(SNodeBase at, string message) { Error(at == null ? 1 : at.Line, at == null ? 1 : at.Col, message); }
            void Warn(SNodeBase at, string message) { Result.Warnings.Add(new SourceError(at.Line, at.Col, message)); }

            public void Run(string source)
            {
                var errors = new List<SourceError>();
                var prog = ConversationSource.Parse(source, errors);
                var chunks = SourceChunk.Split(prog.Tokens);

                // the old code's functions, and their source as the view shows it (for telling which changed)
                lay = names.Layout ?? BablLayout.Of(orig);
                var starts = lay.Functions.ToList();
                for (int k = 0; k < starts.Count; k++)
                {
                    origName[starts[k]] = names.Function(starts[k]);
                    startOf[origName[starts[k]]] = starts[k];
                    origEnd[starts[k]] = k + 1 < starts.Count ? starts[k + 1] : orig.Code.Length;
                }
                var shapes = new Dictionary<string, string>(StringComparer.Ordinal);
                var oldStrings = new HashSet<int>();
                try
                {
                    var view = Decompiler.Decompile(orig, strings.Take(originalStrings).ToArray(), kind, true, names);
                    var reference = ConversationSource.Parse(view.Text, new List<SourceError>());
                    foreach (var ch in SourceChunk.Split(reference.Tokens))
                        if (startOf.ContainsKey(ch.Name)) shapes[ch.Name] = reference.Shape(ch.First, ch.Last);
                    // the strings the old code's source used, for the report of strings no longer used
                    foreach (var tk in reference.Tokens) if (tk.Kind == ConversationSource.T.StringId) oldStrings.Add(tk.Value);
                }
                catch (Exception) { shapes.Clear(); }

                // globals by name: the imported variables, the names files' private globals, globalN
                foreach (var im in orig.Imports) if (!im.IsFunction && im.Name != null && !globalsByName.ContainsKey(im.Name)) globalsByName[im.Name] = im.Index;
                for (int a = 0; a < orig.Variables; a++)
                {
                    if (orig.VariableAt(a) != null) continue;
                    string n = names.Global(a);
                    if (!globalsByName.ContainsKey(n)) globalsByName[n] = a;
                }

                // the functions, found by their braces: each name once, the first is where the conversation starts.
                // A function whose text did not change is the old code, copied (even if the language cannot say all it does).
                var order = new List<Unit>();
                for (int k = 0; k < chunks.Count; k++)
                {
                    var ch = chunks[k];
                    var u = new Unit { Name = ch.Name, Line = ch.Line, Col = ch.Col, Label = "F_" + ch.Name, Chunk = ch };
                    u.F = prog.Functions.FirstOrDefault(f => f.FirstToken == ch.First);
                    if (units.ContainsKey(ch.Name)) { Error(u, "there are two functions called " + ch.Name + "."); continue; }
                    string shape; int old;
                    if (!options.CompileAll && shapes.TryGetValue(ch.Name, out shape) && shape == prog.Shape(ch.First, ch.Last) && startOf.TryGetValue(ch.Name, out old)
                        && (k == 0) == (old == 0))   // the start of the conversation stays the start
                        u.CopyFrom = old;
                    if (u.CopyFrom < 0 && orig.FunctionNamed(ch.Name) != null) Warn(u, ch.Name + " is also a built-in's name; calls to " + ch.Name + " call this function.");
                    units[ch.Name] = u;
                    order.Add(u);
                }
                // the parser's errors, but for those inside functions copied as they were
                foreach (var e in errors)
                    if (!order.Any(u => u.CopyFrom >= 0 && u.Chunk.Holds(e.Line, e.Column))) Result.Errors.Add(e);
                if (order.Count == 0 && Result.Ok) Error(1, 1, "there are no functions: a conversation needs at least main.");
                foreach (var u in order) if (u.CopyFrom < 0 && u.F == null && Result.Ok) Error(u, "the function " + u.Name + " could not be read.");
                if (!Result.Ok) return;

                var header = orig.Clone();
                header.Code = new ushort[0];
                asm.Append(Disassembler.Disassemble(header, null, "compiled from the conversation's source"));
                for (int k = 0; k < order.Count; k++)
                {
                    var u = order[k];
                    if (u.CopyFrom >= 0)
                    {
                        Copy(u);
                        Result.CopiedFunctions.Add(u.Name);
                        for (int i = u.Chunk.First; i <= u.Chunk.Last; i++) if (prog.Tokens[i].Kind == ConversationSource.T.StringId) usedStrings.Add(prog.Tokens[i].Value);
                    }
                    else { new FunctionCoder(this, u, k == 0).Code(); Result.CompiledFunctions.Add(u.Name); }
                }
                if (!Result.Ok) return;

                // jumps: every label there, and in range
                foreach (var j in jumps)
                {
                    int target;
                    if (!labelAt.TryGetValue(j.Item3, out target)) { Error(j.Item4, "no label " + j.Item3 + " (a jump the compiler wrote; please report it)."); continue; }
                    BablOp op; BablOps.TryParse(j.Item1, out op);
                    if (BablOps.IsRelative(op))
                    {
                        int d = target - j.Item2 - 1;
                        if (d < -0x8000 || d > 0x7FFF) Error(j.Item4, "the jump is out of range (" + d + " words; BEQ and BRA reach 32767 either way).");
                    }
                }
                if (pc > 0xFFFF) Error(1, 1, "the code is " + pc + " words; jumps reach only 65535.");
                if (!Result.Ok) return;

                Result.Assembly = asm.ToString();
                Conversation c;
                try { c = Assembler.Assemble(Result.Assembly); }
                catch (AssemblyError e) { Error(1, 1, "the assembler refused the compiled code (" + e.Message + "); please report it."); return; }
                c.Slot = orig.Slot; c.First = orig.First; c.HeaderWord = orig.HeaderWord; c.StringBlock = orig.StringBlock; c.Variables = orig.Variables;
                c.Imports = orig.Clone().Imports;
                c.Trailer = (byte[])orig.Trailer.Clone();
                int bytes = c.ToBytes().Length, max = MaxBlockBytes(kind);
                if (bytes > max) Error(1, 1, string.Format("the conversation is {0} bytes; the game reads at most {1} (0x{1:X}) for a conversation.", bytes, max));
                if (!Result.Ok) return;

                Result.Conversation = c;
                Result.Strings = strings;
                foreach (var u in order)
                {
                    var p = new PlacedFunction { Name = u.Name, Address = labelAt[u.Label], Compiled = u.CopyFrom < 0 };
                    if (u.Labels != null) foreach (var kv in u.Labels) { int a; if (labelAt.TryGetValue(kv.Value, out a)) p.Labels[kv.Key] = a; }
                    if (u.Locals != null) foreach (var kv in u.Locals) p.Locals[kv.Key] = kv.Value;
                    Result.Placed.Add(p);
                }
                for (int i = originalStrings; i < strings.Count; i++) Result.AddedStrings.Add(new StringChange { Id = i, Text = strings[i] });
                foreach (int id in oldStrings.OrderBy(x => x))
                    if (!usedStrings.Contains(id) && id < originalStrings) Result.UnusedStrings.Add(new StringChange { Id = id, Text = strings[id] });
            }

            /// <summary>The number of a string of the source: the one written after it when the text is that string's, else the first string with the text, else a new string at the end of the block.</summary>
            public int StringId(StringE s)
            {
                int id;
                if (s.Text != null && options.Accents != null)
                {
                    string problem = options.Accents.Problem(s.Text);
                    if (problem != null) Error(s, problem);
                }
                if (s.Text == null)
                {
                    id = s.Id;
                    if (id < 0 || id >= strings.Count) Error(s, "there is no string #" + id + " in this conversation's block (it has " + strings.Count + ").");
                }
                else if (s.Id >= 0 && s.Id < strings.Count && strings[s.Id] == s.Text) id = s.Id;
                else
                {
                    id = strings.IndexOf(s.Text);
                    if (id < 0) { id = strings.Count; strings.Add(s.Text); }
                }
                usedStrings.Add(id);
                return id;
            }

            // ----- writing the assembly -----

            public void Label(string name)
            {
                labelAt[name] = pc;
                asm.Append(name).Append(":\n");
            }

            public void Emit(string op, string operand = null, SNodeBase at = null)
            {
                asm.Append("    ").Append(op);
                if (operand != null) asm.Append(' ', Math.Max(1, 10 - op.Length)).Append(operand);
                asm.Append('\n');
                BablOp o;
                BablOps.TryParse(op, out o);
                if (BablOps.IsJump(o) && !operand.StartsWith("@")) jumps.Add(Tuple.Create(op, pc, operand, at));
                pc += operand != null ? 2 : 1;
            }

            public void Patch(string placeholder, string value) { asm.Replace(placeholder, value); }

            public void Word(int w) { asm.Append("    .word 0x").Append(w.ToString("X4")).Append('\n'); pc++; }

            public void Comment(string text) { asm.Append("    ; ").Append(text).Append('\n'); }

            public string NewLabel() { return "G_" + (++gen); }

            public Unit UnitNamed(string name) { Unit u; return units.TryGetValue(name, out u) ? u : null; }

            public int? GlobalNamed(string name)
            {
                int a;
                if (globalsByName.TryGetValue(name, out a)) return a;
                return null;
            }

            public Conversation Original { get { return orig; } }
            public GameKind Kind { get { return kind; } }
            /// <summary>Whether the function writes -n as PUSHI -n: as its old code did, else as the conversation does.</summary>
            public bool PushNegative(string function)
            {
                int start;
                if (!startOf.TryGetValue(function, out start)) return pushNegative;
                return PushesNegative(lay.Code, start, origEnd[start], pushNegative);
            }

            public void AddError(SNodeBase at, string message) { Error(at, message); }
            public void AddError(int line, int col, string message) { Error(line, col, message); }
            public void AddWarning(SNodeBase at, string message) { Warn(at, message); }

            /// <summary>An old function copied as it was: its jumps and calls by label, so it can move.</summary>
            void Copy(Unit u)
            {
                int start = u.CopyFrom, end = origEnd[start];
                if (targets == null)
                {
                    targets = new HashSet<int>();
                    foreach (var ins in lay.Code) if (ins.Known && (BablOps.IsRelative(ins.Op) || ins.Op == BablOp.JMP)) targets.Add(ins.Target);
                }
                asm.Append('\n');
                Comment(u.Name + ": the old code, unchanged (was at 0x" + start.ToString("X4") + ")");
                Label(u.Label);
                for (int i = lay.At[start]; i < lay.Code.Count && lay.Code[i].Address < end; i++)
                {
                    var ins = lay.Code[i];
                    if (ins.Address != start && targets.Contains(ins.Address)) Label("O_" + ins.Address.ToString("X4"));
                    if (!ins.Known) { Word(ins.Word); continue; }
                    if (!ins.HasOperand) { Emit(ins.Op.ToString()); continue; }
                    if (BablOps.IsJump(ins.Op)) { Emit(ins.Op.ToString(), CopyTarget(u, ins), u); continue; }
                    Emit(ins.Op.ToString(), ((short)ins.Operand).ToString(CultureInfo.InvariantCulture));
                }
            }

            string CopyTarget(Unit u, BablInstruction ins)
            {
                int t = ins.Target;
                string name;
                if (origName.TryGetValue(t, out name))
                {
                    var target = UnitNamed(name);
                    if (target != null) return target.Label;
                    Error(u, u.Name + (ins.Op == BablOp.CALL ? " calls " : " jumps to ") + name + ", which the source no longer has.");
                    return "@0x0000";
                }
                if (ins.Op == BablOp.CALL || !lay.At.ContainsKey(t)) return "@0x" + t.ToString("X4");
                int fs = lay.FunctionOf(t);
                var owner = UnitNamed(origName[fs]);
                if (owner != null && owner.CopyFrom == fs) return "O_" + t.ToString("X4");
                Error(u, u.Name + " jumps into " + origName[fs] + (owner == null ? ", which the source no longer has." : ", which was changed: change " + u.Name + " too (its jump is not in the language)."));
                return "@0x0000";
            }
        }

        // ----- one function -----

        sealed class Scope { public string Break, Continue; }

        sealed class FunctionCoder
        {
            readonly Job job;
            readonly Unit u;
            readonly SourceFunction f;
            readonly bool first;
            readonly Dictionary<string, Sym> vars = new Dictionary<string, Sym>(StringComparer.Ordinal);
            readonly Dictionary<string, string> labels = new Dictionary<string, string>(StringComparer.Ordinal);
            readonly Stack<Scope> loops = new Stack<Scope>();
            int declared, temps;
            bool returnsValue, pushNegative;
            string exitLabel, endLabel;

            public FunctionCoder(Job job, Unit u, bool first) { this.job = job; this.u = u; f = u.F; this.first = first; }

            void Error(SNodeBase at, string message) { job.AddError(at, message); }

            public void Code()
            {
                // arguments: the last is arg1, at bp - 2
                for (int k = 0; k < f.Params.Count; k++)
                {
                    var p = f.Params[k];
                    if (vars.ContainsKey(p.Name)) { job.AddError(p.Line, p.Col, "two arguments are called " + p.Name + "."); continue; }
                    vars[p.Name] = new Sym { Kind = SymKind.Arg, Address = -1 - (f.Params.Count - k), Name = p.Name };
                }
                int slot = 1;
                foreach (var v in f.Locals)
                {
                    if (vars.ContainsKey(v.Name)) { job.AddError(v.Line, v.Col, "there are two variables called " + v.Name + " in " + f.Name + "."); continue; }
                    vars[v.Name] = new Sym { Kind = SymKind.Local, Address = slot, Size = v.Size, Array = v.Array, Name = v.Name };
                    slot += v.Size;
                }
                declared = slot - 1;
                returnsValue = HasValueReturn(f.Body);
                pushNegative = job.PushNegative(f.Name);
                // a function that returns a value keeps it in local 1 (Origin's compiler; conv/BABL.C only sees SAVE_REG)
                if (returnsValue && declared == 0) declared = 1;
                // a number of the function's own first: "L_" + function + "_" + label made zz_a's b_c and zz_a_b's c the same word
                labelPrefix = "L" + job.NewLabel().Substring(2) + "_" + f.Name + "_";
                CollectLabels(f.Body);

                exitLabel = job.NewLabel(); endLabel = job.NewLabel();
                job.Comment("");
                job.Label(u.Label);
                if (first) job.Emit("START");
                job.Emit("PUSHBP");
                job.Emit("SPTOBP");
                // the frame is known only at the end: patched below
                job.Emit("PUSHI", FramePlaceholder);
                job.Emit("ADDSP");
                var stmts = f.Body;
                // "return local1;" at the very end of a function that returns a value: the exit itself (the view shows it when a goto goes there)
                int n = stmts.Count;
                if (returnsValue && n > 0 && stmts[n - 1] is ReturnS r && IsResultLocal(r.Value)) n--;
                Block(stmts.Take(n).ToList());
                if (returnsValue)
                {
                    job.Label(exitLabel);
                    job.Emit("PUSHI_EFF", "1");
                    job.Emit("FETCHM");
                    job.Emit("SAVE_REG");
                    job.Emit("POP");
                }
                job.Label(endLabel);
                job.Emit("BPTOSP");
                job.Emit("POPBP");
                job.Emit("RET");
                // the frame's size is pushed as a word the game reads signed
                if (declared + temps > short.MaxValue) job.AddError(f.Line, f.Col, f.Name + " needs " + (declared + temps) + " words of variables; a function has at most " + short.MaxValue + ".");
                job.Patch(FramePlaceholder, (declared + temps).ToString(CultureInfo.InvariantCulture));
                u.Labels = new Dictionary<string, string>(labels, StringComparer.Ordinal);
                u.Locals = vars.Values.Where(v => v.Kind == SymKind.Local || v.Kind == SymKind.Arg).ToDictionary(v => v.Address, v => v.Name);
            }

            const string FramePlaceholder = "#FRAME#";

            bool IsResultLocal(SExpr e)
            {
                var n = e as NameE;
                if (n == null || e.Paren) return false;
                Sym s;
                return vars.TryGetValue(n.Name, out s) && s.Kind == SymKind.Local && s.Address == 1 && !s.Array;
            }

            static bool HasValueReturn(List<SStmt> body)
            {
                bool found = false;
                Action<List<SStmt>> walk = null;
                walk = list =>
                {
                    if (list == null) return;
                    foreach (var s in list)
                    {
                        switch (s)
                        {
                            case ReturnS r: if (r.Value != null) found = true; break;
                            case IfS i: walk(i.Then); walk(i.Else); break;
                            case WhileS w: walk(w.Body); break;
                            case MenuS m: foreach (var c in m.Choices) walk(c.Body); walk(m.Default); break;
                            case SwitchS sw: foreach (var c in sw.Cases) walk(c.Body); walk(sw.Default); break;
                        }
                    }
                };
                walk(body);
                return found;
            }

            string labelPrefix;

            void CollectLabels(List<SStmt> body)
            {
                if (body == null) return;
                foreach (var s in body)
                {
                    switch (s)
                    {
                        case LabelS l:
                            if (labels.ContainsKey(l.Name)) Error(l, "the label " + l.Name + " is in " + f.Name + " twice.");
                            else labels[l.Name] = labelPrefix + l.Name;
                            break;
                        case IfS i: CollectLabels(i.Then); CollectLabels(i.Else); break;
                        case WhileS w: CollectLabels(w.Body); break;
                        case MenuS m: foreach (var c in m.Choices) CollectLabels(c.Body); CollectLabels(m.Default); break;
                        case SwitchS sw: foreach (var c in sw.Cases) CollectLabels(c.Body); CollectLabels(sw.Default); break;
                    }
                }
            }

            int Temp(int size = 1)
            {
                int t = declared + temps + 1;
                temps += size;
                return t;
            }

            // ----- statements -----

            void Block(List<SStmt> list)
            {
                if (list == null) return;
                foreach (var s in list) Statement(s);
            }

            void Statement(SStmt s)
            {
                job.Comment("line " + s.Line);
                switch (s)
                {
                    case AssignS a:
                        Address(a.Target, true);
                        Value(a.Value);
                        job.Emit("STO");
                        break;
                    case CallS c: Call(c.Call); break;
                    case SayS y:
                        Value(y.Text);
                        job.Emit(y.Respond ? "RESPOND" : "SAY");
                        break;
                    case IfS i: If(i); break;
                    case WhileS w: While(w); break;
                    case JumpS j: Jump(j); break;
                    case ReturnS r:
                        if (r.Value == null) { job.Emit("JMP", endLabel, s); break; }
                        if (first) Warn(s, "the conversation's start returns a value: nothing reads it.");
                        // Origin's compiler: the value, then into local 1 as a temporary is stored, then to the exit
                        Value(r.Value);
                        job.Emit("PUSHI_EFF", "1");
                        job.Emit("SWAP");
                        job.Emit("STO");
                        job.Emit("JMP", exitLabel, s);
                        break;
                    case LabelS l: job.Label(labels[l.Name]); break;
                    case WordS w: job.Word(w.Value); break;
                    case SaveRegS v:
                        Value(v.Value);
                        job.Emit("SAVE_REG");
                        job.Emit("POP");
                        break;
                    case MenuS m: Menu(m); break;
                    case SwitchS sw: Switch(sw); break;
                }
            }

            void Warn(SNodeBase at, string m) { job.AddWarning(at, m); }

            static bool IsBuiltinMenuCall(CallE c) { return c.Name == "babl_menu" || c.Name == "babl_fmenu"; }

            void If(IfS i)
            {
                string otherwise = job.NewLabel();
                Value(i.Cond);
                job.Emit("BEQ", otherwise, i);
                Block(i.Then);
                if (i.Else == null) { job.Label(otherwise); return; }
                string done = job.NewLabel();
                // the jump over the else, even after a return or a goto (Origin's compiler)
                job.Emit("BRA", done, i);
                job.Label(otherwise);
                Block(i.Else);
                job.Label(done);
            }

            void While(WhileS w)
            {
                string top = job.NewLabel(), exit = job.NewLabel();
                job.Label(top);
                var body = w.Body;
                bool once = false;
                if (w.Cond != null)
                {
                    Value(w.Cond);
                    job.Emit("BEQ", exit, w);
                }
                else if (body.Count > 0 && body[body.Count - 1] is JumpS b && b.What == JumpKind.Break && b.When == null)
                {
                    // while (true) { ...; break; }: the part repeated by continue, no jump back
                    once = true;
                    body = body.Take(body.Count - 1).ToList();
                }
                loops.Push(new Scope { Break = exit, Continue = top });
                Block(body);
                loops.Pop();
                if (!once) job.Emit(w.Cond != null ? "BRA" : "JMP", top, w);
                job.Label(exit);
            }

            void Jump(JumpS j)
            {
                string target;
                switch (j.What)
                {
                    case JumpKind.Exit: job.Emit("EXIT"); return;
                    case JumpKind.End:
                        // main's lone RET: the conversation ends (conv/BABL.C babl_run returns when the return stack is empty)
                        if (!first) { Error(j, "end; ends the conversation and belongs to its first function; use return; or exit; here."); return; }
                        job.Emit("RET");
                        return;
                    case JumpKind.Break:
                    case JumpKind.Continue:
                        if (loops.Count == 0) { Error(j, (j.What == JumpKind.Break ? "break" : "continue") + " outside a loop."); return; }
                        target = j.What == JumpKind.Break ? loops.Peek().Break : loops.Peek().Continue;
                        break;
                    default:
                        if (!labels.TryGetValue(j.Label, out target))
                        {
                            job.AddError(j.LabelLine, j.LabelCol, "there is no label " + j.Label + " in " + f.Name + " (a goto goes to a label of its own function).");
                            return;
                        }
                        break;
                }
                if (j.When == null) { job.Emit("JMP", target, j); return; }
                // if (e) goto L: BEQ when e's opposite is 0
                Value(Not(j.When));
                job.Emit("BEQ", target, j);
            }

            static readonly Dictionary<string, string> opposite = new Dictionary<string, string>
            {
                { "==", "!=" }, { "!=", "==" }, { "<", ">=" }, { ">=", "<" }, { ">", "<=" }, { "<=", ">" },
            };

            /// <summary>The condition turned over, as the view turns it (Decompiler.Not): a compare's opposite, !x's x, or !e.</summary>
            static SExpr Not(SExpr e)
            {
                var b = e as BinaryE;
                string op;
                if (b != null && opposite.TryGetValue(b.Op, out op)) return new BinaryE { Op = op, A = b.A, B = b.B, Line = b.Line, Col = b.Col };
                var n = e as UnaryE;
                if (n != null && n.Op == "!") return n.A;
                return new UnaryE { Op = "!", A = e, Line = e.Line, Col = e.Col };
            }

            void Menu(MenuS m)
            {
                int count = m.Choices.Count;
                if (count == 0) { Error(m, "the menu has no choices."); return; }
                if (!m.Filtered && count > MaxMenuChoices) Error(m.Choices[MaxMenuChoices], "a menu shows at most " + MaxMenuChoices + " choices (the game keeps them in bablOptionIds[10], conv/CONVERSE.C); this one has " + count + ".");
                if (m.Filtered && count > MenuArray - 1) Error(m.Choices[MenuArray - 1], "an fmenu has at most " + (MenuArray - 1) + " choices (its arrays are " + MenuArray + " words); this one has " + count + ".");
                // the arrays, in the order they are first used: fmenu stores each flag before its choice
                // the choices' array, then the flags' (Origin's compiler)
                int choices = Temp(MenuArray), flags = m.Filtered ? Temp(MenuArray) : 0;
                var ids = new List<int>();
                for (int k = 0; k < count; k++)
                {
                    var ch = m.Choices[k];
                    if (m.Filtered)
                    {
                        job.Emit("PUSHI", (k + 1).ToString());
                        job.Emit("PUSHI_EFF", flags.ToString());
                        job.Emit("OFFSET");
                        if (ch.Flag != null) Value(ch.Flag); else job.Emit("PUSHI", "1");
                        job.Emit("STO");
                    }
                    int id = job.StringId(ch.Text);
                    ids.Add(id);
                    job.Emit("PUSHI", (k + 1).ToString());
                    job.Emit("PUSHI_EFF", choices.ToString());
                    job.Emit("OFFSET");
                    job.Emit("PUSHI", id.ToString());
                    job.Emit("STO");
                }
                job.Emit("PUSHI", (count + 1).ToString());
                job.Emit("PUSHI_EFF", choices.ToString());
                job.Emit("OFFSET");
                job.Emit("PUSHI", "0");
                job.Emit("STO");
                if (m.Filtered) job.Emit("PUSHI_EFF", flags.ToString());
                job.Emit("PUSHI_EFF", choices.ToString());
                job.Emit("PUSHI", "0");
                job.Emit("CALLI", Builtin(m.Filtered ? "babl_fmenu" : "babl_menu", m));
                job.Emit("POP");
                if (m.Filtered) job.Emit("POP");
                job.Emit("POP");
                job.Emit("PUSH_REG");
                if (m.Answer != null)
                {
                    // x = menu { ... }: the answer kept in x
                    Address(m.Answer, true);
                    job.Emit("SWAP");
                    job.Emit("STO");
                    foreach (var ch in m.Choices) if (ch.Body != null && ch.Body.Count > 0) Error(ch.Body[0], "a menu whose answer is kept in a variable has no statements for its choices (test the variable after it).");
                    return;
                }
                int answer = Temp();
                job.Emit("PUSHI_EFF", answer.ToString());
                job.Emit("SWAP");
                job.Emit("STO");
                string end = job.NewLabel();
                string def = m.Default != null ? job.NewLabel() : end;
                var cases = Enumerable.Range(0, count).Where(k => m.Choices[k].Body != null).ToList();
                for (int c = 0; c < cases.Count; c++)
                {
                    int k = cases[c];
                    var ch = m.Choices[k];
                    string next = c + 1 < cases.Count ? job.NewLabel() : def;
                    job.Comment("line " + ch.Line);
                    job.Emit("PUSHI_EFF", answer.ToString());
                    job.Emit("FETCHM");
                    job.Emit("PUSHI", (m.Filtered ? ids[k] : k + 1).ToString());
                    job.Emit("TSTEQ");
                    job.Emit("BEQ", next, m);
                    Block(ch.Body);
                    job.Emit("JMP", end, m);
                    if (next != def) job.Label(next);
                }
                if (m.Default != null)
                {
                    job.Label(def);
                    Block(m.Default);
                }
                job.Label(end);
            }

            void Switch(SwitchS s)
            {
                Value(s.Value);
                int t = Temp();
                job.Emit("PUSHI_EFF", t.ToString());
                job.Emit("SWAP");
                job.Emit("STO");
                string end = job.NewLabel();
                string def = s.Default != null ? job.NewLabel() : end;
                if (s.Cases.Count == 0) Error(s, "the switch has no cases.");
                for (int c = 0; c < s.Cases.Count; c++)
                {
                    var cs = s.Cases[c];
                    string next = c + 1 < s.Cases.Count ? job.NewLabel() : def;
                    job.Comment("line " + cs.Line);
                    Constant(cs.Value);
                    job.Emit("PUSHI_EFF", t.ToString());
                    job.Emit("FETCHM");
                    job.Emit("TSTEQ");
                    job.Emit("BEQ", next, s);
                    Block(cs.Body);
                    job.Emit("JMP", end, s);
                    if (next != def) job.Label(next);
                }
                if (s.Default != null)
                {
                    job.Label(def);
                    Block(s.Default);
                }
                job.Label(end);
            }

            // ----- expressions -----

            void Constant(int v)
            {
                short x = (short)v;
                if (x < 0 && !pushNegative) { job.Emit("PUSHI", (-x).ToString(CultureInfo.InvariantCulture)); job.Emit("NEG"); }
                else job.Emit("PUSHI", x.ToString(CultureInfo.InvariantCulture));
            }

            Sym Lookup(NameE n, bool report = true)
            {
                Sym s;
                if (vars.TryGetValue(n.Name, out s)) return s;
                int? g = job.GlobalNamed(n.Name);
                if (g.HasValue)
                {
                    var im = job.Original.VariableAt(g.Value);
                    return new Sym { Kind = SymKind.Global, Address = g.Value, Import = im, Name = n.Name, Size = im != null ? Math.Max(1, im.Count) : 1 };
                }
                var bi = job.Original.FunctionNamed(n.Name);
                if (bi != null) return new Sym { Kind = SymKind.Builtin, Address = bi.Index, Import = bi, Name = n.Name };
                if (job.UnitNamed(n.Name) != null) return new Sym { Kind = SymKind.Function, Name = n.Name };
                if (report)
                {
                    var m = System.Text.RegularExpressions.Regex.Match(n.Name, "^global([0-9]+)$");
                    int a;
                    if (m.Success && int.TryParse(m.Groups[1].Value, out a) && a >= job.Original.Variables)
                        Error(n, n.Name + " is beyond the conversation's " + job.Original.Variables + " variables: the compiler does not add variables (the count is in the header and BABGLOBS.DAT's records).");
                    else Error(n, "unknown name " + n.Name + " (not a local, an argument, a global or an imported variable of this conversation).");
                }
                return null;
            }

            /// <summary>Pushes the address of an lvalue (the variable a name stands for, a[i], *p).</summary>
            void Address(SExpr e, bool store)
            {
                switch (e)
                {
                    case NameE n:
                        {
                            var s = Lookup(n);
                            if (s == null) { job.Emit("PUSHI", "0"); return; }
                            switch (s.Kind)
                            {
                                case SymKind.Global: job.Emit("PUSHI", s.Address.ToString()); return;
                                case SymKind.Local: case SymKind.Arg: job.Emit("PUSHI_EFF", s.Address.ToString()); return;
                                case SymKind.Builtin: Error(n, (store ? "cannot assign to " : "cannot take the address of ") + n.Name + ": it is an imported function."); job.Emit("PUSHI", "0"); return;
                                default: Error(n, (store ? "cannot assign to " : "cannot take the address of ") + n.Name + ": it is a function."); job.Emit("PUSHI", "0"); return;
                            }
                        }
                    case IndexE ix:
                        {
                            var bn = ix.Base as NameE;
                            Sym s = bn != null && !ix.Base.Paren ? Lookup(bn) : null;
                            if (bn != null && !ix.Base.Paren && s == null) { job.Emit("PUSHI", "0"); return; }
                            // an imported array's element by a constant: its address (play_name[2])
                            if (s != null && s.Kind == SymKind.Global && s.Import != null && s.Size > 1 && ix.Index is NumberE k)
                            {
                                job.Emit("PUSHI", (s.Address + k.Value - 1).ToString());
                                return;
                            }
                            Value(ix.Index);
                            if (s != null)
                            {
                                switch (s.Kind)
                                {
                                    case SymKind.Global: job.Emit("PUSHI", s.Address.ToString()); break;
                                    case SymKind.Local: job.Emit("PUSHI_EFF", s.Address.ToString()); break;
                                    // an argument holds an address: the element is counted from where it points
                                    case SymKind.Arg: job.Emit("PUSHI_EFF", s.Address.ToString()); job.Emit("FETCHM"); break;
                                    default: Error(bn, bn.Name + " is a function, not an array."); job.Emit("PUSHI", "0"); break;
                                }
                            }
                            else Value(ix.Base);
                            job.Emit("OFFSET");
                            return;
                        }
                    case UnaryE u when u.Op == "*":
                        Value(u.A);
                        return;
                }
                Error(e, store ? "this cannot be assigned to (a variable, an element a[i] or *p can)." : "this has no address (a variable, an element a[i] or *p has).");
                job.Emit("PUSHI", "0");
            }

            /// <summary>Pushes the value of an expression.</summary>
            void Value(SExpr e)
            {
                switch (e)
                {
                    case NumberE k: Constant(k.Value); return;
                    case StringE s: job.Emit("PUSHI", job.StringId(s).ToString()); return;
                    case NameE n:
                        {
                            var s = Lookup(n);
                            if (s == null) { job.Emit("PUSHI", "0"); return; }
                            switch (s.Kind)
                            {
                                case SymKind.Global: job.Emit("PUSHI", s.Address.ToString()); job.Emit("FETCHM"); return;
                                case SymKind.Local:
                                    job.Emit("PUSHI_EFF", s.Address.ToString());
                                    // an array's name stands for its address
                                    if (!s.Array) job.Emit("FETCHM");
                                    return;
                                case SymKind.Arg: job.Emit("PUSHI_EFF", s.Address.ToString()); job.Emit("FETCHM"); return;
                                default: Error(n, n.Name + " is a function: call it with " + n.Name + "(...)."); job.Emit("PUSHI", "0"); return;
                            }
                        }
                    case IndexE ix: Address(ix, false); job.Emit("FETCHM"); return;
                    case UnaryE u:
                        switch (u.Op)
                        {
                            case "&": Address(u.A, false); return;
                            case "*": Value(u.A); job.Emit("FETCHM"); return;
                            case "!": Value(u.A); job.Emit("NOT"); return;
                            case "-":
                                if (u.A is NumberE nk && !nk.True)
                                {
                                    // with a minus a word goes down to -32768: -65535 is not 1
                                    if (nk.Value > 32768) Error(u, "-" + nk.Value + " does not fit in a word (-32768 to 65535).");
                                    Constant(-nk.Value & 0xFFFF);
                                    return;
                                }
                                Value(u.A); job.Emit("NEG"); return;
                        }
                        break;
                    case BinaryE b:
                        Value(b.A);
                        Value(b.B);
                        job.Emit(BinaryOp(b.Op));
                        return;
                    case CallE c:
                        Call(c);
                        job.Emit("PUSH_REG");
                        return;
                    case PtrE p:
                        Error(p, "ptr(e) is only an argument of a call (it passes e as the address).");
                        Value(p.A);
                        return;
                }
                Error(e, "this is not a value.");
                job.Emit("PUSHI", "0");
            }

            static string BinaryOp(string op)
            {
                switch (op)
                {
                    case "+": return "ADD";
                    case "-": return "SUB";
                    case "*": return "MUL";
                    case "/": return "DIV";
                    case "%": return "MOD";
                    case "&&": return "AND";
                    case "||": return "OR";
                    case "==": return "TSTEQ";
                    case "!=": return "TSTNE";
                    case "<": return "TSTLT";
                    case "<=": return "TSTLE";
                    case ">": return "TSTGT";
                    case ">=": return "TSTGE";
                    case "streq": return "STRCMP";
                }
                throw new InvalidOperationException(op);
            }

            string Builtin(string name, SNodeBase at)
            {
                var im = job.Original.FunctionNamed(name);
                if (im == null) { Error(at, "this conversation does not import " + name + " (its import table has no such function)."); return "0"; }
                return im.Index.ToString();
            }

            /// <summary>A call's code, without its result: the arguments (each an address), the call, the POPs.</summary>
            void Call(CallE c)
            {
                if (c.Address >= 0)
                {
                    foreach (var a in c.Args) Argument(a);
                    job.Emit("CALL", "@0x" + c.Address.ToString("X4"));
                    for (int k = 0; k < c.Args.Count; k++) job.Emit("POP");
                    return;
                }
                var unit = job.UnitNamed(c.Name);
                if (unit != null)
                {
                    if (c.Args.Count != unit.F.Params.Count)
                        Warn(c, c.Name + " takes " + unit.F.Params.Count + " argument" + (unit.F.Params.Count == 1 ? "" : "s") + "; this call passes " + c.Args.Count + ".");
                    foreach (var a in c.Args) Argument(a);
                    job.Emit("CALL", unit.Label, c);
                    for (int k = 0; k < c.Args.Count; k++) job.Emit("POP");
                    return;
                }
                var im = job.Original.FunctionNamed(c.Name);
                if (im == null)
                {
                    if (vars.ContainsKey(c.Name) || job.GlobalNamed(c.Name).HasValue) Error(c, c.Name + " is a variable, not a function.");
                    else Error(c, "unknown function " + c.Name + " (not a function of the source or an imported function of this conversation).");
                    return;
                }
                var ar = Arity(c.Name);
                if (ar != null && (c.Args.Count < ar[0] || c.Args.Count > ar[1]))
                    Error(c, c.Name + " takes " + (ar[0] == ar[1] ? ar[0].ToString() : ar[0] + " to " + ar[1]) + " argument" + (ar[1] == 1 ? "" : "s") + "; this call passes " + c.Args.Count + ".");
                foreach (var a in c.Args) Argument(a);
                // the count babl_run passes on; the menus get 0 (Origin's compiler)
                job.Emit("PUSHI", IsBuiltinMenuCall(c) ? "0" : c.Args.Count.ToString());
                job.Emit("CALLI", im.Index.ToString());
                for (int k = 0; k <= c.Args.Count; k++) job.Emit("POP");
            }

            /// <summary>
            /// An argument: an address. &amp;x, an array's name and an argument passed on are
            /// addresses already; any other value goes into a new temporary and its address
            /// is passed.
            /// </summary>
            void Argument(SExpr a)
            {
                if (a is UnaryE u && u.Op == "&") { Address(u.A, false); return; }
                if (a is PtrE p) { Value(p.A); return; }
                if (a is NameE n && !a.Paren)
                {
                    Sym s;
                    if (vars.TryGetValue(n.Name, out s) && (s.Kind == SymKind.Arg || s.Kind == SymKind.Local && s.Array))
                    {
                        job.Emit("PUSHI_EFF", s.Address.ToString());
                        if (s.Kind == SymKind.Arg) job.Emit("FETCHM");
                        return;
                    }
                }
                Value(a);
                int t = Temp();
                job.Emit("PUSHI_EFF", t.ToString());
                job.Emit("SWAP");
                job.Emit("STO");
                job.Emit("PUSHI_EFF", t.ToString());
            }
        }
    }
}
