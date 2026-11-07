using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using UwEditor.Core.Conversations;
using UwEditor.Core.Tests;
using Xunit;

namespace UwEditor.App.Tests
{
    /// <summary>
    /// The Dialogue tab as an editor of the conversation's source: typing, the live check,
    /// Apply and its undo, reverting, simulating the typed text, names, navigation and hovers,
    /// unsaved text. Nothing here saves: the library keeps the edits in memory.
    /// </summary>
    public class ConversationDialogueEditorTests
    {
        static string Uw1 { get { return GameData.Game("UW"); } }

        const string FirstLine = "\"Hast thou come to add to my torment?\" #1";
        const string LastChoice = "\"Indeed I have, thou mewling child!\" #5:\n                flee_and_end();\n";

        /// <summary>Replaces text in the editor as typing would (one change of the document).</summary>
        static void Type(ConversationWindow cw, string find, string with)
        {
            int at = cw.DialogueText.IndexOf(find, StringComparison.Ordinal);
            Assert.True(at >= 0, "not in the text: " + find);
            cw.Editor.Document.Replace(at, find.Length, with);
            Ui.Pump();
        }

        static ConversationWindow Corby(out MainWindow w)
        {
            w = Ui.Open(Uw1);
            w.AskBeforeDiscarding = false;
            var cw = w.ShowConversations(1);
            Ui.Pump();
            cw.Model.Tab = 0;
            Ui.Pump();
            return cw;
        }

        [AvaloniaFact]
        public void Typing_a_say_line_and_a_menu_choice_then_applying_changes_the_conversation_the_simulator_shows_it_and_undo_puts_it_back()
        {
            if (Uw1 == null) return;
            var cw = Corby(out var w);
            var lib = cw.Library;
            byte[] before = lib.Archive.Read(1).ToBytes();
            var stringsBefore = lib.StringsOf(cw.Current);
            Assert.False(cw.DialoguePending);
            Assert.False(Ui.Find<Button>(cw, "ApplyDialogueButton").IsEnabled);

            // real typing: a key in the editor makes the text "not applied"
            cw.Editor.CaretOffset = cw.DialogueText.IndexOf(FirstLine, StringComparison.Ordinal) + 1;
            Ui.Type(cw, cw.Editor.TextArea, "Well, ");
            Assert.Contains("\"Well, Hast thou", cw.DialogueText);
            Assert.True(cw.DialoguePending);
            Assert.True(Ui.Find<Button>(cw, "ApplyDialogueButton").IsEnabled);
            cw.Editor.Undo();
            Ui.Pump();
            Assert.False(cw.DialoguePending);

            // change the say line, and add a fifth choice to its menu
            Type(cw, FirstLine, "\"Art thou here to mock me?\"");
            Type(cw, LastChoice, LastChoice + "            \"I shall leave thee be.\":\n                flee_and_end();\n");
            var check = cw.CheckNow();
            Assert.True(check.Ok, string.Join("; ", check.Errors));
            Assert.Empty(cw.Model.Problems);

            Ui.Key(cw, Key.Enter, RawInputModifiers.Control);   // Ctrl+Enter applies
            Assert.False(cw.DialoguePending, cw.Model.Status);
            Assert.True(lib.Archive.IsChanged(1));
            Assert.Contains("Art thou here to mock me?", lib.ShownStringsOf(cw.Current));
            Assert.Contains("I shall leave thee be.", lib.ShownStringsOf(cw.Current));
            // the text is the new conversation's, with the new strings' numbers
            Assert.Contains("say \"Art thou here to mock me?\" #47;", cw.DialogueText);
            Assert.Contains("\"I shall leave thee be.\" #48:", cw.DialogueText);
            Assert.Contains(cw.Model.Lines, l => l.Line.Kind == PseudoKind.Say && l.Text.Contains("Art thou here to mock me?"));
            // the Script and Strings tabs follow
            Assert.Contains("PUSHI     47", cw.Model.Script);
            Assert.Equal(49, cw.Model.Strings.Count);
            Assert.True(cw.Model.CanUndoApply);

            // the simulator runs the applied conversation
            var sw = cw.ShowSimulator();
            Ui.Pump();
            sw.Run();
            Ui.Pump();
            Assert.Equal(SimState.WaitingMenu, sw.Simulator.State);
            Assert.Equal("Art thou here to mock me?", sw.Transcript[0].Text);
            Assert.Equal(5, sw.ChoiceButtons.Count());
            Assert.Equal("I shall leave thee be.", sw.Simulator.Menu.Texts.Last());

            // Undo apply: the bytes and strings as they were, in every view
            Assert.True(cw.UndoApply(), cw.Model.Status);
            Ui.Pump();
            Assert.Equal(before, lib.Archive.Read(1).ToBytes());
            Assert.False(lib.Archive.IsChanged(1));
            Assert.Equal(stringsBefore, lib.StringsOf(cw.Current));
            Assert.False(lib.Strings.IsChanged(cw.Current.StringBlock));
            Assert.Contains("say " + FirstLine + ";", cw.DialogueText);
            Assert.False(cw.DialoguePending);
            Assert.Equal(47, cw.Model.Strings.Count);
            Assert.Empty(sw.Transcript);      // the simulator started again on the conversation as it is
            sw.Run();
            Ui.Pump();
            Assert.Equal("Hast thou come to add to my torment?", sw.Transcript[0].Text);
            Assert.Equal(4, sw.ChoiceButtons.Count());
            // and Redo apply brings it back
            Assert.True(cw.RedoApply());
            Assert.Contains("I shall leave thee be.", lib.ShownStringsOf(cw.Current));
            Assert.True(cw.UndoApply());
            Assert.False(lib.HasChanges);
            sw.Close();
            cw.Close();
            w.Close();
        }

        [AvaloniaFact]
        public void An_error_is_underlined_and_listed_and_apply_and_simulate_are_refused()
        {
            if (Uw1 == null) return;
            var cw = Corby(out var w);
            Type(cw, "flee_and_end();\n", "flee_and_endd();\n");
            var r = cw.CheckNow();
            Assert.False(r.Ok);
            var problem = Assert.Single(cw.Model.Problems);
            Assert.Contains("flee_and_endd", problem.Message);
            Assert.False(problem.Warning);
            // underlined on the name, and its message on hover
            var mark = Assert.Single(cw.Underlined);
            Assert.Equal("flee_and_endd", cw.DialogueText.Substring(mark.StartOffset, mark.Length));
            Assert.Contains("Error: ", cw.HoverTextAt(mark.StartOffset + 3));
            Assert.Contains("1 error", cw.Model.ProblemSummary);

            Assert.False(cw.ApplyDialogue());
            Assert.StartsWith("Not applied: 1 error", cw.Model.Status);
            Assert.False(cw.Library.HasChanges);
            Assert.True(cw.DialoguePending);
            // the caret went to the error
            Assert.Equal(problem.Error.Line, cw.Editor.TextArea.Caret.Line);
            Assert.Null(cw.ShowSimulator());
            Assert.StartsWith("Not simulated", cw.Model.Status);

            // the live check runs after a pause in typing
            Type(cw, "flee_and_endd();\n", "flee_and_end();\n");
            var until = DateTime.Now.AddSeconds(20);
            while (cw.Model.Problems.Count > 0 && DateTime.Now < until) { System.Threading.Thread.Sleep(50); Ui.Pump(); }
            Assert.Empty(cw.Model.Problems);
            Assert.Empty(cw.Underlined);
            Assert.False(cw.DialoguePending);
            cw.Close();
            w.Close();
        }

        [AvaloniaFact]
        public void Untouched_functions_stay_byte_identical_after_an_apply()
        {
            if (Uw1 == null) return;
            var cw = Corby(out var w);
            var lib = cw.Library;
            var old = cw.Current;
            var oldText = lib.Decompile(old);
            string edited = "hast_thou_come_to";
            int at = cw.Names.Layout.Functions.First(f => cw.Names.Function(f) == edited);
            Type(cw, FirstLine, "\"Art thou here to mock me?\"");
            Assert.True(cw.ApplyDialogue(), cw.Model.Status);
            var now = cw.Current;
            Assert.Equal(old.StringBlock, now.StringBlock);
            Assert.Equal(old.Variables, now.Variables);
            Assert.Equal(old.Imports.Select(i => i.Name), now.Imports.Select(i => i.Name));
            // the code before the edited function is the same words; the functions after it have the same text
            for (int i = 0; i < at; i++) Assert.Equal(old.Code[i], now.Code[i]);
            var newText = lib.Decompile(now);
            var oldFns = Functions(oldText.Text);
            var newFns = Functions(newText.Text);
            Assert.Equal(oldFns.Keys, newFns.Keys);
            foreach (var f in oldFns.Keys.Where(k => k != edited)) Assert.Equal(oldFns[f], newFns[f]);
            Assert.NotEqual(oldFns[edited], newFns[edited]);
            // one function compiled
            Assert.Equal(new[] { edited }, cw.LastApplied.CompiledFunctions);
            cw.Close();
            w.Close();
        }

        static Dictionary<string, string> Functions(string text)
        {
            var d = new Dictionary<string, string>();
            foreach (var f in ConversationText.Functions(text)) d[f.Name] = text.Substring(f.Start, f.End - f.Start);
            return d;
        }

        [AvaloniaFact]
        public void Accented_text_goes_through_the_accent_table_and_comes_back()
        {
            if (Uw1 == null) return;
            var cw = Corby(out var w);
            var lib = cw.Library;
            var accents = lib.Session.Accents;
            var keep = accents.Clone();
            try
            {
                accents.Set('$', 'é');
                Type(cw, FirstLine, "\"Café, étranger?\"");
                Assert.True(cw.ApplyDialogue(), cw.Model.Status);
                Assert.Contains("Caf$, $tranger?", lib.StringsOf(cw.Current));
                Assert.Contains("say \"Café, étranger?\" #47;", cw.DialogueText);
                Assert.Equal("Café, étranger?", cw.Model.Strings[47].Text);
                // a letter the table has no code for is an error at its line, and nothing is applied
                Type(cw, "\"Café, étranger?\" #47", "\"Pão?\"");
                var r = cw.CheckNow();
                Assert.False(r.Ok);
                Assert.Contains(r.Errors, e => e.Message.Contains("accent table"));
                Assert.False(cw.ApplyDialogue());
                cw.RevertToApplied();
                Assert.False(cw.DialoguePending);
                Assert.True(cw.UndoApply());
                Assert.False(lib.HasChanges);
            }
            finally
            {
                accents.Clear();
                foreach (var kv in keep.Map) accents.Set(kv.Key, kv.Value);
            }
            cw.Close();
            w.Close();
        }

        [AvaloniaFact]
        public void Revert_to_the_original_puts_back_the_shipped_conversation_as_one_step()
        {
            if (Uw1 == null) return;
            var cw = Corby(out var w);
            var lib = cw.Library;
            byte[] shipped = lib.Archive.RawBlock(1);
            Type(cw, FirstLine, "\"Art thou here to mock me?\"");
            Assert.True(cw.ApplyDialogue());
            Type(cw, "\"Art thou here to mock me?\" #47", "\"Again?\"");
            Assert.True(cw.DialoguePending);
            Assert.True(cw.RevertToOriginal(), cw.Model.Status);
            Assert.Equal(shipped, lib.Archive.Read(1).ToBytes());
            Assert.False(lib.Archive.IsChanged(1));
            Assert.False(lib.Strings.IsChanged(cw.Current.StringBlock));
            Assert.False(cw.DialoguePending);
            Assert.Contains("say " + FirstLine + ";", cw.DialogueText);
            // one step back: the applied edit again
            Assert.True(cw.UndoApply());
            Assert.Contains("Art thou here to mock me?", lib.ShownStringsOf(cw.Current));
            Assert.True(cw.UndoApply());
            Assert.False(lib.HasChanges);
            cw.Close();
            w.Close();
        }

        [AvaloniaFact]
        public void Simulate_runs_the_typed_text_without_applying_it()
        {
            if (Uw1 == null) return;
            var cw = Corby(out var w);
            Type(cw, FirstLine, "\"Art thou here to mock me?\"");
            Ui.Key(cw, Key.R, RawInputModifiers.Control);
            var sw = cw.ShowSimulator();
            Assert.NotNull(sw);
            Assert.True(cw.SimulatingTypedText);
            sw.Run();
            Ui.Pump();
            Assert.Equal("Art thou here to mock me?", sw.Transcript[0].Text);
            Assert.False(cw.Library.HasChanges);
            Assert.True(cw.DialoguePending);
            // reverting and simulating again runs the conversation as it is
            cw.RevertToApplied();
            cw.ShowSimulator();
            Assert.False(cw.SimulatingTypedText);
            sw.Run();
            Ui.Pump();
            Assert.Equal("Hast thou come to add to my torment?", sw.Transcript[0].Text);
            sw.Close();
            cw.Close();
            w.Close();
        }

        [AvaloniaFact]
        public void F2_renames_in_the_typed_text_and_the_names_layer_and_F12_goes_to_a_function()
        {
            if (Uw1 == null) return;
            var cw = Corby(out var w);
            try
            {
                // an edit not applied, then a rename of the function the edit calls
                Type(cw, FirstLine, "\"Art thou here to mock me?\"");
                int call = cw.DialogueText.IndexOf("flee_and_end();", StringComparison.Ordinal);
                cw.Editor.CaretOffset = call + 3;
                var t = cw.NameCandidates().Single();
                Assert.Equal(NameKind.Function, t.Kind);
                Assert.Null(cw.Rename(t, "run_away"));
                Assert.Equal("run_away", cw.Names.NameOf(t));
                Assert.DoesNotContain("flee_and_end", cw.DialogueText);
                Assert.Contains("\nrun_away()", cw.DialogueText);
                Assert.Contains("\"Art thou here to mock me?\"", cw.DialogueText);   // the edit is kept
                Assert.True(cw.DialoguePending);
                var r = cw.CheckNow();
                Assert.True(r.Ok, string.Join("; ", r.Errors));
                Assert.Equal(new[] { "hast_thou_come_to" }, r.CompiledFunctions);   // the renamed function itself is unchanged

                // F12 on a call goes to the function's header
                call = cw.DialogueText.IndexOf("run_away();", StringComparison.Ordinal);
                cw.Editor.CaretOffset = call + 2;
                Ui.Key(cw, Key.F12);
                int header = cw.DialogueText.IndexOf("\nrun_away()", StringComparison.Ordinal) + 1;
                Assert.Equal(header, cw.Editor.SelectionStart);
                Assert.Equal("run_away".Length, cw.Editor.SelectionLength);
                Assert.True(cw.GoToDefinition(call + 2));

                // hovers: a string's number, a new string, a built-in
                int s2 = cw.DialogueText.IndexOf("\"Of what torment dost thou speak?\" #2", StringComparison.Ordinal);
                Assert.Contains("String 2 of block 0x0E01", cw.HoverTextAt(s2 + 4));
                int fresh = cw.DialogueText.IndexOf("\"Art thou here to mock me?\"", StringComparison.Ordinal);
                Assert.Contains("A new string", cw.HoverTextAt(fresh + 2));
                int gq = cw.DialogueText.IndexOf("get_quest(", StringComparison.Ordinal);
                if (gq < 0) gq = cw.DialogueText.IndexOf("sex(", StringComparison.Ordinal);
                if (gq >= 0) Assert.Contains("(BABLHACK.C)", cw.HoverTextAt(gq + 1));
                int npc = cw.DialogueText.IndexOf("npc_attitude", StringComparison.Ordinal);
                Assert.Contains("attitude", cw.HoverTextAt(npc + 1));

                // go to line
                cw.GoTo(10);
                Assert.Equal(10, cw.Editor.TextArea.Caret.Line);
            }
            finally
            {
                // back to the shipped names
                var f = cw.Names.Layout.Functions.FirstOrDefault(a => cw.Names.Function(a) == "run_away");
                if (f != 0) cw.Library.Names.Rename(cw.Current, NameTarget.Function(f), "");
            }
            cw.Close();
            w.Close();
        }

        [AvaloniaFact]
        public void Colours_tabs_and_find_work_in_both_looks()
        {
            if (Uw1 == null) return;
            var cw = Corby(out var w);
            var line = ConversationText.ScanLine("    say \"Hi\" #1; get_quest(4); x = npc_attitude; done(); // note");
            var kinds = Enumerable.Range(0, line.Count).Select(i => cw.Colorizer.Classify(line, i)).ToList();
            Assert.Equal(new[] { "keyword", "string", "stringid", "punct", "builtin", "punct", "number", "punct", "punct", "name", "punct", "variable", "punct", "function", "punct", "punct", "punct", "comment" }, kinds);
            // the colours follow the look
            ColourScheme.Set(ColourScheme.Dark);
            Ui.Pump();
            var dark = DialogueColorizer.KeywordBrush;
            ColourScheme.Set(ColourScheme.Light);
            Ui.Pump();
            Assert.NotEqual(dark, DialogueColorizer.KeywordBrush);
            // find is AvaloniaEdit's search panel
            Assert.NotNull(cw.Editor.SearchPanel);
            // Tab indents the selected lines by four spaces, Shift+Tab takes them back
            var doc = cw.Editor.Document;
            var l3 = doc.GetLineByNumber(3);
            string before = cw.DialogueText;
            cw.Editor.Select(l3.Offset, doc.GetLineByNumber(4).EndOffset - l3.Offset);
            cw.Editor.TextArea.Focus();
            Ui.Key(cw, Key.Tab);
            Assert.StartsWith("    ", doc.GetText(doc.GetLineByNumber(4)));
            Assert.True(cw.DialoguePending);
            Ui.Key(cw, Key.Tab, RawInputModifiers.Shift);
            Assert.Equal(before, cw.DialogueText);
            Assert.False(cw.DialoguePending);
            cw.Close();
            w.Close();
        }

        [AvaloniaFact]
        public void Wrap_long_lines_breaks_a_long_line_on_screen_only_and_is_remembered()
        {
            if (Uw1 == null) return;
            var cw = Corby(out var w);
            cw.Width = 1000; cw.Height = 700;
            string longLine = "\"" + string.Concat(Enumerable.Repeat("Hast thou come to add to my torment? ", 12)).TrimEnd() + "\"";
            Type(cw, FirstLine, longLine);
            int at = cw.DialogueText.IndexOf(longLine, StringComparison.Ordinal);
            var docLine = cw.Editor.Document.GetLineByOffset(at);
            var view = cw.Editor.TextArea.TextView;
            Func<int> screenLines = () => { Ui.Pump(); view.EnsureVisualLines(); return view.GetOrConstructVisualLine(docLine).TextLines.Count; };
            Assert.False(cw.WrapLines);
            Assert.Equal(1, screenLines());

            var box = Ui.Find<CheckBox>(cw, "WrapBox");
            box.IsChecked = true;
            Assert.True(cw.WrapLines);
            Assert.True(cw.Editor.WordWrap);
            Assert.True(screenLines() > 1);
            // only the display: the text's lines are the same
            Assert.Equal(docLine.LineNumber, cw.Editor.Document.GetLineByOffset(at).LineNumber);
            Assert.DoesNotContain("\n", cw.Editor.Document.GetText(docLine));
            Assert.True(cw.CheckNow().Ok);
            // kept in the settings: a new window wraps too
            cw.RevertToApplied();
            cw.Close();
            Ui.Pump();
            var cw2 = w.ShowConversations(1);
            Ui.Pump();
            Assert.True(cw2.WrapLines);
            Assert.True(Ui.Find<CheckBox>(cw2, "WrapBox").IsChecked == true);
            cw2.WrapLines = false;
            Assert.False(cw2.Editor.WordWrap);
            cw2.Close();
            w.Close();
        }

        [AvaloniaFact]
        public void Text_not_applied_counts_as_unsaved_work_and_checkpoints_keep_it()
        {
            if (Uw1 == null) return;
            var cw = Corby(out var w);
            Assert.DoesNotContain("conversations or strings", w.UnsavedParts());
            Type(cw, FirstLine, "\"Art thou here to mock me?\"");
            Assert.Contains("conversations or strings", w.UnsavedParts());
            Assert.True(cw.TextPending);
            var c = w.WriteCheckpoint("test", true);
            Assert.NotNull(c);
            try
            {
                Assert.Contains("dialogue-001.txt", c.Files);
                string kept = cw.DialogueText;
                cw.RevertToApplied();
                Assert.False(cw.DialoguePending);
                string said = w.RestoreCheckpoint(c);
                Assert.Contains("Dialogue text of conversation 1", said);
                var cw2 = w.ShowConversations(1);
                Assert.Equal(kept, cw2.DialogueText);
                Assert.True(cw2.DialoguePending);

                // closing asks: Apply, Discard or Cancel
                w.AskBeforeDiscarding = true;
                MessageWindow asked = null;
                Action<MessageWindow> opening = m => asked = m;
                MessageWindow.Opening += opening;
                try
                {
                    cw2.Close();
                    Ui.Pump();
                    Assert.NotNull(asked);
                    Assert.Contains(asked.Buttons.Children.OfType<Button>(), b => (string)b.Content == "Apply");
                    asked.Close();     // the last button: Cancel
                    Ui.Pump();
                    Assert.True(cw2.IsVisible);
                }
                finally { MessageWindow.Opening -= opening; }
                w.AskBeforeDiscarding = false;
                cw2.RevertToApplied();
                cw2.Close();
            }
            finally { w.CheckpointStore.Delete(c); }
            w.Close();
        }
    }
}
