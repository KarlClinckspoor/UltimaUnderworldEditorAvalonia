using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.CompilerServices;
using UltimateEditor.Games;
using UwEditor.Core.Conversations;
using Xunit;
using Xunit.Abstractions;

namespace UwEditor.Core.Tests
{
    /// <summary>
    /// The manual's conversation recipes (MANUAL.md, "Recipes"): each one, spliced into a real
    /// UW1 conversation, compiles, and runs in the simulator with the effect the manual says.
    /// The recipes' text here is the manual's, character for character (checked below).
    /// </summary>
    public class ConversationRecipesTests
    {
        readonly ITestOutputHelper output;
        public ConversationRecipesTests(ITestOutputHelper output) { this.output = output; }

        // ----- the recipes, as the manual has them -----

        const string Trade = @"trade_stew_for_scales() {
    int stew, given;
    stew = find_barter(283);
    if (stew == 0) {
        say ""Thou hast not offered me any rotworm stew."";
        return;
    }
    give_ptr_npc(&stew, -1);
    given = take_from_npc(285);
    if (given == 2) {
        say ""Thine arms are full. I leave the scales on the floor."";
    }
}";

        const string Gift = @"offer_bread() {
    say ""Wilt thou take a loaf of bread for the road?"";
    menu {
        ""Yes, I thank thee."":
            if (global34 == 0) {
                global34 = 1;
                do_inv_create(177);
                if (take_from_npc(177) == 2) {
                    say ""Thy hands are full. I leave it at thy feet."";
                }
            } else {
                say ""I gave thee my last loaf already."";
            }
        ""No, keep it."";
    }
}";

        const string Ambush = @"thou_hast_come_far_enough() {
    say ""Thou hast come far enough, @GS8. Draw thy weapon!"";
    menu {
        ""Wait! I mean thee no harm."":
            say ""Thy words mean nothing to me."";
        ""Then have at thee!"";
    }
    npc_attitude = 0;
    npc_goal = 5;
    npc_gtarg = 1;
    exit;
}";

        const string Barter = @"trade_goods() {
    int finished, likes[3], dislikes[2];
    likes[1] = 1011;
    likes[2] = -1;
    dislikes[1] = 1000;
    dislikes[2] = -1;
    set_likes_dislikes(likes, dislikes);
    setup_to_barter();
    finished = 0;
    while (!finished) {
        menu {
            ""I make thee this offer."":
                if (do_offer(""Done!"", ""That is not enough."", ""That is worse than before!"", ""I tire of this."", ""Thou hast offered nothing."")) {
                    finished = 1;
                }
            ""I demand that thou givest me these items."":
                if (do_demand(""Take them, then."", ""Never! Thou shalt pay for that!"")) {
                    finished = 1;
                } else {
                    attack_and_end();
                }
            ""Excuse me, I must think about this deal."":
                do_judgement();
            ""I do not wish to barter any further."":
                do_decline();
                finished = 1;
        }
    }
}";

        const string Password = @"ask_for_the_password() {
    int typed;
    say ""Halt! What is the password?"";
    menu {
        ""Other..."":
            typed = babl_ask();
            if (contains(""Deco Morono"", &typed)) {
                say ""Pass, friend."";
            } else {
                say ""That's not it! Begone!"";
                upset_and_end();
            }
        ""I do not know it."":
            say ""Then thou shalt not pass."";
    }
}";

        const string MetA = @"set_quest(12, 1);";

        const string MetB = @"greet_hagbards_friend() {
    if (get_quest(12) == 1) {
        say ""Hagbard told me thou wouldst come. Welcome, friend."";
    } else {
        say ""Thou art new here. Speak with Hagbard, our leader, first."";
    }
}";

        // ----- helpers -----

        /// <summary>The first choice, then the last when a menu comes round again (as ConversationCompilerTests).</summary>
        static int FirstThenLast(SimMenu m, int seen) { return seen == 0 ? 1 : m.Ids.Count; }

        static string Replace(string text, string what, string with)
        {
            int at = text.IndexOf(what, StringComparison.Ordinal);
            Assert.True(at >= 0, "not found: " + what);
            return text.Substring(0, at) + with + text.Substring(at + what.Length);
        }

        /// <summary>The source with the recipe's function added at the end and a statement put first in main.</summary>
        static string Splice(string source, string function, string first)
        {
            int at = source.IndexOf("main() {", StringComparison.Ordinal);
            Assert.True(at >= 0);
            int line = source.IndexOf('\n', at) + 1;
            string text = source.Substring(0, line) + "    " + first + "\n" + source.Substring(line);
            return function == null ? text : text.TrimEnd('\n') + "\n\n" + function.Replace("\r\n", "\n") + "\n";
        }

        static CompileResult Compile(ConversationLibrary lib, Conversation c, string text)
        {
            var r = lib.Compile(c, text);
            Assert.True(r.Ok, "slot " + c.Slot + ": " + string.Join("; ", r.Errors));
            return r;
        }

        static ConversationSimulator Run(GameSession s, CompileResult r, Func<SimMenu, int, int> path, string answer = "yes", Action<SimWorld> setup = null)
        {
            var world = SimWorld.FromGame(s, r.Conversation.Slot, null);
            if (setup != null) setup(world);
            var sim = new ConversationSimulator(r.Conversation, r.Strings, world, (b, i) => s.Strings.Get(b, i), SimWorld.SavedGlobals(s, r.Conversation.Slot)) { LogInstructions = false };
            sim.RunToEnd(path, () => answer, 300000);
            Assert.True(sim.State == SimState.Ended, sim.Error);
            return sim;
        }

        static int Imported(ConversationSimulator sim, Conversation c, string name)
        {
            return sim.GetMem(c.Imports.First(i => !i.IsFunction && i.Name == name).Index);
        }

        /// <summary>The player has the object: in the hand, or put away (the simulator puts the hand away at the next menu).</summary>
        static bool Has(ConversationSimulator sim, Func<SimObject, bool> which)
        {
            return sim.World.PlayerInventory.Concat(new[] { sim.World.InHand }).Select(i => sim.World.Get(i)).Any(o => o != null && which(o));
        }

        static string ThisFile([CallerFilePath] string path = null) { return path; }

        static List<string> Said(ConversationSimulator sim) { return sim.Output.Where(o => o.Kind == SimOutputKind.Say).Select(o => o.Text).ToList(); }

        static ConversationLibrary Open(out GameSession s)
        {
            s = null;
            string g = GameData.Game("UW"); if (g == null) return null;
            s = GameSession.Open(g);
            return ConversationLibrary.Open(s);
        }

        // ----- the recipes -----

        [Fact]
        public void The_manual_has_each_recipe_as_written_here()
        {
            string path = Path.GetFullPath(Path.Combine(Path.GetDirectoryName(ThisFile()), "..", "..", "docs", "MANUAL.md"));
            Assert.True(File.Exists(path), path);
            string manual = File.ReadAllText(path).Replace("\r\n", "\n");
            foreach (var recipe in new[] { Trade, Gift, Ambush, Barter, Password, MetA, MetB })
                Assert.Contains("```\n" + recipe.Replace("\r\n", "\n") + "\n```", manual);
        }

        [Fact]
        public void Trading_an_item_for_another()
        {
            GameSession s; var lib = Open(out s); if (lib == null) return;
            var c = lib.Archive.Read(17);   // the old troll who trades dragon scales for rotworm stew
            var r = Compile(lib, c, Splice(lib.Decompile(c).Text, Trade, "trade_stew_for_scales();"));
            int stew = 0, scales = 0;
            var sim = Run(s, r, FirstThenLast, setup: w =>
            {
                stew = w.Add(283).Index;
                w.PlayerSlots[0] = stew; w.PlayerSelected[0] = true;
                scales = w.Add(285).Index;
                w.NpcInventory.Insert(0, scales);
            });
            Assert.Contains(stew, sim.World.NpcInventory);
            Assert.True(Has(sim, o => o.Index == scales));
            Assert.DoesNotContain("Thou hast not offered me any rotworm stew.", Said(sim));
            // nothing offered: nothing given
            var none = Run(s, r, FirstThenLast);
            Assert.Equal("Thou hast not offered me any rotworm stew.", Said(none).First());
        }

        [Fact]
        public void Giving_an_item_after_a_menu_choice_once()
        {
            GameSession s; var lib = Open(out s); if (lib == null) return;
            var c = lib.Archive.Read(1);
            // global34 is past Corby's 34 variables: an error until the count is raised (the Script tab's .variables)
            Assert.False(lib.Compile(c, Splice(lib.Decompile(c).Text, Gift, "offer_bread();")).Ok);
            c.Variables = 35;
            var r = Compile(lib, c, Splice(lib.Decompile(c).Text, Gift, "offer_bread();"));
            var sim = Run(s, r, FirstThenLast);
            Assert.True(Has(sim, o => o.Item == 177));
            Assert.Equal(1, sim.GetMem(34));
            // the second choice gives nothing
            var no = Run(s, r, (m, seen) => m.Texts.Contains("No, keep it.") ? 2 : FirstThenLast(m, seen));
            Assert.False(Has(no, o => o.Item == 177));
            Assert.Equal(0, no.GetMem(34));
        }

        [Fact]
        public void An_npc_that_threatens_and_attacks()
        {
            GameSession s; var lib = Open(out s); if (lib == null) return;
            var c = lib.Archive.Read(142);   // Rodrick, who waits with goal 10 and attacks after a line
            var r = Compile(lib, c, Splice(lib.Decompile(c).Text, Ambush, "thou_hast_come_far_enough();"));
            var sim = Run(s, r, FirstThenLast);
            Assert.StartsWith("Thou hast come far enough, ", Said(sim)[0]);
            Assert.DoesNotContain("@", Said(sim)[0]);
            Assert.Equal("Thy words mean nothing to me.", Said(sim)[1]);
            Assert.Equal(2, Said(sim).Count);
            Assert.Equal(0, Imported(sim, c, "npc_attitude"));
            Assert.Equal(5, Imported(sim, c, "npc_goal"));
            Assert.Equal(1, Imported(sim, c, "npc_gtarg"));
        }

        [Fact]
        public void Bartering_with_likes_and_dislikes()
        {
            GameSession s; var lib = Open(out s); if (lib == null) return;
            var c = lib.Archive.Read(276);   // a mountainman trader
            var r = Compile(lib, c, Splice(lib.Decompile(c).Text, Barter, "trade_goods();"));
            // after Apply the Dialogue tab shows do_offer's lines as their numbers
            int done = r.Strings.IndexOf("Done!");
            Assert.Contains("do_offer(" + done + ", " + (done + 1) + ", ", lib.Decompile(r.Conversation).Text);
            // an offer with nothing selected, then decline
            var sim = Run(s, r, (m, seen) => m.Texts.Contains("I make thee this offer.") ? (seen == 0 ? 1 : 4) : FirstThenLast(m, seen));
            Assert.Contains("Thou hast offered nothing.", Said(sim));
            Assert.Contains(sim.Log, l => l.Text.Contains("set_likes_dislikes"));
        }

        [Fact]
        public void A_password_typed_in()
        {
            GameSession s; var lib = Open(out s); if (lib == null) return;
            var c = lib.Archive.Read(21);   // Steeltoe, who asks for "Deco Morono"
            var r = Compile(lib, c, Splice(lib.Decompile(c).Text, Password, "ask_for_the_password();"));
            var right = Run(s, r, FirstThenLast, "deco morono, I say");
            Assert.Equal("Pass, friend.", Said(right)[1]);
            var wrong = Run(s, r, FirstThenLast, "open sesame");
            Assert.Equal("That's not it! Begone!", Said(wrong)[1]);
            Assert.Equal(1, Imported(wrong, c, "npc_attitude"));
            // a word inside another is not the word
            var inside = Run(s, r, FirstThenLast, "decomorono");
            Assert.Equal("That's not it! Begone!", Said(inside)[1]);
        }

        [Fact]
        public void One_npc_knows_the_player_met_another()
        {
            GameSession s; var lib = Open(out s); if (lib == null) return;
            var a = lib.Archive.Read(19);   // Hagbard, who sets quest 1 when met
            var ra = Compile(lib, a, Replace(lib.Decompile(a).Text, "        set_quest(1, 1);\n", "        set_quest(1, 1);\n        " + MetA + "\n"));
            var simA = Run(s, ra, FirstThenLast);
            Assert.Equal(1, simA.World.QuestFlag(12));

            var b = lib.Archive.Read(20);   // Gulik, who reads quest 1
            var rb = Compile(lib, b, Splice(lib.Decompile(b).Text, MetB, "greet_hagbards_friend();"));
            var met = Run(s, rb, FirstThenLast, setup: w => w.SetQuestFlag(12, 1));
            Assert.Equal("Hagbard told me thou wouldst come. Welcome, friend.", Said(met)[0]);
            var notMet = Run(s, rb, FirstThenLast);
            Assert.Equal("Thou art new here. Speak with Hagbard, our leader, first.", Said(notMet)[0]);
        }
    }
}
