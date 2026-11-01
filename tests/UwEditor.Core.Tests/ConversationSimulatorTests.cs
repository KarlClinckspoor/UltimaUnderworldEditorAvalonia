using System;
using System.IO;
using System.Linq;
using UltimateEditor.Games;
using UwEditor.Core.Conversations;
using Xunit;
using Xunit.Abstractions;

namespace UwEditor.Core.Tests
{
    public class ConversationSimulatorTests
    {
        readonly ITestOutputHelper output;
        public ConversationSimulatorTests(ITestOutputHelper output) { this.output = output; }

        static ConversationSimulator Sim(GameSession s, ConversationLibrary lib, int slot)
        {
            var c = lib.Archive.Read(slot);
            var world = SimWorld.FromGame(s, slot, null);
            return new ConversationSimulator(c, lib.StringsOf(c), world, (b, i) => s.Strings.Get(b, i), SimWorld.SavedGlobals(s, slot));
        }

        [Theory]
        [InlineData("UW")]
        [InlineData("UW2")]
        public void Every_shipped_conversation_runs_along_a_path_without_errors(string game)
        {
            string g = GameData.Game(game); if (g == null) return;
            var s = GameSession.Open(g);
            var lib = ConversationLibrary.Open(s);
            int ended = 0, total = 0, says = 0;
            foreach (int slot in lib.Archive.Slots())
            {
                var sim = Sim(s, lib, slot);
                // the first answer, and the last one once a menu comes round again (often "goodbye")
                bool done = sim.RunToEnd((m, seen) => seen == 0 ? 1 : m.Ids.Count, () => "yes", 300000);
                total++;
                if (done) ended++;
                if (sim.State == SimState.Error) output.WriteLine("{0}: {1} at {2:X4}", slot, sim.Error, sim.Pc);
                Assert.NotEqual(SimState.Error, sim.State);
                var strs = lib.StringsOf(sim.Conversation);
                foreach (var o in sim.Output.Where(x => x.Kind != SimOutputKind.Note && x.Kind != SimOutputKind.Answer))
                {
                    if ((o.StringId & 0xFFFF) >= 0x200) continue;   // a string the script made
                    Assert.InRange(o.StringId, 0, strs.Count - 1);
                    Assert.Equal(strs[o.StringId], o.Source);
                    if (o.Source.IndexOf('@') < 0) Assert.Equal(o.Source, o.Text);
                    says++;
                }
            }
            output.WriteLine("{0}: {1} of {2} conversations ran to the end, {3} lines said", game, ended, total, says);
            Assert.True(ended > total * 3 / 4, ended + " of " + total);
        }

        [Fact]
        public void Corby_talks_the_player_answers_and_the_name_is_filled_in()
        {
            string g = GameData.Game("UW"); if (g == null) return;
            var s = GameSession.Open(g);
            var lib = ConversationLibrary.Open(s);
            var sim = Sim(s, lib, 1);
            sim.World.PlayerName = "Karl";
            sim.Run();
            Assert.Equal(SimState.WaitingMenu, sim.State);
            Assert.Equal("Hast thou come to add to my torment?", sim.Output[0].Text);
            Assert.Equal(SimOutputKind.Say, sim.Output[0].Kind);
            Assert.Equal("Of what torment dost thou speak?", sim.Menu.Texts[0]);
            Assert.Null(sim.Choose(1));
            Assert.Equal(SimOutputKind.Choice, sim.Output[1].Kind);
            sim.Run();
            Assert.StartsWith("Why, the torment of lost hopes", sim.Output[2].Text);
            // the log has the instructions, the calls with their arguments and the writes
            Assert.Contains(sim.Log, l => l.Kind == SimLogKind.Instruction && l.Text.StartsWith("PUSHI"));
            Assert.Contains(sim.Log, l => l.Kind == SimLogKind.Call && l.Text.StartsWith("babl_menu("));
            Assert.Contains(sim.Log, l => l.Kind == SimLogKind.Write);
            // a memory variable changed by hand
            Assert.Null(sim.SetMemory(0, 5));
            Assert.Equal(5, sim.GetMem(0));
            // a conversation that says the player's name (@GS8: play_name)
            var five = Sim(s, lib, 5);
            five.RunToEnd((m, n) => n == 0 ? 1 : m.Ids.Count, () => "", 100000);
            Assert.Contains(five.Output, o => o.Source != null && o.Source.Contains("@GS8") && o.Text.Contains("Avatar"));
            Assert.Equal(SimState.Ended, five.State);
        }

        [Fact]
        public void Quests_skills_and_random_follow_the_game_rules()
        {
            foreach (var kind in new[] { GameKind.UW1, GameKind.UW2 })
            {
                // set_quest(3, 1); global0 = get_quest(3); x_skills(3, 7); global1 = random(6)
                // (arguments are pushed as addresses, the first argument first: it is args[-n])
                var c = Assembler.Assemble(string.Join("\n", new[]
                {
                    ".slot 1", ".strings 0x0E01", ".variables 4",
                    ".import function get_quest 0 count 1 type 0x129", ".import function set_quest 1 count 1 type 0",
                    ".import function x_skills 2 count 1 type 0x129", ".import function random 3 count 1 type 0x129",
                    "START", "PUSHBP", "SPTOBP", "PUSHI 3", "ADDSP",
                    "PUSHI_EFF 1", "PUSHI 3", "STO", "PUSHI_EFF 2", "PUSHI 1", "STO",
                    "PUSHI_EFF 1", "PUSHI_EFF 2", "PUSHI 2", "CALLI set_quest", "POP", "POP", "POP",
                    "PUSHI_EFF 1", "PUSHI 1", "CALLI get_quest", "POP", "POP", "PUSH_REG", "PUSHI 0", "SWAP", "STO",
                    "PUSHI_EFF 2", "PUSHI 7", "STO",
                    "PUSHI_EFF 1", "PUSHI_EFF 2", "PUSHI 2", "CALLI x_skills", "POP", "POP", "POP",
                    "PUSHI_EFF 3", "PUSHI 6", "STO", "PUSHI_EFF 3", "PUSHI 1", "CALLI random", "POP", "POP", "PUSH_REG", "PUSHI 1", "SWAP", "STO",
                    "EXIT",
                }));
                var w = SimWorld.Defaults(kind);
                w.FixedRandom = 4;
                var sim = new ConversationSimulator(c, new[] { "zero", "one" }, w);
                sim.Run();
                Assert.Equal(SimState.Ended, sim.State);
                Assert.Equal(1, sim.World.QuestFlag(3));
                Assert.Equal(1, sim.GetMem(0));
                Assert.Equal(7, sim.World.Skills[3]);
                Assert.Equal(4, sim.GetMem(1));
            }
        }

    }
}
