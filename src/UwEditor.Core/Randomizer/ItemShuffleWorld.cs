using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace UwEditor.Core.Randomizer
{
    public sealed partial class ShuffleOptions
    {
        /// <summary>
        /// Sets the options to what the world holds: every weight, chance, mean, deviation and
        /// depth slope, the bars and curves, and the special counts. part names one tab
        /// ("weapons", "armour", "food", "runes", "wands", "scrolls", "rings") or null for all.
        /// </summary>
        public void FromWorld(GameSession s, ItemShuffle.GameCensus g, string part)
        {
            ItemShuffle.ApplyWorld(this, s, g, part);
        }
    }

    public static partial class ItemShuffle
    {
        /// <summary>What the levels hold, for the distributions' starting points and the world report.</summary>
        public sealed class GameCensus
        {
            /// <summary>Objects per item id (an ammunition stack counts once).</summary>
            public readonly Dictionary<int, int> Items = new Dictionary<int, int>();
            public int[] Charges = new int[ShuffleOptions.MaxCharges];
            public int[] WandCircles, ScrollCircles, PotionCircles;
            public readonly Dictionary<int, int> WandSpells = new Dictionary<int, int>(), ScrollSpells = new Dictionary<int, int>(), PotionSpells = new Dictionary<int, int>();
            public int[] WeaponQuality = new int[63], ArmourQuality = new int[63];
            /// <summary>Class 12 effects on weapons (no ammunition) and on armour, by level.</summary>
            public readonly List<(int Level, int Effect)> WeaponEnchants = new List<(int, int)>(), ArmourEnchants = new List<(int, int)>();
            public readonly Dictionary<int, int> WeaponsPerLevel = new Dictionary<int, int>(), ArmourPerLevel = new Dictionary<int, int>();
            /// <summary>UW2's special weapon effects 8-13 and Flameproof armour.</summary>
            public readonly Dictionary<int, int> WeaponSpecials = new Dictionary<int, int>();
            public int Flameproof;
            /// <summary>Worn effects (class × 16 + effect) and curses on armour and on rings.</summary>
            public readonly Dictionary<int, int> ArmourEffects = new Dictionary<int, int>(), RingEffects = new Dictionary<int, int>();
            public int ArmourTotal, ArmourCurses, RingTotal, RingMagic, RingCurses;
            public int[] CurseStrength = new int[8];
            public int Launchers, Ammo;
        }

        static void Inc<T>(Dictionary<T, int> d, T k) { int n; d.TryGetValue(k, out n); d[k] = n + 1; }

        public static GameCensus Census(GameSession s, Func<int, LevelModel> level)
        {
            int circles = SpellTables.Circles(s.IsUw2);
            var g = new GameCensus { WandCircles = new int[circles], ScrollCircles = new int[circles], PotionCircles = new int[circles] };
            var melee = new HashSet<int>(ItemTables.Axes.Concat(ItemTables.Swords(s.IsUw2)).Concat(ItemTables.Maces));
            var worn = new HashSet<int>(ItemTables.WornEffects(s).Select(e => e.Code));
            foreach (var slot in s.LevelSlots())
            {
                if (!slot.Present) continue;
                LevelModel m;
                try { m = level(slot.Level); } catch (Exception) { continue; }
                foreach (var o in m.Objects)
                {
                    if (o.Free || o.TileX < 0 || o.IsMobile) continue;
                    if (o.Parent >= 0 && m.Objects[o.Parent].IsTrapOrTrigger) continue;
                    bool f11 = ((o.Record[1] >> 3) & 1) != 0;
                    int q = Math.Max(1, Math.Min(63, o.Quality));
                    int parent = o.Parent >= 0 ? m.Objects[o.Parent].Item : -1;
                    string kind = KindOf(s, o, f11);
                    if (kind != null || o.Item == 0x120) Inc(g.Items, o.Item);
                    bool special = o.Enchanted && o.IsQuantity && o.Link >= 0x200 && !f11;
                    int code = o.Link & 0x1FF, major = code >> 4;
                    bool launcher = ItemTables.Launchers.Contains(o.Item), ammo = ItemTables.Ammo.Contains(o.Item);
                    if (launcher) g.Launchers++;
                    if (ammo) g.Ammo++;
                    if ((melee.Contains(o.Item) || launcher) && !ItemTables.KeptWeapons.Contains(o.Item))
                    {
                        g.WeaponQuality[q - 1]++;
                        Inc(g.WeaponsPerLevel, slot.Level);
                        if (special && major == 12)
                        {
                            int e = code & 15;
                            if (s.IsUw2 && e >= 8) Inc(g.WeaponSpecials, 0xC0 | e);
                            else g.WeaponEnchants.Add((slot.Level, e));
                        }
                    }
                    if (kind == "armour")
                    {
                        g.ArmourQuality[q - 1]++;
                        g.ArmourTotal++;
                        Inc(g.ArmourPerLevel, slot.Level);
                        if (special && major == 12) g.ArmourEnchants.Add((slot.Level, code & 15));
                        else if (special && major == 9) { g.ArmourCurses++; g.CurseStrength[Math.Max(1, Math.Min(8, code & 15)) - 1]++; }
                        else if (special && worn.Contains(code)) Inc(g.ArmourEffects, code);
                        if (special && code == 0x36) g.Flameproof++;
                    }
                    if (kind == "ring")
                    {
                        g.RingTotal++;
                        if (o.Enchanted && o.IsQuantity && o.Link >= 0x200) g.RingMagic++;
                        if (special && major == 9) { g.RingCurses++; g.CurseStrength[Math.Max(1, Math.Min(8, code & 15)) - 1]++; }
                        else if (special && worn.Contains(code)) Inc(g.RingEffects, code);
                    }
                    if (IsWandSpell(o, parent))
                    {
                        g.WandCircles[SpellTables.Circle(s.IsUw2, o.Link & 0x3F) - 1]++;
                        Inc(g.WandSpells, o.Link & 0x3F);
                        g.Charges[Math.Max(1, Math.Min(ShuffleOptions.MaxCharges, o.Quality)) - 1]++;
                    }
                    else if (IsChargedWand(o, f11)) { g.WandCircles[SpellTables.Circle(s.IsUw2, o.Link & 0x3F) - 1]++; Inc(g.WandSpells, o.Link & 0x3F); }
                    else if (IsSpellScroll(o, f11)) { g.ScrollCircles[SpellTables.Circle(s.IsUw2, o.Link & 0x3F) - 1]++; Inc(g.ScrollSpells, o.Link & 0x3F); }
                    else if (IsSpellPotion(s, o, f11)) { g.PotionCircles[SpellTables.Circle(s.IsUw2, o.Link & 0x3F) - 1]++; Inc(g.PotionSpells, o.Link & 0x3F); }
                }
            }
            return g;
        }

        static int Count(GameCensus g, int item) { int n; return g.Items.TryGetValue(item, out n) ? n : 0; }
        static int Count(GameCensus g, IEnumerable<int> items) { return items.Sum(i => Count(g, i)); }

        /// <summary>Weights from counts: each as a percentage of the largest.</summary>
        static double[] Weights(params int[] counts)
        {
            int max = counts.Length == 0 ? 0 : counts.Max();
            return counts.Select(c => max == 0 ? 50 : Math.Round(100.0 * c / max, 1)).ToArray();
        }

        static double Pct(int part, int whole) { return whole == 0 ? 0 : Math.Round(100.0 * part / whole, 1); }

        /// <summary>
        /// Fits a tab's enchantment rows to the world: the depth slider from a weighted least
        /// squares line through each level's enchanted share (rate = b0 + b1 × depth, slider =
        /// b1 / b0), each kind's chance at depth 0, the mean and deviation of its strengths (the
        /// depth shift taken out), its strength bars, and its chance per level (UW1 bars) or per
        /// level within a world (UW2 curve).
        /// </summary>
        static double FitEnchants(GameSession s, List<EnchantGroup> groups, List<(int Level, int Effect)> enchants, Dictionary<int, int> perLevel, out double strengthDepth)
        {
            bool uw2 = s.IsUw2;
            double sw = 0, sx = 0, sy = 0, sxx = 0, sxy = 0;
            foreach (var kv in perLevel)
            {
                double x = DepthOf(uw2, kv.Key), w = kv.Value, y = (double)enchants.Count(e => e.Level == kv.Key) / kv.Value;
                sw += w; sx += w * x; sy += w * y; sxx += w * x * x; sxy += w * x * y;
            }
            double adj = 0;
            if (sw > 0)
            {
                double den = sw * sxx - sx * sx;
                double b1 = Math.Abs(den) < 1e-12 ? 0 : (sw * sxy - sx * sy) / den, b0 = (sy - b1 * sx) / sw;
                adj = b0 > 1e-9 ? Math.Max(0, Math.Min(100, 100 * b1 / b0)) : (b1 > 0 ? 100 : 0);
            }
            // the strength slope: normalised strength (0 weakest, 1 strongest) against depth, all kinds together
            double tw = 0, tx = 0, ty = 0, txx = 0, txy = 0;
            foreach (var e in enchants)
            {
                var g0 = groups.FirstOrDefault(gr => Array.IndexOf(gr.Effects, e.Effect) >= 0);
                if (g0 == null || g0.Effects.Length < 2) continue;
                double x = DepthOf(uw2, e.Level), y = (double)Array.IndexOf(g0.Effects, e.Effect) / (g0.Effects.Length - 1);
                tw++; tx += x; ty += y; txx += x * x; txy += x * y;
            }
            double tden = tw * txx - tx * tx;
            strengthDepth = tw < 2 || Math.Abs(tden) < 1e-12 ? 0 : Math.Round(Math.Max(0, Math.Min(100, 100 * (tw * txy - tx * ty) / tden)));
            double sd0 = strengthDepth;
            int total = perLevel.Values.Sum();
            double factor = total == 0 ? 1 : perLevel.Sum(kv => kv.Value * (1 + adj / 100 * DepthOf(uw2, kv.Key))) / total;
            int levels = s.Profile.LevelSlots;
            foreach (var g in groups)
            {
                int n = g.Effects.Length;
                var mine = enchants.Where(e => Array.IndexOf(g.Effects, e.Effect) >= 0).ToList();
                g.Probability = total == 0 ? 0 : Math.Round(Math.Min(100, 100.0 * mine.Count / total / factor), 1);
                if (mine.Count > 0)
                {
                    var k = mine.Select(e => Array.IndexOf(g.Effects, e.Effect) + 1.0).ToList();
                    // take the depth shift out of each strength, then its mean and spread
                    var r = mine.Select(e => Array.IndexOf(g.Effects, e.Effect) + 1.0 - sd0 / 100 * DepthOf(uw2, e.Level) * (n - 1)).ToList();
                    double mean = r.Average();
                    g.Mean = Math.Round(Math.Max(1, Math.Min(n, mean)), 1);
                    g.Deviation = mine.Count < 2 ? 0.5 : Math.Round(Math.Max(0.3, Math.Sqrt(r.Average(v => (v - mean) * (v - mean)))), 1);
                    var hist = new int[n];
                    foreach (var v in k) hist[(int)v - 1]++;
                    g.StrengthBars = Distributions.Scaled(hist);
                }
                else g.StrengthBars = g.StrengthFromSliders();
                if (uw2)
                {
                    g.DepthCurve = new List<CurvePoint>();
                    for (int x = 1; x <= 8; x++)
                    {
                        int all = perLevel.Where(kv => (kv.Key - 1) % 8 + 1 == x).Sum(kv => kv.Value);
                        int hit = mine.Count(e => (e.Level - 1) % 8 + 1 == x);
                        g.DepthCurve.Add(new CurvePoint(x, Pct(hit, all)));
                    }
                }
                else
                {
                    g.DepthBars = new double[levels];
                    for (int l = 1; l <= levels; l++) { int all; perLevel.TryGetValue(l, out all); g.DepthBars[l - 1] = Pct(mine.Count(e => e.Level == l), all); }
                }
            }
            return Math.Round(adj);
        }

        static void EffectBars(Dictionary<int, double> target, Dictionary<int, int> counts)
        {
            int max = counts.Count == 0 ? 0 : counts.Values.Max();
            foreach (var k in target.Keys.ToList()) { int n; counts.TryGetValue(k, out n); target[k] = max == 0 ? 0 : Math.Round(100.0 * n / max, 1); }
        }

        public static void ApplyWorld(ShuffleOptions o, GameSession s, GameCensus g, string part)
        {
            bool all = part == null;
            if (all || part == "weapons")
            {
                var w = Weights(Count(g, ItemTables.Axes), Count(g, ItemTables.Swords(s.IsUw2)), Count(g, ItemTables.Maces), g.Launchers + g.Ammo);
                o.Axes = w[0]; o.Swords = w[1]; o.Maces = w[2]; o.Ranged = w[3];
                o.LauncherShare = Pct(g.Launchers, g.Launchers + g.Ammo);
                double sdw;
                o.WeaponDepth = FitEnchants(s, o.WeaponEnchants, g.WeaponEnchants, g.WeaponsPerLevel, out sdw);
                o.WeaponStrengthDepth = sdw;
                foreach (var sp in o.WeaponSpecialList) { int n; g.WeaponSpecials.TryGetValue(sp.Special, out n); sp.Count = n; }
                o.WeaponSpecials = g.WeaponSpecials.Count > 0;
                o.WeaponQualityCurve = Distributions.CurveOf(g.WeaponQuality, 1, 8);
            }
            if (all || part == "armour")
            {
                int leather = 0, mail = 0, plate = 0;
                for (int sl = 0; sl < 5; sl++) { leather += Count(g, ItemTables.Armour[sl, 0]); mail += Count(g, ItemTables.Armour[sl, 1]); plate += Count(g, ItemTables.Armour[sl, 2]); }
                var w = Weights(leather, mail, plate, Count(g, ItemTables.ShieldIds));
                o.Leather = w[0]; o.Mail = w[1]; o.Plate = w[2]; o.Shields = w[3];
                double sda;
                o.ArmourDepth = FitEnchants(s, o.ArmourEnchants, g.ArmourEnchants, g.ArmourPerLevel, out sda);
                o.ArmourStrengthDepth = sda;
                int effects = g.ArmourEffects.Values.Sum();
                o.ArmourEffectChance = Pct(effects + g.ArmourCurses, g.ArmourTotal);
                o.ArmourCurseShare = Pct(g.ArmourCurses, effects + g.ArmourCurses);
                EffectBars(o.ArmourEffects, g.ArmourEffects);
                // Flameproof comes with the other effects now; the special adds more only when asked
                foreach (var sp in o.ArmourSpecialList) sp.Count = 0;
                o.ArmourSpecials = false;
                o.ArmourQualityCurve = Distributions.CurveOf(g.ArmourQuality, 1, 8);
                if (g.CurseStrength.Sum() > 0) o.CurseStrength = Distributions.Scaled(g.CurseStrength);
            }
            if (all || part == "food")
            {
                foreach (var k in o.FoodWeights.Keys.ToList()) o.FoodWeights[k] = 0;
                var fw = Weights(o.FoodWeights.Keys.OrderBy(k => k).Select(k => Count(g, k)).ToArray());
                int i = 0; foreach (var k in o.FoodWeights.Keys.OrderBy(k => k).ToList()) o.FoodWeights[k] = fw[i++];
                var lk = o.LightWeights.Keys.OrderBy(k => k).ToList();
                var lw = Weights(lk.Select(k => Count(g, k) + Count(g, k + 4)).ToArray());
                for (i = 0; i < lk.Count; i++) o.LightWeights[lk[i]] = lw[i];
            }
            if (all || part == "runes")
            {
                var rw = Weights(ItemTables.Runes.Select(r => Count(g, r)).ToArray());
                for (int i = 0; i < ItemTables.Runes.Length; i++) o.RuneWeights[ItemTables.Runes[i]] = rw[i];
            }
            if (all || part == "wands")
            {
                o.WandCharges = Distributions.Scaled(g.Charges);
                o.WandCircles = Distributions.Scaled(g.WandCircles);
            }
            if (all || part == "scrolls")
            {
                o.ScrollCircles = Distributions.Scaled(g.ScrollCircles);
                o.PotionCircles = Distributions.Scaled(g.PotionCircles);
            }
            if (all || part == "rings")
            {
                var ids = o.RingWeights.Keys.OrderBy(k => k).ToList();
                var rw = Weights(ids.Select(k => Count(g, k)).ToArray());
                for (int i = 0; i < ids.Count; i++) o.RingWeights[ids[i]] = rw[i];
                o.RingMagic = Pct(g.RingMagic, g.RingTotal);
                int effects = g.RingEffects.Values.Sum();
                o.RingCurseShare = Pct(g.RingCurses, effects + g.RingCurses);
                EffectBars(o.RingEffects, g.RingEffects);
                if (g.CurseStrength.Sum() > 0) o.CurseStrength = Distributions.Scaled(g.CurseStrength);
            }
        }

        /// <summary>The world in words: what the shuffles work with, as the levels hold it.</summary>
        public static string WorldReport(GameSession s, Func<int, LevelModel> level)
        {
            var g = Census(s, level);
            var o = ShuffleOptions.Defaults(s);
            var b = new StringBuilder();
            Func<int, string> name = i => s.Names.Get(i);
            Action<string, IEnumerable<int>> items = (title, ids) =>
            {
                var l = ids.Select(i => (i, n: Count(g, i))).Where(t => t.n > 0).ToList();
                b.AppendFormat("{0} ({1}): {2}\n", title, l.Sum(t => t.n), l.Count == 0 ? "none" : string.Join(", ", l.Select(t => name(t.i) + " " + t.n)));
            };
            b.AppendLine("The world as the levels hold it (objects in traps are templates and not counted).").AppendLine();
            items("Axes", ItemTables.Axes);
            items("Swords and daggers", ItemTables.Swords(s.IsUw2));
            items("Maces and clubs", ItemTables.Maces);
            items("Launchers", ItemTables.Launchers);
            items("Ammunition (stacks)", ItemTables.Ammo);
            Action<string, List<EnchantGroup>, List<(int Level, int Effect)>, Dictionary<int, int>, bool> ench = (title, groups, list, per, armour) =>
            {
                int total = per.Values.Sum();
                b.AppendFormat("{0} enchanted: {1} of {2} ({3}%)", title, list.Count, total, Pct(list.Count, total));
                var byName = list.GroupBy(e => (s.Strings.Get(6, 0x1C0 + (armour ? 0x10 : 0) + e.Effect) ?? "").Trim()).OrderBy(x => x.Key);
                if (list.Count > 0) b.Append(": " + string.Join(", ", byName.Select(x => x.Key + " " + x.Count())));
                b.AppendLine();
                b.Append("  by level: " + string.Join(", ", per.OrderBy(kv => kv.Key).Select(kv => "L" + kv.Key + " " + list.Count(e => e.Level == kv.Key) + "/" + kv.Value))).AppendLine();
            };
            ench("Weapons", o.WeaponEnchants, g.WeaponEnchants, g.WeaponsPerLevel, false);
            if (g.WeaponSpecials.Count > 0) b.AppendLine("Special weapons: " + string.Join(", ", g.WeaponSpecials.OrderBy(k => k.Key).Select(k => (s.Strings.Get(6, 0x1C0 + (k.Key & 15)) ?? "").Trim() + " " + k.Value)));
            b.AppendLine();
            var leather = Enumerable.Range(0, 5).Select(i => ItemTables.Armour[i, 0]); var mail = Enumerable.Range(0, 5).Select(i => ItemTables.Armour[i, 1]); var plate = Enumerable.Range(0, 5).Select(i => ItemTables.Armour[i, 2]);
            items("Leather", leather); items("Mail", mail); items("Plate", plate); items("Shields", ItemTables.ShieldIds);
            ench("Armour", o.ArmourEnchants, g.ArmourEnchants, g.ArmourPerLevel, true);
            b.AppendFormat("Armour with other effects: {0}{1}; cursed: {2}\n", g.ArmourEffects.Values.Sum(),
                g.ArmourEffects.Count == 0 ? "" : " (" + string.Join(", ", g.ArmourEffects.OrderBy(k => k.Key).Select(k => (s.Strings.Get(6, k.Key) ?? "").Trim() + " " + k.Value)) + ")", g.ArmourCurses);
            b.AppendLine();
            items("Food", ItemTables.Food(s));
            items("Lights", ItemTables.LightKinds(s).SelectMany(k => new[] { k, k + 4 }));
            items("Tools", new[] { ItemTables.Pole, ItemTables.FishingPole, ItemTables.Thread(s.IsUw2) });
            items("Runestones", ItemTables.Runes);
            b.AppendLine();
            Action<string, Dictionary<int, int>> spells = (title, d) =>
                b.AppendFormat("{0} ({1}): {2}\n", title, d.Values.Sum(), d.Count == 0 ? "none" : string.Join(", ", d.OrderBy(k => SpellTables.Circle(s.IsUw2, k.Key)).ThenBy(k => k.Key).Select(k => SpellTables.Name(s, k.Key) + " (" + SpellTables.CircleLabel(s.IsUw2, SpellTables.Circle(s.IsUw2, k.Key)) + ") " + k.Value)));
            spells("Wand spells", g.WandSpells);
            var ch = g.Charges.Select((n, i) => (n, c: i + 1)).Where(t => t.n > 0).ToList();
            b.AppendLine("Wand charges: " + (ch.Count == 0 ? "none" : string.Join(", ", ch.Select(t => t.c + "×" + t.n))));
            spells("Scroll spells", g.ScrollSpells);
            spells("Potion spells", g.PotionSpells);
            b.AppendLine();
            items("Rings", ItemTables.Rings(s));
            b.AppendFormat("Rings with an effect: {0} of {1}{2}; cursed: {3}\n", g.RingMagic, g.RingTotal,
                g.RingEffects.Count == 0 ? "" : " (" + string.Join(", ", g.RingEffects.OrderBy(k => k.Key).Select(k => (s.Strings.Get(6, k.Key) ?? "").Trim() + " " + k.Value)) + ")", g.RingCurses);
            Action<string, int[]> quality = (title, h) =>
            {
                int n = h.Sum();
                if (n == 0) return;
                int mode = Array.IndexOf(h, h.Max()) + 1;
                double mean = h.Select((c, i) => (double)c * (i + 1)).Sum() / n;
                b.AppendFormat("{0} quality: mean {1:0.0}, most often {2} ({3} of {4})\n", title, mean, mode, h.Max(), n);
            };
            b.AppendLine();
            quality("Weapon", g.WeaponQuality);
            quality("Armour", g.ArmourQuality);
            return b.ToString();
        }
    }
}
