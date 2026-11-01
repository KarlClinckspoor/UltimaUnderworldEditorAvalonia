# Underworld Editor manual

The editor opens Ultima Underworld I and II, shows their levels in 3D, and edits levels,
conversations, strings, keys, the player, graphics and the executables' start square. Its
changes can be packed into mods. Everything it writes is kept safe: the previous file as
`NAME.EXT.BKP`, and the untouched original as `NAME.EXT.ORIG` the first time a file is written.

Press **F1** or use **Help ▸ Manual** to open this manual. **Help ▸ Controls** lists the keys.

## Opening a game

**File ▸ Open game folder…** (Ctrl+O). Choose the game folder (the one with `DATA` in it),
the `DATA` folder itself, or a saved game (`SAVE1`…). The game is recognised by its
files. A saved game takes its levels and player from the save, and everything else from
`DATA`.

**Unsaved work.** Opening another game or closing the editor asks first when anything is
unsaved: levels and their maps, the player, strings and conversations, global variables,
pictures and zones. **Save** there writes all of them; Ctrl+S (Save the levels) writes the
levels and the player. The global variables and graphics windows also ask when they close
with unsaved changes, since their edits live in the window. The tool windows of a game close
when another game opens.

**The camera starts over the start square of a new game.** The square is read from
`UW.EXE` / `UW2.EXE`, so a patched start square shows too. In a saved game the camera
starts over the player, facing where the player faces. A large **red arrow** marks the
new-game start; in a saved game a **green arrow** marks the player.

**Picking a level:**
- UW1: levels 1–9.
- UW2: pick the world, then a slot in it. Empty slots are marked; they cannot be edited.
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
something and moves the view to it. Going up or down a level keeps the camera over the same X and Y,
at the new level's floor height.

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
LEV.ARK and keeps `LEV.ARK.BKP`, plus `LEV.ARK.ORIG` the first time. The toolbar above the
view chooses the tool:
- **Select:** a click picks an object or tile; a double click also moves the view to it
  (so does double-clicking in the list).
- **Move:** click a square and the selected object goes there, keeping its height above the
  floor. A critter's home moves with it.
- **Paint:** drag over tiles to paint the light flag on or off (only that bit), a floor or
  wall texture from the level's texture list (shown with thumbnails), a tile type, a
  floor height, or a **tile template**: a tile's type, height, floor and wall texture
  together (the ticked parts), taken with **From the selected tile** or from the right-click
  menu. **Fill** makes a click paint every tile the player can walk to from it, by the zone
  fill's rules (no rock, no doors, climbs of at most two steps). **Paint selection** paints
  the selected tiles. A stroke, a fill or a selection painted is one undo step.
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
teleport can aim at another level. A teleport to another level shows its bullseye faded,
with the level and square written beside it; **Go to the teleport's target** (also in the
Edit menu) opens that level on the destination square. **Variable in globals…** opens the
global variables window on the variable a set or check variable trap uses.

### Selecting, copying and moving

- **Ctrl+click** adds an object or a tile to the selection, or takes it out; **Ctrl+drag**
  selects every open tile inside the rectangle, and the objects on them. Selected tiles are
  outlined in blue, selected objects' tiles in yellow. Esc clears the selection.
- **Copy** (Ctrl+C), **Cut** (Ctrl+X), **Paste** (Ctrl+V) and **Duplicate** (Ctrl+D) work on
  the selected objects with everything inside them: a bag's contents, a wand's spell, a
  trigger's chain of traps. Paste puts them on the tile under the mouse (or the selected
  one), keeping their places relative to each other and their heights above the floor; it
  works on any level of the same game. A creature with a conversation copied makes a second
  creature with that conversation, which the status line points out.
- **Drag** a selected object to move it (and the others selected with it) to another tile.
  Dropping it on a container, a creature or a trigger puts it inside (into the trigger's
  chain for a trap). Esc cancels a drag.
- Deleting several objects is one undo step.

**The right-click menu** (a right click without dragging): on an object, copy, cut,
duplicate, paste inside, add an object inside, delete, choose a trap's target, go to a
teleport's target, **Make it run a trap** (then click the trap: it joins the trigger's
chain), **Put inside** (then click the container), frame it, select its tile; on a tile, add
an object here, paste here, use it as the brush template, fill from here, paint the
selection, select the tiles walkable from here, play-test from here, and move the new-game
start or the player there.

### The 2D map

**View ▸ 2D map** (Tab with the mouse over the view) looks straight down without
perspective, north up. Left and right drags move the map, the wheel zooms; clicks, the
right-click menu, Ctrl+drag selection and painting work as in 3D. The ceiling is hidden
while it is on. Tab goes back to the 3D view as it was. **View ▸ Export the map as a
picture…** writes the level from above as a PNG (16 pixels a tile): floors with their
textures, lighter where higher, rock dark, edges where the floor steps, and dots for
objects (creatures red, traps and triggers purple, doors brown, the rest yellow).

### Undo history

**Edit ▸ Undo history…** (Ctrl+H) lists the current level's steps, the newest on top (●
marks where the level is now) and the steps that can be redone above it (↷). Click a step to
go to the level as it was after it. Each level keeps its own history, up to 200 steps.

### World check

**Level ▸ World check…** looks across every level, with the unsaved edits:
- locked locks that no key opens. Keys count wherever they are: on the floor, in
  containers, carried by creatures, made by create-object traps, and in a saved game the
  player's inventory. A lock numbered 0 is listed apart: no key opens it (a lockpick, a
  spell or force does);
- keys whose lock number no lock in the levels has;
- each level's objects against the game's limits (255 mobile, 768 static): marked when 90%
  full, and when full (the game then cannot drop, throw or create objects there either).

Double-click a line to go there.

### Play test

**Level ▸ Play-test from the selected tile** (F5, or Play-test from here in the right-click
menu) runs the game in DOSBox with the unsaved levels, the player on that square. It works
on a copy of the game folder, made afresh each time in the editor's settings folder (`Play
test`); the game folder is never touched.
- With `DATA` open and a square on level 1, the copy's executable gets the square as the
  new-game start: start a new game.
- Anywhere else the player needs a saved game: an open save's own, or with `DATA` open the
  folder's first save, which then gets `DATA`'s levels as edited. Its `PLAYER.DAT` gets the
  position: restore that game (the editor says which).

DOSBox is looked for next to the game (GOG's `DOSBOX` folder) and where it is usually
installed; otherwise the editor asks for it once (**Choose DOSBox for play tests…**). Unsaved
strings and conversations are not in the copy.

### New and cleared levels

**Level ▸ Make this slot a level…** turns an empty slot of Underworld II's `LEV.ARK` into a
level (the game cannot enter an empty slot: it stops with error A003). The new level has no
objects, the free object lists of a new level, the texture map of another level of its world
(or the game's default one), and either a small open room in the middle (5 by 5 tiles at
(30, 30)-(34, 34)) or nothing but rock. **Level ▸ Clear the level…** removes every object of
the level shown, and **Clear the level and its tiles…** also makes every tile solid rock;
textures stay. Underworld II's animations and timers go with the objects, and so does
Ultima Underworld's animation list when the level is saved. Both are one Edit ▸ Undo step
and are written when you save the levels. To reach the new level in the game, point a
teleport trap at it.

**The start and the player:** with the Move tool, click an arrow and then a square. The
new-game start can only move on level 1: it is patched into `UW.EXE` / `UW2.EXE`, which is
kept as `.ORIG`. The player in a saved game can go to any level, and is saved in
`PLAYER.DAT`. Clicking the green arrow opens the player window. Edit ▸ Move the … here
uses the selected tile.


## Trap recipes

**Edit ▸ Trap recipes…** makes a working trigger and trap in one step. Choose a recipe on
the left, fill in what it needs and press **Make**. Squares and objects are chosen with
**Pick in view** (then click in the main view; Esc cancels) or **Use selection** (what is
selected in the main view). Each recipe is one Edit ▸ Undo step on its level, and the
level's list check must still pass, or nothing is changed. The new trigger is selected
afterwards, so the properties panel and the triggers window show its chain.

- **Lever opens a door:** a lever, switch, button or pull chain opens, closes or toggles a
  door. Use one already in the level (one that holds no trigger yet) or put a new one on a
  wall of a tile: on the side you choose, or the first wall found. The lever holds a use
  trigger aimed at the door's square, and a door trap lies there. In Ultima Underworld the
  door trap always takes the door's lock off and puts on the lock it carries, so the
  recipe gives it a copy of the door's own lock; **Unlock the door** leaves it without one.
  In Underworld II the lock is left alone unless you unlock it.
- **Teleport:** stepping on a square sends the player to another square, on this level or
  another one (change level in the main window before picking). **And a way back** adds a
  second pair; it must start and arrive on other squares than the first, or the player
  would be sent straight back. Only the player is teleported.
- **Pressure plate:** stepping on a square runs a trap. In Underworld II it can be a
  pressure plate, which fires when weight comes onto the square; otherwise a move trigger.
- **Object trigger:** picking up, using or looking at an object runs a trap. The object
  must be a single one (not a stack, a creature or an enchanted item) and hold no trigger
  yet; it is made to hold the trigger.

The plate and the object trigger run one of these traps:
- **Damage** to whoever set it off, or poison (strength 1-15, the player only);
- **Show a message**: a new string added to STRINGS.PAK block 9, the text traps' messages.
  Save levels does not write it: save it in Tools ▸ Strings (or when the editor asks on
  closing). Undo removes the trap, not the string;
- **Set a variable**: in Ultima Underworld a game variable 1-63 or a quest bit 0-31, in
  Underworld II a numbered variable (0-255 variables, 256-383 quest flags);
- **Create an object or creature**: a copy appears on the square you choose (or the
  trigger's) each time; the template is kept inside the trap. A creature can be hostile;
- **Work a door**: open, close or toggle it, as above;
- **Remove an object** lying on its tile. Such a trigger works once.

**Works every time** makes the trigger reusable; without it the game deletes the chain
after it runs once. **Creatures set it off too** sets the trigger's enchanted bit.

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
rewritten. UW2's blocks are compressed again. The old files are kept as `CNV.ARK.BKP` and
`STRINGS.PAK.BKP`. Edits stay in memory when the window closes, until the game is closed.

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

## Schedules

**Tools ▸ Schedules…** (Underworld II only) edits `SCD.ARK`, the events that move NPCs about
by the time of day and carry the plot along. With a saved game open it edits that save's
own copy; otherwise `DATA`'s, which a new game copies into `SAVE0`. Ultima Underworld has
no schedules, so the item is disabled there.

The file has 16 schedules (blocks). Block 0 runs by the time of day: 72 steps a day, times
0-71. Block n runs by X clock n (1 the castle plot, 2 the blackrock gems, 3 the djinn
capture, 14 pit kills, 15 the changes counter), times 0-255. A row runs once its block's
clock passes its time, on the level it names (a level, 246 + n for every level of world n,
255 any level), while the player is on that level. A level the player is away from catches
up when he comes back.

- The list shows the block's rows in time order: number, time, level, and what the row does,
  with NPCs named from their conversation (whoami) and races from the creatures.
- Select a row to edit its fields. Time, level, once (the row deletes itself after it runs)
  and event come first, then the event's own fields, named as the game reads them: the
  critters it acts on (by whoami, by race, one object, or every critter), squares, goals,
  attitudes, quest flags, variables and operations. Bytes the event does not read are listed
  as unused. Each field takes only its range.
- Events: 1 change goal, 2 teleport (on the current level only), 3 kill, 4 set a quest flag,
  5 fire the scheduled triggers on a square, 7 a special case (gargoyle, soldier, freeze
  floors, close doors, Nystul, Gotha), 8 attitude, 9 set a variable, 10 test variables,
  11 remove. A negative event is disabled: a test row (10) runs the disabled rows after it
  when its test passes. An event of 12 or more stops the schedules each time it is reached.
- A new time moves the row to its place; rows of the same time keep their order (▲ ▼ change
  it). A test row takes its disabled rows along.
- **Add row** puts an empty row (event 0, any level) after the selected one, with its time;
  **Copy row** a copy of it. **Delete row** removes it.
- **Save SCD.ARK** writes the changed blocks (`SCD.ARK.BKP` keeps the old file). Each level's
  place in a schedule moves with the rows, so rows already run are not run again.
  **Revert** goes back to the file on disk. Unsaved changes are asked about when the window
  closes.

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
`PLAYER.DAT.BKP`.

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

## Fixes

**Level ▸ Fixes…** checks what the game itself relies on, on the levels as edited, and
repairs what it can. **This level** or **Every level**; **Check** runs it again. The list
on the left has each check with its count; pick one to read what it looks for. Each finding
says what is wrong, where, and what its fix does. Double-click it (or **Go to**) to open its
level and select the object or tile.

There are three kinds of finding:

- **List errors** fail the game's own check of the object lists (Obj_ListOkay, what
  **Check the object lists** reports): an object reached twice (a next or contents link
  that loops or runs into another list), objects that are neither free nor on any tile,
  contents cut off by Is quantity set on their container, and faults in the free stacks (an
  index twice, an object in use, a wrong index, a count past the stack, an empty slot on no
  stack).
- **Problems** pass that check but make the game misbehave: a creature on the map but not on
  the active list (it never moves), an active entry that is free, twice or inside a trap; a
  mobile whose tile bytes are not its tile; a creature with 0 hit points (deleted when it
  first moves); a trap or trigger linking to a free index, or a trigger linking to something
  that is not a trap; a delete object trap aimed at the wrong tile; a UW2 animation entry for a
  free object; a book, scroll, gravestone, writing or text trap naming a string that is not
  there.
- **Warnings** may be meant: trap chains that loop, items or creatures in solid rock, items
  under the floor, UW2 animation entries with another tile, locked locks no key opens.

**Fix** repairs the selected finding; **Fix all of this kind** the ones listed; **Fix all**
everything with a fix in the levels checked (warnings only when **Fix all fixes warnings
too** is ticked). Links that arrive a second time are cut (nothing is lost: what follows was
already reached); lost objects are freed as the game frees them (free stack, active list,
animation list, and trap links to them cleared), or put back on the tile of the trap or
trigger that names them; the free stacks and the active list are rebuilt keeping their good
entries in order. Each fix, and each level's Fix all, is one **Edit ▸ Undo** step. Texts,
non-trap links, loops and keyless locks have no automatic fix. UW1 keeps its animation list
in a block of its own, which is not checked.

## Item cleaner

**Level ▸ Item cleaner…** (also from the Fixes window) removes objects by kind from this
level or every level: debris, bones, junk (broken weapons, blood stains), plants, food,
weapons and armour, treasure, potions, lights, or every item that can be picked up. By
default only what lies on the floor; tick **Also inside containers** or **Also what
creatures carry** for more. **Tiles from … to** and **Name contains** narrow it down.

**Preview** lists what would go, with counts by name, and what of those kinds is kept and
why. Never removed: keys, locks, quest items, talismans, runestones, books and scrolls with a
text, maps, anything a trap or trigger names (create-object templates, delete targets), the
kinds an inventory trap asks for, UW1's emeralds and Garamon's bones, UW2's storage crystals,
curious implement and lockpick, objects that hold something (contents, a trigger, a lock),
what characters who talk carry, and creatures, doors, furniture, decals, switches, traps and
triggers. Conversations can ask for ordinary items too: look before cleaning a quest area.

**Remove listed** deletes them as Delete does, one **Edit ▸ Undo** step a level. Nothing is
written to the game until you save the levels.

## Automap and map notes

**Level ▸ Automap and map notes…** (Ctrl+Shift+A) shows the player's map of the current
level and the notes written on it, as the game's map screen would, and follows the level
shown in the main window. The map lives in `LEV.ARK` (UW1 blocks 0x1A + level and 0x23 +
level; UW2 0x9F + level and 0xEF + level). A saved game has its own; a new game starts from
`DATA`'s, which in Underworld II already holds the castle with 14 notes.

- **Mark seen** paints tiles with the byte the game writes when you see one: the tile's
  shape, its floor's water, lava or ice, and (UW1) a door. **Mark unseen** clears them. The
  brush is 1, 3 × 3, 5 × 5 or 9 × 9 tiles; rock is never marked.
- **Mark everything seen** / **Mark nothing seen** do the whole level.
- **Notes**: click to add a note, drag one to move it, select it to change its text (one byte
  a character, at most 49, the game's limit) or delete it. A level holds up to 100.
- **Show the level under the map** draws the level faintly behind, to paint by.
- **Undo** steps back in the window; the changes are saved with the levels (Save levels)
  and count as unsaved work.

## Game tables

**Tools ▸ Game tables…** edits the small tables in the game's `DATA` folder. They hold for
the whole game, saved games too, so the `DATA` folder's copy is changed even when a saved
game is open. Each tab saves its own file (**Save NAME.DAT**); the old file is kept as
`NAME.DAT.BKP`, and the first version as `NAME.DAT.ORIG`. **Revert** goes back to the file
as saved. Changed values are marked, and a changed row of a list starts with •. Closing the
window with unsaved tables asks first.

- **Terrain** (`TERRAIN.DAT`): the terrain of each texture, with its picture. Choose a
  texture on the left, then its terrain on the right, or type the word itself.
  - Underworld: the walls (`W64.TR`, 210 textures) and floors (`F32.TR`, 52) have a table
    each. A wall can be normal, an ankh shrine, stairs, a pipe, a grating, a drain, the
    chained princess, a window, a tapestry or the textured door (the game looks for these);
    a floor normal, water or lava. Ice (0x30) is known to the engine but unused.
  - Underworld II: one table for `T64.TR`'s first 256 textures (the file has 264; the last
    8 have no terrain). The class is normal, water, lava or ice. Water can have a current
    that carries a swimmer north, south, east or west; ice is slippery (0-6) or grips (7). A
    wall can be stairs (3 and 4, drawn as stairs on the automap) or a shaft (5).
  - The automap's colours for water, lava and ice come from this table.
- **Class skills** (`SKILLS.DAT`): each class's strength, dexterity, intelligence and
  bonus points, and its five skill entries. A new character gets the three attributes, then
  the bonus points spread over them at random, none above 30. Then each entry in turn: an
  empty one gives nothing, one skill is given, several make a menu to pick one. An empty
  entry also loses the entries after it (the game does not step past it); **Check** says so.
- **Combinations** (`CMB.DAT`): ten rules, first item + second item → output. Drop one item
  on the other in the inventory (either order) to make the output; a source marked
  *used up* goes away. Neither item may be a stack or hold anything, and the first rule
  naming both is used. In Underworld a rule needs a source used up or it is ignored; its
  unused rules are zeros. Underworld II uses every rule; its unused ones are 0xFFFF.
  **In use** adds or removes a rule.
- **Light** (`SHADES.DAT`): a row for each of the eight light levels. The six values set
  how fast the light falls off with distance, how dark the nearest square is, where the
  darkening starts, how far you see (curvrad, 0-15), and from which shade the textures are
  drawn flat or smaller. The swatches show the shade at distance 0-15. The **Light** box
  above the 3D view shows a light level, and follows a saved table.
  - Underworld II only: **Minimum light** (`DL.DAT`), a byte for each of the 80 levels: a
    light level 0-9 that the player's light never falls below, and where it applies, on
    squares with the light flag or on squares without it. Level 1, the castle, has light 4
    on squares without the flag; its corridors have the flag, so they are dark. The light
    flag is painted with the Paint tool (Light flag on / off); **Flag every open square** and
    **Clear every flag** change the whole level at once, as one Edit ▸ Undo step saved with
    the levels.

## Level textures

**Level ▸ Level textures…** lists the textures the current level can use and follows the
level shown in the main window. A tile names its wall and floor textures by slot, and the
level's texture map says which texture each slot shows.

- Underworld: 48 wall slots (`W64.TR`), 10 floor slots (`F32.TR`, the ceiling is slot 9)
  and 6 door slots (`DOORS.GR`).
- Underworld II: 64 slots of `T64.TR`; the first 16 are also the floor slots (the ceiling is
  slot 15). And 6 door slots.

Each slot shows its picture, the texture number, how many open tiles use it, and the
texture's terrain (water, lava...). Choose a slot, then click another texture on the right
to show it there; textures already in the list are marked •. Each change is one Edit ▸ Undo
step, the 3D view and the paint brush's slots follow, and Save levels writes it with the
level (only the level's texture map block changes).

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

## Object tables

**Tools ▸ Object tables…**: the fixed properties of each kind of object, from `DATA\OBJECTS.DAT`
and `DATA\COMOBJ.DAT`, a tab per table:
- **Weapons** (0x00-0x0F): slash, bash and stab damage, swing charge and speed, skill
  (3 sword, 4 axe, 5 mace, 6 unarmed), durability (255 unbreakable). Entry 15 is the bare fist.
- **Ranged** (0x10-0x1F): ammunition and launchers. A launcher names its ammunition
  (item - 0x10); ammunition has the damage done when it hits.
- **Armour** (0x20-0x3F): protection, durability and the category, which decides the
  paperdoll slot (0 shield, 1 body, 3 leggings, 4 gloves, 5 boots, 8 hat, 9 ring).
- **Creatures** (0x40-0x7F): armour by hit zone, average hit points, attributes, race,
  movement, trading temper, the three attacks, senses, the weapons and loot they carry,
  experience and spells. New creatures and the monster swap read this table.
- **Containers** (0x80-0x8F): capacity and what they accept (-1 anything, an item id, or
  0x200 runes, 0x201 missiles, 0x202 scrolls, 0x203 food, 0x204 keys in UW2).
- **Lights** (0x90-0x9F): burn rate and brightness (light level 0-7) of the lights; lit
  lights are 4-7. The last 8 entries belong to the wands.
- **Food** (0xB0-0xBF): nutrition.
- **Triggers** (0x1A0-0x1AF): the action that sets each trigger off (0 move, 1 step on,
  2 pick up, 4 use, 5 look, 6 unlock, 7 open). In UW2, 0x1B0-0x1BF share these entries.
- **Animations** (0x1C0-0x1CF): how animated objects run their frames.
- **Common properties** (every item, COMOBJ.DAT): height, radius, mass (tenths of a
  stone), value, the flags (solid, can be picked up, can be owned...), toughness and
  quality words, fate when thrown, resistances and how it is drawn.

One row per item, with its picture and name. Click a cell to edit it; Enter or Tab keeps
the value, Shift+Tab goes back, Escape drops it. Numbers are decimal, or hexadecimal with `0x`.
A value outside the field's range is refused, and one the game cannot use (a weapon skill
that is not a weapon skill, an armour category no slot takes) is kept with a warning; both
show in the status line. Changed cells are marked and changed rows have a bar at the left.
Below the grid is what the selected field means, where it is in the file, its range and
the value on disk. Click a column heading to read about the field.

**Filter** shows the rows whose name or number matches; *Changed rows only* shows what you
edited. **Revert row** puts the selected row back as it is in the file; **Revert all** drops
every change. **Save** writes the changed files (old copies kept as `.BKP` and `.ORIG`).
The game reads them at start-up. A saved game has no copy of its own: the files in DATA
apply to every saved game.

## Checkpoints

**File ▸ Checkpoints…** While there are unsaved changes, the editor keeps a copy of them
every few minutes (5 by default; 0 turns it off). Each copy holds the changed levels and
automaps, and STRINGS.PAK or PLAYER.DAT when they have unsaved edits. Only the newest copies are kept: 20
by default, up to 1000. **Checkpoint now** takes one at once. **Restore** (or a double click)
loads one back as unsaved edits: each level becomes one undo step, and nothing in the game
folder changes until you save. Restoring the strings or the player cannot be undone, so the
editor asks first when they have unsaved edits of their own. The copies live in the editor's settings folder, one folder
per opened game. Unsaved conversation scripts are not included; their strings are.

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
  over tiles, or tick *A click fills the area* to fill from the tile under the mouse.
  **Fill the area under the selected tile as zone N** does the same from the selected tile.
  A fill moves left, right, up and down, only through tiles of the start tile's zone. It is
  stopped by walls, by the wall side of diagonal tiles, and by tiles holding a door of any
  kind, open or closed (portcullises and secret doors too); those tiles are not filled. It is
  also stopped by going up more than the **highest climb** (2 floor steps by default; a walk
  climbs 1), measured between the touching edges of the two tiles, so slopes count. Going down
  is never limited: a fill from the top of a drop reaches the bottom, not the other way.
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

**Tools ▸ Shuffle item properties…** changes what items are without moving them. Weapons,
armour, food, lights, tools, runestones, wands, spell scrolls, potions and rings stay where
they lie (on the floor, in a container or a creature's inventory) but may become something
else. Choose the settings in the tabs, press **Preview** to see the report, then **Shuffle**.
**Undo shuffle** puts back every object the shuffles from the window changed; each shuffle is
also one Edit ▸ Undo step per level, and nothing reaches the game until you save the levels.
A level edited after the shuffle is left as it is (its old records could undo those edits
halfway and break its object lists); the report names it, and Edit ▸ Undo steps back there.
The same seed on the same levels gives the same result. The settings are kept between runs
(`item-shuffle-uw1.json` / `-uw2.json` in the settings folder).

**Every setting starts from the world**: the relative weights are the counts in the levels
(the most common kind is 100), the chances, means, deviations and depth slopes are fitted to
the enchantments the levels hold, and the bars and curves follow them. Each tab has **From
the game**, which sets its values to what the levels hold now (after your edits); **All from
the game** does every tab. **World report** writes what the game's levels hold, as saved on
disk: the items by kind, the enchantments by kind and by level, the other effects and curses,
the wand, scroll and potion spells, the rings, and the qualities.

Weights are relative, from 0 to 100: a weight of 0 removes that kind. Never touched:
Caliburn (UW1's shiny sword), Altara's jewelled dagger (UW2), talismans, crowns, dragon skin
boots, fraznium armour, UW2's Guardian signet rings, objects that are templates inside traps,
and the ignored objects.

- **Weapons:** weights for axes, swords and daggers, maces and clubs, and ranged weapons with
  their ammunition; the share of launchers among the ranged objects (the rest become arrows,
  bolts or sling stones, never enchanted); the quality spread (± around each object's own
  quality). Then one row per enchantment kind (Accuracy, Damage) with its chance, the mean
  strength (1 = Minor) and the deviation of a normal draw. Kinds are tried in a random order and
  the first that succeeds is the weapon's. **Depth: chance** raises the chance (double on the
  deepest level at 100); **Depth: strength** moves the mean strength towards the strongest
  (all the way on the deepest level at 100). Level 1 is never changed; in UW2 the depth is the
  level within its world. **Special weapons** (UW2 only: LifeStealer, Undead Bane, Fire Doom,
  Stone Strike, the two door openers) are put on that many random melee weapons.
- **Armour** (gloves, boots and shields included): weights for leather, mail, plate and
  shields; a piece keeps its slot and changes material. Protection and Toughness rows as for
  weapons, with their two depth sliders. **Other effects** are the worn effects without
  degrees (Valor, Leap, Stealth, Missile Protection, Flameproof, regeneration…): the pieces
  they can go on, the chance a piece gets one instead of Protection or Toughness, the share
  **cursed instead**, the curse strength bars, and a bar per effect. **Special armour** puts
  exactly that many Flameproof pieces on top.
- **Food, lights, tools:** a weight per food and per light source (lit ones stay lit), and the
  least number of threads (5), poles (3) and fishing poles (3). When there are fewer, random
  food and lights lying on the floor become them.
- **Runestones:** a weight and a minimum (1) per rune. Minimums are met by changing runestones
  of the kinds with the most to spare; the report says when there are too few runestones.
- **Wands:** bars for the number of charges (1-30) and for the spell's circle. A spell is
  picked by circle, then evenly among that circle's spells (UW1: six to a circle, and a ninth
  bar for Mass Paralyze, Acid, Local Teleport, Mana Boost and Restore Mana, which have no
  runes; UW2: eight to a circle).
- **Scrolls, potions:** circle bars for spell scrolls and for potions. Potions can be limited
  to spells some potion already holds; in UW2 a potion takes the colour the game uses most for
  its new spell, since the colours tell the effect. Readable scrolls, maps and potions with
  special effects stay.
- **Rings:** a weight per ring, the chance a ring carries an effect, the share cursed instead,
  and a bar per worn effect. A curse of strength N does N d8 damage, never taking the wearer
  below 3 hit points, every time what is worn changes.
- **Ignored:** the objects both item tools leave alone (shared with Swap items).

**Custom distributions.** Each enchantment row has **Custom…**: bars for the strength (a
weight per strength) and, for the chance by depth, bars per level (UW1) or a curve over the
levels of a world (UW2). The quality rows have **Custom curve…** (a weight per quality 1-63).
Each replaces its sliders while ticked, and editing it ticks it. They start from what the
sliders give (**From the sliders**), or from the game for quality. On a curve, click to add a
point, drag to move one and right-click to remove one; the ends stay at the ends. The curve
never swings past its points, so no chance goes below 0. Drag bars up and down (the effect
bars left and right); sweeping across sets each bar crossed.

Spells and other enchantments already on objects stay unless **Keep spells and other
enchantments** is off; an object whose link leads to other objects (contents, a spell object)
never has its enchantment replaced.

### Swapping items

**Tools ▸ Swap items…** only moves the existing objects around: nothing is made or removed,
so every item keeps its count, and no weights apply. Tick the kinds taking part (weapons,
armour, food, lights, tools, runestones, wands, spell scrolls, potions, rings) and choose
**within each kind** (a sword stays a weapon) or **across all the ticked kinds** (a sword may
land where a torch was). The objects trade places whole: item, quality, enchantment, quantity
and owner. Charged wands swap among themselves with their spells and charges, since the
charges live in an object inside the wand; the few objects holding triggers stay. **Preview**,
**Swap** and **Undo swap** work as in the shuffle, and the ignore list is the same one.

**Ignored objects** (both tools): pick them from the list of everything the tools touch (with
a filter), or ignore the object selected in the editor. Wands enchanted directly, which never
run out, are ignored at first: UW1's bullfrog wand (it works the level 4 puzzle), UW2's
slightly worn wand of Telekinesis in the Scintillus Academy and its wand of Light. The list is
kept in `item-ignore-uw1.json` / `-uw2.json` in the settings folder.

### Swapping monsters

**Tools ▸ Swap monsters…** makes the monsters trade places. Each spot keeps its position,
home, goal and (by default) whatever lies in its inventory, and takes another monster's kind,
hit points, powerful flag and attitude. **Preview**, **Swap** and **Undo swap** work as in the
item tools; each swap is one Edit ▸ Undo step per level.

- **Swap** within each level, within each world (UW2), or across all levels.
- **Strength**: the game's own **experience value** for the kind (the default), hit points,
  damage per round (the three attacks weighted by how often each is used, damage plus
  strength / 5, and attack spells by how often the creature casts: missiles at their
  OBJECTS.DAT damage, area spells counted as 12), **combined** (√(hit points × (1 + armour /
  8) × damage), so neither side outweighs the other), or none (uniform). The **Strength
  table** tab lists every kind with these measures.
- **Target**: at 0 each spot keeps its difficulty, taking a monster about as strong as the one
  that was there, which already follows the game's depth curve; at 100 stronger monsters go
  deeper; between, a blend.
- **Spread**: at 0 the strongest monster goes to the hardest spot, strictly; at 100 the swap
  is uniform.
- **Keep the hit points at the spot**: a golem put where a 5-hit-point worm was gets 5 hit
  points, and the worm gets the golem's.
- **Carried items go with the monster**: otherwise they stay with the spot, keeping each
  level's loot where it was. Monsters carrying something then only swap within their level.
- **Powerful**: counted in every measure (armour × 5/3, +7..12 attack skill, +4..15 damage,
  experience × 1.5..3). **Roll it again** gives each moved monster a chance, raised with depth.

What stays, and why:
- Creatures with a conversation: the game finds them by their conversation number, and only
  on their own level (plot deaths, schedules, some traps). An option lets them move within
  their level. The **Whitelist** lets chosen ones move anywhere: their conversation and special
  death go with them, only the directions to them stop being right. UW1's level 2 gazer
  (Goldthirst's quest) and UW2's Listener (Altara's dagger) are whitelisted at first.
- Templates inside create-object traps, summoned creatures, creatures with no hit points, the
  Ethereal Void's nameless creatures, and the **Ignored** ones.
- Within their level only: creatures the game calls by race (UW2's liches, the trilkhun (race 0xB),
  level 1's guards, level 4's bloodworms; UW1's men on Tybal's level), creatures
  whose goal targets another creature, and (when carried items go along) creatures carrying
  something.
- Swimmers swap only with swimmers, and a monster only goes where its height fits under the
  ceiling.

**World report** lists the creatures as the world holds them now: how many, how many never
move and why, how many are bound to their level, every kind with its count and the levels it
lives on, every level with its count, how many could move, its experience total and average
and its strongest creature, and the creatures with a conversation.

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
| Anywhere | Ctrl+C, Ctrl+X, Ctrl+V, Ctrl+D | copy, cut, paste, duplicate the selected objects |
| Anywhere | Ctrl+H | undo history |
| Anywhere | F5 | play-test from the selected tile |
| View | Tab | the 2D map, and back |
| View | Ctrl+click, Ctrl+drag | add to the selection, select in a rectangle |
| View | right click | the right-click menu |
| View | drag a selected object | move it (onto a container: put it inside) |
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
| Anywhere | Ctrl+Shift+A | automap and map notes |
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
| `OBJECTS.DAT`, `COMOBJ.DAT` | Object tables |
| `TERRAIN.DAT`, `SKILLS.DAT`, `CMB.DAT`, `SHADES.DAT`, `DL.DAT` | Game tables |
| `SCD.ARK` | Schedules (UW2) |
| `DESC`, save folders | Saved games (rename, copy) |

Before writing, the old file is kept as `NAME.EXT.BKP`. The first time the editor writes a file,
it also keeps the untouched original as `NAME.EXT.ORIG`. The mods window compares with
those originals.
