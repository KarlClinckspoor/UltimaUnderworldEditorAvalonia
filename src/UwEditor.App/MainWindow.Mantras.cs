using Avalonia.Interactivity;

namespace UwEditor.App
{
    /// <summary>The main window's side of Randomizer ▸ Randomize mantras (UW1) and Vowel play: the text randomizers.</summary>
    public partial class MainWindow
    {
        MantrasWindow mantrasWindow;
        VowelPlayWindow vowelPlayWindow;

        void OnMantras(object sender, RoutedEventArgs e) { ShowMantras(); }
        void OnVowelPlay(object sender, RoutedEventArgs e) { ShowVowelPlay(); }

        /// <summary>Randomizer ▸ Randomize mantras: opens (or brings up) the window for the open UW1 game.</summary>
        public MantrasWindow ShowMantras()
        {
            if (vm.Session == null || vm.Session.IsUw2) return null;
            if (mantrasWindow != null) { mantrasWindow.Activate(); return mantrasWindow; }
            var st = Strings();
            if (st == null) return null;
            var w = new MantrasWindow(this, st);
            mantrasWindow = w;
            w.Closed += (s, e) => { if (mantrasWindow == w) mantrasWindow = null; };
            w.Show(this);
            return w;
        }

        /// <summary>Randomizer ▸ Vowel play: opens (or brings up) the window for the open game.</summary>
        public VowelPlayWindow ShowVowelPlay()
        {
            if (vm.Session == null) return null;
            if (vowelPlayWindow != null) { vowelPlayWindow.Activate(); return vowelPlayWindow; }
            var st = Strings();
            if (st == null) return null;
            var w = new VowelPlayWindow(this, vm.Session, st, Conversations());
            vowelPlayWindow = w;
            w.Closed += (s, e) => { if (vowelPlayWindow == w) vowelPlayWindow = null; };
            w.Show(this);
            return w;
        }

        /// <summary>After a text randomizer changed the strings: the open string views show the new text.</summary>
        internal void MantrasChanged(string status)
        {
            stringsWindow?.Model.Reload();
            conversationWindow?.RefreshStrings();
            vm.EditStatus = status;
        }
    }
}
