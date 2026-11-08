using System;
using System.Collections.Generic;
using System.Linq;
using UltimateEditor.Games;
using UwEditor.Core.Editing;

namespace UwEditor.Core.Balance
{
    /// <summary>The blow a creature lands on the player, for the damage armour saves.</summary>
    public sealed class BalanceBlow
    {
        /// <summary>The attack's to-hit (creature attack to-hit, plus equip / 2).</summary>
        public int ToHit = 10;
        /// <summary>The attack's damage plus the creature's Strength / 5.</summary>
        public int Damage = 12;
    }

    /// <summary>One line of the armour overview: a wearable item, plain or with one enchantment.</summary>
    public sealed class ArmourRow
    {
        public int Item;
        public string Name;
        public string Enchantment = "";
        public string Effect = "";
        /// <summary>Body armour, leggings, gloves, boots, helm, shield, ring, crown.</summary>
        public string Slot = "";
        /// <summary>The hit locations it covers (0 torso, 1 hands, 2 legs, 3 head).</summary>
        public int[] Locations = new int[0];
        public int Protection;
        /// <summary>Armour at the quality chosen, as the game (DOS, and Exhumed as shipped) gives it:
        /// quality × protection / 64 + 1, plus a toughness enchantment.</summary>
        public int Armour;
        /// <summary>The armour the game's code means to give: differs from Armour where the code slips
        /// (an armour spell, which adds nothing; Toughness on a shield or ring, which goes twice to the
        /// torso and never to the hands). What a game with the slips fixed gives.</summary>
        public int IntendedArmour;
        /// <summary>The intended and the actual values differ.</summary>
        public bool Bugged { get { return IntendedArmour != Armour || IntendedWorn != Worn; } }
        /// <summary>Taken from the attacker's to-hit at its locations by a protection enchantment.</summary>
        public int ToHitPenalty;
        public double MassStones;
        /// <summary>COMOBJ quality class: how fast a critical hit wears it (2d4 &gt;&gt; class; 3 never).</summary>
        public int QualityClass;
        /// <summary>Expected damage of the reference blow at its location, without it and with it.</summary>
        public double Bare, Worn;
        /// <summary>Expected damage of the reference blow at its location with the intended armour.</summary>
        public double IntendedWorn;
        public double Saved { get { return Bare - Worn; } }
        public double SavedPercent { get { return Bare > 0 ? 100 * Saved / Bare : 0; } }
        public int Plain, Enchanted, Other;
        public int Total { get { return Plain + Enchanted + Other; } }
    }

    /// <summary>
    /// The armour overview (game/PLAYDATA.C armor_val and FixPlayerEquips, combat/COMBAT.C
    /// frp_check and do_damage, the same in both games). A worn piece adds quality × protection
    /// / 64 + 1 to its hit location: body armour the torso, gloves the hands, leggings and boots
    /// the legs (they add up), a helm the head, a shield both torso and hands. A creature's blow
    /// hits one location; to hit it needs its to-hit + a roll 0-30 − the player's Defence (Defense
    /// + the wielded weapon's skill / 2) above 15; its damage, doubled on half the criticals, is
    /// rolled as dice and loses the location's armour. Rings only work through enchantments.
    /// Protection N (class 12, 0-7) takes N + 1 from the attacker's to-hit at the piece's
    /// locations; Toughness (8-15) adds N − 7 armour (on a shield or ring twice to the torso, never
    /// to the hands).
    /// </summary>
    public static class ArmourBalance
    {
        public static readonly string[] LocationNames = { "torso", "hands", "legs", "head" };

        /// <summary>The paperdoll category (OBJECTS.DAT armour byte 3) as a slot and its locations.</summary>
        public static string SlotOf(int category, out int[] locations)
        {
            switch (category)
            {
                case 0: locations = new[] { 0, 1 }; return "Shield";
                case 1: locations = new[] { 0 }; return "Body";
                case 3: locations = new[] { 2 }; return "Legs";
                case 4: locations = new[] { 1 }; return "Gloves";
                case 5: locations = new[] { 2 }; return "Boots";
                case 8: locations = new[] { 3 }; return "Head";
                case 9: locations = new int[0]; return "Ring";
                default: locations = new int[0]; return "Category " + category;
            }
        }

        /// <summary>armor_val: quality × protection / 64 + 1.</summary>
        public static int ArmourValue(int quality, int protection) { return ((quality & 0x3F) * protection >> 6) + 1; }

        /// <summary>The expected damage of a blow on the player at a location with this armour and to-hit penalty.</summary>
        public static double Taken(BalanceBlow b, int defence, int armour, int toHitPenalty, bool easy)
        {
            double e = Dice.PerSwing(b.Damage, b.ToHit - toHitPenalty - defence, 128, 0, armour);
            return easy ? e / 2 : e;
        }

        /// <summary>
        /// What a worn enchantment does. <paramref name="everywhere"/> is the armour it adds at every
        /// location as the game runs; <paramref name="intendedEverywhere"/> what the code means to add
        /// (they differ for the armour spells, see below). Toughness on a shield or ring: the caller
        /// counts <paramref name="armour"/> twice on the torso (actual) or once on torso and hands (intended).
        /// </summary>
        internal static string WornEffect(GameKind kind, ObjectSpell sp, bool shieldOrRing, out int toHit, out int armour, out int everywhere, out int intendedEverywhere)
        {
            toHit = armour = everywhere = intendedEverywhere = 0;
            if (!sp.Has) return "";
            if (sp.Flag11) return "a spell cast when the item is used; nothing while worn";
            if (sp.Major == 12)
            {
                if (sp.Effect < 8) { toHit = sp.Effect + 1; return string.Format("−{0} to the attacker's to-hit at its locations (melee)", toHit); }
                armour = sp.Effect - 7;
                // player_affected_by adds the toughness to armour[slots[0]] in its loop over slots 0 and 1
                return shieldOrRing
                    ? string.Format("+{0} armour on torso and hands intended; actual +{1} on the torso (twice), none on the hands", armour, 2 * armour)
                    : string.Format("+{0} armour at its location", armour);
            }
            int spell = sp.Link & 0x1FF;
            switch (spell)
            {
                // class 2 (armour) keeps its level in bits 4-7 of the bonuses, but parse_spells shifts the
                // bonuses right four times in its stealth loop before it reads them: the armour it adds is
                // bits 8-11, always 0. UW.EXE and UW2.EXE (ovr133:065D, ovr142:0615) do the same.
                case 0x22: intendedEverywhere = 2; return "+2 armour everywhere intended (Resist Blows); actual none: parse_spells drops it";
                case 0x23: intendedEverywhere = 3; return "+3 armour everywhere intended (Thick Skin); actual none: parse_spells drops it";
                case 0x25: intendedEverywhere = 5; return "+5 armour everywhere intended (Iron Flesh); actual none: parse_spells drops it";
                // class 3 minor 1: cmbModTH += 3 at every location, which frp_check takes off the attacker's skill
                case 0x31: toHit = 3; return "−3 to the attacker's to-hit everywhere (melee; adds up)";
                case 0x32: return "quieter (noise −16)";
                case 0x33: return "harder to see (visibility −5)";
                case 0x34: return kind == GameKind.UW2 ? "much harder to see (visibility −16, and Conceal)" : "much harder to see (visibility −16)";
                case 0x35: return "immune to missiles (arrows, bolts, stones, magic missile)";
                case 0x36: return "immune to fire (fireball)";
                case 0x37: return "resists poison";
                case 0x38: return "ignores a third of magic attacks";
                case 0x39: return "ignores two thirds of magic attacks";
                case 0x3A: return kind == GameKind.UW2 ? "+10 + Casting / 5 to the player's attack (Valor)" : "no effect found";
                case 0x3B: return kind == GameKind.UW2 ? "sharp weapons hit harder (Poison Weapon)" : "no effect found";
                case 0xBE: return "+1 hit point a slow tick (Regeneration)";
                case 0xBF: return "+1 mana a slow tick (Mana Regeneration)";
            }
            if (sp.Major == 9) return "a curse";
            if (spell < 8) return "light";
            if (spell >> 4 == 1) return "a motion effect";
            return "see the manual's What enchantments do";
        }

        /// <summary>The overview for the quality, player and blow given.</summary>
        public static List<ArmourRow> Rows(GameSession s, ObjectTables t, Func<int, LevelModel> level, PlayerStats p, int weaponSkill, BalanceBlow blow, int quality)
        {
            var kind = s.Kind;
            var copies = new Dictionary<int, Dictionary<int, int>>();
            var spells = new Dictionary<int, ObjectSpell>();
            foreach (var f in WorldObjects.All(s, level))
            {
                var o = f.Object;
                if (o.Item < 0x20 || o.Item >= 0x40) continue;
                var sp = ObjectSpell.Of(f.Model, o);
                int key = sp.Has ? (sp.Link | (sp.Flag11 ? 0x10000 : 0)) : 0;
                if (sp.Has) spells[key] = sp;
                if (!copies.TryGetValue(o.Item, out var d)) copies[o.Item] = d = new Dictionary<int, int>();
                d[key] = (d.TryGetValue(key, out var c) ? c : 0) + 1;
            }
            int defence = p[PlayerStats.Defense] + p[weaponSkill] / 2;
            double bare = Taken(blow, defence, 0, 0, p.Easy);
            var rows = new List<ArmourRow>();
            for (int item = 0x20; item < 0x40; item++)
            {
                string name = string.IsNullOrWhiteSpace(s.Strings.Get(4, item)) ? null : s.Names.Get(item);
                copies.TryGetValue(item, out var d);
                if (name == null && d == null) continue;
                var head = Row(s, t, item, name, new ObjectSpell(), quality, defence, blow, bare, p.Easy);
                rows.Add(head);
                if (d == null) continue;
                var extra = new List<ArmourRow>();
                foreach (var kv in d)
                {
                    if (kv.Key == 0) { head.Plain += kv.Value; continue; }
                    var sp = spells[kv.Key];
                    var r = Row(s, t, item, name, sp, quality, defence, blow, bare, p.Easy);
                    r.Enchantment = sp.Name(s, item);
                    bool class12 = sp.IsClass12;
                    if (class12) { head.Enchanted += kv.Value; r.Enchanted = kv.Value; } else { head.Other += kv.Value; r.Other = kv.Value; }
                    extra.Add(r);
                }
                rows.AddRange(extra.OrderBy(r => r.Enchantment));
            }
            return rows;
        }

        internal static ArmourRow Row(GameSession s, ObjectTables t, int item, string name, ObjectSpell sp, int quality, int defence, BalanceBlow blow, double bare, bool easy)
        {
            var r = new ArmourRow { Item = item, Name = string.IsNullOrWhiteSpace(name) ? string.Format("item 0x{0:X3}", item) : name.Trim() };
            int row = item - 0x20;
            int cat = t.Get("Armour", row, "Category");
            int[] locs;
            r.Slot = SlotOf(cat, out locs);
            // UW1's shiny shield (in-class 7) is not a shield to the game: only 0x3B-0x3F count in the shield hand
            if (cat == 0 && item < 0x3B) { locs = new int[0]; r.Slot = "Shield (not counted)"; }
            r.Locations = locs;
            r.Protection = t.Get("Armour", row, "Protection");
            r.MassStones = t.Get("Common properties", item, "Mass") / 10.0;
            r.QualityClass = t.Get("Common properties", item, "Quality class");
            bool shieldOrRing = cat == 0 || cat == 9;
            int th, ar, all, intendedAll;
            r.Effect = WornEffect(s.Kind, sp, shieldOrRing, out th, out ar, out all, out intendedAll);
            r.ToHitPenalty = th;
            int own = cat == 9 ? 0 : ArmourValue(quality, r.Protection);
            // at the piece's location (a shield or ring: its torso): the toughness twice there as the game
            // runs, once as meant (and once more on the hands)
            r.Armour = own + (shieldOrRing ? 2 * ar : ar) + all;
            r.IntendedArmour = own + ar + intendedAll;
            r.Bare = bare;
            // a piece the game does not count as worn (a shield it does not take, an unknown slot) does nothing, enchantment included
            if (locs.Length == 0 && cat != 9) { r.Worn = r.IntendedWorn = bare; return r; }
            r.Worn = Taken(blow, defence, r.Armour, th, easy);
            r.IntendedWorn = Taken(blow, defence, r.IntendedArmour, th, easy);
            return r;
        }
    }
}
