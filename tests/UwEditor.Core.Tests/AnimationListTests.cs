using System;
using System.Linq;
using UwEditor.Core.Editing;
using Xunit;

namespace UwEditor.Core.Tests
{
    /// <summary>The running animations (EFFECT.C animlist) follow their objects when they are deleted or moved.</summary>
    public class AnimationListTests
    {
        static int Index(byte[] a, int at, int k) { return (a[at + 6 * k] | a[at + 6 * k + 1] << 8) >> 6; }

        [Theory]
        [InlineData("UW")]
        [InlineData("UW2")]
        public void Deleting_an_animated_object_drops_its_animation_and_moving_one_moves_it(string game)
        {
            string g = GameData.Game(game); if (g == null) return;
            using (var c = new EditingTests.Copy(g))
            {
                var s = GameSession.Open(c.Folder);
                var edits = new LevelEdits(s);
                foreach (var slot in s.LevelSlots().Where(x => x.Present))
                {
                    var e = edits.For(slot.Level);
                    byte[] a = s.IsUw2 ? e.Data : e.AnimBytes;
                    int at = s.IsUw2 ? UltimateEditor.Games.LevelBlock.Uw2OverlaysAt : 0;
                    if (a == null) continue;
                    int n = 0;
                    while (n < 64 && Index(a, at, n) != 0) n++;
                    if (n < 2) continue;
                    // the first animated object that lies on a tile
                    int k = Enumerable.Range(0, n).FirstOrDefault(i => e.Model.Objects[Index(a, at, i)].OnTile && !e.Model.Objects[Index(a, at, i)].Free);
                    int idx = Index(a, at, k);
                    var o = e.Model.Objects[idx];
                    var free = Enumerable.Range(0, 64 * 64).Select(i => (x: i % 64, y: i / 64)).First(t => !e.Model.Tiles[t.x, t.y].IsSolid && (t.x != o.TileX || t.y != o.TileY));
                    e.MoveObject(idx, free.x, free.y);
                    a = s.IsUw2 ? e.Data : e.AnimBytes;
                    Assert.Equal(free.x, a[at + 6 * k + 4]);
                    Assert.Equal(free.y, a[at + 6 * k + 5]);
                    e.DeleteObject(idx);
                    a = s.IsUw2 ? e.Data : e.AnimBytes;
                    Assert.DoesNotContain(Enumerable.Range(0, 64).TakeWhile(i => Index(a, at, i) != 0), i => Index(a, at, i) == idx);
                    Assert.Equal(n - 1, Enumerable.Range(0, 64).TakeWhile(i => Index(a, at, i) != 0).Count());
                    bool ok; e.Check(out ok); Assert.True(ok);
                    edits.Save();
                    var s2 = GameSession.Open(c.Folder);
                    var back = s.IsUw2 ? s2.LevArk.Read(s2.Profile.LevelBlock(slot.Level)) : s2.LevArk.Read(s2.Profile.OverlayBlock(slot.Level));
                    Assert.Equal(n - 1, Enumerable.Range(0, 64).TakeWhile(i => Index(back, at, i) != 0).Count());
                    e.Undo(); e.Undo();
                    a = s.IsUw2 ? e.Data : e.AnimBytes;
                    Assert.Equal(n, Enumerable.Range(0, 64).TakeWhile(i => Index(a, at, i) != 0).Count());
                    return;
                }
                Assert.Fail("no level with two animations");
            }
        }
    }
}
