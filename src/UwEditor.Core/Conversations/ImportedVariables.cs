using System;
using System.Collections.Generic;
using UltimateEditor.Games;

namespace UwEditor.Core.Conversations
{
    /// <summary>
    /// The variables the game copies into a conversation before it runs and back out after
    /// (conv/CONVVARS.C: bab_var at the start, bab_var_out at the end). An imported variable
    /// with another name is the script's own, kept with the private globals in BGLOBALS.DAT.
    /// </summary>
    public static class ImportedVariables
    {
        public sealed class Info
        {
            public string Name;
            public string Description;
            /// <summary>Copied back into the game when the conversation ends.</summary>
            public bool WrittenBack;
        }

        public static readonly Info[] All =
        {
            V("npc_whoami", "the talker's conversation slot (whoami)"),
            V("npc_hunger", "the talker's hunger", true),
            V("npc_health", "the talker's health"),
            V("npc_hp", "the talker's hit points", true),
            V("npc_arms", "the talker's arms"),
            V("npc_power", "the talker's power"),
            V("npc_goal", "the talker's goal (what it does next)", true),
            V("npc_gtarg", "the talker's goal target", true),
            V("npc_talkedto", "1 once the talker has been spoken to"),
            V("npc_level", "the talker's level"),
            V("npc_xhome", "the talker's home tile, x", true),
            V("npc_yhome", "the talker's home tile, y", true),
            V("npc_name", "the talker's name (a string)"),
            V("npc_attitude", "the talker's attitude (0 hostile .. 3 friendly)", true),
            V("play_hunger", "the player's hunger", true),
            V("play_health", "the player's health"),
            V("play_hp", "the player's hit points", true),
            V("play_arms", "the player's arms"),
            V("play_power", "the player's power"),
            V("play_mana", "the player's mana", true),
            V("play_level", "the player's level"),
            V("play_sex", "the player's sex"),
            V("play_poison", "the player's poisoning", true),
            V("play_drawn", "whether the player has a weapon drawn"),
            V("play_name", "the player's name (a string)"),
            V("dungeon_level", "the level the conversation happens on"),
            V("game_time", "the time of day"),
            V("game_mins", "the minutes of the game clock"),
            V("game_days", "the days of the game clock"),
            V("new_player_exp", "experience to give the player", true),
        };

        static Info V(string n, string d, bool back = false) { return new Info { Name = n, Description = d, WrittenBack = back }; }

        static readonly Dictionary<string, Info> byName = Index();
        static Dictionary<string, Info> Index()
        {
            var d = new Dictionary<string, Info>(StringComparer.Ordinal);
            foreach (var i in All) d[i.Name] = i;
            return d;
        }

        public static Info Find(string name)
        {
            Info i;
            return name != null && byName.TryGetValue(name, out i) ? i : null;
        }

        /// <summary>Whether the game fills this import in (both games bind the same names).</summary>
        public static bool IsBound(string name, GameKind kind) { return Find(name) != null; }
    }
}
