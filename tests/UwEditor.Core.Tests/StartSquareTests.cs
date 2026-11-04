using System.Linq;
using UwEditor.Core.Randomizer;
using Xunit;

namespace UwEditor.Core.Tests
{
    /// <summary>The randomizer's start: the new-game square in the executable (moved in the editor), or with the option the game's own (UW1).</summary>
    public class StartSquareTests
    {
        [Fact]
        public void The_zones_start_where_the_executable_says_unless_the_shipped_square_is_asked_for()
        {
            string g = GameData.Game("UW"); if (g == null) return;
            using (var c = new EditingTests.Copy(g))
            {
                var s = GameSession.Open(c.Folder);
                var shipped = s.ShippedNewGameStart();
                Assert.Equal(new[] { 1, 32, 2 }, new[] { shipped.Level, (int)shipped.X, (int)shipped.Y });
                var a = AutoZones.Build(s, l => s.LoadLevel(l), new NavOptions());
                int home = a.Set.StartZone;
                Assert.Equal(home, a.Set.ZoneAt(1, 32, 2));
                // move the start, as the editor's start tool does, to an open square of another zone on level 1
                var m = s.LoadLevel(1);
                int tile = Enumerable.Range(0, 64 * 64).First(t => { int x = t % 64, y = t / 64; int z = a.Set.ZoneAt(1, x, y); return y > 20 && z > ZoneSet.Unassigned && z != home && m.Tiles[x, y].Type == TileType.Open; });
                int tx = tile % 64, ty = tile / 64, there = a.Set.ZoneAt(1, tx, ty);
                s.SetNewGameStart(tx, ty);
                var b = AutoZones.Build(s, l => s.LoadLevel(l), new NavOptions());
                Assert.Equal(there, b.Set.ZoneAt(1, tx, ty));
                Assert.Equal(there, b.Set.StartZone);
                Assert.Equal(tx, (int)b.Start.X);
                // the option: the game's own square, whatever the executable says
                var d = AutoZones.Build(s, l => s.LoadLevel(l), new NavOptions { ShippedStart = true });
                Assert.Equal(home, d.Set.StartZone);
                // the solve over time starts there too, and its report says where
                var t = Timeline.Build(s, l => s.LoadLevel(l), new NavOptions { ShippedStart = true }, new AbilityOptions(), null, null, false);
                Assert.Equal(home, t.Set.StartZone);
                Assert.Contains("Start: the shipped new game start square (32, 2), level 1 (32, 2)", Timeline.Report(t));
                Assert.True(new NavOptions { ShippedStart = true }.Clone().ShippedStart);
            }
        }
    }
}
