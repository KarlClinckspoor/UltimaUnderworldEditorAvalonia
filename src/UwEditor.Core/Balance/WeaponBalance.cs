using System;
using System.Collections.Generic;
using System.Linq;
using UltimateEditor.Games;
using UwEditor.Core.Editing;

namespace UwEditor.Core.Balance
{
    /// <summary>The creature a blow is aimed at, for the expected damage.</summary>
    public sealed class BalanceTarget
    {
        /// <summary>Its defence (creature byte 0x12), taken from the attacker's to-hit.</summary>
        public int Defence = 5;
        /// <summary>The armour taken from each blow's damage (a creature's armour at the zone hit).</summary>
        public int Armour = 0;
        /// <summary>A powerful creature: armour × 5 / 3 against the player's blows.</summary>
        public bool Powerful;
    }

    /// <summary>One line of the weapons overview: a weapon (or a launcher with its ammunition), plain or with one enchantment.</summary>
    public sealed class WeaponRow
    {
        public int Item;
        public string Name;
        /// <summary>The enchantment of this line ("" for the weapon's own line, which counts every copy).</summary>
        public string Enchantment = "";
        /// <summary>What the enchantment does in a fight.</summary>
        public string Effect = "";
        public bool Ranged;
        public string Skill = "";
        /// <summary>Base damage of slash, bash and stab (melee), or the ammunition's damage (ranged, in [0]).</summary>
        public int[] Base = new int[3];
        public int MaxCharge, Speed;
        public int ToHitBonus, DamageBonus;
        /// <summary>Melee: expected damage per swing of each kind, and the seconds a swing takes; ranged: per shot in [0].</summary>
        public double[] PerSwing = new double[3];
        public double[] Seconds = new double[3];
        public double[] PerSecond = new double[3];
        /// <summary>Copies in the world.</summary>
        public int Plain, Enchanted, Special, Other;
        public int Total { get { return Plain + Enchanted + Special + Other; } }
        /// <summary>Ranged: the ammunition and its count in the world (stacks summed).</summary>
        public int Ammo = -1;
        public string AmmoName = "";
        public int AmmoCount;
        public bool Unused { get { return Total == 0; } }
        /// <summary>The best of the three kinds (melee) or the shot (ranged).</summary>
        public double BestPerSecond { get { return Ranged ? PerSecond[0] : PerSecond.Max(); } }
    }

    /// <summary>
    /// The weapons overview: every melee weapon and launcher, how many the levels hold (plain,
    /// enchanted, special and other), and the damage a player with given skills expects from it.
    ///
    /// Melee (combat/COMBAT.C DoPlayerWeapon, frp_check, do_damage, the same in both games):
    /// to hit is Attack / 2 + the weapon's skill + Dexterity / 7 (+7 easy) less the target's
    /// defence, a roll 0-30 hitting above 15 and striking critically above 28; damage is the
    /// swing's base + Strength / 9 (bare hands: Unarmed × 2 / 5 + Strength / 6 + 4), doubled
    /// on half the criticals, rolled as (d / 6)d6 + 1d(d % 6), × the charge / 128 (a full charge
    /// is the weapon's Max charge), less the target's armour. Quality changes nothing.
    ///
    /// Timing (ui/PANELS.C, COMBAT.C player_attack): the clock runs at 256 ticks a second and the
    /// weapon's picture moves on every 32 ticks; the charge grows by Speed every 16 ticks up to
    /// 100. A swing released at full charge takes about 16 + 32 × (wind-up − 1) + the charge
    /// rounded up to 32 + 160 ticks; the wind-up is 4 steps (UW2: 2 for a stab or bare hands).
    ///
    /// Missiles (motion/OBJPHYS.C missile_newhit): only the ammunition's damage counts, scaled by
    /// (Missile × 8 + 192) / 256, less 128/256 on a bad roll and plus 192/256 on a good one; no
    /// charge, no strength, no critical, no enchantment. A shot takes about 5 picture steps
    /// (0.625 s) plus the time spent aiming.
    /// </summary>
    public static class WeaponBalance
    {
        public const int TicksPerSecond = 256;

        /// <summary>The swing kinds, in the weapon table's order.</summary>
        public static readonly string[] Kinds = { "Slash", "Bash", "Stab" };

        /// <summary>Ticks of one melee swing released at full charge (see the class summary).</summary>
        public static int SwingTicks(GameKind kind, int speed, int swingKind, bool bareHands)
        {
            int n = (int)Math.Ceiling(100.0 / Math.Max(1, speed));
            int charge = 16 * n + 1;
            int windUp = kind == GameKind.UW2 && (swingKind == 2 || bareHands) ? 2 : 4;
            return 16 + 32 * (windUp - 1) + 32 * ((charge + 31) / 32) + 160;
        }

        /// <summary>The to-hit and damage a class 12 weapon enchantment adds (EnchantmentEffects; UW2's names are swapped).</summary>
        public static void Bonus(GameKind kind, ObjectSpell sp, out int toHit, out int damage, out bool special)
        {
            toHit = damage = 0; special = false;
            if (!sp.IsClass12) return;
            int e = sp.Effect;
            if (kind == GameKind.UW2)
            {
                if (e < 4) damage = 2 * e + 1;
                else if (e < 8) toHit = 2 * e - 7;
                else
                {
                    special = true;
                    // spec_damage[specweap] for effect 15 reads one byte past its table: the 5 of spec_skill[0]
                    if (e == 15) damage = 5;
                }
            }
            else if ((e & 8) != 0) damage = (e & 7) + 1;
            else toHit = (e & 7) + 1;
        }

        static readonly string[] Uw2Specials = { "heals the player by the damage done (on a critical hit)", "repels the undead (on a critical hit)", "casts a fireball (on a critical hit)", "holds the target (on a critical hit)", "opens a door it hits (on a critical hit)", "opens a door it hits (on every hit)", "nothing", "+5 damage (the bonus table read one byte past its end)" };

        static string EffectText(GameKind kind, ObjectSpell sp, int toHit, int damage, bool special, bool ranged)
        {
            if (!sp.Has) return "";
            if (ranged) return "nothing: launchers and ammunition ignore enchantments";
            if (sp.IsClass12)
            {
                if (special) return Uw2Specials[sp.Effect - 8];
                return toHit > 0 ? "+" + toHit + " to hit" : "+" + damage + " damage";
            }
            return sp.Flag11 ? "nothing in a fight: a spell cast when the weapon is used" : "nothing in a fight (only class 12 counts on a weapon)";
        }

        /// <summary>The overview, for the stats and target given.</summary>
        public static List<WeaponRow> Rows(GameSession s, ObjectTables t, Func<int, LevelModel> level, PlayerStats p, BalanceTarget target, double aimSeconds)
        {
            var kind = s.Kind;
            // what the world holds: per item, the enchantments met (by link) and their counts
            var copies = new Dictionary<int, Dictionary<int, int>>();      // item -> link (0 plain) -> count
            var spells = new Dictionary<int, ObjectSpell>();
            var ammoCount = new Dictionary<int, int>();
            foreach (var f in WorldObjects.All(s, level))
            {
                var o = f.Object;
                if (o.Item >= 0x10 && o.Item < 0x18) { ammoCount[o.Item] = (ammoCount.TryGetValue(o.Item, out var a) ? a : 0) + WorldObjects.Quantity(o); continue; }
                if (o.Item >= 0x20) continue;
                var sp = ObjectSpell.Of(f.Model, o);
                int key = sp.Has ? (sp.Link | (sp.Flag11 ? 0x10000 : 0)) : 0;
                if (sp.Has) spells[key] = sp;
                if (!copies.TryGetValue(o.Item, out var d)) copies[o.Item] = d = new Dictionary<int, int>();
                d[key] = (d.TryGetValue(key, out var c) ? c : 0) + 1;
            }

            var rows = new List<WeaponRow>();
            for (int item = 0; item < 0x20; item++)
            {
                if (item >= 0x10 && item < 0x18) continue;          // ammunition goes with its launcher
                bool ranged = item >= 0x18;
                string name = s.Strings.Get(4, item) == null || string.IsNullOrWhiteSpace(s.Strings.Get(4, item)) ? null : s.Names.Get(item);
                copies.TryGetValue(item, out var d);
                bool fist = item == 0x0F;
                if (!fist && name == null && d == null) continue;
                var head = Row(s, t, item, name, ranged, new ObjectSpell(), p, target, aimSeconds, ammoCount);
                if (ranged && head.Ammo < 0) continue;                      // a missile of the game's own (fireball, dart...), not a launcher
                if (fist) head.Name = "Bare hands (" + (string.IsNullOrWhiteSpace(name) ? "fist" : name.Trim()) + ")";
                rows.Add(head);
                if (d == null) continue;
                var extra = new List<WeaponRow>();
                foreach (var kv in d)
                {
                    if (kv.Key == 0) { head.Plain += kv.Value; continue; }
                    var sp = spells[kv.Key];
                    int th, dm; bool special;
                    Bonus(kind, sp, out th, out dm, out special);
                    bool combat = sp.IsClass12 && !special && !ranged;
                    if (combat) head.Enchanted += kv.Value; else if (special && !ranged) head.Special += kv.Value; else head.Other += kv.Value;
                    var r = Row(s, t, item, name, ranged, sp, p, target, aimSeconds, ammoCount);
                    r.Enchantment = sp.Name(s, item);
                    r.Effect = EffectText(kind, sp, th, dm, special, ranged);
                    if (combat) r.Enchanted = kv.Value; else if (special && !ranged) r.Special = kv.Value; else r.Other = kv.Value;
                    extra.Add(r);
                }
                rows.AddRange(extra.OrderBy(r => r.Enchantment));
            }
            return rows;
        }

        static WeaponRow Row(GameSession s, ObjectTables t, int item, string name, bool ranged, ObjectSpell sp, PlayerStats p, BalanceTarget target, double aimSeconds, Dictionary<int, int> ammoCount)
        {
            var kind = s.Kind;
            var r = new WeaponRow { Item = item, Name = string.IsNullOrWhiteSpace(name) ? string.Format("item 0x{0:X3}", item) : name.Trim(), Ranged = ranged };
            int armour = target.Powerful ? target.Armour * 5 / 3 : target.Armour;
            if (!ranged)
            {
                int skill = t.Get("Weapons", item, "Skill");
                bool bare = skill < PlayerStats.Unarmed || skill >= PlayerStats.Missile;
                if (bare) skill = PlayerStats.Unarmed;
                r.Skill = UltimateEditor.Games.PlayerDat.SkillNames[skill];
                r.MaxCharge = t.Get("Weapons", item, "Max charge");
                r.Speed = t.Get("Weapons", item, "Speed");
                int th, dm; bool special;
                Bonus(kind, sp, out th, out dm, out special);
                r.ToHitBonus = th; r.DamageBonus = dm;
                int askill = p[PlayerStats.Attack] / 2 + p[skill] + p.Dexterity / 7 + (p.Easy ? 7 : 0) + th;
                int d = askill - target.Defence;
                for (int k = 0; k < 3; k++)
                {
                    int b = bare ? p[PlayerStats.Unarmed] * 2 / 5 + p.Strength / 6 + 4 : t.Get("Weapons", item, Kinds[k] + " damage") + p.Strength / 9;
                    r.Base[k] = bare ? b : t.Get("Weapons", item, Kinds[k] + " damage");
                    b += dm;
                    r.PerSwing[k] = Dice.PerSwing(b, d, Math.Max(1, r.MaxCharge), 0, armour);
                    r.Seconds[k] = SwingTicks(kind, r.Speed, k, bare) / (double)TicksPerSecond;
                    r.PerSecond[k] = r.PerSwing[k] / r.Seconds[k];
                }
            }
            else
            {
                r.Skill = "Missile";
                int ammo = (sbyte)(byte)t.Get("Ranged", item - 0x10, "Ammo / damage type");
                if (ammo >= 0 && ammo < 8)
                {
                    r.Ammo = 0x10 + ammo;
                    r.AmmoName = s.Names.Get(r.Ammo)?.Trim() ?? "";
                    r.AmmoCount = ammoCount.TryGetValue(r.Ammo, out var n) ? n : 0;
                    int dmg = t.Get("Ranged", ammo, "Damage");
                    bool scaled = (sbyte)(byte)t.Get("Ranged", ammo, "Ammo / damage type") == -64;
                    r.Base[0] = dmg;
                    r.PerSwing[0] = scaled ? MissileExpected(dmg, p[PlayerStats.Missile], armour) : Dice.Expected(dmg, 128, 0, armour);
                    r.Seconds[0] = 160.0 / TicksPerSecond + Math.Max(0, aimSeconds);
                    r.PerSecond[0] = r.PerSwing[0] / r.Seconds[0];
                }
            }
            return r;
        }

        /// <summary>A missile's expected damage on a hit: the three Missile skill outcomes (skill_check against 10), then the dice.</summary>
        public static double MissileExpected(int damage, int missile, int armour)
        {
            int scale = missile * 8 + 192;
            double bad = Math.Max(0, Math.Min(31, 13 - missile)) / 31.0, good = Math.Max(0, Math.Min(31, missile - 8)) / 31.0;
            double mid = 1 - bad - good;
            return bad * Dice.Expected(damage * (scale - 128) >> 8, 128, 0, armour)
                + mid * Dice.Expected(damage * scale >> 8, 128, 0, armour)
                + good * Dice.Expected(damage * (scale + 192) >> 8, 128, 0, armour);
        }

        /// <summary>A creature class as a target: its defence and its armour (the four zones' average, 255 meaning zone 0).</summary>
        public static BalanceTarget TargetOf(ObjectTables t, int cls)
        {
            int z0 = t.Get("Creatures", cls, "Armour head / level");
            int sum = z0;
            for (int z = 1; z < 4; z++) { int v = t.Get("Creatures", cls, "Armour zone " + z); sum += v == 0xFF ? z0 : v; }
            return new BalanceTarget { Defence = (sbyte)(byte)t.Get("Creatures", cls, "Defence"), Armour = (int)Math.Round(sum / 4.0) };
        }
    }
}
