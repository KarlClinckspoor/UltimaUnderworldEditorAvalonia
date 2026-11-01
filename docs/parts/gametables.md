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
