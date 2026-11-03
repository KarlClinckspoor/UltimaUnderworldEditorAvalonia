using System;
using System.Collections.Generic;
using System.Linq;

namespace UwEditor.Core.Randomizer
{
    /// <summary>An object of the levels that gives the player an ability, and how.</summary>
    public sealed class AbilitySource
    {
        public int Level, Index, Item;
        public string Name;
        /// <summary>levitate, jump, waterwalk, lava, reveal, open, telekinesis, light, gate_travel.</summary>
        public string Ability;
        /// <summary>worn (an enchantment that works while worn), used (cast on use, no charges), scroll, potion, wand (charged), boots (dragon skin).</summary>
        public string Kind;
        /// <summary>Worn or used without limit; false for scrolls, potions and charged wands.</summary>
        public bool Permanent;
        /// <summary>Works while worn (FixPlayerEquips), so also on no-magic squares.</summary>
        public bool Worn;
        /// <summary>The spell or effect, by its game name.</summary>
        public string Spell;
        /// <summary>A charged wand's charges (its spell object's quality), else -1.</summary>
        public int Charges = -1;
        public int TileX, TileY;
        public int Zone;
        public string Holder;
        /// <summary>The token a placement grants: worn:X, perm:X or once:X.</summary>
        public string Token { get { return (Worn ? "worn:" : Permanent ? "perm:" : "once:") + Ability; } }
        public override string ToString()
        {
            return string.Format("{0}: #{1} {2} ({3}{4}){5}, L{6} ({7}, {8})", Token, Index, Name, Spell, Charges >= 0 ? ", " + Charges + " charges" : "", Holder != null ? " in " + Holder : "", Level, TileX, TileY);
        }
    }

    /// <summary>
    /// The player's abilities that the zones' links ask for, the spells and enchantments that
    /// give them, and the objects of the levels that carry them (Phase B of the automatic zones).
    /// UW1 only for now: UW2's spell table differs (its own SPELLS.C), and AbilityOfSpell returns
    /// null there.
    /// </summary>
    public static class Abilities
    {
        /// <summary>The abilities, as the tokens' names (spell:levitate, worn:lava ...). Fly counts as levitate (Requirement.Normalize).</summary>
        public static readonly string[] Names = { "levitate", "jump", "waterwalk", "lava", "reveal", "open", "telekinesis", "light", "gate_travel" };

        public static readonly string[] RuneNames = "An Bet Corp Des Ex Flam Grav Hur In Jux Kal Lor Mani Nox Ort Por Quas Rel Sanct Tym Uus Vas Wis Ylem".Split(' ');

        /// <summary>
        /// UW1's 53 spells (combat/SPELLS.C spells[]): the class byte (class &lt;&lt; 3), the three runes
        /// packed five bits each, first rune highest (0x18 none), and the minor (top two bits the
        /// stability or the area mode). The index is the one objects carry (block 6 0x100 + index).
        /// </summary>
        static readonly int[,] Uw1Spells =
        {
            { 0x00, 0x2178, 0x83 }, { 0x10, 0x0512, 0x02 }, { 0x29, 0x3938, 0x01 },
            { 0x40, 0x2197, 0x01 }, { 0x18, 0x48F8, 0x02 }, { 0x08, 0x51F8, 0x01 },
            { 0x18, 0x0258, 0x01 }, { 0x08, 0x446F, 0x02 }, { 0x20, 0x202C, 0x02 },
            { 0x58, 0x5998, 0x01 }, { 0x38, 0x4058, 0x01 }, { 0x40, 0x2138, 0x03 },
            { 0x58, 0x466F, 0x00 }, { 0x18, 0x064B, 0x03 }, { 0x00, 0x4178, 0x85 },
            { 0x29, 0x38D8, 0x02 }, { 0x5A, 0x4938, 0x02 }, { 0x10, 0x2258, 0x43 },
            { 0x08, 0x5DF8, 0x44 }, { 0x20, 0x2198, 0x04 }, { 0x08, 0x1DF8, 0x03 },
            { 0x38, 0x3598, 0x04 }, { 0x18, 0x48B8, 0x46 }, { 0x5A, 0x0138, 0x03 },
            { 0x29, 0x3CB8, 0x03 }, { 0x38, 0x004C, 0x02 }, { 0x5A, 0x3AD7, 0x04 },
            { 0x18, 0x1A4F, 0x05 }, { 0x5A, 0x12F8, 0x05 }, { 0x58, 0x01B8, 0x06 },
            { 0x20, 0x550C, 0x0F }, { 0x30, 0x55C6, 0x42 }, { 0x58, 0x562F, 0x0A },
            { 0x38, 0x008F, 0x05 }, { 0x00, 0x550B, 0x86 }, { 0x5A, 0x39F7, 0x08 },
            { 0x08, 0x54EF, 0x05 }, { 0x38, 0x2191, 0x03 }, { 0x40, 0x2998, 0x04 },
            { 0x18, 0x564B, 0x44 }, { 0x30, 0x5416, 0x03 }, { 0x30, 0x3810, 0x81 },
            { 0x10, 0x22B2, 0x45 }, { 0x58, 0x55F7, 0x09 }, { 0x58, 0x39F6, 0x07 },
            { 0x30, 0x14F8, 0x44 }, { 0x58, 0x0278, 0x0B }, { 0x58, 0x5542, 0x0C },
            { 0x30, 0x6318, 0x05 }, { 0x28, 0x6318, 0x04 }, { 0x58, 0x6318, 0x0D },
            { 0x50, 0x6318, 0x03 }, { 0x50, 0x6318, 0x09 },
        };

        public static int Uw1SpellCount { get { return Uw1Spells.GetLength(0); } }

        /// <summary>A UW1 spell's class (combat.h SPELL_CLASS) and minor (SPELL_MINOR).</summary>
        public static void Uw1Spell(int spell, out int cls, out int minor)
        {
            cls = (Uw1Spells[spell, 0] & 0xF8) >> 3;
            minor = Uw1Spells[spell, 2] & 0x3F;
        }

        /// <summary>A UW1 spell's runes (0 An .. 23 Ylem), none for the five spells without runes.</summary>
        public static List<int> Uw1Runes(int spell)
        {
            var list = new List<int>();
            int r = Uw1Spells[spell, 1];
            foreach (int sh in new[] { 10, 5, 0 })
            {
                int k = (r >> sh) & 31;
                if (k < 24) list.Add(k);
            }
            return list;
        }

        /// <summary>
        /// The ability an active effect gives (game/PLAYDATA.C player_affected_by): class 11 minor 3,
        /// telekinesis (PickDist 0); class 1, the
        /// motion bits (minor 1 leap, 3 levitate, 4 water walk, 5 fly; 2 slow fall and 6 bounce gate
        /// nothing here); class 3 minor 6, fire resistance (damage_protection_flags 0x08, which
        /// combat/DAMAGE.C check_res uses to cancel the lava's fire damage, motion/PLAYMOVE.C
        /// parse_effect); class 0, light.
        /// </summary>
        public static string OfEffect(int cls, int minor)
        {
            if (cls == 1) return minor == 1 ? "jump" : minor == 3 || minor == 5 ? "levitate" : minor == 4 ? "waterwalk" : null;
            if (cls == 3 && minor == 6) return "lava";
            if (cls == 11 && minor == 3) return "telekinesis";     // PickDist 0: use and take from any distance (ui/INTERACT.C InPickRange, BlockingTerrain)
            if (cls == 0 && minor > 0) return "light";
            return null;
        }

        /// <summary>The ability a spell gives when cast or used: its effect, Reveal (class 6 minor 1, combat/SPELLS.C sp_true_sight) or Open (class 11 minor 5, obj_spells).</summary>
        public static string OfSpell(bool uw2, int spell)
        {
            if (uw2 || spell < 0 || spell >= Uw1SpellCount) return null;
            int cls, minor;
            Uw1Spell(spell, out cls, out minor);
            if (cls == 6 && minor == 1) return "reveal";
            // class 11 spells are xt_spells' subs, not effects: 5 Open (obj_spells picks the lock with skill 45),
            // 8 Telekinesis (set_curmagic(SPELLC_XT, 3), the PickDist 0 effect), 10 Gate Travel (to player->moonstone,
            // game/SKILLS.C do_mstone; Phase D: so its scrolls are found wherever they lie); combat/SPELLS.C xt_spells
            if (cls == 11) return minor == 5 ? "open" : minor == 8 ? "telekinesis" : minor == 10 ? "gate_travel" : null;
            return OfEffect(cls, minor);
        }

        /// <summary>The UW1 spells with runes that give an ability.</summary>
        public static List<int> SpellsGiving(bool uw2, string ability)
        {
            var list = new List<int>();
            if (uw2) return list;
            for (int i = 0; i < Uw1SpellCount; i++)
                if (OfSpell(false, i) == ability && Uw1Runes(i).Count > 0) list.Add(i);
            return list;
        }

        /// <summary>The token of a runestone, as KeyItems names it (rune:ort).</summary>
        public static string RuneToken(GameSession s, int rune)
        {
            string n = s.Names.Get(0xE8 + rune) ?? (RuneNames[rune] + " stone");
            return "rune:" + Slug(n.Replace(" stone", ""));
        }

        /// <summary>What casting a spell from runes needs: the rune bag (runes count only once dropped in it, inv/BAGS.C, combat/RUNES.C add_rune) and each rune.</summary>
        public static string CastRequirement(GameSession s, int spell)
        {
            var terms = new List<string> { "item:rune_bag" };
            foreach (int r in Uw1Runes(spell)) terms.Add(RuneToken(s, r));
            return string.Join(" & ", terms.Distinct());
        }

        static string Slug(string t) { return new string((t ?? "").ToLowerInvariant().Select(c => char.IsLetterOrDigit(c) ? c : '_').ToArray()).Trim('_'); }

        static bool Flag11(ObjectInfo o) { return o.Record != null && o.Record.Length > 1 && ((o.Record[1] >> 3) & 1) != 0; }

        /// <summary>Armour, rings and shields (0x20-0x3F): what the armour and ring slots and the shield hand hold (inv/INVDATA.C ObjWorn).</summary>
        static bool Wearable(int item) { return item >= 0x20 && item < 0x40; }

        /// <summary>
        /// Every object of the levels that gives an ability, read as the game reads it:
        /// <list type="bullet">
        /// <item>a spell (ID_FLAG11) is cast when the object is used (obj/USEITEMS.C UseObj, checkSpell,
        /// decode_obj_spell): a scroll is used up (UseBook), a potion or other food too (UseFood), a
        /// wand's spell object loses a charge each time (useNSpellCharges), while an object enchanted
        /// directly (is_quant) casts without end;</item>
        /// <item>an effect without ID_FLAG11 works while the object is worn (game/PLAYDATA.C
        /// FixPlayerEquips, player_affected_by), in the armour or ring slots or as a shield;</item>
        /// <item>dragon skin boots (0x2F) worn keep lava from hurting (FixPlayerEquips,
        /// motion/PLAYMOVE.C parse_effect).</item>
        /// </list>
        /// </summary>
        public static List<AbilitySource> Find(GameSession s, Func<int, LevelModel> level, ZoneSet zs)
        {
            var list = new List<AbilitySource>();
            if (s.IsUw2) return list;
            foreach (var slot in s.LevelSlots())
            {
                if (!slot.Present) continue;
                LevelModel m;
                try { m = level(slot.Level); } catch (Exception) { continue; }
                if (m == null || !m.Present) continue;
                foreach (var o in m.Objects)
                {
                    if (o.Free || o.TileX < 0 || o.IsTrapOrTrigger) continue;
                    foreach (var src in Classify(s, m, o))
                    {
                        src.Level = slot.Level; src.Index = o.Index; src.Item = o.Item; src.TileX = o.TileX; src.TileY = o.TileY;
                        src.Name = o.Name;
                        src.Zone = zs == null ? 0 : zs.NearestZone(slot.Level, o.TileX, o.TileY);
                        if (o.Parent >= 0) src.Holder = m.Objects[o.Parent].ToString();
                        list.Add(src);
                    }
                }
            }
            return list;
        }

        static IEnumerable<AbilitySource> Classify(GameSession s, LevelModel m, ObjectInfo o)
        {
            int item = o.Item;
            if (item >= 0x140 && item < 0x180) yield break;        // MAJOR_RECT: decode_obj_spell skips it
            // a spell object (0x120, MAJOR_SPEC minor 2 class 0) inside a non-quantity object: a charged spell
            if (!o.IsQuantity && o.Link > 0)
            {
                foreach (int c in o.Children)
                {
                    var sp = m.Objects[c];
                    if (sp.Free || sp.Item != 0x120) continue;
                    int link = sp.Link & 0x1FF;
                    if (Flag11(sp))
                    {
                        if ((link >> 6) != 0) break;
                        string ab = OfSpell(false, link & 0x3F);
                        if (ab != null) yield return new AbilitySource { Ability = ab, Kind = item >= 0x98 && item <= 0x9B ? "wand" : "charged", Permanent = false, Spell = SpellTables.Name(s, link & 0x3F), Charges = sp.Quality };
                    }
                    else if (Wearable(item))
                    {
                        string ab = OfEffect(link >> 4, link & 0xF);
                        if (ab != null) yield return new AbilitySource { Ability = ab, Kind = "worn", Permanent = true, Worn = true, Spell = (s.Strings.Get(6, link) ?? "").Trim() };
                    }
                    break;
                }
            }
            else if (o.IsQuantity && o.Enchanted && o.Link >= 0x200)
            {
                int link = o.Link & 0x1FF;
                if (Flag11(o))
                {
                    if ((link >> 6) == 0 && item != 0x13B)      // the map opens the map screen instead (UseBook)
                    {
                        int spell = link & 0x3F;
                        string ab = OfSpell(false, spell);
                        if (ab != null)
                        {
                            string kind = item >= 0x130 && item < 0x140 ? "scroll" : item >= 0xB0 && item < 0xC0 ? "potion" : "used";
                            yield return new AbilitySource { Ability = ab, Kind = kind, Permanent = kind == "used", Spell = SpellTables.Name(s, spell) };
                        }
                    }
                }
                else if (Wearable(item))
                {
                    string ab = OfEffect(link >> 4, link & 0xF);
                    if (ab != null) yield return new AbilitySource { Ability = ab, Kind = "worn", Permanent = true, Worn = true, Spell = (s.Strings.Get(6, link) ?? "").Trim() };
                    yield break;
                }
            }
            if (item == 0x2F && !(o.IsQuantity && o.Enchanted && o.Link >= 0x200 && !Flag11(o)))
                yield return new AbilitySource { Ability = "lava", Kind = "boots", Permanent = true, Worn = true, Spell = "dragon skin boots" };
        }
    }
}
