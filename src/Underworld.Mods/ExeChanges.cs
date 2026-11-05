using UltimateEditor.Games;

namespace Underworld.Mods
{
    /// <summary>
    /// What a mod does to the executable, for the games that do not run it as it is: the
    /// Underworld Exhumed ports, which take a start square, and UltimaHacks' executable, built
    /// from GOG's, which takes the start square again (docs/mod-format.md, "The executable").
    /// </summary>
    public static class ExeChanges
    {
        /// <summary>The start square of a new game in the modded executable, when the mod moved it.</summary>
        public static bool MovedStart(byte[] original, byte[] modded, GameKind game, out int x, out int y)
        {
            x = y = 0;
            if (!StartPosition.Read(modded, game, out x, out y)) return false;
            int ox, oy;
            return !StartPosition.Read(original, game, out ox, out oy) || ox != x || oy != y;
        }

        /// <summary>Whether every byte the mod changes in the executable is the start square's.</summary>
        public static bool OnlyStart(byte[] original, byte[] modded, GameKind game)
        {
            if (original.Length != modded.Length) return false;
            int at = StartPosition.Find(original, game);
            if (at < 0 || StartPosition.Find(modded, game) != at) return false;
            for (int i = 0; i < original.Length; i++)
                if (original[i] != modded[i] && i != at + 3 && i != at + 5) return false;
            return true;
        }
    }
}
