## Saved games

**File ▸ Saved games…** lists the game folder's four save slots, `SAVE1` to `SAVE4`, with the
description the game's save menu shows (the slot's `DESC` file; "<not used yet>" when it
has none). A saved game is a copy of the game's working folder `SAVE0`: `LEV.ARK` (with the
maps and notes), `PLAYER.DAT`, `BGLOBALS.DAT`, `DESC` and, in Underworld II, `SCD.ARK`.

- **Rename** writes the description: plain characters, at most 30, as the game takes them
  (`DESC.BKP` keeps the old one).
- **Copy to** copies every file of the slot to another slot. When that slot already holds a
  save, the editor asks first and keeps the old one as `SAVEn.BKP` (replacing an older
  backup). The slot open in the editor cannot be replaced; open `DATA` or another slot first.
  Copying the open slot copies it as it is on disk, without unsaved level edits.
- **Open** opens the slot in the editor, asking about unsaved work first.
