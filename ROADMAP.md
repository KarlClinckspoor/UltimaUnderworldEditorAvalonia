# Roadmap: the rest of the Unity editor's features

This file plans how the remaining features of the Unity editor
(KarlClinckspoor/UltimaUnderworldEditorUnity, branch `modding`) come to this editor. The feature
list comes from its manual (`Assets/StreamingAssets/documentation.txt`), its windows
(`Assets/Scripts/UI`, `Extensions/UI`) and the project notes.

The reference for what the files mean stays abedegno/underworld-exhumed. Whenever a ported rule
can be checked there, it is checked there and the source is cited in the code, as for the door
drawing in `ObjectModels.cs`.

Written 2026-10-31, after the viewer: levels in 3D with models, the object list, the
properties, the grid.

---

## 1. What the Unity editor has, and how hard each part is to port

"Unity refs" counts lines that use UnityEngine types in the files involved, which is a rough
measure of the porting work. 0 means the file can be copied as it is, like the Formats folder.

| Area | Unity editor | Main sources | Unity refs | Phase |
|---|---|---|---|---|
| Save, backups, autosave | write only what changed, through a temp file; `.BKP` originals; dated versions in `Editor backups`; autosave copies | `Data/GameWrite.cs`, `Extensions/UI/Autosave.cs` | 1 | A |
| Level writing | rebuild the level block, free lists, active list, UW1 overlay block, UW2 timers; check before crunching | `Data/LevArk.cs` (save half), `Data/LevelData.cs` | 2 | A |
| Object list service | add, remove and move objects; keep chains and free lists right | `Data/StaticObject.cs`, `MobileObject.cs`, `MapTile.cs`, `ObjectRecord.cs` | 13 | A |
| Undo | 30 steps; moves, deletes and container changes only | `CommandList.cs` | — | A (all edits) |
| Object mode | select; drag to move (5-pixel threshold, ghost); drop into containers, critters, doors, wands, levers; traps keep their data positions | `MapCreator.cs`, `StaticObjectScript.cs` | many | B |
| Context menus | copy, remove, add trigger, inventory, link trigger/trap/lever; on a tile: add object, add move trigger, add trap, paste | `UI/StaticObjectPanel.cs`, `TopBar` | many | B |
| Properties panel | editable fields with ranges; "Linked from"; trigger target squares with go-to buttons and magenta crosshairs | `UI/PropertiesPanel.cs`, `StaticObjectPanel.cs` | many | B |
| Tile mode | select; paint a template (type, height, flags) with the right button; wheel changes height and moves objects and doors with it | `UI/TilePanel.cs`, `TileProperties.cs` | many | B |
| Texture mode | paint floor and/or wall textures | `UI/TextureExplorer.cs` | many | B |
| Add object | item picker with categories | `Uw1ItemFilter.cs`, `Games/Uw2Items.cs` | 0 | B |
| UW2 empty slots | "make this slot a level" | `LevArk.EmptyBlock` | 0 | B |
| Player start | new-game square in the executable; player position in a save | `StartWindow.cs`, `PlayerWindow.cs`, `Formats/StartPosition.cs` | few | B |
| Object meaning | roles of quality/owner/link per item; trap and trigger descriptions; create menus | `Uw1ObjectRoles.cs`, `Uw1Traps.cs`, `Games/Uw2Traps.cs` | 0 | C |
| Object fields inspector | every bit with meaning and certainty | `FieldInspectorWindow.cs`, `FieldTooltips.cs` | few | done (panel); C (editing) |
| Triggers and traps of a level | every trigger, its target and chain; unreached traps | `TriggerMapWindow.cs` | few | C |
| Find items in all levels | search by item, owner, quality, enchantment | `ItemFinderWindow.cs` | few | C |
| Fixes | list checks with automatic fixes; item cleaner | `Data/LevelFixes.cs`, `FixesWindow.cs` | 0 / few | C |
| Object tables | OBJECTS.DAT, COMOBJ.DAT editor | `UI/ObjectEditor`, `Formats/Objects` | some | D |
| Terrain | TERRAIN.DAT floor/wall editors | `Formats/Uw1/Uw1SmallFiles.cs` | some | D |
| Keys, mantras | key descriptions (owner = lock number), UW1 shrine mantras | `KeyEditorWindow.cs` | few | D |
| Class skills, combinations, light | SKILLS.DAT, CMB.DAT, SHADES.DAT; UW2 DL.DAT + light flags | `ClassSkillsWindow.cs`, `CombinationsWindow.cs`, `ShadesWindow.cs`, `LightWindow.cs` | few | D |
| Level texture lists | which floors, walls and doors a level uses | `TextureExplorer.cs`, `Data/TextureData.cs` | 3 | D |
| Pictures | replace textures, doors, levers, objects and portraits with PNGs (palette matching); export to PNG | `Data/GrPictures.cs`, `Uw1TrFiles.cs`, `Uw2T64.cs`, `Formats/Graphics` | 17 | D |
| Built-in 3D models | viewer and OBJ export | `ModelViewerWindow.cs`, `Formats/Uw1ExeModels.cs` | few | D |
| Strings | every block of STRINGS.PAK; texts of books, scrolls and traps follow their objects | `Data/StringData.cs`, `Formats/Strings` | 1 | E |
| Conversations | script language, compiler to CNV.ARK, node editor, player/simulator with debug, NPC names, saved-variable names, barter strings | `Data/Conversation.cs`, `ConversationConverter.cs`, `CnvArk.cs`, `UI/Conversations` | 25 + 190 (UI) | E |
| Saved games | slots, rename, BGLOBALS variables by name | `SaveSlotsWindow.cs`, `VariablesWindow.cs`, `Formats/Games/BGlobals.cs` | few | F |
| Character, quest flags | PLAYER.DAT fields, position, quest flags and variables | `PlayerWindow.cs`, `Formats/Games/PlayerDat.cs` | few | F |
| UW2 schedules | SCD.ARK rows by clock | `Games/ScdArk.cs`, `ScheduleWindow.cs` | 0 / few | F |
| Automap and notes | paint the map, notes, map scraps | `AutomapWindow.cs`, `Formats/Games/Automap.cs` | few | F |
| Mods | export a `.uwmod` (also a saved game alone) | `Assets/Scripts/Modding`, `ModExportWindow.cs` | 0 / few | G |
| Clear levels | empty all levels (and conversations) | `MapCreator.cs`, `LevArk.EmptyBlock` | few | H |
| Randomizer, zones | sector mode, zone audit, graph and solver (work in progress, UW1 only) | `Extensions/Core/Zoning`, `Randomizer`, `ZoneWindow.cs`, `ZoneGraphWindow.cs` | 0 / some | H |
| Walk around | first person at eye height | `Extensions/View` | — | done as the orbit view; I (walk mode) |

The manual's reference chapters on triggers, traps, variables and recipes (7 to 13) are
documentation. They become the help text behind the descriptions in phase C.

---

## 2. Foundations for editing (phase A, about 6–8 days)

Everything after this phase edits. So editing, undo and saving are built once, here, and the
features only describe their changes.

### The document

- **The bytes are the truth.** Each loaded level keeps its `LevelBlock` (and texture block) as
  bytes, as now. `TileInfo` and `ObjectInfo` stay read-only views, rebuilt for what an edit
  touched. Whatever the editor does not model stays as it was, the rule the Unity editor
  already follows.
- **`GameDocument`** (Core) holds the open game: the levels touched so far, and the other
  files once they can be edited (STRINGS.PAK, CNV.ARK, the tables, the pictures, the save's
  files). Each knows whether it changed.

### Edits and undo

- An edit is a **command**. It changes the bytes through `FieldDef.Set` and the list operations,
  and records the byte ranges it changed (before and after).
- Undo and redo replay those ranges: exact, cheap, and the same for every kind of edit. The
  Unity editor could not undo field edits or tile painting; here everything can be undone.
  Keep 200 steps.
- After any command, the views of what it touched are rebuilt, and the 3D scene for the tiles
  around it. The renderer already takes a new mesh at any time; for big levels, rebuild only
  the changed tiles' part (later, if needed).

### Object list operations

Port from `StaticObject`, `MapTile` and `LevArk` into Core `ObjectLists`, written on the block
bytes:
- allocate from and free to the mobile and static free lists (`Obj_Alloc`, `Obj_Free`,
  obj/OBJECTS.C);
- link into and unlink from a tile's chain or a parent's link list;
- move between tiles;
- copy with contents;
- delete with contents (a trigger's trap chain; a book's text is freed only when no other
  object uses it);
- keep the active critter list (MobilesOnTiles) and UW2's timer list.

Every operation ends with `LevelBlock.Check` passing, asserted in tests.

### Saving

- **Port `GameWrite`:**
  - write only changed files, through a temporary file;
  - `.BKP` original;
  - dated versions in `Editor backups`;
  - the `.BKP` follows the game in a save.
- **Port the save half of `LevArk`:**
  - rebuild the level block;
  - the UW1 overlay block;
  - the texture block;
  - write only changed blocks;
  - crunch free records only after the check passes.
- **Autosave:** to copies, as the Unity editor does.
- **File menu:** Save (Ctrl+S); asking to save when opening another game or closing.

### Tests

These come from the Unity editor's `Tests/Formats.Tests`, where they exist:
- opening and saving with no edits leaves every file byte-identical, in both games;
- an edit, then undo, gives the original bytes back;
- every list operation keeps `Check` passing;
- the parity tests of the Unity repo for the save path (SaveChangedOnly, Saving, EditingSafety).

---

## 3. Editing in the main view (phase B, about 8–10 days)

### Modes

A small toolbar above the 3D view: **Select / move**, **Paint tiles**, **Paint textures**.
Number keys switch between them. The left panel's lower part shows the mode's settings (the
tile template, the texture brush).

### Objects

- **Editable properties.** The rows become editors: numbers with the field's range
  (`FieldDef.Min`/`Max`), check boxes for single bits, a picker for the item id. Each change
  is a command.
- **Drag in 3D.**
  - The mouse ray meets the floor of the tile under it (`LevelModel.FloorAt`). The object
    moves to that tile and its fine position, and a ghost shows where it lands.
  - The drag starts after 5 pixels, so a click never moves anything.
  - Height: Alt+drag, or the Z field.
  - Traps whose fine position is data keep it and change tile only; the Unity editor has the
    list.
- **Dropping onto an object.**
  - A container, critter, door, wand or lever under the drop point takes the object inside it.
  - What each one accepts is ported from `MapCreator`'s drop rules.
- **Context menu** (right click without dragging; the right drag still orbits):
  - On an object: Copy, Paste, Remove, Add trigger, Link trigger/trap/lever, and an Inventory
    submenu.
  - On a tile: Add object…, Add move trigger, Add trap…, Paste.
- **Add object.** An item picker with the Unity editor's categories (`Uw1ItemFilter`,
  `Uw2Items`). It shows the object pictures, has a search box, and places the object on the
  clicked tile.
- **Links on the map:**
  - "Linked from" and "Acts on" lists in the properties panel, each with a go-to button.
  - The target squares of a selected trigger or trap drawn as magenta crosses, with a line
    from the object.

### Tiles

- **Selection.** Click a tile; Shift+click or drag adds more, for painting and editing several
  tiles at once.
- **Painting.**
  - The tile template (type, height, door bit, no-magic, light) is set in the left panel.
  - Click or drag paints it.
  - The wheel over a selected tile changes its height. Objects lying on the floor move with
    it, and doors are placed again (the Unity editor's rule).
- **Texture painting.**
  - The brush shows the level's floor and wall lists as thumbnails.
  - Click a floor or wall face to paint it. The pick already knows whether a triangle is a
    floor, a wall or a ceiling.
- **A 2D map view.** An orthographic top-down camera, toggled with Tab, with the same picking
  and painting. It is the quickest way to paint many tiles, and it is how the Unity editor
  looks by default. It needs only another projection.

### The rest of phase B

- **Start position:**
  - Tools ▸ New game start square (executable patch with a `.BKP`).
  - The player's position in a save.
  - Both can take the selected tile or be dragged in the view; the magenta marker is already
    drawn.
- **UW2 empty slots:** "Make this slot a level" (`LevArk.EmptyBlock`).

---

## 4. What objects mean (phase C, about 5–7 days)

- **Copy the meaning code** into Core: `Uw1ObjectRoles`, `Uw1Traps`, `Uw2Traps`, `Uw2Items`,
  `Uw1ItemFilter`, `LevelFixes`. They have no Unity references. Give each a test on the
  shipped levels; the Unity notes have the expected figures (1,262 UW2 traps decode, 53 have
  nothing to decode).
- **Properties panel:**
  - a "Meaning" section above the raw record: what quality, owner and link do for this item
    (lock number, key, text number, enchantment, trap settings);
  - for a trap or trigger, the decoded description and its own editors (teleport destination,
    variable and value, text with its string, damage, create-object template).
- **Triggers and traps window:** every trigger, the square its chain runs on, the chain, and
  the traps no trigger reaches. A click selects and frames the object.
- **Find in all levels:** item, owner, quality and enchantment filters over every level. The
  results are listed, and a click opens the level and frames the object.
- **Fixes window:** the checks, with a fix and "fix all" for each, plus the item cleaner and
  its protected categories. All of it runs as commands, so it can be undone.

---

## 5. Data tables and pictures (phase D, about 7–9 days)

Each of these is a separate, non-modal Avalonia window from the Tools menu. They use the
toolkit's grids, lists and pickers, which is why Avalonia was chosen.

- **Object tables:**
  - OBJECTS.DAT and COMOBJ.DAT in a grid with one row per item and sections for weapons,
    armour, critters, containers and so on;
  - names edited through STRINGS.PAK (block 4: "article_name&plural").
- **Terrain:** TERRAIN.DAT (UW1 per floor and wall; UW2 one table of 256 for T64.TR).
- **Keys and mantras:** the key editor with "find a key or lock with this number"; UW1
  mantras.
- **Class skills, combinations, light levels:**
  - SKILLS.DAT, CMB.DAT and SHADES.DAT, with the Unity editor's explanations;
  - the light-level preview, drawn with the GL renderer's textures.
- **UW2 level light:** DL.DAT's minimum light, and the light flag shown on tiles (blue tint in
  the 3D view), painted in tile mode.
- **Level texture lists:**
  - which floors, walls and doors the level uses;
  - change an entry, and the 3D view updates: the layers are rebuilt from the texture map.
- **Pictures:**
  - replace a texture, door, lever, object picture or portrait from a PNG, matching colours to
    the palette (port `GraphicsCodec` use from `GrPictures`/`Uw1TrFiles`/`Uw2T64`; `GrFile`
    rebuilds .GR files);
  - export to PNG;
  - only replaced pictures are written; sizes are checked as before.
- **Built-in models:** a viewer window (the GL renderer with one model and an orbit camera)
  and OBJ export (`Uw1ExeModels.ToObj` exists).

---

## 6. Strings and conversations (phase E, about 10–14 days)

This is the biggest port.

**Done already (2026-10-31):**
- Reading and writing CNV.ARK.
- Editing a conversation's strings.
- The code as assembly, with an exact round trip.
- A readable stack-simulated view.
- Who talks through each slot.
- The global variables window: quests, game variables, X clocks, skills, the talker's and
  player's variables, private globals; their uses; values edited in saves.

What is left below is the high-level language, the graph and the simulator.

- **Strings window:**
  - every block, searchable, editable;
  - Huffman encoding with the loaded tree (UW2's extra characters);
  - texts of books, scrolls, writings and text traps are created and freed with their
    objects (phase A's list operations call it).
- **The conversation compiler:**
  - `ConversationConverter.cs` (3,165 lines) has 2 Unity references;
  - `Conversation.cs` (1,818 lines) has 23, mostly colours, rectangles and serialization for
    the node editor.
  - Split them: the language, compiler and decompiler go into Core unchanged in behaviour,
    tested against the Unity editor's ConversationSafetyTests and a round trip of every
    shipped conversation in both games (UW2's import table and remapped function ids
    included).
- **Two editing views for one conversation:**
  - **Script.** The language as text, in AvaloniaEdit: syntax colouring, errors at their line
    from the compiler, folding.
  - **Graph.** The node editor (nodes, connections, conditions) rebuilt on an Avalonia canvas.
    It is the Unity editor's main way of editing, and it maps onto the same model.
  - Both edit the same model. Switching between them recompiles.
- **Player / simulator:**
  - runs a conversation as the game would;
  - asks for the values only the game knows (inventory, quest flags, clocks);
  - single step, debug log, reset of saved variables, start parameters.
- **NPC names, saved-variable names, barter strings:** in the conversation's side panel.

---

## 7. Saved games, mods, the rest (phases F–I, about 8–10 days)

- **F, saved games:**
  - slots, with their names editable;
  - BGLOBALS variables by name, record resizing;
  - the character (PLAYER.DAT: name, attributes, skills, vitality, mana, level, experience,
    position);
  - quest flags and variables (UW1 flags 0–31, quests 32–35, 64 variables; UW2 flags 0–127,
    quests 128–143, 256 variables, 16 X clocks), known meanings named;
  - UW2 schedules (copy `ScdArk.cs`; a grid of rows by clock, kept in time order);
  - automap and map notes (`Automap` is in Formats: paint seen/known/erase, reveal from the
    geometry, notes, UW2 map scraps).
- **G, mods:** copy `Assets/Scripts/Modding` as Formats was copied. Export to `.uwmod` (base:
  the `.BKP` files or another folder; also a saved game alone), with the verify step.
- **H, levels and zones:**
  - Clear levels;
  - the randomizer and zone tools are work in progress in the Unity editor and UW1-only. They
    come last, behind an option, once they are finished there.
- **I, walk mode:**
  - a first-person camera at eye height in the 3D view, using the walk view's collision
    (`CanStand`);
  - F switches between walking and flying;
  - with the current renderer this is a camera change.

---

## 8. Order, sharing and risks

- **Order:** A → B → C first; that makes a working level editor. D, E and F are independent
  of each other after that, so they can be done in whatever order the work needs. E is the
  largest.
- **Shared code:**
  - Each Unity-free file ported from the Unity editor (the 0-ref files above) is copied as
    Formats was, and noted in its folder's SOURCE.md.
  - When the two editors stop changing those files often, they move to one shared source, as
    PLAN.md §3 describes.
  - Fixes made here are made in the Unity editor too while both live.
- **Tests:** the Unity repo's test suites (parity, saving, editing safety, conversations, game
  facts, restructure) are the yardstick. Each phase brings over the tests for what it ports,
  and all tests run on both games' shipped data.
- **Risks:**
  - The object list operations are where the Unity editor had its worst bugs (removing a
    critter corrupting the active list, free lists past 514 statics). Port them with their
    regression tests first, before any UI uses them.
  - The conversation node editor is a rewrite, not a port.
  - Windows and macOS have not been run yet (PLAN.md §6).

Total: roughly 50–65 working days for everything, with a usable level editor (A–C) after
about 20.
