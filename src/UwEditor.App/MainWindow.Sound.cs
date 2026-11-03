using Avalonia.Interactivity;

namespace UwEditor.App
{
    /// <summary>The main window's side of Tools ▸ Sound and music.</summary>
    public partial class MainWindow
    {
        SoundWindow soundWindow;

        void OnSound(object sender, RoutedEventArgs e) { ShowSound(); }

        /// <summary>Tools ▸ Sound and music: speech, effects (and music) of the game's SOUND folder.</summary>
        public SoundWindow ShowSound()
        {
            if (vm.Session == null) return null;
            if (soundWindow != null) { soundWindow.Activate(); return soundWindow; }
            var w = soundWindow = new SoundWindow(this, vm.Session);
            var music = new MusicPanel(this);
            w.AddPanel("Music", music, () => music.HasChanges, music.SaveAll);
            w.Closed += (s, e) => soundWindow = null;
            MessageWindow.GuardClose(w, "Sound and music", () => w.HasChanges, w.SaveAll, () => AskBeforeDiscarding && !quietClose);
            w.Show(this);
            return w;
        }
    }
}
