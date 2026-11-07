using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Platform.Storage;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Threading;
using UwEditor.Core;
using UwEditor.Core.Conversations;

namespace UwEditor.App
{
    /// <summary>
    /// Tools ▸ Conversations: every conversation of the open game, read from CNV.ARK and
    /// STRINGS.PAK. Dialogue shows what a script does; Strings edits its text; Script edits its
    /// code as assembly; Who talks lists the creatures that use it. Saving writes both files.
    /// </summary>
    public partial class ConversationWindow : Window
    {
        public readonly ConversationViewModel Model = new ConversationViewModel();
        /// <summary>Written to disk: names in the main window may have changed (STRINGS.PAK).</summary>
        public event Action Saved;
        /// <summary>Another conversation is shown, or the shown one's code changed (applied, reverted, saved).</summary>
        public event Action CurrentChanged;
        /// <summary>A string of the shown conversation was edited.</summary>
        public event Action StringsChanged;
        /// <summary>A function, label or variable of the shown conversation was renamed (F2).</summary>
        public event Action NamesChanged;
        ConversationSimulatorWindow simulatorWindow;
        ConversationGraphWindow graphWindow;
        readonly ConversationLibrary lib;
        readonly Action<ConversationUser> goTo;
        readonly Action<int> showGlobals;
        Dictionary<int, List<ConversationUser>> users;
        Conversation current;
        List<string> originalStrings = new List<string>();
        bool dialogueStale;
        /// <summary>The names the shown conversation is shown with.</summary>
        ConversationNames names = ConversationNames.None;

        public ConversationLibrary Library { get { return lib; } }
        public Conversation Current { get { return current; } }

        public ConversationWindow() : this(null, null, null) { }

        public ConversationWindow(ConversationLibrary library, Action<ConversationUser> goTo, Action<int> showGlobals)
        {
            lib = library;
            this.goTo = goTo;
            this.showGlobals = showGlobals;
            InitializeComponent();
            ManualWindow.Attach(this, "Conversations");
            DataContext = Model;
            Model.SelectedChanged += () => Load(Model.Selected == null ? -1 : Model.Selected.Slot);
            Model.TabChanged += () => { if (Model.Tab == 0 && dialogueStale) RefreshDialogue(); };
            AddHandler(KeyDownEvent, OnKey, RoutingStrategies.Tunnel);
            Closed += (o, e) => { simulatorWindow?.Close(); graphWindow?.Close(); };
            if (lib != null)
            {
                // the user's renames live in the settings folder, beside settings.json
                if (lib.Names.UserPath == null) lib.Names = NameBook.Load(lib.Kind, NameBook.UserFile(Settings.Folder, lib.Kind));
                Title = "Conversations — " + lib.Session.Title;
                FillList();
                Model.Status = string.Format("{0} conversations in {1}. Finding who talks through each…", Model.All.Count, lib.Archive.Path)
                    + (lib.Names.LoadError != null ? " The names could not be read: " + lib.Names.LoadError : "");
                _ = FindUsersAsync();
            }
        }

        void OnKey(object sender, KeyEventArgs e)
        {
            var ctrl = e.KeyModifiers.HasFlag(KeyModifiers.Control) || e.KeyModifiers.HasFlag(KeyModifiers.Meta);
            if (ctrl && e.Key == Key.S) { Save(); e.Handled = true; }
            else if (ctrl && e.Key == Key.G) { OpenGlobals(); e.Handled = true; }
            else if (ctrl && e.Key == Key.R && current != null) { ShowSimulator(); e.Handled = true; }
            else if (ctrl && e.Key == Key.Enter && Model.Tab == 2) { ApplyScript(); e.Handled = true; }
            else if (!ctrl && e.Key == Key.F2 && current != null) { _ = BeginRename(); e.Handled = true; }
            else if (ctrl && e.KeyModifiers.HasFlag(KeyModifiers.Shift) && e.Key == Key.K && current != null) { _ = CompileFromSource(); e.Handled = true; }
        }

        void FillList()
        {
            var rows = new List<ConversationRow>();
            foreach (int slot in lib.Archive.Slots())
            {
                Conversation c;
                try { c = lib.Archive.Read(slot); } catch (Exception) { continue; }
                var strs = lib.StringsOf(c);
                string name = lib.Name(slot);
                rows.Add(new ConversationRow
                {
                    Slot = slot,
                    Name = name,
                    Title = string.Format("{0,3}  {1}", slot, name ?? "(no name)"),
                    Detail = string.Format("{0} strings · {1} words of code", strs.Count, c.Code.Length),
                    Search = slot + " " + (name ?? "") + " " + string.Join(" ", strs),
                    Changed = lib.Archive.IsChanged(slot) || lib.Strings.IsChanged(c.StringBlock),
                });
            }
            Model.Fill(rows);
        }

        async Task FindUsersAsync()
        {
            Dictionary<int, List<ConversationUser>> u;
            try { u = await Task.Run(() => lib.FindUsers()); }
            catch (Exception ex) { Model.Status = "Could not read the levels: " + ex.Message; return; }
            users = u;
            foreach (var r in Model.All)
            {
                List<ConversationUser> list;
                int n = users.TryGetValue(r.Slot, out list) ? list.Count : 0;
                r.Detail += n == 0 ? " · nobody in the levels" : string.Format(" · {0} creature{1}", n, n == 1 ? "" : "s");
            }
            if (Model.Selected != null) FillUsers(Model.Selected.Slot);
            Model.Status = string.Format("{0} conversations. You can edit their strings and scripts; Save writes CNV.ARK and STRINGS.PAK.", Model.All.Count);
        }

        /// <summary>Shows a slot (and selects it in the list).</summary>
        public bool SelectSlot(int slot)
        {
            var row = Model.All.FirstOrDefault(r => r.Slot == slot);
            if (row == null) return false;
            if (!Model.Rows.Contains(row)) Model.Filter = "";
            Model.Selected = row;
            List.ScrollIntoView(row);
            return true;
        }

        void Load(int slot)
        {
            Model.Lines.Clear();
            Model.Strings.Clear();
            Model.Users.Clear();
            current = null;
            names = ConversationNames.None;
            if (slot < 0 || lib == null) { Model.Header = ""; Model.Subheader = ""; Model.SetScriptQuietly(""); return; }
            try { current = lib.Archive.Read(slot); }
            catch (Exception ex) { Model.Header = "Conversation " + slot; Model.Subheader = "Cannot be read: " + ex.Message; return; }
            string name = lib.Name(slot);
            Model.Header = string.Format("{0}  ·  conversation {1}", name ?? "No name", slot);
            int imported = current.Imports.Count(i => !i.IsFunction && ImportedVariables.IsBound(i.Name, lib.Kind));
            Model.Subheader = string.Format("String block 0x{0:X4} · {1} words of code · {2} variables ({3} filled in by the game, the rest kept in BGLOBALS.DAT) · {4} built-in functions imported",
                current.StringBlock, current.Code.Length, current.Variables, imported, current.Imports.Count(i => i.IsFunction));
            originalStrings = lib.Strings.Original(current.StringBlock);
            RefreshDialogue();
            FillStrings();
            FillUsers(slot);
            CurrentChanged?.Invoke();
        }

        void RefreshDialogue()
        {
            dialogueStale = false;
            Model.Lines.Clear();
            if (current == null) return;
            var d = lib.Decompile(current);
            names = d.Names;
            bool hide = HideUnusedBox == null || HideUnusedBox.IsChecked == true, skipping = false;
            for (int i = 0; i < d.Lines.Count; i++)
            {
                var l = d.Lines[i];
                if (l.Kind == PseudoKind.Function) skipping = hide && l.Text.Contains("never called");
                // the blank line before a function belongs to it
                bool blankBefore = l.Kind == PseudoKind.Comment && l.Text.Length == 0 && i + 1 < d.Lines.Count && d.Lines[i + 1].Kind == PseudoKind.Function;
                if (blankBefore) { if (hide && d.Lines[i + 1].Text.Contains("never called")) continue; skipping = false; }
                if (skipping) continue;
                Model.Lines.Add(new DialogueLine { Line = l });
            }
            if (!Model.ScriptChanged)
                Model.SetScriptQuietly(Disassembler.Disassemble(current, a => { string n; return d.Notes.TryGetValue(a, out n) ? n : null; },
                    "Conversation " + current.Slot + (lib.Name(current.Slot) is string nm ? " — " + nm : ""), d.Names));
            usesById = new Dictionary<int, List<StringUse>>();
            foreach (var u in d.Strings)
            {
                List<StringUse> list;
                if (!usesById.TryGetValue(u.Id, out list)) usesById[u.Id] = list = new List<StringUse>();
                list.Add(u);
            }
            foreach (var r in Model.Strings) r.Uses = UsesText(r.Index);
        }

        Dictionary<int, List<StringUse>> usesById = new Dictionary<int, List<StringUse>>();

        string UsesText(int id)
        {
            List<StringUse> list;
            if (!usesById.TryGetValue(id, out list) || list.Count == 0) return "";
            return string.Join(", ", list.Select(u => (u.Kind == StringUseKind.Say ? "said" : u.Kind == StringUseKind.Respond ? "responded" : u.Kind == StringUseKind.Menu ? "menu choice" : "argument") + " at " + u.UseAddress.ToString("X4")).Distinct());
        }

        void FillStrings()
        {
            Model.Strings.Clear();
            var strs = lib.StringsOf(current);
            for (int i = 0; i < strs.Count; i++) AddRow(i, strs[i]);
        }

        StringRow AddRow(int i, string text)
        {
            var row = new StringRow { Index = i };
            row.SetQuietly(text);
            row.Changed = i >= originalStrings.Count || originalStrings[i] != text;
            row.Uses = UsesText(i);
            row.Edited += OnStringEdited;
            Model.Strings.Add(row);
            return row;
        }

        void OnStringEdited(StringRow row)
        {
            if (current == null) return;
            var list = Model.Strings.Select(r => (r.Text ?? "").Replace("\r\n", "\n").Replace("\r", "\n")).ToList();
            try
            {
                lib.Strings.Set(current.StringBlock, list);
                foreach (var r in Model.Strings) { r.Error = ""; r.Changed = r.Index >= originalStrings.Count || originalStrings[r.Index] != list[r.Index]; }
                Model.Status = "String " + row.Index + " changed (not saved).";
            }
            catch (Exception ex)
            {
                row.Error = ex.Message;
                Model.Status = ex.Message;
            }
            MarkChanged();
            dialogueStale = true;
            StringsChanged?.Invoke();
        }

        void MarkChanged()
        {
            if (current == null || Model.Selected == null) return;
            Model.Selected.Changed = lib.Archive.IsChanged(current.Slot) || lib.Strings.IsChanged(current.StringBlock);
        }

        void OnHideUnusedChanged(object sender, RoutedEventArgs e) { if (current != null) RefreshDialogue(); }

        void OnAddString(object sender, RoutedEventArgs e) { AddString(); }

        public StringRow AddString(string text = "")
        {
            if (current == null) return null;
            var row = AddRow(Model.Strings.Count, text);
            OnStringEdited(row);
            StringList.ScrollIntoView(row);
            Model.Status = string.Format("String {0} added: the script can say it with PUSHI {0} then SAY.", row.Index);
            return row;
        }

        void FillUsers(int slot)
        {
            Model.Users.Clear();
            List<ConversationUser> list;
            if (users == null) { Model.Users.Add(new UserRow { Text = "Reading the levels…" }); return; }
            if (!users.TryGetValue(slot, out list) || list.Count == 0) { Model.Users.Add(new UserRow { Text = "No creature in the levels talks through this slot." }); return; }
            foreach (var u in list) Model.Users.Add(new UserRow { User = u, Text = u.ToString() });
        }

        void OnUserDoubleTapped(object sender, TappedEventArgs e)
        {
            var row = UserList.SelectedItem as UserRow;
            if (row != null && row.User != null) goTo?.Invoke(row.User);
        }

        void OnDialogueDoubleTapped(object sender, TappedEventArgs e)
        {
            var line = DialogueList.SelectedItem as DialogueLine;
            if (line != null) ShowAddress(line.Line.Address);
        }

        /// <summary>Opens the Script tab with the caret on the instruction at a code address (focus: move the keyboard focus there too).</summary>
        public void ShowAddress(int address, bool focus = true)
        {
            Model.Tab = 2;
            string key = "; " + address.ToString("X4");
            string s = Model.Script;
            int at = -1;
            for (int p = s.IndexOf(key, StringComparison.Ordinal); p >= 0; p = s.IndexOf(key, p + 1, StringComparison.Ordinal))
            {
                int after = p + key.Length;
                if (after >= s.Length || s[after] == ' ' || s[after] == '\n') { at = p; break; }
            }
            if (at < 0) return;
            int lineStart = s.LastIndexOf('\n', at) + 1;
            Dispatcher.UIThread.Post(() =>
            {
                if (focus) ScriptBox.Focus();
                ScriptBox.CaretIndex = lineStart;
                ScriptBox.SelectionStart = lineStart;
                ScriptBox.SelectionEnd = s.IndexOf('\n', at) < 0 ? s.Length : s.IndexOf('\n', at);
            }, DispatcherPriority.Background);
        }

        void OnApplyScript(object sender, RoutedEventArgs e) { ApplyScript(); }

        /// <summary>Assembles the Script text into the conversation. Returns false with the error shown.</summary>
        public bool ApplyScript()
        {
            if (current == null) return false;
            if (!Model.ScriptChanged) return true;
            try
            {
                var c = Assembler.Assemble(Model.Script);
                if (c.Slot != current.Slot) throw new AssemblyError(1, ".slot says " + c.Slot + ", but this is conversation " + current.Slot + ".");
                int oldBlock = current.StringBlock;
                lib.Archive.Write(current.Slot, c);
                current = lib.Archive.Read(current.Slot);
                Model.ScriptChanged = false;
                Model.ScriptError = "";
                if (c.StringBlock != oldBlock) { originalStrings = lib.Strings.Original(c.StringBlock); FillStrings(); }
                RefreshDialogue();
                MarkChanged();
                Model.Status = string.Format("Conversation {0} assembled: {1} words of code (not saved).", current.Slot, current.Code.Length);
                CurrentChanged?.Invoke();
                return true;
            }
            catch (AssemblyError ex)
            {
                Model.ScriptError = ex.Message;
                GoToScriptLine(ex.Line);
            }
            catch (Exception ex) { Model.ScriptError = ex.Message; }
            Model.Status = "The script was not applied: " + Model.ScriptError;
            return false;
        }

        void GoToScriptLine(int line)
        {
            string s = Model.Script;
            int p = 0;
            for (int i = 1; i < line && p >= 0; i++) { p = s.IndexOf('\n', p); if (p >= 0) p++; }
            if (p < 0) return;
            int end = s.IndexOf('\n', p);
            Dispatcher.UIThread.Post(() => { ScriptBox.Focus(); ScriptBox.SelectionStart = p; ScriptBox.SelectionEnd = end < 0 ? s.Length : end; }, DispatcherPriority.Background);
        }

        void OnResetScript(object sender, RoutedEventArgs e)
        {
            Model.ScriptChanged = false;
            RefreshDialogue();
        }

        void OnRevert(object sender, RoutedEventArgs e) { RevertCurrent(); }

        public void RevertCurrent()
        {
            if (current == null) return;
            int slot = current.Slot;
            lib.Strings.Revert(current.StringBlock);
            lib.Archive.Revert(slot);
            var c = lib.Archive.Read(slot);
            if (c != null) lib.Strings.Revert(c.StringBlock);
            Model.ScriptChanged = false;
            Load(slot);
            MarkChanged();
            Model.Status = "Conversation " + slot + " is back as it was last saved.";
            StringsChanged?.Invoke();
        }

        void OnSave(object sender, RoutedEventArgs e) { Save(); }

        /// <summary>Applies an edited script, then writes what changed. Returns what was written, or null.</summary>
        public List<string> Save()
        {
            if (lib == null) return null;
            if (Model.ScriptChanged && !ApplyScript()) return null;
            if (!lib.HasChanges) { Model.Status = "Nothing to save."; return new List<string>(); }
            try
            {
                var done = lib.Save();
                foreach (var r in Model.All) r.Changed = false;
                if (current != null) { originalStrings = lib.Strings.Original(current.StringBlock); foreach (var r in Model.Strings) r.Changed = false; }
                Model.Status = "Saved " + string.Join(" and ", done) + " in " + lib.Session.DataFolder + " (the old files are kept as .BKP).";
                Saved?.Invoke();
                return done;
            }
            catch (Exception ex)
            {
                Model.Status = "Not saved: " + ex.Message;
                return null;
            }
        }

        void OnGlobals(object sender, RoutedEventArgs e) { OpenGlobals(); }

        /// <summary>The creatures in the levels that talk through a slot (empty until the levels have been read).</summary>
        public List<ConversationUser> UsersOf(int slot)
        {
            List<ConversationUser> list;
            return users != null && users.TryGetValue(slot, out list) ? list : new List<ConversationUser>();
        }

        /// <summary>The shown conversation's strings, with unsaved edits.</summary>
        public List<string> CurrentStrings { get { return current == null ? new List<string>() : lib.StringsOf(current); } }

        /// <summary>The Strings tab's row of string n of the shown conversation, or null: editing its Text edits the string.</summary>
        public StringRow StringRowOf(int index) { return Model.Strings.FirstOrDefault(r => r.Index == index); }

        /// <summary>The script text has edits not yet applied.</summary>
        public bool ScriptPending { get { return Model.ScriptChanged; } }

        void OnSimulate(object sender, RoutedEventArgs e) { ShowSimulator(); }
        void OnGraph(object sender, RoutedEventArgs e) { ShowGraph(); }

        /// <summary>The simulator for the shown conversation, one at a time.</summary>
        public ConversationSimulatorWindow ShowSimulator()
        {
            if (lib == null) return null;
            if (simulatorWindow != null) { simulatorWindow.Activate(); return simulatorWindow; }
            simulatorWindow = new ConversationSimulatorWindow(this);
            simulatorWindow.Closed += (o, e) => simulatorWindow = null;
            simulatorWindow.Show(this);
            return simulatorWindow;
        }

        /// <summary>The graph of the shown conversation, one at a time.</summary>
        public ConversationGraphWindow ShowGraph()
        {
            if (lib == null) return null;
            if (graphWindow != null) { graphWindow.Activate(); return graphWindow; }
            graphWindow = new ConversationGraphWindow(this);
            graphWindow.Closed += (o, e) => graphWindow = null;
            graphWindow.Show(this);
            return graphWindow;
        }

        void OpenGlobals() { showGlobals?.Invoke(current == null ? -1 : current.Slot); }

        // ----- names (F2) -----

        /// <summary>The names the shown conversation is shown with.</summary>
        public ConversationNames Names { get { return names; } }

        static readonly Regex quoted = new Regex("\"(?:\\\\.|[^\"\\\\])*\"");
        static readonly Regex word = new Regex("[A-Za-z_][A-Za-z0-9_]*");

        /// <summary>
        /// What F2 can rename now: on the Dialogue tab, the functions, labels and variables on
        /// the selected line (the line's own function or label first); on the Script tab, the
        /// function or label at the caret or in the selection.
        /// </summary>
        public List<NameTarget> NameCandidates()
        {
            var list = new List<NameTarget>();
            if (current == null || names.Layout == null) return list;
            if (Model.Tab == 2)
            {
                string s = ScriptBox.Text ?? "";
                int a = Math.Min(ScriptBox.SelectionStart, ScriptBox.SelectionEnd), b = Math.Max(ScriptBox.SelectionStart, ScriptBox.SelectionEnd);
                if (a == b) a = b = Math.Max(0, Math.Min(ScriptBox.CaretIndex, s.Length));
                while (a > 0 && IsWordChar(s[a - 1])) a--;
                while (b < s.Length && IsWordChar(s[b])) b++;
                int lineStart = a == 0 ? 0 : s.LastIndexOf('\n', a - 1) + 1;
                int nl = s.IndexOf('\n', lineStart), lineEnd = nl < 0 ? s.Length : nl;
                int semi = s.IndexOf(';', lineStart, lineEnd - lineStart);
                // a word in a comment (an address, a string's text) names nothing
                if (b > a && (semi < 0 || a < semi))
                {
                    var t = names.Find(current, s.Substring(a, b - a), 0);
                    if (t.HasValue && (t.Value.Kind == NameKind.Function || t.Value.Kind == NameKind.Label)) list.Add(t.Value);
                }
                return list;
            }
            var line = DialogueList.SelectedItem as DialogueLine;
            if (line == null) return list;
            var l = line.Line;
            int f = names.Layout.FunctionOf(l.Address);
            if (l.Kind == PseudoKind.Function) list.Add(NameTarget.Function(l.Address));
            else if (l.Kind == PseudoKind.Label) list.Add(NameTarget.Label(l.Address));
            foreach (Match m in word.Matches(quoted.Replace(l.Text, " ")))
            {
                var t = names.Find(current, m.Value, f);
                if (t.HasValue && !list.Contains(t.Value) && !(t.Value.Kind == NameKind.Function && t.Value.Address == 0)) list.Add(t.Value);
            }
            return list;
        }

        static bool IsWordChar(char c) { return c == '_' || c < 128 && char.IsLetterOrDigit(c); }

        /// <summary>A short description of what a name belongs to, for the rename dialog.</summary>
        public string Describe(NameTarget t)
        {
            string n = names.NameOf(t);
            switch (t.Kind)
            {
                case NameKind.Function: return "function " + n + " (at " + t.Address.ToString("X4") + ")";
                case NameKind.Label: return "label " + n + " (at " + t.Address.ToString("X4") + ", in " + names.Function(names.Layout.FunctionOf(t.Address)) + ")";
                case NameKind.Global: return "global " + n + " (memory " + t.Address + ")";
                default: return (t.Offset < 0 ? "argument " : "local ") + n + " (in " + names.Function(t.Address) + ")";
            }
        }

        /// <summary>Why t cannot be called name, or null.</summary>
        public string CheckName(NameTarget t, string name)
        {
            if (current == null) return "No conversation is shown.";
            if (Model.ScriptChanged) return "The script has edits that are not applied: apply or undo them first.";
            return names.Check(current, t, (name ?? "").Trim());
        }

        /// <summary>
        /// Renames t in the shown conversation and in every view (Dialogue, Script, the graph and
        /// the simulator); the rename is kept in the user's names file. An empty name goes back
        /// to the shipped name. Returns why it could not be done, or null.
        /// </summary>
        public string Rename(NameTarget t, string name)
        {
            name = (name ?? "").Trim();
            string why = CheckName(t, name);
            if (why != null) { Model.Status = "Not renamed: " + why; return why; }
            string old = names.NameOf(t);
            try { lib.Names.Rename(current, t, name); }
            catch (Exception ex) { Model.Status = "Not renamed: " + ex.Message; return ex.Message; }
            var keep = DialogueList.SelectedItem as DialogueLine;
            RefreshDialogue();
            if (keep != null) DialogueList.SelectedItem = Model.Lines.FirstOrDefault(x => x.Line.Address == keep.Line.Address && x.Line.Kind == keep.Line.Kind);
            Model.Status = string.Format("{0} is now {1} (kept in {2}).", old, names.NameOf(t), lib.Names.UserPath ?? "this session");
            NamesChanged?.Invoke();
            return null;
        }

        /// <summary>F2: asks for a new name for what is under the caret or on the selected line.</summary>
        public async Task BeginRename()
        {
            var list = NameCandidates();
            if (list.Count == 0)
            {
                Model.Status = Model.Tab == 2 ? "Put the caret on a function or label name to rename it (F2)." : Model.Tab == 0 ? "Select a line with a function, label or variable to rename it (F2)." : "F2 renames on the Dialogue and Script tabs.";
                return;
            }
            if (Model.ScriptChanged) { Model.Status = "Not renamed: the script has edits that are not applied: apply or undo them first."; return; }
            await RenameDialog(list);
        }

        /// <summary>The rename dialog: what to rename (when the line has several names), the new name, checked as it is typed.</summary>
        Task RenameDialog(List<NameTarget> list)
        {
            var pick = new ComboBox { ItemsSource = list.Select(Describe).ToList(), SelectedIndex = 0, HorizontalAlignment = HorizontalAlignment.Stretch, IsVisible = list.Count > 1, Margin = new Thickness(0, 0, 0, 8) };
            var what = new TextBlock { Text = Describe(list[0]), IsVisible = list.Count == 1, Margin = new Thickness(0, 0, 0, 8), Opacity = 0.8 };
            var box = new TextBox { Text = names.NameOf(list[0]), FontFamily = Application.Current?.FindResource("Mono") as FontFamily ?? FontFamily.Default };
            var error = new TextBlock { TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 6, 0, 0), Foreground = Application.Current?.FindResource("Warn") as IBrush ?? Brushes.OrangeRed };
            var hint = new TextBlock { Text = "Letters, digits and _. An empty name goes back to the shipped one.", Opacity = 0.7, FontSize = 11, Margin = new Thickness(0, 4, 0, 0), TextWrapping = TextWrapping.Wrap };
            var ok = new Button { Content = "Rename", IsDefault = true, MinWidth = 80, HorizontalContentAlignment = HorizontalAlignment.Center };
            var cancel = new Button { Content = "Cancel", IsCancel = true, MinWidth = 80, HorizontalContentAlignment = HorizontalAlignment.Center };
            var w = new Window
            {
                Title = "Rename", Width = 460, SizeToContent = SizeToContent.Height, CanResize = false, WindowStartupLocation = WindowStartupLocation.CenterOwner,
                Content = new DockPanel
                {
                    Margin = new Thickness(20),
                    Children =
                    {
                        new TextBlock { Text = "Rename", FontSize = 16, FontWeight = FontWeight.SemiBold, Margin = new Thickness(0, 0, 0, 10), [DockPanel.DockProperty] = Dock.Top },
                        new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right, Spacing = 8, Margin = new Thickness(0, 16, 0, 0), Children = { ok, cancel }, [DockPanel.DockProperty] = Dock.Bottom },
                        new StackPanel { Children = { pick, what, box, hint, error } },
                    },
                },
            };
            Action check = () =>
            {
                var t = list[Math.Max(0, pick.SelectedIndex)];
                string why = CheckName(t, box.Text);
                error.Text = why ?? "";
                ok.IsEnabled = why == null;
            };
            pick.SelectionChanged += (o, e) => { box.Text = names.NameOf(list[Math.Max(0, pick.SelectedIndex)]); box.SelectAll(); check(); };
            box.TextChanged += (o, e) => check();
            ok.Click += (o, e) => { if (Rename(list[Math.Max(0, pick.SelectedIndex)], box.Text) == null) w.Close(); else check(); };
            cancel.Click += (o, e) => w.Close();
            w.Opened += (o, e) => { box.Focus(); box.SelectAll(); };
            check();
            RenameWindowOpened?.Invoke(w);
            return w.ShowDialog(this);
        }

        /// <summary>
        /// Developer-only, not in the manual (Ctrl+Shift+K): the Dialogue tab's source in a box,
        /// compiled back into the conversation (ConversationCompiler). The editable Dialogue tab
        /// comes later; this is for trying the compiler.
        /// </summary>
        public Task CompileFromSource()
        {
            if (Model.ScriptChanged) { Model.Status = "The script has edits that are not applied: apply or undo them first."; return Task.CompletedTask; }
            var mono = Application.Current?.FindResource("Mono") as FontFamily ?? FontFamily.Default;
            var box = new TextBox { Text = lib.Decompile(current).Text, AcceptsReturn = true, AcceptsTab = true, TextWrapping = TextWrapping.NoWrap, FontFamily = mono, FontSize = 12 };
            var report = new TextBlock { TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 8, 0, 0), FontFamily = mono, FontSize = 11 };
            var compile = new Button { Content = "Compile", MinWidth = 80, HorizontalContentAlignment = HorizontalAlignment.Center };
            var apply = new Button { Content = "Apply", MinWidth = 80, IsEnabled = false, HorizontalContentAlignment = HorizontalAlignment.Center };
            var close = new Button { Content = "Close", IsCancel = true, MinWidth = 80, HorizontalContentAlignment = HorizontalAlignment.Center };
            var w = new Window
            {
                Title = "Compile from source (developer) — conversation " + current.Slot, Width = 900, Height = 700, WindowStartupLocation = WindowStartupLocation.CenterOwner,
                Content = new DockPanel
                {
                    Margin = new Thickness(12),
                    Children =
                    {
                        new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right, Spacing = 8, Margin = new Thickness(0, 8, 0, 0), Children = { compile, apply, close }, [DockPanel.DockProperty] = Dock.Bottom },
                        new ScrollViewer { MaxHeight = 160, Content = report, [DockPanel.DockProperty] = Dock.Bottom },
                        box,
                    },
                },
            };
            CompileResult last = null;
            compile.Click += (o, e) =>
            {
                last = lib.Compile(current, box.Text);
                report.Text = CompileReport(last);
                apply.IsEnabled = last.Ok;
                if (!last.Ok && last.Errors.Count > 0) GoToSourceLine(box, last.Errors[0].Line);
            };
            box.TextChanged += (o, e) => apply.IsEnabled = false;
            apply.Click += (o, e) => { if (last != null && last.Ok && ApplyCompiled(last)) w.Close(); };
            close.Click += (o, e) => w.Close();
            return w.ShowDialog(this);
        }

        static void GoToSourceLine(TextBox box, int line)
        {
            string s = box.Text ?? "";
            int p = 0;
            for (int i = 1; i < line && p >= 0; i++) { p = s.IndexOf('\n', p); if (p >= 0) p++; }
            if (p < 0) return;
            int end = s.IndexOf('\n', p);
            box.SelectionStart = p;
            box.SelectionEnd = end < 0 ? s.Length : end;
        }

        public static string CompileReport(CompileResult r)
        {
            var lines = new List<string>();
            foreach (var e in r.Errors) lines.Add("Error: " + e);
            foreach (var e in r.Warnings) lines.Add("Warning: " + e);
            if (r.Ok)
            {
                lines.Add(string.Format("Compiled: {0} function{1} compiled, {2} copied unchanged; {3} words of code.", r.CompiledFunctions.Count, r.CompiledFunctions.Count == 1 ? "" : "s", r.CopiedFunctions.Count, r.Conversation.Code.Length));
                foreach (var s in r.AddedStrings) lines.Add("New string " + s);
                foreach (var s in r.UnusedStrings) lines.Add("No longer used (kept): " + s);
            }
            return string.Join("\n", lines);
        }

        /// <summary>Puts a compiled conversation (and any new strings) in place, as Apply on the Script tab does.</summary>
        public bool ApplyCompiled(CompileResult r)
        {
            if (current == null || r == null || !r.Ok || r.Conversation.Slot != current.Slot) return false;
            try { lib.Apply(r); }
            catch (Exception ex) { Model.Status = "Not applied: " + ex.Message; return false; }
            current = lib.Archive.Read(current.Slot);
            if (r.StringsChanged) { FillStrings(); StringsChanged?.Invoke(); }
            RefreshDialogue();
            MarkChanged();
            Model.Status = string.Format("Conversation {0} compiled from source: {1} words of code{2} (not saved).", current.Slot, current.Code.Length, r.AddedStrings.Count > 0 ? ", " + r.AddedStrings.Count + " new string" + (r.AddedStrings.Count == 1 ? "" : "s") : "");
            CurrentChanged?.Invoke();
            return true;
        }

        /// <summary>For tests: the rename dialog as it opens.</summary>
        public static event Action<Window> RenameWindowOpened;

        async void OnExportNames(object sender, RoutedEventArgs e)
        {
            if (lib == null) return;
            var f = await StorageProvider.SaveFilePickerAsync(new FilePickerSaveOptions
            {
                Title = "Export the conversation names", SuggestedFileName = NameBook.ShippedResource(lib.Kind), DefaultExtension = "json",
                FileTypeChoices = new[] { new FilePickerFileType("JSON") { Patterns = new[] { "*.json" } } },
            });
            string path = f?.TryGetLocalPath();
            if (path != null) ExportNames(path);
        }

        /// <summary>Writes the shipped names with the user's renames folded in, in the shipped file's format.</summary>
        public bool ExportNames(string path)
        {
            try
            {
                var file = lib.Names.Export(slot => { var raw = lib.Archive.RawBlock(slot); return raw == null ? null : Conversation.Parse(raw, slot); });
                File.WriteAllText(path, file.ToJson());
                Model.Status = "Names exported to " + path + ": copy it over docs/conversations/" + NameBook.ShippedResource(lib.Kind) + " to ship them.";
                return true;
            }
            catch (Exception ex) { Model.Status = "Names not exported: " + ex.Message; return false; }
        }
    }
}
