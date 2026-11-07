using System;
using System.Collections.Generic;
using UltimateEditor.Games;
using UwEditor.Core.Editing;

namespace UwEditor.Core.Balance
{
    /// <summary>
    /// Borland C's rand(), as both games use it: seed = seed × 0x015A4E35 + 1, the result the
    /// seed's bits 16-30. srand(n) sets the seed to n.
    /// </summary>
    public sealed class BorlandRandom
    {
        uint seed;
        public BorlandRandom(int seed) { this.seed = unchecked((uint)seed); }
        public int Next() { seed = unchecked(seed * 0x015A4E35u + 1); return (int)((seed >> 16) & 0x7FFF); }

        /// <summary>
        /// conv/BARTER.C range(base, min, max): base plus a random min..max−1 per cent of it, in
        /// 16 bits as DOS computes it (the product base × per cent wraps past 7FFFh).
        /// </summary>
        public int Range(int b, int min, int max)
        {
            int pct = min + (int)((long)Next() * (max - min) / 0x8000);
            return (short)(b + (short)(b * pct) / 100);
        }
    }

    /// <summary>The game's damage dice and its blow's arithmetic (combat/COMBAT.C do_damage).</summary>
    public static class Dice
    {
        /// <summary>sys/UTIL.C rollem as do_damage rolls it: (n / 6)d6 plus 1d(n % 6). The chance of each total.</summary>
        public static double[] Roll(int n)
        {
            var p = new double[] { 1 };
            for (int i = 0; i < n / 6; i++) p = Add(p, 6);
            if (n % 6 > 0) p = Add(p, n % 6);
            return p;
        }

        static double[] Add(double[] p, int sides)
        {
            var r = new double[p.Length + sides];
            for (int a = 0; a < p.Length; a++)
                if (p[a] != 0) for (int k = 1; k <= sides; k++) r[a + k] += p[a] / sides;
            return r;
        }

        /// <summary>
        /// The expected damage of one blow that hits (do_damage): damage at least 2, the dice, ×
        /// power / 128 (rounded down), + the hit angle, less the armour (not below 0).
        /// </summary>
        public static double Expected(int damage, int power, int hitangle, int armour)
        {
            if (damage < 2) damage = 2;
            var p = Roll(damage);
            double e = 0;
            for (int r = 0; r < p.Length; r++)
                if (p[r] > 0) e += p[r] * Math.Max(0, ((r * power) >> 7) + hitangle - armour);
            return e;
        }

        /// <summary>
        /// The chances of a hit (a critical included) and of a critical (combat/COMBAT.C frp_check):
        /// the attack skill less the defence is d; a roll 0-30 is added; above 15 hits, above 28 is
        /// critical.
        /// </summary>
        public static void HitChances(int d, out double hit, out double critical)
        {
            hit = Math.Max(0, Math.Min(31, 15 + d)) / 31.0;
            critical = Math.Max(0, Math.Min(31, d + 2)) / 31.0;
        }

        /// <summary>
        /// The expected damage of one swing: missed blows do nothing; a critical hit doubles the
        /// damage before the dice half of the time (frp_check: ((rand() &amp; 31) + 48) &gt;&gt; 5).
        /// </summary>
        public static double PerSwing(int damage, int d, int power, int hitangle, int armour)
        {
            double hit, crit;
            HitChances(d, out hit, out crit);
            double plain = Expected(damage, power, hitangle, armour);
            double doubled = Expected(damage * 2, power, hitangle, armour);
            return (hit - crit) * plain + crit * (plain + doubled) / 2;
        }
    }

    /// <summary>An object's enchantment as the game decodes it (obj/USEITEMS.C decode_obj_spell).</summary>
    public struct ObjectSpell
    {
        public bool Has;
        /// <summary>Flag 11 clear: (link &amp; 0x1FF) &gt;&gt; 4. Flag 11 set (a spell cast on use): −1 for spells 0-63, else 12 + (link &amp; 0x1FF) &gt;&gt; 6.</summary>
        public int Major;
        public int Effect;
        /// <summary>Flag 11: a spell cast when the object is used, not an effect while it is held or worn.</summary>
        public bool Flag11;
        /// <summary>The link the effect is read from (0x200 and up).</summary>
        public int Link;
        /// <summary>Read from a spell object (0x120) inside the object, not from the object's own link.</summary>
        public bool FromSpellObject;

        /// <summary>A weapon or armour enchantment of class 12 (accuracy, damage, protection, toughness and the UW2 special weapons).</summary>
        public bool IsClass12 { get { return Has && !Flag11 && Major == 12; } }

        public static ObjectSpell Of(LevelModel m, ObjectInfo o)
        {
            var r = new ObjectSpell();
            if (o.Item >= 0x180 && o.Item < 0x1C0) return r;                        // traps and triggers
            ObjectInfo spell = null;
            if (!o.IsQuantity && o.Link > 0)
            {
                foreach (int c in o.Children)
                    if (c > 0 && c < m.Objects.Length && m.Objects[c].Item == 0x120) { spell = m.Objects[c]; break; }
                if (spell != null && spell.Link < 0x200) spell = null;
            }
            else if (o.IsQuantity && o.Enchanted && o.Item < 0x1C0 && o.Link >= 0x200) spell = o;
            if (spell == null) return r;
            r.Has = true;
            r.FromSpellObject = spell != o;
            r.Link = spell.Link;
            r.Flag11 = (spell.Record[1] & 0x08) != 0;
            if (r.Flag11)
            {
                int major = (spell.Link & 0x1FF) >> 6;
                r.Major = major == 0 ? -1 : major + 12;
                r.Effect = spell.Link & 0x3F;
            }
            else
            {
                r.Major = (spell.Link & 0x1FF) >> 4;
                r.Effect = spell.Link & 0xF;
            }
            return r;
        }

        /// <summary>The game's name for it (STRINGS.PAK block 6), or a plain description.</summary>
        public string Name(GameSession s, int item)
        {
            if (!Has) return "";
            string n = Enchantments.NameOf(item, true, true, Link, Flag11, s.Strings);
            if (!string.IsNullOrWhiteSpace(n)) return n.Trim();
            return Flag11 ? string.Format("spell 0x{0:X2}", Link & 0x1FF) : string.Format("effect 0x{0:X2}", Link & 0x1FF);
        }
    }

    /// <summary>The objects the world holds: every level as edited, each object that lies on a tile or inside something.</summary>
    public static class WorldObjects
    {
        public struct Found
        {
            public int Level;
            public LevelModel Model;
            public ObjectInfo Object;
        }

        /// <summary>Every object in use on the present levels (on a tile, in a container, carried, or made by a trap).</summary>
        public static IEnumerable<Found> All(GameSession s, Func<int, LevelModel> level)
        {
            foreach (var slot in s.LevelSlots())
            {
                if (!slot.Present) continue;
                LevelModel m;
                try { m = level(slot.Level); } catch (Exception) { continue; }
                if (m == null) continue;
                foreach (var o in m.Objects)
                {
                    if (o == null || o.Free || o.Index == 0 || (o.TileX < 0 && o.Parent < 0)) continue;
                    yield return new Found { Level = slot.Level, Model = m, Object = o };
                }
            }
        }

        /// <summary>A stack's size: the link when the object is a quantity below 0x200, else 1.</summary>
        public static int Quantity(ObjectInfo o) { return o.IsQuantity && o.Link < 0x200 && o.Link > 0 ? o.Link : 1; }
    }

    /// <summary>The player's skills and attributes the balance tools read (indexes of PLAYER.DAT's skills).</summary>
    public sealed class PlayerStats
    {
        public const int Attack = 0, Defense = 1, Unarmed = 2, Sword = 3, Axe = 4, Mace = 5, Missile = 6, Charm = 15, Appraise = 18;

        public int Strength = 20, Dexterity = 20;
        public readonly int[] Skills = new int[20];
        /// <summary>Easy difficulty: +7 to hit, half the damage taken.</summary>
        public bool Easy;

        public PlayerStats()
        {
            for (int i = 0; i < Skills.Length; i++) Skills[i] = 10;
        }

        public int this[int skill] { get { return Skills[skill]; } set { Skills[skill] = Math.Max(0, Math.Min(30, value)); } }

        /// <summary>From a PLAYER.DAT, or the defaults when there is none.</summary>
        public static PlayerStats From(PlayerDat p)
        {
            var r = new PlayerStats();
            if (p == null) return r;
            r.Strength = p.Strength; r.Dexterity = p.Dexterity;
            for (int i = 0; i < r.Skills.Length; i++) r.Skills[i] = p.Skill(i);
            return r;
        }
    }
}
