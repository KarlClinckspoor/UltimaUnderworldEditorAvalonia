First beta of the Avalonia editor for **Ultima Underworld I and II**. Self-contained builds: no .NET install needed.

| Platform | File |
|---|---|
| Windows 10/11 x64 | `UwEditorFree-0.1.0-beta-win-x64.zip` |
| Linux x64 | `UwEditorFree-0.1.0-beta-linux-x64.tar.gz` |
| macOS Apple Silicon | `UwEditorFree-0.1.0-beta-osx-arm64.zip` |
| macOS Intel | `UwEditorFree-0.1.0-beta-osx-x64.zip` |

Unpack, run `UwEditorFree`, then **File ▸ Open game folder** and pick the game folder, its `DATA` folder or a `SAVEn` folder. The manual is under **Help ▸ Manual** (F1).

### What's in it
- 3D and 2D views of every level, with first-person walking, editing of tiles and objects, a brush and fill, multi-select, copy and paste, and undo history.
- Play test in DOSBox from any tile, plus world checks for missing keys, locks without keys and levels near the object limit.
- Editors for conversations (with a simulator and graph), strings, global variables, keys and locks, triggers and traps, the automap, the player and inventory, object and game tables, and UW2 schedules.
- Graphics viewer with a pixel editor (palette picker and variable brush), and an item-slot report covering what uses each item id (levels, conversations, game code, loot and combinations).
- A mod manager, plus a randomizer, monster swap and item swap (work in progress).

### Beta notes
- **Back up your game** (or work through Tools ▸ Mods) before saving.
- Tested on Windows. The Linux build was started headless; the macOS builds are untested.
- **macOS:** the build is not notarised. Run `xattr -d com.apple.quarantine UwEditorFree`, then `./UwEditorFree` from Terminal.
- **Linux:** needs OpenGL 3.3; run `chmod +x UwEditorFree` if the executable bit was lost.
- **Windows:** if SmartScreen warns about an unknown publisher, choose More info ▸ Run anyway.
