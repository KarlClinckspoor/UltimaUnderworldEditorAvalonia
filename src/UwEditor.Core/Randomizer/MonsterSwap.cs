using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.Json;
using UltimateEditor.Games;
using UwEditor.Core.Editing;

namespace UwEditor.Core.Randomizer
{
    /// <summary>One creature kind from OBJECTS.DAT's critter table (64 × 48 bytes at 0x132, include/critter.h struct Creature).</summary>
    public sealed class CreatureKind
    {
        public int Item, AvgHit, Str, Race, Equip, Speed, Caster, Exp, Height;
        public int[] Armour = new int[4];
        public bool Swims, Flier;
        /// <summary>COMOBJ.DAT's resist byte has bit 8 (fire): lava neither stops nor hurts it (critter/AI.C critter_ai, combat/DAMAGE.C check_res).</summary>
        public bool Fireproof;
        public (int Chance, int Damage, int Prob)[] Attacks = new (int, int, int)[3];
        public int[] Spells = new int[3];

        /// <summary>The mean armour over the hit locations (0xFF means "as location 0").</summary>
        public double ArmourMean { get { var a = Armour.Select(x => x == 0xFF ? Armour[0] : x).ToList(); return a.Average(); } }

        public static Dictionary<int, CreatureKind> Read(GameSession s)
        {
            var r = new Dictionary<int, CreatureKind>();
            string p = GameProfile.Find(s.DataFolder, "OBJECTS.DAT");
            byte[] d = p == null ? null : File.ReadAllBytes(p);
            string cp = GameProfile.Find(s.DataFolder, "COMOBJ.DAT");
            byte[] com = cp == null ? null : File.ReadAllBytes(cp);
            if (d == null) return r;
            for (int i = 0; i < 64; i++)
            {
                int at = 0x132 + 0x30 * i;
                if (at + 0x30 > d.Length) break;
                var k = new CreatureKind { Item = 0x40 + i };
                for (int a = 0; a < 4; a++) k.Armour[a] = d[at + a];
                k.AvgHit = d[at + 4];
                k.Str = d[at + 5];
                k.Race = d[at + 9];
                k.Swims = (d[at + 0x0A] & 0x40) != 0;
                k.Flier = (d[at + 0x0A] & 0x80) != 0;
                k.Speed = d[at + 0x0B];
                k.Equip = (sbyte)d[at + 0x11];
                for (int a = 0; a < 3; a++) k.Attacks[a] = ((sbyte)d[at + 0x13 + 3 * a], d[at + 0x14 + 3 * a], d[at + 0x15 + 3 * a]);
                k.Exp = (short)(d[at + 0x28] | d[at + 0x29] << 8);
                for (int a = 0; a < 3; a++) k.Spells[a] = d[at + 0x2A + a];
                k.Caster = d[at + 0x2D] >> 1;
                k.Height = com != null && 2 + 11 * k.Item < com.Length ? com[2 + 11 * k.Item] : 0;
                // struct ComObj (obj/object.h): 11 bytes after a 2-byte header, resist at 0x08
                k.Fireproof = com != null && 2 + 11 * k.Item + 8 < com.Length && (com[2 + 11 * k.Item + 8] & 0x08) != 0;
                r[k.Item] = k;
            }
            return r;
        }
    }

    /// <summary>The monster swap's settings, saved per game.</summary>
    public sealed class MonsterSwapOptions
    {
        public int Seed { get; set; } = 1;
        /// <summary>"level" (within each level), "world" (UW2: within each world; UW1: all), "all".</summary>
        public string Scope { get; set; } = "level";
        /// <summary>"xp" (the game's experience value), "hp", "damage", "combined" (√(toughness × damage)), "none".</summary>
        public string Measure { get; set; } = "xp";
        /// <summary>0: each spot keeps its difficulty (the strength of the monster that was there); 100: by depth; between, a blend.</summary>
        public double DepthBlend { get; set; } = 0;
        /// <summary>0: the strongest monster goes to the hardest spot; 100: uniform.</summary>
        public double Spread { get; set; } = 20;
        public bool KeepHp { get; set; }
        /// <summary>Carried items go with the monster (then those monsters only swap within their level); otherwise they stay with the spot.</summary>
        public bool LootWithMonster { get; set; }
        public bool RerollPowerful { get; set; }
        public double PowerfulChance { get; set; } = 5;
        /// <summary>0-100: how much deeper levels raise the powerful chance (double on the deepest at 100).</summary>
        public double PowerfulDepth { get; set; } = 50;
        /// <summary>Creatures with a conversation may move within their own level.</summary>
        public bool TalkersInLevel { get; set; }
        /// <summary>UW1's fire elementals or UW2's brain creatures neither move nor take another's place (on by default).</summary>
        public bool LeaveSpecialKinds { get; set; } = true;
        /// <summary>Creatures with a conversation allowed to move anyway (the level 2 gazer, the Listener...).</summary>
        public List<IgnoredObject> Whitelist { get; set; }
        public List<IgnoredObject> Ignored { get; set; }
        /// <summary>
        /// The ordering rule: "rank" (noisy rank matching by the scope, target and spread, the
        /// default) or "progression" (Uniform progression: each movement class sorted by
        /// strength and handed out level by level in the game's order; the scope, target and
        /// spread do not apply; MonsterProgression.cs).
        /// </summary>
        public string Rule { get; set; } = "rank";
        /// <summary>Whether the swap uses Uniform progression.</summary>
        [System.Text.Json.Serialization.JsonIgnore]
        public bool IsProgression { get { return Rule == "progression"; } }

        static readonly JsonSerializerOptions Json = new JsonSerializerOptions { WriteIndented = true };
        public string ToJson() { return JsonSerializer.Serialize(this, Json); }
        public static MonsterSwapOptions FromJson(string s) { return JsonSerializer.Deserialize<MonsterSwapOptions>(s, Json); }
    }

    /// <summary>A creature in the world the swap might move, with why it may or may not.</summary>
    public sealed class MonsterSpot
    {
        public int Level, Index;
        public ObjectInfo O;
        public CreatureKind Kind;
        public int Whoami;
        /// <summary>Why it never moves, or null.</summary>
        public string Blocked;
        /// <summary>Why it only swaps within its level, or null.</summary>
        public string LevelBound;
        public bool Water;
        /// <summary>The terrain class of the spot's floor (CreatureTerrain: 0 plain, 1 water, 2 lava, 3 ice).</summary>
        public int Floor;
        public double Strength;
        public string Label;
    }

    /// <summary>What a swap did.</summary>
    public sealed class MonsterSwapResult
    {
        public readonly Dictionary<int, List<KeyValuePair<int, byte[]>>> ByLevel = new Dictionary<int, List<KeyValuePair<int, byte[]>>>();
        public readonly Dictionary<int, List<KeyValuePair<int, byte[]>>> Original = new Dictionary<int, List<KeyValuePair<int, byte[]>>>();
        public readonly StringBuilder Report = new StringBuilder();
        /// <summary>Each level's block as Apply left it: Revert only puts back a level that has not been edited since.</summary>
        public readonly Dictionary<int, byte[]> After = new Dictionary<int, byte[]>();
        public int Changed { get { return ByLevel.Values.Sum(l => l.Count); } }
    }

    /// <summary>
    /// Swaps monsters between spots. The monsters trade identities (item, hit points unless
    /// kept, powerful, attitude, talked-to, conversation number), and each spot keeps its
    /// position, height, home, goal and the rest of its record, so nothing that names a spot
    /// by index or square breaks (no trap or trigger in either game links to a live creature;
    /// MONSTER-SWAP-PLAN). Creatures with a conversation never move unless whitelisted
    /// (plot deaths, schedules and hack traps find them by whoami, on the current level only),
    /// nor templates inside traps, summoned ones or ignored ones. Race-bound creatures (UW2
    /// liches, race 0xB, level 1 guards, level 4 bloodworms; UW1 Tybal's guards), ones whose
    /// goal targets another creature and, when loot goes with the monster, those that carry
    /// something, only swap within their level. Swimmers swap only with swimmers, a monster
    /// only goes where its height fits, and never onto water or lava it cannot stand on
    /// (CreatureTerrain). UW1's fire elementals and UW2's brain creatures stay by default.
    /// </summary>
    public static partial class MonsterSwap
    {
        public static double DepthOf(bool uw2, int level) { return ItemShuffle.DepthOf(uw2, level); }

        static int Word(byte[] r, int at) { return r[at] | r[at + 1] << 8; }

        /// <summary>The damage of a creature spell, by name: missile spells from OBJECTS.DAT's missile table, area spells an estimate.</summary>
        static double SpellDamage(GameSession s, byte[] objects, int spell)
        {
            if (spell == 0xFF || spell > 63) return 0;
            string n = (s.Strings.Get(6, 0x100 + spell) ?? "").Trim().ToLowerInvariant();
            int missile = -1;
            if (n == "magic arrow") missile = 0x17;
            else if (n == "electrical bolt" || n == "lightning") missile = 0x15;
            else if (n == "fireball") missile = 0x14;
            else if (n == "acid") missile = 0x16;
            else if (n == "deadly seeker") missile = 0x1B;
            else if (n == "frost") missile = 0x1C;
            if (missile >= 0 && objects != null && 0x82 + 3 * (missile - 0x10) < objects.Length) return objects[0x82 + 3 * (missile - 0x10)];
            if (n.Contains("sheet lightning") || n.Contains("flame wind") || n.Contains("armageddon") || n.Contains("tremor") || n.Contains("shockwave") || n.Contains("smite"))
                return 12;
            return 0;
        }

        /// <summary>Damage per round: the attacks weighted by their probability (damage + strength / 5), plus attack spells by how often the creature casts.</summary>
        public static double Damage(GameSession s, byte[] objects, CreatureKind k, bool powerful)
        {
            int psum = k.Attacks.Sum(a => a.Prob);
            double melee = psum > 0 ? k.Attacks.Sum(a => a.Prob * (a.Damage + k.Str / 5.0)) / psum : k.Attacks[0].Damage + k.Str / 5.0;
            if (powerful) melee += 9.5;
            double spells = 0;
            if (k.Caster > 0) spells = (11 * SpellDamage(s, objects, k.Spells[0]) + 5 * SpellDamage(s, objects, k.Spells[1])) / 16.0 * Math.Min(1, k.Caster / 64.0);
            return melee + spells;
        }

        public static double Strength(GameSession s, byte[] objects, CreatureKind k, int hp, bool powerful, string measure)
        {
            double tough = hp * (1 + k.ArmourMean * (powerful ? 5.0 / 3 : 1) / 8);
            switch (measure)
            {
                case "hp": return hp;
                case "damage": return Damage(s, objects, k, powerful);
                case "combined": return Math.Sqrt(Math.Max(0, tough) * Damage(s, objects, k, powerful));
                case "none": return 0;
                default: return Math.Max(0, k.Exp) * (powerful ? 2.25 : 1);
            }
        }

        /// <summary>A creature kind the game gives no name (UW1's Ethereal Void creatures 0x4F and 0x7B-0x7E).</summary>
        public static bool Unnamed(GameSession s, int item)
        {
            string n = s.Names.Get(item) ?? "";
            return n.Trim().Length == 0 || n.StartsWith("item 0x") || n.Contains("_");
        }

        /// <summary>The kinds the "leave alone" option keeps: UW1's fire elemental (0x78), UW2's brain creature (0x58).</summary>
        public static int SpecialKind(GameSession s) { return s.IsUw2 ? 0x58 : 0x78; }

        /// <summary>The special kind's name, for the window and the report.</summary>
        public static string SpecialKindName(GameSession s) { return s.IsUw2 ? "brain creatures" : "fire elementals"; }

        static bool RaceBound(GameSession s, CreatureKind k, int level)
        {
            if (s.IsUw2) return k.Race == 0x17 || k.Race == 0x0B || (level == 1 && k.Race == 0x1C) || (level == 4 && s.Names.Get(k.Item).Contains("bloodworm"));
            return level == 7 && k.Race == 0x13;
        }

        /// <summary>Every live creature with what the swap may do with it.</summary>
        public static List<MonsterSpot> Survey(GameSession s, Func<int, LevelModel> level, MonsterSwapOptions opt)
        {
            var kinds = CreatureKind.Read(s);
            string op = GameProfile.Find(s.DataFolder, "OBJECTS.DAT");
            byte[] objects = op == null ? null : File.ReadAllBytes(op);
            var ignored = new HashSet<long>((opt.Ignored ?? new List<IgnoredObject>()).Select(x => (long)x.Level << 32 | (uint)x.Index));
            var white = new HashSet<long>((opt.Whitelist ?? new List<IgnoredObject>()).Select(x => (long)x.Level << 32 | (uint)x.Index));
            var terrain = CreatureTerrain.TryLoad(s);
            int special = SpecialKind(s);
            var l = new List<MonsterSpot>();
            foreach (var slot in s.LevelSlots())
            {
                if (!slot.Present) continue;
                LevelModel m;
                try { m = level(slot.Level); } catch (Exception) { continue; }
                foreach (var o in m.Objects)
                {
                    if (o.Free || !o.IsMobile || !o.IsCritter || o.TileX < 0) continue;
                    CreatureKind k;
                    if (!kinds.TryGetValue(o.Item, out k)) continue;
                    var r = o.Record;
                    int att = Word(r, 0x0D), goal = Word(r, 0x0B);
                    var sp = new MonsterSpot { Level = slot.Level, Index = o.Index, O = o, Kind = k, Whoami = r[0x1A] };
                    long key = (long)slot.Level << 32 | (uint)o.Index;
                    string name = sp.Whoami > 0 ? (s.Strings.Get(7, sp.Whoami + 16) ?? "").Trim() : "";
                    sp.Label = string.Format("L{0} #{1} {2}{3} ({4}, {5}) hp {6}{7}", slot.Level, o.Index, s.Names.Get(o.Item), name.Length > 0 ? " \"" + name + "\"" : "", o.TileX, o.TileY, r[8], ((att >> 10) & 1) != 0 ? " powerful" : "");
                    if (o.Parent >= 0 && m.Objects[o.Parent].IsTrapOrTrigger) sp.Blocked = "a template inside a trap";
                    else if (o.Parent >= 0) sp.Blocked = "inside something";
                    else if (ignored.Contains(key)) sp.Blocked = "ignored";
                    else if (opt.LeaveSpecialKinds && o.Item == special) sp.Blocked = SpecialKindName(s) + ", left alone by the option";
                    else if (((att >> 8) & 1) != 0) sp.Blocked = "summoned";
                    else if (Unnamed(s, o.Item)) sp.Blocked = "a special creature without a name (the Ethereal Void's)";
                    else if (r[8] == 0) sp.Blocked = "no hit points";
                    else if (sp.Whoami > 0 && !white.Contains(key))
                    {
                        if (opt.TalkersInLevel) sp.LevelBound = "has a conversation";
                        else sp.Blocked = "has a conversation";
                    }
                    if (sp.Blocked == null)
                    {
                        if (RaceBound(s, k, slot.Level)) sp.LevelBound = sp.LevelBound ?? "its race is called on this level";
                        int gt = (goal >> 4) & 0xFF;
                        if (gt > 1 && gt < m.Objects.Length && m.Objects[gt].IsCritter) sp.LevelBound = sp.LevelBound ?? "its goal targets another creature";
                        if (opt.LootWithMonster && o.Children.Count > 0) sp.LevelBound = sp.LevelBound ?? "carries something";
                        // a talker's inventory is what its conversation gives and takes: it always goes along, so on its level
                        if (sp.Whoami > 0 && o.Children.Count > 0) sp.LevelBound = sp.LevelBound ?? "carries what its conversation gives or takes";
                    }
                    sp.Water = k.Swims;
                    sp.Floor = CreatureTerrain.FloorClass(s, terrain, m, o.TileX, o.TileY);
                    sp.Strength = Strength(s, objects, k, opt.KeepHp ? k.AvgHit : r[8], ((att >> 10) & 1) != 0, opt.Measure);
                    l.Add(sp);
                }
            }
            return l;
        }

        /// <summary>Whitelisted by default: creatures with a conversation whose plot only needs them alive somewhere (UW1's level 2 gazer, 0x6E; UW2's Listener, 0x91).</summary>
        public static List<IgnoredObject> DefaultWhitelist(GameSession s, Func<int, LevelModel> level)
        {
            int[] who = s.IsUw2 ? new[] { 0x91 } : new[] { 0x6E };
            var o = new MonsterSwapOptions { TalkersInLevel = true };
            return Survey(s, level, o).Where(x => who.Contains(x.Whoami) && x.Blocked == null).Select(x => new IgnoredObject { Level = x.Level, Index = x.Index, Label = x.Label }).ToList();
        }

        /// <summary>The headroom of a spot in z units: the ceiling (128) less the highest floor corner.</summary>
        static int Headroom(LevelModel m, int x, int y)
        {
            var t = m.Tiles[x, y];
            return 128 - LevelGeometry.CornerHeights(t).Max() * 8;
        }

        static double Gauss(Random rng)
        {
            double u1 = 1.0 - rng.NextDouble(), u2 = rng.NextDouble();
            return Math.Sqrt(-2 * Math.Log(u1)) * Math.Cos(2 * Math.PI * u2);
        }

        static List<double> Percentiles(IList<double> v, Random rng)
        {
            // ties broken at random, so equal strengths do not keep their order
            var order = Enumerable.Range(0, v.Count).Select(i => (i, v: v[i], t: rng.NextDouble())).OrderBy(x => x.v).ThenBy(x => x.t).ToList();
            var p = new double[v.Count];
            for (int r = 0; r < order.Count; r++) p[order[r].i] = order.Count == 1 ? 0.5 : (double)r / (order.Count - 1);
            return p.ToList();
        }

        /// <summary>
        /// Pairs monsters with spots by noisy rank matching: each monster's strength percentile and
        /// each spot's target percentile get Normal(0, σ) noise, both are sorted and paired in
        /// order; a monster that does not fit (height) takes the nearest one that does. Spots on
        /// water or lava are filled first, each with the creature nearest its rank that may stand
        /// there (Formula describes it all).
        /// </summary>
        static int[] Match(List<MonsterSpot> spots, MonsterSwapOptions opt, bool uw2, Random rng, Func<MonsterSpot, int> headroom)
        {
            int n = spots.Count;
            var assign = new int[n];
            if (n == 0) return assign;
            bool uniform = opt.Measure == "none" || opt.Spread >= 100;
            double sigma = opt.Spread / 100.0 * 0.5;
            var mp = Percentiles(spots.Select(x => x.Strength).ToList(), rng);
            var keep = Percentiles(spots.Select(x => x.Strength).ToList(), rng);
            var depth = Percentiles(spots.Select(x => DepthOf(uw2, x.Level)).ToList(), rng);
            double w = opt.DepthBlend / 100.0;
            var mk = Enumerable.Range(0, n).Select(i => uniform ? rng.NextDouble() : mp[i] + sigma * Gauss(rng)).ToList();
            var sk = Enumerable.Range(0, n).Select(i => uniform ? rng.NextDouble() : (1 - w) * keep[i] + w * depth[i] + sigma * Gauss(rng)).ToList();
            var monsters = Enumerable.Range(0, n).OrderBy(i => mk[i]).ToList();
            var places = Enumerable.Range(0, n).OrderBy(i => sk[i]).ToList();
            var rankOf = new int[n];
            for (int r = 0; r < n; r++) rankOf[monsters[r]] = r;
            var taken = new bool[n];
            var placed = new bool[n];

            // spots on water or lava first (CreatureTerrain): a spot whose own creature could not stand
            // there keeps it (the game put it there); the others take the creature nearest their rank
            // that may stand there, its height fitting if one does. The narrower floor goes first (in a
            // group of walkers only fliers may go on water, fliers and fire-resistant ones on lava), so
            // each spot finds one: at worst the creature that was there.
            for (int i = 0; i < n; i++)
                if (CreatureTerrain.Restricted(spots[i].Floor) && !CreatureTerrain.CanStand(spots[i].Kind, spots[i].Floor)) { assign[i] = i; taken[i] = placed[i] = true; }
            var classes = new[] { CreatureTerrain.Water, CreatureTerrain.Lava }
                .OrderBy(c => Enumerable.Range(0, n).Count(j => !taken[j] && CreatureTerrain.CanStand(spots[j].Kind, c))).ToList();
            foreach (int cls in classes)
                for (int r = 0; r < n; r++)
                {
                    int place = places[r];
                    if (placed[place] || spots[place].Floor != cls) continue;
                    int room = headroom(spots[place]);
                    int best = -1, bestD = int.MaxValue;
                    bool bestFits = false;
                    for (int j = 0; j < n; j++)
                    {
                        if (taken[j] || !CreatureTerrain.CanStand(spots[j].Kind, cls)) continue;
                        bool fits = spots[j].Kind.Height <= room;
                        int d = Math.Abs(rankOf[j] - r);
                        if ((fits && !bestFits) || (fits == bestFits && d < bestD)) { best = j; bestD = d; bestFits = fits; }
                    }
                    if (best < 0) best = place;                    // cannot happen (see above); the creature stays
                    assign[place] = best; taken[best] = placed[place] = true;
                }

            // walk the other spots in order; take the next monster that fits, else the nearest that does
            var free = monsters.Where(j => !taken[j]).ToList();
            foreach (int place in places)
            {
                if (placed[place]) continue;
                int room = headroom(spots[place]);
                int pick = -1;
                for (int j = 0; j < free.Count && j < 64; j++)
                    if (spots[free[j]].Kind.Height <= room) { pick = j; break; }
                if (pick < 0) pick = 0;
                assign[place] = free[pick];
                free.RemoveAt(pick);
            }
            return assign;
        }

        public static MonsterSwapResult Run(GameSession s, Func<int, LevelModel> level, MonsterSwapOptions opt)
        {
            var res = new MonsterSwapResult();
            var rng = new Random(opt.Seed);
            var all = Survey(s, level, opt);
            var moving = all.Where(x => x.Blocked == null).ToList();
            var models = new Dictionary<int, LevelModel>();
            Func<int, LevelModel> lm = l => { LevelModel m; if (!models.TryGetValue(l, out m)) models[l] = m = level(l); return m; };
            Func<MonsterSpot, int> headroom = sp => Headroom(lm(sp.Level), sp.O.TileX, sp.O.TileY);

            // the powerful flag first, so the strengths count it
            var powerful = moving.ToDictionary(x => x, x => ((Word(x.O.Record, 0x0D) >> 10) & 1) != 0);
            if (opt.RerollPowerful)
            {
                string op = GameProfile.Find(s.DataFolder, "OBJECTS.DAT");
                byte[] objects = op == null ? null : File.ReadAllBytes(op);
                foreach (var x in moving)
                {
                    double p = opt.PowerfulChance / 100 * (1 + opt.PowerfulDepth / 100 * DepthOf(s.IsUw2, x.Level));
                    powerful[x] = rng.NextDouble() < p;
                    x.Strength = Strength(s, objects, x.Kind, opt.KeepHp ? x.Kind.AvgHit : x.O.Record[8], powerful[x], opt.Measure);
                }
            }

            var newRec = new Dictionary<MonsterSpot, byte[]>();
            int moved = 0;
            // the spot takes the monster's identity (and its inventory, with loot, on the same level)
            Action<MonsterSpot, MonsterSpot, bool> put = (place, who, loot) =>
            {
                var r = (byte[])place.O.Record.Clone();
                var src = who.O.Record;
                // the identity: item (bits 0-8 of word 0), hit points, powerful, talked to, attitude, conversation
                int w0 = Word(r, 0) & ~0x1FF | (Word(src, 0) & 0x1FF);
                r[0] = (byte)w0; r[1] = (byte)(w0 >> 8);
                if (!opt.KeepHp) r[8] = src[8];
                int att = Word(r, 0x0D) & ~(0x400 | 0x2000 | 0xC000) | (Word(src, 0x0D) & (0x2000 | 0xC000)) | (powerful[who] ? 0x400 : 0);
                if (loot && place.Level == who.Level)
                {
                    // the inventory goes along: the link (word 3 bits 6-15) and the inventory-made bit
                    int w3 = Word(r, 6) & 0x3F | (Word(src, 6) & ~0x3F);
                    r[6] = (byte)w3; r[7] = (byte)(w3 >> 8);
                    att = att & ~0x1000 | (Word(src, 0x0D) & 0x1000);
                }
                r[0x0D] = (byte)att; r[0x0E] = (byte)(att >> 8);
                r[0x1A] = src[0x1A];
                newRec[place] = r;
                if (who != place) moved++;
            };
            var progressionReport = new StringBuilder();
            if (opt.IsProgression) Progress(s, opt, rng, moving, headroom, put, progressionReport);
            else
            {
                // groups: the scope, level-bound creatures within their level, swimmers apart
                Func<MonsterSpot, string> scopeKey = x =>
                    x.LevelBound != null || opt.Scope == "level" ? "L" + x.Level
                    : opt.Scope == "world" && s.IsUw2 ? "W" + ((x.Level - 1) / 8) : "all";
                var groups = moving.GroupBy(x => scopeKey(x) + (x.LevelBound != null && opt.Scope != "level" ? "/bound" : "") + (x.Water ? "/water" : "")).ToList();
                foreach (var g in groups.OrderBy(x => x.Key, StringComparer.Ordinal))
                {
                    var spots = g.ToList();
                    // inventories trade along with the monsters when asked, or when a talker carrying something is
                    // in the group (such a group is always one level's, so every link stays on its level)
                    bool loot = opt.LootWithMonster || spots.Any(x => x.Whoami > 0 && x.O.Children.Count > 0);
                    var assign = Match(spots, opt, s.IsUw2, rng, headroom);
                    for (int i = 0; i < spots.Count; i++) put(spots[i], spots[assign[i]], loot);
                }
            }
            var byLevel = new SortedDictionary<int, List<string>>();
            foreach (var kv in newRec)
            {
                if (kv.Value.SequenceEqual(kv.Key.O.Record)) continue;
                List<KeyValuePair<int, byte[]>> l, o;
                if (!res.ByLevel.TryGetValue(kv.Key.Level, out l)) res.ByLevel[kv.Key.Level] = l = new List<KeyValuePair<int, byte[]>>();
                if (!res.Original.TryGetValue(kv.Key.Level, out o)) res.Original[kv.Key.Level] = o = new List<KeyValuePair<int, byte[]>>();
                l.Add(new KeyValuePair<int, byte[]>(kv.Key.Index, kv.Value));
                o.Add(new KeyValuePair<int, byte[]>(kv.Key.Index, (byte[])kv.Key.O.Record.Clone()));
                List<string> t;
                if (!byLevel.TryGetValue(kv.Key.Level, out t)) byLevel[kv.Key.Level] = t = new List<string>();
                int item = (kv.Value[0] | kv.Value[1] << 8) & 0x1FF;
                t.Add(string.Format("  ({0}, {1}) {2} hp {3} → {4} hp {5}{6}", kv.Key.O.TileX, kv.Key.O.TileY, s.Names.Get(kv.Key.O.Item), kv.Key.O.Record[8], s.Names.Get(item), kv.Value[8], ((kv.Value[0x0D] | kv.Value[0x0E] << 8) & 0x400) != 0 ? " powerful" : ""));
            }
            var blocked = all.Where(x => x.Blocked != null).GroupBy(x => x.Blocked).Select(g => g.Key + " " + g.Count());
            var bound = moving.Where(x => x.LevelBound != null).GroupBy(x => x.LevelBound).Select(g => g.Key + " " + g.Count());
            res.Report.AppendFormat("Seed {0}: {1} creatures could move, {2} took another's place; {3} records change on {4} levels.\n", opt.Seed, moving.Count, moved, res.Changed, res.ByLevel.Count);
            if (opt.IsProgression)
                res.Report.AppendFormat("Strength: {0}; Uniform progression ({1}){2}{3}.\n", MeasureName(opt.Measure), Progression.OrderText(s),
                    opt.KeepHp ? "; hit points stay with the spot" : "", opt.LootWithMonster ? "; carried items go with the monster" : "; carried items stay with the spot");
            else
            res.Report.AppendFormat("Strength: {0}; target: {1}; spread {2}{3}{4}.\n", MeasureName(opt.Measure), opt.DepthBlend <= 0 ? "each spot keeps its difficulty" : opt.DepthBlend >= 100 ? "depth" : "blend " + opt.DepthBlend + "% depth", opt.Spread,
                opt.KeepHp ? "; hit points stay with the spot" : "", opt.LootWithMonster ? "; carried items go with the monster" : "; carried items stay with the spot");
            res.Report.AppendLine("Never moved: " + string.Join(", ", blocked) + ".");
            if (moving.Any(x => x.LevelBound != null)) res.Report.AppendLine("Only within their level: " + string.Join(", ", bound) + ".");
            int wet = moving.Count(x => x.Floor == CreatureTerrain.Water), hot = moving.Count(x => x.Floor == CreatureTerrain.Lava);
            if (wet + hot > 0) res.Report.AppendFormat("Spots on water: {0}, on lava: {1}; only fliers, swimmers (water) and fire-resistant creatures (lava) are put there.\n", wet, hot);
            if (progressionReport.Length > 0) res.Report.Append(progressionReport);
            res.Report.AppendLine();
            foreach (var kv in byLevel) { res.Report.AppendLine("Level " + kv.Key + ":"); foreach (var t in kv.Value) res.Report.AppendLine(t); }
            return res;
        }

        public static string MeasureName(string m)
        {
            switch (m)
            {
                case "hp": return "hit points";
                case "damage": return "damage per round";
                case "combined": return "√(toughness × damage)";
                case "none": return "none (uniform)";
                default: return "experience value";
            }
        }

        public static void Apply(MonsterSwapResult r, LevelEdits edits, int seed)
        {
            foreach (var kv in r.ByLevel)
            {
                var e = edits.For(kv.Key);
                e.RewriteRecords(kv.Value, "Swap monsters (seed " + seed + ")");
                r.After[kv.Key] = (byte[])e.Data.Clone();
            }
        }

        /// <summary>
        /// Puts the swapped records back. The records hold the object lists (next, link), so a level
        /// edited since the swap is left alone rather than half undone; those levels are returned.
        /// </summary>
        public static List<int> Revert(MonsterSwapResult r, LevelEdits edits)
        {
            var skipped = new List<int>();
            foreach (var kv in r.Original)
            {
                var e = edits.For(kv.Key);
                byte[] after;
                if (r.After.TryGetValue(kv.Key, out after) && !e.Data.AsSpan().SequenceEqual(after)) { skipped.Add(kv.Key); continue; }
                e.RewriteRecords(kv.Value, "Undo monster swap");
            }
            return skipped;
        }

        /// <summary>
        /// The world's creatures in words: how many there are and why some never move, every kind
        /// with its count and experience, and each level's creatures, experience and strongest.
        /// </summary>
        public static string WorldReport(GameSession s, Func<int, LevelModel> level)
        {
            var all = Survey(s, level, new MonsterSwapOptions { Whitelist = new List<IgnoredObject>(), Ignored = new List<IgnoredObject>() });
            var b = new StringBuilder();
            var live = all.Where(x => x.Blocked != "a template inside a trap" && x.Blocked != "inside something").ToList();
            Func<MonsterSpot, bool> powerful = x => ((x.O.Record[0x0D] | x.O.Record[0x0E] << 8) & 0x400) != 0;
            Func<MonsterSpot, double> xp = x => Math.Max(0, x.Kind.Exp) * (powerful(x) ? 2.25 : 1);
            b.AppendLine("The creatures as the levels hold them.").AppendLine();
            b.AppendFormat("{0} creatures in the world ({1} powerful, {2} swimmers, {3} fliers), {4} could move; {5} templates inside create-object traps.\n",
                live.Count, live.Count(powerful), live.Count(x => x.Kind.Swims), live.Count(x => x.Kind.Flier), live.Count(x => x.Blocked == null), all.Count(x => x.Blocked == "a template inside a trap"));
            foreach (var g in live.Where(x => x.Blocked != null).GroupBy(x => x.Blocked)) b.AppendFormat("  never moved, {0}: {1}\n", g.Key, g.Count());
            foreach (var g in live.Where(x => x.Blocked == null && x.LevelBound != null).GroupBy(x => x.LevelBound)) b.AppendFormat("  only within their level, {0}: {1}\n", g.Key, g.Count());
            b.AppendLine().AppendLine("By kind (experience each; powerful; with a conversation):");
            var dup = live.Select(x => x.O.Item).Distinct().GroupBy(i => s.Names.Get(i)).Where(g => g.Count() > 1).Select(g => g.Key).ToList();
            Func<int, string> kindName = i => s.Names.Get(i) + (dup.Contains(s.Names.Get(i)) ? string.Format(" (0x{0:X2})", i) : "");
            foreach (var g in live.GroupBy(x => x.O.Item).OrderBy(g => g.First().Kind.Exp).ThenBy(g => g.Key))
            {
                var k = g.First().Kind;
                b.AppendFormat("  {0,-24} {1,4} × {2,5} xp  hp {3,3}{4}{5}{6}\n", kindName(g.Key), g.Count(), k.Exp, k.AvgHit,
                    g.Count(powerful) > 0 ? "  powerful " + g.Count(powerful) : "", g.Count(x => x.Whoami > 0) > 0 ? "  talking " + g.Count(x => x.Whoami > 0) : "", k.Swims ? "  swims" : k.Flier ? "  flies" : "");
            }
            b.AppendLine().AppendLine("By level (creatures, could move, experience in all and on average, the strongest):");
            foreach (var g in live.GroupBy(x => x.Level).OrderBy(g => g.Key))
            {
                var strongest = g.OrderByDescending(xp).First();
                b.AppendFormat("  L{0,-3} {1,4} creatures, {2,4} could move, {3,7:0} xp, {4,6:0.0} each; strongest {5} ({6:0} xp)\n",
                    g.Key, g.Count(), g.Count(x => x.Blocked == null), g.Sum(xp), g.Average(xp), s.Names.Get(strongest.O.Item) + (strongest.Whoami > 0 ? " \"" + (s.Strings.Get(7, strongest.Whoami + 16) ?? "").Trim() + "\"" : ""), xp(strongest));
            }
            b.AppendLine().AppendLine("Creatures with a conversation (they stay unless whitelisted):");
            foreach (var x in live.Where(x => x.Whoami > 0 && x.Blocked == "has a conversation").Take(400)) b.AppendLine("  " + x.Label);
            return b.ToString();
        }

        /// <summary>The creature kinds with their measures, for the window's strength table.</summary>
        public static string StrengthTable(GameSession s)
        {
            var kinds = CreatureKind.Read(s);
            string op = GameProfile.Find(s.DataFolder, "OBJECTS.DAT");
            byte[] objects = op == null ? null : File.ReadAllBytes(op);
            var b = new StringBuilder();
            b.AppendLine("Kind                     XP   HP  armour  damage  combined  spells                         flags");
            foreach (var k in kinds.Values.Where(k => k.AvgHit > 0).OrderBy(k => k.Exp))
            {
                string n = s.Names.Get(k.Item);
                if (Unnamed(s, k.Item)) continue;
                var sp = k.Caster == 0 ? "" : string.Join(", ", k.Spells.Where(x => x != 0xFF && x < 64).Select(x => SpellTables.Name(s, x)).Distinct());
                b.AppendFormat("{0,-22} {1,5} {2,4} {3,7:0.0} {4,7:0.0} {5,9:0.0}  {6,-30} {7}\n", n, k.Exp, k.AvgHit, k.ArmourMean, Damage(s, objects, k, false),
                    Strength(s, objects, k, k.AvgHit, false, "combined"), sp.Length > 30 ? sp.Substring(0, 30) : sp, (k.Swims ? "swims " : "") + (k.Flier ? "flies " : "") + (k.Fireproof ? "fireproof " : "") + "h" + k.Height);
            }
            b.AppendLine().AppendLine(StrengthNote);
            return b.ToString();
        }

        /// <summary>The note under the strength table.</summary>
        public const string StrengthNote = "Damage: the attacks weighted by probability (damage + strength / 5), plus attack spells (missiles from OBJECTS.DAT, area spells 12) by the casting rate. Combined: √(hp × (1 + armour / 8) × damage). Powerful: armour × 5/3, damage + 9.5, experience × 2.25. Moves: fliers may go anywhere, swimmers on water, fire-resistant creatures on lava.";

        /// <summary>One row of the strength table.</summary>
        public sealed class StrengthRow
        {
            public int Item { get; set; }
            public string Name { get; set; }
            public int Xp { get; set; }
            public int Hp { get; set; }
            public double Armour { get; set; }
            public double Damage { get; set; }
            public double Combined { get; set; }
            public string Spells { get; set; }
            /// <summary>"flies", "swims" or "walks".</summary>
            public string Moves { get; set; }
            public bool Fireproof { get; set; }
            public int Height { get; set; }
        }

        /// <summary>The creature kinds with their measures (the named ones with hit points), by experience.</summary>
        public static List<StrengthRow> StrengthRows(GameSession s)
        {
            var kinds = CreatureKind.Read(s);
            string op = GameProfile.Find(s.DataFolder, "OBJECTS.DAT");
            byte[] objects = op == null ? null : File.ReadAllBytes(op);
            var l = new List<StrengthRow>();
            foreach (var k in kinds.Values.Where(k => k.AvgHit > 0).OrderBy(k => k.Exp).ThenBy(k => k.Item))
            {
                if (Unnamed(s, k.Item)) continue;
                l.Add(new StrengthRow
                {
                    Item = k.Item, Name = s.Names.Get(k.Item), Xp = k.Exp, Hp = k.AvgHit, Armour = k.ArmourMean,
                    Damage = Damage(s, objects, k, false), Combined = Strength(s, objects, k, k.AvgHit, false, "combined"),
                    Spells = k.Caster == 0 ? "" : string.Join(", ", k.Spells.Where(x => x != 0xFF && x < 64).Select(x => SpellTables.Name(s, x)).Distinct()),
                    Moves = k.Flier ? "flies" : k.Swims ? "swims" : "walks", Fireproof = k.Fireproof, Height = k.Height,
                });
            }
            return l;
        }

        static string Num(double v) { return v.ToString("0.###", System.Globalization.CultureInfo.InvariantCulture); }

        /// <summary>
        /// The swap's formula with the settings' values, true to Run and Match: the strength measure,
        /// the ranks, the keys with their noise, the pairing and the powerful roll. N(μ, σ) is a normal
        /// distribution of mean μ and standard deviation σ, U(0, 1) uniform.
        /// </summary>
        public static string Formula(GameSession s, MonsterSwapOptions opt)
        {
            var b = new StringBuilder();
            bool uniform = opt.Measure == "none" || opt.Spread >= 100;
            double sigma = opt.Spread / 100.0 * 0.5, w = opt.DepthBlend / 100.0;
            string hp = opt.KeepHp ? "h = the kind's average hit points (the spot keeps its own)" : "h = the monster's hit points";
            string depth = s.IsUw2 ? "((level − 1) mod 8) / 7" : "min(1, (level − 1) / 7)";
            string pw = opt.RerollPowerful ? "P = 1 if powerful after the roll below, else 0" : "P = 1 if powerful, else 0";
            b.Append("Strength (" + MeasureName(opt.Measure) + "): ");
            switch (opt.Measure)
            {
                case "hp": b.AppendLine("S = h,  " + hp); break;
                case "damage":
                    b.AppendLine("S = D,  " + pw);
                    b.AppendLine("  D = Σ prob·(damage + str/5) / Σ prob + 9.5·P + min(1, cast/64)·(11·spell₁ + 5·spell₂)/16"); break;
                case "combined":
                    b.AppendLine("S = √(h × (1 + A·(P ? 5/3 : 1)/8) × D),  A = the mean armour, " + pw);
                    b.AppendLine("  " + hp);
                    b.AppendLine("  D = Σ prob·(damage + str/5) / Σ prob + 9.5·P + min(1, cast/64)·(11·spell₁ + 5·spell₂)/16"); break;
                case "none": b.AppendLine("S = 0 (not used)"); break;
                default: b.AppendLine("S = max(0, XP) × (P ? 2.25 : 1),  " + pw); break;
            }
            if (opt.Measure == "damage" || opt.Measure == "combined") b.AppendLine("  spell = a missile's damage, 12 for an area spell, else 0");
            if (opt.IsProgression)
            {
                ProgressionFormula(s, b);
                if (opt.RerollPowerful)
                    b.AppendLine("Powerful, rolled first: P(powerful) = " + Num(opt.PowerfulChance / 100) + " × (1 + " + Num(opt.PowerfulDepth / 100) + " × depth),  depth = " + depth);
                return b.ToString().TrimEnd();
            }
            b.Append("In each group of n (").Append(opt.Scope == "level" ? "each level" : opt.Scope == "world" && s.IsUw2 ? "each world" : "all levels")
             .AppendLine("; level-bound ones in their level; swimmers apart):");
            if (uniform)
                b.AppendLine("  monster key m = U(0, 1),  spot key t = U(0, 1)" + (opt.Measure == "none" ? "  (strength: none)" : "  (spread 100)"));
            else
            {
                b.AppendLine("  p = rank(S) / (n − 1), ties at random;  p₀ = p of the monster that was at the spot");
                if (w > 0) b.AppendLine("  d = rank(depth of the spot) / (n − 1),  depth = " + depth);
                string t = w <= 0 ? "p₀" : w >= 1 ? "d" : Num(1 - w) + "·p₀ + " + Num(w) + "·d";
                b.AppendLine("  monster key m = p + N(0, σ);  spot key t = " + t + " + N(0, σ);  σ = 0.5 × spread/100 = " + Num(sigma));
            }
            b.AppendLine("  The k-th spot by t takes the k-th monster by m, if its height fits (else the next of up to 64 that does).");
            b.AppendLine("  Spots on water or lava go first, each taking the monster nearest its rank that may stand there");
            b.AppendLine("  (fliers; swimmers on water; fire-resistant ones on lava), else keeping its own.");
            if (opt.RerollPowerful)
                b.AppendLine("Powerful, rolled first: P(powerful) = " + Num(opt.PowerfulChance / 100) + " × (1 + " + Num(opt.PowerfulDepth / 100) + " × depth),  depth = " + depth);
            return b.ToString().TrimEnd();
        }
    }
}
