### Shuffling item properties

**Randomizer ▸ Item property shuffle…** changes what items are without moving them. Weapons,
armour, food, lights, tools, runestones, wands, spell scrolls, potions and rings stay where
they lie (on the floor, in a container or a creature's inventory) but may become something
else. Each object keeps its owner: food belonging to a rat that becomes a lantern is the rat's
lantern. (The shuffle never moves objects from one spot to another, so it needs no ownership
option; the item swap has one.) Choose the settings in the tabs, press **Preview** to see the report, then **Shuffle**.
**Undo shuffle** puts back every object the shuffles from the window changed; each shuffle is
also one Edit ▸ Undo step per level, and nothing reaches the game until you save the levels.
A level edited after the shuffle is left as it is (its old records could undo those edits
halfway and break its object lists); the report names it, and Edit ▸ Undo steps back there.
The same seed on the same levels gives the same result. The settings are kept between runs
(`item-shuffle-uw1.json` / `-uw2.json` in the settings folder); the item swap keeps its own.

**Formula** shows the math the settings give, in symbols, and follows every change: for
example `quality' = clamp(q + U{−10..10}, 1, 63)` for a quality spread of 10, or
`p = clamp(8.5/100 × (1 + 30/100 × d), 0, 1)` for an enchantment's chance at depth d.
`Cat(a 50, b 25)` is a weighted pick (a twice as often as b), `U{a..b}` an even pick of a whole
number, `N(μ, σ)` a normal draw. The notation is explained at the end of the box.

When the window grows, the formula and the report get the room; the tabs keep their height
and scroll. In a short window the tabs shrink and scroll too.

**Every setting starts from the world**: the relative weights are the counts in the levels
(the most common kind is 100), the chances, means, deviations and depth slopes are fitted to
the enchantments the levels hold, and the bars and curves follow them. Each tab has **From
the game**, which sets its values to what the levels hold now (after your edits); **All from
the game** does every tab. **World report** writes what the game's levels hold, as saved on
disk: the items by kind, the enchantments by kind and by level, the other effects and curses,
the wand, scroll and potion spells, the rings, and the qualities.

Weights are relative, from 0 to 100: a weight of 0 removes that kind. Never touched:
Caliburn (UW1's shiny sword), Altara's jewelled dagger (UW2), talismans, crowns, dragon skin
boots, fraznium armour, UW2's Guardian signet rings, objects that are templates inside traps,
and the ignored objects.

- **Weapons:** weights for axes, swords and daggers, maces and clubs, and ranged weapons with
  their ammunition; the share of launchers among the ranged objects (the rest become arrows,
  bolts or sling stones, never enchanted); the quality spread (± around each object's own
  quality). Then one row per enchantment kind (Accuracy, Damage) with its chance, the mean
  strength (1 = Minor) and the deviation of a normal draw. A weapon is enchanted with the sum
  of the kinds' chances (at most 100%), and the kind is then picked in proportion to its
  chance. **Depth: chance** raises the chance (double on the
  deepest level at 100); **Depth: strength** moves the mean strength towards the strongest
  (all the way on the deepest level at 100). Level 1 is never changed; in UW2 the depth is the
  level within its world. **Special weapons** (UW2 only: LifeStealer, Undead Bane, Fire Doom,
  Stone Strike, the two door openers) are put on that many random melee weapons.
- **Armour** (gloves, boots and shields included): weights for leather, mail, plate and
  shields; a piece keeps its slot and changes material. Protection and Toughness rows as for
  weapons, with their two depth sliders. **Other effects** are the worn effects without
  degrees (Valor, Leap, Stealth, Missile Protection, Flameproof, regeneration…): the pieces
  they can go on, the chance a piece gets one instead of Protection or Toughness, the share
  **cursed instead**, the curse strength bars, and a bar per effect. **Special armour** puts
  exactly that many Flameproof pieces on top.
- **Food, lights, tools:** a weight per food and per light source (lit ones stay lit), and the
  least number of threads (5), poles (3) and fishing poles (3). When there are fewer, random
  food and lights lying on the floor become them.
- **Runestones:** a weight and a minimum (1) per rune. Minimums are met by changing runestones
  of the kinds with the most to spare; the report says when there are too few runestones.
- **Wands:** bars for the number of charges (1-30) and for the spell's circle. A spell is
  picked by circle, then evenly among that circle's spells (UW1: six to a circle, and a ninth
  bar for Mass Paralyze, Acid, Local Teleport, Mana Boost and Restore Mana, which have no
  runes; UW2: eight to a circle).
- **Scrolls, potions:** circle bars for spell scrolls and for potions. Potions can be limited
  to spells some potion already holds; in UW2 a potion takes the colour the game uses most for
  its new spell, since the colours tell the effect. Readable scrolls, maps and potions with
  special effects stay.
- **Rings:** a weight per ring, the chance a ring carries an effect, the share cursed instead,
  and a bar per worn effect. A curse of strength N does N d8 damage, never taking the wearer
  below 3 hit points, every time what is worn changes.
- **Ignored:** the objects the shuffle leaves alone: pick them from the list of everything it
  touches (with a filter), or ignore the object selected in the editor. Wands enchanted
  directly, which never run out, are ignored at first: UW1's bullfrog wand (it works the level
  4 puzzle), UW2's slightly worn wand of Telekinesis in the Scintillus Academy and its wand of
  Light. The list is kept in `item-ignore-uw1.json` / `-uw2.json` in the settings folder; the
  item swap has its own.

**Custom distributions.** Each enchantment row has **Custom…**: bars for the strength (a
weight per strength) and, for the chance by depth, bars per level (UW1) or a curve over the
levels of a world (UW2). The quality rows have **Custom curve…** (a weight per quality 1-63).
Each replaces its sliders while ticked, and editing it ticks it. They start from what the
sliders give (**From the sliders**), or from the game for quality. On a curve, click to add a
point, drag to move one and right-click to remove one; the ends stay at the ends. The curve
never swings past its points, so no chance goes below 0. Drag bars up and down (the effect
bars left and right); sweeping across sets each bar crossed.

Spells and other enchantments already on objects stay unless **Keep spells and other
enchantments** is off; an object whose link leads to other objects (contents, a spell object)
never has its enchantment replaced.

### Swapping items

**Randomizer ▸ Item swap…** only moves the existing objects around: nothing is made or
removed, so every item keeps its count, and no weights apply. Its settings are its own, kept in
`item-swap-uw1.json` / `-uw2.json` in the settings folder, apart from the shuffle's. The window
has three tabs: **Settings**, **Ignored** and **Starting equipment**. **Preview** writes the
report without changing anything, **Swap** applies it, and **Undo swap** puts back every
object the swaps from the window changed. Each swap is also one Edit ▸ Undo step per level,
and nothing reaches the game until you save the levels. The same seed on the same levels
gives the same result.

The objects trade places whole: item, quality, enchantment, quantity and owner (but see below).
Charged wands swap among themselves with their spells and charges, since the charges live in
an object inside the wand; the few objects holding triggers stay. Never moved: Caliburn,
Altara's jewelled dagger (UW2), talismans, quest items, objects that are templates inside
traps, Garamon's bones, the ignored objects and, while kept, the starting equipment.

- **What trades with what:** **within each kind** (a sword stays a weapon) or **across all the
  ticked kinds** (a sword may land where a torch was).
- **Where objects may go:** **within each level**; **within ± N levels** (in UW2 only within
  the same world); **within each world** (UW2: Britannia, the Prison Tower, Killorn Keep and
  so on); or across the whole game. Ultima Underworld has one world, so there "within the
  world" means every level.
- **An item put in a spot takes the spot's owner** (on at first). On UW1's level 1 the cheese
  and the meat at (22, 8) belong to the giant rats; a lantern swapped into their place belongs
  to the rats, and the cheese, wherever it goes, takes the owner of its new spot. This only
  happens where the owner field means "belongs to" on both items: the game reads it so only for
  items whose can-own bit is set in COMOBJ.DAT. For other objects the same field is something
  else (a key's lock, whose bones they are, a text number), and those keep their own. Off,
  every object carries its own owner, as before.
- **Kinds taking part:** weapons, armour, food, lights, tools, runestones, wands, spell
  scrolls, potions, rings, **bones and skulls** (off at first) and **other scenery** (off at
  first). Bones and skulls are the skulls, bones and piles of bones you can pick up; Garamon's
  bones (UW1, level 8, the pile whose owner field is 62) never move, since burying them is part
  of the plot. UW2's one pile of bones with the same mark, in the Tomb of Praecor Loth, stays
  too. Other scenery is the rest of what you can pick up from the scenery range: broken weapons,
  pieces of wood, debris, plants, UW1's dead rotworm, UW2's lump of wax. The pole is a tool.
- **Enchantments:** **After the swap, swap the enchantments among objects of the same class**
  (off at first). The enchantments then trade places among the melee weapons, among the
  launchers, among the armour (shields included) and among the spell scrolls, with the same
  scope; each class can be left out. Every object of a class takes part, plain ones too, so an
  enchantment may move to a sword that had none. Ammunition, Caliburn and the quest armour stay
  as they are.
- **Ignored:** the objects the swap leaves alone, picked from the list of everything it
  touches (with a filter), or the object selected in the editor. The list is the swap's own; it
  starts as a copy of the shuffle's list, which holds the wands enchanted directly at first
  (UW1's bullfrog wand, UW2's slightly worn wand of Telekinesis and wand of Light).
- **Starting equipment:** while **Keep the starting equipment where it is** is ticked (at
  first it is), these objects stay and nothing is put in their place. At first the list holds
  what the game gives the Avatar on level 1, as far as the swap could move it. UW1: the sack
  beside the start square, on (33, 3): the dagger, bread, fish, torch and apple. UW2: in the
  Avatar's quarters, the sack, leather vest and boots on (20, 49) (with thread, a torch and a
  dagger in the sack), and the closet behind the secret door at (19, 50): the pack (dagger,
  water, bread, torch), the sling and its stones, the Ort, Bet, In, Jux and Sanct stones and a
  hand axe. Add or remove objects as for the ignore list; **The game's list** sets it back.

**Formula** shows the math in use with the settings: the pool, the groups, the permutation
(`π ~ a uniform random permutation`; for ± N, the passes of trades that keep every object
within N levels), the owner rule and the enchantment swap. It follows every change. When the
window grows, the formula and the report get the room; the tabs keep their height and scroll.

