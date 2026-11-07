using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text.RegularExpressions;

namespace UwEditor.Core.Conversations
{
    public enum GraphNodeKind { Say, Menu, Code }

    public enum GraphEdgeKind { Next, Jump, Branch, Choice, Call }

    /// <summary>A line a node shows: a line of the readable view (say, respond, menu choice, statement).</summary>
    public sealed class GraphLine
    {
        public string Text;
        public PseudoKind Kind;
        public int Address;
        /// <summary>1 for a statement, 2 for a menu's choice.</summary>
        public int Indent = 1;
        /// <summary>The string it shows, when it is one of the conversation's (say, respond, a menu choice), else -1.</summary>
        public int StringId = -1;
        /// <summary>On a menu's statement line ("x = babl_menu(…)"): the menu.</summary>
        public MenuUse Menu;
        public override string ToString() { return Text; }
    }

    public struct GraphPoint
    {
        public double X, Y;
        public GraphPoint(double x, double y) { X = x; Y = y; }
    }

    /// <summary>A stretch of the conversation's code: what the NPC says, a menu with its answers, or other statements.</summary>
    public sealed class GraphNode
    {
        public int Id;
        /// <summary>The code address it starts at, and the one after its last instruction.</summary>
        public int Address, End;
        /// <summary>The code it covers, as [start, end) ranges: a node that took in others covers theirs too.</summary>
        public readonly List<int[]> Ranges = new List<int[]>();
        public GraphNodeKind Kind;
        /// <summary>The function it belongs to (main, func_XXXX, or its name), and whether it is where that function starts.</summary>
        public string Function;
        public bool Entry;
        /// <summary>The name of the label at its start (LXXXX when it has none).</summary>
        public string Label;
        /// <summary>No flow leaves it: the conversation ends, or the function returns.</summary>
        public bool Ends;
        public readonly List<GraphLine> Lines = new List<GraphLine>();
        /// <summary>The conversation strings its code uses, in order.</summary>
        public readonly List<int> Strings = new List<int>();
        public readonly List<MenuUse> Menus = new List<MenuUse>();
        public double X, Y, Width, Height;
        public int Layer, Order;
        /// <summary>A layout point on a long edge, not drawn.</summary>
        public bool Dummy;
        /// <summary>The edges into and out of it (maintained by the graph).</summary>
        public readonly List<GraphEdge> In = new List<GraphEdge>();
        public readonly List<GraphEdge> Out = new List<GraphEdge>();

        public string Title
        {
            get { return Entry ? Function + "()" : Label ?? "L" + Address.ToString("X4"); }
        }

        public double CenterX { get { return X + Width / 2; } }
        public override string ToString() { return Title + " " + Kind + " (" + Lines.Count + " lines)"; }
    }

    public sealed class GraphEdge
    {
        public GraphNode From, To;
        public GraphEdgeKind Kind;
        /// <summary>The condition under which this way is taken, in the readable view's words (null for plain flow).</summary>
        public string Label;
        /// <summary>For a menu's answer: the choice's text.</summary>
        public string Choice;
        /// <summary>Goes back up (a loop): laid out as if it went down from To to From, and drawn going up into To's bottom.</summary>
        public bool Back;
        /// <summary>A call of a function called from three places or more (the conversation's end, say): left out of the layout, drawn only for a selected node.</summary>
        public bool Shared;
        /// <summary>The layout's points it passes through, from its upper end to its lower end (for a loop: from To to From).</summary>
        public readonly List<GraphNode> Bends = new List<GraphNode>();
        /// <summary>Drawn in straight lines (a loop on one node, round its side); else a curve through the points that leaves and enters each one vertically.</summary>
        public bool Straight;
        /// <summary>Where its label goes (the label's middle).</summary>
        public GraphPoint LabelAt;
        public readonly List<GraphPoint> Points = new List<GraphPoint>();
        public override string ToString() { return From.Title + " → " + To.Title + " " + Kind + (Label == null ? "" : " [" + Label + "]"); }
    }

    /// <summary>
    /// A conversation's flow as a graph, derived from its code and its readable view: the code
    /// is cut into basic blocks at jump targets, function starts and after every jump; each
    /// block's lines are the readable view's lines at its addresses; blocks that only pass on
    /// are skipped and straight runs are joined. Edges are the jumps (conditional ones labelled
    /// with their condition in script form), the fall-throughs, the calls of script functions,
    /// and, where a menu's result is compared with a number, the answer it stands for.
    /// Only code reached from the start is shown. Layout lays it out in layers.
    /// </summary>
    public sealed class ConversationGraph
    {
        public readonly List<GraphNode> Nodes = new List<GraphNode>();
        public readonly List<GraphEdge> Edges = new List<GraphEdge>();
        /// <summary>The drawing's extent: from (Left, Top) to (Width, Height), margins included.</summary>
        public double Width { get; private set; }
        public double Height { get; private set; }
        public double Left { get; private set; }
        public double Top { get; private set; }
        /// <summary>Edges are drawn in straight lines through their points (the hanging layout), not curves leaving and entering each point vertically.</summary>
        public bool StraightEdges { get; internal set; }

        /// <summary>The visible nodes (not the layout's points).</summary>
        public IEnumerable<GraphNode> Visible { get { return Nodes.Where(n => !n.Dummy); } }

        /// <summary>The node holding a code address, or null.</summary>
        public GraphNode NodeAt(int address)
        {
            foreach (var n in Nodes)
                if (!n.Dummy)
                    foreach (var r in n.Ranges) if (address >= r[0] && address < r[1]) return n;
            return null;
        }

        /// <param name="d">The conversation decompiled without folding (Decompiler.Decompile(..., fold: false)).</param>
        public static ConversationGraph Build(Conversation c, DecompileResult d)
        {
            var g = new ConversationGraph();
            var code = BablCode.Decode(c.Code);
            if (code.Count == 0) return g;
            var at = new Dictionary<int, int>();
            for (int i = 0; i < code.Count; i++) at[code[i].Address] = i;

            // functions, as the readable view names them
            var funcs = new SortedDictionary<int, string>();
            foreach (var l in d.Lines)
                if (l.Kind == PseudoKind.Function)
                {
                    int p = l.Text.IndexOf('(');
                    funcs[l.Address] = p > 0 ? l.Text.Substring(0, p) : l.Text;
                }
            if (!funcs.ContainsKey(0)) funcs[0] = "main";

            // leaders: the start, function starts, jump targets, and what follows a jump or an end
            var leaders = new SortedSet<int> { 0 };
            foreach (int f in funcs.Keys) if (at.ContainsKey(f)) leaders.Add(f);
            foreach (var ins in code)
            {
                if (!ins.Known) { leaders.Add(ins.Address + 1); continue; }
                var op = ins.Op;
                if (BablOps.IsJump(op) && op != BablOp.CALL && at.ContainsKey(ins.Target)) leaders.Add(ins.Target);
                if (op == BablOp.JMP || op == BablOp.BRA || op == BablOp.BEQ || op == BablOp.BNE || op == BablOp.RET || op == BablOp.EXIT)
                    leaders.Add(ins.Address + ins.Length);
            }
            var starts = leaders.Where(a => at.ContainsKey(a)).ToList();
            var byStart = new Dictionary<int, GraphNode>();
            for (int i = 0; i < starts.Count; i++)
            {
                int end = i + 1 < starts.Count ? starts[i + 1] : c.Code.Length;
                var fn = funcs.Where(kv => kv.Key <= starts[i]).Select(kv => kv.Value).LastOrDefault() ?? "main";
                var n = new GraphNode { Address = starts[i], End = end, Function = fn, Entry = funcs.ContainsKey(starts[i]), Label = d.Names.Label(starts[i]) };
                n.Ranges.Add(new[] { starts[i], end });
                byStart[n.Address] = n;
                g.Nodes.Add(n);
            }
            Func<int, GraphNode> blockOf = a =>
            {
                int lo = 0, hi = g.Nodes.Count - 1, found = -1;
                while (lo <= hi) { int m = (lo + hi) / 2; if (g.Nodes[m].Address <= a) { found = m; lo = m + 1; } else hi = m - 1; }
                return found >= 0 && a < g.Nodes[found].End ? g.Nodes[found] : null;
            };

            // edges
            foreach (var n in g.Nodes.ToList())
            {
                BablInstruction last = default(BablInstruction);
                bool any = false;
                for (int i = at[n.Address]; i < code.Count && code[i].Address < n.End; i++)
                {
                    var ins = code[i];
                    last = ins; any = true;
                    if (ins.Known && ins.Op == BablOp.CALL && byStart.ContainsKey(ins.Target)) g.Link(n, byStart[ins.Target], GraphEdgeKind.Call, "call");
                }
                if (!any) continue;
                GraphNode next;
                byStart.TryGetValue(n.End, out next);
                if (!last.Known) continue;
                switch (last.Op)
                {
                    case BablOp.JMP:
                    case BablOp.BRA:
                        {
                            GraphNode t;
                            if (byStart.TryGetValue(last.Target, out t)) g.Link(n, t, t == next ? GraphEdgeKind.Next : GraphEdgeKind.Jump, null);
                            break;
                        }
                    case BablOp.BEQ:
                    case BablOp.BNE:
                        {
                            string yes, no;
                            d.Branches.TryGetValue(last.Address, out yes);
                            d.FallThrough.TryGetValue(last.Address, out no);
                            GraphNode t;
                            if (byStart.TryGetValue(last.Target, out t)) g.Link(n, t, GraphEdgeKind.Branch, yes ?? "?");
                            if (next != null) g.Link(n, next, GraphEdgeKind.Branch, no ?? "?");
                            break;
                        }
                    case BablOp.RET:
                    case BablOp.EXIT:
                        break;
                    default:
                        if (next != null) g.Link(n, next, GraphEdgeKind.Next, null);
                        break;
                }
            }

            // what each block shows
            var uses = new Dictionary<int, List<StringUse>>();
            foreach (var u in d.Strings)
            {
                List<StringUse> l;
                if (!uses.TryGetValue(u.UseAddress, out l)) uses[u.UseAddress] = l = new List<StringUse>();
                l.Add(u);
            }
            var menus = d.Menus.OrderBy(m => m.Address).ToList();
            MenuUse current = null;
            foreach (var l in d.Lines)
            {
                if (l.Kind == PseudoKind.Label || l.Kind == PseudoKind.Function || l.Kind == PseudoKind.Comment) continue;
                if (l.Kind == PseudoKind.Menu && l.Indent < 2 && (l.Text.Contains("babl_menu(") || l.Text.Contains("babl_fmenu(")))
                    // the statement comes at or after the call it holds: the last menu called up to here
                    current = menus.LastOrDefault(m => m.Address <= l.Address);
                if (l.Kind == PseudoKind.Statement && (l.Text.StartsWith("goto L") || l.Text.StartsWith("if ") && l.Text.Contains(" goto L"))) continue;
                if (l.Kind == PseudoKind.Statement && (l.Text == "return" || l.Text == "end" || l.Text == "exit")) continue;
                var n = blockOf(l.Address);
                if (n == null) continue;
                var gl = new GraphLine { Text = l.Text, Kind = l.Kind, Address = l.Address, Indent = Math.Max(1, l.Indent) };
                List<StringUse> lu;
                if ((l.Kind == PseudoKind.Say || l.Kind == PseudoKind.Respond) && uses.TryGetValue(l.Address, out lu))
                {
                    var su = lu.FirstOrDefault(x => x.Kind == StringUseKind.Say || x.Kind == StringUseKind.Respond);
                    if (su != null) gl.StringId = su.Id;
                }
                if (l.Kind == PseudoKind.Menu && l.Indent < 2) gl.Menu = current;
                if (l.Kind == PseudoKind.Menu && l.Indent >= 2 && current != null)
                {
                    int colon = l.Text.IndexOf(':');
                    int k;
                    if (colon > 0 && int.TryParse(l.Text.Substring(0, colon), NumberStyles.Integer, CultureInfo.InvariantCulture, out k) && k >= 1 && k <= current.Ids.Count)
                        gl.StringId = current.Ids[k - 1];
                }
                n.Lines.Add(gl);
            }
            foreach (var n in g.Nodes)
            {
                foreach (var kv in uses)
                    if (kv.Key >= n.Address && kv.Key < n.End)
                        foreach (var u in kv.Value) if (!n.Strings.Contains(u.Id)) n.Strings.Add(u.Id);
                foreach (var m in d.Menus) if (m.Address >= n.Address && m.Address < n.End) n.Menus.Add(m);
            }

            g.KeepReached();
            g.Simplify();
            g.LabelAnswers(d);
            foreach (var n in g.Nodes)
            {
                var calls = n.In.Where(e => e.Kind == GraphEdgeKind.Call).ToList();
                if (calls.Count >= 3) foreach (var e in calls) e.Shared = true;
            }
            int id = 0;
            foreach (var n in g.Nodes)
            {
                n.Id = id++;
                n.Kind = n.Menus.Count > 0 || n.Lines.Any(x => x.Kind == PseudoKind.Menu && x.Indent >= 2) ? GraphNodeKind.Menu
                    : n.Lines.Any(x => x.Kind == PseudoKind.Say || x.Kind == PseudoKind.Respond || x.Text.StartsWith("print(")) ? GraphNodeKind.Say : GraphNodeKind.Code;
                n.Ends = !n.Out.Any(e => e.Kind != GraphEdgeKind.Call);
            }
            return g;
        }

        GraphEdge Link(GraphNode a, GraphNode b, GraphEdgeKind kind, string label)
        {
            var e = new GraphEdge { From = a, To = b, Kind = kind, Label = label };
            Edges.Add(e);
            a.Out.Add(e);
            b.In.Add(e);
            return e;
        }

        void Unlink(GraphEdge e)
        {
            Edges.Remove(e);
            e.From.Out.Remove(e);
            e.To.In.Remove(e);
        }

        void KeepReached()
        {
            if (Nodes.Count == 0) return;
            var seen = new HashSet<GraphNode>();
            var todo = new Stack<GraphNode>();
            todo.Push(Nodes[0]);
            while (todo.Count > 0)
            {
                var n = todo.Pop();
                if (!seen.Add(n)) continue;
                foreach (var e in n.Out) todo.Push(e.To);
            }
            foreach (var n in Nodes.Where(x => !seen.Contains(x)).ToList()) Remove(n);
        }

        void Remove(GraphNode n)
        {
            foreach (var e in n.In.Concat(n.Out).ToList()) Unlink(e);
            Nodes.Remove(n);
        }

        static bool Plain(GraphEdge e) { return e.Kind == GraphEdgeKind.Next || e.Kind == GraphEdgeKind.Jump; }

        static bool Empty(GraphNode n) { return n.Lines.Count == 0 && n.Strings.Count == 0 && n.Menus.Count == 0; }

        /// <summary>
        /// Makes the flow readable: blocks that show nothing and only pass on are skipped; straight
        /// runs are joined; empty blocks where a function just returns go (their predecessors
        /// then end); a block that only tests a condition is folded into the branch that leads
        /// to it, the conditions joined ("x != 1" then "x == 2" is "x == 2"; "x != 1" then
        /// "x != 2" is "x != 1, 2"); and a block that only calls a function and then returns
        /// is replaced by an edge to that function.
        /// </summary>
        void Simplify()
        {
            bool changed = true;
            while (changed)
            {
                changed = false;
                foreach (var n in Nodes.ToList())
                {
                    if (!Nodes.Contains(n) || n.Entry || n == Nodes[0]) continue;
                    var flow = n.Out.Where(e => e.Kind != GraphEdgeKind.Call).ToList();
                    // a pass-through
                    if (Empty(n) && n.Out.Count == 1 && flow.Count == 1 && Plain(flow[0]) && flow[0].To != n)
                    {
                        Redirect(n, flow[0].To, null);
                        changed = true;
                        continue;
                    }
                    // an empty end (a function's return)
                    if (Empty(n) && n.Out.Count == 0)
                    {
                        Remove(n);
                        changed = true;
                        continue;
                    }
                    // a test only: fold into the branch that leads here
                    if (Empty(n) && n.Out.Count == 2 && flow.Count == 2 && flow.All(e => e.Kind == GraphEdgeKind.Branch) && n.In.Count == 1
                        && n.In[0].From != n && n.In[0].Kind == GraphEdgeKind.Branch && flow.All(e => e.To != n))
                    {
                        var into = n.In[0];
                        var p = into.From;
                        Unlink(into);
                        foreach (var e in flow) { Unlink(e); Link(p, e.To, GraphEdgeKind.Branch, Join(into.Label, e.Label)); }
                        Nodes.Remove(n);
                        changed = true;
                        continue;
                    }
                    // a call, then nothing: straight to the function
                    var calls = n.Out.Where(e => e.Kind == GraphEdgeKind.Call).ToList();
                    if (calls.Count == 1 && n.Strings.Count == 0 && n.Menus.Count == 0 && n.Lines.Count == 1 && n.Lines[0].Text.EndsWith("()") && n.Lines[0].Text.StartsWith(calls[0].To.Function)
                        && (flow.Count == 0 || flow.Count == 1 && Plain(flow[0]) && Empty(flow[0].To) && !flow[0].To.Out.Any(e => e.Kind != GraphEdgeKind.Call) && !flow[0].To.Entry)
                        && n.In.All(e => e.From != n))
                    {
                        var to = calls[0].To;
                        foreach (var e in flow) Unlink(e);
                        Redirect(n, to, "calls");
                        changed = true;
                        continue;
                    }
                }
                foreach (var a in Nodes.ToList())
                {
                    if (!Nodes.Contains(a)) continue;
                    var outs = a.Out.Where(e => e.Kind != GraphEdgeKind.Call).ToList();
                    if (outs.Count != 1 || !Plain(outs[0])) continue;
                    var b = outs[0].To;
                    if (b == a || b.Entry || b.In.Count != 1 || b == Nodes[0]) continue;
                    // b joins a
                    Unlink(outs[0]);
                    a.End = Math.Max(a.End, b.End);
                    a.Ranges.AddRange(b.Ranges);
                    a.Lines.AddRange(b.Lines);
                    foreach (int s in b.Strings) if (!a.Strings.Contains(s)) a.Strings.Add(s);
                    a.Menus.AddRange(b.Menus);
                    foreach (var e in b.Out.ToList()) { Unlink(e); Link(a, e.To == b ? a : e.To, e.Kind, e.Label); }
                    Nodes.Remove(b);
                    changed = true;
                }
            }
            // a joined block covers addresses of the blocks it took in: keep the address order for NodeAt
            Nodes.Sort((x, y) => x.Address.CompareTo(y.Address));
            // the same edge twice (two ways into one block with the same condition) is drawn once
            foreach (var n in Nodes)
                foreach (var grp in n.Out.GroupBy(e => e.To.Address + "|" + e.Kind + "|" + e.Label).Where(x => x.Count() > 1).ToList())
                    foreach (var e in grp.Skip(1).ToList()) Unlink(e);
        }

        /// <summary>Sends n's incoming edges to another node instead and drops n. A plain edge takes the note given (null keeps it plain).</summary>
        void Redirect(GraphNode n, GraphNode to, string note)
        {
            foreach (var e in n.In.ToList())
            {
                Unlink(e);
                if (e.From == n) continue;
                var ne = Link(e.From, to, e.Kind, e.Label);
                if (note != null && Plain(ne)) ne.Kind = GraphEdgeKind.Call;
            }
            foreach (var e in n.Out.ToList()) Unlink(e);
            Nodes.Remove(n);
        }

        static readonly Regex Compare = new Regex(@"^(.+?) (==|!=) (-?\d+(?:, -?\d+)*)$");

        /// <summary>The condition of going through a test and then one of its ways.</summary>
        static string Join(string first, string then)
        {
            if (string.IsNullOrEmpty(first)) return then;
            if (string.IsNullOrEmpty(then)) return first;
            var a = Compare.Match(first);
            var b = Compare.Match(then);
            if (a.Success && b.Success && a.Groups[1].Value == b.Groups[1].Value && a.Groups[2].Value == "!=")
            {
                // x != 1 and x == 2 is x == 2; x != 1 and x != 2 is x != 1, 2
                if (b.Groups[2].Value == "==" && !b.Groups[3].Value.Contains(",")) return then;
                if (b.Groups[2].Value == "!=") return a.Groups[1].Value + " != " + a.Groups[3].Value + ", " + b.Groups[3].Value;
            }
            return Paren(first) + " and " + Paren(then);
        }

        static string Paren(string s) { return s.Contains(" or ") ? "(" + s + ")" : s; }

        /// <summary>A branch on "x == n" after "x = babl_menu(…)" in the same function is that menu's answer n (babl_fmenu: the answer with string n).</summary>
        void LabelAnswers(DecompileResult d)
        {
            var results = new List<Tuple<GraphNode, string, MenuUse>>();   // node, variable, menu
            foreach (var n in Nodes)
                foreach (var l in n.Lines)
                {
                    if (l.Menu == null) continue;
                    int eq = l.Text.IndexOf(" = babl_", StringComparison.Ordinal);
                    results.Add(Tuple.Create(n, eq > 0 ? l.Text.Substring(0, eq).Trim() : null, l.Menu));
                }
            foreach (var e in Edges)
            {
                if (e.Kind != GraphEdgeKind.Branch || e.Label == null) continue;
                // "x == n", or "x == n and something more" (an answer that then tests more)
                string head = e.Label;
                int and = head.IndexOf(" and ", StringComparison.Ordinal);
                if (and > 0 && !head.StartsWith("(")) head = head.Substring(0, and);
                var mt = Compare.Match(head);
                if (!mt.Success || mt.Groups[2].Value != "==" || mt.Groups[3].Value.Contains(",")) continue;
                string v = mt.Groups[1].Value;
                int k = int.Parse(mt.Groups[3].Value, CultureInfo.InvariantCulture);
                MenuUse menu = null;
                // the menu stored into this variable in this node, else the last one before it in the same function
                var own = results.LastOrDefault(r => r.Item1 == e.From && (r.Item2 == v || r.Item2 == null && v.StartsWith("babl_")));
                if (own != null) menu = own.Item3;
                else
                {
                    var before = results.Where(r => r.Item1.Function == e.From.Function && r.Item2 == v && r.Item3.Address < e.From.End).OrderBy(r => r.Item3.Address).LastOrDefault();
                    if (before != null) menu = before.Item3;
                }
                if (menu == null) continue;
                int index = menu.Filtered ? menu.Ids.IndexOf(k) : k - 1;
                if (index < 0 || index >= menu.Choices.Count) continue;
                e.Kind = GraphEdgeKind.Choice;
                e.Choice = (index + 1) + ": " + menu.Choices[index];
            }
        }

        // ----- layout -----

        /// <summary>The bottom of each layer, while the nodes are in layers (null after the hanging layout moved them).</summary>
        double[] bands;

        /// <summary>
        /// Lays the graph out in layers, top to bottom (each node's Width and Height must be set):
        /// loops are found by a depth-first walk from the start; layers are the longest path from
        /// the start; every edge, a loop turned round, gets a point in each layer it passes
        /// through, as tall as the layer, so it goes past the layer's nodes and not over them;
        /// the order in each layer follows the neighbours' average in sweeps up and down, with
        /// neighbours swapped where that crosses fewer edges, and the order with the fewest
        /// crossings seen is kept; each node then moves under its parents as far as its layer
        /// leaves room. Loops are drawn going up into the bottom of the node they go back to.
        /// </summary>
        public void Layout(double hgap = 40, double vgap = 70) { Layout(hgap, vgap, null); }

        public void Layout(double hgap, double vgap, LayeredOptions o)
        {
            o = o ?? new LayeredOptions();
            Nodes.RemoveAll(n => n.Dummy);
            foreach (var e in Edges) { e.Points.Clear(); e.Back = false; e.Bends.Clear(); e.Straight = false; }
            var real = Nodes.ToList();
            bands = null;
            StraightEdges = false;
            if (real.Count == 0) { Width = Height = Left = Top = 0; return; }

            // loops: a depth-first walk from the start, then from what is left
            var state = new Dictionary<GraphNode, int>();
            var discovery = new Dictionary<GraphNode, int>();
            foreach (var root in new[] { real[0] }.Concat(real.Where(n => n.Entry)).Concat(real))
            {
                if (state.ContainsKey(root)) continue;
                var stack = new Stack<Tuple<GraphNode, int>>();
                stack.Push(Tuple.Create(root, 0));
                state[root] = 1; discovery[root] = discovery.Count;
                while (stack.Count > 0)
                {
                    var top = stack.Pop();
                    var n = top.Item1;
                    var outs = n.Out.Where(e => !e.Shared).OrderBy(e => e.Kind == GraphEdgeKind.Call ? 1 : 0).ToList();
                    if (top.Item2 < outs.Count)
                    {
                        stack.Push(Tuple.Create(n, top.Item2 + 1));
                        var e = outs[top.Item2];
                        int s;
                        state.TryGetValue(e.To, out s);
                        if (s == 1) e.Back = true;
                        else if (s == 0) { state[e.To] = 1; discovery[e.To] = discovery.Count; stack.Push(Tuple.Create(e.To, 0)); }
                    }
                    else state[n] = 2;
                }
            }

            // layers: longest path over the forward edges
            var forward = Edges.Where(e => !e.Back && !e.Shared && e.From != e.To).ToList();
            var indeg = real.ToDictionary(n => n, n => 0);
            foreach (var e in forward) indeg[e.To]++;
            var queue = new Queue<GraphNode>(real.Where(n => indeg[n] == 0).OrderBy(n => discovery[n]));
            foreach (var n in real) n.Layer = 0;
            while (queue.Count > 0)
            {
                var n = queue.Dequeue();
                foreach (var e in n.Out)
                {
                    if (e.Back || e.Shared || e.From == e.To) continue;
                    e.To.Layer = Math.Max(e.To.Layer, n.Layer + 1);
                    if (--indeg[e.To] == 0) queue.Enqueue(e.To);
                }
            }

            // points for edges crossing layers (a loop from its lower end's side: it goes up)
            var up = new Dictionary<GraphNode, List<GraphNode>>();
            var down = new Dictionary<GraphNode, List<GraphNode>>();
            foreach (var n in real) { up[n] = new List<GraphNode>(); down[n] = new List<GraphNode>(); }
            Action<GraphNode, GraphNode> join = (a, b) => { down[a].Add(b); up[b].Add(a); };
            var laid = o.TurnLoops ? Edges.Where(e => !e.Shared && e.From != e.To).ToList() : forward;
            foreach (var e in laid)
            {
                var upper = e.Back ? e.To : e.From;
                var lower = e.Back ? e.From : e.To;
                var prev = upper;
                for (int L = upper.Layer + 1; L < lower.Layer; L++)
                {
                    var dmy = new GraphNode { Dummy = true, Layer = L, Width = 8, Height = 0, Address = -1, End = -1 };
                    Nodes.Add(dmy);
                    up[dmy] = new List<GraphNode>(); down[dmy] = new List<GraphNode>();
                    discovery[dmy] = discovery[upper];
                    e.Bends.Add(dmy);
                    join(prev, dmy);
                    prev = dmy;
                }
                join(prev, lower);
            }

            int layers = Nodes.Max(n => n.Layer) + 1;
            var rows = new List<List<GraphNode>>();
            for (int L = 0; L < layers; L++) rows.Add(Nodes.Where(n => n.Layer == L).OrderBy(n => discovery[n]).ThenBy(n => n.Address).ToList());
            Order(rows, up, down, o);

            // heights of the layers
            double y = 20;
            var layerY = new double[layers];
            var layerH = new double[layers];
            for (int L = 0; L < layers; L++)
            {
                layerY[L] = y;
                layerH[L] = rows[L].Where(n => !n.Dummy).Select(n => n.Height).DefaultIfEmpty(0).Max();
                if (layerH[L] == 0) layerH[L] = 10;
                y += layerH[L] + vgap;
            }
            foreach (var n in Nodes)
            {
                if (!n.Dummy) n.Y = layerY[n.Layer];
                else if (o.TallBends) { n.Y = layerY[n.Layer]; n.Height = layerH[n.Layer]; }
                else { n.Y = layerY[n.Layer] + layerH[n.Layer] / 2; n.Height = 0; }
            }

            // x: packed, then pulled under the parents (and over the children)
            foreach (var r in rows) { double x = 20; foreach (var n in r) { n.X = x; x += n.Width + (n.Dummy ? hgap / 2 : hgap); } }
            for (int pass = 0; pass < 8; pass++)
            {
                bool downward = pass % 2 == 0;
                for (int k = 0; k < layers; k++)
                {
                    int L = downward ? k : layers - 1 - k;
                    var r = rows[L];
                    var map = downward ? up : down;
                    var want = r.Select(n => map[n].Count > 0 ? map[n].Average(x => x.CenterX) - n.Width / 2 : n.X).ToArray();
                    Place(r, want, hgap);
                }
            }
            double minX = Nodes.Min(n => n.X);
            foreach (var n in Nodes) n.X += 20 - minX;

            if (o.TallBends) bands = layerY.Select((t, L) => t + layerH[L]).ToArray();
            if (o.TurnLoops) Route();
            else RouteRoundTheSide();
        }

        /// <summary>The order in each layer: neighbours' averages in sweeps down and up, then (with Transpose) neighbours swapped while that crosses fewer edges; the order with the fewest crossings is kept (with KeepBest).</summary>
        static void Order(List<List<GraphNode>> rows, Dictionary<GraphNode, List<GraphNode>> up, Dictionary<GraphNode, List<GraphNode>> down, LayeredOptions o)
        {
            int layers = rows.Count;
            Action renumber = () => { foreach (var r in rows) for (int i = 0; i < r.Count; i++) r[i].Order = i; };
            renumber();
            Func<GraphNode, Dictionary<GraphNode, List<GraphNode>>, double> bary = (n, map) =>
            {
                var l = map[n];
                return l.Count == 0 ? n.Order : l.Average(x => (double)x.Order);
            };
            // crossings between layer L and the next
            Func<int, int> between = L =>
            {
                var pairs = new List<int[]>();
                foreach (var u in rows[L]) foreach (var v in down[u]) if (v.Layer == L + 1) pairs.Add(new[] { u.Order, v.Order });
                int c = 0;
                for (int i = 0; i < pairs.Count; i++)
                    for (int j = i + 1; j < pairs.Count; j++)
                    {
                        int a = pairs[i][0] - pairs[j][0], b = pairs[i][1] - pairs[j][1];
                        if (a < 0 && b > 0 || a > 0 && b < 0) c++;
                    }
                return c;
            };
            Func<int> total = () => { int c = 0; for (int L = 0; L + 1 < layers; L++) c += between(L); return c; };
            // crossings of u's and v's edges with the layers above and below, u left of v
            Func<GraphNode, GraphNode, int> pair = (u, v) =>
            {
                int c = 0;
                foreach (var map in new[] { up, down })
                    foreach (var p in map[u])
                        foreach (var q in map[v])
                            if (p.Layer == q.Layer && p.Order > q.Order) c++;
                return c;
            };
            Action transpose = () =>
            {
                for (int round = 0; round < 8; round++)
                {
                    bool better = false;
                    foreach (var r in rows)
                        for (int i = 0; i + 1 < r.Count; i++)
                        {
                            var u = r[i]; var v = r[i + 1];
                            if (pair(v, u) < pair(u, v))
                            {
                                r[i] = v; r[i + 1] = u; v.Order = i; u.Order = i + 1;
                                better = true;
                            }
                        }
                    if (!better) break;
                }
            };

            var best = rows.Select(r => r.ToList()).ToList();
            int bestCount = o.KeepBest ? total() : int.MaxValue, stale = 0;
            for (int sweep = 0; sweep < o.Sweeps && stale < 8 && bestCount > 0; sweep++)
            {
                if (sweep % 2 == 0) for (int L = 1; L < layers; L++) { rows[L] = rows[L].OrderBy(n => bary(n, up)).ThenBy(n => n.Order).ToList(); for (int i = 0; i < rows[L].Count; i++) rows[L][i].Order = i; }
                else for (int L = layers - 2; L >= 0; L--) { rows[L] = rows[L].OrderBy(n => bary(n, down)).ThenBy(n => n.Order).ToList(); for (int i = 0; i < rows[L].Count; i++) rows[L][i].Order = i; }
                if (o.Transpose) transpose();
                if (!o.KeepBest) continue;
                int c = total();
                if (c < bestCount) { bestCount = c; best = rows.Select(r => r.ToList()).ToList(); stale = 0; }
                else stale++;
            }
            if (o.KeepBest) for (int L = 0; L < layers; L++) rows[L] = best[L];
            renumber();
        }

        /// <summary>Puts a layer's nodes as near the wanted x as their order and gaps allow.</summary>
        static void Place(List<GraphNode> r, double[] want, double gap)
        {
            int n = r.Count;
            if (n == 0) return;
            var x = new double[n];
            for (int i = 0; i < n; i++)
            {
                double g = i > 0 ? (r[i - 1].Dummy || r[i].Dummy ? gap / 2 : gap) : 0;
                x[i] = i == 0 ? want[0] : Math.Max(want[i], x[i - 1] + r[i - 1].Width + g);
            }
            // pull back left where the push went further than wanted
            for (int i = n - 2; i >= 0; i--)
            {
                double g = r[i].Dummy || r[i + 1].Dummy ? gap / 2 : gap;
                x[i] = Math.Min(Math.Max(x[i], want[i]), x[i + 1] - r[i].Width - g);
            }
            for (int i = 0; i < n; i++) r[i].X = x[i];
        }

        /// <summary>The nodes no longer stand in layers (the hanging layout moves them freely).</summary>
        internal void LeaveLayers() { bands = null; }

        /// <summary>
        /// The edges' points from where the nodes and the layout's points are now, and the
        /// drawing's extent. An edge leaves the bottom of its upper end and enters the top of its
        /// lower end (a loop the other way round: out of the top of the node it leaves, up into the
        /// bottom of the one it goes back to), at places along the side in the order of where the
        /// edges go next, so edges of one node do not cross each other there; in layers, an edge
        /// passes each layer straight down beside its nodes.
        /// </summary>
        public void Route()
        {
            var bottom = new Dictionary<GraphNode, List<Tuple<GraphEdge, double>>>();
            var top = new Dictionary<GraphNode, List<Tuple<GraphEdge, double>>>();
            Action<Dictionary<GraphNode, List<Tuple<GraphEdge, double>>>, GraphNode, GraphEdge, GraphNode> port = (map, n, e, next) =>
            {
                List<Tuple<GraphEdge, double>> l;
                if (!map.TryGetValue(n, out l)) map[n] = l = new List<Tuple<GraphEdge, double>>();
                l.Add(Tuple.Create(e, next.CenterX));
            };
            foreach (var e in Edges)
            {
                e.Points.Clear();
                if (e.Shared || e.From == e.To) continue;
                var upper = e.Back ? e.To : e.From;
                var lower = e.Back ? e.From : e.To;
                port(bottom, upper, e, e.Bends.Count > 0 ? e.Bends[0] : lower);
                port(top, lower, e, e.Bends.Count > 0 ? e.Bends[e.Bends.Count - 1] : upper);
            }
            var number = new Dictionary<GraphEdge, int>();
            for (int i = 0; i < Edges.Count; i++) number[Edges[i]] = i;
            var at = new Dictionary<GraphEdge, double>();   // x where it leaves its upper end
            var into = new Dictionary<GraphEdge, double>(); // x where it enters its lower end
            var rank = new Dictionary<GraphEdge, int>();     // its place among the edges leaving the same side of the node it leaves
            foreach (var kv in bottom)
            {
                var l = kv.Value.OrderBy(t => t.Item2).ThenBy(t => number[t.Item1]).ToList();
                for (int i = 0; i < l.Count; i++) { at[l[i].Item1] = kv.Key.X + kv.Key.Width * (i + 1) / (l.Count + 1); if (!l[i].Item1.Back) rank[l[i].Item1] = i; }
            }
            foreach (var kv in top)
            {
                var l = kv.Value.OrderBy(t => t.Item2).ThenBy(t => number[t.Item1]).ToList();
                for (int i = 0; i < l.Count; i++) { into[l[i].Item1] = kv.Key.X + kv.Key.Width * (i + 1) / (l.Count + 1); if (l[i].Item1.Back) rank[l[i].Item1] = i; }
            }
            foreach (var e in Edges)
            {
                if (e.Shared)
                {
                    e.Straight = false;
                    e.Points.Add(new GraphPoint(e.From.CenterX, e.From.Y + e.From.Height));
                    e.Points.Add(new GraphPoint(e.To.CenterX, e.To.Y));
                    e.LabelAt = Mid(e.Points[0], e.Points[1]);
                    continue;
                }
                if (e.From == e.To)
                {
                    // round its right side
                    var a = e.From;
                    double right = a.X + a.Width + 18, ay = a.Y + Math.Max(12, a.Height - 10), by = a.Y + Math.Min(12, a.Height / 2);
                    e.Straight = true;
                    e.Points.Add(new GraphPoint(a.X + a.Width, ay));
                    e.Points.Add(new GraphPoint(right, ay));
                    e.Points.Add(new GraphPoint(right, by));
                    e.Points.Add(new GraphPoint(a.X + a.Width, by));
                    e.LabelAt = new GraphPoint(right + 4, (ay + by) / 2);
                    continue;
                }
                e.Straight = StraightEdges;
                var upper = e.Back ? e.To : e.From;
                var lower = e.Back ? e.From : e.To;
                var p = new List<GraphPoint>();
                double sx = at[e];
                p.Add(new GraphPoint(sx, upper.Y + upper.Height));
                // straight down to the bottom of its layer, past the layer's taller nodes
                if (bands != null && upper.Layer < bands.Length && bands[upper.Layer] > upper.Y + upper.Height + 0.5) p.Add(new GraphPoint(sx, bands[upper.Layer]));
                int gap = p.Count - 1;
                foreach (var d in e.Bends)
                {
                    p.Add(new GraphPoint(d.CenterX, d.Y));
                    if (d.Height > 0.5) p.Add(new GraphPoint(d.CenterX, d.Y + d.Height));
                }
                p.Add(new GraphPoint(into[e], lower.Y));
                // the label on the first stretch away from the node it leaves, near that node, where
                // edges leaving together have parted; every other one a little further on
                double along = rank[e] % 2 == 0 ? 0.3 : 0.62;
                if (e.Back)
                {
                    p.Reverse();
                    e.LabelAt = StraightEdges ? Along(p[0], p[1], along) : Curve(p[0], p[1], along);
                }
                else e.LabelAt = StraightEdges ? Along(p[gap], p[gap + 1], along) : Curve(p[gap], p[gap + 1], along);
                e.Points.AddRange(p);
            }
            Bounds();
        }

        static GraphPoint Mid(GraphPoint a, GraphPoint b) { return Along(a, b, 0.5); }

        static GraphPoint Along(GraphPoint a, GraphPoint b, double t) { return new GraphPoint(a.X + (b.X - a.X) * t, a.Y + (b.Y - a.Y) * t); }

        /// <summary>The point at t of the curve drawn from a to b (leaving and entering vertically).</summary>
        static GraphPoint Curve(GraphPoint a, GraphPoint b, double t)
        {
            double u = 1 - t, my = (a.Y + b.Y) / 2;
            double k0 = u * u * u, k1 = 3 * u * u * t, k2 = 3 * u * t * t, k3 = t * t * t;
            return new GraphPoint((k0 + k1) * a.X + (k2 + k3) * b.X, k0 * a.Y + (k1 + k2) * my + k3 * b.Y);
        }

        /// <summary>The drawing's extent, with a margin of 20.</summary>
        void Bounds()
        {
            double x0 = double.MaxValue, y0 = double.MaxValue, x1 = double.MinValue, y1 = double.MinValue;
            foreach (var n in Nodes)
            {
                x0 = Math.Min(x0, n.X); y0 = Math.Min(y0, n.Y);
                x1 = Math.Max(x1, n.X + n.Width); y1 = Math.Max(y1, n.Y + n.Height);
            }
            foreach (var e in Edges)
                foreach (var p in e.Points) { x0 = Math.Min(x0, p.X); y0 = Math.Min(y0, p.Y); x1 = Math.Max(x1, p.X); y1 = Math.Max(y1, p.Y); }
            if (x0 > x1) { Left = Top = Width = Height = 0; return; }
            Left = Math.Min(0, x0 - 20); Top = Math.Min(0, y0 - 20);
            Width = x1 + 20; Height = y1 + 20;
        }

        /// <summary>The routing before 2026-11-05, for comparison: loops round the right of everything in the layers they span, at their ends' heights.</summary>
        void RouteRoundTheSide()
        {
            foreach (var e in Edges)
            {
                e.Points.Clear();
                if (e.Shared && !e.Back)
                {
                    e.Points.Add(new GraphPoint(e.From.CenterX, e.From.Y + e.From.Height));
                    e.Points.Add(new GraphPoint(e.To.CenterX, e.To.Y));
                    e.LabelAt = Mid(e.Points[0], e.Points[1]);
                    continue;
                }
                if (e.Back || e.From == e.To)
                {
                    var a = e.From; var b = e.To;
                    int lo = Math.Min(a.Layer, b.Layer), hi = Math.Max(a.Layer, b.Layer);
                    double right = Nodes.Where(n => n.Layer >= lo && n.Layer <= hi).Max(n => n.X + n.Width) + 24 + 6 * (Math.Abs(a.Address) % 5);
                    double ay = a.Y + Math.Min(a.Height - 6, 14), by = b.Y + Math.Min(b.Height - 6, 10);
                    e.Straight = true;
                    e.Points.Add(new GraphPoint(a.X + a.Width, ay));
                    e.Points.Add(new GraphPoint(right, ay));
                    e.Points.Add(new GraphPoint(right, by));
                    e.Points.Add(new GraphPoint(b.X + b.Width, by));
                    e.LabelAt = new GraphPoint(right + 4, (ay + by) / 2);
                    continue;
                }
                var outs = e.From.Out.Where(x => !x.Back && !x.Shared && x.From != x.To).OrderBy(x => x.To.CenterX).ToList();
                var ins = e.To.In.Where(x => !x.Back && !x.Shared && x.From != x.To).OrderBy(x => x.From.CenterX).ToList();
                double sx = e.From.X + e.From.Width * (outs.IndexOf(e) + 1) / (outs.Count + 1);
                double tx = e.To.X + e.To.Width * (ins.IndexOf(e) + 1) / (ins.Count + 1);
                e.Points.Add(new GraphPoint(sx, e.From.Y + e.From.Height));
                foreach (var d in e.Bends)
                {
                    e.Points.Add(new GraphPoint(d.CenterX, d.Y));
                    if (d.Height > 0.5) e.Points.Add(new GraphPoint(d.CenterX, d.Y + d.Height));
                }
                e.Points.Add(new GraphPoint(tx, e.To.Y));
                e.LabelAt = e.Points.Count >= 3 ? e.Points[1] : new GraphPoint((e.Points[0].X + e.Points[1].X) / 2, e.Points[0].Y + (e.Points[1].Y - e.Points[0].Y) * 0.45);
            }
            Bounds();
        }
    }

    /// <summary>The layered layout's choices (the defaults are the best; the others are there to compare).</summary>
    public sealed class LayeredOptions
    {
        /// <summary>Sweeps of ordering the layers by the neighbours' averages.</summary>
        public int Sweeps = 24;
        /// <summary>Keep the order with the fewest crossings seen, not the last.</summary>
        public bool KeepBest = true;
        /// <summary>After each sweep, swap neighbours in a layer while that crosses fewer edges.</summary>
        public bool Transpose = true;
        /// <summary>An edge's point in a layer it passes is as tall as the layer (it goes straight down past the layer's nodes), not a point in the layer's middle.</summary>
        public bool TallBends = true;
        /// <summary>Loops take part in the layout turned round (points in the layers they pass) and are drawn going up; else they go round the right of everything.</summary>
        public bool TurnLoops = true;

        /// <summary>The layout as it was before 2026-11-05.</summary>
        public static LayeredOptions Before { get { return new LayeredOptions { Sweeps = 6, KeepBest = false, Transpose = false, TallBends = false, TurnLoops = false }; } }
    }
}
