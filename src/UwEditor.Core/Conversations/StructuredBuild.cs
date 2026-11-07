using System;
using System.Collections.Generic;
using System.Linq;

namespace UwEditor.Core.Conversations
{
    /// <summary>
    /// What the structured view could not fold into if, while, menu and switch, for one
    /// conversation: the gotos left, and the checks that every statement of the code is shown
    /// exactly once, in the code's order.
    /// </summary>
    public sealed class StructureReport
    {
        /// <summary>The goto statements left (each "if (c) goto L;" counts as one).</summary>
        public int Gotos;
        /// <summary>Where they are: function, address and label.</summary>
        public readonly List<string> GotoSites = new List<string>();
        /// <summary>Jumps that cannot run (right after another jump, a return or exit): not shown.</summary>
        public int DeadJumps;
        /// <summary>Menus shown as an assignment and an if chain, because their compares do not follow the choices.</summary>
        public int MenuFallbacks;
        /// <summary>Menus shown as menu { } blocks, and switches.</summary>
        public int Menus, Switches, Loops;
        /// <summary>Statements the structure lost, showed twice, or showed out of the code's order (none when all is well).</summary>
        public readonly List<string> Problems = new List<string>();
        /// <summary>Temporaries (SWAP stores) that no construct explains, shown as plain assignments.</summary>
        public int LooseTemps;
        /// <summary>Jumps to where control goes anyway (the end of an enclosing if, say): not shown.</summary>
        public int Redundant;
        /// <summary>How each construct's jumps are coded, "role opcode" → count (if-else BRA, return JMP...): what a compiler must emit to give the same bytes.</summary>
        public readonly SortedDictionary<string, int> Roles = new SortedDictionary<string, int>();
        /// <summary>Functions with a declared local after a temporary (the compiler puts temporaries after the locals).</summary>
        public int TempsBeforeLocals;
        internal void Role(string what) { int n; Roles.TryGetValue(what, out n); Roles[what] = n + 1; }
    }

    public static partial class Decompiler
    {
        // ----- the structured tree -----

        abstract class Node
        {
            /// <summary>The first flat of the node (Block sets it).</summary>
            public int Lo = -1;
            /// <summary>The flats this node shows (its own, not its children's).</summary>
            public readonly List<Flat> Covers = new List<Flat>();
        }
        sealed class SNode : Node { public Flat F; }
        sealed class LabelNode : Node { public int Addr; }
        sealed class ReturnNode : Node { public Expr Value; public int Addr; }
        sealed class JumpNode : Node
        {
            /// <summary>"break", "continue" or "goto".</summary>
            public string What; public int Dest; public Flat F;
            /// <summary>For a branch: the condition under which it jumps.</summary>
            public Expr When;
        }
        sealed class IfNode : Node
        {
            public Flat Branch; public Expr Cond;
            public List<Node> Then, Else;
            public Flat Skip;
        }
        sealed class WhileNode : Node
        {
            public Flat Head;
            /// <summary>The condition (null: true) and its branch.</summary>
            public Expr Cond; public Flat CondBranch;
            public List<Node> Body;
            /// <summary>The jump back at the end, or null when the loop only repeats by continue (shown with a break at the end).</summary>
            public Flat Back;
            public bool TrailingBreak;
            public int Addr;
        }
        sealed class Choice
        {
            public int Id; public Expr Flag; public int Addr;
            public bool HasCase; public List<Node> Body; public Flat Compare, EndJump;
        }
        sealed class MenuNode : Node
        {
            public bool Filtered; public Flat Assign; public Call Call;
            public readonly List<Choice> Choices = new List<Choice>();
            public List<Node> Default;
            /// <summary>When the menu has no compare chain: the variable that gets the answer.</summary>
            public Expr Answer;
        }
        sealed class Case { public int Value; public List<Node> Body; public Flat Compare, EndJump; }
        sealed class SwitchNode : Node
        {
            public Flat Assign; public Expr Value;
            public readonly List<Case> Cases = new List<Case>();
            public List<Node> Default;
        }

        /// <summary>The structuring of one function.</summary>
        sealed class Shaper
        {
            public Fn Fn;
            public Ctx C;
            public StructureReport Report;
            List<Flat> fl;
            /// <summary>Menu assignments by flat index, with the array stores before them.</summary>
            readonly Dictionary<int, MenuInfo> menus = new Dictionary<int, MenuInfo>();
            /// <summary>Flat index of a menu's first array store → the menu assignment's index.</summary>
            readonly Dictionary<int, int> menuStart = new Dictionary<int, int>();
            /// <summary>Back jumps by target address: the indexes of the jumps.</summary>
            readonly Dictionary<int, List<int>> backJumps = new Dictionary<int, List<int>>();
            readonly HashSet<int> loopHeads = new HashSet<int>();
            /// <summary>Temporary slots: the menu arrays and answers, switch values, folded arguments.</summary>
            public readonly HashSet<int> TempSlots = new HashSet<int>();

            sealed class MenuInfo
            {
                public int First;
                public bool Filtered;
                public int Choices, Flags;
                public List<int> Ids = new List<int>();
                public List<Expr> FlagValues = new List<Expr>();
                public List<int> IdAddr = new List<int>();
                public List<Flat> Stores = new List<Flat>();
            }

            sealed class Scope
            {
                public int Break = -1, Continue = -1;
            }

            int AddrAt(int i)
            {
                if (i >= fl.Count) return Fn.End;
                return fl[i].Kind == FK.Label ? fl[i].Addr : fl[i].Start;
            }

            int Index(int addr)
            {
                int k;
                return Fn.IndexOf.TryGetValue(addr, out k) ? k : -1;
            }

            public List<Node> Run()
            {
                fl = Fn.Flats;
                for (int i = 0; i < fl.Count; i++)
                {
                    var f = fl[i];
                    if ((f.Kind == FK.Jump || f.Kind == FK.Branch) && f.Dest <= f.Addr)
                    {
                        List<int> l;
                        if (!backJumps.TryGetValue(f.Dest, out l)) backJumps[f.Dest] = l = new List<int>();
                        l.Add(i);
                    }
                    MenuInfo m;
                    if (IsMenuAssign(i, out m)) { menus[i] = m; if (m.First < i) menuStart[m.First] = i; }
                }
                foreach (var f in fl) if (f.Folded) { var fa = f.Target as FrameAddr; if (fa != null) TempSlots.Add(fa.Offset); }
                int hi = Fn.ExitIdx >= 0 ? Fn.ExitIdx : Fn.EpilogueIdx >= 0 ? Index(fl[Fn.EpilogueIdx].Start) : fl.Count;
                if (hi < 0) hi = Fn.EpilogueIdx;
                refs = new Dictionary<int, int>();
                foreach (var f in fl) if (f.Kind == FK.Jump || f.Kind == FK.Branch) { int n; refs.TryGetValue(f.Dest, out n); refs[f.Dest] = n + 1; }
                var body = Block(0, hi, new Scope(), new HashSet<int> { AddrAt(hi) });
                return body;
            }

            // ----- menus -----

            /// <summary>
            /// answer = babl_menu(choices) stored as a temporary, after the stores that fill the
            /// choices array (choices[k] = string, 0 at the end) and for babl_fmenu the flags
            /// array (choices[k], flags[k] in turn).
            /// </summary>
            bool IsMenuAssign(int i, out MenuInfo m)
            {
                m = null;
                var f = fl[i];
                if (f.Kind != FK.Assign || !f.Swap || !(f.Target is FrameAddr)) return false;
                var call = f.Value as Call;
                if (call == null || !call.Builtin || (call.Name != "babl_menu" && call.Name != "babl_fmenu")) return false;
                bool filtered = call.Name == "babl_fmenu";
                if (call.Args.Count != (filtered ? 2 : 1)) return false;
                var ca = call.Args[call.Args.Count - 1] as FrameAddr;
                var fa = filtered ? call.Args[0] as FrameAddr : null;
                if (ca == null || filtered && fa == null) return false;
                m = new MenuInfo { Filtered = filtered, Choices = ca.Offset, Flags = filtered ? fa.Offset : 0 };
                // walk back over the array stores: the terminator, then (flag, id) pairs from the last
                int j = i - 1;
                int slot;
                Func<int, int> skipTemps = x => { while (x >= 0 && fl[x].Kind == FK.Assign && fl[x].Folded) x--; return x; };
                if (j < 0 || !StoreInto(fl[j], m.Choices, out slot) || !IsZero(fl[j].Value)) { m = null; return false; }
                int n = slot - 1;   // choices[n + 1] = 0
                var stores = new List<Flat> { fl[j] };
                j--;
                for (int k = n; k >= 1; k--)
                {
                    // babl_fmenu: flags[k] = flag; choices[k] = string (conv/CONVERSE.C conv_fmenu reads them in pairs)
                    var id = j >= 0 ? fl[j].Value as Const : null;
                    if (j < 0 || !StoreInto(fl[j], m.Choices, out slot) || slot != k || id == null || id.Value == 0) { m = null; return false; }
                    stores.Insert(0, fl[j]);
                    m.Ids.Insert(0, id.Value);
                    m.IdAddr.Insert(0, id.At);
                    j = skipTemps(j - 1);
                    Expr flag = null;
                    if (filtered)
                    {
                        if (j < 0 || !StoreInto(fl[j], m.Flags, out slot) || slot != k) { m = null; return false; }
                        flag = fl[j].Value;
                        stores.Insert(0, fl[j]);
                        j = skipTemps(j - 1);
                    }
                    m.FlagValues.Insert(0, flag);
                }
                if (n == 0) { m = null; return false; }
                m.First = j + 1;
                m.Stores = stores;
                return true;
            }

            static bool IsZero(Expr e) { var k = e as Const; return k != null && k.Value == 0; }

            /// <summary>An assignment to array[k] (a frame array at slot base, k from 1), not a temporary.</summary>
            static bool StoreInto(Flat f, int baseSlot, out int k)
            {
                k = 0;
                if (f.Kind != FK.Assign || f.Swap) return false;
                var el = f.Target as Elem;
                if (el == null) return false;
                var b = el.Base as FrameAddr; var idx = el.Index as Const;
                if (b == null || idx == null || b.Offset != baseSlot) return false;
                k = (short)idx.Value;
                return true;
            }

            /// <summary>"slot == k" (menu: answer first) or "k == slot" (switch: the constant first).</summary>
            static bool IsCompare(Flat f, int slot, bool constFirst, out int k)
            {
                k = 0;
                if (f.Kind != FK.Branch || f.Op != BablOp.BEQ) return false;
                var o = f.E as Op;
                if (o == null || o.Unary || o.Symbol != "==") return false;
                var c = (constFirst ? o.A : o.B) as Const;
                var l = (constFirst ? o.B : o.A) as Load;
                var fa = l == null ? null : l.Addr as FrameAddr;
                if (c == null || fa == null || fa.Offset != slot) return false;
                k = (short)c.Value;
                return true;
            }

            sealed class Arm { public int Value; public int CompareIdx, BodyLo, BodyHi, JumpIdx; }

            /// <summary>How many jumps and branches go to each address.</summary>
            Dictionary<int, int> refs;

            int Refs(int addr) { int n; return refs.TryGetValue(addr, out n) ? n : 0; }

            /// <summary>
            /// The addresses that lead to the same place as addr: addr itself, and where the
            /// forward jumps found there (after labels) go; with the enclosing region's follow
            /// when addr is where that region ends.
            /// </summary>
            HashSet<int> Thread(int addr, HashSet<int> outer, int outerEnd)
            {
                var set = new HashSet<int>();
                for (int guard = 0; guard < 64; guard++)
                {
                    set.Add(addr);
                    if (addr == outerEnd || outer.Contains(addr)) { set.UnionWith(outer); break; }
                    int k = addr == Fn.End ? fl.Count : Index(addr);
                    if (k < 0) break;
                    while (k < fl.Count && fl[k].Kind == FK.Label) k++;
                    if (k >= fl.Count) { set.Add(Fn.End); break; }
                    var g = fl[k];
                    if (g.Kind == FK.Jump && g.Dest > g.Addr) { addr = g.Dest; continue; }
                    break;
                }
                return set;
            }

            /// <summary>
            /// The compare chain after a menu or switch: "if slot == k { body; JMP end }" for each
            /// case, the last compare jumping to the end (or to code run when no case matches).
            /// </summary>
            bool Chain(int from, int hi, HashSet<int> fw, int slot, bool constFirst, out List<Arm> arms, out int endIdx, out int defaultLo, out int end)
            {
                arms = new List<Arm>();
                endIdx = -1; defaultLo = -1;
                end = -1;
                int j = from;
                while (j < hi)
                {
                    int k;
                    if (!IsCompare(fl[j], slot, constFirst, out k)) break;
                    int t = fl[j].Dest == Fn.End ? fl.Count : Index(fl[j].Dest);
                    if (t <= j + 1) break;
                    if (t > hi)
                    {
                        // the last case's compare goes to the end, beyond the region (the region's follow)
                        if (!fw.Contains(fl[j].Dest)) break;
                        int ji = -1;
                        for (int x = hi - 1; x > j; x--) if (fl[x].Kind != FK.Label) { ji = x; break; }
                        if (ji < 0 || fl[ji].Kind != FK.Jump || fl[ji].Op != BablOp.JMP || (end >= 0 ? fl[ji].Dest != end : !fw.Contains(fl[ji].Dest))) break;
                        if (end < 0) end = fl[ji].Dest;
                        arms.Add(new Arm { Value = k, CompareIdx = j, BodyLo = j + 1, BodyHi = ji, JumpIdx = ji });
                        j = hi;
                        break;
                    }
                    var last = fl[t - 1];
                    if (last.Kind != FK.Jump || last.Op != BablOp.JMP) break;
                    if (end < 0) end = last.Dest;
                    else if (last.Dest != end) break;
                    arms.Add(new Arm { Value = k, CompareIdx = j, BodyLo = j + 1, BodyHi = t - 1, JumpIdx = t - 1 });
                    j = t;
                    if (fl[arms[arms.Count - 1].CompareIdx].Dest == end) break;
                    // the next case's label: only this compare goes there
                    while (j < hi && fl[j].Kind == FK.Label && Refs(fl[j].Addr) == 1) j++;
                }
                if (arms.Count == 0) return false;
                endIdx = end == Fn.End ? fl.Count : Index(end);
                if (endIdx < j || endIdx > hi)
                {
                    // a menu at the end of the region: its end is where the region goes
                    if (!fw.Contains(end)) { arms.Clear(); return false; }
                    endIdx = hi;
                }
                if (fl[arms[arms.Count - 1].CompareIdx].Dest != end) defaultLo = j;
                return true;
            }

            int Menu(int i, int hi, Scope sc, HashSet<int> fw, List<Node> outList)
            {
                var m = menus[i];
                var f = fl[i];
                var call = (Call)f.Value;
                int slot = ((FrameAddr)f.Target).Offset;
                var node = new MenuNode { Filtered = m.Filtered, Assign = f, Call = call };
                node.Covers.AddRange(m.Stores);
                node.Covers.Add(f);
                for (int k = 0; k < m.Ids.Count; k++) node.Choices.Add(new Choice { Id = m.Ids[k], Flag = m.FlagValues[k], Addr = m.IdAddr[k] });
                List<Arm> arms; int endIdx, defaultLo, end;
                bool chained = Chain(i + 1, hi, fw, slot, false, out arms, out endIdx, out defaultLo, out end);
                // the cases must follow the choices: each a choice (by number, or by string for babl_fmenu), in order
                int prev = -1;
                var match = new List<int>();
                if (chained)
                {
                    foreach (var a in arms)
                    {
                        int c = m.Filtered ? m.Ids.IndexOf(a.Value) : a.Value - 1;
                        if (c < 0 || c >= m.Ids.Count || c <= prev) { chained = false; break; }
                        match.Add(c);
                        prev = c;
                    }
                }
                Report.Menus++;
                TempSlots.Add(slot);
                for (int k = 0; k < 21; k++) TempSlots.Add(m.Choices + k);
                if (m.Filtered) for (int k = 0; k < 21; k++) TempSlots.Add(m.Flags + k);
                if (!chained)
                {
                    if (arms.Count > 0) Report.MenuFallbacks++;
                    node.Answer = f.Target;
                    outList.Add(node);
                    return i + 1;
                }
                var endFw = Thread(end, fw, AddrAt(hi));
                for (int a = 0; a < arms.Count; a++)
                {
                    var arm = arms[a];
                    var ch = node.Choices[match[a]];
                    ch.HasCase = true;
                    ch.Compare = fl[arm.CompareIdx];
                    ch.EndJump = fl[arm.JumpIdx];
                    var caseFw = new HashSet<int>(endFw) { AddrAt(arm.BodyHi) };
                    ch.Body = Block(arm.BodyLo, arm.BodyHi, sc, caseFw);
                }
                if (defaultLo >= 0) node.Default = Block(defaultLo, endIdx, sc, endFw);
                outList.Add(node);
                return endIdx;
            }

            bool IsSwitchAssign(int i, int hi)
            {
                var f = fl[i];
                if (f.Kind != FK.Assign || !f.Swap || !(f.Target is FrameAddr)) return false;
                int k;
                return i + 1 < hi && IsCompare(fl[i + 1], ((FrameAddr)f.Target).Offset, true, out k);
            }

            int Switch(int i, int hi, Scope sc, HashSet<int> fw, List<Node> outList)
            {
                var f = fl[i];
                int slot = ((FrameAddr)f.Target).Offset;
                List<Arm> arms; int endIdx, defaultLo, end;
                if (!Chain(i + 1, hi, fw, slot, true, out arms, out endIdx, out defaultLo, out end)) return -1;
                var node = new SwitchNode { Assign = f, Value = f.Value };
                node.Covers.Add(f);
                var endFw = Thread(end, fw, AddrAt(hi));
                foreach (var arm in arms)
                {
                    var cs = new Case { Value = arm.Value, Compare = fl[arm.CompareIdx], EndJump = fl[arm.JumpIdx] };
                    cs.Body = Block(arm.BodyLo, arm.BodyHi, sc, new HashSet<int>(endFw) { AddrAt(arm.BodyHi) });
                    node.Cases.Add(cs);
                }
                if (defaultLo >= 0) node.Default = Block(defaultLo, endIdx, sc, endFw);
                TempSlots.Add(slot);
                Report.Switches++;
                outList.Add(node);
                return endIdx;
            }

            // ----- blocks -----

            /// <summary>The statements of flats lo..hi-1; fw holds the addresses that lead where control goes after them.</summary>
            List<Node> Block(int lo, int hi, Scope sc, HashSet<int> fw)
            {
                var list = new List<Node>();
                int i = lo;
                while (i < hi)
                {
                    int before = list.Count;
                    int start = i;
                    i = Statement(i, hi, sc, fw, list);
                    for (int k = before; k < list.Count; k++) if (list[k].Lo < 0) list[k].Lo = start;
                }
                return list;
            }

            int Statement(int i, int hi, Scope sc, HashSet<int> fw, List<Node> list)
            {
                var f = fl[i];
                switch (f.Kind)
                {
                    case FK.Label:
                        {
                            int next = Loop(i, hi, sc, fw, list);
                            if (next >= 0) return next;
                            list.Add(new LabelNode { Addr = f.Addr });
                            return i + 1;
                        }
                    case FK.Branch: return If(i, hi, sc, fw, list);
                    case FK.Jump: return Jump(i, hi, sc, fw, list);
                    case FK.Assign:
                        {
                            int mi;
                            if (menuStart.TryGetValue(i, out mi) && mi < hi) return Menu(mi, hi, sc, fw, list);
                            if (menus.ContainsKey(i)) return Menu(i, hi, sc, fw, list);
                            if (IsSwitchAssign(i, hi)) { int next = Switch(i, hi, sc, fw, list); if (next > 0) return next; }
                            var fa = f.Target as FrameAddr;
                            if (Fn.ExitAddr >= 0 && fa != null && fa.Offset == Fn.Result)
                            {
                                // local = e; JMP exit (or falling into it): return e
                                if (i + 1 < hi && fl[i + 1].Kind == FK.Jump && fl[i + 1].Dest == Fn.ExitAddr)
                                {
                                    var r = new ReturnNode { Value = f.Value, Addr = f.Addr };
                                    r.Covers.Add(f); r.Covers.Add(fl[i + 1]);
                                    list.Add(r);
                                    return i + 2;
                                }
                                if (i + 1 == hi && fw.Contains(Fn.ExitAddr))
                                {
                                    var r = new ReturnNode { Value = f.Value, Addr = f.Addr };
                                    r.Covers.Add(f);
                                    list.Add(r);
                                    return i + 1;
                                }
                            }
                            // a folded temporary is shown inside the call that takes it
                            if (!f.Folded)
                            {
                                if (f.Swap) Report.LooseTemps++;
                                var s = new SNode { F = f };
                                s.Covers.Add(f);
                                list.Add(s);
                            }
                            return i + 1;
                        }
                    default:
                        {
                            var s = new SNode { F = f };
                            s.Covers.Add(f);
                            list.Add(s);
                            return i + 1;
                        }
                }
            }

            int[] Snapshot() { return new[] { Report.Menus, Report.Switches, Report.Loops, Report.MenuFallbacks, Report.LooseTemps, Report.DeadJumps, Report.Redundant }; }
            void Restore(int[] s) { Report.Menus = s[0]; Report.Switches = s[1]; Report.Loops = s[2]; Report.MenuFallbacks = s[3]; Report.LooseTemps = s[4]; Report.DeadJumps = s[5]; Report.Redundant = s[6]; }

            /// <summary>A label that later jumps come back to: a while loop. Returns -1 when it is not one.</summary>
            int Loop(int i, int hi, Scope sc, HashSet<int> fw, List<Node> list)
            {
                int L = fl[i].Addr;
                List<int> back;
                if (!backJumps.TryGetValue(L, out back) || loopHeads.Contains(L)) return -1;
                int last = back.Max();
                if (last >= hi) return -1;
                // no jump comes back to the label from after the loop (jumps from before only arrive at its start)
                for (int k = hi; k < fl.Count; k++)
                {
                    var g = fl[k];
                    if ((g.Kind == FK.Jump || g.Kind == FK.Branch) && g.Dest == L) return -1;
                }
                // L: if (!c) goto exit; ...; goto L; exit: is while (c) { ... }
                if (fl[last].Kind == FK.Jump && i + 1 < last && fl[i + 1].Kind == FK.Branch
                    && Thread(AddrAt(last + 1), fw, AddrAt(hi)).Contains(fl[i + 1].Dest))
                {
                    if (EnteredFromOutside(i, last + 1)) return -1;
                    loopHeads.Add(L);
                    return While(i, last, hi, fw, list);
                }
                var snap = Snapshot();
                var heads = new HashSet<int>(loopHeads);
                loopHeads.Add(L);
                // first the rest of the region as the loop's body, to see where the jumps back are
                var tent = Block(i + 1, hi, new Scope { Continue = L }, fw);
                int m = -1;
                for (int k = 0; k < tent.Count; k++) if (HasContinue(tent[k])) m = k;
                Restore(snap);
                loopHeads.Clear(); loopHeads.UnionWith(heads);
                if (m < 0) return -1;
                loopHeads.Add(L);
                var tj = tent[m] as JumpNode;
                if (tj != null && tj.When == null && tj.F == fl[last])
                {
                    if (EnteredFromOutside(i, last + 1)) { loopHeads.Remove(L); return -1; }
                    return While(i, last, hi, fw, list);
                }
                // repeated only by continue (from a menu's case, say): while (true) { ...; break; }
                int restLo = m + 1 < tent.Count ? tent[m + 1].Lo : hi;
                // a goto from outside into the loop's body reads worse than the goto back
                if (EnteredFromOutside(i, restLo)) { loopHeads.Remove(L); return -1; }
                var wl = new WhileNode { Head = fl[i], Addr = L };
                var bodyFw = Thread(AddrAt(restLo), fw, AddrAt(hi));
                wl.Body = Block(i + 1, restLo, new Scope { Continue = L }, bodyFw);
                wl.TrailingBreak = FallsThrough(wl.Body);
                Report.Loops++;
                list.Add(wl);
                return restLo;
            }

            /// <summary>The loop from the label at i to the jump back at last: while (cond) { ... } or while (true) { ... }.</summary>
            int While(int i, int last, int hi, HashSet<int> fw, List<Node> list)
            {
                int L = fl[i].Addr;
                {
                    int exit = AddrAt(last + 1);
                    var exitFw = Thread(exit, fw, AddrAt(hi));
                    var w = new WhileNode { Head = fl[i], Back = fl[last], Addr = L };
                    int bodyLo = i + 1;
                    if (bodyLo < last && fl[bodyLo].Kind == FK.Branch && exitFw.Contains(fl[bodyLo].Dest))
                    {
                        w.Cond = fl[bodyLo].E; w.CondBranch = fl[bodyLo];
                        w.Addr = fl[bodyLo].Addr;
                        bodyLo++;
                    }
                    w.Body = Block(bodyLo, last, new Scope { Break = exit, Continue = L }, new HashSet<int> { AddrAt(last), L });
                    Report.Loops++;
                    list.Add(w);
                    return last + 1;
                }
            }

            /// <summary>Some jump from outside flats lo..hi-1 goes to a label inside them (not the first).</summary>
            bool EnteredFromOutside(int lo, int hi)
            {
                var inside = new HashSet<int>();
                for (int k = lo + 1; k < hi; k++) if (fl[k].Kind == FK.Label && fl[k].Addr != fl[lo].Addr) inside.Add(fl[k].Addr);
                if (inside.Count == 0) return false;
                for (int k = 0; k < fl.Count; k++)
                {
                    if (k >= lo && k < hi) continue;
                    var g = fl[k];
                    if ((g.Kind == FK.Jump || g.Kind == FK.Branch) && inside.Contains(g.Dest)) return true;
                }
                return false;
            }

            static bool HasContinue(Node n)
            {
                var j = n as JumpNode;
                if (j != null) return j.What == "continue";
                if (n is WhileNode) return false;
                foreach (var c in Children(n)) foreach (var x in c) if (HasContinue(x)) return true;
                return false;
            }

            public static IEnumerable<List<Node>> Children(Node n)
            {
                var i = n as IfNode;
                if (i != null) { yield return i.Then; if (i.Else != null) yield return i.Else; }
                var w = n as WhileNode;
                if (w != null) yield return w.Body;
                var m = n as MenuNode;
                if (m != null) { foreach (var c in m.Choices) if (c.Body != null) yield return c.Body; if (m.Default != null) yield return m.Default; }
                var s = n as SwitchNode;
                if (s != null) { foreach (var c in s.Cases) yield return c.Body; if (s.Default != null) yield return s.Default; }
            }

            /// <summary>Whether control can run off the end of these statements.</summary>
            static bool FallsThrough(List<Node> list)
            {
                var shown = list.Where(x => !(x is DeadNode) && !(x is LabelNode)).ToList();
                if (shown.Count == 0) return true;
                var n = shown[shown.Count - 1];
                if (n is ReturnNode) return false;
                var j = n as JumpNode;
                if (j != null) return j.When != null;
                var s = n as SNode;
                if (s != null) return !(s.F.Kind == FK.Exit || s.F.Kind == FK.Return);
                var i = n as IfNode;
                if (i != null) return i.Else == null || FallsThrough(i.Then) || FallsThrough(i.Else);
                return true;
            }

            int If(int i, int hi, Scope sc, HashSet<int> fw, List<Node> list)
            {
                var f = fl[i];
                int T = f.Dest;
                int t = T == Fn.End ? fl.Count : Index(T);
                var node = new IfNode { Branch = f, Cond = f.E };
                node.Covers.Add(f);
                if (t < 0 || t <= i || t > hi)
                {
                    if (fw.Contains(T) && T > f.Addr)
                    {
                        // if (c) { the rest of the region }
                        node.Then = Block(i + 1, hi, sc, fw);
                        list.Add(node);
                        return hi;
                    }
                    // a branch out of the region: if (!c) break / continue / goto
                    list.Add(JumpOut(f, Not(f.E), sc));
                    return i + 1;
                }
                var last = t - 1 > i ? fl[t - 1] : null;
                if (last != null && last.Kind == FK.Jump && last.Dest > last.Addr && !(last.Dest == Fn.ExitAddr && ReturnsBefore(t - 1)))
                {
                    int E = last.Dest;
                    int e = E == Fn.End ? fl.Count : Index(E);
                    int elseHi = e > t && e <= hi ? e : fw.Contains(E) ? hi : -1;
                    if (elseHi >= t)
                    {
                        var elseFw = elseHi == hi ? fw : Thread(AddrAt(elseHi), fw, AddrAt(hi));
                        node.Then = Block(i + 1, t - 1, sc, new HashSet<int>(elseFw) { AddrAt(t - 1) });
                        node.Skip = last;
                        node.Covers.Add(last);
                        node.Else = Block(t, elseHi, sc, elseFw);
                        list.Add(node);
                        return elseHi;
                    }
                }
                node.Then = Block(i + 1, t, sc, Thread(T, fw, AddrAt(hi)));
                list.Add(node);
                return t;
            }

            /// <summary>The flat at k is a jump to the exit that is a return (an assignment to the result before it).</summary>
            bool ReturnsBefore(int k)
            {
                if (k < 1) return false;
                var p = fl[k - 1];
                var fa = p.Kind == FK.Assign ? p.Target as FrameAddr : null;
                return fa != null && fa.Offset == Fn.Result;
            }

            /// <summary>A jump out of the statements around it: break, continue, return (to a void function's end) or goto.</summary>
            JumpNode JumpOut(Flat f, Expr when, Scope sc)
            {
                string what = f.Dest == sc.Continue ? "continue" : f.Dest == sc.Break ? "break"
                    : Fn.EpilogueIdx >= 0 && Fn.ExitAddr < 0 && f.Dest == fl[Fn.EpilogueIdx].Start ? "return" : "goto";
                var j = new JumpNode { F = f, Dest = f.Dest, When = when, What = what };
                j.Covers.Add(f);
                return j;
            }

            int Jump(int i, int hi, Scope sc, HashSet<int> fw, List<Node> list)
            {
                var f = fl[i];
                // a jump that cannot run: right after a jump, return or exit, with no label
                if (i > 0 && (fl[i - 1].Kind == FK.Jump || fl[i - 1].Kind == FK.Exit || fl[i - 1].Kind == FK.Return && !fl[i - 1].Epilogue))
                {
                    Report.DeadJumps++;
                    list.Add(new DeadNode(f));
                    return i + 1;
                }
                // the last statement of a region jumping to where the region goes anyway
                bool lastOne = true;
                for (int k = i + 1; k < hi; k++) if (fl[k].Kind != FK.Label) { lastOne = false; break; }
                if (lastOne && fw.Contains(f.Dest) && f.Dest > f.Addr)
                {
                    if (f.Dest != AddrAt(i + 1)) Report.Redundant++;
                    list.Add(new DeadNode(f));
                    return i + 1;
                }
                list.Add(JumpOut(f, null, sc));
                return i + 1;
            }
        }

        /// <summary>A jump nothing needs (it cannot run, or goes where control goes anyway): not shown.</summary>
        sealed class DeadNode : Node { public DeadNode(Flat f) { Covers.Add(f); } }
    }
}
