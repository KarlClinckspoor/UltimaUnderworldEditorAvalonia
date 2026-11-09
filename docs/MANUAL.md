# Underworld Editor manual

The editor opens Ultima Underworld I and II, shows their levels in 3D, and edits levels,
conversations, strings, keys, the player, graphics and the executables' start square. Its
changes can be packed into mods. Nothing it overwrites is lost: it keeps the previous file as
`NAME.EXT.BKP` and, the first time it writes a file, the untouched original as `NAME.EXT.ORIG`.

Press **F1** or use **Help ▸ Manual** to open this manual. **Help ▸ Controls** lists the keys.
**Help ▸ About** shows the editor's version and the commit it was built from.

## Opening a game

Use **File ▸ Open game folder…** (Ctrl+O) and choose the game folder (the one with `DATA` in
it), the `DATA` folder itself, or a saved game (`SAVE1`…). The editor recognises the game by
its files. A saved game takes its levels and player from the save, and everything else from
`DATA`. A saved game is any folder with `LEV.ARK` and `PLAYER.DAT`, whatever its name (a copy
called `Before the bug`, say). A saved game kept outside a game folder is read together with a
game folder you choose once; the editor remembers that choice.

**File ▸ Open recent** lists the last games opened. A long path shows its start (the drive
or `/` and the first folder) and its end (the last folders), with `…` for the middle; the
item's tooltip shows the whole path.

### GOG's game.gog

GOG's current release (2025) does not install the games as files. Each install (`Ultima
Underworld`, `Ultima Underworld 2`) holds `game.gog`, the games' CD as an ISO image, which GOG's
DOSBox mounts as drive D:. The same image is in both installs and holds both games (`UW` and
`UW2` on the disc). **File ▸ Open GOG's game.gog…** opens it; so do opening the install's
folder with Open game folder and naming the image on the command line.

1. The editor checks the image's SHA-256. An image it was not checked with (another release,
   or a damaged copy) is copied only when you say so.
2. It asks where the copies go: **the editor's folder** (a new folder each time, under
   `GOG copies` in the settings folder) or **a folder you choose**. A chosen folder must not
   already hold a copy (`UW` or `UW2` with files in it), so no game you changed is
   overwritten.
3. It asks which game to edit now. Both are copied, into `UW` and `UW2`; the other one opens
   with Open game folder.

Each copy gets every file of its game on the disc, plus that game's `UNDEROM1` or
`UNDEROM2` files (the sound setup GOG's installer wrote), so it plays as GOG's does. Each game
has its own install, so the other game's files come from its install beside this one
(`Ultima Underworld 2` next to `Ultima Underworld`). When that game is not installed, the
editor says so: its copy keeps the disc's own `UW.CFG`, which has no sound. The folder of the
copies keeps a note, `GOG install.txt`, naming each game's install, so **Run in DOSBox** and
**Play test** use that install's DOSBox and settings.

**The GOG install is never changed.** GOG Galaxy and the install's shortcuts always play the
original game from `game.gog`, which never sees the copy. To play a game changed here, make
it a mod (**Tools ▸ Mods ▸ Make a mod**) and use it with the mod manager, which keeps its own
copy of the game for GOG's installs (see `ModManager/README.md`), or run the copy from the
editor (Run and Test).

**Open recent** keeps both: the copy's folder, to go on editing it, and `game.gog`, shown as
"Start over from …", which makes a fresh copy (the copies made before stay where they are).

When you open a saved game, the editor checks it for problems that crash or upset the game.
The status line names them, and **File ▸ Check and repair the saved game…** repairs them.

**Unsaved work.** Opening another game or closing the editor asks first when anything is
unsaved: levels and their maps, the player, strings and conversations, global variables,
pictures and zones. **Save** in that prompt writes all of them; Ctrl+S (Save the levels)
writes the levels and the player. **File ▸ Save all changes** (Ctrl+Alt+S) writes everything
unsaved at once, including the tool windows' work. **File ▸ Restore all…** drops every unsaved
change and reads the game again from its files, keeping the level and the camera. It first
keeps a checkpoint of what it drops, so **File ▸ Checkpoints…** can bring that back. The
global variables and graphics windows also ask when they close with unsaved changes, since
their edits are held in the window. A game's tool windows close when another game opens.

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
  A wall's texture starts at the wall's top edge (the ceiling, or the higher floor of a
  step) and runs down, a texture every 4 floor steps, as the game lays it. So under the
  ceiling (height 16) a wall ends on a whole texture only when its floor is at 0, 4, 8 or 12,
  and is cut at the bottom otherwise, in the game as here.
  A texture reads left to right as you face the wall from the open side, from whichever side
  you look, as the game draws it (`3d/GRIDDB.C` builds each face as the player sees it). So a
  picture is never mirrored: the two halves of Underworld's arched double door (wall textures
  38 and 39) join up from both sides.
- The ceiling (G), seen only from below, as in the game.
- Objects drawn the game's way, from COMOBJ.DAT's render field:
  - doors in their frames (open doors swung by their flags, the portcullis raised, the
    secret door in the wall texture);
  - levers, switches, writings and paintings on their walls;
  - furniture, pillars, bridges and so on as the 3D models built into `UW.EXE` /
    `UW2.EXE`;
  - everything else as its picture, always facing the camera.
- M switches the models off. Invisible objects and traps or triggers can be shown too.
- **Facing** (in the Show box) draws a white arrow on each object that faces a way:
  creatures, 3D models, doors, levers, writings and wall pictures (not the objects drawn as
  pictures that always face the camera). It points in the heading's direction (heading × 45°
  clockwise from north). On levers and wall pictures, whose heading points into their wall,
  it points out of the wall, the way their face looks.
- A yellow grid every N tiles, laid on the floors (H toggles it; N is set in the Show box).
  The view's edges label each line, as `X: 30` or `Y: 45`.
- **Light** (in the Show box): full light, or one of the game's eight light levels. These
  are no light, candle, torch, taper, lantern, night vision, light sphere or Wizard Eye, and
  the brightest. The view is then shaded as the game shades it. Each square gets one of the
  16 shading maps of LIGHT.DAT (MONO.DAT's greys for night vision), by its distance in
  squares and the level's row of SHADES.DAT. Squares beyond the level's range are not drawn.
  The centre is the point looked at, or the eye in first person. In UW2, a level for which
  DL.DAT sets a minimum light level is never shown darker than that minimum.

**Moving the camera.** Drag with the left or right button to turn round the point
looked at. Drag with the middle button, or with Shift, to move that point. Use the wheel
or + and − to move closer or farther away. W A S D or the arrows move, and Q and E turn, for
as long as they are held. The motion is smooth: the view picks up speed over a moment and
slows down the same way when the key is released. The speed grows with the distance from the
point looked at, and Shift makes moving three times faster and turning twice as fast. R and V
go up and down a floor step. F frames the selection, T looks straight down, and Home goes back
to the start or the player. A double click selects something and moves the view to it. Going
up or down a level keeps the camera over the same X and Y, at the new level's floor height.

## Finding and selecting

**Finding objects:**
- Creatures that talk show their name in brackets, and unique objects their plot name,
  as in "fighter (Corby)" and "shiny sword (Caliburn, the Sword of Justice)".
  Enchanted objects show their enchantment.
- The search box matches item names, NPC names ("human (Lord British)"), the text of
  books, scrolls, writings, gravestones and text traps, and `#index` or `0x` item ids.
- The chips narrow the list to weapons, food, monsters, keys, traps or triggers (any of
  the chosen kinds); Enchanted narrows it to enchanted objects.
- Each line shows the object's picture as the game shows it.
- Names are coloured by what the object is, in the list and at the top of the properties
  panel: creatures by their attitude (hostile red, upset brown, mellow green, friendly blue),
  enchanted objects purple. Traps and triggers are underlined. A creature keeps its
  attitude's colour; what it carries has its own line and colour.
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

Typing a value updates only that object's line in the list and the panel rows that changed,
so the list keeps the lines you expanded and its scroll position.

**Level ▸ Check the object lists** runs the game's own consistency check (Obj_ListOkay).

## Editing levels

Each level keeps its own undo history (Ctrl+Z, Ctrl+Y). A change made on several levels at
once (the key item scatter's Apply, the swaps, the item property shuffle, the named
challenges, and their own undo buttons) is undone and redone on all of them together, from
any of its levels. That way no level keeps items moved to or from another. If one of those
levels has been changed again since, the undo is refused and the status line names the level:
undo that later change there first. The undo is also refused when one of the levels no longer
holds its part of the change, either because a change made there after undoing it dropped the
redo steps, or because the oldest steps were dropped after 200. The change can then no longer
be undone or redone as a whole. Whatever such a change did outside the levels (the explored
maps, the named challenges' maps, the creature table and the player) is undone and redone
with it. The **Undo history** window counts the steps of the level shown; going back into a
change made with other levels undoes all of it. **Save levels** (Ctrl+S) writes LEV.ARK and
keeps `LEV.ARK.BKP`, plus `LEV.ARK.ORIG` the first time.

The toolbar above the view chooses the tool:
- **Select:** a click picks an object or tile; a double click also moves the view to it
  (so does double-clicking in the list).
- **Move:** click a square and the selected object goes there, keeping its height above the
  floor. A critter's home moves with it.
- **Paint:** drag over tiles to paint the light flag on or off (only that bit), a floor or
  wall texture from the level's texture list (shown with thumbnails), a tile type, a
  floor height, or a **tile template**. A template is a tile's type, height, floor texture
  and wall texture together (only the ticked parts), taken with **From the selected tile**,
  from the right-click menu, or with Ctrl+click. Choosing the template brush takes the selected
  tile's template, if none was taken yet (before that it is an open tile, height 4, slot 0 and
  slot 0). The line beside the boxes always says what the template holds.
  - **Ctrl+click** with the Paint tool takes the brush from a tile instead of painting it. It
    selects the tile, and the brush takes what that tile has: its template, its floor or wall
    slot, its type or its height. Solid rock can be picked for the type, height and template. With **Fill**, a click paints every tile the player can walk
  to from the clicked tile, by the zone fill's rules (no rock, no doors, climbs of at most
  two steps). **Paint selection** paints the selected tiles. Each stroke, fill or painted
  selection is one undo step.
  - **Strokes:** hold the button and drag to paint every tile the mouse passes over, even when
    it moves fast. A stroke can start where nothing is drawn. With the Paint tool a second
    press soon after a click is not a double-click, so a click followed by a drag paints on.
    While you drag, the mouse aims at the level as it was when the stroke began. The walls and
    steps the stroke itself makes (painting rock, raising floors) never stop the next tile
    being painted, whichever way you drag.
  - **Solid rock:** the tile type, floor height and template brushes reach solid tiles too,
    by a click or a drag. A solid tile has no surface of its own: each wall belongs to the open
    tile in front of it, and rock has no top. So the brush follows the mouse's line into the
    level and takes the first rock it meets between the floor and the ceiling, unless a floor or
    an object is nearer. That is the rock behind a wall, or the rock under the mouse seen from
    above (in the 2D map, or over the walls). Dragging across rock opens or changes every rock
    tile the mouse passes over. The texture brushes and the light flag paint the tile hit.
  - **Objects to the floor** (tile type, floor height and template brushes): after a tile is
    painted, the objects lying on it go on its new floor. The height follows the floor under
    each one's fine position, so on a slope they sit at different heights. Objects from 0x160
    on keep their height: pillars, bridges, writing and pictures on walls, switches, levers,
    traps and triggers.
- **Ctrl+wheel** raises or lowers the floor under the mouse, together with the objects on it.
- The messages about what was just done show over the top of the view, so a long one never
  changes the view's size.
- **First person** (P): walk the level at eye height. W A S D walk, Q and E or a drag turn,
  R and V look up and down, and Esc leaves. Rock and floors more than two steps up stop you.

You can type into any record field in the properties panel (decimal or `0x` hex), except
the list links, which the editor maintains. Small fields (fine X and Y, height, quality,
owner, heading) have − and + buttons; hold them down to repeat. One-bit flags (invisible,
enchanted, is quantity and so on) are checkboxes. The heading shows as an arrow, with north
up. An owner field has a list of races to choose from. Each race is named after its
creatures, from OBJECTS.DAT's race byte. A creature's
**Attitude**, **Goal** and **Saved goal** have lists too (▾), and the value's meaning shows
beside the number (8: mill about near home). The **Link** of a creature or a container has
a **+** that adds an object to its inventory or contents.

The panel's **OBJECT** and **TILE** headings fold their sections away when clicked, and
unfold them on a second click; the editor remembers which are folded.

**Add…** (Insert) opens a window that lists the items as a tree. The groups are the object
list's kinds (melee weapons, armour, containers and so on); a search opens the groups that
match. The form changes with the kind of item:
- a boulder or food gets quality, owner and quantity;
- a weapon, armour or ring gets its enchantment, from the weapon/armour list (which names
  what each really does) or the 64 spells, and how it is stored. In UW2 a warning says
  that the game's own names swap the Accuracy and Damage enchantments;
- a wand gets its spell and charges, kept in a spell object inside it;
- a book or scroll gets its text, previewed;
- a key gets its lock and description, and a lock gets its number, locked flag and
  lockpicking difficulty;
- a creature gets hit points, attitude and conversation;
- a trap or trigger gets its description.

The new object goes on the selected tile or into the selected container. The player
window's **Add…** uses the same form for the inventory. **Delete** (Del) removes the object
and everything it holds.

**Traps and triggers:** selecting one shows what its kind does and what each field means,
from event/TRIGGER.C. The square it acts on gets a **red bullseye**: a trigger's target, a
teleport's destination, a delete trap's square, or where the chains that reach a trap run.
**Choose the target in the view** lets you click the square instead of typing it; a
teleport can aim at another level. A teleport to another level shows its bullseye faded,
with the level and square written beside it; **Go to the teleport's target** (also in the
Edit menu) opens that level on the destination square. **Variable in globals…** opens the
global variables window on the variable a set or check variable trap uses.

### Collision boxes

**View ▸ Collision edges** and **View ▸ Collision faces** draw the objects' collision boxes in
the view: the edges as lines (seen through walls), the faces see-through. Static objects are
yellow-orange, creatures magenta, and a box that overlaps another is red.

The boxes are the game's own (motion/COLLIDE.C):
- **Shape:** a square that never turns, the object's middle ± COMOBJ's **Radius** in fine
  units (8 a tile), from the object's height up by COMOBJ's **Height** (8 z units a floor
  step). Radius 4 is the whole tile, as for the bridge. The sizes are in **Tools ▸ Object
  tables ▸ Common properties**.
- **What has one:** creatures, and static objects with a height whose Touch flag is set. Flat
  objects and the ones without Touch (coins, food) never block anything, so they get no box.
  The player is item 0x7F (radius 2, height 23).
- **Overlap, with a creature:** the game's test as a moving creature sees it. Bounds are
  inclusive, and creatures count their radius one smaller, so two creatures of radius 2 overlap
  within 2 fine units of each other.
- **Overlap, two statics:** they never move into each other, so only boxes that really share
  space count.

Nothing in the game pushes objects apart. A move that ends inside another box is undone, so a
creature placed inside one may never move. **Save levels** warns about overlaps with a creature
that the save adds to the level as it was on disk. The ones already there stay quiet, and
**Level ▸ Fixes** lists them all.

### Light flags

**View ▸ Light flags** marks every open square with the light flag (bit 8 of the tile, painted
with **Light flag on** and **off**). Each one gets a faint blue see-through sheet over its floor,
following slopes, drawn the way the zones are. The sheets follow your painting at once, in the
3D view and the 2D map. Underworld II uses the flag for light (DL.DAT, Tools ▸ Game tables);
Ultima Underworld keeps it but does not use it.

### Selecting, copying and moving

- **Ctrl+click** adds an object or a tile to the selection, or takes it out; **Ctrl+drag**
  selects every open tile inside the rectangle, and the objects on them. Selected tiles are
  outlined in blue, selected objects' tiles in yellow. Esc clears the selection.
- **Copy** (Ctrl+C), **Cut** (Ctrl+X), **Paste** (Ctrl+V) and **Duplicate** (Ctrl+D) work on
  the selected objects with everything inside them: a bag's contents, a wand's spell, a
  trigger's chain of traps. Paste puts them on the tile under the mouse (or the selected
  one), keeping their places relative to each other and their heights above the floor; it
  works on any level of the same game. Copying a creature that has a conversation makes a
  second creature with the same conversation; the status line points this out.
- **Drag** a selected object to move it (and the others selected with it) to another tile or
  to another place in its own tile. While dragging, the object is drawn where it would land,
  a small yellow square marks the eighth of the tile under the mouse, and the status line
  reads the place as tile.fine: `(10.2, 20.5)` is tile (10, 20) at fine X 2, fine Y 5. Moved
  within its tile, an object keeps its place in the tile's list. Dropping it on a container, a
  creature or a trigger puts it inside (into the trigger's chain for a trap). Esc cancels a
  drag.
- Deleting several objects is one undo step.
- **Objects on this tile** (in the right-click menu on a tile) lists every object there,
  with what each holds under it, and selects the one clicked. This is how to reach an object
  hidden behind another. Once selected, it can be dragged from anywhere on its tile, even
  where another object is in front of it.

**The right-click menu** opens on a right click without dragging. On an object it offers
copy, cut, duplicate, paste inside, add an object inside (to its inventory, for a creature),
delete, choose a trap's target, go to a teleport's target, **Make it run a trap** (then click
the trap: it joins the trigger's chain), **Put inside** (then click the container), frame it
and select its tile. On a tile it offers the objects on it, add an object here, paste here,
use it as the brush template, fill from here, paint the selection, select the tiles walkable
from here, play-test from here, and move the new-game start or the player there.

### The 2D map

**View ▸ 2D map** (Tab with the mouse over the view) looks straight down without
perspective, north up. Left and right drags move the map, the wheel zooms; clicks, the
right-click menu, Ctrl+drag selection and painting work as in 3D. The ceiling is hidden
while the map is shown. Tab goes back to the 3D view as it was. **View ▸ Export the map as a
picture…** writes the level from above as a PNG (16 pixels a tile): floors with their
textures, lighter where higher, rock dark, edges where the floor steps, and dots for
objects (creatures red, traps and triggers purple, doors brown, the rest yellow).

### Undo history

**Edit ▸ Undo history…** (Ctrl+H) lists the current level's steps, the newest on top (●
marks where the level is now) and the steps that can be redone above it (↷). Click a step to
go to the level as it was after it. Each level keeps its own history, up to 200 steps. A
step made on several levels at once goes back and forward on all of them together, as
Edit ▸ Undo does.

### World check

**Level ▸ World check…** looks across every level, with the unsaved edits:
- locked locks that no key opens. Keys count wherever they are: on the floor, in
  containers, carried by creatures, made by create-object traps, and in a saved game the
  player's inventory. A lock numbered 0 is listed separately: no key opens it (a lockpick,
  a spell or force does);
- keys whose lock number matches no lock in the levels;
- each level's objects against the game's limits (255 mobile, 768 static): marked when 90%
  full, and when full (the game then cannot drop, throw or create objects there either).

Double-click a line to go there.

### Run and Test

The **Run and Test** menu starts the game on what you are editing.

- **Run in DOSBox** (Ctrl+F5) runs the game in DOSBox on the game folder itself, not a copy,
  so the saved game being edited is the one the game restores (and games saved there stay).
  The editor writes a DOSBox configuration for the run. It starts from GOG's own (the
  `dosbox….conf` next to the game, so its CPU speed and sound settings are kept) or, without
  one, from the editor's defaults (an SVGA machine with EMS, a Sound Blaster 16 at 220, IRQ 7,
  DMA 1, and an MPU-401). Its `[autoexec]` is replaced so that C: is the game folder and the
  game starts at once. Unsaved changes are not in the files yet, so the editor asks whether to
  save them first.
- **Run in Underworld Exhumed** (Shift+F5) runs the native port on the game folder, with the
  folder as the port's home too, so Journey Onward lists the folder's saved games (the one
  being edited among them) and games saved in the port land there. The port also keeps its
  settings file (`uw1port.cfg` / `uw2port.cfg`) and its last recordings in its home.
- A saved game kept outside the game folder runs from a copy instead, as saved game 1 (in
  the editor's settings folder, `Run`); games saved there stay in the copy.
- **Programs and paths…** sets DOSBox (DOSBox, DOSBox-X or DOSBox Staging), extra DOSBox
  arguments, and the two ports (`uw1port`, `uw2port`). An empty path lets the editor look
  for the program itself. **Show the configuration a run uses** shows the DOSBox
  configuration.

### Play test

**Run and Test ▸ Play-test from the selected tile** (F5, or Play-test from here in the right-click
menu) runs the game in DOSBox with the unsaved levels and the player on that square. It works
on a copy of the game folder, made afresh each time in the editor's settings folder (`Play
test`); the game folder is never touched.
- With `DATA` open and a square on level 1, the square is patched into the copy's executable
  as the new-game start; start a new game.
- Anywhere else, the player needs a saved game: the open save itself or, with `DATA` open,
  the folder's first save, which then gets `DATA`'s levels as edited. The position is written
  to its `PLAYER.DAT`; restore that game (the editor says which).

The editor looks for DOSBox next to the game (GOG's `DOSBOX` folder) and where it is usually
installed; otherwise choose it in **Run and Test ▸ Programs and paths…**. It runs with the
same configuration as Run in DOSBox, with C: on the copy. Unsaved strings and conversations
are not in the copy.

### Checking in Underworld Exhumed

**Run and Test ▸ Check in Underworld Exhumed…** loads the edits with the game's own code.
[Underworld Exhumed](https://github.com/abedegno/underworld-exhumed) is a native port of
the original games, built from their decompiled source; get a release for your system
(`uw1-…` releases for Ultima Underworld, `uw2-…` for Underworld II) and unpack it. The
editor looks for `uw1port` / `uw2port` next to the game, in Downloads and next to the editor;
if it finds neither, use **Choose the port…** once.

Tick the levels to visit (**Edited** picks the levels with unsaved changes) and press **Run
the check**. The editor then:
1. copies the game, with the unsaved levels (and player), into the editor's settings folder
   (`Exhumed check`); the game folder is never touched;
2. puts a saved game holding them in the port's home there as slot I: the open save itself
   or, with `DATA` open, the folder's first save carrying `DATA`'s levels as edited. UW1 with no
   saved game at all starts a new character instead (every default, named "avatar");
3. runs the port with no window, from an input script: Journey Onward and slot I (or the
   new character), then for each level the port's test cheat `goto LEVEL X Y`, which moves
   the player there as a teleport trap does, so the game loads the level itself. The square
   is open, flat, dry and unoccupied, with open floor round it (on the start level, the start
   square when it qualifies), because the game kills a player it cannot place. When some
   ticked levels have no such square, the check names them all at once; **Untick the levels
   with nowhere to stand** unticks them. A level takes about five seconds.
   By default the player stops once on each level, on its own square, for about five
   seconds. That checks that the level loads and that the area round that square runs, but
   much happens only near the player: creatures more than 10 squares from him stand frozen,
   triggers fire only when walked on, and only what is in view is drawn (a bad texture or
   model crashes only when drawn). Three options make the check more thorough, each adding
   stops (the window says how many, and about how long the run takes):
   - **Also stop beside every edit**: a stop near each spot the unsaved edits changed (tiles,
     and objects added, removed, moved or changed), at most 12 a level, 5 squares apart. When
     there is no flat square near the edit, any open, dry, empty square with room round it is
     used.
   - **Sweep each level**: a stop in every 16 × 16 area of the level that has room to stand,
     up to 16 a level.
   - **Look around at each stop**: the turn-left key is held for one full turn (about 3.1
     seconds in both games: `motion/PHYSICS.C`), so the game draws everything round the stop.

   The choices are remembered. Triggers still do not fire and nobody is talked to: the player
   is put down on empty squares and does not walk.
   With **Keep the player alive** (on at first) the player is toughened up first, since the
   port has no god mode. The check's saved game puts the player at experience level 16 (the
   highest). The port's `set` cheat raises defense to 30 and strength to the most the hit
   points allow; the maximum hit points follow (30 + level × strength / 5), as they do when a
   level is gained. The cheat then fills the hit points before each level, so monsters near
   the square do not end the check. At level 16 that is strength 70 and 254 hit points: the
   game keeps the maximum in one byte (`game/SKILLS.C` `player_compute`), so more strength
   would wrap round to a few hit points. A new character (UW1 with no saved game to start
   from) stays at level 1, as no cheat sets the level, and gets strength 99 and 49 hit points;
4. in UW1, saves in slot I at the end (Ctrl+S, slot I, Enter), so the game writes every
   level it visited, and checks those levels again here (the object-list check and the list
   errors of **Level ▸ Fixes**).

The report says what went wrong: a crash of the port ("host fault"), a halt, the game's own
fatal error ("Underworld can no longer run"), a `goto` the game refused, a level never
reached, or findings in the levels the game saved. When the port logs a "far null pointer
read", the report lists it with the level it came after. The game read through a null
pointer, which DOS allowed, so it carried on; this can mean an object link pointing nowhere,
or one of the game's own slips (after a death, say). The report ends with everything the port
printed and shows the last screen. The check's folder keeps the script (`script.txt`), the saved game
(`home/SAVE1`) and the save as written (`before`), so a problem can be run again by hand:
`uw1port --data game --home home --input-script script.txt` (with `EXHUME_TEST_CHEATS=1` set,
which the port asks for before it runs a test cheat).

**Show the game's window while it runs** runs the port with its window visible instead of
hidden (the choice is remembered), so you can watch where the player is and what the game
draws when something goes wrong. The script still plays the keys at set times, so do not use
the keyboard or mouse in that window; the run takes as long as a hidden one.

While a check runs, **Run the check** becomes **Stop the check**, which closes the port
(nothing is then checked). Closing the window while a check runs asks first (**Stop and close**
or **Keep running**); the port also stops when the editor closes.

**Level 9 in UW1.** Underworld Exhumed crashes on arriving at level 9 (the void), whatever the
levels hold. It crashes with a new character on the shipped game, after the `goto` cheat, and
after the talisman ritual's moongate (the last talisman burnt in the Slasher's lava on level 8
with Garamon buried: `game/SKILLS.C` `check_victory` sends the player to level 9 (27, 23)). It
is a bug in the port (UW1 v1.0.2, and a build of November 2026), so the check cannot test
level 9. While **This port crashes on UW1's level 9** is ticked (on at first, remembered),
level 9's box says so and is left out of **Edited**, **All** and the first choice. If you tick
level 9 by hand, it comes last, after the save in slot I, so the other levels are still
checked and saved, and its crash is reported as a note ("PASSED for the rest; level 9 could
not be checked") rather than a problem. The **DOSBox spot test** checks level 9 in the original game. Untick the
option once a release of the port fixes level 9: a crash there is then a problem again.

Underworld II is checked from a saved game and does not save at the end (its save menu is
not scripted yet). Unsaved strings and conversations are not in the copy.

### DOSBox spot test

**Run and Test ▸ DOSBox spot test…** takes the squares the Underworld Exhumed check stops at
and has you play them in the original game, four at a time. Tick the levels (**Edited**,
**All**, **None**) and the same options as the check: each level's own square always, plus
**a spot beside every edit** and **a sweep of each level** (one in every 16 × 16 area with
room to stand). The window says how many spots that makes and in how many rounds.

The tester is a new character: level 1, with no experience, quests, runes or skills, and the
game variables and clock of a new game (as `game/CHARGEN.C` `init_char` sets them). The
tester has the attributes and **Defense** you set (30 each at first), and full hit points and
mana as the game works them out (`game/SKILLS.C`: 30 + level × strength / 5 hit points).
With **Wear the best armour**, the tester also wears the best piece for each body slot from
`OBJECTS.DAT` (helmet, breastplate, plate gauntlets, plate leggings, plate boots, all in
perfect condition; the unbreakable specials such as the dragon skin boots are left out) and
holds a tower shield in the shield hand. The tester carries nothing else. The player object
the game restores (`inv/INVSAVE.C`) is taken from one of the game's saved games when there is
one (the open save, or the folder's first); otherwise it is built from scratch, as the game's
own `InitPlayerRec` builds it.

**Start the test** copies the game into the editor's settings folder (`Spot test`; the
game folder is never touched), with the levels as they are now (unsaved edits included).
Every round uses those same levels. A round uses saved games 1–4. Each holds those levels,
the tester standing on its spot, a new game's conversation globals (`BABGLOBS.DAT` with
every global 0) and a description naming the spot (`5/37 L2 31,40 sweep`: its number, the
level, the square and why). DOSBox then starts on the copy, with GOG's configuration (or the
editor's), as **Run in DOSBox** does. In the game choose **Journey Onward** and the first
saved game, look around, then **Options ▸ Restore Game** for the next.

When you close DOSBox, the window comes back and shows how many spots have been tested and
how many are left, the four just played, and a box for notes. **Next 4 spots** counts the
four as tested (the notes go in the test's log, `spot-test-log.txt`) and starts DOSBox on
the next four; **These 4 again** starts DOSBox on the same four; **Stop the test** ends it
there. If your DOSBox hands over to another program and closes at once, press **DOSBox is
closed** when you are done. If DOSBox closes within seconds the window says so instead of
asking. The spot test works with UW1 only for now.

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
  recipe gives it a copy of the door's own lock; **Unlock the door** leaves the trap without
  one.
  In Underworld II the lock is left alone unless you unlock it.
- **Teleport:** stepping on a square sends the player to another square, on this level or
  another one (change level in the main window before picking). **And a way back** adds a
  second pair; it must start and arrive on different squares from the first, or the player
  would be sent straight back. Only the player is teleported.
- **Pressure plate:** stepping on a square runs a trap. In Underworld II it can be a
  pressure plate, which fires when weight comes onto the square; otherwise a move trigger.
- **Object trigger:** picking up, using or looking at an object runs a trap. The object
  must be a single one (not a stack, a creature or an enchanted item) and hold no trigger
  yet; it is made to hold the trigger.
- **Wandering monsters:** a spawner, as the shipped levels of both games have them (34 in
  Ultima Underworld, 49 in Underworld II). It is a create-object trap with no trigger,
  holding its creature, and the game runs it by itself: every 8 game minutes (Ultima
  Underworld) or 10 (Underworld II), with chance 1 in 4, while the player is more than 8
  squares away, and whenever he sleeps. Each run makes the creature with the **Chance** set
  (the trap's quality q gives (63 − q) in 63), unless a creature it made is still within 4
  squares; spawners within 4 squares of each other hold each other back. Made creatures are
  temporary: they go when the player sleeps. The square must hold no other create-object
  trap, since the game takes only the first one on a square. In Underworld II the creature
  can be the **random monster** (0x07F, the "adventurer"), chosen afresh each run: creature
  0x40 + row × 16 + column, with rows up to (the level's place in its world × 3 + the
  castle plot's progress) / 9 and columns up to the player's level + the castle plot's
  progress + 1, at most 16; a strong creature is kept only with chance 1 in its strength.
  So deeper levels, progress in the castle plot and a stronger player bring stronger
  monsters: this is how mages and worse turn up in Britannia's sewers late in the game.
  Ultima Underworld has no random monster; its spawners copy their creature as it is.

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

**Tools ▸ Conversations ▸ All conversations…** (Ctrl+Shift+C) lists every conversation in CNV.ARK, by slot and
by the name the creature talks under. The list filter matches slots, names and any text a
conversation says. The window has four tabs:
- **Dialogue:** the conversation as source you can edit (see "Editing on the Dialogue tab"
  below), rebuilt from its code by following the conversation machine's stack (conv/BABL.C)
  and read back in the conversation language (`docs/conversation-language.md`):
  - each function with its arguments and its locals (`int local3[5];`);
  - `say` and `respond` with their text and string number;
  - `if` / `else if` / `else`, `while` loops, `return e;` for a function's value;
  - each menu as a `menu { }` block: the choices in the order the player sees them, each
    with what it does (`"Tell me more." #7:` and the lines under it), a choice nothing
    tests for with `;`, and `fmenu` for the filtered menu with each choice's condition;
  - `switch` where the code compares one value with several numbers;
  - calls with every argument shown: a constant the script passes to a built-in is in the
    call (`get_quest(32)`), a variable passed by address has `&`.
  A `goto` and its label (named, see "Names and F2" below) are left only where the script
  itself jumps between parts in a way no `if` or loop can show (UW1 has 277, mostly in
  conversation 4; UW2 895). Functions the script never calls (the compiler's small library)
  are folded to one line unless you ask (**Fold uncalled functions**): they stay
  in the text, as they are part of the conversation.
- **Strings:** the conversation's block of STRINGS.PAK, edited in place. Each string shows
  where the script uses it. **Add string** appends one, for the script to say.
- **Script:** the code as assembly, one instruction a line (conv.h's opcodes):
  - labels for jumps and calls;
  - imported functions by name;
  - the header and import table as directives;
  - comments with each address and the strings.
  **Apply** (Ctrl+Enter) assembles it, as one step **Undo apply** takes back. A mistake is
  reported at its line. Disassembling and assembling every shipped conversation of both
  games gives back the same bytes.
- **Who talks:** the creatures in the levels that use the slot (whoami, or the generic
  slot for whoami 0). Double-click one to go to it in the main window.
**Save** (Ctrl+S) writes CNV.ARK and STRINGS.PAK in `DATA`, with only the changed blocks
rewritten (text typed and not applied is applied first). UW2's blocks are compressed again.
The old files are kept as `CNV.ARK.BKP` and `STRINGS.PAK.BKP`. When the window closes, its
applied edits stay in memory until the game is closed. **Revert conversation** puts the shown
conversation back as last saved.

### Editing on the Dialogue tab

The Dialogue tab is a text editor of the conversation's source. Change a line, add a menu
choice, call another function, add one: the compiler (`ConversationCompiler`, described in
`docs/conversation-language.md`) turns the text back into the conversation's code.
- **The editor:** colours for keywords, strings, numbers, string numbers (`#7`), comments,
  the script's functions, the game's built-ins and its variables (in the light and the dark
  look); line numbers; folding of each function (the box beside its first line); **Ctrl+F**
  finds (and replaces), **Ctrl+L** goes to a line, **Ctrl+Z** and **Ctrl+Y** undo and redo
  typing, **Tab** and **Shift+Tab** indent and outdent the selected lines by four spaces.
  **Wrap long lines** breaks a line longer than the editor is wide onto more lines on screen,
  the rest indented under its start; the text keeps its lines (a long `say` is still one
  line, with one line number). The choice is kept in the settings.
- **Checking as you type:** a moment after you stop typing, the text is compiled in the
  background. Mistakes are underlined (errors in red, warnings in amber; hover one for its
  message) and listed under the editor; double-click one to go to it. The line above the
  list says whether the text compiles and how many functions it changes.
- **Apply** (Ctrl+Enter) compiles the text and puts the conversation and its new strings in
  the editor's copy of CNV.ARK and STRINGS.PAK, as one step. It is refused while the text has
  errors. A function whose text you did not change is copied from the old code byte for byte;
  only the edited functions are compiled. A string whose text is a string of the
  conversation's block is that string; a new or changed line becomes a new string at the end
  of the block (the old one stays, in case something else says it). After Apply the text is
  the conversation's again, with the new strings' numbers. The Script and Strings tabs, the
  graph and the simulator follow. Nothing is written to the game until you save.
- **Undo apply** and **Redo apply** step through the Applies of the shown conversation (and
  its reverts and Script applies), its code and strings together. A string edited on the
  Strings tab or a name changed with F2 joins the last Apply's step: Undo apply takes it back
  with that Apply, and nothing undone can be redone over it. Saving starts them again.
- **Revert to last applied** throws away the edits not applied (Ctrl+Z in the text brings
  them back). **Revert to the original** puts back the conversation and its strings as the
  game shipped them: from `CNV.ARK.ORIG` and `STRINGS.PAK.ORIG`, the untouched copies the
  editor keeps the first time it saves, or the files as they are when there are none. It is
  one step too: Undo apply takes it back.
- **Simulate** (Ctrl+R) on the Dialogue tab with edits not applied runs the text as typed,
  compiled in memory and not applied; the simulator's title says so. When the text does not
  compile, the status line says why and the caret goes to the first error. Simulate again
  after more edits.
- **Names:** the text refers to functions, labels and globals by their names (see "Names and
  F2"). **F2** renames the name at the caret, in the text and in your names. **F12** or
  **Ctrl+click** on a function, label or local goes to where it is defined. Hover a string
  for its number in the block (or "a new string"), a built-in for what it does (from the
  game's code), a variable the game fills in for what it holds. An edited function would lose
  its names (they follow a function's body, and the edit changes it), so Apply keeps the names
  your text gave it in your names file.
- **Unsaved work:** text typed and not applied counts as unsaved work. Closing the window, or
  picking another conversation in the list, asks whether to apply it (or discard it, or stay), and checkpoints keep it (`dialogue-NNN.txt`, as `conversation-NNN.txt`
  keeps a Script typed and not applied); restoring a checkpoint puts the text back in the
  editor. The Dialogue and Script tabs each refuse to apply while the other has text not
  applied.

Saving CNV.ARK also keeps the conversations' private globals in step. The game reads a
conversation's private globals from its record in the save's `BGLOBALS.DAT` (at most as many
words as the script has variables), and a new game makes that file from `DATA\BABGLOBS.DAT`
(conv/BABL.C `init_babl`, `bab_get_globals`). When a conversation's variable count grows, or
an empty slot gets a conversation, saving grows or adds its record in `BABGLOBS.DAT`, and in
the open save's `BGLOBALS.DAT` too (new words 0, the others kept). Records are never made
smaller. Other saved games are not changed.

#### Recipes

Worked examples in the Dialogue tab's language, each based on a shipped conversation. Each was
spliced into the UW1 conversation named, compiled and run in the simulator
(`tests/UwEditor.Core.Tests/ConversationRecipesTests.cs`). A recipe that is a function can go
anywhere in the source: call it from a menu choice or from the opening function. The item
numbers are UW1's; where UW2 differs, it says so. First:
- Every conversation imports the game's built-ins and the variables it fills in, so any
  conversation can use them (but five UW1 ones, slots 11, 26, 115, 186 and 295, lack
  `find_barter_total`: the compiler says so).
- A private global (`global34`, or a name such as `met_before`) must be below the
  conversation's variable count, given in the first line's comment. The compiler does not add
  variables: to get a new one, raise `.variables` on the Script tab and Apply, then use the
  next number. Saving grows its record (above).
- Items are given by their object id in decimal (177 is 0xB1, a loaf of bread). An entry of 1000
  or more names a class of 16 items, 1000 + id / 16: 1011 is 176-191 (food), 1000 is 0-15 (axes,
  swords, maces).
- New lines become new strings at the end of the conversation's block when you Apply.

**Trade one item for another.** The player gives rotworm stew, the NPC gives dragon scales, as
the old troll of slot 17 does.

```
trade_stew_for_scales() {
    int stew, given;
    stew = find_barter(283);
    if (stew == 0) {
        say "Thou hast not offered me any rotworm stew.";
        return;
    }
    give_ptr_npc(&stew, -1);
    given = take_from_npc(285);
    if (given == 2) {
        say "Thine arms are full. I leave the scales on the floor.";
    }
}
```

- Before choosing the answer that calls this, the player puts the stew in one of their trade
  slots on the conversation screen (four, UW2 six) and clicks it to select it. `find_barter`
  gives that object, or 0 when no selected slot holds item 283; `give_ptr_npc(&stew, -1)` moves
  it to the NPC (-1: the whole stack).
- `take_from_npc(285)` hands over the NPC's first object of type 285: into the player's hand,
  else a free trade slot (1), else onto the floor beside the NPC (2). It gives nothing (0) when
  the NPC has none or the player's hand already holds something.
- The NPC must own the item: put it in the creature's inventory in the level, or make it with
  `do_inv_create` (next recipe). A creature whose **Inventory made** flag is clear also gets its
  class's loot the first time it talks or trades.

**Give an item after a menu choice.** Once only.

```
offer_bread() {
    say "Wilt thou take a loaf of bread for the road?";
    menu {
        "Yes, I thank thee.":
            if (global34 == 0) {
                global34 = 1;
                do_inv_create(177);
                if (take_from_npc(177) == 2) {
                    say "Thy hands are full. I leave it at thy feet.";
                }
            } else {
                say "I gave thee my last loaf already.";
            }
        "No, keep it.";
    }
}
```

- `do_inv_create(177)` makes a loaf (quality 63) in the NPC's inventory and `take_from_npc`
  hands it over. Biden (slot 143) gives a scroll this way, Anjor (slot 161) a gold nugget.
- `global34` keeps the gift to once: a private global keeps its value from one conversation to
  the next. The test splices this into Corby (slot 1), whose 34 variables end at `global33`,
  after raising `.variables` to 35; use a global your conversation has and nothing else uses.
- A choice written with `;` has no statements: the menu just ends.

**Wait for the player, threaten, attack.** The waiting is object data; the conversation does
the rest.

```
thou_hast_come_far_enough() {
    say "Thou hast come far enough, @GS8. Draw thy weapon!";
    menu {
        "Wait! I mean thee no harm.":
            say "Thy words mean nothing to me.";
        "Then have at thee!";
    }
    npc_attitude = 0;
    npc_goal = 5;
    npc_gtarg = 1;
    exit;
}
```

- In the creature's object properties set **Goal** to 10. It then stands where it is; when it
  sees or hears the player within about 2.5 tiles it turns to face them, and when the player is
  within about 1.5 tiles and facing it the conversation starts by itself (critter/AI.C
  `crit_talk`). Rodrick (slot 142, level 4) ships this way, as do the stone golem of level 6 and
  the goblin guards of level 7. Make this function the first thing the opening calls.
- UW1: **Attitude** may be anything, 0 hostile included (Rodrick's): a creature with goal 10
  talks however it feels. UW2: a hostile one with goal 10 turns to guarding and attacks without a
  word, so give it 1 to 3.
- When the conversation ends the game copies `npc_attitude`, `npc_goal` and `npc_gtarg` back to
  the creature and sets its **Talked to** flag. Goal 5 with target 1 attacks the player;
  attitude 0 keeps it hostile, and clicking it to talk gets "You get no response" (UW1 lets
  Rodrick, the golem and Tyball talk anyway). The library's
  `attack_and_end()` does the same with attitude 1 (upset), through `end_conversation`.
- An ending that leaves the goal at 10 starts the conversation again as soon as the player
  faces the creature: a peaceful ending sets `npc_goal` too (0 stand, 8 mill about).
- `set_race_attitude(race, attitude, range)` turns its kind nearby too, as the level 7 goblin
  guards (slot 209) do with `set_race_attitude(9, 0, 6)`.

**Barter, likes and greed.**

```
trade_goods() {
    int finished, likes[3], dislikes[2];
    likes[1] = 1011;
    likes[2] = -1;
    dislikes[1] = 1000;
    dislikes[2] = -1;
    set_likes_dislikes(likes, dislikes);
    setup_to_barter();
    finished = 0;
    while (!finished) {
        menu {
            "I make thee this offer.":
                if (do_offer("Done!", "That is not enough.", "That is worse than before!", "I tire of this.", "Thou hast offered nothing.")) {
                    finished = 1;
                }
            "I demand that thou givest me these items.":
                if (do_demand("Take them, then.", "Never! Thou shalt pay for that!")) {
                    finished = 1;
                } else {
                    attack_and_end();
                }
            "Excuse me, I must think about this deal.":
                do_judgement();
            "I do not wish to barter any further.":
                do_decline();
                finished = 1;
        }
    }
}
```

- There is no separate barter screen: the trade slots are on every conversation screen.
  `setup_to_barter()` fills the NPC's slots from its inventory, skipping its first weapon and
  anything worth nothing (with more items than slots, some are picked at random). The player
  selects items on both sides. `do_offer` tries the trade and says one of its five lines
  (accepted, not enough, worse than the last offer, out of patience, nothing selected); it gives
  1 when the trade is made. `do_demand` asks for the NPC's selected items for nothing,
  `do_judgement` says the player's own appraisal, `do_decline` puts the NPC's items back. The
  mountainman traders (slots 276 and 278) have this loop.
- After Apply the Dialogue tab shows `do_offer`'s and `do_demand`'s lines as their numbers
  (`do_offer(47, 48, 49, 50, 51)`); edit them on the Strings tab.
- A refused demand sets the NPC to attack, but when the conversation ends the game copies
  `npc_goal` back over that, so call `attack_and_end()` (or set `npc_goal` and `npc_gtarg`), as
  the shipped traders do.
- `set_likes_dislikes(likes, dislikes)` takes two lists ending in -1, of item ids or classes. A
  liked item is worth 1.5 times as much to the NPC; a disliked one is worth nothing and is not
  taken. The game keeps only where the lists are, and forgets them when a conversation starts:
  keep them in locals of the function that runs the haggling (as here) or in private globals,
  and call it every conversation before the first offer. Zak (slot 28) calls it only on the
  first visit, so later visits trade without them; slots 6, 276 and 278 have the call in a
  function nothing calls.
- Greed (the gain in per cent the NPC wants), patience (bad offers it takes) and how far off its
  valuations are come from its creature class: **Haggle**, **Patience** and **Shrewd** in Object
  tables' **Creatures**, blurred for each creature by its object number, then the player's
  **Charm** lowers the greed by 2 a point and raises the patience by half. The game works them
  out when the conversation starts and no built-in reads or changes them, so a conversation
  cannot make a trader greedier: change the class's figures (every creature of the class) or use
  another class. The Barter simulator (above, under Balance) shows a trader's figures and what it
  accepts.

**Answers the player types.** Passwords, riddles, names.

```
ask_for_the_password() {
    int typed;
    say "Halt! What is the password?";
    menu {
        "Other...":
            typed = babl_ask();
            if (contains("Deco Morono", &typed)) {
                say "Pass, friend.";
            } else {
                say "That's not it! Begone!";
                upset_and_end();
            }
        "I do not know it.":
            say "Then thou shalt not pass.";
    }
}
```

- `babl_ask()` lets the player type up to 50 characters and gives the number of a string
  holding them; keep it in a variable and pass its address. The shipped scripts offer it as a
  menu choice "Other...".
- `contains(word, text)` is 1 when the text holds the word or phrase as a whole word, ignoring
  case: "deco morono, I say" passes, "decomorono" does not. `compare(a, b)` is 1 when the two
  strings are the same, ignoring case: the whole answer must match.
- Shipped ones: Steeltoe (slot 21) wants the password "Deco Morono"; Garamon (slot 27) waits for
  "Talismans", then "volcano" or "lava", counting wrong tries in a global to give hints; Murgo
  (slot 24) has the player guess his name and try lizardman words, with `compare`.
- The mantras chanted at shrines are not a conversation: the game's own code reads them (string
  block 2), and the conversation editor cannot change them. **Randomizer ▸ Randomize mantras
  (UW1)** swaps them and changes the conversations that name them.

**One NPC knows the player met another.** In A's opening function (Hagbard's, slot 19, has
`set_quest(1, 1);` there):

```
set_quest(12, 1);
```

In B, a function its opening calls first:

```
greet_hagbards_friend() {
    if (get_quest(12) == 1) {
        say "Hagbard told me thou wouldst come. Welcome, friend.";
    } else {
        say "Thou art new here. Speak with Hagbard, our leader, first.";
    }
}
```

- Quest flags are kept in the player's record (`PLAYER.DAT`): every conversation reads the same
  ones, so they carry news from one NPC to another. Hagbard sets quest 1 when first met, and
  Gulik (slot 20) reads it to catch the player in a lie.
- UW1 has quest flags 0-31 and quest bytes 32-35, but a conversation can use only flags 0-14
  (`get_quest` and `set_quest` shift a 16-bit 1: flags 16-31 read 0 and never change, and
  setting 15 sets 16-31 too). The shipped game uses 0-11 and 32, so 12 to 14 are free, and 33 to
  35 hold 0-255. UW2 has flags 0-127 (set them
  to 0 or 1 only) and bytes 128-143. **Tools ▸ Global variables** lists which ones the
  conversations use.
- A private global (`met_before` in most conversations, set by `end_conversation`) is kept per
  conversation slot in `BGLOBALS.DAT`: no other conversation can read it, and every creature
  that talks with that slot shares it (all goblins with the generic goblin conversation).
  `npc_talkedto` is per creature: its **Talked to** flag, which the game sets after every
  conversation with it. Hagbard opens differently with it.
- The game variables (`x_traps`, shared with set and check variable traps) are another shared
  store.

### Names and F2

The Dialogue, Script, graph and simulator show names for a conversation's functions, labels
and variables in place of `func_02B5`, `L02CF`, `global33` and `local22`. Names are only for
reading: they never change the bytes, and the Script assembles to the same code with them.
- **Where they come from:** one names file per game, shipped with the editor
  (`docs/conversations/names-uw1.json` and `names-uw2.json`). A first pass named:
  - the compiler's library, by hand: the functions found in every conversation, such as
    `end_conversation`, `goodbye`, `attack_and_end`, `set_attitude_and_end`, `get_time`;
  - each function that says something or shows a menu, after its first line
    (`hail_stranger_be_thou`) or its menu's first choice (`menu_i_wish_to_become`);
  - the function `main` calls, when it says nothing itself, `start`;
  - labels by role: `done` (the function ends), `loop` and `menu_loop` (jumped back to),
    `end_of_menu` (a menu's answers meet), `not_choice_2` (an answer other than 2),
    `otherwise`, `end_if`. Names are unique in a conversation, so repeats get `_2`, `_3`;
  - globals where their use is plain: `met_before` (set when a conversation ends, read
    where it starts), `quest_32` (a copy of quest 32), `xclock_3`.
- **When they apply:** a slot's names are kept with the SHA-256 of its CNV.ARK block. They
  apply as they are to that block. When the block is another (the conversation was edited,
  or another release), the names follow each function by its body (its code, with jumps
  counted from the function and calls from the call), labels and locals with it; globals
  keep their address. Library names follow bodies in every slot.
- **F2** renames. On the Dialogue tab it renames the function, label or variable whose name
  is at the caret, in the text too when it has edits not applied (a local only in its
  function); on the Script tab, the function or label at the caret. A name is letters, digits and `_`, not starting with a digit. It must not
  clash with another name in the conversation, an imported function or variable, an
  instruction or a word the views use (`if`, `while`, `menu`, `goto`, `say`...). An empty name goes back to
  the shipped one. Imported variables keep the game's names. With unapplied Script edits,
  apply or undo them first.
- **Your renames** are kept in `conversation-names-uw1.json` (or `-uw2`) in the editor's
  settings folder, and come before the shipped names. **Export names…** (Dialogue tab)
  writes the shipped names with yours folded in, in the shipped file's format: copy it over
  the file in `docs/conversations` to ship them. A rename made in an edited conversation
  goes to the function with the same body in the shipped block; one in new code is left out.
- **Editing the file by hand:** `library` names the shared functions by body; `slots` holds
  each conversation's `sha256`, `globals` by memory address, and `functions` by hex address,
  each with `name`, `body`, `labels` (hex addresses) and `locals` (frame offsets: 1 and up the
  locals, -2 and down the arguments). A rerun of the first pass (the test
  `Write_the_shipped_names_files_when_asked` with `UW_WRITE_NAMES=1`) keeps the names already
  in the files.

### Conversation simulator

**Simulate…** in the conversation window (Ctrl+R), or **Tools ▸ Conversations ▸ Simulator…**,
runs the conversation shown as the game does. It follows the conversation machine of
conv/BABL.C and the game's built-in functions (conv/CONVERSE.C, BABLHACK.C, BARTER.C).
Nothing is written to the game's files.
- **Run** (F5) runs until a menu, a question, the end, a stop address or the instruction
  limit. **Step** (F10) runs one instruction. **Reset** starts again.
- The NPC's lines, your choices and the narration (`print`) appear on the left, with the
  @ variables filled in (`@GS8` the player's name, and so on). A menu's answers are buttons;
  click one or press its number. With **Run on after a choice** ticked, the conversation goes
  on to the next menu. When the script asks for typed text (`babl_ask`), a box appears.
- **Stop at:** code addresses (hex, as the Script tab shows them) where Run stops.
  **Follow in the script** selects the current instruction in the Script tab.
- **Variables:** the current values. First the script's memory: the variables the game fills in
  (`npc_attitude`, `play_name`...) and the conversation's private globals. Then the quests,
  game variables, X clocks and skills the conversation uses (or all of them). Type a new
  value to change it; it takes effect from the next instruction.
- **Start-up:** what only the game knows, filled in from the open game:
  - the talker (one of the creatures that use the slot, from its level object: hit points,
    goal, attitude, home and inventory), or default values;
  - with a saved game open, the player from `PLAYER.DAT` (name, sex, skills, quests, game
    variables, X clocks, inventory) and the private globals from `BGLOBALS.DAT`. `DATA` has
    no player, so a new game's zeros are used;
  - how `random` rolls (a seed, or always one result);
  - whether the talker accepts an offer (`do_offer`) and gives in to a demand
    (`do_demand`);
  - the talker's inventory and the player's trade slots, one object a line: the item number,
    `x` and a quantity, `q` and a quality, and `selected` for an item offered.
  **Reset with these** starts again with them.
- **Log:** each instruction (if **Log every instruction** is ticked), each call of a
  built-in with its arguments and result, and each write to memory.
- At the end the status line says what the game would keep: the private globals saved in
  `BGLOBALS.DAT`, and the talker's and player's values copied back (a new attitude, a goal,
  experience).

Built-ins that act on the world outside the conversation (doors, teleports, attitudes of
other creatures, pit fights) are only noted in the log. An object of the level that the
simulator was not given (index 1 is the player) is replaced by a stand-in with every field 0. The simulator runs
the applied code: apply the Script tab's edits first.

### Conversation graph

**Graph…** in the conversation window, or **Tools ▸ Conversations ▸ Graph…**, shows the
conversation's flow as a graph, read from its code. The code is split where it jumps; each
node shows the readable view's lines for its piece of code.
- Brown nodes are where the NPC speaks, blue ones show a menu with its numbered answers,
  grey ones are other code. A node's title is its label (`L02AE`) or its function; it says
  when the conversation ends or the function returns there.
- Blue lines are a menu's answers: the answer's text over its condition (`local22 == 1`).
  Amber lines are other conditions, in the readable view's words. Dashed lines are calls of
  the script's functions. A line going back up (a loop) leaves the top of its node and goes
  up into the bottom of the node it returns to, its arrow pointing up; a loop on one node goes
  round its right side. Calls of a function used from many places (the conversation's end,
  for one) are drawn only for the selected node.
- Code that only passes on, tests a condition or calls a function is folded into the lines
  around it. Functions never called are left out.
- Drag to move the view and use the wheel to zoom; **Start** goes back to the top, and
  **Fit** shows the whole graph. **Statements** shows or hides the other statements in the
  nodes.
- There are two layouts, **In layers** and **Hanging**:
  - **In layers** (the usual) puts every node in a row under the nodes that lead to it. Of
    the many orders tried for each row, the one with the fewest crossing lines is kept. A line
    that passes a row goes straight down between its nodes, so no line is drawn over a node.
  - **Hanging** hangs the graph from its start like a mobile, pinned at the top: each node
    weighs according to its size and each line is a spring, stiffer for plain flow than for
    conditions, answers and calls, and weak for loops. Nodes push each other apart sideways and
    every node hangs below the ones that lead to it. It starts from the layered layout and
    moves until it comes to rest. Drag a node to move it: it stays where you put it, marked with a dot,
    and the rest hangs round it; right-click it, or press **Let go** for all of them, to let it
    hang freely again. Lines are drawn straight. Across both games' conversations it has about
    twice as many line crossings as **In layers** and draws some lines over nodes; use it to
    pull a part of the graph apart by hand.
- A click on a node selects its first instruction in the Script tab and shows the node on
  the right: its strings, its code, and where it leads (click one to go there).
- Editing a string on the right edits that string of the conversation, as the Strings tab
  does: it is marked changed and saved with the conversation. **Revert conversation** undoes
  it.

## Balance

**Tools ▸ Balance** holds three windows that work out what the game does with items, from the
game's own rules (underworld-exhumed: conv/BARTER.C, combat/COMBAT.C, game/PLAYDATA.C), so a
modder can see whether a value, a weapon or a piece of armour is out of line. They read the
object tables as the Object tables window has them, with its unsaved edits, every time they are
brought up. They read the levels as edited; **Read the levels again** counts again after a
change. The player's sliders start from the open saved game's PLAYER.DAT, or at Strength and
Dexterity 20 and every skill 10. **From PLAYER.DAT** and **Defaults** set them again.

### Barter simulator

**Tools ▸ Balance ▸ Barter simulator…** puts a trader and two sides of items together, and says
what the trader makes of the trade.

- **Trader:** every creature of the levels with a conversation of its own (by level, name and
  object number), then every creature class ("Any goblin"). **Use the selected creature** takes
  the creature selected in the main window.
- **Temper:** a trader's greed (how much more than it gives it wants, in per cent), patience and
  judgement come from its creature's haggle, patience and shrewd figures (OBJECTS.DAT creature
  bytes 0x0D-0x0E). Each trader blurs them by a draw seeded with its object number, so a
  creature of the levels always draws the same, and the window shows its exact figures. For a
  creature class it shows the range and uses the middle. The player's **Charm** lowers the
  greed by 2 a point and raises the patience by half a point.
- **Likes and dislikes** are read from the trader's conversation (its `set_likes_dislikes`
  call). A liked item counts 1.5 times; a disliked one is worth nothing and stays with the
  player. Only a few conversations set them: 7 in UW1, 3 in UW2.
- **Items:** choose a kind and an item and add it to either side, or add the object selected
  in the main window with its quality and stack size. Each item has **Quantity** (1-511) and
  **Quality** (0-63) sliders and shows what it is worth to the trader. **Value…** opens Object
  tables on the item's common properties, where its value can be changed; the simulator follows
  when it is brought up again.
- **Result:** the trader values the player's side with its likes and its own side without them.
  It accepts when (player's side − its side) × 100 / its side reaches its greed. The window shows
  that figure, the range its misjudgement can move it over (up to its judgement in per cent,
  the same each time for the same object), and the player's own remark ("…I think that I am
  getting a fair deal"), which **Appraise** only colours: the trader ignores it.
- **Balance trade** adds gold coins (0xA0). When the trader would refuse, it adds the fewest
  coins it accepts to the player's side; when the player gives more, it adds the most coins the
  trader still pays back. **Equal worth** aims at an even trade instead of the trader's greed.
  UW2 counts every coin at quality 63 (about 3 a coin). UW1 uses the coins' own quality: the
  **Coin quality** slider starts at the quality the levels' coins have most (40, about 1.9 a
  coin).

The game multiplies value × quantity in 16 bits, and UW1 × quality too: a stack past 32767
(in UW1, more than about 270 coins of quality 40) is valued wrongly, even below 0. The window
says so under the result. The simulator does not play the demand (a threat), which weighs the
player's level and health against the trader's.

Changing an item's value is already possible in **Object tables** (the Common properties
table, Value), and the **Value…** button goes there. A separate sheet with one item's every
property from all the tables would be possible: every field is described in one place.

### Balance weapons

**Tools ▸ Balance ▸ Weapons…** lists every melee weapon (Melee tab) and every launcher with its
ammunition (Launchers tab). For each it shows how many the levels hold: plain, with an accuracy
or damage enchantment, special (UW2's special weapons) and other (an enchantment that does
nothing in a fight). Under each weapon's own line, an indented line for each enchantment found
gives that copy's figures. Click a header to sort by it.

- **Melee:** the expected damage of a slash, a bash and a stab, misses and critical hits
  counted, and per second when the player swings over and over, releasing at full charge. To hit
  is Attack / 2 + the weapon's skill + Dexterity / 7 (+7 on Easy) against the target's
  defence; a roll 0-30 above 15 hits, above 28 is critical and doubles the damage half of the
  time. Damage is the swing's base + Strength / 9 (bare hands: Unarmed × 2 / 5 + Strength /
  6 + 4), rolled as (d / 6)d6 + 1d(d % 6), × Max charge / 128, less the target's armour.
  Quality changes nothing.
- **Timing:** the game's clock runs at 256 ticks a second and the weapon's picture moves on
  every 32 ticks. The charge grows by the weapon's Speed every 16 ticks up to 100. A swing takes
  the wind-up (4 steps; in UW2 2 for a stab or bare hands), the charge rounded up to a step,
  and 5 steps more. UW1's longsword takes about 1.56 s.
- **Launchers:** only the ammunition's damage counts, × (Missile × 8 + 192) / 256, less
  128/256 on a bad roll and plus 192/256 on a good one. Charge, Strength and enchantments do
  nothing, and there is no critical hit; a shot that reaches the target always hits. A shot takes
  about 0.625 s plus the **Aim** time. The ammunition column counts the stacks' items in the
  levels.
- **Target:** its defence and armour, or a creature's (the four hit zones' average armour).
  **Powerful** counts the armour 5/3 times.

UW2's weapon enchantment names are swapped: the Effect column says what each one does.

### Balance armour and rings

**Tools ▸ Balance ▸ Armour and rings…** lists every wearable item with how many the levels hold
(plain, with Protection or Toughness, other), the hit locations it covers and its armour at the
**Quality** chosen: quality × protection / 64 + 1.

- A creature's blow hits one location: torso, hands, legs or head. Body armour covers the
  torso, gloves the hands, leggings and boots the legs (they add up), a helm the head, and a
  shield (items 0x3B-0x3F) both torso and hands.
- **The blow:** its to-hit and damage, or a creature's strongest attack (to-hit + equip / 2,
  damage + Strength / 5). It needs to-hit + a roll 0-30 − the player's Defence (Defense + the
  weapon skill / 2) above 15. The Blow column is its expected damage at the item's location with
  the item worn; Saved compares it with nothing worn there.
- **Enchantments:** Protection takes its level from the attacker's to-hit at the item's
  locations (melee only); Toughness adds its level to the armour. The to-hit protection effect
  (class 3, minor 1) takes 3 from the attacker's to-hit everywhere, adding up. The names say
  what each does in both games: unlike UW2's weapon names, Protection and Toughness are not
  swapped. Rings do nothing without an enchantment.
- **Intended and actual.** Two slips in the game's code (both games, the same in Underworld
  Exhumed as shipped) make it give something other than what it means to. **Armour** and
  **Blow** are what the game does; **Intended** and **Int. blow** show what was meant, on the
  rows where the two differ: what a game with the slips fixed gives.
  - Resist Blows, Thick Skin and Iron Flesh, cast or on an item, are meant to add 2, 3 and 5
    armour everywhere (the best one counts), and add nothing: `parse_spells` reads their level
    after its stealth loop has shifted it away.
  - Toughness on a shield or ring is meant to add its level on the torso and on the hands; the
    game adds it twice on the torso and never on the hands (`player_affected_by` writes to the
    first location in its loop over both). The row shows the torso.
- **Wear:** a critical hit takes 2d4 >> quality class off the quality of the helm, the leggings
  (the boots one time in five) or the shield; class 3 never wears.

## Global variables

**Tools ▸ Global variables…** (Ctrl+Shift+G) lists the game state that conversations can
read and change, with the conversations that read and write each item:
- quest flags and quest bytes (`get_quest`, `set_quest`);
- the game variables traps share (`x_traps`; UW2's numbered variables);
- UW2's X clocks (`x_clock`);
- skills (`x_skills`);
- the talker's and player's variables copied in and out (`npc_attitude`, `play_hp`…);
- each conversation's own globals, kept in `BGLOBALS.DAT`.
The uses come from the constant arguments of those calls. With a saved game open, the
values come from its `PLAYER.DAT` and `BGLOBALS.DAT`; they can be changed and saved, and
the old files are kept as `.BKP`. With `DATA` open (a new game), they read 0. Double-click a
use to open its conversation at that line.

## Schedules

**Tools ▸ Schedules…** (Underworld II only) edits `SCD.ARK`, the events that move NPCs about
by the time of day and carry the plot along. With a saved game open it edits that save's
own copy; otherwise it edits `DATA`'s, which a new game copies into `SAVE0`. Ultima Underworld has
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
- Changing a row's time moves the row to its place; rows with the same time keep their
  order (▲ ▼ change it). A test row takes its disabled rows along.
- **Add row** puts an empty row (event 0, any level) after the selected one, with its time;
  **Copy row** adds a copy of it. **Delete row** removes it.
- **Save SCD.ARK** writes the changed blocks (`SCD.ARK.BKP` keeps the old file). Each level's
  place in a schedule moves with the rows, so rows already run are not run again.
  **Revert** goes back to the file on disk. The window asks about unsaved changes when it
  closes.

## Player and inventory

**Tools ▸ Player and inventory…** (Ctrl+Shift+P), with a saved game open. It edits the
saved character in `PLAYER.DAT`:
- name, class, sex, hand and portrait (the HEADS.GR picture shown);
- attributes, vitality, mana, hunger, fatigue, carrying capacity;
- level, experience, skill points and the twenty skills;
- UW1's story bits, which are not quest flags (**Story**): **Garamon buried** and **Talismans
  can be destroyed in the lava** (byte 0x62 bits 3 and 2), and the talismans left to destroy
  (byte 0x6D: 8, then 255 once the moongate has opened). Using Garamon's bones on his grave
  sets both bits before his ghost speaks (`obj/USEITEMS.C` `UseBonesOn`), so his questions do
  not matter; killing the ghost clears the second again. Without that bit, talismans thrown
  into the Slasher's lava are not destroyed. Quest 5 is not this flag: nothing in the game
  sets it. The section also has **Tybal's orb destroyed**, **Cup of Wonder found** and **Key
  of Truth given** (byte 0x60 bits 5, 7 and 6: level 7's mana and maze, the flute's tune and
  the shrine mantra each give their item only once). It also holds the levels of the
  **moonstone** (where Gate Travel goes; 0 while carried) and the **silver tree** (where a dead
  player comes back; 0 for none), in byte 0x5E. Less tied to the plot are **Armageddon cast**
  (levels arrived on are emptied), the **maze navigation spell** and the **dreams** (word
  0x6E, a bit per dream, spelt out under the box).

**Checks** are listed beside the fields and update as you type:
- **Game detects:** in UW2, strength + dexterity + intelligence above 64 makes the
  end-of-game screen add "AND CHEATED ON THEIR CHARACTER" (game/SKILLS.C `game_stats`).
  UW1 has no such check.
- **Not reachable:** values that character creation and play never give: attributes over 30
  or outside the class's start in SKILLS.DAT, skills over 30, a sixth portrait, and levels
  outside 1–16.
- **Notes:** maxima that disagree with what the game computes (`player_compute`), a level
  the experience has not earned, and the like. *Set the maxima as the game computes them*
  fixes those.

The hand bit means right-handed in UW1 and left-handed in UW2: their creation menus list
the choices the other way round, so the labels come from CHRGEN.DAT. The **Inventory** tab
lists every slot, named on the left, from top to bottom as on the paper doll (shoulders,
head, torso, gloves, hands, rings, legs, feet), then the eight pack slots. Objects show their
icons, container contents hang under their container, and an empty slot says so:
- each object's id, quality, owner, quantity or special and flags can be changed;
- **Move to** puts the selected object in any slot (an object already there takes its old
  slot, or a free pack slot) or inside a container. Dragging in the list does the same:
  drop the object onto a container to put it inside, onto an empty slot to move it there,
  onto an object in a container to put it just before that object, or onto an object in a
  slot to swap the two;
- **Add to** says where new objects go: the first free pack slot, an empty slot or a
  container. Clicking a container or an empty slot in the list picks it, and it stays
  picked while you add, so several objects can go into the same bag one after another.
  **Add…**'s **Add, keep open** adds one object after another from the form;
- **Remove from the inventory** (Del) takes the object out, together with whatever is inside
  it.

Saving renumbers the objects depth first, as the game's `InvSaveNexts` does. Rewriting a
shipped save's inventory unchanged gives the same bytes, except slots 19–27 (an open
bag's view, which the game rebuilds): those are cleared. The old file is kept as
`PLAYER.DAT.BKP`.

## Graphics

**Tools ▸ Graphics…** (Ctrl+Shift+R), with four tabs:
- **Pictures:** every `.GR`, `.TR`, `.BYT` and UW2's `BYT.ARK` in DATA, as thumbnails.
  Each is shown large in a palette from PALS.DAT; UW2's screens start in the palette the
  game shows them with.
  - Underworld keeps every wall texture twice: 64 × 64 in `W64.TR` and 16 × 16 in `W16.TR`.
    Floors are kept in `F32.TR` (32 × 32) and `F16.TR` (16 × 16). The game draws walls and
    floors near you with the big picture and those a few squares away with the 16 × 16 one
    (`3d/GRIDDB.C` `txtwal` and `txtflr`); further still they are flat colours. Opening one of
    these files shows a note: change the same texture number in both files, or distant walls
    or floors show the old picture. Underworld II has only `T64.TR`.
- **Critter animations:** the CRIT art files by the creatures that use them. Their
  animations come from the UW1 page headers or UW2's `CR.AN`. Pick an auxiliary palette;
  play the animation or step through its frames.
- **Cutscenes:** every CUTS cutscene and its LPF animations, with a frame slider, playing
  at the file's frame rate.
- **Fonts:** the six fonts and the accent table (see [Fonts](#fonts) below).

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

Replacing a cutscene frame re-encodes only its own delta, the next frame's delta and the loop
back to the first frame. Saving keeps each old file as `.BKP`.

### Fonts

The **Fonts** tab edits the six `DATA\FONT*.SYS` files. They are the same in both games:

| Font | Where the game uses it |
|---|---|
| `FONT5X6P` | the scroll: messages, conversations, books, the inventory, cutscene subtitles |
| `FONT5X6I` | stats and skills |
| `FONTBIG` | menus and cutscene titles |
| `FONTCHAR` | character creation and skills |
| `FONT4X5P` | map notes and labels (capitals and symbols only) |
| `FONTBUTN` | buttons (the 26 capitals) |

- **The codes:** a grid of codes 0–127. Each cell shows the code, its glyph, its letter (from
  the accent table, else the ASCII character) and its width. Mapped codes have an orange frame.
  Grey cells are codes the DOS game draws as '?' (see below).
- **The glyph editor:** click or drag to draw. The first cell you touch decides whether the
  stroke draws or rubs out; the right button always rubs out. Drag the red **width marker**,
  or click in the strip under the cells, to set how far the text moves on after the
  character. Pixels past the width are shaded: the game still draws them, over the next
  character. The blue line is the baseline. The buttons copy and paste glyphs (between codes
  and between fonts; a glyph from another font is cut or padded), clear, move the pixels a
  step, undo and revert the glyph.
- **Make accented letter:** copies a letter's glyph to the code you are editing and adds a
  mark: grave, acute, circumflex, tilde, diaeresis, cedilla or ring. Each mark is drawn for
  the font's size. It goes over the letter's ink (the cedilla under it), in the tallest
  drawing that fits the free rows. A capital in a small font has no free row above it, so the
  mark replaces its top row. Touch the result up by hand.
- **Preview:** type a line, accented letters included. It is drawn as the game draws it, at
  2–5 × scale, on the scroll's paper colour, with its width in pixels. The game measures a
  line with `string_width`, which counts only the first 54 characters; the preview says so
  when a line is longer.
- **Space width** and **max width** (the header's widest glyph; *Max width = widest* sets it)
  can be changed. Height and bytes a row are fixed: the game lays text out by them.
- **Save fonts** writes every changed font with all 128 glyphs. The first original is kept as
  `.ORIG` and the last version as `.BKP`, as for other files, and **Make a mod** carries the
  fonts. The shipped files hold fewer glyphs (`FONT4X5P` 97, `FONTBUTN` 94, the others 127),
  but the game always reads 128. A code past a file's end reads whatever an earlier font left
  in memory, which is what made lowercase map notes hang the game. A saved font has no such
  codes: a missing glyph is written blank, with width 0.
- A font the game could not load is refused: a glyph is at most **33 bytes** (rows and width
  byte). The game keeps the 128 glyphs in a buffer that ends where the text colours start:
  4,318 bytes in UW1, 4,322 in UW2. `FONTBIG`, the biggest, uses 31.

### The accent table

Translated text can use accented letters (à, é, ç, ü, ñ…) without changing the game's
program. A letter gets one of the game's codes, the font gets a glyph for it at that code,
and the strings editors write the letter as the code.

- The table is kept **per game folder** in the editor's settings, and shown in the Fonts tab.
  Set a letter for the code you are editing (*This code's letter*), or fill the free codes
  from a preset (*Portuguese*, *Spanish*, *French*, *German*, *Italian*, *Common Western*):
  the preset's letters, most used first, take the free codes in order, as far as they go.
- **Which codes are free:** the DOS game draws only codes 20h–7Ah and draws any other code as
  '?' (`gfx/GRLIBI.ASM`, the same in UW2 and in Underworld Exhumed). So a letter needs a
  printable symbol that no string uses:
  - **UW1:** `$ % [ ]`;
  - **UW2:** `$ [ ] ^` and `` ` ``.

  *Also symbols only the debug strings use* adds the ones used only in block 24, which play
  never shows (UW1 `# * /`, UW2 `* =`). *Also codes the DOS game draws as '?'* offers 1–31
  and 7Bh–7Fh, for a game whose program is changed to draw them. Never `|` (ends a string),
  `\` (the scroll's escapes) or `@` (conversations' substitutions), nor tab, line break,
  carriage return, letters or digits.
- With so few codes, give them to the letters the translation uses most, and write capitals
  without accents.
- **More letters need a changed executable.** When the table maps a letter to a code the
  stock game cannot draw, or a preset has letters left over, a large red warning appears at
  the top of the Fonts tab and above the accent table. For those letters to show, the game's
  executable has to be changed. The change is two bytes of the text drawer
  (`gfx/GRLIBI.ASM` at 4B2C, the same in both games):
  - `cmp al,20h` becomes `cmp al,01h`;
  - `cmp al,7Ah` becomes `cmp al,7Fh`.

  The drawer then draws codes 1–127. Bytes of 128 and up stay '?': the compare is signed,
  and the fonts have 128 glyphs. The editor does not make this change yet; it could be added
  later, the way it already changes the start square in the executable. Without the change,
  the DOS game draws those letters as '?'. So do Underworld Exhumed and UltimaHacks: they do
  not run the DOS executable, so the same change would have to be made in their own code.
- In the conversations' word matching (`conv/BABL.C`), a mapped symbol counts as punctuation,
  so it splits words there.
- **Strings** (Tools ▸ Strings, the Keys window, the conversation editor's Strings tab and
  the Dialogue source) show mapped codes as their letters and take typed letters. Saving
  writes the codes. A letter with no code is refused, and so is a mapped symbol typed as
  itself; the message says why. The compiler reports such a letter at its line.
- **Checks** (in the Fonts tab, and after saving strings) list what stops a letter showing:
  - a mapped letter used in text whose font has no glyph at its code, or still the shipped
    symbol. Every string is checked against the scroll font; block 1 also against `FONTBIG`
    and `FONTCHAR`, block 2 against `FONT5X6I` and `FONTCHAR`, cutscenes against `FONTBIG`;
  - a mapped code the DOS game draws as '?';
  - a mapped code in a map note.

### Font files: PNG, BDF and TrueType

The Fonts tab exports the font shown and imports into it.

- **PNG sheet** (export and import): 16 × 8 cells, code 0 at the top left. Each cell is
  black ink on white with one more row under the glyph: its red pixels mark the width. Grey
  lines part the cells. Draw in any paint program. On import, any dark pixel is ink and any
  pixel of the width row that is not white counts. A sheet of the wrong size is refused.
- **BDF** (export and import): the standard text bitmap-font format of X11 and of font
  editors such as FontForge. Each glyph is named `uwNNN` by its code. It is encoded at its
  letter from the accent table (ASCII for 20h–7Eh; other codes as `ENCODING -1 code`), with
  its width as `DWIDTH`. The game's header goes into `UW_` properties, so a font exported and
  imported is the same file byte for byte. A BDF from elsewhere is fitted into the game
  font's height and columns, on its baseline. Each glyph's code comes from its name, its
  `ENCODING -1` code or its character through the accent table.
- **TrueType** (export only): a font to install in Windows, macOS or Linux, to use in a
  translation's documents or a mock-up. Each pixel is a square, with no hinting. Letters are
  at their accent-table characters, ASCII at its own, and other codes in the private use area
  (U+E000 + code). At a size of one pixel per font pixel, without smoothing, it draws exactly
  the game's glyphs. The font is named after the game and the file, such as "UW1 FONT5X6P".

## Pixel editor

**Edit pixels…** in the graphics window (pictures, critter frames, cutscene frames), or
**Edit its picture…** in an object's right-click menu, opens a picture to draw on, enlarged
with a grid, at its own size. The colours offered are the ones the picture can use: the
palette's 256 for 8-bit pictures (portraits, textures, screens), or the 16 or 32 of its
auxiliary palette for 4-bit pictures (most item pictures, critter frames). A dot marks the
colours it uses now, and the transparent colour shows as ∅ over a checkerboard.

**Cycling colours.** The games animate some palette entries by rotating their colours instead
of redrawing pixels: water, lava and fire. A corner of black and white stripes marks those
swatches, and the tooltip names the run of entries the colour rotates through. Whatever is
drawn with such a colour changes colour in the game. During play, both games rotate each run
by one place four times a second (`gfx/COLCYCLE.C` cycle_colors, called every frame from
`ui/INTERACT.C`), in whatever palette is loaded:

| Game | Entries | Direction |
|---|---|---|
| UW1 | 0x30–0x3F, in four runs of four (blues) | each takes the next entry's colour |
| UW1 | 0x10–0x14 and 0x15–0x17 (fire reds and yellows) | each takes the previous entry's colour |
| UW2 | 0xE0–0xEF, in four runs of four (blues) | each takes the next entry's colour |
| UW2 | 0x03–0x07 and 0x08–0x0A (fire reds and yellows) | each takes the previous entry's colour |

UW1's main menu (`OPSCR.BYT`) also rotates 0x40–0x7F while it waits (`ui/MAINMENU.C`). A 4-bit
picture's colour cycles when its auxiliary palette points at one of these entries. Cutscenes
have their own cycles in each animation file; these are not marked. **Animate the cycling
colours** shows the drawing and the preview cycling as they would in the game.

- **Tools:** pencil (B), eraser (E, the transparent colour), colour picker (I, or Alt+click),
  fill (G), line (L), rectangle (R) and filled rectangle (Shift+R).
- **Brush width:** 1 pixel by default, up to 16 ([ and ] change it). The left button draws
  with the first colour, the right button with the second; X swaps them. Click a colour to make
  it the first, right-click it for the second.
- **Zoom** (+ and −) and **Grid**. Under the drawing, a preview shows the picture at its own
  size and at twice that (a large picture at a size that fits). The tool and colour columns
  scroll when the window is too short for them.
- **Undo** and **Redo** (Ctrl+Z, Ctrl+Y), and **Revert to the picture as opened**.
- **Apply** puts the drawing into the picture without saving it yet. The file is saved from
  the graphics window, from the prompt on closing, or with **Apply and save**, which writes
  the file at once, keeping `NAME.EXT.BKP`. Closing with unapplied drawing asks first.

An item with no picture (UW1's nameless slots) gets a blank one to draw on, shaped as the
nearest picture in `OBJECTS.GR`. A critter page must stay under 0x7FFF bytes, and a cutscene
frame is encoded again with the next one; the editor says so when a drawing does not fit. After
`OBJECTS.GR` or a texture file is saved, the 3D view, the object list and the item icons in the
open windows (Item slots, Object tables, Add an object, the inventory, Find, Triggers) show the
new pictures, without opening the game again. A saved wall, floor or door texture also shows in
the tile panel, the brush's slots, Level textures and Game tables. Icons follow saved pictures only: applied but
unsaved drawing shows in the pixel editor and in the Item slots picture.

## Keys and locks

**Tools ▸ Keys and locks…** (Ctrl+Shift+K) lists every lock number with its keys and locks
across all levels: where each lies, whether it is locked, and the keys' description
(STRINGS.PAK block 5, 100 + the number), which can be edited. Double-click a line to go
there.

## Triggers and traps

**Tools ▸ Triggers and traps…** (Ctrl+Shift+T) lists every trigger of the level, where it
lies, the square its chain runs on and each trap in the chain, then the wandering-monster
spawners (create-object traps the game runs by itself; see Trap recipes ▸ Wandering
monsters) with what each makes and how often, and last the other traps no trigger reaches.
Each line explains itself; double-click one to go to it in the map.

## Fixes

**Level ▸ Fixes…** checks what the game itself relies on, on the levels as edited, and
repairs what it can. Choose **This level** or **Every level**; **Check** runs the checks again. The list
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
  under the floor, UW2 animation entries with another tile, locked locks no key opens, and
  overlapping hitboxes (a creature's collision box sharing space with another creature's or
  a blocking object's; see **Collision boxes**). The shipped levels have a few, such as a
  mountainman standing in a door on Ultima Underworld's level 2.

**Fix** repairs the selected finding, **Fix all of this kind** repairs the ones listed, and
**Fix all** repairs everything with a fix in the levels checked (warnings only when **Fix all
includes warnings** is ticked). A link that reaches an object a second time is cut (nothing
is lost: what follows was already reached). Lost objects are freed as the game frees them
(free stack, active list, animation list, and trap links to them cleared), or put back on the
tile of the trap or trigger that names them. The free stacks and the active list are rebuilt,
keeping their good entries in order. Each fix, and each level's Fix all, is one **Edit ▸ Undo** step. Texts,
non-trap links, loops, keyless locks and overlapping hitboxes have no automatic fix. UW1 keeps its animation list
in a block of its own, which is not checked.

## Item cleaner

**Level ▸ Item cleaner…** (also from the Fixes window) removes objects by kind from this
level or every level: debris, bones, junk (broken weapons, blood stains), plants, food,
weapons and armour, treasure, potions, lights, or every item that can be picked up. By
default it removes only what lies on the floor; tick **Also inside containers** or **Also
what creatures carry** to include more. **Tiles from … to** and **Name contains** narrow it down.

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

**Level ▸ Automap and map notes…** (Ctrl+Shift+A) shows the player's map of a level and the
notes written on it, as the game's map screen would. It opens on the level the main window
shows, then keeps its own choice:
- Underworld: the list holds levels 1-9, then pages 10-99 (notes only, see below).
- Underworld II: pick the world, then the level in it (each world lists how many of its 8
  slots hold a level).
- ◀ and ▶ (or Page Up and Page Down) step to the page before or after; Underworld II skips
  empty slots. **The main view's level** jumps back to the level the main window shows.
- A page with no level under it (a notes-only page, an empty slot) takes notes but no marks. The map lives in `LEV.ARK` (UW1 blocks 0x1A + level and 0x23 +
level; UW2 0x9F + level and 0xEF + level). A saved game has its own; a new game starts from
`DATA`'s, which in Underworld II already holds the castle with 14 notes.

- **Mark seen** paints tiles with the byte the game writes when you see one: the tile's
  shape, its floor's water, lava or ice, and (UW1) a door. **Mark unseen** clears them. The
  brush is 1, 3 × 3, 5 × 5 or 9 × 9 tiles; rock is never marked.
- **Mark everything seen** / **Mark nothing seen** do the whole level.
- **Notes**: click to add a note, drag one to move it, select it to change its text (one byte
  a character, at most 49, the game's limit) or delete it. A level holds up to 100 notes.
- **Pages past the last level** (Underworld): the map screen's up arrow goes on past level 9,
  up to page 99 (`ui/AUTOMAP.C`). Those pages show a blank map with their own notes, from
  `LEV.ARK` blocks 0x2D-0x86, which nothing else uses. A new game copies `DATA`'s `LEV.ARK`,
  notes included, so notes put there travel with a mod. The key item scatter can write its
  spoiler log there (pages 9, 10 and on).
- **Show the level under the map** draws the level faintly behind the map, as a guide for
  painting.
- **Undo** steps back in the window; the changes are saved with the levels (Save levels)
  and count as unsaved work.

## Game tables

**Tools ▸ Game tables…** edits the small tables in the game's `DATA` folder. They apply to
the whole game, saved games included, so the `DATA` folder's copy is changed even when a saved
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
  the bonus points spread over them at random, none above 30. Then each entry is taken in
  turn: an empty entry gives nothing, an entry with one skill gives that skill, and an entry
  with several makes a menu to pick one from. An empty
  entry also loses the entries after it (the game does not step past it); **Check** says so.
- **Combinations** (`CMB.DAT`): ten rules, first item + second item → output. Drop one item
  on the other in the inventory (either order) to make the output; a source marked
  *used up* goes away. Neither item may be a stack or hold anything, and the first rule
  naming both is used. In Underworld a rule is ignored unless at least one source is used
  up; its unused rules are zeros. Underworld II uses every rule; its unused ones are 0xFFFF.
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
  and 6 door slots (`DOORS.GR`). The list to edit is chosen as **Wall textures**,
  **Floor/ceiling textures** or **Door textures**: a level has one ceiling texture, the one
  in floor slot 9, and floors set to slot 9 show it too.
- Underworld II: 64 slots of `T64.TR`; the first 16 are also the floor slots (the ceiling is
  slot 15), plus 6 door slots. The list is **Wall, floor and ceiling textures**.

Each slot shows its picture, the texture number, how many open tiles use it, and the
texture's terrain (water, lava...). Choose a slot, then click another texture on the right;
textures already in the list are marked •. A click only marks the texture: the bar above the
textures says what would change ("Wall slot 3: texture 12 → 27. 140 tiles change."). **Use it**
(Enter, or a second click on the same texture, so a double-click works too) puts it in the slot;
**Cancel** (Esc), choosing another slot or clicking the slot's own texture leaves the slot as it
was. A stray click therefore changes nothing.

Each change is one Edit ▸ Undo step, and **Undo** beside the status line takes the last one back
while it is still the next step to undo. The 3D view and the paint brush's slots follow, and
Save levels writes the change with the level (only the level's texture map block changes).

## Strings

**Tools ▸ Strings…** (Ctrl+Shift+S) shows every block of STRINGS.PAK, with the conversations'
blocks named; search one block or all of them. Strings can be edited and added. Saving
updates the names everywhere in the editor. The window shares the conversation editor's copy
of the file, so edits made in both are never written over each other.

- Letters of the [accent table](#the-accent-table) are shown and typed as letters and saved
  as their codes. A letter without a code is refused, with the reason.
- STRINGS.PAK packs all text with one Huffman tree. When a string has a character the tree
  has no code for (an accented letter's code, say), saving builds a new tree from every
  string's characters and writes every block again with it. The game reads whatever tree the
  file has. Characters the game cannot hold are refused: code 0 and codes of 128 and up, `|`
  (it ends a string), and strings over 511 characters. After saving, the
  [checks](#the-accent-table) list letters the fonts cannot show yet.

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

**Tools ▸ Object tables…** shows the fixed properties of each kind of object, from
`DATA\OBJECTS.DAT` and `DATA\COMOBJ.DAT`, with a tab per table:
- **Weapons** (0x00-0x0F): slash, bash and stab damage, swing charge and speed, skill
  (3 sword, 4 axe, 5 mace, 6 unarmed), durability (255 unbreakable). Entry 15 is the bare fist.
- **Ranged** (0x10-0x1F): ammunition and launchers. A launcher names its ammunition
  (item - 0x10); ammunition has the damage done when it hits.
- **Armour** (0x20-0x3F): protection, durability and the category, which decides the
  paper doll slot (0 shield, 1 body, 3 leggings, 4 gloves, 5 boots, 8 hat, 9 ring).
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
  stone), value, the flags (solid, pick up, pickable, can be owned...), toughness and
  quality words, fate when thrown, resistances and how it is drawn.

Two of the common properties sound alike but do different things (both games):
- **Pickable** decides whether the item can be taken from the world: picking it up in the
  view puts it on the cursor. Creatures, scenery, furniture and doors have it clear.
- **Pick up** decides where the held item may go: a pack, shoulder or hand slot, or a bag.
  The armour and ring slots, a weapon in the weapon hand, a light, and the rune bag, quiver,
  map case and bowl have rules of their own. An item that is Pickable but not Pick up can be
  taken from the world and only put down or thrown again.

Every shipped item that is Pickable is also Pick up. A few fixed objects have Pick up set
with no effect (UW1's urn, orb, campfire, fountain and cauldron; UW2's skull 0x13, orb, force
field, campfire and fountain).

**Value** is a worth in trade, not a price in coins. A trader adds up each side of the trade:
value × quantity × quality / 64 (at least 1; 0 at quality 0), blurred by how well it
judges, and 1.5 times as much for an item it likes. It never puts an item of value 0 up for
trade, and such an item is worth nothing to it. A gold coin's value is 3 in both games:
- UW2 counts every coin as quality 63, so a coin is worth about 3;
- UW1 uses the coins' own quality, mostly 40 in its levels, so a coin is worth about 1.9.

So an item of value 30 at full quality is worth about 10 coins in UW2 and about 15 in UW1.
UW2's vending machines charge (value + 1) / 2 + 1 gold pieces. For the coins and gems
(0xA0-0xA7), a higher value also makes them rarer in creature loot.

Each table has one row per item, with its picture and name. Click a cell to edit it; Enter
or Tab keeps the value, Shift+Tab goes back, and Esc drops it. Numbers are decimal, or hexadecimal with `0x`.
Only the cell being edited has a frame; once it closes, only the changed mark is left.
A value outside the field's range is refused, and one the game cannot use (a weapon skill
that is not a weapon skill, an armour category no slot takes) is kept with a warning; both
show in the status line. Changed cells are marked and changed rows have a bar at the left.
Below the grid, the editor explains what the selected field means, where it is in the file,
its range and the value on disk. Click a column heading to read about the field.

**Filter** shows the rows whose name or number matches; *Changed rows only* shows what you
edited. **Revert row** puts the selected row back as it is in the file; **Revert all** drops
every change. **Save** writes the changed files (old copies kept as `.BKP` and `.ORIG`).
The game reads them at start-up. A saved game has no copy of its own: the files in DATA
apply to every saved game.

## Item slots

**Tools ▸ Item slots…** (or **Item slot…** in an object's right-click menu) lists every item
id (weapons, missiles, armour, and the containers, lights, treasure, food, scenery and special
items) with what uses it, so that you can turn a slot into a new item. The game has 512 ids and decides
how an object acts by its class (a weapon, food, a light…), so a new item is an existing id
given a new name, picture and stats.

For each id it lists:
- objects with that id in every level, as edited: on the floor, in containers, carried by
  creatures, or made by create-object traps;
- **conversations** that create, take, look for or delete it (`do_inv_create`,
  `do_inv_delete`, `take_from_npc`, `take_id_from_npc`, `find_inv`, `find_barter`,
  `find_barter_total`), with the line; a call that names the id's whole class (1000 + the
  class) is marked as such. Caliburn, for example, is made by Shak's conversation from the blade and the hilt;
- the **game's own code**, when a source file of the game names the id (its use, its look, a
  combination, combat…): that behaviour stays with the id;
- creature loot and arms in OBJECTS.DAT, and item combinations (CMB.DAT);
- UW1's quest items by name.

**Free slots only** shows the ids nothing uses. A free slot is safe to make into a new item of
its class. A slot used only by objects can be freed by moving or deleting them. A slot that
the code or a conversation uses keeps that behaviour, whatever its name. For the selected id:
- **Name:** article, name and plural, written to STRINGS.PAK block 4 as `a_name&plural`
  (**Save STRINGS.PAK**, or with the other string edits);
- **Edit the picture…** opens the pixel editor on its `OBJECTS.GR` picture;
- **Edit its stats** and **Edit its common properties** open the object tables on its row
  (damage, protection, nourishment, brightness…; mass, value, size, flags);
- **Place one on the selected tile** adds one to the level.

Two things are not checked: text that only mentions an item in a conversation's words, and
UW2's `babl_hack` calls.

## Sound and music

**Tools ▸ Sound and music…** plays, exports and replaces the games' digitised sound and edits
the effects list. The files are in the game folder's `SOUND` folder; a saved game uses the
same ones. Nothing is written until **Save**; each file written keeps `NAME.EXT.BKP`, and
the first version `NAME.EXT.ORIG`. Closing the window with unsaved files asks first.

**Play** plays a file as it is now, including unsaved imports; **Stop** stops it. Sound goes out
through OpenAL. Without a sound device the status line says so and nothing else happens.
**Export WAV…** writes the sound as an 8-bit mono WAV file at the file's rate. **Import
WAV…** replaces it. **Revert to the original** goes back to the file as shipped (its `.ORIG`
copy), or removes a file the editor added; an unsaved import is simply dropped.

**Importing.** Any WAV file can be read: 8, 16, 24 or 32-bit PCM or 32-bit float, any rate,
mono or stereo. It is mixed to mono, its silent start and end are cut (**Trim silence**),
it is resampled (a windowed-sinc filter), raised to full volume (**Normalise**) and
dithered down to 8 bits. It is written the way the game reads it: one plain block of 8-bit
samples. The game skips the file's first 32 bytes and plays the rest as sound, so any other
shape would click. **Import at** sets the rate: the game's own (12048 Hz in Underworld,
11111 Hz in Underworld II), or 8000 to 22050 Hz. Lower rates fit longer clips. A clip over
the file's limit is refused or, depending on **Over the limit**, shortened or written at a
lower rate so that it fits; the status line gives the length in seconds.

The limits, from the games' code:

| File | Limit |
|---|---|
| Underworld `nn.VOC` (speech) | 98,304 bytes (8.2 s at 12048 Hz); the rest is never played |
| Underworld II `BSPnn.VOC` (speech) | none: it is streamed |
| Underworld II `SPnn.VOC`, `UWnn.VOC` (effects, trap sounds) | under 65,536 bytes (5.9 s at 11111 Hz) |

Underworld II's digitised effects share a cache of four 16 KB pages: a big one pushes the
others out, so keep them short.

- **Speech.** Lists each speech file with the cutscene and the subtitle line it goes with
  (STRINGS block 0xC00 + the cutscene), its length, rate and size against the limit. In
  Underworld all 42 files belong to the introduction (cutscene 0), one subtitle line each. In Underworld II
  the eight `BSPnn.VOC` are spoken in cutscenes 4–7 and 24–27; the introduction has no
  speech. The cutscene's say command starts the file and the next one cuts it, so a new line
  should take about as long as the old one. Conversations have no speech in either game.
  **Export all…** writes every file into a folder.
- **Effects** (`SOUNDS.DAT`): every effect, with what plays it in the game, taken from the
  games' code, and the creatures whose footsteps or death cry it is. Select one to change
  its patch (a timbre of bank 1 in `UW.AD` and `UW.MT`), note, volume and length (in 1/256
  seconds). Underworld has 24 effects and Underworld II 49; the games hold no more.
  Underworld's effects are all notes played on the music card (FM or MT-32), which the
  editor cannot play. **Revert SOUNDS.DAT** goes back to the file as saved.
  - Underworld II: an effect with **Digital** ticked plays `SOUND\SPnn.VOC` (nn its number)
    on the Sound Blaster; the others are music-card notes. Ticking **Digital** on any effect
    makes it digitised: import its `SPnn.VOC` (until there is one, the game plays the note).
    **Priority** decides which sound keeps the digital channel when two play at once.
  - Underworld II trap sounds: effects 100 to 163 are `UW00.VOC` to `UW63.VOC`. A
    special-effects trap (0x184) with quality 2 and owner nn plays `UWnn.VOC` when it is set
    off. The list shows the existing ones, and the traps in the levels that play each (the
    game ships `UW00` and `UW01`, each played by traps in one level). **Add a trap sound
    from WAV…** makes a new `UWnn.VOC`; then put a trap with that owner in a level. New voice
    lines can be added this way, as data alone.

Make a mod (**Tools ▸ Mods**) carries the changed files in `SOUND` along with the rest, plus
the new files the game opens by number: UW1 speech `nn.VOC`, UW2 `SPnn.VOC`, `UWnn.VOC` and
`BSPnn.VOC`. Any other new file prevents a mod from being made (see [Mods](#mods)).

### Music

The **Music** tab lists the game's themes. Each theme has two files in `SOUND`: one for the
FM cards (AdLib, Sound Blaster; UW1 `AWnn.XMI`, UW2 `UWAnn.XMI`) and one for the Roland
MT-32 (UW1 `UWnn.XMI`, UW2 `UWRnn.XMI`). The two digits are the theme number in octal: theme 8
is `xx10.XMI`. The list shows each theme's number, when the game plays it, both files' sizes,
how many timbres each needs, and the result of the checks below. Select a theme to see the
full report: when it plays and what follows it, the checks of both files and the timbres they
use.

The editor does not play music. To listen to a theme, export it and open the MIDI file in a
player. A MIDI player uses General MIDI instruments, not the game's timbres, so it sounds
different from the game; the MT-32 file plays with the wrong instruments on a General MIDI
device, because MT-32 program numbers name other instruments.

- **Export FM MIDI…** and **Export MT-32 MIDI…** save the selected theme as a standard MIDI
  file (format 0). The timing is exact: the game's music runs at 120 ticks a second, and so
  does the file (60 ticks a beat at 120 beats a minute). The game's own controllers are kept;
  its loops (controllers 116 and 117) also get `loopStart` and `loopEnd` markers, which other
  programs read. The file carries the theme's timbre list in an event other programs ignore,
  so exporting and importing a theme again gives the same file.
- **Export all…** saves both files of every theme into a folder (`AW01.mid`, `UW01.mid`...).
- **Import MIDI…** makes the selected theme from a MIDI file (format 0 or 1). **Import makes**
  picks which files: both (the usual case: the game needs both), FM only or MT-32 only.
  **MT-32 programs**: a MIDI file made for General MIDI goes through the editor's General MIDI
  → MT-32 map (each instrument to the MT-32 preset with the closest name); **Keep the program
  numbers** is for a file made for the MT-32; or give **a second MIDI file** for the MT-32
  version.
  - Note-ons and note-offs become notes with durations; tempo changes are followed.
  - Drums are on channel 10. The FM file can only use the drum keys `UW.AD` has (24 in UW1,
    26 in UW2). **Replace drums UW.AD lacks** plays the nearest drum it has instead (the
    report lists them); when it is unticked, such a theme is refused.
  - The game plays channels 2 to 10 only. **Move channels 1 and 11–16 to free ones** moves
    them to unused channels from 2 to 9.
  - Loops: `loopStart`/`loopEnd` markers (a number after `loopStart` plays the loop that many times)
    become the game's FOR/NEXT loops. **Controller 111 starts a loop** reads RPG Maker's loop
    mark. **Loop the whole theme** loops everything endlessly. Most themes need no loop: the
    game restarts or replaces a theme when it ends.
  - Bank selects for General MIDI (controllers 0 and 32) are dropped; controller 114 selects
    the game's timbre bank. Track names and other text are left out to save room.

  The report shows what the conversion changed and the checks. A theme with an error is
  refused and nothing changes. Otherwise the theme is marked changed; nothing is written until
  you save.
- **Revert to the original** puts back both files of the selected theme as the game shipped
  them (from the `.ORIG` copies the editor keeps), when you save.

The checks follow the game and its sound drivers. Errors:

- **Too big.** UW1 reads a theme into 6,300 bytes, UW2 into 9,000. The game does not check the
  length: a bigger file overwrites memory.
- **A missing timbre.** Every timbre a theme lists must be in the timbre library: `UW.AD` for
  the FM file (UW2 also `UW.OPL`, the same file), `UW.MT` for the MT-32 file. If one is
  missing, the game turns the music off for the rest of the session. The FM file can use the
  128 programs of bank 0 and the drum keys of bank 127. The MT-32 file uses the MT-32's own
  128 presets (bank 0), as all the shipped themes do; `UW.MT` holds the effects' timbres.
- **More than 32 notes at once.** The game's sequencer holds 32 sounding notes; the next one
  takes the first one's place and that note never stops.
- **Loops nested more than 4 deep**, where a loop's end then jumps back to the wrong place.
- **An endless loop in a theme the game waits for** (the victory theme; UW1 theme 11), and a
  missing end.

Warnings and notes: notes on channels the game does not play; programs or drum keys missing
from the timbre list; MT-32 drum keys outside 35–76. The sound effects borrow channels from
the music: up to 4 in UW1 and 3 in UW2, each time taking whichever of channels 2–9 has the
fewest notes sounding (the highest on a tie). While an effect plays, the music's notes on its channel are
dropped. A theme that uses at most 4 (UW1) or 5 (UW2) of channels 2–9 leaves them free. FM
cards have 9 voices (18 on an OPL3), shared with the effects.

UW1 theme 11 is never played: no call in the game asks for it.

## Checkpoints

**File ▸ Checkpoints…** While there are unsaved changes, the editor keeps a copy of them
every few minutes (5 by default; 0 turns it off). Each copy holds the changed levels and
automaps, and STRINGS.PAK or PLAYER.DAT when they have unsaved edits. Only the newest copies
are kept: 20 by default, up to 1000. **Checkpoint now** takes one at once. **Restore** (or a
double click) loads one back as unsaved edits: each level becomes one undo step, and nothing
in the game folder changes until you save. Restoring the strings or the player cannot be
undone, so the editor asks first when they have unsaved edits of their own. The copies are
kept in the editor's settings folder, one folder per opened game. Unsaved conversation
scripts are not included; their strings are.

### When the editor crashes

An error the editor does not expect is written to a crash log (`crashes` in the settings
folder; the newest 20 are kept), and the unsaved work is saved in a checkpoint at once. Most
errors affect only one command: the editor says so and carries on, and **Open the log
folder** shows the log to attach to a bug report. If anything looks wrong after that, restore
the checkpoint or save to a copy first.

If the editor stops without closing normally (a crash, being killed, the computer turned off)
while work was unsaved, it says so the next time it starts and offers to open that game and
restore the newest checkpoint as unsaved edits. Work done after the last checkpoint is lost,
so at most you lose one checkpoint interval's work. A running editor keeps a small file in the
settings folder (`running-PID.json`) for this; a clean exit removes it.

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

### Check and repair a saved game

**File ▸ Check and repair the saved game…** looks for problems in a saved game that make the
original game crash or complain, and repairs them as unsaved edits (look them over, then Save; nothing
else in the save changes):
- **Map notes read from another block.** UW1's `LEV.ARK` has no table of lengths: a block runs
  to the next one. When every note of a level is erased, the game writes that level's notes
  as a block of length 0 at the end of the file, and the next block it appends (a new level's
  automap) starts at the same place. The notes then read as that automap: about 75 "notes"
  with no end, and opening the map can crash the game, which copies each note into 50 bytes
  (Underworld Exhumed's issue 7; its port guards the copy since 1.0.2). The repair drops the
  notes block; the other level's automap is kept as it is.
- **Notes with no end** within 50 bytes, notes blocks that are not whole notes or hold more
  than 100 (the game reads them into room for 100), and notes off the map screen. The repair
  keeps the whole notes that end and lie on the screen.
- **Automap blocks** whose length is not 4096 (the game copies the block before it checks
  the length).
- **Objects lost from the object lists**: in UW1, asking for part of a stack that then proves
  too heavy to pick up takes an object off the floor and never frees it, and the game says
  "Problems in object list" on entering the level (Underworld Exhumed's issue 9; its
  `tidy-objects` enhancement puts them back). The repair frees them, with the other list
  repairs of **Level ▸ Fixes**.

**File ▸ Export as a saved game…** writes the unsaved levels (and player) as a saved game. It
starts from the open save's files or, with `DATA` open, the folder's first save carrying
`DATA`'s levels, and writes `LEV.ARK` as edited, with the description "Editor" and the time. Put it in one of
Underworld Exhumed's slots (its home folder, `%APPDATA%\uw1port` on Windows, `~/.uw1port`
elsewhere; `uw2port` for UW2), where Journey Onward or Ctrl+R finds it, or in a folder of
your choice, such as a `SAVEn` folder of the original game for DOSBox. A slot already there is
replaced; a folder holding other files is refused.

## Colour scheme

**View ▸ Colour scheme** switches between light colours (the default) and dark ones for every
window. The choice is remembered between runs. The 3D view and the pictures keep their own
colours.

**View ▸ Layout** chooses how much room the controls take, with either colour scheme.
**Compact (classic, with borders)**, the default, is closer to Windows XP's dialogs. Buttons,
boxes, menus, tabs and list rows are shorter, the white space between things is halved, and
borders take its place to guide the eye:
framed side panels, lists, trees and tab pages, buttons with a raised face and an edge,
classic tabs joined to their page, and section headings drawn as header bars. Font sizes do
not change. **Comfortable** is the spacious modern look. The layout applies to every window at
once, open or opened later, and is remembered between runs.

The mod manager (`ModManager/`) has the same look and the same two choices, under its
**Settings ▸ Appearance**, which also start as Light and Compact.

## What enchantments do

- **A weapon's blow** looks only at a weapon enchantment, major class 12 (COMBAT.C
  `DoPlayerWeapon`):
  - In UW1, effects 0–7 add (effect & 7) + 1 to hit, and effects 8–15 add it to damage.
  - In UW2, effects 0–3 add 2·effect + 1 damage, and effects 4–7 add 2·effect − 7 to hit
    (the names say the opposite). Effects 8–15 are the special weapons. Their power fires on
    a critical hit, or, for the sixth of them, on every hit: it heals the player, repels the undead,
    casts a fireball, holds the target, or opens a door.
- **Anything else on a weapon does nothing** while it is wielded. Only armour slots, rings
  and a shield in the off hand count as worn (INVDATA.C `ObjWorn`), and only enchantments
  without flag 11 apply while worn (PLAYDATA.C).
- **A spell (flag 11) on any object** is cast when the object is used (double-clicked), at
  most every 0x2FD ticks (USEITEMS.C `checkSpell`). An enchanted object never runs out; only
  a wand's spell object has charges.

## Randomizer

All of it is in the **Randomizer** menu:
- **Key item scatter ▸ Zones** (UW1 only) holds the zoning. Its **Zone graph…** and **Autozone…**
  buttons open the zone graph and the **Automatic zones** the logic is built from.
- **Key item scatter ▸ Apply** is the randomizer that moves the key items so the game stays
  beatable.
- **Item property shuffle**.
- **Swap ▸ Swap items** and **Swap ▸ Swap monsters**.
- **Named challenges**.
- **Randomize mantras (UW1)**.
- **Vowel play**.

The logic behind them is explained in **How the randomizer's logic works** below.

**The Pac-Man maze's glowing rocks are never touched.** In Ultima Underworld, every glowing rock
on level 5 is left alone by every randomizer, whatever the settings. That is the 83 rocks of the
maze in the level's north-west corner, plus the one a creature on that level carries. They are
never moved, swapped, rerolled, removed, counted or used as a spot for something else, and they
are not in the lists of objects to ignore. **Place anywhere** also never puts an item in the maze,
which runs from (2, 2) to (21, 22).

**Randomizer ▸ Key item scatter ▸ Zones…** opens the zoning of the game. A zone is a part of the world
that can be walked without meeting a requirement. Every tile has a zone id: 0 is rock, 1 is
open but not in a zone yet (magenta in the view), and 2–255 are the zones. The file is
`uw1-zones.json` (or `uw2-zones.json`) in `DATA`, the same format the Unity editor uses.
**Load the UW1 example** starts from Karl's UW1 zoning, built from his painted maps and
notes: 91 zones, 139 links and 50 grants.

- **Current zone:** the zone being painted and listed, with **Add zone**, **Rename**,
  **Remove zone** (its tiles become unassigned; its links and grants go) and a colour.
  Colours default to a Glasbey palette: each colour is as far as possible, in CIELAB, from all the ones before it.
- **Mark as start** and **Mark as goal** use the square selected in the view. The line under
  them says where the game starts and ends now, and the zone list marks those zones
  ("start", "ends the game").
  - **Mark as start** moves the new-game start there, as the editor's start tool does. A new
    game always starts on level 1, so only a level 1 square will do. The square is saved in
    `UW.EXE` at once, the original kept as `UW.EXE.ORIG`, and the zoning's start zone follows.
  - **Mark as goal** puts a square that ends the game there: a move trigger running a hack
    trap of quality 0x3F, like level 9's at (52, 51), which stays. That trap sets the game's
    end mode, and the ending then plays on any level (event/TRIGGER.C `do_trap_hack`,
    game/SKILLS.C `check_victory`). It is one undo step (Edit ▸ Undo) and is saved with the
    level. Ultima Underworld 1 only.
- **Painting:** with **Paint in the view** on, the Paint tool paints the current zone. Drag
  over tiles, or tick *A click fills the area* to fill from the tile under the mouse.
  **Fill the area under the selected tile as zone N** does the same from the selected tile.
  A fill moves left, right, up and down, only through tiles of the start tile's zone. It is
  stopped by walls, by the wall side of diagonal tiles, and by tiles holding a door of any
  kind, open or closed (portcullises and secret doors too); those tiles are not filled. It is
  also stopped by going up more than the **highest climb** (2 floor steps by default; a walk
  climbs 1), measured between the touching edges of the two tiles, so slopes count. Going down
  is never limited: a fill from the top of a drop reaches the bottom, but not the other way
  round.
  Rock is never painted, and **Undo painting** steps back through the strokes. While the
  window is open, the view shows each tile's zone colour on its floor.
- **Key items** of the current zone are listed on the right. The list holds keys (by the lock
  they open), the talismans, quest and unique items, runestones, creatures that talk, and the
  creatures whose death the game checks. Plants, bridges and furniture are left out. Any
  object can be marked as a key item, or left out, by hand; the zone file remembers it. **Add
  them to the zone's grants** turns them into what the zone gives (`key:5`, `talisman:cup`,
  `npc:corby`…).
- **Columns:** the three columns (the current zone, its key items, its links) can be made
  wider or narrower by dragging the gaps between them.
- **Links of zone N** (the right-hand column) lists the ways from and to the current zone,
  with what each needs.
  - **New link to** a zone, one-way or two-way, takes its requirement from the doors between
    the two zones when there are any.
  - Click a link to edit it below the list: its zones and direction, kind, sureness, the
    requirements (all must hold; `|` within a line for "or"), **From the doors**, notes, a
    go-to for its square, and **Delete the link**.
  - The zone graph shows the links as they change, so the two windows can sit side by side.
- **Check if solvable…** runs the solve over time on this zoning, as Apply uses it (unsaved
  changes and the split option included).
  - The options are the Apply window's when it is open, else the Automatic zones window's,
    else the defaults.
  - The line beside the button says whether the goal is reached and by what, and how many of
    the main option sets reach it. The report opens in a window of its own.
  - The solve runs in the background, on a copy of the levels taken when it starts, so the
    editor stays responsive meanwhile (as it does during the zone graph's Check the logic and
    the Automatic zones window's builds).
  - The start is the one the solver uses: the new-game square in `UW.EXE`, or the game's own
    with **Consider the current starting square** off in the open Apply or Automatic zones window.
- **Split multi-level zones by level for the solver** (remembered): Apply and Check the logic
  treat each zone painted on several levels as one zone a level, joined by free two-way links.
  The line under it lists the zones on several levels. See **How the randomizer's logic
  works**, Zones from a zoning.
- **Autozone…** opens the Automatic zones window, whose **Build and Use** puts automatic zones
  here.

Apply and the zone graph's **Check the logic** use this zoning, unsaved changes included.

The Zones window's **Zone graph…** button opens a view of the zoning. It follows the Zones
window's edits as you make them, so the two windows can sit side by side; links are made
and edited in the Zones window, not here.

Each zone has a label, placed in a column for the level it first appears on. Drag the labels
to arrange them; the positions are saved in the zone file. Each label's inside is the zone's
colour in the zoning, and its outline the colour of the level it is on (its first level, for a
zone on several); the row under the buttons is the key.
- **The start**, the zone of the new-game square in `UW.EXE`, has a thick outline round it.
- **The goal** zones have a thick dashed one: level 9's arrival square, and the squares that
  end the game.

Lines are the links between zones:
- one arrowhead means one-way, two mean two-way;
- dashed means still to check;
- the colour is the kind (door, portcullis, secret, drop, lever and so on).

- **Click** a label or a line to see the zone or the link on the right. **Edit in the Zones
  window** opens it there.
- **Check the logic…** runs the solve over time on the zoning shown (with its unsaved
  changes), as **Apply** uses it, on the levels with their unsaved edits.
  - The zones are those of the zoning, laid on the levels' regions (see **How the
    randomizer's logic works**, Zones from a zoning).
  - Mechanisms are set off and applied when reached, and create traps and counts are
    followed.
  - The graph then shows what the solve reached. Those zones keep their colour, with the
    step they were reached in, in brackets. The others fade. A line from a zone reached to
    one not reached turns red, with what it needs written on it.
  - The report opens in a window of its own. It shows the steps, the changes, the goal, and
    the zoning's warnings: zones merged, zones made for areas the zoning leaves out, and
    zones whose parts have no free way between them.
  - The options are the Apply window's when it is open, else the Automatic zones window's,
    else the defaults. A zoning with nothing painted yet is solved on automatic zones.
- **Reset graph appearance** takes away what Check the logic showed.
- **Panning and zooming:** drag with the middle mouse button, over the labels and lines too,
  to move the view. The mouse wheel zooms in and out about the pointer, from 0.3 to 2, and
  **Fit** shows every label. The scroll bars still scroll.
- **Force layout** lays the labels out live by forces: every link pulls the two zones it joins
  together, every pair of zones pushes apart, and a weak pull keeps everything near the
  middle, until it settles. **Attraction** and **Repulsion** set how hard the links pull and
  the zones push (1 is the usual; change them while it runs). Drag a label meanwhile to hold
  it in place. Press **Force layout** again to stop; the new places are kept in the zoning
  (save it to keep them). **Arrange by level** puts them back in columns.

### How the randomizer's logic works

This section explains, in one place, how the editor decides what the player can reach and
how **Key item scatter ▸ Apply** uses that to move items without making the game unbeatable. It
covers Ultima Underworld 1, the only game whose facts have been written so far. The windows are described in
**Automatic zones** and **Apply** below. The findings behind the rules, with their
sources in the game's code, are in `docs/autozones-uw1.md` and `docs/facts/uw1-quests.md`.

The rules are glitchless: no trick, clip or sequence break counts, only plain play.

#### 1. From tiles to regions to zones

The editor reads every level and groups its open tiles into **regions**: areas in which the
player can walk anywhere and back. Two touching tiles are in one region when the step
between them, measured at their touching edges (so slopes count), is at most the **highest
climb** (2 floor steps by default) both ways. Ceilings are ignored: no area of Ultima
Underworld is cut off by a low ceiling. A tile holding a door is a region of its own, so the
door can be a requirement. Water joins regions (the player swims), but lava tiles only join
other lava. Tiles with the no-magic bit are marked.

Between regions the editor makes **edges**, each one way or both ways, each with a
requirement:

- **drop**: going down more than the highest climb, one way. Its fall height is recorded; no
  drop in the shipped levels kills a new character (the worst does about 8 damage).
- **climb**: the way back up a drop. Up to twice the highest climb (4 steps) it needs the
  jump ability; above that, levitation.
- **jump**: two surfaces with 1 or 2 tiles of lower ground between them (a pit, water, lava)
  in a straight line, when the far side is no higher than the near side plus the highest
  climb. 3 tiles need the jump ability. Diagonal jumps are an option (off by default).
- **door**: through a door tile. A locked door needs its key (`key:N`), picking (`pick:Z`,
  the lock's difficulty) or bashing (always allowed: fists break any door that can be
  broken). An unlocked door is free. A door that only a lever or a trap opens needs that
  lever.
- **stairs** and **teleport**: every trigger the player sets off whose chain reaches a
  teleport trap makes a one-way edge to where it lands. Stairs are the ones that change
  level.
- **lever**: a door a lever opens needs the zone the lever is in (`zone:N`). A lever out of
  the hand's reach (from the floor in front of it, by the game's own reach test) also needs
  the pole, Telekinesis or levitation. A lever is used only from its own side of a closed
  door, and a lever set in rock only once the rock is opened.
- walking into **lava** needs the lava ability.

Regions that reach each other freely, both ways, are merged into **zones**, the unit the
solver and the shuffle work with. **Cut at level changes** keeps each level's zones apart
even where free stairs join them; **Fold empty dead ends** merges pits and ledges that hold
nothing into their only neighbour. Each zone gets the items lying in it as **placements**:
what holding them grants.

**Zones from a zoning.** When the Zones window holds a zoning (painted by hand, or made by
the automatic zones and changed there), the solver uses its zones instead of making them:

- **Regions:** each region takes the zone that holds most of its tiles. A region is the
  finest part there is, since the player walks all of it and back, so two zones painted on
  one region become one. The report says which.
- **Door squares** (which a fill leaves out) take the zone of the area around them.
- **Open areas the zoning leaves out** get zones of their own.
- **Doorways closed behind the player** keep a zone of their own, so the door stays one way.
- **Links:** the links between zones still come from the levels (doors, drops, climbs,
  levers…). The zoning's own links are added where the levels do not give them; they hold
  in every state of the world.
- **After a change** to a level, its rebuilt regions take the zones the same way, so a
  painted zone stays one zone. An automatic zoning is regrouped as the automatic zones are.

A zone counts as reached all at once. A painted zone whose parts have no free way between
them (a locked door, a climb or a jump inside it) therefore makes the solver too kind: it
counts the whole zone reached as soon as any part is. The report lists such zones with
their number of parts. Splitting them in the Zones window makes the logic strict again.
The same holds after a change in the world: a change that cuts a painted zone in two (a
portcullis closing inside it, say) leaves both parts in that zone, so what lies behind the
closed part still counts as reachable. (Automatic zones give the cut-off part a zone of its
own.) Whether a painted zoning should be split that way too is still open.

**Split multi-level zones by level** (an option in the Zones window, remembered) gives the
solver a copy of the zoning in which each zone painted on several levels becomes one zone a
level.
- **Ids and names:** the first level keeps the zone's id; the others get free ids, the same
  name with "(L3)" and so on added, and the same colour.
- **Links:** the parts are joined in level order by free two-way links. A hand-made zoning
  takes the way between levels as given, and so does the solver.
- **What changes:** the report's "no free way between them" list then shows only gaps within
  a level.
- **What does not:** the zoning in the Zones window stays as it is; Apply and Check the logic
  use the split copy.

#### 2. Requirements

Every edge, link and fact has a requirement in a small language: tokens joined by `&` (and)
and `|` (or), with brackets. The tokens:

| Token | Holds when |
|---|---|
| `free` | always |
| `key:N` | a key whose owner field is N is held (it opens locks numbered N) |
| `pick:Z` | lock picking is in the logic, a lock pick is held, and the skill can open difficulty Z |
| `bash` | always (fists) |
| `item:NAME` | that item is held: `item:pole`, `item:orb_rock`, `item:picture_tom` … |
| `talisman:NAME` | that talisman is held (sword, shield, ring, taper, cup, wine, book, standard) |
| `rune:LETTER` | that runestone is held |
| `spell:ABILITY` | the ability is available here (below) |
| `npc:NAME` | the creature's zone is reached, so it can be talked to |
| `flag:NAME` | an event happened: a lever pulled, a trap set off, a quest step done |
| `zone:N` | zone N is reached (a lever on the other side of a door) |
| `search:Z` | a look at height Z can find what it hides (below) |
| `option:NAME` | a logic option is on: `option:magic`, `option:kill_npcs`, `option:ring_needs_lava` … |

#### 3. Abilities and where they come from

The abilities are **levitate** (Levitate, Fly), **jump** (Leap), **waterwalk** (Water Walk),
**lava** (Flameproof, the dragon skin boots), **reveal** (Reveal), **open** (Open, which picks
locks as with skill 45), **telekinesis** (Ort Por Ylem; it counts wherever the pole does) and
**gate travel**. `spell:X` holds when the ability can be **cast** or **used** there:

- **Cast** (`cast:X`), only with **Magic** on: the rune bag and the spell's runestones are
  held. Character level, mana and the casting roll are ignored.
- **Used** (`use:X`): an item gives it. `worn:X` is a worn enchantment (the Ring of
  Levitation, the Ring of Leap, the dragon skin boots); it works everywhere. `perm:X` is an
  item that casts without charges. `once:X` is a scroll, a potion or a charged wand; these
  count only with **Single-use items count** on.

**Where magic fails.** On a no-magic tile, and on all of level 9, no spell works, cast or from
an item; only worn enchantments do (a link starting there takes only `worn:` sources). On
level 7, while Tyball's orb stands, the player has no mana: casting there needs
`flag:orb_destroyed` (an orb rock used on the orb), while items still work.

**Hidden gates.** Some walls open when the player looks at a decoration on them (a look
trigger), often with a secret door behind. The look trigger's height Z is a Search check: a
look passes when Search − Z + a random 0–30 is above 15. A failed look changes nothing and
can be repeated, so with **Retry until found** on, any nonzero chance counts: with Search 0
a gate of height Z passes (15 − Z) in 31 looks, and none above height 14 (the highest in
the game is 10). With retry off, the Search skill must be at least Z. Reveal passes any gate
up to height 29. Such a gate needs `spell:reveal | search:Z`.

**Lock picking.** A lock of difficulty Z opens when the Pick Lock skill − 3Z + 30 is above
15 (a failed try can be repeated; the pick does not break). Difficulty 14 needs skill 31,
and 15 never opens. With **Lock picking in logic** off, no door is picked.

#### 4. The facts

What the levels do not say is written in the facts files (`docs/facts/uw1-facts.json` and
`uw1-quests.json`, shipped with the editor, hand-editable). A fact can grant a token where its
requirement holds (**craft**, **flag**), add a requirement to a token's placements (**trade**:
an NPC hands it over only for something), or count items (**count**: 20 coins). It can also
add a **link** between two places, a **warp** (Gate Travel to the moonstone; the silver
sapling) or the **goal**, mark lever chains as **free** once their zone is reached, or say
what a world change **needs** besides its zone. Among them:

- **The goal**: reaching level 9. It needs the Slasher's chamber opened with the Key of
  Infinity, Garamon's bones buried on his grave, and the eight talismans thrown into the
  chamber's lava; the moongate then takes the player to level 9. **Require breaking the orb**
  adds Tyball's orb. Reaching a square that ends the game counts as well: level 9's at
  (52, 51), and any put elsewhere with the Zones window's **Mark as goal**. The solver finds
  them in the levels: every trigger whose chain reaches hack trap 0x3F.
- **The talismans**: the sword (blade and hilt reforged by Shak for **20 coins**), the shield
  (the golem yields it), the ring (the level 5 lever code), the taper (Zak gives it for
  **food**), the cup (Eyesnack's tune played on a flute at its place), the wine and the book
  (lying on level 6), the standard (Dorna, once Rodrick is dead).
- **The Key of Infinity**: Truth (the mantra FANLO at any shrine, so one reachable shrine is
  enough), Love (Judy, for the picture of Tom), Courage (lying on level 3); they combine in
  any order.
- **The dragon skin boots** (walking on lava): Lanugo's stew recipe, read with a bowl holding
  a dead rotworm, a mushroom and a flask of port, makes rotworm stew; Sethar Strongarm gives
  dragon scales for the stew; Marrowsuck makes the boots from the scales and strong thread
  for any food.
- **Puzzles counted as solved** once reached: the level 4 lever puzzle, all levers of
  level 5, the bullfrog puzzle (or levitation over it).
- **The Ring of Levitation's area** on level 7: with **Ring of Levitation needs lava
  protection** on (the default), it needs the lava ability; with the option off, the area is
  one zone with the ledge at (59, 43), from which the player can jump there.
- **The damaging maze** on level 7: its trap tiles are walls.
- **Items NPCs hold**: each needs its quest; with **Killing NPCs in logic** on, killing the
  NPC once its zone is reached also counts. What hostile creatures carry is always free.

#### 5. The world over time

Many trigger chains change the world: a lever drains a pool, carrying the crystal splinter
past a square opens the tombs, a look removes rock, a door closes behind the player. Each is
a **mechanism** with a flag. When the solver can set it off (its trigger's zone is reached and
its conditions hold: an item carried, a variable's value, a Search check, a fact's needs), the
flag is gained and the change is applied to that level: terrain rewritten, objects deleted,
doors opened or closed. That level alone is then rebuilt; its new regions take the zone that
held most of their tiles, so placements keep their zones.

**Changes that close things.** A door that a trigger closes behind the player becomes one
way: free towards the trigger, back only with what opens the closed door. A lever whose
change only takes ways away is never pulled by the solver.

**Getting back.** For every zone reached, a **return check** asks whether the player can get
back with what he holds. A zone he can enter but not leave is a **one-way trap**; a shuffle
must not leave the player stranded in one with something he still needs. Once the silver
sapling can be planted, no zone is a trap: dying takes the player back to it.

#### 6. The solver

The solver works in **spheres**. Sphere 0 is what the player reaches from the start with
nothing. Each following sphere adds what the items, abilities, flags and world changes of
the earlier spheres make reachable. The solver stops when nothing new is added; the goal is
then either reached or not.
The report lists each sphere (zones reached, tokens gained, changes applied), what blocks the
links still closed, the one-way traps, and a table of the main option sets. On the shipped
game, with the default options, the goal is reached in 26 spheres.

#### 7. The shuffle

**Apply** uses **assumed fill**, the usual method of game randomizers:

1. The levels are read as they are, and the solver's world is built.
2. With lock picking off, a door that only picking can open is unlocked if it blocks
   something that is needed and does not move.
3. The key items are put in a random order. Each is placed in turn: the solver runs with
   the items already placed where they now lie and every key item **not yet placed counted
   as held**, and one of the reachable empty places is chosen at random. Because every later
   item is assumed held, a place is never chosen that only a later item could open, which
   is what keeps the game beatable.
4. The other items fill the remaining places at random.
5. The shuffled levels are solved again from scratch, with nothing assumed. The goal must
   be reached and no one-way trap may strand the player; otherwise the next seed is tried
   (up to **Retries** times).

Items trade places whole (item, quality, flags, quantity or link, owner, enchantment), so a
key keeps its lock number. With **Place anywhere** (on at first), the places on the floor are
first moved to random squares the logic reaches, so items do not end up where items lay. An item placed inside a locked chest needs what opens the chest.
Items NPCs hand over in conversation, trap templates, objects a trap or trigger names, the
moonstone and Tyball's orb stay where they are, but still count in the logic. The **Vow of
poverty** and **Starvation** challenges spare only the 20 coins beside Shak and the meat
beside Zak, and those stay where they are too.

The spoiler log names each place by the zones of the hand-made zoning (the zone file in
`DATA`, or else Karl's painted UW1 map), with its level and square.

#### 8. The options at a glance

| Option | Default | What it changes |
|---|---|---|
| Highest climb | 2 steps | a two-way walk; up to twice that with jump |
| Gap jumps | on, 2 tiles (3 with jump) | jumps across pits, water and lava |
| Diagonal jumps | off | jumps on the diagonals too |
| Cut at level changes | on | zones never span two levels |
| Fold empty dead ends | on | fewer, larger zones |
| Consider the current starting square in the editor | on | off: the game's own start, level 1 (32, 2) |
| Magic | on | casting from runes counts |
| Single-use items count | off | scrolls, potions and charged wands count |
| Lock picking in logic, skill | off, 30 | doors may be picked |
| Search skill, retry | 0, on | look gates by Search |
| Killing NPCs in logic | off | an NPC's items by killing it |
| Ring of Levitation needs lava protection | on | the ring's area needs the lava ability |
| Require breaking the orb | off | the goal also needs Tyball's orb broken |

#### 9. What the logic simplifies

- Casting ignores level, mana and the casting roll.
- A spell cast before stepping onto a no-magic tile is assumed not to carry over (in the
  game it does).
- Jumps are judged by tiles, not by speed and run-up; the jump spell's height is an estimate.
- Bashing assumes any weapon and strength will do; chest locks are treated as unbreakable.
- Filler items (food, coins) are checked by the final solve, not while the key items are
  placed.
- Nothing has been checked by playing a shuffled game through yet.

### Automatic zones

The Zones window's **Autozone…** button opens **Automatic zones**, which builds the zones
from the levels instead of painting them. It reads the levels as they are in the editor, with unsaved edits. It is made
for UW1; UW2's levels are read the same way but nobody has checked the result.

It works in steps:

- **Regions.** The open tiles of each level are grouped into regions. Inside a region the
  player can walk from any tile to any other and back: no step climbs more than the
  **highest climb**, either way. Each tile holding a door (any door, portcullis or secret
  door) is a region of its own. Lava tiles make regions of their own. Water joins regions,
  because swimming is normal movement in UW1, but the window counts water tiles. Tiles where
  magic does not work are counted too (all of level 9 counts).
- **Edges** join the regions. A **drop** goes down more than the highest climb; it is free and
  one-way. A **climb** is the other way: it needs the jump spell up to the climb set **with the
  jump spell**, and levitation above that. A **jump** crosses a straight gap of lower ground:
  free up to **Gap jumps** tiles (2), and with the jump spell up to one tile more (3). The far side
  may be no higher than the near side plus the highest climb. **Diagonals too** also tries the
  diagonals; a diagonal tile counts as 1.4. A **door** edge needs what the door's lock needs
  (key, picking, bashing), or the lever that opens it. Walking into lava needs
  the lava ability, `spell:lava` (below). Ceilings are ignored.
- **Teleports.** Every trigger the player sets off whose chain reaches a teleport trap makes
  a one-way edge to the square it lands on. Those that change level are **stairs**. When the
  stairs go both ways, there are two edges.
- **Levers.** A switch holding a use trigger is a lever. The window works out from where it can
  be reached: the hand reaches 1.5 tiles away, 3 floor steps above the feet and 1 below; the
  pole 2.5 tiles, 6 steps up and 2 down. A door a lever opens needs `zone:N`, N being the
  lever's zone. A lever the hand cannot reach also needs `item:pole | spell:levitate`.
- **Mechanisms** are the trigger chains that change the world: terrain changes, deleted and
  created objects, special traps, variables and doors closed or opened by walking. Each gets a
  flag name, such as `flag:terrain_L3_971` (level 3, trigger 971). The solver applies them
  over time (see **The world over time** below).
- **Zones.** Regions that reach each other for free (in both directions, possibly by a round
  trip) become one zone. A door tile goes with a zone next to it. With **Cut at level
  changes** on, stairs never join zones, so each zone stays on one level. **Fold empty dead
  ends** puts a pit or ledge that has nothing in it and only one neighbour into that
  neighbour's zone. Zones are named by what they are (`L5 ledge at (2, 10)`). Key items become
  what each zone grants, as **Add them to the zone's grants** does. The goal is reaching level
  9 (a fact) or, failing that, the largest zone of the last level.
- **Start.** With **Consider the current starting square in the editor** on (the default),
  the start is the new-game square the executable now holds: the game's own, or where the
  editor's start tool moved it. With it off, the start is the game's own square, level 1
  (32, 2), whatever the executable says. The report's **Start** line says which square and zone were used.

The options come in two groups. **Autozone movement** says how tiles become regions and
regions zones (climbs, gap jumps, the level cut, dead ends, the start). **Player logic (the
solver)** says what the solver may expect of the player. The Apply window has the same two
groups.

- **Build and Report** builds the zones and shows the report: the solve over time (the last
  phase, which the others lead up to). The report covers the options, the start, the solver's result and
  a table of the main option sets, the timeline sphere by sphere with the changes set off and
  applied, the changes that close things and how each is treated, the one-way traps and the
  return check. The shuffle itself (Phase D) is **Apply**, which has its own spoiler log.
- **Build and Use** builds the zones, puts them in the Zones window and closes this window.
  It asks first if the Zones window's zoning has unsaved changes. In the Zones window you can
  look at the zones in the view, change them and save them; saving asks for a file name and
  suggests `uw1-zones-auto.json`, and the painted `uw1-zones.json` is never written unless you
  choose it. Apply then uses them. Painting on those zones or drawing a link there makes the
  zoning your own: after a change in the world the solver then keeps its zones, as for a
  painted zoning, rather than regrouping them as automatic ones.

The Zones window's **Autozone…** button opens this window.

Fall damage is not a requirement: no drop in UW1 kills a new character (the worst fall does
about 8 damage).

#### Abilities and logic options

The window also decides what the logic may expect of the player. The **Player logic (the
solver)** rows:

- **Magic.** Casting from runes counts. A spell can be cast once the rune bag and its runes
  are reached. Character level, mana and the casting roll are ignored.
- **Single-use items count.** Scrolls, potions and wands with charges count. With it off,
  only items that keep working count: a worn enchantment, or an item enchanted to cast without
  charges.
- **Lock picking in logic, skill.** The solver may pick locks with this Pick Lock skill, if a
  lock pick is reached. A lock of difficulty Z can be picked when skill − 3Z + 30 is above 15
  (with retries; the pick does not break). Difficulty 14 needs skill 31, and 15 never opens.
  The Open spell picks with skill 45.
- **Search skill** and **Retry until found.** These apply to walls that a look opens (below).

The tiles of the damaging maze on level 7 that hold a move trigger hurting the player are
always walls (a fact). Damage-trap tiles elsewhere are not: making every one a wall left the
solver unable to finish, so that option is no longer offered.

**Abilities.** Links that need levitation, the jump spell, water walking, walking on lava,
Reveal or Open name them as `spell:levitate`, `spell:jump`, `spell:waterwalk`, `spell:lava`,
`spell:reveal`. Each holds when it can be cast (`cast:levitate`: the runes, with Magic on) or
used (`use:levitate`). The window finds where each comes from:

- spells, from the game's spell table: Levitate and Fly, Leap, Water Walk, Flameproof,
  Reveal, Open;
- items in the levels, on the floor, in containers and carried by creatures: enchanted rings
  and armour worn (`worn:`), items that cast without charges (`perm:`), and scrolls, potions
  and charged wands (`once:`);
- the dragon skin boots, from the facts file (below).

**Where magic does not work.** On a no-magic tile and on level 9, no spell works, cast or from
an item; worn enchantments still do. A link starting there only takes `worn:` sources. On
level 7, while Tyball's orb stands, the player has no mana: casting there needs
`flag:orb_destroyed` (an orb rock used on the orb), while items still work.

**Hidden gates.** Some walls open when the player looks at a decoration on them: often a
secret door stands inside the rock. Such a wall becomes a `secret` link that needs
`spell:reveal | search:Z`. Z is the height of the look trigger. A look at height 0 always
works. Otherwise a look passes one time in 31 for each point the Search skill is above Z − 15
(Search 0 against height 10: 5 in 31), and a failed look can be repeated. **Retry until found**
counts any chance; with it off, the Search skill must be at least Z. Reveal works up to height 29.
Secret doors themselves open like any door: no search is needed once they can be reached.

**The facts file.** `docs/facts/uw1-facts.json`, shipped with the editor, holds what the levels
do not say: the dragon skin boots (Marrowsuck makes them from dragon scales and strong thread
for food; Sethar Strongarm gives the scales for rotworm stew; the stew is made from Lanugo's
recipe, a bowl, a dead rotworm, a mushroom and port), the orb, level 7's lack of mana, the
damaging maze, and notes on some hidden gates. Each fact names its source in the game.

**The damaging maze.** On level 7, every tile of the maze holding a move trigger that hurts
the player is a wall.

The **Report** starts with the Phase B part: the solve with the chosen options and a table
of the main option sets (Magic, single-use items, picking and bashing, on and off); each
ability's sources and the sphere where it first holds; the links that need each ability; the
hidden gates and how they are passed; the walls; the dragon skin boots and where each part
is; and, when picking or bashing is off, **the doors the shuffle must open**: doors that only
picking or bashing opens, with what opening each would add.

In **Logic**, bashing is not an option: fists bash any door that can be broken, so a
breakable door is always open to the logic. **The doors the shuffle must open** are therefore
only the doors that need picking, when lock picking is off. Three more options:

- **Killing NPCs in logic.** An item that a named, non-hostile creature carries (a key, a
  talisman, the crystal splinter) can be obtained by killing the creature once its zone is
  reached. With it off, such an item needs what a fact says (the quest that gets it); with no
  fact, it cannot be obtained. What hostile creatures carry is always free: the player fights
  them anyway.
- **Ring of Levitation needs lava protection** (on). The Ring of Levitation's area on level 7
  can be reached from (59, 43) by jumping, but that is an ordeal. With it on, the area needs
  lava protection (the dragon skin boots or Flameproof); with it off, the area shares a zone
  with (59, 43).
- **Require breaking the orb** (off). The goal also needs Tyball's orb on level 7 broken (an
  orb rock used on it). With it off, the goal is reaching level 9 by the ritual, which ends
  the game.

The pole and the Telekinesis spell (Ort Por Ylem) count the same: a lever out of the hand's
reach needs `item:pole | spell:telekinesis | spell:levitate`. A lever is only used from its
own side of a closed door, and a lever set in rock (behind a wall decoration) only once the
rock is opened.

#### The world over time

The solver follows the world as it changes. When the player can set a mechanism off (its
trigger's zone is reached and its conditions hold: an item carried, a variable's value, a
Search check for a look), its flag is gained. Its change is applied to its level: terrain
rewritten, objects deleted, doors opened. That level alone is then rebuilt and the search goes
on. For example, lever #972 on level 3 drains the pool and opens the way to the sword blade;
walking past (20, 42) on level 7 with the crystal splinter opens the tombs.

The zones stay those of the base state. After a change, the level's regions are grouped again
and each group takes the base zone that has most of its tiles. A part that a change cuts off
gets a new zone, numbered from 1000. A zone that a change joins to another is linked to it
both ways for free.

**Changes that close things.** A door that a trigger closes behind the player (by walking or
picking something up) is one way: free towards the trigger, and back only with what opens the
closed door (its new lock's key, picking, or a lever on that side). A door closed without a
locked lock opens by hand, so it changes nothing. A change the player chooses (a lever) that
only takes ways away is never applied. A change that opens and closes things is applied: the
zones reached before stay reached.

**Return check.** For each zone reached, the solver checks that the player can get back to the
start with what he held when he got there (and what he finds on the way). A zone he cannot
leave is a **one-way trap**. The report lists each trap, how it is entered, what it holds and
whether that strands him (an item needed later that only the trap holds). It also solves again
taking nothing from inside the traps. Once the silver sapling can be planted, no zone counts as
a trap: dying takes the player back to it.

**The facts files.** Every `docs/facts/uw1-*.json` shipped with the editor is read:
`uw1-facts.json` and the quest facts `uw1-quests.json`. Facts can also add a **link**
between the zones of two places, a **warp** (Gate Travel, the silver sapling), the **goal** and
what it needs, mark lever chains as **free** once their zone is reached (Karl: the level 4 lever
puzzle and all levers on level 5), or say what a mechanism **needs** besides its zone (the
Slasher's chamber needs the Key of Infinity). Facts can test the options as `option:magic`,
`option:kill_npcs`, `option:ring_needs_lava` and so on. The file's `notes` describe the format.

The **Report** starts with the Phase C part: the solve and the goal, a table of the main
option sets (Magic, single-use items, picking, killing NPCs, the ring), the timeline sphere by
sphere (zones reached, items gained, changes set off and applied), the changes that close
things and how each is treated, the changes that open things, the return check, the facts'
links, warps and goal, the items NPCs carry and the items a trap creates. The rules are
glitchless.

### Shuffling item properties

**Randomizer ▸ Item property shuffle…** changes what items are. Weapons, armour, food, lights,
tools, runestones, wands, spell scrolls, potions and rings may become something else. Each
object keeps its owner: food belonging to a rat that becomes a lantern is the rat's lantern.
(The shuffle never trades objects between spots, so it needs no ownership option; the item swap
has one.)

**Place anywhere** (on at first) also puts the items it rolls that lie on the floor on random
squares of their level:
- open floor, slopes, and the open half of a diagonal tile (the item lies inside that half, at
  the floor's height there, so on a slope it is neither buried nor floating);
- not water or lava, and not a square with a door, a trap or a trigger;
- anywhere else on the level: these items carry no logic, so where they land does not matter
  to finishing the game (one may land somewhere hard to reach, or out of reach);
- one item a square while there are squares.

Items in containers and creatures' inventories, and the starting equipment, stay where they
are. With it off, everything stays where it lies. The rolls do not depend on it: the same seed
gives the same items either way. The item swap never moves items to new squares: it keeps them
in the places items had. Choose the settings in the tabs, press **Preview** to see the
report, then **Shuffle**. **Undo shuffle** puts back every object changed by the shuffles
made in this window; each shuffle is
also one Edit ▸ Undo step per level, and nothing reaches the game until you save the levels.
A level edited after the shuffle is left as it is (its old records could undo those edits
halfway and break its object lists); the report names it, and Edit ▸ Undo steps back there.
The same seed on the same levels gives the same result. The settings are kept between runs
(`item-shuffle-uw1.json` / `-uw2.json` in the settings folder); the item swap keeps its own.

**Formula** shows the maths the settings give, in symbols, and follows every change: for
example `quality' = clamp(q + U{−10..10}, 1, 63)` for a quality spread of 10, or
`p = clamp(8.5/100 × (1 + 30/100 × d), 0, 1)` for an enchantment's chance at depth d.
`Cat(a 50, b 25)` is a weighted pick (a twice as often as b), `U{a..b}` an even pick of a whole
number, `N(μ, σ)` a normal draw. The notation is explained at the end of the box.

When the window grows, the formula and the report get the room; the tabs keep their height
and scroll. In a short window the tabs shrink and scroll too.

**Every setting starts from the world**: the relative weights are the counts in the levels
(the most common kind is 100), the chances, means, deviations and depth slopes are fitted to
the enchantments the levels hold, and the bars and curves follow them. Each tab has **From
the game**, which sets its values to what the levels hold now (after your edits); **All from
the game** does every tab. **World report** writes what the game's levels hold, as saved on
disk: the items by kind, the enchantments by kind and by level, the other effects and curses,
the wand, scroll and potion spells, the rings, and the qualities.

Weights are relative, from 0 to 100: a weight of 0 removes that kind. The shuffle never
touches Caliburn (UW1's shiny sword), Altara's jewelled dagger (UW2), talismans, crowns, dragon skin
boots, fraznium armour, UW2's Guardian signet rings, objects that are templates inside traps,
and the ignored objects.

- **Weapons:** weights for axes, swords and daggers, maces and clubs, and ranged weapons with
  their ammunition; the share of launchers among the ranged objects (the rest become arrows,
  bolts or sling stones, never enchanted); the quality spread (± around each object's own
  quality). Then one row per enchantment kind (Accuracy, Damage) with its chance, the mean
  strength (1 = Minor) and the deviation of a normal draw. A weapon is enchanted with the sum
  of the kinds' chances (at most 100%), and the kind is then picked in proportion to its
  chance. **Depth: chance** raises the chance (double on the
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
  minimum number of threads (5), poles (3) and fishing poles (3). When there are fewer, random
  food and lights lying on the floor are turned into them.
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
  and a bar per worn effect. Every time what is worn changes, a curse of strength N does N d8
  damage, never taking the wearer below 3 hit points.
- **Ignored:** the objects the shuffle leaves alone: pick them from the list of everything it
  touches (with a filter), or ignore the object selected in the editor. Wands enchanted
  directly, which never run out, are ignored at first: UW1's bullfrog wand (it works the level
  4 puzzle), UW2's slightly worn wand of Telekinesis in the Scintillus Academy and its wand of
  Light. The list is kept in `item-ignore-uw1.json` / `-uw2.json` in the settings folder; the
  item swap has its own.

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

**Randomizer ▸ Swap ▸ Swap items…** only moves the existing objects around: nothing is made or
removed, so every item keeps its count, and no weights apply. Its settings are its own, kept in
`item-swap-uw1.json` / `-uw2.json` in the settings folder, apart from the shuffle's. The window
has three tabs: **Settings**, **Ignored** and **Starting equipment**. **Preview** writes the
report without changing anything, **Swap** applies it, and **Undo swap** puts back every
object the swaps from the window changed. Each swap is also one Edit ▸ Undo step per level,
and nothing reaches the game until you save the levels. The same seed on the same levels
gives the same result.

The objects trade places whole: item, quality, enchantment, quantity and owner (but see below).
Charged wands swap among themselves with their spells and charges, since the charges live in
an object inside the wand; the few objects holding triggers stay. Never moved: Caliburn,
Altara's jewelled dagger (UW2), talismans, quest items, objects that are templates inside
traps, Garamon's bones, the ignored objects and, while kept, the starting equipment.

- **Order:** **Random** (at first) moves the objects within the choices below. **Uniform
  progression** hands out the weapons, armour, spell scrolls and wands by strength: the
  weakest of each kind go to the first level, the strongest to the last. Each kind is sorted
  on its own. A weapon's strength is its highest damage (slash, bash or stab, from
  OBJECTS.DAT) plus its enchantment's level; ammunition counts its missile damage, and a
  launcher the damage of the ammunition it fires. Armour counts its protection plus its
  enchantment's level. Spell scrolls and wands count the spell's circle (UW1's spells without
  runes count 9; a wand without a spell 0). An enchantment's level is its degree (Accuracy,
  Damage, Protection, Toughness: 1 to 8; UW2's weapon ones 1, 3, 5, 7), a spell's circle, or
  4.5 for a curse or an effect without degrees. The levels are visited in order: UW1 from level
  1 to level 9; UW2 world by world, Britannia, the Prison Tower, Killorn Keep, the Ice Caverns,
  Talorus, the Scintillus Academy, the Pits of Carnage, the Tomb of Praecor Loth and the
  Ethereal Void, levels in order within each world. UW2's slot 73, outside the worlds, keeps
  what it has. Each level keeps its number of objects of each kind and takes the next ones from
  the sorted list. Within a level they go to its spots at random. Food, lights, tools,
  runestones, potions, rings, bones and scenery have no strength here: they are swapped at
  random over the whole game. Uniform progression works within each kind across the whole
  game, so **What trades with what** and **Where objects may go** are greyed while it is
  chosen. The starting equipment, the ignored objects and the spot's owner work as before.
  With **swap the enchantments**, the enchantments are handed out the same way, by their level.
  The report lists, for each kind and level, the count and the strengths before → after.
- **What trades with what:** **within each kind** (a sword stays a weapon) or **across all the
  ticked kinds** (a sword may land where a torch was).
- **Where objects may go:** **within each level**; **within ± N levels** (in UW2 only within
  the same world); **within each world** (UW2: Britannia, the Prison Tower, Killorn Keep and
  so on); or across the whole game. Ultima Underworld has one world, so there "within the
  world" means every level.
- **An item put in a spot takes the spot's owner** (on at first). On UW1's level 1 the cheese
  and the meat at (22, 8) belong to the giant rats; a lantern swapped into their place belongs
  to the rats, and the cheese, wherever it goes, takes the owner of its new spot. This only
  happens where the owner field means "belongs to" on both items: the game reads it that way
  only for items whose can-own bit is set in COMOBJ.DAT. For other objects the same field is
  something else (a key's lock, whose bones they are, a text number), and those keep their
  own. With it off, every object keeps its own owner.
- **Kinds taking part:** weapons, armour, food, lights, tools, runestones, wands, spell
  scrolls, potions, rings, **bones and skulls** (off at first) and **other scenery** (off at
  first). Bones and skulls are the skulls, bones and piles of bones you can pick up; Garamon's
  bones (UW1, level 8, the pile whose owner field is 62) never move, since burying them is part
  of the plot. UW2's one pile of bones with the same mark, in the Tomb of Praecor Loth, stays
  too. Other scenery is the rest of what you can pick up from the scenery range: broken weapons,
  pieces of wood, debris, plants, UW1's dead rotworm, UW2's lump of wax. The pole is a tool.
- **Enchantments:** **After the swap, swap the enchantments among objects of the same class**
  (off at first). The enchantments then trade places among the melee weapons, among the
  launchers, among the armour (shields included) and among the spell scrolls, with the same
  scope; each class can be left out. Every object of a class takes part, plain ones too, so an
  enchantment may move to a sword that had none. Ammunition, Caliburn and the quest armour stay
  as they are.
- **Ignored:** the objects the swap leaves alone, picked from the list of everything it
  touches (with a filter), or the object selected in the editor. The list is the swap's own; it
  starts as a copy of the shuffle's list, which holds the wands enchanted directly at first
  (UW1's bullfrog wand, UW2's slightly worn wand of Telekinesis and wand of Light).
- **Starting equipment:** while **Keep the starting equipment where it is** is ticked (at
  first it is), these objects stay and nothing is put in their place. At first the list holds
  what the game gives the Avatar on level 1, as far as the swap could move it. UW1: the sack
  beside the start square, on (33, 3): the dagger, bread, fish, torch and apple. UW2: in the
  Avatar's quarters, the sack, leather vest and boots on (20, 49) (with thread, a torch and a
  dagger in the sack), and the closet behind the secret door at (19, 50): the pack (dagger,
  water, bread, torch), the sling and its stones, the Ort, Bet, In, Jux and Sanct stones and a
  hand axe. Add or remove objects as for the ignore list; **The game's list** sets it back.

**Formula** shows the maths the settings use: the pool, the groups, the permutation
(`π ~ a uniform random permutation`; for ± N, the passes of trades that keep every object
within N levels), the owner rule and the enchantment swap. It follows every change. When the
window grows, the formula and the report get the room; the tabs keep their height and scroll.

Under Uniform progression the formula shows the strengths and the walk: level ℓ with n_ℓ
objects of a kind takes ranks r_ℓ … r_ℓ + n_ℓ − 1 of the sorted list.

### Swapping monsters

**Randomizer ▸ Swap ▸ Swap monsters…** makes the monsters trade places. Each spot keeps its position,
home, goal and (by default) whatever lies in its inventory, and takes another monster's kind,
hit points, powerful flag and attitude. **Preview**, **Swap** and **Undo swap** work as in the
item tools; each swap is one Edit ▸ Undo step per level.

- **Order:** **Rank matching** (at first) uses **Swap**, **Target** and **Spread** below.
  **Uniform progression** sorts the monsters by **Strength** (experience at first) and hands
  them out level by level, in the same order as the item swap's (UW1 levels 1 to 9; UW2 world
  by world, from Britannia to the Ethereal Void, the Pits of Carnage before the Tomb of
  Praecor Loth). Fliers, swimmers, fire-resistant creatures and the other walkers are each
  sorted on their own, so a level keeps its number of each. Each level takes the next
  monsters of the list; within a level they go to its spots at random. A monster only goes
  where it may stand (fliers anywhere, swimmers on water, fire-resistant ones on lava) and,
  if one does, where its height fits. When none of a level's share fits a spot, the spot takes
  the next monster in the list that does; the one left over goes to the next level. The report
  counts these. Creatures with a conversation stay, level-bound ones (liches, guards, Tybal's
  men...) trade only within their level, and the ignore list and the brain creatures or fire
  elementals option still apply. A creature that the game placed on water or lava it could not
  otherwise stand on keeps its spot. **Swap**, **Target** and **Spread** are greyed while Uniform
  progression is chosen. The report lists, for each class and level, the count and the
  strengths before → after.
- **Swap** within each level, within each world (UW2), or across all levels.
- **Strength**: the game's own **experience value** for the kind (the default), hit points,
  damage per round (the three attacks weighted by how often each is used, damage plus
  strength / 5, and attack spells by how often the creature casts: missiles at their
  OBJECTS.DAT damage, area spells counted as 12), **combined** (√(hit points × (1 + armour /
  8) × damage), so neither side outweighs the other), or none (uniform).
- **Target**: at 0 each spot keeps its difficulty, taking a monster about as strong as the one
  that was there, which already follows the game's depth curve; at 100 stronger monsters go
  deeper; in between, a blend of the two.
- **Spread**: at 0 the strongest monster goes to the hardest spot, strictly; at 100 the swap
  is uniform.
- **Keep the hit points at the spot**: a golem put where a 5-hit-point worm was gets 5 hit
  points, and the worm gets the golem's.
- **Carried items go with the monster**: otherwise they stay with the spot, keeping each
  level's loot where it was. Monsters carrying something then only swap within their level.
- **Leave the fire elementals alone** (UW1) or **Leave the brain creatures alone** (UW2): on
  at first. Those creatures neither move nor take another's place.
- **Powerful**: counted in every measure (armour × 5/3, +7..12 attack skill, +4..15 damage,
  experience × 1.5..3). **Roll it again** gives each moved monster a new chance of being powerful, higher with depth.
- **Formula** shows how the swap works out with the settings as they are, and changes with
  them. N(μ, σ) is a normal distribution of mean μ and standard deviation σ; U(0, 1) is
  uniform. In short: each monster's strength gets a rank p from 0 (weakest) to 1 (strongest)
  in its group, each monster a key p + N(0, σ), each spot a key from the rank of the monster
  that was there (and its depth, by Target) plus N(0, σ), with σ = 0.5 × Spread / 100; both
  are sorted and paired in order.

The settings keep their size; the report below takes the rest of the window. In a short
window the settings scroll.

What stays, and why:
- Creatures with a conversation: the game finds them by their conversation number, and only
  on their own level (plot deaths, schedules, some traps). An option lets them move within
  their level. The **Whitelist** lets chosen ones move anywhere: their conversation and special
  death go with them, only the directions to them stop being right. UW1's level 2 gazer
  (Goldthirst's quest) and UW2's Listener (Altara's dagger) are whitelisted at first.
- Templates inside create-object traps, summoned creatures, creatures with no hit points, the
  Ethereal Void's nameless creatures, and the **Ignored** ones.
- Within their level only: creatures the game calls by race (UW2's liches, the trilkhun
  (race 0xB), level 1's guards, level 4's bloodworms; UW1's men on Tybal's level), creatures
  whose goal targets another creature, and (when carried items go along) creatures carrying
  something.
- Swimmers swap only with swimmers, and a monster only goes where its height fits under the
  ceiling.
- Water and lava: a creature that walks drowns in water, and lava burns it unless it resists
  fire. So a spot on water only takes a flier or a swimmer, and a spot on lava only a flier or
  a creature that resists fire (UW1's fire elementals; UW2's fire elementals, hordlings,
  despoilers and destroyers). These spots are filled first, each with the fitting monster
  nearest its strength; a spot whose own creature could go nowhere else keeps it. The
  floor's kind comes from TERRAIN.DAT, how a creature moves from OBJECTS.DAT and COMOBJ.DAT.

In the **Whitelist** and **Ignored** tabs, Ctrl+click and Shift+click select several entries
(Ctrl+A all those shown), and the buttons add or remove them all at once.

The **Strength table** tab lists every kind with its experience, hit points, armour, damage,
combined strength, how it moves, whether it resists fire, its height and its spells. Click a
column's header to sort by it; click it again to reverse the order.

**World report** lists the creatures as the world holds them now: how many, how many never
move and why, how many are bound to their level, every kind with its count and the levels it
lives on, every level with its count, how many could move, its experience total and average
and its strongest creature, and the creatures with a conversation.

### Named challenges

**Randomizer ▸ Named challenges…** changes the whole game by a rule: no food, no weapons,
every door locked, and so on. Tick one or more challenges. **Preview** shows what would
change, challenge by challenge and level by level, with the first few examples. **Apply**
makes the change: one Edit ▸ Undo step per level. **Undo challenges** puts everything back as
it was. Nothing is written to the game until you save. The same seed gives the same result.

A challenge covers every object of the levels: on the floor, in containers, carried by
creatures, and the templates inside create-object traps (or the traps would make the objects
again). It also covers the loot creatures get when they die or first trade (OBJECTS.DAT's
creature table) and, in a saved game, the player's inventory. Quest items are never touched:
keys, talismans, Caliburn, the Book of Honesty, Underworld II's quest items and the like.

The challenges:

- **Allergic to magic**: weapons, armour, rings and crowns lose their enchantments; spell
  scrolls become blank; wands lose their spells, except the plot's (Ultima Underworld's
  bullfrog wand, Underworld II's wands of Telekinesis and Light). The runestones and the rune
  bag go. In a save, the runes in the bag and on the shelf go too.
- **Bones rise**: a skull and two bones (or piles of bones) lying within a 2 × 2 square of
  tiles rise as a hostile skeleton, on the skull's tile. Each object is used once. Garamon's
  bones stay.
- **DMD mode**: creatures without a conversation turn hostile, and every creature becomes
  powerful. The special creatures stay as they are.
- **Dystopia**: every weapon and piece of armour is badly worn (quality 1-15).
- **Eternal darkness**: every torch, candle, lantern and light sphere goes, lit or not.
  Ultima Underworld keeps the Taper of Sacrifice. The tiles' own light stays.
- **Fasting**: only part of the food stays, picked at random. Set the share with **Food
  remaining**. Fish counts as food; drinks, mushrooms and plants do not.
- **Ghost town**: every creature without a conversation goes, except the special creatures,
  and so do the creature templates inside traps. What a creature carried lies on the floor
  where it stood.
- **Hoarding**: food, weapons and armour leave the floors and chests. Ordinary monsters
  (no conversation, not special) and traders carry them, spread evenly. Goods stay on their
  own level when it has someone to carry them; otherwise they go to the nearest level that has
  (in Underworld II, in the same world). With Ghost town, only traders are left to carry them.
- **Illiterate**: books, scrolls and maps go, except the ones the plot needs.
- **Lockdown**: every closed door that can be broken gets a lock no key opens. Pick it
  (harder deeper down, never too hard to pick) or break the door. Portcullises, secret doors,
  doors that cannot be broken, doors with a lock and doors that a lever or trigger works are
  left alone.
- **Merchant**: all food goes to the traders (the characters who barter), spread evenly.
  Creatures no longer get food as loot.
- **Monk**: no weapons: swords, axes, maces, slings, bows, crossbows and their ammunition.
- **Naked**: no armour, shields, rings or crowns.
- **Old school**: the map item goes. Draw your own.
- **Peaceful**: every creature is friendly at the start.
- **Precognition**: every level's map is fully drawn. In DATA this reaches new games.
- **Ranger**: each melee weapon becomes a launcher or a stack of ammunition, half and half.
  The launchers are weighted from the least to the most damaging (10:3:2:1 at first; change
  them in the window). Ammunition is sling stones, bolts or arrows, 1 to 15 of them.
- **Starvation**: no food and no fishing poles. The Ylem runestones become other runes, so
  Create Food cannot be cast. One piece of meat is placed beside Zak (level 3, (48, 38)), the
  least he accepts for the taper; it is the only exception.
- **Utopia**: every weapon and piece of armour is in excellent shape (quality 63).
- **Vegetarian**: no meat, no fish, no fishing poles.
- **Vow of poverty**: no coins and no gems, and creatures carry no treasure. Ultima
  Underworld's emeralds stay (a puzzle needs them). One pile of 20 coins is placed beside
  Shak (level 2, (60, 4)), since reforging the sword needs it; it is the only exception.

Some pairs cannot go together: Monk and Ranger, Monk and Hoarding, Utopia and Dystopia,
Peaceful and DMD mode, Merchant and Hoarding. The challenges are applied in this order: Ghost
town, Bones rise, Peaceful, DMD mode, Vow of poverty, Allergic to magic, Illiterate, Old
school, Eternal darkness, Naked, Monk, Ranger, Vegetarian, Starvation, Fasting, Utopia,
Dystopia, Merchant, Hoarding, Lockdown, Precognition.

The **Special creatures** tab lists the creatures Ghost town keeps, DMD mode leaves alone
and Hoarding gives nothing to. At first it holds the fire elementals (Ultima Underworld) or the
brain creatures (Underworld II) and the Ethereal Void's creatures without a name.

Changes to OBJECTS.DAT's creature table open in **Tools ▸ Object tables** as unsaved edits;
**File ▸ Save all changes** writes them. OBJECTS.DAT lives in DATA, so it applies to every
saved game.

Limits:

- Conversations are not changed. Items a character hands over in a conversation still
  appear.
- Attitudes change in play: conversations and the game still make creatures friendly or
  hostile.
- A pole and strong thread still make a fishing pole.
- Loot made when a creature dies keeps the game's own quality.

### Randomize mantras (UW1)

**Randomizer ▸ Randomize mantras (UW1)…** makes the shrines' mantras swap words. For example,
the Attack mantra might become SOL and the Casting mantra RA. Every book, scroll, wall writing
and conversation that names a mantra is changed to the new word, so the game's hints still
work. The window has a **Seed** (**New seed** picks one), **Preview**, **Apply** and **Undo
mantras**. The report lists each mantra's old word, its new word and what the shrine does with
it, then every text that changed. The same seed always gives the same result.

How it works:

- The game looks up a typed mantra among strings 0x33 to 0x4C of string block 2. These are the
  20 skill mantras, INSAHN (the way to the Cup of Wonder), FANLO (the Key of Truth), NO, and the
  three group mantras SUMM RA, MU AHM and OM CAH (game/SKILLS.C).
- The words move between those strings. What a shrine does stays with each string: whatever
  word now sits in FANLO's place gives the Key of Truth.
- Every mantra that takes part gets another mantra's word. None keeps its own.
- **Two mantras stay:**
  - **INSAHN:** the incense visions show its three parts as pictures (cutscenes 11 to 13), and
    pictures can't follow a change of word.
  - **NO:** it does nothing, and no text names it.
- **Texts:** a mantra in capitals ("RA", "SUMM RA") is replaced wherever it stands as a whole
  word. A mantra written in title case is replaced only if it is long enough not to be a common
  word, like "Summ Ra" or "Fanlo" in the book "Folanae Fanlo". The text keeps its case.
- **Where the changes go:** they are unsaved edits to STRINGS.PAK, shared with **Tools ▸
  Strings** and the conversation window, which show them at once. **File ▸ Save** writes them.
- **Undo mantras** puts back the strings that still read as Apply left them. Edit ▸ Undo does
  not cover strings.
- UW2 has no shrine mantras, so the menu item is greyed out there.

### Vowel play

**Randomizer ▸ Vowel play…** shuffles the vowels of the game's proper names, the way the Quest
64 randomizer Merrow does with its monsters. "Garamon" might become "Guromon" and "Lord British"
"Lord Britush". It works on both games.

- **The new spelling:** each vowel (a, e, i, o, u; y counts as a consonant) is drawn again and
  keeps its case. So a name keeps its length, its consonants and its capitals. **Keep a first
  vowel** leaves a vowel at the start alone, so "Illomo" still begins with I.
- **One spelling everywhere:** a name gets one new spelling from the seed and the name. Every
  string uses it: the names creatures talk under (string block 7), conversations, books,
  scrolls and cutscenes. Possessives ("Garamon's") and capitals ("GARAMON") follow. Matching is
  case-sensitive, so "Dr. Owl" changes and "an owl" does not.
- **The names:** the list on the left, one name per line, is saved for each game. Edit it
  freely. **The game's names** fills it again with:
  - every capitalized word of the names creatures talk under that has a vowel and never appears
    in lower case in the game's text ("Door", "Rat" and "Data" do, so they are left out; so are
    titles such as Sir, Lord and Dr);
  - the game's other names that its text uses: the Avatar, Lord British and Britannia, plus
    Cabirus, Almric, Arial, Corwin and others in Ultima Underworld, and the Guardian, Blackrock
    and Killorn in Underworld II.
- **Words that never change:**
  - the shrine mantras (Randomize mantras covers those);
  - "Folanae";
  - every word of a text that a conversation compares typed text against with compare or
    contains: passwords like "Deco Morono" or "xyzzy", and the answers NPCs wait for. In
    Ultima Underworld this keeps Murgo, Zak and Cabirus; in Underworld II, Bishop.
  The report lists these words.
- **No clashes:** a new spelling is never the old one, a word the game's text already uses (in
  any case), another name or another name's new spelling. A name with no vowel to change stays.
- **Where the changes go:** like Randomize mantras, the changes are unsaved STRINGS.PAK edits,
  shown at once in Tools ▸ Strings and the conversation window. **Undo vowel play** puts them
  back, and **File ▸ Save** writes them. Rude results are not filtered out.

### Apply

**Randomizer ▸ Key item scatter ▸ Apply…** (the key item shuffle) moves the key items to new
places so that the game can still be finished with the logic you choose. It works on Ultima Underworld 1 only, because
the facts and the solver it uses are UW1's. It reads the levels as they are in the editor,
with unsaved edits and with any Named challenges or swaps already applied.

**What moves.** Tick the categories:

- **Keys** lying in the levels (a key keeps its lock number).
- **Key of Infinity parts** lying in the levels (the Key of Courage).
- **Talismans lying as objects**: the Book of Honesty and the Wine of Compassion. The others
  are made or given by events and conversations; they stay, but what they need still counts.
- **Runestones and the rune bag.**
- **Ability items**: the rings of Levitation and Leap, scrolls and potions that give an
  ability, the pole and the lock picks.
- **Dragon skin boots' ingredients**: strong thread, bowls, the dead rotworm, mushrooms and
  flasks of port.
- **Plot items** lying in the levels: the halves of the sword, the orb rocks, Garamon's
  bones, the flutes and the like.

**Place anywhere** (on at first) turns the places on the floor into random squares of their
level instead of where items lay:
- open floor, slopes and the open half of a diagonal tile, at the floor's height there;
- not water or lava, and not a square with a door, a trap or a trigger;
- only in zones the logic reaches when every item that moves is counted as held, so the fill
  can still put each key item somewhere reachable;
- one place a square while there are squares.

Places in containers and creatures' inventories stay those places. The log says how many places
moved, and gives each item's old square with "a random square" for its new one. Off, items
trade the places where items lay, as before.

**Spoiler log in the map notes** (off at first) writes the log into the game's own map notes
at **Apply** (Underworld only; `LEV.ARK` in `DATA`, so a new game has them, and a mod made
from the game carries them):
- **page 9**, the Ethereal Void's map, keeps its own notes and gets a warning that the next
  pages hold spoilers;
- **from page 10: hints**, on as many pages as they need. Each item, in the order the logic
  reaches them, with the name of the hand-made zone it lies in and its level ("Key of Courage:
  Tyball's crib (L7)"), or, without names, its level and the part of the level ("level 3,
  southeast"). ", hidden" means it is in a container or carried;
- **then a page that says only SOLUTIONS NEXT**, so that turning one page too far does not
  give anything away;
- **then the solutions:** each item's level, square and what holds it ("L3 (12, 40) in sack").
- **Which items:** the artifacts (the talismans, the Key of Infinity's parts, and Caliburn,
  its blade and its hilt), whether or not the logic needs them, and every other key item the shuffled game cannot be finished without. That is checked by
  solving the shuffled game without the item, with the run's logic options. So a key to a door
  the options let you bash or pick, or that you can go round, gets no hint, and neither does
  one of two keys to the same lock.
- A page holds 25 lines; each page's first line says where the next one is.
- Keys are named by the first lock they open ("key to door, L2 (15, 51)").
- Everything is written in capitals, as the game itself writes notes. The map screen's note
  font has no lowercase letters, and a note with one hangs the game as soon as its page is
  shown (in DOSBox and in Underworld Exhumed alike). The editor writes every map note in
  capitals, and saving rewrites any lowercase note it finds on a page it opened.
- The map screen's up arrow reaches these pages past level 9 (see **Automap and map notes**).
- Undo apply and Edit ▸ Undo take the notes back with the rest. A later Apply replaces the log
  of an earlier one.

**Creature inventories** lets items carried by creatures without a conversation take part.
Their inventories become places too.

**Additional items to include in the shuffle** shows what else takes part ("none" at first);
**…** opens a window listing every kind of item the player can pick up that lies in the levels,
with how many there are, by class:
- weapons; armour, shields and crowns; rings; wands; potions; food and drink; lights; coins,
  gems and treasure; books, scrolls and maps; containers; skulls and bones; broken weapons,
  wood, plants and debris; other things (bedrolls, instruments, tools …);
- a class's box ticks all its kinds; **The item swap's kinds** ticks what the old "Everything
  else as filler" moved; **All** and **None** do what they say.

The kinds ticked take part as filler: they trade places with the key items and with each other,
and their places become places too. Key items move by their category whatever is ticked here.
A **container** takes everything it holds along, and goes only to a place on its own level (what
it holds is a list of that level's objects). A container holding something the logic needs, a
key item, or a trap or trigger stays where it is, and what it holds takes part instead.
Fixed objects the game marks as pickable (urns, campfires, fountains, cauldrons) are not offered.

**At the start** (both off at first) adds two aids, done at **Apply** as part of the same
step, so **Undo apply** takes them back too:
- **A full torch (quality 63) and the map beside the start.** The start is the square the
  solver uses: the new-game square in `UW.EXE`, or the game's own with **Consider the current
  starting square in the editor** off.
  - Apply looks on the start and the eight squares around it, on the floor and inside
    containers there. The shipped game has a sack at (33, 3) holding a torch (quality 40) and
    the map.
  - A torch found there is filled up to 63 (the most a light holds).
  - A missing torch, or a missing map, is put on the floor of the first open square beside
    the start.
- **Every map square explored.** Every level's map shows every open square, as if the player
  had seen it all: each square gets the code the game writes when the player sees it. The
  notes are kept. Edit ▸ Undo of the apply and **Undo apply** both take back the maps with the
  levels (Undo apply leaves the maps of levels changed since); Edit ▸ Redo explores them again.

The options are laid out in columns: the checkboxes of a group start at the same places.

Some objects always stay where they are. The log lists them with the reason: items carried
by an NPC with a conversation, a create trap's template, objects a trap or trigger names,
objects that hold other objects or a trigger, the orbs, the moonstone (Gate Travel goes to
it), and the coins and food beside Shak and Zak.

**Zones.** Apply uses the Zones window's zoning, with its unsaved changes; the row at the top
says which file and how many zones. The zones come from it as described in **How the
randomizer's logic works** (Zones from a zoning). With no zoning yet (nothing painted), Apply
makes automatic zones from the movement options. **Quick autozone** makes those automatic
zones at once and puts them in the Zones window, to look at, change and save there (saving
asks for a name; `uw1-zones-auto.json` is suggested). It is only available while there is no
zoning, and greys out once clicked. If a zoning appears in the Zones window while the zones
are being made, Apply asks before replacing it. **Zones window…** opens that window.

**The logic** rows are the same as in the Automatic zones window, with one more:
**Require breaking the orb (level 7)**. With it on, finishing also needs Tyball's orb broken
with an orb rock. The goal is reaching level 9, through the ritual in the Slasher's chamber,
or a square that ends the game (see the Zones window's **Mark as goal**). Stepping on such a
square plays the ending at once, so it needs only this option's orb, not what level 9's goal
needs.

**How it works.** The key items, in a random order, are placed one by one. For each, the
solver of the automatic zones (with the world changing over time) finds the places it can
reach, counting every key item not placed yet as already held. One of those places is chosen
at random. The other items then fill the remaining places at random. At the end the shuffled
levels are solved again from scratch, with nothing counted as held. The goal must be reached,
and no one-way trap may strand the player with something they need. If not, the next seed is
tried, up to **Retries** times. The same seed on the same levels gives the same shuffle, and
the seed the log names (after failed attempts, the one that worked) gives it again on its own:
Place anywhere draws each attempt's squares from that attempt's seed.

**Doors with lock picking off.** When lock picking is not in the logic, a key item never goes
behind a door that only picking opens. If something the logic needs and that does not move
lies behind such a door, the door is unlocked as part of the shuffle, and the log says so.

- **Preview** computes a shuffle and shows its spoiler log. Nothing changes.
- **Apply** writes the shuffle shown into the levels (or computes one first). Items trade
  places whole: item, quality, flags, quantity or link, owner and enchantment. Each level is
  one undoable step, saved with the levels.
- **Undo apply** puts the levels back as they were, byte for byte. A level edited since is
  left alone; Edit ▸ Undo still steps back there.
- **Export spoiler log…** saves the log as a text file.

**The spoiler log** gives the seed and options, what moves and what stays, the doors
unlocked, the route (spheres, when each talisman and key comes) and, sphere by sphere, where
each key item went: level, square, the container or creature holding it, the name of the
hand-made zone it lies in, where it was, and what the place held before. A painted zoning in
the Zones window names the places itself; otherwise the names come from the zone file in
`DATA`, or else Karl's painted UW1 map. The log's first lines say which zones were used.

## Mods

**Tools ▸ Mods…** (Ctrl+Shift+M) makes and loads mods. A mod (`.uwmod`, format 2; the details are in
[docs/mod-format.md](mod-format.md)) holds only the differences from the game's own files, never
the files themselves, so it can be shared without the game's data:
- **It changes files the game already has**: the executable and the files under `DATA`, `CRIT`,
  `CUTS` and `SOUND`. It never removes a file, and never touches a saved game. The only files
  it adds are sound files that the game opens by a number from its data and plays even though
  they are not shipped: UW1 speech `SOUND\nn.VOC`; UW2 effects `SPnn.VOC`, trap sounds `UWnn.VOC` and
  speech `BSPnn.VOC`. New music cannot be added (the themes' numbers are fixed in the game's
  code): a new theme replaces the one it goes into.
- **Make a mod** compares the game with the originals the editor kept (`.ORIG`), or with an
  untouched copy, and writes the `.uwmod` (a zip with `mod.json` and one delta per file). A UW2
  archive's compressed blocks are compared uncompressed, so a changed object carries only its
  own bytes. Every delta is checked to give back the saved file exactly before the mod is
  written. A file removed, or added where the game would never open it, is listed and no mod
  is made.
- **Load a mod** checks each file against the open game. A file can be the original the mod
  was made from (the mod applies), already as the mod makes it, a different file (another
  release, another mod, or changed since: the mod does not apply), or missing. A mod applies
  only to the exact originals (their SHA-256), and the files it writes are then the same, byte
  for byte, as the ones its author saved. Loading a mod then opens the game again. Mods made by earlier versions (format 1)
  are not read.
- **Changed files as a .zip…** (on Make a mod) puts the saved files that differ from the
  originals (the same comparison as **Collect the changes**), whole, into a `.zip` at their
  folders (`DATA\LEV.ARK`, `SOUND\12.VOC`…), with a `README-CHANGED-FILES.txt` listing them.
  Unzip it over your own copy of the game, keeping the folders, to get your changes there.
  It is **not for the mod manager**, which takes `.uwmod` files, and **not for sharing in
  public**: it holds the game's own data. Share a `.uwmod` instead.

## Keys and mouse

| Where | Key | What it does |
|---|---|---|
| View | Left click | select an object or tile (Move tool: put the selection there) |
| View | Double click | select and go to it |
| View | Left or right drag | turn round the point looked at |
| View | Middle or Shift+drag | move the point |
| View | Wheel, + and − | closer, farther |
| View | Ctrl+wheel | raise or lower the floor under the mouse |
| View | W A S D, arrows | move while held (Shift: faster) |
| View | Q, E | turn while held |
| View | R, V | up, down a floor step |
| First person | P | start or stop walking |
| First person | W A S D, wheel | walk (held keys glide), step |
| First person | Q E, drag | turn |
| First person | R, V | look up, down while held |
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
| `SOUND\*.VOC`, `SOUND\*.XMI`, `SOUNDS.DAT` | Sound and music |
| `DESC`, save folders | Saved games (rename, copy), Export as a saved game |

In the editor's settings folder (`%APPDATA%\UwEditorAvalonia`, `~/.config/UwEditorAvalonia` on Linux):
`settings.json`, `checkpoints`, `crashes` (crash logs), `running-PID.json` (while it runs),
`Play test` and `Exhumed check` (copies of the game for those). Versions before the rename kept
this folder in `UwEditorFree`; the first time this version starts, it moves that folder to the
new name.

Before writing a file, the editor keeps the old one as `NAME.EXT.BKP`. The first time the editor writes a file,
it also keeps the untouched original as `NAME.EXT.ORIG`. The mods window compares with
those originals.
