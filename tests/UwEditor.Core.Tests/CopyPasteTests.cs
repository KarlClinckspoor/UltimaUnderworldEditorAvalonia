using System;
using System.Collections.Generic;
using System.Linq;
using UwEditor.Core.Editing;
using Xunit;

namespace UwEditor.Core.Tests
{
    public class CopyPasteTests
    {
        static bool Ok(LevelEditor e) { bool ok; e.Check(out ok); return ok; }

        static int[] Items(LevelModel m, int i)
        {
            var l = new List<int>();
            Action<int> walk = null;
            walk = k => { l.Add(m.Objects[k].Item); foreach (var c in m.Objects[k].Children) walk(c); };
            walk(i);
            return l.ToArray();
        }

        [Theory]
        [InlineData("UW")]
        [InlineData("UW2")]
        public void Containers_and_trigger_chains_paste_whole_on_any_level_and_undo_exactly(string game)
        {
            string g = GameData.Game(game); if (g == null) return;
            var s = GameSession.Open(g);
            var edits = new LevelEdits(s);
            var e = edits.For(1);
            var m = e.Model;
            var bag = m.Objects.First(o => !o.Free && o.OnTile && !o.IsMobile && !o.IsTrapOrTrigger && o.Children.Count >= 2);
            var trig = m.Objects.First(o => !o.Free && o.OnTile && o.IsTrapOrTrigger && o.Children.Count >= 1);
            var clip = e.Copy(new[] { bag.Index, trig.Index });
            Assert.Equal(2, clip.Roots.Count);
            var open = Enumerable.Range(0, 64 * 64).Select(i => (x: i % 64, y: i / 64)).Where(t => !m.Tiles[t.x, t.y].IsSolid).Skip(40).First();
            var before = (byte[])e.Data.Clone();
            var made = e.Paste(clip, open.x, open.y);
            Assert.True(Ok(e));
            Assert.Equal(Items(m, bag.Index), Items(e.Model, made[0]));
            Assert.Equal(open.x, e.Model.Objects[made[0]].TileX);
            e.Undo();
            Assert.Equal(before, e.Data);
            // another level of the same game
            int other = s.LevelSlots().First(x => x.Present && x.Level != 1).Level;
            var e2 = edits.For(other);
            var t2 = Enumerable.Range(0, 64 * 64).Select(i => (x: i % 64, y: i / 64)).First(t => !e2.Model.Tiles[t.x, t.y].IsSolid);
            var made2 = e2.Paste(clip, t2.x, t2.y);
            Assert.True(Ok(e2));
            Assert.Equal(Items(m, bag.Index), Items(e2.Model, made2[0]));
            // into a container
            var made3 = e.Paste(e.Copy(new[] { bag.Children[0] }), 0, 0, bag.Index);
            Assert.Contains(made3[0], e.Model.Objects[bag.Index].Children);
            Assert.True(Ok(e));
        }

        [Fact]
        public void A_clip_from_the_other_game_or_onto_rock_is_refused()
        {
            string g1 = GameData.Game("UW"), g2 = GameData.Game("UW2"); if (g1 == null || g2 == null) return;
            var e1 = new LevelEdits(GameSession.Open(g1)).For(1);
            var e2 = new LevelEdits(GameSession.Open(g2)).For(1);
            var o = e1.Model.Objects.First(x => !x.Free && x.OnTile && !x.IsMobile);
            var clip = e1.Copy(new[] { o.Index });
            Assert.Throws<InvalidOperationException>(() => e2.Paste(clip, 30, 30));
            var rock = Enumerable.Range(0, 64 * 64).Select(i => (x: i % 64, y: i / 64)).First(t => e1.Model.Tiles[t.x, t.y].IsSolid);
            Assert.Throws<InvalidOperationException>(() => e1.Paste(clip, rock.x, rock.y));
            Assert.False(e1.Dirty);
        }
    }
}
