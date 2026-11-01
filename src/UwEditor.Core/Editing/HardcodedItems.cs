using System.Collections.Generic;
using System.Linq;
using UltimateEditor.Games;

namespace UwEditor.Core.Editing
{
    /// <summary>
    /// The items the games' own code names, so their behaviour comes with the id and does not
    /// follow a new name or picture: every ITEM_ constant of underworld-exhumed's include/items.h
    /// that a source file of uw1/src or uw2/src uses, with those files (made by a search of
    /// the reconstructed source, 2026-11). Code that tests a range of ids (a class, "id &gt;= x")
    /// is not in it; the class of an item still decides what kind of thing it is.
    /// </summary>
    public static class HardcodedItems
    {
        /// <summary>id → "NAME|file file…" (files without .C).</summary>
        static readonly Dictionary<int, string> Uw1 = new Dictionary<int, string>
        {
            { 0x00A, "SHINY_SWORD|obj/LOOK" },
            { 0x00F, "FIST|combat/COMBAT inv/INVDATA" },
            { 0x010, "SLING_STONE|inv/INVPANEL obj/USEITEMS" },
            { 0x012, "ARROW_12|inv/INVPANEL" },
            { 0x014, "FIREBALL|obj/EFFECT" },
            { 0x015, "LIGHTNING_BOLT|obj/EFFECT" },
            { 0x018, "SLING|inv/BAGS" },
            { 0x019, "BOW|combat/MISSILE inv/BAGS" },
            { 0x01A, "CROSSBOW|combat/MISSILE inv/BAGS" },
            { 0x01F, "JEWELED_BOW|inv/BAGS" },
            { 0x020, "LEATHER_VEST|combat/COMBAT" },
            { 0x023, "LEATHER_LEGGINGS|combat/COMBAT" },
            { 0x026, "LEATHER_GLOVES|combat/COMBAT" },
            { 0x029, "LEATHER_BOOTS|combat/COMBAT" },
            { 0x02C, "LEATHER_CAP|combat/COMBAT" },
            { 0x02F, "DRAGON_SKIN_BOOTS|game/PLAYDATA" },
            { 0x036, "IRON_RING|obj/LOOK" },
            { 0x037, "SHINY_SHIELD|obj/LOOK" },
            { 0x07F, "ADVENTURER|critter/PATHFIND event/TRIGGER game/PLAYDATA game/PLAYER motion/PHYSICS obj/USEITEMS" },
            { 0x08E, "BOWL|obj/COMBINE" },
            { 0x08F, "RUNE_BAG|inv/BAGS" },
            { 0x090, "LANTERN|obj/USEITEMS" },
            { 0x091, "TORCH|obj/USEITEMS" },
            { 0x092, "CANDLE|inv/INVPANEL obj/USEITEMS" },
            { 0x093, "TAPER|obj/LOOK obj/USEITEMS" },
            { 0x094, "LIT_LANTERN|obj/USEITEMS" },
            { 0x095, "LIT_TORCH|obj/USEITEMS" },
            { 0x097, "LIT_TAPER|obj/LOOK" },
            { 0x098, "WAND_98|obj/USEITEMS" },
            { 0x09B, "WAND_9B|obj/USEITEMS" },
            { 0x0A1, "GOLD_COIN|conv/BARTER" },
            { 0x0AE, "SHINY_CUP|obj/LOOK" },
            { 0x0B6, "FISH|event/WORLDEV obj/USEITEMS" },
            { 0x0B8, "MUSHROOM|obj/COMBINE obj/USEITEMS" },
            { 0x0B9, "TOADSTOOL|obj/USEITEMS" },
            { 0x0BA, "BOTTLE_OF_ALE|obj/USEITEMS" },
            { 0x0BB, "RED_POTION|obj/USEITEMS" },
            { 0x0BC, "GREEN_POTION|obj/USEITEMS" },
            { 0x0BD, "BOTTLE_OF_WATER|obj/USEITEMS" },
            { 0x0BE, "FLASK_OF_PORT|obj/COMBINE obj/USEITEMS" },
            { 0x0BF, "BOTTLE_OF_WINE|obj/LOOK obj/USEITEMS" },
            { 0x0C2, "SKULL_C2|game/SKILLS obj/LOOK obj/USEITEMS" },
            { 0x0C6, "PILE_OF_BONES_C6|obj/LOOK obj/USEITEMS" },
            { 0x0CC, "PIECE_OF_WOOD_CC|obj/USEITEMS" },
            { 0x0CD, "PIECE_OF_WOOD_CD|obj/USEITEMS" },
            { 0x0CE, "PLANT_CE|inv/INVPANEL obj/USEITEMS" },
            { 0x0CF, "PLANT_CF|inv/INVPANEL obj/USEITEMS" },
            { 0x0D5, "PILE_OF_DEBRIS_D5|combat/DAMAGE inv/INVDATA obj/USEITEMS" },
            { 0x0D6, "PILE_OF_DEBRIS_D6|combat/DAMAGE" },
            { 0x0D7, "ANVIL|obj/USEITEMS" },
            { 0x0D8, "POLE|obj/USEITEMS" },
            { 0x0D9, "DEAD_ROTWORM|inv/INVPANEL obj/COMBINE obj/USEITEMS" },
            { 0x0E0, "RUNESTONE|3d/DRAWOBJ" },
            { 0x0E1, "KEY_OF_TRUTH|game/SKILLS" },
            { 0x0E7, "KEY_OF_INFINITY|obj/USEITEMS" },
            { 0x0FD, "VAS_STONE|event/WORLDEV" },
            { 0x101, "LOCKPICK|obj/USEITEMS" },
            { 0x10F, "LOCK|obj/USEITEMS" },
            { 0x112, "ORB_ROCK|obj/USEITEMS" },
            { 0x114, "BOOK_114|obj/USEITEMS" },
            { 0x115, "BLOCK_OF_BURNING_INCENSE|obj/USEITEMS" },
            { 0x117, "ORB|obj/USEITEMS" },
            { 0x11B, "ROTWORM_STEW|inv/INVPANEL obj/COMBINE obj/USEITEMS" },
            { 0x11F, "STANDARD|obj/LOOK" },
            { 0x121, "BEDROLL|obj/USEITEMS" },
            { 0x122, "SILVER_SEED|obj/USEITEMS" },
            { 0x123, "MANDOLIN|obj/USEITEMS" },
            { 0x124, "FLUTE|obj/USEITEMS" },
            { 0x125, "LEECHES|inv/INVPANEL obj/USEITEMS" },
            { 0x126, "MOONSTONE|game/SKILLS inv/BAGS ui/INTERACT" },
            { 0x127, "SPIKE|obj/USEITEMS" },
            { 0x128, "ROCK_HAMMER|obj/USEITEMS" },
            { 0x129, "GLOWING_ROCK|obj/USEITEMS" },
            { 0x12B, "FISHING_POLE|obj/USEITEMS" },
            { 0x12D, "OIL_FLASK|obj/USEITEMS" },
            { 0x12E, "FOUNTAIN_12E|obj/USEITEMS" },
            { 0x12F, "CAULDRON|obj/USEITEMS" },
            { 0x136, "BOOK_136|obj/LOOK" },
            { 0x13B, "MAP|obj/LOOK obj/USEITEMS" },
            { 0x140, "DOOR_140|obj/USEITEMS" },
            { 0x147, "SECRET_DOOR_147|combat/DAMAGE obj/USEITEMS" },
            { 0x153, "LARGE_BOULDER_153|obj/USEITEMS" },
            { 0x154, "LARGE_BOULDER_154|combat/SPELLS" },
            { 0x155, "BOULDER|obj/USEITEMS" },
            { 0x156, "SMALL_BOULDER|obj/USEITEMS" },
            { 0x157, "SHRINE|conv/CONVERSE" },
            { 0x15A, "MOONGATE|game/SKILLS" },
            { 0x15B, "BARREL|combat/DAMAGE" },
            { 0x15D, "CHEST|combat/DAMAGE" },
            { 0x164, "BRIDGE|3d/GAMESORT ui/INTERACT" },
            { 0x165, "GRAVESTONE|obj/USEITEMS" },
            { 0x16E, "TMAP_C|3d/GAMESORT conv/CONVERSE obj/USEITEMS ui/INTERACT" },
            { 0x1A0, "MOVE_TRIGGER|event/WORLDEV motion/PHYSICS" },
            { 0x1C2, "EXPLOSION_1C2|combat/SPELLS motion/OBJPHYS obj/EFFECT" },
            { 0x1C5, "SPLASH_1C5|combat/SPELLS obj/EFFECT" },
            { 0x1CA, "SILVER_TREE|game/SKILLS ui/INTERACT" },
            { 0x1CB, "DAMAGE|combat/COMBAT" },
            { 0x1CF, "MOVING_DOOR|3d/GAMESORT combat/COMBAT obj/USEITEMS" },
        };

        static readonly Dictionary<int, string> Uw2 = new Dictionary<int, string>
        {
            { 0x000, "HAND_AXE|event/WORLDEV" },
            { 0x003, "DAGGER|combat/DAMAGE event/WORLDEV" },
            { 0x004, "SHORTSWORD|event/WORLDEV" },
            { 0x007, "CUDGEL|combat/COMBAT event/WORLDEV" },
            { 0x00A, "JEWELLED_DAGGER|combat/COMBAT combat/DAMAGE" },
            { 0x00F, "FIST|inv/INVDATA" },
            { 0x010, "SLING_STONE|event/WORLDEV inv/INVPANEL obj/USEITEMS" },
            { 0x012, "ARROW_12|inv/INVPANEL" },
            { 0x013, "SKULL_13|combat/SPELLS combat/SPELLS2 event/WORLDEV" },
            { 0x014, "FIREBALL_14|obj/EFFECT" },
            { 0x015, "LIGHTNING_BOLT|obj/EFFECT" },
            { 0x018, "SLING|combat/COMBAT inv/BAGS" },
            { 0x019, "BOW|combat/COMBAT inv/BAGS" },
            { 0x01A, "CROSSBOW|inv/BAGS" },
            { 0x01B, "HOMING_DART|critter/PATHFIND" },
            { 0x01D, "FIREBALL_1D|critter/CRITTIME motion/OBJPHYS obj/EFFECT" },
            { 0x01E, "SATELLITE|combat/SPELLS2 critter/PATHFIND motion/OBJPHYS" },
            { 0x01F, "JEWELED_BOW|inv/BAGS" },
            { 0x020, "LEATHER_VEST|combat/COMBAT" },
            { 0x023, "LEATHER_LEGGINGS|combat/COMBAT" },
            { 0x026, "LEATHER_GLOVES|combat/COMBAT event/WORLDEV" },
            { 0x029, "LEATHER_BOOTS|combat/COMBAT" },
            { 0x02C, "LEATHER_CAP|combat/COMBAT" },
            { 0x033, "FRAZNIUM_GAUNTLETS|event/WORLDEV" },
            { 0x034, "FRAZNIUM_CIRCLET|event/WORLDEV" },
            { 0x035, "GUARDIAN_SIGNET_RING|combat/SPELLS2 conv/BABLHACK" },
            { 0x03C, "WOODEN_SHIELD|event/WORLDEV" },
            { 0x040, "ROTWORM|event/WORLDEV" },
            { 0x04B, "IMP|combat/SPELLS2" },
            { 0x04E, "BLOODWORM|event/WORLDEV" },
            { 0x05C, "GREAT_TROLL|conv/BABLHACK" },
            { 0x05E, "HORDLING|combat/SPELLS2 event/WORLDEV" },
            { 0x064, "DESPOILER|combat/SPELLS2" },
            { 0x068, "DESTROYER|combat/SPELLS2" },
            { 0x075, "HUMAN_75|conv/BABLHACK" },
            { 0x078, "HUMAN_78|conv/BABLHACK" },
            { 0x07B, "HUMAN_7B|conv/BABLHACK" },
            { 0x07F, "ADVENTURER|critter/PATHFIND event/TRIGGER game/PLAYER motion/PHYSICS obj/USEITEMS" },
            { 0x08F, "RUNE_BAG|inv/BAGS" },
            { 0x090, "LANTERN|obj/USEITEMS" },
            { 0x091, "TORCH|event/WORLDEV obj/USEITEMS" },
            { 0x092, "CANDLE|obj/USEITEMS" },
            { 0x093, "LIGHT_SPHERE|event/WORLDEV obj/USEITEMS" },
            { 0x094, "LIT_LANTERN|obj/USEITEMS" },
            { 0x095, "LIT_TORCH|obj/USEITEMS" },
            { 0x097, "LIT_LIGHT_SPHERE|obj/TREASURE" },
            { 0x098, "WAND_98|obj/OBJUSE" },
            { 0x09B, "WAND_9B|event/WORLDEV obj/OBJUSE" },
            { 0x0A0, "COIN|combat/SPELLS2 conv/BARTER event/WORLDEV" },
            { 0x0A1, "STORAGE_CRYSTAL|inv/INVPANEL obj/OBJUSE" },
            { 0x0B0, "PIECE_OF_MEAT_B0|event/WORLDEV obj/USEITEMS" },
            { 0x0B6, "FISH|combat/SPELLS2 event/WORLDEV obj/USEITEMS" },
            { 0x0B9, "MUSHROOM|combat/SPELLS2 event/WORLDEV obj/USEITEMS" },
            { 0x0BA, "HONEYCOMB|obj/USEITEMS" },
            { 0x0BB, "BOTTLE_OF_ALE|event/WORLDEV inv/INVPANEL obj/USEITEMS" },
            { 0x0BC, "BOTTLE_OF_WATER|event/WORLDEV inv/INVPANEL obj/USEITEMS" },
            { 0x0BD, "BOTTLE_OF_WINE|inv/INVPANEL obj/USEITEMS" },
            { 0x0BE, "MEAT_ON_A_STICK|obj/USEITEMS" },
            { 0x0C1, "GRASS|event/WORLDEV" },
            { 0x0C2, "SKULL_C2|combat/SPELLS2 obj/LOOK obj/USEITEMS" },
            { 0x0C3, "SKULL_C3|event/WORLDEV" },
            { 0x0C5, "BONE_C5|inv/INVPANEL obj/USEITEMS" },
            { 0x0C6, "PILE_OF_BONES_C6|obj/LOOK obj/USEITEMS" },
            { 0x0C7, "BROKEN_DAGGER|combat/DAMAGE event/WORLDEV" },
            { 0x0CC, "PIECE_OF_WOOD_CC|obj/USEITEMS" },
            { 0x0CD, "PIECE_OF_WOOD_CD|obj/USEITEMS" },
            { 0x0CE, "PLANT_CE|inv/INVPANEL obj/USEITEMS" },
            { 0x0CF, "PLANT_CF|inv/INVPANEL obj/USEITEMS" },
            { 0x0D2, "LUMP_OF_WAX|inv/INVPANEL obj/USEITEMS" },
            { 0x0D6, "PILE_OF_DEBRIS_D6|combat/DAMAGE event/WORLDEV" },
            { 0x0D7, "ANVIL|obj/USEITEMS" },
            { 0x0D8, "POLE|obj/USEITEMS" },
            { 0x0D9, "PLANT_D9|event/WORLDEV" },
            { 0x0DA, "PLANT_DA|event/WORLDEV" },
            { 0x0DC, "PILE_OF_WOOD_CHIPS|combat/DAMAGE" },
            { 0x0DF, "BLOOD_STAIN_DF|game/SKILLS" },
            { 0x0E0, "RUNESTONE|3d/DRAWOBJ" },
            { 0x0E1, "BLACK_POTION|obj/USEITEMS" },
            { 0x0E2, "PURPLE_POTION|obj/USEITEMS" },
            { 0x0E3, "YELLOW_POTION|obj/USEITEMS" },
            { 0x0E4, "GREEN_POTION|event/WORLDEV obj/USEITEMS" },
            { 0x0E5, "RED_POTION|event/WORLDEV obj/USEITEMS" },
            { 0x0E6, "COLORLESS_POTION|obj/USEITEMS" },
            { 0x0E7, "BROWN_POTION|obj/LOOK obj/USEITEMS" },
            { 0x0F4, "MANI_STONE|event/WORLDEV" },
            { 0x101, "LOCKPICK|event/WORLDEV obj/OBJUSE" },
            { 0x10F, "LOCK|obj/OBJUSE" },
            { 0x110, "EYEBALL|obj/USEITEMS" },
            { 0x111, "HORN|obj/USEITEMS" },
            { 0x114, "PLANT_114|inv/INVPANEL obj/USEITEMS" },
            { 0x116, "BOTTLE_116|combat/DAMAGE obj/USEITEMS" },
            { 0x121, "BEDROLL|obj/USEITEMS" },
            { 0x123, "MANDOLIN|obj/USEITEMS" },
            { 0x124, "FLUTE|obj/USEITEMS" },
            { 0x125, "LEECHES|event/WORLDEV obj/USEITEMS" },
            { 0x126, "MOONSTONE|combat/SPELLS game/SKILLS inv/BAGS ui/INTERACT" },
            { 0x128, "ROCK_HAMMER|obj/USEITEMS" },
            { 0x129, "RESILIENT_SPHERE_129|combat/SPELLS2" },
            { 0x12B, "FISHING_POLE|obj/USEITEMS" },
            { 0x12D, "OIL_FLASK|obj/USEITEMS" },
            { 0x12E, "FOUNTAIN_12E|obj/OBJUSE obj/USEITEMS" },
            { 0x138, "BOOK_138|ui/INTERACT" },
            { 0x139, "BIT_OF_A_MAP|obj/LOOK obj/USEITEMS" },
            { 0x13A, "MAP|obj/LOOK obj/USEITEMS" },
            { 0x13D, "BOTTLE_13D|event/WORLDEV obj/USEITEMS" },
            { 0x13E, "STICK|inv/INVPANEL obj/USEITEMS" },
            { 0x13F, "RESILIENT_SPHERE_13F|critter/CRITTIME motion/OBJPHYS" },
            { 0x147, "SECRET_DOOR_147|combat/DAMAGE" },
            { 0x153, "LARGE_BOULDER_153|obj/USEITEMS" },
            { 0x154, "LARGE_BOULDER_154|combat/SPELLS2" },
            { 0x155, "BOULDER|obj/USEITEMS" },
            { 0x156, "SMALL_BOULDER|obj/USEITEMS" },
            { 0x158, "TABLE|3d/GAMESORT" },
            { 0x15B, "BARREL|combat/DAMAGE" },
            { 0x15D, "CHEST|combat/DAMAGE" },
            { 0x164, "BRIDGE|3d/GAMESORT event/TRIGGER ui/AUTOMAP ui/INTERACT" },
            { 0x165, "GRAVESTONE|obj/USEITEMS" },
            { 0x168, "LARGE_BLACKROCK_GEM|obj/USEITEMS" },
            { 0x16D, "FORCE_FIELD_16D|event/WORLDEV" },
            { 0x16E, "TMAP_C|3d/GAMESORT ui/INTERACT" },
            { 0x180, "DAMAGE_TRAP|event/WORLDEV" },
            { 0x183, "HACK_TRAP|event/WORLDEV" },
            { 0x19E, "FLAM_RUNE|combat/SPELLS combat/SPELLS2 obj/OBJUSE obj/USEITEMS" },
            { 0x19F, "TYM_RUNE|combat/SPELLS combat/SPELLS2 obj/OBJUSE" },
            { 0x1A0, "MOVE_TRIGGER_1A0|motion/PHYSICS" },
            { 0x1C2, "EXPLOSION_1C2|combat/SPELLS combat/SPELLS2 obj/EFFECT obj/USEITEMS" },
            { 0x1C5, "LIGHTNING_1C5|combat/SPELLS obj/EFFECT" },
            { 0x1CA, "FROST|combat/SPELLS ui/INTERACT" },
            { 0x1CB, "FLASH|combat/COMBAT" },
            { 0x1CD, "WISP|combat/SPELLS2 conv/CONVERSE ui/INTERACT" },
            { 0x1CF, "MOVING_DOOR|3d/GAMESORT combat/COMBAT obj/USEITEMS" },
        };

        /// <summary>The source's name for the item and the files whose code names it, or null.</summary>
        public static (string Name, string[] Files)? Of(GameKind kind, int item)
        {
            string v;
            if (!(kind == GameKind.UW2 ? Uw2 : Uw1).TryGetValue(item, out v)) return null;
            var p = v.Split('|');
            return (p[0], p[1].Split(' '));
        }

        /// <summary>What the files do, in a few words, for the report.</summary>
        public static string Describe(string file)
        {
            switch (file)
            {
                case "obj/USEITEMS": case "obj/OBJUSE": return "what happens when it is used";
                case "obj/LOOK": return "what looking at it says";
                case "obj/COMBINE": return "combining it with another item";
                case "obj/EFFECT": return "spell and animation effects";
                case "event/WORLDEV": return "scripted world events";
                case "event/TRIGGER": return "triggers";
                case "combat/COMBAT": case "combat/DAMAGE": case "combat/MISSILE": return "combat";
                case "combat/SPELLS": case "combat/SPELLS2": return "spells";
                case "inv/INVPANEL": case "inv/INVDATA": case "inv/BAGS": return "the inventory";
                case "conv/BARTER": case "conv/CONVERSE": case "conv/BABLHACK": return "conversations and barter";
                case "game/SKILLS": return "skills (tracking, lore)";
                case "game/PLAYDATA": case "game/PLAYER": return "the player";
                case "critter/PATHFIND": case "critter/CRITTIME": return "creatures";
                case "motion/PHYSICS": case "motion/OBJPHYS": return "movement";
                case "3d/DRAWOBJ": case "3d/GAMESORT": return "drawing";
                case "ui/INTERACT": case "ui/AUTOMAP": return "the interface";
                case "obj/TREASURE": return "treasure";
                default: return "";
            }
        }
    }
}
