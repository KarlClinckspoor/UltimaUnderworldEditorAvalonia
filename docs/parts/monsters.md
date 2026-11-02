### Swapping monsters

**Randomizer ▸ Monster swap…** makes the monsters trade places. Each spot keeps its position,
home, goal and (by default) whatever lies in its inventory, and takes another monster's kind,
hit points, powerful flag and attitude. **Preview**, **Swap** and **Undo swap** work as in the
item tools; each swap is one Edit ▸ Undo step per level.

- **Swap** within each level, within each world (UW2), or across all levels.
- **Strength**: the game's own **experience value** for the kind (the default), hit points,
  damage per round (the three attacks weighted by how often each is used, damage plus
  strength / 5, and attack spells by how often the creature casts: missiles at their
  OBJECTS.DAT damage, area spells counted as 12), **combined** (√(hit points × (1 + armour /
  8) × damage), so neither side outweighs the other), or none (uniform).
- **Target**: at 0 each spot keeps its difficulty, taking a monster about as strong as the one
  that was there, which already follows the game's depth curve; at 100 stronger monsters go
  deeper; between, a blend.
- **Spread**: at 0 the strongest monster goes to the hardest spot, strictly; at 100 the swap
  is uniform.
- **Keep the hit points at the spot**: a golem put where a 5-hit-point worm was gets 5 hit
  points, and the worm gets the golem's.
- **Carried items go with the monster**: otherwise they stay with the spot, keeping each
  level's loot where it was. Monsters carrying something then only swap within their level.
- **Leave the fire elementals alone** (UW1) or **Leave the brain creatures alone** (UW2): on
  at first. Those creatures neither move nor take another's place.
- **Powerful**: counted in every measure (armour × 5/3, +7..12 attack skill, +4..15 damage,
  experience × 1.5..3). **Roll it again** gives each moved monster a chance, raised with depth.
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
- Within their level only: creatures the game calls by race (UW2's liches, the trilkhun (race 0xB),
  level 1's guards, level 4's bloodworms; UW1's men on Tybal's level), creatures
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
