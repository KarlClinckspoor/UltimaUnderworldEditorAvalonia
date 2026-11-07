using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using UltimateEditor.Games;

namespace UwEditor.Core.Conversations
{
    /// <summary>
    /// The structured view's text: the tree StructuredBuild.cs makes, printed in the
    /// conversation language (docs/conversation-language.md), with the names applied. Each
    /// line keeps the address of the instruction it comes from, as the flat view does.
    /// </summary>
    public static partial class Decompiler
    {
        /// <summary>One local variable of a function: its first slot and its size (1, or an array's).</summary>
        sealed class LocalVar { public int Slot, Size; public bool Array; }

        sealed class Printer
        {
            public Ctx C;
            public Fn Fn;
            public Shaper Shaper;
            public StructureReport Report;
            public List<PseudoLine> Lines;
            /// <summary>The flats in the order the text shows them, for the checks.</summary>
            public readonly List<Flat> Shown = new List<Flat>();
            /// <summary>Labels some goto goes to.</summary>
            public HashSet<int> GotoLabels = new HashSet<int>();
            /// <summary>Local variables by slot (temporaries left out).</summary>
            public SortedDictionary<int, LocalVar> Vars = new SortedDictionary<int, LocalVar>();
            int indent;

            void Line(int addr, string text, PseudoKind kind, int end = -1, int start = -1)
            {
                Lines.Add(new PseudoLine { Address = addr, Indent = indent, Text = text, Kind = kind, Start = start >= 0 ? start : end >= 0 ? addr : -1, End = end });
            }

            // ----- variables -----

            /// <summary>Finds the function's variables: arrays (indexed from their slot), scalars, the temporaries left out.</summary>
            public void Layout(List<Node> body)
            {
                var arrays = new HashSet<int>();
                var scalars = new HashSet<int>();
                Action<Expr, bool> walk = null;
                walk = (e, addr) =>
                {
                    if (e == null) return;
                    var fa = e as FrameAddr;
                    if (fa != null) { if (fa.Offset > 0) scalars.Add(fa.Offset); return; }
                    var el = e as Elem;
                    if (el != null)
                    {
                        var b = el.Base as FrameAddr;
                        if (b != null) { if (b.Offset > 0) arrays.Add(b.Offset); }
                        else walk(el.Base, false);
                        walk(el.Index, false);
                        return;
                    }
                    var ld = e as Load;
                    if (ld != null) { walk(ld.Addr, true); return; }
                    var op = e as Op;
                    if (op != null) { walk(op.A, false); walk(op.B, false); return; }
                    var call = e as Call;
                    if (call != null) { foreach (var a in call.Args) walk(a, false); return; }
                    var t = e as Temp;
                    if (t != null) { walk(t.Value, false); return; }
                };
                foreach (var f in Fn.Flats)
                {
                    if (f.Folded) { walk(f.Value, false); continue; }
                    walk(f.Target, true); walk(f.Value, false); walk(f.E, false);
                }
                var temps = Shaper.TempSlots;
                var starts = new SortedSet<int>();
                foreach (var s in arrays) if (!temps.Contains(s)) starts.Add(s);
                foreach (var s in scalars) if (!temps.Contains(s)) starts.Add(s);
                var list = starts.ToList();
                if (temps.Count > 0 && list.Count > 0 && list[list.Count - 1] > temps.Min()) Report.TempsBeforeLocals++;
                for (int k = 0; k < list.Count; k++)
                {
                    int s = list[k];
                    int next = k + 1 < list.Count ? list[k + 1] : Fn.Locals + 1;
                    // a temporary between them ends the array too
                    for (int x = s + 1; x < next; x++) if (temps.Contains(x)) { next = x; break; }
                    bool arr = arrays.Contains(s);
                    Vars[s] = new LocalVar { Slot = s, Size = arr ? Math.Max(1, next - s) : 1, Array = arr };
                }
                // locals the code never uses, before the temporaries: declared too, as the frame has them
                int firstTemp = temps.Count > 0 ? temps.Min() : Fn.Locals + 1;
                for (int s = 1; s < firstTemp && s <= Fn.Locals; s++)
                {
                    int idx;
                    if (VarOf(s, out idx) == null) Vars[s] = new LocalVar { Slot = s, Size = 1 };
                }
            }

            /// <summary>The variable holding a slot: the scalar itself, or the array and the slot's index in it (from 1).</summary>
            LocalVar VarOf(int slot, out int index)
            {
                index = 0;
                LocalVar v;
                if (Vars.TryGetValue(slot, out v)) { index = v.Array ? 1 : 0; return v; }
                foreach (var x in Vars.Values)
                    if (x.Array && slot > x.Slot && slot < x.Slot + x.Size) { index = slot - x.Slot + 1; return x; }
                return null;
            }

            string Local(int slot) { return C.Names.Local(Fn.Start, slot); }

            // ----- expressions -----

            static int Prec(Expr e)
            {
                var o = e as Op;
                if (o == null) return 9;
                if (o.Unary) return 8;
                switch (o.Symbol)
                {
                    case "*": case "/": case "%": return 7;
                    case "+": case "-": return 6;
                    case "<": case "<=": case ">": case ">=": return 5;
                    case "==": case "!=": return 4;
                    case "&&": return 3;
                    case "||": return 2;
                    case "streq": return 9;
                }
                return 1;
            }

            public string R(Expr e, int min = 0)
            {
                string t = Raw(e);
                return Prec(e) < min ? "(" + t + ")" : t;
            }

            string Raw(Expr e)
            {
                var k = e as Const;
                if (k != null) return ((short)k.Value).ToString();
                var fa = e as FrameAddr;
                if (fa != null) return AddrOf(fa);
                var el = e as Elem;
                if (el != null) return "&" + ElemText(el);
                var ld = e as Load;
                if (ld != null) return Deref(ld.Addr);
                var o = e as Op;
                if (o != null)
                {
                    int p = Prec(o);
                    if (o.Symbol == "streq") return "streq(" + StrArg(o.A) + ", " + StrArg(o.B) + ")";
                    if (o.Unary)
                    {
                        // keep "- -1" apart
                        string a = R(o.A, p);
                        return o.Symbol + (o.Symbol == "-" && a.StartsWith("-") ? " " : "") + a;
                    }
                    return R(o.A, p) + " " + o.Symbol + " " + R(o.B, p + 1);
                }
                var call = e as Call;
                if (call != null) return CallText(call);
                var t = e as Temp;
                if (t != null) return R(t.Value);
                var raw = e as Raw;
                return raw != null ? raw.S : "?";
            }

            string StrArg(Expr e)
            {
                var k = e as Const;
                return k != null ? StrLit(k.Value) : R(e, 0);
            }

            string StrLit(int id)
            {
                if (C.Strings != null && id >= 0 && id < C.Strings.Length) return Quote(C.Strings[id]) + " #" + id;
                return "#" + id;
            }

            /// <summary>The address of a frame slot: an array's name (it stands for its address), &amp;name, or &amp;array[i].</summary>
            string AddrOf(FrameAddr fa)
            {
                if (fa.Offset <= 0) return "&" + Local(fa.Offset);
                int idx;
                var v = VarOf(fa.Offset, out idx);
                if (v == null) return "&" + Local(fa.Offset);
                if (v.Array) return idx == 1 ? Local(v.Slot) : "&" + Local(v.Slot) + "[" + idx + "]";
                return "&" + Local(v.Slot);
            }

            string ElemText(Elem el)
            {
                string b;
                var fb = el.Base as FrameAddr;
                var kb = el.Base as Const;
                var lb = el.Base as Load;
                if (fb != null) b = fb.Offset > 0 && VarOf(fb.Offset, out _) is LocalVar v && v.Slot == fb.Offset ? Local(fb.Offset) : "(" + AddrOf(fb) + ")";
                else if (kb != null) b = C.GlobalName(kb.Value);
                else if (lb != null && (lb.Addr is FrameAddr || lb.Addr is Const)) b = Deref(lb.Addr);
                else b = "(" + R(el.Base) + ")";
                if (lb != null && lb.Addr is FrameAddr lfa && lfa.Offset > 0 && VarOf(lfa.Offset, out _) is LocalVar lv && lv.Array) b = "(" + b + ")";
                return b + "[" + R(el.Index) + "]";
            }

            /// <summary>The variable at an address, as an lvalue.</summary>
            public string Deref(Expr addr)
            {
                var k = addr as Const;
                if (k != null) return C.GlobalName(k.Value);
                var fa = addr as FrameAddr;
                if (fa != null)
                {
                    if (fa.Offset <= 0) return Local(fa.Offset);
                    int idx;
                    var v = VarOf(fa.Offset, out idx);
                    if (v == null) return Local(fa.Offset);
                    return v.Array ? Local(v.Slot) + "[" + idx + "]" : Local(v.Slot);
                }
                var el = addr as Elem;
                if (el != null) return ElemText(el);
                return "*" + R(addr, 8);
            }

            string CallText(Call call)
            {
                var parts = new List<string>();
                for (int i = 0; i < call.Args.Count; i++)
                {
                    var a = call.Args[i];
                    var t = a as Temp;
                    if (t != null)
                    {
                        var k = t.Value as Const;
                        parts.Add(k != null && call.Builtin && StringArgument(call.Name, i, call.Args.Count) ? StrLit(k.Value) : R(t.Value));
                        continue;
                    }
                    parts.Add(PassedText(a));
                }
                return call.Name + "(" + string.Join(", ", parts) + ")";
            }

            /// <summary>An argument passed as it is (an address): &amp;x, an array's name, or a pointer argument passed on.</summary>
            string PassedText(Expr a)
            {
                if (a is FrameAddr fa) return AddrOf(fa);
                if (a is Elem el) return "&" + ElemText(el);
                if (a is Const k) return "&" + C.GlobalName(k.Value);
                if (a is Load ld && ld.Addr is FrameAddr p && p.Offset < 0) return Local(p.Offset);
                return "ptr(" + R(a) + ")";
            }

            // ----- statements -----

            void Show(Flat f)
            {
                if (f == null) return;
                ShowTemps(f.Target); ShowTemps(f.Value); ShowTemps(f.E);
                Shown.Add(f);
            }

            void ShowTemps(Expr e)
            {
                if (e == null) return;
                if (e is Temp t) { ShowTemps(t.Value); Shown.Add(t.Store); return; }
                if (e is Op o) { ShowTemps(o.A); ShowTemps(o.B); return; }
                if (e is Load l) { ShowTemps(l.Addr); return; }
                if (e is Elem el) { ShowTemps(el.Base); ShowTemps(el.Index); return; }
                if (e is Call c) foreach (var a in c.Args) ShowTemps(a);
            }

            public void Function(List<Node> body, int callerArgs)
            {
                string name = C.FunctionNames[Fn.Start];
                int args = Math.Max(Fn.Params, callerArgs);
                var ps = new List<string>();
                for (int k = args; k >= 1; k--) ps.Add(Local(-1 - k));
                Line(Fn.Start, "", PseudoKind.Comment);
                indent = 0;
                Line(Fn.Start, name + "(" + string.Join(", ", ps) + ") {"
                    + (Fn.Locals > 0 ? "    // " + Fn.Locals + " local" + (Fn.Locals == 1 ? "" : "s") : "")
                    + (Fn.Reached ? "" : "    // never called"), PseudoKind.Function, Fn.Start + 1);
                indent = 1;
                if (Fn.ExitIdx >= 0) Report.Role("result-local " + Fn.Result);
                foreach (var f in Fn.Flats) CountRoles(f.Target); foreach (var f in Fn.Flats) { CountRoles(f.Value); CountRoles(f.E); }
                if (Vars.Count > 0)
                    Line(Fn.Start, "int " + string.Join(", ", Vars.Values.Select(v => Local(v.Slot) + (v.Array ? "[" + v.Size + "]" : ""))) + ";", PseudoKind.Statement);
                Block(body);
                // the exit block "return local" and the epilogue: shown only when a goto needs the exit's label
                if (Fn.ExitIdx >= 0)
                {
                    if (GotoLabels.Contains(Fn.ExitAddr))
                    {
                        indent = 0;
                        Line(Fn.ExitAddr, C.Names.Label(Fn.ExitAddr) + ":", PseudoKind.Label);
                        indent = 1;
                        var sr = Fn.Flats[Fn.ExitIdx].Kind == FK.Label ? Fn.Flats[Fn.ExitIdx + 1] : Fn.Flats[Fn.ExitIdx];
                        Line(sr.Addr, "return " + R(sr.E) + ";", PseudoKind.Statement);
                    }
                    for (int k = Fn.ExitIdx; k < Fn.Flats.Count; k++) if (Fn.Flats[k].Kind != FK.Label) Shown.Add(Fn.Flats[k]);
                }
                else if (Fn.EpilogueIdx >= 0)
                {
                    int e = Fn.Flats[Fn.EpilogueIdx].Start;
                    if (GotoLabels.Contains(e)) { indent = 0; Line(e, C.Names.Label(e) + ":", PseudoKind.Label); indent = 1; }
                    for (int k = Fn.EpilogueIdx; k < Fn.Flats.Count; k++) if (Fn.Flats[k].Kind != FK.Label) Shown.Add(Fn.Flats[k]);
                }
                indent = 0;
                Line(Fn.End - 1, "}", PseudoKind.Comment);
            }

            /// <summary>Counts how calls pass their count and arguments, for the language notes.</summary>
            void CountRoles(Expr e)
            {
                if (e == null) return;
                if (e is Call call)
                {
                    if (call.Builtin)
                    {
                        var k = call.Count as Const;
                        Report.Role(k == null ? "calli-count computed" : (short)k.Value == call.Args.Count ? "calli-count args" : "calli-count other " + call.Name + "=" + (short)k.Value);
                    }
                    foreach (var a in call.Args)
                    {
                        Report.Role("arg " + (a is Temp ? "temp" : a is FrameAddr fa ? (fa.Offset < 0 ? "frame-arg" : "frame") : a is Elem ? "elem" : a is Const ? "global" : a is Load l && l.Addr is FrameAddr p && p.Offset < 0 ? "pointer-arg" : "other"));
                        CountRoles(a);
                    }
                }
                else if (e is Op o) { CountRoles(o.A); CountRoles(o.B); }
                else if (e is Load l2) { if (l2.Addr is Load) Report.Role("deref-pointer"); CountRoles(l2.Addr); }
                else if (e is Elem el) { Report.Role("index " + (el.Base is FrameAddr fb ? (fb.Offset < 0 ? "frame-arg" : "local-array") : el.Base is Const ? "global" : el.Base is Load lb && lb.Addr is FrameAddr pb ? (pb.Offset < 0 ? "pointer-arg" : "pointer-local") : "other")); CountRoles(el.Base); CountRoles(el.Index); }
                else if (e is Temp t) CountRoles(t.Value);
                else if (e is Const c && c.Negated) Report.Role("negated-constant");
                else if (e is Const c2 && (short)c2.Value < 0) Report.Role("negative-pushed");
            }

            /// <summary>Collects the labels that gotos go to, before printing.</summary>
            public void FindGotos(List<Node> list)
            {
                foreach (var n in list)
                {
                    var j = n as JumpNode;
                    if (j != null && j.What == "goto") GotoLabels.Add(j.Dest);
                    foreach (var c in Shaper.Children(n)) FindGotos(c);
                }
            }

            static IEnumerable<List<Node>> Shaper_Children(Node n)
            {
                if (n is IfNode i) { yield return i.Then; if (i.Else != null) yield return i.Else; }
                if (n is WhileNode w) yield return w.Body;
                if (n is MenuNode m) { foreach (var c in m.Choices) if (c.Body != null) yield return c.Body; if (m.Default != null) yield return m.Default; }
                if (n is SwitchNode s) { foreach (var c in s.Cases) yield return c.Body; if (s.Default != null) yield return s.Default; }
            }

            void Block(List<Node> list)
            {
                foreach (var n in list) Statement(n);
            }

            void Nested(List<Node> list)
            {
                indent++;
                Block(list);
                indent--;
            }

            void Statement(Node n)
            {
                switch (n)
                {
                    case SNode s: Simple(s.F); break;
                    case LabelNode l:
                        if (GotoLabels.Contains(l.Addr))
                        {
                            int keep = indent;
                            indent = Math.Max(0, indent - 1);
                            Line(l.Addr, C.Names.Label(l.Addr) + ":", PseudoKind.Label);
                            indent = keep;
                        }
                        break;
                    case DeadNode d: Shown.AddRange(d.Covers); break;
                    case ReturnNode r:
                        foreach (var f in r.Covers) Show(f);
                        Report.Role(r.Covers.Count > 1 ? "return-value " + r.Covers[1].Op : "return-value fall-in");
                        Line(r.Addr, r.Value == null ? "return;" : "return " + R(r.Value) + ";", PseudoKind.Statement, r.Covers[r.Covers.Count - 1].End, r.Covers[0].Start);
                        break;
                    case JumpNode j:
                        {
                            Show(j.F);
                            Report.Role(j.What + (j.When != null ? "-if" : "") + " " + j.F.Op);
                            string what = j.What == "goto" ? "goto " + C.Names.Label(j.Dest) : j.What;
                            if (j.What == "goto") { Report.Gotos++; Report.GotoSites.Add(C.FunctionNames[Fn.Start] + " " + j.F.Addr.ToString("X4") + " " + j.F.Op + (j.Dest > j.F.Addr ? " forward" : " back") + " goto " + C.Names.Label(j.Dest)); }
                            Line(j.F.Addr, (j.When != null ? "if (" + R(j.When) + ") " : "") + what + ";", PseudoKind.Statement, j.F.End, j.F.Start);
                            break;
                        }
                    case IfNode i: If(i, false); break;
                    case WhileNode w:
                        {
                            if (GotoLabels.Contains(w.Head.Addr))
                            {
                                int keep = indent;
                                indent = Math.Max(0, indent - 1);
                                Line(w.Head.Addr, C.Names.Label(w.Head.Addr) + ":", PseudoKind.Label);
                                indent = keep;
                            }
                            if (w.CondBranch != null) Show(w.CondBranch);
                            Line(w.Addr, "while (" + (w.Cond == null ? "true" : R(w.Cond)) + ") {", PseudoKind.Statement, w.CondBranch != null ? w.CondBranch.End : -1, w.CondBranch != null ? w.CondBranch.Start : -1);
                            Nested(w.Body);
                            if (w.TrailingBreak) { indent++; Line(w.Addr, "break;", PseudoKind.Statement); indent--; }
                            if (w.Back != null) Show(w.Back);
                            Report.Role("while" + (w.Cond != null ? "-cond" : "-true") + " " + (w.Back != null ? w.Back.Op.ToString() : "continue-only"));
                            Line(w.Back != null ? w.Back.Addr : w.Addr, "}", PseudoKind.Comment);
                            break;
                        }
                    case MenuNode m: Menu(m); break;
                    case SwitchNode sw: Switch(sw); break;
                }
            }

            /// <summary>Whether the statements print nothing (only jumps nothing needs, and labels no goto names).</summary>
            bool Invisible(List<Node> list)
            {
                return list.All(x => x is DeadNode || x is LabelNode l && !GotoLabels.Contains(l.Addr));
            }

            void If(IfNode i, bool elseIf)
            {
                Show(i.Branch);
                Report.Role(i.Skip != null ? "if-else " + i.Skip.Op : "if");
                // if (c) {} else { ... } made with a BRA (Origin's compiler, from an else in the source) is shown as it
                // is, so it compiles back to the same code; one made with a JMP is shown as if (!c) { ... }
                if (i.Else != null && Invisible(i.Then) && !Invisible(i.Else) && (i.Skip == null || i.Skip.Op != BablOp.BRA))
                {
                    Line(i.Branch.Addr, (elseIf ? "} else if (" : "if (") + R(Not(i.Cond)) + ") {", PseudoKind.Statement, i.Branch.End, i.Branch.Start);
                    Block(i.Then);
                    if (i.Skip != null) Show(i.Skip);
                    Nested(i.Else);
                    Line(i.Branch.Addr, "}", PseudoKind.Comment);
                    return;
                }
                Line(i.Branch.Addr, (elseIf ? "} else if (" : "if (") + R(i.Cond) + ") {", PseudoKind.Statement, i.Branch.End, i.Branch.Start);
                Nested(i.Then);
                if (i.Skip != null) Show(i.Skip);
                if (i.Else != null && i.Else.Count > 0)
                {
                    // else if: the else holds one if and nothing else shown
                    var shown = i.Else.Where(x => !(x is DeadNode) && !(x is LabelNode l && !GotoLabels.Contains(l.Addr))).ToList();
                    if (shown.Count == 1 && shown[0] is IfNode inner)
                    {
                        int at = i.Else.IndexOf(inner);
                        for (int k = 0; k < at; k++) Statement(i.Else[k]);
                        If(inner, true);
                        for (int k = at + 1; k < i.Else.Count; k++) Statement(i.Else[k]);
                        return;
                    }
                    Line(i.Skip != null ? i.Skip.Addr : i.Branch.Addr, "} else {", PseudoKind.Statement, i.Skip != null ? i.Skip.End : -1);
                    Nested(i.Else);
                }
                else if (i.Else != null) Line(i.Skip.Addr, "} else {", PseudoKind.Statement, i.Skip.End);
                Line(i.Branch.Addr, "}", PseudoKind.Comment);
            }

            void Menu(MenuNode m)
            {
                foreach (var f in m.Covers)
                {
                    if (f == m.Assign) break;
                    Show(f);
                }
                Show(m.Assign);
                string head = (m.Answer != null ? Deref(m.Answer) + " = " : "") + (m.Filtered ? "fmenu" : "menu") + " {";
                Line(m.Assign.Addr, head, PseudoKind.Menu, m.Assign.End, m.Covers[0].Start);
                indent++;
                foreach (var ch in m.Choices)
                {
                    string flag = m.Filtered && !(ch.Flag is Const k && k.Value == 1) ? " if (" + R(ch.Flag) + ")" : "";
                    if (!ch.HasCase) { Line(ch.Addr, StrLit(ch.Id) + flag + ";", PseudoKind.Menu); continue; }
                    Show(ch.Compare);
                    Line(ch.Compare.Addr, StrLit(ch.Id) + flag + ":" + (Invisible(ch.Body) ? " {}" : ""), PseudoKind.Menu, ch.Compare.End, ch.Compare.Start);
                    Nested(ch.Body);
                    Show(ch.EndJump);
                    Report.Role("menu-case " + ch.EndJump.Op);
                }
                if (m.Default != null)
                {
                    Line(m.Assign.Addr, "default:", PseudoKind.Menu);
                    Nested(m.Default);
                }
                indent--;
                Line(m.Assign.Addr, "}", PseudoKind.Comment);
            }

            void Switch(SwitchNode s)
            {
                Show(s.Assign);
                Line(s.Assign.Addr, "switch (" + R(s.Value) + ") {", PseudoKind.Statement, s.Assign.End, s.Assign.Start);
                foreach (var cs in s.Cases)
                {
                    Show(cs.Compare);
                    Line(cs.Compare.Addr, "case " + cs.Value + ":" + (Invisible(cs.Body) ? " {}" : ""), PseudoKind.Statement, cs.Compare.End, cs.Compare.Start);
                    Nested(cs.Body);
                    Show(cs.EndJump);
                }
                if (s.Default != null) { Line(s.Assign.Addr, "default:", PseudoKind.Statement); Nested(s.Default); }
                Line(s.Assign.Addr, "}", PseudoKind.Comment);
            }

            void Simple(Flat f)
            {
                Show(f);
                switch (f.Kind)
                {
                    case FK.Assign: Line(f.Addr, Deref(f.Target) + " = " + R(f.Value) + ";", PseudoKind.Statement, f.End, f.Start); break;
                    case FK.Call: Line(f.Addr, R(f.E) + ";", PseudoKind.Statement, f.End, f.Start); break;
                    case FK.Say:
                    case FK.Respond:
                        {
                            bool say = f.Kind == FK.Say;
                            var k = f.E as Const;
                            Line(f.Addr, (say ? "say " : "respond ") + (k != null ? StrLit(k.Value) : R(f.E)) + ";", say ? PseudoKind.Say : PseudoKind.Respond, f.End, f.Start);
                            break;
                        }
                    case FK.Exit: Line(f.Addr, "exit;", PseudoKind.Statement, f.End, f.Start); break;
                    case FK.Return: Line(f.Addr, Fn.Start == 0 ? "end;" : "return;", PseudoKind.Statement, f.End, f.Start); break;
                    case FK.SaveReg: Line(f.Addr, "save_reg " + R(f.E) + ";", PseudoKind.Statement, f.End, f.Start); break;
                    case FK.Word: Line(f.Addr, "word 0x" + f.Word.ToString("X4") + ";    // not an instruction", PseudoKind.Statement, f.End, f.Start); break;
                    default: Line(f.Addr, f.Kind.ToString(), PseudoKind.Statement, f.End, f.Start); break;
                }
            }

            /// <summary>Every statement of the code shown exactly once, in the code's order.</summary>
            public void Check()
            {
                var seen = new HashSet<Flat>();
                int prev = -1;
                foreach (var f in Shown)
                {
                    if (!seen.Add(f)) Report.Problems.Add(string.Format("{0}: {1} shown twice", C.FunctionNames[Fn.Start], f));
                    if (f.Addr <= prev) Report.Problems.Add(string.Format("{0}: {1} shown after {2:X4}", C.FunctionNames[Fn.Start], f, prev));
                    prev = Math.Max(prev, f.Addr);
                }
                foreach (var f in Fn.Flats)
                    if (f.Kind != FK.Label && !seen.Contains(f)) Report.Problems.Add(string.Format("{0}: {1} not shown", C.FunctionNames[Fn.Start], f));
            }
        }

        /// <summary>The condition with its sense turned over: a compare's opposite, !x's x, or !(x).</summary>
        static Expr Not(Expr e)
        {
            var o = e as Op;
            string sym;
            if (o != null && !o.Unary && opposite.TryGetValue(o.Symbol, out sym)) return new Op { Symbol = sym, A = o.A, B = o.B };
            if (o != null && o.Unary && o.Symbol == "!") return o.A;
            return new Op { Symbol = "!", A = e, Unary = true };
        }

        /// <summary>
        /// The structured view: every function's control flow as if / else, while, menu and
        /// switch blocks, in the conversation language (docs/conversation-language.md).
        /// </summary>
        static void Structure(Conversation conv, string[] strings, GameKind kind, ConversationNames names, DecompileResult r)
        {
            var report = new StructureReport();
            r.Structure = report;
            Ctx c;
            var trees = Trees(conv, strings, kind, names, report, out c);
            var lines = new List<PseudoLine>();
            lines.Add(new PseudoLine { Address = 0, Kind = PseudoKind.Comment, Text = string.Format("// {0} variables in memory (imports and private globals), string block 0x{1:X4}", conv.Variables, conv.StringBlock) });
            foreach (var t in trees)
            {
                c.Function = t.Fn.Start;
                var p = new Printer { C = c, Fn = t.Fn, Shaper = t.Shaper, Report = report, Lines = lines };
                p.Layout(t.Body);
                p.FindGotos(t.Body);
                p.Function(t.Body, t.CallerArgs);
                p.Check();
            }
            r.Lines.Clear();
            r.Lines.AddRange(lines);
        }

        /// <summary>One function's structured statements.</summary>
        sealed class Tree { public Fn Fn; public Shaper Shaper; public List<Node> Body; public int CallerArgs; }

        static List<Tree> Trees(Conversation conv, string[] strings, GameKind kind, ConversationNames names, StructureReport report, out Ctx c)
        {
            names = names ?? ConversationNames.None;
            c = new Ctx { Conv = conv, Strings = strings, Kind = kind, Result = new DecompileResult(), Names = names };
            var lay = names.Layout ?? BablLayout.Of(conv);
            foreach (int f in lay.Functions) c.FunctionNames[f] = names.Function(f);
            var fl = new List<int>(lay.Functions);
            var fns = new List<Fn>();
            var callerArgs = new Dictionary<int, int>();
            for (int k = 0; k < fl.Count; k++)
            {
                int start = fl[k], end = k + 1 < fl.Count ? fl[k + 1] : conv.Code.Length;
                var fn = ReadFunction(c, lay, start, end, lay.Called.Contains(start) || start == 0);
                fns.Add(fn);
                foreach (var f in fn.Flats)
                {
                    Action<Expr> scan = null;
                    scan = e =>
                    {
                        if (e == null) return;
                        if (e is Call call)
                        {
                            if (!call.Builtin) { int n; callerArgs.TryGetValue(call.Target, out n); callerArgs[call.Target] = Math.Max(n, call.Pops); }
                            foreach (var a in call.Args) scan(a);
                        }
                        else if (e is Op o) { scan(o.A); scan(o.B); }
                        else if (e is Load l) scan(l.Addr);
                        else if (e is Elem el) { scan(el.Base); scan(el.Index); }
                        else if (e is Temp t) scan(t.Value);
                        else if (e is FrameAddr fa && fa.Offset <= -2) fn.Params = Math.Max(fn.Params, -fa.Offset - 1);
                    };
                    scan(f.Target); scan(f.Value); scan(f.E);
                }
            }
            var trees = new List<Tree>();
            foreach (var fn in fns)
            {
                c.Function = fn.Start;
                var shaper = new Shaper { Fn = fn, C = c, Report = report };
                var body = shaper.Run();
                int ca;
                callerArgs.TryGetValue(fn.Start, out ca);
                trees.Add(new Tree { Fn = fn, Shaper = shaper, Body = body, CallerArgs = ca });
            }
            return trees;
        }
    }
}
