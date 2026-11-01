using System;
using System.Collections.Generic;
using System.Linq;
using UltimateEditor.Games;
using UltimateEditor.Uw1;
using UwEditor.Core.Editing;
using Xunit;
using Xunit.Abstractions;

namespace UwEditor.Core.Tests
{
    /// <summary>
    /// Random edits, as a person makes them in any order: tiles, adding, deleting, moving,
    /// putting inside, copying and pasting, changing fields, undo and redo. After each edit the
    /// game's object-list check must pass and no new list error may appear; at the end the level
    /// must come back the same from the archive it is saved into, undoing everything must give
    /// the original bytes back and redoing everything the edited ones. Each run has a seed; a
    /// failure names it and the last edits.
    /// </summary>
    public class RandomEditTests
    {
        readonly ITestOutputHelper output;
        public RandomEditTests(ITestOutputHelper output) { this.output = output; }

        public const int Steps = 150;   // below LevelEditor.UndoDepth, so every step can be undone

        [Theory]
        [InlineData("UW", 1, 1)]
        [InlineData("UW", 2, 2)]
        [InlineData("UW", 3, 3)]
        [InlineData("UW", 5, 4)]
        [InlineData("UW", 7, 5)]
        [InlineData("UW", 9, 6)]
        [InlineData("UW2", 1, 7)]
        [InlineData("UW2", 2, 8)]
        [InlineData("UW2", 9, 9)]
        [InlineData("UW2", 17, 10)]
        [InlineData("UW2", 25, 11)]
        public void Random_edits_keep_the_level_sound_and_undo_exactly(string game, int level, int seed)
        {
            string g = GameData.Game(game); if (g == null) return;
            var s = GameSession.Open(g);
            if (!s.LevelSlots().Any(x => x.Level == level && x.Present)) return;
            var run = new RandomEdits(s, level, seed);
            try { run.Run(Steps); }
            catch (Exception ex)
            {
                Assert.Fail(string.Format("{0} level {1}, seed {2}, step {3}: {4}\nLast edits:\n{5}", game, level, seed, run.Done, ex.Message, string.Join("\n", run.Log.Skip(Math.Max(0, run.Log.Count - 15)))));
            }
            output.WriteLine("{0} level {1} seed {2}: {3} edits, {4} refused; kinds: {5}", game, level, seed, run.Done, run.Refused,
                string.Join(", ", run.Kinds.OrderBy(k => k.Key).Select(k => k.Key + " " + k.Value)));
        }

        /// <summary>A few hundred more seeds when UW_RANDOM_EDITS=N is set (N runs each game).</summary>
        [Fact]
        public void Many_seeds_when_asked()
        {
            int n;
            if (!int.TryParse(Environment.GetEnvironmentVariable("UW_RANDOM_EDITS"), out n) || n <= 0) return;
            foreach (var game in new[] { "UW", "UW2" })
            {
                string g = GameData.Game(game); if (g == null) continue;
                var s = GameSession.Open(g);
                var levels = s.LevelSlots().Where(x => x.Present).Select(x => x.Level).ToList();
                for (int seed = 1000; seed < 1000 + n; seed++)
                {
                    int level = levels[seed % levels.Count];
                    var run = new RandomEdits(s, level, seed);
                    try { run.Run(Steps); }
                    catch (Exception ex)
                    {
                        Assert.Fail(string.Format("{0} level {1}, seed {2}, step {3}: {4}\nLast edits:\n{5}", game, level, seed, run.Done, ex.Message, string.Join("\n", run.Log.Skip(Math.Max(0, run.Log.Count - 15)))));
                    }
                }
            }
        }
    }

    /// <summary>One run of random edits on one level.</summary>
    public sealed class RandomEdits
    {
        readonly GameSession s;
        readonly int level;
        readonly Random r;
        readonly LevelEdits edits;
        readonly LevelEditor e;
        public readonly List<string> Log = new List<string>();
        public readonly Dictionary<string, int> Kinds = new Dictionary<string, int>();
        public int Done, Refused;
        Dictionary<string, int> baseline;

        public RandomEdits(GameSession s, int level, int seed)
        {
            this.s = s; this.level = level;
            r = new Random(seed);
            edits = new LevelEdits(s);
            e = edits.For(level);
        }

        /// <summary>List errors (the ones that break the game's lists), counted by check.</summary>
        Dictionary<string, int> ListErrors()
        {
            return LevelFixes.Find(e).Where(f => f.Severity == FixSeverity.ListError || f.Check == LevelFixes.ActiveList || f.Check == LevelFixes.PlayerInList || f.Check == LevelFixes.Overlays)
                .GroupBy(f => f.Check.Title).ToDictionary(k => k.Key, k => k.Count());
        }

        void Sound(string after)
        {
            bool ok;
            string why = e.Check(out ok);
            if (!ok) throw new Exception("the object lists fail the game's check after " + after + ": " + why);
            foreach (var kv in ListErrors())
            {
                int was;
                baseline.TryGetValue(kv.Key, out was);
                if (kv.Value > was) throw new Exception("new findings of \"" + kv.Key + "\" after " + after + " (" + was + " → " + kv.Value + "): " +
                    string.Join("; ", LevelFixes.Find(e).Where(f => f.Check.Title == kv.Key).Take(3).Select(f => f.Text)));
            }
        }

        public void Run(int steps)
        {
            var original = (byte[])e.Data.Clone();
            var originalTex = e.TextureBytes == null ? null : (byte[])e.TextureBytes.Clone();
            var originalAnim = e.AnimBytes == null ? null : (byte[])e.AnimBytes.Clone();
            baseline = ListErrors();
            Sound("nothing");
            for (int i = 0; i < steps; i++)
            {
                string what = OneEdit();
                Log.Add(i + ": " + what);
                Done = i + 1;
                Sound(what);
            }
            // saved into the archive and read back: the same level
            var ark = Archive.Open(edits.ToBytes(), s.Kind);
            var back = ark.Read(s.Profile.LevelBlock(level));
            if (!back.SequenceEqual(e.Data)) throw new Exception("the level read back from the saved archive differs from the edited one");
            var model = LevelModel.FromBytes(s, level, back, e.TextureBytes);
            var now = e.Model;
            for (int k = 0; k < now.Objects.Length; k++)
                if (now.Objects[k].Free != model.Objects[k].Free || now.Objects[k].Parent != model.Objects[k].Parent || now.Objects[k].TileX != model.Objects[k].TileX)
                    throw new Exception("object #" + k + " is not the same after reading the saved archive");
            // everything undone: the original bytes; everything redone: the edited ones
            var edited = (byte[])e.Data.Clone();
            var editedAnim = e.AnimBytes == null ? null : (byte[])e.AnimBytes.Clone();
            int undos = e.UndoLabels.Count;
            for (int k = 0; k < undos; k++) e.Undo();
            if (!e.Data.SequenceEqual(original)) throw new Exception("undoing all " + undos + " steps does not give the original level back (first difference at byte " + FirstDiff(e.Data, original) + ")");
            if (originalTex != null && !e.TextureBytes.SequenceEqual(originalTex)) throw new Exception("undoing everything does not give the original texture map back");
            if (originalAnim != null && !e.AnimBytes.SequenceEqual(originalAnim)) throw new Exception("undoing everything does not give the original animation list back");
            for (int k = 0; k < undos; k++) e.Redo();
            if (!e.Data.SequenceEqual(edited)) throw new Exception("redoing all steps does not give the edited level back (first difference at byte " + FirstDiff(e.Data, edited) + ")");
            if (editedAnim != null && !e.AnimBytes.SequenceEqual(editedAnim)) throw new Exception("redoing everything does not give the edited animation list back");
        }

        static int FirstDiff(byte[] a, byte[] b)
        {
            for (int i = 0; i < Math.Min(a.Length, b.Length); i++) if (a[i] != b[i]) return i;
            return Math.Min(a.Length, b.Length);
        }

        T Pick<T>(IList<T> list) { return list[r.Next(list.Count)]; }

        List<(int X, int Y)> OpenTiles()
        {
            var m = e.Model;
            var l = new List<(int, int)>();
            for (int y = 1; y < 63; y++) for (int x = 1; x < 63; x++) if (!m.Tiles[x, y].IsSolid) l.Add((x, y));
            return l;
        }

        List<ObjectInfo> Placed(Func<ObjectInfo, bool> where = null)
        {
            // the player's own record (#1) and record #0 are never edited by hand
            return e.Model.Objects.Where(o => !o.Free && o.Index > 1 && (o.OnTile || o.Parent >= 0) && (where == null || where(o))).ToList();
        }

        static readonly int[] AddItems = { 0x000, 0x004, 0x010, 0x020, 0x030, 0x080, 0x081, 0x0A0, 0x0B0, 0x0B7, 0x0C0, 0x0E0, 0x100, 0x130, 0x138 };

        ObjectSpec RandomSpec()
        {
            var m = e.Model;
            if (r.Next(6) == 0)
            {
                // a creature of a kind the level already has
                var kinds = m.Objects.Where(o => !o.Free && o.IsCritter && o.TileX >= 0).Select(o => o.Item).Distinct().ToList();
                if (kinds.Count > 0) return new ObjectSpec { Item = Pick(kinds), Attitude = r.Next(4), IsQuantity = false, Link = 0 };
            }
            int item = Pick(AddItems);
            var spec = new ObjectSpec { Item = item, Quality = r.Next(64), Owner = r.Next(4) == 0 ? r.Next(64) : 0, Heading = r.Next(8), FineX = r.Next(8), FineY = r.Next(8) };
            if (item == 0x0A0) spec.Link = 1 + r.Next(200);
            if ((item & 0x1F0) == 0x080 && r.Next(2) == 0)
            {
                spec.IsQuantity = false; spec.Link = 0;
                for (int k = r.Next(1, 4); k > 0; k--) spec.Contents.Add(new ObjectSpec { Item = Pick(new[] { 0x0A0, 0x0B0, 0x000, 0x130 }), Quality = r.Next(64) });
            }
            return spec;
        }

        static readonly string[] TileFields = { "Type", "Floor height", "Floor texture", "Wall texture", "Light" };
        static readonly string[] ObjectFields = { "Quality", "Owner", "Heading", "Fine X", "Fine Y", "Z", "Invisible", "Tenacious" };

        string OneEdit()
        {
            string kind;
            int roll = r.Next(100);
            if (roll < 14) kind = "tile";
            else if (roll < 30) kind = "add";
            else if (roll < 40) kind = "delete";
            else if (roll < 50) kind = "move";
            else if (roll < 57) kind = "move anywhere";
            else if (roll < 64) kind = "put inside";
            else if (roll < 76) kind = "field";
            else if (roll < 81) kind = "item";
            else if (roll < 87) kind = "copy paste";
            else if (roll < 91) kind = "batch";
            else if (roll < 96) kind = "undo";
            else kind = "redo";
            int n; Kinds.TryGetValue(kind, out n); Kinds[kind] = n + 1;
            var before = (byte[])e.Data.Clone();
            int undoCount = e.UndoLabels.Count;
            try { return kind + ": " + Do(kind); }
            catch (Exception ex) when (ex is InvalidOperationException || ex is ArgumentOutOfRangeException || ex is ArgumentException)
            {
                // a refusal is a correct answer to an edit that makes no sense; the level must be untouched
                Refused++;
                if (!e.Data.SequenceEqual(before) || e.UndoLabels.Count != undoCount) throw new Exception(kind + " was refused (" + ex.Message + ") but changed the level");
                return kind + " refused: " + ex.Message;
            }
        }

        string Do(string kind)
        {
            var m = e.Model;
            switch (kind)
            {
                case "tile":
                {
                    var t = (X: 1 + r.Next(62), Y: 1 + r.Next(62));
                    string f = Pick(TileFields);
                    int v = f == "Type" ? r.Next(10) : f == "Floor height" ? r.Next(16) : f == "Floor texture" ? r.Next(m.FloorMap.Length) : f == "Wall texture" ? r.Next(m.WallMap.Length) : r.Next(2);
                    e.SetTileField(t.X, t.Y, f, v);
                    return string.Format("{0} of ({1}, {2}) = {3}", f, t.X, t.Y, v);
                }
                case "add":
                {
                    var spec = RandomSpec();
                    var containers = Placed(o => !o.IsQuantity && !o.IsTrapOrTrigger && (o.Item & 0x1F0) == 0x080);
                    if (!spec.IsMobile && containers.Count > 0 && r.Next(3) == 0)
                    {
                        var c = Pick(containers);
                        int i = e.AddObject(spec, c.TileX, c.TileY, c.Index);
                        return string.Format("0x{0:X3} inside #{1} → #{2}", spec.Item, c.Index, i);
                    }
                    var t = Pick(OpenTiles());
                    int j = e.AddObject(spec, t.X, t.Y);
                    return string.Format("0x{0:X3} on ({1}, {2}) → #{3}", spec.Item, t.X, t.Y, j);
                }
                case "delete":
                {
                    var l = Placed();
                    if (l.Count == 0) return "nothing to delete";
                    var o = Pick(l);
                    e.DeleteObject(o.Index);
                    return string.Format("#{0} 0x{1:X3}", o.Index, o.Item);
                }
                case "move":
                {
                    var l = Placed(o => o.OnTile);
                    if (l.Count == 0) return "nothing on a tile";
                    var o = Pick(l); var t = Pick(OpenTiles());
                    e.MoveObject(o.Index, t.X, t.Y, r.Next(8), r.Next(8));
                    return string.Format("#{0} to ({1}, {2})", o.Index, t.X, t.Y);
                }
                case "move anywhere":
                {
                    var l = Placed();
                    if (l.Count == 0) return "nothing";
                    var o = Pick(l); var t = Pick(OpenTiles());
                    e.MoveObjectAnywhere(o.Index, t.X, t.Y);
                    return string.Format("#{0} (in #{1}) to ({2}, {3})", o.Index, o.Parent, t.X, t.Y);
                }
                case "put inside":
                {
                    var l = Placed();
                    var c = Placed(o => !o.IsQuantity && (o.IsCritter || (o.Item & 0x1F0) == 0x080 || o.IsTrapOrTrigger));
                    if (l.Count == 0 || c.Count == 0) return "nothing";
                    var a = Pick(l); var b = Pick(c);
                    if (!e.CanPutInside(a.Index, b.Index)) return string.Format("#{0} cannot go in #{1}", a.Index, b.Index);
                    e.PutInside(a.Index, b.Index);
                    return string.Format("#{0} into #{1}", a.Index, b.Index);
                }
                case "field":
                {
                    var l = Placed();
                    if (l.Count == 0) return "nothing";
                    var o = Pick(l);
                    var f = (o.IsMobile ? Uw1Schemas.MobileObject : Uw1Schemas.StaticObject)[Pick(ObjectFields)];
                    int v = (int)(f.Min + r.Next((int)(f.Max - f.Min + 1)));
                    e.SetObjectField(o.Index, f, v);
                    return string.Format("{0} of #{1} = {2}", f.Name, o.Index, v);
                }
                case "item":
                {
                    // another item of the same class (a dagger for a sword, a fish for an apple)
                    var l = Placed(o => !o.IsMobile && !o.IsTrapOrTrigger && o.Item < 0x180 && (o.Item < 0x40 || o.Item >= 0x80));
                    if (l.Count == 0) return "nothing";
                    var o = Pick(l);
                    int item = (o.Item & ~0xF) | r.Next(16);
                    e.SetObjectField(o.Index, Uw1Schemas.StaticObject["Item id"], item);
                    return string.Format("#{0} 0x{1:X3} → 0x{2:X3}", o.Index, o.Item, item);
                }
                case "copy paste":
                {
                    var l = Placed(o => o.OnTile);
                    if (l.Count == 0) return "nothing";
                    var pick = Enumerable.Range(0, 1 + r.Next(3)).Select(_ => Pick(l).Index).Distinct().ToList();
                    var clip = e.Copy(pick);
                    var t = Pick(OpenTiles());
                    var made = e.Paste(clip, t.X, t.Y);
                    return string.Format("#{0} to ({1}, {2}) → {3}", string.Join(", #", pick), t.X, t.Y, string.Join(", ", made));
                }
                case "batch":
                {
                    var said = new List<string>();
                    e.Batch("random batch", () =>
                    {
                        for (int k = 0; k < 3; k++)
                        {
                            var t = Pick(OpenTiles());
                            int i = e.AddObject(RandomSpec(), t.X, t.Y);
                            said.Add("#" + i);
                        }
                        var placed = Placed(o => o.OnTile);
                        if (placed.Count > 0) { var o = Pick(placed); e.DeleteObject(o.Index); said.Add("-#" + o.Index); }
                    });
                    return string.Join(" ", said);
                }
                case "undo":
                    if (e.UndoLabels.Count == 0) return "nothing to undo";
                    e.Undo(); return "";
                case "redo":
                    if (e.RedoLabels.Count == 0) return "nothing to redo";
                    e.Redo(); return "";
            }
            return "?";
        }
    }
}
