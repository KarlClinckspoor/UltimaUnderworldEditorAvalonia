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
    /// mechanisms; Phase B: abilities and logic options; Phase C: the world changing over time),
    /// compares them with a painted zoning, runs the timeline solver, and hands the result to the
    /// zones window or a JSON file.
    /// </summary>
    public sealed class AutoZonesWindow : Window
    {
        readonly MainWindow main;
        readonly GameSession session;
        readonly NavOptions opt = new NavOptions();
        readonly AbilityOptions abil = new AbilityOptions();
        readonly TextBox report = new TextBox { IsReadOnly = true, AcceptsReturn = true, TextWrapping = TextWrapping.NoWrap, FontFamily = new FontFamily("Cascadia Mono,Consolas,DejaVu Sans Mono,monospace"), FontSize = 12 };
        readonly TextBlock summary = new TextBlock { TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 4, 0, 4) };
        readonly ComboBox compareBox = new ComboBox { Width = 330 };
        readonly Button openButton = new Button { Content = "Open in the Zones window", IsEnabled = false };
        readonly Button exportButton = new Button { Content = "Export JSON…", IsEnabled = false };
        readonly Button reportButton = new Button { Content = "Report", IsEnabled = false };

        /// <summary>The last build, or null.</summary>
        public AutoZoneResult Result { get; private set; }
        /// <summary>The last build's abilities, sources, hidden gates and solves (Phase B), or null.</summary>
        public AbilityZoneResult Abilities { get; private set; }
        /// <summary>The last build's world changes, timeline solves and return check (Phase C), or null.</summary>
        public TimelineResult Timeline { get; private set; }
        /// <summary>The logic options of the next build (Phase B).</summary>
        public AbilityOptions LogicOptions { get { return abil; } }
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
                       "The solver follows the world as it changes: when the player can set a mechanism off, its change (terrain, deleted walls, doors) is applied and that level rebuilt. " +
                       "Doors closed behind the player are one way. A return check looks for zones he can enter but not leave. " +
                       "The logic options say what the solver may expect of the player: where each ability comes from, picking, the Search skill. Bashing doors is always allowed." + (s.IsUw2 ? " Made for UW1; UW2's levels are read the same way but not checked." : ""),
            });
            // the movement and logic options (shared with the Apply window)
            top.Children.Add(new LogicOptionsPanel(opt, abil, true));
            compareBox.ItemsSource = CompareChoices();
            compareBox.SelectedIndex = 0;
            top.Children.Add(Row(Label("Compare with"), compareBox));

            var build = new Button { Content = "Build", FontWeight = FontWeight.SemiBold };
            build.Click += (o, e) => BuildNow();
            reportButton.Click += (o, e) => { if (Result != null) report.Text = FullReport(); };
            openButton.Click += (o, e) => OpenInZones();
            exportButton.Click += (o, e) => Export();
            ToolTip.SetTip(reportButton, "The solve over time: the timeline sphere by sphere, the changes set off, the doors closed behind, the return check and the goal with the main option sets.");
            var buttons = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8, Margin = new Thickness(0, 8, 0, 4) };
            buttons.Children.Add(build); buttons.Children.Add(reportButton); buttons.Children.Add(openButton); buttons.Children.Add(exportButton);
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
                Timeline = Core.Randomizer.Timeline.Build(session, level, opt, abil.Clone(), Painted);
                Abilities = Timeline.Abilities;
                Result = Abilities.Auto;
                var g = Result.Graph;
                var zs = Result.Set;
                var sv = Timeline.Solve;
                summary.Text = string.Format("{0} regions, {1} edges, {2} zones, {3} links, {4} placements, {5} mechanisms ({6} applied when set off, {7} doors closed behind), {8} levers ({9} out of reach), {10} ability sources, {11} hidden gates. Timeline: {12}, {13} of {14} zones reached, {15} changes applied, {16} one-way traps.",
                    g.Regions.Count, g.Edges.Count, zs.Zones.Count(z => z.Id > ZoneSet.Unassigned), zs.Links.Count, zs.Placements.Count, g.Mechanisms.Count, Timeline.Changes.Count(c => c.Applied), Timeline.Closers.Count, g.Levers.Count, g.Levers.Count(l => l.OutOfReach),
                    Abilities.Sources.Count, Abilities.Hidden.Count,
                    sv == null ? "failed" : sv.GoalReached ? "goal reached" : "goal not reached", sv?.ZoneSphere.Count(kv => kv.Key < 1000) ?? 0, zs.Zones.Count(z => z.Id > ZoneSet.Unassigned), sv?.Applied.Count ?? 0, sv?.Traps.Count ?? 0);
                report.Text = FullReport();
                openButton.IsEnabled = exportButton.IsEnabled = reportButton.IsEnabled = true;
            }
            catch (Exception ex) { summary.Text = "Could not build the zones: " + ex.Message; }
        }

        /// <summary>The report: the solve over time (Phase C), which the earlier phases' reports are steps towards.</summary>
        public string FullReport()
        {
            if (Result == null) return "";
            return Timeline != null ? Core.Randomizer.Timeline.Report(Timeline) : AbilityZones.Report(Abilities);
        }

        /// <summary>The report shown now.</summary>
        public string ReportText { get { return report.Text; } }

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
    }
}
