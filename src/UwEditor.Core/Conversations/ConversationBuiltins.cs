using System;
using System.Collections.Generic;
using System.Linq;

namespace UwEditor.Core.Conversations
{
    /// <summary>
    /// What each built-in function of the conversation machine does, in a line, for the
    /// Dialogue tab's hovers: read from the game's code (uw1 and uw2 conv/CONVERSE.C, the
    /// bab_fun table at Converse; conv/BABL.C, the string built-ins; conv/BABLHACK.C and
    /// conv/BARTER.C). Arguments are named in the order a call writes them: the first written is the game's highest-numbered (arg3 of set_race_attitude is the race), as the language lists arguments.
    /// </summary>
    public static class ConversationBuiltins
    {
        static readonly Dictionary<string, string> about = new Dictionary<string, string>(StringComparer.Ordinal)
        {
            // conv/CONVERSE.C
            { "babl_menu", "babl_menu(choices): shows a 0-terminated array of string numbers as a numbered menu and returns the number chosen, from 1 (CONVERSE.C conv_choice). The menu statement calls it." },
            { "babl_fmenu", "babl_fmenu(flags, choices): the same, showing only the choices whose flag is not 0, and returning the chosen string's number (CONVERSE.C conv_fmenu). The fmenu statement calls it." },
            { "say", "say(text): the talker's line, in the talker's scroll (CONVERSE.C npc_say). The say statement." },
            { "respond", "respond(text): a line in the player's scroll (CONVERSE.C play_respond). The respond statement." },
            { "print", "print(text): narration, in its own colour (CONVERSE.C conv_print)." },
            { "pause", "pause(n): waits n half-seconds (CONVERSE.C conv_pause)." },
            { "babl_ask", "babl_ask(): lets the player type up to 50 characters; returns the number of a string holding them (CONVERSE.C getInputText)." },
            { "show_inv", "show_inv(item_ids, indices): fills the two arrays (4 entries) with the player's selected trade items; returns how many (CONVERSE.C conv_check_inv)." },
            { "find_barter", "find_barter(item): the object index of the player's selected trade item of that type (1000 and up: a class), else 0 (CONVERSE.C)." },
            { "find_barter_total", "find_barter_total(item, count, indices, total): the player's selected trade items of a type and their total quantity; 1 when there are any (CONVERSE.C)." },
            { "give_to_npc", "give_to_npc(count, indices): gives the listed objects to the talker if every one is among the player's selected trade items; 1 if given (CONVERSE.C conv_give_inv)." },
            { "give_ptr_npc", "give_ptr_npc(object, quantity): gives the talker an object, from the trade slots or quantity of it from the player's inventory (CONVERSE.C)." },
            { "find_inv", "find_inv(item, whose): finds an item (1000 and up: a class) in the talker's or the player's inventory, containers too; returns its index or 0 (BARTER.C npc_barter_find)." },
            { "take_from_npc", "take_from_npc(item): the talker hands over an item of that type (1000 and up: a class): into the player's hand, a free trade slot, or at its feet (BARTER.C npc_barter_give)." },
            { "take_id_from_npc", "take_id_from_npc(object): the same for one object, by its index (BARTER.C npc_barter_give_id)." },
            { "do_inv_create", "do_inv_create(item): creates an item (quality 63) in the talker's inventory and returns its index (BARTER.C npc_inv_create)." },
            { "do_inv_delete", "do_inv_delete(item): removes the first object of that type from the talker's inventory; 1 if there was one (BARTER.C npc_inv_delete)." },
            { "identify_inv", "identify_inv(object, with_article, name, identified): the object's name as a new string stored in name, and its value to the talker returned (CONVERSE.C conv_inv_name)." },
            { "count_inv", "count_inv(object): how many of an object: its quantity, or 1 (CONVERSE.C)." },
            { "check_inv_quality", "check_inv_quality(object): reads the object's quality but does not return it; the script gets whatever was left in the register (CONVERSE.C)." },
            { "set_inv_quality", "set_inv_quality(object, quality): sets an object's quality, to 6 bits (CONVERSE.C)." },
            { "switch_pic", "switch_pic(n): shows another portrait and name for the talker: whoami n below 0x100, else generic head n - 0x100 (UW2, CONVERSE.C)." },
            // conv/BARTER.C
            { "setup_to_barter", "setup_to_barter(): fills the talker's trade slots from its inventory (BARTER.C)." },
            { "do_offer", "do_offer(..., accepted, not_enough, worse, out_of_patience, nothing_offered): the player offers the selected items for the talker's; the last five arguments are the strings said. Returns 1 when the trade is made (BARTER.C)." },
            { "do_demand", "do_demand(given_up, refused): the player demands the talker's selected items for nothing; the arguments are the strings said. Returns 1 when it gives in (BARTER.C)." },
            { "do_decline", "do_decline(): the trade is declined; the talker's slot items go back (BARTER.C)." },
            { "do_judgement", "do_judgement(): the player's appraisal of the offer, from \"a terrible deal\" to \"an excellent deal\" (BARTER.C)." },
            { "end_barter", "end_barter(): gives back what is still in the trade slots (BARTER.C)." },
            { "set_likes_dislikes", "set_likes_dislikes(likes, dislikes): points at the talker's lists of liked and disliked items, two arrays ending in -1 (BARTER.C npc_likes_dislikes)." },
            { "give_all_stuff", "give_all_stuff(): the talker gives the player everything in its trade slots, as after an accepted offer (UW2, BARTER.C)." },
            // conv/BABLHACK.C
            { "get_quest", "get_quest(quest): a quest flag's value (BABLHACK.C)." },
            { "set_quest", "set_quest(quest, value): sets a quest flag (BABLHACK.C)." },
            { "sex", "sex(male_text, female_text): the first string for a male player, the second for a female one (BABLHACK.C)." },
            { "x_skills", "x_skills(skill, value): 0 to 30 sets a skill, anything else only reads it; returns the skill's value (BABLHACK.C)." },
            { "x_traps", "x_traps(variable, value): sets a game variable when the value is 0..63 (UW2: 0..0x3FF), else only reads it; returns its value (BABLHACK.C)." },
            { "x_clock", "x_clock(clock, value): with a value above 0x100 reads an X clock, else sets it (UW2, BABLHACK.C)." },
            { "x_exp", "x_exp(points): gives the player experience; returns the new total / 16 (UW2, BABLHACK.C)." },
            { "set_attitude", "set_attitude(whoami, attitude): the first active creature with that whoami takes the attitude (BABLHACK.C)." },
            { "set_race_attitude", "set_race_attitude(race, attitude, range): sets the attitude of the talker's kind of that race within range tiles of its home (BABLHACK.C)." },
            { "gronk_door", "gronk_door(x, y, how): opens (0), closes (1) or toggles (2) the door at a tile; 0 when there is none (BABLHACK.C)." },
            { "take_from_npc_inv", "take_from_npc_inv(n): the object index n links into the talker's inventory (0 the first), or 0; nothing is moved (BABLHACK.C)." },
            { "add_to_npc_inv", "add_to_npc_inv(object): appends an object to the talker's inventory (BABLHACK.C)." },
            { "place_object", "place_object(object, x, y): takes an object from the talker and puts it at a tile, or at the player's feet for a negative x (BABLHACK.C)." },
            { "remove_talker", "remove_talker(): takes the talker off the map (BABLHACK.C)." },
            { "transform_talker", "transform_talker(a, b, c, d): transforms the talker into another creature (UW2, BABLHACK.C)." },
            { "set_sequence", "set_sequence(whoami, sequence, frame): sets the animation of every creature with that whoami (UW2, BABLHACK.C)." },
            { "teleport_player", "teleport_player(x, y, level): where the player goes when the conversation is over (UW2, BABLHACK.C)." },
            { "teleport_talker", "teleport_talker(x, y): where the talker goes when the conversation is over (UW2, BABLHACK.C)." },
            { "x_obj_stuff", "x_obj_stuff(object, set, heading, owner, flags, link, flag10, flag9, quality): with set not 0 writes an object's fields from the variables, else reads them (-1 leaves a field alone) (BABLHACK.C)." },
            { "x_obj_pos", "x_obj_pos(object, mode, x, y, z): mode 1 sets an object's position and height, else reads them (BABLHACK.C)." },
            { "babl_hack", "babl_hack(what, ...): UW2's special actions, chosen by the first argument (the arena's fighters among them) (UW2, BABLHACK.C)." },
            { "do_input_wait", "do_input_wait(): waits for a key or a click (UW2)." },
            // conv/BABL.C
            { "compare", "compare(a, b): 1 when two strings are equal after @-substitution, ignoring case (BABL.C bab_compare)." },
            { "random", "random(n): a number from 1 to n (BABL.C BabRand)." },
            { "plural", "plural(count, one_text, plural_text): the second argument's string when the count is 1 or less, else the third (BABL.C babPluralize)." },
            { "contains", "contains(word, text): 1 when the text holds the word as a whole word (BABL.C StringContains)." },
            { "append", "append(a, b): a new string, a followed by b (BABL.C babl_str_append)." },
            { "copy", "copy(text): a new string holding a copy of the text (BABL.C STRING_COPY)." },
            { "find", "find(array, count, value): the 1-based position of a value among the first count words of an array, or 0 (BABL.C conv_find)." },
            { "length", "length(text): a string's length (BABL.C conv_length)." },
            { "val", "val(text): the number a string holds (BABL.C DoVal)." },
        };

        /// <summary>What a built-in function does, or null.</summary>
        public static string Describe(string name)
        {
            string s;
            return name != null && about.TryGetValue(name, out s) ? s : null;
        }

        /// <summary>What an imported variable holds (ImportedVariables), or null.</summary>
        public static string DescribeVariable(string name)
        {
            var v = ImportedVariables.All.FirstOrDefault(x => x.Name == name);
            return v == null ? null : name + ": " + v.Description + (v.WrittenBack ? " (written back into the game when the conversation ends)" : "") + ".";
        }

        /// <summary>The built-in functions described.</summary>
        public static IEnumerable<string> Names { get { return about.Keys; } }
    }
}
