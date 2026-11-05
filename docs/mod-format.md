# Mod format 2 (`.uwmod`): design

Status: **agreed**, being built (see "Building it" at the end). It replaces the editor's `Mods/ModPackage.cs` (format 1)
and the Unity editor's `Assets/Scripts/Modding`, which the mod manager builds against today.
Neither is carried over: format 2 is written from scratch in `src/Underworld.Mods` and shared by
the editor and `ModManager/`.

## Rules

1. **A mod changes files the game already has, and nothing else.** One game (UW1 or UW2), in
   its folder: the executable and the files under `DATA`, `CRIT`, `CUTS` and `SOUND`. A mod never
   adds, deletes or renames a file, and never touches a `SAVEn` folder. The game reads only the
   files it knows, so a new music theme replaces the bytes of the `.XMI` it goes into, and a
   new sound replaces an existing `.VOC`.
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

A zip:

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
block, so a plain delta would carry most of the block recompressed: original content. BLOCK
diffs the decompressed block instead and recompresses on applying, with the same `Lzss` code
the editor saves with. Make a mod uses BLOCK for a block only when that encoding gives exactly
the edited file's bytes, and falls back to COPY/ADD otherwise. UW1's archives are not
compressed and need only COPY and ADD.

## The executable

- **DOS (the manager's normal case):** the executable's delta is applied like any file.
- **Underworld Exhumed (the ports):** the port never runs the executable. The manager applies
  the delta in memory and reads the start square from the result (`StartPosition.Read`), then
  passes it to the port (`--features` says whether the port can take it). Any other change to
  the executable is named as one the port cannot play.
- **UltimaHacks:** the manager builds the hacked executable from GOG's. A delta made against
  GOG's original does not fit it, so the manager accepts a mod's executable change with hacks
  on only when every byte it changes belongs to the start square, which it then writes into the
  hacked executable with `StartPosition.Patch`.

## Where it is used

| Who | What |
| --- | --- |
| `src/Underworld.Mods` (new, net10.0, references `Underworld.Formats`) | `Delta.Make/Apply`, `ModPackage.Read/Write/Make`, the path rules, the start-square reading. No UI. |
| The editor's Mods window | **Make a mod**: compares the game folder with its originals (the `.ORIG` copies, or a folder of originals) and refuses files that are new or missing, naming them. **Load a mod**: applies one with the same code. `Mods/ModPackage.cs` goes. |
| `ModManager/` | Drops `Underworld.Shared` and the Unity checkout; uses `Underworld.Formats` and `Underworld.Mods`. |

Format 1 packages (this editor's and the Unity editor's) are not read: the program says the mod
was made by an older version.

## Testing (the reason for all this)

For each kind of change: copy a clean game twice; make the change with the editor's core in one
copy and save it; make a mod from it; install that mod with the manager's code (`DosGame`) into
the other copy; every file of the two copies must be equal byte for byte, and going back to
the original game must give back the clean files. The cases run from a few tiles and objects in
`LEV.ARK` through scatters, swaps and challenges to `SHADES.DAT`, `COMOBJ.DAT`, strings,
pictures, conversations, music, sounds and the start square, on UW1 and UW2 where they apply.
Each also checks that the package holds no original block or file.

## Decided (2026-11-05)

- **Saved games are not mods.** The manager stops exporting and installing save slots; it still
  backs a slot up before changing anything and can restore it. Saves may come back later as a
  type of their own.
- **The sound window only replaces.** It replaces the sound in a `.VOC` the game ships; adding
  and removing files goes, so everything it makes can go into a mod.
- **Exact originals only.** A mod made on GOG's files refuses another release of the same game.
  Matching archive blocks one by one could lift that later without changing the format.

## Building it

1. `src/Underworld.Mods` and its tests: the delta (with BLOCK), the package, the rules.
2. The editor's Mods window on it; `Mods/ModPackage.cs` and the sound window's add/remove go;
   the manual.
3. The manager on it: `Underworld.Shared` and the Unity checkout go, and so do save export and
   install.
4. The round-trip tests above.
