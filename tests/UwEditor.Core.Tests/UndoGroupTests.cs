using System;
using System.Linq;
using UwEditor.Core.Editing;
using UwEditor.Core.Randomizer;
using Xunit;

namespace UwEditor.Core.Tests
{
    /// <summary>
    /// A change made on several levels at once (LevelEdits.Together: a randomizer's apply) undoes
    /// and redoes as one from any of its levels, so Edit ▸ Undo on one level cannot leave items
    /// moved on the others (some twice, some gone).
    /// </summary>
    public class UndoGroupTests
    {
        [Fact]
        public void One_undo_takes_back_a_change_made_on_several_levels_on_all_of_them()
        {
            string g = GameData.Game("UW"); if (g == null) return;
            var s = GameSession.Open(g);
            var edits = new LevelEdits(s);
            var before = s.LevelSlots().Where(x => x.Present).ToDictionary(x => x.Level, x => (byte[])edits.Model(x.Level).Block.Data.Clone());
            var opt = new KeyShuffleOptions();
            var r = KeyShuffle.Run(s, edits, opt);
            Assert.True(r.Success);
            KeyShuffle.Apply(r, edits);
            var levels = r.After.Keys.OrderBy(x => x).ToList();
            Assert.True(levels.Count > 3);
            var after = levels.ToDictionary(l => l, l => (byte[])edits.For(l).Data.Clone());
            // Edit ▸ Undo on one of the levels (the one shown): every level of the apply goes back
            string refused;
            var undone = edits.Undo(levels[1], out refused);
            Assert.Null(refused);
            Assert.Equal(levels, undone);
            foreach (var kv in before) Assert.True(edits.Model(kv.Key).Block.Data.AsSpan().SequenceEqual(kv.Value), "level " + kv.Key + " is back");
            // and Redo on another brings all of them again
            Assert.Equal(levels, edits.Redo(levels[0], out refused));
            foreach (var l in levels) Assert.True(edits.For(l).Data.AsSpan().SequenceEqual(after[l]));
            // a level changed after the apply: the undo is refused, nothing moves
            var e = edits.For(levels[2]);
            e.SetTileField(30, 30, "Wall texture", (e.TileField(30, 30, "Wall texture") + 1) % 60);
            Assert.Empty(edits.Undo(levels[0], out refused));
            Assert.Contains("level " + levels[2] + " changed again after it", refused);
            foreach (var l in levels.Where(l => l != levels[2])) Assert.True(edits.For(l).Data.AsSpan().SequenceEqual(after[l]));
            // undo that change first, then the apply goes back whole
            Assert.Equal(new[] { levels[2] }, edits.Undo(levels[2], out refused));
            Assert.Equal(levels, edits.Undo(levels[2], out refused));
            foreach (var kv in before) Assert.True(edits.Model(kv.Key).Block.Data.AsSpan().SequenceEqual(kv.Value));
            // the levels are as they were, so a new scatter works as the first did
            var again = KeyShuffle.Run(s, edits, opt);
            Assert.True(again.Success, string.Join("; ", again.Failures));
            Assert.Equal(r.UsedSeed, again.UsedSeed);
            // a change on one level alone undoes alone, as before
            var one = edits.For(1);
            one.SetTileField(31, 31, "Wall texture", (one.TileField(31, 31, "Wall texture") + 1) % 60);
            Assert.Equal(0, one.UndoGroup);
            Assert.Equal(new[] { 1 }, edits.Undo(1, out refused));
        }

        [Fact]
        public void The_history_steps_through_a_change_of_several_levels_whole()
        {
            string g = GameData.Game("UW"); if (g == null) return;
            var s = GameSession.Open(g);
            var edits = new LevelEdits(s);
            byte[] b1 = (byte[])edits.For(1).Data.Clone(), b2 = (byte[])edits.For(2).Data.Clone();
            edits.Together(() =>
            {
                foreach (int l in new[] { 1, 2 })
                {
                    var e = edits.For(l);
                    e.SetTileField(20, 20, "Wall texture", (e.TileField(20, 20, "Wall texture") + 2) % 60);
                    e.SetTileField(21, 20, "Wall texture", (e.TileField(21, 20, "Wall texture") + 2) % 60);   // two steps on a level, one group
                }
            });
            Assert.Equal(edits.For(1).UndoGroup, edits.For(2).UndoGroup);
            Assert.NotEqual(0, edits.For(1).UndoGroup);
            string refused;
            Assert.Equal(1, edits.Step(2, 1, out refused));
            Assert.True(edits.For(1).Data.AsSpan().SequenceEqual(b1));
            Assert.True(edits.For(2).Data.AsSpan().SequenceEqual(b2));
            Assert.False(edits.For(1).CanUndo);
            Assert.Equal(1, edits.Step(1, -1, out refused));
            Assert.False(edits.For(1).Data.AsSpan().SequenceEqual(b1));
            Assert.False(edits.For(2).Data.AsSpan().SequenceEqual(b2));
        }
    }
}
