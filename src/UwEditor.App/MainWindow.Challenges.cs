using Avalonia.Interactivity;
using UwEditor.Core.Editing;
using UwEditor.Core.Player;

namespace UwEditor.App
{
    /// <summary>The main window's side of Randomizer ▸ Named challenges.</summary>
    public partial class MainWindow
    {
        ChallengesWindow challengesWindow;

        void OnChallenges(object sender, RoutedEventArgs e) { ShowChallenges(); }

        /// <summary>Randomizer ▸ Named challenges: opens (or brings up) the window for the open game.</summary>
        public ChallengesWindow ShowChallenges()
        {
            if (vm.Session == null || vm.Edits == null) return null;
            if (challengesWindow != null) { challengesWindow.Activate(); return challengesWindow; }
            var w = new ChallengesWindow(this, vm.Session);
            challengesWindow = w;
            w.Closed += (s, e) => { if (challengesWindow == w) challengesWindow = null; };
            w.Show(this);
            return w;
        }

        /// <summary>OBJECTS.DAT's tables as edited in the object tables window (opened when open is set, so the challenges' changes are its unsaved edits), or null.</summary>
        internal ObjectTables ChallengeTables(bool open)
        {
            if (objectTablesWindow != null) return objectTablesWindow.Tables;
            if (!open) return null;
            return ShowObjectTables()?.Tables;
        }

        /// <summary>The open save's player sheet (shared with the player window), or null.</summary>
        internal PlayerSheet ChallengePlayer() { return PlayerSheetOf(); }

        /// <summary>The player window shows the sheet as it was: it closes before the challenges change it (as restoring a checkpoint does).</summary>
        internal void ChallengePlayerChanging() { playerWindow?.Close(); }
    }
}
