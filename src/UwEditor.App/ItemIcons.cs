using System.Collections.Generic;
using Avalonia.Controls;

namespace UwEditor.App
{
    /// <summary>A window that shows item icons (OBJECTS.GR pictures) it took when it filled its lists:
    /// after a picture file is saved, the main window asks it to take them again.</summary>
    public interface IShowsItemIcons
    {
        /// <summary>Takes every shown item's icon again (the main view model's cache was emptied).</summary>
        void RefreshIcons();
    }

    /// <summary>A window that shows wall, floor or door textures it drew when it filled its lists:
    /// after a texture file (.TR, DOORS.GR) is saved, the main window asks it to draw them again.</summary>
    public interface IShowsTextures
    {
        /// <summary>Draws every shown texture again from the session's graphics (read again from disk).</summary>
        void RefreshTextures();
    }

    public static class ItemIcons
    {
        /// <summary>The windows owned by a window, and the ones they own, that show item icons.</summary>
        public static List<IShowsItemIcons> In(Window owner) { return Owned<IShowsItemIcons>(owner); }

        /// <summary>The windows owned by a window, and the ones they own, that are of a kind.</summary>
        public static List<T> Owned<T>(Window owner) where T : class
        {
            var found = new List<T>();
            Walk(owner, found, new HashSet<Window>());
            return found;
        }

        static void Walk<T>(Window w, List<T> found, HashSet<Window> seen) where T : class
        {
            foreach (var o in w.OwnedWindows)
            {
                if (!seen.Add(o)) continue;
                if (o is T s) found.Add(s);
                Walk(o, found, seen);
            }
        }
    }
}
