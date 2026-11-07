using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;

namespace UwEditor.Core.Conversations
{
    /// <summary>One decoded instruction (or a word that is not one).</summary>
    public struct BablInstruction
    {
        public int Address;
        public int Word;
        public bool Known;
        public BablOp Op { get { return (BablOp)Word; } }
        public bool HasOperand;
        public int Operand;
        public int Length { get { return HasOperand ? 2 : 1; } }
        public int Target { get { return BablOps.Target(Op, Address, Operand); } }
    }

    public static class BablCode
    {
        /// <summary>Decodes the code from word 0 to the end; an unknown word, or an opcode with its operand missing, is a lone word.</summary>
        public static List<BablInstruction> Decode(ushort[] code)
        {
            var list = new List<BablInstruction>();
            int pc = 0;
            while (pc < code.Length)
            {
                var ins = new BablInstruction { Address = pc, Word = code[pc], Known = BablOps.IsKnown(code[pc]) };
                if (ins.Known && BablOps.HasOperand(ins.Op))
                {
                    if (pc + 1 < code.Length) { ins.HasOperand = true; ins.Operand = code[pc + 1]; }
                    else ins.Known = false;
                }
                list.Add(ins);
                pc += ins.Length;
            }
            return list;
        }
    }

    /// <summary>
    /// The text form of a conversation: the header as directives, then one instruction a line,
    /// with labels for jump and call targets. Assembler.Assemble reads it back to the same bytes.
    /// </summary>
    public static class Disassembler
    {
        /// <param name="note">Optional comment for an instruction address (a string's text, say).</param>
        /// <param name="names">The names for functions and labels (NameBook.Resolve); null for func_XXXX and LXXXX. Names never change the bytes.</param>
        public static string Disassemble(Conversation c, Func<int, string> note = null, string title = null, ConversationNames names = null)
        {
            names = names ?? ConversationNames.None;
            var sb = new StringBuilder();
            var code = BablCode.Decode(c.Code);
            var starts = new HashSet<int>();
            foreach (var i in code) starts.Add(i.Address);
            var labels = new Dictionary<int, string>();
            var functions = new HashSet<int>();
            foreach (var i in code)
            {
                if (!i.Known || !BablOps.IsJump(i.Op)) continue;
                int t = i.Target;
                if (!starts.Contains(t)) continue;
                if (i.Op == BablOp.CALL) { labels[t] = names.Function(t); functions.Add(t); }
                else if (!labels.ContainsKey(t)) labels[t] = names.Label(t);
            }

            if (title != null) sb.Append("; ").Append(title).Append('\n');
            sb.Append("; Lines are: directives (.name), labels (name:), instructions; ';' starts a comment.\n");
            sb.Append("; Jumps and calls name a label, or @address. CALLI names an imported function.\n");
            sb.AppendFormat(".slot {0}\n", c.Slot);
            if (c.First != 0x828) sb.AppendFormat(".first 0x{0:X}\n", c.First);
            if (c.HeaderWord != 0) sb.AppendFormat(".header 0x{0:X}\n", c.HeaderWord);
            sb.AppendFormat(".strings 0x{0:X4}\n", c.StringBlock);
            sb.AppendFormat(".variables {0}\n", c.Variables);
            foreach (var im in c.Imports)
                sb.AppendFormat(".import {0} {1} {2} count {3} type 0x{4:X}{5}\n", im.IsFunction ? "function" : (im.Kind == BablImport.VariableKind ? "variable" : "kind 0x" + im.Kind.ToString("X")),
                    im.Name, im.Index, im.Count, im.Type, im.Kind == BablImport.FunctionKind || im.Kind == BablImport.VariableKind ? "" : "");
            if (c.Trailer.Length > 0)
            {
                sb.Append(".trailer");
                foreach (var b in c.Trailer) sb.Append(' ').Append(b.ToString("X2"));
                sb.Append('\n');
            }
            sb.Append('\n');

            foreach (var i in code)
            {
                string label;
                if (labels.TryGetValue(i.Address, out label))
                {
                    if (functions.Contains(i.Address)) sb.Append('\n');
                    sb.Append(label).Append(":\n");
                }
                string text;
                if (!i.Known) text = ".word 0x" + i.Word.ToString("X4");
                else if (!i.HasOperand) text = i.Op.ToString();
                else text = i.Op.ToString().PadRight(10) + Operand(c, i, starts, labels);
                string comment = "; " + i.Address.ToString("X4");
                string n = note == null ? null : note(i.Address);
                if (!string.IsNullOrEmpty(n)) comment += "  " + n;
                sb.Append("    ").Append(text.PadRight(32)).Append(comment).Append('\n');
            }
            return sb.ToString();
        }

        static string Operand(Conversation c, BablInstruction i, HashSet<int> starts, Dictionary<int, string> labels)
        {
            if (BablOps.IsJump(i.Op))
            {
                string l;
                int t = i.Target;
                return labels.TryGetValue(t, out l) ? l : "@0x" + t.ToString("X4");
            }
            if (i.Op == BablOp.CALLI)
            {
                var f = c.Function(i.Operand);
                int same = 0;
                foreach (var im in c.Imports) if (im.IsFunction && im.Name == (f == null ? null : f.Name)) same++;
                return f != null && same == 1 && IsName(f.Name) ? f.Name : i.Operand.ToString();
            }
            return ((short)i.Operand).ToString(CultureInfo.InvariantCulture);
        }

        internal static bool IsName(string s)
        {
            if (string.IsNullOrEmpty(s) || char.IsDigit(s[0]) || s[0] == '-') return false;
            foreach (char ch in s) if (!(char.IsLetterOrDigit(ch) || ch == '_')) return false;
            return true;
        }
    }

    public sealed class AssemblyError : Exception
    {
        public int Line { get; private set; }
        public AssemblyError(int line, string message) : base("Line " + line + ": " + message) { Line = line; }
    }

    /// <summary>Reads the text Disassembler writes back into a conversation.</summary>
    public static class Assembler
    {
        public static Conversation Assemble(string text)
        {
            var c = new Conversation();
            bool slotSeen = false, stringsSeen = false, varsSeen = false;
            var lines = text.Replace("\r\n", "\n").Split('\n');
            // pass 1: directives, labels and addresses
            var items = new List<Tuple<int, int, string, string>>();   // line, address, op, operand
            var labels = new Dictionary<string, int>(StringComparer.Ordinal);
            int pc = 0;
            for (int ln = 0; ln < lines.Length; ln++)
            {
                int lineNo = ln + 1;
                string s = lines[ln];
                int semi = s.IndexOf(';');
                if (semi >= 0) s = s.Substring(0, semi);
                s = s.Trim();
                while (s.Length > 0)
                {
                    int colon = s.IndexOf(':');
                    if (colon <= 0) break;
                    string name = s.Substring(0, colon).Trim();
                    if (!Disassembler.IsName(name)) break;
                    if (labels.ContainsKey(name)) throw new AssemblyError(lineNo, "the label " + name + " is defined twice.");
                    labels[name] = pc;
                    s = s.Substring(colon + 1).Trim();
                }
                if (s.Length == 0) continue;
                var parts = Split(s);
                string head = parts[0];
                if (head.StartsWith("."))
                {
                    switch (head.ToLowerInvariant())
                    {
                        case ".slot": c.Slot = Number(parts, 1, lineNo); slotSeen = true; break;
                        case ".first": c.First = (uint)Number(parts, 1, lineNo); break;
                        case ".header": c.HeaderWord = Number(parts, 1, lineNo) & 0xFFFF; break;
                        case ".strings": c.StringBlock = Number(parts, 1, lineNo) & 0xFFFF; stringsSeen = true; break;
                        case ".variables": c.Variables = Number(parts, 1, lineNo) & 0xFFFF; varsSeen = true; break;
                        case ".import": c.Imports.Add(Import(parts, lineNo)); break;
                        case ".trailer":
                            {
                                var b = new List<byte>();
                                for (int k = 1; k < parts.Count; k++)
                                {
                                    int v;
                                    if (!int.TryParse(parts[k], NumberStyles.HexNumber, CultureInfo.InvariantCulture, out v) || v < 0 || v > 255)
                                        throw new AssemblyError(lineNo, "'" + parts[k] + "' is not a hex byte.");
                                    b.Add((byte)v);
                                }
                                c.Trailer = b.ToArray();
                                break;
                            }
                        case ".word":
                            if (parts.Count < 2) throw new AssemblyError(lineNo, ".word needs a value.");
                            for (int k = 1; k < parts.Count; k++) { items.Add(Tuple.Create(lineNo, pc, ".word", parts[k])); pc++; }
                            break;
                        default: throw new AssemblyError(lineNo, "unknown directive " + head + ".");
                    }
                    continue;
                }
                BablOp op;
                if (!BablOps.TryParse(head, out op)) throw new AssemblyError(lineNo, "unknown instruction '" + head + "'.");
                bool needs = BablOps.HasOperand(op);
                if (needs && parts.Count != 2) throw new AssemblyError(lineNo, op + " takes one operand.");
                if (!needs && parts.Count != 1) throw new AssemblyError(lineNo, op + " takes no operand.");
                items.Add(Tuple.Create(lineNo, pc, op.ToString(), needs ? parts[1] : null));
                pc += needs ? 2 : 1;
            }
            if (!slotSeen) throw new AssemblyError(1, "the .slot directive is missing.");
            if (!stringsSeen) throw new AssemblyError(1, "the .strings directive is missing.");
            if (!varsSeen) throw new AssemblyError(1, "the .variables directive is missing.");

            // pass 2: words
            var code = new ushort[pc];
            foreach (var it in items)
            {
                int lineNo = it.Item1, at = it.Item2;
                if (it.Item3 == ".word") { code[at] = (ushort)ParseNumber(it.Item4, lineNo); continue; }
                BablOp op;
                BablOps.TryParse(it.Item3, out op);
                code[at] = (ushort)op;
                if (it.Item4 == null) continue;
                string a = it.Item4;
                int operand;
                if (BablOps.IsJump(op))
                {
                    int target;
                    if (a.StartsWith("@")) target = ParseNumber(a.Substring(1), lineNo);
                    else if (!labels.TryGetValue(a, out target)) throw new AssemblyError(lineNo, "no label called " + a + ".");
                    operand = BablOps.OperandFor(op, at, target);
                }
                else if (op == BablOp.CALLI && !LooksNumeric(a))
                {
                    var f = c.FunctionNamed(a);
                    if (f == null) throw new AssemblyError(lineNo, a + " is not an imported function (add it with .import function).");
                    operand = f.Index;
                }
                else operand = ParseNumber(a, lineNo);
                code[at + 1] = (ushort)operand;
            }
            c.Code = code;
            if (code.Length == 0 || code[0] != (ushort)BablOp.START)
                throw new AssemblyError(1, "the code must start with START (babl_run refuses anything else).");
            return c;
        }

        static BablImport Import(List<string> p, int line)
        {
            // .import function|variable|kind K name index count C type T
            if (p.Count < 4) throw new AssemblyError(line, ".import needs: function or variable, a name and an index.");
            int i = 1;
            var im = new BablImport();
            string what = p[i++].ToLowerInvariant();
            if (what == "function") im.Kind = BablImport.FunctionKind;
            else if (what == "variable") im.Kind = BablImport.VariableKind;
            else if (what == "kind") im.Kind = Number(p, i++, line);
            else throw new AssemblyError(line, ".import takes function or variable, not " + p[1] + ".");
            if (i >= p.Count) throw new AssemblyError(line, ".import needs a name.");
            im.Name = p[i++];
            im.Index = Number(p, i++, line) & 0xFFFF;
            while (i < p.Count)
            {
                string key = p[i++].ToLowerInvariant();
                if (key == "count") im.Count = Number(p, i++, line) & 0xFFFF;
                else if (key == "type") im.Type = Number(p, i++, line) & 0xFFFF;
                else throw new AssemblyError(line, "unknown .import field " + key + ".");
            }
            return im;
        }

        static List<string> Split(string s)
        {
            var r = new List<string>();
            foreach (var x in s.Split(new[] { ' ', '\t', ',' }, StringSplitOptions.RemoveEmptyEntries)) r.Add(x);
            return r;
        }

        static int Number(List<string> p, int i, int line)
        {
            if (i >= p.Count) throw new AssemblyError(line, p[0] + " needs a value.");
            return ParseNumber(p[i], line);
        }

        static bool LooksNumeric(string s) { return s.Length > 0 && (char.IsDigit(s[0]) || s[0] == '-'); }

        public static int ParseNumber(string s, int line)
        {
            bool neg = s.StartsWith("-");
            string t = neg ? s.Substring(1) : s;
            int v;
            bool ok = t.StartsWith("0x", StringComparison.OrdinalIgnoreCase)
                ? int.TryParse(t.Substring(2), NumberStyles.HexNumber, CultureInfo.InvariantCulture, out v)
                : int.TryParse(t, NumberStyles.None, CultureInfo.InvariantCulture, out v);
            if (!ok) throw new AssemblyError(line, "'" + s + "' is not a number.");
            v = neg ? -v : v;
            if (v < -0x8000 || v > 0xFFFF) throw new AssemblyError(line, s + " does not fit in a word.");
            return v;
        }
    }
}
