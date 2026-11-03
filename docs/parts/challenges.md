### Named challenges

**Randomizer ▸ Named challenges…** changes the whole game by a rule: no food, no weapons,
every door locked, and so on. Tick one or more challenges. **Preview** shows what would
change, challenge by challenge and level by level, with the first few examples. **Apply**
makes the change: one Edit ▸ Undo step per level. **Undo challenges** puts everything back as
it was. Nothing is written to the game until you save. The same seed gives the same result.

A challenge covers every object of the levels: on the floor, in containers, carried by
creatures, and the templates inside create-object traps (or the traps would make the objects
again). It also covers the loot creatures get when they die or first trade (OBJECTS.DAT's
creature table) and, in a saved game, the player's inventory. Quest items are never touched:
keys, talismans, Caliburn, the Book of Honesty, Underworld II's quest items and the like.

The challenges:

- **Allergic to magic**: weapons, armour, rings and crowns lose their enchantments; spell
  scrolls become blank; wands lose their spells, except the plot's (Ultima Underworld's
  bullfrog wand, Underworld II's wands of Telekinesis and Light). The runestones and the rune
  bag go. In a save, the runes in the bag and on the shelf go too.
- **Bones rise**: a skull and two bones (or piles of bones) lying within a 2 × 2 square of
  tiles rise as a hostile skeleton, on the skull's tile. Each object is used once. Garamon's
  bones stay.
- **DMD mode**: creatures without a conversation turn hostile, and every creature becomes
  powerful. The special creatures stay as they are.
- **Dystopia**: every weapon and piece of armour is badly worn (quality 1-15).
- **Eternal darkness**: every torch, candle, lantern and light sphere goes, lit or not.
  Ultima Underworld keeps the Taper of Sacrifice. The tiles' own light stays.
- **Fasting**: only part of the food stays, picked at random. Set the share with **Food
  remaining**. Fish counts as food; drinks, mushrooms and plants do not.
- **Ghost town**: every creature without a conversation goes, except the special creatures,
  and so do the creature templates inside traps. What a creature carried lies on the floor
  where it stood.
- **Hoarding**: food, weapons and armour leave the floors and chests. Ordinary monsters
  (no conversation, not special) and traders carry them, spread evenly. Goods stay on their
  own level when it has someone to carry them; otherwise they go to the nearest level that has
  (in Underworld II, in the same world). With Ghost town, only traders are left to carry them.
- **Illiterate**: books, scrolls and maps go, except the ones the plot needs.
- **Lockdown**: every closed door that can be broken gets a lock no key opens. Pick it
  (harder deeper down, never too hard to pick) or break the door. Portcullises, secret doors,
  doors that cannot be broken, doors with a lock and doors a lever or trigger works are left
  alone.
- **Merchant**: all food goes to the traders (the characters who barter), spread evenly.
  Creatures no longer get food as loot.
- **Monk**: no weapons: swords, axes, maces, slings, bows, crossbows and their ammunition.
- **Naked**: no armour, shields, rings or crowns.
- **Old school**: the map item goes. Draw your own.
- **Peaceful**: every creature is friendly at the start.
- **Precognition**: every level's map is fully drawn. In DATA this reaches new games.
- **Ranger**: each melee weapon becomes a launcher or a stack of ammunition, half and half.
  The launchers are weighted from the least to the most damaging (10:3:2:1 at first; change
  them in the window). Ammunition is sling stones, bolts or arrows, 1 to 15 of them.
- **Starvation**: no food and no fishing poles. The Ylem runestones become other runes, so
  Create Food cannot be cast.
- **Utopia**: every weapon and piece of armour is in excellent shape (quality 63).
- **Vegetarian**: no meat, no fish, no fishing poles.
- **Vow of poverty**: no coins and no gems, and creatures carry no treasure. Ultima
  Underworld's emeralds stay (a puzzle needs them).

Some pairs cannot go together: Monk and Ranger, Monk and Hoarding, Utopia and Dystopia,
Peaceful and DMD mode, Merchant and Hoarding. The others are applied in this order: Ghost
town, Bones rise, Peaceful, DMD mode, Vow of poverty, Allergic to magic, Illiterate, Old
school, Eternal darkness, Naked, Monk, Ranger, Vegetarian, Starvation, Fasting, Utopia,
Dystopia, Merchant, Hoarding, Lockdown, Precognition.

The **Special creatures** tab lists the creatures Ghost town keeps, DMD mode leaves alone
and Hoarding gives nothing. At first it holds the fire elementals (Ultima Underworld) or the
brain creatures (Underworld II) and the Ethereal Void's creatures without a name.

Changes to OBJECTS.DAT's creature table open in **Tools ▸ Object tables** as unsaved edits;
**File ▸ Save all** writes them. OBJECTS.DAT lives in DATA, so it serves every save.

Limits:

- Conversations are not changed. Items a character hands over in a conversation still
  appear.
- Attitudes change in play: conversations and the game still make creatures friendly or
  hostile.
- A pole and strong thread still make a fishing pole.
- Loot made when a creature dies keeps the game's own quality.
