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
