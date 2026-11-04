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
        public void The_kept_alive_strength_never_wraps_the_hit_points()
        {
            // the game keeps the maximum hit points in a byte (avghit): 30 + level × strength / 5 must stay within 255
            for (int level = 1; level <= ExhumedCheck.TopLevel; level++)
            {
                int str = ExhumedCheck.AliveStrengthAt(level);
                Assert.InRange(str, 1, 99);
                Assert.InRange(ExhumedCheck.MaxHpAt(level, str), 1, 255);
                Assert.True(str == 99 || ExhumedCheck.MaxHpAt(level, str + 1) > 255);   // the most that fits
            }
            Assert.Equal(99, ExhumedCheck.AliveStrengthAt(1));
            Assert.Equal(70, ExhumedCheck.AliveStrengthAt(16));
            Assert.Equal(254, ExhumedCheck.MaxHpAt(16, 70));
        }

        [Fact]
        public void A_check_from_a_saved_game_makes_the_player_level_16_with_full_hit_points()
        {
            string g = GameData.Game("UW2"); if (g == null) return;
            if (!Enumerable.Range(1, 4).Any(i => File.Exists(Path.Combine(g, "SAVE" + i, "PLAYER.DAT")))) return;
            var s = GameSession.Open(g);
            string dir = Temp("level16");
            try
            {
                var plan = ExhumedCheck.Prepare(s, new LevelEdits(s), null, new[] { 1 }, dir);
                Assert.False(plan.NewCharacter);
                Assert.Equal(16, plan.AliveLevel);
                Assert.Equal(70, plan.AliveStrength);
                Assert.Equal(254, plan.AliveHp);
                var pd = UltimateEditor.Games.PlayerDat.Parse(File.ReadAllBytes(Path.Combine(plan.Home, "SAVE1", "PLAYER.DAT")), s.Kind);
                Assert.Equal(16, pd.Level);
                Assert.True(pd.ExperienceTenths >= 960000);
                Assert.Equal(254, pd.MaxVitality);
                Assert.Equal(254, pd.Vitality);
                // the save the game's own is compared with holds the same player
                var before = UltimateEditor.Games.PlayerDat.Parse(File.ReadAllBytes(Path.Combine(plan.Before, "PLAYER.DAT")), s.Kind);
                Assert.Equal(16, before.Level);
                var script = File.ReadAllText(plan.Script);
                Assert.Contains("cheat set str 70", script);
                Assert.Contains("cheat set hp 254", script);
                Assert.DoesNotContain("cheat set str 99", script);
                Assert.Contains("experience level 16", plan.Note);
                // not kept alive: the player is left as saved
                var off = ExhumedCheck.Prepare(s, new LevelEdits(s), null, new[] { 1 }, dir, true, false);
                var asSaved = UltimateEditor.Games.PlayerDat.Parse(File.ReadAllBytes(Path.Combine(off.Home, "SAVE1", "PLAYER.DAT")), s.Kind);
                var source = UltimateEditor.Games.PlayerDat.Parse(File.ReadAllBytes(UltimateEditor.Games.GameProfile.Find(ExhumedCheck.SaveSource(s), "PLAYER.DAT")), s.Kind);
                Assert.Equal(source.Level, asSaved.Level);
            }
            finally { try { Directory.Delete(dir, true); } catch (Exception) { } }
        }

        [Fact]
        public void The_port_runs_hidden_unless_its_window_is_asked_for()
        {
            string g = GameData.Game("UW"); if (g == null) return;
            var s = GameSession.Open(g);
            string dir = Temp("shown");
            try
            {
                var hidden = ExhumedCheck.Prepare(s, new LevelEdits(s), null, new[] { 2 }, dir);
                Assert.False(hidden.Shown);
                Assert.Contains("--hidden", hidden.Arguments);
                var shown = ExhumedCheck.Prepare(s, new LevelEdits(s), null, new[] { 2 }, dir, true, true, null, true);
                Assert.True(shown.Shown);
                Assert.DoesNotContain("--hidden", shown.Arguments);
                Assert.Contains("--input-script", shown.Arguments);
                Assert.Contains("--exit-after", shown.Arguments);
                Assert.Contains("window shown", shown.Note);
            }
            finally { try { Directory.Delete(dir, true); } catch (Exception) { } }
        }

        [Fact]
        public void UW1_level_9_goes_last_after_the_save_and_its_port_crash_is_a_note()
        {
            string g = GameData.Game("UW"); if (g == null) return;
            var s = GameSession.Open(g);
            Assert.Equal(9, ExhumedCheck.PortCrashLevel(s.Kind));
            Assert.Equal(0, ExhumedCheck.PortCrashLevel(UltimateEditor.Games.GameKind.UW2));
            string dir = Temp("level9");
            try
            {
                var plan = ExhumedCheck.Prepare(s, new LevelEdits(s), null, new[] { 9, 2 }, dir);
                Assert.True(plan.CrashLevelLast);
                Assert.Equal(new[] { 2, 9 }, plan.Visits.Select(v => v.Level).Distinct().ToArray());
                var lines = File.ReadAllLines(plan.Script).Select(l => l.Split('#')[0].Trim()).Where(l => l.Length > 0).ToList();
                int save = lines.FindIndex(l => l.EndsWith("key s"));
                int go2 = lines.FindIndex(l => l.Contains("cheat goto 2 "));
                int go9 = lines.FindIndex(l => l.Contains("cheat goto 9 "));
                Assert.True(go2 >= 0 && save > go2 && go9 > save, "the save comes between level 2 and level 9");
                Assert.Equal(1, lines.Count(l => l.EndsWith("key s")));
                var times = lines.Select(l => int.Parse(l.Split(' ')[0])).ToList();
                Assert.Equal(times.OrderBy(x => x), times);
                Assert.Contains("before level 9", plan.Note);

                var v2 = plan.Visits.First(v => v.Level == 2); var v9 = plan.Visits.First(v => v.Level == 9);
                string Goto(ExhumedVisit v) => "uw1port: cheat: goto " + v.Level + " " + v.X + " " + v.Y + " (not as DOS)\n";
                // the game saved before level 9 (as the port does): the levels as they are
                string lev = Path.GetFileName(s.LevArkPath);
                Directory.CreateDirectory(Path.Combine(plan.Home, "SAVE1"));
                File.Copy(Path.Combine(plan.Game, Path.GetFileName(Path.GetDirectoryName(s.LevArkPath)), lev), Path.Combine(plan.Home, "SAVE1", lev), true);
                // the port crashes on arriving at level 9: passed, with a note
                var r =ExhumedCheck.Analyze(s, plan, 3, Goto(v2) + Goto(v9) + "uw1port: host fault; the call stack:\n(no call stack on this host)\n", false);
                Assert.Equal(9, r.PortCrashedOn);
                Assert.DoesNotContain(r.Problems, p => p.Contains("crashed") || p.Contains("halted") || p.Contains("never reached"));
                Assert.Contains(r.Notes, n => n.Contains("Underworld Exhumed crashed on arriving at level 9"));
                Assert.StartsWith("PASSED for the rest; level 9 could not be checked", r.Report());
                // a crash on another level is still a problem
                var other = ExhumedCheck.Analyze(s, plan, 3, Goto(v2) + "uw1port: host fault; the call stack:\n", false);
                Assert.Equal(0, other.PortCrashedOn);
                Assert.Contains(other.Problems, p => p.Contains("crashed"));
                Assert.Contains(other.Problems, p => p.Contains("never reached"));
                // without level 9, nothing changes
                var plain = ExhumedCheck.Prepare(s, new LevelEdits(s), null, new[] { 2 }, dir);
                Assert.False(plain.CrashLevelLast);
                Assert.DoesNotContain("level 9", File.ReadAllText(plain.Script));
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
