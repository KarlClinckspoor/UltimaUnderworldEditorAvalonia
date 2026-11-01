using System.IO;
using System.Linq;
using UltimateEditor.Games;
using UwEditor.Core.Editing;
using Xunit;

namespace UwEditor.Core.Tests
{
    public class NewLevelsTests
    {
        [Theory]
        [InlineData(GameKind.UW1)]
        [InlineData(GameKind.UW2)]
        public void An_empty_block_passes_the_games_list_check(GameKind kind)
        {
            foreach (bool room in new[] { false, true })
            {
                var b = new LevelBlock(NewLevels.EmptyBlock(kind, room), kind);
                bool ok;
                string report = b.Check(out ok);
                Assert.True(ok, report);
                Assert.True(b.MagicOk);
                Assert.Equal(253, b.MobileTop);
                Assert.Equal(767, b.StaticTop);
                Assert.Equal(0, b.ActiveCount);
                Assert.Equal(room ? 1 : 0, b.TileType(32, 32));
            }
        }

        [Fact]
        public void A_slot_made_a_level_saves_reopens_and_takes_objects()
        {
            string uw2 = GameData.Game("UW2");
            if (uw2 == null) return;
            using (var c = new EditingTests.Copy(uw2))
            {
                var s = GameSession.Open(c.Folder);
                int slot = s.LevelSlots().First(x => !x.Present).Level;
                var edits = new LevelEdits(s);
                Assert.False(edits.Model(slot).Present);
                var tex = NewLevels.TextureMap(s, slot);
                Assert.Equal(0x86, tex.Length);
                var e = edits.CreateLevel(slot, NewLevels.EmptyBlock(s.Kind, true), tex, "Make a level");
                Assert.True(e.Model.Present);
                bool ok;
                e.Check(out ok);
                Assert.True(ok);
                // undo: an empty slot again, and saving writes nothing for it
                e.Undo();
                Assert.False(e.Model.Present);
                var before = File.ReadAllBytes(s.LevArkPath);
                Assert.Equal(before, edits.ToBytes());
                e.Redo();
                Assert.Equal(new[] { slot }, edits.Save());

                var s2 = GameSession.Open(c.Folder);
                Assert.True(s2.LevelSlots()[slot - 1].Present);
                var m = s2.LoadLevel(slot);
                Assert.True(m.Present);
                Assert.True(m.CheckOk, m.CheckReport);
                Assert.Equal(tex, m.TextureBytes);
                var e2 = new LevelEdits(s2).For(slot);
                int added = e2.AddObject(new ObjectSpec { Item = 0x0A0 }, 32, 32);   // a static object in the room
                Assert.True(added >= LevelBlock.Mobiles);
                e2.Check(out ok);
                Assert.True(ok);
            }
        }

        [Theory]
        [InlineData("UW", false)]
        [InlineData("UW2", true)]
        public void Clearing_a_level_passes_the_check_and_undo_restores(string game, bool tiles)
        {
            string folder = GameData.Game(game);
            if (folder == null) return;
            using (var c = new EditingTests.Copy(folder))
            {
                var s = GameSession.Open(c.Folder);
                var edits = new LevelEdits(s);
                var e = edits.For(1);
                var old = (byte[])e.Data.Clone();
                e.EditBytes("Clear the level", (d, t) => { NewLevels.ResetObjects(d, s.Kind); if (tiles) NewLevels.MakeAllSolid(d); });
                bool ok;
                string report = e.Check(out ok);
                Assert.True(ok, report);
                Assert.Equal(0, e.Model.UsedObjectCount);
                Assert.Equal(tiles ? 0 : s.LoadLevel(1).OpenTileCount, e.Model.OpenTileCount);
                edits.Save();
                var m = GameSession.Open(c.Folder).LoadLevel(1);
                Assert.True(m.CheckOk, m.CheckReport);
                if (s.Kind == GameKind.UW1)
                {
                    // no animation is left on a freed object
                    var anim = GameSession.Open(c.Folder).LevArk.Read(s.Profile.OverlayBlock(1));
                    Assert.Equal(0, anim[0] | anim[1] << 8);
                }
                e.Undo();
                Assert.Equal(old, e.Data);
            }
        }
    }
}
