using System;
using System.IO;
using System.Linq;
using UwEditor.Core.Editing;
using Xunit;

namespace UwEditor.Core.Tests
{
    /// <summary>A thorough check: stops beside the edits, a sweep of each level, and a look round at each stop.</summary>
    public class ExhumedStopsTests
    {
        static string Temp(string name) { return Path.Combine(Path.GetTempPath(), "uwedit-" + name + "-" + Guid.NewGuid().ToString("N").Substring(0, 8)); }

        [Fact]
        public void Edits_and_a_sweep_add_stops_and_a_look_turns_once_at_each()
        {
            string g = GameData.Game("UW"); if (g == null) return;
            var s = GameSession.Open(g);
            var edits = new LevelEdits(s);
            var e = edits.For(2);
            // two edits far from the level's own square: tiles beside two squares of the sweep, raised a step
            var m = e.Model;
            var home = ExhumedCheck.Stops(s, null, 2, new ExhumedOptions())[0];
            var cands = ExhumedCheck.Stops(s, null, 2, new ExhumedOptions { Sweep = true }).Where(v => Math.Max(Math.Abs(v.X - home.X), Math.Abs(v.Y - home.Y)) >= 12).ToList();
            var farOnes = new System.Collections.Generic.List<ExhumedVisit> { cands[0] };
            farOnes.Add(cands.First(v => Math.Max(Math.Abs(v.X - cands[0].X), Math.Abs(v.Y - cands[0].Y)) >= 12));
            Assert.Equal(2, farOnes.Count);
            var a = (X: farOnes[0].X + 1, Y: farOnes[0].Y);
            var b = (X: farOnes[1].X + 1, Y: farOnes[1].Y);
            e.SetTileField(a.X, a.Y, "Floor height", m.Tiles[a.X, a.Y].Height + 1);
            e.SetTileField(b.X, b.Y, "Floor height", e.Model.Tiles[b.X, b.Y].Height + 1);
            var spots = ExhumedCheck.EditedSpots(s, edits, 2);
            Assert.Contains((a.X, a.Y), spots);
            Assert.Contains((b.X, b.Y), spots);
            Assert.Equal(2, spots.Count);

            var plain = ExhumedCheck.Stops(s, edits, 2, new ExhumedOptions());
            Assert.Single(plain);
            var byEdits = ExhumedCheck.Stops(s, edits, 2, new ExhumedOptions { VisitEdits = true });
            Assert.True(byEdits.Count == 3, string.Join("; ", byEdits.Select(v => v.Why + " " + v.X + "," + v.Y)) + " | spots " + string.Join("; ", spots) + " | far " + string.Join("; ", farOnes.Select(v => v.X + "," + v.Y)));
            foreach (var v in byEdits.Where(v => v.Why == "edit"))
                Assert.True(spots.Any(sp => Math.Max(Math.Abs(sp.X - v.X), Math.Abs(sp.Y - v.Y)) <= 4));
            var sweep = ExhumedCheck.Stops(s, null, 2, new ExhumedOptions { Sweep = true });
            Assert.True(sweep.Count > 4 && sweep.Count <= 17);
            foreach (var v in sweep)
            {
                Assert.True(ExhumedCheck.Standable(s, s.LoadLevel(2), null, v.X, v.Y));
                Assert.True(sweep.Where(w => w != v).All(w => Math.Max(Math.Abs(w.X - v.X), Math.Abs(w.Y - v.Y)) >= 5));
            }

            string dir = Temp("stops");
            try
            {
                var o = new ExhumedOptions { VisitEdits = true, Sweep = true, LookAround = true };
                var plan = ExhumedCheck.Prepare(s, edits, null, new[] { 2 }, dir, true, true, o);
                Assert.Equal(ExhumedCheck.Stops(s, edits, 2, o).Count, plan.Visits.Count);
                var lines = File.ReadAllLines(plan.Script).Select(l => l.Split('#')[0].Trim()).Where(l => l.Length > 0).ToList();
                Assert.Equal(plan.Visits.Count, lines.Count(l => l.Contains("cheat goto 2 ")));
                Assert.Equal(plan.Visits.Count, lines.Count(l => l.EndsWith("down a")));
                Assert.Equal(plan.Visits.Count, lines.Count(l => l.EndsWith("up a")));
                var times = lines.Select(l => int.Parse(l.Split(' ')[0])).ToList();
                Assert.Equal(times.OrderBy(x => x), times);
                // each turn ends before the next stop
                for (int i = 0; i < lines.Count; i++)
                    if (lines[i].EndsWith("up a") && i + 1 < lines.Count) Assert.True(times[i + 1] > times[i]);
                Assert.Contains("stops", plan.Note);

                // the report: stops reached counted, a missing stop named
                var text = string.Join("\n", plan.Visits.Skip(1).Select(v => "uw1port: cheat: goto " + v.Level + " " + v.X + " " + v.Y + " (not as DOS)"));
                var r = ExhumedCheck.Analyze(s, plan, 0, text, false);
                var first = plan.Visits[0];
                Assert.Contains(r.Problems, p => p.Contains("(" + first.X + ", " + first.Y + ") on level 2 was never reached"));
                Assert.Contains(r.Notes, n => n.Contains("stopped at " + (plan.Visits.Count - 1) + " of " + plan.Visits.Count));
            }
            finally { try { Directory.Delete(dir, true); } catch (Exception) { } }
        }
    }
}
