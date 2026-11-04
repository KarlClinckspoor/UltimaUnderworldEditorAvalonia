using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using UwEditor.Core.Editing;
using Xunit;

namespace UwEditor.Core.Tests
{
    /// <summary>Setting up a check in Underworld Exhumed, reading what the port says, and exporting a saved game.</summary>
    public class ExhumedCheckTests
    {
        static string Temp(string name) { return Path.Combine(Path.GetTempPath(), "uwedit-" + name + "-" + Guid.NewGuid().ToString("N").Substring(0, 8)); }

        static LevelEdits EditOneTile(GameSession s, int level)
        {
            var edits = new LevelEdits(s);
            var e = edits.For(level);
            var t = Enumerable.Range(0, 64 * 64).Select(i => (X: i % 64, Y: i / 64)).First(k => !e.Model.Tiles[k.X, k.Y].IsSolid && e.Model.Tiles[k.X, k.Y].Height < 10);
            e.SetTileField(t.X, t.Y, "Floor height", e.Model.Tiles[t.X, t.Y].Height + 1);
            return edits;
        }

        [Fact]
        public void A_uw1_check_without_a_save_makes_a_new_character_and_visits_each_level()
        {
            string g = GameData.Game("UW"); if (g == null) return;
            var s = GameSession.Open(g);
            var edits = EditOneTile(s, 2);
            string dir = Temp("exhumed");
            try
            {
                var plan = ExhumedCheck.Prepare(s, edits, null, new[] { 2, 5 }, dir);
                Assert.True(plan.NewCharacter);
                Assert.True(plan.SavesAtEnd);
                Assert.Equal(new[] { 2, 5 }, plan.Visits.Select(v => v.Level));
                string script = File.ReadAllText(plan.Script);
                Assert.Contains("Create Character", script);
                foreach (var v in plan.Visits)
                {
                    Assert.Contains("cheat goto " + v.Level + " " + v.X + " " + v.Y, script);
                    var m = edits.Model(v.Level);
                    Assert.Equal(TileType.Open, m.Tiles[v.X, v.Y].Type);
                    Assert.Equal(0, m.Tiles[v.X, v.Y].FirstObject);
                }
                Assert.Contains("key s", script);
                // the copy's DATA holds the edits, the game folder does not
                var copy = File.ReadAllBytes(Path.Combine(plan.Game, "DATA", "LEV.ARK"));
                Assert.Equal(edits.ToBytes(), copy);
                Assert.NotEqual(File.ReadAllBytes(Path.Combine(g, "DATA", "LEV.ARK")), copy);
                Assert.Contains("--hidden", plan.Arguments);
                Assert.Contains("--input-script", plan.Arguments);
                Assert.Equal("1", plan.Environment["EXHUME_TEST_CHEATS"]);
                Assert.True(plan.ExitAfterMs > plan.Visits.Last().At);
                // a second check clears the first; a folder it did not make is refused
                ExhumedCheck.Prepare(s, edits, null, new[] { 2 }, dir);
                string other = Temp("other");
                Directory.CreateDirectory(other); File.WriteAllText(Path.Combine(other, "mine.txt"), "x");
                Assert.Throws<InvalidOperationException>(() => ExhumedCheck.Prepare(s, edits, null, new[] { 2 }, other));
                Directory.Delete(other, true);
                Assert.Throws<InvalidOperationException>(() => ExhumedCheck.Prepare(s, edits, null, new[] { 2 }, g));
            }
            finally { try { Directory.Delete(dir, true); } catch (Exception) { } }
        }

        [Fact]
        public void A_uw2_check_loads_a_saved_game_holding_the_edits()
        {
            string g = GameData.Game("UW2"); if (g == null) return;
            var s = GameSession.Open(g);
            var edits = EditOneTile(s, 1);
            string dir = Temp("exhumed2");
            try
            {
                var plan = ExhumedCheck.Prepare(s, edits, null, new[] { 1 }, dir);
                Assert.False(plan.NewCharacter);
                Assert.False(plan.SavesAtEnd);          // UW2's save menu is not scripted
                Assert.Contains("skip-intro", plan.Arguments);
                Assert.Contains("Journey Onward", File.ReadAllText(plan.Script));
                Assert.Equal(edits.ToBytes(), File.ReadAllBytes(Path.Combine(plan.Home, "SAVE1", "LEV.ARK")));
                Assert.True(File.Exists(Path.Combine(plan.Home, "SAVE1", "PLAYER.DAT")));
                Assert.False(Directory.Exists(Path.Combine(plan.Game, "SAVE1")));
            }
            finally { try { Directory.Delete(dir, true); } catch (Exception) { } }
        }

        [Fact]
        public void The_port_output_is_read_for_crashes_refusals_and_fatal_errors()
        {
            string g = GameData.Game("UW"); if (g == null) return;
            var s = GameSession.Open(g);
            string dir = Temp("exhumed3");
            try
            {
                var plan = ExhumedCheck.Prepare(s, new LevelEdits(s), null, new[] { 1, 3 }, dir, false);
                var good = ExhumedCheck.Analyze(s, plan, 0, "uw1port: cheat: goto 1 30 30 (not as DOS)\nuw1port: cheat: goto 3 31 31 (not as DOS)\n", false);
                Assert.True(good.Passed, good.Report());
                Assert.Equal(new[] { 1, 3 }, good.Reached);
                var refused = ExhumedCheck.Analyze(s, plan, 0, "uw1port: cheat: goto 1 30 30 (not as DOS)\nuw1port: cheat: goto 3 31 31: no free square there (not as DOS)\n", false);
                Assert.False(refused.Passed);
                Assert.Contains(refused.Problems, p => p.Contains("no free square"));
                var crash = ExhumedCheck.Analyze(s, plan, 139, "uw1port: cheat: goto 1 30 30 (not as DOS)\nuw1port: host fault; the call stack:\n", false);
                Assert.Contains(crash.Problems, p => p.Contains("crashed"));
                Assert.Contains(crash.Problems, p => p.Contains("Level 3 was never reached"));
                var fatal = ExhumedCheck.Analyze(s, plan, 232, "Underworld can no longer run.  Error code A123.\n", false);
                Assert.Contains(fatal.Problems, p => p.Contains("fatal error"));
                var hung = ExhumedCheck.Analyze(s, plan, -1, "", true);
                Assert.Contains(hung.Problems, p => p.Contains("did not finish"));
            }
            finally { try { Directory.Delete(dir, true); } catch (Exception) { } }
        }

        [Fact]
        public void A_fake_port_run_end_to_end_reads_back_the_saved_levels()
        {
            if (OperatingSystem.IsWindows()) return;
            string g = GameData.Game("UW"); if (g == null) return;
            var s = GameSession.Open(g);
            var edits = EditOneTile(s, 1);
            string dir = Temp("exhumed4");
            string fake = Path.Combine(Path.GetTempPath(), "uwedit-fakeport-" + Guid.NewGuid().ToString("N").Substring(0, 8) + ".sh");
            try
            {
                var plan = ExhumedCheck.Prepare(s, edits, null, new[] { 1 }, dir);
                // the fake port: says each cheat ran, and "saves" a new game in slot I with the copy's levels
                File.WriteAllText(fake, "#!/bin/sh\nwhile [ $# -gt 0 ]; do case \"$1\" in --home) H=\"$2\"; shift;; --data) D=\"$2\"; shift;; --input-script) S=\"$2\"; shift;; esac; shift; done\n" +
                    "[ \"$EXHUME_TEST_CHEATS\" = 1 ] || { echo 'uw1port: cheat: refused without EXHUME_TEST_CHEATS=1' >&2; exit 1; }\n" +
                    "grep -o 'cheat goto [0-9]* [0-9]* [0-9]*' \"$S\" | while read a b l x y; do echo \"uw1port: cheat: goto $l $x $y (not as DOS)\" >&2; done\n" +
                    "mkdir -p \"$H/SAVE1\"; sleep 1; cp \"$D/DATA/LEV.ARK\" \"$H/SAVE1/LEV.ARK\"; exit 0\n");
                Process.Start("chmod", new[] { "+x", fake }).WaitForExit();
                var psi = new ProcessStartInfo(fake) { UseShellExecute = false, RedirectStandardError = true, RedirectStandardOutput = true };
                foreach (var a in plan.Arguments) psi.ArgumentList.Add(a);
                foreach (var kv in plan.Environment) psi.Environment[kv.Key] = kv.Value;
                var p = Process.Start(psi);
                string output = p.StandardError.ReadToEnd() + p.StandardOutput.ReadToEnd();
                p.WaitForExit();
                var r = ExhumedCheck.Analyze(s, plan, p.ExitCode, output, false);
                Assert.True(r.Passed, r.Report() + "\n" + output);
                Assert.True(r.Resaved);
                Assert.Equal(new[] { 1 }, r.Reached);
            }
            finally { try { Directory.Delete(dir, true); File.Delete(fake); } catch (Exception) { } }
        }

        [Fact]
        public void Edits_export_as_a_saved_game_into_a_new_or_save_folder_only()
        {
            string dir = Temp("export");
            try
            {
                // UW1 with no saved game cannot make one
                string g1 = GameData.Game("UW");
                if (g1 != null && !HasSave(g1)) Assert.Throws<InvalidOperationException>(() => ExhumedCheck.WriteSave(GameSession.Open(g1), null, null, Path.Combine(dir, "x"), null));
                // the rest takes the player from one of UW2's saved games (SAVEn), used when there is one
                string g = GameData.Game("UW2"); if (g == null || !HasSave(g)) return;
                var s = GameSession.Open(g);
                var edits = EditOneTile(s, 1);
                string slot = Path.Combine(dir, "SAVE3");
                ExhumedCheck.WriteSave(s, edits, null, slot, "Editor test");
                Assert.Equal(edits.ToBytes(), File.ReadAllBytes(Path.Combine(slot, "LEV.ARK")));
                Assert.True(File.Exists(Path.Combine(slot, "PLAYER.DAT")));
                var ark = UltimateEditor.Games.Archive.Open(File.ReadAllBytes(Path.Combine(slot, "LEV.ARK")), s.Kind);
                Assert.Equal(edits.For(1).Data, ark.Read(s.Profile.LevelBlock(1)));
                // again over it (a save folder) is fine; over a folder of other files is not
                ExhumedCheck.WriteSave(s, edits, null, slot, "Editor test");
                string other = Path.Combine(dir, "other");
                Directory.CreateDirectory(other); File.WriteAllText(Path.Combine(other, "notes.txt"), "x");
                Assert.Throws<InvalidOperationException>(() => ExhumedCheck.WriteSave(s, edits, null, other, null));
            }
            finally { try { Directory.Delete(dir, true); } catch (Exception) { } }
        }

        /// <summary>Whether the game folder has a saved game (a SAVEn folder with PLAYER.DAT).</summary>
        static bool HasSave(string game)
        {
            return Directory.GetDirectories(game, "SAVE*").Any(d => File.Exists(Path.Combine(d, "PLAYER.DAT")));
        }
    }
}
