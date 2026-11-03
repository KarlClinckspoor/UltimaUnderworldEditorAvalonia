using System;
using System.IO;
using System.Linq;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.LogicalTree;
using UwEditor.Core.Randomizer;
using UwEditor.Core.Tests;
using Xunit;

namespace UwEditor.App.Tests
{
    /// <summary>Randomizer ▸ Key item shuffle: the options shared with the Automatic zones window, preview, shuffle, undo and the spoiler log.</summary>
    public class KeyShuffleWindowTests
    {
        static string Uw1 { get { return GameData.Game("UW"); } }

        static CheckBox Box(Window w, string start)
        {
            return w.GetLogicalDescendants().OfType<CheckBox>().First(c => (c.Content as string ?? "").StartsWith(start));
        }

        [AvaloniaFact]
        public void Preview_shuffle_and_undo()
        {
            if (Uw1 == null) return;
            var w = Ui.Open(Uw1);
            w.AskBeforeDiscarding = false;
            var kw = w.ShowKeyShuffle();
            Ui.Pump();
            Assert.Same(kw, w.ShowKeyShuffle());
            // the logic options are the Automatic zones window's, with the orb option
            Assert.True(Box(kw, "Ring of Levitation").IsChecked);
            Assert.False(Box(kw, "Require breaking the orb").IsChecked);
            Box(kw, "Require breaking the orb").IsChecked = true;
            Ui.Pump();
            Assert.True(kw.Options.Logic.RequireOrb);
            foreach (var c in KeyShuffle.Categories) Assert.True(Box(kw, KeyShuffle.CategoryName(c)).IsChecked);
            Assert.False(Box(kw, "Creature inventories").IsChecked);
            Assert.False(Box(kw, "Everything else as filler").IsChecked);
            kw.GetLogicalDescendants().OfType<NumericUpDown>().First().Value = 11;
            Ui.Pump();
            Assert.Equal(11, kw.Options.Seed);

            var edits = w.ViewModel.Edits;
            var r = kw.PreviewNow();
            Ui.Pump();
            Assert.NotNull(r);
            Assert.True(r.Success, string.Join("; ", r.Failures));
            Assert.Contains("WHERE EACH KEY ITEM WENT", kw.Spoiler);
            Assert.Contains("the goal needs the orb broken", kw.Spoiler);
            Assert.Empty(edits.DirtyLevels);
            string dir = Environment.GetEnvironmentVariable("KEYSHUFFLE_SHOTS");
            if (!string.IsNullOrEmpty(dir)) { Directory.CreateDirectory(dir); new Ui.Codec().Save(Ui.Capture(kw), Path.Combine(dir, "keyshuffle.png")); }

            // Shuffle writes the preview on show (the same seed and levels)
            Assert.True(kw.ShuffleNow());
            Ui.Pump();
            Assert.Same(r, kw.Result);
            Assert.Equal(r.After.Keys.OrderBy(x => x), edits.DirtyLevels.OrderBy(x => x));
            foreach (var kv in r.After) Assert.True(edits.For(kv.Key).Data.AsSpan().SequenceEqual(kv.Value));
            string log = Path.Combine(Path.GetTempPath(), "uwedit-keyshuffle-" + Guid.NewGuid().ToString("N") + ".txt");
            try { kw.ExportTo(log); Assert.Contains("KEY ITEM SHUFFLE", File.ReadAllText(log)); }
            finally { File.Delete(log); }

            // Undo shuffle puts every byte back
            Assert.True(kw.UndoNow());
            Ui.Pump();
            foreach (var kv in r.Before) Assert.True(edits.Model(kv.Key).Block.Data.AsSpan().SequenceEqual(kv.Value), "level " + kv.Key);
            kw.Close();

            // the Automatic zones window shows the same options, the orb one included
            var aw = w.ShowAutoZones();
            Ui.Pump();
            Assert.False(Box(aw, "Require breaking the orb").IsChecked);
            Box(aw, "Require breaking the orb").IsChecked = true;
            Ui.Pump();
            Assert.True(aw.LogicOptions.RequireOrb);
            aw.Close();
            w.Close();
        }
    }
}
