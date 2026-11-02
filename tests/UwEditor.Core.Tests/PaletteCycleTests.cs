using System.IO;
using System.Linq;
using UwEditor.Core.Graphics;
using Xunit;

namespace UwEditor.Core.Tests
{
    /// <summary>The palette entries the games rotate (gfx/COLCYCLE.C) and the rotation itself.</summary>
    public class PaletteCycleTests
    {
        [Fact]
        public void The_cycling_entries_are_the_ones_cycle_colors_rotates_in_each_game()
        {
            var uw1 = PaletteCycles.InGame(false);
            var uw2 = PaletteCycles.InGame(true);
            // UW1: 30h..3Fh in four banks of four, 10h..14h and 15h..17h
            Assert.Equal(Enumerable.Range(0x10, 8).Concat(Enumerable.Range(0x30, 16)).OrderBy(i => i), Enumerable.Range(0, 256).Where(i => PaletteCycles.Find(uw1, i) != null));
            // UW2: E0h..EFh, 3..7 and 8..10h
            Assert.Equal(Enumerable.Range(3, 8).Concat(Enumerable.Range(0xE0, 16)).OrderBy(i => i), Enumerable.Range(0, 256).Where(i => PaletteCycles.Find(uw2, i) != null));
            // the blue Karl painted with: entry 55 in UW1, 231 in UW2 (#14307D in both palettes 0), each in a bank of four
            var b1 = PaletteCycles.Find(uw1, 55); var b2 = PaletteCycles.Find(uw2, 231);
            Assert.Equal(0x34, b1.First); Assert.Equal(4, b1.Count); Assert.False(b1.Up);
            Assert.Equal(0xE4, b2.First); Assert.Equal(4, b2.Count);
            Assert.Equal(0.25, b1.Period, 6);                          // every 64 ticks of the 1/256 s clock
            Assert.True(PaletteCycles.Find(uw1, 0x15).Up);
            Assert.Null(PaletteCycles.Find(uw1, 231));
            Assert.Null(PaletteCycles.Find(uw2, 55));
            Assert.Single(PaletteCycles.MainMenu(false));
            Assert.Empty(PaletteCycles.MainMenu(true));
        }

        /// <summary>rotate_bank as the game does it (gfx/COLCYCLE.C), on colour numbers.</summary>
        static void RotateBank(int[] pal, int first, int count, bool up)
        {
            if (up) { int s = pal[first + count - 1]; for (int i = first + count - 1; i > first; i--) pal[i] = pal[i - 1]; pal[first] = s; }
            else { int s = pal[first]; for (int i = first; i < first + count - 1; i++) pal[i] = pal[i + 1]; pal[first + count - 1] = s; }
        }

        [Fact]
        public void The_colours_some_steps_in_match_rotate_bank_done_as_many_times()
        {
            foreach (bool uw2 in new[] { false, true })
            {
                var cycles = PaletteCycles.InGame(uw2);
                var pal = Enumerable.Range(0, 256).ToArray();
                var colours = Enumerable.Range(0, 256).Select(i => 0xFF000000u | (uint)i).ToArray();
                for (int step = 0; step <= 9; step++)
                {
                    var at = PaletteCycles.At(colours, null, colours, cycles, step * 0.25 + 0.01);
                    Assert.Equal(pal, at.Select(c => (int)(c & 0xFFFFFF)).ToArray());
                    foreach (var c in cycles) RotateBank(pal, c.First, c.Count, c.Up);
                }
            }
        }

        [Fact]
        public void A_picture_through_an_auxiliary_palette_cycles_where_its_entries_point_into_a_cycle()
        {
            var cycles = PaletteCycles.InGame(false);
            var palette = Enumerable.Range(0, 256).Select(i => 0xFF000000u | (uint)i).ToArray();
            int[] map = { 0, 0x30, 0x31, 0x40 };
            var colours = new uint[] { 0, palette[0x30], palette[0x31], palette[0x40] };   // colour 0 transparent
            var at = PaletteCycles.At(colours, map, palette, cycles, 0.3);                  // one step: down, so 30h shows 31h
            Assert.Equal(0u, at[0]);
            Assert.Equal(palette[0x31], at[1]);
            Assert.Equal(palette[0x32], at[2]);
            Assert.Equal(palette[0x40], at[3]);
        }

        [Fact]
        public void The_games_own_palettes_hold_the_blue_bank_at_those_entries()
        {
            foreach (var (game, entry) in new[] { ("UW", 55), ("UW2", 231) })
            {
                string folder = GameData.Game(game);
                if (folder == null) continue;
                var pals = Palettes.Load(Path.Combine(folder, "DATA"));
                Assert.Equal(0xFF14307Du, pals.Get(0)[entry]);
            }
        }
    }
}
