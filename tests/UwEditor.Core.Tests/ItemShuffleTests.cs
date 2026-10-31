using System.Linq;
using UwEditor.Core.Editing;
using UwEditor.Core.Randomizer;
using Xunit;

namespace UwEditor.Core.Tests
{
    public class ItemShuffleTests
    {
        static int CountOf(LevelEdits e, GameSession s, System.Func<ObjectInfo, bool> f)
        {
            int n = 0;
            foreach (var sl in s.LevelSlots()) if (sl.Present) n += e.Model(sl.Level).Objects.Count(o => !o.Free && o.TileX >= 0 && f(o));
            return n;
        }

        [Theory]
        [InlineData("UW")]
        [InlineData("UW2")]
        public void Weights_minimums_and_specials_are_honoured_and_undo_puts_everything_back(string game)
        {
            string g = GameData.Game(game); if (g == null) return;
            var s = GameSession.Open(g);
            var edits = new LevelEdits(s);
            var o = ShuffleOptions.Defaults(s);
            o.Seed = 7; o.Axes = 0; o.MinThread = 6; o.MinPole = 6; o.MinFishingPole = 8; o.WeaponSpecials = true;
            o.RuneMinimum[0xE8] = 4;
            var before = s.LevelSlots().Where(x => x.Present).ToDictionary(x => x.Level, x => (byte[])edits.For(x.Level).Data.Clone());
            int kept = CountOf(edits, s, x => x.Item == 0x0A);

            var r = ItemShuffle.Run(s, edits.Model, o);
            Assert.Equal(r.Report.ToString(), ItemShuffle.Run(s, edits.Model, o).Report.ToString());   // same seed, same result
            ItemShuffle.Apply(r, edits, o.Seed);

            Assert.Equal(0, CountOf(edits, s, x => ItemTables.Axes.Contains(x.Item)));
            Assert.Equal(kept, CountOf(edits, s, x => x.Item == 0x0A));          // Caliburn / the jewelled dagger
            Assert.True(CountOf(edits, s, x => x.Item == ItemTables.Thread(s.IsUw2)) >= 6);
            Assert.True(CountOf(edits, s, x => x.Item == ItemTables.Pole) >= 6);
            Assert.True(CountOf(edits, s, x => x.Item == ItemTables.FishingPole) >= 8);
            Assert.True(CountOf(edits, s, x => x.Item == 0xE8) >= 4);
            foreach (int rune in ItemTables.Runes) Assert.True(CountOf(edits, s, x => x.Item == rune) >= 1);
            if (s.IsUw2)
                for (int e = 8; e <= 13; e++)
                    Assert.Equal(1, CountOf(edits, s, x => x.Item < 0x10 && x.Enchanted && x.IsQuantity && x.Link == (0x200 | 0xC0 | e)));
            Assert.Equal(1, CountOf(edits, s, x => x.Item >= 0x20 && x.Item < 0x40 && x.Enchanted && x.IsQuantity && x.Link == 0x236) - CountBefore(s, 0x236));

            ItemShuffle.Revert(r, edits);
            foreach (var kv in before) Assert.Equal(kv.Value, edits.For(kv.Key).Data);
        }

        static int CountBefore(GameSession s, int link)
        {
            int n = 0;
            foreach (var sl in s.LevelSlots()) if (sl.Present) n += s.LoadLevel(sl.Level).Objects.Count(o => !o.Free && o.TileX >= 0 && o.Item >= 0x20 && o.Item < 0x40 && o.Enchanted && o.IsQuantity && o.Link == link);
            return n;
        }

        [Fact]
        public void Options_round_trip_through_json()
        {
            string g = GameData.Game("UW"); if (g == null) return;
            var o = ShuffleOptions.Defaults(GameSession.Open(g));
            o.Axes = 3; o.RuneMinimum[0xF0] = 5; o.WeaponEnchants[0].Mean = 4.5;
            var b = ShuffleOptions.FromJson(o.ToJson());
            Assert.Equal(3, b.Axes);
            Assert.Equal(5, b.RuneMinimum[0xF0]);
            Assert.Equal(4.5, b.WeaponEnchants[0].Mean);
            Assert.Equal(o.FoodWeights.Count, b.FoodWeights.Count);
        }
    }
}
