using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Platform.Storage;
using UltimateEditor.Games;
using Underworld.Mods;

namespace UwEditor.App
{
    /// <summary>
    /// Opening GOG's game.gog (the CD image both GOG installs play from): both games are copied
    /// out of it into a folder, and the chosen one is opened from there. The GOG install is never
    /// changed. Opening the image again (File ▸ Open recent) starts over with a fresh copy.
    /// </summary>
    public partial class MainWindow
    {
        /// <summary>Tests set the answers here instead of the folder picker: the folder chosen for the copy.</summary>
        public Func<Task<string>> PickGogFolder;

        async void OnOpenGog(object sender, RoutedEventArgs e)
        {
            string path = null;
            try
            {
                var files = await StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
                {
                    Title = "Open GOG's game.gog (in the Ultima Underworld or Ultima Underworld 2 folder)",
                    AllowMultiple = false,
                    FileTypeFilter = new[] { new FilePickerFileType("GOG's game image") { Patterns = new[] { "*.gog" } }, FilePickerFileTypes.All },
                });
                path = files.FirstOrDefault()?.TryGetLocalPath();
            }
            catch (Exception ex)
            {
                _ = MessageWindow.Show(this, "Could not open the file", ex.Message);
                return;
            }
            if (path == null) return;
            if (!await ConfirmDiscard("opening another game")) return;
            await OpenGogImage(path);
        }

        /// <summary>Whether a path is a GOG image (a file ending in .gog) rather than a game or save folder.</summary>
        public static bool IsGogImage(string path)
        {
            if (string.IsNullOrWhiteSpace(path)) return false;
            string p = path.Trim().Trim('"');
            return p.EndsWith(".gog", StringComparison.OrdinalIgnoreCase) && File.Exists(p);
        }

        /// <summary>Where the editor copies the games when the player does not choose: a new folder each time, in the settings folder.</summary>
        public static string DefaultGogFolder()
        {
            string root = Path.Combine(Settings.Folder, "GOG copies");
            string stamp = DateTime.Now.ToString("yyyy-MM-dd HH.mm.ss");
            string dir = Path.Combine(root, stamp);
            for (int n = 2; Directory.Exists(dir); n++) dir = Path.Combine(root, stamp + " (" + n + ")");
            return dir;
        }

        /// <summary>
        /// Copies both games out of GOG's image into a folder (the editor's, or one the player
        /// chooses) and opens the one the player picks. The hash is checked first: an image the
        /// editor does not know is extracted only when the player says so.
        /// </summary>
        public async Task<string> OpenGogImage(string image)
        {
            image = Path.GetFullPath(image.Trim().Trim('"'));
            string sha;
            List<GameKind> games;
            try
            {
                Cursor = new Cursor(StandardCursorType.Wait);
                sha = await Task.Run(() => GogImage.Sha256(image));
                games = await Task.Run(() => GogImage.Games(image));
            }
            catch (Exception ex)
            {
                await MessageWindow.Show(this, "Could not read " + Path.GetFileName(image), ex.Message);
                return null;
            }
            finally { Cursor = Cursor.Default; }
            if (games.Count == 0)
            {
                await MessageWindow.Show(this, "Not GOG's image of the games", image + " holds neither UW\\UW.EXE nor UW2\\UW2.EXE.");
                return null;
            }
            string release = GogImage.Release(sha);
            if (release == null && await MessageWindow.Ask(this, "An image the editor does not know",
                    Path.GetFileName(image) + " is not the GOG release the editor was checked with (its SHA-256 is " + sha + "). It may be another release, or a damaged copy. Copy the games out of it anyway?",
                    "Copy them anyway", "Cancel") != "Copy them anyway")
                return null;

            // where: a new folder of the editor's, or one the player chooses
            string install = Path.GetDirectoryName(image);
            string dflt = DefaultGogFolder();
            string a = await MessageWindow.Ask(this, "Copy the games out of game.gog",
                "game.gog is GOG's CD of both games. The editor copies " + (games.Count == 2 ? "both games" : "the game") + " out of it into a folder and edits that copy: " +
                "the GOG install is left exactly as it is.\n\n" +
                "GOG's launcher always plays from game.gog, so it never sees the copy. To play a game changed here, make a mod of it (Mods ▸ Make a mod) and use it with the mod manager, which keeps its own copy for GOG's installs.\n\n" +
                "The editor's folder is a new one each time:\n" + dflt,
                "Use the editor's folder", "Choose a folder…", "Cancel");
            string dest;
            if (a == "Use the editor's folder") dest = dflt;
            else if (a == "Choose a folder…")
            {
                dest = PickGogFolder != null ? await PickGogFolder() : await PickFolder("The folder to copy the games into (they go into UW and UW2 in it)");
                if (dest == null) return null;
                string full = Path.GetFullPath(dest).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar) + Path.DirectorySeparatorChar;
                if (full.StartsWith(Path.GetFullPath(install).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar) + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase))
                {
                    await MessageWindow.Show(this, "Inside the GOG install", "Choose a folder outside " + install + ": the GOG install is left exactly as it is.");
                    return null;
                }
                List<string> taken;
                try { taken = games.Select(g => Path.Combine(dest, GogImage.FolderOf(g))).Where(d => Directory.Exists(d) && Directory.EnumerateFileSystemEntries(d).Any()).ToList(); }
                catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
                {
                    await MessageWindow.Show(this, "Could not read the folder", ex.Message);
                    return null;
                }
                if (taken.Count > 0)
                {
                    await MessageWindow.Show(this, "The folder already holds a copy",
                        string.Join("\n", taken) + "\n\nis not empty. Choose an empty folder, so no game you changed is overwritten; to go on with that copy, open its folder with File ▸ Open game folder.");
                    return null;
                }
            }
            else return null;

            // which game to edit (the install's own first, when it says)
            GameKind? mine = GogImage.GameOfInstall(install);
            var order = games.OrderBy(g => mine == g ? 0 : 1).ThenBy(g => g).ToList();
            GameKind chosen = order[0];
            if (order.Count > 1)
            {
                string uw1 = "Ultima Underworld", uw2 = "Ultima Underworld II";
                var buttons = order.Select(g => g == GameKind.UW2 ? uw2 : uw1).Concat(new[] { "Cancel" }).ToArray();
                string g = await MessageWindow.Ask(this, "Which game?", "Both games will be in the folder (UW and UW2). Which one do you want to edit now? The other one opens with File ▸ Open game folder.", buttons);
                if (g == uw1) chosen = GameKind.UW1;
                else if (g == uw2) chosen = GameKind.UW2;
                else return null;
            }

            var made = new List<string>();
            bool destExisted = Directory.Exists(dest);
            try
            {
                Cursor = new Cursor(StandardCursorType.Wait);
                await Task.Run(() =>
                {
                    foreach (var g in games)
                    {
                        string to = Path.Combine(dest, GogImage.FolderOf(g));
                        GogImage.Extract(image, g, to, install);
                        made.Add(to);
                    }
                    // Run in DOSBox and Play test find each game's install (its DOSBox and settings) through this
                    GogImage.WriteInstallNote(dest, install, games.ToDictionary(g => g, g => GogImage.SetupInstall(install, g)));
                });
            }
            catch (Exception ex)
            {
                // no half copy is left behind: a retry would find the folder taken
                foreach (var d in made) try { Directory.Delete(d, true); } catch (Exception) { }
                if (!destExisted) try { Directory.Delete(dest, true); } catch (Exception) { }
                await MessageWindow.Show(this, "Could not copy the games", ex.Message);
                return null;
            }
            finally { Cursor = Cursor.Default; }

            // a game whose own install is not found plays without GOG's sound setup: say so
            var bare = games.Where(g => GogImage.SetupInstall(install, g) == null).ToList();
            if (bare.Count > 0)
                await MessageWindow.Show(this, "No sound setup for " + string.Join(" and ", bare.Select(g => g == GameKind.UW2 ? "Ultima Underworld II" : "Ultima Underworld")),
                    "Neither " + install + " nor a folder beside it holds " + string.Join(" or ", bare.Select(GogImage.HardDiskFolderOf)) +
                    ", GOG's sound setup of the CD version. The copy keeps the disc's own UW.CFG, which has no sound: install that game from GOG beside this one and open its game.gog, or set the sound up in the copy's DATA\\UW.CFG.");

            // the image stays in the recent list: opening it again starts over with a fresh copy
            settings.AddRecent(image);
            settings.Save();
            string folder = Path.Combine(dest, GogImage.FolderOf(chosen));
            OpenPathNow(folder);
            return folder;
        }

        async Task<string> PickFolder(string title)
        {
            try
            {
                var picked = await StorageProvider.OpenFolderPickerAsync(new FolderPickerOpenOptions { Title = title, AllowMultiple = false });
                return picked.FirstOrDefault()?.TryGetLocalPath();
            }
            catch (Exception ex)
            {
                await MessageWindow.Show(this, "Could not open the folder", ex.Message);
                return null;
            }
        }
    }
}
