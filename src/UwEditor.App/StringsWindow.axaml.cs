using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Media;
using UwEditor.Core.Editing;

namespace UwEditor.App
{
    public sealed class PakStringRow
    {
        public int Block { get; set; }
        public int Index { get; set; }
        public string Text { get; set; }
        public bool Changed { get; set; }
        public bool ShowBlock { get; set; }
        public string Where { get { return ShowBlock ? string.Format("{0}:{1}", Block, Index) : Index.ToString(); } }
        public IBrush WhereBrush { get { return Changed ? Brushes.Orange : Brushes.Gray; } }
    }

    public sealed class StringsViewModel : Observable
    {
        public StringsEditor Editor { get; private set; }
        public ObservableCollection<StringBlockInfo> Blocks { get; } = new ObservableCollection<StringBlockInfo>();
        public ObservableCollection<PakStringRow> Rows { get; } = new ObservableCollection<PakStringRow>();
        List<StringBlockInfo> all = new List<StringBlockInfo>();
        StringBlockInfo block;
        PakStringRow row;
        string blockFilter = "", search = "", editText = "", error = "", status = "";
        bool searchingAll;

        public StringsViewModel(StringsEditor e)
        {
            Editor = e;
            ReadBlocks();
            Status = string.Format("{0} blocks. Changes are held until you save; the first original is kept as STRINGS.PAK.ORIG.", all.Count);
        }

        void ReadBlocks()
        {
            int keep = block?.Number ?? -1;
            all = Editor.Blocks();
            FilterBlocks();
            var b = Blocks.FirstOrDefault(x => x.Number == keep);
            if (b != null) { block = b; Raise(nameof(Block)); }
        }

        void FilterBlocks()
        {
            string f = blockFilter.Trim().ToLowerInvariant();
            Blocks.Clear();
            foreach (var b in all)
                if (f.Length == 0 || b.Label.ToLowerInvariant().Contains(f)) Blocks.Add(b);
        }

        public string Status { get { return status; } set { Set(ref status, value); } }
        public string Error { get { return error; } set { Set(ref error, value ?? ""); } }
        public string BlockFilter { get { return blockFilter; } set { if (Set(ref blockFilter, value ?? "")) FilterBlocks(); } }
        public string Search { get { return search; } set { if (Set(ref search, value ?? "")) { searchingAll = false; FillRows(); } } }
        public string EditText { get { return editText; } set { Set(ref editText, value ?? ""); } }
        public bool HasChanges { get { return Editor.HasChanges; } }
        public bool HasBlock { get { return block != null; } }
        public bool HasString { get { return row != null; } }
        public bool BlockChanged { get { return block != null && Editor.Pak.IsChanged(block.Number); } }
        public string EditWhere { get { return row == null ? "Choose a string" : string.Format("Block {0} ({1}), string {2}", row.Block, StringsEditor.Describe(Editor.Session, row.Block), row.Index); } }

        public StringBlockInfo Block
        {
            get { return block; }
            set
            {
                if (!Set(ref block, value)) return;
                searchingAll = false;
                FillRows();
                Raise(nameof(HasBlock)); Raise(nameof(BlockChanged));
            }
        }

        public PakStringRow Row
        {
            get { return row; }
            set { if (Set(ref row, value)) { EditText = value?.Text ?? ""; Error = ""; Raise(nameof(HasString)); Raise(nameof(EditWhere)); } }
        }

        void FillRows()
        {
            Rows.Clear();
            if (searchingAll)
            {
                foreach (var h in Editor.Search(search).Take(2000)) Rows.Add(new PakStringRow { Block = h.Block, Index = h.Index, Text = h.Text, ShowBlock = true, Changed = Editor.IsChanged(h.Block, h.Index) });
                Status = Rows.Count + " strings in all blocks contain '" + search.Trim() + "'" + (Rows.Count >= 2000 ? " (first 2000)" : "") + ".";
                return;
            }
            if (block == null) return;
            var l = Editor.Get(block.Number);
            string f = search.Trim();
            for (int i = 0; i < l.Count; i++)
                if (f.Length == 0 || (l[i] ?? "").IndexOf(f, StringComparison.OrdinalIgnoreCase) >= 0)
                    Rows.Add(new PakStringRow { Block = block.Number, Index = i, Text = l[i], Changed = Editor.IsChanged(block.Number, i) });
        }

        public void SearchAll() { searchingAll = true; FillRows(); }

        /// <summary>Reads the strings again (after the accent table changed).</summary>
        public void Reload()
        {
            int b = row?.Block ?? -1, i = row?.Index ?? -1;
            ReadBlocks(); FillRows();
            if (b >= 0) Row = Rows.FirstOrDefault(r => r.Block == b && r.Index == i);
        }

        /// <summary>What stops the accent table's letters showing (FontChecks), run after saving; set by the main window.</summary>
        public Func<List<string>> Checks;
        /// <summary>The checks' findings after the last save (empty when none).</summary>
        public List<string> LastChecks { get; private set; } = new List<string>();

        /// <summary>Shows a block (and selects a string in it).</summary>
        public void Show(int number, int index = -1)
        {
            if (blockFilter.Length > 0) BlockFilter = "";
            Block = Blocks.FirstOrDefault(b => b.Number == number);
            if (index >= 0) Row = Rows.FirstOrDefault(r => r.Index == index);
        }

        void AfterChange(int b, int i, string what)
        {
            ReadBlocks();
            FillRows();
            Row = Rows.FirstOrDefault(r => r.Block == b && r.Index == i);
            Status = what;
            Raise(nameof(HasChanges)); Raise(nameof(BlockChanged));
        }

        public bool Apply()
        {
            if (row == null) return false;
            int b = row.Block, i = row.Index;
            try { Editor.Set(b, i, EditText); Error = ""; AfterChange(b, i, string.Format("String {0} of block {1} changed (not saved).", i, b)); return true; }
            catch (Exception ex) { Error = ex.Message; return false; }
        }

        public int Add()
        {
            if (block == null) return -1;
            int b = block.Number;
            try { int i = Editor.Add(b, EditText); Error = ""; searchingAll = false; search = ""; Raise(nameof(Search)); AfterChange(b, i, string.Format("String {0} added to block {1} (not saved).", i, b)); return i; }
            catch (Exception ex) { Error = ex.Message; return -1; }
        }

        public void Revert()
        {
            if (block == null) return;
            int b = block.Number;
            Editor.Revert(b);
            AfterChange(b, -1, "Block " + b + " reverted.");
        }

        public bool Save()
        {
            bool newTree = Editor.Pak.NeedsNewTree;
            Editor.Save();
            ReadBlocks(); FillRows();
            LastChecks = Checks?.Invoke() ?? new List<string>();
            Status = "STRINGS.PAK saved" + (newTree ? " with a new Huffman tree (a string has a character the old tree had no code for)" : "") + "; the editor's names and texts were read again."
                + (LastChecks.Count > 0 ? " " + LastChecks.Count + " accent problem" + (LastChecks.Count == 1 ? "" : "s") + ": see Tools ▸ Graphics ▸ Fonts." : "");
            Raise(nameof(HasChanges)); Raise(nameof(BlockChanged));
            return true;
        }
    }

    /// <summary>Tools ▸ Strings: every block of STRINGS.PAK, searched, edited and added to (characters the file's tree cannot write are refused).</summary>
    public partial class StringsWindow : Window
    {
        public StringsViewModel Model { get; private set; }

        public StringsWindow() { InitializeComponent(); }

        public StringsWindow(StringsEditor editor)
        {
            InitializeComponent();
            Model = new StringsViewModel(editor);
            DataContext = Model;
            ManualWindow.Attach(this, "Strings");
            Title = "Strings — " + editor.Session.Title;
            EditBox.AddHandler(KeyDownEvent, (s, e) =>
            {
                if (e.Key == Key.Enter && (e.KeyModifiers & KeyModifiers.Control) != 0) { Model.Apply(); e.Handled = true; }
            }, RoutingStrategies.Tunnel);
            if (Model.Blocks.Count > 0) Model.Block = Model.Blocks[0];
        }

        void OnApply(object sender, RoutedEventArgs e) { Model.Apply(); }
        void OnAdd(object sender, RoutedEventArgs e) { Model.Add(); }
        void OnRevert(object sender, RoutedEventArgs e) { Model.Revert(); }
        void OnSearchAll(object sender, RoutedEventArgs e) { Model.SearchAll(); }

        void OnSearchKey(object sender, KeyEventArgs e)
        {
            if (e.Key == Key.Enter && (e.KeyModifiers & KeyModifiers.Control) != 0) { Model.SearchAll(); e.Handled = true; }
        }

        void OnSave(object sender, RoutedEventArgs e)
        {
            try
            {
                Model.Save();
                if (Model.LastChecks.Count > 0)
                    _ = MessageWindow.Show(this, "Accented letters the game cannot show yet", "STRINGS.PAK was saved. The accent table's letters need these fixed (Tools ▸ Graphics ▸ Fonts):\n\n" + string.Join("\n", Model.LastChecks));
            }
            catch (Exception ex) { _ = MessageWindow.Show(this, "Could not save STRINGS.PAK", ex.Message); }
        }
    }
}
