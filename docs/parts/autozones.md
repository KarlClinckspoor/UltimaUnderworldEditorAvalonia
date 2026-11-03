### Automatic zones

**Randomizer ▸ Scatter ▸ Automatic zones…** builds the zones from the levels instead of
painting them. It reads the levels as they are in the editor, with unsaved edits. It is made
for UW1; UW2's levels are read the same way but nobody has checked the result.

It works in steps:

- **Regions.** The open tiles of each level are grouped into regions. Inside a region the
  player can walk from any tile to any other and back: no step climbs more than the
  **highest climb**, either way. Each tile holding a door (any door, portcullis or secret
  door) is a region of its own. Lava tiles make regions of their own. Water joins regions,
  because swimming is normal movement in UW1, but the window counts it. Tiles where magic does
  not work are counted too (all of level 9 counts).
- **Edges** join the regions. A **drop** goes down more than the highest climb; it is free and
  one-way. A **climb** is the other way: it needs the jump spell up to the climb set **with the
  jump spell**, and levitation above that. A **jump** crosses a straight gap of lower ground:
  free up to **Gap jumps** tiles (2), with the jump spell up to the next number (3). The far side
  may be no higher than the near side plus the highest climb. **Diagonals too** also tries the
  diagonals; a diagonal tile counts as 1.4. A **door** edge needs what the door's lock needs
  (key, picking, bashing), or the lever that opens it. Walking into lava needs
  `hazard:lava`, a placeholder for now. Ceilings are ignored.
- **Teleports.** Every trigger the player sets off whose chain reaches a teleport trap makes
  a one-way edge to the square it lands on. Those that change level are **stairs**. When the
  stairs go both ways, there are two edges.
- **Levers.** A switch holding a use trigger is a lever. The window works out from where it can
  be reached: the hand reaches 1.5 tiles away, 3 floor steps above the feet and 1 below; the
  pole 2.5 tiles, 6 steps up and 2 down. A door a lever opens needs `zone:N`, N being the
  lever's zone. A lever the hand cannot reach also needs `item:pole | spell:levitate`.
- **Mechanisms** are the trigger chains that change the world: terrain changes, deleted and
  created objects, special traps, variables and doors closed or opened by walking. Each gets a
  flag name, such as `flag:terrain_L3_971` (level 3, trigger 971). They are listed in the
  report but not applied yet: a wall that a trap removes stays a wall for now.
- **Zones.** Regions that reach each other for free (in both directions, maybe by a round
  trip) become one zone. A door tile goes with a zone next to it. With **Cut at level
  changes** on, stairs never join zones, so each zone stays on one level. **Fold empty dead
  ends** puts a pit or ledge that has nothing in it and only one neighbour into that
  neighbour's zone. Zones are named after the painted zone most of them lies in, else by what
  they are (`L5 ledge at (2, 10)`). Key items become what each zone grants, as **Add them to
  the zone's grants** does. The start is the new-game square. The goal is the painted goal
  zone, or the largest zone of the last level.

**Compare with** picks the painted zoning to compare with: Karl's map shipped with the editor,
the zone file in `DATA`, or nothing.

- **Build** builds the zones and shows the report.
- **Report** shows it again. It has the counts, the solver's result (also with
  `hazard:lava` assumed, to show what blocks next), the comparison, the levers out of reach,
  the biggest drops and the mechanisms.
- **Solver report** shows the solver's own report: spheres, route and blocked links.
- **Open in the Zones window** loads the automatic zones into the Zones window to look at
  them in the view and edit them. Saving there asks for a file name and suggests
  `uw1-zones-auto.json`. The painted `uw1-zones.json` is never written unless you choose it.
- **Export JSON…** saves the automatic zones as a zone file.

Fall damage is not a requirement: no drop in UW1 kills a new character (the worst fall does
about 8 damage).
