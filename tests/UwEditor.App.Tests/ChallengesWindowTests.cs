using System;
using System.IO;
using System.Linq;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using UwEditor.Core.Randomizer;
using UwEditor.Core.Tests;
using Xunit;

namespace UwEditor.App.Tests
{
    /// <summary>Randomizer ▸ Named challenges: the list, conflicts, Preview, Apply into the levels and the object tables, and Undo.</summary>
    public class ChallengesWindowTests
    {
        static string Uw1 { get { return GameData.Game("UW"); } }
        static string Uw2 { get { return GameData.Game("UW2"); } }

        static MainWindow OpenQuiet(string folder)
        {
            var w = Ui.Open(folder);
            w.AskBeforeDiscarding = false;
            return w;
        }

        /// <summary>Saves a capture when CHALLENGE_SHOTS names a folder (to look at the window).</summary>
        static void Shot(Window w, string name)
        {
            string dir = Environment.GetEnvironmentVariable("CHALLENGE_SHOTS");
            if (string.IsNullOrEmpty(dir)) return;
            Directory.CreateDirectory(dir);
            new Ui.Codec().Save(Ui.Capture(w), Path.Combine(dir, name + ".png"));
        }

        [AvaloniaFact]
        public void The_window_lists_the_challenges_and_refuses_conflicting_pairs()
        {
            if (Uw1 == null) return;
            var w = OpenQuiet(Uw1);
            var cw = w.ShowChallenges();
            Ui.Pump();
            Assert.Same(cw, w.ShowChallenges());
            foreach (var b in Challenges.All) cw.Box(b.Id).IsChecked = false;
            cw.Box(Challenge.Monk).IsChecked = true;
            cw.Box(Challenge.Ranger).IsChecked = true;
            Ui.Pump();
            Assert.NotNull(cw.ConflictText);
            Assert.Contains("Monk and Ranger", cw.ConflictText);
            cw.Preview();
            Assert.Contains("cannot go together", cw.ReportText);
            cw.ApplyChallenges();
            Assert.False(cw.CanUndo);
            Assert.False(w.ViewModel.Edits.HasChanges);
            cw.Box(Challenge.Ranger).IsChecked = false;
            Ui.Pump();
            Assert.Null(cw.ConflictText);
            cw.FastingSlider.Value = 30;
            Assert.Equal(30, cw.Options.FastingKeep);
            cw.RangerBoxes[1].Value = 5;
            Assert.Equal(5, cw.Options.RangerWeights[1]);
            Shot(cw, "challenges");
            cw.Close();
            w.Close();
        }

        [AvaloniaFact]
        public void Apply_changes_the_levels_and_the_creature_table_and_undo_puts_them_back()
        {
            if (Uw2 == null) return;
            var w = OpenQuiet(Uw2);
            var cw = w.ShowChallenges();
            Ui.Pump();
            foreach (var b in Challenges.All) cw.Box(b.Id).IsChecked = false;
            cw.Box(Challenge.VowOfPoverty).IsChecked = true;
            cw.Box(Challenge.Lockdown).IsChecked = true;
            cw.Box(Challenge.GhostTown).IsChecked = true;
            cw.Box(Challenge.Precognition).IsChecked = true;
            var before = w.ViewModel.Session.LevelSlots().Where(x => x.Present).ToDictionary(x => x.Level, x => (byte[])w.ViewModel.Session.LoadLevel(x.Level).Block.Data.Clone());
            cw.Preview();
            Assert.Contains("Preview", cw.ReportText);
            Assert.Contains("Lockdown:", cw.ReportText);
            Assert.False(w.ViewModel.Edits.HasChanges);
            cw.ApplyChallenges();
            Ui.Pump();
            Assert.True(cw.CanUndo);
            Assert.True(w.ViewModel.Edits.HasChanges);
            Assert.NotNull(w.ObjectTablesOpen);
            Assert.True(w.ObjectTablesOpen.HasChanges);
            Assert.Contains(w.UnsavedParts(), p => p.Contains("object tables"));
            Assert.Contains(w.ViewModel.Edits.DirtyLevels, l => w.ViewModel.Edits.Existing(l) != null && !w.ViewModel.Edits.Existing(l).Data.AsSpan().SequenceEqual(before[l]));
            Shot(cw, "challenges-applied");
            cw.UndoAll();
            Ui.Pump();
            Assert.False(cw.CanUndo);
            foreach (var kv in before)
            {
                var e = w.ViewModel.Edits.Existing(kv.Key);
                if (e != null) Assert.Equal(kv.Value, e.Data);
            }
            Assert.False(w.ObjectTablesOpen.HasChanges);
            w.ObjectTablesOpen.Close();
            cw.Close();
            w.Close();
        }
    }
}
