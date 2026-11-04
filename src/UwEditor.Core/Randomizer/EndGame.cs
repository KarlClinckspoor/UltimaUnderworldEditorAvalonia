using System;
using System.Collections.Generic;
using System.Linq;
using UwEditor.Core.Editing;

namespace UwEditor.Core.Randomizer
{
    /// <summary>A square whose trigger ends the game (UW1: a chain reaching hack trap 0x3F).</summary>
    public sealed class EndTrigger
    {
        public int Level, Trigger, Trap, X, Y;
        public override string ToString() { return string.Format("L{0} ({1}, {2})", Level, X, Y); }
    }

    /// <summary>
    /// The ends of the game in the levels: every trigger lying on a square whose chain reaches
    /// a hack trap of quality 0x3F (event/TRIGGER.C do_trap_hack sets EndGameMode; game/SKILLS.C
    /// check_victory plays the ending on any level). The shipped UW1 has one, level 9 (52, 51);
    /// the Zones window's Mark as goal adds more.
    /// </summary>
    public static class EndGame
    {
        public static List<EndTrigger> Find(GameSession s, Func<int, LevelModel> level)
        {
            var list = new List<EndTrigger>();
            if (s.IsUw2) return list;
            foreach (var slot in s.LevelSlots())
            {
                if (!slot.Present) continue;
                LevelModel m;
                try { m = level(slot.Level); } catch (Exception) { continue; }
                if (m == null || !m.Present) continue;
                foreach (var ch in Triggers.Chains(m))
                {
                    if (!ch.Trigger.OnTile) continue;
                    var hit = ch.Steps.FirstOrDefault(o => o.Item == TrapRecipes.HackTrap && o.Quality == TrapRecipes.EndOfGameQuality);
                    if (hit == null) continue;
                    list.Add(new EndTrigger { Level = slot.Level, Trigger = ch.Trigger.Index, Trap = hit.Index, X = ch.Trigger.TileX, Y = ch.Trigger.TileY });
                }
            }
            return list;
        }
    }
}
