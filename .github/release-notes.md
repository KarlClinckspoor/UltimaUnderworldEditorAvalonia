The Avalonia editor for **Ultima Underworld I and II**, and now the **Ultima Underworlds Mod Manager** beside it. Self-contained builds: no .NET install needed.

| Platform | Editor | Mod manager (with the `uwmod` command line) |
|---|---|---|
| Windows 10/11 x64 | `UwEditorAvalonia-VERSION-win-x64.zip` | `UltimaUnderworldsModManager-MMVERSION-win-x64.zip` |
| Linux x64 | `UwEditorAvalonia-VERSION-linux-x64.tar.gz` | `UltimaUnderworldsModManager-MMVERSION-linux-x64.tar.gz` |
| macOS Apple Silicon | `UwEditorAvalonia-VERSION-osx-arm64.zip` | `UltimaUnderworldsModManager-MMVERSION-osx-arm64.zip` |
| macOS Intel | `UwEditorAvalonia-VERSION-osx-x64.zip` | `UltimaUnderworldsModManager-MMVERSION-osx-x64.zip` |

**Editor:** unpack, run `UwEditorAvalonia`, then **File ▸ Open game folder** and pick the game folder, its `DATA` folder or a `SAVEn` folder. The manual is under **Help ▸ Manual** (F1), and the version under **Help ▸ About**.

**Mod manager:** unpack, run `UltimaUnderworldsModManager`, add a DOS game folder or an Underworld Exhumed program, import `.uwmod` files and pick one per game. The version is at the top of **Settings**.

### New since 0.1.2-beta
**Conversations**
- The Dialogue tab is an editor: the conversation language with colours, folding, find, go to line, a live check (errors underlined and listed), and **Apply** (Ctrl+Enter), which compiles the text back into the conversation and its strings. Undo apply / Redo apply, Revert to last applied, Revert to the original. Simulate runs the text as typed.
- A compiler from the Dialogue source to the game's bytecode: every shipped conversation (97 in UW1, 102 in UW2) compiles back to the same bytes, and an edit changes only the function edited.
- A structured view: if / else, while, menu and switch blocks instead of jumps.
- Names: shipped names for the conversations' functions, labels and globals in both games, carried over to edited blocks function by function. **F2** renames; your names are kept in the settings folder.

**Fonts and translations**
- **Tools ▸ Graphics ▸ Fonts**: a glyph editor for the games' FONT*.SYS, copy and paste between fonts, Make accented letter, PNG and BDF export and import, TrueType export.
- An accent table per game folder, with presets: accented letters take the printable symbols no string uses (the game draws only codes 20h-7Ah). STRINGS.PAK gets a new Huffman tree when a character has no code yet.

**Randomizer**
- Place anywhere (on by default): floor items go to random squares of their level that the logic reaches.
- Key item scatter: a spoiler log in UW1's map notes (hints for artifacts and crucial items, then the solutions), and a picker for additional items to include.

**Balance and editing**
- **Tools ▸ Balance**: a barter simulator, a weapons overview and an armour and rings overview, worked out from the games' own rules.
- Level textures: pick, then Use it; Undo takes the last change back. Saved pictures refresh the windows that show them.
- Automap window with its own page picker (UW1's notes-only pages 10-99). The 3D view draws wall textures unmirrored and from the face's top edge, as the game does.

**GOG's game.gog**
- The editor (File ▸ Open GOG's game.gog…) and the mod manager (Add a DOS game… on a GOG install) copy the games out of GOG's CD image and work on that copy; the GOG install is left as it is. Play mods on such an install with the manager's Play.

**Fixes from review**
- The conversation window asks before showing another conversation when the text has edits not applied (they were thrown away). A string edit or rename after an Apply is no longer lost by Undo apply / Redo apply. A names file that cannot be read is kept as `.bad` before it is saved over.
- The compiler: an answered menu (`x = menu {...};`) reads back from its own view, labels of two functions no longer clash, `#n` beyond the block and numbers below -32768 are errors, a frame too big for a word is an error. Strings are set before the code, so a refused string leaves the conversation as it was.
- Balance windows follow saved object tables; the barter ranges' minimums are as the game draws them; Charm updates the trader's text. The additional-items picker's class boxes tick and untick as they say. An item shuffle that only moves items is applied.
- Fonts: free accent codes are worked out from the strings as they are now. BDF export says proportional spacing and a numeric default character; TrueType export sets the private-use range bit correctly.
- GOG copies: forgetting a DOS copy no longer deletes the folder an Exhumed game plays from; Exhumed uses the copy of its own game only; a moved or uninstalled GOG install no longer stops the game page; a failed copy in the editor leaves no half copy behind.

### Beta notes
- **Back up your game** (or work through Tools ▸ Mods or the mod manager) before saving.
- Tested on Windows. The editor's Linux build has been started headless; the manager's Linux build and all macOS builds are untested.
- **macOS:** the builds are not notarised. Run `xattr -d com.apple.quarantine` on the program, then start it from Terminal (`./UwEditorAvalonia`, `./UltimaUnderworldsModManager`).
- **Linux:** the editor needs OpenGL 3.3; run `chmod +x` on the programs if the executable bit was lost.
- **Windows:** if SmartScreen warns about an unknown publisher, choose More info ▸ Run anyway.
