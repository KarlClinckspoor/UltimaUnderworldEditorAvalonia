# Uniform progression: paragraphs for the manual

Two additions, one for each existing section. Neither section changes its heading, so the
windows' `ManualWindow.Attach` names stay "Swapping items" and "Swapping monsters".

## For "### Swapping items"

Insert as the first bullet of the list (before **What trades with what**):

- **Order:** **Random** (at first) moves the objects within the choices below. **Uniform
  progression** hands out the weapons, armour, spell scrolls and wands by strength: the
  weakest of each kind go to the first level, the strongest to the last. Each kind is sorted
  on its own. A weapon's strength is its highest damage (slash, bash or stab, from
  OBJECTS.DAT) plus its enchantment's level; ammunition counts its missile damage, and a
  launcher the damage of the ammunition it fires. Armour counts its protection plus its
  enchantment's level. Spell scrolls and wands count the spell's circle (UW1's spells without
  runes count 9; a wand without a spell 0). An enchantment's level is its degree (Accuracy,
  Damage, Protection, Toughness: 1 to 8; UW2's weapon ones 1, 3, 5, 7), a spell's circle, or
  4.5 for a curse or an effect without degrees. The levels are walked in order: UW1 from level
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

Add to the **Formula** paragraph at the end of the section:

Under Uniform progression the formula shows the strengths and the walk: level ℓ with n_ℓ
objects of a kind takes ranks r_ℓ … r_ℓ + n_ℓ − 1 of the sorted list.

## For "### Swapping monsters"

Insert as the first bullet of the list (before **Swap**):

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
  elementals option hold as before. A creature the game put on water or lava it could not be
  put on keeps its spot. **Swap**, **Target** and **Spread** are greyed while Uniform
  progression is chosen. The report lists, for each class and level, the count and the
  strengths before → after.
