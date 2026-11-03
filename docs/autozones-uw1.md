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
