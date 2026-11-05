using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using UwEditor.Core;
using UwEditor.Core.Conversations;

namespace UwEditor.App
{
    /// <summary>
    /// Tools ▸ Global variables: the game state conversations reach — quest flags and bytes,
    /// game variables, X clocks, skills, the talker's and player's variables and each
    /// conversation's own — with the values in the open save, and which conversations read
    /// and write each. Values can be changed in a saved game.
    /// </summary>
    public partial class GlobalsWindow : Window
    {
        public readonly GlobalsViewModel Model = new GlobalsViewModel();
        readonly ConversationLibrary lib;
        readonly Action<int, int> openConversation;
        GlobalVariables globals;

        public GlobalVariables Globals { get { return globals; } }
        /// <summary>Finished reading (for tests).</summary>
        public Task Loading { get; private set; }

        public GlobalsWindow() : this(null, null) { }

        readonly UwEditor.Core.Player.PlayerSheet sheet;

        public GlobalsWindow(ConversationLibrary library, Action<int, int> openConversation, UwEditor.Core.Player.PlayerSheet sheet = null)
        {
            lib = library;
            this.sheet = sheet;
            this.openConversation = openConversation;
            InitializeComponent();
            ManualWindow.Attach(this, "Global variables");
            DataContext = Model;
            Model.SelectedChanged += ShowSelected;
            AddHandler(KeyDownEvent, (s, e) =>
            {
                if ((e.KeyModifiers.HasFlag(KeyModifiers.Control) || e.KeyModifiers.HasFlag(KeyModifiers.Meta)) && e.Key == Key.S) { Save(); e.Handled = true; }
            }, RoutingStrategies.Tunnel);
            Loading = lib == null ? Task.CompletedTask : LoadAsync();
        }

        async Task LoadAsync()
        {
            Model.Status = "Reading every conversation…";
            var session = lib.Session;
            GlobalVariables g;
            try
            {
                g = await Task.Run(() =>
                {
                    var uses = GlobalUsage.Collect(lib.Archive, null, lib.Kind);
                    return GlobalVariables.Load(session, lib.Archive, uses, lib.Name, sheet);
                });
            }
            catch (Exception ex) { Model.Status = "Could not read the variables: " + ex.Message; return; }
            Show(g);
        }

        void Show(GlobalVariables g)
        {
            globals = g;
            var s = lib.Session;
            Title = "Global variables — " + s.Title;
            SourceText.Text = !s.IsSave ? "DATA is open: in a new game these all start at 0. Open a SAVE folder to see and change a saved game's values."
                : g.Player == null ? Path.GetFileName(s.SaveFolder) + " has no readable PLAYER.DAT."
                : "Values from " + Path.GetFileName(s.SaveFolder) + "'s PLAYER.DAT and BGLOBALS.DAT. Edit them, then click Save values.";
            SaveButton.IsEnabled = g.CanEdit;
            Model.All.Clear();
            foreach (var v in g.All)
            {
                var row = new GlobalRow
                {
                    Variable = v,
                    ReadBy = Slots(v.Readers),
                    WrittenBy = Slots(v.Writers),
                };
                row.SetQuietly(v.Value.HasValue ? v.Value.Value.ToString(CultureInfo.InvariantCulture) : "");
                row.Edited += OnValueEdited;
                Model.All.Add(row);
            }
            Model.Apply();
            int used = g.All.Count(v => v.Uses.Count > 0);
            Model.Status = string.Format("Conversations use {0} of the {1} globals. Hover over a row to see where it is kept.", used, g.All.Count);
        }

        static string Slots(IEnumerable<int> slots)
        {
            var l = slots.ToList();
            if (l.Count == 0) return "";
            return (l.Count > 6 ? string.Join(", ", l.Take(6)) + " +" + (l.Count - 6) : string.Join(", ", l));
        }

        void OnValueEdited(GlobalRow row)
        {
            int v;
            string t = (row.Value ?? "").Trim();
            bool ok = t.StartsWith("0x", StringComparison.OrdinalIgnoreCase)
                ? int.TryParse(t.Substring(2), NumberStyles.HexNumber, CultureInfo.InvariantCulture, out v)
                : int.TryParse(t, NumberStyles.Integer, CultureInfo.InvariantCulture, out v);
            if (!ok) { row.Error = "Not a number."; Model.Status = row.Variable + ": '" + t + "' is not a number."; return; }
            string err = globals.Set(row.Variable, v);
            row.Error = err;
            Model.Status = err ?? string.Format("{0} = {1} (not saved).", row.Variable, v);
        }

        /// <summary>Selects a global by kind and number (and slot, for a private one).</summary>
        public bool SelectVariable(GlobalKind kind, int number, int slot = -1)
        {
            var row = Model.All.FirstOrDefault(r => r.Variable.Kind == kind && r.Variable.Number == number && (kind != GlobalKind.Private || r.Variable.Slot == slot));
            if (row == null) return false;
            if (!Model.Rows.Contains(row)) { Model.Filter = ""; Model.KindIndex = 0; Model.OnlyUsed = false; Model.OnlySet = false; }
            Model.Selected = row;
            VarList.ScrollIntoView(row);
            return true;
        }

        /// <summary>Shows only one conversation's globals (its private ones and the shared ones it uses).</summary>
        public void ShowConversation(int slot)
        {
            if (slot < 0) return;
            Model.Filter = "";
            Model.KindIndex = 0;
            Model.OnlyUsed = true;
            Model.Rows.Clear();
            foreach (var r in Model.All)
                if (r.Variable.Uses.Any(u => u.Slot == slot)) Model.Rows.Add(r);
            Model.Status = string.Format("Showing the {0} globals that conversation {1} uses. Change the filter to see the others.", Model.Rows.Count, slot);
        }

        void ShowSelected()
        {
            Model.Uses.Clear();
            var r = Model.Selected;
            if (r == null) { Model.Detail = ""; return; }
            var v = r.Variable;
            Model.Detail = v.KindName + " " + v.Label + "\n" + v.Description + (v.Value.HasValue ? "\nValue: " + v.Value : "");
            foreach (var u in v.Uses.OrderBy(u => u.Slot).ThenBy(u => u.Address))
            {
                string name = lib.Name(u.Slot);
                string what = u.Access == GlobalAccess.Read ? "reads" : u.Access == GlobalAccess.Write ? "sets" : "reads and sets";
                string val = u.Access != GlobalAccess.Read ? (u.Value.HasValue ? " to " + u.Value : " to a computed value") : "";
                Model.Uses.Add(new UseRow
                {
                    Use = u,
                    Text = string.Format("{0} {1}: {2}{3} ({4}, at {5:X4})", u.Slot, name ?? "", what, val, u.Via, u.Address),
                });
            }
            if (v.Uses.Count == 0) Model.Uses.Add(new UseRow { Text = "No conversation uses it by a constant number. Traps and the game's own code may still use it." });
        }

        void OnUseDoubleTapped(object sender, TappedEventArgs e)
        {
            var row = UseList.SelectedItem as UseRow;
            if (row != null && row.Use != null) openConversation?.Invoke(row.Use.Slot, row.Use.Address);
        }

        void OnRefresh(object sender, RoutedEventArgs e) { Loading = LoadAsync(); }

        void OnSave(object sender, RoutedEventArgs e) { Save(); }

        public List<string> Save()
        {
            if (globals == null) return null;
            if (!globals.CanEdit) { Model.Status = "DATA holds a new game's values. Open a SAVE folder to change a saved game's values."; return null; }
            try
            {
                var done = globals.Save();
                Model.Status = done.Count == 0 ? "Nothing to save." : "Saved " + string.Join(" and ", done.Select(Path.GetFileName)) + " (the old files are kept as .BKP).";
                return done;
            }
            catch (Exception ex) { Model.Status = "Not saved: " + ex.Message; return null; }
        }
    }
}
