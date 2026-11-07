using System;
using System.Collections.Generic;
using System.Text;
using UltimateEditor.Games;

namespace UwEditor.Core.Conversations
{
    /// <summary>One line of the readable view, with the code address it starts at.</summary>
    public sealed class PseudoLine
    {
        public int Address;
        public int Indent;
        public string Text;
        public PseudoKind Kind;
        /// <summary>The instructions the line shows, Start to End - 1 (-1: only Address). Address is the one that does the work (STO, SAY, the call, the branch).</summary>
        public int Start = -1, End = -1;
        /// <summary>For a conditional goto: the condition under which the next line runs (it falls through).</summary>
        internal string Guard;
        /// <summary>For a goto: the address it goes to (else -1).</summary>
        internal int Target = -1;
        public override string ToString() { return new string(' ', 4 * Indent) + Text; }
    }

    public enum PseudoKind { Statement, Label, Function, Say, Respond, Menu, Comment }

    /// <summary>How a conversation uses one of its strings.</summary>
    public enum StringUseKind { Say, Respond, Menu, Argument }

    public sealed class StringUse
    {
        /// <summary>The PUSHI that pushes the string's number.</summary>
        public int Address;
        public int Id;
        public StringUseKind Kind;
        /// <summary>The instruction that uses it (SAY, RESPOND, CALLI).</summary>
        public int UseAddress;
    }

    /// <summary>The kinds of game state a conversation reaches.</summary>
    public enum GlobalKind { Quest, GameVariable, XClock, Skill, Private, Imported }

    public enum GlobalAccess { Read, Write, ReadWrite }

    /// <summary>A place where a conversation reads or writes a global: a built-in with constant arguments, or script memory below babl_nvars.</summary>
    public sealed class GlobalUse
    {
        public int Slot;
        public int Address;
        public GlobalKind Kind;
        /// <summary>The quest, variable, clock or skill number; the memory address for private and imported variables.</summary>
        public int Number;
        /// <summary>For imported variables, their name (npc_attitude, play_hp).</summary>
        public string Name;
        public GlobalAccess Access;
        /// <summary>The value written, when it is a constant.</summary>
        public int? Value;
        /// <summary>How it was reached: get_quest, set_quest, x_traps, x_clock, x_skills, or memory.</summary>
        public string Via;
        public override string ToString() { return string.Format("{0} {1} {2} {3}{4} at {5:X4}", Kind, Number, Name, Access, Value.HasValue ? " = " + Value : "", Address); }
    }

    /// <summary>A menu the script shows: the babl_menu or babl_fmenu call, and the string ids of its choices.</summary>
    public sealed class MenuUse
    {
        public int Address;
        public bool Filtered;
        public readonly List<int> Ids = new List<int>();
        /// <summary>The choices as the readable view shows them (quoted text and number).</summary>
        public readonly List<string> Choices = new List<string>();
    }

    public sealed class DecompileResult
    {
        public readonly List<PseudoLine> Lines = new List<PseudoLine>();
        /// <summary>For each BEQ and BNE, by address: the condition under which it jumps, in the readable view's words.</summary>
        public readonly Dictionary<int, string> Branches = new Dictionary<int, string>();
        /// <summary>For each BEQ and BNE: the condition under which it falls through to the next instruction.</summary>
        public readonly Dictionary<int, string> FallThrough = new Dictionary<int, string>();
        public readonly List<MenuUse> Menus = new List<MenuUse>();
        public readonly List<StringUse> Strings = new List<StringUse>();
        public readonly List<GlobalUse> Globals = new List<GlobalUse>();
        /// <summary>Notes for the disassembly, by instruction address.</summary>
        public readonly Dictionary<int, string> Notes = new Dictionary<int, string>();
        /// <summary>The names the view was made with (ConversationNames.None: the placeholders).</summary>
        public ConversationNames Names = ConversationNames.None;
        /// <summary>For the structured view: the gotos left and the checks (null for the flat view).</summary>
        public StructureReport Structure;
        public string Text
        {
            get
            {
                var sb = new StringBuilder();
                foreach (var l in Lines) sb.Append(l.ToString()).Append('\n');
                return sb.ToString();
            }
        }
    }

    /// <summary>
    /// A readable view of a conversation's code, from simulating the machine's stack
    /// (conv/BABL.C babl_run): expressions come back together, say and respond show their
    /// text, menus their choices, and calls to the built-ins that touch game state
    /// (get_quest, set_quest, x_traps, x_clock, x_skills) are collected with their constant
    /// arguments. Control flow stays as labels and gotos, as the code has it; only simple
    /// "if" blocks are folded. Arguments are listed in the order the script pushes them, so
    /// the first is the built-in's args[-n] and the last its args[-1].
    /// </summary>
    public static partial class Decompiler
    {
        abstract class Expr
        {
            public abstract string Text(Ctx c);
        }
        sealed class Const : Expr
        {
            public int Value; public int At;
            /// <summary>Pushed positive and negated (PUSHI n NEG), not pushed as a negative number.</summary>
            public bool Negated;
            public override string Text(Ctx c) { return ((short)Value).ToString(); }
        }
        sealed class FrameAddr : Expr
        {
            public int Offset;
            public override string Text(Ctx c) { return "&" + c.FrameName(Offset); }
        }
        sealed class Elem : Expr
        {
            public Expr Base, Index;
            public override string Text(Ctx c) { return "&" + Deref(c, this); }
        }
        sealed class Load : Expr
        {
            public Expr Addr;
            public override string Text(Ctx c) { return Deref(c, Addr); }
        }
        sealed class Op : Expr
        {
            public string Symbol; public Expr A, B; public bool Unary;
            public override string Text(Ctx c)
            {
                if (Unary) return Symbol + Paren(c, A);
                return Paren(c, A) + " " + Symbol + " " + Paren(c, B);
            }
            static string Paren(Ctx c, Expr e) { var t = e.Text(c); return e is Op ? "(" + t + ")" : t; }
        }
        sealed class Call : Expr
        {
            public string Name; public List<Expr> Args = new List<Expr>(); public bool Builtin; public int At;
            public string Rendered;
            /// <summary>For CALL, the function's address; for CALLI, the import number.</summary>
            public int Target;
            /// <summary>For CALLI, the count pushed before it (not always the number of arguments: babl_menu gets 0).</summary>
            public Expr Count;
            /// <summary>The POPs after the call.</summary>
            public int Pops;
            public override string Text(Ctx c) { return Rendered ?? (Rendered = c.RenderCall(this)); }
        }
        sealed class Raw : Expr
        {
            public string S;
            public override string Text(Ctx c) { return S; }
        }

        static string Deref(Ctx c, Expr addr)
        {
            var k = addr as Const;
            if (k != null) return c.GlobalName(k.Value);
            var f = addr as FrameAddr;
            if (f != null) return c.FrameName(f.Offset);
            var e = addr as Elem;
            if (e != null)
            {
                string b = e.Base is FrameAddr || e.Base is Const ? Deref(c, e.Base) : "(" + e.Base.Text(c) + ")";
                return b + "[" + e.Index.Text(c) + "]";
            }
            var l = addr as Load;
            if (l != null) return "*" + Deref(c, l.Addr);
            return "mem[" + addr.Text(c) + "]";
        }

        sealed class Ctx
        {
            public Conversation Conv;
            public string[] Strings;
            public GameKind Kind;
            public DecompileResult Result;
            public Dictionary<int, string> FunctionNames = new Dictionary<int, string>();
            public ConversationNames Names = ConversationNames.None;
            /// <summary>The start of the function being read, for its locals' names.</summary>
            public int Function;
            /// <summary>Frame slot → the constant last stored there and the PUSHI that pushed it.</summary>
            public Dictionary<int, Const> Known = new Dictionary<int, Const>();
            /// <summary>The line that stored each known constant, hidden once a built-in takes it as an argument.</summary>
            public Dictionary<int, PseudoLine> KnownLine = new Dictionary<int, PseudoLine>();
            public HashSet<PseudoLine> Hide = new HashSet<PseudoLine>();
            /// <summary>Menus met while rendering, shown as lines under the statement that has their marker.</summary>
            public List<List<string>> Menus = new List<List<string>>();

            void Taken(int slot)
            {
                PseudoLine l;
                if (KnownLine.TryGetValue(slot, out l)) Hide.Add(l);
            }

            public string GlobalName(int a)
            {
                var im = Conv.VariableAt(a);
                if (im != null) return im.Count > 1 ? im.Name + "[" + (a - im.Index + 1) + "]" : im.Name;
                return a >= 0 && a < Conv.Variables ? Names.Global(a) : "mem[" + a + "]";
            }

            public string FrameName(int n) { return Names.Local(Function, n); }

            public string Str(int id)
            {
                if (Strings != null && id >= 0 && id < Strings.Length) return Quote(Strings[id]) + " #" + id;
                return "string #" + id;
            }

            /// <summary>The constant an argument address holds, when it is a frame slot the code just set.</summary>
            public Const ValueAt(Expr addr)
            {
                int slot;
                if (!FrameSlot(addr, out slot)) return null;
                Const k;
                if (!Known.TryGetValue(slot, out k)) return null;
                Taken(slot);
                return k;
            }

            public string RenderCall(Call call)
            {
                if (!call.Builtin)
                {
                    var parts = new List<string>();
                    foreach (var a in call.Args) parts.Add(a.Text(this));
                    return call.Name + "(" + string.Join(", ", parts) + ")";
                }
                var p = new List<string>();
                for (int i = 0; i < call.Args.Count; i++) p.Add(Arg(call, i));
                return call.Name + "(" + string.Join(", ", p) + ")";
            }

            string Arg(Call call, int i)
            {
                var a = call.Args[i];
                bool stringArg = StringArgument(call.Name, i, call.Args.Count);
                if ((call.Name == "babl_menu" || call.Name == "babl_fmenu") && i == 0)
                {
                    var choices = MenuChoices(a, call.At);
                    if (choices != null)
                    {
                        if (call.Name == "babl_fmenu") Result.Menus.Find(m => m.Address == call.At).Filtered = true;
                        Menus.Add(choices);
                        return "\u0001" + (Menus.Count - 1) + "\u0001";
                    }
                }
                var k = ValueAt(a);
                if (k != null)
                {
                    if (stringArg) { Use(k.At, k.Value, StringUseKind.Argument, call.At); return Str(k.Value); }
                    return ((short)k.Value).ToString();
                }
                // a built-in reads the value at the address (getmem(args[-n]))
                var ld = a as FrameAddr != null || a is Elem || a is Const ? new Load { Addr = a }.Text(this) : "*(" + a.Text(this) + ")";
                return ld;
            }

            /// <summary>A menu's choices: string numbers in consecutive frame slots, up to a 0.</summary>
            public List<string> MenuChoices(Expr array, int callAt)
            {
                int slot;
                if (!FrameSlot(array, out slot)) return null;
                var list = new List<string>();
                var use = new MenuUse { Address = callAt };
                for (int s = slot; s < slot + 64; s++)
                {
                    Const k;
                    if (!Known.TryGetValue(s, out k)) break;
                    Taken(s);
                    if (k.Value == 0) break;
                    Use(k.At, k.Value, StringUseKind.Menu, callAt);
                    list.Add(Str(k.Value));
                    use.Ids.Add(k.Value);
                }
                if (list.Count == 0) return null;
                use.Choices.AddRange(list);
                if (!Result.Menus.Exists(m => m.Address == callAt)) Result.Menus.Add(use);
                return list;
            }

            public void Use(int at, int id, StringUseKind kind, int useAt)
            {
                foreach (var u in Result.Strings) if (u.Address == at && u.UseAddress == useAt) return;
                Result.Strings.Add(new StringUse { Address = at, Id = id, Kind = kind, UseAddress = useAt });
                string text = Strings != null && id >= 0 && id < Strings.Length ? Strings[id] : null;
                if (text != null) Result.Notes[at] = "#" + id + " " + Quote(Shorten(text, 60));
            }

            static bool FrameSlotOf(Expr e, out int slot)
            {
                slot = 0;
                var f = e as FrameAddr;
                if (f != null) { slot = f.Offset; return true; }
                var el = e as Elem;
                if (el != null)
                {
                    var fb = el.Base as FrameAddr;
                    var idx = el.Index as Const;
                    if (fb != null && idx != null) { slot = fb.Offset + (short)idx.Value - 1; return true; }
                }
                return false;
            }

            public bool FrameSlot(Expr e, out int slot) { return FrameSlotOf(e, out slot); }
        }

        /// <summary>Built-ins whose argument i (in push order) is a string number.</summary>
        static bool StringArgument(string f, int i, int n)
        {
            switch (f)
            {
                case "sex": return true;
                case "print": case "length": case "val": return true;
                case "compare": case "contains": case "append": case "copy": case "find": return i < 2;
                case "plural": return i > 0;
            }
            return false;
        }

        public static string Quote(string s)
        {
            if (s == null) return "\"\"";
            return "\"" + s.Replace("\\", "\\\\").Replace("\"", "\\\"").Replace("\n", "\\n") + "\"";
        }

        static string Shorten(string s, int n) { return s.Length <= n ? s : s.Substring(0, n - 1) + "…"; }

        /// <param name="fold">Fold simple "if" blocks (the readable view); false keeps every conditional goto, as the graph wants.</param>
        /// <param name="names">The names to show (NameBook.Resolve); null for the placeholders.</param>
        public static DecompileResult Decompile(Conversation conv, string[] strings, GameKind kind, bool fold = true, ConversationNames names = null)
        {
            var r = new DecompileResult();
            names = names ?? ConversationNames.None;
            r.Names = names;
            var c = new Ctx { Conv = conv, Strings = strings, Kind = kind, Result = r, Names = names };
            // functions: word 0, CALL targets, and every PUSHBP SPTOBP (the compiler's prologue); labels: jump targets
            var lay = names.Layout ?? BablLayout.Of(conv);
            var code = lay.Code;
            var at = lay.At;
            var funcs = lay.Functions;
            var labels = lay.Labels;
            var called = lay.Called;
            foreach (int f in funcs) c.FunctionNames[f] = names.Function(f);

            r.Lines.Add(new PseudoLine { Address = 0, Kind = PseudoKind.Comment, Text = string.Format("// {0} variables in memory (imports and private globals), string block 0x{1:X4}", conv.Variables, conv.StringBlock) });
            var fl = new List<int>(funcs);
            for (int fi = 0; fi < fl.Count; fi++)
            {
                int start = fl[fi], end = fi + 1 < fl.Count ? fl[fi + 1] : conv.Code.Length;
                DecompileFunction(c, code, at, start, end, labels, called.Contains(start) || start == 0, fold);
            }
            // the readable view: the lines again, structured (the metadata above stays the flat view's)
            if (fold)
            {
                var folded = new List<PseudoLine>(r.Lines);
                try { Structure(conv, strings, kind, names, r); }
                catch (Exception ex)
                {
                    // code the structurer cannot follow (an edited script, say): the flat view, folded as before
                    r.Lines.Clear();
                    r.Lines.AddRange(folded);
                    r.Structure = new StructureReport();
                    r.Structure.Problems.Add("not structured: " + ex.Message);
                }
            }
            return r;
        }

        static void DecompileFunction(Ctx c, List<BablInstruction> code, Dictionary<int, int> at, int start, int end, HashSet<int> labels, bool reached, bool fold)
        {
            var r = c.Result;
            var lines = new List<PseudoLine>();
            var stack = new List<Expr>();
            c.Known.Clear();
            c.KnownLine.Clear();
            c.Hide.Clear();
            Call pending = null;
            c.Function = start;
            int i0 = at[start];
            int locals = 0;
            int i = i0;
            // the prologue: [START] PUSHBP SPTOBP PUSHI n ADDSP
            if (i < code.Count && code[i].Known && code[i].Op == BablOp.START) i++;
            if (i + 3 < code.Count && Is(code[i], BablOp.PUSHBP) && Is(code[i + 1], BablOp.SPTOBP) && Is(code[i + 2], BablOp.PUSHI) && Is(code[i + 3], BablOp.ADDSP))
            {
                locals = (short)code[i + 2].Operand;
                i += 4;
            }
            string name = c.FunctionNames[start];
            r.Lines.Add(new PseudoLine { Address = start, Kind = PseudoKind.Comment, Text = "" });
            r.Lines.Add(new PseudoLine
            {
                Address = start, Kind = PseudoKind.Function,
                Text = name + "()" + (locals > 0 ? "    // " + locals + " local" + (locals == 1 ? "" : "s") : "") + (reached ? "" : "    // never called"),
            });

            Action<int, string, PseudoKind> emit = (a, t, k) =>
            {
                int m = t.IndexOf('\u0001');
                if (m < 0) { lines.Add(new PseudoLine { Address = a, Indent = 1, Text = t, Kind = k }); return; }
                // a menu: the statement, then a line a choice, numbered as the menu returns them
                int e2 = t.IndexOf('\u0001', m + 1);
                var choices = c.Menus[int.Parse(t.Substring(m + 1, e2 - m - 1))];
                lines.Add(new PseudoLine { Address = a, Indent = 1, Text = t.Substring(0, m) + "…" + t.Substring(e2 + 1), Kind = PseudoKind.Menu });
                for (int n = 0; n < choices.Count; n++)
                    lines.Add(new PseudoLine { Address = a, Indent = 2, Text = (n + 1) + ": " + choices[n], Kind = PseudoKind.Menu });
            };
            Action flush = () =>
            {
                if (pending != null) { emit(pending.At, pending.Text(c), PseudoKind.Statement); pending = null; }
            };
            Func<Expr> pop = () =>
            {
                if (stack.Count == 0) return new Raw { S = "<empty>" };
                var e = stack[stack.Count - 1];
                stack.RemoveAt(stack.Count - 1);
                return e;
            };

            for (; i < code.Count && code[i].Address < end; i++)
            {
                var ins = code[i];
                int a = ins.Address;
                if (labels.Contains(a))
                {
                    flush();
                    c.Known.Clear();
                    c.KnownLine.Clear();
                    lines.Add(new PseudoLine { Address = a, Indent = 0, Text = c.Names.Label(a) + ":", Kind = PseudoKind.Label });
                }
                if (!ins.Known) { flush(); emit(a, "word 0x" + ins.Word.ToString("X4") + "    // not an instruction", PseudoKind.Statement); continue; }
                var op = ins.Op;
                if (op != BablOp.POP && op != BablOp.PUSH_REG) flush();
                switch (op)
                {
                    case BablOp.NOP: case BablOp.START: break;
                    case BablOp.PUSHI: stack.Add(new Const { Value = ins.Operand, At = a }); break;
                    case BablOp.PUSHI_EFF: stack.Add(new FrameAddr { Offset = (short)ins.Operand }); break;
                    case BablOp.FETCHM:
                        {
                            var addr = pop();
                            var k = addr as Const;
                            if (k != null) Memory(c, k.Value, GlobalAccess.Read, null, a);
                            stack.Add(new Load { Addr = addr });
                            break;
                        }
                    case BablOp.OFFSET:
                        {
                            var b = pop(); var idx = pop();
                            stack.Add(new Elem { Base = b, Index = idx });
                            break;
                        }
                    case BablOp.STO:
                        {
                            var v = pop(); var addr = pop();
                            int slot;
                            var kv = v as Const;
                            var ka = addr as Const;
                            if (ka != null) Memory(c, ka.Value, GlobalAccess.Write, kv == null ? (int?)null : (short)kv.Value, a);
                            emit(a, Deref(c, addr) + " = " + v.Text(c), PseudoKind.Statement);
                            if (c.FrameSlot(addr, out slot))
                            {
                                if (kv != null) { c.Known[slot] = kv; c.KnownLine[slot] = lines[lines.Count - 1]; }
                                else { c.Known.Remove(slot); c.KnownLine.Remove(slot); }
                            }
                            break;
                        }
                    case BablOp.SWAP:
                        if (stack.Count >= 2) { var t = stack[stack.Count - 1]; stack[stack.Count - 1] = stack[stack.Count - 2]; stack[stack.Count - 2] = t; }
                        break;
                    case BablOp.ADD: Bin(stack, pop, "+"); break;
                    case BablOp.SUB: Bin(stack, pop, "-"); break;
                    case BablOp.MUL: Bin(stack, pop, "*"); break;
                    case BablOp.DIV: Bin(stack, pop, "/"); break;
                    case BablOp.MOD: Bin(stack, pop, "%"); break;
                    case BablOp.OR: Bin(stack, pop, "or"); break;
                    case BablOp.AND: Bin(stack, pop, "and"); break;
                    case BablOp.TSTGT: Bin(stack, pop, ">"); break;
                    case BablOp.TSTGE: Bin(stack, pop, ">="); break;
                    case BablOp.TSTLT: Bin(stack, pop, "<"); break;
                    case BablOp.TSTLE: Bin(stack, pop, "<="); break;
                    case BablOp.TSTEQ: Bin(stack, pop, "=="); break;
                    case BablOp.TSTNE: Bin(stack, pop, "!="); break;
                    case BablOp.NOT: stack.Add(new Op { Symbol = "not ", A = pop(), Unary = true }); break;
                    case BablOp.NEG:
                        {
                            var x = pop();
                            var k = x as Const;
                            stack.Add(k != null ? (Expr)new Const { Value = (-(short)k.Value) & 0xFFFF, At = k.At } : new Op { Symbol = "-", A = x, Unary = true });
                            break;
                        }
                    case BablOp.STRCMP:
                        {
                            var y = pop(); var x = pop();
                            stack.Add(new Raw { S = "strcmp(" + StrText(c, x, a) + ", " + StrText(c, y, a) + ")" });
                            break;
                        }
                    case BablOp.JMP: case BablOp.BRA:
                        {
                            int t = ins.Target;
                            if (t == a + 2) break;   // the compiler's jump to the next line
                            if (i > 0 && code[i - 1].Known && (code[i - 1].Op == BablOp.JMP || code[i - 1].Op == BablOp.BRA) && !labels.Contains(a)) break;   // unreachable
                            emit(a, "goto " + c.Names.Label(t), PseudoKind.Statement);
                            lines[lines.Count - 1].Target = t;
                            break;
                        }
                    case BablOp.BEQ:
                    case BablOp.BNE:
                        {
                            var cond = pop();
                            string yes = Bare(cond.Text(c)), no = Negate(c, cond);
                            bool beq = op == BablOp.BEQ;
                            emit(a, "if " + (beq ? no : yes) + " goto " + c.Names.Label(ins.Target), PseudoKind.Statement);
                            lines[lines.Count - 1].Guard = beq ? yes : no;
                            lines[lines.Count - 1].Target = ins.Target;
                            r.Branches[a] = Unmark(beq ? no : yes);
                            r.FallThrough[a] = Unmark(beq ? yes : no);
                            break;
                        }
                    case BablOp.CALL:
                        {
                            string fname;
                            if (!c.FunctionNames.TryGetValue(ins.Target, out fname)) fname = "@" + ins.Target.ToString("X4");
                            int n = Pops(code, i + 1);
                            var call = new Call { Name = fname, At = a };
                            // a script function's arguments are what this statement left on the stack
                            int take = Math.Min(n, stack.Count);
                            for (int k = stack.Count - take; k < stack.Count; k++) call.Args.Add(stack[k]);
                            pending = call;
                            break;
                        }
                    case BablOp.CALLI:
                        {
                            var f = c.Conv.Function(ins.Operand);
                            string fname = f != null ? f.Name : "builtin" + ins.Operand;
                            var count = pop();
                            int pops = Pops(code, i + 1);
                            int n = pops > 0 ? pops - 1 : (count is Const ? (short)((Const)count).Value : 0);
                            n = Math.Max(0, Math.Min(n, stack.Count));
                            var call = new Call { Name = fname, Builtin = true, At = a };
                            for (int k = stack.Count - n; k < stack.Count; k++) call.Args.Add(stack[k]);
                            stack.Add(new Raw { S = "<result>" });
                            call.Text(c);   // render now, while the known constants are the ones the call sees
                            Builtin(c, call, a);
                            if (fname == "babl_menu" || fname == "babl_fmenu" || fname == "babl_ask")
                            {
                                // keep the menu on its own line even when the choice is used at once
                            }
                            pending = call;
                            break;
                        }
                    case BablOp.RET:
                        emit(a, stack.Count > 0 || start != 0 ? "return" : "end", PseudoKind.Statement);
                        break;
                    case BablOp.POP:
                        pop();
                        break;
                    case BablOp.PUSH_REG:
                        if (pending != null) { stack.Add(pending); pending = null; }
                        else stack.Add(new Raw { S = "result" });
                        break;
                    case BablOp.SAVE_REG:
                        if (stack.Count > 0)
                        {
                            var v = stack[stack.Count - 1];
                            emit(a, "return " + v.Text(c), PseudoKind.Statement);
                            stack[stack.Count - 1] = new Raw { S = "<returned>" };
                        }
                        break;
                    case BablOp.PUSHBP: stack.Add(new Raw { S = "bp" }); break;
                    case BablOp.POPBP: pop(); break;
                    case BablOp.SPTOBP: break;
                    case BablOp.BPTOSP: stack.Clear(); break;
                    case BablOp.ADDSP: pop(); break;
                    case BablOp.EXIT: emit(a, "exit", PseudoKind.Statement); break;
                    case BablOp.SAY:
                    case BablOp.RESPOND:
                        {
                            var s = pop();
                            var k = s as Const;
                            bool say = op == BablOp.SAY;
                            if (k != null) c.Use(k.At, k.Value, say ? StringUseKind.Say : StringUseKind.Respond, a);
                            emit(a, (say ? "say " : "respond ") + (k != null ? c.Str(k.Value) : s.Text(c)), say ? PseudoKind.Say : PseudoKind.Respond);
                            break;
                        }
                }
            }
            flush();
            // the epilogue BPTOSP POPBP RET is the "return" at the end: drop it when it is the last line
            if (lines.Count > 0 && lines[lines.Count - 1].Text == "return") lines.RemoveAt(lines.Count - 1);
            lines.RemoveAll(l => c.Hide.Contains(l));
            if (fold) Fold(lines);
            foreach (var l in lines) if (l.Kind == PseudoKind.Statement && l.Text.StartsWith("babl_")) l.Kind = PseudoKind.Menu;
            r.Lines.AddRange(lines);
        }

        static string StrText(Ctx c, Expr e, int useAt)
        {
            var k = e as Const;
            if (k != null) { c.Use(k.At, k.Value, StringUseKind.Argument, useAt); return c.Str(k.Value); }
            return e.Text(c);
        }

        static string Bare(string t) { return t; }

        /// <summary>A condition's text with a menu's marker shown as the readable view shows it.</summary>
        static string Unmark(string t)
        {
            int m;
            while ((m = t.IndexOf('\u0001')) >= 0)
            {
                int e = t.IndexOf('\u0001', m + 1);
                if (e < 0) break;
                t = t.Substring(0, m) + "…" + t.Substring(e + 1);
            }
            return t;
        }

        static readonly Dictionary<string, string> opposite = new Dictionary<string, string>
        {
            { "==", "!=" }, { "!=", "==" }, { "<", ">=" }, { ">=", "<" }, { ">", "<=" }, { "<=", ">" },
        };

        static string Negate(Ctx c, Expr e)
        {
            var o = e as Op;
            string sym;
            if (o != null && !o.Unary && opposite.TryGetValue(o.Symbol, out sym))
                return new Op { Symbol = sym, A = o.A, B = o.B }.Text(c);
            if (o != null && o.Unary && o.Symbol == "not ") return o.A.Text(c);
            return "not (" + e.Text(c) + ")";
        }

        /// <summary>
        /// Folds "if not C goto L; ...; L:" into an indented block when nothing else jumps to L
        /// and the block holds no labels: "if C:" over the lines it guards.
        /// </summary>
        static void Fold(List<PseudoLine> lines)
        {
            var refs = new Dictionary<int, int>();
            foreach (var l in lines)
                if (l.Target >= 0) refs[l.Target] = refs.ContainsKey(l.Target) ? refs[l.Target] + 1 : 1;
            for (int i = 0; i < lines.Count; i++)
            {
                var l = lines[i];
                if (l.Kind != PseudoKind.Statement || l.Guard == null || l.Target < 0) continue;
                int n;
                if (!refs.TryGetValue(l.Target, out n) || n != 1) continue;
                int j = i + 1;
                bool ok = false;
                for (; j < lines.Count; j++)
                {
                    if (lines[j].Kind == PseudoKind.Label) { ok = lines[j].Address == l.Target; break; }
                }
                if (!ok || j == i + 1) continue;
                l.Text = "if " + l.Guard + ":";
                l.Target = -1;
                for (int k = i + 1; k < j; k++) lines[k].Indent++;
                lines.RemoveAt(j);
            }
        }

        static bool Is(BablInstruction i, BablOp op) { return i.Known && i.Op == op; }

        static int Pops(List<BablInstruction> code, int from)
        {
            int n = 0;
            while (from + n < code.Count && Is(code[from + n], BablOp.POP)) n++;
            return n;
        }

        static void Bin(List<Expr> stack, Func<Expr> pop, string sym)
        {
            var b = pop(); var a = pop();
            stack.Add(new Op { Symbol = sym, A = a, B = b });
        }

        static void Memory(Ctx c, int address, GlobalAccess access, int? value, int at)
        {
            if (address < 0 || address >= c.Conv.Variables) return;
            var im = c.Conv.VariableAt(address);
            bool imported = im != null && ImportedVariables.IsBound(im.Name, c.Kind);
            c.Result.Globals.Add(new GlobalUse
            {
                Slot = c.Conv.Slot, Address = at, Access = access, Value = value, Via = "memory",
                Kind = imported ? GlobalKind.Imported : GlobalKind.Private,
                Number = address, Name = im != null ? im.Name : null,
            });
        }

        /// <summary>The built-ins that reach game state, with their constant arguments.</summary>
        static void Builtin(Ctx c, Call call, int at)
        {
            Func<int, int?> arg = i =>
            {
                if (i >= call.Args.Count) return null;
                var k = c.ValueAt(call.Args[i]);
                return k == null ? (int?)null : (short)k.Value;
            };
            int? a0 = arg(0), a1 = arg(1);
            bool uw2 = c.Kind == GameKind.UW2;
            Action<GlobalKind, int, GlobalAccess, int?> add = (kind, n, acc, v) =>
                c.Result.Globals.Add(new GlobalUse { Slot = c.Conv.Slot, Address = at, Kind = kind, Number = n, Access = acc, Value = v, Via = call.Name });
            switch (call.Name)
            {
                case "get_quest":
                    if (a0.HasValue) add(GlobalKind.Quest, a0.Value, GlobalAccess.Read, null);
                    break;
                case "set_quest":
                    if (a0.HasValue) add(GlobalKind.Quest, a0.Value, GlobalAccess.Write, a1);
                    break;
                case "x_traps":
                    if (a0.HasValue)
                    {
                        // UW1 sets game_vars[var] for a value 0-63, UW2 its numbered variable for 0-0x3FF;
                        // otherwise it only reads (uw1/uw2 conv/BABLHACK.C x_traps)
                        int max = uw2 ? 0x3FF : 0x3F;
                        var acc = !a1.HasValue ? GlobalAccess.Write : a1.Value >= 0 && a1.Value <= max ? GlobalAccess.Write : GlobalAccess.Read;
                        int? v = acc == GlobalAccess.Write ? a1 : null;
                        GlobalKind kind; int n;
                        Numbered(uw2, a0.Value, out kind, out n);
                        add(kind, n, acc, v);
                    }
                    break;
                case "x_clock":
                    if (a0.HasValue)
                    {
                        var acc = !a1.HasValue ? GlobalAccess.Write : a1.Value > 0x100 ? GlobalAccess.Read : GlobalAccess.Write;
                        add(GlobalKind.XClock, a0.Value, acc, acc == GlobalAccess.Write ? a1 : null);
                    }
                    break;
                case "x_skills":
                    if (a0.HasValue)
                    {
                        var acc = !a1.HasValue ? GlobalAccess.Write : a1.Value >= 0 && a1.Value <= 30 ? GlobalAccess.Write : a1.Value > 10000 && uw2 ? GlobalAccess.ReadWrite : GlobalAccess.Read;
                        add(GlobalKind.Skill, a0.Value, acc, acc == GlobalAccess.Write ? a1 : null);
                    }
                    break;
            }
        }

        /// <summary>UW2's numbered variables (event/TRIGGER.C): 0-0xFF the variables, 0x100-0x17F quests 0-127, 0x180-0x18F quests 128-143, 0x190-0x19F the X clocks.</summary>
        public static void Numbered(bool uw2, int v, out GlobalKind kind, out int n)
        {
            kind = GlobalKind.GameVariable; n = v;
            if (!uw2 || v < 0x100) return;
            if (v < 0x180) { kind = GlobalKind.Quest; n = v - 0x100; }
            else if (v < 0x190) { kind = GlobalKind.Quest; n = 128 + v - 0x180; }
            else if (v < 0x1A0) { kind = GlobalKind.XClock; n = v - 0x190; }
        }
    }
}
