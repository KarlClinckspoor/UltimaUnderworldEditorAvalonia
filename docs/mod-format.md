# Mod format 2 (`.uwmod`): design

Status: **built** (see "Building it" at the end). It replaced the editor's `Mods/ModPackage.cs`
(format 1) and the Unity editor's `Assets/Scripts/Modding`, which the mod manager used to build
against. Neither was carried over: format 2 was written from scratch in `src/Underworld.Mods`,
which the editor and `ModManager/` share.

## Rules

1. **A mod changes files the game already has, and nothing else.** It is for one game (UW1 or
   UW2) and changes, in its folder, the executable and the files under `DATA`, `CRIT`, `CUTS` and `SOUND`, but not
   `UW.CFG`, the player's sound setup. A mod never
   deletes or renames a file, and never touches a `SAVEn` folder. The game reads only the
   files it knows, so a new music theme replaces the bytes of the `.XMI` it goes into (the
   themes' numbers are fixed in the game's code).

   **The one exception: sound files the game opens by a number from its data.** A mod may add
   one of these, since the game plays it even though GOG's release does not ship it; its content
   is all the mod's own (`GamePaths.CanAdd`). Numbers are written as the game writes them: two
   digits below 10, otherwise as many as needed.

   | Game | File | Opened for | Source |
   | --- | --- | --- | --- |
   | UW1 | `SOUND/nn.VOC`, 0-998 | a cutscene's speech | `uw1 sound/SOUND.C play_speech`, `gfx/CUTS.C` |
   | UW2 | `SOUND/SPnn.VOC`, 0-99 | an effect marked digital in `SOUNDS.DAT` | `uw2 sound/SOUND.C digi_fx_play` |
   | UW2 | `SOUND/UWnn.VOC`, 0-155 | a special-effects trap of type 2 playing sound nn + 100 | `event/WORLDEV.C do_sfx`, `digi_fx_play` |
   | UW2 | `SOUND/BSPnn.VOC`, 0-255 | a cutscene's speech | `gfx/CUTS.C big_speech_play` |
2. **A mod carries differences, never an original file.** Each changed file is stored as a
   delta: the new bytes, plus instructions to copy everything else from the original. No
   whole original file, and no unchanged block, string or picture, goes into a package.
3. **A delta applies to one exact original.** Each file names the SHA-256 and size of the
   original it was made from and of the result. A file that is anything else is refused,
   never patched "close enough".
4. **The result is byte-exact by construction.** Make a mod applies every delta to the original
   and compares the result with the edited file before writing the package; the manager checks
   the result's SHA-256 before writing it. A mod installed by the manager is therefore the same,
   byte for byte, as the folder the editor saved.

## The package

A package is a zip file:

```
mod.json
deltas/UW.EXE.uwd
deltas/DATA/LEV.ARK.uwd
deltas/SOUND/UW01.XMI.uwd
```

`mod.json`:

```json
{
  "format": 2,
  "game": "uw1",
  "id": "karl.garamon-moved",
  "name": "Garamon moved",
  "version": "1.0.0",
  "authors": ["Karl"],
  "description": "",
  "made_with": "UwEditorAvalonia 0.x",
  "files": [
    {
      "path": "DATA/LEV.ARK",
      "base":   { "sha256": "…", "size": 290864 },
      "target": { "sha256": "…", "size": 291102 },
      "delta":  { "path": "deltas/DATA/LEV.ARK.uwd", "sha256": "…" }
    }
  ]
}
```

- `path`: upper case, `/`-separated, relative to the game folder; it must be the executable
  (`UW.EXE` / `UW2.EXE`) or lie under `DATA/`, `CRIT/`, `CUTS/` or `SOUND/`, with no `..`.
- `"base": null` marks a file the mod adds (rule 1's exception); its delta is made from an
  empty original (ADD only). A game that already has a file of that name refuses the mod, and
  going back to the original game removes it.
- Packages are untrusted: anything that breaks these rules, a delta whose hash does not match,
  or a delta that reads outside its original is refused on import.
- What a mod changes (levels, strings, start square, music themes) is not stored: readers work
  it out from the original and the result, so it can never disagree with the bytes.

## The delta (`.uwd`)

Little-endian, numbers as unsigned LEB128 varints (`v`), signed ones zigzag-coded (`z`):

```
"UWD2"  v:baseLength  v:targetLength
op*
0x00                                   end
```

| Op | Fields | Writes |
| --- | --- | --- |
| `0x01` COPY | `z` offset (from the end of the previous COPY), `v` length | that run of the original |
| `0x02` ADD | `v` length, bytes | the bytes given (the mod's own content) |
| `0x03` BLOCK | `v` block number in the original archive, a mode byte (`1`: LZSS, `0`: plain), a nested delta (COPY and ADD, from the start of the block, ending with `0x00`) | UW2 archives only: the original block decompressed, the nested delta applied, then stored as the mode says (`Lzss.EncodeBlock` for LZSS) |

**Making a delta:** index the original's 16-byte windows by hash; walk the edited file taking
the longest match at each point (extended backwards over the last ADD), COPY for matches of 8
bytes or more, ADD for the rest. Files are at most about 1 MB (UW2's `T64.TR`), so this is fast.

**Why BLOCK:** UW2 stores most archive blocks LZSS-compressed (`LEV.ARK`, `CNV.ARK`,
`BYT.ARK`…). One changed object changes the compressed bytes from that point to the end of the
block, so a plain delta would carry most of the block recompressed, which is original content. BLOCK
diffs the decompressed block instead and recompresses on applying, with the same `Lzss` code
the editor saves with. Make a mod uses BLOCK for a block only when that encoding gives exactly
the edited file's bytes, and falls back to COPY/ADD otherwise. UW1's archives are not
compressed and need only COPY and ADD.

## The executable

- **DOS (the manager's normal case):** the executable's delta is applied like any file.
- **Underworld Exhumed (the ports):** the port never runs the executable. The manager applies
  the delta in memory and reads the start square from the result (`StartPosition.Read`), then
  passes it to the port (`--features` says whether the port can take it). Other changed bytes go
  into the port's patch file when they lie in the data it reads; any others are named as ones
  the port cannot play. A pending pull request to Underworld Exhumed adds a
  `--starting-coordinates` option; once merged, only `ExhumedGame` changes (the manager already
  passes the square as a setting, never as executable bytes).
- **UltimaHacks:** the manager builds the hacked executable from GOG's. A delta made against
  GOG's original does not fit it, so the manager accepts a mod's executable change with hacks
  on only when every byte it changes belongs to the start square, which it then writes into the
  hacked executable with `StartPosition.Patch`.

## Where it is used

| Who | What |
| --- | --- |
| `src/Underworld.Mods` (new, net10.0, references `Underworld.Formats`) | `Delta.Make/Apply`, `ModPackage.Read/Write/Make`, the path rules, the start-square reading. No UI. |
| The editor's Mods window | **Make a mod**: compares the game folder with its originals (the `.ORIG` copies, or a folder of originals) and refuses files that are new or missing, naming them. **Load a mod**: applies one with the same code. `Mods/ModPackage.cs` is removed. |
| `ModManager/` | Drops `Underworld.Shared` and the Unity checkout; uses `Underworld.Formats` and `Underworld.Mods`. |

Format 1 packages (this editor's and the Unity editor's) are not read: the program says the mod
was made by an older version.

## Testing (the reason for all this)

`tests/ModRoundTrip.Tests` (`RoundTrip.cs` is the harness). Each kind of change uses three
copies of a clean game. The change is made in one with the editor's core and saved (SafeFile,
so `.ORIG` copies are kept), then `RoundTrip.Check`:

1. makes the mod as **Make a mod** does, comparing both with the `.ORIG` copies and with the
   clean game (the two must agree), and names the files it expects;
2. checks the package carries nothing of the originals: no 32-byte run of what a delta adds is
   found in the original file (for UW2's archives, in its blocks decompressed);
3. imports it into the mod manager's library and installs it into the second copy (`DosGame`):
   every game file must be the edited one, byte for byte, with no changes left over;
4. switches back to the original game: every file must be the clean one again;
5. applies it with the editor's **Load a mod** to the third copy: the same files again;
6. prepares an Underworld Exhumed home over the clean game: the mod's files are there byte for
   byte, and a moved start square is in the home's settings.

The cases (UW1 and UW2 unless marked):

| File | Cases |
| --- | --- |
| `LevelTests` | a few objects added and deleted and a tile raised; many tiles and objects on every level |
| `RandomizerTests` | key item scatter (UW1; also with the start kit and explored maps), item shuffle, item swap (any, by kind), monster swap, named challenges (levels, automaps and the creature table) |
| `TableTests` | SHADES.DAT; OBJECTS.DAT with COMOBJ.DAT; COMOBJ.DAT alone; SKILLS.DAT, TERRAIN.DAT and CMB.DAT; DL.DAT (UW2); schedules in SCD.ARK (UW2) |
| `TextTests` | strings changed, lengthened and added |
| `GameFileTests` | the start square (and its delta of a few bytes); the start square with UltimaHacks (the hacked executable with the mod's square); UW2's new trap sound, a replaced effect and SOUNDS.DAT; UW1 speech replaced and added; a music theme in both versions; a conversation; an object picture, a critter frame, a cutscene frame; everything at once (the package at most a fiftieth of the files it changes) |

```sh
UW_REQUIRE_GAMEDATA=1 dotnet test tests/ModRoundTrip.Tests     # about 10 minutes
```

## Decided (2026-11-05)

- **Saved games are not mods.** The manager stops exporting and installing save slots; it still
  backs a slot up before changing anything and can restore it. Saves may come back later as a
  type of their own.
- **New sound files only where the game opens them by number.** First decided as "replace
  only"; the Exhumed sources then showed UW1 and UW2 build the speech and UW2 the effect and
  trap file names from numbers in their data, so those may be added (rule 1's exception). The
  sound window keeps "Add a trap sound"; any other new file cannot go into a mod.
- **Exact originals only.** A mod made on GOG's files refuses another release of the same game.
  Matching archive blocks one by one could lift that later without changing the format.

## Building it

1. `src/Underworld.Mods` and its tests: the delta (with BLOCK), the package, the rules. (Done.)
2. The editor's Mods window on it, `Mods/ModPackage.cs` removed, and the manual. (Done.)
3. The manager on it, with `Underworld.Shared`, the Unity checkout, save export and save
   install removed. (Done; the folder's originals are the editor's `.ORIG` first versions, not `.BKP`.)
4. The round-trip tests above. (Done: 46 cases.)
