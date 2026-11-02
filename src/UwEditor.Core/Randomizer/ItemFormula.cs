using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;

namespace UwEditor.Core.Randomizer
{
    /// <summary>
    /// The item tools' math in words and symbols, from the settings as they are: what Run does
    /// with them (ItemShuffle.cs, ItemSwap.cs). Kept beside the code so the two agree.
    /// </summary>
    public static partial class ItemShuffle
    {
        static string F(double v) { return v.ToString("0.##", CultureInfo.InvariantCulture); }

        static string Notation()
        {
            return "Notation: Cat(a w1, b w2, …) picks a with probability w1 / Σw; U{a..b} a whole number from a to b, each as likely; " +
                   "U(S) an element of S, each as likely; N(μ, σ) a normal draw; clamp(x, a, b) keeps x in [a, b]; round to the nearest whole number. " +
                   "Every draw comes from one random sequence started by the seed.\n";
        }

        /// <summary>A weight list as Cat(name w, …), the zero weights left out.</summary>
        static string Cat(IEnumerable<(string Name, double W)> ws)
        {
            var l = ws.Where(x => x.W > 0).Select(x => x.Name + " " + F(x.W)).ToList();
            return l.Count == 0 ? "nothing (every weight is 0: left as they are)" : "Cat(" + string.Join(", ", l) + ")";
        }

        static string Bars(double[] b, Func<int, string> label)
        {
            if (b == null) return "nothing";
            return Cat(b.Select((w, i) => (label(i), w)));
        }

        static string DepthText(bool uw2) { return uw2 ? "d = (level within its world − 1) / 7" : "d = min(1, (level − 1) / 7)"; }

        static void EnchantFormula(StringBuilder b, GameSession s, List<EnchantGroup> groups, double depth, double strengthDepth, double scale, string scaleText)
        {
            b.AppendFormat("    {0}\n", DepthText(s.IsUw2));
            foreach (var g in groups)
            {
                if (g.Effects == null || g.Effects.Length == 0) continue;
                int n = g.Effects.Length;
                string p = g.CustomDepth
                    ? (s.IsUw2 ? "curve(level within world) / 100 (custom)" : "bars[level] / 100 (custom: " + string.Join(", ", (g.DepthBars ?? new double[0]).Select(F)) + ")")
                    : string.Format("{0}/100 × (1 + {1}/100 × d)", F(g.Probability), F(depth));
                string st = g.CustomStrength
                    ? "Cat by the custom bars (" + string.Join(", ", (g.StrengthBars ?? new double[0]).Select((w, i) => (i + 1) + " " + F(w))) + ")"
                    : string.Format("clamp(round(N({0} + {1}/100 × d × {2}, {3})), 1, {4})", F(g.Mean), F(strengthDepth), n - 1, F(g.Deviation), n);
                b.AppendFormat("    {0}: p = clamp({1}{2}, 0, 1); strength 1..{3} ~ {4}\n", g.Name, p, scale == 1 ? "" : " × " + scaleText, n, st);
            }
            b.AppendLine("    enchanted with probability min(1, Σp); the kind ~ Cat(p); a link to other objects (contents) is never replaced");
        }

        /// <summary>The property shuffle's math with these settings.</summary>
        public static string ShuffleFormula(GameSession s, ShuffleOptions o)
        {
            var b = new StringBuilder();
            bool uw2 = s.IsUw2;
            b.AppendFormat("Seed {0}. Objects stay where they lie and keep their owner field (an item put in place of another takes the spot's owner); q is an object's quality now.\n", o.Seed);
            b.AppendLine(o.KeepOtherEnchantments ? "Spells and other enchantments already on objects are kept; only plain and class 12 ones are rolled." : "Every enchantment may be rolled again.");
            b.AppendLine();
            if (o.ShuffleWeapons)
            {
                b.AppendLine("Weapons (" + (uw2 ? "the jewelled dagger" : "Caliburn") + " kept):");
                b.AppendLine("  group ~ " + Cat(new[] { ("axes", o.Axes), ("swords", o.Swords), ("maces", o.Maces), ("ranged", o.Ranged) }) + "; item ~ U(the group's items)");
                b.AppendFormat("  ranged: a launcher with probability {0}%, otherwise ammunition (count kept, or U{{3..12}}; never enchanted)\n", F(o.LauncherShare));
                b.AppendLine("  quality' = " + (o.CustomWeaponQuality ? "Cat(the custom curve at 1..63)" : o.WeaponQualitySpread <= 0 ? "q" : string.Format("clamp(q + U{{−{0}..{0}}}, 1, 63)", o.WeaponQualitySpread)));
                EnchantFormula(b, s, o.WeaponEnchants, o.WeaponDepth, o.WeaponStrengthDepth, 1, "");
                if (uw2 && o.WeaponSpecials) b.AppendLine("  then special weapons: " + string.Join(", ", o.WeaponSpecialList.Where(x => x.On && x.Count > 0).Select(x => x.Name + " ×" + x.Count)) + " on random melee weapons");
            }
            if (o.ShuffleArmour)
            {
                b.AppendLine("Armour (slot kept, quest pieces kept):");
                b.AppendLine("  material ~ " + Cat(new[] { ("leather", o.Leather), ("mail", o.Mail), ("plate", o.Plate), ("shield", o.Shields) }) + "; a shield ~ U(shields)");
                b.AppendLine("  quality' = " + (o.CustomArmourQuality ? "Cat(the custom curve at 1..63)" : o.ArmourQualitySpread <= 0 ? "q" : string.Format("clamp(q + U{{−{0}..{0}}}, 1, 63)", o.ArmourQualitySpread)));
                double e = o.ArmourEffectChance;
                if (o.ArmourEffectsOn)
                {
                    b.AppendFormat("  first, on the ticked slots, another effect with probability {0}%: cursed with probability {1}% (strength ~ {2}, × d8 damage), else effect ~ Cat(the effect bars)\n",
                        F(e), F(o.ArmourCurseShare), Bars(o.CurseStrength, i => (i + 1).ToString()));
                    EnchantFormula(b, s, o.ArmourEnchants, o.ArmourDepth, o.ArmourStrengthDepth, 2, "1 / max(0.05, 1 − " + F(e) + "/100) on those slots");
                }
                else EnchantFormula(b, s, o.ArmourEnchants, o.ArmourDepth, o.ArmourStrengthDepth, 1, "");
                if (o.ArmourSpecials) b.AppendLine("  then special armour: " + string.Join(", ", o.ArmourSpecialList.Where(x => x.On && x.Count > 0).Select(x => x.Name + " ×" + x.Count)) + " on random unenchanted pieces");
            }
            if (o.ShuffleFood) b.AppendLine("Food: item ~ " + Cat(o.FoodWeights.OrderBy(k => k.Key).Select(k => (s.Names.Get(k.Key), k.Value))));
            if (o.ShuffleLights) b.AppendLine("Lights: item ~ " + Cat(o.LightWeights.OrderBy(k => k.Key).Select(k => (s.Names.Get(k.Key), k.Value))) + "; lit ones stay lit");
            if (o.ShuffleFood || o.ShuffleLights)
                b.AppendFormat("Tools: while there are fewer than {0} thread, {1} poles, {2} fishing poles, random food and lights on the floor (food first) become them, quality 40\n", o.MinThread, o.MinPole, o.MinFishingPole);
            if (o.ShuffleRunes)
                b.AppendLine("Runestones: rune ~ " + Cat(ItemTables.Runes.Select(r => (s.Names.Get(r).Replace(" stone", ""), o.RuneWeights.ContainsKey(r) ? o.RuneWeights[r] : 0)))
                    + "; then, while a rune has fewer than its minimum, a stone of the rune with the most to spare becomes it");
            Func<int, string> circle = i => "circle " + SpellTables.CircleLabel(uw2, i + 1);
            if (o.ShuffleWands)
                b.AppendLine("Wands: circle ~ " + Bars(o.WandCircles, circle) + "; spell ~ U(the circle's spells); charges (wands holding a spell object) ~ " + Bars(o.WandCharges, i => (i + 1).ToString()));
            if (o.ShuffleScrolls) b.AppendLine("Spell scrolls: circle ~ " + Bars(o.ScrollCircles, circle) + "; spell ~ U(the circle's spells)");
            if (o.ShufflePotions)
                b.AppendLine("Potions: circle ~ " + Bars(o.PotionCircles, circle) + "; spell ~ U(the circle's spells" + (o.PotionsKnownOnly ? " some potion holds" : "") + ")" + (uw2 ? "; colour = the one the game uses most for that spell" : ""));
            if (o.ShuffleRings)
                b.AppendFormat("Rings: ring ~ {0}; with probability {1}% an effect: cursed with probability {2}% (strength ~ {3}), else effect ~ Cat(the effect bars); otherwise plain\n",
                    Cat((o.RingWeights ?? new Dictionary<int, double>()).OrderBy(k => k.Key).Select(k => (s.Names.Get(k.Key), k.Value))), F(o.RingMagic), F(o.RingCurseShare), Bars(o.CurseStrength, i => (i + 1).ToString()));
            b.AppendLine().Append(Notation());
            return b.ToString().TrimEnd();
        }

        /// <summary>The item swap's math with these settings.</summary>
        public static string SwapFormula(GameSession s, ShuffleOptions o)
        {
            var b = new StringBuilder();
            var kinds = new List<string>();
            if (o.ShuffleWeapons) kinds.Add("weapons"); if (o.ShuffleArmour) kinds.Add("armour"); if (o.ShuffleFood) kinds.Add("food"); if (o.ShuffleLights) kinds.Add("lights");
            if (o.SwapTools) kinds.Add("tools"); if (o.ShuffleRunes) kinds.Add("runestones"); if (o.ShuffleWands) kinds.Add("wands"); if (o.ShuffleScrolls) kinds.Add("spell scrolls");
            if (o.ShufflePotions) kinds.Add("potions"); if (o.ShuffleRings) kinds.Add("rings"); if (o.SwapBones) kinds.Add("bones and skulls"); if (o.SwapScenery) kinds.Add("other scenery");
            b.AppendFormat("Seed {0}. Pool: the {1}{4}, less {2} ignored object(s){3}.\n", o.Seed, kinds.Count == 0 ? "nothing (no kind ticked)" : string.Join(", ", kinds),
                o.Ignored == null ? 0 : o.Ignored.Count, o.Kept != null && o.Kept.Count > 0 ? " and " + o.Kept.Count + " of the starting equipment" : "", o.SwapBones ? " (Garamon's bones never)" : "");
            bool any = o.Mode == "swap-any";
            b.AppendFormat("Groups: {0}, {1}.\n", any ? "every ticked kind together" : "each kind on its own", ScopeText(s, o));
            switch (o.SwapScope)
            {
                case "near":
                    b.AppendFormat("{0}: π starts as the identity; {1} passes, each over the spots in a random order: spot a picks b ~ U(spots at levels L(a) − {2} .. L(a) + {2}), " +
                                   "and a and b trade objects when |L(π(a)) − L(b)| ≤ {2} and |L(π(b)) − L(a)| ≤ {2}, so no object goes more than {2} level(s) from where it was.\n",
                                   s.IsUw2 ? "Within each world" : "Within the game", NearPasses, Math.Max(0, o.SwapRange));
                    break;
                default:
                    b.AppendLine("Within a group" + (o.SwapScope == "level" ? " and level" : o.SwapScope == "world" && s.IsUw2 ? " and world" : "") + ": π ~ a uniform random permutation (Fisher–Yates); spot i receives the object from spot π(i).");
                    break;
            }
            b.AppendLine("The object moves whole: item, quality, flags, enchantment, quantity" + (o.SpotOwnership ? "." : ", owner."));
            if (o.SpotOwnership)
                b.AppendLine("Owner: owner'(i) = owner(i) when can_own(item(i)) and can_own(item(π(i))) (COMOBJ.DAT can-own bit; keys and fixed objects never), else owner(π(i)).");
            if (o.ShuffleWands) b.AppendLine("Charged wands: the wand and its spell object (spell and charges) are one unit, permuted among the wands the same way.");
            if (o.SwapEnchantments)
            {
                var cl = new List<string>();
                if (o.EnchantWeapons) { cl.Add("melee weapons"); cl.Add("launchers"); }
                if (o.EnchantArmour) cl.Add("armour");
                if (o.EnchantScrolls) cl.Add("spell scrolls");
                b.AppendFormat("Then, for each class ({0}): e = (enchanted bit, is-quantity, link, flag 11) of every object of the class, the same scope; e'(i) = e(σ(i)) with σ a new permutation drawn as above.\n",
                    cl.Count == 0 ? "none ticked" : string.Join(", ", cl));
            }
            b.AppendLine("Counts never change: Σ over the pool of [item = x] is the same before and after, for every x.");
            b.AppendLine().Append(Notation());
            return b.ToString().TrimEnd();
        }
    }
}
