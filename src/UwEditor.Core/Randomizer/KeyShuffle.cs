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
        /// <summary>Tests only: the first attempts are counted as failed after their scatter, to check that the used seed gives the run again.</summary>
        internal int FailAttempts;
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
        /// <summary>Everything else the item swap moves (weapons, armour, food, lights …) takes part as filler, and its places too (the window now picks these through Extra).</summary>
        public bool Filler;
        /// <summary>Additional item kinds that take part as filler, with their places (ExtraItems: the picker). Containers among them move with what they hold, within their level.</summary>
        public HashSet<int> Extra = new HashSet<int>();
        /// <summary>At Apply: a torch of quality 63 and the map beside the start square (StartAids.PutKit).</summary>
        public bool StartKit;
        /// <summary>At Apply: every level's map explored (StartAids.ExploreMaps).</summary>
        public bool ExploreMaps;
        /// <summary>Places on the floor become random squares of their level that the logic reaches (Anywhere), instead of where items lay.</summary>
        public bool Anywhere = true;
        /// <summary>At Apply: the spoiler log in the map notes: a warning on page 9, the hints from page 10, a page saying the solutions come next, then the solutions (SpoilerNotes).</summary>
        public bool SpoilerNotes;

        public KeyShuffleOptions Clone()
        {
            var o = (KeyShuffleOptions)MemberwiseClone();
            o.Logic = Logic.Clone();
            o.Nav = Nav.Clone();
            o.Move = new HashSet<string>(Move);
            o.Extra = new HashSet<int>(Extra ?? new HashSet<int>());
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
        /// <summary>It lies on the floor (not in a container or a creature).</summary>
        public bool OnFloor;
        /// <summary>A container picked as an additional item: it moves with what it holds, to a place on its own level.</summary>
        public bool Container;
        /// <summary>For an object inside such a container: the container (level:index); it goes where the container goes.</summary>
        public string RidesIn;
        /// <summary>Its square before Place anywhere moved its place (-1: not moved).</summary>
        public int FromX = -1, FromY = -1;
        public bool Relocated { get { return FromX >= 0; } }
        /// <summary>Where the item was in the levels (before Place anywhere).</summary>
        public string WasWhere { get { return Relocated ? string.Format("L{0} ({1}, {2})", Level, FromX, FromY) : Where; } }
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
        /// <summary>The maps Apply explored (ExploreMaps), as they were before, for Undo.</summary>
        public readonly Dictionary<int, Tuple<byte[], List<UwEditor.Core.Editing.MapNote>>> MapsBefore = new Dictionary<int, Tuple<byte[], List<UwEditor.Core.Editing.MapNote>>>();
        /// <summary>Place anywhere: the places moved to random squares, and the levels that had no square for them.</summary>
        public int Relocated;
        public readonly List<string> AnywhereNotes = new List<string>();
        /// <summary>What Apply did besides moving the items (the torch and map, the maps explored).</summary>
        public readonly List<string> AppliedAids = new List<string>();
        public bool IsApplied { get { return Applied.Count > 0; } }

        /// <summary>The categories whose items are always worth a hint: the talismans and the Key of Infinity's parts.</summary>
        public static readonly HashSet<string> Artifacts = new HashSet<string> { "talismans", "infinity" };

        /// <summary>An artifact: a talisman, a part of the Key of Infinity, or a unique item with a proper name (Caliburn's blade and hilt: NamedItems). Other plot items (six orb rocks, the flutes) are hinted only when crucial.</summary>
        public static bool IsArtifact(ShuffleObject o) { return Artifacts.Contains(o.Category) || NamedItems.Of(UltimateEditor.Games.GameKind.UW1, o.Item) != null; }
        internal Func<ShuffleObject, bool> crucialTest;
        readonly Dictionary<ShuffleObject, bool> crucial = new Dictionary<ShuffleObject, bool>();

        /// <summary>
        /// Whether the shuffled game cannot be finished without this key item: a solve of the
        /// shuffled placements without it (the same logic options, so a door the options let the
        /// player bash, pick or go round needs no key) misses the goal. An item with a twin (two
        /// keys to one lock) is not crucial. Computed once an item, when asked (the spoiler notes).
        /// </summary>
        public bool IsCrucial(ShuffleObject item)
        {
            bool c;
            if (crucial.TryGetValue(item, out c)) return c;
            c = crucialTest != null && crucialTest(item);
            crucial[item] = c;
            return c;
        }

        /// <summary>An item the spoiler notes give: an artifact, whatever placed it (Caliburn's blade is no key the logic asks for, yet worth a hint), or a key item the game cannot be finished without.</summary>
        public bool WorthAHint(KeyShuffleMove m) { return IsArtifact(m.Item) || m.ByFill && IsCrucial(m.Item); }
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
            var extra = opt.Extra ?? new HashSet<int>();
            foreach (int level in w.Levels)
            {
                var m = w.Model(level);
                var named = Named(m);
                HashSet<int> abil;
                if (!abilityItems.TryGetValue(level, out abil)) abil = new HashSet<int>();
                var boxes = MovingContainers(s, m, extra, abil, byObject, needed, t, level);
                foreach (var o in m.Objects)
                {
                    if (o.Free || o.TileX < 0 || o.IsCritter || o.Item >= 0x140 || o.Index < 2) continue;
                    string key = level + ":" + o.Index;
                    List<Placement> gives;
                    if (!byObject.TryGetValue(key, out gives)) gives = new List<Placement>();
                    // inside a container that moves: it goes along, and its placements go with the container
                    int box = -1;
                    for (int up = o.Parent, g = 0; up >= 0 && g < 64; up = m.Objects[up].Parent, g++) if (boxes.ContainsKey(up)) box = up;
                    if (box >= 0)
                    {
                        var ride = new ShuffleObject { Level = level, Index = o.Index, Item = o.Item, Name = s.Names.Get(o.Item), X = o.TileX, Y = o.TileY, Category = "filler", Record = RecordOf(o), RidesIn = level + ":" + box,
                            Stays = "inside a container that moves (#" + box + " " + s.Names.Get(m.Objects[box].Item) + "): it goes along" };
                        ride.Gives.AddRange(gives);
                        list.Add(ride);
                        continue;
                    }
                    string cat = CategoryOf(s, o, abil, gives);
                    bool fill = filler.Contains(key) || (extra.Contains(o.Item) && ExtraItems.ClassOf(s, o.Item) != null);
                    if (cat == null && !fill) continue;
                    var so = new ShuffleObject
                    {
                        Level = level, Index = o.Index, Item = o.Item, Name = s.Names.Get(o.Item), X = o.TileX, Y = o.TileY,
                        Category = cat ?? "filler", Record = RecordOf(o), Zone = t.Set.NearestZone(level, o.TileX, o.TileY),
                    };
                    so.Gives.AddRange(gives);
                    List<Placement> inside;
                    if (cat == null && boxes.TryGetValue(o.Index, out inside)) { so.Container = true; so.Gives.AddRange(inside); }
                    so.OnFloor = o.OnTile;
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
                    else if (!o.IsQuantity && o.Link != 0 && !so.Container) so.Stays = extra.Contains(o.Item) && ExtraItems.IsContainer(s, o.Item)
                        ? "a container holding something the logic needs or a key item (its contents take part instead)" : "it holds other objects (its list stays on its level)";
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

        /// <summary>
        /// The containers on a level that move with what they hold: picked as additional items,
        /// no category of their own (the rune bag, a bowl keep theirs), nothing inside that the
        /// logic needs, that has a category of its own or that is a trap or trigger; only the
        /// outermost of nested ones (the inner ride along). With the placements of everything
        /// inside, which go where the container goes.
        /// </summary>
        static Dictionary<int, List<Placement>> MovingContainers(GameSession s, LevelModel m, HashSet<int> extra, HashSet<int> abil, Dictionary<string, List<Placement>> byObject, HashSet<string> needed, TimelineResult t, int level)
        {
            var boxes = new Dictionary<int, List<Placement>>();
            if (extra == null || extra.Count == 0) return boxes;
            foreach (var o in m.Objects)
            {
                if (o.Free || o.TileX < 0 || o.IsMobile || o.IsQuantity || o.Link == 0 || o.Index < 2) continue;
                if (!extra.Contains(o.Item) || !ExtraItems.IsContainer(s, o.Item)) continue;
                List<Placement> own;
                if (!byObject.TryGetValue(level + ":" + o.Index, out own)) own = new List<Placement>();
                if (CategoryOf(s, o, abil, own) != null) continue;
                // not inside a trap, nor carried by a creature with a conversation
                bool bad = false;
                for (int up = o.Parent, g = 0; up >= 0 && g < 64; up = m.Objects[up].Parent, g++)
                {
                    var u = m.Objects[up];
                    if (u.IsTrapOrTrigger || (u.IsCritter && u.Record.Length > 0x1A && u.Record[0x1A] != 0)) bad = true;
                }
                var gives = new List<Placement>();
                var stack = new Stack<int>(o.Children);
                int guard = 0;
                while (stack.Count > 0 && !bad && guard++ < 1100)
                {
                    var c = m.Objects[stack.Pop()];
                    if (c.Free) continue;
                    List<Placement> cg;
                    if (!byObject.TryGetValue(level + ":" + c.Index, out cg)) cg = new List<Placement>();
                    if (c.IsTrapOrTrigger || CategoryOf(s, c, abil, cg) != null || cg.Any(p => IsNeeded(needed, p, t))) { bad = true; break; }
                    gives.AddRange(cg);
                    foreach (int k in c.Children) stack.Push(k);
                }
                if (!bad) boxes[o.Index] = gives;
            }
            // nested: only the outermost moves
            foreach (int b in boxes.Keys.ToList())
                for (int up = m.Objects[b].Parent, g = 0; up >= 0 && g < 64; up = m.Objects[up].Parent, g++)
                    if (boxes.ContainsKey(up)) { boxes.Remove(b); break; }
            return boxes;
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
            // what moves, and what rides along inside a container that moves: their placements are the movers' now
            var moving = new HashSet<string>(objs.Where(o => o.Stays == null || o.RidesIn != null).Select(o => o.Key2));
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
            if (s.IsUw2) { r.Refused = "The key item shuffle works on Ultima Underworld 1 only: its facts and solver cover no other game."; r.Log.AppendLine(r.Refused); return r; }
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
            // Place anywhere: the squares are worked out once; each attempt scatters the places with its own seed
            var anywhere = opt.Anywhere ? PrepareAnywhere(s, w, t, opt, objs) : null;
            r.Objects.AddRange(objs);
            var solver = MakeSolver(t, opt, objs);
            var places = solver.Places;
            if (places.Count == 0) { r.Refused = "Nothing moves with these categories."; r.Log.AppendLine(r.Refused); return r; }

            var keys = places.Where(o => o.Key).OrderBy(o => o.Level).ThenBy(o => o.Index).ToList();
            var rest = places.Where(o => !o.Key).OrderBy(o => o.Level).ThenBy(o => o.Index).ToList();
            var assumedRest = GivesTokens(t, rest).ToList();
            int tries = Math.Max(1, opt.Retries + 1);
            for (int attempt = 0; attempt < tries; attempt++)
            {
                int seed = unchecked(opt.Seed + attempt);
                r.Attempts = attempt + 1;
                // the places on their squares for this seed, so the seed the log names gives this run again
                var wa = w;
                if (anywhere != null) { wa = w.Copy(); anywhere.Scatter(s, wa, t, seed, r); }
                if (attempt < opt.FailAttempts) { r.Failures.Add("seed " + seed + ": counted as failed (test)"); continue; }

                // ---- the spheres of the game before the fill: every item where it is, every place probed
                var orig = places.ToDictionary(o => o, o => o);
                var sv0 = solver.Run(orig, Enumerable.Range(0, places.Count), null, "original");
                r.SpheresBefore = sv0.Spheres.Count;
                for (int i = 0; i < places.Count; i++) { int sp; places[i].Sphere = sv0.TokenSphere.TryGetValue("loc:" + i, out sp) ? sp : -1; }
                foreach (var o in objs.Where(o => o.Stays != null)) o.Sphere = -1;

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
                // the rest at random: containers first, each to a place on its own level (their contents list is the level's)
                var others = rest.ToList();
                Shuffle(rng, others);
                foreach (var box in others.Where(o => o.Container))
                {
                    var same = empty.Where(i => places[i].Level == box.Level).ToList();
                    if (same.Count == 0) { fail = "no place left on level " + box.Level + " for the container " + box; break; }
                    int at = same[rng.Next(same.Count)];
                    placed[box] = places[at];
                    empty.Remove(at);
                }
                if (fail != null) { r.Failures.Add("seed " + seed + ": " + fail); continue; }
                var loose = others.Where(o => !o.Container).ToList();
                for (int k = 0; k < loose.Count; k++) placed[loose[k]] = places[empty[k]];
                // write it on a copy and solve that afresh
                var wv = wa.Copy();
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
                var placedFinal = new Dictionary<ShuffleObject, ShuffleObject>(placed);
                r.crucialTest = item =>
                {
                    var without = placedFinal.Where(kv => kv.Key != item).ToDictionary(kv => kv.Key, kv => kv.Value);
                    return !solver.Run(without, Enumerable.Empty<int>(), null, "without " + item.Name).GoalReached;
                };
                foreach (var kv in placed)
                {
                    int i = places.IndexOf(kv.Value), sp;
                    r.Moves.Add(new KeyShuffleMove { Item = kv.Key, Place = kv.Value, ByFill = kv.Key.Key, Sphere = final.TokenSphere.TryGetValue("loc:" + i, out sp) ? sp : -1 });
                }
                foreach (int l in wv.Levels)
                    if (!wv.Data[l].AsSpan().SequenceEqual(r.Before[l])) r.After[l] = wv.Data[l];
                break;
            }
            // no shuffle found: the places are where the items lie, as nothing is written
            if (!r.Success && anywhere != null) anywhere.Restore(r);
            WriteLog(s, r, t);
            return r;
        }

        /// <summary>
        /// Place anywhere: every place that moves and lies on the floor goes to a random square of
        /// its level (Anywhere.Qualifies: open, not water or lava, no door, trap or trigger; slopes
        /// and the open half of a diagonal allowed) whose zone the logic reaches with every moving
        /// item held, one place a square while there are squares. The squares are found once
        /// (PrepareAnywhere); each attempt of the fill scatters the places with its own seed
        /// (Scatter), so the seed the log names gives the same squares again. The places' records
        /// move in the attempt's copy of the world's bytes before the fill, so the fill, the check
        /// and Apply all see them there; the zone graph does not change, since loose items do not
        /// shape it.
        /// </summary>
        static AnywherePlan PrepareAnywhere(GameSession s, World w, TimelineResult t, KeyShuffleOptions opt, List<ShuffleObject> objs)
        {
            var floor = objs.Where(o => o.Stays == null && o.OnFloor).ToList();
            if (floor.Count == 0) return null;
            var sv = Timeline.Solve(t, opt.Logic, "every moving item held", GivesTokens(t, objs.Where(o => o.Stays == null)).ToList());
            var zones = new HashSet<int>(sv.ZoneSphere.Keys);
            var terrain = CreatureTerrain.TryLoad(s);
            var plan = new AnywherePlan();
            foreach (var g in floor.GroupBy(o => o.Level).OrderBy(g => g.Key))
            {
                int level = g.Key;
                var m = w.Model(level);
                var squares = Anywhere.Squares(s, m, terrain, (x, y) => { int z = t.Set.ZoneAt(level, x, y); return z > ZoneSet.Unassigned && zones.Contains(z); });
                plan.Levels.Add(new AnywhereLevel { Level = level, Model = m, Squares = squares, Places = g.OrderBy(o => o.Index).ToList() });
            }
            foreach (var o in floor) plan.Home[o] = (o.X, o.Y, o.Zone);
            return plan;
        }

        sealed class AnywhereLevel
        {
            public int Level;
            public LevelModel Model;
            public List<(int X, int Y)> Squares;
            public List<ShuffleObject> Places;
        }

        sealed class AnywherePlan
        {
            public readonly List<AnywhereLevel> Levels = new List<AnywhereLevel>();
            public readonly Dictionary<ShuffleObject, (int X, int Y, int Zone)> Home = new Dictionary<ShuffleObject, (int X, int Y, int Zone)>();

            /// <summary>Puts every place back on the square its item lies on.</summary>
            public void Restore(KeyShuffleResult r)
            {
                foreach (var kv in Home) { kv.Key.X = kv.Value.X; kv.Key.Y = kv.Value.Y; kv.Key.Zone = kv.Value.Zone; kv.Key.FromX = kv.Key.FromY = -1; }
                r.Relocated = 0;
                r.AnywhereNotes.Clear();
            }

            /// <summary>Moves the places to random squares in w (a copy of the world for one attempt), drawn from the attempt's seed.</summary>
            public void Scatter(GameSession s, World w, TimelineResult t, int seed, KeyShuffleResult r)
            {
                Restore(r);
                var rng = new Random(unchecked(seed * 31 + 0x5CA7));
                foreach (var lv in Levels)
                {
                    var spots = Anywhere.Spots(rng, lv.Model, lv.Squares, lv.Places.Count);
                    if (spots.Count == 0) { r.AnywhereNotes.Add("level " + lv.Level + ": no square the logic reaches qualifies; its " + lv.Places.Count + " places stay where items lay"); continue; }
                    for (int k = 0; k < lv.Places.Count; k++)
                    {
                        var o = lv.Places[k];
                        Anywhere.Move(s, lv.Level, w.Data[lv.Level], w.Tex[lv.Level], o.Index, spots[k]);
                        o.FromX = o.X; o.FromY = o.Y;
                        o.X = spots[k].X; o.Y = spots[k].Y;
                        o.Zone = t.Set.NearestZone(lv.Level, o.X, o.Y);
                        r.Relocated++;
                    }
                    w.Changed(lv.Level);
                }
            }
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
                if (!e.Data.AsSpan().SequenceEqual(r.Before[kv.Key])) throw new InvalidOperationException("Level " + kv.Key + " has changed since the shuffle was computed. Preview it again.");
            }
            r.AppliedAids.Clear();
            r.MapsBefore.Clear();
            Dictionary<int, byte[]> appliedThen = null;
            edits.Together(() =>
            {
                foreach (var kv in r.After.OrderBy(k => k.Key))
                {
                    var e = edits.For(kv.Key);
                    e.Replace(kv.Value, null, "Key item scatter: apply (seed " + r.UsedSeed + ")");
                    r.Applied[kv.Key] = (byte[])e.Data.Clone();
                }
                // the helps chosen: part of the same step, so Undo takes them back with the items
                if (r.Options.StartKit)
                {
                    var st = r.Options.Nav.StartOf(edits.Session);
                    if (st != null)
                    {
                        var e = edits.For(st.Level);
                        // a start level the scatter left alone: Undo puts back the level as it is now (Before holds it as computed, which may be older)
                        if (!r.After.ContainsKey(st.Level)) r.Before[st.Level] = (byte[])e.Data.Clone();
                        r.AppliedAids.AddRange(StartAids.PutKit(edits.Session, edits, st));
                        r.Applied[st.Level] = (byte[])e.Data.Clone();
                    }
                }
                if (r.Options.ExploreMaps)
                {
                    foreach (var kv in StartAids.ExploreMaps(edits.Session, edits, "Key item scatter: maps explored")) r.MapsBefore[kv.Key] = kv.Value;
                    r.AppliedAids.Add(r.MapsBefore.Count == 0 ? "Every map was already explored." : "The maps of levels " + string.Join(", ", r.MapsBefore.Keys.OrderBy(k => k)) + " were explored.");
                }
                if (r.Options.SpoilerNotes)
                {
                    var pages = PutSpoilerNotes(r, edits);
                    r.AppliedAids.Add(pages.Count == 0 ? "The spoiler log was already in the map notes." : "The spoiler log was written in the map notes: a warning on page " + SpoilerNotes.WarningPage + ", hints from page " + SpoilerNotes.HintsPage + ", then a page saying the solutions come next, then the solutions (pages " + string.Join(", ", pages.OrderBy(k => k)) + " changed).");
                }
            },
            // Edit ▸ Undo took the whole apply back: the maps too, and the result is no longer applied; Redo brings both again
            undone: () =>
            {
                RestoreMaps(r, edits, null);
                appliedThen = new Dictionary<int, byte[]>(r.Applied);
                r.Applied.Clear();
            },
            redone: () =>
            {
                if (appliedThen != null) foreach (var kv in appliedThen) r.Applied[kv.Key] = kv.Value;
                if (r.Options.ExploreMaps)
                    foreach (var kv in StartAids.ExploreMaps(edits.Session, edits, "Key item scatter: maps explored")) r.MapsBefore[kv.Key] = kv.Value;
                if (r.Options.SpoilerNotes) PutSpoilerNotes(r, edits);
            });
        }

        /// <summary>The spoiler log written in the map notes; each page's notes as they were go with the maps for Undo (a page the maps already hold keeps its older state). Returns the pages changed.</summary>
        static List<int> PutSpoilerNotes(KeyShuffleResult r, LevelEdits edits)
        {
            var before = SpoilerNotes.Write(edits.Session, edits, r, "Key item scatter: spoiler log in the map notes");
            foreach (var kv in before) if (!r.MapsBefore.ContainsKey(kv.Key)) r.MapsBefore[kv.Key] = kv.Value;
            return before.Keys.ToList();
        }

        /// <summary>The maps explored at Apply put back as they were (a map is cosmetic: even if marked since), but not on the levels in skip.</summary>
        static void RestoreMaps(KeyShuffleResult r, LevelEdits edits, ICollection<int> skip)
        {
            foreach (var kv in r.MapsBefore)
            {
                if (skip != null && skip.Contains(kv.Key)) continue;
                var a = edits.AutomapFor(kv.Key);
                a.Replace(kv.Value.Item1, kv.Value.Item2, "Undo key item scatter: maps");
                edits.AutomapEdited(a);
            }
            r.MapsBefore.Clear();
        }

        /// <summary>Puts every level back as it was before Apply, byte for byte (one undoable step a level); a level edited since is left alone and returned.</summary>
        public static List<int> Undo(KeyShuffleResult r, LevelEdits edits)
        {
            var skipped = new List<int>();
            edits.Together(() =>
            {
                foreach (var kv in r.Applied.OrderBy(k => k.Key))
                {
                    var e = edits.For(kv.Key);
                    if (!e.Data.AsSpan().SequenceEqual(kv.Value)) { skipped.Add(kv.Key); continue; }
                    e.Replace(r.Before[kv.Key], null, "Undo key item shuffle");
                }
            });
            // the maps explored at Apply, as they were, but not on the levels left alone (edited since)
            RestoreMaps(r, edits, skipped);
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

        /// <summary>Where an item was before the shuffle (before Place anywhere moved its record), named.</summary>
        static string NamedWas(ZoneSet names, ShuffleObject o)
        {
            if (!o.Relocated) return Named(names, o);
            string n = PlaceName(names, o.Level, o.FromX, o.FromY);
            return n.Length == 0 ? o.WasWhere : o.WasWhere + " in " + n;
        }

        static void WriteLog(GameSession s, KeyShuffleResult r, TimelineResult t)
        {
            var b = r.Log;
            var opt = r.Options;
            b.AppendLine("KEY ITEM SHUFFLE (Ultima Underworld)");
            b.AppendLine();
            b.AppendLine(string.Format("Seed {0}{1}.", opt.Seed, r.Success ? (r.UsedSeed != opt.Seed ? ", used seed " + r.UsedSeed + " after " + (r.Attempts - 1) + " failed attempt" + (r.Attempts == 2 ? "" : "s") : "") : ": NO SHUFFLE FOUND in " + r.Attempts + " attempts"));
            b.AppendLine("Logic: " + opt.Logic + ".");
            b.AppendLine("Moving: " + string.Join(", ", Categories.Where(opt.Move.Contains).Select(CategoryName)) + (opt.Filler ? ", and everything else as filler" : "")
                + (opt.Extra != null && opt.Extra.Count > 0 ? ", and " + opt.Extra.Count + " additional item kind" + (opt.Extra.Count == 1 ? "" : "s") : "")
                + "; creature inventories " + (opt.CreatureInventories ? "in (creatures without a conversation)" : "out") + ".");
            b.AppendLine("Method: assumed fill on the time-aware solver; a fresh solve of the shuffled levels (with the return check) must reach the goal.");
            b.AppendLine(opt.Anywhere
                ? "Place anywhere: on; " + r.Relocated + " places on the floor moved to random squares of their level that the logic reaches (not water or lava, no door, trap or trigger)" + (r.AnywhereNotes.Count > 0 ? "; " + string.Join("; ", r.AnywhereNotes) : "") + "."
                : "Place anywhere: off; items trade the places where items lay.");
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
                    b.AppendLine(string.Format("    {0} [{1}]: {2}, zone {3} (was {4}; {5}, reached in sphere {6} of the original)", m.Item.Name, m.Item.Tokens, Named(names, m.Place), m.Place.Zone, NamedWas(names, m.Item),
                        m.Place.Relocated ? "a random square" : "the place held #" + m.Place.Index + " " + m.Place.Name, m.Place.Sphere < 0 ? "never" : m.Place.Sphere.ToString()));
            }
            var others = r.Moves.Where(m => !m.ByFill && (m.Item != m.Place || m.Place.Relocated)).ToList();
            if (others.Count > 0)
            {
                b.AppendLine();
                b.AppendLine("THE OTHER ITEMS (" + others.Count + " moved)");
                foreach (var m in others.OrderBy(x => x.Place.Level).ThenBy(x => x.Place.Index).Take(400))
                    b.AppendLine(string.Format("  {0} -> {1} (was {2})", m.Item.Name, Named(names, m.Place), NamedWas(names, m.Item)));
                if (others.Count > 400) b.AppendLine("  …");
            }
        }
    }
}
