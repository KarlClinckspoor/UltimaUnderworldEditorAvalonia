using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Avalonia.Controls;
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
        readonly ConversationLibrary lib;
        readonly Action<ConversationUser> goTo;
        readonly Action<int> showGlobals;
        Dictionary<int, List<ConversationUser>> users;
        Conversation current;
        List<string> originalStrings = new List<string>();
        bool dialogueStale;

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
            if (lib != null)
            {
                Title = "Conversations — " + lib.Session.Title;
                FillList();
                Model.Status = string.Format("{0} conversations in {1}. Finding who talks through each…", Model.All.Count, lib.Archive.Path);
                _ = FindUsersAsync();
            }
        }

        void OnKey(object sender, KeyEventArgs e)
        {
            var ctrl = e.KeyModifiers.HasFlag(KeyModifiers.Control) || e.KeyModifiers.HasFlag(KeyModifiers.Meta);
            if (ctrl && e.Key == Key.S) { Save(); e.Handled = true; }
            else if (ctrl && e.Key == Key.G) { OpenGlobals(); e.Handled = true; }
            else if (ctrl && e.Key == Key.Enter && Model.Tab == 2) { ApplyScript(); e.Handled = true; }
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
            Model.Status = string.Format("{0} conversations. Strings and scripts can be edited; Save writes CNV.ARK and STRINGS.PAK.", Model.All.Count);
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
            if (slot < 0 || lib == null) { Model.Header = ""; Model.Subheader = ""; Model.SetScriptQuietly(""); return; }
            try { current = lib.Archive.Read(slot); }
            catch (Exception ex) { Model.Header = "Conversation " + slot; Model.Subheader = "Cannot be read: " + ex.Message; return; }
            string name = lib.Name(slot);
            Model.Header = string.Format("{0}  ·  conversation {1}", name ?? "No name", slot);
            int imported = current.Imports.Count(i => !i.IsFunction && ImportedVariables.IsBound(i.Name, lib.Kind));
            Model.Subheader = string.Format("Strings block 0x{0:X4} · {1} words of code · {2} variables ({3} the game fills in, the rest kept in BGLOBALS.DAT) · {4} built-ins imported",
                current.StringBlock, current.Code.Length, current.Variables, imported, current.Imports.Count(i => i.IsFunction));
            originalStrings = lib.Strings.Original(current.StringBlock);
            RefreshDialogue();
            FillStrings();
            FillUsers(slot);
        }

        void RefreshDialogue()
        {
            dialogueStale = false;
            Model.Lines.Clear();
            if (current == null) return;
            var d = lib.Decompile(current);
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
                    "Conversation " + current.Slot + (lib.Name(current.Slot) is string nm ? " — " + nm : "")));
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

        /// <summary>Opens the Script tab with the caret on the instruction at a code address.</summary>
        public void ShowAddress(int address)
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
                ScriptBox.Focus();
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
            Model.Status = "Conversation " + slot + " is as it was saved.";
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

        void OpenGlobals() { showGlobals?.Invoke(current == null ? -1 : current.Slot); }
    }
}
