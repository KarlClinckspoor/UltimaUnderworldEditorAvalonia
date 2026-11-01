using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UltimateEditor.Games;

namespace UwEditor.Core.Editing
{
    /// <summary>How bad a finding is.</summary>
    public enum FixSeverity
    {
        /// <summary>The level fails the game's own list check (Obj_ListOkay, obj/OBJECTS.C).</summary>
        ListError = 0,
        /// <summary>The lists pass, but the game will misbehave (a creature that never moves, a trap that frees the wrong object...).</summary>
        Problem = 1,
        /// <summary>Odd, but maybe meant: an item under the floor, a locked door with no key.</summary>
        Warning = 2,
    }

    /// <summary>One kind of check of the Fixes window, with what it looks for.</summary>
    public sealed class FixCheck
    {
        public string Id { get; private set; }
        public string Title { get; private set; }
        public string Description { get; private set; }
        public FixSeverity Severity { get; private set; }

        internal FixCheck(string id, string title, FixSeverity severity, string description)
        {
            Id = id; Title = title; Severity = severity; Description = description;
        }

        public override string ToString() { return Title; }
    }

    /// <summary>One problem found in a level, with its repair (none for some).</summary>
    public sealed class LevelFinding
    {
        public int Level { get; internal set; }
        public FixCheck Check { get; internal set; }
        public string Text { get; internal set; }
        /// <summary>The object it is about, or -1.</summary>
        public int Object { get; internal set; } = -1;
        /// <summary>The tile it is about (or the object's tile), or -1.</summary>
        public int X { get; internal set; } = -1;
        public int Y { get; internal set; } = -1;
        /// <summary>What the fix does, in a few words; null when there is no automatic fix.</summary>
        public string FixText { get; internal set; }
        /// <summary>The repair on the level block bytes. Each one checks that what it changes is still as it was found.</summary>
        internal Action<byte[]> Repair;

        public bool CanFix { get { return Repair != null; } }
        public FixSeverity Severity { get { return Check.Severity; } }

        public override string ToString() { return string.Format("L{0} {1}: {2}", Level, Check.Title, Text); }
    }

    /// <summary>What the checks need from the other levels (keys for the locks, the kinds the inventory traps ask for).</summary>
    public sealed class FixContext
    {
        /// <summary>Lock numbers some key in the game opens (KeyIndex).</summary>
        public HashSet<int> KeyNumbers { get; private set; }

        public static FixContext Build(GameSession s, Func<int, LevelModel> level)
        {
            var c = new FixContext { KeyNumbers = new HashSet<int>() };
            foreach (var g in KeyIndex.Build(s, level)) if (g.Keys.Count > 0) c.KeyNumbers.Add(g.Id);
            return c;
        }
    }

    /// <summary>
    /// The Fixes window's checks, both games, on a level block's bytes (the Unity editor's
    /// Data/LevelFixes.cs, extended). The game's rule (Obj_ListOkay, obj/OBJECTS.C): every
    /// object index from 2 is either on a free stack (mobile 2-0xFF at 0x7300, count − 1 at
    /// 0x7C02; static 0x100-0x3FF at 0x74FC, at 0x7C04) or reached from a tile's list (next
    /// links, and the link of an object that is not is-quantity), exactly once. The active list
    /// (bytes at 0x7AFC, count at 0x7C00) holds the mobiles in use, which the critter code runs
    /// each frame (Obj_Alloc's active_critter, Obj_Free's free_critter; critter/CRITTIME.C).
    /// Repairs work on the bytes, through <see cref="LevelEditor.EditBytes"/>, one undoable step
    /// a fix or a Fix all.
    /// </summary>
    public static class LevelFixes
    {
        public static readonly FixCheck ReachedTwice = new FixCheck("twice", "Reached twice", FixSeverity.ListError,
            "An object is reached twice from the tile lists: a next or contents link points back into a list (a loop) or into another list (a shared chain). The game's check fails, and moving or deleting the object breaks both lists. The fix cuts the link that arrives the second time; nothing is lost, as everything after it was already reached.");
        public static readonly FixCheck PlayerInList = new FixCheck("player", "Player's record in a list", FixSeverity.Problem,
            "A list reaches object 1, the player's record, which never lies in a level's lists. The fix cuts that link.");
        public static readonly FixCheck LostContents = new FixCheck("contents", "Contents under is-quantity", FixSeverity.ListError,
            "A container, creature, door, switch or wand has Is quantity set, but its link names objects that nothing else reaches: its contents, cut off when the flag was set. The fix clears Is quantity, so the link is a list again.");
        public static readonly FixCheck Unreferenced = new FixCheck("lost", "Unreferenced objects", FixSeverity.ListError,
            "An object that is neither on a free stack nor reached from any tile (with what it holds and what follows it). The game's check fails and the slot is lost. The fix frees it the way Obj_Free does (back on its free stack, off the active list, out of the animation list, and traps' links to it cleared); an object a trap or trigger names is put back on that trap's tile instead.");
        public static readonly FixCheck FreeStacks = new FixCheck("free", "Free stacks", FixSeverity.ListError,
            "The free stacks hold an object in use (the game would hand it out again), an index twice, an index of the wrong kind or out of range, or a count past the stack; or an empty slot is on no stack. The fix rebuilds both stacks: the good entries in their order (the game takes from the top), then the empty slots at the bottom.");
        public static readonly FixCheck ActiveList = new FixCheck("active", "Active mobile list", FixSeverity.Problem,
            "A mobile on the map that is not on the active list never moves or acts; an entry that is free, twice, or a mobile inside a trap or container makes the game run it as a critter. The fix rebuilds the list from the mobiles lying on the map, keeping the order.");
        public static readonly FixCheck HomeTile = new FixCheck("home", "Mobile's tile", FixSeverity.Problem,
            "A mobile's tile (Home X and Y, bytes 0x16-0x17) is not the tile whose list holds it. The game finds a mobile's list by these (critter/PATHFIND.C move_me_joe, Obj_Punt): removing it from the wrong list leaves it in the real one, freed. The fix sets them to its tile.");
        public static readonly FixCheck DeadCreature = new FixCheck("hp", "Creatures with 0 hit points", FixSeverity.Problem,
            "A creature with 0 hit points is deleted the first time it moves (critter/PATHFIND.C move_me_joe, through Obj_Punt), unless its quality class is 3 or its kind's tenacity (COMOBJ.DAT) is above 10 (obj/OBJECTS.C Obj_Elem_Fate). The fix gives it its kind's average hit points (OBJECTS.DAT).");
        public static readonly FixCheck TriggerLink = new FixCheck("trigger", "Trap and trigger links", FixSeverity.Problem,
            "A trigger or trap links to an index that is free (the game would run whatever record is there), or a trigger links to something that is not a trap. The fix clears a link to a free index (a trigger with no trap does nothing: event/TRIGGER.C UseTrigger); a link to a non-trap has no automatic fix.");
        public static readonly FixCheck DeleteTarget = new FixCheck("delete", "Delete-object traps", FixSeverity.Problem,
            "A delete object trap (0x18B) removes its linked object from the list of tile (quality, owner) (event/TRIGGER.C UseTrap). When the object does not lie on that tile, the game frees it while it stays in its real list. The fix points the trap at the object's tile.");
        public static readonly FixCheck Overlays = new FixCheck("anim", "Animation overlays", FixSeverity.Problem,
            "UW2 keeps its animation list in the level block (0x7C08, 64 entries; map/MAP.C Anim_Load). An entry names an object that is free or on no tile, so the game animates whatever record is there. The fix drops the entry (rem_anim_from_list, obj/EFFECT.C).");
        public static readonly FixCheck OverlayTile = new FixCheck("animtile", "Animation overlay tiles", FixSeverity.Warning,
            "A UW2 animation entry's tile is not the tile its object lies on. When a limited animation ends, the game takes the object off that tile's list (obj/EFFECT.C), so it stays where it is. The fix sets the entry's tile.");
        public static readonly FixCheck MissingText = new FixCheck("text", "Texts that are not there", FixSeverity.Problem,
            "A book, scroll, gravestone, writing or text trap names a string that STRINGS.PAK does not have, so the game shows nothing or garbage. No automatic fix: pick another string in the object's properties.");
        public static readonly FixCheck TrapLoop = new FixCheck("loop", "Trap chains that loop", FixSeverity.Warning,
            "A chain of traps (each trap's link names the next) comes back to itself. Unless a trap in it ends the chain, the game runs it for ever. No automatic fix.");
        public static readonly FixCheck SolidTile = new FixCheck("solid", "Objects in solid rock", FixSeverity.Warning,
            "An item or creature lies on a solid tile, where the player cannot reach it (traps, triggers and the objects they name are left out: they are often kept there). The fix moves it to the nearest open tile, on its floor.");
        public static readonly FixCheck BelowFloor = new FixCheck("floor", "Items under the floor", FixSeverity.Warning,
            "An item that can be picked up lies below the floor of its open tile. The fix puts it on the floor.");
        public static readonly FixCheck LockNoKey = new FixCheck("key", "Locks with no key", FixSeverity.Warning,
            "A locked lock (other than lock 0, which no key opens) whose number no key in any level has. It may be meant (opened by a lever, a trap or a conversation), or the key is in the player's inventory. No automatic fix.");

        /// <summary>Every check, in the order the fixes are applied by Fix all.</summary>
        public static readonly IReadOnlyList<FixCheck> Checks = new[]
        {
            ReachedTwice, PlayerInList, LostContents, Unreferenced, FreeStacks, ActiveList, HomeTile, DeadCreature,
            TriggerLink, DeleteTarget, Overlays, MissingText, TrapLoop, OverlayTile, SolidTile, BelowFloor, LockNoKey,
        };

        // ---- bytes

        static int W(byte[] d, int o) { return d[o] | d[o + 1] << 8; }
        static void P(byte[] d, int o, int v) { d[o] = (byte)v; d[o + 1] = (byte)(v >> 8); }
        static int Off(int i) { return LevelBlock.ObjectOffset(i); }
        static int Size(int i) { return i < LevelBlock.Mobiles ? LevelBlock.MobileSize : LevelBlock.StaticSize; }
        static int TileAt(int x, int y) { return LevelBlock.TilesAt + (y * 64 + x) * 4; }
        static int ItemOf(byte[] d, int i) { return W(d, Off(i)) & 0x1FF; }
        static bool Quant(byte[] d, int i) { return (W(d, Off(i)) & 0x8000) != 0; }
        static int NextOf(byte[] d, int i) { return W(d, Off(i) + 4) >> 6; }
        static int LinkOf(byte[] d, int i) { return W(d, Off(i) + 6) >> 6; }
        static int QualityOf(byte[] d, int i) { return W(d, Off(i) + 4) & 0x3F; }
        static int OwnerOf(byte[] d, int i) { return W(d, Off(i) + 6) & 0x3F; }
        static int ZOf(byte[] d, int i) { return W(d, Off(i) + 2) & 0x7F; }
        static void SetNext(byte[] d, int i, int n) { int at = Off(i) + 4; P(d, at, (W(d, at) & 0x3F) | n << 6); }
        static void SetLink(byte[] d, int i, int n) { int at = Off(i) + 6; P(d, at, (W(d, at) & 0x3F) | n << 6); }
        static int Head(byte[] d, int x, int y) { return W(d, TileAt(x, y) + 2) >> 6; }
        static void SetHead(byte[] d, int x, int y, int n) { int at = TileAt(x, y) + 2; P(d, at, (W(d, at) & 0x3F) | n << 6); }
        static int TileType(byte[] d, int x, int y) { int t = d[TileAt(x, y)] & 0xF; return t > 9 ? 0 : t; }
        static int FloorStep(byte[] d, int x, int y) { return (d[TileAt(x, y)] >> 4) & 0xF; }
        static bool IsEvent(int item) { return item >= 0x180 && item < 0x1C0; }
        static bool IsTrap(int item) { return item >= 0x180 && item < 0x1A0; }
        static bool IsTrigger(int item) { return item >= 0x1A0 && item < 0x1C0; }
        static bool IsCritter(int item) { return item >= 0x40 && item < 0x80; }
        static int HomeX(byte[] d, int i) { return W(d, Off(i) + 0x16) >> 10; }
        static int HomeY(byte[] d, int i) { return (W(d, Off(i) + 0x16) >> 4) & 0x3F; }
        static bool Blank(byte[] d, int i) { int at = Off(i), n = Size(i); for (int k = 0; k < n; k++) if (d[at + k] != 0) return false; return true; }

        /// <summary>Where a list link is kept: a tile's first object, an object's next or its contents link.</summary>
        struct Ptr
        {
            public int Kind;   // 0 tile head (A = x, B = y), 1 next of A, 2 link of A
            public int A, B;
            public int Get(byte[] d) { return Kind == 0 ? Head(d, A, B) : Kind == 1 ? NextOf(d, A) : LinkOf(d, A); }
            public void Set(byte[] d, int v) { if (Kind == 0) SetHead(d, A, B, v); else if (Kind == 1) SetNext(d, A, v); else SetLink(d, A, v); }
            public string Describe() { return Kind == 0 ? string.Format("the first object of tile ({0}, {1})", A, B) : Kind == 1 ? "the next link of #" + A : "the contents link of #" + A; }
        }

        /// <summary>The level's lists, walked the way Obj_ListOkay walks them, stopping at a repeat.</summary>
        sealed class Scan
        {
            public readonly byte[] D;
            public readonly bool[] Seen = new bool[LevelBlock.Objects];
            public readonly int[] TileX = new int[LevelBlock.Objects], TileY = new int[LevelBlock.Objects], Parent = new int[LevelBlock.Objects];
            public readonly List<KeyValuePair<Ptr, int>> Cuts = new List<KeyValuePair<Ptr, int>>();
            /// <summary>Valid, first-time, unused entries of each stack, in stack order (bottom first).</summary>
            public readonly List<int> MobFree = new List<int>(), StatFree = new List<int>();
            public readonly bool[] OnStack = new bool[LevelBlock.Objects];
            public readonly List<string> StackFaults = new List<string>();
            public readonly List<int> StackFaultObjects = new List<int>();

            public Scan(byte[] d)
            {
                D = d;
                for (int i = 0; i < LevelBlock.Objects; i++) { TileX[i] = TileY[i] = Parent[i] = -1; }
                for (int y = 0; y < 64; y++)
                    for (int x = 0; x < 64; x++)
                        Walk(new Ptr { Kind = 0, A = x, B = y }, Head(d, x, y), x, y, -1, 0);
                ReadStack(LevelBlock.MobFreeAt, LevelBlock.MobTopAt, 254, 2, LevelBlock.Mobiles, MobFree, "mobile");
                ReadStack(LevelBlock.StaticFreeAt, LevelBlock.StaticTopAt, 768, LevelBlock.Mobiles, LevelBlock.Objects, StatFree, "static");
            }

            void Walk(Ptr from, int i, int x, int y, int parent, int depth)
            {
                var p = from;
                while (i > 0)
                {
                    if (Seen[i] || i == 1) { Cuts.Add(new KeyValuePair<Ptr, int>(p, i)); return; }
                    Seen[i] = true;
                    TileX[i] = x; TileY[i] = y; Parent[i] = parent;
                    // the game's check follows a contents link 64 deep (LevelBlock.Walk)
                    if (!Quant(D, i) && LinkOf(D, i) > 0 && depth < 64) Walk(new Ptr { Kind = 2, A = i }, LinkOf(D, i), x, y, i, depth + 1);
                    p = new Ptr { Kind = 1, A = i };
                    i = NextOf(D, i);
                }
            }

            void ReadStack(int at, int topAt, int cap, int lo, int hi, List<int> keep, string kind)
            {
                int top = (short)W(D, topAt);
                if (top < -1 || top >= cap) { StackFaults.Add(string.Format("the {0} stack's count ({1}) is past the stack (it holds {2})", kind, top + 1, cap)); StackFaultObjects.Add(-1); }
                int n = Math.Max(0, Math.Min(cap, top + 1));
                for (int k = 0; k < n; k++)
                {
                    int i = W(D, at + 2 * k);
                    if (i < lo || i >= hi) { StackFaults.Add(string.Format("entry {0} of the {1} stack is {2}, not a {1} index ({3}-{4})", k, kind, i, lo, hi - 1)); StackFaultObjects.Add(-1); continue; }
                    if (OnStack[i]) { StackFaults.Add(string.Format("index {0} is on the {1} stack twice", i, kind)); StackFaultObjects.Add(i); continue; }
                    if (Seen[i]) { StackFaults.Add(string.Format("#{0} is in use but also on the {1} stack: the game would hand it out again", i, kind)); StackFaultObjects.Add(i); continue; }
                    OnStack[i] = true;
                    keep.Add(i);
                }
            }

            public bool Reached(int i) { return i > 0 && i < LevelBlock.Objects && Seen[i]; }
            public bool OnTile(int i) { return Seen[i] && Parent[i] < 0; }
            /// <summary>Neither reached nor on a free stack (index 2 and up).</summary>
            public bool Lost(int i) { return i >= 2 && !Seen[i] && !OnStack[i]; }
        }

        // ---- the free stacks and the active list, written back

        static void WriteStacks(byte[] d, List<int> mob, List<int> stat)
        {
            for (int k = 0; k < 254; k++) P(d, LevelBlock.MobFreeAt + 2 * k, k < mob.Count ? mob[k] : 0);
            for (int k = 0; k < 768; k++) P(d, LevelBlock.StaticFreeAt + 2 * k, k < stat.Count ? stat[k] : 0);
            P(d, LevelBlock.MobTopAt, mob.Count - 1);
            P(d, LevelBlock.StaticTopAt, stat.Count - 1);
        }

        /// <summary>The active list's entries, as stored (count clamped to the space, 0x104 bytes).</summary>
        static List<int> ReadActive(byte[] d)
        {
            int n = Math.Min(W(d, LevelBlock.ActiveCountAt), LevelBlock.ActiveCountAt - LevelBlock.ActiveAt);
            var list = new List<int>();
            for (int k = 0; k < n; k++) list.Add(d[LevelBlock.ActiveAt + k]);
            return list;
        }

        static void WriteActive(byte[] d, List<int> list)
        {
            int space = LevelBlock.ActiveCountAt - LevelBlock.ActiveAt;
            for (int k = 0; k < space; k++) d[LevelBlock.ActiveAt + k] = (byte)(k < list.Count ? list[k] : 0);
            P(d, LevelBlock.ActiveCountAt, list.Count);
        }

        /// <summary>The stacks rebuilt: their good entries in order, empty (all-zero) lost slots at the bottom, then freed indices pushed on top (Obj_Free).</summary>
        static void RebuildStacks(byte[] d, IEnumerable<int> freed)
        {
            var s = new Scan(d);
            var mob = new List<int>(s.MobFree);
            var stat = new List<int>(s.StatFree);
            var add = new HashSet<int>(freed ?? Enumerable.Empty<int>());
            var bottomMob = new List<int>();
            var bottomStat = new List<int>();
            for (int i = LevelBlock.Objects - 1; i >= 2; i--)
                if (s.Lost(i) && Blank(d, i) && !add.Contains(i)) (i < LevelBlock.Mobiles ? bottomMob : bottomStat).Add(i);
            mob.InsertRange(0, bottomMob);
            stat.InsertRange(0, bottomStat);
            foreach (int i in add.OrderByDescending(x => x))
                if (s.Lost(i)) (i < LevelBlock.Mobiles ? mob : stat).Add(i);
            WriteStacks(d, mob, stat);
        }

        /// <summary>The active list as the mobiles lying on the map, keeping the order of the good entries.</summary>
        static void RebuildActive(byte[] d)
        {
            var s = new Scan(d);
            var list = new List<int>();
            var have = new HashSet<int>();
            foreach (int v in ReadActive(d))
                if (v >= 2 && v < LevelBlock.Mobiles && s.OnTile(v) && have.Add(v)) list.Add(v);
            for (int i = 2; i < LevelBlock.Mobiles; i++)
                if (s.OnTile(i) && have.Add(i)) list.Add(i);
            WriteActive(d, list);
        }

        /// <summary>UW2: the animation list's entries (object index, tile), up to the first empty one (Anim_Load).</summary>
        static int OverlayCount(byte[] d, GameKind game)
        {
            if (game != GameKind.UW2 || d.Length < LevelBlock.Uw2TimersAt) return 0;
            int n = 0;
            while (n < 64 && (W(d, LevelBlock.Uw2OverlaysAt + n * 6) >> 6) != 0) n++;
            return n;
        }

        /// <summary>Drops animation entry n, the last filling its place (obj/EFFECT.C rem_anim_from_list); the rest zeroed as Anim_Save does.</summary>
        static void DropOverlay(byte[] d, GameKind game, int n)
        {
            int count = OverlayCount(d, game);
            if (n >= count) return;
            int at = LevelBlock.Uw2OverlaysAt;
            if (n != count - 1) Array.Copy(d, at + (count - 1) * 6, d, at + n * 6, 6);
            Array.Clear(d, at + (count - 1) * 6, 6);
        }

        /// <summary>
        /// Frees objects as Obj_Free does (obj/OBJECTS.C): records cleared, mobiles off the active
        /// list (free_critter), their animation entries dropped (Obj_Punt's rem_anim_from_list),
        /// traps' and triggers' links to them cleared (so a reused slot is not set off by an old
        /// trigger), then on top of their free stacks.
        /// </summary>
        static void FreeObjects(byte[] d, GameKind game, ICollection<int> set)
        {
            var s = new Scan(d);
            foreach (int i in set) if (!s.Lost(i)) throw new InvalidOperationException("#" + i + " is in use or free already.");
            for (int j = 2; j < LevelBlock.Objects; j++)
                if (s.Reached(j) && IsEvent(ItemOf(d, j)) && set.Contains(LinkOf(d, j))) SetLink(d, j, 0);
            var active = ReadActive(d);
            if (active.RemoveAll(v => set.Contains(v)) > 0) WriteActive(d, active);
            for (int n = OverlayCount(d, game) - 1; n >= 0; n--)
                if (set.Contains(W(d, LevelBlock.Uw2OverlaysAt + n * 6) >> 6)) DropOverlay(d, game, n);
            foreach (int i in set) Array.Clear(d, Off(i), Size(i));
            RebuildStacks(d, set);
        }

        // ---- finding

        static string Name(GameSession s, byte[] d, int i) { return "#" + i + " " + s.Names.Get(ItemOf(d, i)); }

        /// <summary>The findings of one level as edited.</summary>
        public static List<LevelFinding> Find(LevelEditor e, FixContext context = null)
        {
            return Find(e.Session, e.Level, e.Data, context);
        }

        /// <summary>The findings of a level block (its bytes are read, not changed).</summary>
        public static List<LevelFinding> Find(GameSession session, int level, byte[] data, FixContext context = null)
        {
            var list = new List<LevelFinding>();
            if (data == null || data.Length < LevelBlock.MagicAt + 2 || W(data, LevelBlock.MagicAt) != LevelBlock.Magic) return list;
            var game = session.Kind;
            var d = data;
            var s = new Scan(d);
            Func<FixCheck, int, string, LevelFinding> add = (c, obj, text) =>
            {
                var f = new LevelFinding { Level = level, Check = c, Object = obj, Text = text };
                if (obj > 0 && obj < LevelBlock.Objects && s.Seen[obj]) { f.X = s.TileX[obj]; f.Y = s.TileY[obj]; }
                list.Add(f);
                return f;
            };

            // ---- links that arrive a second time
            foreach (var cut in s.Cuts)
            {
                var p = cut.Key; int target = cut.Value;
                var f = target == 1
                    ? add(PlayerInList, p.Kind == 0 ? -1 : p.A, Capital(p.Describe()) + " points at object 1, the player's record.")
                    : add(ReachedTwice, target, string.Format("{0} points at {1}, already reached{2}.", Capital(p.Describe()), Name(session, d, target),
                        s.TileX[target] >= 0 ? string.Format(" from tile ({0}, {1})", s.TileX[target], s.TileY[target]) : ""));
                if (p.Kind == 0) { f.X = p.A; f.Y = p.B; } else if (s.Seen[p.A]) { f.Object = target == 1 ? p.A : f.Object; f.X = s.TileX[p.A]; f.Y = s.TileY[p.A]; }
                f.FixText = "Cut the link";
                var pp = p; int t = target;
                f.Repair = b => { if (pp.Get(b) == t) pp.Set(b, 0); };
            }

            // ---- lost objects: grouped into what is reached from each lost root
            var lost = new List<int>();
            for (int i = 2; i < LevelBlock.Objects; i++) if (s.Lost(i) && !Blank(d, i)) lost.Add(i);
            var lostSet = new HashSet<int>(lost);
            var pointed = new HashSet<int>();
            foreach (int i in lost)
            {
                int n = NextOf(d, i); if (lostSet.Contains(n) && n != i) pointed.Add(n);
                if (!Quant(d, i)) { int l = LinkOf(d, i); if (lostSet.Contains(l) && l != i) pointed.Add(l); }
            }
            var covered = new HashSet<int>();
            // who names each lost root: a trap or trigger's link, or an is-quantity holder's link
            var namers = new Dictionary<int, int>();
            var holders = new Dictionary<int, int>();
            for (int j = 2; j < LevelBlock.Objects; j++)
            {
                if (!s.Reached(j)) continue;
                int it = ItemOf(d, j), l = LinkOf(d, j);
                if (!lostSet.Contains(l)) continue;
                if (IsEvent(it)) { if (!namers.ContainsKey(l)) namers[l] = j; }
                else if (Quant(d, j) && HoldsLists(it, game) && !holders.ContainsKey(l)) holders[l] = j;
            }
            var roots = lost.Where(i => !pointed.Contains(i)).ToList();
            roots.AddRange(lost);   // what is left after the roots: loops of lost objects, by their lowest index
            foreach (int root in roots)
            {
                if (covered.Contains(root)) continue;
                var comp = Component(d, root, lostSet, covered);
                string more = comp.Count > 1 ? string.Format(" (with {0} more object{1} it holds or that follow it)", comp.Count - 1, comp.Count == 2 ? "" : "s") : "";
                int holder, namer;
                if (holders.TryGetValue(root, out holder))
                {
                    var f = add(LostContents, holder, string.Format("{0} has Is quantity set, but its link {1} is the head of its lost contents, {2}{3}.", Name(session, d, holder), root, Name(session, d, root), more));
                    f.FixText = "Clear Is quantity";
                    int h = holder, r = root;
                    f.Repair = b => { if (Quant(b, h) && LinkOf(b, h) == r) { int at = Off(h); P(b, at, W(b, at) & 0x7FFF); } };
                }
                else if (namers.TryGetValue(root, out namer) && s.TileX[namer] >= 0)
                {
                    int tx = s.TileX[namer], ty = s.TileY[namer];
                    var f = add(Unreferenced, root, string.Format("{0}{1} is on no tile, but {2} names it.", Name(session, d, root), more, Name(session, d, namer)));
                    f.X = tx; f.Y = ty;
                    f.FixText = string.Format("Put it on tile ({0}, {1})", tx, ty);
                    int r = root;
                    f.Repair = b => { var sc = new Scan(b); if (!sc.Lost(r)) return; SetNext(b, r, Head(b, tx, ty)); SetHead(b, tx, ty, r); RebuildActive(b); };
                }
                else
                {
                    bool mobile = root < LevelBlock.Mobiles;
                    var f = add(Unreferenced, root, string.Format("{0}{1} is on no tile and in nothing, and not free{2}.", Name(session, d, root), more,
                        mobile && IsCritter(ItemOf(d, root)) ? string.Format(" (its tile would be ({0}, {1}))", HomeX(d, root), HomeY(d, root)) : ""));
                    if (mobile) { f.X = HomeX(d, root); f.Y = HomeY(d, root); }
                    f.FixText = comp.Count > 1 ? "Free them" : "Free it";
                    var set = comp;
                    f.Repair = b => { var sc = new Scan(b); var still = set.Where(sc.Lost).ToList(); if (still.Count > 0) FreeObjects(b, game, still); };
                }
            }

            // ---- free stacks
            var blankLost = Enumerable.Range(2, LevelBlock.Objects - 2).Where(i => s.Lost(i) && Blank(d, i)).ToList();
            if (s.StackFaults.Count > 0 || blankLost.Count > 0)
            {
                for (int k = 0; k < s.StackFaults.Count; k++)
                {
                    var f = add(FreeStacks, s.StackFaultObjects[k], Capital(s.StackFaults[k]) + ".");
                    f.FixText = "Rebuild the free stacks";
                    f.Repair = b => RebuildStacks(b, null);
                }
                if (blankLost.Count > 0)
                {
                    var f = add(FreeStacks, blankLost[0], string.Format("{0} empty slot{1} on no free stack and in no list ({2}{3}): the game can never use {4}.",
                        blankLost.Count, blankLost.Count == 1 ? " is" : "s are", string.Join(", ", blankLost.Take(8)), blankLost.Count > 8 ? ", ..." : "", blankLost.Count == 1 ? "it" : "them"));
                    f.X = f.Y = -1;
                    f.FixText = "Rebuild the free stacks";
                    f.Repair = b => RebuildStacks(b, null);
                }
            }

            // ---- the active mobile list
            {
                int stored = W(d, LevelBlock.ActiveCountAt);
                var active = ReadActive(d);
                var seenA = new HashSet<int>();
                var faults = new List<KeyValuePair<int, string>>();
                if (stored > LevelBlock.ActiveCountAt - LevelBlock.ActiveAt) faults.Add(new KeyValuePair<int, string>(-1, string.Format("The active list's count ({0}) is past its space.", stored)));
                foreach (int v in active)
                {
                    if (!seenA.Add(v)) { faults.Add(new KeyValuePair<int, string>(v, string.Format("#{0} is on the active list twice.", v))); continue; }
                    if (v < 2) faults.Add(new KeyValuePair<int, string>(-1, string.Format("The active list holds {0}, which is not a mobile.", v)));
                    else if (!s.Reached(v)) faults.Add(new KeyValuePair<int, string>(v, string.Format("The active list holds #{0}, which is {1}.", v, s.OnStack[v] ? "free" : "on no tile")));
                    else if (!s.OnTile(v)) faults.Add(new KeyValuePair<int, string>(v, string.Format("{0} lies inside #{1} but is on the active list: the game would run it as a critter.", Name(session, d, v), s.Parent[v])));
                }
                for (int i = 2; i < LevelBlock.Mobiles; i++)
                    if (s.OnTile(i) && !seenA.Contains(i)) faults.Add(new KeyValuePair<int, string>(i, string.Format("{0} lies on the map but is not on the active list: it will not move or act.", Name(session, d, i))));
                foreach (var kv in faults)
                {
                    var f = add(ActiveList, kv.Key, kv.Value);
                    f.FixText = "Rebuild the active list";
                    f.Repair = RebuildActive;
                }
            }

            // ---- objects one by one
            var named = new HashSet<int>();   // what traps and triggers name, and what lies inside them
            for (int j = 2; j < LevelBlock.Objects; j++)
                if (s.Reached(j) && IsEvent(ItemOf(d, j)) && LinkOf(d, j) > 0) named.Add(LinkOf(d, j));
            for (int i = 2; i < LevelBlock.Objects; i++)
                if (s.Reached(i) && s.Parent[i] >= 0 && IsEvent(ItemOf(d, s.Parent[i]))) named.Add(i);

            for (int i = 2; i < LevelBlock.Objects; i++)
            {
                if (!s.Reached(i)) continue;
                int it = ItemOf(d, i);
                int idx = i;
                bool onTile = s.OnTile(i);
                int tx = s.TileX[i], ty = s.TileY[i];

                if (i < LevelBlock.Mobiles && onTile && (HomeX(d, i) != tx || HomeY(d, i) != ty))
                {
                    var f = add(HomeTile, i, string.Format("{0} lies on tile ({1}, {2}), but its tile bytes say ({3}, {4}).", Name(session, d, i), tx, ty, HomeX(d, i), HomeY(d, i)));
                    f.FixText = "Set them to its tile";
                    f.Repair = b =>
                    {
                        int at = Off(idx) + 0x16;
                        P(b, at, (W(b, at) & 0xF) | (ty & 0x3F) << 4 | (tx & 0x3F) << 10);
                    };
                }

                if (i < LevelBlock.Mobiles && IsCritter(it) && onTile && d[Off(i) + 8] == 0 && ((Races.ComObjByte(session, it, 6) >> 2) & 3) < 3
                    && ((Races.ComObjByte(session, it, 9) >> 2) & 0xF) <= 10
                    // kinds without a name (UW1's 0x7D and 0x7E on level 9) are the game's own special objects, left as shipped
                    && !string.IsNullOrWhiteSpace(session.Strings?.Get(4, it)))
                {
                    int hp = AverageHp(session, it);
                    var f = add(DeadCreature, i, string.Format("{0} has 0 hit points: the game deletes it when it first moves.", Name(session, d, i)));
                    f.FixText = "Give it " + hp + " hit points";
                    f.Repair = b => { if (b[Off(idx) + 8] == 0) b[Off(idx) + 8] = (byte)hp; };
                }

                if (IsEvent(it))
                {
                    int l = LinkOf(d, i);
                    if (l > 1 && !s.Reached(l))
                    {
                        var f = add(TriggerLink, i, string.Format("{0} links to {1}, which is {2}.", Name(session, d, i), l, s.OnStack[l] ? "free" : "on no tile"));
                        if (s.OnStack[l] || Blank(d, l))
                        {
                            f.FixText = "Clear the link";
                            f.Repair = b => { var sc = new Scan(b); int ll = LinkOf(b, idx); if (ll == l && !sc.Reached(ll)) SetLink(b, idx, 0); };
                        }
                    }
                    else if (l > 1 && IsTrigger(it) && !IsEvent(ItemOf(d, l)))
                        add(TriggerLink, i, string.Format("{0} links to {1}, which is not a trap: the game would run it as one.", Name(session, d, i), Name(session, d, l)));

                    // a chain of traps that comes back to itself (event/TRIGGER.C UseTrap follows each trap's link)
                    var chain = new List<int> { i };
                    int cur = l, guard = 0;
                    while (cur > 1 && s.Reached(cur) && IsEvent(ItemOf(d, cur)) && guard++ < 1100)
                    {
                        int at = chain.IndexOf(cur);
                        if (at >= 0)
                        {
                            if (at == 0 && chain.Min() == i)   // reported once, from the chain's lowest index
                                add(TrapLoop, i, string.Format("The trap chain {0} comes back to #{1}.", string.Join(" → ", chain.Select(c => "#" + c)), i));
                            break;
                        }
                        chain.Add(cur);
                        cur = LinkOf(d, cur);
                    }

                    // delete object trap: its target must lie on tile (quality, owner)
                    if (it == 0x18B && l > 1 && s.Reached(l))
                    {
                        int q = QualityOf(d, i), o = OwnerOf(d, i);
                        if (s.OnTile(l) && (s.TileX[l] != q || s.TileY[l] != o))
                        {
                            int lx = s.TileX[l], ly = s.TileY[l];
                            var f = add(DeleteTarget, i, string.Format("{0} deletes {1} from tile ({2}, {3}), but it lies on ({4}, {5}).", Name(session, d, i), Name(session, d, l), q, o, lx, ly));
                            f.FixText = string.Format("Point it at ({0}, {1})", lx, ly);
                            f.Repair = b =>
                            {
                                if (LinkOf(b, idx) != l) return;
                                P(b, Off(idx) + 4, (W(b, Off(idx) + 4) & ~0x3F) | lx);
                                P(b, Off(idx) + 6, (W(b, Off(idx) + 6) & ~0x3F) | ly);
                            };
                        }
                        else if (!s.OnTile(l))
                            add(DeleteTarget, i, string.Format("{0} deletes {1}, which lies inside #{2}: the game only looks on the tile's own list.", Name(session, d, i), Name(session, d, l), s.Parent[l]));
                    }
                }

                string missing = MissingString(session, d, i, it);
                if (missing != null) add(MissingText, i, Name(session, d, i) + " " + missing + ".");

                if (onTile && TileType(d, tx, ty) == 0 && it < 0x140 && !named.Contains(i))
                {
                    int nx, ny;
                    var f = add(SolidTile, i, string.Format("{0} lies on solid tile ({1}, {2}).", Name(session, d, i), tx, ty));
                    if (NearestOpen(d, tx, ty, out nx, out ny))
                    {
                        f.FixText = string.Format("Move it to ({0}, {1})", nx, ny);
                        f.Repair = b => MoveTo(b, idx, tx, ty, nx, ny);
                    }
                }
                else if (onTile && TileType(d, tx, ty) == 1 && it < 0x140 && !IsCritter(it) && (Races.ComObjByte(session, it, 3) & 0x20) != 0 && ZOf(d, i) < FloorStep(d, tx, ty) * 8)
                {
                    int floor = Math.Min(127, FloorStep(d, tx, ty) * 8);
                    var f = add(BelowFloor, i, string.Format("{0} at height {1} is below the floor ({2}) of tile ({3}, {4}).", Name(session, d, i), ZOf(d, i), floor, tx, ty));
                    f.FixText = "Put it on the floor";
                    f.Repair = b => { int at = Off(idx) + 2; if ((W(b, at) & 0x7F) < floor) P(b, at, (W(b, at) & ~0x7F) | floor); };
                }

                if (it == 0x10F && Quant(d, i) && context != null && ((W(d, Off(i)) >> 9) & 1) != 0)
                {
                    // lock 0 is opened by levers and traps, not keys; a lock inside a door trap is a template
                    int id = LinkOf(d, i) & 0x1FF;
                    if (id != 0 && !named.Contains(i) && !context.KeyNumbers.Contains(id))
                        add(LockNoKey, i, string.Format("{0} (lock {1}{2}) is locked, and no key in the levels opens it.", Name(session, d, i), id, s.Parent[i] >= 0 ? ", in " + Name(session, d, s.Parent[i]) : ""));
                }
            }

            // ---- UW2's animation list
            for (int n = 0, count = OverlayCount(d, game); n < count; n++)
            {
                int at = LevelBlock.Uw2OverlaysAt + n * 6;
                int obj = W(d, at) >> 6, ox = d[at + 4], oy = d[at + 5];
                int entry = n;
                if (!s.Reached(obj))
                {
                    var f = add(Overlays, obj, string.Format("Animation entry {0} names #{1}, which is {2}.", n, obj, s.OnStack[obj] ? "free" : "on no tile"));
                    f.X = ox < 64 ? ox : -1; f.Y = oy < 64 ? oy : -1;
                    f.FixText = "Drop the entry";
                    f.Repair = b => { int a2 = LevelBlock.Uw2OverlaysAt + entry * 6; if (entry < OverlayCount(b, game) && (W(b, a2) >> 6) == obj) DropOverlay(b, game, entry); };
                }
                else if (s.OnTile(obj) && (s.TileX[obj] != ox || s.TileY[obj] != oy))
                {
                    int lx = s.TileX[obj], ly = s.TileY[obj];
                    var f = add(OverlayTile, obj, string.Format("Animation entry {0} of {1} says tile ({2}, {3}), but it lies on ({4}, {5}).", n, Name(session, d, obj), ox, oy, lx, ly));
                    f.FixText = "Set the entry's tile";
                    f.Repair = b => { int a2 = LevelBlock.Uw2OverlaysAt + entry * 6; if ((W(b, a2) >> 6) == obj) { b[a2 + 4] = (byte)lx; b[a2 + 5] = (byte)ly; } };
                }
            }
            return list;
        }

        /// <summary>Kinds whose link is normally a list (a container's contents, a creature's inventory, a door's lock, a switch's or wand's objects).</summary>
        static bool HoldsLists(int item, GameKind game)
        {
            var c = ItemCategories.Of(item, game);
            return (c & (ItemCategory.Container | ItemCategory.Creature | ItemCategory.Door | ItemCategory.Switch | ItemCategory.Wand)) != 0;
        }

        /// <summary>The lost objects reached from root through next and contents links (not through anything already covered).</summary>
        static List<int> Component(byte[] d, int root, HashSet<int> lost, HashSet<int> covered)
        {
            var comp = new List<int>();
            var stack = new Stack<int>();
            stack.Push(root);
            while (stack.Count > 0)
            {
                int i = stack.Pop();
                if (!lost.Contains(i) || !covered.Add(i)) continue;
                comp.Add(i);
                stack.Push(NextOf(d, i));
                if (!Quant(d, i)) stack.Push(LinkOf(d, i));
            }
            return comp;
        }

        /// <summary>
        /// What is wrong with an object's text, or null: a book or scroll's string is block 3
        /// link − 0x200 (not an enchanted scroll, whose link is a spell); a gravestone (0x165)
        /// and a writing (0x166) are block 8 link − 0x200; a text trap (0x190) is block 9
        /// quality &lt;&lt; 5 | owner (UW2 the owner's low 5 bits; event/TRIGGER.C UseTrap).
        /// The same rules as <see cref="ObjectTexts.TextOf"/>.
        /// </summary>
        static string MissingString(GameSession s, byte[] d, int i, int item)
        {
            if (s.Strings == null) return null;
            bool quant = Quant(d, i);
            int link = LinkOf(d, i);
            int block = -1, index = -1;
            var cat = ItemCategories.Of(item, s.Kind);
            bool enchanted = (W(d, Off(i)) & 0x1000) != 0;
            if ((cat & (ItemCategory.Book | ItemCategory.Scroll)) != 0 && quant && !enchanted && link >= 0x200)
            {
                // UW2's maps (0x138-0x13A) are scrolls by class but draw the automap
                if (s.IsUw2 && item >= 0x138 && item <= 0x13A) return null;
                block = 3; index = link - 0x200;
            }
            else if ((item == 0x165 || item == 0x166) && quant && link >= 0x200) { block = 8; index = link - 0x200; }
            else if (item == 0x190) { block = 9; index = QualityOf(d, i) << 5 | (s.IsUw2 ? OwnerOf(d, i) & 0x1F : OwnerOf(d, i)); }
            if (block < 0 || s.Strings.Block(block).Length == 0) return null;
            return s.Strings.Get(block, index) == null ? string.Format("names string {0} of block {1}, which has {2}", index, block, s.Strings.Block(block).Length) : null;
        }

        static string Capital(string t) { return t.Length == 0 ? t : char.ToUpperInvariant(t[0]) + t.Substring(1); }

        /// <summary>A creature kind's average hit points (OBJECTS.DAT, 0x30 bytes a creature from 0x132, byte 4; as LevelEditor gives new creatures).</summary>
        static int AverageHp(GameSession s, int item)
        {
            try
            {
                string p = GameProfile.Find(s.DataFolder, "OBJECTS.DAT");
                if (p == null) return 10;
                var d = File.ReadAllBytes(p);
                int at = 0x132 + 0x30 * (item - 0x40) + 4;
                return at < d.Length ? Math.Max(1, (int)d[at]) : 10;
            }
            catch (IOException) { return 10; }
        }

        /// <summary>The open tile nearest (x, y), by rings; false when the level has none.</summary>
        static bool NearestOpen(byte[] d, int x, int y, out int nx, out int ny)
        {
            for (int r = 1; r < 64; r++)
            {
                int best = int.MaxValue; nx = ny = -1;
                for (int yy = y - r; yy <= y + r; yy++)
                    for (int xx = x - r; xx <= x + r; xx++)
                    {
                        if (xx < 0 || yy < 0 || xx > 63 || yy > 63) continue;
                        if (Math.Max(Math.Abs(xx - x), Math.Abs(yy - y)) != r || TileType(d, xx, yy) == 0) continue;
                        int dist = (xx - x) * (xx - x) + (yy - y) * (yy - y);
                        if (dist < best) { best = dist; nx = xx; ny = yy; }
                    }
                if (nx >= 0) return true;
            }
            nx = ny = -1;
            return false;
        }

        /// <summary>Takes an object lying on tile (x, y) off its list and puts it at the head of (nx, ny)'s, on the floor (a mobile's tile bytes too).</summary>
        static void MoveTo(byte[] b, int i, int x, int y, int nx, int ny)
        {
            var s = new Scan(b);
            if (!s.OnTile(i) || s.TileX[i] != x || s.TileY[i] != y) return;
            if (Head(b, x, y) == i) SetHead(b, x, y, NextOf(b, i));
            else
            {
                int p = Head(b, x, y), guard = 0;
                while (p > 0 && NextOf(b, p) != i && guard++ < 1100) p = NextOf(b, p);
                if (p <= 0 || guard >= 1100) return;
                SetNext(b, p, NextOf(b, i));
            }
            SetNext(b, i, Head(b, nx, ny));
            SetHead(b, nx, ny, i);
            int at = Off(i) + 2;
            P(b, at, (W(b, at) & ~0x7F) | Math.Min(127, FloorStep(b, nx, ny) * 8));
            if (i < LevelBlock.Mobiles) { int h = Off(i) + 0x16; P(b, h, (W(b, h) & 0xF) | (ny & 0x3F) << 4 | (nx & 0x3F) << 10); }
        }

        // ---- fixing

        /// <summary>Applies one finding's fix to the level, as one undoable step. False when it has none or nothing changed.</summary>
        public static bool Fix(LevelEditor e, LevelFinding f)
        {
            if (f == null || f.Repair == null || f.Level != e.Level) return false;
            var before = (byte[])e.Data.Clone();
            var after = (byte[])e.Data.Clone();
            f.Repair(after);
            if (after.SequenceEqual(before)) return false;
            e.EditBytes("Fix: " + f.Check.Title.ToLowerInvariant(), (data, tex) => Array.Copy(after, data, data.Length));
            return true;
        }

        /// <summary>Several findings of one level fixed together, as one undoable step. Returns how many changed something.</summary>
        public static int FixMany(LevelEditor e, IEnumerable<LevelFinding> list, string label)
        {
            var copy = (byte[])e.Data.Clone();
            int n = 0;
            foreach (var f in list)
            {
                if (f.Repair == null || f.Level != e.Level) continue;
                var before = (byte[])copy.Clone();
                f.Repair(copy);
                if (!before.SequenceEqual(copy)) n++;
            }
            if (n > 0) e.EditBytes(label, (data, tex) => Array.Copy(copy, data, data.Length));
            return n;
        }

        /// <summary>
        /// Fixes everything that has a fix (warnings only when asked), as one undoable step.
        /// Returns the number of findings fixed (0: nothing changed, no step).
        /// </summary>
        public static int FixAll(LevelEditor e, bool warningsToo = false, FixContext context = null)
        {
            var copy = (byte[])e.Data.Clone();
            int n = FixAllBytes(e.Session, e.Level, copy, warningsToo, context);
            if (n == 0 || copy.SequenceEqual(e.Data)) return 0;
            e.EditBytes("Fix all (" + n + ")", (data, tex) => Array.Copy(copy, data, data.Length));
            return n;
        }

        /// <summary>
        /// The Fix all of a level block's bytes, in place: in each round the fixes of the first
        /// check (in <see cref="Checks"/> order) that has any are applied, then the level is
        /// checked again, until nothing fixable is left. List links are cut first, lost contents
        /// restored, lost objects freed, then the stacks and the active list rebuilt.
        /// </summary>
        public static int FixAllBytes(GameSession s, int level, byte[] data, bool warningsToo = false, FixContext context = null)
        {
            int fixedCount = 0;
            for (int round = 0; round < 200; round++)
            {
                var found = Find(s, level, data, context).Where(f => f.CanFix && (warningsToo || f.Severity != FixSeverity.Warning)).ToList();
                bool changed = false;
                foreach (var check in Checks)
                {
                    var these = found.Where(f => f.Check == check).ToList();
                    if (these.Count == 0) continue;
                    var before = (byte[])data.Clone();
                    foreach (var f in these) f.Repair(data);
                    if (data.SequenceEqual(before)) continue;   // these no longer apply: try the next check
                    fixedCount += these.Count;
                    changed = true;
                    break;
                }
                if (!changed) break;
            }
            return fixedCount;
        }
    }
}
