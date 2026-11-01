using System;
using System.IO;
using System.Linq;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.VisualTree;
using UwEditor.Core.Randomizer;
using UwEditor.Core.Tests;
using Xunit;

namespace UwEditor.App.Tests
{
    /// <summary>Tools ▸ Object tables: OBJECTS.DAT and COMOBJ.DAT in grids.</summary>
    public class ObjectTablesWindowTests
    {
        /// <summary>A copy of a game's DATA and executable in a temporary folder.</summary>
        sealed class Copy : IDisposable
        {
            public readonly string Folder = Path.Combine(Path.GetTempPath(), "uwedit-objtables-" + Guid.NewGuid().ToString("N"));
            public Copy(string from)
            {
                Directory.CreateDirectory(Path.Combine(Folder, "DATA"));
                foreach (var f in Directory.GetFiles(Path.Combine(from, "DATA"))) File.Copy(f, Path.Combine(Folder, "DATA", Path.GetFileName(f)));
                foreach (var f in Directory.GetFiles(from, "*.EXE")) File.Copy(f, Path.Combine(Folder, Path.GetFileName(f)));
            }
            public void Dispose() { try { Directory.Delete(Folder, true); } catch (Exception) { } }
        }

        [AvaloniaTheory]
        [InlineData("UW")]
        [InlineData("UW2")]
        public void Editing_a_cell_and_saving_writes_the_file_and_the_game_reads_it(string game)
        {
            string g = GameData.Game(game); if (g == null) return;
            using (var copy = new Copy(g))
            {
                var w = Ui.Open(copy.Folder);
                w.AskBeforeDiscarding = false;
                var ot = w.ShowObjectTables();
                Ui.Pump();
                Assert.NotNull(ot.Tables);
                Assert.Same(ot, w.ShowObjectTables());              // one window
                Assert.Equal(10, ot.GetVisualDescendants().OfType<TabItem>().Count());
                Assert.False(ot.HasChanges);

                string path = ot.Tables.ObjectsPath;
                byte[] before = File.ReadAllBytes(path);
                int at = 0x132 + 0x30 * 5 + 4;                      // creature 0x45's average hit points
                int hp = before[at] == 250 ? 251 : 250;
                Assert.Null(ot.EditCell("Creatures", 5, "Average HP", hp.ToString()));
                Assert.True(ot.HasChanges);
                Assert.Contains("Average HP", ot.ExplanationText);
                Assert.Contains("was " + before[at], ot.ExplanationText);

                // out of range: refused, reported, nothing changed
                string err = ot.EditCell("Creatures", 5, "Strength", "300");
                Assert.NotNull(err);
                Assert.Contains("0 to 255", ot.StatusText);
                Assert.Equal(before[at + 1], ot.Tables.Get("Creatures", 5, "Strength"));

                // the filter
                var filter = ot.GetVisualDescendants().OfType<TextBox>().First(t => (t.Watermark ?? "").ToString().StartsWith("Filter"));
                filter.Text = "0x45";
                Ui.Pump();
                Assert.Equal(1, ot.VisibleRows("Creatures"));
                filter.Text = "";
                Ui.Pump();
                Assert.Equal(64, ot.VisibleRows("Creatures"));

                Assert.True(ot.SaveAll());
                Assert.False(ot.HasChanges);
                byte[] after = File.ReadAllBytes(path);
                Assert.Equal(hp, after[at]);
                Assert.Equal(new[] { at }, Enumerable.Range(0, before.Length).Where(i => before[i] != after[i]).ToArray());
                Assert.Equal(hp, CreatureKind.Read(w.ViewModel.Session)[0x45].AvgHit);
                Assert.Contains("Saved", ot.StatusText);

                // revert: a row back as in the file
                Assert.Null(ot.EditCell("Weapons", 2, "Durability", "7"));
                Assert.True(ot.HasChanges);
                ot.SelectRow("Weapons", 2);
                var revert = ot.GetVisualDescendants().OfType<Button>().First(b => (b.Content as string) == "Revert row");
                Ui.Click(ot, revert);
                Assert.False(ot.HasChanges);
                ot.Close();
                Ui.Pump();
                Assert.Null(w.ObjectTablesOpen);
                w.Close();
            }
        }

        [AvaloniaFact]
        public void Clicking_a_cell_and_typing_edits_it()
        {
            string g = GameData.Game("UW"); if (g == null) return;
            var w = Ui.Open(g);
            w.AskBeforeDiscarding = false;
            var ot = w.ShowObjectTables();
            ot.Width = 1180; ot.Height = 780;
            Ui.Pump();
            ot.ShowTable("Weapons");
            Ui.Pump();
            var list = ot.GetVisualDescendants().OfType<ListBox>().First(l => l.IsEffectivelyVisible);
            var item = list.GetVisualDescendants().OfType<ListBoxItem>().First();
            var cells = item.GetVisualDescendants().OfType<Border>().Where(b => b.Width >= 40 && b.Width <= 96 && b.Parent is StackPanel sp && sp.Height == 24).ToList();
            Assert.NotEmpty(cells);
            Ui.Click(ot, cells[0]);                                 // slash damage of item 0
            var box = cells[0].Child as TextBox;
            Assert.NotNull(box);
            Assert.Equal(ot.Tables.Get("Weapons", 0, "Slash damage").ToString(), box.Text);
            box.Text = "42";
            Ui.Key(ot, Key.Enter);
            Assert.Equal(42, ot.Tables.Get("Weapons", 0, "Slash damage"));
            Assert.True(ot.HasChanges);
            Assert.IsType<TextBlock>(cells[0].Child);
            Assert.Contains("Slash damage = 42", ot.StatusText);

            // Tab keeps the value and edits the next field; Escape drops the typing
            Ui.Click(ot, cells[0]);
            ((TextBox)cells[0].Child).Text = "43";
            ot.KeyPressQwerty(PhysicalKey.Tab, RawInputModifiers.None);
            ot.KeyReleaseQwerty(PhysicalKey.Tab, RawInputModifiers.None);
            Ui.Pump();
            Assert.Equal(43, ot.Tables.Get("Weapons", 0, "Slash damage"));
            var next = cells[1].Child as TextBox;
            Assert.NotNull(next);
            next.Text = "99";
            Ui.Key(ot, Key.Escape);
            Assert.NotEqual(99, ot.Tables.Get("Weapons", 0, "Bash damage"));
            Assert.IsType<TextBlock>(cells[1].Child);
            ot.Tables.RevertAll();
            Assert.False(ot.HasChanges);
            ot.Close();
            w.Close();
        }
    }
}
