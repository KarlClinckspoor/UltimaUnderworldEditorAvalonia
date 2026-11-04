using System;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using UwEditor.Core.Randomizer;

namespace UwEditor.App
{
    /// <summary>
    /// The automatic zones' options as rows of controls, shared by the Automatic zones window and
    /// the Apply window: the autozone movement options (climbs, gap jumps, the level cut, dead
    /// ends, the start square) when asked, and the player logic (what the solver may expect of
    /// the player). The controls write straight into the options objects given.
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
            Spacing = 6;
            if (movement)
            {
                Children.Add(Heading("AUTOZONE MOVEMENT", "how the levels' tiles become regions, joined by drops, climbs and jumps, and the regions zones"));
                var climb = Num(nav.MaxClimb, 1, 15, v => nav.MaxClimb = v);
                var spellClimb = Num(nav.JumpSpellClimb, 1, 15, v => nav.JumpSpellClimb = v);
                var gap = Num(nav.GapTiles, 0, 6, v => nav.GapTiles = v);
                var spellGap = Num(nav.JumpSpellGapTiles, 0, 8, v => nav.JumpSpellGapTiles = v);
                Children.Add(Row(Label("Highest climb"), climb, Note("floor steps, both ways inside a region (the Zones window's setting)"), Label("with the jump spell", 130), spellClimb));
                Children.Add(Row(Label("Gap jumps"), Check("on", nav.GapJumps, v => nav.GapJumps = v), Label("tiles", 40), gap, Label("with the jump spell", 130), spellGap,
                    Check("diagonals too", nav.DiagonalJumps, v => nav.DiagonalJumps = v)));
                Children.Add(Row(Label("Zones"), Check("Cut at level changes", nav.CutAtLevelChanges, v => nav.CutAtLevelChanges = v),
                    Check("Fold empty dead ends (pits and ledges with nothing in them)", nav.FoldEmptyDeadEnds, v => nav.FoldEmptyDeadEnds = v)));
                var start = Check("Consider the current starting square in the editor", !nav.ShippedStart, v => nav.ShippedStart = !v);
                ToolTip.SetTip(start, "On: the player starts on the new-game square the executable now holds (moved with the editor's start tool). Off: on the game's own square, level 1 (32, 2).");
                Children.Add(Row(Label("Start"), start));
            }
            Children.Add(Heading("PLAYER LOGIC (THE SOLVER)", "what the solver may expect of the player"));
            var pickSkill = Num(logic.PickSkill, 0, 30, v => logic.PickSkill = v);
            var search = Num(logic.SearchSkill, 0, 30, v => logic.SearchSkill = v);
            Children.Add(Row(Label("Abilities"), Check("Magic (casting from runes)", logic.Magic, v => logic.Magic = v),
                Check("Single-use items count (scrolls, potions, charged wands)", logic.SingleUseItems, v => logic.SingleUseItems = v)));
            Children.Add(Row(Label(""), Check("Lock picking in logic, skill", logic.LockPicking, v => logic.LockPicking = v), pickSkill,
                Check("Killing NPCs in logic (to take what they carry)", logic.KillNpcs, v => logic.KillNpcs = v)));
            // the damage-trap tiles of the level 7 maze (the facts' area) are always walls; the option for every other one is no longer offered
            Children.Add(Row(Label(""), Label("Search skill", 90), search, Check("Retry until found", logic.SearchRetry, v => logic.SearchRetry = v)));
            Children.Add(Row(Label(""), Check("Ring of Levitation needs lava protection", logic.RingNeedsLava, v => logic.RingNeedsLava = v),
                Check("Require breaking the orb (level 7)", logic.RequireOrb, v => logic.RequireOrb = v)));
        }

        // ---- small builders, as the other randomizer windows

        public static StackPanel Row(params Control[] c)
        {
            var p = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8 };
            foreach (var x in c) if (x != null) p.Children.Add(x);
            return p;
        }

        public static TextBlock Label(string t, double w = 110) { return new TextBlock { Text = t, Width = w, VerticalAlignment = VerticalAlignment.Center }; }

        static Control Heading(string title, string what)
        {
            var p = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8, Margin = new Thickness(0, 6, 0, 0) };
            p.Children.Add(new TextBlock { Text = title, FontWeight = FontWeight.SemiBold, FontSize = 12, VerticalAlignment = VerticalAlignment.Center });
            p.Children.Add(new TextBlock { Text = what, FontSize = 12, Opacity = 0.7, VerticalAlignment = VerticalAlignment.Center });
            return p;
        }
        public static TextBlock Note(string t) { return new TextBlock { Text = t, FontSize = 12, Opacity = 0.7, VerticalAlignment = VerticalAlignment.Center }; }

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
