using System;
using System.IO;
using System.Linq;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Media.Imaging;
using UwEditor.Core.Tests;
using Xunit;

namespace UwEditor.App.Tests
{
    /// <summary>Saving a wall texture: the 3D view, the tile panel, the brush and the level textures window show it.</summary>
    public class TextureSaveTests
    {
        sealed class GameCopy : IDisposable
        {
            public readonly string Folder = Path.Combine(Path.GetTempPath(), "uwedit-tex-" + Guid.NewGuid().ToString("N"));
            public GameCopy(string from)
            {
                Directory.CreateDirectory(Path.Combine(Folder, "DATA"));
                foreach (var f in Directory.GetFiles(Path.Combine(from, "DATA"))) File.Copy(f, Path.Combine(Folder, "DATA", Path.GetFileName(f)));
            }
            public void Dispose() { try { Directory.Delete(Folder, true); } catch (Exception) { } }
        }

        static byte[] Corner(Bitmap b)
        {
            var wb = Assert.IsType<WriteableBitmap>(b);
            var px = new byte[4];
            using (var fb = wb.Lock()) System.Runtime.InteropServices.Marshal.Copy(fb.Address, px, 0, 4);
            return px;
        }

        [AvaloniaTheory]
        [InlineData("UW")]
        [InlineData("UW2")]
        public void Saving_a_wall_texture_shows_it_in_the_view_and_the_texture_lists(string game)
        {
            string g = GameData.Game(game); if (g == null) return;
            using (var copy = new GameCopy(g))
            {
                var w = Ui.Open(copy.Folder);
                w.AskBeforeDiscarding = false;
                var vm = w.ViewModel;
                var s = vm.Session;
                var level = vm.Level;
                // a tile with a wall, and the texture its wall slot shows
                int tx = -1, ty = -1;
                for (int y = 0; y < 64 && tx < 0; y++)
                    for (int x = 0; x < 64; x++)
                        if ((int)level.Tiles[x, y].Type != 0) { tx = x; ty = y; break; }
                int slot = level.Tiles[tx, ty].WallIndex;
                int tex = level.WallTextureNumber(slot);
                vm.Select(-1, tx, ty, false);
                vm.PaintMode = 3;      // the brush paints wall textures: its slots have pictures
                Ui.Pump();
                var oldThumb = vm.WallThumb;
                var oldSlot = vm.Slots.Count > slot ? vm.Slots[slot].Picture : null;
                var lt = w.ShowLevelTextures();
                Ui.Pump();
                lt.Slots.SelectedIndex = slot;
                Ui.Pump();
                var oldRow = ((TableRow)lt.Slots.SelectedItem).Picture;
                var view = w.FindControl<LevelView>("View");
                var oldLayer = view.Scene.Layers[slot];

                // a colour the texture does not use, over its top-left corner
                var f = s.Pictures.Get(s.IsUw2 ? "T64.TR" : "W64.TR");
                var img = f.GetOrBlank(tex, s.Pictures.Palettes, f.DefaultPalette(tex));
                var used = img.Pixels.ToHashSet();
                int c0 = Enumerable.Range(1, img.Colors.Length - 1).First(i => !used.Contains(i));
                var px = (int[])img.Pixels.Clone();
                for (int y = 0; y < 4; y++) for (int x = 0; x < 4; x++) px[y * img.Width + x] = c0;
                f.Replace(tex, img, px);
                s.Pictures.Save(f);
                Ui.Pump();

                var want = s.Graphics.TextureRgba(s.Graphics.WallTexture(tex), s.Graphics.WallEdge, 64).Pixels.Take(4).ToArray();
                // the 3D view
                var layer = view.Scene.Layers[slot];
                Assert.NotSame(oldLayer, layer);
                Assert.NotEqual(oldLayer.Pixels.Take(4).ToArray(), layer.Pixels.Take(4).ToArray());
                // the tile panel
                Assert.NotSame(oldThumb, vm.WallThumb);
                Assert.Equal(want, Corner(vm.WallThumb));
                // the brush's slots
                if (oldSlot != null) Assert.NotSame(oldSlot, vm.Slots[slot].Picture);
                // the level textures window: the slot and the picker
                var row = (TableRow)lt.Slots.SelectedItem;
                Assert.NotSame(oldRow, row.Picture);
                Assert.Equal(want, Corner(row.Picture));

                lt.Close();
                w.Close();
            }
        }
    }
}
