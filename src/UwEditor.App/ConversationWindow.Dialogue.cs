using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Threading;
using AvaloniaEdit;
using AvaloniaEdit.Document;
using AvaloniaEdit.Folding;
using UwEditor.Core.Conversations;

namespace UwEditor.App
{
    /// <summary>
    /// The Dialogue tab: the conversation's source in the conversation language
    /// (docs/conversation-language.md), edited in AvaloniaEdit with colours, line numbers,
    /// find (Ctrl+F), go to line (Ctrl+L), folding and indenting. The text is compiled in the
    /// background after a pause in typing (ConversationCompiler) and its mistakes underlined
    /// and listed; Apply compiles it into the conversation and its strings as one step that
    /// Undo apply puts back.
    /// </summary>
    public partial class ConversationWindow
    {
        /// <summary>How long typing must pause before the text is checked.</summary>
        public static TimeSpan CheckDelay = TimeSpan.FromMilliseconds(450);

        readonly DialogueColorizer colorizer = new DialogueColorizer();
        readonly ProblemRenderer problemRenderer = new ProblemRenderer();
        FoldingManager folding;
        /// <summary>Counts edits: a check waits for the text to rest (CheckDelay with no newer edit).</summary>
        int typingVersion;
        int checkVersion;
        /// <summary>The source of the conversation as it is in the library (decompiled at load and after each change): the text is "not applied" when it differs.</summary>
        string appliedText = "";
        bool settingText;
        CompileResult lastCheck;
        /// <summary>The typed text compiled for the simulator (Simulate with edits not applied), or null.</summary>
        CompileResult simulated;

        /// <summary>An undo step: the slot before and after (its code and strings, and the user's names for it).</summary>
        sealed class ApplyStep { public ConversationState Before, After; public SlotNames NamesBefore, NamesAfter; public string What; }
        readonly Dictionary<int, List<ApplyStep>> undoSteps = new Dictionary<int, List<ApplyStep>>(), redoSteps = new Dictionary<int, List<ApplyStep>>();

        /// <summary>The Dialogue tab's editor.</summary>
        public TextEditor Editor { get { return SourceEditor; } }
        /// <summary>The Dialogue tab's text, as typed.</summary>
        public string DialogueText { get { return SourceEditor.Text ?? ""; } }
        /// <summary>The Dialogue tab has text not applied.</summary>
        public bool DialoguePending { get { return Model.DialogueChanged; } }
        /// <summary>Text typed and not applied, on the Dialogue or the Script tab.</summary>
        public bool TextPending { get { return Model.DialogueChanged || Model.ScriptChanged; } }
        /// <summary>The last check of the text (live or by Apply), or null.</summary>
        public CompileResult LastCheck { get { return lastCheck; } }
        /// <summary>What the last Apply of the Dialogue text compiled, or null.</summary>
        public CompileResult LastApplied { get; private set; }
        /// <summary>The underlined problems, where they are in the text now.</summary>
        public IEnumerable<SourceProblem> Underlined { get { return problemRenderer.All; } }
        public DialogueColorizer Colorizer { get { return colorizer; } }
        public FoldingManager Folding { get { return folding; } }

        void InitDialogue()
        {
            var ed = SourceEditor;
            ed.Options.IndentationSize = 4;
            ed.Options.ConvertTabsToSpaces = true;
            ed.Options.HighlightCurrentLine = true;
            ed.Options.EnableHyperlinks = false;
            ed.Options.EnableEmailHyperlinks = false;
            ed.Options.AllowScrollBelowDocument = true;
            // a wrapped line's rest starts under the line's own indent, plus one step
            ed.Options.InheritWordWrapIndentation = true;
            ed.Options.WordWrapIndentation = 32;
            ed.TextArea.TextView.LineTransformers.Add(colorizer);
            ed.TextArea.TextView.BackgroundRenderers.Add(problemRenderer);
            folding = FoldingManager.Install(ed.TextArea);
            ed.TextChanged += OnSourceChanged;
            ed.PointerHover += OnSourceHover;
            // the tip is the hover's only: cleared when the pointer moves on, so it never shows on its own elsewhere
            ed.PointerHoverStopped += (o, e) => { ToolTip.SetIsOpen(ed, false); ToolTip.SetTip(ed, null); };
            ed.TextArea.AddHandler(PointerPressedEvent, OnSourcePointerPressed, RoutingStrategies.Tunnel);
            ActualThemeVariantChanged += (o, e) => ed.TextArea.TextView.Redraw();
        }

        // ----- the text -----

        /// <summary>
        /// Puts text in the editor. fresh: another conversation (the undo history starts again);
        /// otherwise only the lines that differ are replaced, as one step Ctrl+Z takes back, and
        /// the caret keeps its place.
        /// </summary>
        void SetSourceText(string text, bool fresh)
        {
            var doc = SourceEditor.Document;
            text = text ?? "";
            settingText = true;
            try
            {
                if (fresh)
                {
                    doc.Text = text;
                    doc.UndoStack.ClearAll();
                    SourceEditor.CaretOffset = 0;
                    SourceEditor.ScrollToHome();
                }
                else
                {
                    string old = doc.Text;
                    if (old == text) return;
                    int pre = 0, max = Math.Min(old.Length, text.Length);
                    while (pre < max && old[pre] == text[pre]) pre++;
                    int suf = 0;
                    while (suf < max - pre && old[old.Length - 1 - suf] == text[text.Length - 1 - suf]) suf++;
                    doc.Replace(pre, old.Length - pre - suf, text.Substring(pre, text.Length - pre - suf));
                }
            }
            finally { settingText = false; }
            SourceChanged();
            UpdateFoldings();
        }

        /// <summary>
        /// The source of the conversation as it is now goes in the editor (the decompiler's
        /// text): replaced when there are no edits to keep (force: always), else kept and only
        /// what counts as applied moves on.
        /// </summary>
        void ShowSource(DecompileResult d, bool fresh, bool force)
        {
            string src = d.Text;
            appliedText = src;
            colorizer.SetConversation(current);
            if (fresh || force || SourceEditor.Text == null || !Model.DialogueChanged || SourceEditor.Text == "") SetSourceText(src, fresh);
            else SourceChanged();
        }

        /// <summary>Replaces the Dialogue tab's text with text, as typed (a checkpoint's text not applied).</summary>
        public void SetDialogueText(string text)
        {
            Model.Tab = 0;
            SetSourceText(text, false);
        }

        void OnSourceChanged(object sender, EventArgs e)
        {
            if (settingText) return;
            SourceChanged();
        }

        void SourceChanged()
        {
            Model.DialogueChanged = current != null && (SourceEditor.Text ?? "") != appliedText;
            if (current == null) return;
            // after a pause in typing: a delay on the thread pool, then the check on the UI thread if nothing newer came
            int v = ++typingVersion;
            _ = Task.Delay(CheckDelay).ContinueWith(t => Dispatcher.UIThread.Post(() => { if (v == typingVersion) _ = CheckAsync(); }), TaskScheduler.Default);
        }

        // ----- checking -----

        /// <summary>Compiles the text as it is now, without applying it (on this thread).</summary>
        public CompileResult CompileText() { return CompileText(DialogueText); }

        CompileResult CompileText(string text)
        {
            try { return lib.Compile(current, text); }
            catch (Exception ex)
            {
                var r = new CompileResult();
                r.Errors.Add(new SourceError(1, 1, "The compiler failed: " + ex.Message));
                return r;
            }
        }

        /// <summary>Checks the text in the background and shows its errors when done (the newest check wins).</summary>
        public async Task CheckAsync()
        {
            if (current == null || lib == null) return;
            int version = ++checkVersion;
            string text = DialogueText;
            var c = current;
            var strings = lib.ShownStringsOf(c);
            var names = lib.Names.Resolve(c);
            var kind = lib.Kind;
            var options = new CompileOptions { Accents = lib.Accents };
            CompileResult r;
            try { r = await Task.Run(() => ConversationCompiler.Compile(c, strings, kind, names, text, options)); }
            catch (Exception ex) { r = new CompileResult(); r.Errors.Add(new SourceError(1, 1, "The compiler failed: " + ex.Message)); }
            if (version != checkVersion || c != current || text != DialogueText) return;
            ShowCheck(r);
        }

        /// <summary>Checks the text now and shows its errors (tests, and before Apply).</summary>
        public CompileResult CheckNow()
        {
            typingVersion++;
            checkVersion++;
            if (current == null) return null;
            var r = CompileText();
            ShowCheck(r);
            return r;
        }

        void ShowCheck(CompileResult r)
        {
            lastCheck = r;
            var doc = SourceEditor.Document;
            var list = new List<SourceProblem>();
            Model.Problems.Clear();
            foreach (var e in r.Errors) { list.Add(Problem(doc, e, false)); Model.Problems.Add(new ProblemRow { Error = e }); }
            foreach (var e in r.Warnings) { list.Add(Problem(doc, e, true)); Model.Problems.Add(new ProblemRow { Error = e, Warning = true }); }
            problemRenderer.Set(doc, list);
            SourceEditor.TextArea.TextView.InvalidateLayer(AvaloniaEdit.Rendering.KnownLayer.Selection);
            if (r.Ok)
            {
                string what = !Model.DialogueChanged ? "The text is the conversation as it is." :
                    string.Format("The text compiles: {0} function{1} changed{2}. Apply (Ctrl+Enter) puts it in the conversation.", r.CompiledFunctions.Count, r.CompiledFunctions.Count == 1 ? "" : "s",
                        r.AddedStrings.Count > 0 ? ", " + r.AddedStrings.Count + " new string" + (r.AddedStrings.Count == 1 ? "" : "s") : "");
                Model.ProblemSummary = what + (r.Warnings.Count > 0 ? string.Format(" {0} warning{1}.", r.Warnings.Count, r.Warnings.Count == 1 ? "" : "s") : "");
            }
            else Model.ProblemSummary = string.Format("{0} error{1}{2}: Apply and Simulate are refused until they are fixed. Double-click one to go to it.", r.Errors.Count, r.Errors.Count == 1 ? "" : "s",
                r.Warnings.Count > 0 ? string.Format(", {0} warning{1}", r.Warnings.Count, r.Warnings.Count == 1 ? "" : "s") : "");
        }

        /// <summary>A problem's place in the text: the token at its line and column (a character when there is none).</summary>
        static SourceProblem Problem(TextDocument doc, SourceError e, bool warning)
        {
            int line = Math.Max(1, Math.Min(e.Line, doc.LineCount));
            var dl = doc.GetLineByNumber(line);
            int col = Math.Max(1, e.Column);
            int start = Math.Min(dl.Offset + col - 1, dl.EndOffset);
            int length = 1;
            var tok = ConversationText.SpanAt(doc.Text, start);
            if (tok.HasValue && tok.Value.Start <= start && tok.Value.End > start && tok.Value.Kind != SpanKind.Comment) { start = tok.Value.Start; length = tok.Value.Length; }
            if (start >= dl.EndOffset)
            {
                // past the end of the line: the line's last character, or the line break
                start = dl.Length > 0 ? dl.EndOffset - 1 : dl.Offset;
                length = dl.Length > 0 ? 1 : Math.Min(1, doc.TextLength - start);
            }
            return new SourceProblem { StartOffset = start, Length = Math.Max(0, length), Warning = warning, Message = e.Message, Line = e.Line, Column = e.Column };
        }

        void OnProblemDoubleTapped(object sender, TappedEventArgs e)
        {
            var row = ProblemList.SelectedItem as ProblemRow;
            if (row != null) GoTo(row.Error.Line, row.Error.Column);
        }

        /// <summary>Puts the caret at a line and column of the text (from 1) and the keyboard focus in the editor.</summary>
        public void GoTo(int line, int column = 1)
        {
            Model.Tab = 0;
            var doc = SourceEditor.Document;
            line = Math.Max(1, Math.Min(line, doc.LineCount));
            var dl = doc.GetLineByNumber(line);
            int at = Math.Min(dl.Offset + Math.Max(0, column - 1), dl.EndOffset);
            UnfoldAt(at);
            SourceEditor.CaretOffset = at;
            SourceEditor.TextArea.Caret.BringCaretToView();
            SourceEditor.ScrollToLine(line);
            Dispatcher.UIThread.Post(() => SourceEditor.TextArea.Focus(), DispatcherPriority.Background);
        }

        /// <summary>Ctrl+L: asks for a line number and goes there.</summary>
        public Task AskGoToLine()
        {
            var box = new TextBox { Text = SourceEditor.TextArea.Caret.Line.ToString(CultureInfo.InvariantCulture), Width = 120 };
            var ok = new Button { Content = "Go", IsDefault = true, MinWidth = 70, HorizontalContentAlignment = HorizontalAlignment.Center };
            var cancel = new Button { Content = "Cancel", IsCancel = true, MinWidth = 70, HorizontalContentAlignment = HorizontalAlignment.Center };
            var w = new Window
            {
                Title = "Go to line", SizeToContent = SizeToContent.WidthAndHeight, CanResize = false, WindowStartupLocation = WindowStartupLocation.CenterOwner,
                Content = new StackPanel
                {
                    Margin = new Thickness(16), Spacing = 10,
                    Children =
                    {
                        new TextBlock { Text = "Line (1 to " + SourceEditor.Document.LineCount + ")" },
                        box,
                        new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8, HorizontalAlignment = HorizontalAlignment.Right, Children = { ok, cancel } },
                    },
                },
            };
            ok.Click += (o, e) => { int n; if (int.TryParse(box.Text, NumberStyles.None, CultureInfo.InvariantCulture, out n)) { w.Close(); GoTo(n); } };
            cancel.Click += (o, e) => w.Close();
            w.Opened += (o, e) => { box.Focus(); box.SelectAll(); };
            GoToLineOpened?.Invoke(w);
            return w.ShowDialog(this);
        }

        /// <summary>For tests: the go-to-line dialog as it opens.</summary>
        public static event Action<Window> GoToLineOpened;

        // ----- wrapping -----

        /// <summary>The Dialogue tab breaks lines longer than the editor is wide (display only: the text's lines stay as they are).</summary>
        public bool WrapLines
        {
            get { return SourceEditor.WordWrap; }
            set { if (WrapBox.IsChecked != value) WrapBox.IsChecked = value; SourceEditor.WordWrap = value; }
        }

        /// <summary>Wrap long lines was switched (the main window keeps the choice in the settings).</summary>
        public event Action<bool> WrapChanged;

        void OnWrapChanged(object sender, RoutedEventArgs e)
        {
            bool on = WrapBox.IsChecked == true;
            SourceEditor.WordWrap = on;
            // a wrapped editor has no horizontal scrolling
            SourceEditor.HorizontalScrollBarVisibility = on ? Avalonia.Controls.Primitives.ScrollBarVisibility.Disabled : Avalonia.Controls.Primitives.ScrollBarVisibility.Auto;
            WrapChanged?.Invoke(on);
        }

        // ----- folding -----

        void UpdateFoldings()
        {
            if (folding == null) return;
            string text = SourceEditor.Document.Text;
            var list = new List<NewFolding>();
            foreach (var f in ConversationText.Functions(text))
            {
                int headerEnd = text.IndexOf('\n', f.Start);
                if (headerEnd < 0 || headerEnd >= f.End) continue;
                int end = f.End;
                while (end > headerEnd && char.IsWhiteSpace(text[end - 1])) end--;
                if (end <= headerEnd + 1) continue;
                list.Add(new NewFolding(headerEnd, end) { Name = " … }", DefaultClosed = f.NeverCalled && HideUnusedBox?.IsChecked == true });
            }
            folding.UpdateFoldings(list, -1);
        }

        /// <summary>Folds (hide) or unfolds every function the view marks as never called.</summary>
        void FoldNeverCalled(bool hide)
        {
            if (folding == null) return;
            string text = SourceEditor.Document.Text;
            var never = new HashSet<int>(ConversationText.Functions(text).Where(f => f.NeverCalled).Select(f => text.IndexOf('\n', f.Start)));
            foreach (var s in folding.AllFoldings) if (never.Contains(s.StartOffset)) s.IsFolded = hide;
        }

        void UnfoldAt(int offset)
        {
            if (folding == null) return;
            foreach (var s in folding.GetFoldingsContaining(offset)) if (s.StartOffset < offset) s.IsFolded = false;
        }

        // ----- Apply, revert, undo -----

        void OnApplyDialogue(object sender, RoutedEventArgs e) { ApplyDialogue(); }
        void OnRevertToApplied(object sender, RoutedEventArgs e) { RevertToApplied(); }
        void OnRevertToOriginal(object sender, RoutedEventArgs e) { RevertToOriginal(); }
        void OnUndoApply(object sender, RoutedEventArgs e) { UndoApply(); }
        void OnRedoApply(object sender, RoutedEventArgs e) { RedoApply(); }

        /// <summary>
        /// Compiles the Dialogue text into the conversation and its new strings, as one step
        /// (Undo apply). Refused, with the errors shown, when the text does not compile.
        /// </summary>
        public bool ApplyDialogue()
        {
            if (current == null || lib == null) return false;
            if (!Model.DialogueChanged) { Model.Status = "Nothing to apply: the text is the conversation as it is."; return true; }
            if (Model.ScriptChanged) { Model.Status = "Not applied: the Script tab has edits that are not applied; apply or undo them first."; return false; }
            var r = CheckNow();
            if (!r.Ok)
            {
                var first = r.Errors[0];
                Model.Status = string.Format("Not applied: {0} error{1}. The first, at line {2}: {3}", r.Errors.Count, r.Errors.Count == 1 ? "" : "s", first.Line, first.Message);
                GoTo(first.Line, first.Column);
                return false;
            }
            if (!Commit(st => ApplyAndName(r), "Apply")) return false;
            LastApplied = r;
            string strs = r.AddedStrings.Count == 0 ? "" : "; new string" + (r.AddedStrings.Count == 1 ? " " : "s ") + string.Join(", ", r.AddedStrings.Select(x => "#" + x.Id));
            Model.Status = string.Format("Conversation {0} applied: {1} function{2} compiled, {3} unchanged; {4} words of code{5} (not saved; Undo apply puts it back).",
                current.Slot, r.CompiledFunctions.Count, r.CompiledFunctions.Count == 1 ? "" : "s", r.CopiedFunctions.Count, current.Code.Length, strs);
            return true;
        }

        /// <summary>
        /// Changes the shown conversation in the library as one undoable step: the state before
        /// is kept, change runs, and every view follows (the Dialogue text, Script, Strings, the
        /// graph and the simulator). A change that throws is put back.
        /// </summary>
        bool Commit(Action<ConversationState> change, string what)
        {
            int slot = current.Slot;
            var before = lib.Snapshot(slot);
            var namesBefore = lib.Names.UserSlot(slot);
            try { change(before); }
            catch (Exception ex)
            {
                try { lib.Restore(before); lib.Names.SetUserSlot(slot, namesBefore); } catch (Exception) { }
                Model.Status = "Not applied: " + ex.Message;
                return false;
            }
            Push(undoSteps, slot, new ApplyStep { Before = before, After = lib.Snapshot(slot), NamesBefore = namesBefore, NamesAfter = lib.Names.UserSlot(slot), What = what });
            List<ApplyStep> redo;
            if (redoSteps.TryGetValue(slot, out redo)) redo.Clear();
            AfterLibraryChange(before.Block);
            return true;
        }

        static void Push(Dictionary<int, List<ApplyStep>> steps, int slot, ApplyStep s)
        {
            List<ApplyStep> list;
            if (!steps.TryGetValue(slot, out list)) steps[slot] = list = new List<ApplyStep>();
            list.Add(s);
            if (list.Count > 100) list.RemoveAt(0);
        }

        /// <summary>After the library's copy of the shown conversation changed: every view shows it again, the Dialogue text replaced.</summary>
        void AfterLibraryChange(int oldBlock)
        {
            current = lib.Archive.Read(current.Slot);
            simulated = null;
            if (current.StringBlock != oldBlock) originalStrings = lib.Strings.Original(current.StringBlock).Select(lib.Accents.ToDisplay).ToList();
            FillStrings();
            RefreshDialogue(forceText: true);
            MarkChanged();
            UpdateUndoButtons();
            CheckNow();
            CurrentChanged?.Invoke();
            StringsChanged?.Invoke();
        }

        void UpdateUndoButtons()
        {
            List<ApplyStep> u, r;
            int slot = current == null ? -1 : current.Slot;
            Model.CanUndoApply = slot >= 0 && undoSteps.TryGetValue(slot, out u) && u.Count > 0;
            Model.CanRedoApply = slot >= 0 && redoSteps.TryGetValue(slot, out r) && r.Count > 0;
        }

        /// <summary>Puts the shown conversation back as it was before its last Apply (or revert). Refused while the text has edits not applied.</summary>
        public bool UndoApply() { return Step(undoSteps, redoSteps, true); }

        /// <summary>Applies again what Undo apply took back.</summary>
        public bool RedoApply() { return Step(redoSteps, undoSteps, false); }

        bool Step(Dictionary<int, List<ApplyStep>> from, Dictionary<int, List<ApplyStep>> to, bool undo)
        {
            if (current == null) return false;
            List<ApplyStep> list;
            if (!from.TryGetValue(current.Slot, out list) || list.Count == 0) { Model.Status = undo ? "Nothing to undo for this conversation." : "Nothing to redo for this conversation."; return false; }
            if (TextPending) { Model.Status = (undo ? "Not undone" : "Not redone") + ": the text has edits that are not applied. Apply them, or revert to the last applied, first."; return false; }
            var s = list[list.Count - 1];
            int oldBlock = current.StringBlock;
            try { lib.Restore(undo ? s.Before : s.After); lib.Names.SetUserSlot(current.Slot, undo ? s.NamesBefore : s.NamesAfter); }
            catch (Exception ex) { Model.Status = "Could not put it back: " + ex.Message; return false; }
            list.RemoveAt(list.Count - 1);
            Push(to, current.Slot, s);
            AfterLibraryChange(oldBlock);
            Model.Status = string.Format("{0} of conversation {1} {2} (not saved).", s.What, current.Slot, undo ? "undone" : "done again");
            return true;
        }

        /// <summary>Puts a compiled conversation in the library, and the names its source gave the functions it compiled in the user's names (so the text keeps them).</summary>
        void ApplyAndName(CompileResult r)
        {
            lib.Apply(r);
            lib.Names.Adopt(lib.Archive.Read(r.Conversation.Slot), r.Placed);
        }

        /// <summary>Applies the text typed on the Script and Dialogue tabs (closing asks for it). False when one is refused.</summary>
        public bool ApplyPending()
        {
            if (Model.ScriptChanged && !ApplyScript()) return false;
            if (Model.DialogueChanged && !ApplyDialogue()) return false;
            return true;
        }

        /// <summary>Throws away the Dialogue tab's edits not applied (Ctrl+Z in the text brings them back).</summary>
        public void RevertToApplied()
        {
            if (current == null) return;
            SetSourceText(appliedText, false);
            CheckNow();
            Model.Status = "The text is the conversation as it is now; Ctrl+Z in the text brings the edits back.";
        }

        /// <summary>Puts back the shown conversation and its strings as shipped, as one step (Undo apply takes it back).</summary>
        public bool RevertToOriginal()
        {
            if (current == null || lib == null) return false;
            if (Model.ScriptChanged) { Model.Status = "Not reverted: the Script tab has edits that are not applied; apply or undo them first."; return false; }
            if (lib.Shipped(current.Slot) == null) { Model.Status = "There is no shipped copy of conversation " + current.Slot + "."; return false; }
            if (!Commit(st => lib.RevertToShipped(current.Slot), "Revert to the original")) return false;
            Model.Status = string.Format("Conversation {0} is back as shipped (not saved; Undo apply takes it back).", current.Slot);
            return true;
        }

        /// <summary>Every slot's undo history starts again (after saving: the saved files are the new base).</summary>
        void ForgetApplySteps()
        {
            undoSteps.Clear();
            redoSteps.Clear();
            UpdateUndoButtons();
        }

        // ----- simulating the typed text -----

        /// <summary>The conversation the simulator runs: the typed text compiled when Simulate was asked for with edits not applied, else the shown one.</summary>
        public Conversation SimulatedConversation { get { return simulated != null ? simulated.Conversation : current; } }
        /// <summary>The strings the simulator shows (as shown, the accent table's letters).</summary>
        public List<string> SimulatedStrings { get { return simulated != null ? new List<string>(simulated.Strings) : CurrentStrings; } }
        /// <summary>The simulator runs the typed text, not applied.</summary>
        public bool SimulatingTypedText { get { return simulated != null; } }

        /// <summary>For Simulate: compiles the typed text when the Dialogue tab has edits not applied. False (with why in the status) when it does not compile.</summary>
        bool PrepareSimulation()
        {
            simulated = null;
            if (current == null || Model.Tab != 0 || !Model.DialogueChanged) return true;
            var r = CheckNow();
            if (!r.Ok)
            {
                var first = r.Errors[0];
                Model.Status = string.Format("Not simulated: the text does not compile ({0} error{1}; the first, at line {2}: {3}). Fix it, or revert to the last applied, and simulate again.",
                    r.Errors.Count, r.Errors.Count == 1 ? "" : "s", first.Line, first.Message);
                GoTo(first.Line, first.Column);
                return false;
            }
            simulated = r;
            Model.Status = "Simulating the text as typed (not applied).";
            return true;
        }

        // ----- hovers and going to definitions -----

        void OnSourceHover(object sender, PointerEventArgs e)
        {
            var pos = SourceEditor.GetPositionFromPoint(e.GetPosition(SourceEditor));
            if (!pos.HasValue) return;
            int offset = SourceEditor.Document.GetOffset(pos.Value.Location);
            string tip = HoverTextAt(offset);
            if (string.IsNullOrEmpty(tip)) return;
            ToolTip.SetTip(SourceEditor, new TextBlock { Text = tip, TextWrapping = TextWrapping.Wrap, MaxWidth = 520 });
            ToolTip.SetIsOpen(SourceEditor, true);
            e.Handled = true;
        }

        /// <summary>
        /// What hovering at an offset shows: the problems there; for a string, its number in the
        /// block (or that Apply adds it); for a built-in or imported variable, what it does; for a
        /// function, where it is. Null for nothing.
        /// </summary>
        public string HoverTextAt(int offset)
        {
            if (current == null) return null;
            var lines = new List<string>();
            foreach (var p in problemRenderer.At(offset)) lines.Add((p.Warning ? "Warning: " : "Error: ") + p.Message);
            string text = DialogueText;
            var tok = ConversationText.SpanAt(text, offset);
            if (tok.HasValue)
            {
                var t = tok.Value;
                string about = null;
                if (t.Kind == SpanKind.String) about = StringHover(text, t);
                else if (t.Kind == SpanKind.StringId)
                {
                    int id;
                    var strs = lib.ShownStringsOf(current);
                    if (int.TryParse(t.Text.Substring(1), NumberStyles.None, CultureInfo.InvariantCulture, out id))
                        about = id < strs.Count ? "String " + id + " of block 0x" + current.StringBlock.ToString("X4") + ": " + Decompiler.Quote(strs[id]) : "String " + id + ": the block has only " + strs.Count + " strings.";
                }
                else if (t.Kind == SpanKind.Name)
                {
                    var line = LineSpans(text, offset, out int lineStart);
                    int i = line.FindIndex(x => x.Start + lineStart == t.Start);
                    string kind = i >= 0 ? colorizer.Classify(line, i) : "name";
                    if (kind == "builtin") about = ConversationBuiltins.Describe(t.Text) ?? t.Text + ": a built-in function of the game.";
                    else if (kind == "variable") about = ConversationBuiltins.DescribeVariable(t.Text) ?? t.Text + ": a variable the game fills in.";
                    else if (kind == "function")
                    {
                        var f = ConversationText.Functions(text).FirstOrDefault(x => x.Name == t.Text);
                        about = f != null ? "Function " + t.Text + ", line " + f.Line + (f.NeverCalled ? " (never called)" : "") + ". F12 or Ctrl+click goes there." : "There is no function " + t.Text + " in the text.";
                    }
                }
                if (about != null) lines.Add(about);
            }
            return lines.Count == 0 ? null : string.Join("\n", lines);
        }

        static List<SourceSpan> LineSpans(string text, int offset, out int lineStart)
        {
            lineStart = offset == 0 ? 0 : text.LastIndexOf('\n', Math.Min(offset, text.Length) - 1) + 1;
            int nl = text.IndexOf('\n', lineStart);
            var spans = ConversationText.ScanLine(text, lineStart, nl < 0 ? text.Length : nl);
            int ls = lineStart;
            return spans.Select(s => new SourceSpan { Kind = s.Kind, Start = s.Start - ls, Length = s.Length, Text = s.Text }).ToList();
        }

        /// <summary>A string's hover: the number the compiler takes it as (the compiler's rule: "Words" in the language).</summary>
        string StringHover(string text, SourceSpan t)
        {
            var strs = lib.ShownStringsOf(current);
            string s = ConversationText.Unquote(t.Text);
            string block = " of block 0x" + current.StringBlock.ToString("X4");
            // a number written after it: "text" #n
            var line = LineSpans(text, t.Start, out int ls);
            int i = line.FindIndex(x => x.Start + ls == t.Start);
            if (i >= 0 && i + 1 < line.Count && line[i + 1].Kind == SpanKind.StringId)
            {
                int id;
                if (int.TryParse(line[i + 1].Text.Substring(1), NumberStyles.None, CultureInfo.InvariantCulture, out id) && id < strs.Count && strs[id] == s)
                    return "String " + id + block + ".";
            }
            int at = strs.IndexOf(s);
            if (at >= 0) return "String " + at + block + (i >= 0 && i + 1 < line.Count && line[i + 1].Kind == SpanKind.StringId ? " (its text is not string " + line[i + 1].Text.Substring(1) + "'s, so it is taken as the first string with this text)." : ".");
            var added = lastCheck != null && lastCheck.Ok ? lastCheck.AddedStrings.FirstOrDefault(x => x.Text == s) : null;
            return added != null ? "A new string: Apply adds it" + block + " as string " + added.Id + "." : "A new string: Apply adds it at the end of the block" + block + ".";
        }

        void OnSourcePointerPressed(object sender, PointerPressedEventArgs e)
        {
            if (!e.KeyModifiers.HasFlag(KeyModifiers.Control) || !e.GetCurrentPoint(SourceEditor).Properties.IsLeftButtonPressed) return;
            var pos = SourceEditor.GetPositionFromPoint(e.GetPosition(SourceEditor));
            if (!pos.HasValue) return;
            if (GoToDefinition(SourceEditor.Document.GetOffset(pos.Value.Location))) e.Handled = true;
        }

        /// <summary>F12 / Ctrl+click: the name at an offset (the caret's when -1) to where it is defined: a function's header, a label, a local's or an argument's declaration.</summary>
        public bool GoToDefinition(int offset = -1)
        {
            if (offset < 0) offset = SourceEditor.CaretOffset;
            string text = DialogueText;
            var tok = ConversationText.SpanAt(text, offset);
            if (!tok.HasValue || tok.Value.Kind != SpanKind.Name) { Model.Status = "Put the caret on a function, label or local to go to it (F12)."; return false; }
            int at = ConversationText.DefinitionOf(text, tok.Value.Text, offset);
            if (at < 0)
            {
                string d = ConversationBuiltins.Describe(tok.Value.Text) ?? ConversationBuiltins.DescribeVariable(tok.Value.Text);
                Model.Status = d != null ? tok.Value.Text + " is the game's: " + d : "There is no definition of " + tok.Value.Text + " in the text.";
                return false;
            }
            UnfoldAt(at);
            SourceEditor.Select(at, tok.Value.Text.Length);
            SourceEditor.TextArea.Caret.Offset = at;
            SourceEditor.ScrollToLine(SourceEditor.Document.GetLineByOffset(at).LineNumber);
            Model.Status = tok.Value.Text + ": line " + SourceEditor.Document.GetLineByOffset(at).LineNumber + ".";
            return true;
        }

        // ----- names (F2) in the text -----

        /// <summary>The function, label or variable whose name is at the caret, in the conversation as applied.</summary>
        List<NameTarget> DialogueNameCandidates()
        {
            var list = new List<NameTarget>();
            string text = DialogueText;
            int caret = SourceEditor.SelectionLength > 0 ? SourceEditor.SelectionStart : SourceEditor.CaretOffset;
            var tok = ConversationText.SpanAt(text, caret);
            if (!tok.HasValue || tok.Value.Kind != SpanKind.Name) return list;
            int fn = 0;
            var range = ConversationText.FunctionAt(text, caret);
            if (range != null)
            {
                var ft = names.Find(current, range.Name, 0);
                if (ft.HasValue && ft.Value.Kind == NameKind.Function) fn = ft.Value.Address;
            }
            var t = names.Find(current, tok.Value.Text, fn);
            if (t.HasValue && !(t.Value.Kind == NameKind.Function && t.Value.Address == 0)) list.Add(t.Value);
            return list;
        }

        /// <summary>A rename on the names layer, made in the typed text too when it has edits not applied (a local's only in its function).</summary>
        void RenameInText(NameTarget t, string oldName, string newName)
        {
            if (!Model.DialogueChanged || oldName == newName) return;
            string text = DialogueText;
            FunctionRange scope = null;
            if (t.Kind == NameKind.Local)
            {
                string fname = names.Function(t.Address);
                scope = ConversationText.Functions(text).FirstOrDefault(f => f.Name == fname);
                if (scope == null) return;
            }
            var uses = ConversationText.Uses(text, oldName, scope);
            if (uses.Count == 0) return;
            var doc = SourceEditor.Document;
            settingText = true;
            try
            {
                using (doc.RunUpdate())
                    for (int i = uses.Count - 1; i >= 0; i--) doc.Replace(uses[i], oldName.Length, newName);
            }
            finally { settingText = false; }
        }
    }
}
