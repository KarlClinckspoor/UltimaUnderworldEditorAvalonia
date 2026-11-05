The Avalonia editor for **Ultima Underworld I and II**, and now the **Ultima Underworlds Mod Manager** beside it. Self-contained builds: no .NET install needed.

| Platform | Editor | Mod manager (with the `uwmod` command line) |
|---|---|---|
| Windows 10/11 x64 | `UwEditorAvalonia-VERSION-win-x64.zip` | `UltimaUnderworldsModManager-MMVERSION-win-x64.zip` |
| Linux x64 | `UwEditorAvalonia-VERSION-linux-x64.tar.gz` | `UltimaUnderworldsModManager-MMVERSION-linux-x64.tar.gz` |
| macOS Apple Silicon | `UwEditorAvalonia-VERSION-osx-arm64.zip` | `UltimaUnderworldsModManager-MMVERSION-osx-arm64.zip` |
| macOS Intel | `UwEditorAvalonia-VERSION-osx-x64.zip` | `UltimaUnderworldsModManager-MMVERSION-osx-x64.zip` |

**Editor:** unpack, run `UwEditorAvalonia`, then **File ▸ Open game folder** and pick the game folder, its `DATA` folder or a `SAVEn` folder. The manual is under **Help ▸ Manual** (F1), and the version under **Help ▸ About**.

**Mod manager:** unpack, run `UltimaUnderworldsModManager`, add a DOS game folder or an Underworld Exhumed program, import `.uwmod` files and pick one per game. The version is at the top of **Settings**.

### New since 0.1.1-beta
**Mods**
- Mod format 2: a `.uwmod` holds only the differences from the game's own files, with the SHA-256 of each original, so it applies only to those files and gives back the author's files byte for byte. Mods may add the sounds the game opens by number. Format 1 mods are no longer read.
- **Tools ▸ Mods ▸ Changed files as a .zip**: your changed files, whole, in their folders, to unzip over your own copy of the game. It is not for the mod manager, and not to share: it holds game data.

**Mod manager (new)**
- Installs mods one at a time into DOS game folders (the originals are kept in its vault; changes are safe to interrupt) and into Underworld Exhumed, where each mod gets a home folder of its own. Built-in UltimaHacks for the DOS games, saved-game backups, and capturing a folder's changes as a mod.
- Play starts DOSBox with GOG's own setups, or with GOG's settings for a folder without them. Each game has Start options for a settings file and arguments of your own.
- Exhumed: Play names any DLL a build lacks and offers to copy it from a release. The enhancements chosen in the game's settings are kept between plays.

**Randomizer**
- Automatic zones (UW1): walkable regions, abilities and the world over time, solved for whether the game can be finished; a zone graph; Zones window with Mark as start / goal and Check if solvable.
- Key item scatter (UW1), named challenges, uniform progression in the item and monster swaps, and new monster and item swap options.

**Editing and testing**
- Sound and music: speech, effects and music (VOC, WAV, XMI, MIDI) with the games' limits.
- Player window: inventory slots in paper-doll order, and UW1's story progress (Garamon, talismans, orb, cup, key, moonstone, Armageddon…).
- Underworld Exhumed check and DOSBox spot test; a Run and Test menu; undo of changes made on several levels at once.
- Object fields named from the game's code; a folding properties panel; the conversation graph with 70% fewer crossings.
- Light or Dark and Compact or Comfortable layouts, shared by both apps.

### Beta notes
- **Back up your game** (or work through Tools ▸ Mods or the mod manager) before saving.
- Tested on Windows. The editor's Linux build has been started headless; the manager's Linux build and all macOS builds are untested.
- **macOS:** the builds are not notarised. Run `xattr -d com.apple.quarantine` on the program, then start it from Terminal (`./UwEditorAvalonia`, `./UltimaUnderworldsModManager`).
- **Linux:** the editor needs OpenGL 3.3; run `chmod +x` on the programs if the executable bit was lost.
- **Windows:** if SmartScreen warns about an unknown publisher, choose More info ▸ Run anyway.
