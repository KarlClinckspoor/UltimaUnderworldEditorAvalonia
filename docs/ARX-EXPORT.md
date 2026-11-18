# Export of UW1 levels for ArxStructor

The editor writes Ultima Underworld 1's levels as a folder of JSON and PNG files that
ArxStructor (the Arx Fatalis level editor) converts into Arx levels. This is the answer to
ArxStructor's request of 2026-11-18: stages 1 (geometry and textures) and 2 (objects, doors,
triggers, links, creatures, classes, sprites, strings). Stage 3 (conversations) is not done.

The code is `src/UwEditor.Core/Export/ArxExport*.cs` (entry point `ArxExport.Run(session,
folder, level)`), the command `src/UwEditor.Cli`, the tests
`tests/UwEditor.Core.Tests/ArxExportTests.cs`.

## Running it

```
UwEditor.Cli export-arx --game <UW folder> --out <folder> [--level N]
```

- `--game`: the game folder (with DATA), the DATA folder, or anything `GameSession.Open`
  accepts. `--level N`: only level N (1-9); without it every level.
- Exit code 0 when written; 1 when the export failed (no game there, no such level, a
  file that cannot be read or written, a UW2 game); 2 for a wrong command line. The reason
  goes to standard error. On success it prints one line per level and the file count.
- The output folder is made if missing. Files of the same name are replaced, other files
  left alone.
- The same game files give the same output, byte for byte: keys in a fixed order, lists in
  a fixed order, numbers in the invariant culture (shortest form that reads back the same),
  UTF-8 without a byte order mark, LF line ends, no time stamps anywhere. The manifest's
  `editorVersion` is the version without the commit, so two builds of one version agree.
- The game files are only read. (Opening a game removes temporary files a crashed editor
  left in DATA, as the editor always does.)
- UwEditor.Cli is built with the solution but is **not yet in the release packages**.
- The export holds the game's own pictures and texts: keep it out of any repository.

A real export of the GOG UW1 files: level 1 alone is 792 files, 5.5 MB (level1.json 3.6
MB); all nine levels are 800 files, 36 MB.

## Folder layout

```
<out>/
  manifest.json
  levels/level1.json ... level9.json
  textures/walls/NNN.png      W64.TR, 64 x 64     (NNN_fK.png: palette-cycle frames)
  textures/floors/NNN.png     F32.TR, 32 x 32
  textures/doors/NNN.png      DOORS.GR, 32 x 64
  textures/textures.json
  objects/classes.json
  objects/sprites/NNN.png     OBJECTS.GR, by item number
  strings.json
```

`NNN` is the texture's number in its file, three digits (the number `WallMap`, `FloorMap` and
`DoorMap` hold), or the item number for sprites.

## Conventions, as numbers

| Thing | Value | Source |
| --- | --- | --- |
| Grid | 64 x 64 tiles; x runs east, y runs north; tile index = y * 64 + x | LevelModel |
| Tile size | 1.0 | LevelGeometry |
| Floor height | 0-15 steps, one step 0.25 tile | `LevelGeometry.Step` |
| Ceiling | height 16 = 4.0 tiles, flat, everywhere in UW1 | `LevelGeometry.CeilingHeight`, `LevelModel.HasCeiling` |
| Slope | rises one step across the tile towards the direction in its name | `LevelGeometry.CornerHeights` |
| Diagonal tile | open in the half its name gives (OpenSE: the south-east half), a wall along the diagonal | `LevelGeometry.EdgeOpen`, `FloorCorners` |
| Object in a tile | `(fine + 0.5) / 8`, fine 0-7 | `ObjectInfo.WorldX/WorldY` |
| Object height | `z / 32` tiles: 8 units a floor step | `ObjectInfo.WorldZ` |
| Heading | 0-7, 45 degrees a step, 0 north, clockwise seen from above (2 east, 4 south, 6 west) | question 1 |
| Mesh axes | x east, y up, z north, in tiles: a left-handed set, drawn unmirrored | LevelGeometry, OrbitCamera |

The manifest's `units` writes these out.

**Winding.** Every triangle's front faces the open space: floors up, ceilings down, walls
into the tile they belong to. Seen from the front, in the game's own unmirrored view (x east
to the right, z north away from you, y up), the three vertices run counter-clockwise. In plain
numbers: `cross(v1 - v0, v2 - v0)` computed on (x, y, z) points **away** from the open space
(down under a floor, up above a ceiling, into the rock behind a wall); negate it for the
front normal. If you load the coordinates into a right-handed program unchanged, the picture
is mirrored and the same triangles read clockwise from the front.

**Texture coordinates.** v = 0 is the top row of the PNG; v grows downward.

- Walls: u runs 0 to 1 across the tile edge, the texture's left column on the left of a
  viewer standing in the open tile (the same picture reads the same from every side). v is
  measured in tiles down from the face's top edge: 0 at both top corners, the face's mean
  height at both bottom corners. The texture is one tile high and repeats.
- Floors: u = world x; v = world y, so the texture's top row lies along the tile's **south**
  edge and its left column along the west edge (seen from above with north up, a floor shows
  its texture upside down; `LevelGeometry.FloorTextureRow`, from 3d/GRIDDB.C). One unit of u
  or v is one tile, and the texture repeats every tile.
- Ceilings: u = world x; v = -(y + 1) + (1 - northing), so the top row lies along the north edge.

## manifest.json

| Field | Meaning |
| --- | --- |
| `format` | `"uw-arx-export"` |
| `formatVersion` | 1 |
| `game` | `"uw1"` |
| `editorVersion` | the editor's version (`0.1.56`) |
| `levels` | the levels written |
| `units` | the conventions above |
| `doorModels` | extents of UW.EXE's door models in tiles (question 5), or null without UW.EXE |
| `sourceFiles` | each game file read: `name` (path under the game folder), `size`, `sha256` |
| `files` | each file written except the manifest: `path`, `size`, `sha256` |

## levels/levelN.json

Keys in this order: `level`, `tiles`, `textureMap`, `mesh`, `summary`, then stage 2:
`objects`, `doors`, `links`, `triggers`, `trapsWithoutTrigger`, `tileChanges`,
`playerStart`, `lights`, `standable`. Indexes are object indexes of the level (0-1023; 0-255
mobile). "-1" means none where an index is expected (`parent`, `holder`), as in LevelModel.

### tiles

All 4096, y then x, one per line.

| Field | Meaning |
| --- | --- |
| `x`, `y` | the tile |
| `type` | `Solid`, `Open`, `OpenSE`, `OpenSW`, `OpenNE`, `OpenNW`, `SlopeUpN`, `SlopeUpS`, `SlopeUpE`, `SlopeUpW` (a stored type above 9 reads as Solid, as LevelModel does) |
| `height` | 0-15 |
| `floorIndex`, `wallIndex` | as stored (floor 4 bits, wall 6 bits) |
| `floorTexture`, `wallTexture` | the texture numbers through the level's maps; null for an index the map lacks (UW1 floor 10-15, wall 48-63) |
| `light`, `noMagic`, `doorBit` | as stored (`light` is the 2-bit field) |
| `firstObject` | as stored |
| `record` | the 4 bytes, lower-case hex |

### textureMap

`walls` (48 numbers of W64.TR), `floors` (10 of F32.TR), `doors` (6 of DOORS.GR),
`ceilingFloorIndex` (9 in UW1), `ceilingTexture` (the floor texture number it gives), and
`raw` (the 0x7A bytes of the level's texture map block, hex).

### mesh

The level's surfaces exactly as `LevelGeometry.Build(level, ceiling: true)` makes them for the
3D view, with the per-triangle facts added (the renderer's mesh is unchanged).

- `vertexFields`: `["x", "y", "z", "u", "v"]`.
- `vertices`: one `[x, y, z, u, v]` per line. Not welded: triangle i uses vertices 3i, 3i+1, 3i+2.
- `triangles`, one per line:
  - `v`: its three vertex numbers;
  - `tile`: `[x, y]`;
  - `kind`: `floor`, `wall` or `ceiling`;
  - `texture`: `{"set": "walls" | "floors", "number": N}`, resolved through the level's maps
    (null when the index has no texture; never in the shipped levels);
  - `edge` (walls only): `W`, `E`, `S`, `N` or `diagonal`, the side of `tile` it stands on.
- Order: tile by tile, y then x; within a tile, as Build makes them (floor and ceiling
  triangles, then the diagonal wall, then the W, E, S, N walls).
- No merging, no subdivision. A floor is one triangle (diagonal tiles) or two; a wall face is
  two triangles. A wall face whose one end has no height (beside a slope) gives a triangle of
  no area: they are kept as Build makes them (52 of level 1's 4390 wall triangles).
- Floors and ceilings: one texture each per tile, the ceiling the level's floor index 9.
- Walls: see question 2. Doors, door frames and other objects are **not** in the mesh; they
  are objects (`doors`, `objects`).
- Left out: the renderer's per-vertex shade and texture layer (editor-only).

### summary

`openTiles`, `solidTiles`, `tilesByType` (each type name), `triangles` (`floor`, `wall`,
`ceiling`, `total`), `vertices`, `trianglesWithoutTexture`, `lowestFloor` and
`highestFloor` (of the open tiles), `objects`, `mobileObjects`, `staticObjects` (those not on
a free list).

| Level | open | triangles | floor | wall | ceiling | floors | objects | doors | links | triggers | tile changes |
| --- | --- | --- | --- | --- | --- | --- | --- | --- | --- | --- | --- |
| 1 | 2036 | 12018 | 3814 | 4390 | 3814 | 0-13 | 534 | 33 | 3 | 22 | 4 |
| 2 | 1995 | 12334 | 3792 | 4750 | 3792 | 0-13 | 526 | 24 | 6 | 31 | 1 |
| 3 | 1935 | 11502 | 3754 | 3994 | 3754 | 0-13 | 563 | 35 | 8 | 44 | 7 |
| 4 | 1922 | 11752 | 3676 | 4400 | 3676 | 0-13 | 581 | 35 | 9 | 27 | 11 |
| 5 | 2002 | 12812 | 3895 | 5022 | 3895 | 0-12 | 583 | 22 | 11 | 40 | 2 |
| 6 | 2101 | 12732 | 3928 | 4876 | 3928 | 0-13 | 507 | 15 | 6 | 16 | 3 |
| 7 | 2224 | 14336 | 4217 | 5902 | 4217 | 0-13 | 608 | 48 | 10 | 92 | 11 |
| 8 | 2294 | 14694 | 4572 | 5550 | 4572 | 0-12 | 622 | 4 | 6 | 15 | 1 |
| 9 | 3010 | 14212 | 6020 | 2172 | 6020 | 0-12 | 220 | 0 | 5 | 5 | 0 |

### objects

Every object not on a free list (`LevelModel.ReadObjects`), by index, one per line.

| Field | Meaning |
| --- | --- |
| `index`, `mobile`, `item`, `name` | `ObjectInfo.Index`, `IsMobile`, the item number, `GameSession.Names` |
| `category` | `ItemCategories.Of` (see classes.json) |
| `tileX`, `tileY` | the tile whose list holds it, directly or through its holder; -1 when no tile reaches it |
| `fineX`, `fineY`, `z`, `heading` | as stored |
| `worldX`, `worldY`, `worldZ` | the position in tiles for an object lying on its tile; null inside a container or creature (its stored position means nothing there) |
| `quality`, `owner`, `link`, `next`, `isQuantity` | as stored |
| `invisible`, `enchanted` | flags (bits 14 and 12) |
| `flags` | bits 9-12 as one number (a door's swing, a trap's trigger count, a lever's position...) |
| `tenacious` | bit 13 |
| `parent`, `children`, `onTile` | the containment LevelModel resolves (a container's contents, a creature's inventory, a trigger's trap, a door's lock, a lever's trigger) |
| `heightIsData`, `finePositionIsData` | for traps and triggers, z or fine x/y are settings, not a place (`Triggers.HeightIsData`, `FinePositionIsData`) |
| `text` | a book's, scroll's, writing's, gravestone's or text trap's text, when it has one (`ObjectTexts.TextOf`) |
| `mobileFields` | mobile objects: every field of the 19 mobile bytes, under `Uw1Schemas.MobileObject`'s names in camelCase (`hp`, `goal`, `goalTarget`, `attitude`, `homeX`, `homeY`, `whoami` (the conversation slot), `destinationX`...) |
| `goalName`, `attitudeName`, `npcName` | creatures: the goal and attitude in words, the name it talks under |
| `record` | the record, hex (8 bytes static, 27 mobile) |

A creature's `quality` and `owner` are its home tile as the conversations see it; its
`link` its inventory. Every field's meaning by item kind is in docs/object-fields.md and the
editor's properties panel.

### doors

Each door object (0x140-0x14F), as the 3D view draws it (`ObjectModels.Build`, 3d/DRAWOBJ.C
do_door):

| Field | Meaning |
| --- | --- |
| `index`, `item`, `name` | the object |
| `kind` | `door` (item & 7 = 0-5), `portcullis` (6), `secret door` (7) |
| `tileX`, `tileY`, `fineX`, `fineY`, `worldX..Z` | where it stands |
| `heading`, `headingDegrees` | its turn |
| `plane` | `east-west` for heading 0 or 4 (the door spans x and closes a north-south passage), `north-south` for 2 or 6 |
| `open` | item 0x148-0x14F |
| `swing` | bits 9-11: how far open it is drawn turned |
| `texture` | the leaf: `doors` DoorMap[item & 7]; a secret door the tile's wall texture; null for the portcullis (a plain-coloured model) |
| `frameTexture` | the frame is drawn in the tile's wall texture |
| `lock`, `locked`, `keyNumber`, `pickDifficulty`, `relocks` | the lock object it holds (0x10F, `LevelEditor.LockOf`): flag 9 locked, the number keys open (link & 0x1FF), pick difficulty (its z), flag 10 stays after unlocking. A key opens the lock whose number equals its owner field (`KeyIndex`) |
| `spiked` | `DoorSpikes.Holds` |
| `opensWith`, `notes` | `ZoneDoors.Requirement`: `free`, or alternatives such as `key:2 \| pick:2 \| bash` |

### links

The ways out of the level and across it. UW1 has no stair object: a stairway is a move
trigger whose chain reaches a teleport trap (0x181) whose z names another level
(`Randomizer/NavRegions.cs` makes its "stairs" edges the same way).

| Field | Meaning |
| --- | --- |
| `kind` | `level` (to another level), `teleport` (within the level), `end of game` (hack trap 0x3F) |
| `trigger`, `triggerName`, `tileX`, `tileY`, `holder`, `playerSetsOff` | the trigger that runs it and where it lies; null trigger for a teleport no trigger reaches (then the trap's own tile) |
| `trap` | the teleport (or hack) trap |
| `toLevel`, `toX`, `toY` | where it sends the player |
| `checks` | the check variable traps before it in the chain: it may only work when they pass |

### triggers

Each trigger on the level (`Triggers.TriggersOf`), with `type` (Move, Pick up, Use, Look, Step
on, Open, Unlock trigger), where it lies, `holder` (the lever or door holding it, or -1),
`targetX`/`targetY` (the whole chain runs at this square, not where the trigger lies),
`playerSetsOff` (flag 11), `creaturesSetOff` (enchanted), `reusable` (flag 10), `mode`
(OBJECTS.DAT's trigger table) and `chain`: the traps in order (`Triggers.Chain`; a trigger met in
a chain ends it there and runs its own chain, listed under that trigger). Each step: `index`,
`item`, `name`, `type` (the trap's name: Damage, Teleport, Arrow, Do (special), Pit, Change
terrain, Spell, Create object, Door, Ward, Tell, Delete object, Inventory, Set variable, Check
variable, Combination, Text string), `runsAtX/Y`, `summary` (`Triggers.Short`), `params`
(`TypedFields.For`: `name`, `key`, `value`, and `meaning` where the value is a choice) and,
by type: `area` and `sets` (change terrain), `destination` (teleport), `special` (hack),
`object`/`objectName` (create and delete object), `door` (door trap: the door on the run
square), `text` (text string). The raw values are in `objects`. `trapsWithoutTrigger`: the
traps no trigger's chain reaches (`Triggers.Unreached`), in the same form.

### tileChanges

The tiles that can change in play (question 7): `kind` `change terrain` with `trap`,
`triggers`, the rectangle `x`, `y`, `w`, `h`, `sets` (`type`, `height`, `floorIndex`,
`floorTexture`, `wallIndex`, `wallTexture`; null for what it keeps) and `text`; and the
special traps that move a floor, `eight-position switch height` (hack 3) and `bullfrog
puzzle` (hack 0x18), with the `squares` they act on.

### playerStart

On level 1 only (null elsewhere): `level`, `tileX`, `tileY`, `x`, `y` (the middle of the start
square, read from UW.EXE: `Games/StartPosition.cs`), `z` (its floor, tiles), `headingDegrees`
null (unknown, see question 1), `note`, `description`.

### lights

The light sources 0x90-0x97 (candle, torch, taper, lantern; 0x94-0x97 lit) with `lit` and
`brightness` (OBJECTS.DAT's light table: lit candle 1, torch 2, taper 3, lantern 4; unlit 0),
on a tile or carried (`onTile`, `parent`). In UW1 a light lying in the level lights nothing
round it: the 3D view's light comes from what the player carries and his spells only
(`GameLight.cs`, map/LIGHTING.C). Tiles have no light value beyond the 2-bit `light` field
(bit 8 tested when the player moves, bit 9 unknown). Deviation: COMOBJ.DAT's "Light" bit is
not used, because in UW1 it is set on the missiles (0x10-0x17, 0x151, 0x152) and on no light.

### standable

Objects the player can stand on (question 6): `index`, `item`, `name`, `tileX`, `tileY`,
`radius` (fine units; 4 = the whole tile), `wholeTile`, `bottom`, `top` (z units, 8 a step),
`topTiles`.

## textures/textures.json

- `palette`: 256 `[r, g, b]`, PALS.DAT palette 0 (the 3D view's), the 6-bit values scaled as
  `value * 255 / 63` rounded down (`GameGraphics.Load`).
- `paletteCycles`: the cycled palette ranges (question 8).
- `textures`, one per line: `set`, `number`, `file`, `width`, `height`, `terrain` and
  `terrainWord` (TERRAIN.DAT; null for doors), `animated` (false, or `frames`,
  `frameSeconds`, `cycledPixels`, `files` (frame 0 is the plain file), `paletteRanges`),
  `transparent`, `transparentIndex`.
- PNGs are 8-bit RGBA, at the original size, not scaled or filtered. Walls (210, 64 x 64) and
  floors (52, 32 x 32) are opaque, drawn through palette 0. Doors (13, 32 x 64, from DOORS.GR)
  are drawn as the game draws .GR pictures: palette index 0 is transparent (all four bytes 0);
  `transparent` says whether a picture has such a pixel (13 of 13 do).
- Terrain: walls take UW1's wall types (`TerrainTable.Uw1Walls`: normal, ankh, stairs up,
  stairs down, pipe, grating, drain, chained princess, window, tapestry, textured door);
  floors the class, the word shifted right 4: normal, water (16, 17, 32, 33, 34), lava (23,
  24, 25); no UW1 floor is ice.

## objects/classes.json

One entry per item 0-511, one per line: `item`, `hex`, `name`, `category`
(`ItemCategories.Of`: MeleeWeapon, Ammunition, Launcher, Armour, Shield, Ring, Crown,
Creature, Container, Light, Wand, Treasure, Food, Potion, Scenery, QuestKey, Runestone, Key,
Lock, Quest, MagicItem, SpellObject, Book, Scroll, Door, Furniture, Decal, Switch, Trap,
Trigger, Animated), `drawnAs` (`ObjectModels.Classify`: Sprite, Critter, Model, Door,
TmapLever, TmapWall, None), `model` (the built-in model), `sprite` (its PNG or null) and
`tables`: every field of each object table that covers the item (`ObjectTables.Layout`:
`weapons`, `ranged`, `armour`, `creatures`, `containers`, `lights`, `food`, `triggers`,
`animations` from OBJECTS.DAT, `commonProperties` from COMOBJ.DAT), under the editor's field
names in camelCase. Every field's meaning and source is in `Uw1Schemas` and `ObjectTables`.

## objects/sprites, strings.json

- `objects/sprites/NNN.png`: OBJECTS.GR's picture of item NNN (387 of them), RGBA, index 0
  transparent, as the 3D view draws sprites.
- `strings.json`: the strings window's export (`StringsEditor.ExportJson`): `file`, then
  `blocks` with `block`, `hex`, `description`, `strings`. Letters as the editor shows them
  (the accent table's codes as their letters).

## Where this differs from the request

1. **Winding is made consistent by the export.** `LevelGeometry.Build` puts a wall's corners
   in the order of its edge, so the walls on the E and S edges and the diagonal walls of
   OpenNE and OpenNW tiles are wound the other way round from the floors and the other walls.
   The editor never shows it (the renderer turns face culling off). The export swaps the
   second and third vertex of those triangles, so every front faces the open space; the
   texture coordinates go with their vertices, so nothing else changes.
2. **Vertices are `[x, y, z, u, v]` arrays**, not objects, named by `vertexFields`, to keep
   the files small (a level has about 36,000 vertices).
3. **The mesh has no door triangles** and the `doors` texture set is never used by a
   triangle: doors and their frames are objects the game draws as models (`doors`, and
   `manifest.doorModels` for their size). The `doors` PNGs are for those leaves.
4. **No `edge` for doors.** Doors do not stand on a tile edge; they stand inside their tile
   at their fine position (question 5). `plane` and the position say where.
5. **`playerStart.headingDegrees` is null**: the editor does not know the new game's facing.
6. **`worldX/Y/Z` are null for objects inside a container or creature**, whose stored fine
   position and z mean nothing.
7. **`parent` and `holder` are -1 for none**, as LevelModel keeps them, not null.
8. **classes.json uses the editor's object tables (`ObjectTables`, `Uw1Schemas`)**, not the
   older `CommonData`/`WeaponData`/... classes of Underworld.Formats: they cover every field
   of every table, with names checked against the game's code.
9. **Lights are the light sources 0x90-0x97 only**, with the light table's brightness; see
   `lights` for why.
10. **Added**: `manifest.files` (hashes of the files written), `manifest.doorModels`,
    `textureMap.raw`, objects' `category`, `next`, `flags`, `tenacious`, `text`,
    `heightIsData`, `finePositionIsData`, creatures' `goalName`, `attitudeName`, `npcName`,
    the level lists `trapsWithoutTrigger`, `tileChanges` and `standable`, textures'
    `terrainWord` and `cycledPixels`, and the summary's object counts.
11. **Animated textures** are written with all their distinct frames (4, 5 or 15), not "a few".
12. **UW2 is refused** (exit 1) rather than exported half-checked (question 10).

## The ten questions

**1. Heading: which direction is 0, and does it turn clockwise?**
0 is north and it turns clockwise seen from above: 2 east, 4 south, 6 west; degrees =
heading * 45. Sources: `ObjectModels.Builder.Add` turns a model "clockwise from above: north
(0, 1) turns to east (1, 0) at 90 degrees" by heading * 45 (`ObjectModels.Build`);
`LevelEditor.SnapToWall` faces a wall object into the north wall with heading 0, east 2,
south 4, west 6; `StartPoint.FacingDegrees` documents "the game's headings: 0 north, 90 east".
A creature's full facing is heading * 32 + its fine heading, 0-255 (`Uw1Schemas.MobileObject`).
Sure for the editor, whose 3D view places wall objects and doors on the right walls with it.
Not known: the facing of a new game's player (the editor reads only the start square).

**2. Walls: one piece or several, and where does the texture start?**
One piece per tile edge (two triangles). It belongs to the lower tile and faces into it:
from this tile's floor (each end at its own corner height) up to the neighbour's floor, or
up to the ceiling when the neighbour is rock, a diagonal tile closed on that side, or the
map's edge (`LevelGeometry.Build`). A diagonal wall always runs from the floor to the
ceiling. Nothing is split by height. The texture starts at the **top** of each face: v = 0
at both top corners, the face's height in tiles at the bottom ones, one tile of texture per
tile of height, so a face lower than a tile shows only the texture's top part (`WallQuad`,
from 3d/GRIDDB.C txtwal and 3d/TMAPOPS.ASM do_compact_tmap). A sloped face takes the mean
of its two ends' heights for the bottom v. Sure that this is what the editor draws; the
mean-height rule for sloped faces is the editor's own simplification (its comment says so).

**3. Ceiling: always flat at height 16 in UW1, with one texture per level?**
Yes. Flat at 16 steps (4.0 tiles) over every open tile, drawn with the level's floor index 9
(`LevelModel.CeilingFloorIndex`, 3d/GRIDDB.C), so one texture per level, which differs from
level to level (level 1: floor texture 15; `textureMap.ceilingTexture`). `HasCeiling` is
always true in UW1. Sure.

**4. Water and lava: only floor texture, or a depth or separate surface?**
Only the floor texture: its TERRAIN.DAT word (`terrain` in textures.json: water 16, 17,
32-34; lava 23-25). The editor draws water and lava floors at the tile's own height like any
floor (`LevelGeometry.Build`), and its pathing treats water as a floor the player swims over
at that height ("UW1 swimming is normal movement, motion/PHYSICS.C parse_player_terr",
`NavRegions`). Lava burns (1 fire damage one time in five, `CreatureTerrain`). Not known
from the editor's code: whether the game shows the player or objects lower when in water;
nothing in the editor models a depth.

**5. Doors: on a tile edge or in the middle, and how big is the opening?**
Neither exactly: a door is an object at its own fine position in an open tile, turned by its
heading (`ObjectModels.Build`). Along its plane it is always at fine 3 or 4 (the middle);
across it, anywhere from 0 to 7 (of UW1's 216 doors, 101 stand at fine 3-4 both ways, 43 at
an edge, fine 0 or 7, the rest between). Sizes, from the editor's decoding of UW.EXE's models
(`manifest.doorModels`): the frame (model 1) is one tile wide (x -0.4375 to 0.5625 of the
door's point, so from the tile's west to east edge for a door at fine 3) and the 3D view
raises its top to the ceiling; the leaf (model 0xE, also the secret door 0xF) is 0.5 tile wide,
0.8125 tile (3.25 steps) tall and 1/32 thick, hinged 0.1875 west of the door's point, so the
opening is the middle half of the tile, from the floor to 0.8125 up. The door texture is 32 x
64. Fairly sure of the frame and leaf extents as decoded; the decoder "only collects geometry
for display" (`Uw1ExeModels`), so the exact opening is not checked against the game. The
portcullis model (0xC) decodes flat (no height), so its size is not known.

**6. Which objects act as floor?**
Those with COMOBJ.DAT's Solid flag and a collision box: the player can stand on their top
(motion/MOTION.C get_pcoll; `Hitboxes`). Listed per level in `standable`. Those whose box
covers the whole tile (radius 4) are a second floor over the tile: in UW1 the bridge (0x164),
which the randomizer's model treats as a deck (`NavRegions.NavDeck`); it is the only
whole-tile one in the nine levels (97 bridges, none on level 5, 8 or 9). The others are
benches, barrels, boulders, chests, chairs, nightstands and shrines, whose top can be climbed
on. Sure for the rule the editor models; build floor at least for the `wholeTile` entries.

**7. Tiles that change in play, per level?**
Yes: `tileChanges` in each level file, from `TerrainPreview.List` (every change terrain trap
at each square its triggers run it at, with what it sets) and the special traps that set a
tile's height (hack 3, the eight-position switches of level 1; hack 0x18, the bullfrog puzzle
of level 4). Counts: 4, 1, 7, 11, 2, 3, 11, 1, 0 for levels 1-9. Not covered: a change made
by a conversation or by code of the game that is not a trap; the editor knows of none in UW1
but has not proven there are none. Fairly sure.

**8. Animated textures: frames and timing?**
The game rotates palette entries, not pictures: in play, one step every 64 ticks of the
1/256 s clock, four steps a second, in four banks of four (0x30-0x3F, each entry taking the
next one's colour) and the runs 0x10-0x14 (five) and 0x15-0x17 (three, the other way)
(`Graphics/PaletteCycles.cs`, gfx/COLCYCLE.C cycle_colors). A texture using a bank shows 4
different pictures; one using 0x10-0x14 5; one using both runs 15. textures.json gives each
animated texture all its frames (`NNN_fK.png`) and `frameSeconds` 0.25: 23 textures with 4
frames (among them the water floors and the drain walls), 7 with 5 (among them lava floor
23), 2 with 15 (lava floors 24 and 25). `cycledPixels` says how much of the
picture moves (a few textures use a cycled colour in a handful of pixels). Sure.

**9. Anything in LevelGeometry known to be wrong or unfinished?**
- Walls are not wound consistently (E and S edges, OpenNE and OpenNW diagonals face the rock);
  the export corrects it (deviation 1).
- Faces beside slopes can taper to nothing at one end, giving triangles of no area (kept).
- A sloped wall's bottom v is the mean of its ends (an approximation, see question 2).
- It builds only the tile surfaces: door frames, doors, bridges and other models are not in
  it (`ObjectModels` draws them; the 3D view also stretches the door frame and the pillar to
  the ceiling because the model decoder stops them at height 1).
- An index the texture map lacks (UW1 floor index 10-15, wall 48-63) gets a checkerboard;
  the export writes `texture: null` there. No shipped UW1 level has one.
Nothing else is known to be wrong; the picture tests of the 3D view cover it.

**10. UW2: the same export with only the counts changed?**
No, not with the counts alone. `LevelGeometry` already builds UW2 levels (64-entry texture
lists from T64.TR for walls and floors, ceiling index 0xF, no ceiling in the Ethereal Void,
80 level slots in worlds of 8), but the export would also have to change: one texture file
for walls and floors, TERRAIN.DAT's UW2 layout (class in bits 6-7, ice, currents and grip in
bits 3-5, stairs and shafts in bits 0-2), the trap readings (UW2's traps differ; `TypedFields`
names only what both games share, `Uw2Traps` describes the rest), UW2's trigger kinds
(0x1B0 and up, timers, schedules) and level numbering by world. The export refuses UW2 until
that is done.
