using System;
using System.Collections.Generic;
using System.Security.Cryptography;
using System.Text;

namespace UwEditor.Core.Conversations
{
    /// <summary>
    /// Where a conversation's functions and labels are. A function starts at word 0, at every
    /// CALL target, and at every PUSHBP SPTOBP after a RET, EXIT or JMP (the compiler's
    /// prologue, conv/BABL.C babl_run runs it as code like any other); it runs up to the next
    /// function. A label is the target of a JMP, BRA, BEQ or BNE.
    /// </summary>
    public sealed class BablLayout
    {
        public List<BablInstruction> Code;
        /// <summary>Instruction index by address.</summary>
        public Dictionary<int, int> At = new Dictionary<int, int>();
        public SortedSet<int> Functions = new SortedSet<int> { 0 };
        public HashSet<int> Labels = new HashSet<int>();
        /// <summary>The functions some CALL goes to.</summary>
        public HashSet<int> Called = new HashSet<int>();
        int[] starts;
        int length;

        /// <summary>The conversation, for the import names Body uses (null: numbers only).</summary>
        public Conversation Conversation;

        public static BablLayout Of(Conversation c) { var l = Of(c.Code); l.Conversation = c; return l; }

        public static BablLayout Of(ushort[] words)
        {
            var l = new BablLayout { Code = BablCode.Decode(words), length = words.Length };
            var code = l.Code;
            for (int i = 0; i < code.Count; i++) l.At[code[i].Address] = i;
            for (int i = 0; i < code.Count; i++)
            {
                var ins = code[i];
                if (!ins.Known) continue;
                if (ins.Op == BablOp.CALL && l.At.ContainsKey(ins.Target)) { l.Functions.Add(ins.Target); l.Called.Add(ins.Target); }
                else if (BablOps.IsJump(ins.Op) && l.At.ContainsKey(ins.Target)) l.Labels.Add(ins.Target);
                if (ins.Op == BablOp.PUSHBP && i + 1 < code.Count && code[i + 1].Known && code[i + 1].Op == BablOp.SPTOBP
                    && i > 0 && code[i - 1].Known && (code[i - 1].Op == BablOp.RET || code[i - 1].Op == BablOp.EXIT || code[i - 1].Op == BablOp.JMP))
                    l.Functions.Add(ins.Address);
            }
            l.starts = new int[l.Functions.Count];
            l.Functions.CopyTo(l.starts);
            return l;
        }

        /// <summary>The start of the function holding an address.</summary>
        public int FunctionOf(int address)
        {
            int i = Array.BinarySearch(starts, address);
            if (i < 0) i = ~i - 1;
            return i < 0 ? 0 : starts[i];
        }

        /// <summary>The address after the function that starts at start.</summary>
        public int EndOf(int start)
        {
            int i = Array.BinarySearch(starts, start);
            if (i < 0) i = ~i - 1;
            return i + 1 < starts.Length ? starts[i + 1] : length;
        }

        /// <summary>
        /// A fingerprint of a function's code that does not depend on where it sits: its words,
        /// with each JMP's target counted from the function's start and each CALL's from the
        /// call (BEQ, BNE and BRA are relative already), and built-ins and imported variables
        /// by name (CALLI's number and a variable's address come from the conversation's import
        /// table, conv/BABL.C DoReadHeader). The same function compiled into another
        /// conversation, or moved by an edit, has the same body.
        /// </summary>
        public string Body(int start)
        {
            int end = EndOf(start);
            var sb = new StringBuilder();
            int i0;
            if (!At.TryGetValue(start, out i0)) return "";
            var c = Conversation;
            var addresses = c == null ? null : AddressPushes(i0, end);
            for (int i = i0; i < Code.Count && Code[i].Address < end; i++)
            {
                var ins = Code[i];
                sb.Append(ins.Word.ToString("X")).Append(' ');
                if (!ins.HasOperand) continue;
                int v = ins.Operand;
                BablImport im;
                if (c != null && ins.Known && ins.Op == BablOp.CALLI && (im = c.Function(v)) != null) { sb.Append(im.Name).Append(' '); continue; }
                if (c != null && ins.Known && ins.Op == BablOp.PUSHI && addresses.Contains(i) && (im = c.VariableAt(v)) != null)
                {
                    sb.Append(im.Name).Append('+').Append(v - im.Index).Append(' ');
                    continue;
                }
                if (ins.Known && ins.Op == BablOp.JMP) v = (ins.Target - start) & 0xFFFF;
                else if (ins.Known && ins.Op == BablOp.CALL) v = (ins.Target - ins.Address) & 0xFFFF;
                sb.Append(v.ToString("X")).Append(' ');
            }
            return Hash(Encoding.ASCII.GetBytes(sb.ToString())).Substring(0, 16);
        }

        /// <summary>
        /// The PUSHIs, by instruction index, whose value a FETCHM, STO or OFFSET takes as a memory
        /// address: the stack followed straight through the function (branches not followed;
        /// it is only for the fingerprint).
        /// </summary>
        HashSet<int> AddressPushes(int i0, int end)
        {
            var marks = new HashSet<int>();
            var st = new List<int>();
            Func<int> pop = () => { if (st.Count == 0) return -1; int x = st[st.Count - 1]; st.RemoveAt(st.Count - 1); return x; };
            for (int i = i0; i < Code.Count && Code[i].Address < end; i++)
            {
                var ins = Code[i];
                if (!ins.Known) { st.Clear(); continue; }
                switch (ins.Op)
                {
                    case BablOp.PUSHI: st.Add(i); break;
                    case BablOp.PUSHI_EFF: case BablOp.PUSHBP: case BablOp.PUSH_REG: st.Add(-1); break;
                    case BablOp.FETCHM: { int a = pop(); if (a >= 0) marks.Add(a); st.Add(-1); break; }
                    case BablOp.STO: { pop(); int a = pop(); if (a >= 0) marks.Add(a); break; }
                    case BablOp.OFFSET: { int b = pop(); pop(); if (b >= 0) marks.Add(b); st.Add(-1); break; }
                    case BablOp.ADD: case BablOp.MUL: case BablOp.SUB: case BablOp.DIV: case BablOp.MOD: case BablOp.OR: case BablOp.AND:
                    case BablOp.TSTGT: case BablOp.TSTGE: case BablOp.TSTLT: case BablOp.TSTLE: case BablOp.TSTEQ: case BablOp.TSTNE: case BablOp.STRCMP:
                        pop(); pop(); st.Add(-1); break;
                    case BablOp.NOT: case BablOp.NEG: pop(); st.Add(-1); break;
                    case BablOp.SWAP: if (st.Count >= 2) { int t = st[st.Count - 1]; st[st.Count - 1] = st[st.Count - 2]; st[st.Count - 2] = t; } break;
                    case BablOp.POP: case BablOp.ADDSP: case BablOp.POPBP: case BablOp.BEQ: case BablOp.BNE: case BablOp.CALLI: case BablOp.SAY: case BablOp.RESPOND:
                        pop(); break;
                    case BablOp.BPTOSP: st.Clear(); break;
                }
            }
            return marks;
        }

        public static string Hash(byte[] data)
        {
            var h = SHA256.HashData(data);
            var sb = new StringBuilder(64);
            foreach (var b in h) sb.Append(b.ToString("x2"));
            return sb.ToString();
        }
    }
}
