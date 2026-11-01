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
