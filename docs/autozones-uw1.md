# Automatic zones, Phase A: findings on UW1

Built from the shipped UW1 levels with the default options: highest climb 2 floor steps (4
with the jump spell), gap jumps of 2 tiles (3 with the jump spell), no diagonal jumps, cut at
level changes, empty dead ends folded. The code is `Core/Randomizer/NavRegions.cs` (regions,
edges, levers, mechanisms) and `Core/Randomizer/AutoZones.cs` (zones, comparison, report).
The tests are `tests/UwEditor.Core.Tests/AutoZonesTests.cs`.

## Counts

| | |
| --- | --- |
| Regions | 685: 197 door tiles, 59 lava, 33 with water, 6 with no-magic tiles |
| Region edges | 1717: door 656, climb 267, drop 250, jump 195, walk 175, portcullis 58, secret 56, stairs 48, teleport 12 |
| Zones | 240 (54 empty dead ends folded into their neighbour; without folding there are more than the 254 ids) |
| Zone links | 496: drop 132, climb 132, walk 103, door 56, jump 36, stairs 22, portcullis 9, teleport 5, secret 1 |
| Placements | 197 (from the key items) |
| Mechanisms | 136: terrain 33, create 32, delete 22, variable 19, hack 16, door 13, quest 1 |
| Levers | 37 switches holding a trigger; 4 out of the hand's reach |

Regions and zones per level: L1 82/21, L2 54/16, L3 61/19, L4 77/16, L5 68/22, L6 111/36,
L7 151/66, L8 77/43, L9 4/1.

## Facts from the game's code

- **Fall damage** (`motion/PHYSICS.C` phys_affect_player, `motion/MOTION.C` do_zbounce). On
  landing, the vertical speed becomes v/15 upwards and the impact is (v/15) × (15 − bounce).
  The player's bounce is 5. The damage is impact >> 8. It is doubled when the landing
  bounces, that is when v/3 is at least 0x8D. A successful Acrobat check against twice the
  damage cuts it to damage × (30 − Acrobat) / 30. Only damage above 3 is dealt. Gravity is −4
  a tick (−2 with Leap), and a jump starts at 0x263. The editor estimates the landing speed by
  assuming a standing jump peaks at the highest climb (2 steps), so v = 0x263 × √(steps / 2).
  That gives 0 damage up to 3 steps, 4 for 4–7 steps, 6 for 8–12 and 8 for 13–15. A new
  character has 30 + Strength / 5 hit points, minus 6 to 11 at the start (`game/SKILLS.C`,
  `game/CHARGEN.C`). So **no drop kills a fresh character**. The worst drop in UW1 is 13 steps,
  on L1 at (47, 54), from the grey goblin area: about 8 damage. Of the 250 drop edges, 118
  (8 to 12 steps) do about 6, 95 (4 to 7 steps) about 4, and 36 (3 steps) nothing. *Not settled:* the jump's real peak height (the z integration in `space_to_motion`
  uses several unit scales). If the jump peaks higher, falls hurt less than this.
- **Reach** (`ui/INTERACT.C` InPickRange and BlockingTerrain; `game/PLAYDATA.C`
  init_spells). Using or picking up is allowed within a squared distance of 0x90 eighth-tiles
  (1.5 tiles), or 0x190 (2.5 tiles) while the pole is in use (`obj/USEITEMS.C` UseUtil and
  UsePoleOn: only a switch is used from afar). Height: the object may be at most 24 z units
  (3 floor steps) above the feet and 8 below. With the pole that is 48 and 16. For the hand,
  a tile on the line that is lower than the feet less one step blocks, and so does crossing
  another terrain class.
- **Who sets a trigger off** (`event/TRIGGER.C` UseTrigger). The player needs flag bit 11.
  UW1's step-on trigger never fires. A teleport moves only the player.
- **Level 7's magic orb** (`game/GAMEWRAP.C` do_level_hacks, `obj/USEITEMS.C`
  TybalsOrb_seg040_A12). Level 7 has no no-magic tiles. While `player->orb` is 0, arriving on
  level 7 keeps the maximum mana aside and sets mana and maximum mana to 0. Leaving puts the
  maximum back, with a quarter of it as mana. The orb rock (0x112) used on the orb (0x117,
  #820 in Tyball's crib, L7 (54, 9)) destroys the orb. It sets `player->orb` and gives the
  mana back, and every creature with whoami 0xE7 (Tyball) is left with half its hit points
  plus one. While the orb stands, Tyball's guards (race 0x13) close in and do not cast
  (`critter/AI.C`). The orb rocks lie on L8: #521 at (58, 59), #523 at (55, 48), #795 at
  (59, 46). In the requirement language, "no magic on L7" is `item:orb_rock` (until the orb is
  broken, `spell:` terms do not hold there). That is a Phase B/C fact; Phase A only marks it.
  UW1's no-magic tiles are 16 on L6, at (5..8, 17..18) and (3..6, 27..30). Level 9 refuses
  every spell (`combat/SPELLS.C` do_spell).

## Karl's three notes

- **Level 7, holding a special item.** It is the **crystal splinter** (0x111; #903 lies in
  the prison cells with key 23, L7 (10, 51)). Move trigger #887 at (20, 42) runs inventory
  trap #896. In UW1 that trap ends the chain unless the player carries item quality × 32 +
  owner = 0x111 (z 1: at least one). Then pick-up trigger #849 runs at (21, 40), text trap
  #893 prints "There is a high pitched keening noise as the rocks part.", and change-terrain
  trap #446 turns (20, 41) into open floor at height 12. Delete trap #839 removes the move
  trigger. So **walking past (20, 42) with the splinter opens the rock into the tombs**. That
  is mechanism `flag:terrain_L7_887`, with conditions `item:crystal`. It is the only
  inventory trap in UW1. From the tombs side, look trigger #829 (in an apple) opens the same
  square (`flag:terrain_L7_829`).
- **Level 7, the orb.** See above: the orb is a mana drain on the whole level, not no-magic
  tiles.
- **Level 3, lever #972** (at (52, 13), z 112, reached from (52, 12), floor 12: within reach).
  Its use trigger #971 runs a chain at (44, 12):
  1. Change terrain #970 turns row (44..49, 12) into rock at height 12. This is a dam on the
     south shore of the lake, which is water at height 11.
  2. Text #584 prints "You hear the roar of tons of water falling to your west."
  3. Move trigger #968 runs change terrain #969 at (44, 8). It turns (44..49, 8..11), the
     pool, into dry open floor (texture 0) at height 4.
  4. Move trigger #549 runs change terrain #550. It opens (46, 7), which was rock, at height 4.
  (46, 7) joins the drained pool to the room at (45..48, 4..6), height 4. So the lever drains the pool and opens
  the way to the **sword blade area**. The blade itself (#882) lies further along that area,
  at (58, 9). One then drops 8 steps from the floor at height 12 on
  either side into the pool. This is mechanism `flag:terrain_L3_971`. Until Phase C applies
  it, no link leads into the sword area.

## Levers out of reach

All four are on L4, in the lever puzzle room (57..61, 30..34), whose floor is at height 2.
The room is entered from a ledge at (56, 33), at height 8:

- #526 at (57, 31), z 72. The pole reaches it from the ledge. It raises (60, 31) to height 5.
- #811 at (60, 30), z 88. From the raised (60, 31) it is exactly at the pole's reach (48
  above). It raises (60, 33) to 7.
- #853 at (61, 33), z 104. With the pole, from the raised (60, 33). It raises (58, 33) to 9.
- #799 at (58, 34), z 120. With the pole, from the raised (58, 33). It opens
  (57..58, 28..31) at height 8: the way north to Karl's zone 41 (the alcove after the lever
  puzzle).

So the puzzle is a chain: each lever raises the floor from which the pole reaches the next
one. Climbing onto a raised tile needs a climb of 3, that is the jump spell, or levitation.
Phase A can only say `item:pole | spell:levitate` for #526 and `spell:levitate` for the
other three. Phase C, which applies the terrain changes, will find the pole route.

## How far the solver gets

With the default solver options, it reaches 99 of 240 zones over 17 spheres, on levels 1 to
8. The goal (L9) is not reached. What blocks it:

- `hazard:lava` blocks 43 links (Phase B: lava boots or fire resistance).
- Keys 23 and 27 (on Tyball, behind lava) and picks of difficulty 10, 13 and 14 (lock picking
  is off by default).

With `hazard:lava` assumed, it reaches 201 zones over 23 spheres. Then only pick:10 and pick:14
block it, and `flag:var31_is_470` on L5: lever #667's check-variable trap #650 toggles the door
at (3, 27) when game variables 31, 32 and 33 read 7, 2 and 6, packed. The three levers #669,
#668 and #658 each add 1 to one of them. That is the Kneenibble code of Karl's zone 49.
`flag:var27_is_38` on L4 is the same kind of thing: switch #747 toggles the massive door at
(13, 44) of the "plate secret area" when variable 27 is right.

29 zones have no link into them, because only a mechanism, a quest or a spell gets there:

- the sword blade area (lever #972);
- the moonorb area (L2: Gate Travel);
- the L3 secret bandit room;
- the L3 and L4 secret alcoves, the L7 tombs, and two parts of L7 opened by look triggers on
  special texture objects (secret walls that Search opens);
- the L4 alcove after the lever puzzle;
- small sealed ledges, and a sealed 4 by 4 room at about (30, 30) on L1 to L4;
- the L8 Slasher chamber: the door is a texture object that the Key of Infinity opens. Its
  open trigger is set off "with how 7", `obj/USEITEMS.C`.

The goal zone itself (L9) is reached only after the talismans are thrown into the lava,
which is a Phase C fact. The painted goal zone 87 has no tiles, because Karl's map8 is not
painted. So the auto goal is the largest zone of L9.

## Differences from the painted map

- **Zone 2 is split.** Karl's zone 2, the main areas of L1 to L6, falls into 38 auto zones of
  3 tiles or more. Each level is its own zone because of the cut at level changes. Inside a
  level, parts are joined only one way: drops, climbs above 2 and jump-spell gaps. With the
  cut off, the stairs join the levels' main areas.
- **L7: zone 2 with the spider maze (60) and guard room 1 (59).** The auto zones join them.
  Portcullis #734 at (4, 29) has no lock, and in UW1 any unlocked door, a portcullis too, opens
  by hand (`obj/USEITEMS.C` UseRect, checkLock returns 1). Portcullis #735 at (7, 35) stands
  open. Move trigger #656 closes #735 behind the player (`flag:door_L7_656`), but closing is a
  mechanism and is not applied in Phase A.
- **The damaging maze (70) and Tyball's crib (71)** are one auto zone. The maze's damage traps
  are not movement rules.
- **Secret walls.** Karl's free secret links 47, 90, 92, 121 and 122, and 97 (`zone:64`) are
  terrain changes set off by look triggers on special texture objects, often with a Search
  difficulty in z. The auto zones list them as mechanisms (`flag:terrain_*` plus
  `flag:delete_*`), not links.
- **Painted links with no auto counterpart (16):**
  - 30 (moonorb);
  - 39 (the waterfall alcove: found only as terrain mechanism #537/#538);
  - 40 (the sword, above);
  - 58 (the lever puzzle, above);
  - 101 (the crystal, above);
  - 107;
  - 118 (`key:27` portcullis to Arial's prison: the auto map has key:27 links elsewhere on L7);
  - 137 and 138 (the Slasher chamber door);
  - 139 (the ritual);
  - and the secret links.
- **Auto links the painted map lacks (24 between painted zones).** These are mostly:
  - drops into L7's northern lava river (79) and climbs out of it with levitation;
  - jump-spell climbs and gaps on L7;
  - the guard room 2 doors (`key:23`);
  - the door between the lava river and the cavern of dead things (`bash`).
- **Unpainted tiles.** 3136 auto-zoned tiles have no painted zone, mostly L9 (3010 tiles).

## Not settled

- The jump's peak height, so the exact fall damage, and the jump-spell climb (taken as
  twice the climb, since Leap halves gravity).
- Gap jumps: a straight run over lower ground, by tile counts. The real running jump depends
  on speed and the player's radius.
- The edges of door tiles take each door's lock as it is now. Door traps that put a locked
  lock on a door (24 of 27 in UW1) are mechanisms, so "the portcullis closes behind you" is
  not modelled.
- Trigger chains with a check-variable trap are given a `flag:varZ_is_V` term, marked
  `todo`. Which levers set the value is left to Phase C.
- The item token of 0x117 is `item:orb`, which also covers the crystal balls on L1, L2 and
  L4.

# Phase B: abilities and logic options

Built with the default options (Magic on, single-use items off, lock picking off, bashing on,
Search 0 with retries). The code is `Core/Randomizer/AbilitySources.cs` (the spell table and
the items), `AbilityZones.cs` (walls, hidden gates, where magic fails, placements, solves,
report) and `Facts.cs` with `docs/facts/uw1-facts.json`; the tests are
`tests/UwEditor.Core.Tests/AbilityZonesTests.cs`.

## Counts

686 regions, 1751 region edges (34 of them hidden-gate edges), 242 zones, 530 links, 293
placements (280 read from the levels, 13 from the facts). 18 hidden gates. 20 maze tiles
walled. 3 region edges start on no-magic squares, 147 on level 7.

## The requirement language

`spell:X` now means "ability X is available here": `cast:X` (the runes of a spell giving X
and the rune bag; Magic on) or `use:X`. `use:X` is `worn:X` (an enchantment worn, which works
everywhere), `perm:X` (an item that casts without charges) or, with the option, `once:X`
(scroll, potion, charged wand). `search:Z` is a look of height Z. The solver keeps the old
meaning (`AllowSpells`) unless `SolverOptions.AbilitySources` is set, so painted zonings solve
as before. `hazard:lava` is gone: lava is `spell:lava`.

## Sources per ability (spells from combat/SPELLS.C spells[]; items read as obj/USEITEMS.C and game/PLAYDATA.C do)

| Ability | Spells (runes) | Items |
| --- | --- | --- |
| levitate | Levitate (Hur Por), Fly (Vas Hur Por) | ring of Levitate #683, L7 (44, 44), worn; two Fly potions on L2 (#772 carried by a mountainman, #872 in the potion maze room), single-use |
| jump | Leap (Uus Por) | ring of Leap #914, L4 (35, 5), worn |
| waterwalk | Water Walk (Ylem Por) | none |
| lava | Flameproof (Sanct Flam) | Flameproof potion #522, L7 (46, 2), in the damaging maze, single-use; dragon skin boots (crafted, below) |
| reveal | Reveal (Ort An Quas) | three scrolls: L5 #823, L6 #603 and #617, single-use |
| open | Open (Ex Ylem): a lock pick of skill 45 | none |
| light | Light, Night Vision, Daylight | three scrolls; helmet of Magic Lantern #474, L7, worn |

No UW1 wand carries an ability (the ten charged wands hold attack spells, Name Enchantment,
Smite Undead; wand #555 holds the bullfrog spell). The leather boots of Slow Fall (#595, L4)
only soften falls. Light gates nothing. Water walking gates nothing either, since swimming is
free in the Phase A rules. Flameproof gives the fire resistance bit (0x08) that cancels the
lava's damage (combat/DAMAGE.C check_res, motion/PLAYMOVE.C parse_effect: 1 fire damage one
time in five per update). First sphere with the defaults: water walk 3, levitate 4, jump and
reveal 5, lava 6 (the boots; Flameproof's runes come later), open 7.

## Where magic does not work (combat/SPELLS.C do_spell, obj/USEITEMS.C checkSpell)

do_spell refuses on a no-magic square and on level 9, whoever casts. A scroll, a wand, a potion
or an item's spell goes through the same path (inanimate_spell with the player as the caster,
tested at the player's square), so **no-magic tiles stop scrolls, wands and potions too**. A
worn enchantment does not go through do_spell (FixPlayerEquips calls player_affected_by), so it
keeps working. An active spell cast before stepping onto such a square is not ended there
either; the automatic zones take the strict reading (only `worn:` sources on links starting
there). On L7 the orb leaves the player with no mana, so `spell:X` there reads
`(cast:X & flag:orb_destroyed) | use:X`; the facts grant `flag:orb_destroyed` in the orb's zone
(Tyball's crib) with `item:orb_rock`. The solver reaches it in sphere 15.

## Hidden things: Reveal and the Search skill

- **Secret doors** (0x147) are ordinary doors drawn like walls: UseRect opens them like any
  other, and nothing searches for them (only critters refuse to use them, critter/PATHFIND.C).
  Reveal does not act on them.
- The hidden part is **look triggers** (0x1A3) in wall decorations (special tmap objects): the
  chain opens the rock square where the secret door stands (change terrain) and deletes the
  decoration. 18 such gates in UW1; 12 of them have height 0.
- **The check** (event/TRIGGER.C UseTrigger): only a look trigger whose z is above 0 is
  checked, with skill_check(Search, z) > 0, that is Search − z + rand() % 31 > 15
  (game/SKILLCHK.C). **With Search 0, a height of z passes with chance (15 − z) / 31** per look,
  never above 14. A failed look returns before anything is changed or marked, and looking works
  from any distance, so **retrying is allowed** and Search 0 eventually passes every UW1 gate
  (the highest is 10: 5 in 31). A sure pass needs Search z + 16 (26 for height 10).
- **Reveal** (combat/SPELLS.C sp_true_sight) sets off look triggers of objects within 2 squares
  of the square 4 ahead, 3d4 times, with Search raised to 45: sure up to height 29.
- The back access of **Tyball's crib** (#349 at (42, 10)) opens with look trigger #351 of
  height 0: any look, no Reveal, no skill. Karl's links 47 (height 7), 90, 92, 111, 121, 122 are
  hidden gates; the gate behind 92 (into the tombs, #343) and those of 121 and 122 are looked at
  from inside the tombs, so the tombs are entered only through the crystal mechanism (Phase C).
- Option: the gate holds with Reveal, or with retries any chance above 0, or without retries
  even odds (Search at least the height).

## Lock picking and bashing

UseLockpickOn calls checkLock with the Pick Lock skill: skill_check(skill, 3z) > 0, so skill −
3z + rand() % 31 > 15; z 14 needs skill 31 first, z 15 never opens. The pick does not break, so
with retries a lock of difficulty z opens when the skill is above 3z − 15: Pick Lock 0 opens z 0
to 4, 30 opens up to 13; without retries the editor asks for skill 3z (15 in 31). The Open spell
picks with skill 45 (z 14 too). Picking also needs a lock pick (six in UW1, the first at
L1 (35, 61)).

**Doors the shuffle must open** (picking and bashing both off): doors that nothing else opens,
blocked in the solve, with what opening each alone adds:

- L3 (15, 26) to the corridor with skeletons (`pick:4 | bash`): 5 zones and key 11, which opens
  four more doors;
- L3 (4, 55) to the shadow beasts (`pick:6 | bash`): the Key of Courage and key 10;
- L8 (47, 42) to Carasso (`pick:10 | bash`): key 28 and Carasso;
- L2 (11, 13) to the vault ledge (`pick:10`): 2 zones, nothing the logic needs;
- nothing behind: L4 (61, 6) Rawstag's ankh shrine (`pick:14`: only the Open spell opens it),
  L7 (41, 42) and (52, 54) into the cavern of dead things (`bash`).

## The dragon skin boots, as the data shows them

Karl's list is nearly right; the conversations differ in three places. Sethar Strongarm gives
**dragon scales**, not boots, and wants **rotworm stew**; Marrowsuck makes the boots from the
scales and the thread and wants **any food** (items 0xB0-0xBF), not only fish; the recipe is
read with the bowl in the pack.

- `worn:lava` ⇐ `item:dragon_scales & item:strong_thread & npc:marrowsuck & item:food`
  (conversation 6: find_barter 285 and 284, do_inv_create 47, find_barter 1011)
- `item:dragon_scales` (Sethar holds #653) ⇐ `item:rotworm_stew` (conversation 17)
- `item:rotworm_stew` ⇐ `item:bowl & item:dead_rotworm & item:mushroom & item:flask_of_port &
  item:stew_recipe` (obj/COMBINE.C make_stew: a bowl holding only those three)

Where each is: strong thread L1 (20, 56..57), three pieces, and one carried by Tyball; bowls L1
start (26, 12), L4 and L6; dead rotworm L4 (51, 6), and five rotworms (L1 two, L3, L6; a
rotworm always leaves one, critter/PATHFIND.C build_corpse); mushrooms on every level 1-6;
port L1 Outcasts (30, 12) and five more; the recipe, scroll #810 carried by **Lanugo**, L1 (4,
56), handed over when asked about his stew; **Sethar Strongarm** L4 (49, 22); **Marrowsuck**
L5 (34, 23); food everywhere. Fishing (a fishing pole facing water, one bite in five) is not
needed. The solver has the boots in sphere 6. No dragon skin boots lie in the levels.

## The damaging maze

The maze's damage traps lie in rock and are set off by move triggers on the maze tiles. The
fact walls every tile of L7 (42..62, 17..38) holding a move trigger whose chain hurts the
player: 20 tiles. The pick-up triggers at (37..38, 35..38) hurt only when the object is taken,
so they are not walls. The global option would wall 31 more tiles: L2 18 (the trap corridor at
(25..29, 49..54) and three more), L4 1, L5 2, L7 8 (the maze entrance (54, 39) and the
damage room west of the maze), L8 2.

## How far the solver gets (242 zones; the goal on L9 needs the talismans, Phase C)

| Magic | Single-use | Picking and bashing | Zones | Spheres |
| --- | --- | --- | --- | --- |
| on | off | on | 212 | 23 |
| on | off | off | 200 | 25 |
| on | on | on | 212 | 20 |
| on | on | off | 200 | 21 |
| off | off | on | 181 | 23 |
| off | off | off | 168 | 25 |
| off | on | on | 211 | 20 |
| off | on | off | 200 | 21 |

With the defaults (picking off, bashing on) it reaches 209. Without Magic and single-use items,
levitation blocks the most (19 links): the only lasting source, the ring, is on L7 behind
levitation itself. The rest is blocked by Phase C facts: `flag:var27_is_38` and
`flag:var31_is_470` (lever codes), the tombs (the crystal), the sword area, Gate Travel.

## Not settled

- Casting ignores level, mana and the casting roll (Karl accepted it); Open on a z 14 lock
  costs mana per try.
- The no-magic reading is strict: a spell cast before stepping onto the square keeps working.
- Telekinesis (sets the pick distance to 0) might replace the pole; not modelled.
- The maze rectangle is hand-set; the maze's own entrance tile (54, 39) holds a damage trigger
  but lies outside it.
- Bashing a door needs no weapon or strength in the model.

# Phase C (C1): the world over time

Built with the default options (Magic on, single-use items off, lock picking off, bashing
always, Search 0 with retries, Ring of Levitation needs lava protection, killing NPCs off). The
code is `Core/Randomizer/Timeline.cs` (changes, solver, return check, report), with additions to
`NavRegions.cs` (free chains, telekinesis, closers, lever sides), `AbilityZones.cs` (options as
tokens, NPC and created items) and `Facts.cs` (every `docs/facts/uw1-*.json`, the new kinds).
The tests are `tests/UwEditor.Core.Tests/TimelineTests.cs`.

## Karl's corrections

1. **Ring of Levitation.** The ring (#683, L7 (44, 44)) lies in its own zone; fact
   `ring_of_levitation` links it with the zone of (59, 43) both ways, `spell:lava` with
   `option:ring_needs_lava`, free without (`otherwise`). The solve reaches it in sphere 12, after
   the dragon skin boots (sphere 6).
2. **L4 lever puzzle.** Fact `l4_lever_puzzle` (kind `free`) makes #526, #811, #853 and #799
   usable from their zone. The timeline applies the four terrain changes in spheres 5 and 6 and
   reaches the alcove after the puzzle in sphere 7, with or without Magic.
3. **L5 levers.** Fact `l5_levers` makes every L5 lever free from where it is used (not from the
   start: the levers must still be reached). The Kneenibble portcullis then needs only lever
   #667's zone, and lever #663's create trap makes the Ring of Humility (sphere 6).
4. **Telekinesis** (Ort Por Ylem, spell 35; `xt_spells` 8 sets the PickDist 0 effect,
   `game/PLAYDATA.C player_affected_by`, so nothing is too far or too high) is an ability like
   the others, and every `item:pole` of a lever's reach reads `item:pole | spell:telekinesis`.
5. **Bashing** is no option any more: `bash` always holds. The doors the shuffle must open are
   those that only picking opens: L2 (11, 13) `pick:10` (the vault ledge) and L4 (61, 6)
   `pick:14` (Rawstag's shrine).
6. **Glitchless:** no option or fact allows tricks; `option:glitches` is never on.

Also from the quest facts' requests: a switch in a rock square (lever #972 behind the vines) is
used only once a change-terrain trap opens the square (its flags join the lever's
requirement: `flag:terrain_L3_586`); a switch is used only from its own side of a closed door
(Eb's #840, Drog's #807, the armoury's #830 no longer count from outside); a mechanism can
take conditions from a fact (kind `needs`: the Slasher's chamber, #539, needs the Key of
Infinity).

## How changes are applied and zones kept

The base zones are Phase B's. When a mechanism's trigger zone is reached and its conditions
hold (inventory, variables unless free, a look's Search check, a `needs` fact), its flag is
granted. Terrain changes (`event/WORLDEV.C change_terrain`: type, height up to 13, floor),
deleted objects and door traps that open are applied to a copy of the level, and that level
alone is rebuilt (regions, edges, hidden gates, no-magic narrowing). Its regions are grouped
as the zones are (free strongly connected parts, door tiles with a neighbour, dead ends
folded). Each group takes the base zone with most of its tiles, each base zone going to one
group; a group left over is a new zone (id 1000 and up, the same id in every state); a base
zone whose tiles all went elsewhere is linked to the zone that took them, free both ways.
Placements follow their objects' tiles. Stairs keep their base zones (not rebuilt). Each set
of changes is built once and cached, so the ten solves of the report take about 2 s.

Of the 136 mechanisms, 56 are applied when set off (terrain changes, deletions, doors opened;
9 of them change no way); 12 door closers are made one way instead; the rest (variables, creatures, hack traps) only grant their flag. With the defaults, 116 are set off and 42 applied.

## Changes that close things

- **Doors closed behind the player** (walk-onto door traps; UW1's door trap first gives the
  door a copy of the trap's lock, `event/TRIGGER.C TRAP_DOOR`). One way: free towards the
  trigger, back only with what opens the closed door:
  L1 (47, 51) #634 (key 3, pick 5, or Eb's button from inside); L1 (15, 34) #635 (key 4, pick 5,
  or Drog's button from inside); L2 (13, 30) #844 (pick 6 or the lever inside); L5 (57, 2) #651
  (a walking toggle: key 15 or the lever); L7 (27, 54) #643 (key 23, pick 13); L7 (7, 35) #656
  (key 23, pick 13, or pull chain #423 outside); L7 (19, 4) #668 and (29, 21) #709 (the pull
  chains); L7 (24, 56) #888, the jail behind the prison guard (key 23 only).
  The L3 door at (6, 8) has three such triggers (#886, #888, #889) on both sides and another
  free way round: every way through it needs it closed; the doorway is a zone of its own.
  The lever that must be worked again is written `now_zone:N`: in the return check it holds
  only if zone N is reached again, not because it was reached before.
- **Changes that open and close** (applied; the zones reached before stay reached): lever #972
  turns six lake squares to rock (the dam) and leaves a ledge at (50, 8) as a zone of its own;
  the L4 lever puzzle's raised floors make three small zones; L6 #802 and #915, L7 #351, #866,
  #873, #914 and #933 open walls that also end a drop or a secret way; the look-trigger walls
  (L5 #987, L7 #343, #829, #887, #926) replace their hidden-gate links by real ones.
- **Not applied:** none of the chosen changes only closes. L6 #822 deletes the bridge at
  (27, 50); bridges are not walked on in the automatic zones, so it changes nothing here.
  The jail's conversation (variables 50 and 51) is not modelled.

## One-way traps

With the defaults: none. With Magic off, single-use items off and picking on: the L2 vault's
lower area, reached by a drop from the vault ledge (behind the `pick:10` door), nothing inside.
Without the quest facts and without a planted sapling, the return check uses what the player
held when he got there and the levers he had reached before. With the quest facts, the silver
seed (L1) is held from sphere 2, so the sapling saves every later zone.

## How far the solver gets

Without the quest facts (only `uw1-facts.json`): 212 of 246 zones with the defaults (22
spheres); picking on 215; Magic off 212 (the ring through the boots gives levitation);
killing NPCs 221. The goal is not reached (no goal fact). Blocked: the tombs (the crystal is
Kallistan's), Smonden's key 26, `flag:var27_is_38` (the plate area code), picks 10 and 14.

With `uw1-quests.json` (C2, read from its branch): the goal is reached with every main option
set, 227 to 228 zones in 23 to 26 spheres.

## Not settled

- Stairs are not rebuilt with their level: a stairs link keeps the base zone of its ends.
- A change that opens and closes is applied at once when it can go off; the solver does not
  choose to wait.
- The classification compares the level before and after; a few hidden-gate links that a
  change replaces still show as "gone".
- The L3 door at (6, 8) is closed in both directions, which is strict.
- A lever is never used across a closed portcullis, though the game's reach test
  (`ui/INTERACT.C InPickRange`, `BlockingTerrain`) does not look at doors.

# Phase D: the key item shuffle

The code is `Core/Randomizer/KeyShuffle.cs` (survey, fill, check, writing, log),
`ChallengeExceptions.cs` (the two challenge exceptions), additions to `Facts.cs` (kind `count`,
goal facts with an option), `Timeline.cs` (counts, the goal's options, placements a run can
replace) and `AbilityZones.cs` (one placement per object, count placements, locked
containers), and `App/KeyShuffleWindow.cs` with `App/LogicOptionsPanel.cs` (the logic options,
now shared with the Automatic zones window). The tests are
`tests/UwEditor.Core.Tests/KeyShuffleTests.cs` and `tests/UwEditor.App.Tests/KeyShuffleWindowTests.cs`.

## Karl's corrections

1. **20 gold for Shak, food for Zak.** The sword needs `item:gold_20`: fact `gold_20` (kind
   `count`) adds up the coins (0xA0) and gold coins (0xA1) of the zones reached, each stack by
   its quantity (conversation 2 func_11F7, conv/CONVERSE.C count_inv; a gold coin counts 1, not
   10). The taper needs `item:food` (or killing Zak with the option). Both are needed for the goal
   through the talismans: with every coin deleted, or every food deleted, the goal is not reached.
   - **Vow of poverty** leaves one pile of 20 coins at **L2 (60, 4)**, beside Shak (60, 3).
   - **Starvation** leaves **one piece of meat** (0xB0, quality 40) at **L3 (48, 38)**, beside
     Zak (47, 38). That is the least he takes: the taper is worth 1, the meat 3 (at worst 2 after
     his appraisal), and his greed is at most 67 % (conv/BARTER.C do_offer, assess_value; creature
     0x68: haggle 9, shrewd 10).
   - These are the only exceptions; the later challenges (Fasting, Merchant, Hoarding) leave
     them alone. **Contradiction:** Zak does not need food as such. He likes food and drink
     (0xB0-0xBF) and lights and wands (0x90-0x9F), and takes any offer worth 2 or more. The logic
     keeps Karl's rule, which is stricter.
2. **The goal is reaching L9** (fact `goal_reach_level9`, L9 (27, 23)); the end trigger is no
   longer a step.
3. **The Key of Love** needs `npc:judy & item:picture_tom` only.
4. **Require breaking the orb** (`option:require_orb`, off by default; fact `goal_orb`): the goal
   also needs `flag:orb_destroyed`. Both windows have the checkbox.

Also changed so that facts follow the items the shuffle moves: Gate Travel is an ability (its
scrolls are found wherever they lie; the five gate travel facts are gone), and Garamon's bones
give `item:garamon_bones` wherever they lie (the flag fact is gone). The automatic zones now
keep one placement per object (not one per zone and token), and an item in a locked chest needs
what opens the lock (`key:N | pick:Z`; UW1 has two locked chests: L1 (3, 50), key 4, and L4
(19, 20), no key).

## What moves

With the default categories (creature inventories and filler off), **161 objects move**:
ability items 28 (17 key items), Key of Infinity parts 1 (the Key of Courage), boots'
ingredients 37, keys 11, plot items 39 (16 key), runestones and the rune bag 43 (28 key),
talismans 2 (book, wine). 112 are key items (a token they give is one the logic asks for); the
other 49 fill the rest. With filler on, 515 objects move; with creature inventories also, 523.

What stays (with the defaults): 26 carried by creatures (with creature inventories on: those
whose creature has a conversation, 63 with filler), 3 orbs holding triggers, 2 create trap
templates (the Ring of Humility, the ghost's key 15), the moonstone (Gate Travel goes to it;
the sealed moonorb area is entered only that way) and Tyball's orb (#820, named by
orb_destroyed). Never: objects a trap or trigger names, objects holding a trigger or other
objects (their lists stay on their level), mobile records, the emeralds and inventory-trap
kinds, items whose token a trade fact ties to a holder, coins and food beside Shak and Zak.

## The algorithm as built

1. Build the zones and the timeline on the levels as they are (with the chosen options).
2. Lock picking off: solve with every moving item held. If the goal is blocked by a door that
   only picking opens, unlock it (clear the lock's locked flag) and rebuild; repeat.
3. Survey the objects; the places are the slots of the objects that move.
4. Probe every place in the original game (its zone and sphere).
5. For seed, seed + 1, …: shuffle the key items; for each, solve with the placed items at their
   places, the items not yet placed (and all the others) assumed held, and a probe token on
   every empty place; choose a reached place at random. Then the other items fill the rest at
   random.
6. Write the records into a copy of the levels, build the timeline afresh and solve with
   nothing assumed. Accept when the goal is reached and no one-way trap strands the player.

Each fill step is one timeline run (15 to 120 ms); a whole shuffle takes about 2.5 s (3 s with
filler).

## Results

| Options | Seeds | Success | Attempts | Spheres after (before) |
| --- | --- | --- | --- | --- |
| defaults | 20 | 20 | 20 | 21-23 (26) |
| filler | 5 | 5 | 5 | 21-24 |
| creature inventories | 5 | 5 | 5 | 21-24 |
| Magic off | 5 | 5 | 5 | 21 |
| Magic off, single-use | 5 | 5 | 5 | 21-24 |
| lock picking on | 5 | 5 | 5 | 21-24 |
| require the orb | 5 | 5 | 5 | 21-25 |
| Magic off, orb, filler, creatures | 10 | 10 | 10 | 21-24 |
| killing NPCs, creatures, filler | 5 | 5 | 5 | 21-23 |
| Vow of poverty + Starvation, filler | 1 | 1 | 1 | 24 |

No attempt failed the final check. Doors unlocked with picking off on the shipped levels: none
(no door that only picking opens blocks the goal: the L2 vault ledge and Rawstag's shrine hold
nothing needed). The test makes the shadow beasts' door on L3 (4, 55) pick-only and keeps the
keys in place: the shuffle unlocks it.

## Not settled

- The fill counts every filler item as held while it places the key items, so food and coins
  are checked only by the final solve (they are plentiful; no attempt has failed on them).
- A chest's lock is taken as unbreakable.
- Items keep their own owner field; a key item put where a race owns things is not marked as
  theirs, and an owned item keeps its owner when it moves.
