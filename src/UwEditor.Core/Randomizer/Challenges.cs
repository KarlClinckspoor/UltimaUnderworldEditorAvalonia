using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.Json;
using UltimateEditor.Games;
using UwEditor.Core.Conversations;
using UwEditor.Core.Editing;
using UwEditor.Core.Player;
using MapNote = UwEditor.Core.Editing.MapNote;

namespace UwEditor.Core.Randomizer
{
    /// <summary>The named challenges, in alphabetical order of their names.</summary>
    public enum Challenge
    {
        AllergicToMagic, BonesRise, DmdMode, Dystopia, EternalDarkness, Fasting, GhostTown, Hoarding, Illiterate, Lockdown, Merchant, Monk,
        Naked, OldSchool, Peaceful, Precognition, Ranger, Starvation, Utopia, Vegetarian, VowOfPoverty,
    }

    /// <summary>A challenge's name and its one-line description, for the window.</summary>
    public sealed class ChallengeInfo
    {
        public Challenge Id { get; private set; }
        public string Name { get; private set; }
        public string Summary { get; private set; }
        public ChallengeInfo(Challenge id, string name, string summary) { Id = id; Name = name; Summary = summary; }
        public override string ToString() { return Name; }
    }

    /// <summary>The named challenges' settings, saved per game.</summary>
    public sealed class ChallengeOptions
    {
        public int Seed { get; set; } = 1;
        /// <summary>The ticked challenges, by their <see cref="Challenge"/> names.</summary>
        public List<string> Selected { get; set; } = new List<string>();
        /// <summary>Fasting: the share of the food that stays, 0-100 %.</summary>
        public double FastingKeep { get; set; } = 50;
        /// <summary>Ranger: the weights of the launchers from the least to the most damaging (OBJECTS.DAT).</summary>
        public double[] RangerWeights { get; set; } = { 10, 3, 2, 1 };
        /// <summary>Creatures of special note: DMD mode leaves them as they are and Hoarding gives them nothing. Null until filled with the defaults.</summary>
        public List<IgnoredObject> Special { get; set; }

        public bool IsOn(Challenge c) { return Selected != null && Selected.Contains(c.ToString()); }

        public void Set(Challenge c, bool on)
        {
            if (Selected == null) Selected = new List<string>();
            Selected.RemoveAll(x => x == c.ToString());
            if (on) Selected.Add(c.ToString());
        }

        /// <summary>The ticked challenges.</summary>
        public List<Challenge> On() { return Challenges.All.Select(i => i.Id).Where(IsOn).ToList(); }

        static readonly JsonSerializerOptions Json = new JsonSerializerOptions { WriteIndented = true };
        public string ToJson() { return JsonSerializer.Serialize(this, Json); }
        public static ChallengeOptions FromJson(string s) { return JsonSerializer.Deserialize<ChallengeOptions>(s, Json); }
    }

    /// <summary>How many changes a challenge made on one level, with the first few as examples.</summary>
    public sealed class ChallengeTally
    {
        public int Count;
        public readonly List<string> Examples = new List<string>();
    }

    /// <summary>What one challenge did: per level (0 for OBJECTS.DAT, -1 for the player's inventory) and in notes.</summary>
    public sealed class ChallengeSection
    {
        public Challenge Id;
        public readonly SortedDictionary<int, ChallengeTally> ByLevel = new SortedDictionary<int, ChallengeTally>();
        public readonly List<string> Notes = new List<string>();
        public readonly SortedDictionary<string, int> Kept = new SortedDictionary<string, int>();
        public int Total { get { return ByLevel.Values.Sum(t => t.Count); } }
        public const int MaxExamples = 3;

        public void Add(int level, string example, int count = 1)
        {
            ChallengeTally t;
            if (!ByLevel.TryGetValue(level, out t)) ByLevel[level] = t = new ChallengeTally();
            t.Count += count;
            if (t.Examples.Count < MaxExamples && example != null) t.Examples.Add(example);
        }

        public void Keep(string why) { int n; Kept.TryGetValue(why, out n); Kept[why] = n + 1; }
    }

    /// <summary>One field of OBJECTS.DAT's creature table that a run changed.</summary>
    public sealed class TableChange
    {
        public int Row;
        public string Field;
        public int Old, New;
        public override string ToString() { return string.Format("creature 0x{0:X2} {1}: {2} → {3}", 0x40 + Row, Field, Old, New); }
    }

    /// <summary>
    /// What a run works on: copies of every level (LevelEditors of their own, started from the
    /// levels as edited), of the automaps, of OBJECTS.DAT's bytes and of the player sheet. A
    /// preview throws them away; Apply puts the result into the real ones.
    /// </summary>
    public sealed class ChallengeWorld
    {
        public GameSession Session { get; private set; }
        readonly Dictionary<int, LevelEditor> editors = new Dictionary<int, LevelEditor>();
        readonly Dictionary<int, Automap> maps = new Dictionary<int, Automap>();
        readonly Dictionary<int, byte[]> startData = new Dictionary<int, byte[]>(), startTex = new Dictionary<int, byte[]>(), startMap = new Dictionary<int, byte[]>();
        /// <summary>OBJECTS.DAT as the run leaves it (a copy).</summary>
        public byte[] Objects { get; private set; }
        /// <summary>OBJECTS.DAT as it was when the run started.</summary>
        public byte[] ObjectsBefore { get; private set; }
        /// <summary>A copy of the open save's player sheet, or null (DATA, or no PLAYER.DAT).</summary>
        public PlayerSheet Player { get; private set; }
        public bool PlayerChanged { get; internal set; }
        /// <summary>Conversation slots that call setup_to_barter.</summary>
        public HashSet<int> TraderSlots { get; private set; }
        /// <summary>The levels there are (present slots, as edited).</summary>
        public List<int> Levels { get; private set; }

        /// <summary>Copies of what the run touches. objects: OBJECTS.DAT's bytes as edited (null: DATA's file); player: the open save's sheet (null: none); traders: the conversation slots that barter (null: read from CNV.ARK).</summary>
        public static ChallengeWorld Copy(GameSession s, LevelEdits edits, byte[] objects, PlayerSheet player, HashSet<int> traders = null)
        {
            var w = new ChallengeWorld { Session = s };
            w.edits_ = edits;
            if (objects == null)
            {
                string p = GameProfile.Find(s.DataFolder, "OBJECTS.DAT");
                objects = p == null ? new byte[0] : File.ReadAllBytes(p);
            }
            w.Objects = (byte[])objects.Clone();
            w.ObjectsBefore = (byte[])objects.Clone();
            if (player != null) w.Player = PlayerSheet.FromBytes(player.ToBytes(), s.Kind, s.DataFolder);
            w.TraderSlots = traders ?? Challenges.TraderSlots(s);
            w.Levels = new List<int>();
            foreach (var slot in s.LevelSlots())
            {
                LevelModel m;
                try { m = edits != null ? edits.Model(slot.Level) : s.LoadLevel(slot.Level); } catch (Exception) { continue; }
                if (m != null && m.Present) w.Levels.Add(slot.Level);
            }
            return w;
        }

        ChallengeWorld() { }
        LevelEdits edits_;

        /// <summary>The run's own editor of a level, started from the level as edited.</summary>
        public LevelEditor Editor(int level)
        {
            LevelEditor e;
            if (editors.TryGetValue(level, out e)) return e;
            e = new LevelEditor(Session, level);
            var real = edits_ == null ? null : edits_.Existing(level);
            if (real != null && (!real.Data.AsSpan().SequenceEqual(e.Data) || !SameTex(real.TextureBytes, e.TextureBytes)))
                e.Replace(real.Data, real.TextureBytes, "as edited");
            editors[level] = e;
            startData[level] = (byte[])e.Data.Clone();
            startTex[level] = e.TextureBytes == null ? null : (byte[])e.TextureBytes.Clone();
            return e;
        }

        static bool SameTex(byte[] a, byte[] b) { return a == null ? b == null : b != null && a.AsSpan().SequenceEqual(b); }

        /// <summary>The run's own copy of a level's automap, started from the map as edited.</summary>
        public Automap Map(int level)
        {
            Automap a;
            if (maps.TryGetValue(level, out a)) return a;
            a = Automap.Load(Session, level);
            var real = edits_ == null ? null : edits_.Automaps.FirstOrDefault(x => x.Level == level);
            if (real != null) a.Replace(real.Map, real.Notes, "as edited");
            maps[level] = a;
            startMap[level] = (byte[])a.Map.Clone();
            return a;
        }

        /// <summary>The levels whose copy the run opened.</summary>
        public IEnumerable<int> OpenedLevels { get { return editors.Keys.OrderBy(l => l); } }
        public IEnumerable<int> OpenedMaps { get { return maps.Keys.OrderBy(l => l); } }
        internal LevelEditor Opened(int level) { LevelEditor e; return editors.TryGetValue(level, out e) ? e : null; }
        /// <summary>Whether the run changed a level's block (or texture map).</summary>
        public bool LevelChanged(int level)
        {
            LevelEditor e;
            if (!editors.TryGetValue(level, out e)) return false;
            return !e.Data.AsSpan().SequenceEqual(startData[level]) || !SameTex(e.TextureBytes, startTex[level]);
        }
        /// <summary>Whether the run changed a level's automap.</summary>
        public bool MapChanged(int level)
        {
            Automap a;
            return maps.TryGetValue(level, out a) && !a.Map.AsSpan().SequenceEqual(startMap[level]);
        }
        internal Automap OpenedMap(int level) { Automap a; return maps.TryGetValue(level, out a) ? a : null; }

        /// <summary>A level's model as the run has it now.</summary>
        public LevelModel Model(int level) { return Editor(level).Model; }
    }

    /// <summary>What a run did, or would do.</summary>
    public sealed class ChallengeResult
    {
        /// <summary>Why the run was refused (a conflicting pair, nothing ticked), or null.</summary>
        public string Refused;
        public ChallengeWorld World;
        public readonly List<ChallengeSection> Sections = new List<ChallengeSection>();
        public readonly List<TableChange> TableChanges = new List<TableChange>();
        public readonly StringBuilder Report = new StringBuilder();
        public int Seed;

        /// <summary>The levels whose block the run changed.</summary>
        public List<int> ChangedLevels()
        {
            var l = new List<int>();
            if (World == null) return l;
            foreach (int level in World.OpenedLevels)
                if (World.LevelChanged(level)) l.Add(level);
            return l;
        }

        public int Total { get { return Sections.Sum(s => s.Total); } }
    }

    /// <summary>What Apply changed, kept so Undo can put it back as it was.</summary>
    public sealed class ChallengeApplied
    {
        public readonly Dictionary<int, Tuple<byte[], byte[]>> Before = new Dictionary<int, Tuple<byte[], byte[]>>();
        public readonly Dictionary<int, byte[]> After = new Dictionary<int, byte[]>();
        public readonly Dictionary<int, Tuple<byte[], List<MapNote>>> MapBefore = new Dictionary<int, Tuple<byte[], List<MapNote>>>();
        public readonly Dictionary<int, byte[]> MapAfter = new Dictionary<int, byte[]>();
        public readonly List<TableChange> Table = new List<TableChange>();
        internal PlayerSnapshot Player;
        public string Label;
        /// <summary>The player sheet was changed (Undo puts it back).</summary>
        public bool ChangedPlayer { get { return Player != null; } }
        public bool Empty { get { return Before.Count == 0 && MapBefore.Count == 0 && Table.Count == 0 && Player == null; } }
    }

    /// <summary>A player sheet as it was: the record, every inventory object with its record, list and slot.</summary>
    internal sealed class PlayerSnapshot
    {
        byte[] record;
        List<InventoryItem> top;
        List<Tuple<InventoryItem, byte[], List<InventoryItem>, InventoryItem, int>> items;
        bool changed, dirty;

        public static PlayerSnapshot Take(PlayerSheet p)
        {
            var s = new PlayerSnapshot { record = (byte[])p.Dat.Record.Clone(), top = p.Inventory.Top.ToList(), changed = p.Inventory.Changed, dirty = p.Dirty };
            s.items = p.Inventory.All().Select(i => Tuple.Create(i, (byte[])i.Record.Clone(), i.Contents.ToList(), i.Parent, i.Slot)).ToList();
            return s;
        }

        public void Restore(PlayerSheet p)
        {
            Array.Copy(record, p.Dat.Record, Math.Min(record.Length, p.Dat.Record.Length));
            p.Inventory.Top.Clear();
            p.Inventory.Top.AddRange(top);
            foreach (var t in items)
            {
                Array.Copy(t.Item2, t.Item1.Record, 8);
                t.Item1.Contents.Clear();
                t.Item1.Contents.AddRange(t.Item3);
                t.Item1.Parent = t.Item4;
                t.Item1.Slot = t.Item5;
            }
            p.Inventory.Changed = changed;
            p.Dirty = dirty;
        }
    }

    /// <summary>
    /// Randomizer ▸ Named challenges: whole-game rule changes (no food, no weapons, every door
    /// locked, …) applied to every level, to OBJECTS.DAT's creature table (the loot a creature
    /// gets when it dies or first trades, obj/TREASURE.C generate_inventory) and, in a save, to
    /// the player's inventory. Quest items are never touched, nor conversations. The challenges
    /// are listed alphabetically (<see cref="All"/>) and applied in a fixed order
    /// (<see cref="Order"/>). A run works on copies (<see cref="ChallengeWorld"/>); Apply puts
    /// the copies' bytes into the real levels, one undoable step each, and Undo puts back what
    /// was there, byte for byte.
    /// </summary>
    public static partial class Challenges
    {
        /// <summary>Every challenge, alphabetically.</summary>
        public static readonly ChallengeInfo[] All =
        {
            new ChallengeInfo(Challenge.AllergicToMagic, "Allergic to magic", "Weapons, armour, rings and scrolls lose their enchantments, wands their spells (but the plot's), and the runestones and the rune bag go."),
            new ChallengeInfo(Challenge.BonesRise, "Bones rise", "A skull and two bones lying within a 2 × 2 square of tiles rise as a skeleton."),
            new ChallengeInfo(Challenge.DmdMode, "DMD mode", "Creatures without a conversation turn hostile, and every creature becomes powerful (the special ones stay as they are)."),
            new ChallengeInfo(Challenge.Dystopia, "Dystopia", "Every weapon and piece of armour is badly worn (quality 1-15)."),
            new ChallengeInfo(Challenge.EternalDarkness, "Eternal darkness", "Every torch, candle, lantern and light sphere goes (Ultima Underworld keeps the Taper of Sacrifice)."),
            new ChallengeInfo(Challenge.Fasting, "Fasting", "Only part of the food stays, chosen at random. Drinks stay."),
            new ChallengeInfo(Challenge.GhostTown, "Ghost town", "Every creature without a conversation goes (the special ones stay), templates in traps too; what they carried lies where they stood."),
            new ChallengeInfo(Challenge.Hoarding, "Hoarding", "Food, weapons and armour leave the floors and chests: ordinary monsters and traders carry them."),
            new ChallengeInfo(Challenge.Illiterate, "Illiterate", "Books, scrolls and maps go, except the ones the plot needs."),
            new ChallengeInfo(Challenge.Lockdown, "Lockdown", "Every closed door that can be broken gets a lock no key opens: pick it or bash it."),
            new ChallengeInfo(Challenge.Merchant, "Merchant", "All food goes to the traders' inventories; the rest of the world has none."),
            new ChallengeInfo(Challenge.Monk, "Monk", "No weapons: swords, axes, maces, slings, bows, crossbows and their ammunition go."),
            new ChallengeInfo(Challenge.Naked, "Naked", "No armour, shields, rings or crowns."),
            new ChallengeInfo(Challenge.OldSchool, "Old school", "The map item goes: draw your own."),
            new ChallengeInfo(Challenge.Peaceful, "Peaceful", "Every creature is friendly at the start."),
            new ChallengeInfo(Challenge.Precognition, "Precognition", "Every level's map is fully drawn from the start."),
            new ChallengeInfo(Challenge.Ranger, "Ranger", "Each melee weapon becomes a launcher or a stack of ammunition."),
            new ChallengeInfo(Challenge.Starvation, "Starvation", "No food and no fishing poles; the Ylem runestones become other runes."),
            new ChallengeInfo(Challenge.Utopia, "Utopia", "Every weapon and piece of armour is in excellent shape (quality 63)."),
            new ChallengeInfo(Challenge.Vegetarian, "Vegetarian", "No meat, no fish, no fishing poles."),
            new ChallengeInfo(Challenge.VowOfPoverty, "Vow of poverty", "No coins and no gems, and creatures carry no treasure."),
        };

        /// <summary>
        /// The order the ticked challenges are applied in: creatures first (Ghost town, then Bones
        /// rise, so the risen skeletons stay and take Peaceful's or DMD mode's attitude); then the removals,
        /// from treasure and magic to food; the conversions and qualities of what is left; the
        /// moves (Merchant, Hoarding) of what remains; the doors; and last the automaps, which
        /// draw the doors.
        /// </summary>
        public static readonly Challenge[] Order =
        {
            Challenge.GhostTown, Challenge.BonesRise, Challenge.Peaceful, Challenge.DmdMode, Challenge.VowOfPoverty, Challenge.AllergicToMagic, Challenge.Illiterate,
            Challenge.OldSchool, Challenge.EternalDarkness, Challenge.Naked, Challenge.Monk, Challenge.Ranger,
            Challenge.Vegetarian, Challenge.Starvation, Challenge.Fasting, Challenge.Utopia, Challenge.Dystopia,
            Challenge.Merchant, Challenge.Hoarding, Challenge.Lockdown, Challenge.Precognition,
        };

        /// <summary>The pairs that cannot go together.</summary>
        public static readonly Challenge[][] Conflicting =
        {
            new[] { Challenge.Monk, Challenge.Ranger }, new[] { Challenge.Monk, Challenge.Hoarding },
            new[] { Challenge.Utopia, Challenge.Dystopia }, new[] { Challenge.Peaceful, Challenge.DmdMode },
            new[] { Challenge.Merchant, Challenge.Hoarding },
        };

        public static ChallengeInfo Info(Challenge c) { return All.First(i => i.Id == c); }
        public static string NameOf(Challenge c) { return Info(c).Name; }

        /// <summary>Why the ticked challenges cannot be applied together, or null.</summary>
        public static string Conflicts(IEnumerable<Challenge> on)
        {
            var set = new HashSet<Challenge>(on);
            var bad = Conflicting.Where(p => set.Contains(p[0]) && set.Contains(p[1])).ToList();
            if (bad.Count == 0) return null;
            return "These cannot go together: " + string.Join("; ", bad.Select(p => NameOf(p[0]) + " and " + NameOf(p[1]) + Why(p))) + ". Untick one of each pair.";
        }

        static string Why(Challenge[] p)
        {
            if (p.Contains(Challenge.Monk) && p.Contains(Challenge.Ranger)) return " (Monk takes away the launchers Ranger makes)";
            if (p.Contains(Challenge.Monk)) return " (Hoarding would hand out the weapons Monk takes away)";
            if (p.Contains(Challenge.Utopia)) return " (both set the quality)";
            if (p.Contains(Challenge.Peaceful)) return " (one makes creatures friendly, the other hostile)";
            return " (both move the food)";
        }

        /// <summary>A random generator of its own for each challenge, so ticking another does not change this one's result.</summary>
        static Random RngFor(int seed, Challenge c)
        {
            unchecked { return new Random(seed * 397 ^ ((int)c + 1) * 7919); }
        }

        /// <summary>Runs the ticked challenges on the world's copies, in <see cref="Order"/>, and writes the report.</summary>
        public static ChallengeResult Run(ChallengeWorld w, ChallengeOptions opt)
        {
            var r = new ChallengeResult { World = w, Seed = opt.Seed };
            var on = opt.On();
            if (on.Count == 0) { r.Refused = "Tick at least one challenge."; r.Report.Append(r.Refused); return r; }
            r.Refused = Conflicts(on);
            if (r.Refused != null) { r.Report.Append(r.Refused); return r; }
            var ctx = new Ctx(w, opt);
            foreach (var c in Order)
            {
                if (!on.Contains(c)) continue;
                var sec = new ChallengeSection { Id = c };
                r.Sections.Add(sec);
                ctx.Rng = RngFor(opt.Seed, c);
                ctx.Sec = sec;
                RunOne(ctx, c);
            }
            DiffTable(w, r.TableChanges);
            WriteReport(w, opt, r);
            return r;
        }

        static void RunOne(Ctx x, Challenge c)
        {
            switch (c)
            {
                case Challenge.GhostTown: GhostTown(x); break;
                case Challenge.BonesRise: BonesRise(x); break;
                case Challenge.Peaceful: Peaceful(x); break;
                case Challenge.DmdMode: Dmd(x); break;
                case Challenge.VowOfPoverty: Poverty(x); break;
                case Challenge.AllergicToMagic: Allergic(x); break;
                case Challenge.Illiterate: Illiterate(x); break;
                case Challenge.OldSchool: OldSchool(x); break;
                case Challenge.EternalDarkness: Darkness(x); break;
                case Challenge.Naked: Naked(x); break;
                case Challenge.Monk: Monk(x); break;
                case Challenge.Ranger: Ranger(x); break;
                case Challenge.Vegetarian: Vegetarian(x); break;
                case Challenge.Starvation: Starvation(x); break;
                case Challenge.Fasting: Fasting(x); break;
                case Challenge.Utopia: Quality(x, true); break;
                case Challenge.Dystopia: Quality(x, false); break;
                case Challenge.Merchant: Move(x, true); break;
                case Challenge.Hoarding: Move(x, false); break;
                case Challenge.Lockdown: Lockdown(x); break;
                case Challenge.Precognition: Precognition(x); break;
            }
        }

        // ---- the report

        static string LevelName(GameSession s, int level)
        {
            if (level == 0) return "OBJECTS.DAT";
            if (level < 0) return "Player";
            return s.IsUw2 ? s.Profile.LevelName(level) : "Level " + level;
        }

        static void WriteReport(ChallengeWorld w, ChallengeOptions opt, ChallengeResult r)
        {
            var s = w.Session;
            var b = r.Report;
            var changed = r.ChangedLevels();
            b.AppendFormat("Seed {0}: {1} challenge{2}, {3} changes; {4} level{5} change{6}", opt.Seed, r.Sections.Count, r.Sections.Count == 1 ? "" : "s", r.Total,
                changed.Count, changed.Count == 1 ? "" : "s", changed.Count == 1 ? "s" : "");
            int maps = w.OpenedMaps.Count(w.MapChanged);
            if (maps > 0) b.AppendFormat(", {0} automap{1}", maps, maps == 1 ? "" : "s");
            if (r.TableChanges.Count > 0) b.AppendFormat(", {0} field{1} of OBJECTS.DAT's creature table", r.TableChanges.Count, r.TableChanges.Count == 1 ? "" : "s");
            if (w.PlayerChanged) b.Append(", the player");
            b.AppendLine(".");
            b.AppendLine("Applied in this order: " + string.Join(", ", r.Sections.Select(x => NameOf(x.Id))) + ".");
            foreach (var sec in r.Sections)
            {
                b.AppendLine();
                b.AppendFormat("{0}: {1} change{2}.\n", NameOf(sec.Id), sec.Total, sec.Total == 1 ? "" : "s");
                foreach (var n in sec.Notes) b.AppendLine("  " + n);
                if (sec.Kept.Count > 0) b.AppendLine("  Left alone: " + string.Join(", ", sec.Kept.Select(k => k.Key + " " + k.Value)) + ".");
                foreach (var kv in sec.ByLevel)
                    b.AppendFormat("  {0}: {1}{2}\n", LevelName(s, kv.Key), kv.Value.Count, kv.Value.Examples.Count == 0 ? "" : " — " + string.Join("; ", kv.Value.Examples) + (kv.Value.Count > kv.Value.Examples.Count ? "; …" : ""));
            }
            foreach (int level in changed)
            {
                bool ok;
                string check = w.Opened(level).Check(out ok);
                if (!ok) b.AppendLine().AppendFormat("Warning: {0} fails the game's list check after the run: {1}\n", LevelName(s, level), check);
            }
        }

        // ---- applying and undoing

        /// <summary>
        /// Puts a run's result into the real levels (one undoable step a level, through
        /// <see cref="LevelEditor.Replace"/>), automaps, OBJECTS.DAT's creature table (setTable:
        /// row, field, value; it returns null or why not) and player sheet. Returns what Undo needs.
        /// </summary>
        public static ChallengeApplied Apply(ChallengeResult r, LevelEdits edits, Func<int, string, int, string> setTable, PlayerSheet player, string label)
        {
            if (r.Refused != null) throw new InvalidOperationException(r.Refused);
            var a = new ChallengeApplied { Label = label };
            var w = r.World;
            foreach (int level in r.ChangedLevels())
            {
                var se = w.Opened(level);
                var e = edits.For(level);
                if (e.Data.AsSpan().SequenceEqual(se.Data) && (e.TextureBytes == null || se.TextureBytes == null || e.TextureBytes.AsSpan().SequenceEqual(se.TextureBytes))) continue;
                a.Before[level] = Tuple.Create((byte[])e.Data.Clone(), e.TextureBytes == null ? null : (byte[])e.TextureBytes.Clone());
                e.Replace(se.Data, se.TextureBytes, label);
                a.After[level] = (byte[])e.Data.Clone();
            }
            foreach (int level in w.OpenedMaps)
            {
                var sa = w.OpenedMap(level);
                if (!w.MapChanged(level)) continue;
                var ra = edits.AutomapFor(level);
                if (ra.Map.AsSpan().SequenceEqual(sa.Map)) continue;
                a.MapBefore[level] = Tuple.Create((byte[])ra.Map.Clone(), ra.Notes.Select(n => new MapNote { Text = n.Text, X = n.X, Y = n.Y }).ToList());
                ra.Replace(sa.Map, sa.Notes, label);
                edits.AutomapEdited(ra);
                a.MapAfter[level] = (byte[])ra.Map.Clone();
            }
            if (setTable != null)
                foreach (var t in r.TableChanges)
                {
                    string err = setTable(t.Row, t.Field, t.New);
                    if (err == null) a.Table.Add(t);
                }
            if (player != null && w.Player != null && w.PlayerChanged)
            {
                a.Player = PlayerSnapshot.Take(player);
                Array.Copy(w.Player.Dat.Record, player.Dat.Record, Math.Min(player.Dat.Record.Length, w.Player.Dat.Record.Length));
                player.Inventory.Top.Clear();
                player.Inventory.Top.AddRange(w.Player.Inventory.Top);
                player.Inventory.Changed = true;
                player.Dirty = true;
            }
            return a;
        }

        /// <summary>
        /// Puts back what Apply changed, as it was. A level or automap changed since is left alone
        /// (its old bytes would undo those edits too); those levels are returned (Edit ▸ Undo
        /// still steps back there).
        /// </summary>
        public static List<int> Undo(ChallengeApplied a, LevelEdits edits, Func<int, string, int, string> setTable, PlayerSheet player)
        {
            var skipped = new List<int>();
            string label = "Undo " + (a.Label ?? "named challenges");
            foreach (var kv in a.Before)
            {
                var e = edits.For(kv.Key);
                if (!e.Data.AsSpan().SequenceEqual(a.After[kv.Key])) { skipped.Add(kv.Key); continue; }
                e.Replace(kv.Value.Item1, kv.Value.Item2, label);
            }
            foreach (var kv in a.MapBefore)
            {
                var m = edits.AutomapFor(kv.Key);
                if (!m.Map.AsSpan().SequenceEqual(a.MapAfter[kv.Key])) { if (!skipped.Contains(kv.Key)) skipped.Add(kv.Key); continue; }
                m.Replace(kv.Value.Item1, kv.Value.Item2, label);
                edits.AutomapEdited(m);
            }
            if (setTable != null)
                for (int i = a.Table.Count - 1; i >= 0; i--) setTable(a.Table[i].Row, a.Table[i].Field, a.Table[i].Old);
            if (a.Player != null && player != null) a.Player.Restore(player);
            skipped.Sort();
            return skipped;
        }

        // ---- traders and special creatures

        /// <summary>
        /// The conversation slots whose code calls setup_to_barter (conv/BARTER.C): their
        /// characters trade, filling the barter area from their own inventory. Read-only scan of
        /// CNV.ARK: the import named setup_to_barter and a CALLI of it.
        /// </summary>
        public static HashSet<int> TraderSlots(GameSession s)
        {
            var set = new HashSet<int>();
            string cnv = GameProfile.Find(s.DataFolder, "CNV.ARK");
            if (cnv == null) return set;
            ConversationArchive arc;
            try { arc = ConversationArchive.Load(cnv, s.Kind); } catch (Exception) { return set; }
            foreach (int slot in arc.Slots())
            {
                Conversation c;
                try { c = arc.Read(slot); } catch (Exception) { continue; }
                if (c == null) continue;
                var imp = c.FunctionNamed("setup_to_barter");
                if (imp == null) continue;
                for (int pc = 0; pc < c.Code.Length; pc++)
                {
                    int op = c.Code[pc];
                    if (!BablOps.IsKnown(op) || !BablOps.HasOperand((BablOp)op)) continue;
                    if (pc + 1 < c.Code.Length && op == (int)BablOp.CALLI && c.Code[pc + 1] == imp.Index) { set.Add(slot); break; }
                    pc++;
                }
            }
            return set;
        }

        static int Word(byte[] r, int at) { return r[at] | r[at + 1] << 8; }

        static string CreatureLabel(GameSession s, LevelModel m, ObjectInfo o)
        {
            int att = Word(o.Record, 0x0D) >> 14;
            string inside = o.Parent >= 0 ? " in #" + o.Parent + " " + s.Names.Get(m.Objects[o.Parent].Item) : "";
            return string.Format("L{0} #{1} {2} ({3}, {4}) {5}{6}", m.Level, o.Index, s.Names.Get(o.Item), o.TileX, o.TileY, AttitudeName(att), inside);
        }

        static string AttitudeName(int a) { return a == 0 ? "hostile" : a == 1 ? "upset" : a == 2 ? "mellow" : "friendly"; }

        /// <summary>The creatures without a conversation (on the map or templates in traps): what the special list can hold.</summary>
        public static List<IgnoredObject> SpecialCandidates(GameSession s, Func<int, LevelModel> level)
        {
            var l = new List<IgnoredObject>();
            foreach (var slot in s.LevelSlots())
            {
                LevelModel m;
                try { m = level(slot.Level); } catch (Exception) { continue; }
                if (m == null || !m.Present) continue;
                foreach (var o in m.Objects)
                    if (!o.Free && o.TileX >= 0 && o.IsCritter && o.IsMobile && o.Record[0x1A] == 0)
                        l.Add(new IgnoredObject { Level = slot.Level, Index = o.Index, Label = CreatureLabel(s, m, o) });
            }
            return l;
        }

        /// <summary>
        /// The special creatures at first: of the creatures without a conversation, the kinds the
        /// monster swap leaves alone (UW1's fire elementals, 0x78; UW2's brain creatures, 0x58;
        /// <see cref="MonsterSwap.SpecialKind"/>) and the creatures the game gives no name (the
        /// Ethereal Void's, <see cref="MonsterSwap.Unnamed"/>). The plot's own creatures all have
        /// a conversation (UW1's quest creatures, KeyItems.Uw1QuestCreatures, are found by whoami),
        /// and creatures with a conversation are never changed by DMD mode anyway.
        /// </summary>
        public static List<IgnoredObject> DefaultSpecial(GameSession s, Func<int, LevelModel> level)
        {
            int kind = MonsterSwap.SpecialKind(s);
            var l = new List<IgnoredObject>();
            foreach (var c in SpecialCandidates(s, level))
            {
                var o = level(c.Level).Objects[c.Index];
                if (o.Item == kind || MonsterSwap.Unnamed(s, o.Item)) l.Add(c);
            }
            return l;
        }

        /// <summary>In words, what the default special list holds, for the window.</summary>
        public static string SpecialDefaultsText(GameSession s)
        {
            return s.IsUw2
                ? "At first: the brain creatures (the creature the monster swap leaves alone too) and the Ethereal Void's creatures without a name. The plot's characters all have a conversation, and creatures with a conversation are never made hostile."
                : "At first: the fire elementals of level 8 (the creature the monster swap leaves alone too) and the Ethereal Void's creatures without a name. The plot's creatures (the golem, Garamon, the prisoners, Tybal…) all have a conversation, and creatures with a conversation are never made hostile.";
        }
    }
}
