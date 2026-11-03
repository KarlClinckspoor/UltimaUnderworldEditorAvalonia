using System;
using System.Collections.Generic;
using System.Linq;
using UwEditor.Core.Editing;

namespace UwEditor.Core.Sound
{
    /// <summary>What an effect number is: what plays it and where the call is.</summary>
    public sealed class EffectUse
    {
        public int Number;
        public string What;
        /// <summary>The source files of the calls (underworld-exhumed, uwN/src).</summary>
        public string Source;
    }

    /// <summary>
    /// The effect numbers of SOUNDS.DAT, from every play_effect, play_effect_here,
    /// play_effect_on_mobile(_src) call and the tables they read in underworld-exhumed's
    /// uw1/src and uw2/src (paths below are relative to those). Footsteps and death cries of
    /// creatures go by the creature's sound and death kinds (OBJECTS.DAT creature bytes 0x10
    /// and 8, critter/AI.C); ForCreatures names the creatures of each kind.
    /// </summary>
    public static class SoundEffectNames
    {
        static readonly EffectUse[] Uw1 =
        {
            E(0, "Swimming: the water sound, looped while the player is in water", "motion/PLAYMOVE.C make_noise"),
            E(1, "Footstep, one foot: the player walking; creatures of footstep kind 1", "motion/PLAYMOVE.C, critter/AI.C"),
            E(2, "Footstep, the other foot: as 1", "motion/PLAYMOVE.C, critter/AI.C"),
            E(3, "The player is hit", "combat/COMBAT.C"),
            E(4, "A creature is hit", "combat/COMBAT.C"),
            E(5, "Splash: a fall into water; creatures of footstep kind 3", "motion/MOTION.C, critter/AI.C"),
            E(6, "A creature dies (death kind 1)", "critter/AI.C crit_die"),
            E(7, "A blow on metal: the player's weapon hits a wall; a blow blocked by metal armour", "combat/COMBAT.C"),
            E(8, "A blow blocked by other armour", "combat/COMBAT.C"),
            E(9, "A bow or crossbow is fired", "combat/MISSILE.C"),
            E(10, "A miss; a missile is launched", "combat/COMBAT.C, combat/MISSILE.C"),
            E(11, "A door opens or closes", "obj/USEITEMS.C OpenDoor, CloseDoor"),
            E(12, "A door moving", "obj/EFFECT.C"),
            E(13, "Creatures of footstep kind 5", "critter/AI.C"),
            E(14, "Creatures of footstep kind 4", "critter/AI.C"),
            E(15, "A thud: a fall, a landing, an object hitting hard; no room to drop", "motion/MOTION.C, PHYSICS.C, OBJPHYS.C, combat/MISSILE.C"),
            E(16, "A spell is cast", "combat/RUNES.C player_cast"),
            E(17, "Clang: cutscene opcode 15", "gfx/CUTS.C cutsop_clang"),
            E(18, "The tremor spell", "combat/SPELLS.C"),
            E(19, "A button or switch flipped; a lock picked", "obj/USEITEMS.C UseRect, UseLockpickOn"),
            E(20, "A portcullis opens or closes", "obj/USEITEMS.C OpenDoor, CloseDoor"),
            E(21, "Not ready to cast again", "combat/RUNES.C, obj/USEITEMS.C checkSpell"),
            E(22, "The spell fails (fizzle)", "combat/RUNES.C fail_spell"),
            E(23, "Creatures of footstep kind 2", "critter/AI.C"),
        };

        static readonly EffectUse[] Uw2 =
        {
            E(0, "Swimming: the water sound, looped while the player is in water", "motion/PLAYMOVE.C make_noise"),
            E(1, "Footstep on the floor, one foot: the player; creatures of footstep kind 1 (as 0x5A); kept in the cache", "motion/PLAYMOVE.C step_sfx, critter/AI.C, game/UWEDIT.C"),
            E(2, "Footstep on the floor, the other foot: as 1 (0x5B)", "motion/PLAYMOVE.C step_sfx, critter/AI.C"),
            E(3, "The player is hit", "combat/COMBAT.C"),
            E(4, "A creature is hit (weapon kinds 0 and 1)", "combat/COMBAT.C"),
            E(5, "A big splash: something of mass 100 or more falls into water", "motion/MOTION.C"),
            E(6, "A creature dies (death kind 1 and unlisted kinds)", "critter/AI.C"),
            E(7, "A blow on metal: the player's weapon of kind 1 or 2 hits a wall; a blow blocked by metal", "combat/COMBAT.C"),
            E(8, "A blow on a wall with other weapons; a blow blocked otherwise", "combat/COMBAT.C"),
            E(9, "A bow is fired", "combat/MISSILE.C"),
            E(10, "A miss; an arrow, a creature's or a trap's missile, spell missiles 6 and 12 in flight", "combat/COMBAT.C, combat/MISSILE.C"),
            E(11, "A door opens or closes; kept in the cache", "obj/USEITEMS.C OpenDoor, CloseDoor, game/UWEDIT.C"),
            E(12, "A door moving", "obj/EFFECT.C"),
            E(13, "Creatures of footstep kind 5", "critter/AI.C"),
            E(14, "Creatures of footstep kind 4", "critter/AI.C"),
            E(15, "A thud: a fall, a landing, an object hitting hard", "motion/MOTION.C, PHYSICS.C, OBJPHYS.C"),
            E(16, "A spell is cast (the spells not listed under 40–44)", "combat/RUNES.C player_cast"),
            E(17, "No call found (UW1's cutscene clang; UW2's clang opcode is empty)", "gfx/CUTS.C cutsop_clang"),
            E(18, "The tremor spell; a Guardian magic marker cut; a key gem fused", "combat/SPELLS2.C, obj/USEITEMS.C"),
            E(19, "A switch flipped; a lock picked", "obj/OBJUSE.C flip_switch, obj/USEITEMS.C"),
            E(20, "A portcullis opens or closes", "obj/USEITEMS.C OpenDoor, CloseDoor"),
            E(21, "Not ready to cast again", "combat/RUNES.C, obj/OBJUSE.C"),
            E(22, "The spell fails (fizzle)", "combat/RUNES.C"),
            E(23, "Creatures of footstep kind 2", "critter/AI.C"),
            E(24, "A small splash: mass under 20 falls into water", "motion/MOTION.C"),
            E(25, "A splash: mass under 100 falls into water", "motion/MOTION.C"),
            E(26, "A swimming stroke", "motion/PLAYMOVE.C make_noise"),
            E(27, "A creature is hit with a weapon of kind 2", "combat/COMBAT.C"),
            E(28, "A thrown object in flight", "combat/MISSILE.C missile_fire"),
            E(29, "Footsteps on frictionless ice; creatures of footstep kind 7", "motion/PLAYMOVE.C step_sfx, critter/AI.C"),
            E(30, "Drinking a potion; eating something that harms", "obj/USEITEMS.C"),
            E(31, "Eating (fed)", "obj/USEITEMS.C"),
            E(32, "A light is lit", "obj/USEITEMS.C"),
            E(33, "Eating (hungry)", "obj/USEITEMS.C"),
            E(34, "A creature dies (death kind 2)", "critter/AI.C"),
            E(35, "A creature dies (death kind 3)", "critter/AI.C"),
            E(36, "A creature dies (death kind 4)", "critter/AI.C"),
            E(37, "Eating when nearly full", "obj/USEITEMS.C"),
            E(38, "A portcullis moving", "obj/EFFECT.C"),
            E(39, "Creatures of footstep kind 3", "critter/AI.C"),
            E(40, "A spell missile is launched; spells of minor 9", "combat/MISSILE.C spell_fire, combat/RUNES.C"),
            E(41, "Some spells: create (minor 5), minor 12", "combat/RUNES.C"),
            E(42, "Spells of classes 0 and 3 and most area spells; a Guardian marker cut; a key gem fused", "combat/RUNES.C, combat/SPELLS2.C, obj/USEITEMS.C"),
            E(43, "Area spells", "combat/RUNES.C"),
            E(44, "Spells of class 2; the last key gem fused", "combat/RUNES.C, obj/USEITEMS.C"),
            E(45, "Refused: no room to fire or drop; a locked door", "combat/MISSILE.C, obj/USEITEMS.C"),
            E(46, "No call found", ""),
            E(47, "Footstep on ice, one foot; creatures of footstep kind 6", "motion/PLAYMOVE.C step_sfx, critter/AI.C"),
            E(48, "Footstep on ice, the other foot; creatures of footstep kind 6", "motion/PLAYMOVE.C step_sfx, critter/AI.C"),
        };

        static EffectUse E(int n, string what, string src) { return new EffectUse { Number = n, What = what, Source = src }; }

        /// <summary>The game's table of effect uses.</summary>
        public static IReadOnlyList<EffectUse> Table(bool uw2) { return uw2 ? Uw2 : Uw1; }

        /// <summary>What effect n is; trap sounds (100 and up, UW2) too.</summary>
        public static EffectUse Get(bool uw2, int n)
        {
            if (uw2 && n >= 100) return new EffectUse { Number = n, What = string.Format("Played by special-effects traps (0x184) of quality 2 and owner {0}", n - 100), Source = "event/WORLDEV.C do_sfx" };
            var t = Table(uw2);
            return n >= 0 && n < t.Count ? t[n] : new EffectUse { Number = n, What = "No call found", Source = "" };
        }

        /// <summary>The footstep effects each creature footstep kind plays (critter/AI.C).</summary>
        static int[] StepEffects(bool uw2, int kind)
        {
            switch (kind)
            {
                case 1: return new[] { 1, 2 };
                case 2: return new[] { 0x17 };
                case 3: return new[] { uw2 ? 0x27 : 5 };
                case 4: return new[] { 0xE };
                case 5: return new[] { 0xD };
                case 6: return uw2 ? new[] { 0x2F, 0x30 } : new int[0];
                case 7: return uw2 ? new[] { 0x1D } : new int[0];
            }
            return new int[0];
        }

        /// <summary>The effect a creature death kind plays (critter/AI.C; UW1 only kind 1).</summary>
        static int DeathEffect(bool uw2, int kind)
        {
            if (kind == 0) return -1;
            if (!uw2) return kind == 1 ? 6 : -1;
            switch (kind) { case 2: return 0x22; case 3: return 0x23; case 4: return 0x24; default: return 6; }
        }

        /// <summary>
        /// The creatures whose footsteps or death cry are effect n, by name, from OBJECTS.DAT
        /// (empty when it cannot be read).
        /// </summary>
        public static List<string> ForCreatures(GameSession s, ObjectTables tables, int n)
        {
            var names = new List<string>();
            if (tables == null) return names;
            try
            {
                var t = ObjectTables.Table("Creatures");
                for (int row = 0; row < t.Count - 1; row++)   // the last row is the player's
                {
                    int item = t.ItemOf(row);
                    string name = s.Names.Get(item);
                    if (string.IsNullOrEmpty(name)) continue;
                    int sound = tables.Get(t, row, t.Schema["Sound"]), death = tables.Get(t, row, t.Schema["Death"]);
                    var why = new List<string>();
                    if (StepEffects(s.IsUw2, sound).Contains(n)) why.Add("steps");
                    if (DeathEffect(s.IsUw2, death) == n) why.Add("death");
                    if (why.Count > 0) names.Add(name + " (" + string.Join(", ", why) + ")");
                }
            }
            catch (Exception) { }
            return names.Distinct().ToList();
        }
    }
}
