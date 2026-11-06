# Ultima Underworlds Mod Manager

Installs mods for **Ultima Underworld** and **Ultima Underworld II** made with the Ultima
Underworld Editor (Avalonia; **Tools ▸ Mods**) in this repository. It is a desktop app
(Avalonia: Windows, macOS, Linux) and a command-line tool, `uwmod`, built on the same core. It
was brought in with its history from `KarlClinckspoor/UltimaUnderworldsModManager`.

## What it does

- **Mods are differences from the game's own files** (`.uwmod`, format 2). The spec is
  [docs/mod-format.md](../docs/mod-format.md), and the code is `src/Underworld.Mods`, shared with
  the editor. A mod changes files the game already has, and adds only the sound files the game
  opens by number. Each file names the SHA-256 of the exact original it applies to and of the
  result, so a mod installs only on those originals, and then gives back, byte for byte, the
  files its author saved. A mod made from other files (another release, another mod) is refused
  before anything is written.
- **One mod at a time.** Pick a mod or "The original game" for each game you added.
- **GOG's current release** (2025) plays from a CD image, `game.gog`, beside a small
  `UNDEROM1` or `UNDEROM2` folder; there are no game files to change. Choose the install's
  folder with **Add a DOS game…** (or `uwmod add-dos FOLDER`). The manager checks the image's
  SHA-256 (it asks before using an image of another hash, `--unknown-image`), copies the
  game out of it into its own folder (`games`, with the install's sound setup from
  `UNDEROM1`/`UNDEROM2`), and that copy takes the mods. The game is the install's (from
  `UNDEROM1`/`dosboxULTIMA1.conf` and the like), else the one you choose (`--game uw1|uw2`).
  **The GOG install is not changed:** GOG Galaxy and its shortcuts still play the original
  game from `game.gog`, so a modded game plays only through the manager's **Play**. Play
  uses the install's DOSBox and GOG's settings (`dosboxULTIMA1.conf` or 2, which GOG's Graphic
  Mode Setup changes), not the setup that mounts the image, then the manager's start, which
  mounts the copy. Forgetting the game deletes the copy, unless it holds saved games. Exhumed
  added on such an install reads the manager's copy.
- **DOS games are changed in place.** Before the manager touches a file it keeps the original
  in its vault, so the original game is always one click away. Where the editor has written a
  file, its first version (`NAME.EXT.ORIG`) counts as the original. A change is all or nothing:
  if a write fails, the files already written are put back. A change the program could not
  finish (a crash, a power cut) is undone the next time the manager starts, or with
  `uwmod recover`. The folder keeps a small `.uwmm-target.json` that records what its originals
  are, so adding the folder again later never mistakes a mod's files for the originals.
- **Packages are untrusted.** A package may only change the game's own files: the executable
  and the DATA, CRIT, CUTS and SOUND folders, not the player's `UW.CFG` or saved games. A
  package that changes anything else, or has a damaged delta, is refused when it is imported.
- **UltimaHacks** (mouse-look, WASD and more) is built in for the DOS games: tick it and the
  manager makes the hacked executable from GOG's. A mod's change to the executable is made
  against GOG's executable, so with UltimaHacks only a moved start square can follow (it is
  written into the hacked executable). With the box ticked, a mod that changes more of the
  executable is refused.
- **Underworld Exhumed** (the native ports, uw1port/uw2port) is never changed: each mod gets a
  home folder of its own, laid over the game's data, and the port is started with it. The
  manager compares the mod's executable with the original. A moved start square goes into the
  home's settings (`start-square`), and other changed bytes go into its patch file
  (`exe-patch`) when they lie in the data the port reads. Both need a port built from the
  `modding` branch of [the fork](https://github.com/KarlClinckspoor/underworld-exhumed-enhanced);
  the manager asks the port what it supports (`--features`) and says when a mod needs more.
  The **enhancements** are the home's own setting (`enhance=` in its settings file): the
  manager's boxes and the game's settings menu both change it, Play leaves it as it is, and the
  boxes show what the game saved once it closes. A mod's new home starts with the
  enhancements last chosen.
- **Play** starts DOSBox on a DOS folder with GOG's own setups (`dosbox*.conf` in the folder).
  A folder without them gets a setup with GOG's settings for the two games (an S3 SVGA machine
  with 16 MB, EMS and XMS, maximum cycles, a Sound Blaster Pro at 220/7/1 and an MPU-401, full
  screen at the desktop's size), written to the manager's `dosbox` folder at each start.
  Exhumed starts on the mod's home. Each game's **Start options** add a settings file and
  arguments of your own: for DOSBox a `.conf` read after the others, so its settings win (for
  example `[sdl]` `fullscreen=false`), and for Exhumed `key=value` lines copied into the
  home's settings file at each start (`start-square` and `exe-patch` stay the mod's). The
  arguments follow the manager's (`uwmod start-options` sets both from the command line).
  An Exhumed program built from the sources comes without its DLLs (SDL3.dll and the others),
  and Windows stops it at once without a word: Play names the missing DLLs and offers to copy
  them from a release's folder, and says so when a game stops as soon as it starts.
- **Saved games are backed up**, not shared: a slot's files go into the vault before anything
  replaces them, and a backup can be restored. (Saved games are not mods.)
- **Capture**: changes made to a DOS folder by hand (or by the editor) can be turned into a mod.
- **The editor's look** (`src/UwEditor.Look`): its Light and Dark colours and its Compact and
  Comfortable layouts, chosen in **Settings ▸ Appearance** (Light and Compact by default).
- **Version:** shown at the top of **Settings** (`<Version>` in `ModManager/Directory.Build.props`,
  raised with every change: 0.2.1, 0.2.2…), with the commit it was built from.

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

The tests use the editor's game files: `UW_GAMEDATA` (a folder with `UW` and/or `UW2`), or
failing that the git-ignored `tests/gamedata` beside `UltimaUnderworldEditorAvalonia.sln`. The
tests copy them and never write to the folder itself; they make their changes with the editor's
own writers (`UwEditor.Core`). A game that is not there is not tested (those tests pass without
checking anything), unless `UW_REQUIRE_GAMEDATA=1` is set.
`UWMM_TEST_UW1PORT` / `UWMM_TEST_UW2PORT`, set to built ports from the fork's `modding`
branch, add tests that start a port and check the start square of a new game.
`tests/UwModManager.UiTests` drives the app headless and writes screenshots to its
`bin/.../screenshots`.

## Where the manager keeps its files

`%APPDATA%\UltimaUnderworldsModManager` on Windows,
`~/Library/Application Support/UltimaUnderworldsModManager` on macOS,
`~/.local/share/UltimaUnderworldsModManager` elsewhere; `UWMM_HOME` overrides it. It holds the
vault of originals, the library of imported mods, the Exhumed homes, the saved-game backups and
the copies of the games taken out of GOG's `game.gog` (`games`).

## Credits

- [UltimaHacks](https://github.com/JohnGlassmyer/UltimaHacks) by John Glassmyer, MIT licence
  (`hacks/LICENSE-UltimaHacks.txt`). `hacks/uw1.hack` and `hacks/uw2.hack` are its compiled
  hacks, unchanged, from commit 0f449c7.
- [Underworld Exhumed](https://github.com/abedegno/underworld-exhumed), the native ports.
- Ultima Underworld and Ultima Underworld II are © Electronic Arts; you need your own copies.
