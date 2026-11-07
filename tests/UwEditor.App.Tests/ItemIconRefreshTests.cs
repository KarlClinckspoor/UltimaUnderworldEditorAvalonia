using System;
using System.IO;
using System.Linq;
using Avalonia.Headless.XUnit;
using Avalonia.Media.Imaging;
using UwEditor.Core.Tests;
using Xunit;

namespace UwEditor.App.Tests
{
    /// <summary>Saving an item's picture: the open lists show the new icon without opening the game again.</summary>
    public class ItemIconRefreshTests
    {
        static string Uw1 { get { return GameData.Game("UW"); } }

        sealed class GameCopy : IDisposable
        {
            public readonly string Folder = Path.Combine(Path.GetTempPath(), "uwedit-icons-" + Guid.NewGuid().ToString("N"));
            public GameCopy(string from)
            {
                Directory.CreateDirectory(Path.Combine(Folder, "DATA"));
                foreach (var f in Directory.GetFiles(Path.Combine(from, "DATA"))) File.Copy(f, Path.Combine(Folder, "DATA", Path.GetFileName(f)));
            }
            public void Dispose() { try { Directory.Delete(Folder, true); } catch (Exception) { } }
        }

        /// <summary>The top-left pixel of an icon, as RGBA.</summary>
        static byte[] Corner(Bitmap b)
        {
            var wb = Assert.IsType<WriteableBitmap>(b);
            var px = new byte[4];
            using (var fb = wb.Lock()) System.Runtime.InteropServices.Marshal.Copy(fb.Address, px, 0, 4);
            return px;
        }

        /// <param name="item">0x1B: the unused blunderbuss, no name and no picture in the shipped files;
        /// 0xB5: a loaf of bread, named and drawn.</param>
        [AvaloniaTheory]
        [InlineData(0x1B)]
        [InlineData(0xB5)]
        public void Saving_an_items_picture_refreshes_the_item_slots_list_and_the_add_object_window(int item)
        {
            if (Uw1 == null) return;
            using (var copy = new GameCopy(Uw1))
            {
                var w = Ui.Open(copy.Folder);
                w.AskBeforeDiscarding = false;
                var vm = w.ViewModel;

                var slots = w.ShowItemSlots(item);
                var until = DateTime.UtcNow.AddSeconds(60);
                while (!slots.Loading.IsCompleted && DateTime.UtcNow < until) { Ui.Pump(); System.Threading.Thread.Sleep(20); }
                Ui.Pump();
                slots.ShowItem(item);
                var row = slots.RowOf(item);
                Assert.NotNull(row);
                var oldRowIcon = row.Icon;      // null when OBJECTS.GR has no picture for it
                var oldPicture = slots.PictureShown;

                var add = new AddObjectWindow(vm.Session, vm.IconOf, false, "On the selected tile.");
                add.Show(w);
                Ui.Pump();
                // the window lists only named items
                var choice = add.Model.Items.FirstOrDefault(c => c.Item == item);
                if (item == 0xB5) Assert.NotNull(choice);
                var oldChoiceIcon = choice?.Icon;

                // a colour the picture does not use, in its top-left corner
                var pe = w.EditItemPicture(item);
                Ui.Pump();
                Assert.NotNull(pe);
                pe.AskOnClose = false;
                int c0 = Enumerable.Range(0, pe.Doc.Colors.Length).First(i => i != pe.Doc.Transparent && !pe.Doc.Used().Contains(i));
                pe.Doc.Begin(); pe.Doc.Rectangle(0, 0, 3, 3, 1, c0, true); pe.Doc.End();
                Assert.True(pe.ApplyAndSave());
                Ui.Pump();

                // the editor read the saved file again: its picture has the new corner
                var pic = vm.Session.Graphics.ObjectPictures[item];
                var want = pic.Pixels.Take(4).ToArray();
                if (oldRowIcon != null) Assert.NotEqual(Corner(oldRowIcon), want);

                // the item slots list took the new icon, and so did the big picture
                Assert.NotNull(row.Icon);
                Assert.NotSame(oldRowIcon, row.Icon);
                Assert.Equal(want, Corner(row.Icon));
                Assert.NotSame(oldPicture, slots.PictureShown);
                // the add-object window too
                if (choice != null)
                {
                    Assert.NotSame(oldChoiceIcon, choice.Icon);
                    Assert.Equal(want, Corner(choice.Icon));
                }
                // and the cache every list reads from
                Assert.Equal(want, Corner(vm.IconOf(item)));

                pe.Close();
                add.Close();
                slots.Close();
                w.Close();
            }
        }
    }
}
