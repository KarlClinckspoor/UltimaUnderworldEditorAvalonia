using System.Collections.Generic;
using UwEditor.Core.Editing;

namespace UwEditor.Core
{
    /// <summary>The values a creature's attitude and goal take, for the properties panel's lists.</summary>
    public static class CritterChoices
    {
        /// <summary>include/object.h OBJ_ATTITUDE.</summary>
        public static readonly List<Choice> Attitudes = new List<Choice>
        {
            new Choice { Value = 0, Label = "0 hostile" },
            new Choice { Value = 1, Label = "1 upset" },
            new Choice { Value = 2, Label = "2 mellow" },
            new Choice { Value = 3, Label = "3 friendly" },
        };

        /// <summary>
        /// The goals (the goal word's low nibble, critter_mv's switch): include/critter.h GOAL_*
        /// in UW1, critter/AI.C's list in UW2 (3 there is moved by other code, 15 frozen).
        /// </summary>
        public static List<Choice> Goals(bool uw2)
        {
            return new List<Choice>
            {
                new Choice { Value = 0, Label = "0 stand, watching for the player" },
                new Choice { Value = 1, Label = "1 go home, then mill about" },
                new Choice { Value = 2, Label = "2 wander" },
                new Choice { Value = 3, Label = uw2 ? "3 moved by other code (follows)" : "3 follow the goal target" },
                new Choice { Value = 4, Label = "4 guard: attack what comes near" },
                new Choice { Value = 5, Label = "5 attack the goal target" },
                new Choice { Value = 6, Label = "6 flee from the goal target" },
                new Choice { Value = 7, Label = "7 stand (as 0)" },
                new Choice { Value = 8, Label = "8 mill about near home" },
                new Choice { Value = 9, Label = "9 fight back, cornered" },
                new Choice { Value = 10, Label = "10 come and talk to the player" },
                new Choice { Value = 11, Label = "11 flutter at random" },
                new Choice { Value = 12, Label = "12 hover at home" },
                uw2 ? new Choice { Value = 15, Label = "15 frozen while the goal target counts down" } : null,
            }.FindAll(c => c != null);
        }
    }
}
