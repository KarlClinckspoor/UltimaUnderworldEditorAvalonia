### Automatic zones

*(Phase C: additions to this section. Replace the "Bashing in logic" item, the last sentence of
the "Mechanisms" item, and "Solver report"; add the rest after "Abilities and logic options".)*

- **Mechanisms** are now applied, see "The world over time" below.
- **Static solver report** (was **Solver report**) shows the solver without world changes:
  spheres, route and blocked links.

In **Logic**, bashing is no longer an option: fists bash any door that can be broken, so a
breakable door is always open to the logic. **The doors the shuffle must open** are now only
the doors that need picking, when lock picking is off. Two options are new:

- **Killing NPCs in logic.** An item that a named, non-hostile creature carries (a key, a
  talisman, the crystal splinter) can be had by killing it once its zone is reached. Off, such
  an item needs what a fact says (the quest that gets it); with no fact, it cannot be had. What
  hostile creatures carry is always free: the player fights them anyway.
- **Ring of Levitation needs lava protection** (on). The Ring of Levitation's area on level 7
  can be reached from (59, 43) by jumping, but that is an ordeal. On, the area needs lava
  protection (the dragon skin boots or Flameproof); off, it is one zone with that of (59, 43).

The pole and the Telekinesis spell (Ort Por Ylem) count the same: a lever out of the hand's
reach needs `item:pole | spell:telekinesis | spell:levitate`. A lever is only used from its
own side of a closed door, and a lever set in rock (behind a wall decoration) only once the
rock is opened.

#### The world over time

The solver now follows the world as it changes. When the player can set a mechanism off (its
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
`uw1-facts.json` and the quest facts `uw1-quests.json`. Facts can now also add a **link**
between the zones of two places, a **warp** (Gate Travel, the silver sapling), the **goal** and
what it needs, mark lever chains as **free** once their zone is reached (Karl: the level 4 lever
puzzle and all levers on level 5), or say what a mechanism **needs** besides its zone (the
Slasher's chamber needs the Key of Infinity). Facts can test the options as `option:magic`,
`option:kill_npcs`, `option:ring_needs_lava` and so on. The file's `notes` describe the format.

The **Report** now starts with the Phase C part: the solve and the goal, a table of the main
option sets (Magic, single-use items, picking, killing NPCs, the ring), the timeline sphere by
sphere (zones reached, items gained, changes set off and applied), the changes that close
things and how each is treated, the changes that open things, the return check, the facts'
links, warps and goal, the items NPCs carry and the items a trap creates. The rules are
glitchless.
