# Ultima Underworlds Mod Manager

Installs mods for **Ultima Underworld** and **Ultima Underworld II** made with the Ultima
Underworld Editor (Avalonia; **Tools ▸ Mods**), in this repository. A desktop app (Avalonia:
Windows, macOS, Linux) and a command line tool, `uwmod`, over the same core. It was brought in
with its history from `KarlClinckspoor/UltimaUnderworldsModManager`.

## What it does

- **Mods are differences from the game's own files** (`.uwmod`, format 2: the spec is
  [docs/mod-format.md](../docs/mod-format.md), the code `src/Underworld.Mods`, shared with the
  editor). A mod changes files the game already has, and adds only the sound files the game
  opens by number. Each file names the SHA-256 of the exact original it applies to and of the
  result, so a mod installs only on those originals, and then gives back, byte for byte, the
  files its author saved. A mod made from other files (another release, another mod) is refused
  before anything is written.
- **One mod at a time.** Pick a mod or "The original game" for each game you added.
- **DOS games are changed in place.** Before the manager touches a file it keeps the original
  in its vault, so the original game is always one click away. Where the editor has written a
  file, its first version (`NAME.EXT.ORIG`) counts as the original. A change is all or nothing:
  if a write fails, the files already written are put back, and a change the program could not
  finish (a crash, a power cut) is undone the next time the manager starts (or with
  `uwmod recover`). The folder keeps a small `.uwmm-target.json` saying what its originals are,
  so adding it again later never takes a mod's files for the originals.
- **Packages are untrusted.** A package may only change the game's own files (the executable
  and the DATA, CRIT, CUTS and SOUND folders, not the player's `UW.CFG` or saved games); anything
  else, and any damaged delta, is refused when it is imported.
- **UltimaHacks** (mouse-look, WASD and more) is built in for the DOS games: tick it and the
  manager makes the hacked executable from GOG's. A mod's change to the executable is made
  against GOG's, so with UltimaHacks only a start square follows (written into the hacked
  executable); a mod that changes more of the executable is refused with the box ticked.
- **Underworld Exhumed** (the native ports, uw1port/uw2port) is never changed: each mod gets a
  home folder of its own, laid over the game's data, and the port is started with it. The
  manager compares the mod's executable with the original: a moved start square goes into the
  home's settings (`start-square`), other changed bytes into its patch file (`exe-patch`), when
  they lie in the data the port reads. Both need a port built from the `modding` branch of
  [the fork](https://github.com/KarlClinckspoor/underworld-exhumed-enhanced); the manager asks
  the port what it supports (`--features`) and says when a mod needs more.
- **Saved games are backed up**, not shared: a slot's files go into the vault before anything
  replaces them, and a backup can be restored. (Saved games are not mods.)
- **Capture**: changes made to a DOS folder by hand (or by the editor) can be turned into a mod.

## Building

The manager builds against this repository's `src/Underworld.Formats` and `src/Underworld.Mods`.
`ModManager/Directory.Build.props` imports the repository's own (for `AvaloniaVersion`), then
turns on nullable references and implicit usings for the manager's projects only. The projects
are in the editor's `UltimaUnderworldEditorAvalonia.sln` (folder ModManager), and
`UltimaUnderworldsModManager.slnx` here builds the manager alone.

From the repository's top, with the .NET 10 SDK:

```sh
dotnet build ModManager/UltimaUnderworldsModManager.slnx
dotnet run --project ModManager/src/UwModManager.App      # the app
dotnet run --project ModManager/src/UwModManager.Cli -- help
dotnet publish ModManager/src/UwModManager.App -c Release -r win-x64 --self-contained
```

## Tests

```sh
UW_REQUIRE_GAMEDATA=1 dotnet test ModManager/tests/UwModManager.Tests
UW_REQUIRE_GAMEDATA=1 dotnet test ModManager/tests/UwModManager.UiTests
```

The game files are the editor's: `UW_GAMEDATA` (a folder with `UW` and/or `UW2`), else the
git-ignored `tests/gamedata` beside `UltimaUnderworldEditorAvalonia.sln`. The tests copy them
and never write to the folder itself; they make their changes with the editor's own writers
(`UwEditor.Core`). A game that is not there is not tested (those tests pass without checking
anything), unless `UW_REQUIRE_GAMEDATA=1`.
`UWMM_TEST_UW1PORT` / `UWMM_TEST_UW2PORT`, set to built ports from the fork's `modding`
branch, add tests that start a port and check the start square of a new game.
`tests/UwModManager.UiTests` drives the app headless and writes screenshots to its
`bin/.../screenshots`.

## Where the manager keeps its files

`%APPDATA%\UltimaUnderworldsModManager` on Windows,
`~/Library/Application Support/UltimaUnderworldsModManager` on macOS,
`~/.local/share/UltimaUnderworldsModManager` elsewhere; `UWMM_HOME` overrides it. In it: the
vault of originals, the library of imported mods, the Exhumed homes and saved-game backups.

## Credits

- [UltimaHacks](https://github.com/JohnGlassmyer/UltimaHacks) by John Glassmyer, MIT licence
  (`hacks/LICENSE-UltimaHacks.txt`). `hacks/uw1.hack` and `hacks/uw2.hack` are its compiled
  hacks, unchanged, from commit 0f449c7.
- [Underworld Exhumed](https://github.com/abedegno/underworld-exhumed), the native ports.
- Ultima Underworld and Ultima Underworld II are © Electronic Arts; you need your own copies.
