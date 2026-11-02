using System;
using System.Collections.Generic;
using System.Numerics;
using Avalonia.Interactivity;
using UwEditor.Core;
using UwEditor.Core.Randomizer;

namespace UwEditor.App
{
    /// <summary>The main window's side of the randomizer's zoning: the shared state, its windows, painting and the overlay.</summary>
    public partial class MainWindow
    {
        ZoningState zoning;
        ZonesWindow zonesWindow;
        ZoneGraphWindow zoneGraphWindow;
        List<float> zoneOverlay;
        int zoneOverlayLevel = -1;

        /// <summary>The zoning of the open game (read from DATA's zone file, or a new one).</summary>
        public ZoningState Zoning()
        {
            if (vm.Session == null) return null;
            if (zoning == null || zoning.Session != vm.Session)
            {
                zoning = new ZoningState(vm.Session, l => vm.Edits != null ? vm.Edits.Model(l) : vm.Session.LoadLevel(l));
                zoning.MaxClimb = settings.ZoneClimb;
                zoning.Changed += ZonesChanged;
                if (vm.Edits != null) vm.Edits.Changed += e => zoning?.LevelsChanged();
            }
            return zoning;
        }

        void OnZones(object sender, RoutedEventArgs e) { ShowZones(); }
        void OnZoneGraph(object sender, RoutedEventArgs e) { ShowZoneGraph(); }
        void OnItemShuffle(object sender, RoutedEventArgs e) { ShowItemShuffle(); }
        void OnItemSwap(object sender, RoutedEventArgs e) { ShowItemSwap(); }
        void OnMonsterSwap(object sender, RoutedEventArgs e) { ShowMonsterSwap(); }
        void OnAutomap(object sender, RoutedEventArgs e) { ShowAutomap(); }

        AutomapWindow automapWindow;

        public AutomapWindow ShowAutomap()
        {
            if (vm.Session == null || vm.Edits == null) return null;
            if (automapWindow != null) { automapWindow.Activate(); return automapWindow; }
            automapWindow = new AutomapWindow(this);
            automapWindow.Closed += (s, e) => automapWindow = null;
            automapWindow.Show(this);
            return automapWindow;
        }

        MonsterSwapWindow monsterSwapWindow;

        public MonsterSwapWindow ShowMonsterSwap()
        {
            if (vm.Session == null || vm.Edits == null) return null;
            if (monsterSwapWindow != null) { monsterSwapWindow.Activate(); return monsterSwapWindow; }
            monsterSwapWindow = new MonsterSwapWindow(this, vm.Session);
            monsterSwapWindow.Closed += (s, e) => monsterSwapWindow = null;
            monsterSwapWindow.Show(this);
            return monsterSwapWindow;
        }

        ItemSwapWindow itemSwapWindow;

        public ItemSwapWindow ShowItemSwap()
        {
            if (vm.Session == null || vm.Edits == null) return null;
            if (itemSwapWindow != null) { itemSwapWindow.Activate(); return itemSwapWindow; }
            itemSwapWindow = new ItemSwapWindow(this, vm.Session);
            itemSwapWindow.Closed += (s, e) => itemSwapWindow = null;
            itemSwapWindow.Show(this);
            return itemSwapWindow;
        }

        ItemShuffleWindow itemShuffleWindow;

        public ItemShuffleWindow ShowItemShuffle()
        {
            if (vm.Session == null || vm.Edits == null) return null;
            if (itemShuffleWindow != null) { itemShuffleWindow.Activate(); return itemShuffleWindow; }
            itemShuffleWindow = new ItemShuffleWindow(this, vm.Session);
            itemShuffleWindow.Closed += (s, e) => itemShuffleWindow = null;
            itemShuffleWindow.Show(this);
            return itemShuffleWindow;
        }

        /// <summary>The item shuffle changed objects on many levels: the object list and the edit state are read again.</summary>
        public void ShuffleApplied() { RandomizerApplied("Item properties shuffled; Edit ▸ Undo (per level) or the shuffle window's Undo shuffle puts them back."); }

        /// <summary>A randomizer tool changed or restored levels: the zones follow, and the status line says so.</summary>
        public void RandomizerApplied(string status)
        {
            zoning?.LevelsChanged();
            vm.EditStatus = status;
        }

        public ZonesWindow ShowZones()
        {
            var z = Zoning();
            if (z == null) return null;
            if (zonesWindow != null) { zonesWindow.Activate(); return zonesWindow; }
            zonesWindow = new ZonesWindow(this, z);
            zonesWindow.Closed += (s, e) => { zonesWindow = null; ZonesChanged("tiles"); };
            zonesWindow.Show(this);
            ZonesChanged("tiles");
            return zonesWindow;
        }

        public ZoneGraphWindow ShowZoneGraph()
        {
            var z = Zoning();
            if (z == null) return null;
            if (zoneGraphWindow != null) { zoneGraphWindow.Activate(); return zoneGraphWindow; }
            zoneGraphWindow = new ZoneGraphWindow(this, z);
            zoneGraphWindow.Closed += (s, e) => zoneGraphWindow = null;
            zoneGraphWindow.Show(this);
            return zoneGraphWindow;
        }

        /// <summary>The zones window turned zone painting on or off.</summary>
        public void ZonePaintingChanged()
        {
            if (zoning != null && zoning.Painting)
            {
                vm.ToolIndex = 2;
                vm.EditStatus = "Painting zone " + zoning.CurrentZone + ": drag over tiles" + (zoning.FillMode ? " (a click fills the area)" : "") + ". Rock is never painted.";
            }
            else if (vm.ToolIndex == 2) vm.EditStatus = "";
        }

        /// <summary>Paints or fills a tile with the current zone when zone painting is on; false otherwise.</summary>
        bool ZonePaint(int x, int y, bool start)
        {
            if (zoning == null || !zoning.Painting || zonesWindow == null) return false;
            int level = vm.Level.Level;
            if (zoning.FillMode)
            {
                if (!start) return true;
                int n = zoning.FillArea(level, x, y, zoning.CurrentZone);
                vm.EditStatus = n == 0 ? "Nothing to fill there." : string.Format("{0} tiles filled as zone {1}.", n, zoning.CurrentZone);
                return true;
            }
            zoning.PaintTile(level, x, y, start);
            return true;
        }

        /// <summary>The zoning changed: the overlay is drawn again.</summary>
        public void ZonesChanged(string what)
        {
            if (what == "tiles" || what == "file" || what == "current" || what == "zones") zoneOverlay = null;
            UpdateMarkers();
        }

        /// <summary>Each tile's zone colour as a sheet over its floor (unassigned tiles magenta), while the zones window is open.</summary>
        void AddZoneOverlay(List<float> tris)
        {
            var level = vm.Level;
            if (zoning == null || zonesWindow == null || !zoning.ShowOverlay || level == null || level.Level > zoning.Set.Levels) return;
            if (zoneOverlay == null || zoneOverlayLevel != level.Level)
            {
                zoneOverlay = new List<float>();
                zoneOverlayLevel = level.Level;
                var t = zoning.Set.Tiles[level.Level - 1];
                if (t != null)
                    for (int y = 0; y < 64; y++)
                        for (int x = 0; x < 64; x++)
                        {
                            int id = t[y * 64 + x];
                            if (id == ZoneSet.Solid || level.Tiles[x, y].IsSolid) continue;
                            uint c = zoning.Set.ColorOf(id);
                            float alpha = id == ZoneSet.Unassigned ? 0.6f : id == zoning.CurrentZone ? 0.55f : 0.38f;
                            var col = new Vector4((c >> 16 & 0xFF) / 255f, (c >> 8 & 0xFF) / 255f, (c & 0xFF) / 255f, alpha);
                            const float up = 0.025f;
                            Markers.TileSheet(zoneOverlay, x, y,
                                (float)level.FloorAt(x, y, 0, 0) + up, (float)level.FloorAt(x, y, 1, 0) + up,
                                (float)level.FloorAt(x, y, 0, 1) + up, (float)level.FloorAt(x, y, 1, 1) + up, col);
                        }
            }
            tris.AddRange(zoneOverlay);
        }

        /// <summary>Opens a level and selects a tile, looking at it.</summary>
        public void GoToTile(int level, int x, int y)
        {
            if (vm.Session == null) return;
            if (vm.Level == null || vm.Level.Level != level) vm.LoadLevel(level);
            if (vm.Level == null) return;
            vm.Select(-1, x, y, true);
            Frame();
            Activate();
        }
    }
}
