using System;
using System.Linq;
using Avalonia.Interactivity;
using UwEditor.Core;
using UwEditor.Core.Editing;

namespace UwEditor.App
{
    /// <summary>The main window's tool windows for keys, triggers, strings and mods.</summary>
    public partial class MainWindow
    {
        KeysWindow keysWindow;
        TriggersWindow triggersWindow;
        StringsWindow stringsWindow;
        ModsWindow modsWindow;
        StringsEditor strings;

        void OnKeys(object sender, RoutedEventArgs e) { ShowKeys(); }
        void OnTriggers(object sender, RoutedEventArgs e) { ShowTriggers(); }
        void OnStrings(object sender, RoutedEventArgs e) { ShowStrings(); }
        void OnMods(object sender, RoutedEventArgs e) { ShowMods(); }

        /// <summary>STRINGS.PAK with its unsaved edits, shared by the keys and strings windows.</summary>
        public StringsEditor Strings()
        {
            if (vm.Session == null) return null;
            if (strings == null || strings.Session != vm.Session)
            {
                UwEditor.Core.Conversations.StringsPakFile shared = null;
                try { shared = Conversations()?.Strings; } catch (Exception) { shared = null; }
                try { strings = StringsEditor.Open(vm.Session, shared); }
                catch (Exception ex) { _ = MessageWindow.Show(this, "No strings", ex.Message); return null; }
                strings.Saved += () => { vm.Session.ReloadStrings(); vm.RefreshNames(); };
            }
            return strings;
        }

        public KeysWindow ShowKeys()
        {
            var st = Strings();
            if (st == null) return null;
            if (keysWindow != null) { keysWindow.Activate(); return keysWindow; }
            keysWindow = new KeysWindow(vm, st, GoToPlace);
            keysWindow.Closed += (s, e) => keysWindow = null;
            keysWindow.Show(this);
            return keysWindow;
        }

        public TriggersWindow ShowTriggers()
        {
            if (vm.Session == null || vm.Level == null) return null;
            if (triggersWindow != null) { triggersWindow.Activate(); triggersWindow.Refresh(); return triggersWindow; }
            triggersWindow = new TriggersWindow(vm, (level, index) => GoToObject(level, index), n => { var g = ShowGlobals(); if (g != null) _ = SelectGlobal(g, n.Item1, n.Item2); });
            triggersWindow.Closed += (s, e) => triggersWindow = null;
            triggersWindow.Show(this);
            return triggersWindow;
        }

        public StringsWindow ShowStrings(int block = -1, int index = -1)
        {
            var st = Strings();
            if (st == null) return null;
            if (stringsWindow == null)
            {
                stringsWindow = new StringsWindow(st);
                stringsWindow.Closed += (s, e) => stringsWindow = null;
                stringsWindow.Show(this);
            }
            else stringsWindow.Activate();
            if (block >= 0) stringsWindow.Model.Show(block, index);
            return stringsWindow;
        }

        public ModsWindow ShowMods()
        {
            if (vm.Session == null) return null;
            if (modsWindow != null) { modsWindow.Activate(); return modsWindow; }
            modsWindow = new ModsWindow(vm.Session, () => HasUnsaved(), path => OpenPathNow(path));
            modsWindow.Closed += (s, e) => modsWindow = null;
            modsWindow.Show(this);
            return modsWindow;
        }

        /// <summary>For scripted screenshots (--window).</summary>
        void OpenWindowAtStart(string name)
        {
            switch (name)
            {
                case "keys": ShowKeys(); break;
                case "strings": ShowStrings(4, 0); break;
                case "triggers": ShowTriggers(); break;
                case "mods": ShowMods(); break;
                case "add":
                    var w = new AddObjectWindow(vm.Session, vm.IconOf, false, "On the selected tile.");
                    if (Program.ItemAtStart >= 0) { w.Model.Filter = "0x" + Program.ItemAtStart.ToString("X3"); w.Model.Selected = w.Model.Items.FirstOrDefault(i => i.Item == Program.ItemAtStart); }
                    w.Show(this);
                    break;
            }
        }

        void GoToPlace(KeyPlace p) { GoToObject(p.Level, p.Holder >= 0 ? p.Index : p.Index); }

        /// <summary>Opens a level, selects an object and frames it.</summary>
        public void GoToObject(int level, int index)
        {
            if (vm.Session == null) return;
            if (vm.Level == null || vm.Level.Level != level) vm.LoadLevel(level);
            if (vm.Level == null || index < 0 || index >= vm.Level.Objects.Length) return;
            var o = vm.Level.Objects[index];
            vm.Select(o.Index, o.TileX, o.TileY, true);
            if (vm.SelectedNode != null) ObjectTree.ScrollIntoView(vm.SelectedNode);
            Frame();
            Activate();
        }
    }
}
