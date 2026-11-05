using System;
using System.Collections.Generic;
using System.Linq;
using UltimateEditor.Games;
using UltimateEditor.Uw1;

namespace UwEditor.Core.Editing
{
    /// <summary>What a door trap does to the door on its square (event/TRIGGER.C UseTrap, TRAP_DOOR: quality 1-3).</summary>
    public enum DoorAction { Open = 1, Close = 2, Toggle = 3 }

    /// <summary>The wall of a tile a new lever goes on (y north, x east).</summary>
    public enum WallSide { Auto, North, East, South, West }

    /// <summary>What sets off a trigger put inside an object (OBJECTS.DAT's trigger modes: pick up 2, use 4, look 5).</summary>
    public enum ObjectEvent { PickUp, Use, Look }

    /// <summary>The traps a recipe can end in.</summary>
    public enum RecipeTrap { Damage, Message, SetVariable, CreateObject, Door, DeleteObject }

    /// <summary>Who may set a new trigger off, and whether it lasts.</summary>
    public sealed class TriggerOptions
    {
        /// <summary>Flag 10: the trigger works every time. Without it the chain is deleted after it runs once (UseTrigger, delete_trap).</summary>
        public bool Reusable = true;
        /// <summary>The enchanted bit: creatures set it off too.</summary>
        public bool Creatures;
    }

    /// <summary>The trap a recipe makes, and its settings.</summary>
    public sealed class TrapAction
    {
        public RecipeTrap Kind;
        /// <summary>Damage: hit points (1-63); with Poison, the poison strength (1-15).</summary>
        public int Damage = 5;
        public bool Poison;
        /// <summary>Message: a new string for STRINGS.PAK block 9, or (when TextIndex ≥ 0) an existing one.</summary>
        public string Text;
        public int TextIndex = -1;
        /// <summary>Set variable: the variable (UW1 1-63, or the quest bit 0-31 with QuestBit; UW2 a numbered variable 0-0x19F).</summary>
        public int Variable = 1;
        /// <summary>UW1 only: Variable is a quest bit (set variable trap with z 0).</summary>
        public bool QuestBit;
        /// <summary>The trap's heading: 0 add, 1 subtract, 2 set, 3 and, 4 or, 5 xor, 6 shift left (UW1 quest bits: 1 clear, 5 toggle, others set).</summary>
        public int Operation = 2;
        public int Value = 1;
        /// <summary>Create object: the object copied each time (kept inside the trap) and the square it appears on (-1: the trigger's square).</summary>
        public ObjectSpec Template;
        public int X = -1, Y = -1;
        /// <summary>Door: the door object (on its tile), what happens to it, and whether its lock is taken off.</summary>
        public int Door = -1;
        public DoorAction DoorAction = DoorAction.Toggle;
        public bool Unlock;
        /// <summary>Delete object: the object removed (lying on its tile).</summary>
        public int Delete = -1;
    }

    /// <summary>A lever (or switch, button, pull chain) that works a door.</summary>
    public sealed class LeverDoorOptions
    {
        public int Door = -1;
        /// <summary>An existing switch (0x170-0x17F) in the level, or -1 for a new one on a wall of (WallX, WallY).</summary>
        public int Lever = -1;
        public int WallX = -1, WallY = -1;
        public WallSide Side = WallSide.Auto;
        public int LeverItem = 0x175;
        public DoorAction Action = DoorAction.Toggle;
        public bool Unlock;
        public TriggerOptions Trigger = new TriggerOptions();
    }

    /// <summary>A pair of teleports: stepping on From sends the player to To (on the destination editor's level), and optionally back.</summary>
    public sealed class TeleportOptions
    {
        public int FromX = -1, FromY = -1, ToX = -1, ToY = -1;
        public bool Back;
        /// <summary>The way back: a square on the destination level, and where on the first level it leads.</summary>
        public int BackFromX = -1, BackFromY = -1, BackToX = -1, BackToY = -1;
        public TriggerOptions Trigger = new TriggerOptions();
    }

    /// <summary>A wandering-monster spawner: the square, the creature (UW2: 0x7F for a random one) and the chance each run.</summary>
    public sealed class SpawnerOptions
    {
        public int X = -1, Y = -1;
        public int Item = 0x43;
        /// <summary>The chance, in percent, that a run makes the creature (the trap's quality is 63 minus that in 63rds).</summary>
        public double Chance = 30;
        public bool Hostile = true;
    }

    /// <summary>What a recipe made.</summary>
    public sealed class RecipeResult
    {
        public int Level;
        /// <summary>The new trigger (or the first one).</summary>
        public int Trigger = -1;
        /// <summary>The first trap of the trigger's chain.</summary>
        public int Trap = -1;
        /// <summary>The lever, when the recipe added one.</summary>
        public int Lever = -1;
        /// <summary>Every object added, in order.</summary>
        public readonly List<int> Made = new List<int>();
        /// <summary>The block 9 string a text trap prints (-1: none).</summary>
        public int StringIndex = -1;
        /// <summary>The way back of a teleport pair (on the destination level), or null.</summary>
        public RecipeResult Back;
        public string Summary;
        public override string ToString() { return Summary; }
    }

    /// <summary>A recipe's name and explanation, for the window.</summary>
    public sealed class RecipeInfo
    {
        public string Name, Explanation;
        public override string ToString() { return Name; }
    }

    /// <summary>
    /// Trap recipes: a working trigger → trap chain made in one undo step (LevelEditor.Batch),
    /// laid out the way the shipped levels of both games have it (event/TRIGGER.C UseTrigger,
    /// UseTrap; obj/USEITEMS.C checkTrap):
    /// - a trigger's quality and owner are its target square, where the chain runs; its link
    ///   names the first trap (is_quant set, as every shipped trigger). Flag 11 lets the player
    ///   set it off, flag 10 makes it reusable, the enchanted bit lets creatures set it off;
    /// - the first trap lies on the target square (a used-up trigger looks for it there:
    ///   delete_trap on Map_GetAddr(quality, owner)); its flags 9-12 count the triggers
    ///   pointing at it. Traps are not is_quant, so whatever their link names (a create
    ///   object trap's template, a UW1 door trap's lock) lies inside them;
    /// - a lever is a switch (class 0x17) whose contents hold a use trigger: using it runs
    ///   checkTrap(who, switch, 4), which fires the first trap-class object in the list. A
    ///   switch in its "on" state passes to the next trigger in its list, so with one trigger
    ///   both states fire it;
    /// - a trigger inside an object (pick up 2, use 4, look 5) needs the object to hold a
    ///   list: is_quant clear.
    /// </summary>
    public static class TrapRecipes
    {
        public const int MoveTrigger = 0x1A0, PickUpTrigger = 0x1A1, UseTrigger = 0x1A2, LookTrigger = 0x1A3, Uw2PressureTrigger = 0x1A4;
        public const int DamageTrap = 0x180, TeleportTrap = 0x181, CreateTrap = 0x187, DoorTrap = 0x188, DeleteTrap = 0x18B, SetVariableTrap = 0x18D, TextTrap = 0x190;
        /// <summary>STRINGS.PAK block 9: the text string traps' messages, in both games (include/ui.h STR_TRAPTEXT 0x1200).</summary>
        public const int TextBlock = 9;

        public static readonly RecipeInfo[] Recipes =
        {
            new RecipeInfo { Name = "Lever opens a door", Explanation = "A lever, switch, button or pull chain that opens, closes or toggles a door. Use one already in the level, or put a new one on a wall of a tile. Its use trigger runs a door trap on the door's square." },
            new RecipeInfo { Name = "Teleport", Explanation = "Stepping on a square sends the player to another square, on this level or another one (the teleport's z is the level). Optionally, a second pair of squares sends the player back. Only the player is teleported." },
            new RecipeInfo { Name = "Pressure plate", Explanation = "Stepping on a square runs a trap: damage, a message, a variable, a new object or creature, a door, or removing an object. A move trigger works in both games; Underworld II also has pressure plates, which fire when weight comes onto the square." },
            new RecipeInfo { Name = "Object trigger", Explanation = "Picking up, using or looking at an object runs a trap: a message, a variable, damage, a new object or creature, a door, or removing an object. The trigger goes inside the object." },
            new RecipeInfo { Name = "Wandering monsters", Explanation = "A spawner: a create-object trap with no trigger, which the game runs by itself every few game minutes (10 in Underworld II, 8 in Ultima Underworld) with a 1 in 4 chance while the player is more than 8 squares away, and whenever he sleeps. Each run makes its creature with the chance set here, unless a creature it made is still within 4 squares. The creatures it makes go away when the player sleeps. In Underworld II the creature can be a random monster that grows stronger with the level, the castle plot and the player's level." },
        };

        static FieldDef Field(int index, string name) { return (index < LevelBlock.Mobiles ? Uw1Schemas.MobileObject : Uw1Schemas.StaticObject)[name]; }

        static void Need(bool ok, string why) { if (!ok) throw new InvalidOperationException(why); }

        static string Name(GameSession s, int item) { return s.Names.Get(item) ?? ("item 0x" + item.ToString("X3")); }

        static bool IsDoor(int item) { return item >= 0x140 && item < 0x150; }
        static bool IsSwitch(int item) { return item >= 0x170 && item < 0x180; }

        static void Square(LevelModel m, int x, int y, string what)
        {
            Need(x >= 0 && x < 64 && y >= 0 && y < 64, what + ": choose a square.");
            Need(!m.Tiles[x, y].IsSolid, string.Format("{0}: ({1}, {2}) is solid rock.", what, x, y));
        }

        // ---- the pieces

        /// <summary>A trap as the shipped levels have them: invisible, not is_quant, fine 0, 0, at the floor.</summary>
        static ObjectSpec TrapSpec(int item, int z)
        {
            return new ObjectSpec { Item = item, Quality = 0, Owner = 0, IsQuantity = false, Link = 0, Invisible = true, Flags = 0, FineX = 0, FineY = 0, Z = z };
        }

        /// <summary>A trigger aimed at (tx, ty): is_quant, invisible, flag 11 (the player), flag 10 if reusable, enchanted for creatures.</summary>
        static ObjectSpec TriggerSpec(int item, int tx, int ty, TriggerOptions t, int? z)
        {
            t = t ?? new TriggerOptions();
            // a UW2 pressure plate keeps its state in fine y (bit 0 pressed, bit 1 floor texture, bit 2 the heavy threshold: TRIGGER.C check_pplate): 0 starts it up
            return new ObjectSpec { Item = item, Quality = tx & 0x3F, Owner = ty & 0x3F, IsQuantity = true, Link = 0, Invisible = true, Flags = 4 | (t.Reusable ? 2 : 0), Enchanted = t.Creatures, FineX = 3, FineY = item == Uw2PressureTrigger ? 0 : 3, Z = z };
        }

        /// <summary>A copy of an object's record fields (a UW1 door trap carries a copy of the door's lock).</summary>
        static ObjectSpec SpecOf(ObjectInfo o)
        {
            int w0 = o.Record[0] | o.Record[1] << 8;
            return new ObjectSpec
            {
                Item = o.Item, Quality = o.Quality, Owner = o.Owner, Link = o.Link, IsQuantity = o.IsQuantity, Enchanted = o.Enchanted, Invisible = o.Invisible,
                Flags = (w0 >> 9) & 7, Heading = o.Heading, FineX = o.FineX, FineY = o.FineY, Z = o.Z,
            };
        }

        static void Word(LevelEditor e, int index, int at, Func<int, int> change)
        {
            e.EditBytes("#" + index, (d, t) =>
            {
                int o = LevelBlock.ObjectOffset(index) + at;
                int w = change(d[o] | d[o + 1] << 8);
                d[o] = (byte)w; d[o + 1] = (byte)(w >> 8);
            });
        }

        /// <summary>An is_quant object's link (a trigger's trap, a delete trap's object). Never a list head.</summary>
        static void SetLink(LevelEditor e, int index, int link)
        {
            Need(e.Model.Objects[index].IsQuantity, "#" + index + "'s link is a list.");
            Word(e, index, 6, w => (w & 0x3F) | (link & 0x3FF) << 6);
        }

        /// <summary>A trap's flags 9-12 (bit 12 is the enchanted bit, the 8s): how many triggers link to it, as delete_trap counts them.</summary>
        static void CountTriggers(LevelEditor e, int trap)
        {
            var m = e.Model;
            int n = m.Objects.Count(o => !o.Free && o.TileX >= 0 && Triggers.IsTrigger(o.Item) && o.Link == trap);
            Word(e, trap, 0, w => (w & ~0x1E00) | Math.Min(15, n) << 9);
        }

        /// <summary>
        /// A creature kept inside a trap as its template is not on the active list (the game
        /// keeps only mobiles lying on a tile there: LevelBlock.MobilesOnTiles); AddObject put it
        /// on, so it comes off again the way free_critter does it (obj/OBJECTS.C).
        /// </summary>
        static void OffActiveList(LevelEditor e, int mobile)
        {
            var kind = e.Session.Kind;
            e.EditBytes("#" + mobile, (d, t) =>
            {
                var b = new LevelBlock(d, kind);
                int n = b.ActiveCount;
                for (int k = 0; k < n; k++)
                    if (d[LevelBlock.ActiveAt + k] == mobile)
                    {
                        d[LevelBlock.ActiveAt + k] = d[LevelBlock.ActiveAt + n - 1];
                        d[LevelBlock.ActiveAt + n - 1] = 0;
                        b.Put16(LevelBlock.ActiveCountAt, n - 1);
                        break;
                    }
            });
        }

        static int Add(LevelEditor e, RecipeResult r, ObjectSpec spec, int x, int y, int container = -1)
        {
            int i = e.AddObject(spec, x, y, container);
            r.Made.Add(i);
            return i;
        }

        /// <summary>What a trap action needs, checked before anything changes.</summary>
        static void CheckAction(LevelEditor e, TrapAction a, int sx, int sy, int host, StringsEditor strings)
        {
            var m = e.Model;
            var s = e.Session;
            Need(a != null, "Choose what the trap does.");
            switch (a.Kind)
            {
                case RecipeTrap.Damage:
                    if (a.Poison) Need(a.Damage >= 1 && a.Damage <= 15, "Poison strength is 1-15 (it is kept in 4 bits).");
                    else Need(a.Damage >= 1 && a.Damage <= 63, "Damage is 1-63.");
                    break;
                case RecipeTrap.Message:
                    if (a.TextIndex >= 0) Need(a.TextIndex < 2048, "A text trap reaches strings 0-2047 of block 9.");
                    else
                    {
                        Need(!string.IsNullOrWhiteSpace(a.Text), "Type the message.");
                        Need(strings != null, "The strings (STRINGS.PAK) are not open.");
                        Need(strings.Get(TextBlock).Count < 2048, "Block 9 is full: a text trap reaches strings 0-2047.");
                    }
                    break;
                case RecipeTrap.SetVariable:
                    Need(a.Operation >= 0 && a.Operation <= 7, "The operation is 0-7.");
                    if (s.IsUw2)
                    {
                        Need(a.Variable >= 0 && a.Variable < 0x1A0, "Underworld II's numbered variables are 0-0x19F.");
                        Need(a.Value >= 0 && a.Value < 4096, "The value is 0-4095.");
                    }
                    else if (a.QuestBit) Need(a.Variable >= 0 && a.Variable < 32, "Quest bits are 0-31.");
                    else
                    {
                        Need(a.Variable >= 1 && a.Variable <= 63, "Ultima Underworld's game variables are 1-63 (0 cannot be set).");
                        Need(a.Value >= 0 && a.Value < 256, "The value is 0-255 (the variable keeps 6 bits).");
                    }
                    break;
                case RecipeTrap.CreateObject:
                    Need(a.Template != null, "Choose the object to create.");
                    Square(m, a.X < 0 ? sx : a.X, a.X < 0 ? sy : a.Y, "Where it appears");
                    break;
                case RecipeTrap.Door:
                {
                    Need(a.Door > 0 && a.Door < m.Objects.Length && !m.Objects[a.Door].Free, "Choose the door.");
                    var d = m.Objects[a.Door];
                    Need(IsDoor(d.Item), "#" + a.Door + " " + d.Name + " is not a door (0x140-0x14F).");
                    Need(d.OnTile, "The door must lie on its tile: the door trap looks in the square's own list.");
                    Need(!m.Tiles[d.TileX, d.TileY].IsSolid, string.Format("The door's square ({0}, {1}) is rock, so the door trap cannot lie there.", d.TileX, d.TileY));
                    break;
                }
                case RecipeTrap.DeleteObject:
                {
                    Need(a.Delete > 1 && a.Delete < m.Objects.Length && !m.Objects[a.Delete].Free, "Choose the object to remove.");
                    var d = m.Objects[a.Delete];
                    Need(d.OnTile, "The object to remove must lie on its tile (not inside something): the delete trap looks in the square's list.");
                    Need(!d.IsCritter, "A creature cannot be removed this way (it would stay on the active list).");
                    Need(!d.IsTrapOrTrigger, "Remove traps and triggers in the triggers window.");
                    Need(a.Delete != host, "The object would remove itself.");
                    break;
                }
            }
        }

        /// <summary>Places the trap of an action (on the target square, top of its list) and returns it; tx, ty is the square the trigger aims at.</summary>
        static int MakeTrap(LevelEditor e, RecipeResult r, TrapAction a, int sx, int sy, int stringIndex, out int tx, out int ty)
        {
            var s = e.Session;
            var m = e.Model;
            tx = sx; ty = sy;
            ObjectSpec t;
            switch (a.Kind)
            {
                case RecipeTrap.Damage:
                    // UseTrap TRAP_DAMAGE: quality damage to whoever set it off; owner set poisons the player at strength quality (WORLDEV.C whack_thing)
                    t = TrapSpec(DamageTrap, e.FloorZ(tx, ty));
                    t.Quality = a.Damage; t.Owner = a.Poison ? 1 : 0;
                    return Add(e, r, t, tx, ty);
                case RecipeTrap.Message:
                    // TRAP_TEXT_STRING: string quality << 5 | owner of block 9 (UW2: owner & 0x1F, owner bit 5 prints never)
                    t = TrapSpec(TextTrap, e.FloorZ(tx, ty));
                    t.Quality = stringIndex >> 5; t.Owner = stringIndex & 31;
                    return Add(e, r, t, tx, ty);
                case RecipeTrap.SetVariable:
                    t = TrapSpec(SetVariableTrap, e.FloorZ(tx, ty));
                    t.Heading = a.Operation & 7;
                    if (s.IsUw2)
                    {
                        // uw2 TRAP_SET_VARIABLE: variable z + (fine x << 7), value (quality << 6) + owner, operation heading; fine y 0 leaves the "changed" clock
                        t.Z = a.Variable & 0x7F; t.FineX = a.Variable >> 7;
                        t.Quality = a.Value >> 6; t.Owner = a.Value & 63;
                    }
                    else
                    {
                        // uw1 TRAP_SET_VARIABLE: z 0 a quest bit, else game variable z; value ((quality << 5 | owner) << 3) | fine y
                        int v = a.QuestBit ? a.Variable : a.Value;
                        t.Z = a.QuestBit ? 0 : a.Variable;
                        t.Quality = v >> 8; t.Owner = (v >> 3) & 31; t.FineY = v & 7;
                    }
                    return Add(e, r, t, tx, ty);
                case RecipeTrap.CreateObject:
                {
                    // TRAP_CREATE_OBJECT: copies the linked object onto the square the chain runs at, at the template's own fine position and z; quality 0 always
                    if (a.X >= 0) { tx = a.X; ty = a.Y; }
                    int trap = Add(e, r, TrapSpec(CreateTrap, e.FloorZ(tx, ty)), tx, ty);
                    var tmp = Clone(a.Template);
                    tmp.Z = e.FloorZ(tx, ty, tmp.FineX, tmp.FineY);
                    if (tmp.IsMobile) { tmp.Quality = tx; tmp.Owner = ty; }       // as the shipped templates have it
                    int made = Add(e, r, tmp, tx, ty, trap);
                    if (tmp.IsMobile) OffActiveList(e, made);
                    return trap;
                }
                case RecipeTrap.Door:
                {
                    var door = m.Objects[a.Door];
                    tx = door.TileX; ty = door.TileY;
                    t = TrapSpec(DoorTrap, e.FloorZ(tx, ty));
                    t.Quality = (int)a.DoorAction;
                    ObjectSpec lockCopy = null;
                    if (s.IsUw2) t.Owner = a.Unlock ? 1 : 0;   // uw2: owner set swaps the lock (no link: removes it); owner 0 leaves it
                    else if (!a.Unlock)
                    {
                        // uw1 always takes the door's lock off and puts on a copy of the trap's linked lock: give it a copy of the present one
                        var lk = door.Children.Select(c => m.Objects[c]).FirstOrDefault(c => c.Item == 0x10F);
                        if (lk != null) lockCopy = SpecOf(lk);
                    }
                    int trap = Add(e, r, t, tx, ty);
                    if (lockCopy != null) Add(e, r, lockCopy, tx, ty, trap);
                    return trap;
                }
                case RecipeTrap.DeleteObject:
                {
                    // TRAP_DELETE_OBJECT: frees its linked object from square (quality, owner); is_quant, as the shipped ones, so the object is not its contents
                    var d = m.Objects[a.Delete];
                    t = TrapSpec(DeleteTrap, e.FloorZ(tx, ty));
                    t.Quality = d.TileX; t.Owner = d.TileY; t.IsQuantity = true;
                    int trap = Add(e, r, t, tx, ty);
                    SetLink(e, trap, a.Delete);
                    return trap;
                }
            }
            throw new InvalidOperationException("Unknown trap.");
        }

        static ObjectSpec Clone(ObjectSpec a)
        {
            var c = new ObjectSpec
            {
                Item = a.Item, Quality = a.Quality, Owner = a.Owner, Link = a.Link, IsQuantity = a.IsQuantity, Enchanted = a.Enchanted, Invisible = a.Invisible,
                Flags = a.Flags, Heading = a.Heading, FineX = a.FineX, FineY = a.FineY, Z = a.Z, Hp = a.Hp, Attitude = a.Attitude, Whoami = a.Whoami, Goal = a.Goal,
            };
            foreach (var x in a.Contents) c.Contents.Add(Clone(x));
            return c;
        }

        /// <summary>A delete trap is used once: a reusable trigger would free whatever later takes the object's slot.</summary>
        static TriggerOptions For(TrapAction a, TriggerOptions t)
        {
            t = t ?? new TriggerOptions();
            if (a.Kind == RecipeTrap.DeleteObject && t.Reusable) return new TriggerOptions { Reusable = false, Creatures = t.Creatures };
            return t;
        }

        static void Checked(LevelEditor e)
        {
            bool ok;
            string report = e.Check(out ok);
            Need(ok, "The level's list check failed after the recipe (" + report + "); nothing was changed.");
        }

        static string Describe(GameSession s, TrapAction a, int stringIndex)
        {
            switch (a.Kind)
            {
                case RecipeTrap.Damage: return a.Poison ? "poisons (strength " + a.Damage + ")" : a.Damage + " damage";
                case RecipeTrap.Message: return "prints string " + stringIndex + " of block 9";
                case RecipeTrap.SetVariable: return (s.IsUw2 ? Uw2Traps.VariableName(a.Variable) : a.QuestBit ? "quest bit " + a.Variable : "variable " + a.Variable) + ": operation " + a.Operation + ", value " + a.Value;
                case RecipeTrap.CreateObject: return "creates " + Name(s, a.Template.Item);
                case RecipeTrap.Door: return a.DoorAction.ToString().ToLowerInvariant() + "s the door";
                case RecipeTrap.DeleteObject: return "removes #" + a.Delete;
            }
            return "";
        }

        // ---- 1. a lever works a door

        /// <summary>The fine position and heading of a lever on a wall (as the shipped ones: north fy 7 heading 0, east fx 7 heading 2, south fy 0 heading 4, west fx 0 heading 6).</summary>
        public static void WallPlace(WallSide side, out int fx, out int fy, out int heading)
        {
            switch (side)
            {
                case WallSide.East: fx = 7; fy = 3; heading = 2; return;
                case WallSide.South: fx = 3; fy = 0; heading = 4; return;
                case WallSide.West: fx = 0; fy = 3; heading = 6; return;
                default: fx = 3; fy = 7; heading = 0; return;
            }
        }

        /// <summary>Whether the side of an open tile is a wall (rock or the map's edge across it).</summary>
        public static bool IsWall(LevelModel m, int x, int y, WallSide side)
        {
            int nx = x, ny = y;
            switch (side)
            {
                case WallSide.North: ny++; break;
                case WallSide.East: nx++; break;
                case WallSide.South: ny--; break;
                case WallSide.West: nx--; break;
                default: return false;
            }
            return nx < 0 || ny < 0 || nx > 63 || ny > 63 || m.Tiles[nx, ny].IsSolid;
        }

        /// <summary>The side a lever goes on: the one asked for, or the first wall (north, east, south, west).</summary>
        public static WallSide ResolveSide(LevelModel m, int x, int y, WallSide side)
        {
            if (side != WallSide.Auto)
            {
                Need(IsWall(m, x, y, side), string.Format("The {0} side of ({1}, {2}) is not a wall.", side.ToString().ToLowerInvariant(), x, y));
                return side;
            }
            foreach (var w in new[] { WallSide.North, WallSide.East, WallSide.South, WallSide.West })
                if (IsWall(m, x, y, w)) return w;
            throw new InvalidOperationException(string.Format("({0}, {1}) has no wall beside it: choose a tile next to a wall.", x, y));
        }

        /// <summary>The switches a new lever can be (the "off" forms, 0x170-0x177).</summary>
        public static List<int> LeverItems() { return Enumerable.Range(0x170, 8).ToList(); }

        static void CheckHost(LevelModel m, int index, string what)
        {
            Need(index > 1 && index < m.Objects.Length && !m.Objects[index].Free && m.Objects[index].TileX >= 0, "Choose " + what + " in the level.");
            var o = m.Objects[index];
            Need(!o.IsTrapOrTrigger, "#" + index + " is a trap or trigger itself.");
            Need(!o.IsCritter, "A creature cannot hold a trigger.");
            if (o.IsQuantity)
            {
                Need(o.Link < 0x200, "#" + index + " " + o.Name + " holds a special value in its link (an enchantment or a text), so it cannot hold a trigger.");
                Need(o.Link <= 1, "#" + index + " " + o.Name + " is a stack of " + o.Link + ": split it first (a trigger needs a single object).");
            }
            else
                Need(!o.Children.Any(c => m.Objects[c].IsTrapOrTrigger), "#" + index + " " + o.Name + " already holds a trap or trigger: change its chain in the triggers window.");
        }

        /// <summary>Makes the object hold a list (is_quant clear): a single object, its link then empty.</summary>
        static void MakeHolder(LevelEditor e, int index)
        {
            if (e.Model.Objects[index].IsQuantity) e.SetObjectField(index, Field(index, "Is quantity"), 0);
        }

        /// <summary>A lever (existing or new on a wall) whose use trigger runs a door trap on the door's square. One undo step.</summary>
        public static RecipeResult LeverDoor(LevelEditor e, LeverDoorOptions o)
        {
            var s = e.Session;
            var m = e.Model;
            var action = new TrapAction { Kind = RecipeTrap.Door, Door = o.Door, DoorAction = o.Action, Unlock = o.Unlock };
            CheckAction(e, action, 0, 0, -1, null);
            WallSide side = WallSide.Auto;
            if (o.Lever >= 0)
            {
                CheckHost(m, o.Lever, "the lever");
                Need(IsSwitch(m.Objects[o.Lever].Item), "#" + o.Lever + " " + m.Objects[o.Lever].Name + " is not a lever, switch, button or pull chain (0x170-0x17F).");
            }
            else
            {
                Square(m, o.WallX, o.WallY, "The lever's tile");
                Need(IsSwitch(o.LeverItem), "A lever is item 0x170-0x17F.");
                side = ResolveSide(m, o.WallX, o.WallY, o.Side);
            }
            var r = new RecipeResult { Level = e.Level };
            e.Batch("Recipe: lever and door", () =>
            {
                int lever = o.Lever;
                if (lever < 0)
                {
                    int fx, fy, h;
                    WallPlace(side, out fx, out fy, out h);
                    var spec = new ObjectSpec { Item = o.LeverItem, Quality = 0x28, IsQuantity = false, Link = 0, Heading = h, FineX = fx, FineY = fy };
                    spec.Z = Math.Min(127, e.FloorZ(o.WallX, o.WallY, fx, fy) + 16);
                    lever = Add(e, r, spec, o.WallX, o.WallY);
                    r.Lever = lever;
                }
                else MakeHolder(e, lever);
                int tx, ty;
                int trap = MakeTrap(e, r, action, 0, 0, -1, out tx, out ty);
                int trig = Add(e, r, TriggerSpec(UseTrigger, tx, ty, o.Trigger, 0), 0, 0, lever);
                SetLink(e, trig, trap);
                CountTriggers(e, trap);
                Checked(e);
                r.Trigger = trig; r.Trap = trap;
            });
            var lv = e.Model.Objects[r.Lever >= 0 ? r.Lever : o.Lever];
            r.Summary = string.Format("#{0} {1} on ({2}, {3}) {4} #{5} on ({6}, {7}): use trigger #{8}, door trap #{9}.", lv.Index, lv.Name, lv.TileX, lv.TileY,
                Describe(s, action, -1).Replace("the door", Name(s, m.Objects[o.Door].Item)), o.Door, e.Model.Objects[r.Trap].TileX, e.Model.Objects[r.Trap].TileY, r.Trigger, r.Trap);
            return r;
        }

        // ---- 2. teleports

        static RecipeResult OneTeleport(LevelEditor e, int ax, int ay, int destLevel, int bx, int by, TriggerOptions t)
        {
            var r = new RecipeResult { Level = e.Level };
            // TRAP_TELEPORT: quality, owner the square, z the level (0 this level); a move trigger on the square runs it
            var tp = TrapSpec(TeleportTrap, e.FloorZ(ax, ay));
            tp.Quality = bx; tp.Owner = by; tp.Z = destLevel == e.Level ? 0 : destLevel;
            int trap = Add(e, r, tp, ax, ay);
            int trig = Add(e, r, TriggerSpec(MoveTrigger, ax, ay, t, null), ax, ay);
            SetLink(e, trig, trap);
            CountTriggers(e, trap);
            Checked(e);
            r.Trigger = trig; r.Trap = trap;
            r.Summary = string.Format("Stepping on ({0}, {1}) of level {2} teleports to ({3}, {4}) of level {5}: move trigger #{6}, teleport trap #{7}.", ax, ay, e.Level, bx, by, destLevel, trig, trap);
            return r;
        }

        static void Destination(LevelModel m, int x, int y, string what)
        {
            Square(m, x, y, what);
            // uw1 do_teleport ignores x 0; 0x3F skips the search for a free square (Uw1Traps.Roles)
            Need(x >= 1 && x < 63 && y < 63, what + ": x 1-62 and y 0-62.");
        }

        /// <summary>
        /// A teleport pair: a move trigger on From with a teleport trap there sends the player to
        /// To on the destination editor's level (the same editor for this level); with Back a
        /// second pair on the destination level leads back. One undo step on each level changed.
        /// </summary>
        public static RecipeResult Teleport(LevelEditor from, LevelEditor to, TeleportOptions o)
        {
            to = to ?? from;
            Need(to.Session == from.Session, "Both levels must be of the same game.");
            var mf = from.Model; var mt = to.Model;
            Square(mf, o.FromX, o.FromY, "Step on");
            Destination(mt, o.ToX, o.ToY, "Arrive at");
            bool same = from == to;
            if (same) Need(o.FromX != o.ToX || o.FromY != o.ToY, "The player would arrive where he started.");
            if (o.Back)
            {
                Square(mt, o.BackFromX, o.BackFromY, "Back: step on");
                Destination(mf, o.BackToX, o.BackToY, "Back: arrive at");
                Need(o.BackFromX != o.ToX || o.BackFromY != o.ToY, "The way back must start on a different square from the arrival, or the player is sent straight back.");
                Need(o.BackToX != o.FromX || o.BackToY != o.FromY, "The way back must arrive on a different square from where the first teleport starts, or the player is sent straight back.");
                if (same) Need(o.BackFromX != o.FromX || o.BackFromY != o.FromY, "The way back needs its own square.");
            }
            RecipeResult r = null;
            if (same)
            {
                from.Batch("Recipe: teleports", () =>
                {
                    r = OneTeleport(from, o.FromX, o.FromY, from.Level, o.ToX, o.ToY, o.Trigger);
                    if (o.Back) r.Back = OneTeleport(from, o.BackFromX, o.BackFromY, from.Level, o.BackToX, o.BackToY, o.Trigger);
                });
                return r;
            }
            from.Batch("Recipe: teleport to level " + to.Level, () => r = OneTeleport(from, o.FromX, o.FromY, to.Level, o.ToX, o.ToY, o.Trigger));
            if (o.Back)
            {
                try { to.Batch("Recipe: teleport back to level " + from.Level, () => r.Back = OneTeleport(to, o.BackFromX, o.BackFromY, from.Level, o.BackToX, o.BackToY, o.Trigger)); }
                catch { from.Undo(); throw; }
            }
            return r;
        }

        // ---- 3. a pressure plate

        /// <summary>
        /// A trigger on a square that runs a trap: a move trigger (both games: walked into at the
        /// floor) or, in UW2, a pressure trigger (TRIGGER.C check_pplate: weight coming onto the
        /// square, at the trigger's height). One undo step; a message's new string goes into
        /// <paramref name="strings"/> (block 9) once the level change is made.
        /// </summary>
        public static RecipeResult Plate(LevelEditor e, int x, int y, bool pressure, TrapAction a, TriggerOptions t = null, StringsEditor strings = null)
        {
            var s = e.Session;
            Square(e.Model, x, y, "The plate");
            Need(!pressure || s.IsUw2, "Ultima Underworld has no pressure plates: use a move trigger.");
            CheckAction(e, a, x, y, -1, strings);
            t = For(a, t);
            int str = a.Kind == RecipeTrap.Message ? (a.TextIndex >= 0 ? a.TextIndex : strings.Get(TextBlock).Count) : -1;
            var r = new RecipeResult { Level = e.Level, StringIndex = str };
            e.Batch("Recipe: pressure plate", () =>
            {
                int tx, ty;
                int trap = MakeTrap(e, r, a, x, y, str, out tx, out ty);
                int trig = Add(e, r, TriggerSpec(pressure ? Uw2PressureTrigger : MoveTrigger, tx, ty, t, null), x, y);
                SetLink(e, trig, trap);
                CountTriggers(e, trap);
                Checked(e);
                r.Trigger = trig; r.Trap = trap;
            });
            AddString(a, strings, str);
            r.Summary = string.Format("Stepping on ({0}, {1}) {2}: {3} #{4}, {5} #{6}.", x, y, Describe(s, a, str), pressure ? "pressure trigger" : "move trigger", r.Trigger, Name(s, e.Model.Objects[r.Trap].Item), r.Trap);
            return r;
        }

        /// <summary>UW1's hack trap (trap type 3) and the quality that ends the game.</summary>
        public const int HackTrap = 0x183, EndOfGameQuality = 0x3F;

        /// <summary>
        /// UW1: a square that ends the game: a move trigger running a hack trap of quality 0x3F,
        /// as level 9's (#1022 at (52, 51) and #1021). The trap sets EndGameMode (event/TRIGGER.C
        /// do_trap_hack), and check_victory (game/SKILLS.C), run every frame on any level, then
        /// plays the ending. One undo step.
        /// </summary>
        public static RecipeResult EndOfGame(LevelEditor e, int x, int y)
        {
            var s = e.Session;
            Need(!s.IsUw2, "Only Ultima Underworld 1's end of the game is known (hack trap 0x3F).");
            Square(e.Model, x, y, "The end of the game");
            var r = new RecipeResult { Level = e.Level };
            e.Batch("End of the game here", () =>
            {
                var t = TrapSpec(HackTrap, e.FloorZ(x, y));
                t.Quality = EndOfGameQuality; t.Owner = 0;
                int trap = Add(e, r, t, x, y);
                int trig = Add(e, r, TriggerSpec(MoveTrigger, x, y, new TriggerOptions { Reusable = true }, null), x, y);
                SetLink(e, trig, trap);
                CountTriggers(e, trap);
                Checked(e);
                r.Trigger = trig; r.Trap = trap;
            });
            r.Summary = string.Format("Stepping on ({0}, {1}) of level {2} ends the game: move trigger #{3}, hack trap #{4} (0x3F).", x, y, e.Level, r.Trigger, r.Trap);
            return r;
        }

        static void AddString(TrapAction a, StringsEditor strings, int str)
        {
            if (a.Kind != RecipeTrap.Message || a.TextIndex >= 0) return;
            int n = strings.Add(TextBlock, a.Text);
            if (n != str) throw new InvalidOperationException("Block 9 changed while the recipe ran: the text trap prints string " + str + ", but the message went to " + n + ".");
        }

        // ---- 4. a trigger in an object

        public static int TriggerItem(ObjectEvent when) { return when == ObjectEvent.PickUp ? PickUpTrigger : when == ObjectEvent.Look ? LookTrigger : UseTrigger; }

        /// <summary>
        /// A pick up, use or look trigger inside an existing object that runs a trap on the
        /// object's square (checkTrap: the first trap-class object in its list). A single object
        /// that is is_quant is made to hold a list. One undo step.
        /// </summary>
        public static RecipeResult ObjectTrigger(LevelEditor e, int index, ObjectEvent when, TrapAction a, TriggerOptions t = null, StringsEditor strings = null)
        {
            var s = e.Session;
            var m = e.Model;
            CheckHost(m, index, "the object");
            var host = m.Objects[index];
            int x = host.TileX, y = host.TileY;
            CheckAction(e, a, x, y, index, strings);
            t = For(a, t);
            int str = a.Kind == RecipeTrap.Message ? (a.TextIndex >= 0 ? a.TextIndex : strings.Get(TextBlock).Count) : -1;
            var r = new RecipeResult { Level = e.Level, StringIndex = str };
            e.Batch("Recipe: " + when.ToString().ToLowerInvariant() + " trigger", () =>
            {
                MakeHolder(e, index);
                int tx, ty;
                int trap = MakeTrap(e, r, a, x, y, str, out tx, out ty);
                // a look trigger's z is the Search difficulty (UseTrigger: skill_check when z > 0): 0, none
                int trig = Add(e, r, TriggerSpec(TriggerItem(when), tx, ty, t, 0), x, y, index);
                SetLink(e, trig, trap);
                CountTriggers(e, trap);
                Checked(e);
                r.Trigger = trig; r.Trap = trap;
            });
            AddString(a, strings, str);
            string verb = when == ObjectEvent.PickUp ? "Picking up" : when == ObjectEvent.Look ? "Looking at" : "Using";
            r.Summary = string.Format("{0} #{1} {2} {3}: {4} trigger #{5}, {6} #{7}.", verb, index, host.Name, Describe(s, a, str), when == ObjectEvent.PickUp ? "pick up" : when.ToString().ToLowerInvariant(), r.Trigger, Name(s, e.Model.Objects[r.Trap].Item), r.Trap);
            return r;
        }

        // ---- 5. wandering monsters

        /// <summary>
        /// A wandering-monster spawner on (x, y): a create-object trap with flags 0 (no trigger
        /// counts itself on it) holding the creature as its template, which the game runs by
        /// itself (event/TRIGGER.C DoWanderingMonsters, both games). Its quality q gives the chance
        /// (63 − q) in 63 a run (UseTrap TRAP_CREATE_OBJECT). In UW2 the adventurer (0x7F) is a
        /// random monster chosen each run; UW1 copies its template as it is, so it needs a real
        /// creature. The template's quality and owner are its home square, as the shipped
        /// spawners have them. One undo step; r.Trap is the spawner, there is no trigger.
        /// </summary>
        public static RecipeResult Spawner(LevelEditor e, SpawnerOptions o)
        {
            var s = e.Session;
            var m = e.Model;
            Need(o != null, "Choose the square and the creature.");
            Square(m, o.X, o.Y, "The spawner");
            Need(o.Item >= 0x40 && o.Item < 0x80, "A spawner makes a creature (0x40-0x7F).");
            Need(s.IsUw2 || o.Item != Triggers.Adventurer, "Ultima Underworld has no random monster: choose a creature.");
            Need(o.Chance > 0 && o.Chance <= 100, "The chance must be more than 0 and at most 100 %.");
            int q = Triggers.SpawnQuality(o.Chance);
            Need(q < 63, "The chance is too low: the trap would never make anything.");
            var first = Triggers.FirstCreateTrapOn(m, o.X, o.Y);
            Need(first == null, string.Format("({0}, {1}) already holds create-object trap #{2}: the game runs only the first one on a square, so a spawner there would never run.", o.X, o.Y, first == null ? 0 : first.Index));
            var near = Triggers.Spawners(m).Where(x => Math.Abs(x.TileX - o.X) <= 4 && Math.Abs(x.TileY - o.Y) <= 4).ToList();
            var r = new RecipeResult { Level = e.Level };
            e.Batch("Recipe: wandering monsters", () =>
            {
                var t = TrapSpec(CreateTrap, e.FloorZ(o.X, o.Y));
                t.Quality = q;
                int trap = Add(e, r, t, o.X, o.Y);
                var tmp = ObjectSpec.Default(o.Item, s.Kind);
                tmp.Z = e.FloorZ(o.X, o.Y, tmp.FineX, tmp.FineY);
                tmp.Quality = o.X; tmp.Owner = o.Y;              // its home, as the shipped templates have it
                if (o.Hostile) tmp.Attitude = 0;
                int made = Add(e, r, tmp, o.X, o.Y, trap);
                OffActiveList(e, made);
                Checked(e);
                r.Trap = trap;
            });
            string what = s.IsUw2 && o.Item == Triggers.Adventurer ? "a random monster" : Name(s, o.Item);
            r.Summary = string.Format("Spawner #{0} on ({1}, {2}) makes {3}, {4:0} % each run (quality {5}).{6}", r.Trap, o.X, o.Y, what, Triggers.SpawnChance(q), q,
                near.Count > 0 ? " Spawners within 4 squares (" + string.Join(", ", near.Select(x => "#" + x.Index)) + ") hold each other back: a creature made by one stops the others." : "");
            return r;
        }
    }
}
