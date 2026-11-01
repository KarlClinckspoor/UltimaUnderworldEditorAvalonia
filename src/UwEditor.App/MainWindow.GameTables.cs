using System;
using Avalonia.Interactivity;
using UwEditor.Core;

namespace UwEditor.App
{
    /// <summary>The main window's side of Tools ▸ Game tables and Level ▸ Level textures.</summary>
    public partial class MainWindow
    {
        GameTablesWindow gameTablesWindow;
        LevelTexturesWindow levelTexturesWindow;

        void OnGameTables(object sender, RoutedEventArgs e) { ShowGameTables(); }
        void OnLevelTextures(object sender, RoutedEventArgs e) { ShowLevelTextures(); }

        /// <summary>Tools ▸ Game tables: TERRAIN.DAT, SKILLS.DAT, CMB.DAT, SHADES.DAT and DL.DAT.</summary>
        public GameTablesWindow ShowGameTables()
        {
            if (vm.Session == null) return null;
            if (gameTablesWindow != null) { gameTablesWindow.Activate(); return gameTablesWindow; }
            var w = gameTablesWindow = new GameTablesWindow(this, vm.Session);
            w.Closed += (s, e) => gameTablesWindow = null;
            MessageWindow.GuardClose(w, "Game tables", () => w.HasChanges, w.SaveAll, () => AskBeforeDiscarding && !quietClose);
            w.Show(this);
            return w;
        }

        /// <summary>Level ▸ Level textures: the current level's wall, floor and door textures.</summary>
        public LevelTexturesWindow ShowLevelTextures()
        {
            if (vm.Session == null || vm.Edits == null) return null;
            if (levelTexturesWindow != null) { levelTexturesWindow.Activate(); return levelTexturesWindow; }
            levelTexturesWindow = new LevelTexturesWindow(this);
            levelTexturesWindow.Closed += (s, e) => levelTexturesWindow = null;
            levelTexturesWindow.Show(this);
            return levelTexturesWindow;
        }

        /// <summary>SHADES.DAT or DL.DAT was saved: the view's game light reads them again.</summary>
        public void GameLightChanged()
        {
            if (vm.Session == null || vm.Light == null) return;
            try { vm.Light.CopyFrom(GameLight.Load(vm.Session.DataFolder, vm.Session.Graphics.Palette)); }
            catch (Exception) { return; }
            ApplyLight();
        }
    }
}
