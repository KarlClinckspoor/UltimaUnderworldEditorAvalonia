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
