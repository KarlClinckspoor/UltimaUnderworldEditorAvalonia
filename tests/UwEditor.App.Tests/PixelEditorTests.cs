using System;
using System.IO;
using System.Linq;
using Avalonia.Headless.XUnit;
using UwEditor.Core;
using UwEditor.Core.Tests;
using Xunit;

namespace UwEditor.App.Tests
{
    public class PixelEditorTests
    {
        static string Uw1 { get { return GameData.Game("UW"); } }

        sealed class GameCopy : IDisposable
        {
            public readonly string Folder = Path.Combine(Path.GetTempPath(), "uwedit-pix-" + Guid.NewGuid().ToString("N"));
            public GameCopy(string from)
            {
                foreach (var d in new[] { "DATA" })
                {
                    Directory.CreateDirectory(Path.Combine(Folder, d));
                    foreach (var f in Directory.GetFiles(Path.Combine(from, d))) File.Copy(f, Path.Combine(Folder, d, Path.GetFileName(f)));
                }
            }
            public void Dispose() { try { Directory.Delete(Folder, true); } catch (Exception) { } }
        }

        [AvaloniaFact]
        public void Drawing_on_an_items_picture_and_saving_writes_OBJECTS_GR_and_the_view_reads_it_again()
        {
            if (Uw1 == null) return;
            using (var copy = new GameCopy(Uw1))
            {
                var w = Ui.Open(copy.Folder);
                w.AskBeforeDiscarding = false;
                var pe = w.EditItemPicture(0xB5);
                Ui.Pump();
                Assert.NotNull(pe);
                pe.AskOnClose = false;
                int c = Enumerable.Range(0, pe.Doc.Colors.Length).First(i => i != pe.Doc.Transparent && !pe.Doc.Used().Contains(i));
                pe.Doc.Begin(); pe.Doc.Rectangle(0, 0, 3, 3, 1, c, true); pe.Doc.End();
                Assert.True(pe.HasChanges);
                Assert.DoesNotContain("pictures", w.UnsavedParts());          // drawn, not applied yet
                Assert.True(pe.Apply(false));
                Assert.Contains("pictures", w.UnsavedParts());
                Assert.True(pe.ApplyAndSave());
                Assert.DoesNotContain("pictures", w.UnsavedParts());
                var f = UwEditor.Core.Graphics.PictureFile.Load(Path.Combine(copy.Folder, "DATA", "OBJECTS.GR"), w.ViewModel.Session.Kind);
                var pal = UwEditor.Core.Graphics.Palettes.Load(Path.Combine(copy.Folder, "DATA"));
                Assert.Equal(c, f.Get(0xB5, pal, 0).Pixels[0]);
                Assert.True(File.Exists(Path.Combine(copy.Folder, "DATA", "OBJECTS.GR.BKP")));
                pe.Close();
                // the item slot: a new name for the second loaf of bread
                var slots = w.ShowItemSlots(0xB5);
                var until = DateTime.UtcNow.AddSeconds(60);
                while (!slots.Loading.IsCompleted && DateTime.UtcNow < until) { Ui.Pump(); System.Threading.Thread.Sleep(20); }
                Ui.Pump();
                slots.ShowItem(0xB5);
                Assert.Equal(0xB5, slots.Selected.Item);
                Assert.True(slots.SetName("a", "honey cake", "honey cakes"));
                Assert.Equal("a_honey cake&honey cakes", w.Strings().Get(4)[0xB5]);
                Assert.Contains(w.UnsavedParts(), p => p.Contains("strings"));
                slots.Close();
                // the graphics window opens the editor on the picture it shows
                var gw = w.ShowGraphics();
                Ui.Pump();
                var pe2 = gw.EditPicture();
                Assert.NotNull(pe2);
                pe2.AskOnClose = false;
                pe2.Close();
                w.Close();
            }
        }
    }
}
