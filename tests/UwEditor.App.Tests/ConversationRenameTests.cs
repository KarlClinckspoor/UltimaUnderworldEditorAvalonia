using System;
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
    /// <summary>F2 in the conversation editor: renaming a function, label or variable everywhere it shows, and keeping the rename.</summary>
    public class ConversationRenameTests
    {
        [AvaloniaFact]
        public void F2_renames_a_function_in_every_view_and_the_script_still_assembles_to_the_same_bytes()
        {
            string uw1 = GameData.Game("UW"); if (uw1 == null) return;
            var w = Ui.Open(uw1);
            var cw = w.ShowConversations(1);
            Ui.Pump();
            string userFile = NameBook.UserFile(Settings.Folder, UltimateEditor.Games.GameKind.UW1);
            Assert.Equal(userFile, cw.Library.Names.UserPath);
            var graph = cw.ShowGraph();
            var sim = cw.ShowSimulator();
            Ui.Pump();

            // the shipped names show: a function named after what Corby says first
            var fline = cw.Model.Lines.First(l => l.Line.Kind == PseudoKind.Function && l.Line.Address != 0 && !l.Text.StartsWith("func_") && !l.Text.Contains("never called"));
            var target = NameTarget.Function(fline.Line.Address);
            string shipped = cw.Names.Function(fline.Line.Address);
            Assert.StartsWith(shipped + "()", fline.Text);
            Assert.Contains(graph.Graph.Visible, n => n.Title == shipped + "()");

            cw.Model.Tab = 0;
            var list = Ui.Find<ListBox>(cw, "DialogueList");
            list.SelectedItem = fline;
            Ui.Pump();
            Assert.Equal(target, cw.NameCandidates()[0]);

            // F2 asks; a clash and a bad name are refused
            Window dialog = null;
            Action<Window> opened = d => dialog = d;
            ConversationWindow.RenameWindowOpened += opened;
            Ui.Key(cw, Key.F2);
            Ui.Pump();
            ConversationWindow.RenameWindowOpened -= opened;
            Assert.NotNull(dialog);
            dialog.Close();
            Ui.Pump();
            string other = cw.Names.Function(cw.Names.Layout.Functions.First(f => f != 0 && f != target.Address));
            Assert.NotNull(cw.Rename(target, other));
            Assert.NotNull(cw.Rename(target, "has space"));

            Assert.Null(cw.Rename(target, "corby_greets"));
            Ui.Pump();
            Assert.Contains(cw.Model.Lines, l => l.Line.Kind == PseudoKind.Function && l.Text.StartsWith("corby_greets()"));
            Assert.Contains("CALL      corby_greets", cw.Model.Script);
            Assert.False(cw.ScriptPending);
            Assert.Equal(cw.Current.ToBytes(), Assembler.Assemble(cw.Model.Script).ToBytes());
            Assert.Contains(graph.Graph.Visible, n => n.Title == "corby_greets()");
            Assert.Contains("corby_greets", File.ReadAllText(userFile));
            Assert.False(cw.Library.HasChanges);

            // on the Script tab, the label at the caret
            cw.Model.Tab = 2;
            Ui.Pump();
            var box = Ui.Find<TextBox>(cw, "ScriptBox");
            int label = cw.Names.Layout.Labels.Min();
            string lname = cw.Names.Label(label);
            int at = cw.Model.Script.IndexOf("\n" + lname + ":", StringComparison.Ordinal) + 2;
            box.SelectionStart = box.SelectionEnd = at;
            box.CaretIndex = at;
            Assert.Equal(NameTarget.Label(label), cw.NameCandidates().Single());
            Assert.Null(cw.Rename(NameTarget.Label(label), "first_label"));
            Assert.Contains("first_label:", cw.Model.Script);
            Assert.Equal(cw.Current.ToBytes(), Assembler.Assemble(cw.Model.Script).ToBytes());

            // the export folds the renames into the shipped file's format
            string export = Path.Combine(Path.GetTempPath(), "uwnames-export-" + Guid.NewGuid().ToString("N") + ".json");
            Assert.True(cw.ExportNames(export));
            var file = NameFile.Parse(File.ReadAllText(export));
            Assert.Equal("corby_greets", file.Slots[1].Functions[target.Address].Name);
            File.Delete(export);

            // a new window remembers them; an empty name goes back to the shipped one
            cw.Close();
            var cw2 = w.ShowConversations(1);
            Ui.Pump();
            Assert.Equal("corby_greets", cw2.Names.Function(target.Address));
            Assert.Null(cw2.Rename(target, ""));
            Assert.Null(cw2.Rename(NameTarget.Label(label), ""));
            Assert.Equal(shipped, cw2.Names.Function(target.Address));
            cw2.Close();
            sim.Close();
            w.Close();
        }
    }
}
