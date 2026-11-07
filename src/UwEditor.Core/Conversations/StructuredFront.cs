using System;
using System.Collections.Generic;
using UltimateEditor.Games;

namespace UwEditor.Core.Conversations
{
    /// <summary>
    /// The structured view's first step: each function read into a flat list of statements
    /// (assignments, calls, say, respond, jumps and branches, labels), simulating the stack
    /// as conv/BABL.C babl_run does, without hiding anything. The structurer
    /// (StructuredBuild.cs) then turns the jumps into if, while, menu and switch.
    /// </summary>
    public static partial class Decompiler
    {
        enum FK { Label, Assign, Call, Say, Respond, Exit, Return, SaveReg, Jump, Branch, Word }

        /// <summary>One flat statement of a function.</summary>
        sealed class Flat
        {
            public FK Kind;
            /// <summary>The instruction that makes the statement (STO, SAY, CALL, the jump...): the line's address, as the flat view has it.</summary>
            public int Addr;
            /// <summary>The first instruction of the statement and the address after its last.</summary>
            public int Start, End;
            public Expr Target, Value;
            /// <summary>An assignment made PUSH value; PUSHI_EFF n; SWAP; STO: the compiler's temporary (a constant argument, a menu's answer, a switch's value).</summary>
            public bool Swap;
            /// <summary>A call, the said text, a returned value, or for a branch the condition under which it falls through (runs the next line).</summary>
            public Expr E;
            public BablOp Op;
            public int Dest = -1;
            /// <summary>The RET that ends the function (after BPTOSP POPBP).</summary>
            public bool Epilogue;
            /// <summary>A temporary shown inside the call that takes it.</summary>
            public bool Folded;
            public int Word;
            public override string ToString() { return Kind + " " + Addr.ToString("X4") + (Dest >= 0 ? " -> " + Dest.ToString("X4") : ""); }
        }

        /// <summary>A call's argument that the compiler put into a temporary local (PUSH value; PUSHI_EFF n; SWAP; STO; PUSHI_EFF n): shown as the value.</summary>
        sealed class Temp : Expr
        {
            public int Slot; public Expr Value; public Flat Store;
            public override string Text(Ctx c) { return Value.Text(c); }
        }

        /// <summary>A function read flat.</summary>
        sealed class Fn
        {
            public int Start, End, Locals;
            public bool Reached;
            public readonly List<Flat> Flats = new List<Flat>();
            /// <summary>The RET at the end (index in Flats), or -1.</summary>
            public int EpilogueIdx = -1;
            /// <summary>The exit block "push local; SAVE_REG; POP" before the epilogue: index of its first flat (its label if it has one), its address, and the local returned.</summary>
            public int ExitIdx = -1, ExitAddr = -1, Result;
            /// <summary>Instruction index by address, for jump targets: the first flat at or after an address.</summary>
            public readonly Dictionary<int, int> IndexOf = new Dictionary<int, int>();
            /// <summary>The highest argument used (arg1 is bp - 2).</summary>
            public int Params;
            /// <summary>The function starts with the prologue PUSHBP SPTOBP PUSHI n ADDSP.</summary>
            public bool Frame;
        }

        static Fn ReadFunction(Ctx c, BablLayout lay, int start, int end, bool reached)
        {
            var code = lay.Code;
            var fn = new Fn { Start = start, End = end, Reached = reached };
            var fl = fn.Flats;
            var stack = new List<Expr>();
            var temps = new Dictionary<int, Flat>();
            c.Function = start;
            int i = lay.At[start];
            if (i < code.Count && code[i].Known && code[i].Op == BablOp.START) i++;
            if (i + 3 < code.Count && Is(code[i], BablOp.PUSHBP) && Is(code[i + 1], BablOp.SPTOBP) && Is(code[i + 2], BablOp.PUSHI) && Is(code[i + 3], BablOp.ADDSP))
            {
                fn.Locals = (short)code[i + 2].Operand;
                fn.Frame = true;
                i += 4;
            }
            int stmtStart = i < code.Count ? code[i].Address : end;
            Call pending = null;
            int pendingStart = 0;

            Action<Flat> add = f =>
            {
                if (f.Start == 0 && f.Kind != FK.Label) f.Start = stmtStart;
                if (f.End == 0) f.End = f.Addr + (f.Kind == FK.Jump || f.Kind == FK.Branch ? 2 : 1);
                fl.Add(f);
                if (f.Kind != FK.Label) stmtStart = f.End;
                if (!(f.Kind == FK.Assign && f.Swap)) temps.Clear();
            };
            Action flush = () =>
            {
                if (pending == null) return;
                var p = pending;
                pending = null;
                int e = p.At + 2 + p.Pops;
                add(new Flat { Kind = FK.Call, Addr = p.At, Start = pendingStart, End = e, E = p });
            };
            Func<Expr> pop = () =>
            {
                if (stack.Count == 0) return new Raw { S = "<empty>" };
                var e = stack[stack.Count - 1];
                stack.RemoveAt(stack.Count - 1);
                return e;
            };
            Action<Call> resolveTemps = call =>
            {
                for (int k = 0; k < call.Args.Count; k++)
                {
                    var fa = call.Args[k] as FrameAddr;
                    Flat t;
                    if (fa != null && temps.TryGetValue(fa.Offset, out t) && !t.Folded)
                    {
                        call.Args[k] = new Temp { Slot = fa.Offset, Value = t.Value, Store = t };
                        t.Folded = true;
                        temps.Remove(fa.Offset);
                        // the statement that shows the call starts where its temporary did
                        stmtStart = Math.Min(stmtStart, t.Start);
                    }
                }
            };

            for (; i < code.Count && code[i].Address < end; i++)
            {
                var ins = code[i];
                int a = ins.Address;
                if (lay.Labels.Contains(a))
                {
                    flush();
                    stmtStart = a;
                    add(new Flat { Kind = FK.Label, Addr = a, Start = a, End = a });
                }
                if (!ins.Known) { flush(); add(new Flat { Kind = FK.Word, Addr = a, Word = ins.Word }); continue; }
                var op = ins.Op;
                if (op != BablOp.POP && op != BablOp.PUSH_REG) flush();
                switch (op)
                {
                    case BablOp.NOP: case BablOp.START: break;
                    case BablOp.PUSHI: stack.Add(new Const { Value = ins.Operand, At = a }); break;
                    case BablOp.PUSHI_EFF: stack.Add(new FrameAddr { Offset = (short)ins.Operand }); break;
                    case BablOp.FETCHM: stack.Add(new Load { Addr = pop() }); break;
                    case BablOp.OFFSET:
                        {
                            var b = pop(); var idx = pop();
                            stack.Add(new Elem { Base = b, Index = idx });
                            break;
                        }
                    case BablOp.STO:
                        {
                            var v = pop(); var addr = pop();
                            bool swap = i > 0 && Is(code[i - 1], BablOp.SWAP);
                            var f = new Flat { Kind = FK.Assign, Addr = a, Target = addr, Value = v, Swap = swap };
                            add(f);
                            var fa = addr as FrameAddr;
                            if (swap && fa != null) temps[fa.Offset] = f;
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
                    case BablOp.OR: Bin(stack, pop, "||"); break;
                    case BablOp.AND: Bin(stack, pop, "&&"); break;
                    case BablOp.TSTGT: Bin(stack, pop, ">"); break;
                    case BablOp.TSTGE: Bin(stack, pop, ">="); break;
                    case BablOp.TSTLT: Bin(stack, pop, "<"); break;
                    case BablOp.TSTLE: Bin(stack, pop, "<="); break;
                    case BablOp.TSTEQ: Bin(stack, pop, "=="); break;
                    case BablOp.TSTNE: Bin(stack, pop, "!="); break;
                    case BablOp.STRCMP: Bin(stack, pop, "streq"); break;
                    case BablOp.NOT: stack.Add(new Op { Symbol = "!", A = pop(), Unary = true }); break;
                    case BablOp.NEG:
                        {
                            var x = pop();
                            var k = x as Const;
                            stack.Add(k != null ? (Expr)new Const { Value = (-(short)k.Value) & 0xFFFF, At = k.At, Negated = true } : new Op { Symbol = "-", A = x, Unary = true });
                            break;
                        }
                    case BablOp.JMP:
                    case BablOp.BRA:
                        add(new Flat { Kind = FK.Jump, Addr = a, Op = op, Dest = ins.Target });
                        break;
                    case BablOp.BEQ:
                    case BablOp.BNE:
                        {
                            var cond = pop();
                            // BEQ jumps when the value is 0: the next line runs when it is not
                            if (op == BablOp.BNE) cond = new Op { Symbol = "!", A = cond, Unary = true };
                            add(new Flat { Kind = FK.Branch, Addr = a, Op = op, Dest = ins.Target, E = cond });
                            break;
                        }
                    case BablOp.CALL:
                        {
                            string fname;
                            if (!c.FunctionNames.TryGetValue(ins.Target, out fname)) fname = "@" + ins.Target.ToString("X4");
                            int n = Pops(code, i + 1);
                            var call = new Call { Name = fname, At = a, Target = ins.Target, Pops = n };
                            int take = Math.Min(n, stack.Count);
                            for (int k = stack.Count - take; k < stack.Count; k++) call.Args.Add(stack[k]);
                            pendingStart = stmtStart;
                            resolveTemps(call);
                            pendingStart = Math.Min(pendingStart, stmtStart);
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
                            var call = new Call { Name = fname, Builtin = true, At = a, Target = ins.Operand, Count = count, Pops = pops };
                            for (int k = stack.Count - n; k < stack.Count; k++) call.Args.Add(stack[k]);
                            resolveTemps(call);
                            // the result takes the count's place (talk_calli), popped with the arguments
                            stack.Add(new Raw { S = "<result>" });
                            pending = call;
                            pendingStart = stmtStart;
                            break;
                        }
                    case BablOp.RET: add(new Flat { Kind = FK.Return, Addr = a }); break;
                    case BablOp.POP: pop(); break;
                    case BablOp.PUSH_REG:
                        if (pending != null) { stack.Add(pending); pending = null; }
                        else stack.Add(new Raw { S = "result" });
                        break;
                    case BablOp.SAVE_REG:
                        add(new Flat { Kind = FK.SaveReg, Addr = a, E = stack.Count > 0 ? stack[stack.Count - 1] : new Raw { S = "<empty>" } });
                        break;
                    case BablOp.PUSHBP: stack.Add(new Raw { S = "bp" }); break;
                    case BablOp.POPBP: pop(); break;
                    case BablOp.SPTOBP: break;
                    case BablOp.BPTOSP: stack.Clear(); break;
                    case BablOp.ADDSP: pop(); break;
                    case BablOp.EXIT: add(new Flat { Kind = FK.Exit, Addr = a }); break;
                    case BablOp.SAY:
                    case BablOp.RESPOND:
                        add(new Flat { Kind = op == BablOp.SAY ? FK.Say : FK.Respond, Addr = a, E = pop() });
                        break;
                }
            }
            flush();

            // the epilogue: BPTOSP POPBP RET at the end (main ends with a lone RET)
            if (fl.Count > 0 && fl[fl.Count - 1].Kind == FK.Return)
            {
                var r = fl[fl.Count - 1];
                int ri = lay.At[r.Addr];
                bool frame = ri >= 2 && Is(code[ri - 1], BablOp.POPBP) && Is(code[ri - 2], BablOp.BPTOSP);
                if (frame || start == 0)
                {
                    r.Epilogue = true;
                    if (frame) { r.Start = code[ri - 2].Address; }
                    fn.EpilogueIdx = fl.Count - 1;
                    // the exit block of a function that returns a value: push local; SAVE_REG; POP
                    // (a bare return; jumps past it to the epilogue, which puts a label between them)
                    int s = fl.Count - 2;
                    if (s >= 0 && fl[s].Kind == FK.Label && fl[s].Addr == r.Start) s--;
                    if (s >= 0 && fl[s].Kind == FK.SaveReg)
                    {
                        var ld = fl[s].E as Load;
                        var fa = ld == null ? null : ld.Addr as FrameAddr;
                        if (fa != null && fa.Offset > 0)
                        {
                            fn.Result = fa.Offset;
                            fn.ExitIdx = s > 0 && fl[s - 1].Kind == FK.Label && fl[s - 1].Addr == fl[s].Start ? s - 1 : s;
                            fn.ExitAddr = fl[s].Start;
                        }
                    }
                }
            }
            for (int k = fl.Count - 1; k >= 0; k--)
            {
                fn.IndexOf[fl[k].Kind == FK.Label ? fl[k].Addr : fl[k].Start] = k;
                if (fl[k].Kind == FK.Label) fn.IndexOf[fl[k].Addr] = k;
            }
            return fn;
        }
    }
}
