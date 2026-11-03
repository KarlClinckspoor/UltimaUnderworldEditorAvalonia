using System;
using System.IO;
using System.Linq;
using UwEditor.Core.Editing;
using Xunit;
using Xunit.Abstractions;

namespace UwEditor.Core.Tests
{
    /// <summary>The check's setup: every level with nowhere to stand named at once, the player kept alive, and the port's other messages read.</summary>
    public class ExhumedGroundTests
    {
        readonly ITestOutputHelper output;
        public ExhumedGroundTests(ITestOutputHelper output) { this.output = output; }

        static string Temp(string name) { return Path.Combine(Path.GetTempPath(), "uwedit-" + name + "-" + Guid.NewGuid().ToString("N").Substring(0, 8)); }

        [Theory]
        [InlineData("UW")]
        [InlineData("UW2")]
        public void Every_level_with_nowhere_to_stand_is_named_at_once(string game)
        {
            string g = GameData.Game(game); if (g == null) return;
            var s = GameSession.Open(g);
            var edits = new LevelEdits(s);
            var all = s.LevelSlots().Where(x => x.Present).Select(x => x.Level).ToList();
            // make two levels solid rock, so at least those have nowhere to stand
            var rock = all.Take(2).ToList();
            foreach (int l in rock)
            {
                var e = edits.For(l);
                e.EditBytes("rock", (d, tex) => { for (int i = 0; i < 64 * 64; i++) { d[i * 4] = (byte)(d[i * 4] & 0xF0); } });
            }
            var nowhere = ExhumedCheck.LevelsWithNowhereToStand(s, edits, all);
            output.WriteLine(game + ": " + string.Join(", ", nowhere));
            foreach (int l in rock) Assert.Contains(l, nowhere);
            string dir = Temp("ground");
            try
            {
                var ex = Assert.Throws<NowhereToStandException>(() => ExhumedCheck.Prepare(s, edits, null, all, dir));
                Assert.Equal(nowhere, ex.Levels);
                foreach (int l in nowhere) Assert.Contains(l.ToString(), ex.Message);
                // without them the check sets up
                var plan = ExhumedCheck.Prepare(s, edits, null, all.Except(nowhere).ToList(), dir);
                Assert.Equal(all.Count - nowhere.Count, plan.Visits.Count);
            }
            finally { try { Directory.Delete(dir, true); } catch (Exception) { } }
        }

        [Fact]
        public void The_player_is_kept_alive_before_each_level()
        {
            string g = GameData.Game("UW"); if (g == null) return;
            var s = GameSession.Open(g);
            string dir = Temp("alive");
            try
            {
                var plan = ExhumedCheck.Prepare(s, new LevelEdits(s), null, new[] { 2, 3 }, dir);
                Assert.True(plan.KeepAlive);
                Assert.Equal(30 + 1 * 99 / 5, plan.AliveHp);       // a new character is level 1
                var lines = File.ReadAllLines(plan.Script).Select(l => l.Split('#')[0].Trim()).Where(l => l.Length > 0).ToList();
                int str = lines.FindIndex(l => l.EndsWith("cheat set str 99"));
                int def = lines.FindIndex(l => l.EndsWith("cheat set defense 30"));
                Assert.True(str >= 0 && def > str);
                foreach (var v in plan.Visits)
                {
                    int go = lines.FindIndex(l => l.EndsWith("cheat goto " + v.Level + " " + v.X + " " + v.Y));
                    Assert.True(go > def);
                    Assert.EndsWith("cheat set hp " + plan.AliveHp, lines[go - 1]);
                }
                // times rise through the script
                var times = lines.Select(l => int.Parse(l.Split(' ')[0])).ToList();
                Assert.Equal(times.OrderBy(x => x), times);
                var off = ExhumedCheck.Prepare(s, new LevelEdits(s), null, new[] { 2 }, dir, true, false);
                Assert.DoesNotContain("cheat set", File.ReadAllText(off.Script));
            }
            finally { try { Directory.Delete(dir, true); } catch (Exception) { } }
        }

        [Fact]
        public void Null_pointer_reads_and_refused_set_cheats_are_reported_as_notes()
        {
            string g = GameData.Game("UW"); if (g == null) return;
            var s = GameSession.Open(g);
            string dir = Temp("notes");
            try
            {
                var plan = ExhumedCheck.Prepare(s, new LevelEdits(s), null, new[] { 2 }, dir, false);
                var v = plan.Visits[0];
                string text = "uw1port: cheat: set str 99 (not as DOS)\n" +
                              "uw1port: cheat: set hp 49: hp 1 to the player's maximum (set FIELD VALUE) (not as DOS)\n" +
                              "uw1port: cheat: goto " + v.Level + " " + v.X + " " + v.Y + " (not as DOS)\n" +
                              "uw1port: far null pointer read at src/critter/AI.C:812\n";
                var r = ExhumedCheck.Analyze(s, plan, 0, text, false);
                Assert.True(r.Passed, r.Report());
                Assert.Contains(r.Notes, n => n.Contains("set hp 49") && n.Contains("refused"));
                Assert.Contains(r.Notes, n => n.Contains("AI.C:812") && n.Contains("after going to level 2"));
            }
            finally { try { Directory.Delete(dir, true); } catch (Exception) { } }
        }
    }
}
