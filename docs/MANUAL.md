# Underworld Editor manual

The editor opens Ultima Underworld I and II, shows their levels in 3D, and edits levels,
conversations, strings, keys, the player, graphics and the executables' start square. Its
changes can be packed into mods. Everything it writes is kept safe: the previous file as
`NAME.BKP`, and the untouched original as `NAME.EXT.ORIG` the first time a file is written.

Press **F1** or use **Help ▸ Manual** to open this manual. **Help ▸ Controls** lists the keys.

## Opening a game

**File ▸ Open game folder…** (Ctrl+O). Choose the game folder (the one with `DATA` in it),
the `DATA` folder itself, or a saved game (`SAVE1`…). The game is recognised by its
files. A saved game takes its levels and player from the save, and everything else from
`DATA`.

**The camera starts over the start square of a new game.** The square is read from
`UW.EXE` / `UW2.EXE`, so a patched start square shows too. In a saved game the camera
starts over the player, facing where the player faces. A large **red arrow** marks the
new-game start; in a saved game a **green arrow** marks the player.

**Picking a level:**
- UW1: levels 1–9.
- UW2: pick the world, then a slot in it. Empty slots are marked.
- PgUp and PgDn step through the levels.

## The 3D view

**The 3D view:**
- Floors, walls, slopes and diagonal walls, textured through the level's texture map.
- The ceiling (G), seen only from below, as in the game.
- Objects drawn the game's way, from COMOBJ.DAT's render field:
  - doors in their frames (open doors swung by their flags, the portcullis raised, the
    secret door in the wall texture);
  - levers, switches, writings and paintings on their walls;
  - furniture, pillars, bridges and so on as the 3D models built into `UW.EXE` /
    `UW2.EXE`;
  - everything else as its picture, always facing the camera.
- M switches the models off. Invisible objects and traps or triggers can be shown too.
- A yellow grid every N tiles (H; N set in the Show box), laid on the floors. The view's
  edges name each line, as `X: 30` or `Y: 45`.
- **Light** (in the Show box): full light, or one of the game's eight light levels. These
  are no light, candle, torch, taper, lantern, night vision, light sphere or Wizard Eye, and
  the brightest. The view is then shaded as the game shades it. Each square gets one of the
  16 shading maps of LIGHT.DAT (MONO.DAT's greys for night vision), by its distance in
  squares and the level's row of SHADES.DAT. Squares beyond the level's range are not drawn.
  The centre is the point looked at, or the eye in first person. In UW2, a level DL.DAT
  never lets go darker shows at its minimum.

**Moving the camera.** Drag with the left or right button to turn round the point
looked at. Drag with the middle button, or with Shift, to move that point. Use the wheel
or + and − to come closer or go farther. W A S D or the arrows move (Shift is faster),
Q and E turn, and R and V go up and down a floor step. F frames the selection, T looks
straight down, and Home goes back to the start or the player. A double click selects
something and moves the view to it.

## Finding and selecting

**Finding objects:**
- Creatures that talk show their name and unique objects their plot name in brackets,
  as in "fighter (Corby)" and "shiny sword (Caliburn, the Sword of Justice)".
  Enchanted objects show their enchantment.
- The search box matches item names, NPC names ("human (Lord British)"), the text of
  books, scrolls, writings, gravestones and text traps, `#index` and `0x` item ids.
- The chips narrow the list to weapons, food, monsters, keys, traps or triggers (any of
  those chosen), and Enchanted to enchanted objects.
- Each line shows the object's picture as the game shows it.
- **Level ▸ Find in all levels…** (Ctrl+Shift+F) runs the same search, with every item
  kind, over all levels. Double-click a result to open its level with the object selected.

**Selecting:**
- Click an object or a tile in the view, or pick one in the object list.
- The list shows the objects lying on tiles in index order. Whatever is inside an object
  (container contents, inventories, a trigger's trap, a door's lock) hangs under it.
- The filter takes a name, `#index` or `0x` item id.

**Properties:** every field of the object and tile records, with its value in decimal and
hex. The tooltip gives the field's meaning and its source in the reconstructed game code.
Fields whose meaning is only inferred or unknown are marked. The tile panel also shows
its wall and floor textures.

**Level ▸ Check the object lists:** runs the game's own consistency check (Obj_ListOkay).

## Editing levels

Each level keeps its own undo history (Ctrl+Z, Ctrl+Y). **Save levels** (Ctrl+S) writes
LEV.ARK and keeps `LEV.BKP`, plus `LEV.ARK.ORIG` the first time. The toolbar above the
view chooses the tool:
- **Select:** a click picks an object or tile; a double click also moves the view to it
  (so does double-clicking in the list).
- **Move:** click a square and the selected object goes there, keeping its height above the
  floor. A critter's home moves with it.
- **Paint:** drag over tiles to paint the light flag on or off (only that bit), a floor or
  wall texture from the level's texture list (shown with thumbnails), a tile type, or a
  floor height.
- **Ctrl+wheel** raises or lowers the floor under the mouse, and the objects on it with it.
- **First person** (P): walk the level at eye height. W A S D walk, Q E or a drag turns,
  R V look up and down, and Esc leaves. Rock and floors more than two steps up stop you.

Any record field can be typed into in the properties panel (decimal or `0x` hex), except
the list links, which the editor keeps. Small fields (fine X and Y, height, quality,
owner, heading) have − and + buttons; hold them down to repeat. One-bit flags (invisible,
enchanted, is quantity and so on) are checkboxes. The heading shows as an arrow, with north
up. An owner field has a list of the races to choose
from. Each race is named by its creatures, from OBJECTS.DAT's race byte.

**Add…** (Insert) opens a window that lists the items as a tree. The groups are the object
list's kinds (melee weapons, armour, containers and so on); a search opens the groups that
match. The form changes with the kind of item:
- a boulder or food gets quality, owner and quantity;
- a weapon, armour or ring gets its enchantment, from the weapon/armour list (which names
  what each really does) or the 64 spells, with how it is stored. In UW2 a warning says
  that the game's own names swap the Accuracy and Damage enchantments;
- a wand gets its spell and charges, kept in a spell object inside it;
- a book or scroll gets its text, previewed;
- a key gets its lock and description, and a lock gets its number, locked flag and
  lockpicking difficulty;
- a creature gets hit points, attitude and conversation;
- a trap or trigger gets its description.

The new object goes on the selected tile or into the selected container. The player
window's **Add…** uses the same form for the inventory. **Delete** (Del) frees the object
and what it holds.

**Traps and triggers:** selecting one shows what its kind does and what each field means,
from event/TRIGGER.C. The square it acts on gets a **red bullseye**: a trigger's target, a
teleport's destination, a delete trap's square, or where the chains that reach a trap run.
**Choose the target in the view** lets you click the square instead of typing it; a
teleport can aim at another level. **Variable in globals…** opens the global variables
window on the variable a set or check variable trap uses.

**The start and the player:** with the Move tool, click an arrow and then a square. The
new-game start can only move on level 1: it is patched into `UW.EXE` / `UW2.EXE`, which is
kept as `.ORIG`. The player in a saved game can go to any level, and is saved in
`PLAYER.DAT`. Clicking the green arrow opens the player window. Edit ▸ Move the … here
uses the selected tile.


## Conversations

**Tools ▸ Conversations…** (Ctrl+Shift+C): every conversation in CNV.ARK, by slot and the
name the creature talks under. The list filter matches slots, names and any text a
conversation says. Four tabs:
- **Dialogue:** what the script does, rebuilt from its code by following the
  machine's stack (conv/BABL.C). It shows the say and respond lines with their text, each
  menu with its numbered choices, and the checks on quests, variables and the talker's
  state. Simple `if` blocks are folded; other jumps stay as labels. Functions the script
  never calls (the compiler's small library) are hidden unless you ask. Double-click a
  line to see its code.
- **Strings:** the conversation's block of STRINGS.PAK, edited in place. Each string shows
  where the script uses it. **Add string** appends one, for the script to say.
- **Script:** the code as assembly, one instruction a line (conv.h's opcodes):
  - labels for jumps and calls;
  - imported functions by name;
  - the header and import table as directives;
  - comments with each address and the strings.
  **Apply** (Ctrl+Enter) assembles it. A mistake is reported at its line. Disassembling
  and assembling every shipped conversation of both games gives back the same bytes.
- **Who talks:** the creatures in the levels that use the slot (whoami, or the generic
  slot for whoami 0). Double-click one to go to it in the main window.
**Save** (Ctrl+S) writes CNV.ARK and STRINGS.PAK in `DATA`, with only the changed blocks
rewritten. UW2's blocks are compressed again. The old files are kept as `CNV.BKP` and
`STRINGS.BKP`. Edits stay in memory when the window closes, until the game is closed.

## Global variables

**Tools ▸ Global variables…** (Ctrl+Shift+G): the game state conversations can read and
change, with the conversations that read and write each:
- quest flags and quest bytes (`get_quest`, `set_quest`);
- the game variables traps share (`x_traps`; UW2's numbered variables);
- UW2's X clocks (`x_clock`);
- skills (`x_skills`);
- the talker's and player's variables copied in and out (`npc_attitude`, `play_hp`…);
- each conversation's own globals, kept in `BGLOBALS.DAT`.
The uses come from the constant arguments of those calls. With a saved game open, the
values come from its `PLAYER.DAT` and `BGLOBALS.DAT`; they can be changed and saved, and
the old files are kept as `.BKP`. With `DATA` open, a new game, they read 0. Double-click a
use to open its conversation at that line.

## Player and inventory

**Tools ▸ Player and inventory…** (Ctrl+Shift+P), with a saved game open. It edits the
saved character in `PLAYER.DAT`:
- name, class, sex, hand and portrait (the HEADS.GR picture shown);
- attributes, vitality, mana, hunger, fatigue, carrying capacity;
- level, experience, skill points and the twenty skills.

**Checks** are listed beside it and update as you type:
- **Game detects:** in UW2, strength + dexterity + intelligence above 64 makes the
  end-of-game screen add "AND CHEATED ON THEIR CHARACTER" (game/SKILLS.C `game_stats`).
  UW1 has no such check.
- **Not reachable:** values creation and play never give. Attributes over 30 or outside
  the class's start in SKILLS.DAT, skills over 30, a sixth portrait, levels outside 1–16.
- **Notes:** maxima that disagree with what the game computes (`player_compute`), a level
  the experience has not earned, and the like. *Set the maxima as the game computes them*
  fixes those.

The hand bit means right-handed in UW1 and left-handed in UW2: their creation menus list
the choices the other way round, so the labels come from CHRGEN.DAT. The **Inventory** tab
shows the worn and held objects, the pack and container contents, with icons:
- each object's id, quality, owner, quantity or special, flags and slot can be changed;
- objects can be added to the pack or into a container, or removed.

Saving renumbers the objects depth first, as the game's `InvSaveNexts` does. Rewriting a
shipped save's inventory unchanged gives the same bytes, except slots 19–27 (an open
bag's view, which the game rebuilds): those are cleared. The old file is kept as
`PLAYER.BKP`.

## Graphics

**Tools ▸ Graphics…** (Ctrl+Shift+R), with three tabs:
- **Pictures:** every `.GR`, `.TR`, `.BYT` and UW2's `BYT.ARK` in DATA, as thumbnails.
  Each is shown large with a palette of PALS.DAT; UW2's screens start in the palette the
  game shows them with.
- **Critter animations:** the CRIT art files by the creatures that use them. Their
  animations come from the UW1 page headers or UW2's `CR.AN`. Pick an auxiliary palette;
  play the animation or step through its frames.
- **Cutscenes:** every CUTS cutscene and its LPF animations, with a frame slider, playing
  at the file's frame rate.

**Export** writes an indexed PNG whose palette is the picture's own colours (its 16 or 32
auxiliary colours, or the 256 of the palette), so the indices survive a round trip.
**Export all frames** writes a cutscene animation as numbered PNG files.

**Import** takes a PNG back and refuses:
- **another size**;
- **colours the picture cannot use:** a 4-bit sprite has 15 colours and transparency,
  a critter frame 31 or 15, an 8-bit picture its palette. The list names each missing
  colour with its pixel count and the nearest one it has; *Use the nearest colours*
  maps them instead;
- **transparency** in a picture the game draws solid (textures, screens);
- **files the game could not load:**
  - a critter page over the 0x7FFF bytes `load_cpg` reads;
  - a cutscene page over its 64 KB window or 256 records;
  - more than 256 pages;
  - texture and screen sizes are fixed.

A replaced cutscene frame re-encodes only its own delta, the next frame's and the loop
back to the first. Saving keeps each old file as `.BKP`.

## Keys and locks

**Tools ▸ Keys and locks…** (Ctrl+Shift+K): every lock number with its keys and locks
across all levels, where each lies, whether it is locked, and the keys' description
(STRINGS.PAK block 5, 100 + the number), which can be edited. Double-click to go there.

## Triggers and traps

**Tools ▸ Triggers and traps…** (Ctrl+Shift+T): every trigger of the level, where it
lies, the square its chain runs on and each trap in the chain, then the traps no trigger
reaches. A line explains itself; double-click goes to it in the map.

## Strings

**Tools ▸ Strings…** (Ctrl+Shift+S): every block of STRINGS.PAK, with the conversations
named, searched in one block or all of them. Strings can be edited and added, and
characters the file cannot write are refused. Saving updates the names everywhere in the
editor. It shares the conversation editor's copy of the file, so edits made in both are
never written over each other.

## 3D models

**Tools ▸ 3D models…** (Ctrl+Shift+O) lists the 32 models built into `UW.EXE` or
`UW2.EXE`. These are the furniture, boulders, doors, the shrine, the moongate and so on.
Each one is drawn flat-shaded in its palette colours. Drag to turn it and use the wheel to
zoom. Faces outlined in gold are textured: they take the texture of the object using the
model. The panel lists:
- the model's points and faces, and the colours it uses;
- where it lies in the executable, and how many bytes it has before the next model;
- the items drawn with it.

**Export OBJ…** writes the model as a Wavefront OBJ file (1.0 is one tile, y up), with an
MTL file of its palette colours. **Export all…** writes every model that has faces.

The models cannot be replaced yet. The replacement study in the project notes explains
what that would take.

## Checkpoints

**File ▸ Checkpoints…** While there are unsaved changes, the editor keeps a copy of them
every few minutes (5 by default; 0 turns it off). Each copy holds the changed levels, and
STRINGS.PAK or PLAYER.DAT when they have unsaved edits. Only the newest copies are kept: 20
by default, up to 1000. **Checkpoint now** takes one at once. **Restore** (or a double click)
loads one back as unsaved edits: each level becomes one undo step, and nothing in the game
folder changes until you save. The copies live in the editor's settings folder, one folder
per opened game. Unsaved conversation scripts are not included; their strings are.

## Colour scheme

**View ▸ Colour scheme** switches between dark and light colours for every window. The
choice is remembered between runs. The 3D view and the pictures keep their own colours.

## What enchantments do

- **A weapon's blow** looks only at a weapon enchantment, major class 12 (COMBAT.C
  `DoPlayerWeapon`):
  - In UW1, effects 0–7 add (effect & 7) + 1 to hit, and effects 8–15 add it to damage.
  - In UW2, effects 0–3 add 2·effect + 1 damage, and effects 4–7 add 2·effect − 7 to hit
    (the names say the opposite). Effects 8–15 are the special weapons. Their power fires on
    a critical hit, or on every hit for the sixth: it heals the player, repels the undead,
    casts a fireball, holds the target, or opens a door.
- **Anything else on a weapon does nothing** while it is wielded. Only armour slots, rings
  and a shield in the off hand count as worn (INVDATA.C `ObjWorn`), and only enchantments
  without flag 11 apply while worn (PLAYDATA.C).
- **A spell (flag 11) on any object** is cast when the object is used (double-clicked), at
  most every 0x2FD ticks (USEITEMS.C `checkSpell`). An enchanted object never runs out; only
  a wand's spell object has charges.

## Randomizer (work in progress)

**Tools ▸ Randomizer ▸ Zones…** opens the zoning of the game. A zone is a part of the world
that can be walked without meeting a requirement. Every tile has a zone id: 0 is rock, 1 is
open but not in a zone yet (magenta in the view), and 2–255 are the zones. The file is
`uw1-zones.json` (or `uw2-zones.json`) in `DATA`, the same format the Unity editor uses.
**Load the UW1 example** starts from Karl's UW1 zoning, built from his painted maps and
notes: 91 zones, 139 links and 50 grants.

- **Current zone:** the zone painted and listed. **Add zone**, **Rename**, **Remove zone**
  (its tiles become unassigned; its links and grants go), and a colour. Colours default to a
  Glasbey palette: each colour is as far as possible, in CIELAB, from all the ones before it.
- **Painting:** with **Paint in the view** on, the Paint tool paints the current zone. Drag
  over tiles, or tick *A click fills the area* to flood the connected area under the mouse.
  **Fill the area under the selected tile as zone N** does the same from the selected tile.
  Rock is never painted, and **Undo painting** steps back through the strokes. While the
  window is open, the view shows each tile's zone colour on its floor.
- **Key items** of the current zone are listed on the right. The list holds keys (by the lock
  they open), the talismans, quest and unique items, runestones, creatures that talk, and the
  creatures whose death the game checks. Plants, bridges and furniture are left out. Any
  object can be marked as a key item, or left out, by hand; the zone file remembers it. **Add
  them to the zone's grants** turns them into what the zone gives (`key:5`, `talisman:cup`,
  `npc:corby`…).
- **Import pictures… / Export pictures…** read and write one 64×64 indexed PNG or BMP per
  level, in which the pixel's index is the zone id, with north up. `map0` is level 1, as in
  the Gimp maps.

**Tools ▸ Randomizer ▸ Zone graph…** shows a label for each zone, placed in a column for the
level it first appears on. Drag the labels to arrange them; the positions are saved in the
zone file. Lines are the links between zones:
- one arrowhead means one-way, two mean two-way;
- dashed means still to check;
- the colour is the kind (door, portcullis, secret, drop, lever and so on).

- **Connect one-way / two-way:** click the zone the path starts from, then the zone it leads
  to. If doors join the two zones, the requirement is filled from their locks. A locked door
  that cannot be picked or broken needs its key (`key:N`). Picking a lock of difficulty Z is
  `pick:Z` (Pick Lock 3Z; 14 needs 31; 15 never). Breaking a breakable door is `bash`. A door
  only a lever or a conversation opens becomes a flag to fill in.
- **A selected path** has its zones, direction, kind, sureness, notes, and its list of
  requirements. All of them must hold, and a line with `|` holds when any part does. **From
  the doors** fills the list again. Delete removes the path.
- **Check the logic…** runs the solver from the start zone: the spheres, the route to the
  goal, and what blocks each path.

### Shuffling item properties

**Tools ▸ Randomizer ▸ Shuffle item properties…** changes what items are without moving them,
unlike the zone shuffle. Every weapon, armour piece, food, light and runestone stays where it
lies (on the floor, in a container or a creature's inventory) but may become another item of
its kind. Choose the settings in the four tabs, press **Preview** to see the report, then
**Shuffle**. **Undo shuffle** puts back every object the shuffles from the window changed; each
shuffle is also one Edit ▸ Undo step per level, and nothing reaches the game until you save
the levels. The same seed on the same levels gives the same result. The settings are kept
between runs (`item-shuffle-uw1.json` / `-uw2.json` in the settings folder).

Weights are relative, from 0 to 100: a weight of 0 removes that kind. Never touched:
Caliburn (UW1's shiny sword), Altara's jewelled dagger (UW2), talismans, crowns, dragon skin
boots, fraznium armour and objects that are templates inside traps.

- **Weapons:** weights for axes, swords and daggers, maces and clubs, and ranged weapons with
  their ammunition; the share of launchers among the ranged objects (the rest become arrows,
  bolts or sling stones, never enchanted); the quality spread (± around each object's own
  quality). Then one row per enchantment kind (Accuracy, Damage) with its chance, the mean
  strength (1 = Minor) and the deviation of a normal draw. Kinds are tried in a random order and
  the first that succeeds is the weapon's. The **depth adjustment** raises the chance (up to
  double on the deepest level at 100) and moves the mean towards the strongest; level 1 is
  never changed (UW2: the depth is the level within its world). **Special weapons** (UW2 only:
  LifeStealer, Undead Bane, Fire Doom, Stone Strike, the two door openers) are put on that many
  random melee weapons, 1 each by default.
- **Armour:** weights for leather, mail, plate and shields. A piece keeps its slot and changes
  material; shields stay shields. Protection and Toughness rows as for weapons, depth, quality
  spread, and **Flameproof** (1 by default) on random unenchanted pieces.
- **Food, lights, tools:** a weight per food and per light source (lit ones stay lit), and the
  least number of threads (5), poles (3) and fishing poles (3). When there are fewer, random
  food and lights lying on the floor become them.
- **Runestones:** a weight and a minimum (1) per rune. Minimums are met by changing runestones
  of the kinds with the most to spare; the report says when there are too few runestones.

Spells and other enchantments already on objects stay unless **Keep spells and other
enchantments** is off; an object whose link leads to other objects (contents, a spell object)
never has its enchantment replaced.

## Mods

**Tools ▸ Mods…** (Ctrl+Shift+M):
- **Make a mod** compares the game with the originals the editor kept (`.ORIG`), or with
  an untouched copy, and writes a `.uwmod` (a zip with `mod.json`). It holds only what
  changed: LEV.ARK and other archive blocks, single strings, bytes patched in place (an
  executable's start square, never the program itself), or whole new files. It records
  the SHA-256 of each file it was made from.
- **Load a mod** checks it against the open game, block by block and string by string
  (fits, already applied, changed since, missing). It then applies it and opens the game
  again.

## Keys and mouse

| Where | Key | What it does |
|---|---|---|
| View | Left click | select an object or tile (Move tool: put the selection there) |
| View | Double click | select and go to it |
| View | Left or right drag | turn round the point looked at |
| View | Middle or Shift+drag | move the point |
| View | Wheel, + and − | closer, farther |
| View | Ctrl+wheel | raise or lower the floor under the mouse |
| View | W A S D, arrows | move (Shift: faster) |
| View | Q, E | turn |
| View | R, V | up, down a floor step |
| First person | P | start or stop walking |
| First person | W A S D, wheel | walk |
| First person | Q E, drag | turn |
| First person | R, V | look up, down |
| First person | Esc | stop walking |
| Anywhere | Ctrl+O | open a game |
| Anywhere | Ctrl+S | save the levels (and a moved player) |
| Anywhere | Ctrl+Z, Ctrl+Y | undo, redo |
| Anywhere | Insert, Delete | add an object, delete the selected one |
| Anywhere | Ctrl+Shift+F | find in all levels |
| Anywhere | Ctrl+Shift+C | conversations |
| Anywhere | Ctrl+Shift+G | global variables |
| Anywhere | Ctrl+Shift+P | player and inventory |
| Anywhere | Ctrl+Shift+R | graphics |
| Anywhere | Ctrl+Shift+K | keys and locks |
| Anywhere | Ctrl+Shift+T | triggers and traps |
| Anywhere | Ctrl+Shift+S | strings |
| Anywhere | Ctrl+Shift+O | 3D models |
| Anywhere | Ctrl+Shift+M | mods |
| Anywhere | PgUp, PgDn | previous, next level |
| Anywhere | F | frame the selection |
| Anywhere | T | look straight down |
| Anywhere | G, H, M | ceiling, grid, 3D models on or off |
| Anywhere | Home | back to the start or the player |
| Anywhere | Esc | cancel a target choice, or clear the selection |
| Anywhere | F1 | this manual |
| Properties | − +, held | step a field down or up |

## Files the editor writes

| File | When |
|---|---|
| `LEV.ARK` | Save levels (in the open save, or in `DATA`) |
| `CNV.ARK`, `STRINGS.PAK` | Conversations, Strings, Keys and locks |
| `PLAYER.DAT`, `BGLOBALS.DAT` | Player window, Global variables, a moved player (saved games only) |
| `UW.EXE`, `UW2.EXE` | the new-game start square, patched in place |
| `.GR`, `.TR`, `.BYT`, CRIT, CUTS files | Graphics imports |

Before writing, the old file is kept as `NAME.BKP`. The first time the editor writes a file,
it also keeps the untouched original as `NAME.EXT.ORIG`. The mods window compares with
those originals.
