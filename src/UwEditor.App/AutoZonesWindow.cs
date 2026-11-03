using System;
using System.IO;
using System.Linq;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Platform.Storage;
using UwEditor.Core;
using UwEditor.Core.Randomizer;

namespace UwEditor.App
{
    /// <summary>
    /// Builds the randomizer's zones from the levels (Phase A: regions, edges, levers and
    /// mechanisms), compares them with a painted zoning, runs the solver, and hands the result to
    /// the zones window or a JSON file.
    /// </summary>
    public sealed class AutoZonesWindow : Window
    {
        readonly MainWindow main;
        readonly GameSession session;
        readonly NavOptions opt = new NavOptions();
        readonly TextBox report = new TextBox { IsReadOnly = true, AcceptsReturn = true, TextWrapping = TextWrapping.NoWrap, FontFamily = new FontFamily("Cascadia Mono,Consolas,DejaVu Sans Mono,monospace"), FontSize = 12 };
        readonly TextBlock summary = new TextBlock { TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 4, 0, 4) };
        readonly ComboBox compareBox = new ComboBox { Width = 330 };
        readonly Button openButton = new Button { Content = "Open in the Zones window", IsEnabled = false };
        readonly Button exportButton = new Button { Content = "Export JSON…", IsEnabled = false };
        readonly Button reportButton = new Button { Content = "Report", IsEnabled = false };

        /// <summary>The last build, or null.</summary>
        public AutoZoneResult Result { get; private set; }
        /// <summary>The painted zoning the last build was compared with, or null.</summary>
        public ZoneSet Painted { get; private set; }

        public AutoZonesWindow(MainWindow main, GameSession s)
        {
            this.main = main;
            session = s;
            Title = "Automatic zones";
            Width = 980; Height = 760; MinWidth = 640; MinHeight = 480;
            WindowStartupLocation = WindowStartupLocation.CenterOwner;
            opt.MaxClimb = main.ZoneClimb;
            opt.JumpSpellClimb = 2 * opt.MaxClimb;

            var top = new StackPanel { Spacing = 6 };
            top.Children.Add(new TextBlock { Text = "Automatic zones", FontSize = 16, FontWeight = FontWeight.SemiBold });
            top.Children.Add(new TextBlock
            {
                TextWrapping = TextWrapping.Wrap, FontSize = 12, Opacity = 0.75,
                Text = "Groups each level's open tiles into regions where the player walks anywhere and back, joins them by drops, climbs, gap jumps, doors and the levels' teleports, " +
                       "reads the levers and other trigger chains, and merges what is reachable both ways for free into zones. Movement is by rules of thumb; ceilings are ignored. " +
                       "World changes (terrain, deleted walls) are listed, not applied yet." + (s.IsUw2 ? " Made for UW1; UW2's levels are read the same way but not checked." : ""),
            });
            var climb = Num(opt.MaxClimb, 1, 15, v => { opt.MaxClimb = v; });
            var spellClimb = Num(opt.JumpSpellClimb, 1, 15, v => opt.JumpSpellClimb = v);
            var gap = Num(opt.GapTiles, 0, 6, v => opt.GapTiles = v);
            var spellGap = Num(opt.JumpSpellGapTiles, 0, 8, v => opt.JumpSpellGapTiles = v);
            top.Children.Add(Row(Label("Highest climb"), climb, Note("floor steps, both ways inside a region (the Zones window's setting)"), Label("with the jump spell", 130), spellClimb));
            top.Children.Add(Row(Label("Gap jumps"), Check("on", opt.GapJumps, v => opt.GapJumps = v), Label("tiles", 40), gap, Label("with the jump spell", 130), spellGap,
                Check("diagonals too", opt.DiagonalJumps, v => opt.DiagonalJumps = v)));
            top.Children.Add(Row(Label("Zones"), Check("Cut at level changes", opt.CutAtLevelChanges, v => opt.CutAtLevelChanges = v),
                Check("Fold empty dead ends (pits and ledges with nothing in them)", opt.FoldEmptyDeadEnds, v => opt.FoldEmptyDeadEnds = v)));
            compareBox.ItemsSource = CompareChoices();
            compareBox.SelectedIndex = 0;
            top.Children.Add(Row(Label("Compare with"), compareBox));

            var build = new Button { Content = "Build", FontWeight = FontWeight.SemiBold };
            build.Click += (o, e) => BuildNow();
            reportButton.Click += (o, e) => { if (Result != null) report.Text = AutoZones.Report(Result, Painted, session); };
            openButton.Click += (o, e) => OpenInZones();
            exportButton.Click += (o, e) => Export();
            var solve = new Button { Content = "Solver report", IsEnabled = true };
            solve.Click += (o, e) => { if (Result?.Solve != null) report.Text = Result.Solve.Report(Result.Set); };
            var buttons = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8, Margin = new Thickness(0, 8, 0, 4) };
            buttons.Children.Add(build); buttons.Children.Add(reportButton); buttons.Children.Add(solve); buttons.Children.Add(openButton); buttons.Children.Add(exportButton);
            top.Children.Add(buttons);
            top.Children.Add(summary);

            var root = new Grid { Margin = new Thickness(16), RowDefinitions = new RowDefinitions("Auto,*") };
            Grid.SetRow(report, 1);
            root.Children.Add(top); root.Children.Add(report);
            Content = root;
            ManualWindow.Attach(this, "Automatic zones");
        }

        string[] CompareChoices()
        {
            var list = new System.Collections.Generic.List<string> { "Nothing" };
            if (!session.IsUw2) list.Insert(0, "Karl's painted UW1 map (shipped)");
            if (File.Exists(Path.Combine(session.DataFolder, ZoneSet.DefaultFileName(session.IsUw2)))) list.Insert(0, "The zone file in DATA (" + ZoneSet.DefaultFileName(session.IsUw2) + ")");
            return list.ToArray();
        }

        ZoneSet ChosenPainted()
        {
            string c = compareBox.SelectedItem as string ?? "";
            try
            {
                if (c.StartsWith("The zone file")) return ZoneSet.Load(Path.Combine(session.DataFolder, ZoneSet.DefaultFileName(session.IsUw2)), session.Profile.LevelSlots);
                if (c.StartsWith("Karl"))
                    using (var st = typeof(ZoningState).Assembly.GetManifestResourceStream("uw1-zones.example.json"))
                    using (var r = new StreamReader(st))
                        return ZoneSet.FromJson(r.ReadToEnd(), session.Profile.LevelSlots);
            }
            catch (Exception) { }
            return null;
        }

        /// <summary>Builds the zones with the current options (the levels with their unsaved edits).</summary>
        public void BuildNow()
        {
            try
            {
                Painted = ChosenPainted();
                var edits = main.ViewModel.Edits;
                Func<int, LevelModel> level = l => edits != null ? edits.Model(l) : session.LoadLevel(l);
                Result = AutoZones.Build(session, level, opt, Painted);
                var g = Result.Graph;
                var zs = Result.Set;
                var sv = Result.Solve;
                summary.Text = string.Format("{0} regions, {1} edges, {2} zones, {3} links, {4} placements, {5} mechanisms, {6} levers ({7} out of reach). Solver: {8}, {9} of {10} zones reached{11}.",
                    g.Regions.Count, g.Edges.Count, zs.Zones.Count(z => z.Id > ZoneSet.Unassigned), zs.Links.Count, zs.Placements.Count, g.Mechanisms.Count, g.Levers.Count, g.Levers.Count(l => l.OutOfReach),
                    sv == null ? "failed" : sv.GoalReached ? "goal reached" : "goal not reached", sv?.ZoneSphere.Count ?? 0, zs.Zones.Count(z => z.Id > ZoneSet.Unassigned),
                    Result.SolveWithLava != null ? string.Format(" ({0} with hazard:lava assumed)", Result.SolveWithLava.ZoneSphere.Count) : "");
                report.Text = AutoZones.Report(Result, Painted, session);
                openButton.IsEnabled = exportButton.IsEnabled = reportButton.IsEnabled = true;
            }
            catch (Exception ex) { summary.Text = "Could not build the zones: " + ex.Message; }
        }

        string SuggestedPath { get { return Path.Combine(session.DataFolder, AutoZones.DefaultFileName(session.IsUw2)); } }

        async void OpenInZones()
        {
            if (Result == null) return;
            var z = main.Zoning();
            if (z == null) return;
            if (z.Dirty)
            {
                string r = await MessageWindow.Ask(this, "Unsaved zoning", "The Zones window's zoning has unsaved changes. Replace it with the automatic zones anyway?", "Replace", "Cancel");
                if (r != "Replace") return;
            }
            // a copy, so that editing it there leaves this window's result alone
            z.Adopt(ZoneSet.FromJson(Result.Set.ToJson(), session.Profile.LevelSlots), SuggestedPath);
            main.ShowZones();
            summary.Text = "Opened in the Zones window. Saving there asks for a file name (" + AutoZones.DefaultFileName(session.IsUw2) + " suggested); the painted " + ZoneSet.DefaultFileName(session.IsUw2) + " is not touched.";
        }

        async void Export()
        {
            if (Result == null) return;
            var f = await StorageProvider.SaveFilePickerAsync(new FilePickerSaveOptions
            {
                Title = "Export the automatic zones", SuggestedFileName = AutoZones.DefaultFileName(session.IsUw2), DefaultExtension = "json",
                FileTypeChoices = new[] { new FilePickerFileType("Zone file") { Patterns = new[] { "*.json" } } },
            });
            string path = f?.TryGetLocalPath();
            if (path == null) return;
            if (string.Equals(Path.GetFileName(path), ZoneSet.DefaultFileName(session.IsUw2), StringComparison.OrdinalIgnoreCase) && File.Exists(path))
            {
                string r = await MessageWindow.Ask(this, "Replace the painted zones?", "This replaces " + Path.GetFileName(path) + ", the painted zoning. Export there anyway?", "Replace", "Cancel");
                if (r != "Replace") return;
            }
            try { Result.Set.Save(path); summary.Text = "Exported " + path + "."; }
            catch (Exception ex) { summary.Text = "Could not export: " + ex.Message; }
        }

        // ---- small builders, as the other randomizer windows

        static StackPanel Row(params Control[] c)
        {
            var p = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8 };
            foreach (var x in c) if (x != null) p.Children.Add(x);
            return p;
        }

        static TextBlock Label(string t, double w = 110) { return new TextBlock { Text = t, Width = w, VerticalAlignment = VerticalAlignment.Center }; }
        static TextBlock Note(string t) { return new TextBlock { Text = t, FontSize = 12, Opacity = 0.7, VerticalAlignment = VerticalAlignment.Center }; }

        static CheckBox Check(string t, bool on, Action<bool> set)
        {
            var c = new CheckBox { Content = t, IsChecked = on, VerticalAlignment = VerticalAlignment.Center };
            c.IsCheckedChanged += (o, e) => set(c.IsChecked == true);
            return c;
        }

        static NumericUpDown Num(int v, int min, int max, Action<int> set)
        {
            var n = new NumericUpDown { Minimum = min, Maximum = max, Increment = 1, FormatString = "0", Value = v, Width = 110 };
            n.ValueChanged += (o, e) => set((int)(n.Value ?? v));
            return n;
        }
    }
}
