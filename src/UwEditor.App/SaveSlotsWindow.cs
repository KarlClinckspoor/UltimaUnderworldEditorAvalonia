using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using UwEditor.Core.Editing;

namespace UwEditor.App
{
    /// <summary>
    /// File ▸ Saved games: the game folder's SAVE1 to SAVE4 with their descriptions (DESC, as
    /// the game's save menu shows them). A description can be renamed, a slot copied to
    /// another (the replaced one kept as SAVEn.BKP), and a slot opened in the editor.
    /// </summary>
    public sealed class SaveSlotsWindow : Window
    {
        readonly MainWindow main;
        readonly StackPanel list = new StackPanel { Spacing = 10 };
        readonly TextBlock heading = new TextBlock { TextWrapping = TextWrapping.Wrap, FontSize = 12, Opacity = 0.75 };
        readonly TextBlock status = new TextBlock { TextWrapping = TextWrapping.Wrap, Opacity = 0.8, Margin = new Thickness(0, 8, 0, 0) };
        List<SaveSlot> slots = new List<SaveSlot>();

        public List<SaveSlot> Slots { get { return slots; } }
        public string StatusText { get { return status.Text; } }
        string GameFolder { get { return main.ViewModel.Session.GameFolder; } }

        public SaveSlotsWindow(MainWindow main)
        {
            this.main = main;
            Title = "Saved games";
            Width = 860; Height = 560; MinWidth = 640; MinHeight = 400;
            WindowStartupLocation = WindowStartupLocation.CenterOwner;
            var top = new StackPanel { Spacing = 6, Margin = new Thickness(0, 0, 0, 10) };
            top.Children.Add(new TextBlock { Text = "Saved games", FontSize = 16, FontWeight = FontWeight.SemiBold });
            top.Children.Add(heading);
            DockPanel.SetDock(top, Dock.Top);
            DockPanel.SetDock(status, Dock.Bottom);
            var dock = new DockPanel { Margin = new Thickness(16) };
            dock.Children.Add(top); dock.Children.Add(status);
            dock.Children.Add(new ScrollViewer { Content = list });
            Content = dock;
            Activated += (o, e) => Refresh();
            Refresh();
            ManualWindow.Attach(this, "Saved games");
        }

        bool IsOpen(SaveSlot s)
        {
            var se = main.ViewModel.Session;
            return se != null && se.IsSave && string.Equals(Path.GetFullPath(se.SaveFolder).TrimEnd('/', '\\'), Path.GetFullPath(s.Folder).TrimEnd('/', '\\'), StringComparison.OrdinalIgnoreCase);
        }

        /// <summary>Reads the slots again.</summary>
        public void Refresh()
        {
            var se = main.ViewModel.Session;
            if (se == null) return;
            slots = SaveSlots.List(GameFolder);
            heading.Text = "Open: " + (se.IsSave ? Path.GetFileName(se.SaveFolder) : "DATA (what a new game starts with)") + ". " +
                "A saved game is a copy of the game's working folder SAVE0: LEV.ARK (with the maps and notes), PLAYER.DAT, BGLOBALS.DAT, DESC" + (se.IsUw2 ? " and SCD.ARK" : "") + ". " +
                "The save menu lists SAVE1 to SAVE4 by their description (up to 30 characters); SAVE0 is overwritten whenever a game is started or restored.";
            list.Children.Clear();
            foreach (var s in slots) list.Children.Add(SlotRow(s));
        }

        Control SlotRow(SaveSlot s)
        {
            bool open = IsOpen(s);
            var name = new TextBlock { Text = s.Name + (open ? " (open)" : ""), Width = 110, FontWeight = FontWeight.SemiBold, VerticalAlignment = VerticalAlignment.Center };
            var desc = new TextBox { Text = s.Used ? s.Description : "", PlaceholderText = s.Exists ? "no description" : "empty slot", MaxLength = SaveSlots.DescMax, Width = 250, IsEnabled = s.Exists };
            var rename = new Button { Content = "Rename", IsEnabled = s.Exists };
            rename.Click += (o, e) => Rename(s.Number, desc.Text);
            var openB = new Button { Content = "Open", IsEnabled = s.HasLevels && !open };
            ToolTip.SetTip(openB, "Open this saved game in the editor (it asks about unsaved work first).");
            openB.Click += (o, e) => Open(s.Number);
            var target = new ComboBox { ItemsSource = slots.Where(x => x.Number != s.Number).Select(x => x.Name).ToList(), SelectedIndex = 0, Width = 100, IsEnabled = s.Exists };
            var copy = new Button { Content = "Copy to", IsEnabled = s.Exists };
            copy.Click += async (o, e) =>
            {
                var others = slots.Where(x => x.Number != s.Number).ToList();
                if (target.SelectedIndex < 0) return;
                await Copy(s.Number, others[target.SelectedIndex].Number);
            };
            var row = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8 };
            foreach (var c in new Control[] { name, desc, rename, openB, copy, target }) row.Children.Add(c);
            string info = !s.Exists ? "Not created yet: the game makes the folder when it first saves here."
                : string.Format("{0}{1}{2}", s.HasLevels ? "LEV.ARK" : "no LEV.ARK", s.Used ? "" : ", no DESC (the menu shows <not used yet>)",
                    s.Written != DateTime.MinValue ? ", written " + s.Written.ToString("yyyy-MM-dd HH:mm") : "");
            var p = new StackPanel { Spacing = 2 };
            p.Children.Add(row);
            p.Children.Add(new TextBlock { Text = info, FontSize = 12, Opacity = 0.7, Margin = new Thickness(118, 0, 0, 0) });
            return p;
        }

        SaveSlot Slot(int n) { return slots.First(x => x.Number == n); }

        /// <summary>Writes slot n's description (DESC.BKP keeps the old one).</summary>
        public bool Rename(int n, string text)
        {
            var s = Slot(n);
            try
            {
                var bytes = SaveSlots.DescBytes(text);
                SaveSlots.Rename(s, text);
                status.Text = s.Name + " is now \"" + System.Text.Encoding.ASCII.GetString(bytes) + "\"" + ((text ?? "").Length > SaveSlots.DescMax ? " (cut to the game's 30 characters)." : ".")
                    + (IsOpen(s) ? " The title bar shows the new name after the save is opened again." : "");
                Refresh();
                return true;
            }
            catch (Exception ex) { status.Text = ex.Message; return false; }
        }

        /// <summary>Copies slot from onto slot to, asking first when to already holds a save.</summary>
        public async Task<bool> Copy(int from, int to)
        {
            var t = Slot(to);
            if (IsOpen(t)) { status.Text = t.Name + " is open in the editor: open DATA or another slot before replacing it."; return false; }
            var f = Slot(from);
            string note = IsOpen(f) && main.ViewModel.HasLevelChanges ? " The copy is of " + f.Name + " as it is on disk: unsaved level edits are not in it." : "";
            if (t.Exists)
            {
                string a = await MessageWindow.Ask(this, "Replace a saved game",
                    "Replace " + t.Name + (t.Used ? " (\"" + t.Description + "\")" : "") + " with a copy of " + f.Name + (f.Used ? " (\"" + f.Description + "\")" : "") + "? " +
                    "The old " + t.Name + " is kept as " + t.Name + SaveSlots.BackupSuffix + ", replacing an older one." + note,
                    "Replace", "Cancel");
                if (a != "Replace") return false;
            }
            return CopyNow(from, to);
        }

        /// <summary>Copies a slot without asking. False (the reason in the status line) when refused or failed.</summary>
        public bool CopyNow(int from, int to)
        {
            var f = Slot(from);
            var t = Slot(to);
            if (IsOpen(t)) { status.Text = t.Name + " is open in the editor: open DATA or another slot before replacing it."; return false; }
            try
            {
                string bkp = SaveSlots.Copy(f, t);
                status.Text = f.Name + " copied to " + t.Name + (bkp != null ? "; the old " + t.Name + " is kept as " + Path.GetFileName(bkp) + "." : ".");
                Refresh();
                return true;
            }
            catch (Exception ex) { status.Text = ex.Message; return false; }
        }

        /// <summary>Opens slot n in the editor (the main window asks about unsaved work first).</summary>
        public void Open(int n)
        {
            var s = Slot(n);
            if (!s.HasLevels) { status.Text = s.Name + " has no LEV.ARK."; return; }
            main.OpenPath(s.Folder);
        }
    }
}
