using System;
using System.IO;
using System.Linq;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using UwEditor.Core.Conversations;
using UwEditor.Core.Tests;
using UwEditor.TestSupport;
using Xunit;

namespace UwEditor.App.Tests
{
    /// <summary>The conversation simulator and graph windows, driven without a screen.</summary>
    public class ConversationSimulatorWindowTests
    {
        static string Uw1 { get { return GameData.Game("UW"); } }
        static string Uw2 { get { return GameData.Game("UW2"); } }

        /// <summary>Where a test may leave a picture of a window (UWEDITOR_SHOTS), or null.</summary>
        static void Shot(Window w, string name)
        {
            string dir = Environment.GetEnvironmentVariable("UWEDITOR_SHOTS");
            if (string.IsNullOrEmpty(dir)) return;
            Directory.CreateDirectory(dir);
            new Ui.Codec().Save(Ui.Capture(w), Path.Combine(dir, name + ".png"));
        }

        [AvaloniaFact]
        public void Simulator_runs_Corby_shows_the_menu_as_buttons_and_takes_a_choice()
        {
            if (Uw1 == null) return;
            string cnv = Path.Combine(Uw1, "DATA", "CNV.ARK");
            var before = File.GetLastWriteTimeUtc(cnv);
            var w = Ui.Open(Uw1);
            var sw = w.ShowConversationSimulator(1);
            sw.Width = 1180; sw.Height = 820;
            Ui.Pump();
            Assert.NotNull(sw.Simulator);
            Assert.Equal(SimState.Running, sw.Simulator.State);
            Assert.Empty(sw.Transcript);
            sw.Step();
            Assert.Equal(1, sw.Simulator.Steps);
            sw.Run();
            Ui.Pump();
            Assert.Equal(SimState.WaitingMenu, sw.Simulator.State);
            Assert.Equal("Hast thou come to add to my torment?", sw.Transcript[0].Text);
            var buttons = sw.ChoiceButtons.ToList();
            Assert.Equal(4, buttons.Count);
            Shot(sw, "simulator-corby");
            Ui.Click(sw, buttons[0]);
            Assert.Equal("Of what torment dost thou speak?", sw.Transcript[1].Text);
            Assert.Equal("You", sw.Transcript[1].Who);
            Assert.StartsWith("Why, the torment of lost hopes", sw.Transcript[2].Text);
            Assert.Equal(SimState.WaitingMenu, sw.Simulator.State);
            // the variables: memory first, editable
            var g0 = sw.Variables.First(r => r.Kind.StartsWith("Private"));
            g0.Value = "7";
            Assert.Equal(7, sw.Simulator.GetMem(0));
            Assert.Contains(sw.LogLines, l => l.Contains("babl_menu("));
            Assert.Contains(sw.StartValues, r => r.Name == "npc_attitude");
            // back to the start
            sw.Reset();
            Assert.Empty(sw.Transcript);
            // run along the first answers to the end
            sw.Run();
            for (int i = 0; i < 40 && sw.Simulator.State == SimState.WaitingMenu; i++) sw.Choose(sw.Simulator.Menu.Ids.Count);
            Assert.Equal(SimState.Ended, sw.Simulator.State);
            Assert.Equal(before, File.GetLastWriteTimeUtc(cnv));
            Assert.False(w.Conversations().HasChanges);
            sw.Close();
            w.Close();
        }

        [AvaloniaFact]
        public void Simulator_of_a_saved_game_takes_the_player_from_PLAYER_DAT_and_asks_for_typed_answers()
        {
            if (Uw2 == null) return;
            var w = Ui.Open(Path.Combine(Uw2, "SAVE1"));
            var sw = w.ShowConversationSimulator(1);
            Ui.Pump();
            var name = UwEditor.Core.Player.PlayerSheet.Load(Path.Combine(Uw2, "SAVE1", "PLAYER.DAT"), UltimateEditor.Games.GameKind.UW2, Path.Combine(Uw2, "DATA")).Name;
            Assert.Equal(name, sw.StartWorld.PlayerName);
            sw.Run();
            // the goblin guards: "I know it" leads to babl_ask for the password
            Assert.Equal(SimState.WaitingMenu, sw.Simulator.State);
            sw.Choose(2);
            Assert.Equal(SimState.WaitingMenu, sw.Simulator.State);
            sw.Choose(1);
            if (sw.Simulator.State == SimState.WaitingMenu) sw.Choose(1);
            Assert.Equal(SimState.WaitingAsk, sw.Simulator.State);
            Assert.True(sw.AskBox.IsVisible);
            sw.Answer("swordfish");
            Assert.Contains(sw.Transcript, r => r.Who == "You type" && r.Text == "swordfish");
            Assert.Contains(sw.Transcript, r => r.Who == "NPC" && r.Text == "swordfish");
            sw.Close();
            w.Close();
        }

        [AvaloniaFact]
        public void Graph_shows_Corby_a_click_selects_the_script_line_and_editing_a_node_edits_the_string()
        {
            if (Uw1 == null) return;
            var w = Ui.Open(Uw1);
            var gw = w.ShowConversationGraph(1);
            gw.Width = 1300; gw.Height = 860;
            Ui.Pump();
            var cw = w.ShowConversations();
            var g = gw.Graph;
            Assert.NotNull(g);
            Assert.True(g.Visible.Count() > 10);
            var menu = g.Visible.First(n => n.Kind == GraphNodeKind.Menu && n.Lines.Any(l => l.StringId == 1));
            gw.Canvas.Select(menu, true);
            Ui.Pump();
            var p = gw.Canvas.PointOf(menu);
            Ui.ClickAt(gw, gw.Canvas, p.X, p.Y);
            Assert.Same(menu, gw.Canvas.Selected);
            Assert.Equal(2, cw.Model.Tab);
            var script = Ui.Find<TextBox>(cw, "ScriptBox");
            string selected = cw.Model.Script.Substring(script.SelectionStart, Math.Max(0, script.SelectionEnd - script.SelectionStart));
            Assert.Contains("; " + menu.Address.ToString("X4"), selected);
            Shot(gw, "graph-corby");
            // the node's strings beside the graph edit the conversation's strings
            var box = gw.StringBoxes.First(b => (int)b.Tag == 1);
            box.Text = "Hast thou come to add to my misery?";
            Ui.Pump();
            Assert.True(cw.Library.Strings.IsChanged(cw.Current.StringBlock));
            Assert.Equal("Hast thou come to add to my misery?", cw.CurrentStrings[1]);
            Assert.True(cw.Model.Strings[1].Changed);
            Assert.True(cw.Model.Selected.Changed);
            Assert.Contains(gw.Graph.Visible, n => n.Lines.Any(l => l.StringId == 1));
            // the editor's revert undoes it
            cw.RevertCurrent();
            Ui.Pump();
            Assert.False(cw.Library.Strings.IsChanged(cw.Current.StringBlock));
            Assert.Equal("Hast thou come to add to my torment?", cw.CurrentStrings[1]);
            // another conversation: the graph follows
            cw.SelectSlot(2);
            Ui.Pump();
            Assert.Contains("conversation 2", gw.Title + " conversation " + cw.Current.Slot);
            Assert.True(gw.Graph.Visible.Any());
            gw.Canvas.Fit();
            Shot(gw, "graph-shak-fit");
            gw.Canvas.Home();
            Shot(gw, "graph-shak-start");
            gw.Close();
            w.Close();
        }
    }
}
