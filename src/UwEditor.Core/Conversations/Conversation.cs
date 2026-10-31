using System;
using System.Collections.Generic;
using System.IO;
using System.Text;

namespace UwEditor.Core.Conversations
{
    /// <summary>The conversation machine's opcodes (uw1/uw2 include/conv.h enum BablOp).</summary>
    public enum BablOp
    {
        NOP = 0x00, ADD, MUL, SUB, DIV, MOD, OR, AND,
        NOT = 0x08, TSTGT, TSTGE, TSTLT, TSTLE, TSTEQ, TSTNE, JMP,
        BEQ = 0x10, BNE, BRA, CALL, CALLI, RET, PUSHI, PUSHI_EFF,
        POP = 0x18, SWAP, PUSHBP, POPBP, SPTOBP, BPTOSP, ADDSP, FETCHM,
        STO = 0x20, OFFSET, START, SAVE_REG, PUSH_REG, STRCMP, EXIT,
        SAY = 0x27, RESPOND, NEG,
    }

    public static class BablOps
    {
        public const int Count = 0x2A;

        public static bool IsKnown(int w) { return w >= 0 && w < Count; }

        /// <summary>JMP, BEQ, BNE, BRA, CALL, CALLI, PUSHI and PUSHI_EFF take one operand word (conv/BABL.C babl_run).</summary>
        public static bool HasOperand(BablOp op)
        {
            switch (op)
            {
                case BablOp.JMP: case BablOp.BEQ: case BablOp.BNE: case BablOp.BRA:
                case BablOp.CALL: case BablOp.CALLI: case BablOp.PUSHI: case BablOp.PUSHI_EFF:
                    return true;
            }
            return false;
        }

        /// <summary>BEQ, BNE and BRA jump relative to the operand word: pc = operand + pc + 1.</summary>
        public static bool IsRelative(BablOp op) { return op == BablOp.BEQ || op == BablOp.BNE || op == BablOp.BRA; }

        /// <summary>JMP and CALL take an absolute word address.</summary>
        public static bool IsAbsolute(BablOp op) { return op == BablOp.JMP || op == BablOp.CALL; }

        public static bool IsJump(BablOp op) { return IsRelative(op) || IsAbsolute(op); }

        /// <summary>The address a jump at addr with this operand goes to.</summary>
        public static int Target(BablOp op, int addr, int operand)
        {
            return IsRelative(op) ? (addr + 1 + (short)operand) & 0xFFFF : operand & 0xFFFF;
        }

        /// <summary>The operand that makes a jump at addr go to target.</summary>
        public static int OperandFor(BablOp op, int addr, int target)
        {
            return IsRelative(op) ? (target - addr - 1) & 0xFFFF : target & 0xFFFF;
        }

        static readonly Dictionary<string, BablOp> byName = Build();
        static Dictionary<string, BablOp> Build()
        {
            var d = new Dictionary<string, BablOp>(StringComparer.OrdinalIgnoreCase);
            foreach (BablOp op in Enum.GetValues(typeof(BablOp))) d[op.ToString()] = op;
            return d;
        }

        public static bool TryParse(string name, out BablOp op) { return byName.TryGetValue(name, out op); }
    }

    /// <summary>
    /// One entry of a conversation's import table: a built-in function (kind 0x111, index the
    /// function number CALLI takes) or a variable (kind 0x10F, index its address in script
    /// memory). Count is 1 for every shipped import; type 0x129 int, 0x12B string, 0 void
    /// (conv/BABL.C DoReadHeader, bab_var_clear; docs/FORMATS.md).
    /// </summary>
    public sealed class BablImport
    {
        public const int FunctionKind = 0x111, VariableKind = 0x10F;

        public string Name;
        public int Index;
        public int Count = 1;
        public int Kind;
        public int Type;

        public bool IsFunction { get { return Kind == FunctionKind; } }

        public BablImport Clone() { return (BablImport)MemberwiseClone(); }

        public override string ToString() { return (IsFunction ? "function " : "variable ") + Name + " " + Index; }
    }

    /// <summary>
    /// A conversation as CNV.ARK keeps it, one block per slot: a long (0x828), the code size in
    /// words (a long), a word (0), the string block, the number of variables (babl_nvars),
    /// the import count, the imports (a word name length, the name, then the words index,
    /// count, kind and type) and the code, 16-bit words (conv/BABL.C DoReadHeader,
    /// docs/FORMATS.md "CNV.ARK"). Script memory holds babl_nvars words of variables first —
    /// the imported ones at their addresses, the rest private and kept in BGLOBALS.DAT — then
    /// the stack. Bytes after the code, if any, are kept.
    /// </summary>
    public sealed class Conversation
    {
        public int Slot;
        public uint First = 0x828;
        public int HeaderWord;
        public int StringBlock;
        public int Variables;
        public List<BablImport> Imports = new List<BablImport>();
        public ushort[] Code = new ushort[0];
        public byte[] Trailer = new byte[0];

        public static Conversation Parse(byte[] d, int slot)
        {
            if (d == null || d.Length < 0x10) throw new InvalidDataException("Conversation " + slot + " is too short for a header (" + (d == null ? 0 : d.Length) + " bytes).");
            var c = new Conversation { Slot = slot };
            c.First = U32(d, 0);
            uint size = U32(d, 4);
            c.HeaderWord = U16(d, 8);
            c.StringBlock = U16(d, 0xA);
            c.Variables = U16(d, 0xC);
            int n = U16(d, 0xE);
            int p = 0x10;
            for (int i = 0; i < n; i++)
            {
                if (p + 2 > d.Length) throw new InvalidDataException("Conversation " + slot + ": import " + i + " runs past the block.");
                int len = U16(d, p); p += 2;
                if (p + len + 8 > d.Length) throw new InvalidDataException("Conversation " + slot + ": import " + i + " runs past the block.");
                var im = new BablImport { Name = Encoding.ASCII.GetString(d, p, len) };
                p += len;
                im.Index = U16(d, p); im.Count = U16(d, p + 2); im.Kind = U16(d, p + 4); im.Type = U16(d, p + 6);
                p += 8;
                c.Imports.Add(im);
            }
            if (p + 2L * size > d.Length) throw new InvalidDataException("Conversation " + slot + ": " + size + " words of code do not fit in the block.");
            c.Code = new ushort[size];
            for (int i = 0; i < size; i++) c.Code[i] = (ushort)U16(d, p + 2 * i);
            p += 2 * (int)size;
            c.Trailer = new byte[d.Length - p];
            Array.Copy(d, p, c.Trailer, 0, c.Trailer.Length);
            return c;
        }

        public byte[] ToBytes()
        {
            var o = new MemoryStream();
            W32(o, First);
            W32(o, (uint)Code.Length);
            W16(o, HeaderWord);
            W16(o, StringBlock);
            W16(o, Variables);
            W16(o, Imports.Count);
            foreach (var im in Imports)
            {
                var name = Encoding.ASCII.GetBytes(im.Name ?? "");
                W16(o, name.Length);
                o.Write(name, 0, name.Length);
                W16(o, im.Index); W16(o, im.Count); W16(o, im.Kind); W16(o, im.Type);
            }
            foreach (var w in Code) W16(o, w);
            o.Write(Trailer, 0, Trailer.Length);
            return o.ToArray();
        }

        public Conversation Clone()
        {
            var c = (Conversation)MemberwiseClone();
            var imports = new List<BablImport>();
            foreach (var im in Imports) imports.Add(im.Clone());
            c.Imports = imports;
            c.Code = (ushort[])Code.Clone();
            c.Trailer = (byte[])Trailer.Clone();
            return c;
        }

        /// <summary>The function import CALLI n calls, or null.</summary>
        public BablImport Function(int number)
        {
            foreach (var im in Imports) if (im.IsFunction && im.Index == number) return im;
            return null;
        }

        public BablImport FunctionNamed(string name)
        {
            foreach (var im in Imports) if (im.IsFunction && im.Name == name) return im;
            return null;
        }

        /// <summary>The imported variable at a memory address, or null.</summary>
        public BablImport VariableAt(int address)
        {
            foreach (var im in Imports)
                if (!im.IsFunction && address >= im.Index && address < im.Index + Math.Max(1, im.Count)) return im;
            return null;
        }

        public BablImport VariableNamed(string name)
        {
            foreach (var im in Imports) if (!im.IsFunction && im.Name == name) return im;
            return null;
        }

        static int U16(byte[] d, int p) { return d[p] | d[p + 1] << 8; }
        static uint U32(byte[] d, int p) { return (uint)(d[p] | d[p + 1] << 8 | d[p + 2] << 16 | d[p + 3] << 24); }
        static void W16(Stream o, int v) { o.WriteByte((byte)v); o.WriteByte((byte)(v >> 8)); }
        static void W32(Stream o, uint v) { W16(o, (int)(v & 0xFFFF)); W16(o, (int)(v >> 16)); }
    }
}
