# Ultima Underworlds Mod Manager

Installs mods for **Ultima Underworld** and **Ultima Underworld II** made with the
[Ultima Underworld Editor](https://github.com/KarlClinckspoor/UltimaUnderworldEditorUnity)
(Tools > Export as mod). A desktop app (Avalonia: Windows, macOS, Linux) and a command line
tool, `uwmod`, over the same core.

It lives in the Avalonia editor's repository (`ModManager/`, brought in with its history from
`KarlClinckspoor/UltimaUnderworldsModManager`). It still reads the Unity editor's mod format:
the Avalonia editor's own **Make a mod** writes a different `mod.json` (strings and executable
changes are laid out otherwise), so its mods do not install here yet. Moving both onto one
shared `Underworld.Modding` library is the next step.

## What it does

- **Mods are packages of changes**, `.uwmod` files: a zip with `mod.json` and the changed
  parts only — level blocks of `LEV.ARK`, strings of `STRINGS.PAK`, pictures of `.GR`/`.BYT`
  files, byte ranges, whole files, executable tweaks (such as the start square) and saved
  games. Every part carries the hash of what it was made from, so a mod that does not fit the
  game you have is refused rather than half-applied.
- **One mod at a time.** Pick a mod or "The original game" for each game you added.
- **DOS games are changed in place.** Before the manager touches a file it keeps the original
  in its vault, so the original game is always one click away. If the folder has the editor's
  `.BKP` backups, those count as the originals. A change is all or nothing: if a write fails,
  the files already written are put back, and a change the program could not finish (a crash,
  a power cut) is undone the next time the manager starts (or with `uwmod recover`). The folder
  keeps a small `.uwmm-target.json` saying what its originals are, so adding it again later
  never takes a mod's files for the originals.
- **Packages are untrusted.** A package may only change the game's own files (the executable
  and the DATA, CRIT, CUTS and SOUND folders) and a saved game's files; anything else is
  refused when it is imported.
- **UltimaHacks** (mouse-look, WASD and more) is built in for the DOS games: tick it and the
  manager makes the hacked executable from GOG's and applies the mod's executable tweaks on top.
- **Underworld Exhumed** (the native ports, uw1port/uw2port) is never changed: each mod gets a
  home folder of its own, laid over the game's data, and the port is started with it. Start
  squares and executable tweaks need a port built from the `modding` branch of
  [the fork](https://github.com/KarlClinckspoor/underworld-exhumed-enhanced); the manager asks
  the port what it supports (`--features`) and says when a mod needs more.
- **Saved games** as small mods: export a slot as a `.uwmod`, install one into a slot (the
  slot is backed up first, and backups can be restored).
- **Capture**: changes made to a DOS folder by hand (or by the editor) can be turned into a mod.

## Building

The file formats and the mod packages are shared with the editor, whose `Underworld.Formats`
and `Underworld.Modding` code is Unity-free. For now the manager compiles those sources from a
Unity editor checkout (with `Assets/Scripts/Modding`), found, in order:

1. beside the Avalonia editor's repository: `../UltimaUnderworldEditorUnity`;
2. `ModManager/external/UltimaUnderworldEditorUnity` (for instance as a git submodule);
3. `dotnet build -p:EditorRoot=PATH`.

`ModManager/Directory.Build.props` imports the repository's own (for `AvaloniaVersion`), then
turns on nullable references and implicit usings for the manager's projects only. The projects
are in the editor's `UltimaUnderworldEditorAvalonia.sln` (folder ModManager), and
`UltimaUnderworldsModManager.slnx` here still builds the manager alone.

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
git-ignored `tests/gamedata` beside `UltimaUnderworldEditorAvalonia.sln`. The tests copy them,
put any `.BKP` originals back in the copies and never write to the folder itself. A game that
is not there is not tested (those tests pass without checking anything), unless
`UW_REQUIRE_GAMEDATA=1`.
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
