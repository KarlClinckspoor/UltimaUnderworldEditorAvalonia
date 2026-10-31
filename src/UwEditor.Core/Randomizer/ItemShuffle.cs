using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.Json;
using UwEditor.Core.Editing;

namespace UwEditor.Core.Randomizer
{
    /// <summary>One kind of enchantment (Accuracy, Damage, Protection, Toughness): its effects from weakest to strongest and how they are rolled.</summary>
    public sealed class EnchantGroup
    {
        public string Name { get; set; }
        /// <summary>Class 12 effects, weakest first (STRINGS.PAK block 6 0x1C0 + effect, + 0x10 for armour).</summary>
        public int[] Effects { get; set; }
        /// <summary>Chance (0-100) that an object gets this enchantment.</summary>
        public double Probability { get; set; } = 8;
        /// <summary>Mean strength, 1 = the weakest.</summary>
        public double Mean { get; set; } = 2;
        public double Deviation { get; set; } = 1;
    }

    /// <summary>A special enchantment to put on a number of objects (UW2's special weapons, Flameproof armour).</summary>
    public sealed class SpecialChoice
    {
        public string Name { get; set; }
        /// <summary>The special value: 0x200 + this is the link (0xC8-0xCD a UW2 special weapon, 0x36 Flameproof).</summary>
        public int Special { get; set; }
        public bool On { get; set; } = true;
        public int Count { get; set; } = 1;
        public string Note { get; set; } = "";
    }

    /// <summary>The item shuffle's settings, saved between runs.</summary>
    public sealed class ShuffleOptions
    {
        public string Game { get; set; } = "UW1";
        public int Seed { get; set; } = 1;
        public bool IncludeInventories { get; set; } = true;
        /// <summary>Spells and other class effects already on objects stay; only plain and class 12 objects are rolled.</summary>
        public bool KeepOtherEnchantments { get; set; } = true;

        // weapons
        public bool ShuffleWeapons { get; set; } = true;
        public double Axes { get; set; } = 50;
        public double Swords { get; set; } = 50;
        public double Maces { get; set; } = 50;
        public double Ranged { get; set; } = 50;
        public List<EnchantGroup> WeaponEnchants { get; set; } = new List<EnchantGroup>();
        /// <summary>0-100: how much deeper levels raise the chance and the strength of enchantments.</summary>
        public double WeaponDepth { get; set; } = 30;
        public bool WeaponSpecials { get; set; }
        public List<SpecialChoice> WeaponSpecialList { get; set; } = new List<SpecialChoice>();
        /// <summary>± this round each object's own quality (0: unchanged).</summary>
        public int WeaponQualitySpread { get; set; } = 10;
        /// <summary>Of the ranged objects, the share (0-100) that are bows, slings and crossbows; the rest are ammunition.</summary>
        public double LauncherShare { get; set; } = 25;

        // armour
        public bool ShuffleArmour { get; set; } = true;
        public double Leather { get; set; } = 50;
        public double Mail { get; set; } = 50;
        public double Plate { get; set; } = 50;
        public double Shields { get; set; } = 50;
        public List<EnchantGroup> ArmourEnchants { get; set; } = new List<EnchantGroup>();
        public double ArmourDepth { get; set; } = 30;
        public bool ArmourSpecials { get; set; } = true;
        public List<SpecialChoice> ArmourSpecialList { get; set; } = new List<SpecialChoice>();
        public int ArmourQualitySpread { get; set; } = 10;

        // food, light, tools
        public bool ShuffleFood { get; set; } = true;
        public Dictionary<int, double> FoodWeights { get; set; } = new Dictionary<int, double>();
        public bool ShuffleLights { get; set; } = true;
        public Dictionary<int, double> LightWeights { get; set; } = new Dictionary<int, double>();
        public int MinThread { get; set; } = 5;
        public int MinPole { get; set; } = 3;
        public int MinFishingPole { get; set; } = 3;

        // runestones
        public bool ShuffleRunes { get; set; } = true;
        public Dictionary<int, double> RuneWeights { get; set; } = new Dictionary<int, double>();
        public Dictionary<int, int> RuneMinimum { get; set; } = new Dictionary<int, int>();

        static readonly JsonSerializerOptions Json = new JsonSerializerOptions { WriteIndented = true };
        public string ToJson() { return JsonSerializer.Serialize(this, Json); }
        public static ShuffleOptions FromJson(string s) { return JsonSerializer.Deserialize<ShuffleOptions>(s, Json); }

        /// <summary>The defaults for a game: its enchantment names, food, lights and runes.</summary>
        public static ShuffleOptions Defaults(GameSession s)
        {
            var o = new ShuffleOptions { Game = s.IsUw2 ? "UW2" : "UW1", Seed = Environment.TickCount & 0x7FFFFFFF };
            o.WeaponEnchants = Groups(s, false);
            o.ArmourEnchants = Groups(s, true);
            if (s.IsUw2)
            {
                // effects 8-13 are the special weapons (COMBAT.C player_attack, specweap = effect - 7)
                string[] notes = { "heals the player by the damage done", "repels the undead", "a fireball at the target", "holds the target", "opens a door (on a critical)", "opens a door (every hit)" };
                for (int e = 8; e <= 13; e++)
                    o.WeaponSpecialList.Add(new SpecialChoice { Name = (s.Strings.Get(6, 0x1C0 + e) ?? "special " + e).Trim(), Special = 0xC0 | e, Count = 1, Note = notes[e - 8] });
            }
            o.ArmourSpecialList.Add(new SpecialChoice { Name = (s.Strings.Get(6, 0x36) ?? "Flameproof").Trim(), Special = 0x36, Count = 1, Note = "class 3 effect 6, worn" });
            foreach (int f in ItemTables.Food(s)) o.FoodWeights[f] = 50;
            foreach (int l in ItemTables.LightKinds(s)) o.LightWeights[l] = 50;
            foreach (int r in ItemTables.Runes) { o.RuneWeights[r] = 50; o.RuneMinimum[r] = 1; }
            return o;
        }

        /// <summary>The class 12 effects grouped by their name's last word (Minor Accuracy … Unsurpassed Accuracy), weakest first.</summary>
        static List<EnchantGroup> Groups(GameSession s, bool armour)
        {
            var groups = new List<EnchantGroup>();
            int last = s.IsUw2 && !armour ? 7 : 15;     // UW2's weapon effects 8-15 are the special weapons
            for (int e = 0; e <= last; e++)
            {
                string n = (s.Strings.Get(6, 0x1C0 + (armour ? 0x10 : 0) + e) ?? "").Trim();
                string word = n.Split(' ').LastOrDefault() ?? n;
                if (word.Length == 0) continue;
                var g = groups.FirstOrDefault(x => x.Name == word);
                if (g == null) groups.Add(g = new EnchantGroup { Name = word, Effects = new int[0] });
                g.Effects = g.Effects.Concat(new[] { e }).ToArray();
            }
            foreach (var g in groups) { g.Mean = Math.Max(1, g.Effects.Length / 4.0); g.Deviation = Math.Max(0.5, g.Effects.Length / 6.0); }
            return groups;
        }
    }

    /// <summary>The item ids the shuffle works with, by game.</summary>
    public static class ItemTables
    {
        public static readonly int[] Axes = { 0x00, 0x01, 0x02, 0x0B };
        public static int[] Swords(bool uw2) { return new[] { 0x03, 0x04, 0x05, 0x06, 0x0C, 0x0D }; }
        public static readonly int[] Maces = { 0x07, 0x08, 0x09, 0x0E };
        public static readonly int[] Launchers = { 0x18, 0x19, 0x1A, 0x1F };
        public static readonly int[] Ammo = { 0x10, 0x11, 0x12 };
        /// <summary>Weapons the shuffle never touches: UW1's Caliburn (the shiny sword), UW2's jewelled dagger (Altara's).</summary>
        public static readonly int[] KeptWeapons = { 0x0A };

        /// <summary>Armour by slot (body, legs, hands, feet, head) and material (leather, mail, plate).</summary>
        public static readonly int[,] Armour = { { 0x20, 0x21, 0x22 }, { 0x23, 0x24, 0x25 }, { 0x26, 0x27, 0x28 }, { 0x29, 0x2A, 0x2B }, { 0x2C, 0x2D, 0x2E } };
        public static readonly int[] ShieldIds = { 0x3B, 0x3C, 0x3D, 0x3E, 0x3F };

        public static int Slot(int item) { for (int s = 0; s < 5; s++) for (int m = 0; m < 3; m++) if (Armour[s, m] == item) return s; return -1; }

        public static IEnumerable<int> Food(GameSession s)
        {
            for (int i = 0xB0; i < 0xC0; i++)
            {
                if ((ItemCategories.Of(i, s.Kind) & ItemCategory.Food) == 0) continue;
                if (!s.IsUw2 && i == 0xBF) continue;   // UW1's bottle of wine is the Wine of Compassion
                yield return i;
            }
        }

        /// <summary>The unlit light kinds (0x90-0x93); the lit one is 4 more. UW1's taper is the Taper of Sacrifice and stays.</summary>
        public static IEnumerable<int> LightKinds(GameSession s)
        {
            for (int i = 0x90; i < 0x94; i++) if (s.IsUw2 || i != 0x93) yield return i;
        }

        public static readonly int[] Runes = Enumerable.Range(0xE8, 24).ToArray();
        public const int Pole = 0xD8, FishingPole = 0x12B;
        public static int Thread(bool uw2) { return uw2 ? 0x12C : 0x11C; }
    }

    /// <summary>What a shuffle did, or would do.</summary>
    public sealed class ShuffleResult
    {
        public readonly Dictionary<int, List<ItemRewrite>> ByLevel = new Dictionary<int, List<ItemRewrite>>();
        /// <summary>The same objects as they were: applying these undoes the shuffle.</summary>
        public readonly Dictionary<int, List<ItemRewrite>> Original = new Dictionary<int, List<ItemRewrite>>();
        public readonly StringBuilder Report = new StringBuilder();
        public int Changed { get { return ByLevel.Values.Sum(l => l.Count); } }
    }

    /// <summary>
    /// Shuffles item properties in place: every weapon, armour piece, food, light and runestone
    /// stays where it lies but may become another item of its group, by the weights chosen, and
    /// weapons and armour get their quality spread and enchantments rolled again (class 12, by
    /// kind: chance, mean and deviation of the strength, raised with depth). Special enchantments
    /// and minimum numbers of tools and runes are then made from what was shuffled. Caliburn and
    /// the other quest objects are never touched. The same seed on the same levels gives the same
    /// result, so a preview and the shuffle agree.
    /// </summary>
    public static class ItemShuffle
    {
        sealed class Cand
        {
            public int Level, Index;
            public ObjectInfo O;
            public ItemRewrite R;
            public bool Touched;
            public double Depth;
        }

        public static ShuffleResult Run(GameSession s, Func<int, LevelModel> level, ShuffleOptions opt)
        {
            var res = new ShuffleResult();
            var rng = new Random(opt.Seed);
            bool uw2 = s.IsUw2;
            var all = new List<Cand>();
            foreach (var slot in s.LevelSlots())
            {
                if (!slot.Present) continue;
                LevelModel m;
                try { m = level(slot.Level); } catch (Exception) { continue; }
                double depth = uw2 ? ((slot.Level - 1) % 8) / 7.0 : Math.Min(1.0, (slot.Level - 1) / 7.0);
                foreach (var o in m.Objects)
                {
                    if (o.Free || o.TileX < 0 || o.IsMobile) continue;
                    if (!opt.IncludeInventories && o.Parent >= 0 && m.Objects[o.Parent].IsCritter) continue;
                    // objects inside traps are templates (create object): left alone
                    if (o.Parent >= 0 && m.Objects[o.Parent].IsTrapOrTrigger) continue;
                    all.Add(new Cand
                    {
                        Level = slot.Level, Index = o.Index, O = o, Depth = depth,
                        R = Of(o),
                    });
                }
            }
            var before = Count(all, false);

            if (opt.ShuffleWeapons) Weapons(s, opt, rng, all, res);
            if (opt.ShuffleArmour) Armour(s, opt, rng, all, res);
            if (opt.ShuffleFood || opt.ShuffleLights) FoodAndLights(s, opt, rng, all, res);
            Tools(s, opt, rng, all, res);
            if (opt.ShuffleRunes) Runes(s, opt, rng, all, res);

            foreach (var c in all.Where(c => c.Touched && Differs(c)))
            {
                List<ItemRewrite> l;
                if (!res.ByLevel.TryGetValue(c.Level, out l)) res.ByLevel[c.Level] = l = new List<ItemRewrite>();
                l.Add(c.R);
                if (!res.Original.TryGetValue(c.Level, out l)) res.Original[c.Level] = l = new List<ItemRewrite>();
                l.Add(Of(c.O));
            }
            var after = Count(all, true);
            res.Report.Insert(0, string.Format("Seed {0}: {1} objects change on {2} levels.\n\n", opt.Seed, res.Changed, res.ByLevel.Count));
            res.Report.AppendLine().AppendLine("Item counts before → after:");
            foreach (var k in before.Keys.Union(after.Keys).OrderBy(x => x))
            {
                int b, a;
                before.TryGetValue(k, out b); after.TryGetValue(k, out a);
                if (a != b) res.Report.AppendFormat("  0x{0:X3} {1,-24} {2,4} → {3,4}\n", k, s.Names.Get(k), b, a);
            }
            return res;
        }

        static ItemRewrite Of(ObjectInfo o)
        {
            return new ItemRewrite { Index = o.Index, Item = o.Item, Quality = o.Quality, Flags = (o.Record[1] >> 1) & 7, Link = o.Link, Enchanted = o.Enchanted, IsQuantity = o.IsQuantity };
        }

        static bool Differs(Cand c)
        {
            var o = c.O; var r = c.R;
            return r.Item != o.Item || r.Quality != o.Quality || r.Flags != ((o.Record[1] >> 1) & 7) || r.Enchanted != o.Enchanted || r.IsQuantity != o.IsQuantity || (o.IsQuantity && r.Link != o.Link);
        }

        static Dictionary<int, int> Count(List<Cand> all, bool after)
        {
            var d = new Dictionary<int, int>();
            foreach (var c in all) { int it = after ? c.R.Item : c.O.Item; int n; d.TryGetValue(it, out n); d[it] = n + 1; }
            return d;
        }

        static int Pick(Random rng, IList<double> weights)
        {
            double sum = weights.Sum(w => Math.Max(0, w));
            if (sum <= 0) return -1;
            double r = rng.NextDouble() * sum;
            for (int i = 0; i < weights.Count; i++) { r -= Math.Max(0, weights[i]); if (r < 0) return i; }
            return weights.Count - 1;
        }

        static double Normal(Random rng)
        {
            double u1 = 1.0 - rng.NextDouble(), u2 = rng.NextDouble();
            return Math.Sqrt(-2 * Math.Log(u1)) * Math.Cos(2 * Math.PI * u2);
        }

        static int Spread(Random rng, int q, int spread)
        {
            if (spread <= 0) return q;
            return Math.Max(1, Math.Min(63, q + rng.Next(-spread, spread + 1)));
        }

        /// <summary>Whether an object's enchantment is one the shuffle rolls again: none, or class 12 (flag 11 clear).</summary>
        static bool Rerollable(Cand c, ShuffleOptions opt)
        {
            // a link to other objects (contents, a spell object) is never replaced
            if (!c.O.IsQuantity && c.O.Link != 0) return false;
            if (!c.O.Enchanted) return true;
            bool flag11 = ((c.O.Record[1] >> 3) & 1) != 0;
            bool class12 = !flag11 && c.O.Link >= 0x200 && ((c.O.Link & 0x1FF) >> 4) == 12;
            return class12 || !opt.KeepOtherEnchantments;
        }

        /// <summary>Rolls a class 12 enchantment; true when one was given.</summary>
        static bool Enchant(Random rng, Cand c, List<EnchantGroup> groups, double depthAdj, Dictionary<string, int> tally)
        {
            double dep = depthAdj / 100.0 * c.Depth;
            foreach (var g in groups.OrderBy(x => rng.Next()))
            {
                if (g.Effects == null || g.Effects.Length == 0) continue;
                double p = Math.Min(1, g.Probability / 100.0 * (1 + dep));
                if (rng.NextDouble() >= p) continue;
                double mean = g.Mean + dep * (g.Effects.Length - 1);
                int strength = (int)Math.Round(mean + g.Deviation * Normal(rng));
                strength = Math.Max(1, Math.Min(g.Effects.Length, strength));
                c.R.Enchanted = true; c.R.IsQuantity = true; c.R.Flags &= ~4;
                c.R.Link = 0x200 | 0xC0 | g.Effects[strength - 1];
                string key = g.Name + " " + strength;
                int n; tally.TryGetValue(key, out n); tally[key] = n + 1;
                return true;
            }
            return false;
        }

        static void Plain(Cand c, int quantity = 1)
        {
            c.R.Enchanted = false; c.R.IsQuantity = true; c.R.Flags &= ~4;
            c.R.Link = Math.Max(1, Math.Min(0x1FF, quantity));
        }

        static void Weapons(GameSession s, ShuffleOptions opt, Random rng, List<Cand> all, ShuffleResult res)
        {
            bool uw2 = s.IsUw2;
            var types = new[] { ItemTables.Axes, ItemTables.Swords(uw2), ItemTables.Maces, ItemTables.Launchers.Concat(ItemTables.Ammo).ToArray() };
            var weights = new[] { opt.Axes, opt.Swords, opt.Maces, opt.Ranged };
            var weaponIds = new HashSet<int>(types.SelectMany(t => t));
            var cands = all.Where(c => weaponIds.Contains(c.O.Item) && !ItemTables.KeptWeapons.Contains(c.O.Item)).ToList();
            var tally = new Dictionary<string, int>();
            int enchanted = 0;
            if (weights.Sum() <= 0) { res.Report.AppendLine("Weapons: every weight is 0, so weapons were left as they are."); return; }
            foreach (var c in cands)
            {
                int t = Pick(rng, weights);
                bool wasAmmo = ItemTables.Ammo.Contains(c.O.Item);
                int item;
                bool ammo = false;
                if (t == 3)
                {
                    ammo = rng.NextDouble() * 100 >= opt.LauncherShare;
                    var set = ammo ? ItemTables.Ammo : ItemTables.Launchers;
                    item = set[rng.Next(set.Length)];
                }
                else item = types[t][rng.Next(types[t].Length)];
                c.R.Item = item;
                c.R.Quality = Spread(rng, c.O.Quality, opt.WeaponQualitySpread);
                c.Touched = true;
                if (ammo) { Plain(c, wasAmmo && c.O.IsQuantity && c.O.Link < 0x200 ? c.O.Link : rng.Next(3, 13)); continue; }
                if (!Rerollable(c, opt)) continue;
                if (Enchant(rng, c, opt.WeaponEnchants, opt.WeaponDepth, tally)) enchanted++;
                else Plain(c);
            }
            res.Report.AppendFormat("Weapons: {0} shuffled ({1} kept), {2} enchanted", cands.Count, uw2 ? "the jewelled dagger" : "Caliburn", enchanted);
            if (tally.Count > 0) res.Report.Append(": " + string.Join(", ", tally.OrderBy(k => k.Key).Select(k => k.Key + " ×" + k.Value)));
            res.Report.AppendLine(".");
            if (opt.WeaponSpecials && uw2)
            {
                var melee = cands.Where(c => Rerollable(c, opt) && !ItemTables.Launchers.Contains(c.R.Item) && !ItemTables.Ammo.Contains(c.R.Item)).OrderBy(x => rng.Next()).ToList();
                Specials(s, rng, melee, opt.WeaponSpecialList, res, "special weapon");
            }
        }

        static void Specials(GameSession s, Random rng, List<Cand> pool, List<SpecialChoice> list, ShuffleResult res, string what)
        {
            int k = 0;
            foreach (var sp in list.Where(x => x.On && x.Count > 0))
            {
                for (int i = 0; i < sp.Count; i++)
                {
                    if (k >= pool.Count) { res.Report.AppendLine("  Not enough objects left for every " + what + "."); return; }
                    var c = pool[k++];
                    c.R.Enchanted = true; c.R.IsQuantity = true; c.R.Flags &= ~4; c.R.Link = 0x200 | sp.Special; c.Touched = true;
                    res.Report.AppendFormat("  {0}: {1} on #{2}, level {3} ({4}, {5}).\n", sp.Name, s.Names.Get(c.R.Item), c.Index, c.Level, c.O.TileX, c.O.TileY);
                }
            }
        }

        static void Armour(GameSession s, ShuffleOptions opt, Random rng, List<Cand> all, ShuffleResult res)
        {
            var weights = new[] { opt.Leather, opt.Mail, opt.Plate, opt.Shields };
            if (weights.Sum() <= 0) { res.Report.AppendLine("Armour: every weight is 0, so armour was left as it is."); return; }
            var cands = all.Where(c => ItemTables.Slot(c.O.Item) >= 0 || ItemTables.ShieldIds.Contains(c.O.Item)).ToList();
            var tally = new Dictionary<string, int>();
            int enchanted = 0;
            foreach (var c in cands)
            {
                int t = Pick(rng, weights);
                if (t == 3) c.R.Item = ItemTables.ShieldIds[rng.Next(ItemTables.ShieldIds.Length)];
                else
                {
                    int slot = ItemTables.Slot(c.O.Item);
                    if (slot < 0) slot = rng.Next(5);
                    c.R.Item = ItemTables.Armour[slot, t];
                }
                c.R.Quality = Spread(rng, c.O.Quality, opt.ArmourQualitySpread);
                c.Touched = true;
                if (!Rerollable(c, opt)) continue;
                if (Enchant(rng, c, opt.ArmourEnchants, opt.ArmourDepth, tally)) enchanted++;
                else Plain(c);
            }
            res.Report.AppendFormat("Armour: {0} shuffled (quest armour, crowns and the talisman shield kept), {1} enchanted", cands.Count, enchanted);
            if (tally.Count > 0) res.Report.Append(": " + string.Join(", ", tally.OrderBy(k => k.Key).Select(k => k.Key + " ×" + k.Value)));
            res.Report.AppendLine(".");
            if (opt.ArmourSpecials)
                Specials(s, rng, cands.Where(c => !c.R.Enchanted && Rerollable(c, opt)).OrderBy(x => rng.Next()).ToList(), opt.ArmourSpecialList, res, "special armour");
        }

        static void FoodAndLights(GameSession s, ShuffleOptions opt, Random rng, List<Cand> all, ShuffleResult res)
        {
            if (opt.ShuffleFood)
            {
                var ids = opt.FoodWeights.Keys.OrderBy(k => k).ToList();
                var w = ids.Select(k => opt.FoodWeights[k]).ToList();
                var cands = all.Where(c => ids.Contains(c.O.Item)).ToList();
                if (w.Sum() > 0)
                    foreach (var c in cands) { c.R.Item = ids[Pick(rng, w)]; c.Touched = true; }
                res.Report.AppendFormat("Food: {0} shuffled.\n", w.Sum() > 0 ? cands.Count : 0);
            }
            if (opt.ShuffleLights)
            {
                var kinds = opt.LightWeights.Keys.OrderBy(k => k).ToList();
                var w = kinds.Select(k => opt.LightWeights[k]).ToList();
                var cands = all.Where(c => kinds.Contains(c.O.Item) || kinds.Contains(c.O.Item - 4)).ToList();
                if (w.Sum() > 0)
                    foreach (var c in cands)
                    {
                        bool lit = c.O.Item >= 0x94;
                        c.R.Item = kinds[Pick(rng, w)] + (lit ? 4 : 0);
                        c.Touched = true;
                    }
                res.Report.AppendFormat("Lights: {0} shuffled (lit ones stay lit).\n", w.Sum() > 0 ? cands.Count : 0);
            }
        }

        /// <summary>Makes sure of a minimum of threads, poles and fishing poles by turning shuffled food and lights into them.</summary>
        static void Tools(GameSession s, ShuffleOptions opt, Random rng, List<Cand> all, ShuffleResult res)
        {
            var food = new HashSet<int>(ItemTables.Food(s));
            var lights = new HashSet<int>(ItemTables.LightKinds(s).SelectMany(k => new[] { k, k + 4 }));
            var pool = all.Where(c => (food.Contains(c.R.Item) || lights.Contains(c.R.Item)) && c.O.Parent < 0).OrderBy(c => food.Contains(c.R.Item) ? 0 : 1).ThenBy(c => rng.Next()).ToList();
            int used = 0;
            foreach (var (item, min, name) in new[] { (ItemTables.Thread(s.IsUw2), opt.MinThread, "thread"), (ItemTables.Pole, opt.MinPole, "pole"), (ItemTables.FishingPole, opt.MinFishingPole, "fishing pole") })
            {
                int have = all.Count(c => c.R.Item == item);
                int add = Math.Max(0, min - have);
                int made = 0;
                for (; made < add && used < pool.Count; made++)
                {
                    var c = pool[used++];
                    c.R.Item = item; c.R.Quality = 40; Plain(c); c.Touched = true;
                }
                res.Report.AppendFormat("Tools: {0} {1}{2} found, {3} made from food and lights{4}.\n", have, name, have == 1 ? "" : "s", made, made < add ? " (not enough food and lights for the minimum)" : "");
            }
        }

        static void Runes(GameSession s, ShuffleOptions opt, Random rng, List<Cand> all, ShuffleResult res)
        {
            var ids = ItemTables.Runes.ToList();
            var w = ids.Select(k => opt.RuneWeights.ContainsKey(k) ? opt.RuneWeights[k] : 0).ToList();
            var cands = all.Where(c => ids.Contains(c.O.Item)).ToList();
            if (w.Sum() <= 0) { res.Report.AppendLine("Runestones: every weight is 0, so they were left as they are."); return; }
            foreach (var c in cands) { c.R.Item = ids[Pick(rng, w)]; c.Touched = true; }
            // minimums: take from the runes with the most to spare
            int moved = 0;
            foreach (int r in ids.OrderBy(x => rng.Next()))
            {
                int min = opt.RuneMinimum.ContainsKey(r) ? opt.RuneMinimum[r] : 0;
                while (cands.Count(c => c.R.Item == r) < min)
                {
                    var spare = cands.Where(c => c.R.Item != r).GroupBy(c => c.R.Item)
                        .Select(g => new { Rune = g.Key, Spare = g.Count() - (opt.RuneMinimum.ContainsKey(g.Key) ? opt.RuneMinimum[g.Key] : 0), Items = g.ToList() })
                        .Where(g => g.Spare > 0).OrderByDescending(g => g.Spare).FirstOrDefault();
                    if (spare == null) break;
                    spare.Items[rng.Next(spare.Items.Count)].R.Item = r;
                    moved++;
                }
            }
            var missing = ids.Where(r => cands.Count(c => c.R.Item == r) < (opt.RuneMinimum.ContainsKey(r) ? opt.RuneMinimum[r] : 0)).ToList();
            res.Report.AppendFormat("Runestones: {0} shuffled, {1} changed again for the minimums{2}.\n", cands.Count, moved,
                missing.Count > 0 ? "; too few runestones for the minimums of " + string.Join(", ", missing.Select(m => s.Names.Get(m))) : "");
        }

        /// <summary>Applies a result to the level editors, one undoable change per level.</summary>
        public static void Apply(ShuffleResult r, LevelEdits edits, int seed)
        {
            foreach (var kv in r.ByLevel) edits.For(kv.Key).RewriteItems(kv.Value, "Shuffle item properties (seed " + seed + ")");
        }

        /// <summary>Puts every object a shuffle changed back as it was (itself one undoable change per level).</summary>
        public static void Revert(ShuffleResult r, LevelEdits edits)
        {
            foreach (var kv in r.Original) edits.For(kv.Key).RewriteItems(kv.Value, "Undo item shuffle");
        }
    }
}
