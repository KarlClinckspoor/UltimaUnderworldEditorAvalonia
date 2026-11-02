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

        /// <summary>The strength comes from <see cref="StrengthBars"/> (a weight per strength, weakest first) instead of the mean and deviation.</summary>
        public bool CustomStrength { get; set; }
        public double[] StrengthBars { get; set; }
        /// <summary>The chance comes from the depth curve instead of the chance and depth sliders: UW1 <see cref="DepthBars"/> (a chance % per level), UW2 <see cref="DepthCurve"/> (x the level within its world 1-8, y the chance %).</summary>
        public bool CustomDepth { get; set; }
        public double[] DepthBars { get; set; }
        public List<CurvePoint> DepthCurve { get; set; }

        /// <summary>The strength bars the sliders give (what they start from).</summary>
        public double[] StrengthFromSliders() { return Distributions.NormalBars(Effects.Length, Mean, Deviation); }

        /// <summary>UW1: the chance per level (1-9) the chance and depth sliders give.</summary>
        public double[] DepthBarsFromSliders(double depthAdj, int levels)
        {
            var b = new double[levels];
            for (int l = 1; l <= levels; l++) b[l - 1] = Math.Round(Math.Min(100, Probability * (1 + depthAdj / 100 * ItemShuffle.DepthOf(false, l))), 1);
            return b;
        }

        /// <summary>UW2: the chance per level within a world (1-8) the sliders give, as curve points.</summary>
        public List<CurvePoint> DepthCurveFromSliders(double depthAdj)
        {
            var l = new List<CurvePoint>();
            for (int x = 1; x <= 8; x++) l.Add(new CurvePoint(x, Math.Round(Math.Min(100, Probability * (1 + depthAdj / 100 * ItemShuffle.DepthOf(true, x))), 1)));
            return l;
        }
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

    /// <summary>An object both shuffles leave alone.</summary>
    public sealed class IgnoredObject
    {
        public int Level { get; set; }
        public int Index { get; set; }
        public string Label { get; set; } = "";
        public override string ToString() { return Label; }
    }

    /// <summary>The item shuffle's settings, saved between runs.</summary>
    public sealed partial class ShuffleOptions
    {
        public string Game { get; set; } = "UW1";
        /// <summary>2: the settings start from the world. Older files are fitted to the world once when loaded.</summary>
        public int Version { get; set; }
        public const int CurrentVersion = 2;
        public int Seed { get; set; } = 1;
        public bool IncludeInventories { get; set; } = true;
        /// <summary>Spells and other class effects already on objects stay; only plain and class 12 objects are rolled.</summary>
        public bool KeepOtherEnchantments { get; set; } = true;
        /// <summary>"shuffle" (properties rolled by the tabs), "swap-kind" (existing objects trade places within their kind), "swap-any" (across all the kinds taking part).</summary>
        public string Mode { get; set; } = "shuffle";
        /// <summary>Objects never touched, by level and index. Null until filled with the game's defaults.</summary>
        public List<IgnoredObject> Ignored { get; set; }
        /// <summary>Poles, fishing poles and thread take part in swaps.</summary>
        public bool SwapTools { get; set; } = true;

        // weapons
        public bool ShuffleWeapons { get; set; } = true;
        public double Axes { get; set; } = 50;
        public double Swords { get; set; } = 50;
        public double Maces { get; set; } = 50;
        public double Ranged { get; set; } = 50;
        public List<EnchantGroup> WeaponEnchants { get; set; } = new List<EnchantGroup>();
        /// <summary>0-100: how much deeper levels raise the chance and the strength of enchantments.</summary>
        public double WeaponDepth { get; set; } = 30;
        /// <summary>0-100: how far deeper levels move the mean strength towards the strongest.</summary>
        public double WeaponStrengthDepth { get; set; } = 30;
        public bool WeaponSpecials { get; set; }
        public List<SpecialChoice> WeaponSpecialList { get; set; } = new List<SpecialChoice>();
        /// <summary>± this round each object's own quality (0: unchanged).</summary>
        public int WeaponQualitySpread { get; set; } = 10;
        /// <summary>Weapon quality comes from <see cref="WeaponQualityCurve"/> (x 1-63, y a relative weight) instead of the spread.</summary>
        public bool CustomWeaponQuality { get; set; }
        public List<CurvePoint> WeaponQualityCurve { get; set; }
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
        public double ArmourStrengthDepth { get; set; } = 30;
        public bool ArmourSpecials { get; set; } = true;
        public List<SpecialChoice> ArmourSpecialList { get; set; } = new List<SpecialChoice>();
        public int ArmourQualitySpread { get; set; } = 10;
        public bool CustomArmourQuality { get; set; }
        public List<CurvePoint> ArmourQualityCurve { get; set; }

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

        // rings, gloves and boots
        public bool ShuffleRings { get; set; } = true;
        public Dictionary<int, double> RingWeights { get; set; }
        /// <summary>Chance (0-100) that a ring carries a worn effect (or a curse).</summary>
        public double RingMagic { get; set; } = 40;
        /// <summary>Weights of the rings' worn effects, by class × 16 + effect (classes 0-3 and 11, as player_affected_by applies them).</summary>
        public Dictionary<int, double> RingEffects { get; set; }
        /// <summary>Of the rings that get an effect, the share (0-100) cursed instead.</summary>
        public double RingCurseShare { get; set; } = 15;
        /// <summary>Armour may carry a worn effect without degrees (Valor, Leap, Missile Protection...) instead of Protection or Toughness.</summary>
        public bool ArmourEffectsOn { get; set; } = true;
        /// <summary>Chance (0-100) that a piece gets one of those (or a curse).</summary>
        public double ArmourEffectChance { get; set; } = 6;
        public double ArmourCurseShare { get; set; } = 15;
        public Dictionary<int, double> ArmourEffects { get; set; }
        /// <summary>Which pieces can get them: body, legs, gloves, boots, helm, shields.</summary>
        public bool[] ArmourEffectSlots { get; set; }
        /// <summary>A weight per curse strength 1-8: the curse does that many d8 of damage, never below 3 hit points, whenever the worn things change (SPELLS.C backfire).</summary>
        public double[] CurseStrength { get; set; }

        // wands and scrolls
        public bool ShuffleWands { get; set; } = true;
        /// <summary>A weight per number of charges, 1 to <see cref="MaxCharges"/>.</summary>
        public double[] WandCharges { get; set; }
        /// <summary>A weight per circle (UW1: 1-8 and the spells without runes).</summary>
        public double[] WandCircles { get; set; }
        public bool ShuffleScrolls { get; set; } = true;
        public double[] ScrollCircles { get; set; }
        public bool ShufflePotions { get; set; } = true;
        public double[] PotionCircles { get; set; }
        /// <summary>Potions only get spells some potion in the game already holds (heals, mana, cure poison...), by circle.</summary>
        public bool PotionsKnownOnly { get; set; } = true;
        public const int MaxCharges = 30;

        static readonly JsonSerializerOptions Json = new JsonSerializerOptions { WriteIndented = true };
        public string ToJson() { return JsonSerializer.Serialize(this, Json); }
        public static ShuffleOptions FromJson(string s) { return JsonSerializer.Deserialize<ShuffleOptions>(s, Json); }

        /// <summary>The defaults for a game: its enchantment names, food, lights and runes.</summary>
        public static ShuffleOptions Defaults(GameSession s)
        {
            var o = new ShuffleOptions { Game = s.IsUw2 ? "UW2" : "UW1", Seed = Environment.TickCount & 0x7FFFFFFF, Version = CurrentVersion };
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
            o.FillTables(s);
            return o;
        }

        /// <summary>The defaults, with the wand, scroll and quality distributions taken from the levels as they are.</summary>
        public static ShuffleOptions Defaults(GameSession s, Func<int, LevelModel> level)
        {
            var o = Defaults(s);
            o.Complete(s, level);
            if (level != null) o.FromWorld(s, ItemShuffle.Census(s, level), null);
            return o;
        }

        /// <summary>Fills what an older settings file lacks: the wand and scroll bars and the quality curves, from the game's levels (or flat without them).</summary>
        public void Complete(GameSession s, Func<int, LevelModel> level)
        {
            var g = level == null ? null : ItemShuffle.Census(s, level);
            int circles = SpellTables.Circles(s.IsUw2);
            if (WandCharges == null || WandCharges.Length != MaxCharges) WandCharges = g != null ? Distributions.Scaled(g.Charges) : Enumerable.Repeat(50.0, MaxCharges).ToArray();
            if (WandCircles == null || WandCircles.Length != circles) WandCircles = g != null ? Distributions.Scaled(g.WandCircles) : Flat(circles, s.IsUw2);
            if (PotionCircles == null || PotionCircles.Length != circles) PotionCircles = g != null ? Distributions.Scaled(g.PotionCircles) : Flat(circles, s.IsUw2);
            if (ScrollCircles == null || ScrollCircles.Length != circles) ScrollCircles = g != null ? Distributions.Scaled(g.ScrollCircles) : Flat(circles, s.IsUw2);
            if (WeaponQualityCurve == null || WeaponQualityCurve.Count < 2) WeaponQualityCurve = g != null ? Distributions.CurveOf(g.WeaponQuality, 1, 8) : new List<CurvePoint> { new CurvePoint(1, 50), new CurvePoint(63, 50) };
            FillTables(s);
            if (Ignored == null) Ignored = level == null ? new List<IgnoredObject>() : ItemShuffle.DefaultIgnored(s, level);
            if (ArmourQualityCurve == null || ArmourQualityCurve.Count < 2) ArmourQualityCurve = g != null ? Distributions.CurveOf(g.ArmourQuality, 1, 8) : new List<CurvePoint> { new CurvePoint(1, 50), new CurvePoint(63, 50) };
        }

        /// <summary>The ring, worn effect and curse tables (from the game's strings, not its levels).</summary>
        void FillTables(GameSession s)
        {
            if (RingWeights == null || RingWeights.Count == 0) { RingWeights = new Dictionary<int, double>(); foreach (int r in ItemTables.Rings(s)) RingWeights[r] = 50; }
            if (RingEffects == null || RingEffects.Count == 0) { RingEffects = new Dictionary<int, double>(); foreach (var e in ItemTables.WornEffects(s)) RingEffects[e.Code] = e.Code == 0 ? 0 : 50; }
            if (ArmourEffects == null || ArmourEffects.Count == 0) { ArmourEffects = new Dictionary<int, double>(); foreach (var e in ItemTables.WornEffects(s)) ArmourEffects[e.Code] = e.Code == 0 ? 0 : 50; }
            if (ArmourEffectSlots == null || ArmourEffectSlots.Length != 6) ArmourEffectSlots = new[] { true, true, true, true, true, true };
            if (CurseStrength == null || CurseStrength.Length != 8) CurseStrength = new double[] { 100, 40, 15, 5, 0, 0, 0, 0 };
        }

        static double[] Flat(int circles, bool uw2)
        {
            var b = Enumerable.Repeat(50.0, circles).ToArray();
            if (!uw2) b[8] = 0;    // the spells without runes
            return b;
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

        /// <summary>The armour-effect slot (0 body, 1 legs, 2 gloves, 3 boots, 4 helm) of an armour slot (0 body, 1 legs, 2 hands, 3 feet, 4 head): the same order.</summary>
        public static int EffectSlot(int slot) { return slot; }

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

        /// <summary>The rings: UW1 gold, silver, red (its iron ring, 0x36, is the Ring of Humility, a talisman: never made or changed); UW2 copper, gold, silver, red (its Guardian signet rings, 0x35, are quest items and stay).</summary>
        public static int[] Rings(GameSession s) { return s.IsUw2 ? new[] { 0x37, 0x38, 0x39, 0x3A } : new[] { 0x38, 0x39, 0x3A }; }
        public static readonly int[] Gloves = { 0x26, 0x27, 0x28 };
        public static readonly int[] Boots = { 0x29, 0x2A, 0x2B };

        public sealed class WornEffect { public int Code; public string Name, Group; }

        /// <summary>
        /// The effects that do something worn (PLAYDATA.C player_affected_by): light (class 0),
        /// motion (1), armour (2), protection, stealth and resistances (3), and of class 11 only
        /// haste, wizard eye, pick distance and the two regenerations. Named by block 6.
        /// </summary>
        public static List<WornEffect> WornEffects(GameSession s)
        {
            var l = new List<WornEffect>();
            string[] groups = { "Light", "Motion", "Armour", "Protection" };
            foreach (int c in new[] { 0, 1, 2, 3, 11 })
                for (int e = 0; e < 16; e++)
                {
                    if (c == 11 && !(e <= 3 || e == 14 || e == 15)) continue;
                    var n = s.Strings.Get(6, c * 16 + e);
                    if (string.IsNullOrWhiteSpace(n)) continue;
                    l.Add(new WornEffect { Code = c * 16 + e, Name = n.Trim(), Group = c == 11 ? "Other" : groups[c] });
                }
            return l;
        }
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
        /// <summary>Each level's block as Apply left it: Revert only puts back a level that has not been edited since.</summary>
        public readonly Dictionary<int, byte[]> After = new Dictionary<int, byte[]>();
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
    public static partial class ItemShuffle
    {
        sealed class Cand
        {
            public int Level, Index;
            public ObjectInfo O;
            public ItemRewrite R;
            public bool Touched;
            public double Depth;
            /// <summary>The level for depth curves: UW1 1-9, UW2 the level within its world 1-8.</summary>
            public int DepthLevel;
            public int ParentItem = -1, ParentIndex = -1;
            public bool Flag11 { get { return ((O.Record[1] >> 3) & 1) != 0; } }
        }

        /// <summary>How deep a level is, 0 (level 1) to 1: UW1 (level - 1) / 7, UW2 the level within its world.</summary>
        public static double DepthOf(bool uw2, int level)
        {
            return uw2 ? ((level - 1) % 8) / 7.0 : Math.Min(1.0, (level - 1) / 7.0);
        }

        /// <summary>A wand's spell object (0x120 inside a wand 0x98-0x9B): flag 11, link 0x200 + one of the 64 spells, quality the charges (USEITEMS.C decode_obj_spell, useNSpellCharges).</summary>
        static bool IsWandSpell(ObjectInfo o, int parentItem)
        {
            return o.Item == 0x120 && parentItem >= 0x98 && parentItem <= 0x9B && o.IsQuantity && ((o.Record[1] >> 3) & 1) != 0 && o.Link >= 0x200 && ((o.Link & 0x1FF) >> 6) == 0;
        }

        /// <summary>A wand enchanted directly (is-quantity, flag 11): it casts its spell with no charges counted.</summary>
        static bool IsChargedWand(ObjectInfo o, bool f11)
        {
            return o.Item >= 0x98 && o.Item <= 0x9B && o.Enchanted && o.IsQuantity && f11 && o.Link >= 0x200 && ((o.Link & 0x1FF) >> 6) == 0;
        }

        /// <summary>The potions: UW1 red and green (0xBB, 0xBC); UW2 0xE0-0xE7, whatever block 4 calls a potion.</summary>
        public static bool IsPotionItem(GameSession s, int item)
        {
            if (!s.IsUw2) return item == 0xBB || item == 0xBC;
            return item >= 0xE0 && item <= 0xE7 && (s.Names.Get(item) ?? "").Contains("potion");
        }

        /// <summary>A potion holding one of the 64 spells (flag 11). Potions with special effects (class 13 and up) are not.</summary>
        static bool IsSpellPotion(GameSession s, ObjectInfo o, bool f11)
        {
            return IsPotionItem(s, o.Item) && o.Enchanted && o.IsQuantity && f11 && o.Link >= 0x200 && ((o.Link & 0x1FF) >> 6) == 0;
        }

        /// <summary>A spell scroll: books, scrolls and maps (0x130-0x13F) enchanted with one of the 64 spells (flag 11).</summary>
        static bool IsSpellScroll(ObjectInfo o, bool f11)
        {
            return o.Item >= 0x130 && o.Item <= 0x13F && o.Enchanted && o.IsQuantity && f11 && o.Link >= 0x200 && ((o.Link & 0x1FF) >> 6) == 0;
        }

        public static ShuffleResult Run(GameSession s, Func<int, LevelModel> level, ShuffleOptions opt)
        {
            var res = new ShuffleResult();
            var rng = new Random(opt.Seed);
            bool uw2 = s.IsUw2;
            var all = new List<Cand>();
            var ignored = new HashSet<long>((opt.Ignored ?? new List<IgnoredObject>()).Select(x => (long)x.Level << 32 | (uint)x.Index));
            int ignoredCount = ignored.Count, keptCount = 0;
            bool swapping = opt.Mode == "swap-kind" || opt.Mode == "swap-any";
            // the swap's kept objects (the starting equipment) stay as the ignored ones do
            if (swapping && opt.Kept != null)
                foreach (var k in opt.Kept) if (ignored.Add((long)k.Level << 32 | (uint)k.Index)) keptCount++;
            foreach (var slot in s.LevelSlots())
            {
                if (!slot.Present) continue;
                LevelModel m;
                try { m = level(slot.Level); } catch (Exception) { continue; }
                double depth = DepthOf(uw2, slot.Level);
                int depthLevel = uw2 ? (slot.Level - 1) % 8 + 1 : slot.Level;
                foreach (var o in m.Objects)
                {
                    if (o.Free || o.TileX < 0 || o.IsMobile) continue;
                    // every container up the chain counts: a sack a creature carries is in its inventory,
                    // and anything inside a trap (however deep) is a create-object template, left alone
                    bool inCritter = false, inTrap = false;
                    for (int up = o.Parent, guard = 0; up >= 0 && guard < 1024; up = m.Objects[up].Parent, guard++)
                    {
                        if (m.Objects[up].IsCritter) inCritter = true;
                        if (m.Objects[up].IsTrapOrTrigger) inTrap = true;
                    }
                    if (!opt.IncludeInventories && inCritter) continue;
                    if (inTrap) continue;
                    // an object holding triggers (pick up, look) is what they are about: it keeps its kind
                    if (!o.IsQuantity && o.Children.Any(c => m.Objects[c].IsTrapOrTrigger)) continue;
                    // ignored objects, and a wand's spell object when the wand is ignored
                    if (ignored.Contains((long)slot.Level << 32 | (uint)o.Index)) continue;
                    if (o.Parent >= 0 && ignored.Contains((long)slot.Level << 32 | (uint)o.Parent)) continue;
                    all.Add(new Cand
                    {
                        Level = slot.Level, Index = o.Index, O = o, Depth = depth, DepthLevel = depthLevel,
                        ParentItem = o.Parent >= 0 ? m.Objects[o.Parent].Item : -1, ParentIndex = o.Parent,
                        R = Of(o),
                    });
                }
            }
            var before = Count(all, false);

            if (swapping) Swap(s, opt, rng, all, res, opt.Mode == "swap-any");
            else
            {
                if (opt.ShuffleWeapons) Weapons(s, opt, rng, all, res);
                if (opt.ShuffleArmour) Armour(s, opt, rng, all, res);
                if (opt.ShuffleFood || opt.ShuffleLights) FoodAndLights(s, opt, rng, all, res);
                if (opt.ShuffleFood || opt.ShuffleLights) Tools(s, opt, rng, all, res);   // tools are made from food and lights
                if (opt.ShuffleRunes) Runes(s, opt, rng, all, res);
                if (opt.ShuffleWands) Wands(s, opt, rng, all, res);
                if (opt.ShuffleScrolls) Scrolls(s, opt, rng, all, res);
                if (opt.ShufflePotions) Potions(s, opt, rng, all, res);
                if (opt.ShuffleRings) Rings(s, opt, rng, all, res);
            }
            if (ignoredCount > 0) res.Report.AppendFormat("{0} ignored object(s) left alone.\n", ignoredCount);
            if (keptCount > 0) res.Report.AppendFormat("{0} object(s) of the starting equipment kept where they are.\n", keptCount);

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
            return new ItemRewrite { Index = o.Index, Item = o.Item, Quality = o.Quality, Flags = (o.Record[1] >> 1) & 7, Link = o.Link, Owner = o.Owner, Enchanted = o.Enchanted, IsQuantity = o.IsQuantity, Exact = true };
        }

        static bool Differs(Cand c)
        {
            var o = c.O; var r = c.R;
            return r.Item != o.Item || r.Quality != o.Quality || r.Flags != ((o.Record[1] >> 1) & 7) || r.Enchanted != o.Enchanted || r.IsQuantity != o.IsQuantity || r.Link != o.Link || r.Owner != o.Owner;
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

        /// <summary>A quality 1-63 drawn from the curve (its value at each quality is the weight); the object's own when the curve is 0 everywhere.</summary>
        static int Quality(Random rng, List<CurvePoint> curve, int own)
        {
            int i = Distributions.Pick(rng, Distributions.Sample(curve, 1, 63));
            return i < 0 ? own : i + 1;
        }

        /// <summary>Whether an object's enchantment is one the shuffle rolls again: none, or class 12 (flag 11 clear).</summary>
        static bool Rerollable(Cand c, ShuffleOptions opt, bool worn = false)
        {
            // a link to other objects (contents, a spell object) is never replaced
            if (!c.O.IsQuantity && c.O.Link != 0) return false;
            if (!c.O.Enchanted || c.O.Link < 0x200) return true;
            bool flag11 = ((c.O.Record[1] >> 3) & 1) != 0;
            int major = (c.O.Link & 0x1FF) >> 4;
            if (!flag11 && major == 12) return true;
            // armour and rings: their worn effects and curses are what the tab rolls
            if (worn && !flag11 && (major <= 3 || major == 9 || major == 11)) return true;
            return !opt.KeepOtherEnchantments;
        }

        /// <summary>Gives a worn effect by the weights, or with curseShare a curse (class 9, strength by the bars); false when no weight is above 0.</summary>
        static bool RollEffect(GameSession s, Random rng, Cand c, Dictionary<int, double> effects, double curseShare, double[] curseStrength, Dictionary<string, int> tally)
        {
            int code;
            string k;
            if (rng.NextDouble() * 100 < curseShare)
            {
                int st = Distributions.Pick(rng, curseStrength);
                if (st < 0) return false;
                code = 0x90 | (st + 1);
                k = "cursed " + (st + 1) + "d8";
            }
            else
            {
                var codes = effects.Keys.OrderBy(x => x).ToList();
                int i = Distributions.Pick(rng, codes.Select(x => effects[x]).ToList());
                if (i < 0) return false;
                code = codes[i];
                k = (s.Strings.Get(6, code) ?? code.ToString()).Trim();
            }
            int n; tally.TryGetValue(k, out n); tally[k] = n + 1;
            c.R.Enchanted = true; c.R.IsQuantity = true; c.R.Flags &= ~4; c.R.Link = 0x200 | code;
            c.Touched = true;
            return true;
        }

        /// <summary>
        /// Rolls a class 12 enchantment; true when one was given. Each kind's chance p is its share of
        /// the items, so the item is enchanted with chance Σp (at most 1) and the kind is then drawn by
        /// p: trying the kinds one after another would make every kind rarer than its share. scale
        /// raises the chances of items that already passed on another roll (armour's worn effects).
        /// </summary>
        static bool Enchant(Random rng, Cand c, List<EnchantGroup> groups, double depthAdj, double strengthDepth, Dictionary<string, int> tally, bool uw2, double scale = 1)
        {
            double dep = depthAdj / 100.0 * c.Depth, sdep = strengthDepth / 100.0 * c.Depth;
            var live = new List<EnchantGroup>();
            var ps = new List<double>();
            foreach (var g in groups)
            {
                if (g.Effects == null || g.Effects.Length == 0) continue;
                if (g.CustomStrength && g.StrengthBars != null && g.StrengthBars.Length == g.Effects.Length && g.StrengthBars.All(b => b <= 0)) continue;   // every bar at 0: this kind never comes
                double p;
                if (g.CustomDepth && uw2 && g.DepthCurve != null && g.DepthCurve.Count > 0) p = Distributions.Eval(g.DepthCurve, c.DepthLevel) / 100.0;
                else if (g.CustomDepth && !uw2 && g.DepthBars != null && g.DepthBars.Length > 0) p = g.DepthBars[Math.Min(g.DepthBars.Length, c.DepthLevel) - 1] / 100.0;
                else p = g.Probability / 100.0 * (1 + dep);
                p = Math.Max(0, Math.Min(1, p * scale));
                if (p <= 0) continue;
                live.Add(g); ps.Add(p);
            }
            double sum = ps.Sum();
            if (sum <= 0 || rng.NextDouble() >= Math.Min(1, sum)) return false;
            int pick = Distributions.Pick(rng, ps);
            if (pick < 0) return false;
            var e = live[pick];
            int strength;
            if (e.CustomStrength && e.StrengthBars != null && e.StrengthBars.Length == e.Effects.Length)
            {
                strength = Distributions.Pick(rng, e.StrengthBars) + 1;
                if (strength == 0) return false;
            }
            else
            {
                double mean = e.Mean + sdep * (e.Effects.Length - 1);
                strength = (int)Math.Round(mean + e.Deviation * Normal(rng));
            }
            strength = Math.Max(1, Math.Min(e.Effects.Length, strength));
            c.R.Enchanted = true; c.R.IsQuantity = true; c.R.Flags &= ~4;
            c.R.Link = 0x200 | 0xC0 | e.Effects[strength - 1];
            string key = e.Name + " " + strength;
            int n; tally.TryGetValue(key, out n); tally[key] = n + 1;
            return true;
        }

        static void Plain(Cand c, int quantity = 1)
        {
            if (!c.O.IsQuantity && c.O.Link != 0) return;      // a list (contents, a spell object) stays
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
                c.R.Quality = opt.CustomWeaponQuality ? Quality(rng, opt.WeaponQualityCurve, c.O.Quality) : Spread(rng, c.O.Quality, opt.WeaponQualitySpread);
                c.Touched = true;
                if (ammo) { Plain(c, wasAmmo && c.O.IsQuantity && c.O.Link < 0x200 ? c.O.Link : rng.Next(3, 13)); continue; }
                if (!Rerollable(c, opt)) continue;
                if (Enchant(rng, c, opt.WeaponEnchants, opt.WeaponDepth, opt.WeaponStrengthDepth, tally, uw2)) enchanted++;
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
            var effects = new Dictionary<string, int>();
            int enchanted = 0, withEffect = 0;
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
                c.R.Quality = opt.CustomArmourQuality ? Quality(rng, opt.ArmourQualityCurve, c.O.Quality) : Spread(rng, c.O.Quality, opt.ArmourQualitySpread);
                c.Touched = true;
                if (!Rerollable(c, opt, true)) continue;
                int slot6 = ItemTables.ShieldIds.Contains(c.R.Item) ? 5 : ItemTables.EffectSlot(ItemTables.Slot(c.R.Item));
                if (opt.ArmourEffectsOn && opt.ArmourEffects != null && opt.ArmourEffectSlots != null && slot6 >= 0 && opt.ArmourEffectSlots[slot6]
                    && rng.NextDouble() * 100 < opt.ArmourEffectChance && RollEffect(s, rng, c, opt.ArmourEffects, opt.ArmourCurseShare, opt.CurseStrength, effects)) { withEffect++; continue; }
                // the worn-effect roll came first and passed this piece over: its class 12 chance is that of the pieces left
                bool effectSlot = opt.ArmourEffectsOn && opt.ArmourEffects != null && opt.ArmourEffectSlots != null && slot6 >= 0 && opt.ArmourEffectSlots[slot6];
                double left = effectSlot ? Math.Max(0.05, 1 - opt.ArmourEffectChance / 100.0) : 1;
                if (Enchant(rng, c, opt.ArmourEnchants, opt.ArmourDepth, opt.ArmourStrengthDepth, tally, s.IsUw2, 1 / left)) enchanted++;
                else Plain(c);
            }
            res.Report.AppendFormat("Armour: {0} shuffled (quest armour, crowns and the talisman shield kept), {1} enchanted", cands.Count, enchanted);
            if (tally.Count > 0) res.Report.Append(": " + string.Join(", ", tally.OrderBy(k => k.Key).Select(k => k.Key + " ×" + k.Value)));
            res.Report.AppendLine(".");
            if (withEffect > 0) res.Report.AppendFormat("  {0} with other effects: {1}.\n", withEffect, string.Join(", ", effects.OrderBy(k => k.Key).Select(k => k.Key + " ×" + k.Value)));
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
            var pool = all.Where(c => (food.Contains(c.R.Item) || lights.Contains(c.R.Item)) && c.O.Parent < 0 && (c.O.IsQuantity || c.O.Link == 0)).OrderBy(c => food.Contains(c.R.Item) ? 0 : 1).ThenBy(c => rng.Next()).ToList();
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

        /// <summary>The kind an object takes part as (weapon, armour, food, light, tool, rune, wand, scroll; with scenery, the swap's bones and scenery), or null. Exempt objects are null.</summary>
        static string KindOf(GameSession s, ObjectInfo o, bool f11, bool scenery = false)
        {
            int it = o.Item;
            if (scenery) { string sk = SceneryKind(s, o); if (sk != null) return sk; }
            if (ItemTables.KeptWeapons.Contains(it)) return null;
            if (ItemTables.Axes.Contains(it) || ItemTables.Swords(s.IsUw2).Contains(it) || ItemTables.Maces.Contains(it) || ItemTables.Launchers.Contains(it) || ItemTables.Ammo.Contains(it)) return "weapon";
            if (ItemTables.Slot(it) >= 0 || ItemTables.ShieldIds.Contains(it)) return "armour";
            if (ItemTables.Food(s).Contains(it)) return "food";
            if (ItemTables.LightKinds(s).Any(k => k == it || k + 4 == it)) return "light";
            if (it == ItemTables.Pole || it == ItemTables.FishingPole || it == ItemTables.Thread(s.IsUw2)) return "tool";
            if (ItemTables.Runes.Contains(it)) return "rune";
            if (it >= 0x98 && it <= 0x9B) return "wand";
            if (ItemTables.Rings(s).Contains(it)) return "ring";
            if (IsSpellScroll(o, f11)) return "scroll";
            if (IsPotionItem(s, it)) return "potion";
            return null;
        }

        static bool KindOn(ShuffleOptions opt, string kind)
        {
            switch (kind)
            {
                case "weapon": return opt.ShuffleWeapons;
                case "armour": return opt.ShuffleArmour;
                case "food": return opt.ShuffleFood;
                case "light": return opt.ShuffleLights;
                case "tool": return opt.SwapTools;
                case "rune": return opt.ShuffleRunes;
                case "wand": return opt.ShuffleWands;
                case "scroll": return opt.ShuffleScrolls;
                case "ring": return opt.ShuffleRings;
                case "potion": return opt.ShufflePotions;
                case "bones": return opt.SwapBones;
                case "scenery": return opt.SwapScenery;
                default: return false;
            }
        }

        /// <summary>
        /// Swap mode: the existing objects trade places, whole (item, quality, flags, enchantment,
        /// quantity, contents such as a wand's spell object, and the owner unless the spot keeps
        /// its own, <see cref="ShuffleOptions.SpotOwnership"/>), within their kind or across every
        /// kind taking part, and within the scope (<see cref="ShuffleOptions.SwapScope"/>).
        /// Nothing new is made: the counts of every item stay the same. Then, if asked, the
        /// enchantments trade places within each class.
        /// </summary>
        static void Swap(GameSession s, ShuffleOptions opt, Random rng, List<Cand> all, ShuffleResult res, bool anyKind)
        {
            var byKey = all.ToDictionary(c => (long)c.Level << 32 | (uint)c.Index);
            var groups = new Dictionary<string, List<Cand>>();
            var wands = new List<Cand[]>();      // a charged wand and its spell object, moved together
            int stay = 0, owners = 0;
            foreach (var c in all)
            {
                string k = KindOf(s, c.O, c.Flag11, true);
                if (k == null || !KindOn(opt, k)) continue;
                if (!c.O.IsQuantity && c.O.Link != 0)
                {
                    // a list (a wand's spell object, a trigger): its link is an index on this level only
                    Cand child;
                    if (k == "wand" && byKey.TryGetValue((long)c.Level << 32 | (uint)c.O.Link, out child) && IsWandSpell(child.O, c.O.Item))
                        wands.Add(new[] { c, child });
                    else stay++;
                    continue;
                }
                string key = anyKind ? "all" : k;
                List<Cand> l;
                if (!groups.TryGetValue(key, out l)) groups[key] = l = new List<Cand>();
                l.Add(c);
            }
            res.Report.AppendLine((anyKind ? "Swap, any kind: existing objects trade places across every kind taking part, " : "Swap, same kind: existing objects trade places within their kind, ") + ScopeText(s, opt) + ".");
            foreach (var g in groups.OrderBy(x => x.Key, StringComparer.Ordinal))
            {
                var list = g.Value;
                var records = list.Select(c => Of(c.O)).ToList();
                var p = Permutation(rng, s, list.Select(c => c.Level).ToList(), opt);
                int moved = 0;
                for (int i = 0; i < list.Count; i++)
                {
                    var r = records[p[i]];
                    r.Index = list[i].Index;
                    if (opt.SpotOwnership && TakeSpotOwner(s, list[i].O, ref r)) owners++;
                    list[i].R = r;
                    list[i].Touched = true;
                    if (r.Item != list[i].O.Item || r.Link != list[i].O.Link) moved++;
                }
                res.Report.AppendFormat("  {0}: {1} objects, {2} changed.\n", anyKind ? "all kinds" : g.Key, list.Count, moved);
            }
            if (wands.Count > 0)
            {
                // each wand keeps its own list; the wand's item, quality and owner and the spell
                // object's spell and charges move
                var units = wands.Select(u => new[] { Of(u[0].O), Of(u[1].O) }).ToList();
                var p = Permutation(rng, s, wands.Select(u => u[0].Level).ToList(), opt);
                for (int i = 0; i < wands.Count; i++)
                {
                    var w = units[p[i]][0]; var sp = units[p[i]][1];
                    w.Index = wands[i][0].Index; w.Link = wands[i][0].O.Link; w.IsQuantity = false;
                    if (opt.SpotOwnership && TakeSpotOwner(s, wands[i][0].O, ref w)) owners++;
                    sp.Index = wands[i][1].Index;
                    wands[i][0].R = w; wands[i][1].R = sp;
                    wands[i][0].Touched = wands[i][1].Touched = true;
                }
                res.Report.AppendFormat("  charged wands: {0}, swapped among themselves with their spells and charges (the charges live in an object inside the wand).\n", wands.Count);
            }
            if (stay > 0) res.Report.AppendFormat("  {0} object(s) holding triggers or other objects stay where they are.\n", stay);
            if (groups.Count == 0 && wands.Count == 0) res.Report.AppendLine("  Nothing takes part: tick at least one kind.");
            if (opt.SpotOwnership && owners > 0) res.Report.AppendFormat("  {0} object(s) took the owner of the spot they went to.\n", owners);
            if (opt.SwapEnchantments)
            {
                // the wands' parts never take part; everything else of the classes does, whether or not its kind was swapped
                var wandParts = new HashSet<Cand>(wands.SelectMany(u => u));
                SwapEnchantmentsAfter(s, opt, rng, all.Where(c => !wandParts.Contains(c)).ToList(), res);
            }
        }

        /// <summary>
        /// The spot keeps its owner: when the owner field is an ownership on both the object that
        /// was at the spot and the one put there (<see cref="Ownable"/>), the incoming record
        /// takes the spot's owner. True when that changed the owner it brought.
        /// </summary>
        static bool TakeSpotOwner(GameSession s, ObjectInfo spot, ref ItemRewrite incoming)
        {
            if (!Ownable(s, spot.Item) || !Ownable(s, incoming.Item)) return false;
            bool changed = incoming.Owner != spot.Owner;
            incoming.Owner = spot.Owner;
            return changed;
        }

        static void Shuffle<T>(Random rng, IList<T> l)
        {
            for (int i = l.Count - 1; i > 0; i--) { int j = rng.Next(i + 1); var t = l[i]; l[i] = l[j]; l[j] = t; }
        }

        /// <summary>What an object is, for lists: level, index, name, its spell or enchantment, where.</summary>
        public static string Describe(GameSession s, LevelModel m, ObjectInfo o)
        {
            bool f11 = ((o.Record[1] >> 3) & 1) != 0;
            string extra = "";
            if (!o.IsQuantity && o.Link > 0 && o.Link < m.Objects.Length && m.Objects[o.Link].Item == 0x120)
            {
                var sp = m.Objects[o.Link];
                extra = ((sp.Link & 0x1FF) >> 6) == 0 ? string.Format(" of {0}, {1} charges", SpellTables.Name(s, sp.Link & 0x3F), sp.Quality) : " (a special spell)";
            }
            else if (o.Enchanted && o.IsQuantity && o.Link >= 0x200)
            {
                if (f11 && ((o.Link & 0x1FF) >> 6) == 0) extra = " of " + SpellTables.Name(s, o.Link & 0x3F);
                else if (f11) extra = string.Format(" (special: class {0} effect {1})", ((o.Link & 0x1FF) >> 6) + 12, o.Link & 0x3F);
                else { var n = Enchantments.NameOf(o.Item, true, true, o.Link, false, s.Strings); if (!string.IsNullOrWhiteSpace(n)) extra = " (" + n.Trim() + ")"; }
            }
            string where = o.Parent >= 0 ? " in #" + o.Parent + " " + s.Names.Get(m.Objects[o.Parent].Item) : "";
            return string.Format("L{0} #{1} {2}{3} ({4}, {5}){6}", m.Level, o.Index, s.Names.Get(o.Item), extra, o.TileX, o.TileY, where);
        }

        /// <summary>The objects the shuffles can touch (both modes), for picking ones to ignore.</summary>
        public static List<IgnoredObject> Candidates(GameSession s, Func<int, LevelModel> level)
        {
            var l = new List<IgnoredObject>();
            foreach (var slot in s.LevelSlots())
            {
                if (!slot.Present) continue;
                LevelModel m;
                try { m = level(slot.Level); } catch (Exception) { continue; }
                foreach (var o in m.Objects)
                {
                    if (o.Free || o.TileX < 0 || o.IsMobile) continue;
                    if (o.Parent >= 0 && m.Objects[o.Parent].IsTrapOrTrigger) continue;
                    if (KindOf(s, o, ((o.Record[1] >> 3) & 1) != 0) == null) continue;
                    l.Add(new IgnoredObject { Level = slot.Level, Index = o.Index, Label = Describe(s, m, o) });
                }
            }
            return l;
        }

        /// <summary>
        /// Objects ignored by default: wands enchanted directly instead of holding a spell object
        /// with charges. They never run out and are quest wands: UW1's bullfrog wand (class 13
        /// effect 3, works the level 4 puzzle), UW2's slightly worn wand of Telekinesis in the
        /// Scintillus Academy (gone when the player leaves its level) and its wand of Light.
        /// </summary>
        public static List<IgnoredObject> DefaultIgnored(GameSession s, Func<int, LevelModel> level)
        {
            var l = new List<IgnoredObject>();
            foreach (var c in Candidates(s, level))
            {
                var m = level(c.Level);
                var o = m.Objects[c.Index];
                bool f11 = ((o.Record[1] >> 3) & 1) != 0;
                if (o.Item >= 0x98 && o.Item <= 0x9B && o.Enchanted && o.IsQuantity && o.Link >= 0x200)
                {
                    if (!s.IsUw2 && ((o.Link & 0x1FF) >> 6) == 1 && (o.Link & 0x3F) == 3) c.Label = Describe(s, m, o).Replace(" (special: class 13 effect 3)", " of the bullfrog (level 4 puzzle)");
                    l.Add(c);
                }
            }
            return l;
        }

        /// <summary>Rings become another ring by weight and, with the ring chance, get a worn effect by weight or a curse.</summary>
        static void Rings(GameSession s, ShuffleOptions opt, Random rng, List<Cand> all, ShuffleResult res)
        {
            if (opt.RingEffects == null || opt.RingWeights == null || opt.CurseStrength == null) { res.Report.AppendLine("Rings: no tables set."); return; }
            var tally = new Dictionary<string, int>();
            var ids = opt.RingWeights.Keys.OrderBy(k => k).ToList();
            var rw = ids.Select(k => opt.RingWeights[k]).ToList();
            int rings = 0, magic = 0;
            foreach (var c in all.Where(c => ItemTables.Rings(s).Contains(c.O.Item)))
            {
                int i = Distributions.Pick(rng, rw);
                if (i >= 0) c.R.Item = ids[i];
                c.Touched = true;
                rings++;
                if (!Rerollable(c, opt, true)) continue;
                if (rng.NextDouble() * 100 < opt.RingMagic && RollEffect(s, rng, c, opt.RingEffects, opt.RingCurseShare, opt.CurseStrength, tally)) magic++;
                else Plain(c);
            }
            res.Report.AppendFormat("Rings: {0} shuffled, {1} with an effect", rings, magic);
            if (tally.Count > 0) res.Report.Append(": " + string.Join(", ", tally.OrderBy(k => k.Key).Select(k => k.Key + " ×" + k.Value)));
            res.Report.AppendLine(".");
        }

        /// <summary>A spell by the circle weights, uniform within the circle; -1 when no weight is above 0.</summary>
        static int SpellBy(GameSession s, Random rng, double[] circles, List<int>[] pools)
        {
            var w = circles.Select((x, i) => pools[i].Count == 0 ? 0 : x).ToArray();
            int c = Distributions.Pick(rng, w);
            if (c < 0) return -1;
            return pools[c][rng.Next(pools[c].Count)];
        }

        static List<int>[] Pools(GameSession s)
        {
            int n = SpellTables.Circles(s.IsUw2);
            var p = new List<int>[n];
            for (int i = 0; i < n; i++) p[i] = SpellTables.InCircle(s, i + 1);
            return p;
        }

        static void CircleReport(GameSession s, ShuffleResult res, string what, int[] before, int[] after)
        {
            res.Report.Append("  " + what + " by circle before → after:");
            for (int i = 0; i < before.Length; i++)
                if (before[i] != 0 || after[i] != 0) res.Report.AppendFormat(" {0}: {1}→{2}", SpellTables.CircleLabel(s.IsUw2, i + 1), before[i], after[i]);
            res.Report.AppendLine();
        }

        /// <summary>Wands get a spell by the circle weights and, when they count charges, a number of charges by the charge weights.</summary>
        static void Wands(GameSession s, ShuffleOptions opt, Random rng, List<Cand> all, ShuffleResult res)
        {
            if (opt.WandCircles == null || opt.WandCharges == null) { res.Report.AppendLine("Wands: no distributions set."); return; }
            var pools = Pools(s);
            int n = pools.Length;
            int[] before = new int[n], after = new int[n];
            int count = 0;
            foreach (var c in all)
            {
                bool spellObj = IsWandSpell(c.O, c.ParentItem), direct = !spellObj && IsChargedWand(c.O, c.Flag11);
                if (!spellObj && !direct) continue;
                int sp = SpellBy(s, rng, opt.WandCircles, pools);
                if (sp < 0) { res.Report.AppendLine("Wands: every circle weight is 0, so wands were left as they are."); return; }
                before[SpellTables.Circle(s.IsUw2, c.O.Link & 0x3F) - 1]++;
                after[SpellTables.Circle(s.IsUw2, sp) - 1]++;
                c.R.Link = 0x200 | sp;
                if (spellObj)
                {
                    int ch = Distributions.Pick(rng, opt.WandCharges);
                    if (ch >= 0) c.R.Quality = ch + 1;
                }
                c.Touched = true;
                count++;
            }
            res.Report.AppendFormat("Wands: {0} shuffled (broken wands and wands with other effects kept).\n", count);
            if (count > 0) CircleReport(s, res, "Spells", before, after);
        }

        /// <summary>Spell scrolls get a spell by the circle weights.</summary>
        static void Scrolls(GameSession s, ShuffleOptions opt, Random rng, List<Cand> all, ShuffleResult res)
        {
            if (opt.ScrollCircles == null) { res.Report.AppendLine("Scrolls: no distribution set."); return; }
            var pools = Pools(s);
            int n = pools.Length;
            int[] before = new int[n], after = new int[n];
            int count = 0;
            foreach (var c in all)
            {
                if (!IsSpellScroll(c.O, c.Flag11)) continue;
                int sp = SpellBy(s, rng, opt.ScrollCircles, pools);
                if (sp < 0) { res.Report.AppendLine("Scrolls: every circle weight is 0, so scrolls were left as they are."); return; }
                before[SpellTables.Circle(s.IsUw2, c.O.Link & 0x3F) - 1]++;
                after[SpellTables.Circle(s.IsUw2, sp) - 1]++;
                c.R.Link = 0x200 | sp;
                c.Touched = true;
                count++;
            }
            res.Report.AppendFormat("Scrolls: {0} spell scrolls shuffled (readable scrolls and maps kept).\n", count);
            if (count > 0) CircleReport(s, res, "Spells", before, after);
        }

        /// <summary>
        /// Potions holding one of the 64 spells get a spell by the circle weights (with "known
        /// only", among the spells potions already hold). In UW2, whose colours tell the effect,
        /// a potion takes the colour the game uses most for its new spell.
        /// </summary>
        static void Potions(GameSession s, ShuffleOptions opt, Random rng, List<Cand> all, ShuffleResult res)
        {
            if (opt.PotionCircles == null) { res.Report.AppendLine("Potions: no distribution set."); return; }
            var cands = all.Where(c => IsSpellPotion(s, c.O, c.Flag11)).ToList();
            var colour = new Dictionary<int, Dictionary<int, int>>();
            foreach (var c in cands)
            {
                int sp0 = c.O.Link & 0x3F;
                Dictionary<int, int> d;
                if (!colour.TryGetValue(sp0, out d)) colour[sp0] = d = new Dictionary<int, int>();
                int n; d.TryGetValue(c.O.Item, out n); d[c.O.Item] = n + 1;
            }
            var pools = Pools(s);
            if (opt.PotionsKnownOnly) for (int i = 0; i < pools.Length; i++) pools[i] = pools[i].Where(colour.ContainsKey).ToList();
            int nc = pools.Length;
            int[] before = new int[nc], after = new int[nc];
            foreach (var c in cands)
            {
                int sp = SpellBy(s, rng, opt.PotionCircles, pools);
                if (sp < 0) { res.Report.AppendLine("Potions: no circle with a weight above 0 has spells to give, so potions were left as they are."); return; }
                before[SpellTables.Circle(s.IsUw2, c.O.Link & 0x3F) - 1]++;
                after[SpellTables.Circle(s.IsUw2, sp) - 1]++;
                c.R.Link = 0x200 | sp;
                Dictionary<int, int> d;
                if (s.IsUw2 && colour.TryGetValue(sp, out d)) c.R.Item = d.OrderByDescending(kv => kv.Value).ThenBy(kv => kv.Key).First().Key;
                c.Touched = true;
            }
            res.Report.AppendFormat("Potions: {0} shuffled{1} (potions with special effects kept).\n", cands.Count, s.IsUw2 ? ", coloured by their new spell" : "");
            if (cands.Count > 0) CircleReport(s, res, "Spells", before, after);
        }

        /// <summary>Applies a result to the level editors, one undoable change per level.</summary>
        public static void Apply(ShuffleResult r, LevelEdits edits, int seed)
        {
            foreach (var kv in r.ByLevel)
            {
                var e = edits.For(kv.Key);
                e.RewriteItems(kv.Value, "Shuffle item properties (seed " + seed + ")");
                r.After[kv.Key] = (byte[])e.Data.Clone();
            }
        }

        /// <summary>
        /// Puts every object a shuffle changed back as it was (itself one undoable change per level).
        /// A level edited since the shuffle is left alone, since its old records could undo those edits
        /// halfway and break its lists; those levels are returned (Edit ▸ Undo still steps back there).
        /// </summary>
        public static List<int> Revert(ShuffleResult r, LevelEdits edits)
        {
            var skipped = new List<int>();
            foreach (var kv in r.Original)
            {
                var e = edits.For(kv.Key);
                byte[] after;
                if (r.After.TryGetValue(kv.Key, out after) && !e.Data.AsSpan().SequenceEqual(after)) { skipped.Add(kv.Key); continue; }
                e.RewriteItems(kv.Value, "Undo item shuffle");
            }
            return skipped;
        }
    }
}
