using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading.Tasks;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Media.Imaging;
using UwEditor.Core;

namespace UwEditor.App
{
    /// <summary>One result line: the object's picture, where it is, and what matched.</summary>
    public sealed class HitRow
    {
        public SearchHit Hit { get; set; }
        public Bitmap Icon { get; set; }
        public string Title { get; set; }
        public string Detail { get; set; }
        public bool HasDetail { get { return !string.IsNullOrEmpty(Detail); } }
    }

    public sealed class FindViewModel : Observable
    {
        string text = "", status = "Search every level of the open game.";
        int kindIndex;
        bool enchanted, busy;
        HitRow selected;

        public string Text { get { return text; } set { Set(ref text, value ?? ""); } }
        public List<string> Kinds { get; } = ItemCategories.Choices.Select(c => c.Value).ToList();
        public int KindIndex { get { return kindIndex; } set { Set(ref kindIndex, value); } }
        public bool EnchantedOnly { get { return enchanted; } set { Set(ref enchanted, value); } }
        public bool Busy { get { return busy; } set { Set(ref busy, value); } }
        public string Status { get { return status; } set { Set(ref status, value); } }
        public ObservableCollection<HitRow> Hits { get; } = new ObservableCollection<HitRow>();
        public HitRow Selected { get { return selected; } set { if (Set(ref selected, value)) Raise(nameof(HasSelection)); } }
        public bool HasSelection { get { return selected != null; } }

        public ObjectQuery Query()
        {
            return new ObjectQuery
            {
                Text = text,
                Categories = ItemCategories.Choices[Math.Clamp(kindIndex, 0, ItemCategories.Choices.Length - 1)].Key,
                EnchantedOnly = enchanted,
            };
        }
    }

    /// <summary>Tools ▸ Find in all levels: searches every level with the object list's rules
    /// and kinds; a result opens its level and selects the object.</summary>
    public partial class FindWindow : Window
    {
        public readonly FindViewModel Model = new FindViewModel();
        readonly MainViewModel main;
        readonly Action<SearchHit> goTo;
        public const int MaxHits = 2000;

        public FindWindow() : this(null, null) { }

        public FindWindow(MainViewModel main, Action<SearchHit> goTo)
        {
            this.main = main;
            this.goTo = goTo;
            InitializeComponent();
            ManualWindow.Attach(this, "Finding and selecting");
            DataContext = Model;
            Opened += (s, e) => QueryBox.Focus();
            KeyDown += (s, e) => { if (e.Key == Key.Escape) Close(); };
            Results.KeyDown += (s, e) => { if (e.Key == Key.Enter) { GoToSelected(); e.Handled = true; } };
        }

        void OnSearch(object sender, RoutedEventArgs e) { _ = SearchAsync(); }

        /// <summary>Runs the search off the window's thread and fills the results.</summary>
        public async Task SearchAsync()
        {
            var session = main?.Session;
            if (session == null) { Model.Status = "Open a game first."; return; }
            var q = Model.Query();
            Model.Busy = true;
            Model.Status = "Searching…";
            Model.Hits.Clear();
            List<SearchHit> hits;
            try { hits = await Task.Run(() => LevelSearch.Run(session, q, MaxHits)); }
            finally { Model.Busy = false; }
            foreach (var h in hits)
            {
                string detail = string.Join("  ·  ", new[] { h.Inside.Length > 0 ? "inside " + h.Inside : null, h.Matched.Length > 0 ? "“" + h.Matched + "”" : null }.Where(x => x != null));
                Model.Hits.Add(new HitRow
                {
                    Hit = h, Icon = main.IconOf(h.Item), Detail = detail,
                    Title = string.Format("{0}  ·  #{1}  {2}  ({3}, {4})", h.LevelName, h.Index, h.Name, h.TileX, h.TileY),
                });
            }
            int levels = hits.Select(h => h.Level).Distinct().Count();
            Model.Status = hits.Count == 0 ? "Nothing found." :
                string.Format("{0}{1} found on {2} level{3}. Double-click or Enter to go there.", hits.Count >= MaxHits ? "The first " : "", hits.Count, levels, levels == 1 ? "" : "s");
            if (Model.Hits.Count > 0) Model.Selected = Model.Hits[0];
        }

        void OnGoTo(object sender, RoutedEventArgs e) { GoToSelected(); }
        void OnResultDoubleTapped(object sender, TappedEventArgs e) { GoToSelected(); }

        public void GoToSelected()
        {
            if (Model.Selected != null) goTo?.Invoke(Model.Selected.Hit);
        }
    }
}
