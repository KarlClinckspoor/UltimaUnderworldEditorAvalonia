using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UltimateEditor.Games;
using UltimateEditor.Uw1;

namespace UwEditor.Core.Editing
{
    /// <summary>
    /// The edits of one level: its block and texture map bytes, changed in place, with undo and
    /// redo by snapshots. Objects are added and freed the way the game does (obj/OBJECTS.C
    /// Obj_Alloc, Obj_Free, Obj_Add): from the free stacks (mobile: count − 1 at 0x7C02, indexes
    /// from 0x7300; static: at 0x7C04, from 0x74FC), a new mobile joining the active list (bytes
    /// from 0x7AFC, count at 0x7C00), at the head of its tile's list.
    /// </summary>
    public sealed class LevelEditor
    {
        public GameSession Session { get; private set; }
        public int Level { get; private set; }
        byte[] data, tex, anim;
        LevelModel model;
        readonly List<Tuple<string, byte[], byte[], byte[]>> undo = new List<Tuple<string, byte[], byte[], byte[]>>(), redo = new List<Tuple<string, byte[], byte[], byte[]>>();
        public const int UndoDepth = 200;
        public bool Dirty { get; private set; }
        /// <summary>Raised after every change, undo and redo.</summary>
        public event Action Changed;
        int batchDepth;
        bool batchChanged;

        public LevelEditor(GameSession s, int level)
        {
            Session = s; Level = level;
            var m = s.LoadLevel(level);
            data = (byte[])m.Block.Data.Clone();
            tex = m.TextureBytes;
            // UW1 keeps the running animations (EFFECT.C animlist, 64 × 6 bytes) in block level + 8; UW2 inside the level block
            int ab = s.Profile.OverlayBlock(level);
            if (ab >= 0 && s.LevArk.Has(ab))
            {
                var a = s.LevArk.Read(ab);
                if (a != null && a.Length >= AnimBlockLength) { anim = (byte[])a.Clone(); animOriginal = (byte[])a.Clone(); }
            }
        }

        /// <summary>The animation list's block (MAX_ANIMS 64 entries of 6 bytes: object link word, frames left, x, y).</summary>
        public const int AnimBlockLength = 0x180;
        byte[] animOriginal;
        /// <summary>UW1's animation block as edited (null when the level has none), and whether it changed.</summary>
        public byte[] AnimBytes { get { return anim; } }
        public bool AnimChanged { get { return anim != null && animOriginal != null && !anim.AsSpan().SequenceEqual(animOriginal); } }

        public byte[] Data { get { return data; } }
        public byte[] TextureBytes { get { return tex; } }
        LevelBlock B { get { return new LevelBlock(data, Session.Kind); } }

        /// <summary>The level as it now is (rebuilt after each change).</summary>
        internal void Reread() { model = null; }

        public LevelModel Model
        {
            get { return model ?? (model = LevelModel.FromBytes(Session, Level, data, tex)); }
        }

        /// <summary>After saving: the level is clean, its history kept.</summary>
        internal void MarkSaved() { Dirty = false; if (anim != null) animOriginal = (byte[])anim.Clone(); }

        /// <summary>The undo steps, oldest first, and the redo steps, the next one last.</summary>
        public IReadOnlyList<string> UndoLabels { get { return undo.Select(u => u.Item1).ToList(); } }
        public IReadOnlyList<string> RedoLabels { get { return redo.Select(r => r.Item1).ToList(); } }

        /// <summary>Goes back (steps &gt; 0 undoes that many) or forward (steps &lt; 0 redoes) through the history.</summary>
        public void Step(int steps)
        {
            for (; steps > 0 && CanUndo; steps--) Undo();
            for (; steps < 0 && CanRedo; steps++) Redo();
        }

        public bool CanUndo { get { return undo.Count > 0; } }
        public bool CanRedo { get { return redo.Count > 0; } }
        public string UndoLabel { get { return undo.Count > 0 ? undo[undo.Count - 1].Item1 : null; } }
        public string RedoLabel { get { return redo.Count > 0 ? redo[redo.Count - 1].Item1 : null; } }

        /// <summary>Starts a group of changes that undo as one (a painting stroke).</summary>
        public void BeginBatch(string label)
        {
            if (batchDepth++ == 0) { Snapshot(label); batchChanged = false; }
        }

        public void EndBatch()
        {
            if (batchDepth == 0) return;
            if (--batchDepth == 0 && !batchChanged && undo.Count > 0) undo.RemoveAt(undo.Count - 1);
        }

        void Snapshot(string label)
        {
            undo.Add(Tuple.Create(label, (byte[])data.Clone(), tex == null ? null : (byte[])tex.Clone(), anim == null ? null : (byte[])anim.Clone()));
            if (undo.Count > UndoDepth) undo.RemoveAt(0);
        }

        /// <summary>An undo or redo while a stroke is open closes the stroke first (an empty one leaves no step).</summary>
        void CloseBatch()
        {
            if (batchDepth == 0) return;
            batchDepth = 0;
            if (!batchChanged && undo.Count > 0) undo.RemoveAt(undo.Count - 1);
        }

        /// <summary>Runs one change: a snapshot first (unless in a batch), then the change, then Changed.</summary>
        void Change(string label, Action a)
        {
            if (batchDepth == 0) Snapshot(label);
            byte[] before = batchDepth == 0 ? null : (byte[])data.Clone(), animBefore = batchDepth == 0 || anim == null ? null : (byte[])anim.Clone();
            try { a(); }
            catch
            {
                if (batchDepth == 0) { var u = undo[undo.Count - 1]; data = u.Item2; tex = u.Item3; anim = u.Item4; undo.RemoveAt(undo.Count - 1); }
                else if (before != null) { data = before; if (animBefore != null) anim = animBefore; }
                model = null;
                throw;
            }
            if (batchDepth > 0) batchChanged = true;
            redo.Clear();                                       // only a change that happened drops the redo steps
            Done();
        }

        void Done()
        {
            Dirty = true;
            model = null;
            Changed?.Invoke();
        }

        /// <summary>Replaces the whole level (a checkpoint restored), as one undoable change.</summary>
        public void Replace(byte[] newData, byte[] newTex, string label)
        {
            if (newData == null || newData.Length != data.Length) throw new InvalidOperationException("The level block has another size (" + (newData == null ? 0 : newData.Length) + " bytes, not " + data.Length + ").");
            Change(label, () => { data = (byte[])newData.Clone(); if (newTex != null) tex = (byte[])newTex.Clone(); });
        }

        /// <summary>
        /// A change written straight on the level block bytes (and the texture map, which may be
        /// null): one undoable step, the model rebuilt after. For tools that know the format
        /// (repairs, recipes, new levels); a change that throws is rolled back.
        /// </summary>
        public void EditBytes(string label, Action<byte[], byte[]> change)
        {
            Change(label, () => change(data, tex));
        }

        public void Undo()
        {
            CloseBatch();
            if (undo.Count == 0) return;
            var u = undo[undo.Count - 1];
            undo.RemoveAt(undo.Count - 1);
            redo.Add(Tuple.Create(u.Item1, data, tex, anim));
            data = u.Item2; tex = u.Item3; anim = u.Item4;
            Done();
        }

        public void Redo()
        {
            CloseBatch();
            if (redo.Count == 0) return;
            var r = redo[redo.Count - 1];
            redo.RemoveAt(redo.Count - 1);
            undo.Add(Tuple.Create(r.Item1, data, tex, anim));
            data = r.Item2; tex = r.Item3; anim = r.Item4;
            Done();
        }

        // ---- tiles

        static int TileAt(int x, int y) { return LevelBlock.TilesAt + (y * 64 + x) * 4; }

        public int TileField(int x, int y, string field) { return Uw1Schemas.Tile[field].Get(data, TileAt(x, y)); }

        /// <summary>Sets a field of a tile's record (Type, Floor height, Light, Floor texture, No magic, Door, Wall texture).</summary>
        public void SetTileField(int x, int y, string field, int value)
        {
            var f = Uw1Schemas.Tile[field];
            if (f.Name == "First object") throw new InvalidOperationException("The first object is the tile's object list; add, move or delete objects instead.");
            if (value < f.Min || value > f.Max) throw new ArgumentOutOfRangeException(field, field + " is " + f.Min + "-" + f.Max + ".");
            if (field == "Floor texture" && value >= Model.FloorMap.Length) throw new ArgumentOutOfRangeException(field, "The level has floor textures 0-" + (Model.FloorMap.Length - 1) + ".");
            if (field == "Wall texture" && value >= Model.WallMap.Length) throw new ArgumentOutOfRangeException(field, "The level has wall textures 0-" + (Model.WallMap.Length - 1) + ".");
            if (field == "Type" && value > 9) throw new ArgumentOutOfRangeException(field, "Tile types are 0-9.");
            if (f.Get(data, TileAt(x, y)) == value) return;
            Change(field + " of (" + x + ", " + y + ")", () => f.Set(data, value, TileAt(x, y)));
        }

        /// <summary>The light flag only (bit 8 of the tile, tested as the player moves); bit 9 is left alone.</summary>
        public void SetLightBit(int x, int y, bool on)
        {
            int v = TileField(x, y, "Light");
            int nv = on ? v | 1 : v & ~1;
            if (nv != v) SetTileField(x, y, "Light", nv);
        }

        /// <summary>
        /// Raises (or lowers) a tile's floor by steps, clamped to 0-15, and moves the objects lying
        /// on it with it (z, 8 a step) so they stay on the floor.
        /// </summary>
        public bool RaiseFloor(int x, int y, int steps)
        {
            int h = TileField(x, y, "Floor height"), nh = Math.Max(0, Math.Min(15, h + steps));
            if (nh == h) return false;
            int d = (nh - h) * 8;
            Change("Floor height of (" + x + ", " + y + ")", () =>
            {
                Uw1Schemas.Tile["Floor height"].Set(data, nh, TileAt(x, y));
                foreach (var o in Model.TileObjects(x, y))
                {
                    int at = LevelBlock.ObjectOffset(o.Index) + 2;
                    int w = data[at] | data[at + 1] << 8;
                    int z = Math.Max(0, Math.Min(127, (w & 0x7F) + d));
                    w = (w & ~0x7F) | z;
                    data[at] = (byte)w; data[at + 1] = (byte)(w >> 8);
                }
            });
            return true;
        }

        // ---- objects

        /// <summary>Fields the editor does not let you type, because they hold the object lists.</summary>
        public static bool IsListField(string name) { return name == "Next" || name == "First object"; }

        /// <summary>Writes whole object records (the monster swap, and putting them back) as one undoable change.</summary>
        public void RewriteRecords(IList<KeyValuePair<int, byte[]>> records, string label)
        {
            if (records.Count == 0) return;
            Change(label, () =>
            {
                foreach (var r in records)
                {
                    int at = LevelBlock.ObjectOffset(r.Key);
                    int size = r.Key < LevelBlock.Mobiles ? LevelBlock.MobileSize : LevelBlock.StaticSize;
                    Array.Copy(r.Value, 0, data, at, Math.Min(size, r.Value.Length));
                }
            });
        }

        /// <summary>
        /// Rewrites the item, flags, quality and link words of several objects in one undoable
        /// change (the item shuffle): for each object index, its new item id, quality, flags 9-11,
        /// enchanted and is-quantity bits, and link. Position, next and owner are kept. Objects whose
        /// link is a list (containers, critters) keep their link.
        /// </summary>
        public void RewriteItems(IList<ItemRewrite> changes, string label)
        {
            if (changes.Count == 0) return;
            Change(label, () =>
            {
                foreach (var c in changes)
                {
                    int at = LevelBlock.ObjectOffset(c.Index);
                    int w0 = data[at] | data[at + 1] << 8, w2 = data[at + 4] | data[at + 5] << 8, w3 = data[at + 6] | data[at + 7] << 8;
                    bool listLink = (w0 & 0x8000) == 0 && (w3 >> 6) != 0;
                    // tenacious and invisible kept; a list keeps its is-quantity bit (clear) and its link,
                    // unless the rewrite moves whole objects (Exact: a swap, or putting objects back)
                    if (c.Exact) listLink = false;
                    w0 = (w0 & 0x6000) | (c.Item & 0x1FF) | (c.Flags & 7) << 9 | (c.Enchanted ? 0x1000 : 0) | (listLink ? (w0 & 0x8000) : (c.IsQuantity ? 0x8000 : 0));
                    w2 = (w2 & ~0x3F) | (c.Quality & 0x3F);
                    if (!listLink) w3 = (w3 & 0x3F) | (c.Link & 0x3FF) << 6;
                    if (c.Exact) w3 = (w3 & ~0x3F) | (c.Owner & 0x3F);
                    data[at] = (byte)w0; data[at + 1] = (byte)(w0 >> 8);
                    data[at + 4] = (byte)w2; data[at + 5] = (byte)(w2 >> 8);
                    data[at + 6] = (byte)w3; data[at + 7] = (byte)(w3 >> 8);
                }
            });
        }

        public void SetObjectField(int index, FieldDef f, int value)
        {
            if (IsListField(f.Name)) throw new InvalidOperationException("Next is the object list; move or delete the object instead.");
            var o = Model.Objects[index];
            if (f.Name == "Link" && !o.IsQuantity && value != o.Link) throw new InvalidOperationException("This object's link is its contents (a list); set Is quantity first, or add and delete objects inside it.");
            if (f.Name == "Is quantity" && value != 0 && !o.IsQuantity && o.Children.Count > 0) throw new InvalidOperationException("Empty it first: its link holds " + o.Children.Count + " objects.");
            if (value < f.Min || value > f.Max) throw new ArgumentOutOfRangeException(f.Name, f.Name + " is " + f.Min + "-" + f.Max + ".");
            if (f.Name == "Item id")
            {
                // a 27-byte mobile record and an 8-byte static one are not interchangeable (obj/OBJECTS.C)
                bool critter = value >= 0x40 && value < 0x80;
                if (critter && index >= LevelBlock.Mobiles) throw new InvalidOperationException("A creature needs a mobile record (#1-#255): delete this object and add the creature instead.");
                if (!critter && o.IsCritter) throw new InvalidOperationException("A creature's record stays a creature (it is on the active list): delete it and add the object instead.");
            }
            if (f.Get(data, LevelBlock.ObjectOffset(index)) == value) return;
            Change(f.Name + " of #" + index, () =>
            {
                f.Set(data, value, LevelBlock.ObjectOffset(index));
                // the link changes meaning both ways: a list head becomes a quantity, a quantity must not become a list head
                if (f.Name == "Is quantity" && o.Link != 0 && (value != 0) != o.IsQuantity) Uw1Schemas.StaticObject["Link"].Set(data, 0, LevelBlock.ObjectOffset(index));
            });
        }

        int Alloc(bool mobile)
        {
            var b = B;
            if (mobile)
            {
                int top = b.MobileTop;
                if (top < 0) throw new InvalidOperationException("No free mobile object left in this level (256 at most).");
                int i = b.MobileFree(top);
                b.Put16(LevelBlock.MobTopAt, top - 1);
                // active_critter: a new mobile joins the active list
                int n = b.ActiveCount;
                if (n < 0x100 + 4) { data[LevelBlock.ActiveAt + n] = (byte)i; b.Put16(LevelBlock.ActiveCountAt, n + 1); }
                return i;
            }
            int st = b.StaticTop;
            if (st < 0) throw new InvalidOperationException("No free static object left in this level (768 at most).");
            int s = b.StaticFree(st);
            b.Put16(LevelBlock.StaticTopAt, st - 1);
            return s;
        }

        void FreeIndex(int i)
        {
            RemoveAnimation(i);
            var b = B;
            int size = i < LevelBlock.Mobiles ? LevelBlock.MobileSize : LevelBlock.StaticSize;
            Array.Clear(data, LevelBlock.ObjectOffset(i), size);
            if (i < LevelBlock.Mobiles)
            {
                int top = b.MobileTop + 1;
                b.Put16(LevelBlock.MobTopAt, top);
                b.Put16(LevelBlock.MobFreeAt + 2 * top, i);
                // free_critter: off the active list
                int n = b.ActiveCount;
                for (int k = 0; k < n; k++)
                    if (data[LevelBlock.ActiveAt + k] == i)
                    {
                        data[LevelBlock.ActiveAt + k] = data[LevelBlock.ActiveAt + n - 1];
                        data[LevelBlock.ActiveAt + n - 1] = 0;
                        b.Put16(LevelBlock.ActiveCountAt, n - 1);
                        break;
                    }
            }
            else
            {
                int top = b.StaticTop + 1;
                b.Put16(LevelBlock.StaticTopAt, top);
                b.Put16(LevelBlock.StaticFreeAt + 2 * top, i);
            }
        }

        int GetW(int at) { return data[at] | data[at + 1] << 8; }
        void PutW(int at, int v) { data[at] = (byte)v; data[at + 1] = (byte)(v >> 8); }
        int NextOf(int i) { return GetW(LevelBlock.ObjectOffset(i) + 4) >> 6; }
        void SetNext(int i, int n) { int at = LevelBlock.ObjectOffset(i) + 4; PutW(at, (GetW(at) & 0x3F) | n << 6); }
        int LinkOf(int i) { return GetW(LevelBlock.ObjectOffset(i) + 6) >> 6; }
        void SetLink(int i, int n) { int at = LevelBlock.ObjectOffset(i) + 6; PutW(at, (GetW(at) & 0x3F) | n << 6); }
        int HeadOf(int x, int y) { return GetW(TileAt(x, y) + 2) >> 6; }
        void SetHead(int x, int y, int n) { int at = TileAt(x, y) + 2; PutW(at, (GetW(at) & 0x3F) | n << 6); }
        bool IsQuant(int i) { return (GetW(LevelBlock.ObjectOffset(i)) & 0x8000) != 0; }

        // ---- running animations (EFFECT.C animlist): they name objects by index and tile

        /// <summary>The animation list (UW1's own block, UW2's inside the level block at 0x7C08) and its start.</summary>
        byte[] AnimList(out int at)
        {
            if (Session.IsUw2) { at = LevelBlock.Uw2OverlaysAt; return data.Length >= at + AnimBlockLength ? data : null; }
            at = 0; return anim;
        }

        static int AnimIndex(byte[] a, int at, int k) { return (a[at + 6 * k] | a[at + 6 * k + 1] << 8) >> 6; }

        /// <summary>A freed object's animation goes (rem_anim_from_list: the last entry fills its place, a zero index ends the list).</summary>
        void RemoveAnimation(int index)
        {
            int at;
            var a = AnimList(out at);
            if (a == null) return;
            int n = 0;
            while (n < 64 && AnimIndex(a, at, n) != 0) n++;
            for (int k = 0; k < n; k++)
                if (AnimIndex(a, at, k) == index)
                {
                    if (k != n - 1) Array.Copy(a, at + 6 * (n - 1), a, at + 6 * k, 6);
                    Array.Clear(a, at + 6 * (n - 1), 6);
                    return;
                }
        }

        /// <summary>A moved object's animation follows it to its new tile (the list keeps x, y).</summary>
        void MoveAnimation(int index, int x, int y)
        {
            int at;
            var a = AnimList(out at);
            if (a == null) return;
            for (int k = 0; k < 64 && AnimIndex(a, at, k) != 0; k++)
                if (AnimIndex(a, at, k) == index) { a[at + 6 * k + 4] = (byte)x; a[at + 6 * k + 5] = (byte)y; }
        }

        /// <summary>Takes an object off the list it is on (its tile's, or its container's).</summary>
        void Unlink(ObjectInfo o)
        {
            int i = o.Index;
            if (o.Parent >= 0)
            {
                int c = o.Parent;
                if (LinkOf(c) == i) { SetLink(c, NextOf(i)); SetNext(i, 0); return; }
                for (int p = LinkOf(c), guard = 0; p > 0 && guard++ < 1100; p = NextOf(p))
                    if (NextOf(p) == i) { SetNext(p, NextOf(i)); SetNext(i, 0); return; }
            }
            else if (o.TileX >= 0)
            {
                if (HeadOf(o.TileX, o.TileY) == i) { SetHead(o.TileX, o.TileY, NextOf(i)); SetNext(i, 0); return; }
                for (int p = HeadOf(o.TileX, o.TileY), guard = 0; p > 0 && guard++ < 1100; p = NextOf(p))
                    if (NextOf(p) == i) { SetNext(p, NextOf(i)); SetNext(i, 0); return; }
            }
            throw new InvalidOperationException("#" + i + " is on no list the editor can find.");
        }

        /// <summary>The z of the floor in the middle of a tile (8 a step; +4 on a slope).</summary>
        public int FloorZ(int x, int y, int fineX = 3, int fineY = 3)
        {
            var t = Model.Tiles[x, y];
            double h = Model.FloorAt(x, y, (fineX + 0.5) / 8, (fineY + 0.5) / 8) / LevelGeometry.Step;
            return Math.Max(0, Math.Min(127, (int)Math.Round(h * 8)));
        }

        /// <summary>
        /// Adds an object (and what goes inside it): on a tile (at the head of its list, at the
        /// floor unless spec.Z says otherwise) or inside a container (container ≥ 0). Returns its index.
        /// </summary>
        public int AddObject(ObjectSpec spec, int x, int y, int container = -1)
        {
            if (container < 0 && Model.Tiles[x, y].IsSolid) throw new InvalidOperationException("(" + x + ", " + y + ") is solid rock: objects go on open tiles.");
            if (container >= 0 && IsQuant(container)) throw new InvalidOperationException("#" + container + " is not a container: its link is a quantity or special value.");
            int result = -1;
            Change("Add " + spec.Describe(Session.Names), () => result = Place(spec, x, y, container));
            return result;
        }

        int Place(ObjectSpec spec, int x, int y, int container)
        {
            int i = Alloc(spec.IsMobile);
            int at = LevelBlock.ObjectOffset(i);
            var rec = new byte[8];
            int z = spec.Z ?? (container >= 0 ? 0 : FloorZ(x, y, spec.FineX, spec.FineY));
            spec.Write(rec, z);
            Array.Copy(rec, 0, data, at, 8);
            if (spec.IsMobile) InitMobile(i, spec, x, y);
            if (container >= 0) { SetNext(i, LinkOf(container)); SetLink(container, i); }
            else { SetNext(i, HeadOf(x, y)); SetHead(x, y, i); }
            foreach (var c in spec.Contents) Place(c, x, y, i);
            return i;
        }

        /// <summary>A new critter's mobile bytes: copied from one of the same kind in the level if there is one, else hit points from OBJECTS.DAT; home on its tile.</summary>
        void InitMobile(int i, ObjectSpec spec, int x, int y)
        {
            int at = LevelBlock.ObjectOffset(i);
            var like = Model.Objects.FirstOrDefault(o => o.IsMobile && !o.Free && o.TileX >= 0 && o.Item == spec.Item && o.Index != i);
            // a free record keeps whatever the last creature there left (shipped levels have them): start clean
            Array.Clear(data, at + 8, LevelBlock.MobileSize - 8);
            var S = Uw1Schemas.MobileObject;
            if (like != null)
            {
                // the same kind's settings (speed, terrain, heading and the like), but none of its state
                Array.Copy(like.Record, 8, data, at + 8, LevelBlock.MobileSize - 8);
                foreach (var n in new[] { "Goal target", "Summoned", "Talked to", "Destination X", "Destination Y", "Damage taken", "Last hit", "Inventory made" })
                    if (S.Fields.Any(f => f.Name == n)) S[n].Set(data, 0, at);
            }
            S["HP"].Set(data, spec.Hp > 0 ? Math.Min(255, spec.Hp) : (like != null ? like.Record[8] : AverageHp(spec.Item)), at);
            S["Attitude"].Set(data, spec.Attitude & 3, at);
            S["Home X"].Set(data, x, at);
            S["Home Y"].Set(data, y, at);
            if (spec.Goal > 0) S["Goal"].Set(data, spec.Goal & 15, at);
            data[at + 0x1A] = (byte)spec.Whoami;
        }

        int AverageHp(int item)
        {
            string p = GameProfile.Find(Session.DataFolder, "OBJECTS.DAT");
            if (p == null) return 10;
            var d = File.ReadAllBytes(p);
            int at = 0x132 + 0x30 * (item - 0x40) + 4;
            return at < d.Length ? Math.Max(1, (int)d[at]) : 10;
        }

        /// <summary>Deletes an object and everything inside it, back to the free stacks.</summary>
        public void DeleteObject(int index)
        {
            var o = Model.Objects[index];
            if (o.Free || (o.TileX < 0 && o.Parent < 0)) throw new InvalidOperationException("#" + index + " is not in the level.");
            Change("Delete #" + index + " " + o.Name, () =>
            {
                Unlink(o);
                FreeTree(index, 0);
            });
        }

        void FreeTree(int i, int depth)
        {
            if (!IsQuant(i) && depth < 64)
                for (int c = LinkOf(i), guard = 0; c > 0 && guard++ < 1100;)
                {
                    int next = NextOf(c);
                    FreeTree(c, depth + 1);
                    c = next;
                }
            FreeIndex(i);
        }

        /// <summary>Moves an object lying on a tile to another tile (keeping its height above the floor).</summary>
        public void MoveObject(int index, int x, int y, int? fineX = null, int? fineY = null)
        {
            var o = Model.Objects[index];
            if (!o.OnTile) throw new InvalidOperationException("Only objects lying on a tile can be moved to another (this one is inside #" + o.Parent + ").");
            if (Model.Tiles[x, y].IsSolid) throw new InvalidOperationException("(" + x + ", " + y + ") is solid rock.");
            Change("Move #" + index + " to (" + x + ", " + y + ")", () =>
            {
                int above = o.Z - FloorZ(o.TileX, o.TileY, o.FineX, o.FineY);
                Unlink(o);
                SetNext(index, HeadOf(x, y));
                SetHead(x, y, index);
                var S = Uw1Schemas.StaticObject;
                int at = LevelBlock.ObjectOffset(index);
                int fx = fineX ?? o.FineX, fy = fineY ?? o.FineY;
                S["Fine X"].Set(data, fx, at);
                S["Fine Y"].Set(data, fy, at);
                S["Z"].Set(data, Math.Max(0, Math.Min(127, FloorZ(x, y, fx, fy) + Math.Max(0, above))), at);
                if (o.IsMobile) { Uw1Schemas.MobileObject["Home X"].Set(data, x, at); Uw1Schemas.MobileObject["Home Y"].Set(data, y, at); }
                MoveAnimation(index, x, y);
            });
        }

        /// <summary>Whether an object can go inside another: the container's link is a list (not a quantity or special value), and it is not the object itself or inside it.</summary>
        public bool CanPutInside(int index, int container)
        {
            if (index <= 0 || container <= 0 || index == container) return false;
            var m = Model;
            if (m.Objects[index].Free || m.Objects[container].Free || IsQuant(container)) return false;
            if (m.Objects[index].IsMobile && !m.Objects[container].IsCritter && !m.Objects[container].IsTrapOrTrigger && m.Objects[index].IsCritter) return false;   // a creature in a bag
            for (int up = container, g = 0; up >= 0 && g < 1024; up = m.Objects[up].Parent, g++) if (up == index) return false;
            return true;
        }

        /// <summary>Puts an object (from a tile or another container) inside a container, a creature's inventory, or a trigger's chain (at the head of its list): one undoable change.</summary>
        public void PutInside(int index, int container)
        {
            if (!CanPutInside(index, container)) throw new InvalidOperationException(Model.Objects[container].Name + " cannot hold " + Model.Objects[index].Name + ".");
            var o = Model.Objects[index];
            Change("Put #" + index + " " + o.Name + " in #" + container, () =>
            {
                Unlink(o);
                SetNext(index, LinkOf(container));
                SetLink(container, index);
                MoveAnimation(index, Model.Objects[container].TileX, Model.Objects[container].TileY);
            });
        }

        // ---- copy and paste

        /// <summary>
        /// The objects as a clip: each one's record with everything inside it (contents, a wand's
        /// spell, a trigger's traps), and where it lies relative to the first one.
        /// </summary>
        public ObjectClip Copy(IEnumerable<int> indexes)
        {
            var m = Model;
            var list = indexes.Distinct().Where(i => i > 0 && i < m.Objects.Length && !m.Objects[i].Free && (m.Objects[i].OnTile || m.Objects[i].Parent >= 0)).ToList();
            // an object inside another one copied too goes along with it, not twice
            var set = new HashSet<int>(list);
            list = list.Where(i => { for (int up = m.Objects[i].Parent, g = 0; up >= 0 && g < 1024; up = m.Objects[up].Parent, g++) if (set.Contains(up)) return false; return true; }).ToList();
            var clip = new ObjectClip { Kind = Session.Kind, FromLevel = Level };
            if (list.Count == 0) return clip;
            var first = m.Objects[list[0]];
            int ax = first.OnTile ? first.TileX : -1, ay = first.OnTile ? first.TileY : -1;
            foreach (int i in list)
            {
                var o = m.Objects[i];
                var n = Node(i, 0);
                if (o.OnTile && ax >= 0) { n.Dx = o.TileX - ax; n.Dy = o.TileY - ay; n.Above = o.Z - FloorZ(o.TileX, o.TileY, o.FineX, o.FineY); }
                clip.Roots.Add(n);
            }
            return clip;
        }

        ClipNode Node(int i, int depth)
        {
            int size = i < LevelBlock.Mobiles ? LevelBlock.MobileSize : LevelBlock.StaticSize;
            var n = new ClipNode { Mobile = i < LevelBlock.Mobiles, Record = new byte[size] };
            Array.Copy(data, LevelBlock.ObjectOffset(i), n.Record, 0, size);
            if (!IsQuant(i) && depth < 64)
                for (int c = LinkOf(i), guard = 0; c > 0 && guard++ < 1100; c = NextOf(c)) n.Children.Add(Node(c, depth + 1));
            return n;
        }

        /// <summary>
        /// Puts a clip's objects in the level as one undoable change: on tile (x, y) and the tiles
        /// the others had relative to the first, at the same height above the floor (or inside a
        /// container). Objects that would land on rock or off the map are left out. Returns the new
        /// objects' indexes, the first one first.
        /// </summary>
        public List<int> Paste(ObjectClip clip, int x, int y, int container = -1)
        {
            if (clip == null || clip.Roots.Count == 0) throw new InvalidOperationException("Nothing to paste: copy objects first.");
            if (clip.Kind != Session.Kind) throw new InvalidOperationException("These objects come from the other game.");
            if (container >= 0 && IsQuant(container)) throw new InvalidOperationException("#" + container + " is not a container.");
            int mob = clip.Count(true), sta = clip.Count(false), freeM = B.MobileTop + 1, freeS = B.StaticTop + 1;
            if (mob > freeM || sta > freeS) throw new InvalidOperationException(string.Format("Not enough free objects: the clip needs {0} mobile and {1} static, the level has {2} and {3}.", mob, sta, freeM, freeS));
            var made = new List<int>();
            Change("Paste " + clip.Describe(Session.Names), () =>
            {
                foreach (var r in clip.Roots)
                {
                    int tx = x + r.Dx, ty = y + r.Dy;
                    if (container < 0 && (tx < 0 || ty < 0 || tx > 63 || ty > 63 || Model.Tiles[tx, ty].IsSolid)) continue;
                    made.Add(PasteNode(r, tx, ty, container, true));
                }
                if (made.Count == 0) throw new InvalidOperationException("Every object would land on rock there.");
            });
            return made;
        }

        int PasteNode(ClipNode n, int x, int y, int container, bool root)
        {
            int i = Alloc(n.Mobile);
            int at = LevelBlock.ObjectOffset(i);
            Array.Copy(n.Record, 0, data, at, Math.Min(n.Record.Length, n.Mobile ? LevelBlock.MobileSize : LevelBlock.StaticSize));
            SetNext(i, 0);
            if (!IsQuant(i)) SetLink(i, 0);
            var S = Uw1Schemas.StaticObject;
            if (root && container < 0)
            {
                int fx = S["Fine X"].Get(data, at), fy = S["Fine Y"].Get(data, at);
                S["Z"].Set(data, Math.Max(0, Math.Min(127, FloorZ(x, y, fx, fy) + Math.Max(0, n.Above))), at);
            }
            if (n.Mobile) { Uw1Schemas.MobileObject["Home X"].Set(data, x, at); Uw1Schemas.MobileObject["Home Y"].Set(data, y, at); }
            // the contents in their order: each put at the head, last first
            if (!IsQuant(i)) for (int k = n.Children.Count - 1; k >= 0; k--) PasteNode(n.Children[k], x, y, i, false);
            if (container >= 0) { SetNext(i, LinkOf(container)); SetLink(container, i); }
            else if (root) { SetNext(i, HeadOf(x, y)); SetHead(x, y, i); }
            return i;
        }

        /// <summary>Moves an object to a tile from wherever it is: another tile (as MoveObject), or out of a container, a creature or a trigger's chain (onto the floor).</summary>
        public void MoveObjectAnywhere(int index, int x, int y, int? fineX = null, int? fineY = null)
        {
            var o = Model.Objects[index];
            if (o.OnTile) { MoveObject(index, x, y, fineX, fineY); return; }
            if (o.Parent < 0) throw new InvalidOperationException("#" + index + " is not in the level.");
            if (Model.Tiles[x, y].IsSolid) throw new InvalidOperationException("(" + x + ", " + y + ") is solid rock.");
            Change("Take #" + index + " out to (" + x + ", " + y + ")", () =>
            {
                Unlink(o);
                SetNext(index, HeadOf(x, y));
                SetHead(x, y, index);
                var S = Uw1Schemas.StaticObject;
                int at = LevelBlock.ObjectOffset(index);
                int fx = fineX ?? 3, fy = fineY ?? 3;
                S["Fine X"].Set(data, fx, at);
                S["Fine Y"].Set(data, fy, at);
                S["Z"].Set(data, FloorZ(x, y, fx, fy), at);
                if (o.IsMobile) { Uw1Schemas.MobileObject["Home X"].Set(data, x, at); Uw1Schemas.MobileObject["Home Y"].Set(data, y, at); }
            });
        }

        /// <summary>The game's object-list check on the edited level.</summary>
        public string Check(out bool ok) { return B.Check(out ok); }
    }

    /// <summary>The new item words of one object (LevelEditor.RewriteItems).</summary>
    public struct ItemRewrite
    {
        public int Index, Item, Quality, Flags, Link, Owner;
        public bool Enchanted, IsQuantity;
        /// <summary>Write is-quantity, the link and the owner as given, even over a list link (a whole object moved here).</summary>
        public bool Exact;
    }

    /// <summary>The edited levels of a game, saved into its LEV.ARK together.</summary>
    public sealed class LevelEdits
    {
        public GameSession Session { get; private set; }
        readonly Dictionary<int, LevelEditor> editors = new Dictionary<int, LevelEditor>();
        public event Action<LevelEditor> Changed;

        public LevelEdits(GameSession s) { Session = s; }

        readonly Dictionary<int, Automap> automaps = new Dictionary<int, Automap>();
        /// <summary>A level's map or notes changed.</summary>
        public event Action<Automap> AutomapChanged;

        /// <summary>The player's map and notes of a level, as edited.</summary>
        public Automap AutomapFor(int level)
        {
            Automap a;
            if (!automaps.TryGetValue(level, out a)) automaps[level] = a = Automap.Load(Session, level);
            return a;
        }

        /// <summary>Tells the editor (and the save button) that an automap changed.</summary>
        public void AutomapEdited(Automap a) { AutomapChanged?.Invoke(a); }

        public LevelEditor For(int level)
        {
            LevelEditor e;
            if (!editors.TryGetValue(level, out e))
            {
                editors[level] = e = new LevelEditor(Session, level);
                e.Changed += () => Changed?.Invoke(e);
            }
            return e;
        }

        public LevelEditor Existing(int level) { LevelEditor e; return editors.TryGetValue(level, out e) ? e : null; }
        public bool HasChanges { get { return editors.Values.Any(e => e.Dirty) || automaps.Values.Any(a => a.Dirty); } }
        public IEnumerable<LevelEditor> Editors { get { return editors.Values; } }
        public IEnumerable<Automap> Automaps { get { return automaps.Values; } }
        public IEnumerable<int> DirtyLevels { get { return editors.Values.Where(e => e.Dirty).Select(e => e.Level).Union(automaps.Values.Where(a => a.Dirty).Select(a => a.Level)).OrderBy(l => l); } }

        /// <summary>The level as edited, or as the archive has it.</summary>
        public LevelModel Model(int level) { var e = Existing(level); return e != null ? e.Model : Session.LoadLevel(level); }

        /// <summary>The names changed (STRINGS.PAK saved): the editors' models are built again.</summary>
        public void Reread() { foreach (var e in editors.Values) e.Reread(); }

        /// <summary>LEV.ARK with the edited levels in it.</summary>
        public byte[] ToBytes()
        {
            var ark = Archive.Open(Session.LevArk.ToBytes(), Session.Kind);
            foreach (var e in editors.Values.Where(x => x.Dirty))
            {
                int block = Session.Profile.LevelBlock(e.Level);
                // a slot made a level and then undone holds no level again: nothing to write
                if (!ark.Has(block) && !new LevelBlock(e.Data, Session.Kind).MagicOk) continue;
                ark.Write(block, e.Data);
                if (e.TextureBytes != null) ark.Write(Session.Profile.TextureBlock(e.Level), e.TextureBytes);
                // the editor's own list first (deletes and moves keep it), then anything a raw change (a cleared level) left pointing at free records
                if (e.AnimChanged) ark.Write(Session.Profile.OverlayBlock(e.Level), e.AnimBytes);
                if (Session.Kind == GameKind.UW1) PruneUw1Animations(ark, e);
            }
            foreach (var a in automaps.Values.Where(x => x.Dirty))
            {
                ark.Write(Automap.MapBlock(Session.Kind, a.Level), a.Map);
                ark.Write(Automap.NotesBlock(Session.Kind, a.Level), Automap.WriteNotes(a.Notes));
            }
            return ark.ToBytes();
        }

        /// <summary>
        /// UW1 keeps a level's animation list in its own block, level + 8 (obj/EFFECT.C
        /// Anim_Load), outside what the editor edits. Entries whose object is now free (deleted,
        /// or the level cleared) are taken out, so the game does not animate a free record;
        /// the list stays as it was when none are.
        /// </summary>
        void PruneUw1Animations(Archive ark, LevelEditor e)
        {
            int ab = Session.Profile.OverlayBlock(e.Level);
            if (ab < 0 || !ark.Has(ab)) return;
            var list = ark.Read(ab);
            if (list == null || list.Length != 0x180) return;
            int[] free, reached;
            new LevelBlock(e.Data, Session.Kind).Counts(out free, out reached);
            int n = 0;
            while (n < 64 && ((list[n * 6] | list[n * 6 + 1] << 8) >> 6) != 0) n++;
            var keep = new List<int>();
            for (int i = 0; i < n; i++)
            {
                int obj = (list[i * 6] | list[i * 6 + 1] << 8) >> 6;
                if (obj < LevelBlock.Objects && free[obj] == 0) keep.Add(i);
            }
            if (keep.Count == n) return;
            var nl = (byte[])list.Clone();
            for (int k = 0; k < keep.Count; k++) Array.Copy(list, keep[k] * 6, nl, k * 6, 6);
            Array.Clear(nl, keep.Count * 6, (n - keep.Count) * 6);       // a zero object ends the list (Anim_Save)
            ark.Write(ab, nl);
        }

        /// <summary>
        /// Puts a whole new level into a slot (an empty one, say, whose model is not Present):
        /// the block and texture map, as one undoable step of that level's editor, saved with
        /// the levels. Returns the editor.
        /// </summary>
        public LevelEditor CreateLevel(int level, byte[] data, byte[] tex, string label)
        {
            var e = For(level);
            e.Replace(data, tex, label);
            return e;
        }

        /// <summary>Writes LEV.ARK (the old file kept as LEV.ARK.BKP). Returns the levels written.</summary>
        public List<int> Save()
        {
            var levels = DirtyLevels.ToList();
            if (levels.Count == 0) return levels;
            var bytes = ToBytes();
            Conversations.SafeFile.Write(Session.LevArkPath, bytes);
            Session.ReplaceLevArk(bytes);
            foreach (var e in editors.Values) e.MarkSaved();
            foreach (var a in automaps.Values) a.MarkSaved();
            return levels;
        }
    }
}
