using System;
using System.Collections.Generic;
using System.Linq;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using UwEditor.Core;
using UwEditor.Core.Balance;
using UwEditor.Core.Editing;

namespace UwEditor.App
{
    /// <summary>A labelled whole-number slider with its value beside it.</summary>
    public sealed class StatSlider : Grid
    {
        readonly Slider slider;
        readonly TextBlock shown = new TextBlock { VerticalAlignment = VerticalAlignment.Center, TextAlignment = TextAlignment.Right, FontFamily = new FontFamily("Cascadia Mono,Consolas,DejaVu Sans Mono,monospace") };
        bool quiet;
        public event Action<int> ValueChanged;
        public string Label { get; private set; }

        public StatSlider(string label, int min, int max, int value, string tip = null, double labelWidth = 96)
        {
            Label = label;
            ColumnDefinitions = new ColumnDefinitions(labelWidth + ",*,36");
            var l = new TextBlock { Text = label, VerticalAlignment = VerticalAlignment.Center, TextTrimming = TextTrimming.CharacterEllipsis };
            slider = new Slider { Minimum = min, Maximum = max, Value = value, IsSnapToTickEnabled = true, TickFrequency = 1, SmallChange = 1, LargeChange = 5, Margin = new Thickness(6, -8), MinWidth = 90, VerticalAlignment = VerticalAlignment.Center };
            Height = 30;
            shown.Text = value.ToString();
            slider.PropertyChanged += (o, e) =>
            {
                if (e.Property != Slider.ValueProperty) return;
                shown.Text = Value.ToString();
                if (!quiet) ValueChanged?.Invoke(Value);
            };
            if (tip != null) { ToolTip.SetTip(l, tip); ToolTip.SetTip(slider, tip); }
            Grid.SetColumn(slider, 1); Grid.SetColumn(shown, 2);
            Children.Add(l); Children.Add(slider); Children.Add(shown);
        }

        public int Value
        {
            get { return (int)Math.Round(slider.Value); }
            set { slider.Value = Math.Max(slider.Minimum, Math.Min(slider.Maximum, value)); }
        }

        /// <summary>Sets the value without raising ValueChanged.</summary>
        public void SetQuietly(int v) { quiet = true; try { Value = v; } finally { quiet = false; } }
    }

    /// <summary>The player's attributes and skills as sliders, shared by the balance windows.</summary>
    public sealed class PlayerStatsPanel : StackPanel
    {
        public readonly PlayerStats Stats;
        readonly List<Tuple<StatSlider, Func<PlayerStats, int>>> sliders = new List<Tuple<StatSlider, Func<PlayerStats, int>>>();
        readonly CheckBox easy;
        public event Action Changed;

        /// <param name="skills">Skill indexes (PlayerStats) to show; −1 Strength, −2 Dexterity.</param>
        public PlayerStatsPanel(GameSession s, PlayerStats stats, IEnumerable<int> skills, bool showEasy)
        {
            Stats = stats;
            Spacing = 2;
            Children.Add(new TextBlock { Text = "PLAYER", FontSize = 11, FontWeight = FontWeight.SemiBold, Opacity = 0.7, Margin = new Thickness(0, 0, 0, 2) });
            foreach (int k in skills)
            {
                string name = k == -1 ? "Strength" : k == -2 ? "Dexterity" : UltimateEditor.Games.PlayerDat.SkillNames[k];
                Func<PlayerStats, int> get = k == -1 ? (Func<PlayerStats, int>)(p => p.Strength) : k == -2 ? (p => p.Dexterity) : (p => p[k]);
                int idx = k;
                var sl = new StatSlider(name, 0, k < 0 ? 45 : 30, get(stats), Tip(k));
                sl.ValueChanged += v =>
                {
                    if (idx == -1) Stats.Strength = v; else if (idx == -2) Stats.Dexterity = v; else Stats[idx] = v;
                    Changed?.Invoke();
                };
                sliders.Add(Tuple.Create(sl, get));
                Children.Add(sl);
            }
            var row = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 10, Margin = new Thickness(0, 4, 0, 0) };
            if (showEasy)
            {
                easy = new CheckBox { Content = "Easy", IsChecked = stats.Easy };
                ToolTip.SetTip(easy, "Easy difficulty: +7 to the player's to-hit, and the damage the player takes is halved.");
                easy.IsCheckedChanged += (o, e) => { Stats.Easy = easy.IsChecked == true; Changed?.Invoke(); };
                row.Children.Add(easy);
            }
            var fromSave = new Button { Content = "From PLAYER.DAT", IsEnabled = s.Player != null, Padding = new Thickness(8, 2) };
            ToolTip.SetTip(fromSave, s.Player != null ? "The figures of the open saved game's player." : "Open a saved game to take its player's figures.");
            fromSave.Click += (o, e) => Load(PlayerStats.From(s.Player));
            var reset = new Button { Content = "Defaults", Padding = new Thickness(8, 2) };
            ToolTip.SetTip(reset, "Strength and Dexterity 20, every skill 10.");
            reset.Click += (o, e) => Load(new PlayerStats());
            row.Children.Add(fromSave); row.Children.Add(reset);
            Children.Add(row);
        }

        public StatSlider SliderOf(string label) { return sliders.Select(t => t.Item1).FirstOrDefault(x => x.Label == label); }

        void Load(PlayerStats p)
        {
            Stats.Strength = p.Strength; Stats.Dexterity = p.Dexterity;
            for (int i = 0; i < Stats.Skills.Length; i++) Stats.Skills[i] = p.Skills[i];
            foreach (var t in sliders) t.Item1.SetQuietly(t.Item2(Stats));
            Changed?.Invoke();
        }

        static string Tip(int k)
        {
            switch (k)
            {
                case -1: return "Melee damage + Strength / 9 (bare hands: / 6). Missiles ignore it.";
                case -2: return "To hit + Dexterity / 7 (melee).";
                case PlayerStats.Attack: return "To hit + Attack / 2 (melee).";
                case PlayerStats.Defense: return "The player's Defence is Defense + the wielded weapon's skill / 2: it is taken from a creature's to-hit.";
                case PlayerStats.Unarmed: return "Bare hands: to hit + Unarmed, damage Unarmed × 2 / 5 + Strength / 6 + 4.";
                case PlayerStats.Sword: case PlayerStats.Axe: case PlayerStats.Mace: return "To hit + the skill, for weapons of this skill (OBJECTS.DAT weapon Skill).";
                case PlayerStats.Missile: return "Missile damage × (Missile × 8 + 192) / 256, less 128/256 on a bad roll (13 − Missile in 31) and plus 192/256 on a good one (Missile − 8 in 31).";
                case PlayerStats.Charm: return "Charm: the trader's greed − 2 a point, its patience + half a point (conv/BARTER.C barter_init).";
                case PlayerStats.Appraise: return "Appraise: only the player's own remark about the deal (do_judgement), off by up to 50 − Appraise × 1.5 per cent. The trader ignores it.";
            }
            return null;
        }
    }

    /// <summary>Helpers the balance windows share.</summary>
    public static class BalanceUi
    {
        /// <summary>The levels as edited, read once (the windows read them again on Refresh).</summary>
        public static Func<int, LevelModel> Levels(MainWindow main)
        {
            var vm = main.ViewModel;
            var s = vm.Session;
            var cache = new Dictionary<int, LevelModel>();
            return l =>
            {
                LevelModel m;
                if (!cache.TryGetValue(l, out m)) cache[l] = m = vm.Edits != null ? vm.Edits.Model(l) : s.LoadLevel(l);
                return m;
            };
        }

        /// <summary>The object tables as the object tables window has them (unsaved edits included), else as saved.</summary>
        public static ObjectTables Tables(MainWindow main, ref ObjectTables own)
        {
            var open = main.ObjectTablesOpen?.Tables;
            if (open != null) return open;
            if (own == null) own = ObjectTables.Load(main.ViewModel.Session);
            return own;
        }

        public static TextBlock Heading(string t) { return new TextBlock { Text = t, FontSize = 11, FontWeight = FontWeight.SemiBold, Opacity = 0.7, Margin = new Thickness(0, 10, 0, 2) }; }
        public static TextBlock Note(string t) { return new TextBlock { Text = t, FontSize = 12, Opacity = 0.75, TextWrapping = TextWrapping.Wrap }; }
        public static string F(double v) { return v.ToString("0.0", System.Globalization.CultureInfo.InvariantCulture); }
        public static string F2(double v) { return v.ToString("0.00", System.Globalization.CultureInfo.InvariantCulture); }
    }
}
