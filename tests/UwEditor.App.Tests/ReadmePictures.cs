using System;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using UwEditor.Core.Tests;
using Xunit;

namespace UwEditor.App.Tests
{
    /// <summary>
    /// The README's pictures of the tool windows, drawn headless from your own game files. Off
    /// unless UW_README_PICTURES names the folder to write them to (docs/images): each test then
    /// opens a window in a telling state and saves it as a PNG. Nothing is compared.
    /// </summary>
    public class ReadmePictures
    {
        static string Out { get { return Environment.GetEnvironmentVariable("UW_README_PICTURES"); } }
        static string Uw1 { get { return GameData.Game("UW"); } }
        static string Uw2 { get { return GameData.Game("UW2"); } }

        static bool Off(string game) { return string.IsNullOrEmpty(Out) || game == null; }

        static void Save(Window win, string name, int width, int height, Control focus = null)
        {
            win.Width = width; win.Height = height;
            Ui.Pump(); Ui.Pump();
            Directory.CreateDirectory(Out);
            new Ui.Codec().Save(Ui.Capture(win, focus), Path.Combine(Out, name + ".png"));
        }

        static void WaitFor(Task t)
        {
            var until = DateTime.UtcNow.AddSeconds(120);
            while (t != null && !t.IsCompleted && DateTime.UtcNow < until) { Ui.Pump(); System.Threading.Thread.Sleep(20); }
            Ui.Pump();
        }

        [AvaloniaFact]
        public async Task Find_in_all_levels()
        {
            if (Off(Uw1)) return;
            var w = Ui.Open(Uw1);
            var find = w.ShowFind();
            find.Model.Text = "sword";
            await find.SearchAsync();
            Save(find, "find", 1100, 700, Ui.Find<ListBox>(find, "Results"));
            find.Close(); w.Close();
        }

        [AvaloniaFact]
        public void Conversations()
        {
            if (Off(Uw1)) return;
            var w = Ui.Open(Uw1);
            var cw = w.ShowConversations(1);
            Save(cw, "conversations", 1280, 820);
            var gw = w.ShowConversationGraph(1);
            Ui.Pump();
            Save(gw, "conversation-graph", 1300, 860);
            var sw = w.ShowConversationSimulator(1);
            Ui.Pump();
            Save(sw, "conversation-simulator", 1180, 820);
            sw.Close(); gw.Close(); cw.Close(); w.Close();
        }

        [AvaloniaFact]
        public void Globals()
        {
            if (Off(Uw2) || !Directory.Exists(Path.Combine(Uw2, "SAVE1"))) return;
            var w = Ui.Open(Path.Combine(Uw2, "SAVE1"));
            var gw = w.ShowGlobals();
            WaitFor(gw.Loading);
            Save(gw, "globals", 1200, 780);
            gw.Close(); w.Close();
        }

        [AvaloniaFact]
        public void Player()
        {
            if (Off(Uw2) || !Directory.Exists(Path.Combine(Uw2, "SAVE1"))) return;
            var w = Ui.Open(Path.Combine(Uw2, "SAVE1"));
            var pw = w.ShowPlayer();
            Save(pw, "player", 1180, 800);
            pw.ShowTab(1);
            var m = pw.Model;
            m.Selected = m.Items.First(n => n.Item != null && n.Item.ItemId == 0x80);
            m.NewItem = 0xA0; m.Add();
            m.NewItem = 0x13C; m.Add();
            Save(pw, "player-inventory", 1180, 800, Ui.Find<TreeView>(pw, "ItemTree"));
            pw.Close(); w.Close();
        }

        [AvaloniaFact]
        public void Graphics_and_pixels()
        {
            if (Off(Uw1)) return;
            var w = Ui.Open(Uw1);
            w.AskBeforeDiscarding = false;
            var gw = w.ShowGraphics();
            gw.ShowTab(1);
            Save(gw, "graphics-critters", 1280, 820);
            gw.ShowTab(2);
            var m = gw.Model;
            // a cutscene animation with a picture in the middle of it
            foreach (var c in m.Cutscenes.ToList())
            {
                m.CutsceneItem = c;
                if (m.Lpf == null || m.Lpf.FrameCount < 20) continue;
                m.CutFrame = m.Lpf.FrameCount / 2;
                break;
            }
            Save(gw, "graphics-cutscenes", 1280, 820);
            gw.Close();
            var pe = w.EditItemPicture(0x0A);
            pe.AskOnClose = false;
            Save(pe, "pixel-editor", 1100, 760);
            pe.Close(); w.Close();
        }

        [AvaloniaFact]
        public void Models()
        {
            if (Off(Uw1)) return;
            var w = Ui.Open(Uw1);
            var mw = w.ShowModels();
            Save(mw, "models", 1100, 720);
            mw.Close(); w.Close();
        }

        [AvaloniaFact]
        public void Keys_triggers_strings_mods()
        {
            if (Off(Uw1)) return;
            var w = Ui.Open(Uw1);
            var kw = w.ShowKeys();
            WaitFor(kw.Loading);
            Save(kw, "keys", 1150, 720);
            kw.Close();
            var tw = w.ShowTriggers();
            Save(tw, "triggers", 1150, 720);
            tw.Close();
            var sw = w.ShowStrings(3, 0);
            Save(sw, "strings", 1150, 720);
            sw.Close();
            var mw = w.ShowMods();
            Save(mw, "mods", 1000, 680);
            mw.Close(); w.Close();
        }

        [AvaloniaFact]
        public void Tables_and_slots()
        {
            if (Off(Uw1)) return;
            var w = Ui.Open(Uw1);
            var ot = w.ShowObjectTables();
            Save(ot, "object-tables", 1280, 780);
            ot.Close();
            var slots = w.ShowItemSlots(0x04);
            WaitFor(slots.Loading);
            slots.ShowItem(0x04);
            Save(slots, "item-slots", 1250, 800);
            slots.Close(); w.Close();
        }

        [AvaloniaFact]
        public void Sound()
        {
            if (Off(Uw2)) return;
            var w = Ui.Open(Uw2);
            var sw = w.ShowSound();
            sw.Tabs.SelectedIndex = 2;
            Save(sw, "sound", 1100, 700);
            sw.Close(); w.Close();
        }

        [AvaloniaFact]
        public void Level_tools()
        {
            if (Off(Uw1)) return;
            var w = Ui.Open(Uw1);
            w.AskBeforeDiscarding = false;
            var rw = w.ShowTrapRecipes();
            rw.ShowRecipe(1);
            Save(rw, "trap-recipes", 1100, 760);
            rw.Close();
            var am = w.ShowAutomap();
            Save(am, "automap", 1100, 800);
            am.Close(); w.Close();
        }

        [AvaloniaFact]
        public void Randomizer()
        {
            if (Off(Uw1)) return;
            var w = Ui.Open(Uw1);
            w.AskBeforeDiscarding = false;
            var z = w.Zoning();
            z.LoadExample();
            var zw = w.ShowZones();
            Save(zw, "zones", 1000, 820);
            var g = w.ShowZoneGraph();
            Ui.Pump();
            Save(g, "zone-graph", 1300, 880);
            g.Close(); zw.Close();
            var aw = w.ShowAutoZones();
            WaitFor(aw.BuildAsync());
            Save(aw, "autozones", 1150, 820);
            aw.Close();
            var kw = w.ShowKeyShuffle();
            kw.PreviewNow();
            Save(kw, "key-scatter", 1100, 820);
            kw.Close();
            var sh = w.ShowItemShuffle();
            Save(sh, "item-shuffle", 1280, 860);
            sh.Close();
            var ms = w.ShowMonsterSwap();
            Save(ms, "monster-swap", 1280, 860);
            ms.Close();
            var isw = w.ShowItemSwap();
            Save(isw, "item-swap", 1280, 860);
            isw.Close();
            var cw = w.ShowChallenges();
            foreach (var b in UwEditor.Core.Randomizer.Challenges.All) cw.Box(b.Id).IsChecked = false;
            cw.Box(UwEditor.Core.Randomizer.Challenge.Monk).IsChecked = true;
            cw.Box(UwEditor.Core.Randomizer.Challenge.BonesRise).IsChecked = true;
            cw.Preview();
            Save(cw, "challenges", 1100, 780);
            cw.Close(); w.Close();
        }
    }
}
