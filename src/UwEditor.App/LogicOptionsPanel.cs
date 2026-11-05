using System;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using UwEditor.Core.Randomizer;

namespace UwEditor.App
{
    /// <summary>
    /// Rows of options lined up in columns: a label column (110 wide), then cells that take the
    /// width of the widest one in their column, so the checkboxes of every row start at the same
    /// places. A row with fewer cells than columns lets its last cell span the rest.
    /// </summary>
    public sealed class OptionGrid : Grid
    {
        readonly int columns;

        public OptionGrid(int columns)
        {
            this.columns = columns;
            ColumnDefinitions.Add(new ColumnDefinition(110, GridUnitType.Pixel));
            for (int i = 0; i < columns; i++) ColumnDefinitions.Add(new ColumnDefinition(GridLength.Auto));
            ColumnDefinitions.Add(new ColumnDefinition(1, GridUnitType.Star));
        }

        public void AddRow(string label, params Control[] cells)
        {
            int row = RowDefinitions.Count;
            RowDefinitions.Add(new RowDefinition(GridLength.Auto));
            if (!string.IsNullOrEmpty(label))
            {
                var l = new TextBlock { Text = label, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 1, 8, 1) };
                SetRow(l, row);
                Children.Add(l);
            }
            for (int i = 0; i < cells.Length && i < columns; i++)
            {
                var c = cells[i];
                if (c == null) continue;
                c.VerticalAlignment = VerticalAlignment.Center;
                c.HorizontalAlignment = HorizontalAlignment.Left;
                c.Margin = new Thickness(0, 1, 20, 1);
                SetRow(c, row);
                SetColumn(c, 1 + i);
                if (i == cells.Length - 1 && cells.Length < columns) SetColumnSpan(c, columns - i + 1);
                Children.Add(c);
            }
        }
    }

    /// <summary>
    /// The automatic zones' options as rows of controls, shared by the Automatic zones window and
    /// the Apply window: the autozone movement options (climbs, gap jumps, the level cut, dead
    /// ends, the start square) when asked, and the player logic (what the solver may expect of
    /// the player), each group lined up in columns. The controls write straight into the options
    /// objects given.
    /// </summary>
    public sealed class LogicOptionsPanel : StackPanel
    {
        public NavOptions Nav { get; private set; }
        public AbilityOptions Logic { get; private set; }
        /// <summary>Raised when any option changes.</summary>
        public event Action Changed;

        public LogicOptionsPanel(NavOptions nav, AbilityOptions logic, bool movement)
        {
            Nav = nav;
            Logic = logic;
            Spacing = 2;
            if (movement)
            {
                Children.Add(Heading("AUTOZONE MOVEMENT", "how the levels' tiles become regions joined by drops, climbs and jumps, and how regions become zones"));
                var g = new OptionGrid(3);
                var climb = Num(nav.MaxClimb, 1, 15, v => nav.MaxClimb = v);
                ToolTip.SetTip(climb, "The highest climb in floor steps, both ways inside a region (the Zones window's setting).");
                var spellClimb = Num(nav.JumpSpellClimb, 1, 15, v => nav.JumpSpellClimb = v);
                var gap = Num(nav.GapTiles, 0, 6, v => nav.GapTiles = v);
                var spellGap = Num(nav.JumpSpellGapTiles, 0, 8, v => nav.JumpSpellGapTiles = v);
                g.AddRow("Highest climb", Row(climb, Note("floor steps")), Row(Label("With the jump spell", 130), spellClimb), null);
                g.AddRow("Gap jumps", Row(Check("On, up to (tiles)", nav.GapJumps, v => nav.GapJumps = v), gap), Row(Label("With the jump spell", 130), spellGap),
                    Check("Diagonals too", nav.DiagonalJumps, v => nav.DiagonalJumps = v));
                g.AddRow("Zones", Check("Cut at level changes", nav.CutAtLevelChanges, v => nav.CutAtLevelChanges = v),
                    Check("Fold empty dead ends (pits and ledges with nothing in them)", nav.FoldEmptyDeadEnds, v => nav.FoldEmptyDeadEnds = v));
                var start = Check("Consider the current starting square in the editor", !nav.ShippedStart, v => nav.ShippedStart = !v);
                ToolTip.SetTip(start, "When ticked, the player starts on the new-game square the executable now holds (moved with the editor's start tool). When unticked, the player starts on the game's own square, level 1 (32, 2).");
                g.AddRow("Start", start);
                Children.Add(g);
            }
            Children.Add(Heading("PLAYER LOGIC (THE SOLVER)", "what the solver may expect of the player"));
            var lg = new OptionGrid(2);
            var pickSkill = Num(logic.PickSkill, 0, 30, v => logic.PickSkill = v);
            var search = Num(logic.SearchSkill, 0, 30, v => logic.SearchSkill = v);
            lg.AddRow("Abilities", Check("Magic (casting from runes)", logic.Magic, v => logic.Magic = v),
                Check("Single-use items count (scrolls, potions, charged wands)", logic.SingleUseItems, v => logic.SingleUseItems = v));
            lg.AddRow("Locks, NPCs", Row(Check("Lock picking in logic, skill", logic.LockPicking, v => logic.LockPicking = v), pickSkill),
                Check("Killing NPCs in logic (to take what they carry)", logic.KillNpcs, v => logic.KillNpcs = v));
            // the damage-trap tiles of the level 7 maze (the facts' area) are always walls; the option for every other one is no longer offered
            lg.AddRow("Search", Row(Label("Search skill", 90), search), Check("Retry until found", logic.SearchRetry, v => logic.SearchRetry = v));
            lg.AddRow("Goal, ring", Check("Ring of Levitation needs lava protection", logic.RingNeedsLava, v => logic.RingNeedsLava = v),
                Check("Require breaking the orb (level 7)", logic.RequireOrb, v => logic.RequireOrb = v));
            Children.Add(lg);
        }

        // ---- small builders, as the other randomizer windows

        public static StackPanel Row(params Control[] c)
        {
            var p = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8 };
            foreach (var x in c) if (x != null) p.Children.Add(x);
            return p;
        }

        public static TextBlock Label(string t, double w = 110) { return new TextBlock { Text = t, Width = w, VerticalAlignment = VerticalAlignment.Center }; }
        public static TextBlock Note(string t) { return new TextBlock { Text = t, FontSize = 12, Opacity = 0.7, VerticalAlignment = VerticalAlignment.Center }; }

        public static Control Heading(string title, string what)
        {
            var p = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8, Margin = new Thickness(0, 6, 0, 2) };
            p.Children.Add(new TextBlock { Text = title, FontWeight = FontWeight.SemiBold, FontSize = 12, VerticalAlignment = VerticalAlignment.Center });
            p.Children.Add(new TextBlock { Text = what, FontSize = 12, Opacity = 0.7, VerticalAlignment = VerticalAlignment.Center });
            return p;
        }

        CheckBox Check(string t, bool on, Action<bool> set)
        {
            var c = new CheckBox { Content = t, IsChecked = on, VerticalAlignment = VerticalAlignment.Center };
            c.IsCheckedChanged += (o, e) => { set(c.IsChecked == true); Changed?.Invoke(); };
            return c;
        }

        NumericUpDown Num(int v, int min, int max, Action<int> set)
        {
            var n = new NumericUpDown { Minimum = min, Maximum = max, Increment = 1, FormatString = "0", Value = v, Width = 110 };
            n.ValueChanged += (o, e) => { set((int)(n.Value ?? v)); Changed?.Invoke(); };
            return n;
        }
    }
}
