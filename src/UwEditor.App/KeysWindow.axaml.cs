using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading.Tasks;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Threading;
using UwEditor.Core;
using UwEditor.Core.Editing;

namespace UwEditor.App
{
    /// <summary>A key or lock line: where it is and whether it is locked.</summary>
    public sealed class PlaceRow
    {
        public KeyPlace Place { get; set; }
        public override string ToString()
        {
            var p = Place;
            string s = p.ToString();
            if (p.Item == 0x10F) s += p.Locked ? " · locked" : " · unlocked";
            if (p.Relockable) s += " · relockable";
            return s;
        }
    }

    public sealed class KeysViewModel : Observable
    {
        public MainViewModel Main { get; private set; }
        public StringsEditor Strings { get; private set; }
        public ObservableCollection<KeyGroup> Groups { get; } = new ObservableCollection<KeyGroup>();
        public ObservableCollection<PlaceRow> Keys { get; } = new ObservableCollection<PlaceRow>();
        public ObservableCollection<PlaceRow> Locks { get; } = new ObservableCollection<PlaceRow>();
        string status = "", editText = "", textError = "";
        KeyGroup selected;
        public string Status { get { return status; } set { Set(ref status, value); } }
        public string EditText { get { return editText; } set { Set(ref editText, value ?? ""); } }
        public string TextError { get { return textError; } set { Set(ref textError, value ?? ""); } }
        public bool HasChanges { get { return Strings.HasChanges; } }
        public bool HasSelection { get { return selected != null; } }
        public string TextWhere { get { return selected == null ? "" : string.Format("Block 5, string {0} (0x{0:X3})", selected.TextIndex); } }

        public KeysViewModel(MainViewModel main, StringsEditor strings)
        {
            Main = main; Strings = strings;
            Strings.Changed += OnStringsChanged;
        }

        void OnStringsChanged() { Raise(nameof(HasChanges)); }

        /// <summary>Stops listening to the shared strings (the window closed).</summary>
        public void Detach() { Strings.Changed -= OnStringsChanged; }

        public KeyGroup Selected
        {
            get { return selected; }
            set
            {
                if (!Set(ref selected, value)) return;
                Keys.Clear(); Locks.Clear();
                if (value != null)
                {
                    foreach (var k in value.Keys) Keys.Add(new PlaceRow { Place = k });
                    foreach (var l in value.Locks) Locks.Add(new PlaceRow { Place = l });
                }
                EditText = value?.Text ?? "";
                TextError = "";
                Raise(nameof(HasSelection)); Raise(nameof(TextWhere));
            }
        }

        /// <summary>Reads every level (with their unsaved edits) for keys and locks.</summary>
        public async Task Build()
        {
            Status = "Reading the levels…";
            var s = Main.Session;
            var edits = Main.Edits;
            List<KeyGroup> groups = null;
            await Task.Run(() => groups = KeyIndex.Build(s, l => edits != null ? edits.Model(l) : s.LoadLevel(l)));
            int keep = selected?.Id ?? -1;
            Groups.Clear();
            foreach (var g in groups) { g.Text = Strings.Get(5, g.TextIndex); Groups.Add(g); }
            Selected = Groups.FirstOrDefault(g => g.Id == keep) ?? Groups.FirstOrDefault();
            Status = string.Format("{0} lock numbers: {1} keys, {2} locks.", groups.Count, groups.Sum(g => g.Keys.Count), groups.Sum(g => g.Locks.Count));
        }

        public bool Apply()
        {
            if (selected == null) return false;
            try
            {
                Strings.Set(5, selected.TextIndex, EditText.Trim());
                selected.Text = EditText.Trim();
                TextError = "";
                Status = string.Format("Description of lock {0}'s keys changed (not saved).", selected.Id);
                int i = Groups.IndexOf(selected);
                var g = selected;
                if (i >= 0) { Groups[i] = g; selected = null; Selected = g; }
                return true;
            }
            catch (Exception ex) { TextError = ex.Message; return false; }
        }
    }

    /// <summary>Tools ▸ Keys and locks: every lock number with its keys, its locks and the keys' description.</summary>
    public partial class KeysWindow : Window
    {
        public KeysViewModel Model { get; private set; }
        readonly Action<KeyPlace> goTo;
        public Task Loading { get; private set; }

        public KeysWindow() { InitializeComponent(); }

        public KeysWindow(MainViewModel main, StringsEditor strings, Action<KeyPlace> goTo)
        {
            InitializeComponent();
            this.goTo = goTo;
            Model = new KeysViewModel(main, strings);
            DataContext = Model;
            ManualWindow.Attach(this, "Keys and locks");
            Title = "Keys and locks — " + main.Session.Title;
            Loading = Model.Build();
            Closed += (o, e) => Model.Detach();
        }

        void OnRefresh(object sender, RoutedEventArgs e) { Loading = Model.Build(); }
        void OnApply(object sender, RoutedEventArgs e) { Model.Apply(); }

        void OnSave(object sender, RoutedEventArgs e)
        {
            try { Model.Strings.Save(); Model.Status = "STRINGS.PAK saved."; }
            catch (Exception ex) { _ = MessageWindow.Show(this, "Could not save STRINGS.PAK", ex.Message); }
        }

        void OnPlaceDoubleTapped(object sender, TappedEventArgs e)
        {
            var row = (sender as ListBox)?.SelectedItem as PlaceRow;
            if (row != null) goTo?.Invoke(row.Place);
        }
    }
}
