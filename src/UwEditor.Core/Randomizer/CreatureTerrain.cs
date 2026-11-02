using System;
using UwEditor.Core.Editing;

namespace UwEditor.Core.Randomizer
{
    /// <summary>
    /// Where a creature may stand, by the floor's terrain class and how the creature moves.
    ///
    /// The floor's class comes from DATA\TERRAIN.DAT through the level's floor texture map: UW1
    /// takes the word shifted right 4 (0x10 water, 0x20 lava; uw1 include/map.h TERRAIN_*,
    /// critter/PATHFIND.C hyp_move), UW2 bits 6-7 (1 water, 2 lava, 3 ice; uw2 include/map.h
    /// TERR_CLASS). How a creature moves is OBJECTS.DAT's creature byte 0x0A: bit 6 swims, bit 7
    /// flies (include/critter.h struct Creature), and fire resistance is COMOBJ.DAT's resist
    /// byte bit 8 (obj/object.h DMG_FIRE).
    ///
    /// The rules (both games, critter/PATHFIND.C init_ai, crit_hndlr_walk; critter/AI.C critter_ai;
    /// motion/OBJPHYS.C set_phys_data; docs/behaviour/npc-ai.md "One step, in order"):
    /// - A walker whose footprint is all on water drowns on its next step (crit_hndlr_walk).
    /// - On lava an object takes 1 fire damage one time in five (set_phys_data), unless it
    ///   resists fire (combat/DAMAGE.C check_res); critter_ai lets a fire-resistant creature
    ///   ignore lava when it moves.
    /// - Swimmers (handler CT4) ignore water, and their paths keep off lava and plain floor.
    /// - Fliers (CT2) ignore the floor's terrain.
    /// No other floor damages a creature in either game: set_phys_data's lava damage is the only
    /// terrain damage to objects (ice in UW2 only makes the floor slippery, motion/PHYSICS.C).
    /// </summary>
    public static class CreatureTerrain
    {
        public const int Plain = 0, Water = 1, Lava = 2, Ice = 3;

        /// <summary>A terrain class in words.</summary>
        public static string Name(int cls)
        {
            switch (cls)
            {
                case Water: return "water";
                case Lava: return "lava";
                case Ice: return "ice";
                default: return "plain floor";
            }
        }

        /// <summary>The game's TERRAIN.DAT, or null when it cannot be read.</summary>
        public static TerrainTable TryLoad(GameSession s)
        {
            try { var t = TerrainTable.Load(s); return t.Exists ? t : null; } catch (Exception) { return null; }
        }

        /// <summary>The terrain class of a tile's floor (0 plain, 1 water, 2 lava, 3 ice); plain when the table is missing.</summary>
        public static int FloorClass(GameSession s, TerrainTable terrain, LevelModel m, int x, int y)
        {
            if (terrain == null || x < 0 || y < 0 || x >= LevelModel.Size || y >= LevelModel.Size) return Plain;
            var t = m.Tiles[x, y];
            if (t == null || t.FloorIndex < 0 || m.FloorMap == null || t.FloorIndex >= m.FloorMap.Length) return Plain;
            int word = terrain.Get(terrain.EntryOf(m.FloorMap[t.FloorIndex], true));
            // UW1: map.h "a floor's class is the word shifted right 4"; UW2: TERR_CLASS 0xC0
            return s.IsUw2 ? (word >> 6) & 3 : (word >> 4) & 3;
        }

        /// <summary>
        /// Whether a creature of this kind may be placed on a floor of this class: anyone on plain
        /// floor and ice; on water only fliers and swimmers; on lava only fliers and creatures that
        /// resist fire.
        /// </summary>
        public static bool CanStand(CreatureKind k, int cls)
        {
            if (k.Flier) return true;
            if (cls == Water) return k.Swims;
            if (cls == Lava) return k.Fireproof;
            return true;
        }

        /// <summary>A floor some creatures may not be placed on (water or lava).</summary>
        public static bool Restricted(int cls) { return cls == Water || cls == Lava; }
    }
}
