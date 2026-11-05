using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using UwEditor.Core;

namespace UwEditor.App
{
    /// <summary>
    /// Run and Test ▸ DOSBox spot test: the squares the Underworld Exhumed check stops at, played
    /// by hand in the original game. Four at a time become saved games 1-4 in a copy of the game,
    /// with the same levels and a fresh tester on each square; DOSBox starts on the copy, and
    /// when it is closed the window asks for the next four, showing how many are done.
    /// </summary>
    public sealed class SpotTestWindow : Window
    {
        readonly MainWindow main;
        readonly StackPanel levelBoxes = new StackPanel { Spacing = 2 };
        readonly List<CheckBox> boxes = new List<CheckBox>();
        readonly CheckBox editsBox = new CheckBox { Content = "Also a spot beside every edit (tiles and objects changed)" };
        readonly CheckBox sweepBox = new CheckBox { Content = "Sweep each level: also a spot in every 16 × 16 area with room to stand" };
        readonly NumericUpDown str = Stat(), dex = Stat(), intel = Stat(), def = Stat();
        readonly CheckBox armourBox = new CheckBox { Content = "Wear the best armour (helmet, breastplate, plate gauntlets, leggings and boots, tower shield)", IsChecked = true };
        readonly TextBlock estimate = new TextBlock { FontSize = 12, Opacity = 0.75, TextWrapping = TextWrapping.Wrap };
        readonly Button start = new Button { Content = "Start the test", IsDefault = true };
        readonly Border prompt = new Border { Padding = new Thickness(12), CornerRadius = new CornerRadius(4), BorderThickness = new Thickness(1), BorderBrush = new SolidColorBrush(Color.Parse("#60808080")), IsVisible = false, Margin = new Thickness(0, 10, 0, 0) };
        readonly TextBlock progressText = new TextBlock { FontSize = 15, FontWeight = FontWeight.SemiBold, TextWrapping = TextWrapping.Wrap };
        readonly ProgressBar progress = new ProgressBar { Minimum = 0, Height = 8, Margin = new Thickness(0, 6, 0, 6) };
        readonly TextBlock roundText = new TextBlock { FontSize = 12, TextWrapping = TextWrapping.Wrap, FontFamily = new FontFamily("Cascadia Mono,Consolas,Menlo,DejaVu Sans Mono,monospace") };
        readonly TextBlock stateText = new TextBlock { FontSize = 12, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 6, 0, 6) };
        readonly TextBox notes = new TextBox { AcceptsReturn = true, TextWrapping = TextWrapping.Wrap, MinHeight = 60, Watermark = "Notes on these spots (kept in the test's log)" };
        readonly Button next = new Button { Content = "Next 4 spots", IsDefault = false };
        readonly Button again = new Button { Content = "These 4 again" };
        readonly Button stop = new Button { Content = "Stop the test" };
        readonly Button stopWaiting = new Button { Content = "DOSBox is closed", IsVisible = false };
        readonly Button openFolder = new Button { Content = "Open the test's folder" };
        TaskCompletionSource<bool> waiting;

        public SpotTestRun Run { get; private set; }
        /// <summary>DOSBox is running on the round (the prompt waits for it to close).</summary>
        public bool Waiting { get { return waiting != null; } }
        /// <summary>DOSBox's program and arguments for the round launched last (for the tests).</summary>
        public (string Program, List<string> Arguments) LastLaunch { get; private set; }
        public string Status { get { return stateText.Text; } }
        /// <summary>DOSBox closing sooner than this (seconds) means the game did not run: a warning instead of the prompt's question.</summary>
        public double QuickExit = 5;

        static NumericUpDown Stat() { return new NumericUpDown { Minimum = 0, Maximum = 99, Increment = 1, FormatString = "0", Value = 30, Width = 110 }; }

        public SpotTestWindow(MainWindow main)
        {
            this.main = main;
            var s = main.ViewModel.Session;
            Title = "DOSBox spot test";
            Width = 920; Height = 780; MinWidth = 640; MinHeight = 500;
            WindowStartupLocation = WindowStartupLocation.CenterOwner;

            var head = new StackPanel { Spacing = 6, Margin = new Thickness(0, 0, 0, 10) };
            head.Children.Add(new TextBlock { Text = "DOSBox spot test", FontSize = 16, FontWeight = FontWeight.SemiBold });
            head.Children.Add(new TextBlock
            {
                TextWrapping = TextWrapping.Wrap, FontSize = 12, Opacity = 0.8,
                Text = "Here you play the squares the Underworld Exhumed check stops at, in the original game. Four at a time become saved games 1–4 in a copy of the game, " +
                       "all with your levels as they are now and a fresh tester standing on the square. DOSBox starts on the copy: restore each saved game in turn and look around. " +
                       "When you close DOSBox, this window asks for the next four. Nothing in your game folder is changed.",
            });

            var left = new DockPanel { Width = 230, Margin = new Thickness(0, 0, 12, 0) };
            var pickRow = new WrapPanel { Margin = new Thickness(0, 0, 0, 6) };
            var edited = new Button { Content = "Edited", Margin = new Thickness(0, 0, 6, 6) };
            edited.Click += (o, e) => Choose(ExhumedCheck.EditedLevels(main.ViewModel.Edits));
            var all = new Button { Content = "All", Margin = new Thickness(0, 0, 6, 6) };
            all.Click += (o, e) => Choose(boxes.Select(b => (int)b.Tag).ToList());
            var none = new Button { Content = "None", Margin = new Thickness(0, 0, 6, 6) };
            none.Click += (o, e) => Choose(new List<int>());
            pickRow.Children.Add(edited); pickRow.Children.Add(all); pickRow.Children.Add(none);
            var levelsHead = new TextBlock { Text = "Levels to visit", FontWeight = FontWeight.SemiBold, Margin = new Thickness(0, 0, 0, 4) };
            DockPanel.SetDock(levelsHead, Dock.Top); DockPanel.SetDock(pickRow, Dock.Top);
            left.Children.Add(levelsHead); left.Children.Add(pickRow);
            left.Children.Add(new ScrollViewer { Content = levelBoxes });
            if (s != null)
            {
                var dirty = new HashSet<int>(ExhumedCheck.EditedLevels(main.ViewModel.Edits));
                foreach (var slot in s.LevelSlots().Where(x => x.Present))
                {
                    var b = new CheckBox { Content = slot.Name + (dirty.Contains(slot.Level) ? "  (edited)" : ""), Tag = slot.Level, IsChecked = dirty.Contains(slot.Level) };
                    b.IsCheckedChanged += (o, e) => Estimate();
                    boxes.Add(b); levelBoxes.Children.Add(b);
                }
                if (dirty.Count == 0 && main.ViewModel.Level != null) Choose(new List<int> { main.ViewModel.Level.Level });
            }

            var th = main.ExhumedThoroughness;
            editsBox.IsChecked = th.VisitEdits; sweepBox.IsChecked = th.Sweep;
            foreach (var c in new[] { editsBox, sweepBox }) c.IsCheckedChanged += (o, e) => Estimate();
            ToolTip.SetTip(editsBox, "As in the Exhumed check: a square beside each edited spot, at most 12 per level and 5 squares apart.");
            ToolTip.SetTip(sweepBox, "As in the Exhumed check: a square in each 16 × 16 area of the level that has an open, flat, dry one.");

            var spots = new StackPanel { Spacing = 4 };
            spots.Children.Add(new TextBlock { Text = "Spots", FontWeight = FontWeight.SemiBold });
            spots.Children.Add(new TextBlock { Text = "Each level gets its own square (the start square on the start level), and the options below add more, the same as in the Underworld Exhumed check.", FontSize = 12, Opacity = 0.75, TextWrapping = TextWrapping.Wrap });
            spots.Children.Add(editsBox); spots.Children.Add(sweepBox);

            var tester = new StackPanel { Spacing = 4, Margin = new Thickness(0, 10, 0, 0) };
            tester.Children.Add(new TextBlock { Text = "The tester", FontWeight = FontWeight.SemiBold });
            tester.Children.Add(new TextBlock { Text = "A new character (level 1, no quests, no skills but Defense), made from a saved game's player when the game has one.", FontSize = 12, Opacity = 0.75, TextWrapping = TextWrapping.Wrap });
            var stats = new WrapPanel();
            foreach (var (label, box) in new[] { ("Strength", str), ("Dexterity", dex), ("Intelligence", intel), ("Defense", def) })
            {
                var cell = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 6, Margin = new Thickness(0, 0, 16, 4) };
                cell.Children.Add(new TextBlock { Text = label, VerticalAlignment = VerticalAlignment.Center, Width = 76 });
                cell.Children.Add(box);
                stats.Children.Add(cell);
            }
            tester.Children.Add(stats);
            tester.Children.Add(armourBox);

            var buttons = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8, Margin = new Thickness(0, 10, 0, 0) };
            start.Click += async (o, e) => await StartTest();
            openFolder.Click += (o, e) => { if (Run != null) OpenInShell(Run.Folder); };
            openFolder.IsEnabled = false;
            buttons.Children.Add(start); buttons.Children.Add(openFolder);

            // the prompt between rounds
            next.Classes.Add("accent");
            next.Click += async (o, e) => await NextRound();
            again.Click += async (o, e) => await Again();
            stop.Click += (o, e) => StopTest();
            stopWaiting.Click += (o, e) => DosBoxClosed();
            var promptButtons = new WrapPanel { Margin = new Thickness(0, 8, 0, 0) };
            foreach (var b in new[] { next, again, stop, stopWaiting }) { b.Margin = new Thickness(0, 0, 8, 6); promptButtons.Children.Add(b); }
            var pp = new StackPanel();
            pp.Children.Add(progressText); pp.Children.Add(progress); pp.Children.Add(roundText); pp.Children.Add(stateText); pp.Children.Add(notes); pp.Children.Add(promptButtons);
            prompt.Child = pp;

            var right = new StackPanel { Spacing = 2 };
            right.Children.Add(spots); right.Children.Add(tester); right.Children.Add(estimate); right.Children.Add(buttons); right.Children.Add(prompt);

            var body = new DockPanel();
            DockPanel.SetDock(left, Dock.Left);
            body.Children.Add(left); body.Children.Add(new ScrollViewer { Content = right });
            var dock = new DockPanel { Margin = new Thickness(14) };
            DockPanel.SetDock(head, Dock.Top);
            dock.Children.Add(head); dock.Children.Add(body);
            Content = dock;
            ManualWindow.Attach(this, "DOSBox spot test");
            if (s != null && s.IsUw2) { start.IsEnabled = false; estimate.Text = "The spot test is for UW1 for now: UW2's player record is laid out differently."; }
            else Estimate();
            Closing += (o, e) => { if (Run != null && !Run.Finished) SpotTest.Log(Run, "Window closed with " + Run.Remaining + " spot(s) left."); };
        }

        public void Choose(IList<int> levels) { foreach (var b in boxes) b.IsChecked = levels.Contains((int)b.Tag); }

        public List<int> ChosenLevels { get { return boxes.Where(b => b.IsChecked == true).Select(b => (int)b.Tag).ToList(); } }

        public SpotTestOptions Options
        {
            get
            {
                return new SpotTestOptions
                {
                    Stops = new ExhumedOptions { VisitEdits = editsBox.IsChecked == true, Sweep = sweepBox.IsChecked == true },
                    Strength = (int)(str.Value ?? 30), Dexterity = (int)(dex.Value ?? 30), Intelligence = (int)(intel.Value ?? 30), Defense = (int)(def.Value ?? 30),
                    BestArmour = armourBox.IsChecked == true,
                };
            }
        }

        public bool VisitEdits { get { return editsBox.IsChecked == true; } set { editsBox.IsChecked = value; } }
        public bool Sweep { get { return sweepBox.IsChecked == true; } set { sweepBox.IsChecked = value; } }

        /// <summary>How many spots the ticked levels give, and in how many rounds.</summary>
        public int Estimate()
        {
            var s = main.ViewModel.Session;
            if (s == null || s.IsUw2) return 0;
            try
            {
                int n = SpotTest.Spots(s, main.ViewModel.Edits, ChosenLevels, Options.Stops).Count;
                int rounds = (n + 3) / 4;
                estimate.Text = n == 0 ? "Tick at least one level." : string.Format("{0} spot{1} on {2} level{3}: {4} round{5} of up to four saved games.", n, n == 1 ? "" : "s", ChosenLevels.Count, ChosenLevels.Count == 1 ? "" : "s", rounds, rounds == 1 ? "" : "s");
                return n;
            }
            catch (Exception) { estimate.Text = ""; return 0; }
        }

        /// <summary>Sets the test up and launches the first round.</summary>
        public async Task<bool> StartTest()
        {
            var s = main.ViewModel.Session;
            if (s == null || Waiting) return false;
            if (Run != null && !Run.Finished)
            {
                string a = await MessageWindow.Ask(this, "DOSBox spot test", "A test is under way (" + Run.Tested + " of " + Run.Spots.Count + " spots tested). Start again from the first spot?", "Start again", "Cancel");
                if (a != "Start again") return false;
                SpotTest.Log(Run, "Started again with " + Run.Remaining + " spot(s) left.");
            }
            try { Run = SpotTest.Start(s, main.ViewModel.Edits, ChosenLevels, Options, main.SpotTestFolderFor(s)); }
            catch (Exception ex) { estimate.Text = "Could not set the test up: " + ex.Message; return false; }
            main.ExhumedThoroughness = new ExhumedOptions { VisitEdits = VisitEdits, Sweep = Sweep, LookAround = main.ExhumedThoroughness.LookAround };
            openFolder.IsEnabled = true;
            prompt.IsVisible = true;
            notes.Text = "";
            await Launch(Run.Next);
            return true;
        }

        /// <summary>The round just played counts as tested (with the notes); the next one is written and launched.</summary>
        public async Task NextRound()
        {
            if (Run == null || Waiting) return;
            SpotTest.FinishRound(Run, notes.Text);
            notes.Text = "";
            if (Run.Finished) { ShowFinished(); return; }
            await Launch(Run.Next);
        }

        /// <summary>The same round again (DOSBox started again on it).</summary>
        public async Task Again()
        {
            if (Run == null || Waiting) return;
            await Launch(Run.RoundStart);
        }

        /// <summary>Ends the test here; the log keeps what was tested.</summary>
        public void StopTest()
        {
            if (Run == null || Waiting) return;
            if (!string.IsNullOrWhiteSpace(notes.Text)) SpotTest.FinishRound(Run, notes.Text);
            SpotTest.Log(Run, string.Format("Stopped with {0} of {1} spots tested.", Run.Tested, Run.Spots.Count));
            progressText.Text = string.Format("Stopped: {0} of {1} spots tested, {2} not.", Run.Tested, Run.Spots.Count, Run.Remaining);
            stateText.Text = "The log of what was tested, with your notes, is at " + Path.Combine(Run.Folder, SpotTest.LogName) + ". Start the test again to begin from the first spot.";
            roundText.Text = "";
            SetButtons(false, false);
            notes.IsVisible = false;
            Run = null;
        }

        void ShowFinished()
        {
            SpotTest.Log(Run, "Finished: all " + Run.Spots.Count + " spots tested.");
            progressText.Text = string.Format("Done: all {0} spots tested.", Run.Spots.Count);
            progress.Maximum = Math.Max(1, Run.Spots.Count); progress.Value = Run.Spots.Count;
            roundText.Text = "";
            stateText.Text = "The log of the rounds, with your notes, is at " + Path.Combine(Run.Folder, SpotTest.LogName) + ".";
            notes.IsVisible = false;
            SetButtons(false, false);
            main.ViewModel.EditStatus = "DOSBox spot test finished: " + Run.Spots.Count + " spots.";
        }

        void SetButtons(bool between, bool waitingNow)
        {
            next.IsVisible = again.IsVisible = stop.IsVisible = between || waitingNow;
            next.IsEnabled = again.IsEnabled = stop.IsEnabled = between && !waitingNow;
            stopWaiting.IsVisible = waitingNow;
            start.IsEnabled = !waitingNow;
        }

        void ShowProgress()
        {
            int left = Run.Spots.Count - (Run.RoundStart + Run.RoundCount);
            progressText.Text = string.Format("{0} of {1} spots tested, {2} left after these {3}.", Run.Tested, Run.Spots.Count, Math.Max(0, left), Run.RoundCount);
            progress.Maximum = Math.Max(1, Run.Spots.Count); progress.Value = Run.Tested;
            roundText.Text = string.Join("\n", Enumerable.Range(0, Run.RoundCount).Select(i => SpotTest.Describe(Run, Run.RoundStart + i, i + 1)));
            Title = string.Format("DOSBox spot test: {0} of {1} tested", Run.Tested, Run.Spots.Count);
            next.Content = left <= 0 ? "Done: these were the last" : "Next " + Math.Min(4, left) + " spot" + (Math.Min(4, left) == 1 ? "" : "s");
            notes.IsVisible = true;
        }

        /// <summary>Writes the round from spot first and runs DOSBox on it; returns when DOSBox is closed.</summary>
        async Task Launch(int first)
        {
            var s = main.ViewModel.Session;
            try { SpotTest.WriteRound(s, Run, first); }
            catch (Exception ex) { stateText.Text = "Could not write the saved games: " + ex.Message; SetButtons(true, false); return; }
            ShowProgress();
            string howTo = "In the game, choose Journey Onward and the first saved game; for each of the others, use Options ▸ Restore Game. Each saved game's description names its spot: its number out of the total, the level, x,y and why.";
            if (!main.SpotTestLaunch)
            {
                stateText.Text = "The saved games are ready in " + Run.Folder + " (DOSBox was not started). " + howTo;
                SetButtons(true, false);
                return;
            }
            string dosbox = main.DosBoxFound();
            if (dosbox == null)
            {
                stateText.Text = "DOSBox was not found: choose it in Run and Test ▸ Programs and paths, then press These 4 again. The saved games are ready in " + Run.Folder + ".";
                SetButtons(true, false);
                main.ShowRunPaths();
                return;
            }
            Process p;
            try
            {
                string conf = main.WriteDosBoxConf(Run.Folder, Run.Exe, "spottest-uw1");
                var args = DosBoxConfig.Arguments(conf, main.DosBoxArgs);
                LastLaunch = (dosbox, args);
                var psi = new ProcessStartInfo(dosbox) { UseShellExecute = false, WorkingDirectory = Path.GetDirectoryName(dosbox) ?? Run.Folder };
                foreach (var a in args) psi.ArgumentList.Add(a);
                p = Process.Start(psi);
            }
            catch (Exception ex) { stateText.Text = "DOSBox did not start (" + dosbox + "): " + ex.Message; SetButtons(true, false); return; }
            stateText.Text = "DOSBox is running. " + howTo + " Close DOSBox when you are done with these.";
            SetButtons(false, true);
            waiting = new TaskCompletionSource<bool>();
            var started = DateTime.UtcNow;
            var exit = p == null ? Task.CompletedTask : p.WaitForExitAsync();
            await Task.WhenAny(exit, waiting.Task);
            bool quick = p != null && p.HasExited && (DateTime.UtcNow - started).TotalSeconds < QuickExit;
            waiting = null;
            AfterDosBox(quick);
        }

        /// <summary>DOSBox counts as closed (the button, for a DOSBox that hands over to another program and leaves).</summary>
        public void DosBoxClosed() { waiting?.TrySetResult(true); }

        void AfterDosBox(bool quick)
        {
            if (Run == null) return;
            SetButtons(true, false);
            stateText.Text = quick
                ? "DOSBox closed within seconds. If the game did not run, check DOSBox in Run and Test ▸ Programs and paths, then press These 4 again."
                : "DOSBox is closed. Did these four look right? Write down anything wrong, then go on.";
            try { Activate(); } catch (Exception) { }
        }

        static void OpenInShell(string dir)
        {
            try { Process.Start(new ProcessStartInfo(dir) { UseShellExecute = true }); } catch (Exception) { }
        }
    }
}
