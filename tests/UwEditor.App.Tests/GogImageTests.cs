using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Interactivity;
using UwEditor.Core.Tests;
using UwEditor.TestSupport;
using Xunit;

namespace UwEditor.App.Tests
{
    /// <summary>File ▸ Open GOG's game.gog: both games copied out of the image, the chosen one opened, the image kept in the recent list.</summary>
    public class GogImageTests
    {
        /// <summary>Answers each question window that opens with the next of these buttons; the texts asked are kept.</summary>
        static IDisposable Answer(Queue<string> answers, List<string> asked)
        {
            Action<MessageWindow> on = m =>
            {
                asked.Add(m.Title);
                string a = answers.Count > 0 ? answers.Dequeue() : null;
                // answered once it is shown
                Avalonia.Threading.Dispatcher.UIThread.Post(() =>
                {
                    var b = m.Buttons?.Children.OfType<Button>().FirstOrDefault(x => (x.Content as string) == a);
                    if (b != null) b.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                    else m.Close();
                });
            };
            MessageWindow.Opening += on;
            return new Unsubscribe(() => MessageWindow.Opening -= on);
        }

        sealed class Unsubscribe : IDisposable
        {
            readonly Action a;
            public Unsubscribe(Action a) { this.a = a; }
            public void Dispose() { a(); }
        }

        static T Wait<T>(Task<T> task)
        {
            for (int i = 0; i < 6000 && !task.IsCompleted; i++) { Ui.Pump(); System.Threading.Thread.Sleep(5); }
            Assert.True(task.IsCompleted, "the image did not open in time");
            return task.Result;
        }

        [AvaloniaFact]
        public void Both_games_are_copied_out_and_the_chosen_one_opens_with_the_image_in_the_recent_list()
        {
            string root = GameData.Root;
            if (root == null || !Directory.Exists(Path.Combine(root, "UW", "DATA")) || !Directory.Exists(Path.Combine(root, "UW2", "DATA"))) return;
            string dir = Path.Combine(Path.GetTempPath(), "uw-gog-app", Guid.NewGuid().ToString("N").Substring(0, 8));
            string install = Path.Combine(dir, "Ultima Underworld"), dest = Path.Combine(dir, "copies");
            Directory.CreateDirectory(install);
            File.WriteAllText(Path.Combine(install, "dosboxULTIMA1.conf"), "[dosbox]\nmachine=svga_s3\n");
            string image = Path.Combine(install, "game.gog");
            FakeGogImage.FromFolders(image, new Dictionary<string, string> { ["UW"] = Path.Combine(root, "UW"), ["UW2"] = Path.Combine(root, "UW2") });
            var before = File.ReadAllBytes(image);
            try
            {
                var w = Ui.Open(null);
                w.PickGogFolder = () => Task.FromResult(dest);
                var asked = new List<string>();
                // the image made here is not GOG's (another hash); a folder of the player's; the second game, not the install's
                using (Answer(new Queue<string>(new[] { "Copy them anyway", "Choose a folder…", "Ultima Underworld II" }), asked))
                {
                    string folder = Wait(w.OpenGogImage(image));
                    Assert.Equal(Path.Combine(dest, "UW2"), folder);
                }
                Assert.Equal(new[] { "An image the editor does not know", "Copy the games out of game.gog", "Which game?" }, asked);
                Ui.Pump();
                Assert.True(w.ViewModel.Session.IsUw2);
                Assert.Equal(Path.Combine(dest, "UW2"), w.ViewModel.Session.GameFolder, ignoreCase: true);
                Assert.True(File.Exists(Path.Combine(dest, "UW", "UW.EXE")));
                Assert.Equal(File.ReadAllBytes(Path.Combine(root, "UW2", "DATA", "LEV.ARK")), File.ReadAllBytes(Path.Combine(dest, "UW2", "DATA", "LEV.ARK")));
                Assert.Equal(before, File.ReadAllBytes(image));
                // nothing new in the install; the copies and nothing else beside it
                Assert.Equal(new[] { "dosboxULTIMA1.conf", "game.gog" }, Directory.GetFileSystemEntries(install).Select(Path.GetFileName).OrderBy(n => n, StringComparer.Ordinal));
                Assert.Equal(new[] { Underworld.Mods.GogImage.InstallNote, "UW", "UW2" }, Directory.GetFileSystemEntries(dest).Select(Path.GetFileName).OrderBy(n => n, StringComparer.Ordinal));
                // Run in DOSBox finds GOG's settings in the install the copies came from
                Assert.Equal(Path.Combine(install, "dosboxULTIMA1.conf"), UwEditor.Core.DosBoxConfig.FindGogConf(Path.Combine(dest, "UW2")), ignoreCase: true);

                // the recent list: the copy, then the image (which starts over)
                var recent = Settings.Load().Recent;
                Assert.Equal(Path.Combine(dest, "UW2"), recent[0], ignoreCase: true);
                Assert.Equal(image, recent[1], ignoreCase: true);

                // starting over into the same folder is refused: the copy there is not overwritten
                asked.Clear();
                using (Answer(new Queue<string>(new[] { "Copy them anyway", "Choose a folder…", "OK" }), asked))
                    Assert.Null(Wait(w.OpenGogImage(image)));
                Assert.Equal("The folder already holds a copy", asked.Last());

                // and nothing goes into the install itself
                w.PickGogFolder = () => Task.FromResult(install);
                using (Answer(new Queue<string>(new[] { "Copy them anyway", "Choose a folder…", "OK" }), asked))
                    Assert.Null(Wait(w.OpenGogImage(image)));
                Assert.Equal("Inside the GOG install", asked.Last());
                Assert.Equal(new[] { "dosboxULTIMA1.conf", "game.gog" }, Directory.GetFileSystemEntries(install).Select(Path.GetFileName).OrderBy(n => n, StringComparer.Ordinal));
                w.Close();
            }
            finally { try { Directory.Delete(dir, true); } catch (IOException) { } }
        }
    }
}
