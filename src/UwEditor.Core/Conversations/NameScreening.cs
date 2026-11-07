using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using UltimateEditor.Games;

namespace UwEditor.Core.Conversations
{
    /// <summary>
    /// The first names for a game's conversations, made once from the game's own CNV.ARK and
    /// STRINGS.PAK and written into docs/conversations/names-uw1.json and names-uw2.json,
    /// where they are refined by hand:
    /// the compiler's shared library functions get the hand-chosen names below (wherever
    /// their body is); a function that says something or shows a menu is named after what it
    /// says first (or "menu_" and its first choice); the function main calls, when it says
    /// nothing itself, is "start"; labels are named by their role (done, loop, menu_loop,
    /// end_of_menu, not_choice_2, otherwise, end_if); and globals where their use is plain
    /// (met_before, quest_32).
    /// </summary>
    public static class NameScreening
    {
        /// <summary>A library function named by hand: where one copy of its body is (game, slot, address), its name and what it does.</summary>
        public sealed class LibraryName
        {
            public int Slot, Address;
            public string Name, Note;
            public LibraryName(int slot, int address, string name, string note) { Slot = slot; Address = address; Name = name; Note = note; }
        }

        // npc_goal and npc_attitude as uw1/uw2 include/critter.h has them (goal 1 go home, 5
        // attack, 6 flee; attitude 1 upset, 2 mellow, 6 an ally, conv/CONVVARS.C); the
        // conversation ends where the script stops (EXIT, conv/BABL.C babl_run).
        static readonly LibraryName[] uw1 =
        {
            new LibraryName(1, 0x0012, "end_conversation", "Sets the global the conversation reads as met_before (the player has talked to it) and stops the script (EXIT)."),
            new LibraryName(3, 0x0012, "end_conversation", "Sets the global the conversation reads as met_before (the player has talked to it) and stops the script (EXIT)."),
            new LibraryName(14, 0x0012, "end_conversation", "Sets the global the conversation reads as met_before (the player has talked to it) and stops the script (EXIT)."),
            new LibraryName(1, 0x0020, "npc_not_hostile", "Returns 0 when the NPC is set on the player (goal 5, 6 or 9 with target 1) or hostile (attitude 0), else 1."),
            new LibraryName(1, 0x0063, "flee_and_end", "The NPC flees the player (goal 6, target 1, attitude upset), and the conversation ends."),
            new LibraryName(11, 0x0063, "flee_and_end", "The NPC flees (goal 6), and the conversation ends."),
            new LibraryName(1, 0x007C, "go_home_and_end", "The NPC heads home (goal 1), and the conversation ends."),
            new LibraryName(11, 0x0072, "go_home_and_end", "The NPC heads home (goal 1), and the conversation ends."),
            new LibraryName(1, 0x008B, "attack_and_end", "The NPC attacks the player (goal 5, target 1, attitude upset), and the conversation ends."),
            new LibraryName(11, 0x0081, "attack_and_end", "The NPC attacks the player (goal 5, target 1, attitude upset), and the conversation ends."),
            new LibraryName(1, 0x00A4, "become_ally", "npc_attitude 6: the NPC becomes the player's ally."),
            new LibraryName(1, 0x00B1, "set_attitude_and_end", "Sets npc_attitude to the argument, and the conversation ends."),
            new LibraryName(11, 0x00A7, "set_attitude_and_end", "Sets npc_attitude to the argument, and the conversation ends."),
            new LibraryName(1, 0x00C2, "mellow_and_end", "npc_attitude 2 (mellow), and the conversation ends."),
            new LibraryName(11, 0x00B8, "mellow_and_end", "npc_attitude 2 (mellow), and the conversation ends."),
            new LibraryName(1, 0x00D1, "upset_and_end", "npc_attitude 1 (upset), and the conversation ends."),
            new LibraryName(11, 0x00C7, "upset_and_end", "npc_attitude 1 (upset), and the conversation ends."),
            new LibraryName(1, 0x00E0, "goodbye", "The conversation ends."),
            new LibraryName(11, 0x00D6, "goodbye", "The conversation ends."),
            new LibraryName(1, 0x00EA, "get_time", "Stores the time (game_days, game_mins) in the argument's two words."),
            new LibraryName(1, 0x0106, "time_has_passed", "Returns 1 when at least the first argument's days and minutes have passed since the time in the second."),
            new LibraryName(1, 0x018F, "time_left", "Stores in the first argument what is left of the second's span, counted from the time in the third."),
            new LibraryName(1, 0x0243, "time_since", "Stores in the first argument the days and minutes since the time in the second."),
            new LibraryName(146, 0x029D, "start", "The Knights of the Crux Ansata's conversation (four slots share it): by quest 32."),
            new LibraryName(146, 0x09D9, "make_offer", "Barter: do_offer with this conversation's strings; when the offer is taken, sets the global that ends the barter menu (main clears it)."),
        };

        static readonly LibraryName[] uw2 =
        {
            new LibraryName(1, 0x0012, "end_conversation", "Sets the global the conversation reads as met_before (the player has talked to it) and stops the script (EXIT)."),
            new LibraryName(1, 0x0020, "player_in_pits", "babl_hack(4): whether the player is in the pits (uw2 conv/BABLHACK.C)."),
            new LibraryName(1, 0x0042, "challenge_to_pit_fight", "babl_hack(0): the talker becomes the player's pit fighter (uw2 conv/BABLHACK.C)."),
            new LibraryName(1, 0x0058, "ran_from_pit_fight", "babl_hack(1): whether the player ran from a pit fight, cleared once read (uw2 conv/BABLHACK.C)."),
            new LibraryName(1, 0x007A, "npc_not_hostile", "Returns 0 when the NPC is set on the player (goal 5, 6 or 9 with target 1) or hostile (attitude 0), else 1."),
            new LibraryName(1, 0x00BD, "player_tile", "x_obj_pos mode 2: the player's tile and height into the three arguments (uw2 conv/BABLHACK.C)."),
            new LibraryName(1, 0x00E8, "flee_and_end", "The NPC flees the player (goal 6, target 1, attitude upset), and the conversation ends."),
            new LibraryName(1, 0x0101, "go_home_and_end", "The NPC heads home (goal 1), and the conversation ends."),
            new LibraryName(1, 0x0110, "attack_and_end", "The NPC attacks the player (goal 5, target 1, attitude upset), and the conversation ends."),
            new LibraryName(1, 0x0129, "become_ally", "npc_attitude 6: the NPC becomes the player's ally."),
            new LibraryName(1, 0x0136, "set_attitude_and_end", "Sets npc_attitude to the argument, and the conversation ends."),
            new LibraryName(1, 0x0147, "mellow_and_end", "npc_attitude 2 (mellow), and the conversation ends."),
            new LibraryName(1, 0x0156, "upset_and_end", "npc_attitude 1 (upset), and the conversation ends."),
            new LibraryName(1, 0x0165, "goodbye", "The conversation ends."),
            new LibraryName(1, 0x016F, "get_time", "Stores the time (game_days, game_mins) in the argument's two words."),
            new LibraryName(1, 0x018B, "time_has_passed", "Returns 1 when at least the first argument's days and minutes have passed since the time in the second."),
            new LibraryName(1, 0x0214, "time_left", "Stores in the first argument what is left of the second's span, counted from the time in the third."),
            new LibraryName(1, 0x02C8, "time_since", "Stores in the first argument the days and minutes since the time in the second."),
            new LibraryName(1, 0x0322, "tick_clock_15", "Adds one to X clock 15."),
            new LibraryName(1, 0x0593, "quest131_bit", "Bit n of quest 131: returns it, or for n + 1000 sets it and returns 1 if it was clear."),
            new LibraryName(1, 0x064D, "pick_quest134_line", "Picks a number 1-9 once (kept in quest 134) and returns string 21 + that number."),
            new LibraryName(17, 0x040D, "make_offer", "Barter: do_offer with this conversation's strings; when the offer is taken, sets the global that ends the barter menu (main clears it)."),
            new LibraryName(34, 0x0874, "wearing_signet_ring", "babl_hack(10): whether the player wears the Guardian's signet ring (uw2 conv/BABLHACK.C)."),
            new LibraryName(98, 0x0441, "pit_fight_1", "Challenges the talker to a pit fight and sends both to the first pit."),
            new LibraryName(98, 0x048E, "pit_fight_2", "Challenges the talker to a pit fight and sends both to the second pit."),
            new LibraryName(98, 0x04DB, "pit_fight_3", "Challenges the talker to a pit fight and sends both to the third pit."),
            new LibraryName(98, 0x0528, "pit_fight_4", "Challenges the talker to a pit fight and sends both to the fourth pit."),
            new LibraryName(130, 0x03FA, "pick_rumor", "Finds the next rumour to tell from the two rumour tables, by rumor_state."),
            new LibraryName(130, 0x04FE, "rumor_state", "A rumour's state: 1 told, 2 ready to tell, 3 ready and then spent, 0 not yet (by quest or X clock)."),
        };

        public static IReadOnlyList<LibraryName> Library(GameKind kind) { return kind == GameKind.UW2 ? uw2 : uw1; }

        /// <summary>A function's code says something, shows a menu or prints: it has lines of its own.</summary>
        public static bool Speaks(Conversation c, BablLayout lay, int f)
        {
            int end = lay.EndOf(f), i;
            if (!lay.At.TryGetValue(f, out i)) return false;
            for (; i < lay.Code.Count && lay.Code[i].Address < end; i++)
            {
                var ins = lay.Code[i];
                if (!ins.Known) continue;
                if (ins.Op == BablOp.SAY || ins.Op == BablOp.RESPOND) return true;
                if (ins.Op == BablOp.CALLI)
                {
                    var fn = c.Function(ins.Operand);
                    if (fn != null && (fn.Name == "babl_menu" || fn.Name == "babl_fmenu" || fn.Name == "print")) return true;
                }
            }
            return false;
        }

        /// <summary>
        /// The compiler's library: function bodies found in three slots or more that have no
        /// lines of their own (main aside). Each slot's copy has the same body.
        /// </summary>
        public static Dictionary<string, List<KeyValuePair<int, int>>> LibraryBodies(ConversationArchive cnv)
        {
            var all = new Dictionary<string, List<KeyValuePair<int, int>>>(StringComparer.Ordinal);
            var speaks = new Dictionary<string, bool>(StringComparer.Ordinal);
            foreach (int s in cnv.Slots())
            {
                var c = cnv.Read(s);
                var lay = BablLayout.Of(c);
                foreach (int f in lay.Functions)
                {
                    if (f == 0) continue;
                    string b = lay.Body(f);
                    List<KeyValuePair<int, int>> list;
                    if (!all.TryGetValue(b, out list)) { all[b] = list = new List<KeyValuePair<int, int>>(); speaks[b] = Speaks(c, lay, f); }
                    list.Add(new KeyValuePair<int, int>(s, f));
                }
            }
            return all.Where(kv => !speaks[kv.Key] && kv.Value.Select(x => x.Key).Distinct().Count() >= 3)
                .ToDictionary(kv => kv.Key, kv => kv.Value, StringComparer.Ordinal);
        }

        /// <param name="talker">The name a slot talks under, kept as a comment in the file (null: none).</param>
        public static NameFile Screen(ConversationArchive cnv, StringsPakFile strings, GameKind kind, Func<int, string> talker = null)
        {
            var file = new NameFile
            {
                Game = kind == GameKind.UW2 ? "UW2" : "UW1",
                Notes = "Names for the conversations' functions, labels, locals and globals, edited by hand. "
                    + "library: the compiler's shared functions, by body (applied in every slot). "
                    + "slots: per conversation, for the block with that sha256; functions by hex address with their labels (hex addresses) and locals (frame offsets), globals by memory address. "
                    + "When a block differs, names follow each function's body. Made first by NameScreening; see docs/MANUAL.md, Conversations.",
            };
            // the library, by hand
            foreach (var ln in Library(kind))
            {
                if (!cnv.Has(ln.Slot)) continue;
                var c = cnv.Read(ln.Slot);
                var lay = BablLayout.Of(c);
                if (!lay.Functions.Contains(ln.Address)) continue;
                var d = Decompiler.Decompile(c, null, kind, false);
                var fn = new FunctionNames { Name = ln.Name, Note = ln.Note, Body = lay.Body(ln.Address) };
                foreach (var kv in LabelRoles(c, lay, d, ln.Address)) fn.Labels[kv.Key - ln.Address] = kv.Value;
                file.Library[fn.Body] = fn;
            }
            foreach (int slot in cnv.Slots())
            {
                var c = cnv.Read(slot);
                var lay = BablLayout.Of(c);
                var strs = strings.Get(c.StringBlock).ToArray();
                var d = Decompiler.Decompile(c, strs, kind, false);
                var s = new SlotNames { Sha256 = NameBook.BlockHash(c), Talker = talker == null ? null : talker(slot) };
                var taken = new HashSet<string>(StringComparer.Ordinal) { "main" };
                foreach (var im in c.Imports) taken.Add(im.Name);
                Func<string, string> unique = n =>
                {
                    string u = n;
                    for (int k = 2; taken.Contains(u); k++) u = n + "_" + k;
                    taken.Add(u);
                    return u;
                };
                var lib = new Dictionary<int, FunctionNames>();
                foreach (int f in lay.Functions)
                {
                    FunctionNames fn;
                    if (f != 0 && file.Library.TryGetValue(lay.Body(f), out fn)) lib[f] = fn;
                }
                // function names, in address order, as NameBook.Resolve gives them out
                var start = StartFunction(lay, lib);
                foreach (int f in lay.Functions)
                {
                    if (f == 0) continue;
                    if (lib.ContainsKey(f)) { unique(lib[f].Name); continue; }
                    string name = SpeechName(d, lay, f, strs);
                    // speaks, but its lines are not constants the view can read (a menu built at run time)
                    if (name == null && Speaks(c, lay, f)) name = "menu_" + f.ToString("X4").ToLowerInvariant();
                    if (name == null && f == start) name = "start";
                    if (name == null) continue;
                    s.Functions[f] = new FunctionNames { Name = unique(name), Body = lay.Body(f) };
                }
                // labels
                foreach (int f in lay.Functions)
                {
                    var roles = LabelRoles(c, lay, d, f);
                    if (lib.ContainsKey(f)) { foreach (var kv in roles) unique(lib[f].Labels.TryGetValue(kv.Key - f, out var ln) ? ln : kv.Value); continue; }
                    if (roles.Count == 0) continue;
                    FunctionNames fn;
                    if (!s.Functions.TryGetValue(f, out fn)) s.Functions[f] = fn = new FunctionNames { Body = lay.Body(f) };
                    foreach (var kv in roles) fn.Labels[kv.Key - f] = unique(kv.Value);
                }
                foreach (var kv in GlobalRoles(c, lay, d, lib, start)) s.Globals[kv.Key] = unique(kv.Value);
                file.Slots[slot] = s;
            }
            return file;
        }

        /// <summary>
        /// A fresh screening with the names already in a file kept: its library entries, and
        /// for each slot whose block is the same, its function, label, local and global names
        /// (what was refined by hand stays; what the screening adds comes in).
        /// </summary>
        public static NameFile Merge(NameFile screened, NameFile existing)
        {
            if (existing == null) return screened;
            if (existing.Notes != null) screened.Notes = existing.Notes;
            foreach (var kv in existing.Library)
            {
                FunctionNames fn;
                if (!screened.Library.TryGetValue(kv.Key, out fn)) { screened.Library[kv.Key] = kv.Value.Clone(); continue; }
                if (kv.Value.Name != null) fn.Name = kv.Value.Name;
                if (kv.Value.Note != null) fn.Note = kv.Value.Note;
                foreach (var l in kv.Value.Labels) fn.Labels[l.Key] = l.Value;
                foreach (var l in kv.Value.Locals) fn.Locals[l.Key] = l.Value;
            }
            foreach (var kv in existing.Slots)
            {
                SlotNames s;
                if (!screened.Slots.TryGetValue(kv.Key, out s) || s.Sha256 != kv.Value.Sha256) continue;
                if (kv.Value.Talker != null) s.Talker = kv.Value.Talker;
                foreach (var g in kv.Value.Globals) s.Globals[g.Key] = g.Value;
                foreach (var f in kv.Value.Functions)
                {
                    FunctionNames fn;
                    if (!s.Functions.TryGetValue(f.Key, out fn)) { s.Functions[f.Key] = f.Value.Clone(); continue; }
                    if (f.Value.Name != null) fn.Name = f.Value.Name;
                    if (f.Value.Note != null) fn.Note = f.Value.Note;
                    foreach (var l in f.Value.Labels) fn.Labels[l.Key] = l.Value;
                    foreach (var l in f.Value.Locals) fn.Locals[l.Key] = l.Value;
                }
            }
            return screened;
        }

        /// <summary>The name a slot talks under, from STRINGS.PAK block 7 (string slot + 16), as ConversationLibrary.Name; null for the generic slots.</summary>
        public static Func<int, string> Talkers(StringsPakFile strings)
        {
            var b = strings.Get(7);
            return slot => slot < 0x100 && slot + 16 < b.Count && !string.IsNullOrWhiteSpace(b[slot + 16]) ? b[slot + 16].Trim() : null;
        }

        /// <summary>The script function main calls first that is not the library's: the conversation proper.</summary>
        static int StartFunction(BablLayout lay, Dictionary<int, FunctionNames> lib)
        {
            int end = lay.EndOf(0);
            for (int i = 0; i < lay.Code.Count && lay.Code[i].Address < end; i++)
            {
                var ins = lay.Code[i];
                if (ins.Known && ins.Op == BablOp.CALL && lay.Functions.Contains(ins.Target) && !lib.ContainsKey(ins.Target)) return ins.Target;
            }
            return -1;
        }

        /// <summary>The instruction at an address is CALLI print (narration in the conversation's window).</summary>
        static bool Prints(BablLayout lay, int address)
        {
            int i;
            if (lay.Conversation == null || !lay.At.TryGetValue(address, out i)) return false;
            var ins = lay.Code[i];
            var fn = ins.Known && ins.Op == BablOp.CALLI ? lay.Conversation.Function(ins.Operand) : null;
            return fn != null && fn.Name == "print";
        }

        static readonly Regex variable = new Regex(@"@[A-Za-z]{2}\d*(?:[A-Za-z]{2}\d*)?");

        /// <summary>A name from what a function says first, or the first choice of the menu it shows first; null when it does neither.</summary>
        static string SpeechName(DecompileResult d, BablLayout lay, int f, string[] strs)
        {
            int end = lay.EndOf(f);
            int best = int.MaxValue, id = -1;
            bool menu = false;
            foreach (var u in d.Strings)
                if ((u.Kind == StringUseKind.Say || u.Kind == StringUseKind.Respond || u.Kind == StringUseKind.Argument && Prints(lay, u.UseAddress))
                    && u.UseAddress >= f && u.UseAddress < end && u.UseAddress < best)
                { best = u.UseAddress; id = u.Id; menu = false; }
            foreach (var m in d.Menus)
                if (m.Address >= f && m.Address < end && m.Address < best && m.Ids.Count > 0)
                { best = m.Address; id = m.Ids[0]; menu = true; }
            if (id < 0) return null;
            string text = strs != null && id < strs.Length ? strs[id] : null;
            string words = Words(text);
            if (words == null) return (menu ? "menu_" : "say_") + id;
            return menu ? "menu_" + words : Safe(words);
        }

        /// <summary>The first few words of a line, snake_case: at most four, about 24 letters.</summary>
        public static string Words(string text)
        {
            if (string.IsNullOrEmpty(text)) return null;
            text = variable.Replace(text, " ");
            var sb = new StringBuilder();
            int n = 0;
            foreach (Match m in Regex.Matches(text.Replace("'", "").Replace("’", ""), "[A-Za-z0-9]+"))
            {
                if (n == 4 || n > 0 && sb.Length + m.Length > 28) break;
                if (sb.Length > 0) sb.Append('_');
                sb.Append(m.Value.ToLowerInvariant());
                n++;
            }
            if (sb.Length == 0) return null;
            string s = sb.ToString();
            if (char.IsDigit(s[0])) s = "n" + s;
            return s;
        }

        /// <summary>A name that is not a word of the views or an instruction.</summary>
        static string Safe(string n)
        {
            var probe = new ConversationNames();
            var c = new Conversation();
            string why = probe.Check(c, NameTarget.Label(1), n);
            return why == null ? n : "say_" + n;
        }

        /// <summary>
        /// The labels in a function, named by role: done (only the function's end follows), bye
        /// (an EXIT), loop or menu_loop (jumped back to), end_of_menu (where a menu's answers
        /// meet), end_if (where an if's branches meet), not_choice_N or choice_N (a menu's answer
        /// tested), otherwise (the other branch of an if).
        /// </summary>
        public static SortedDictionary<int, string> LabelRoles(Conversation c, BablLayout lay, DecompileResult d, int f)
        {
            var r = new SortedDictionary<int, string>();
            int end = lay.EndOf(f);
            var labels = lay.Labels.Where(a => a >= f && a < end).ToList();
            if (labels.Count == 0) return r;
            var jumps = new Dictionary<int, List<BablInstruction>>();
            var menus = new List<int>();
            for (int i = lay.At[f]; i < lay.Code.Count && lay.Code[i].Address < end; i++)
            {
                var ins = lay.Code[i];
                if (!ins.Known) continue;
                if (BablOps.IsJump(ins.Op) && ins.Op != BablOp.CALL)
                {
                    if (!jumps.ContainsKey(ins.Target)) jumps[ins.Target] = new List<BablInstruction>();
                    jumps[ins.Target].Add(ins);
                }
                if (ins.Op == BablOp.CALLI && c.Function(ins.Operand) is BablImport fn && (fn.Name == "babl_menu" || fn.Name == "babl_fmenu")) menus.Add(ins.Address);
            }
            // the variables menus' answers go into
            var menuVars = new HashSet<string>(StringComparer.Ordinal);
            foreach (var l in d.Lines)
            {
                if (l.Address < f || l.Address >= end) continue;
                int eq = l.Text.IndexOf(" = babl_", StringComparison.Ordinal);
                if (eq > 0 && (l.Text.Contains("babl_menu(") || l.Text.Contains("babl_fmenu("))) menuVars.Add(l.Text.Substring(0, eq).Trim());
            }
            Func<int, Match> menuTest = a =>
            {
                string cond;
                if (!d.Branches.TryGetValue(a, out cond)) return null;
                var m = Regex.Match(cond, @"^(\S+) (==|!=) (-?\d+)$");
                return m.Success && menuVars.Contains(m.Groups[1].Value) ? m : null;
            };
            foreach (int t in labels)
            {
                List<BablInstruction> from;
                if (!jumps.TryGetValue(t, out from)) from = new List<BablInstruction>();
                int ti = lay.At[t];
                var first = lay.Code[ti];
                string role;
                if (first.Known && first.Op == BablOp.EXIT) role = "bye";
                else if (OnlyEnd(lay, ti, end)) role = "done";
                else if (from.Any(j => j.Address >= t))
                {
                    int last = from.Max(j => j.Address);
                    role = menus.Any(m => m >= t && m <= last) ? "menu_loop" : "loop";
                }
                else if (from.Any(j => j.Op == BablOp.JMP || j.Op == BablOp.BRA))
                {
                    int lo = from.Min(j => j.Address);
                    int menuBefore = menus.Where(m => m < lo).DefaultIfEmpty(-1).Max();
                    bool dispatch = menuBefore >= 0 && jumps.Values.SelectMany(x => x).Any(j => (j.Op == BablOp.BEQ || j.Op == BablOp.BNE) && j.Address > menuBefore && j.Address < t && menuTest(j.Address) != null);
                    role = dispatch ? "end_of_menu" : "end_if";
                }
                else
                {
                    var m = from.Count == 1 ? menuTest(from[0].Address) : null;
                    if (m != null) role = (m.Groups[2].Value == "!=" ? "not_choice_" : "choice_") + m.Groups[3].Value.Replace("-", "minus_");
                    else
                    {
                        var before = ti > 0 ? lay.Code[ti - 1] : default(BablInstruction);
                        role = before.Known && (before.Op == BablOp.JMP || before.Op == BablOp.BRA) ? "otherwise" : "end_if";
                    }
                }
                r[t] = role;
            }
            return r;
        }

        /// <summary>Only the function's way out follows: no call, store, test or line before its end.</summary>
        static bool OnlyEnd(BablLayout lay, int i, int end)
        {
            for (; i < lay.Code.Count && lay.Code[i].Address < end; i++)
            {
                var ins = lay.Code[i];
                if (!ins.Known) return false;
                switch (ins.Op)
                {
                    case BablOp.CALL: case BablOp.CALLI: case BablOp.STO: case BablOp.SAY: case BablOp.RESPOND: case BablOp.EXIT:
                    case BablOp.BEQ: case BablOp.BNE: case BablOp.JMP: case BablOp.BRA:
                        return false;
                }
            }
            return true;
        }

        static readonly Regex questCopy = new Regex(@"^global(\d+) = get_quest\((\d+)\)$");
        static readonly Regex clockCopy = new Regex(@"^global(\d+) = x_clock\((\d+), 10001\)$");

        /// <summary>
        /// Globals whose use is plain: quest_N and xclock_N (only ever a copy of that quest or X
        /// clock), met_before (only ever set to 1, by end_conversation or the script, and read
        /// where the conversation starts).
        /// </summary>
        static SortedDictionary<int, string> GlobalRoles(Conversation c, BablLayout lay, DecompileResult d, Dictionary<int, FunctionNames> lib, int start)
        {
            var r = new SortedDictionary<int, string>();
            var lineAt = new Dictionary<int, string>();
            foreach (var l in d.Lines) if (l.Kind == PseudoKind.Statement && !lineAt.ContainsKey(l.Address)) lineAt[l.Address] = l.Text;
            var uses = d.Globals.Where(g => g.Kind == GlobalKind.Private && g.Via == "memory").GroupBy(g => g.Number);
            foreach (var g in uses.OrderBy(x => x.Key))
            {
                var writes = g.Where(u => u.Access == GlobalAccess.Write).ToList();
                var reads = g.Where(u => u.Access == GlobalAccess.Read).ToList();
                if (writes.Count == 0) continue;
                string name = null;
                {
                    var texts = writes.Select(w => { string t; return lineAt.TryGetValue(w.Address, out t) ? t : ""; }).ToList();
                    var q = texts.Select(t => questCopy.Match(t)).ToList();
                    var x = texts.Select(t => clockCopy.Match(t)).ToList();
                    if (q.All(m => m.Success) && q.Select(m => m.Groups[2].Value).Distinct().Count() == 1) name = "quest_" + q[0].Groups[2].Value;
                    else if (x.All(m => m.Success) && x.Select(m => m.Groups[2].Value).Distinct().Count() == 1) name = "xclock_" + x[0].Groups[2].Value;
                    else if (start >= 0 && writes.All(w => w.Value == 1 || w.Value == 0 && lay.FunctionOf(w.Address) == 0) && writes.Any(w => w.Value == 1)
                        && reads.Any(u => lay.FunctionOf(u.Address) == start || lay.FunctionOf(u.Address) == 0) && !r.ContainsValue("met_before"))
                        name = "met_before";
                }
                if (name != null) r[g.Key] = name;
            }
            return r;
        }
    }
}
