using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Platform.Storage;
using UwEditor.Core;

namespace UwEditor.App
{
    /// <summary>
    /// Level ▸ Check in Underworld Exhumed: the edits loaded by the game's own code, in the native
    /// port of the decompiled games, run hidden: the player is sent to each chosen level, and in
    /// UW1 the game saves at the end so that the levels it wrote are checked again here.
    /// </summary>
    public sealed class ExhumedWindow : Window
    {
        readonly MainWindow main;
        readonly StackPanel levelBoxes = new StackPanel { Spacing = 2 };
        readonly TextBlock portText = new TextBlock { FontSize = 12, TextWrapping = TextWrapping.Wrap };
        readonly CheckBox saveBox = new CheckBox { Content = "Save in slot I at the end, and check the levels the game wrote", IsChecked = true };
        readonly CheckBox aliveBox = new CheckBox { Content = "Keep the player alive (strength 99, defense 30, full hit points before each level)", IsChecked = true };
        readonly Button untick = new Button { Content = "Untick the levels with nowhere to stand", IsVisible = false };
        readonly CheckBox editsBox = new CheckBox { Content = "Also stop beside every edit (tiles and objects changed), so what is near them is drawn and runs" };
        readonly CheckBox sweepBox = new CheckBox { Content = "Sweep each level: also one stop in every 16 × 16 area with room to stand" };
        readonly CheckBox lookBox = new CheckBox { Content = "Look around at each stop (turn round once, about 3.5 s more a stop)" };
        readonly TextBlock estimate = new TextBlock { FontSize = 12, Opacity = 0.75, TextWrapping = TextWrapping.Wrap };
        List<int> nowhere = new List<int>();
        readonly TextBox report = new TextBox { IsReadOnly = true, AcceptsReturn = true, TextWrapping = TextWrapping.Wrap, FontFamily = new FontFamily("Cascadia Mono,Consolas,Menlo,DejaVu Sans Mono,monospace"), FontSize = 12, MinHeight = 160 };
        readonly Image shot = new Image { Width = 320, Height = 200, Stretch = Stretch.Uniform };
        readonly Button run = new Button { Content = "Run the check", IsDefault = true };
        readonly Button openFolder = new Button { Content = "Open the check's folder", IsEnabled = false };
        readonly List<CheckBox> boxes = new List<CheckBox>();
        Border shotBox;
        public ExhumedPlan LastPlan { get; private set; }
        public ExhumedResult LastResult { get; private set; }
        public bool Running { get; private set; }

        public ExhumedWindow(MainWindow main)
        {
            this.main = main;
            var s = main.ViewModel.Session;
            Title = "Check in Underworld Exhumed";
            Width = 900; Height = 760; MinWidth = 600; MinHeight = 460;
            WindowStartupLocation = WindowStartupLocation.CenterOwner;

            var head = new StackPanel { Spacing = 6, Margin = new Thickness(0, 0, 0, 10) };
            head.Children.Add(new TextBlock { Text = "Check in Underworld Exhumed", FontSize = 16, FontWeight = FontWeight.SemiBold });
            head.Children.Add(new TextBlock
            {
                TextWrapping = TextWrapping.Wrap, FontSize = 12, Opacity = 0.8,
                Text = "Underworld Exhumed is a native port of the original game, built from its decompiled source, so it loads levels with the game's own code. " +
                       "The check copies the game with your unsaved levels into a saved game, runs the port with no window, sends the player to each level ticked below " +
                       "(the port's test cheat), and reports crashes, halts and the game's own fatal errors. In UW1 the game then saves, and the levels it wrote are checked again here. " +
                       "Get the port from github.com/abedegno/underworld-exhumed (Releases: " + (s != null && s.IsUw2 ? "uw2-…" : "uw1-…") + "). Nothing in your game folder is changed.",
            });
            var portRow = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8 };
            var choose = new Button { Content = "Choose the port…" };
            choose.Click += async (o, e) => await ChoosePort();
            portRow.Children.Add(choose); portRow.Children.Add(portText);
            head.Children.Add(portRow);

            var left = new DockPanel { Width = 230, Margin = new Thickness(0, 0, 12, 0) };
            var pickRow = new WrapPanel { Margin = new Thickness(0, 0, 0, 6) };
            var edited = new Button { Content = "Edited", Margin = new Thickness(0, 0, 6, 6) };
            edited.Click += (o, e) => Tick(ExhumedCheck.EditedLevels(main.ViewModel.Edits));
            var all = new Button { Content = "All", Margin = new Thickness(0, 0, 6, 6) };
            all.Click += (o, e) => Tick(boxes.Select(b => (int)b.Tag).ToList());
            var none = new Button { Content = "None", Margin = new Thickness(0, 0, 6, 6) };
            none.Click += (o, e) => Tick(new List<int>());
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
                    var b = new CheckBox { Content = (s.IsUw2 ? slot.Level + " " + slot.Name : slot.Name) + (dirty.Contains(slot.Level) ? "  (edited)" : ""), Tag = slot.Level, IsChecked = dirty.Contains(slot.Level) };
                    boxes.Add(b); levelBoxes.Children.Add(b);
                }
                if (dirty.Count == 0 && main.ViewModel.Level != null) Tick(new List<int> { main.ViewModel.Level.Level });
                saveBox.IsVisible = !s.IsUw2;
            }

            var buttons = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8, Margin = new Thickness(0, 8, 0, 0) };
            run.Click += async (o, e) => await RunCheck();
            openFolder.Click += (o, e) => { if (LastPlan != null) OpenInShell(LastPlan.Folder); };
            buttons.Children.Add(run); buttons.Children.Add(openFolder);
            var top = new StackPanel { Spacing = 4 };
            ToolTip.SetTip(aliveBox, "Underworld Exhumed has no god mode. Its test cheat sets strength and defense (the maximum hit points follow), and fills the hit points before each level, so monsters waiting on a level don't end the check.");
            untick.Click += (o, e) =>
            {
                foreach (var b in boxes) if (nowhere.Contains((int)b.Tag)) b.IsChecked = false;
                untick.IsVisible = false;
                report.Text = "Unticked " + string.Join(", ", nowhere) + ". Run the check again.";
            };
            buttons.Children.Add(untick);
            var th = main.ExhumedThoroughness;
            editsBox.IsChecked = th.VisitEdits; sweepBox.IsChecked = th.Sweep; lookBox.IsChecked = th.LookAround;
            foreach (var c in new[] { editsBox, sweepBox, lookBox })
                c.IsCheckedChanged += (o, e) => { main.ExhumedThoroughness = Thoroughness; Estimate(); };
            foreach (var b in boxes) b.IsCheckedChanged += (o, e) => Estimate();
            ToolTip.SetTip(editsBox, "Creatures more than 10 squares from the player stand frozen, triggers fire only when walked on, and only what is in view is drawn: a stop beside each edited spot makes the game run and draw what changed. At most 12 such stops a level, 5 squares apart.");
            ToolTip.SetTip(sweepBox, "A stop in each 16 × 16 area of the level that has an open, flat, dry square: up to 16 a level, about 5 s each.");
            ToolTip.SetTip(lookBox, "Holds the turn-left key for one full turn at each stop, so the game draws everything around (bad textures and models show only when drawn).");
            top.Children.Add(saveBox); top.Children.Add(aliveBox); top.Children.Add(editsBox); top.Children.Add(sweepBox); top.Children.Add(lookBox); top.Children.Add(estimate); top.Children.Add(buttons);

            var right = new DockPanel();
            shotBox = new Border { Child = shot, BorderBrush = new SolidColorBrush(Color.Parse("#40808080")), BorderThickness = new Thickness(1), HorizontalAlignment = HorizontalAlignment.Left, Margin = new Thickness(0, 8, 0, 0), IsVisible = false };
            DockPanel.SetDock(top, Dock.Top); DockPanel.SetDock(shotBox, Dock.Bottom);
            right.Children.Add(top); right.Children.Add(shotBox); right.Children.Add(report);
            report.Margin = new Thickness(0, 8, 0, 0);

            var body = new DockPanel();
            DockPanel.SetDock(left, Dock.Left);
            body.Children.Add(left); body.Children.Add(right);
            var dock = new DockPanel { Margin = new Thickness(14) };
            DockPanel.SetDock(head, Dock.Top);
            dock.Children.Add(head); dock.Children.Add(body);
            Content = dock;
            ManualWindow.Attach(this, "Checking in Underworld Exhumed");
            ShowPort();
            report.Text = "Tick the levels, then Run the check. A stop takes about " + ExhumedCheck.VisitMs / 1000 + " seconds.";
            Estimate();
        }

        void Tick(IList<int> levels)
        {
            foreach (var b in boxes) b.IsChecked = levels.Contains((int)b.Tag);
        }

        public List<int> ChosenLevels { get { return boxes.Where(b => b.IsChecked == true).Select(b => (int)b.Tag).ToList(); } }

        public void Choose(IList<int> levels) { Tick(levels); }

        /// <summary>The thoroughness ticked.</summary>
        public ExhumedOptions Thoroughness
        {
            get { return new ExhumedOptions { VisitEdits = editsBox.IsChecked == true, Sweep = sweepBox.IsChecked == true, LookAround = lookBox.IsChecked == true }; }
            set { editsBox.IsChecked = value.VisitEdits; sweepBox.IsChecked = value.Sweep; lookBox.IsChecked = value.LookAround; }
        }

        /// <summary>How many stops the ticked levels make and about how long the run takes.</summary>
        public int Estimate()
        {
            var s = main.ViewModel.Session;
            if (s == null) return 0;
            int stops = 0;
            try
            {
                var o = Thoroughness;
                foreach (int l in ChosenLevels) stops += ExhumedCheck.Stops(s, main.ViewModel.Edits, l, o).Count;
                int secs = stops * (ExhumedCheck.VisitMs + (o.LookAround ? ExhumedCheck.LookMs : 0)) / 1000 + 25;
                estimate.Text = string.Format("{0} stop{1} on {2} level{3}: about {4}.", stops, stops == 1 ? "" : "s", ChosenLevels.Count, ChosenLevels.Count == 1 ? "" : "s",
                    secs < 120 ? secs + " seconds" : Math.Round(secs / 60.0) + " minutes");
            }
            catch (Exception) { estimate.Text = ""; }
            return stops;
        }

        /// <summary>The levels the last setup refused for having nowhere to stand.</summary>
        public List<int> NowhereToStand { get { return nowhere; } }

        /// <summary>Unticks the levels with nowhere to stand (the button's work).</summary>
        public void UntickNowhere() { untick.RaiseEvent(new Avalonia.Interactivity.RoutedEventArgs(Button.ClickEvent)); }

        void ShowPort()
        {
            var s = main.ViewModel.Session;
            string p = s == null ? null : main.ExhumedPort(s.Kind);
            portText.Text = p != null ? p : "Not found: choose " + (s == null ? "the port" : ExhumedCheck.ProgramName(s.Kind)) + " (unpack a release of Underworld Exhumed).";
            portText.Opacity = p != null ? 0.8 : 1;
        }

        async Task ChoosePort()
        {
            var s = main.ViewModel.Session;
            if (s == null) return;
            var files = await StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions { Title = "Choose " + ExhumedCheck.ProgramName(s.Kind), AllowMultiple = false });
            string p = files.Count > 0 ? files[0].TryGetLocalPath() : null;
            if (p == null) return;
            main.SetExhumedPort(s.Kind, p);
            ShowPort();
        }

        /// <summary>Sets the check up, runs the port and reports. Returns the result, or null when it could not run.</summary>
        public async Task<ExhumedResult> RunCheck()
        {
            var vm = main.ViewModel;
            var s = vm.Session;
            if (s == null || Running) return null;
            string port = main.ExhumedPort(s.Kind);
            if (port == null && main.ExhumedLaunch) { report.Text = "Choose the port first (" + ExhumedCheck.ProgramName(s.Kind) + ")."; return null; }
            Running = true; run.IsEnabled = false;
            try
            {
                report.Text = "Setting up…";
                ExhumedPlan plan;
                untick.IsVisible = false;
                try { plan = main.PrepareExhumed(ChosenLevels, saveBox.IsChecked == true, aliveBox.IsChecked == true, Thoroughness); }
                catch (NowhereToStandException ex)
                {
                    nowhere = ex.Levels;
                    untick.IsVisible = true;
                    report.Text = "Could not set the check up: " + ex.Message;
                    return null;
                }
                catch (Exception ex) { report.Text = "Could not set the check up: " + ex.Message; return null; }
                LastPlan = plan;
                openFolder.IsEnabled = true;
                if (!main.ExhumedLaunch) { report.Text = "Ready in " + plan.Folder + " (not run).\n" + plan.Note; return null; }
                report.Text = "Running Underworld Exhumed with no window: " + plan.Note + "\nThis takes about " + (plan.ExitAfterMs / 1000 + 5) + " seconds.";
                var (code, output, timedOut) = await RunPort(port, plan);
                var result = ExhumedCheck.Analyze(s, plan, code, output, timedOut);
                LastResult = result;
                var sb = new StringBuilder();
                sb.AppendLine(result.Report());
                sb.AppendLine("What was done: " + plan.Note);
                sb.AppendLine("The check's files: " + plan.Folder + " (script.txt is the input script; home/SAVE1 the saved game).");
                sb.AppendLine();
                sb.AppendLine("The port's output:");
                sb.AppendLine(string.IsNullOrWhiteSpace(output) ? "(nothing)" : output.TrimEnd());
                report.Text = sb.ToString();
                ShowShot(plan.Screenshot);
                vm.EditStatus = result.Passed ? "Underworld Exhumed check passed." : "Underworld Exhumed check: " + result.Problems.Count + " problem(s).";
                return result;
            }
            finally { Running = false; run.IsEnabled = true; }
        }

        void ShowShot(string path)
        {
            try { shot.Source = File.Exists(path) ? new Bitmap(path) : null; } catch (Exception) { shot.Source = null; }
            shotBox.IsVisible = shot.Source != null;
        }

        /// <summary>Runs the port, reading what it prints, and stops it if it outlives its exit time by a minute.</summary>
        static async Task<(int Code, string Output, bool TimedOut)> RunPort(string port, ExhumedPlan plan)
        {
            var psi = new ProcessStartInfo(port) { UseShellExecute = false, RedirectStandardOutput = true, RedirectStandardError = true, CreateNoWindow = true, WorkingDirectory = plan.Folder };
            foreach (var a in plan.Arguments) psi.ArgumentList.Add(a);
            foreach (var kv in plan.Environment) psi.Environment[kv.Key] = kv.Value;
            var output = new StringBuilder();
            object gate = new object();
            using (var p = new Process { StartInfo = psi })
            {
                p.OutputDataReceived += (o, e) => { if (e.Data != null) lock (gate) output.AppendLine(e.Data); };
                p.ErrorDataReceived += (o, e) => { if (e.Data != null) lock (gate) output.AppendLine(e.Data); };
                try { p.Start(); }
                catch (Exception ex) { return (-1, "The port did not start: " + ex.Message, false); }
                p.BeginOutputReadLine(); p.BeginErrorReadLine();
                var exited = await Task.Run(() => p.WaitForExit(plan.ExitAfterMs + 60000));
                if (!exited) { try { p.Kill(true); } catch (Exception) { } }
                else p.WaitForExit();   // the rest of the output
                lock (gate) return (exited ? p.ExitCode : -1, output.ToString(), !exited);
            }
        }

        static void OpenInShell(string dir)
        {
            try { Process.Start(new ProcessStartInfo(dir) { UseShellExecute = true }); } catch (Exception) { }
        }
    }
}
