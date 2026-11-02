using System;
using System.Collections.Generic;

namespace UwEditor.Core.Graphics
{
    /// <summary>A run of palette entries the game rotates by one place at a time.</summary>
    public sealed class PaletteCycle
    {
        /// <summary>The first entry and how many there are.</summary>
        public int First { get; private set; }
        public int Count { get; private set; }
        /// <summary>True: each entry takes the colour of the one before it (the colours move to higher entries); false: of the one after it.</summary>
        public bool Up { get; private set; }
        /// <summary>Time between two steps, in seconds.</summary>
        public double Period { get; private set; }
        /// <summary>Where it cycles ("in the game", "on the main menu").</summary>
        public string Where { get; private set; }
        public int Last { get { return First + Count - 1; } }

        public PaletteCycle(int first, int count, bool up, double period, string where)
        {
            First = first; Count = count; Up = up; Period = period; Where = where;
        }

        public bool Contains(int index) { return index >= First && index <= Last; }

        /// <summary>"0x30–0x33 (48–51)".</summary>
        public string Range { get { return string.Format("0x{0:X2}–0x{1:X2} ({0}–{1})", First, Last); } }

        /// <summary>The palette entry whose colour entry index shows after a number of steps.</summary>
        public int SourceAfter(int index, long steps)
        {
            if (!Contains(index)) return index;
            long k = ((index - First) + (Up ? -steps : steps)) % Count;
            if (k < 0) k += Count;
            return First + (int)k;
        }
    }

    /// <summary>
    /// The palette entries the games animate by rotating them instead of redrawing pixels (water,
    /// lava, the blue and the fire colours). In play the input loop calls cycle_colors every frame
    /// (ui/INTERACT.C display_scr, with the low byte of the 1/256 s clock), which rotates its banks
    /// once every 64 ticks, four times a second (gfx/COLCYCLE.C cycle_colors, rotate_bank):
    /// UW1 the four banks of four 30h–33h, 34h–37h, 38h–3Bh, 3Ch–3Fh one place down and the runs
    /// 10h–14h and 15h–17h one place up; UW2 the banks E0h–EFh and the runs 3–7 and 8–10h. It acts on
    /// whichever palette is loaded, so the same entries cycle in every palette of PALS.DAT, and a
    /// 4-bit picture's colour cycles when its auxiliary palette points at one of them. UW1's main
    /// menu also rotates 40h–7Fh one place up every 14 ticks while it waits (ui/MAINMENU.C ovr138_94).
    /// Cutscenes have their own cycles in each LPF file (gfx/CUTS.C anm_cycle), not listed here.
    /// </summary>
    public static class PaletteCycles
    {
        const double Tick = 1.0 / 256;   // the game clock (include/hookgame.h GAME_TIME: 1/256 s)

        /// <summary>The cycles of play (the 3D view, the inventory, the panels).</summary>
        public static IReadOnlyList<PaletteCycle> InGame(bool uw2)
        {
            double p = 64 * Tick;
            const string w = "in the game";
            if (uw2)
                return new[]
                {
                    new PaletteCycle(0xE0, 4, false, p, w), new PaletteCycle(0xE4, 4, false, p, w),
                    new PaletteCycle(0xE8, 4, false, p, w), new PaletteCycle(0xEC, 4, false, p, w),
                    new PaletteCycle(0x03, 5, true, p, w), new PaletteCycle(0x08, 3, true, p, w),
                };
            return new[]
            {
                new PaletteCycle(0x30, 4, false, p, w), new PaletteCycle(0x34, 4, false, p, w),
                new PaletteCycle(0x38, 4, false, p, w), new PaletteCycle(0x3C, 4, false, p, w),
                new PaletteCycle(0x10, 5, true, p, w), new PaletteCycle(0x15, 3, true, p, w),
            };
        }

        /// <summary>The main menu's cycle: UW1 only (none in UW2).</summary>
        public static IReadOnlyList<PaletteCycle> MainMenu(bool uw2)
        {
            if (uw2) return new PaletteCycle[0];
            return new[] { new PaletteCycle(0x40, 0x40, true, 14 * Tick, "on the main menu") };
        }

        /// <summary>The cycle a palette entry belongs to, or null.</summary>
        public static PaletteCycle Find(IEnumerable<PaletteCycle> cycles, int paletteIndex)
        {
            if (cycles == null) return null;
            foreach (var c in cycles) if (c.Contains(paletteIndex)) return c;
            return null;
        }

        /// <summary>
        /// A colour table as it shows some seconds into the cycling: colours[i] stands for palette
        /// entry map[i] (null: i itself); an entry in a cycle takes the colour of the entry the
        /// rotation has moved there, looked up in palette (the full 256-colour table).
        /// </summary>
        public static uint[] At(uint[] colours, int[] map, uint[] palette, IEnumerable<PaletteCycle> cycles, double seconds)
        {
            var o = (uint[])colours.Clone();
            if (cycles == null || palette == null) return o;
            var list = new List<PaletteCycle>(cycles);
            for (int i = 0; i < o.Length; i++)
            {
                if ((o[i] >> 24) == 0) continue;                // the transparent colour stays
                int e = map != null ? (i < map.Length ? map[i] : -1) : i;
                var c = Find(list, e);
                if (c == null) continue;
                long steps = (long)Math.Floor(seconds / c.Period);
                int src = c.SourceAfter(e, steps);
                if (src >= 0 && src < palette.Length) o[i] = palette[src];
            }
            return o;
        }
    }
}
