using System;
using System.Collections.ObjectModel;
using System.Linq;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Media.Imaging;
using UwEditor.Core;
using UwEditor.Core.Editing;

namespace UwEditor.App
{
    /// <summary>A line of the chains tree: a trigger, a trap it reaches, or a heading.</summary>
    public sealed class ChainNode : Observable
    {
        public string Header { get; set; }
        public Bitmap Icon { get; set; }
        public int Level { get; set; }
        public int Index { get; set; } = -1;
        public ObservableCollection<ChainNode> Children { get; } = new ObservableCollection<ChainNode>();
        bool expanded;
        public bool IsExpanded { get { return expanded; } set { Set(ref expanded, value); } }
    }

    public sealed class TriggersViewModel : Observable
    {
        readonly MainViewModel main;
        public ObservableCollection<ChainNode> Nodes { get; } = new ObservableCollection<ChainNode>();
        ChainNode selected;
        string status = "", help = "", heading = "Triggers and traps";
        int variable = -1, value;
        LevelModel level;

        public TriggersViewModel(MainViewModel main) { this.main = main; }

        public string Status { get { return status; } set { Set(ref status, value); } }
        public string Help { get { return help; } set { Set(ref help, value ?? ""); } }
        public string Heading { get { return heading; } set { Set(ref heading, value); } }
        public bool HasSelection { get { return selected != null && selected.Index >= 0; } }
        public bool HasVariable { get { return variable >= 0; } }
        public int Variable { get { return variable; } }
        public int VariableValue { get { return value; } }
        public LevelModel LevelShown { get { return level; } }

        public ChainNode Selected
        {
            get { return selected; }
            set
            {
                if (!Set(ref selected, value)) return;
                variable = -1;
                if (value != null && value.Index >= 0 && level != null)
                {
                    var o = level.Objects[value.Index];
                    var targets = Triggers.Targets(level, o);
                    Help = (targets.Count > 0 ? "Acts on: " + string.Join("; ", targets.Select(t => t.ToString())) + "\n\n" : "") + (Triggers.Explain(main.Session, o) ?? "Not a trap or trigger: " + o.Name + ".");
                    variable = Triggers.VariableOf(main.Session, o);
                    this.value = o.Quality << 5 | o.Owner;
                }
                else Help = "";
                Raise(nameof(HasSelection)); Raise(nameof(HasVariable));
            }
        }

        string Where(ObjectInfo o)
        {
            if (o.Parent >= 0)
            {
                var p = level.Objects[o.Parent];
                if (!p.IsTrapOrTrigger) return string.Format("in #{0} {1} at ({2}, {3})", p.Index, p.Name, p.TileX, p.TileY);
            }
            return o.TileX >= 0 ? string.Format("at ({0}, {1})", o.TileX, o.TileY) : "";
        }

        public void Build(LevelModel m)
        {
            level = m;
            Nodes.Clear();
            Selected = null;
            if (m == null) return;
            Heading = "Triggers and traps of level " + m.Level;
            var chains = Triggers.Chains(m);
            var s = main.Session;
            foreach (var c in chains)
            {
                var t = c.Trigger;
                var n = new ChainNode { Header = string.Format("#{0} {1} {2}  →  runs on ({3}, {4})", t.Index, t.Name, Where(t), c.TargetX, c.TargetY), Icon = main.IconOf(t.Item), Level = m.Level, Index = t.Index };
                if (c.Steps.Count == 0) n.Children.Add(new ChainNode { Header = "links to nothing: it does nothing" });
                for (int i = 0; i < c.Steps.Count; i++)
                {
                    var o = c.Steps[i];
                    string note = Triggers.IsTrap(o.Item) ? "" : Triggers.IsTrigger(o.Item) ? "  (a trigger: runs its own chain on its own square)" : "  (not a trap)";
                    string what = Triggers.Short(s, o);
                    n.Children.Add(new ChainNode
                    {
                        Header = string.Format("{0} #{1} {2}{3}{4}{5}", i == 0 ? "fires" : "then", o.Index, o.Name, string.IsNullOrEmpty(what) ? "" : ": " + Flat(what), Where(o).Length > 0 ? "  " + Where(o) : "", note),
                        Icon = main.IconOf(o.Item), Level = m.Level, Index = o.Index,
                    });
                }
                Nodes.Add(n);
            }
            var lone = Triggers.Unreached(m, chains);
            if (lone.Count > 0)
            {
                var h = new ChainNode { Header = lone.Count + " traps no trigger reaches (fired by a door, a hack trap, a conversation, a schedule, or nothing)", IsExpanded = true };
                foreach (var o in lone)
                {
                    var from = Triggers.LinkedFrom(m, o);
                    h.Children.Add(new ChainNode
                    {
                        Header = string.Format("#{0} {1}: {2}  {3}{4}", o.Index, o.Name, Flat(Triggers.Short(s, o)), Where(o), from.Count > 0 ? "  · linked from " + string.Join(", ", from.Select(f => "#" + f.Index + " " + f.Name)) : ""),
                        Icon = main.IconOf(o.Item), Level = m.Level, Index = o.Index,
                    });
                }
                Nodes.Add(h);
            }
            Status = string.Format("Level {0}: {1} triggers, {2} traps in their chains, {3} other traps.", m.Level, chains.Count, chains.SelectMany(c => c.Steps).Count(o => Triggers.IsTrap(o.Item)), lone.Count);
        }

        static string Flat(string s)
        {
            s = (s ?? "").Replace('\n', ' ');
            return s.Length > 110 ? s.Substring(0, 110) + "…" : s;
        }
    }

    /// <summary>Tools ▸ Triggers and traps: every trigger of the level shown and the chain it sets off; the traps no trigger reaches.</summary>
    public partial class TriggersWindow : Window
    {
        public TriggersViewModel Model { get; private set; }
        readonly MainViewModel main;
        readonly Action<int, int> goTo;
        readonly Action<(int, int)> openVariable;

        public TriggersWindow() { InitializeComponent(); }

        public TriggersWindow(MainViewModel main, Action<int, int> goTo, Action<(int, int)> openVariable)
        {
            InitializeComponent();
            this.main = main; this.goTo = goTo; this.openVariable = openVariable;
            Model = new TriggersViewModel(main);
            DataContext = Model;
            ManualWindow.Attach(this, "Triggers and traps");
            Refresh();
            main.LevelChanged += OnMainLevel;
            Closed += (s, e) => main.LevelChanged -= OnMainLevel;
        }

        void OnMainLevel(LevelModel l) { Refresh(); }

        /// <summary>Shows the level the main window shows.</summary>
        public void Refresh() { main.FlushRefresh(); Model.Build(main.Level); }

        void OnRefresh(object sender, RoutedEventArgs e) { Refresh(); }
        void OnGoTo(object sender, RoutedEventArgs e) { Go(); }
        void OnDoubleTapped(object sender, TappedEventArgs e) { Go(); }

        void Go()
        {
            var n = Model.Selected;
            if (n != null && n.Index >= 0) goTo?.Invoke(n.Level, n.Index);
        }

        void OnVariable(object sender, RoutedEventArgs e)
        {
            if (Model.HasVariable) openVariable?.Invoke((Model.Variable, Model.VariableValue));
        }
    }
}
