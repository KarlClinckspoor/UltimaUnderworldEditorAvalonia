using System;
using UwEditor.Core;
using UwEditor.Core.Graphics;

namespace UwEditor.App
{
    /// <summary>The pixel editor opened from the level (an object's picture), and the view following saved pictures.</summary>
    public partial class MainWindow
    {
        /// <summary>The pixel editor on an item's picture in OBJECTS.GR (the picture number is the item id).</summary>
        public PixelEditorWindow EditItemPicture(int item)
        {
            var s = vm.Session;
            if (s == null) return null;
            var f = s.Pictures.Get("OBJECTS.GR");
            if (f == null || item < 0 || item >= f.Count) { vm.EditStatus = "OBJECTS.GR has no picture " + item + "."; return null; }
            var img = f.GetOrBlank(item, s.Pictures.Palettes, f.DefaultPalette(item));
            if (img == null) { vm.EditStatus = "Picture " + item + " of OBJECTS.GR is empty."; return null; }
            var w = new PixelEditorWindow(img, string.Format("{0} (OBJECTS.GR picture {1})", s.Names.Get(item), item), f.Describe(item, img) + ". What the inventory and the view show for this item.",
                px => { try { f.Replace(item, img, px); return null; } catch (Exception ex) { return ex.Message; } },
                () => { try { s.Pictures.Save(f); return null; } catch (Exception ex) { return ex.Message; } }, "OBJECTS.GR");
            w.AskOnClose = AskBeforeDiscarding;
            w.Show(this);
            return w;
        }

        ItemSlotsWindow itemSlotsWindow;

        void OnItemSlots(object sender, Avalonia.Interactivity.RoutedEventArgs e) { ShowItemSlots(); }

        /// <summary>The item slots window, on an item (−1: the first free one).</summary>
        public ItemSlotsWindow ShowItemSlots(int item = -1)
        {
            if (vm.Session == null) return null;
            if (itemSlotsWindow != null) { itemSlotsWindow.Activate(); if (item >= 0) itemSlotsWindow.ShowItem(item); return itemSlotsWindow; }
            itemSlotsWindow = new ItemSlotsWindow(this, item);
            itemSlotsWindow.Closed += (s, e) => itemSlotsWindow = null;
            itemSlotsWindow.Show(this);
            return itemSlotsWindow;
        }

        /// <summary>A picture file was written: the view, the sprites and the icons read the pictures again.</summary>
        void OnPicturesSaved(string file)
        {
            var s = vm.Session;
            if (s == null) return;
            string up = file.ToUpperInvariant();
            if (up != "OBJECTS.GR" && !up.EndsWith(".TR") && up != "DOORS.GR" && up != "TMOBJ.GR" && up != "TMFLAT.GR") return;
            s.ReloadGraphics();
            atlas = SpriteAtlas.Build(s.Graphics.ObjectPictures);
            vm.RefreshNames();
            if (vm.Level != null) { scene = LevelScene.Build(vm.Level, atlas, vm.Options); View.SetScene(scene); UpdateMarkers(); }
        }
    }
}
