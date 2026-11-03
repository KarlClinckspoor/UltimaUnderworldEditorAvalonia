#### Abilities and logic options

(Additions to "### Automatic zones". Two sentences of the existing text change: walking into lava
now needs the lava ability, `spell:lava`, not `hazard:lava`; and the report no longer has a
second solve "with `hazard:lava` assumed".)

The window also decides what the logic may expect of the player. The **Logic** rows:

- **Magic.** Casting from runes counts. A spell can be cast once the rune bag and its runes
  are reached. Character level, mana and the casting roll are ignored.
- **Single-use items count.** Scrolls, potions and wands with charges count. Off, only items
  that keep working count: an enchantment worn, or an item enchanted to cast without charges.
- **Lock picking in logic, skill.** The solver may pick locks with this Pick Lock skill, if a
  lock pick is reached. A lock of difficulty Z can be picked when skill − 3Z + 30 is above 15
  (with retries; the pick does not break). Difficulty 14 needs skill 31, and 15 never opens.
  The Open spell picks with skill 45.
- **Bashing in logic.** The solver may break doors that can be broken.
- **Search skill** and **Retry until found.** For walls that a look opens (below).
- **Damage-trap tiles are walls everywhere.** Every tile holding a move trigger that hurts the
  player becomes a wall, not only those of the damaging maze.

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
counts any chance; off, the Search skill must be at least Z. Reveal works up to height 29.
Secret doors themselves open like any door: no search is needed once they can be reached.

**The facts file.** `docs/facts/uw1-facts.json`, shipped with the editor, holds what the levels
do not say: the dragon skin boots (Marrowsuck makes them from dragon scales and strong thread
for food; Sethar Strongarm gives the scales for rotworm stew; the stew is made from Lanugo's
recipe, a bowl, a dead rotworm, a mushroom and port), the orb, level 7's lack of mana, the
damaging maze, and notes on some hidden gates. Each fact names its source in the game.

**The damaging maze.** On level 7, every tile of the maze holding a move trigger that hurts
the player is a wall.

The **Report** now starts with the Phase B part: the solve with the chosen options and a table
of the main option sets (Magic, single-use items, picking and bashing, on and off); each
ability's sources and the sphere where it first holds; the links that need each ability; the
hidden gates and how they are passed; the walls; the dragon skin boots and where each part
is; and, when picking or bashing is off, **the doors the shuffle must open**: doors that only
picking or bashing opens, with what opening each would add.
