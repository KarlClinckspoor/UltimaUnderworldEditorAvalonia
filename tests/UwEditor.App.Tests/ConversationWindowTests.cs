using System;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using UwEditor.Core.Conversations;
using UwEditor.Core.Tests;
using UwEditor.TestSupport;
using Xunit;

namespace UwEditor.App.Tests
{
    /// <summary>The conversation editor and the global variables window, driven without a screen.
    /// Tests that save work on a copy of the game in a temporary folder.</summary>
    public class ConversationWindowTests
    {
        static string Uw1 { get { return GameData.Game("UW"); } }
        static string Uw2 { get { return GameData.Game("UW2"); } }

        /// <summary>A copy of a game folder (DATA, the saves and the executable), deleted by Dispose.</summary>
        sealed class GameCopy : IDisposable
        {
            public readonly string Folder = Path.Combine(Path.GetTempPath(), "uwedit-test-" + Guid.NewGuid().ToString("N"));
            public GameCopy(string from)
            {
                foreach (var dir in Directory.GetDirectories(from))
                {
                    string name = Path.GetFileName(dir);
                    if (name != "DATA" && !name.StartsWith("SAVE")) continue;
                    Directory.CreateDirectory(Path.Combine(Folder, name));
                    foreach (var f in Directory.GetFiles(dir)) File.Copy(f, Path.Combine(Folder, name, Path.GetFileName(f)));
                }
                foreach (var f in Directory.GetFiles(from, "*.EXE")) File.Copy(f, Path.Combine(Folder, Path.GetFileName(f)));
            }
            public void Dispose() { try { Directory.Delete(Folder, true); } catch (Exception) { } }
        }

        static async Task Until(Func<bool> done, int seconds = 30)
        {
            var end = DateTime.Now.AddSeconds(seconds);
            while (!done() && DateTime.Now < end) { await Task.Delay(50); Ui.Pump(); }
            Assert.True(done(), "timed out");
        }

        [AvaloniaFact]
        public async Task Conversation_window_shows_what_Corby_says_and_who_talks()
        {
            if (Uw1 == null) return;
            var w = Ui.Open(Uw1);
            var cw = w.ShowConversations(1);
            Ui.Pump();
            Assert.Equal(97, cw.Model.All.Count);
            Assert.Equal(1, cw.Model.Selected.Slot);
            Assert.Contains("Corby", cw.Model.Header);
            Assert.Contains(cw.Model.Lines, l => l.Line.Kind == PseudoKind.Say && l.Text.Contains("Hast thou come to add to my torment?"));
            // the structured view: a menu block with each choice and what it does
            Assert.Contains(cw.Model.Lines, l => l.Line.Kind == PseudoKind.Menu && l.Text.Trim() == "menu {");
            Assert.Contains(cw.Model.Lines, l => l.Line.Kind == PseudoKind.Menu && l.Text.Trim() == "\"Of what torment dost thou speak?\" #2:");
            Assert.DoesNotContain(cw.Model.Lines, l => l.Text.Contains("goto "));
            // the compiler's library functions are hidden until asked for
            Assert.DoesNotContain(cw.Model.Lines, l => l.Text.Contains("never called"));
            Ui.Find<CheckBox>(cw, "HideUnusedBox").IsChecked = false;
            Ui.Pump();
            Assert.Contains(cw.Model.Lines, l => l.Text.Contains("never called"));
            Assert.Equal(47, cw.Model.Strings.Count);
            Assert.Contains("said at 02B5", cw.Model.Strings[1].Uses);
            await Until(() => cw.Model.Users.Any(u => u.User != null));
            Assert.Single(cw.Model.Users);
            Assert.Contains("1 creature", cw.Model.Selected.Detail);
            // the filter finds a conversation by its text
            cw.Model.Filter = "mewling";
            Assert.Single(cw.Model.Rows);
            cw.Close();
            w.Close();
        }

        [AvaloniaFact]
        public void A_dialogue_line_opens_the_script_at_its_address_and_a_broken_script_points_at_the_line()
        {
            if (Uw1 == null) return;
            var w = Ui.Open(Uw1);
            var cw = w.ShowConversations(1);
            Ui.Pump();
            var say = cw.Model.Lines.First(l => l.Line.Kind == PseudoKind.Say);
            cw.ShowAddress(say.Line.Address);
            Ui.Pump();
            Assert.Equal(2, cw.Model.Tab);
            var box = Ui.Find<TextBox>(cw, "ScriptBox");
            Assert.Contains("SAY", box.SelectedText);
            // a typo: the error names its line, and nothing is applied
            cw.Model.Script = cw.Model.Script.Replace("    SAY ", "    SAYY ");
            Assert.False(cw.ApplyScript());
            Assert.Contains("unknown instruction 'SAYY'", cw.Model.ScriptError);
            Assert.False(cw.Library.Archive.HasChanges);
            cw.Close();
            w.Close();
        }

        [AvaloniaFact]
        public void A_menu_choice_opens_the_script_at_its_compare_and_a_renamed_local_shows_in_the_structured_view()
        {
            if (Uw1 == null) return;
            var w = Ui.Open(Uw1);
            var cw = w.ShowConversations(1);
            Ui.Pump();
            // a choice's line is the compare that picks it: double-click shows that instruction
            var choice = cw.Model.Lines.First(l => l.Line.Kind == PseudoKind.Menu && l.Text.Trim().StartsWith("\"Of what torment"));
            Assert.True(choice.Line.Start >= 0 && choice.Line.End > choice.Line.Address);
            cw.ShowAddress(choice.Line.Address);
            Ui.Pump();
            Assert.Contains("BEQ", Ui.Find<TextBox>(cw, "ScriptBox").SelectedText);
            // F2 on a local: npc_not_hostile's result, shown in its declaration
            Ui.Find<CheckBox>(cw, "HideUnusedBox").IsChecked = false;
            Ui.Pump();
            var fn = cw.Model.Lines.First(l => l.Line.Kind == PseudoKind.Function && l.Text.StartsWith("npc_not_hostile()"));
            var target = NameTarget.Local(fn.Line.Address, 1);
            Assert.Null(cw.Rename(target, "not_hostile"));
            Ui.Pump();
            Assert.Contains(cw.Model.Lines, l => l.Text.Trim() == "int not_hostile;");
            Assert.Null(cw.Rename(target, ""));
            Ui.Pump();
            Assert.Contains(cw.Model.Lines, l => l.Text.Trim() == "int local1;");
            cw.Close();
            w.Close();
        }

        [AvaloniaFact]
        public void Editing_a_string_and_the_script_then_saving_writes_both_files()
        {
            if (Uw1 == null) return;
            using (var copy = new GameCopy(Uw1))
            {
                var w = Ui.Open(copy.Folder);
                var cw = w.ShowConversations(1);
                Ui.Pump();
                cw.Model.Strings[1].Text = "Hast thou come to add to my woes?";
                Assert.True(cw.Model.Strings[1].Changed);
                Assert.True(cw.Model.Selected.Changed);
                var added = cw.AddString("A brand new line.");
                Assert.Equal(47, added.Index);
                // say the new string right after the first say
                cw.Model.Tab = 2;
                int at = cw.Model.Script.IndexOf("    SAY ", StringComparison.Ordinal);
                int lineEnd = cw.Model.Script.IndexOf('\n', at);
                cw.Model.Script = cw.Model.Script.Insert(lineEnd + 1, "    PUSHI 47\n    SAY\n");
                Assert.True(cw.ApplyScript(), cw.Model.ScriptError);
                cw.Model.Tab = 0;
                Ui.Pump();
                Assert.Contains(cw.Model.Lines, l => l.Text.Contains("say \"Hast thou come to add to my woes?\""));
                Assert.Contains(cw.Model.Lines, l => l.Text.Contains("say \"A brand new line.\" #47"));
                var done = cw.Save();
                Assert.NotNull(done);
                Assert.Equal(2, done.Count);
                Assert.True(File.Exists(Path.Combine(copy.Folder, "DATA", "CNV.ARK.BKP")));
                Assert.True(File.Exists(Path.Combine(copy.Folder, "DATA", "STRINGS.PAK.BKP")));
                cw.Close();
                w.Close();

                // read back from the files
                var s = Core.GameSession.Open(copy.Folder);
                var lib = ConversationLibrary.Open(s);
                var c = lib.Archive.Read(1);
                var strs = lib.StringsOf(c);
                Assert.Equal("Hast thou come to add to my woes?", strs[1]);
                Assert.Equal("A brand new line.", strs[47]);
                Assert.Contains(lib.Decompile(c).Lines, l => l.Kind == PseudoKind.Say && l.Text.Contains("#47"));
                // every other conversation is as it was
                var orig = ConversationLibrary.Open(Core.GameSession.Open(Uw1));
                foreach (int slot in orig.Archive.Slots().Where(x => x != 1))
                    Assert.Equal(orig.Archive.RawBlock(slot), lib.Archive.RawBlock(slot));
            }
        }

        [AvaloniaFact]
        public async Task Global_variables_window_lists_quests_with_their_conversations_and_is_read_only_for_DATA()
        {
            if (Uw1 == null) return;
            var w = Ui.Open(Uw1);
            var g = w.ShowGlobals();
            await g.Loading;
            Ui.Pump();
            Assert.False(g.Globals.CanEdit);
            Assert.False(Ui.Find<Button>(g, "SaveButton").IsEnabled);
            Assert.True(g.Model.OnlyUsed);
            Assert.All(g.Model.Rows, r => Assert.NotEmpty(r.Variable.Uses));
            Assert.True(g.SelectVariable(GlobalKind.Quest, 0));
            Assert.Contains("Murgo", g.Model.Detail);
            Assert.Contains(g.Model.Uses, u => u.Use != null && u.Text.Contains("sets"));
            // kinds narrow the list
            g.Model.KindIndex = 1 + (int)GlobalKind.Private;
            Assert.All(g.Model.Rows, r => Assert.Equal(GlobalKind.Private, r.Variable.Kind));
            Assert.NotEmpty(g.Model.Rows);
            // a value cannot be changed in DATA
            var row = g.Model.Rows[0];
            row.Value = "7";
            Assert.True(row.HasError);
            // a use opens its conversation at that line
            Assert.True(g.SelectVariable(GlobalKind.Quest, 0));
            var use = g.Model.Uses.First(u => u.Use != null);
            var cw = w.ShowConversations(use.Use.Slot, use.Use.Address);
            Ui.Pump();
            Assert.Equal(use.Use.Slot, cw.Model.Selected.Slot);
            Assert.Equal(2, cw.Model.Tab);
            cw.Close();
            g.Close();
            w.Close();
        }

        [AvaloniaFact]
        public async Task Changing_values_in_a_saved_game_writes_player_dat()
        {
            if (Uw2 == null || !Directory.Exists(Path.Combine(Uw2, "SAVE1"))) return;
            using (var copy = new GameCopy(Uw2))
            {
                var w = Ui.Open(Path.Combine(copy.Folder, "SAVE1"));
                var g = w.ShowGlobals();
                await g.Loading;
                Ui.Pump();
                Assert.True(g.Globals.CanEdit);
                Assert.True(g.SelectVariable(GlobalKind.XClock, 1));
                g.Model.Selected.Value = "3";
                Assert.False(g.Model.Selected.HasError, g.Model.Selected.Error);
                g.Model.Selected.Value = "999";
                Assert.True(g.Model.Selected.HasError);
                g.Model.Selected.Value = "4";
                var done = g.Save();
                Assert.Contains(done, p => p.EndsWith("PLAYER.DAT"));
                g.Close();
                w.Close();
                var again = GlobalVariables.Load(Core.GameSession.Open(Path.Combine(copy.Folder, "SAVE1")), null, null);
                Assert.Equal(4, again.Find(GlobalKind.XClock, 1).Value);
            }
        }

        // ---- pictures

        [AvaloniaFact]
        public void Snapshot_of_the_conversation_window()
        {
            if (Uw1 == null) return;
            var w = Ui.Open(Uw1);
            var cw = w.ShowConversations(1);
            cw.Width = 1280; cw.Height = 820;
            Ui.Pump();
            var frame = Ui.Capture(cw, Ui.Find<ListBox>(cw, "List"));
            Assert.True(frame.DistinctColours() > 20);
            Expect(Snapshots.Check(Ui.BaselineDir, "conversation-window", frame, new Ui.Codec()));
            cw.Close();
            w.Close();
        }

        [AvaloniaFact]
        public async Task Snapshot_of_the_global_variables_window()
        {
            if (Uw1 == null) return;
            var w = Ui.Open(Uw1);
            var g = w.ShowGlobals();
            g.Width = 1180; g.Height = 760;
            await g.Loading;
            g.SelectVariable(GlobalKind.Quest, 0);
            Ui.Pump();
            var frame = Ui.Capture(g, Ui.Find<ListBox>(g, "UseList"));
            Expect(Snapshots.Check(Ui.BaselineDir, "globals-window", frame, new Ui.Codec()));
            g.Close();
            w.Close();
        }

        static void Expect(SnapshotResult r)
        {
            if (r.Outcome == SnapshotOutcome.Recorded) Assert.Skip(r.Message);
            Assert.True(r.Outcome == SnapshotOutcome.Matched, r.Message);
        }
    }
}
