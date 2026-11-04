using System.Linq;
using UwEditor.Core.Editing;
using UwEditor.Core.Randomizer;
using Xunit;

namespace UwEditor.Core.Tests
{
    /// <summary>
    /// The end of the game (UW1): a trigger running hack trap 0x3F (event/TRIGGER.C do_trap_hack;
    /// game/SKILLS.C check_victory plays the ending on any level). The randomizer counts the game
    /// as won on reaching level 9 or a square that ends it.
    /// </summary>
    public class EndGameTests
    {
        [Fact]
        public void The_game_is_won_on_level_9_or_on_a_square_that_ends_it()
        {
            string g = GameData.Game("UW"); if (g == null) return;
            var s = GameSession.Open(g);
            // the shipped game ends at one square: level 9 (52, 51), move trigger #1022 running hack trap #1021
            var shipped = EndGame.Find(s, l => s.LoadLevel(l));
            var end = Assert.Single(shipped);
            Assert.Equal(new[] { 9, 52, 51, 1022, 1021 }, new[] { end.Level, end.X, end.Y, end.Trigger, end.Trap });

            var edits = new LevelEdits(s);
            // without the Book of Honesty (0x136) the ritual never opens level 9: not won
            foreach (var slot in s.LevelSlots().Where(x => x.Present))
            {
                var e = edits.For(slot.Level);
                foreach (int i in e.Model.Objects.Where(o => !o.Free && o.TileX >= 0 && o.Item == 0x136).Select(o => o.Index).ToList()) e.DeleteObject(i);
            }
            var t = Timeline.Build(s, edits.Model, new NavOptions(), new AbilityOptions(), null, null, false);
            Assert.False(t.Solve.GoalReached);
            Assert.Contains(t.Solve.GoalMissing, m => m.Contains("or an end of the game (L9 (52, 51))"));

            // a square that ends the game in the start area: won there
            var r = TrapRecipes.EndOfGame(edits.For(1), 33, 3);
            var m1 = edits.Model(1);
            Assert.Equal(TrapRecipes.MoveTrigger, m1.Objects[r.Trigger].Item);
            Assert.Equal(r.Trap, m1.Objects[r.Trigger].Link);
            Assert.Equal(TrapRecipes.HackTrap, m1.Objects[r.Trap].Item);
            Assert.Equal(0x3F, m1.Objects[r.Trap].Quality);
            Assert.Equal(new[] { 33, 3 }, new[] { m1.Objects[r.Trigger].TileX, m1.Objects[r.Trigger].TileY });
            Assert.Equal(2, EndGame.Find(s, edits.Model).Count);
            t = Timeline.Build(s, edits.Model, new NavOptions(), new AbilityOptions(), null, null, false);
            Assert.True(t.Solve.GoalReached);
            Assert.Equal("the end of the game at L1 (33, 3)", t.Solve.GoalBy);
            string report = Timeline.Report(t);
            Assert.Contains("Or the end of the game: L1 (33, 3) (trigger #" + r.Trigger + ")", report);
            Assert.Contains("L9 (52, 51) (trigger #1022)", report);
            Assert.Contains("Reached by the end of the game at L1 (33, 3).", report);

            // one undo step takes it away; the shipped game is still won on level 9 alone
            edits.For(1).Undo();
            Assert.Single(EndGame.Find(s, edits.Model));
            var plain = Timeline.Build(s, l => s.LoadLevel(l), new NavOptions(), new AbilityOptions(), null, null, false);
            Assert.True(plain.Solve.GoalReached);
            Assert.Equal("the goal zone", plain.Solve.GoalBy);
            // UW2: not known
            string g2 = GameData.Game("UW2");
            if (g2 != null) Assert.Throws<System.InvalidOperationException>(() => TrapRecipes.EndOfGame(new LevelEdits(GameSession.Open(g2)).For(1), 10, 10));
        }

        [Fact]
        public void A_square_that_ends_the_game_needs_only_the_goal_options_not_level_9s_requirement()
        {
            var res = new TimelineResult { GoalRequires = "talismans:8 & flag:garamon_buried" };
            res.GoalExtras.Add(new Fact { Id = "goal_orb", Option = "require_orb", Requires = "flag:orb_destroyed" });
            var off = new SolverOptions();
            var on = new SolverOptions(); on.OptionsOn.Add("require_orb");
            // level 9's arrival: the goal fact's requirement, and the option's extra when on
            Assert.Equal("talismans:8 & flag:garamon_buried", res.GoalFor(off));
            Assert.Equal("talismans:8 & flag:garamon_buried & flag:orb_destroyed", res.GoalFor(on));
            // a square that ends the game (check_victory plays the ending at once): only the option's extra
            Assert.Equal("free", res.EndFor(off));
            Assert.Equal("flag:orb_destroyed", res.EndFor(on));
        }
    }
}
