using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;
using UltimateEditor.Games;

namespace UwEditor.Core.Conversations
{
    public enum SimState { Running, WaitingMenu, WaitingAsk, Ended, Error }

    public enum SimOutputKind { Say, Respond, Print, Choice, Answer, Note }

    /// <summary>One line of the conversation as the player would see it.</summary>
    public sealed class SimOutput
    {
        public SimOutputKind Kind;
        /// <summary>The text after @-variable substitution (convert_string).</summary>
        public string Text;
        /// <summary>The string it came from, and its text before substitution (null for a note).</summary>
        public int StringId = -1;
        public string Source;
        public int Address;
        public override string ToString() { return Kind + ": " + Text; }
    }

    public enum SimLogKind { Instruction, Call, Write, Note, Error }

    /// <summary>One entry of the debug log.</summary>
    public sealed class SimLog
    {
        public long Step;
        public int Address;
        public SimLogKind Kind;
        public string Text;
        public override string ToString() { return string.Format("{0,6} {1:X4}  {2}", Step, Address, Text); }
    }

    /// <summary>A menu waiting for the player: its choices' string ids and texts, numbered from 1.</summary>
    public sealed class SimMenu
    {
        public readonly List<int> Ids = new List<int>();
        public readonly List<string> Texts = new List<string>();
        /// <summary>babl_fmenu: the result is the chosen string's id, not its number.</summary>
        public bool Filtered;
        public int Address;
    }

    /// <summary>
    /// Runs a conversation as the game does: the stack machine of uw1/uw2 conv/BABL.C
    /// (babl_run and its opcodes; load_script and init's order: the saved private globals
    /// (bab_get_globals), the imports cleared (bab_var_clear), the talker's and player's
    /// values copied in (conv/CONVVARS.C setup_converse_data)), the string built-ins of
    /// conv/BABL.C and the game's built-ins of conv/CONVERSE.C, conv/BABLHACK.C and
    /// conv/BARTER.C, against a SimWorld. Menus and babl_ask stop the machine until Choose
    /// or Answer. A built-in's arguments are addresses in script memory: args[0] the count,
    /// args[-1] the last address pushed before it ("arg1"), args[-2] the one before.
    /// Nothing is written to the game: the world and the memory are this object's own.
    /// </summary>
    public sealed class ConversationSimulator
    {
        /// <summary>The empty string load_script makes first (block 0x7C string 0), and the player's name (block 0x7D, game/PLAYER.C make_string).</summary>
        public const int EmptyStringId = 0x7C << 9, PlayerNameId = 0x7D << 9;
        const int StackWords = 0x800;

        public Conversation Conversation { get; private set; }
        public SimWorld World { get; private set; }
        public GameKind Kind { get { return World.Kind; } }
        readonly IList<string> strings;
        readonly Func<int, int, string> otherString;
        readonly short[] mem;
        readonly int nvars;
        readonly ushort[] code;
        readonly List<string> dynamic = new List<string>();
        int askHandle;
        Random rng;

        public int Pc { get; private set; }
        public int Sp { get; private set; }
        public int Bp { get; private set; }
        public int Reg { get; private set; }
        public long Steps { get; private set; }
        public SimState State { get; private set; }
        public string Error { get; private set; }
        public SimMenu Menu { get; private set; }
        public readonly List<SimOutput> Output = new List<SimOutput>();
        public readonly List<SimLog> Log = new List<SimLog>();
        /// <summary>What the game copies back when the conversation ends (update_converse_data), filled in at the end.</summary>
        public readonly List<string> EndReport = new List<string>();
        /// <summary>Code addresses Run stops at (before running the instruction there).</summary>
        public readonly HashSet<int> Breakpoints = new HashSet<int>();
        /// <summary>Log every instruction, not only calls and writes.</summary>
        public bool LogInstructions = true;
        /// <summary>The log keeps this many entries (the oldest go).</summary>
        public int LogLimit = 20000;
        /// <summary>Raised for every new output line and log entry, and when the state changes.</summary>
        public event Action Changed;

        /// <param name="strings">The conversation's string block (with unsaved edits).</param>
        /// <param name="otherString">Other blocks' strings (block, index), for npc_name and the like; null gives none.</param>
        /// <param name="savedGlobals">The conversation's record of BGLOBALS.DAT, or null (a new game: zeros).</param>
        public ConversationSimulator(Conversation c, IList<string> strings, SimWorld world, Func<int, int, string> otherString = null, int[] savedGlobals = null)
        {
            Conversation = c;
            World = world.Clone();
            this.strings = strings ?? new List<string>();
            this.otherString = otherString;
            nvars = Math.Max(0, c.Variables);
            code = c.Code;
            mem = new short[nvars + StackWords];
            rng = new Random(World.RandomSeed);
            Start(savedGlobals);
        }

        public int Variables { get { return nvars; } }
        public int MemorySize { get { return mem.Length; } }

        void Start(int[] saved)
        {
            // conv/BABL.C load_script: bab_get_globals reads at most babl_nvars words of the record
            if (saved != null) for (int i = 0; i < Math.Min(nvars, saved.Length); i++) mem[i] = (short)saved[i];
            dynamic.Add("");   // empty_string = make_string(empty, STRBLK_DYNAMIC)
            // bab_var_clear: by type, 0x126 a word, 0x12B every element to 0, 0x128 and 0x12A the empty string
            foreach (var im in Conversation.Imports)
            {
                if (im.IsFunction) continue;
                int n = Math.Max(1, im.Count);
                switch (im.Type)
                {
                    case 0x126: SetMem(im.Index, 0, false); break;
                    case 0x12B: for (int i = 0; i < n; i++) SetMem(im.Index + i, 0, false); break;
                    case 0x128: SetMem(im.Index, EmptyStringId, false); break;
                    case 0x12A: for (int i = 0; i < n; i++) SetMem(im.Index + i, EmptyStringId, false); break;
                }
            }
            // conv/CONVVARS.C setup_converse_data: bab_var for each name the script imports
            World.Imported["play_sex"] = World.PlayerFemale ? 1 : 0;
            World.Imported["play_name"] = PlayerNameId;
            foreach (var iv in ImportedVariables.All)
            {
                var im = Conversation.VariableNamed(iv.Name);
                int v;
                if (im == null || !World.Imported.TryGetValue(iv.Name, out v)) continue;
                SetMem(im.Index, v, false);
            }
            Pc = 0; Sp = 0; Bp = 0;
            State = SimState.Running;
            if (code.Length == 0 || code[0] != (ushort)BablOp.START) Fail("The code does not start with START: babl_run refuses to run it (conv/BABL.C).");
            Note("Conversation " + Conversation.Slot + " loaded: " + nvars + " variables, " + code.Length + " words of code. Talker: " + World.TalkerDescription + ".");
        }

        // ----- memory -----

        public int GetMem(int addr)
        {
            if (addr < 0 || addr >= mem.Length) throw new SimFault(string.Format("memory address {0} is outside the script's memory (0-{1})", addr, mem.Length - 1));
            return mem[addr];
        }

        void SetMem(int addr, int value, bool log = true)
        {
            if (addr < 0 || addr >= mem.Length) throw new SimFault(string.Format("memory address {0} is outside the script's memory (0-{1})", addr, mem.Length - 1));
            int old = mem[addr];
            mem[addr] = (short)value;
            if (log && (addr < nvars || LogInstructions)) Add(SimLogKind.Write, MemoryName(addr) + " = " + (short)value + (old != (short)value ? " (was " + old + ")" : ""));
        }

        /// <summary>Changes a memory word from outside (the simulator panel). Returns an error, or null.</summary>
        public string SetMemory(int addr, int value)
        {
            if (addr < 0 || addr >= mem.Length) return "No such address.";
            if (value < -32768 || value > 65535) return "A word is -32768 to 65535.";
            mem[addr] = (short)value;
            Add(SimLogKind.Note, "set by hand: " + MemoryName(addr) + " = " + (short)value);
            return null;
        }

        /// <summary>The names the log and the values use (NameBook.Resolve); the placeholders until set.</summary>
        public ConversationNames Names = ConversationNames.None;

        /// <summary>A memory address as the readable view names it: an import, a global's name, or a frame slot of the current function.</summary>
        public string MemoryName(int addr)
        {
            if (addr < nvars)
            {
                var im = Conversation.VariableAt(addr);
                if (im != null) return im.Count > 1 ? im.Name + "[" + (addr - im.Index + 1) + "]" : im.Name;
                return Names.Global(addr);
            }
            int off = addr - nvars - Bp;
            if (off > 0 || off <= -2) return Names.Local(Names.Layout == null ? 0 : Names.Layout.FunctionOf(Pc), off);
            return "stack[" + (addr - nvars) + "]";
        }

        int S(int i) { return GetMem(nvars + i); }
        void SetS(int i, int v) { int a = nvars + i; if (a < nvars || a >= mem.Length) throw new SimFault("the stack overflowed (sp " + i + ")"); mem[a] = (short)v; }
        int Pop() { int v = S(Sp); Sp--; return v; }
        void Push(int v) { Sp++; SetS(Sp, v); }

        // ----- strings (ui/GAMESTRN.C get_string, make_string) -----

        /// <summary>A string id's text: block 0 is the conversation's, 0x7C the strings the script made, 0x7D the player's name; null when there is none.</summary>
        public string GetString(int id)
        {
            int block = (id & 0xFFFF) >> 9, n = id & 0x1FF;
            if (block == 0x7C) return n < dynamic.Count ? dynamic[n] : null;
            if (block == 0x7D) return n == 0 ? World.PlayerName : null;
            if (block == 0) return n < strings.Count ? strings[n] : null;
            return otherString == null ? null : otherString(block, n);
        }

        int MakeString(string s)
        {
            if (dynamic.Count >= 512) throw new SimFault("more than 512 strings made in one conversation (make_string)");
            dynamic.Add(s ?? "");
            return (0x7C << 9) | (dynamic.Count - 1);
        }

        /// <summary>
        /// The @-variables of a string filled in (conv/BABL.C convert_string): @ then G (a
        /// global, mem[n + i - 1]), S (a frame slot, stack[bp + n + i - 1]), P (a parameter: the
        /// address at stack[bp + n], plus i - 1) or C (the number itself); then I for a number,
        /// anything else for a string id, substituted again; an optional index variable follows.
        /// "@@" is a lone @.
        /// </summary>
        public string Convert(string text, int depth = 0)
        {
            if (text == null) return null;
            if (text.IndexOf('@') < 0 || depth > 8) return text;
            var o = new StringBuilder();
            int p = 0;
            while (p < text.Length)
            {
                if (text[p] == '@')
                {
                    p++;
                    if (p < text.Length && text[p] != '@')
                    {
                        char kind = text[p++];
                        char type = 'I';
                        if (kind != 'C') type = p < text.Length ? text[p++] : 'I';
                        int number = Atoi(text, p);
                        while (p < text.Length && (char.IsDigit(text[p]) || text[p] == '-')) p++;
                        int extra = 0;
                        if (p < text.Length && "GSPC".IndexOf(text[p]) >= 0) extra = AtIndex(text, ref p) - 1;
                        int value = Fetch(kind, number, extra);
                        if (type == 'I') o.Append(((short)value).ToString(CultureInfo.InvariantCulture));
                        else
                        {
                            string s = GetString(value);
                            if (s != null) o.Append(Convert(s, depth + 1));
                        }
                        continue;
                    }
                }
                if (p < text.Length) o.Append(text[p++]);
            }
            return o.ToString();
        }

        int Fetch(char kind, int number, int extra)
        {
            if (kind == 'G') return GetMem(number + extra);
            if (kind == 'P') return GetMem(S(Bp + number) + extra);
            if (kind == 'S') return S(Bp + number + extra);
            return number;
        }

        /// <summary>The index part of an @-variable, itself a variable (an integer one, else 0): AtIndex in conv/BABL.C.</summary>
        int AtIndex(string t, ref int p)
        {
            char kind = t[p++];
            char type = 'I';
            if (kind != 'C') type = p < t.Length ? t[p++] : 'I';
            if (type != 'I') return 0;
            int parsed = Atoi(t, p);
            while (p < t.Length && (char.IsDigit(t[p]) || t[p] == '-')) p++;
            int extra = 0;
            if (p < t.Length && "GSPC".IndexOf(t[p]) >= 0) extra = AtIndex(t, ref p) - 1;
            return Fetch(kind, parsed, extra);
        }

        static int Atoi(string s, int p)
        {
            int i = p, sign = 1, v = 0;
            while (i < s.Length && char.IsWhiteSpace(s[i])) i++;
            if (i < s.Length && (s[i] == '-' || s[i] == '+')) { if (s[i] == '-') sign = -1; i++; }
            while (i < s.Length && char.IsDigit(s[i]) && i - p < 19) { v = v * 10 + (s[i] - '0'); i++; }
            return sign * v;
        }

        string Text(int id) { return Convert(GetString(id) ?? ""); }

        // ----- running -----

        /// <summary>Runs one instruction. False when the machine is waiting, ended or failed.</summary>
        public bool Step()
        {
            if (State != SimState.Running) return false;
            try { Execute(); }
            catch (SimFault f) { Fail(f.Message); }
            Changed?.Invoke();
            return State == SimState.Running;
        }

        /// <summary>Runs until a menu, babl_ask, the end, an error, a breakpoint or maxSteps instructions. Returns the instructions run.</summary>
        public int Run(int maxSteps = 200000)
        {
            int n = 0;
            while (State == SimState.Running && n < maxSteps)
            {
                if (n > 0 && Breakpoints.Contains(Pc)) { Note("Stopped at the breakpoint " + Pc.ToString("X4") + "."); break; }
                try { Execute(); }
                catch (SimFault f) { Fail(f.Message); }
                n++;
            }
            if (State == SimState.Running && n >= maxSteps) Note("Stopped after " + n + " instructions.");
            Changed?.Invoke();
            return n;
        }

        void Execute()
        {
            if (Pc < 0 || Pc >= code.Length) throw new SimFault("the program counter " + Pc + " is outside the code (0-" + (code.Length - 1) + ")");
            int at = Pc;
            int w = code[Pc];
            Steps++;
            Func<int> operand = () =>
            {
                if (at + 1 >= code.Length) throw new SimFault("the operand of the instruction at " + at.ToString("X4") + " is past the end of the code");
                return code[at + 1];
            };
            if (!BablOps.IsKnown(w))
            {
                // babl_run's default case ends the script
                Add(SimLogKind.Note, "word 0x" + w.ToString("X4") + " is not an instruction: babl_run stops here");
                Finish();
                return;
            }
            var op = (BablOp)w;
            if (LogInstructions) Add(SimLogKind.Instruction, BablOps.HasOperand(op) ? op + " " + (short)operand() : op.ToString(), at);
            int a, b;
            switch (op)
            {
                case BablOp.NOP: case BablOp.START: Pc++; break;
                case BablOp.ADD: b = Pop(); a = S(Sp); SetS(Sp, (short)(a + b)); Pc++; break;
                case BablOp.MUL: b = Pop(); a = S(Sp); SetS(Sp, (short)(a * b)); Pc++; break;
                case BablOp.SUB: b = Pop(); a = S(Sp); SetS(Sp, (short)(a - b)); Pc++; break;
                // division and remainder by 0 give 0x7FFF (BABL_DIV, BablMod)
                case BablOp.DIV: b = Pop(); a = S(Sp); SetS(Sp, b != 0 ? (short)(a / b) : 0x7FFF); Pc++; break;
                case BablOp.MOD: b = Pop(); a = S(Sp); SetS(Sp, b != 0 ? (short)(a % b) : 0x7FFF); Pc++; break;
                case BablOp.NEG: SetS(Sp, (short)-S(Sp)); Pc++; break;
                case BablOp.OR: b = Pop(); a = S(Sp); SetS(Sp, a != 0 || b != 0 ? 1 : 0); Pc++; break;
                case BablOp.AND: b = Pop(); a = S(Sp); SetS(Sp, a != 0 && b != 0 ? 1 : 0); Pc++; break;
                case BablOp.NOT: SetS(Sp, S(Sp) == 0 ? 1 : 0); Pc++; break;
                case BablOp.TSTGT: b = Pop(); a = S(Sp); SetS(Sp, a > b ? 1 : 0); Pc++; break;
                case BablOp.TSTGE: b = Pop(); a = S(Sp); SetS(Sp, a >= b ? 1 : 0); Pc++; break;
                case BablOp.TSTLT: b = Pop(); a = S(Sp); SetS(Sp, a < b ? 1 : 0); Pc++; break;
                case BablOp.TSTLE: b = Pop(); a = S(Sp); SetS(Sp, a <= b ? 1 : 0); Pc++; break;
                case BablOp.TSTEQ: b = Pop(); a = S(Sp); SetS(Sp, a == b ? 1 : 0); Pc++; break;
                case BablOp.TSTNE: b = Pop(); a = S(Sp); SetS(Sp, a != b ? 1 : 0); Pc++; break;
                case BablOp.JMP: Pc = operand(); break;
                case BablOp.BRA: Pc = (operand() + at + 1) & 0xFFFF; break;
                case BablOp.BEQ: Pc = Pop() == 0 ? (operand() + at + 1) & 0xFFFF : at + 2; break;
                case BablOp.BNE: Pc = Pop() != 0 ? (operand() + at + 1) & 0xFFFF : at + 2; break;
                case BablOp.CALL: Push(at + 2); Pc = operand(); break;
                case BablOp.RET:
                    // bab_ret: with nothing on the stack the script ends
                    if (Sp > 0) { Pc = S(Sp) & 0xFFFF; Sp--; }
                    else Finish();
                    break;
                case BablOp.POP: Sp--; Pc++; break;
                case BablOp.SWAP: a = S(Sp); SetS(Sp, S(Sp - 1)); SetS(Sp - 1, a); Pc++; break;
                case BablOp.PUSHBP: Push(Bp); Pc++; break;
                case BablOp.POPBP: Bp = S(Sp); Sp--; Pc++; break;
                case BablOp.SPTOBP: Bp = Sp; Pc++; break;
                case BablOp.BPTOSP: Sp = Bp; Pc++; break;
                case BablOp.ADDSP: Sp += S(Sp) - 1; Pc++; if (Sp < -1 || nvars + Sp >= mem.Length) throw new SimFault("ADDSP moved the stack outside memory"); break;
                case BablOp.PUSHI: Push((short)operand()); Pc += 2; break;
                case BablOp.PUSHI_EFF: Push(nvars + (short)operand() + Bp); Pc += 2; break;
                case BablOp.FETCHM: SetS(Sp, GetMem(S(Sp))); Pc++; break;
                case BablOp.OFFSET: b = Pop(); a = S(Sp); SetS(Sp, (short)(a + b - 1)); Pc++; break;   // arrays count from 1
                case BablOp.STO: b = S(Sp); a = S(Sp - 1); SetMem(a, b); Sp -= 2; Pc++; break;
                case BablOp.SAVE_REG: Reg = S(Sp); Pc++; break;
                case BablOp.PUSH_REG: Push(Reg); Pc++; break;
                case BablOp.STRCMP:
                    {
                        // vm_strcmp: equal after substitution, case counts
                        string y = Text(S(Sp)), x = Text(S(Sp - 1));
                        Sp--;
                        SetS(Sp, string.CompareOrdinal(x, y) == 0 ? 1 : 0);
                        Pc++;
                        break;
                    }
                case BablOp.EXIT: Finish(); break;
                case BablOp.SAY:
                case BablOp.RESPOND:
                    {
                        int id = Pop();
                        bool say = op == BablOp.SAY;
                        Emit(say ? SimOutputKind.Say : SimOutputKind.Respond, id, at);
                        Pc++;
                        break;
                    }
                case BablOp.CALLI: CallBuiltin(at, operand()); break;
            }
        }

        void Emit(SimOutputKind kind, int id, int at)
        {
            string src = GetString(id);
            Output.Add(new SimOutput { Kind = kind, StringId = id, Source = src, Text = Convert(src ?? ""), Address = at });
            Add(SimLogKind.Note, (kind == SimOutputKind.Say ? "say" : kind == SimOutputKind.Respond ? "respond" : "print") + " #" + Hex(id) + (src == null ? " (no such string)" : ""), at);
        }

        static string Hex(int id) { return (id & 0xFFFF) >= 0x200 ? "0x" + (id & 0xFFFF).ToString("X4") : id.ToString(); }

        void Finish()
        {
            if (State == SimState.Ended) return;
            State = SimState.Ended;
            Menu = null;
            // ExitConversation: the dynamic strings go, the private globals are saved (bab_put_globals)
            var priv = new List<string>();
            for (int i = 0; i < nvars; i++)
            {
                var im = Conversation.VariableAt(i);
                if (im != null && ImportedVariables.IsBound(im.Name, Kind)) continue;
                if (mem[i] != 0) priv.Add(MemoryName(i) + " = " + mem[i]);
            }
            EndReport.Add("The game would save the private globals in BGLOBALS.DAT: " + (priv.Count == 0 ? "all 0." : string.Join(", ", priv) + " (the rest 0)."));
            // conv/CONVVARS.C update_converse_data: what comes back out
            foreach (var iv in ImportedVariables.All)
            {
                if (!iv.WrittenBack) continue;
                var im = Conversation.VariableNamed(iv.Name);
                if (im == null) continue;
                int v = mem[im.Index], was;
                World.Imported.TryGetValue(iv.Name, out was);
                if (v != was) EndReport.Add(iv.Name + " goes back to the game as " + v + " (was " + was + ")" + Meaning(iv.Name, v) + ".");
            }
            Add(SimLogKind.Note, "The conversation ends after " + Steps + " instructions.");
            foreach (var l in EndReport) Add(SimLogKind.Note, l);
        }

        static string Meaning(string name, int v)
        {
            if (name == "npc_attitude") return v > 3 ? ": friendly and an ally" : v == 0 ? ": hostile" : "";
            if (name == "new_player_exp" && v != 0) return ": the player gets that much experience";
            if (name == "npc_goal") return v == 5 ? ": attack" : v == 10 ? ": talk to the player" : "";
            return "";
        }

        void Fail(string message)
        {
            State = SimState.Error;
            Error = message;
            Add(SimLogKind.Error, "Error: " + message);
        }

        void Note(string text) { Add(SimLogKind.Note, text); }

        void Add(SimLogKind kind, string text, int at = -1)
        {
            Log.Add(new SimLog { Step = Steps, Address = at < 0 ? Pc : at, Kind = kind, Text = text });
            if (Log.Count > LogLimit) Log.RemoveRange(0, Log.Count - LogLimit);
        }

        // ----- menus and questions -----

        /// <summary>Picks menu choice n (from 1) and goes on (conv_play_menu: the choice is echoed as the player's line).</summary>
        public string Choose(int n)
        {
            if (State != SimState.WaitingMenu || Menu == null) return "No menu is waiting.";
            if (n < 1 || n > Menu.Ids.Count) return "Choose 1-" + Menu.Ids.Count + ".";
            var m = Menu;
            int result = Chosen(m, n);
            Return(m.Address, result);
            Changed?.Invoke();
            return null;
        }

        /// <summary>The menu's result for choice n, with the choice echoed (the part of Choose that does not move the program counter).</summary>
        int Chosen(SimMenu m, int n)
        {
            if (World.InHand != 0)
            {
                // the player can use the inventory while a menu waits (converse_event_loop): what was handed over is put away
                World.PlayerInventory.Add(World.InHand);
                World.InHand = 0;
            }
            Output.Add(new SimOutput { Kind = SimOutputKind.Choice, StringId = m.Ids[n - 1], Source = GetString(m.Ids[n - 1]), Text = m.Texts[n - 1], Address = m.Address });
            int result = m.Filtered ? m.Ids[n - 1] : n;
            Add(SimLogKind.Call, (m.Filtered ? "babl_fmenu" : "babl_menu") + " → " + result + " (choice " + n + ")", m.Address);
            Menu = null;
            return result;
        }

        // ----- for the structured view's reference run (Decompiler.RunStructured) -----
        // It runs the structured statements on this machine's memory and stack, with the same
        // built-ins, so the two can be compared on the same menu choices.

        internal int StackBase { get { return nvars; } }
        internal int Mem(int addr) { return GetMem(addr); }
        internal void Store(int addr, int value) { SetMem(addr, value); }
        internal int StackPointer { get { return Sp; } set { Sp = value; } }
        internal int BasePointer { get { return Bp; } set { Bp = value; } }
        internal int Register { get { return Reg; } set { Reg = value; } }
        internal void PushWord(int v) { Push(v); }
        internal int PopWord() { return Pop(); }
        internal int Top { get { return S(Sp); } set { SetS(Sp, value); } }
        internal void SwapTop() { int a = S(Sp); SetS(Sp, S(Sp - 1)); SetS(Sp - 1, a); }
        internal string StringText(int id) { return Text(id); }
        internal void Speak(bool say, int id, int at) { Emit(say ? SimOutputKind.Say : SimOutputKind.Respond, id, at); }
        internal void End() { Finish(); }
        internal void Abort(string why) { if (State != SimState.Error) Fail(why); }

        /// <summary>
        /// Calls built-in number with the count on top of the stack and the argument addresses
        /// under it, as CALLI does; a menu is answered by choose (the menu, how often it was
        /// met), babl_ask by answer. The result replaces the count; null when the run stopped.
        /// </summary>
        internal int? CallDirect(int at, int number, Func<SimMenu, int, int> choose, Func<string> answer, Dictionary<string, int> seen)
        {
            var f = Conversation.Function(number);
            string name = f != null ? f.Name : "builtin" + number;
            int? result = Builtin(name, at, S(Sp));
            if (result == null && State == SimState.WaitingMenu && Menu != null)
            {
                string key = Menu.Address + ":" + string.Join(",", Menu.Ids);
                int n;
                seen.TryGetValue(key, out n);
                seen[key] = n + 1;
                result = Chosen(Menu, Math.Max(1, Math.Min(Menu.Ids.Count, choose(Menu, n))));
                State = SimState.Running;
            }
            else if (result == null && State == SimState.WaitingAsk)
            {
                string text = answer == null ? "" : answer() ?? "";
                if (text.Length > 0x32) text = text.Substring(0, 0x32);
                if (askHandle == 0) askHandle = MakeString(text);
                else dynamic[askHandle & 0x1FF] = text;
                Output.Add(new SimOutput { Kind = SimOutputKind.Answer, Text = text, Address = at });
                result = askHandle;
                State = SimState.Running;
            }
            if (result == null || State != SimState.Running) return null;
            SetS(Sp, result.Value);
            Reg = (short)result.Value;
            return (short)result.Value;
        }

        /// <summary>Gives babl_ask the player's typed answer (at most 0x32 characters, wdialog) and goes on.</summary>
        public string Answer(string text)
        {
            if (State != SimState.WaitingAsk) return "No question is waiting.";
            text = text ?? "";
            if (text.Length > 0x32) text = text.Substring(0, 0x32);
            // getInputText: one dynamic string for every babl_ask of a conversation, its text replaced
            if (askHandle == 0) askHandle = MakeString(text);
            else dynamic[askHandle & 0x1FF] = text;
            Output.Add(new SimOutput { Kind = SimOutputKind.Answer, Text = text, Address = Pc });
            Add(SimLogKind.Call, "babl_ask → #" + Hex(askHandle) + " \"" + text + "\"");
            Return(Pc, askHandle);
            Changed?.Invoke();
            return null;
        }

        /// <summary>A built-in's result: it replaces the count on the stack and goes into reg (talk_calli).</summary>
        void Return(int at, int result)
        {
            State = SimState.Running;
            try { SetS(Sp, result); }
            catch (SimFault f) { Fail(f.Message); return; }
            Reg = (short)result;
            Pc = at + 2;
        }

        // ----- the built-ins -----

        int A(int k) { return S(Sp - k); }          // args[-k]: an address
        int V(int k) { return GetMem(A(k)); }       // getmem(args[-k])

        void CallBuiltin(int at, int number)
        {
            var f = Conversation.Function(number);
            string name = f != null ? f.Name : "builtin" + number;
            int count = S(Sp);
            var args = new List<string>();
            for (int k = Math.Max(0, Math.Min(count, Sp)); k >= 1; k--)
            {
                int addr = A(k);
                args.Add(addr >= 0 && addr < mem.Length ? mem[addr].ToString(CultureInfo.InvariantCulture) : "@" + addr);
            }
            string call = name + "(" + string.Join(", ", args) + ")";
            int? result = Builtin(name, at, count);
            if (result == null)
            {
                Add(SimLogKind.Call, call + " … waiting for the player", at);
                return;
            }
            Add(SimLogKind.Call, call + " → " + (short)result.Value, at);
            if (State == SimState.Running) Return(at, result.Value);
        }

        /// <summary>Runs a built-in; null when it waits for the player (a menu, babl_ask).</summary>
        int? Builtin(string name, int at, int count)
        {
            bool uw2 = World.Uw2;
            switch (name)
            {
                // ----- conv/CONVERSE.C -----
                case "babl_menu":
                case "babl_fmenu":
                    {
                        // conv_choice: a 0-terminated list of string ids at arg1; conv_fmenu: only those whose flag at arg2 is set
                        bool filtered = name == "babl_fmenu";
                        int list = A(1), flags = filtered ? A(2) : 0;
                        var m = new SimMenu { Filtered = filtered, Address = at };
                        for (int i = 0; i < 64; i++)
                        {
                            int id = GetMem(list + i);
                            if (id == 0) break;
                            if (filtered && GetMem(flags + i) == 0) continue;
                            m.Ids.Add(id);
                            m.Texts.Add(Text(id));
                        }
                        if (m.Ids.Count == 0) throw new SimFault(name + " has no choices to show: the game would wait for ever");
                        Menu = m;
                        State = SimState.WaitingMenu;
                        return null;
                    }
                case "babl_ask":
                    State = SimState.WaitingAsk;
                    return null;
                case "print":
                    {
                        int id = V(1);
                        string src = GetString(id);
                        Output.Add(new SimOutput { Kind = SimOutputKind.Print, StringId = id, Source = src, Text = Convert(src ?? ""), Address = at });
                        return 0;
                    }
                case "pause": Note("pause(" + V(1) + "): the game waits a moment."); return 1;
                case "sex": return V(World.PlayerFemale ? 1 : 2);   // getmem(args[-2 + female])
                case "show_inv":
                    {
                        // conv_check_inv: the player's selected trade items, item ids at arg2, indices at arg1
                        var sel = PlayerSelectedItems();
                        for (int i = 0; i < World.TradeSlots; i++)
                        {
                            SetMem(A(2) + i, i < sel.Count ? World.Get(sel[i]).Item : 0);
                            SetMem(A(1) + i, i < sel.Count ? sel[i] : 0);
                        }
                        return sel.Count;
                    }
                case "find_barter":
                    {
                        int wanted = V(1);
                        foreach (int i in PlayerSelectedItems())
                        {
                            var o = World.Get(i);
                            if (wanted < 1000 ? o.Item == wanted : o.Major == (wanted - 1000) >> 2 && o.Minor == ((wanted - 1000) & 3)) return i;
                        }
                        return 0;
                    }
                case "find_barter_total":
                    {
                        // a class search (1000 and up) never finds anything (the enclosing test skips it)
                        int wanted = V(4), matches = 0, total = 0;
                        var found = new List<int>();
                        if (wanted < 1000)
                            foreach (int i in PlayerSelectedItems())
                            {
                                var o = World.Get(i);
                                if (o.Item != wanted) continue;
                                found.Add(i);
                                total += o.IsQuantity && o.Link < 0x200 ? o.Link : 1;
                                matches++;
                            }
                        SetMem(A(3), matches);
                        SetMem(A(1), total);
                        for (int i = 0; i < found.Count; i++) SetMem(A(2) + i, found[i]);
                        return total > 0 ? 1 : 0;
                    }
                case "give_to_npc":
                    {
                        // conv_give_inv: every listed object must be a selected trade item
                        int n = V(2);
                        var sel = PlayerSelectedItems();
                        if (sel.Count < n) return 0;
                        var give = new List<int>();
                        for (int i = 0; i < n; i++)
                        {
                            int idx = GetMem(A(1) + i);
                            if (!sel.Contains(idx) || give.Contains(idx)) return 0;
                            give.Add(idx);
                        }
                        foreach (int idx in give) PlayerBarterGive(idx);
                        return 1;
                    }
                case "give_ptr_npc":
                    {
                        int index = V(2), qty = V(1);
                        if (TakeFromSlots(World.PlayerSlots, World.PlayerSelected, index, false)) { GiveToNpc(index); return 1; }
                        var o = World.Get(index);
                        if (o == null || !RemoveFrom(World.PlayerInventory, index)) return 0;
                        if (qty >= 0 && o.IsQuantity && o.Link < 0x200 && qty < o.Link)
                        {
                            o.Link -= qty;
                            World.PlayerInventory.Add(index);
                            var part = World.Add(o.Item, qty, o.Quality);
                            part.Value = o.Value;
                            GiveToNpc(part.Index);
                        }
                        else GiveToNpc(index);
                        return 1;
                    }
                case "do_inv_delete": return NpcInvDelete(V(1));
                case "find_inv": return NpcBarterFind(V(2), V(1));
                case "take_from_npc": return NpcBarterGive(V(1), false);
                case "take_id_from_npc": return NpcBarterGive(V(1), true);
                case "do_inv_create": return NpcInvCreate(V(1));
                case "identify_inv":
                    {
                        var o = Obj(V(4), name);
                        bool describe = V(3) != 0;
                        string n = World.ItemName(o.Item);
                        int qty = o.Count;
                        string text = qty > 1 ? qty + " " + n : describe ? ("aeiou".IndexOf(char.ToLowerInvariant(n.Length > 0 ? n[0] : 'x')) >= 0 ? "an " : "a ") + n : n;
                        SetMem(A(2), MakeString(text));
                        return Value(o);
                    }
                case "count_inv": { var o = Obj(V(1), name); return o.Count; }
                case "check_inv_quality": { var o = Obj(V(1), name); return o.Quality; }
                case "set_inv_quality": { var o = Obj(V(2), name); o.Quality = V(1) & 0x3F; return 1; }
                case "switch_pic": Note("switch_pic(" + V(1) + "): the talker's portrait and name change."); return 1;

                // ----- conv/BARTER.C -----
                case "setup_to_barter":
                    {
                        // fills the talker's slots from its inventory (the game then may swap some at random)
                        for (int i = 0; i < World.TradeSlots && World.NpcInventory.Count > 0; i++)
                        {
                            if (World.NpcSlots[i] != 0) continue;
                            World.NpcSlots[i] = World.NpcInventory[0];
                            World.NpcSelected[i] = false;
                            World.NpcInventory.RemoveAt(0);
                        }
                        Note("setup_to_barter: the talker shows " + World.NpcSlots.Count(x => x != 0) + " items.");
                        return 0;
                    }
                case "do_offer":
                    {
                        int yes = V(5), no = V(4), none = V(1);
                        if (Nothing(World.PlayerSlots, World.PlayerSelected) || Nothing(World.NpcSlots, World.NpcSelected)) { Say(none, at); return 0; }
                        if (World.OffersAccepted)
                        {
                            Say(yes, at);
                            ReturnToNpc(true);
                            for (int i = 0; i < World.TradeSlots; i++)
                                if (World.PlayerSelected[i] && World.PlayerSlots[i] != 0) { GiveToNpc(World.PlayerSlots[i]); World.PlayerSlots[i] = 0; World.PlayerSelected[i] = false; }
                            // the talker's selected items stay in its slots for the player to take: here they go to the player
                            for (int i = 0; i < World.TradeSlots; i++)
                                if (World.NpcSlots[i] != 0) { World.PlayerInventory.Add(World.NpcSlots[i]); World.NpcSlots[i] = 0; World.NpcSelected[i] = false; }
                            Note("do_offer: the talker accepts (start-up setting).");
                            return 1;
                        }
                        Say(no, at);
                        Note("do_offer: the talker refuses (start-up setting).");
                        return 0;
                    }
                case "do_demand":
                    {
                        int insist = V(2), wont = V(1);
                        var im = Conversation.VariableNamed("npc_attitude");
                        if (World.DemandsGivenIn)
                        {
                            Say(insist, at);
                            ReturnToNpc(true);
                            for (int i = 0; i < World.TradeSlots; i++)
                                if (World.NpcSlots[i] != 0) { World.PlayerInventory.Add(World.NpcSlots[i]); World.NpcSlots[i] = 0; World.NpcSelected[i] = false; }
                            if (im != null && mem[im.Index] > 0) SetMem(im.Index, mem[im.Index] - 1);
                            Note("do_demand: the talker gives in (start-up setting).");
                            return 1;
                        }
                        Say(wont, at);
                        ReturnToNpc(false);
                        Note("do_demand: the talker refuses and attacks the player (goal 5, target 1).");
                        return 0;
                    }
                case "do_decline": ReturnToNpc(false); return 0;
                case "do_judgement": Note("do_judgement: the player appraises the deal (a line of block 7, not simulated)."); return 0;
                case "end_barter":
                    for (int i = 0; i < World.TradeSlots; i++)
                    {
                        if (World.NpcSlots[i] != 0) World.NpcInventory.Add(World.NpcSlots[i]);
                        if (World.PlayerSlots[i] != 0) World.PlayerInventory.Add(World.PlayerSlots[i]);
                        World.NpcSlots[i] = World.PlayerSlots[i] = 0;
                        World.NpcSelected[i] = World.PlayerSelected[i] = false;
                    }
                    return 0;
                case "set_likes_dislikes": Note("set_likes_dislikes: the talker's liked and disliked items (used by trading)."); return 1;
                case "give_all_stuff":
                    {
                        for (int i = 0; i < World.TradeSlots; i++) if (World.NpcSlots[i] != 0) World.NpcSelected[i] = true;
                        if (Nothing(World.NpcSlots, World.NpcSelected)) return 0;
                        ReturnToNpc(true);
                        return 1;
                    }

                // ----- conv/BABLHACK.C -----
                case "get_quest": return GetQuest(V(1));
                case "set_quest": SetQuest(V(2), V(1)); return 0;
                case "x_skills":
                    {
                        int skill = V(2), val = V(1);
                        if (skill < 0 || skill >= World.Skills.Length) throw new SimFault("x_skills: there is no skill " + skill);
                        if (uw2 && val > 10000)
                        {
                            // a skill point spent (grant_skill_advance, simplified to one more point)
                            if (World.SkillPoints > 0 && World.Skills[skill] < 30) { World.SkillPoints--; World.Skills[skill]++; Note("x_skills: a skill point spent on skill " + skill + "."); return 1; }
                            return 0;
                        }
                        if (val == 10000) Note("x_skills: a check of skill " + skill + " (get_skill); the simulator keeps the value.");
                        else if (val >= 0 && val <= 30) World.Skills[skill] = val;
                        return World.Skills[skill];
                    }
                case "x_traps":
                    {
                        int var = V(2), val = V(1);
                        if (!uw2)
                        {
                            if (var < 0 || var >= 64) throw new SimFault("x_traps: UW1 has game variables 0-63, not " + var);
                            if (val >= 0 && val <= 0x3F) World.GameVariables[var] = val;
                            return World.GameVariables[var];
                        }
                        if (val >= 0 && val <= 0x3FF) SetNumbered(var, val);
                        return GetNumbered(var);
                    }
                case "x_clock":
                    {
                        int clock = V(2), val = V(1);
                        if (clock < 0 || clock >= 16) throw new SimFault("x_clock: there is no clock " + clock);
                        if (val > 0x100) return World.XClocks[clock];
                        if (clock == 0) Note("x_clock: clock 0 moves the game clock.");
                        World.XClocks[clock] = val & 0xFF;
                        return 0;
                    }
                case "x_exp": World.Experience += V(1); return (int)((World.Experience >> 4) & 0xFFFF);
                case "set_attitude": Note("set_attitude: the creature with whoami " + V(2) + " gets attitude " + V(1) + "."); return 0;
                case "set_race_attitude": Note("set_race_attitude: race " + V(3) + " near the talker gets attitude " + V(2) + " (range " + V(1) + ")."); return 0;
                case "gronk_door": Note("gronk_door(" + V(3) + ", " + V(2) + ", " + V(1) + "): the door there is " + (V(1) == 0 ? "opened" : V(1) == 1 ? "closed" : "toggled") + " (the simulator assumes one is there)."); return 1;
                case "take_from_npc_inv":
                    {
                        int n = V(1);
                        return n >= 0 && n < World.NpcInventory.Count ? World.NpcInventory[n] : 0;
                    }
                case "add_to_npc_inv":
                    {
                        int index = V(1);
                        if (World.Get(index) != null) { Detach(index); World.NpcInventory.Add(index); }
                        return 0;
                    }
                case "place_object":
                    {
                        int index = V(3), x = V(2), y = V(1);
                        RemoveFrom(World.NpcInventory, index);
                        Note(x < 0 ? "place_object: object " + index + " goes to the player's feet." : "place_object: object " + index + " goes to tile (" + x + ", " + y + ").");
                        return x < 0 || x >= 1 && x < 64 && y >= 1 && y < 64 ? 1 : 0;
                    }
                case "remove_talker": Note("remove_talker: the talker leaves the map."); return 0;
                case "transform_talker": Note("transform_talker: the talker becomes another creature."); return 0;
                case "set_sequence": Note("set_sequence: an animation for whoami " + V(3) + "."); return 0;
                case "teleport_player": Note("teleport_player: after the conversation the player goes to (" + V(3) + ", " + V(2) + ") on level " + V(1) + "."); return 0;
                case "teleport_talker": Note("teleport_talker: after the conversation the talker goes to (" + V(2) + ", " + V(1) + ")."); return 1;
                case "x_obj_stuff": XObjStuff(); return 0;
                case "x_obj_pos": XObjPos(); return 0;
                case "babl_hack": return BablHack();

                // ----- conv/BABL.C, the string built-ins -----
                case "compare":
                    return string.Equals(Text(V(1)), Text(V(2)), StringComparison.OrdinalIgnoreCase) ? 1 : 0;
                case "random":
                    {
                        int n = V(1);
                        if (World.FixedRandom.HasValue) return Math.Max(1, Math.Min(Math.Max(1, n), World.FixedRandom.Value));
                        // BabRand: rand() * arg1 / 0x8000 + 1
                        return (int)((long)rng.Next(0x8000) * n / 0x8000) + 1;
                    }
                case "plural": return V(3) > 1 ? V(1) : V(2);
                case "contains": return Contains(V(1), V(2)) ? 1 : 0;
                case "append": return MakeString((GetString(V(2)) ?? "") + (GetString(V(1)) ?? ""));
                case "copy": return MakeString(GetString(V(1)) ?? "");
                case "find":
                    {
                        int value = V(1), n = V(2), start = A(3);
                        for (int i = 0; i < n; i++) if (GetMem(start + i) == value) return i + 1;
                        return 0;
                    }
                case "length": return (GetString(V(1)) ?? "").Length;
                case "val":
                    {
                        string s = (GetString(V(1)) ?? "").Trim();
                        return Atoi(s, 0);
                    }
                case "say":
                case "respond":
                    Note("CALLI " + name + ": the script calls the say or respond built-in directly; the game passes it the stack, not a text.");
                    return 0;
            }
            // unbound: an import the game never binds returns 0
            Note(name + " is not a built-in the game binds: it returns 0 (conv/BABL.C unbound).");
            return 0;
        }

        SimObject Obj(int index, string who)
        {
            var o = World.Get(index);
            if (o != null) return o;
            if (index <= 0 || index >= 0x400) throw new SimFault(who + ": " + index + " is not an object index");
            // an object of the level the simulator was not given (index 1 is the player): a stand-in with its fields at 0
            o = new SimObject { Index = index, Quality = 0 };
            World.Objects[index] = o;
            Note(who + ": object " + index + (index == 1 ? " (the player)" : "") + " is not in the simulator's world; a stand-in with every field 0 is used.");
            return o;
        }

        /// <summary>assess_value without the talker's likes and blur: the class value times the quantity times quality / 64, at least 1 for a quality above 0.</summary>
        static int Value(SimObject o)
        {
            int v = (short)(o.Value * o.Count);
            if (o.Quality > 0) { v = (short)(v * o.Quality) >> 6; if (v == 0) v = 1; }
            else v = 0;
            return v;
        }

        void Say(int id, int at)
        {
            string src = GetString(id);
            Output.Add(new SimOutput { Kind = SimOutputKind.Say, StringId = id, Source = src, Text = Convert(src ?? ""), Address = at });
        }

        /// <summary>contains: arg1's string holds arg2's as a whole word. The game lowercases the source strings, so case is ignored when neither has an @-variable.</summary>
        bool Contains(int id1, int id2)
        {
            string s1 = GetString(id1) ?? "", s2 = GetString(id2) ?? "";
            string c1 = s1.IndexOf('@') >= 0 ? Convert(s1) : s1.ToLowerInvariant();
            string c2 = s2.IndexOf('@') >= 0 ? Convert(s2) : s2.ToLowerInvariant();
            if (c2.Length == 0) return false;
            for (int p = c1.IndexOf(c2, StringComparison.Ordinal); p >= 0; p = c1.IndexOf(c2, p + c2.Length, StringComparison.Ordinal))
            {
                bool before = p == 0 || char.IsWhiteSpace(c1[p - 1]) || char.IsPunctuation(c1[p - 1]);
                int e = p + c2.Length;
                bool after = e >= c1.Length || char.IsWhiteSpace(c1[e]) || char.IsPunctuation(c1[e]);
                if (before && after) return true;
            }
            return false;
        }

        int GetQuest(int q)
        {
            if (q < 0) return 0;
            if (!World.Uw2)
            {
                // UW1: 1 << quest is a 16-bit int widened with its sign, so only quests 0-15 read right (conv/BABLHACK.C get_quest)
                if (q < 32) return (World.QuestWords[0] & Uw1Mask(q)) != 0 ? 1 : 0;
                if (q < 36) return World.QuestBytes[q - 32];
                return World.QuestTop;
            }
            if (q < 0x80) return (int)((World.QuestWords[q >> 2] & (1u << (q & 3))) >> (q & 3));
            if (q < 0x90) return World.QuestBytes[q - 0x80];
            return World.QuestTop;
        }

        void SetQuest(int q, int v)
        {
            if (q < 0) return;
            if (!World.Uw2)
            {
                if (q < 32)
                {
                    uint m = Uw1Mask(q);
                    World.QuestWords[0] = v != 0 ? World.QuestWords[0] | m : World.QuestWords[0] & ~m;
                    if (q >= 15) Note("set_quest(" + q + "): UW1's 16-bit mask " + (q == 15 ? "also sets bits 16-31" : "is 0, so nothing changes") + ".");
                }
                else if (q < 36) World.QuestBytes[q - 32] = v & 0xFF;
                return;
            }
            if (q < 0x80)
            {
                // a value other than 0 or 1 spills into the next flags, as the game's addition does
                uint w = World.QuestWords[q >> 2];
                World.QuestWords[q >> 2] = (uint)((w & ~(1u << (q & 3))) + ((uint)v << (q & 3)));
            }
            else if (q < 0x90) World.QuestBytes[q - 0x80] = v & 0xFF;
        }

        static uint Uw1Mask(int q) { return q < 16 ? (uint)(int)(short)(1 << q) : 0; }

        /// <summary>UW2's numbered variables (event/TRIGGER.C set_numbered_variable with op 2, set).</summary>
        void SetNumbered(int n, int val)
        {
            if (n < 0) return;
            if (n < 0x100) { World.GameVariables[n] = val & 0xFF; return; }   // (unsigned char)do_math_op(...) & 0x3FF
            n -= 0x100;
            if (n < 0x80) { uint w = World.QuestWords[n >> 2]; World.QuestWords[n >> 2] = (uint)((w & ~(1u << (n & 3))) + ((uint)(val > 0 ? 1 : 0) << (n & 3))); return; }
            n -= 0x80;
            if (n < 0x10) { World.QuestBytes[n] = val & 0xFF; return; }
            n -= 0x10;
            if (n < 0x10) World.XClocks[n] = val & 0xFF;
        }

        int GetNumbered(int n)
        {
            if (n < 0) return 0;
            if (n < 0x100) return World.GameVariables[n];
            n -= 0x100;
            if (n < 0x80) return (int)((World.QuestWords[n >> 2] >> (n & 3)) & 1);
            n -= 0x80;
            if (n < 0x10) return World.QuestBytes[n];
            n -= 0x10;
            return n < 0x10 ? World.XClocks[n] : 0;
        }

        int BablHack()
        {
            int mode = V(1);
            switch (mode)
            {
                case 0: Note("babl_hack 0: the talker challenges the player to a pit fight."); return 0;
                case 1: return 0;   // running away from a fight: not in the simulator
                case 2:
                    {
                        int n = V(2);
                        int placed = Math.Max(0, Math.Min(n, 6));
                        int debt = placed == 2 ? 8 : placed == 3 ? 12 : placed == 4 ? 20 : placed == 5 ? 40 : 0;
                        World.QuestBytes[5] = debt;   // quest 133, Jospur's debt
                        Note("babl_hack 2: " + placed + " pit fighters placed, debt " + debt + ".");
                        return placed;
                    }
                case 3: { int d = World.QuestBytes[5]; World.QuestBytes[5] = 0; return d; }
                case 4: return 0;   // the player is not in the pits
                case 7: Note("babl_hack 7: a wand recharged."); return 1;
                case 8: Note("babl_hack 8: the talker's greed times " + V(2) + "."); return 0;
                case 9: return V(2);
                case 10: return 0;  // the Guardian's signet ring is not worn
            }
            Note("babl_hack " + mode + ".");
            return 0;
        }

        void XObjStuff()
        {
            // x_obj_stuff(arg9 object, arg8 set, arg7 heading, arg6 owner, arg5 flags, arg4 link, arg3 flag10, arg2 flag9, arg1 quality); a -1 is left alone
            var o = Obj(V(9), "x_obj_stuff");
            bool set = V(8) != 0;
            int[] at = { A(7), A(6), A(5), A(4), A(3), A(2), A(1) };
            Func<int, int> get = i => GetMem(at[i]);
            if (set)
            {
                if (get(0) != -1) o.Heading = get(0) & 7;
                if (get(1) != -1) o.Owner = get(1) & 0x3F;
                if (get(2) != -1) o.Flags = get(2) & 7;
                if (get(3) != -1) o.Link = World.Uw2 ? get(3) & 0x3FF : 0x200 | (get(3) & 0x3FF);   // UW1 sets bit 9 of a link it writes
                if (get(4) != -1) o.Flag10 = get(4) != 0;
                if (get(5) != -1) o.Flag9 = get(5) != 0;
                if (get(6) != -1) o.Quality = get(6) & 0x3F;
            }
            else
            {
                if (get(0) != -1) SetMem(at[0], o.Heading);
                if (get(1) != -1) SetMem(at[1], o.Owner);
                if (get(2) != -1) SetMem(at[2], o.Flags);
                if (get(3) != -1) SetMem(at[3], World.Uw2 ? o.Link : o.Link & 0x1FF);
                if (get(4) != -1) SetMem(at[4], o.Flag10 ? 0x400 : 0);
                if (get(5) != -1) SetMem(at[5], o.Flag9 ? 0x200 : 0);
                if (get(6) != -1) SetMem(at[6], o.Quality);
            }
        }

        void XObjPos()
        {
            var o = Obj(V(5), "x_obj_pos");
            int mode = V(4);
            int ax = A(3), ay = A(2), az = A(1);
            if (mode == 1)
            {
                if (GetMem(ax) != -1) o.FineX = GetMem(ax) & 7;
                if (GetMem(ay) != -1) o.FineY = GetMem(ay) & 7;
                if (GetMem(az) != -1) o.Z = GetMem(az) > 0x7F ? 0 : GetMem(az);
            }
            else
            {
                if (GetMem(ax) != -1) SetMem(ax, o.FineX);
                if (GetMem(ay) != -1) SetMem(ay, o.FineY);
                if (GetMem(az) != -1) SetMem(az, o.Z);
            }
        }

        // ----- inventories and the trade slots -----

        List<int> PlayerSelectedItems()
        {
            var l = new List<int>();
            for (int i = 0; i < World.TradeSlots; i++) if (World.PlayerSelected[i] && World.PlayerSlots[i] != 0) l.Add(World.PlayerSlots[i]);
            return l;
        }

        static bool Nothing(int[] slots, bool[] selected)
        {
            for (int i = 0; i < slots.Length; i++) if (selected[i] && slots[i] > 0) return false;
            return true;
        }

        bool TakeFromSlots(int[] slots, bool[] selected, int index, bool all)
        {
            for (int i = 0; i < slots.Length; i++)
                if (slots[i] == index && index != 0) { slots[i] = 0; selected[i] = false; return true; }
            return false;
        }

        void PlayerBarterGive(int index)
        {
            TakeFromSlots(World.PlayerSlots, World.PlayerSelected, index, false);
            GiveToNpc(index);
        }

        /// <summary>npc_inv_add: into the talker's inventory, coins added to a pile it has.</summary>
        void GiveToNpc(int index)
        {
            var o = World.Get(index);
            if (o == null) return;
            Detach(index);
            if (o.IsQuantity && o.Link < 0x200)
                foreach (int i in World.NpcInventory)
                {
                    var other = World.Get(i);
                    if (other != null && other.Item == o.Item && other.IsQuantity && other.Link < 0x200 && World.ItemName(o.Item).IndexOf("coin", StringComparison.OrdinalIgnoreCase) >= 0)
                    {
                        other.Link += o.Link;
                        World.Objects.Remove(index);
                        return;
                    }
                }
            World.NpcInventory.Add(index);
        }

        /// <summary>The talker's slot items back into its inventory: all, or only the unselected ones.</summary>
        void ReturnToNpc(bool onlyUnselected)
        {
            for (int i = 0; i < World.TradeSlots; i++)
            {
                if (World.NpcSlots[i] == 0 || onlyUnselected && World.NpcSelected[i]) continue;
                World.NpcInventory.Add(World.NpcSlots[i]);
                World.NpcSlots[i] = 0;
                World.NpcSelected[i] = false;
            }
        }

        void Detach(int index)
        {
            RemoveFrom(World.NpcInventory, index);
            RemoveFrom(World.PlayerInventory, index);
            TakeFromSlots(World.PlayerSlots, World.PlayerSelected, index, false);
            TakeFromSlots(World.NpcSlots, World.NpcSelected, index, false);
            if (World.InHand == index) World.InHand = 0;
            foreach (var o in World.Objects.Values) o.Contents.Remove(index);
        }

        static bool RemoveFrom(List<int> list, int index) { return list.Remove(index); }

        /// <summary>npc_barter_find: an item (or from 1000 a major and minor) in the talker's or the player's inventory, containers too; its index or 0.</summary>
        int NpcBarterFind(int item, int fromPlayer)
        {
            var roots = fromPlayer != 0 ? World.PlayerInventory : World.NpcInventory;
            int major, minor, cls;
            if (item >= 1000) { major = (item - 1000) >> 2; minor = (item - 1000) & 3; cls = -1; }
            else { major = item >> 6; minor = (item & 0x30) >> 4; cls = item & 0xF; }
            var stack = new Stack<int>(roots.AsEnumerable().Reverse());
            var seen = new HashSet<int>();
            while (stack.Count > 0)
            {
                int i = stack.Pop();
                if (!seen.Add(i)) continue;
                var o = World.Get(i);
                if (o == null) continue;
                if (o.Major == major && o.Minor == minor && (cls < 0 || (o.Item & 0xF) == cls)) return i;
                for (int k = o.Contents.Count - 1; k >= 0; k--) stack.Push(o.Contents[k]);
            }
            return 0;
        }

        /// <summary>npc_barter_give(_id): the talker hands over an item of a type (or class from 1000), or one object; into the player's hand. 0 when there is none or the hand is full.</summary>
        int NpcBarterGive(int what, bool byIndex)
        {
            if (World.InHand != 0) { Note("The player's hand is full: the game gives nothing (CursorObjPtr)."); return 0; }
            foreach (int i in World.NpcInventory)
            {
                var o = World.Get(i);
                if (o == null) continue;
                bool match = byIndex ? i == what : what >= 1000 ? o.Class == what - 1000 : o.Item == what;
                if (!match) continue;
                World.NpcInventory.Remove(i);
                World.InHand = i;
                Note("The talker hands " + World.ItemName(o.Item) + " (object " + i + ") to the player.");
                return 1;
            }
            return 0;
        }

        /// <summary>npc_inv_create: a new item of quality 63 in the talker's inventory, added to a like stack there; its index.</summary>
        int NpcInvCreate(int item)
        {
            if (item < 0 || item >= 0x200) return 0;
            var o = World.Add(item, 1, 0x3F);
            World.Objects.Remove(o.Index);
            o.IsQuantity = item >= 0xA0 && item < 0xA8;   // coins and gems stack (the game asks the object's class)
            o.Link = o.IsQuantity ? 1 : 0;
            foreach (int i in World.NpcInventory)
            {
                var other = World.Get(i);
                if (o.IsQuantity && other != null && other.IsQuantity && other.Item == item && other.Link < 0x200 && other.Link + o.Link < 0x200)
                {
                    other.Link += o.Link;
                    return 0;   // Obj_MemTPtr of the freed object
                }
            }
            World.Objects[o.Index] = o;
            World.NpcInventory.Add(o.Index);
            Note("do_inv_create: " + World.ItemName(item) + " (object " + o.Index + ") made for the talker.");
            return o.Index;
        }

        int NpcInvDelete(int item)
        {
            foreach (int i in World.NpcInventory)
            {
                var o = World.Get(i);
                if (o == null || o.Item != item) continue;
                World.NpcInventory.Remove(i);
                World.Objects.Remove(i);
                return 1;
            }
            return 0;
        }

        // ----- running a path, for tests and the panel -----

        /// <summary>
        /// Runs to the end, choosing with choose (the menu, how often this menu was met; returns
        /// a choice from 1) and answering babl_ask with answer. False when it stopped first
        /// (an error, or maxSteps instructions in all).
        /// </summary>
        public bool RunToEnd(Func<SimMenu, int, int> choose, Func<string> answer = null, long maxSteps = 500000)
        {
            var seen = new Dictionary<string, int>();
            while (Steps < maxSteps)
            {
                Run((int)Math.Min(100000, maxSteps - Steps));
                if (State == SimState.WaitingMenu)
                {
                    string key = Menu.Address + ":" + string.Join(",", Menu.Ids);
                    int n;
                    seen.TryGetValue(key, out n);
                    seen[key] = n + 1;
                    Choose(Math.Max(1, Math.Min(Menu.Ids.Count, choose(Menu, n))));
                }
                else if (State == SimState.WaitingAsk) Answer(answer == null ? "" : answer());
                else if (State != SimState.Running) break;
            }
            return State == SimState.Ended;
        }
    }

    /// <summary>A fault the game would not notice (it reads past its memory) but the simulator stops at.</summary>
    public sealed class SimFault : Exception
    {
        public SimFault(string message) : base(message) { }
    }
}
