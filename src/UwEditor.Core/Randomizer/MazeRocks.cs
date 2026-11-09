namespace UwEditor.Core.Randomizer
{
    /// <summary>
    /// UW1's glowing rocks (0x129) on level 5, the Pac-Man level. 83 of the game's 84 lie in the
    /// maze in that level's north-west corner, one per square from (2, 2) to (20, 22), each a
    /// quantity of 1 (obj/USEITEMS.C UseMagic adds a used rock to the one the player carries); the
    /// 84th is carried by creature #227 on the same level. Every randomizer
    /// leaves them alone (Karl): they are never moved, swapped, rerolled, removed, counted or used
    /// as another object's spot, whatever the settings say.
    /// </summary>
    public static class MazeRocks
    {
        /// <summary>The level of the maze (UW1, 1-based).</summary>
        public const int Level = 5;
        /// <summary>The glowing rock (include/items.h ITEM_GLOWING_ROCK).</summary>
        public const int Item = 0x129;

        /// <summary>True for a glowing rock on UW1's level 5.</summary>
        public static bool Is(GameSession s, int level, ObjectInfo o)
        {
            return s != null && !s.IsUw2 && level == Level && o != null && o.Item == Item;
        }

        /// <summary>
        /// True for a square of the maze (the rocks' squares, the lanes between them and the way
        /// out at (21, 22): (2, 2) to (21, 22) of UW1's level 5; walls all around). Place anywhere
        /// drops nothing there.
        /// </summary>
        public static bool InMaze(GameSession s, int level, int x, int y)
        {
            return s != null && !s.IsUw2 && level == Level && x >= 2 && x <= 21 && y >= 2 && y <= 22;
        }
    }
}
