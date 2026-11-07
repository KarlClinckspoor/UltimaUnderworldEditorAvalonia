using System;
using System.Collections.Generic;
using System.Linq;
using UltimateEditor.Games;

namespace UwEditor.Core.Conversations
{
    /// <summary>
    /// A reference run of the structured view: it executes the if, while, menu and switch
    /// statements the decompiler made (not the bytecode), on a ConversationSimulator's memory,
    /// stack and built-ins, pushing and popping as conv/BABL.C babl_run would. Run with the
    /// same menu choices, it must say, respond and print what the simulator running the
    /// bytecode does: the structure keeps the code's meaning.
    /// </summary>
    public static partial class Decompiler
    {
        /// <summary>
        /// Runs a conversation's structured form to its end. choose picks a menu's choice (the
        /// menu, how often it was met, as ConversationSimulator.RunToEnd); answer answers
        /// babl_ask. Returns the machine it ran on: its Output, State and Error.
        /// </summary>
        public static ConversationSimulator RunStructured(Conversation conv, IList<string> strings, SimWorld world, Func<SimMenu, int, int> choose,
            Func<string> answer = null, Func<int, int, string> otherString = null, int[] savedGlobals = null, long maxSteps = 200000)
        {
            var sim = new ConversationSimulator(conv, strings, world, otherString, savedGlobals);
            sim.LogInstructions = false;
            Ctx c;
            var trees = Trees(conv, strings == null ? null : strings.ToArray(), world.Kind, ConversationNames.None, new StructureReport(), out c);
            var run = new Runner { Sim = sim, Choose = choose, Answer = answer, MaxSteps = maxSteps, Trees = trees.ToDictionary(t => t.Fn.Start) };
            try { run.Main(); }
            catch (Runner.Stop) { }
            catch (SimFault f) { sim.Abort(f.Message); }
            if (run.Failed != null) sim.Abort(run.Failed);
            return sim;
        }

        sealed class Runner
        {
            public ConversationSimulator Sim;
            public Func<SimMenu, int, int> Choose;
            public Func<string> Answer;
            public long MaxSteps, Steps;
            public Dictionary<int, Tree> Trees;
            readonly Dictionary<string, int> seen = new Dictionary<string, int>();

            /// <summary>The run is over (the script ended, failed, or ran too long).</summary>
            public sealed class Stop : Exception { }
            /// <summary>break, continue, return or goto, on its way to the statement that takes it.</summary>
            sealed class Jump : Exception { public string What; public int Label; }

            Tree fn;
            int bp { get { return Sim.BasePointer; } }

            /// <summary>Why the run stopped early (too many steps), or null.</summary>
            public string Failed;

            public void Main()
            {
                Invoke(0, -1);
                Sim.End();
            }

            void Tick()
            {
                if (++Steps > MaxSteps) { Failed = "the structured run took more than " + MaxSteps + " steps"; throw new Stop(); }
                if (Sim.State == SimState.Ended || Sim.State == SimState.Error) throw new Stop();
            }

            /// <summary>Calls a script function: the caller pushed the arguments; CALL pushes the return address (none for main).</summary>
            void Invoke(int start, int retAddr)
            {
                Tree t;
                if (!Trees.TryGetValue(start, out t)) throw new SimFault("no function at " + start.ToString("X4"));
                var keep = fn;
                fn = t;
                if (retAddr >= 0) Sim.PushWord(retAddr);
                if (t.Fn.Frame)
                {
                    // PUSHBP SPTOBP PUSHI n ADDSP: n is pushed where the first local is, so local1 starts as n
                    Sim.PushWord(Sim.BasePointer);
                    Sim.BasePointer = Sim.StackPointer;
                    Sim.PushWord(t.Fn.Locals);
                    Sim.StackPointer += t.Fn.Locals - 1;
                }
                int label = -1;
                int epilogue = t.Fn.EpilogueIdx >= 0 ? t.Fn.Flats[t.Fn.EpilogueIdx].Start : -1;
                // a jump straight to the epilogue (a bare return; in a function that returns a value) skips the exit block, as the bytecode does
                bool skipExit = false;
                while (true)
                {
                    try
                    {
                        if (label < 0) Exec(t.Body);
                        else if (label == t.Fn.ExitAddr) { }
                        else if (epilogue >= 0 && label == epilogue) skipExit = true;
                        else ExecFrom(t.Body, label);
                        break;
                    }
                    catch (Jump j)
                    {
                        if (j.What == "return") { if (epilogue >= 0 && j.Label == epilogue) skipExit = true; break; }
                        if (j.What == "goto") { label = j.Label; continue; }
                        throw new SimFault(j.What + " outside a loop");
                    }
                }
                // the exit block: push local; SAVE_REG; POP
                if (t.Fn.ExitIdx >= 0 && !skipExit)
                {
                    Sim.PushWord(Sim.Mem(Sim.StackBase + bp + t.Fn.Result));
                    Sim.Register = (short)Sim.PopWord();
                }
                if (t.Fn.Frame)
                {
                    // BPTOSP POPBP
                    Sim.StackPointer = Sim.BasePointer;
                    Sim.BasePointer = Sim.PopWord();
                }
                if (start == 0 || Sim.StackPointer <= 0)
                {
                    // bab_ret: a RET with nothing on the stack ends the script (main's does)
                    Sim.End();
                    throw new Stop();
                }
                int ret = Sim.PopWord() & 0xFFFF;   // RET
                if (ret != retAddr) throw new SimFault(start.ToString("X4") + " returns to " + ret.ToString("X4") + ", not to its caller (a script that writes over its frame)");
                fn = keep;
            }

            // ----- statements -----

            void Exec(List<Node> list) { for (int k = 0; k < list.Count; k++) Exec(list[k]); }

            void Exec(Node n)
            {
                Tick();
                switch (n)
                {
                    case SNode s: Flat(s.F); break;
                    case LabelNode _: break;
                    case DeadNode _: break;
                    case ReturnNode r:
                        Flat(r.Covers[0]);
                        throw new Jump { What = "return" };
                    case JumpNode j:
                        if (j.When != null && Cond(j.F.E)) break;
                        throw new Jump { What = j.What, Label = j.Dest };
                    case IfNode i:
                        if (Cond(i.Branch.E)) Exec(i.Then);
                        else if (i.Else != null) Exec(i.Else);
                        break;
                    case WhileNode w: Loop(w, null); break;
                    case MenuNode m: Menu(m, -1); break;
                    case SwitchNode s: Switch(s, -1); break;
                }
            }

            /// <summary>The loop; from: a label in its body where a goto came in (null: from the top).</summary>
            void Loop(WhileNode w, int? from)
            {
                while (true)
                {
                    Tick();
                    if (from == null && w.CondBranch != null && !Cond(w.CondBranch.E)) return;
                    try
                    {
                        if (from != null) { int l = from.Value; from = null; ExecFrom(w.Body, l); }
                        else Exec(w.Body);
                    }
                    catch (Jump j) when (j.What == "break") { return; }
                    catch (Jump j) when (j.What == "continue") { continue; }
                    // falling off the end: the jump back, or the break shown at the end
                    if (w.Back == null) return;
                }
            }

            void Menu(MenuNode m, int from)
            {
                if (from < 0)
                {
                    foreach (var f in m.Covers) if (f != m.Assign && f.Kind == FK.Assign) Flat(f);
                    Flat(m.Assign);
                    if (m.Answer != null) return;
                    foreach (var ch in m.Choices)
                    {
                        if (!ch.HasCase) continue;
                        if (Cond(ch.Compare.E)) { Exec(ch.Body); return; }
                    }
                    if (m.Default != null) Exec(m.Default);
                    return;
                }
                foreach (var ch in m.Choices) if (ch.Body != null && Holds(ch.Body, from)) { ExecFrom(ch.Body, from); return; }
                if (m.Default != null && Holds(m.Default, from)) { ExecFrom(m.Default, from); return; }
                throw new SimFault("label not in the menu");
            }

            void Switch(SwitchNode s, int from)
            {
                if (from < 0)
                {
                    Flat(s.Assign);
                    foreach (var cs in s.Cases) if (Cond(cs.Compare.E)) { Exec(cs.Body); return; }
                    if (s.Default != null) Exec(s.Default);
                    return;
                }
                foreach (var cs in s.Cases) if (Holds(cs.Body, from)) { ExecFrom(cs.Body, from); return; }
                if (s.Default != null && Holds(s.Default, from)) { ExecFrom(s.Default, from); return; }
                throw new SimFault("label not in the switch");
            }

            /// <summary>Runs the statements from a label among them (a goto's), as C does: the statements around it go on as if they had got there.</summary>
            void ExecFrom(List<Node> list, int label)
            {
                for (int k = 0; k < list.Count; k++)
                {
                    var n = list[k];
                    if (n is LabelNode l && l.Addr == label) { for (int x = k + 1; x < list.Count; x++) Exec(list[x]); return; }
                    if (n is WhileNode w0 && w0.Head.Addr == label) { for (int x = k; x < list.Count; x++) Exec(list[x]); return; }
                    if (!Holds(n, label)) continue;
                    switch (n)
                    {
                        case IfNode i:
                            if (Holds(i.Then, label)) ExecFrom(i.Then, label); else ExecFrom(i.Else, label);
                            break;
                        case WhileNode w: Loop(w, label); break;
                        case MenuNode m: Menu(m, label); break;
                        case SwitchNode s: Switch(s, label); break;
                    }
                    for (int x = k + 1; x < list.Count; x++) Exec(list[x]);
                    return;
                }
                throw new SimFault("no label " + label.ToString("X4"));
            }

            static bool Holds(List<Node> list, int label) { return list.Any(n => Holds(n, label)); }

            static bool Holds(Node n, int label)
            {
                if (n is LabelNode l) return l.Addr == label;
                if (n is WhileNode w && w.Head.Addr == label) return true;
                foreach (var c in Shaper.Children(n)) if (Holds(c, label)) return true;
                return false;
            }

            /// <summary>A branch's condition, popped as BEQ pops it: true when the next line runs.</summary>
            bool Cond(Expr e)
            {
                Push(e);
                return Sim.PopWord() != 0;
            }

            void Flat(Flat f)
            {
                switch (f.Kind)
                {
                    case FK.Assign:
                        if (f.Swap)
                        {
                            // PUSH value; PUSHI_EFF n; SWAP; STO
                            Push(f.Value);
                            Push(f.Target);
                            Sim.SwapTop();
                        }
                        else { Push(f.Target); Push(f.Value); }
                        Sto();
                        break;
                    case FK.Call:
                        {
                            var call = (Call)f.E;
                            DoCall(call);
                            Sim.StackPointer -= call.Pops;
                            break;
                        }
                    case FK.Say:
                    case FK.Respond:
                        Push(f.E);
                        Sim.Speak(f.Kind == FK.Say, Sim.PopWord(), f.Addr);
                        break;
                    case FK.Exit: Sim.End(); throw new Stop();
                    case FK.Return:
                        if (fn.Fn.Start == 0) { Sim.End(); throw new Stop(); }
                        throw new Jump { What = "return" };
                    case FK.SaveReg: Push(f.E); Sim.Register = (short)Sim.Top; break;
                    case FK.Word: Sim.End(); throw new Stop();
                    default: throw new SimFault("cannot run " + f);
                }
            }

            /// <summary>STO: the value on top, the address under it.</summary>
            void Sto()
            {
                int v = Sim.PopWord(), a = Sim.PopWord();
                Sim.Store(a, v);
            }

            // ----- expressions: pushed as the code pushes them -----

            void Push(Expr e)
            {
                switch (e)
                {
                    case Const k: Sim.PushWord((short)k.Value); return;
                    case FrameAddr fa: Sim.PushWord(Sim.StackBase + fa.Offset + bp); return;
                    case Load l: Push(l.Addr); Sim.Top = Sim.Mem(Sim.Top); return;
                    case Elem el:
                        {
                            Push(el.Index); Push(el.Base);
                            int b = Sim.PopWord();
                            Sim.Top = (short)(Sim.Top + b - 1);
                            return;
                        }
                    case Op o:
                        {
                            if (o.Unary)
                            {
                                Push(o.A);
                                int x = Sim.Top;
                                Sim.Top = o.Symbol == "!" ? (x == 0 ? 1 : 0) : (short)-x;
                                return;
                            }
                            Push(o.A); Push(o.B);
                            int b = Sim.PopWord(), a = Sim.Top;
                            Sim.Top = Binary(o.Symbol, a, b);
                            return;
                        }
                    case Call call:
                        DoCall(call);
                        // POP the count and arguments, PUSH_REG
                        Sim.StackPointer -= call.Pops;
                        Sim.PushWord(Sim.Register);
                        return;
                    case Temp t:
                        {
                            // PUSH value; PUSHI_EFF n; SWAP; STO; PUSHI_EFF n
                            int addr = Sim.StackBase + t.Slot + bp;
                            Push(t.Value);
                            Sim.PushWord(addr);
                            Sim.SwapTop();
                            Sto();
                            Sim.PushWord(addr);
                            return;
                        }
                }
                throw new SimFault("cannot evaluate " + e.GetType().Name);
            }

            int Binary(string sym, int a, int b)
            {
                switch (sym)
                {
                    case "+": return (short)(a + b);
                    case "-": return (short)(a - b);
                    case "*": return (short)(a * b);
                    case "/": return b != 0 ? (short)(a / b) : 0x7FFF;
                    case "%": return b != 0 ? (short)(a % b) : 0x7FFF;
                    case "||": return a != 0 || b != 0 ? 1 : 0;
                    case "&&": return a != 0 && b != 0 ? 1 : 0;
                    case ">": return a > b ? 1 : 0;
                    case ">=": return a >= b ? 1 : 0;
                    case "<": return a < b ? 1 : 0;
                    case "<=": return a <= b ? 1 : 0;
                    case "==": return a == b ? 1 : 0;
                    case "!=": return a != b ? 1 : 0;
                    case "streq": return string.CompareOrdinal(Sim.StringText(a), Sim.StringText(b)) == 0 ? 1 : 0;
                }
                throw new SimFault("no operator " + sym);
            }

            /// <summary>The arguments pushed, then the call; the result is in reg (and for a built-in in the count's place).</summary>
            void DoCall(Call call)
            {
                foreach (var a in call.Args) Push(a);
                if (call.Builtin)
                {
                    Push(call.Count);
                    var r = Sim.CallDirect(call.At, call.Target, Choose, Answer, seen);
                    if (r == null) throw new Stop();
                }
                else Invoke(call.Target, call.At + 2);
            }
        }
    }
}
