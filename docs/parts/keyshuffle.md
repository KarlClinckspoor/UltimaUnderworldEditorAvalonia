### Key item shuffle

**Randomizer ▸ Key item shuffle…** moves the key items to new places so that the game can
still be finished with the logic you choose. It works on Ultima Underworld 1 only, because
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

**Creature inventories** lets items carried by creatures without a conversation take part.
Their inventories become places too. **Everything else as filler** adds every other item the
item swap moves (weapons, armour, food, coins, lights …), and their places.

Some objects always stay where they are. The log lists them with the reason: items carried
by an NPC with a conversation, a create trap's template, objects a trap or trigger names,
objects that hold other objects or a trigger, the orbs, the moonstone (Gate Travel goes to
it), and the coins and food beside Shak and Zak.

**The logic** rows are the same as in the Automatic zones window, with one more:
**Require breaking the orb (level 7)**. With it on, finishing also needs Tyball's orb broken
with an orb rock. The goal is reaching level 9, through the ritual in the Slasher's chamber.

**How it works.** The key items, in a random order, are placed one by one. For each, the
solver of the automatic zones (with the world changing over time) finds the places it can
reach, counting every key item not placed yet as already held. One of those places is chosen
at random. The other items then fill the remaining places at random. At the end the shuffled
levels are solved again from scratch, with nothing counted as held. The goal must be reached,
and no one-way trap may strand the player with something he needs. If not, the next seed is
tried, up to **Retries** times. The same seed on the same levels gives the same shuffle.

**Doors with lock picking off.** When lock picking is not in the logic, a key item never goes
behind a door that only picking opens. If something the logic needs and that does not move
lies behind such a door, the door is unlocked as part of the shuffle, and the log says so.

- **Preview** computes a shuffle and shows its spoiler log. Nothing changes.
- **Shuffle** writes the shuffle on show into the levels (or computes one first). Items trade
  places whole: item, quality, flags, quantity or link, owner and enchantment. Each level is
  one undoable step, saved with the levels.
- **Undo shuffle** puts the levels back as they were, byte for byte. A level edited since is
  left alone; Edit ▸ Undo still steps back there.
- **Export spoiler log…** saves the log as a text file.

**The spoiler log** gives the seed and options, what moves and what stays, the doors
unlocked, the route (spheres, when each talisman and key comes) and, sphere by sphere, where
each key item went: level, square, the container or creature holding it, where it was, and
what the place held before.
