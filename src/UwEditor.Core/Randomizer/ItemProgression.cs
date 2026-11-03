using System;
using System.Collections.Generic;
using System.Linq;
using UwEditor.Core.Editing;

namespace UwEditor.Core.Randomizer
{
    public sealed partial class ShuffleOptions
    {
        /// <summary>
        /// Swap: how objects find their spots. "random" (a random permutation within the scope, the
        /// default) or "progression" (Uniform progression: each kind sorted by strength and handed
        /// out level by level in the game's order, see <see cref="Progression"/>). Progression
        /// works on the whole game and within each kind: the scope and "across all kinds" do not
        /// apply.
        /// </summary>
        public string SwapRule { get; set; } = "random";

        /// <summary>Whether the swap uses Uniform progression.</summary>
        [System.Text.Json.Serialization.JsonIgnore]
        public bool IsProgression { get { return SwapRule == "progression"; } }
    }

    public static partial class ItemShuffle
    {
        /// <summary>The kinds Uniform progression ranks by strength; the others are swapped at random over the whole game.</summary>
        public static readonly string[] RankedKinds = { "weapon", "armour", "scroll", "wand" };

        /// <summary>Whether Uniform progression ranks this swap kind by strength.</summary>
        public static bool Ranked(string kind) { return Array.IndexOf(RankedKinds, kind) >= 0; }

        /// <summary>The kind's plural name, for the report.</summary>
        static string KindName(string kind)
        {
            switch (kind)
            {
                case "weapon": return "weapons";
                case "armour": return "armour";
                case "scroll": return "spell scrolls";
                case "wand": return "wands";
                default: return kind;
            }
        }

        /// <summary>
        /// An object's strength for Uniform progression, by what it is (the record r, as moved
        /// whole): weapons the damage (<see cref="Progression.WeaponDamage"/>) + the enchantment's
        /// level, armour the protection + the enchantment's level, spell scrolls and wands the
        /// spell's circle (a wand's spell is in its spell object, spell; a wand without one, 0).
        /// </summary>
        public static double ItemStrength(GameSession s, byte[] objects, ItemRewrite r, ItemRewrite? spell = null)
        {
            bool f11 = (r.Flags & 4) != 0;
            double ench = Progression.EnchantLevel(s, r.Item, r.Enchanted, r.IsQuantity, r.Link, f11);
            if (r.Item < 0x20) return Progression.WeaponDamage(objects, r.Item) + ench;
            if (r.Item < 0x40) return Progression.ArmourProtection(objects, r.Item) + ench;
            if (r.Item >= 0x98 && r.Item <= 0x9B)
            {
                if (spell.HasValue) { var sp = spell.Value; return ((sp.Link & 0x1FF) >> 6) == 0 && sp.Link >= 0x200 ? Progression.SpellCircle(s.IsUw2, sp.Link) : 0; }
                return f11 ? ench : 0;
            }
            if (r.Item >= 0x130 && r.Item <= 0x13F) return f11 ? ench : 0;
            return 0;
        }

        /// <summary>An object's strength for Uniform progression as it lies (a wand's spell from its spell object, when it holds one).</summary>
        public static double ItemStrength(GameSession s, byte[] objects, LevelModel m, ObjectInfo o)
        {
            ItemRewrite? spell = null;
            if (o.Item >= 0x98 && o.Item <= 0x9B && !o.IsQuantity && o.Link > 0 && o.Link < m.Objects.Length && m.Objects[o.Link].Item == 0x120) spell = Of(m.Objects[o.Link]);
            return ItemStrength(s, objects, Of(o), spell);
        }

        /// <summary>The options as the scope of the unranked kinds under progression: the whole game.</summary>
        static readonly ShuffleOptions WholeGame = new ShuffleOptions { SwapScope = "game" };

        /// <summary>
        /// One list under Uniform progression: the permutation (spot i takes the object of spot
        /// p[i]) and the report lines, per level in the walk: the count and the strengths before →
        /// after.
        /// </summary>
        static int[] ProgressionOf(GameSession s, Random rng, ShuffleResult res, string what, IList<int> levels, IList<double> strength)
        {
            int fb, rl;
            var p = Progression.Assign(rng, s, levels, strength, null, out fb, out rl);
            res.Report.AppendFormat("  {0} by level (count: strength before → after):\n", what);
            foreach (int l in Progression.LevelOrder(s, levels))
            {
                var idx = Enumerable.Range(0, levels.Count).Where(i => levels[i] == l).ToList();
                res.Report.AppendFormat("    {0}: {1}: {2} → {3}\n", Progression.LevelName(s, l), idx.Count, Progression.Range(idx.Select(i => strength[i])), Progression.Range(idx.Select(i => strength[p[i]])));
            }
            return p;
        }
    }
}
