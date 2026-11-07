The Avalonia editor for **Ultima Underworld I and II**, and now the **Ultima Underworlds Mod Manager** beside it. Self-contained builds: no .NET install needed.

| Platform | Editor | Mod manager (with the `uwmod` command line) |
|---|---|---|
| Windows 10/11 x64 | `UwEditorAvalonia-VERSION-win-x64.zip` | `UltimaUnderworldsModManager-MMVERSION-win-x64.zip` |
| Linux x64 | `UwEditorAvalonia-VERSION-linux-x64.tar.gz` | `UltimaUnderworldsModManager-MMVERSION-linux-x64.tar.gz` |
| macOS Apple Silicon | `UwEditorAvalonia-VERSION-osx-arm64.zip` | `UltimaUnderworldsModManager-MMVERSION-osx-arm64.zip` |
| macOS Intel | `UwEditorAvalonia-VERSION-osx-x64.zip` | `UltimaUnderworldsModManager-MMVERSION-osx-x64.zip` |

**Editor:** unpack, run `UwEditorAvalonia`, then **File ▸ Open game folder** and pick the game folder, its `DATA` folder or a `SAVEn` folder. The manual is under **Help ▸ Manual** (F1), and the version under **Help ▸ About**.

**Mod manager:** unpack, run `UltimaUnderworldsModManager`, add a DOS game folder or an Underworld Exhumed program, import `.uwmod` files and pick one per game. The version is at the top of **Settings**.

### New since 0.1.14-beta
**Manual**
- Conversation recipes (Conversations ▸ Editing on the Dialogue tab ▸ Recipes): trade one item for another, give an item after a choice, an NPC that waits, threatens and attacks, barter with likes and greed, answers the player types ("Deco Morono", "Talismans", "volcano"), and one NPC knowing the player met another. Each is based on a shipped conversation and compiled and run by a test. They also say what a conversation cannot do: talking on approach is the creature's Goal (10), greed comes from the creature class, UW1 conversations can use quest flags 0-14 only.

**Fixes**
- Key item scatter: the seed the log names reproduces the run on its own, also after failed attempts (Place anywhere now draws each attempt's squares from that attempt's seed, so a retry can also get past a bad layout).
- Key item scatter: every artifact moved gets a hint and a solution in the map notes, whether or not the logic needs it (Caliburn's blade and hilt).
- Conversations: a bare `return;` in a function that returns a value reads back as `return;` (it showed as a `goto`, and the function's `return 5` was lost).
- GOG's game.gog: the other game's copy takes its sound setup from its own install beside this one (Ultima Underworld 2 next to Ultima Underworld), and Run in DOSBox uses that install's settings; when that game is not installed, the editor says the copy has no sound.
- Fonts: an import, or a change of the space or max width, is one undo step that puts back the whole font (an import took 128 undos and left the header).
- Object properties: creature goal 10 reads "wait, then talk to the player (it does not walk up)".

### Beta notes
- **Back up your game** (or work through Tools ▸ Mods or the mod manager) before saving.
- Tested on Windows. The editor's Linux build has been started headless; the manager's Linux build and all macOS builds are untested.
- **macOS:** the builds are not notarised. Run `xattr -d com.apple.quarantine` on the program, then start it from Terminal (`./UwEditorAvalonia`, `./UltimaUnderworldsModManager`).
- **Linux:** the editor needs OpenGL 3.3; run `chmod +x` on the programs if the executable bit was lost.
- **Windows:** if SmartScreen warns about an unknown publisher, choose More info ▸ Run anyway.
