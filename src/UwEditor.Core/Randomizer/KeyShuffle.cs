using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using UltimateEditor.Games;
using UwEditor.Core.Editing;

namespace UwEditor.Core.Randomizer
{
    /// <summary>The key item shuffle's settings: the seed, the logic, what moves.</summary>
    public sealed class KeyShuffleOptions
    {
        public int Seed = 1;
        /// <summary>Further seeds tried (seed + 1, seed + 2 …) when a fill fails the final check.</summary>
        public int Retries = 20;
        /// <summary>The logic options (shared with the Automatic zones window).</summary>
        public AbilityOptions Logic = new AbilityOptions();
        /// <summary>A hand-made zone set (Karl's painted map, or the zone file in DATA) whose zone names label the places in the spoiler log; null for none.</summary>
        public ZoneSet Names;
        /// <summary>Where Names came from, said in the log.</summary>
        public string NamesFrom = "";
        public NavOptions Nav = new NavOptions();
        /// <summary>The zoning the solver uses (the Zones window's), or null: automatic zones made from the movement options.</summary>
        public ZoneSet Zoning;
        /// <summary>Where Zoning came from, said in the log.</summary>
        public string ZoningFrom = "";
        /// <summary>The categories that move (<see cref="KeyShuffle.Categories"/>).</summary>
        public HashSet<string> Move = new HashSet<string>(KeyShuffle.Categories);
        /// <summary>Items carried by creatures without a conversation take part, and their inventories are places.</summary>
        public bool CreatureInventories;
        /// <summary>Everything else the item swap moves (weapons, armour, food, coins, lights …) takes part as filler, and its places too.</summary>
        public bool Filler;

        public KeyShuffleOptions Clone()
        {
            var o = (KeyShuffleOptions)MemberwiseClone();
            o.Logic = Logic.Clone();
            o.Nav = Nav.Clone();
            o.Move = new HashSet<string>(Move);
            return o;
        }
    }

    /// <summary>An object the shuffle looked at: one that moves (an item, and its place a location) or one that stays, and why.</summary>
    public sealed class ShuffleObject
    {
        public int Level, Index, Item;
        public string Name = "";
        /// <summary>Its tile (a container's or a creature's for what they hold).</summary>
        public int X, Y;
        /// <summary>What holds it ("in #12 sack", "carried by #200 goblin"), or "" on the floor.</summary>
        public string Holder = "";
        /// <summary>keys, infinity, talismans, runes, abilities, ingredients, plot, filler.</summary>
        public string Category = "";
        /// <summary>A token it gives is one the logic asks for: it is placed by the fill, one by one.</summary>
        public bool Key;
        /// <summary>What it gives, as the placements of the levels read it (tokens, and count facts with quantities).</summary>
        public readonly List<Placement> Gives = new List<Placement>();
        /// <summary>Why it stays where it is, or null when it moves.</summary>
        public string Stays;
        /// <summary>What its place needs besides being reached (the locked container it lies in), or "".</summary>
        public string Requires = "";
        /// <summary>The zone it lies in, and the sphere at which the original game reaches its place (-1: never).</summary>
        public int Zone, Sphere = -1;
        /// <summary>Its record (item, quality, flags, link, owner, enchantment, is-quantity): what moves.</summary>
        public ItemRewrite Record;
        public string Key2 { get { return Level + ":" + Index; } }
        public string Where { get { return string.Format("L{0} ({1}, {2}){3}", Level, X, Y, Holder.Length > 0 ? " " + Holder : ""); } }
        public string Tokens { get { return string.Join(", ", Gives.Select(p => p.Grants.Length > 0 ? p.Grants : p.Quantity + " towards " + p.Counts).Distinct()); } }
        public override string ToString() { return string.Format("#{0} {1}, {2}", Index, Name, Where); }
    }

    /// <summary>The levels' bytes at one moment (KeyShuffle.Snapshot).</summary>
    public sealed class LevelSnapshot
    {
        internal KeyShuffle.World W;
        /// <summary>A level of the snapshot (to build zones from another thread).</summary>
        public LevelModel Level(int level) { return W.Model(level); }
    }

    /// <summary>One item put in a place.</summary>
    public sealed class KeyShuffleMove
    {
        /// <summary>The item (where it was) and the place it went to (what was there).</summary>
        public ShuffleObject Item, Place;
        /// <summary>The sphere at which the shuffled game reaches the place (-1: never; only for filler that nothing needs).</summary>
        public int Sphere = -1;
        /// <summary>Placed by the fill (a key item) or after it (the rest).</summary>
        public bool ByFill;
    }

    /// <summary>A door unlocked because only picking opened it and something the logic needs lies behind it (lock picking off).</summary>
    public sealed class UnlockedDoor
    {
        public int Level, X, Y, Door, Lock;
        public string Why = "";
        public override string ToString() { return string.Format("L{0} ({1}, {2}) #{3} (lock #{4}): {5}", Level, X, Y, Door, Lock, Why); }
    }

    /// <summary>What a shuffle did (or would do): the moves, the doors unlocked, the levels' bytes before and after, the check and the log.</summary>
    public sealed class KeyShuffleResult
    {
        public KeyShuffleOptions Options;
        public bool Success;
        /// <summary>Why nothing could be done (no UW1, nothing moves …), or null.</summary>
        public string Refused;
        /// <summary>The seed that gave the result (the asked seed, or a later one after retries).</summary>
        public int UsedSeed;
        public int Attempts;
        /// <summary>Why each failed attempt failed.</summary>
        public readonly List<string> Failures = new List<string>();
        /// <summary>Every object looked at; those that move have Stays null.</summary>
        public readonly List<ShuffleObject> Objects = new List<ShuffleObject>();
        public readonly List<KeyShuffleMove> Moves = new List<KeyShuffleMove>();
        public readonly List<UnlockedDoor> Unlocked = new List<UnlockedDoor>();
        /// <summary>The doors only picking opens (lock picking off), with what lies behind (for the log).</summary>
        public readonly List<string> PickOnlyDoors = new List<string>();
        /// <summary>The levels' bytes the shuffle started from, and what it makes of them (only levels that change).</summary>
        public readonly Dictionary<int, byte[]> Before = new Dictionary<int, byte[]>(), After = new Dictionary<int, byte[]>();
        public readonly Dictionary<int, byte[]> Textures = new Dictionary<int, byte[]>();
        /// <summary>The fresh solve of the shuffled levels (nothing assumed): the check.</summary>
        public TimelineSolve Check;
        public TimelineResult CheckBuild;
        /// <summary>The spheres of the original game (the same options) and of the shuffled one.</summary>
        public int SpheresBefore, SpheresAfter;
        public readonly StringBuilder Log = new StringBuilder();
        /// <summary>Each level's block as Apply left it: Undo only puts back a level not edited since.</summary>
        public readonly Dictionary<int, byte[]> Applied = new Dictionary<int, byte[]>();
        public bool IsApplied { get { return Applied.Count > 0; } }
        public IEnumerable<ShuffleObject> Moving { get { return Objects.Where(o => o.Stays == null); } }
    }

    /// <summary>
    /// The key item shuffle (Phase D of the automatic zones, UW1): the key items (keys, the Key of
    /// Infinity's parts, the talismans lying as objects, runestones and the rune bag, ability items,
    /// the dragon skin boots' ingredients, plot items lying in the levels) move to new places so that
    /// the game stays beatable under the chosen logic. The method is assumed fill: the key items in
    /// a random order are placed one by one, each time assuming that every key item not yet placed
    /// is held, computing the places the time-aware solver (<see cref="Timeline"/>) reaches with
    /// the chosen options, and choosing one of them at random. The other items fill the remaining
    /// places at random. A fresh solve of the shuffled levels (nothing assumed, with the return
    /// check) must reach the goal with no one-way trap stranding the player; otherwise the next
    /// seed is tried. Items trade places whole (their records: item, quality, flags, link or
    /// quantity, owner, enchantment; a key keeps its lock number in its owner field), across
    /// levels; nothing is written until Apply, one undoable step a level, and Undo puts back every
    /// byte. With lock picking off, a door only picking opens is unlocked when something the
    /// logic needs and that does not move lies behind it (Karl's rule).
    /// </summary>
    public static class KeyShuffle
    {
        /// <summary>The categories that can move.</summary>
        public static readonly string[] Categories = { "keys", "infinity", "talismans", "runes", "abilities", "ingredients", "plot" };

        public static string CategoryName(string c)
        {
            switch (c)
            {
                case "keys": return "Keys";
                case "infinity": return "Key of Infinity parts";
                case "talismans": return "Talismans lying as objects";
                case "runes": return "Runestones and the rune bag";
                case "abilities": return "Ability items";
                case "ingredients": return "Dragon skin boots' ingredients";
                case "plot": return "Plot items";
                case "filler": return "Everything else (filler)";
                default: return c;
            }
        }

        /// <summary>The boots' ingredients that move (Marrowsuck's food payment is any food: it moves only as filler).</summary>
        static readonly HashSet<string> Ingredients = new HashSet<string> { "item:strong_thread", "item:bowl", "item:dead_rotworm", "item:mushroom", "item:flask_of_port" };

        // ---------------------------------------------------------------- the levels as bytes

        internal sealed class World
        {
            public GameSession S;
            public readonly Dictionary<int, byte[]> Data = new Dictionary<int, byte[]>(), Tex = new Dictionary<int, byte[]>();
            readonly Dictionary<int, LevelModel> models = new Dictionary<int, LevelModel>();
            public List<int> Levels = new List<int>();

            public LevelModel Model(int level)
            {
                LevelModel m;
                if (models.TryGetValue(level, out m)) return m;
                byte[] d;
                if (!Data.TryGetValue(level, out d)) return S.LoadLevel(level);
                m = LevelModel.FromBytes(S, level, d, Tex[level]);
                models[level] = m;
                return m;
            }

            public void Changed(int level) { models.Remove(level); }

            public World Copy()
            {
                var w = new World { S = S, Levels = Levels };
                foreach (var kv in Data) w.Data[kv.Key] = (byte[])kv.Value.Clone();
                foreach (var kv in Tex) w.Tex[kv.Key] = kv.Value == null ? null : (byte[])kv.Value.Clone();
                return w;
            }
        }

        static World Load(GameSession s, LevelEdits edits)
        {
            var w = new World { S = s };
            foreach (var slot in s.LevelSlots())
            {
                if (!slot.Present) continue;
                LevelModel m;
                try { m = edits != null ? edits.Model(slot.Level) : s.LoadLevel(slot.Level); } catch (Exception) { continue; }
                if (m == null || !m.Present) continue;
                var e = edits == null ? null : edits.Existing(slot.Level);
                w.Data[slot.Level] = (byte[])(e != null ? e.Data : m.Block.Data).Clone();
                var tex = e != null ? e.TextureBytes : m.TextureBytes;
                w.Tex[slot.Level] = tex == null ? null : (byte[])tex.Clone();
                w.Levels.Add(slot.Level);
            }
            return w;
        }

        static ItemRewrite RecordOf(ObjectInfo o)
        {
            return new ItemRewrite { Index = o.Index, Item = o.Item, Quality = o.Quality, Flags = (o.Record[1] >> 1) & 7, Link = o.Link, Owner = o.Owner, Enchanted = o.Enchanted, IsQuantity = o.IsQuantity, Exact = true };
        }

        /// <summary>Writes a whole object's item words into a slot, as LevelEditor.RewriteItems does with Exact (tenacious and invisible stay the slot's).</summary>
        static void Write(byte[] data, ItemRewrite c)
        {
            int at = LevelBlock.ObjectOffset(c.Index);
            int w0 = data[at] | data[at + 1] << 8, w2 = data[at + 4] | data[at + 5] << 8, w3 = data[at + 6] | data[at + 7] << 8;
            w0 = (w0 & 0x6000) | (c.Item & 0x1FF) | (c.Flags & 7) << 9 | (c.Enchanted ? 0x1000 : 0) | (c.IsQuantity ? 0x8000 : 0);
            w2 = (w2 & ~0x3F) | (c.Quality & 0x3F);
            w3 = (c.Owner & 0x3F) | (c.Link & 0x3FF) << 6;
            data[at] = (byte)w0; data[at + 1] = (byte)(w0 >> 8);
            data[at + 4] = (byte)w2; data[at + 5] = (byte)(w2 >> 8);
            data[at + 6] = (byte)w3; data[at + 7] = (byte)(w3 >> 8);
        }

        // ---------------------------------------------------------------- what moves

        static readonly Regex PlacedObject = new Regex(@"^#(\d+) .*?level (\d+) \(");

        /// <summary>Objects named by a trap or trigger's link (a delete trap's target and the like), not create traps' templates.</summary>
        static HashSet<int> Named(LevelModel m)
        {
            var set = new HashSet<int>();
            foreach (var o in m.Objects)
            {
                if (o.Free || o.TileX < 0 || !o.IsTrapOrTrigger || o.Link <= 0 || o.Link >= m.Objects.Length) continue;
                if (o.Item == 0x187 && !o.IsQuantity) continue;
                set.Add(o.Link);
            }
            return set;
        }

        /// <summary>The category of an object, or null when it is none of them (then only filler).</summary>
        static string CategoryOf(GameSession s, ObjectInfo o, HashSet<int> abilityItems, IEnumerable<Placement> gives)
        {
            int it = o.Item;
            if (KeyIndex.IsKey(it, s.Kind)) return "keys";
            if (it >= 0xE1 && it <= 0xE7) return "infinity";
            string kind;
            string tok = KeyItems.Classify(s, o, out kind);
            if (kind == "talisman") return "talismans";
            if ((it >= 0xE8 && it <= 0xFF) || it == Challenges.RuneBag) return "runes";
            if (abilityItems.Contains(o.Index) || it == ItemTables.Pole || it == 0x101 || it == 0x126 || it == 0x122) return "abilities";
            if (gives.Any(p => Ingredients.Contains(Requirement.Normalize(p.Grants)))) return "ingredients";
            if (kind == "quest item") return "plot";
            return null;
        }

        /// <summary>The tokens the logic asks for anywhere (links, facts, placements' conditions, mechanisms, the goal), with what each ability or count stands for.</summary>
        static HashSet<string> Needed(TimelineResult t, SolverOptions so)
        {
            var texts = new List<string>();
            texts.AddRange(t.Set.Links.Select(k => k.Requires));
            texts.AddRange(t.FactLinks.Select(k => k.Requires));
            foreach (var f in t.Facts.Facts) { texts.Add(f.Requires); texts.Add(f.Otherwise); texts.Add(f.Until); }
            texts.AddRange(t.Set.Placements.Select(p => p.Requires));
            foreach (var m in t.Abilities.Auto.Graph.Mechanisms) { texts.Add(m.Conditions); texts.Add(Regex.Replace(m.SetOff ?? "", @"region:\d+", "free")); }
            texts.Add(t.GoalFor(so));
            var toks = new List<string>();
            foreach (var x in texts)
            {
                if (string.IsNullOrWhiteSpace(x)) continue;
                var r = Requirement.TryParse(x, out _);
                if (r != null) r.Tokens(toks);
            }
            var h = new HashSet<string>();
            foreach (var raw in toks)
            {
                string k = Requirement.Normalize(raw);
                h.Add(k);
                int c = k.IndexOf(':');
                string head = c < 0 ? k : k.Substring(0, c), tail = c < 0 ? "" : k.Substring(c + 1);
                if (head == "spell" || head == "use") { foreach (var p in new[] { "cast:", "worn:", "perm:", "once:", "spell:" }) h.Add(p + tail); }
                if (head == "talismans") h.Add("talisman:*");
                if (head == "pick") h.Add("item:lockpick");
            }
            return h;
        }

        static bool IsNeeded(HashSet<string> needed, Placement p, TimelineResult t)
        {
            if (p.Grants.Length > 0)
            {
                string g = Requirement.Normalize(p.Grants);
                return needed.Contains(g) || (g.StartsWith("talisman:") && needed.Contains("talisman:*"));
            }
            var cf = t.Facts.OfKind("count").FirstOrDefault(f => f.Id == p.Counts);
            return cf != null && needed.Contains(Requirement.Normalize(cf.Grants));
        }

        /// <summary>Every item object of the levels the shuffle could look at, each with its category, what it gives, and why it stays (or null).</summary>
        static List<ShuffleObject> Survey(GameSession s, World w, TimelineResult t, KeyShuffleOptions opt, HashSet<string> needed)
        {
            var list = new List<ShuffleObject>();
            var byObject = new Dictionary<string, List<Placement>>();
            foreach (var p in t.Set.Placements)
            {
                if (p.Source != "levels") continue;
                var mm = PlacedObject.Match(p.Notes ?? "");
                if (!mm.Success) continue;
                string k = mm.Groups[2].Value + ":" + mm.Groups[1].Value;
                List<Placement> l;
                if (!byObject.TryGetValue(k, out l)) byObject[k] = l = new List<Placement>();
                l.Add(p);
            }
            var abilityItems = new Dictionary<int, HashSet<int>>();
            foreach (var src in t.Abilities.Sources) { HashSet<int> h; if (!abilityItems.TryGetValue(src.Level, out h)) abilityItems[src.Level] = h = new HashSet<int>(); h.Add(src.Index); }
            // objects the facts name by object as an item to have (flag facts) or as a warp's destination: they stay
            var factNamed = new Dictionary<string, string>();
            foreach (var f in t.Facts.Facts)
            {
                if (f.Kind == "flag" && f.Object >= 0) factNamed[f.Level + ":" + f.Object] = f.Id;
                if (f.Kind == "warp" && f.To != null && f.To.HasObject) factNamed[f.To.Level + ":" + f.To.Object] = f.Id;
            }
            var traded = new HashSet<string>(t.Facts.OfKind("trade").Select(f => Requirement.Normalize(f.Token)));
            var guarded = new HashSet<int> { 0xA7 };      // the emerald puzzle (event/WORLDEV.C emerald_trap), as ItemCleaner guards it
            foreach (int l in w.Levels) foreach (var o in w.Model(l).Objects) if (!o.Free && o.TileX >= 0 && o.Item == 0x18C) guarded.Add((o.Quality << 5 | o.Owner) & 0x1FF);
            // the squares beside Shak and Zak, where the challenges' exceptions lie
            var exceptionSquares = new HashSet<string>();
            foreach (int l in w.Levels)
                foreach (var o in w.Model(l).Objects)
                    if (!o.Free && o.OnTile && o.IsCritter && o.Record.Length > 0x1A && (o.Record[0x1A] == Challenges.ShakWhoami || o.Record[0x1A] == Challenges.ZakWhoami))
                        for (int dx = -1; dx <= 1; dx++) for (int dy = -1; dy <= 1; dy++) exceptionSquares.Add(l + ":" + (o.TileX + dx) + ":" + (o.TileY + dy));
            var filler = opt.Filler ? new HashSet<string>(ItemShuffle.Candidates(s, w.Model).Select(c => c.Level + ":" + c.Index)) : new HashSet<string>();
            foreach (int level in w.Levels)
            {
                var m = w.Model(level);
                var named = Named(m);
                HashSet<int> abil;
                if (!abilityItems.TryGetValue(level, out abil)) abil = new HashSet<int>();
                foreach (var o in m.Objects)
                {
                    if (o.Free || o.TileX < 0 || o.IsCritter || o.Item >= 0x140 || o.Index < 2) continue;
                    string key = level + ":" + o.Index;
                    List<Placement> gives;
                    if (!byObject.TryGetValue(key, out gives)) gives = new List<Placement>();
                    string cat = CategoryOf(s, o, abil, gives);
                    bool fill = filler.Contains(key);
                    if (cat == null && !fill) continue;
                    var so = new ShuffleObject
                    {
                        Level = level, Index = o.Index, Item = o.Item, Name = s.Names.Get(o.Item), X = o.TileX, Y = o.TileY,
                        Category = cat ?? "filler", Record = RecordOf(o), Zone = t.Set.NearestZone(level, o.TileX, o.TileY),
                    };
                    so.Gives.AddRange(gives);
                    so.Requires = AbilityZones.ContainerLock(m, o) ?? "";
                    if (so.Requires.Length > 0) so.Holder += " (locked: " + so.Requires + ")";
                    // who holds it, and whether that keeps it
                    bool trap = false, template = false;
                    ObjectInfo carrier = null;
                    for (int up = o.Parent, g = 0; up >= 0 && g < 64; up = m.Objects[up].Parent, g++)
                    {
                        var u = m.Objects[up];
                        if (u.IsTrapOrTrigger) { trap = true; if (u.Item == 0x187) template = true; }
                        if (u.IsCritter && carrier == null) carrier = u;
                    }
                    if (o.Parent >= 0)
                    {
                        var h = m.Objects[o.Parent];
                        so.Holder = carrier != null ? string.Format("carried by #{0} {1}", carrier.Index, ObjectTexts.NpcName(s, carrier) ?? s.Names.Get(carrier.Item)) : string.Format("in #{0} {1}", h.Index, s.Names.Get(h.Item));
                    }
                    so.Key = cat != null && gives.Any(p => IsNeeded(needed, p, t));
                    string factId;
                    if (cat != null && !opt.Move.Contains(cat)) so.Stays = "its category does not move";
                    else if (template) so.Stays = "a create trap's template (the trap makes it)";
                    else if (trap) so.Stays = "inside a trap";
                    else if (named.Contains(o.Index)) so.Stays = "a trap or trigger names it";
                    else if (o.Children.Any(c => m.Objects[c].IsTrapOrTrigger)) so.Stays = "it holds a trap or trigger";
                    else if (!o.IsQuantity && o.Link != 0) so.Stays = "it holds other objects (its list stays on its level)";
                    else if (o.IsMobile) so.Stays = "a mobile record";
                    else if (carrier != null && !opt.CreatureInventories) so.Stays = "carried by a creature (creature inventories off)";
                    else if (carrier != null && carrier.Record.Length > 0x1A && carrier.Record[0x1A] != 0) so.Stays = "carried by an NPC with a conversation (it may hand it over)";
                    else if (factNamed.TryGetValue(key, out factId)) so.Stays = "the fact " + factId + " names it by its object";
                    else if (gives.Any(p => p.Grants.Length > 0 && traded.Contains(Requirement.Normalize(p.Grants)))) so.Stays = "a trade fact ties its token to who holds it";
                    else if (o.Item == 0x117) so.Stays = "an orb (Tyball's orb on L7 is the same item, and the facts name it)";
                    else if (guarded.Contains(o.Item)) so.Stays = "the game looks for this kind (the emerald puzzle, an inventory trap)";
                    else if (exceptionSquares.Contains(level + ":" + o.TileX + ":" + o.TileY) && (Challenges.IsCoinOrGem(s, o.Item) || Challenges.IsFood(s, o.Item)) && cat == null)
                        so.Stays = "beside Shak or Zak (the challenges' exceptions)";
                    else if (o.Item == 0x126) so.Stays = "the moonstone: Gate Travel goes to it (fact gate_travel), and the sealed moonorb area beside it is entered only that way";
                    list.Add(so);
                }
            }
            return list;
        }

        // ---------------------------------------------------------------- the solves

        sealed class Solver
        {
            public TimelineResult T;
            public KeyShuffleOptions Opt;
            /// <summary>The zone set's placements that stay as they are (not of an object that moves).</summary>
            public List<Placement> Fixed;
            public List<ShuffleObject> Places;
            public Dictionary<Placement, int[]> Spots = new Dictionary<Placement, int[]>();

            public TimelineSolve Run(Dictionary<ShuffleObject, ShuffleObject> placed, IEnumerable<int> probes, IEnumerable<string> assumed, string label)
            {
                var list = new List<Placement>(Fixed);
                var spots = new Dictionary<Placement, int[]>();
                foreach (var kv in placed)
                    foreach (var g in kv.Key.Gives)
                    {
                        var p = new Placement { Zone = kv.Value.Zone, Grants = g.Grants, Requires = kv.Value.Requires, Counts = g.Counts, Quantity = g.Quantity, Notes = g.Notes, Source = "key item shuffle" };
                        list.Add(p);
                        spots[p] = new[] { kv.Value.Level, kv.Value.X, kv.Value.Y };
                    }
                foreach (int i in probes)
                {
                    var at = Places[i];
                    var p = new Placement { Zone = at.Zone, Grants = "loc:" + i, Requires = at.Requires, Notes = "place " + i, Source = "key item shuffle" };
                    list.Add(p);
                    spots[p] = new[] { at.Level, at.X, at.Y };
                }
                var so = Timeline.SolverFor(T, Opt.Logic, assumed);
                T.Engine.UsePlacements(list, spots);
                try { return T.Engine.Run(so, Opt.Logic, label, null); }
                finally { T.Engine.UsePlacements(null, null); }
            }
        }

        static Solver MakeSolver(TimelineResult t, KeyShuffleOptions opt, List<ShuffleObject> objs)
        {
            var moving = new HashSet<string>(objs.Where(o => o.Stays == null).Select(o => o.Key2));
            var fixedList = new List<Placement>();
            foreach (var p in t.Set.Placements)
            {
                var mm = p.Source == "levels" ? PlacedObject.Match(p.Notes ?? "") : Match.Empty;
                if (mm.Success && moving.Contains(mm.Groups[2].Value + ":" + mm.Groups[1].Value)) continue;
                fixedList.Add(p);
            }
            return new Solver { T = t, Opt = opt, Fixed = fixedList, Places = objs.Where(o => o.Stays == null).ToList() };
        }

        static IEnumerable<string> GivesTokens(TimelineResult t, IEnumerable<ShuffleObject> objs)
        {
            var h = new HashSet<string>();
            foreach (var o in objs)
                foreach (var g in o.Gives)
                {
                    if (g.Grants.Length > 0) { h.Add(Requirement.Normalize(g.Grants)); continue; }
                    var cf = t.Facts.OfKind("count").FirstOrDefault(f => f.Id == g.Counts);
                    if (cf != null) h.Add(Requirement.Normalize(cf.Grants));
                }
            return h;
        }

        // ---------------------------------------------------------------- the shuffle

        /// <summary>
        /// Computes a shuffle of the levels as they are (with their unsaved edits): nothing is
        /// written. UW1 only (the facts and the time-aware solver are UW1's).
        /// </summary>
        public static KeyShuffleResult Run(GameSession s, LevelEdits edits, KeyShuffleOptions opt, FactSet facts = null)
        {
            return RunOn(s, Snapshot(s, edits), opt, facts);
        }

        /// <summary>A copy of the levels' bytes as they are now (with their unsaved edits), to shuffle from another thread.</summary>
        public static LevelSnapshot Snapshot(GameSession s, LevelEdits edits) { return new LevelSnapshot { W = Load(s, edits) }; }

        /// <summary>Computes a shuffle of a snapshot of the levels (see <see cref="Snapshot"/>): nothing is written.</summary>
        public static KeyShuffleResult RunOn(GameSession s, LevelSnapshot levels, KeyShuffleOptions opt, FactSet facts = null)
        {
            opt = opt.Clone();
            var r = new KeyShuffleResult { Options = opt };
            if (s.IsUw2) { r.Refused = "The key item shuffle works on Ultima Underworld 1 only (its facts and solver)."; r.Log.AppendLine(r.Refused); return r; }
            var w0 = levels.W.Copy();
            foreach (int l in w0.Levels) { r.Before[l] = (byte[])w0.Data[l].Clone(); r.Textures[l] = w0.Tex[l]; }
            var w = w0.Copy();
            var t = Timeline.Build(s, w.Model, opt.Nav, opt.Logic, null, facts, false, opt.Zoning);
            var so0 = Timeline.SolverFor(t, opt.Logic, null);
            var needed = Needed(t, so0);
            var objs = Survey(s, w, t, opt, needed);

            // ---- doors only picking opens (lock picking off): unlock those in front of something needed that does not move
            if (!opt.Logic.LockPicking) Unlock(s, w, ref t, opt, facts, objs, r);
            if (r.Unlocked.Count > 0)
            {
                so0 = Timeline.SolverFor(t, opt.Logic, null);
                needed = Needed(t, so0);
                objs = Survey(s, w, t, opt, needed);
            }
            r.Objects.AddRange(objs);
            var solver = MakeSolver(t, opt, objs);
            var places = solver.Places;
            if (places.Count == 0) { r.Refused = "Nothing moves with these categories."; r.Log.AppendLine(r.Refused); return r; }

            // ---- the spheres of the original game: every item where it is, every place probed
            var orig = places.ToDictionary(o => o, o => o);
            var sv0 = solver.Run(orig, Enumerable.Range(0, places.Count), null, "original");
            r.SpheresBefore = sv0.Spheres.Count;
            for (int i = 0; i < places.Count; i++) { int sp; places[i].Sphere = sv0.TokenSphere.TryGetValue("loc:" + i, out sp) ? sp : -1; }
            foreach (var o in objs.Where(o => o.Stays != null)) o.Sphere = -1;

            var keys = places.Where(o => o.Key).OrderBy(o => o.Level).ThenBy(o => o.Index).ToList();
            var rest = places.Where(o => !o.Key).OrderBy(o => o.Level).ThenBy(o => o.Index).ToList();
            var assumedRest = GivesTokens(t, rest).ToList();
            int tries = Math.Max(1, opt.Retries + 1);
            for (int attempt = 0; attempt < tries; attempt++)
            {
                int seed = unchecked(opt.Seed + attempt);
                r.Attempts = attempt + 1;
                var rng = new Random(seed);
                var order = keys.ToList();
                Shuffle(rng, order);
                var empty = new List<int>(Enumerable.Range(0, places.Count));
                var placed = new Dictionary<ShuffleObject, ShuffleObject>();
                string fail = null;
                for (int k = 0; k < order.Count && fail == null; k++)
                {
                    var item = order[k];
                    var assumed = GivesTokens(t, order.Skip(k + 1)).Concat(assumedRest);
                    var sv = solver.Run(placed, empty, assumed, "fill");
                    var reach = empty.Where(i => sv.TokenSphere.ContainsKey("loc:" + i)).ToList();
                    if (reach.Count == 0) { fail = "no place left that the logic reaches for " + item; break; }
                    int at = reach[rng.Next(reach.Count)];
                    placed[item] = places[at];
                    empty.Remove(at);
                }
                if (fail != null) { r.Failures.Add("seed " + seed + ": " + fail); continue; }
                // the rest at random
                var others = rest.ToList();
                Shuffle(rng, others);
                for (int k = 0; k < others.Count; k++) placed[others[k]] = places[empty[k]];
                // write it on a copy and solve that afresh
                var wv = w.Copy();
                foreach (var kv in placed)
                {
                    if (kv.Key == kv.Value) continue;
                    var rec = kv.Key.Record;
                    rec.Index = kv.Value.Index;
                    Write(wv.Data[kv.Value.Level], rec);
                }
                TimelineResult tv;
                try { tv = Timeline.Build(s, wv.Model, opt.Nav, opt.Logic, null, facts, false, opt.Zoning); }
                catch (Exception ex) { r.Failures.Add("seed " + seed + ": the check failed: " + ex.Message); continue; }
                var check = tv.Solve;
                var strands = check.Traps.Where(x => x.Strands.Count > 0).ToList();
                if (!check.GoalReached) { r.Failures.Add("seed " + seed + ": the shuffled levels do not reach the goal (missing " + string.Join(", ", check.GoalMissing.Distinct().Take(6)) + ")"); continue; }
                if (strands.Count > 0) { r.Failures.Add("seed " + seed + ": a one-way trap strands the player (" + string.Join("; ", strands.Select(x => SolveResult.ZoneName(tv.Set, x.Zone) + ": " + string.Join(", ", x.Strands))) + ")"); continue; }
                // success
                r.Success = true;
                r.UsedSeed = seed;
                r.Check = check;
                r.CheckBuild = tv;
                r.SpheresAfter = check.Spheres.Count;
                var final = solver.Run(placed, Enumerable.Range(0, places.Count), null, "shuffled");
                foreach (var kv in placed)
                {
                    int i = places.IndexOf(kv.Value), sp;
                    r.Moves.Add(new KeyShuffleMove { Item = kv.Key, Place = kv.Value, ByFill = kv.Key.Key, Sphere = final.TokenSphere.TryGetValue("loc:" + i, out sp) ? sp : -1 });
                }
                foreach (int l in wv.Levels)
                    if (!wv.Data[l].AsSpan().SequenceEqual(r.Before[l])) r.After[l] = wv.Data[l];
                break;
            }
            WriteLog(s, r, t);
            return r;
        }

        static void Shuffle<T>(Random rng, IList<T> l)
        {
            for (int i = l.Count - 1; i > 0; i--) { int j = rng.Next(i + 1); var x = l[i]; l[i] = l[j]; l[j] = x; }
        }

        /// <summary>
        /// Lock picking off: the doors that only picking opens (no key in the levels fits and the
        /// door cannot be bashed) and that block the goal when every item that moves is assumed
        /// held (so what lies behind and is needed does not move) are unlocked: the lock's locked
        /// flag (flag 9, obj/USEITEMS.C checkLock) is cleared, one door at a time until the goal
        /// is reached or nothing more helps. Karl: "If lockpicking is set as NOT required, then any
        /// locked doors with no keys will have to be passable if a key item is behind their zone."
        /// The fill never puts a key item behind such a door, since the logic does not reach it.
        /// </summary>
        static void Unlock(GameSession s, World w, ref TimelineResult t, KeyShuffleOptions opt, FactSet facts, List<ShuffleObject> objs, KeyShuffleResult r)
        {
            for (int round = 0; round < 12; round++)
            {
                var assumed = GivesTokens(t, objs.Where(o => o.Stays == null)).ToList();
                var sv = Timeline.Solve(t, opt.Logic, "every moving item held", assumed);
                var doors = new List<BlockedLink>();
                foreach (var b in sv.Blocked)
                {
                    var k = b.Link;
                    if ((k.Kind != "door" && k.Kind != "portcullis" && k.Kind != "secret") || k.Level <= 0) continue;
                    if (!(k.Requires ?? "").Contains("pick:")) continue;
                    if (doors.Any(d => d.Link.Level == k.Level && d.Link.X == k.X && d.Link.Y == k.Y)) continue;
                    doors.Add(b);
                }
                if (round == 0)
                    foreach (var b in doors) r.PickOnlyDoors.Add(string.Format("L{0} ({1}, {2}) {3}: {4} -> {5}", b.Link.Level, b.Link.X, b.Link.Y, b.Link.Requires, SolveResult.ZoneName(t.Set, b.FromZone), SolveResult.ZoneName(t.Set, b.ToZone)));
                if (sv.GoalReached || doors.Count == 0) return;
                // the first door (in a fixed order) whose opening adds most
                UnlockedDoor best = null;
                foreach (var b in doors.OrderBy(d => d.Link.Level).ThenBy(d => d.Link.Y).ThenBy(d => d.Link.X))
                {
                    var m = w.Model(b.Link.Level);
                    // the edge's square is the door's or the one beside it: the closed door there that only picking opens
                    foreach (var d in new[] { (0, 0), (1, 0), (-1, 0), (0, 1), (0, -1) })
                    {
                        int x = b.Link.X + d.Item1, y = b.Link.Y + d.Item2;
                        if (x < 0 || y < 0 || x > 63 || y > 63) continue;
                        foreach (var door in m.TileObjects(x, y).Where(o => o.Item >= 0x140 && o.Item <= 0x147))
                        {
                            string req = ZoneDoors.Requirement(s, m, door, out _);
                            if (!req.Split('|').Select(a => a.Trim()).All(a => a.StartsWith("pick:"))) continue;
                            var lk = door.Children.Select(c => m.Objects[c]).FirstOrDefault(o => o.Item == 0x10F && ((o.Record[1] >> 1) & 1) != 0);
                            if (lk == null) continue;
                            best = new UnlockedDoor { Level = b.Link.Level, X = x, Y = y, Door = door.Index, Lock = lk.Index, Why = "only picking opened it (" + req + "), and the goal needs what lies behind (" + SolveResult.ZoneName(t.Set, b.ToZone) + ")" };
                            break;
                        }
                        if (best != null) break;
                    }
                    if (best != null) break;
                }
                if (best == null) return;
                int at = LevelBlock.ObjectOffset(best.Lock);
                w.Data[best.Level][at + 1] &= 0xFD;      // flag 9: locked
                w.Changed(best.Level);
                r.Unlocked.Add(best);
                t = Timeline.Build(s, w.Model, opt.Nav, opt.Logic, null, facts, false, opt.Zoning);
            }
        }

        // ---------------------------------------------------------------- writing

        /// <summary>Puts the shuffle into the levels: one undoable step a level (LevelEditor.Replace). Throws when a level changed since the shuffle was computed.</summary>
        public static void Apply(KeyShuffleResult r, LevelEdits edits)
        {
            if (!r.Success) throw new InvalidOperationException("There is no successful shuffle to apply.");
            foreach (var kv in r.After)
            {
                var e = edits.For(kv.Key);
                if (!e.Data.AsSpan().SequenceEqual(r.Before[kv.Key])) throw new InvalidOperationException("Level " + kv.Key + " changed since the shuffle was computed: preview again.");
            }
            foreach (var kv in r.After.OrderBy(k => k.Key))
            {
                var e = edits.For(kv.Key);
                e.Replace(kv.Value, null, "Scatter: apply (seed " + r.UsedSeed + ")");
                r.Applied[kv.Key] = (byte[])e.Data.Clone();
            }
        }

        /// <summary>Puts every level back as it was before Apply, byte for byte (one undoable step a level); a level edited since is left alone and returned.</summary>
        public static List<int> Undo(KeyShuffleResult r, LevelEdits edits)
        {
            var skipped = new List<int>();
            foreach (var kv in r.Applied.OrderBy(k => k.Key))
            {
                var e = edits.For(kv.Key);
                if (!e.Data.AsSpan().SequenceEqual(kv.Value)) { skipped.Add(kv.Key); continue; }
                e.Replace(r.Before[kv.Key], null, "Undo key item shuffle");
            }
            r.Applied.Clear();
            return skipped;
        }

        // ---------------------------------------------------------------- the spoiler log

        /// <summary>
        /// The name of the hand-made zone a square lies in (the painted map's), quoted; the
        /// nearest named zone within a square when the square itself is unassigned (a door
        /// tile, say); "" without names.
        /// </summary>
        public static string PlaceName(ZoneSet names, int level, int x, int y)
        {
            if (names == null || level < 1 || level > names.Levels) return "";
            int z = names.ZoneAt(level, x, y);
            for (int r = 1; z <= ZoneSet.Unassigned && r <= 2; r++)
                for (int dy = -r; dy <= r && z <= ZoneSet.Unassigned; dy++)
                    for (int dx = -r; dx <= r && z <= ZoneSet.Unassigned; dx++)
                        if (x + dx >= 0 && x + dx < 64 && y + dy >= 0 && y + dy < 64) z = names.ZoneAt(level, x + dx, y + dy);
            if (z <= ZoneSet.Unassigned) return "";
            var zone = names.GetZone(z);
            string n = zone != null && !string.IsNullOrWhiteSpace(zone.Name) ? zone.Name.Trim() : "zone " + z;
            return "\u201C" + n + "\u201D";
        }

        static string Named(ZoneSet names, ShuffleObject o)
        {
            string n = PlaceName(names, o.Level, o.X, o.Y);
            return n.Length == 0 ? o.Where : o.Where + " in " + n;
        }

        static void WriteLog(GameSession s, KeyShuffleResult r, TimelineResult t)
        {
            var b = r.Log;
            var opt = r.Options;
            b.AppendLine("KEY ITEM SHUFFLE (Ultima Underworld)");
            b.AppendLine();
            b.AppendLine(string.Format("Seed {0}{1}.", opt.Seed, r.Success ? (r.UsedSeed != opt.Seed ? ", used seed " + r.UsedSeed + " after " + (r.Attempts - 1) + " failed attempt" + (r.Attempts == 2 ? "" : "s") : "") : ": NO SHUFFLE FOUND in " + r.Attempts + " attempts"));
            b.AppendLine("Logic: " + opt.Logic + ".");
            b.AppendLine("Moving: " + string.Join(", ", Categories.Where(opt.Move.Contains).Select(CategoryName)) + (opt.Filler ? ", and everything else as filler" : "") + "; creature inventories " + (opt.CreatureInventories ? "in (creatures without a conversation)" : "out") + ".");
            b.AppendLine("Method: assumed fill on the time-aware solver; a fresh solve of the shuffled levels (with the return check) must reach the goal.");
            var names = opt.Names;
            if (opt.Zoning != null)
                b.AppendLine("Zones: " + (opt.ZoningFrom.Length > 0 ? opt.ZoningFrom : "the zoning given") + " (the Zones window's), laid on the levels' regions; \"zone N\" is its zone N.");
            else
                b.AppendLine("Zones: automatic, made from the movement options.");
            b.AppendLine(names != null ? "Places are named by " + (opt.NamesFrom.Length > 0 ? opt.NamesFrom : "the hand-made zones") + (opt.Zoning != null ? "." : "; \"zone N\" is the automatic zone.") : "Places are given by level and square" + (opt.Zoning != null ? "." : "; \"zone N\" is the automatic zone."));
            foreach (var f in r.Failures) b.AppendLine("  Failed: " + f);
            b.AppendLine();
            var moving = r.Moving.ToList();
            b.AppendLine(string.Format("{0} objects move ({1} key items placed by the fill, {2} others), over {0} places. By category: {3}.", moving.Count, moving.Count(o => o.Key), moving.Count(o => !o.Key),
                string.Join(", ", moving.GroupBy(o => o.Category).OrderBy(g => g.Key).Select(g => CategoryName(g.Key) + " " + g.Count() + " (" + g.Count(o => o.Key) + " key)"))));
            var stays = r.Objects.Where(o => o.Stays != null && o.Stays != "its category does not move").ToList();
            b.AppendLine("Stay where they are: " + (stays.Count == 0 ? "none" : string.Join("; ", stays.GroupBy(o => o.Stays).OrderByDescending(g => g.Count()).Select(g => g.Key + ": " + g.Count()))) + ".");
            foreach (var o in stays.Where(o => o.Category != "filler" && o.Gives.Any())) b.AppendLine("  " + o + " [" + o.Tokens + "]: " + o.Stays);
            b.AppendLine();
            b.AppendLine("Doors only picking opens (lock picking " + (opt.Logic.LockPicking ? "on: none counted" : "off") + "): " + (r.PickOnlyDoors.Count == 0 ? "none blocks the logic" : string.Join("; ", r.PickOnlyDoors)) + ".");
            b.AppendLine("Doors unlocked: " + (r.Unlocked.Count == 0 ? "none" : string.Join("; ", r.Unlocked)) + ".");
            if (!r.Success) return;
            b.AppendLine();
            b.AppendLine(string.Format("ROUTE: the shuffled game reaches the goal in {0} spheres (the original, with these options, in {1}); {2} zones reached; one-way traps {3}.", r.SpheresAfter, r.SpheresBefore,
                r.Check.ZoneSphere.Count(kv => kv.Key < 1000), r.Check.Traps.Count));
            var milestones = new[] { "talisman:sword", "talisman:shield", "talisman:ring", "talisman:taper", "talisman:cup", "talisman:wine", "talisman:book", "talisman:standard", "item:key_of_truth", "item:key_of_love", "item:key_of_courage", "item:key_of_infinity", "flag:garamon_buried", "flag:orb_destroyed", "item:rune_bag", "worn:lava", "spell:lava" };
            var got = milestones.Where(m => r.Check.TokenSphere.ContainsKey(m)).Select(m => m + " " + r.Check.TokenSphere[m]).ToList();
            if (got.Count > 0) b.AppendLine("  Milestones (token sphere): " + string.Join(", ", got) + ".");
            if (r.Check.ZoneSphere.ContainsKey(r.CheckBuild.GoalZone)) b.AppendLine("  Level 9 reached in sphere " + r.Check.ZoneSphere[r.CheckBuild.GoalZone] + ".");
            b.AppendLine();
            b.AppendLine("WHERE EACH KEY ITEM WENT (by the sphere at which the shuffled game reaches its new place)");
            foreach (var g in r.Moves.Where(m => m.ByFill).GroupBy(m => m.Sphere).OrderBy(g => g.Key))
            {
                b.AppendLine("  Sphere " + (g.Key < 0 ? "never" : g.Key.ToString()) + ":");
                foreach (var m in g.OrderBy(x => x.Item.Category).ThenBy(x => x.Item.Name))
                    b.AppendLine(string.Format("    {0} [{1}]: {2}, zone {3} (was {4}; the place held #{5} {6}, reached in sphere {7} of the original)", m.Item.Name, m.Item.Tokens, Named(names, m.Place), m.Place.Zone, Named(names, m.Item), m.Place.Index, m.Place.Name,
                        m.Place.Sphere < 0 ? "never" : m.Place.Sphere.ToString()));
            }
            var others = r.Moves.Where(m => !m.ByFill && m.Item != m.Place).ToList();
            if (others.Count > 0)
            {
                b.AppendLine();
                b.AppendLine("THE OTHER ITEMS (" + others.Count + " moved)");
                foreach (var m in others.OrderBy(x => x.Place.Level).ThenBy(x => x.Place.Index).Take(400))
                    b.AppendLine(string.Format("  {0} -> {1} (was {2})", m.Item.Name, Named(names, m.Place), Named(names, m.Item)));
                if (others.Count > 400) b.AppendLine("  …");
            }
        }
    }
}
