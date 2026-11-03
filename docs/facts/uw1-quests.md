# UW1 quest facts

This is the account behind `uw1-quests.json`: one section per quest, with each fact's id, what it says and the evidence. "Conversation N" is the slot in CNV.ARK, read with the editor's decompiler. Code references are to the reconstructed game in underworld-exhumed (`uw1/src`). Tiles are `L<level> (x, y)` and objects are `#index`.

Rules followed:
- **Glitchless.** No tricks or sequence breaks beyond plain play (Karl's point 6).
- **Known words.** The player knows every mantra, password, tune and lever code (Karl).
- **Kills.** Killing an NPC that only fights (Tyball, Rodrick), or a creature the quest is about (the golem), is plain play. Taking an item from a friendly NPC by killing it needs `option:kill_npcs`.
- **Confidence.** `certain` means read in the data and the code. `probable` means read, but one step is assumed (a dialogue path, a reachable tile, a puzzle being solvable).

## The goal

The chain, in order:
1. Open the Slasher chamber on L8 with the Key of Infinity.
2. Bury Garamon's bones in his grave on L5.
3. Throw the eight talismans into the chamber's lava.
4. The moongate takes the player to L9, the Ethereal Void.
5. Walk to the end trigger.

- **goal_void_end** (`goal`, probable). The end is move trigger #1022 at L9 (52, 51), which runs hack trap #1021 (quality 0x3F, `EndGameMode`; event/TRIGGER.C do_trap_hack). It stands on the high platform (height 12), reached by the steps at (52, 43..49). The Phase A regions put L9 (27, 23) and (52, 51) in one region. It is *probable* rather than certain because the Void's force fields (0x16D, many lines of them) are not modelled. They have no code of their own. The move triggers on the south strip ((10, 51), (13, 16), (19, 58), (32, 58)) send the player back to the start, (27, 23). On L9 nothing can be cast (combat/SPELLS.C do_spell) or kept (motion/OBJPHYS.C mob_to_static), and the player cannot die (game/SKILLS.C player_is_dead: talismans 0 keeps 4 HP).
- **ritual** (`link` L8 (32, 30) → L9 (27, 23), one way, certain). In motion/OBJPHYS.C, mob_to_static destroys a fate-10 object that lands in lava on level 8 within |dx| + |dy| < 6 of (32, 32), but only while `player->talisman_ok` is set. Otherwise it sets a dream bit and the talisman stays. When the count reaches 0, game/SKILLS.C check_victory makes a moongate at (32, 32), prints "The Slasher of Veils is dragged from this world." and teleports the player to L9 (0x1B, 0x17). The fate-10 items in COMOBJ.DAT are 0x0A sword, 0x36 ring, 0x37 shield, 0x93/0x97 taper, 0xAE cup, 0xBF wine, 0x11F standard and 0x136 book. The chamber (zone of (32, 29)) holds the lava pool at (30..34, 31..34).
- **garamon_bones** (`flag` L8 #820, certain). Garamon's bones are the pile of bones #820 (0xC6, owner 0x3E) at L8 (2, 15). The other piles have owner 0 and do nothing, so the bones get their own token, `item:garamon_bones`.
- **garamon_buried** (`flag` L5 #599, certain). obj/USEITEMS.C UseBonesOn: bones with owner 0x3E used on a gravestone with special link 0x21 set `talisman_ok`. That gravestone is #599 at L5 (54, 54), link 0x221. Garamon's ghost then talks (conversation 27: "cast all eight into the Abyss"), and the trigger at (54, 52) is set off. Garamon's quest 5, set in his talk, is checked by nothing. So the painted `flag:talismans_quest` is really "bones buried".
- **slasher_door** (`link` L8 (32, 26) ↔ (32, 29), certain). The door is the picture #541 at (32, 27), set in front of the rock (32, 28). obj/USEITEMS.C seg040_352B_AFF: the Key of Infinity (0xE7) used on a 0x16E object over a textured door is used up and runs the object's traps with "how 7". Open trigger #539 → change terrain #538 opens (32, 28) at height 2, and delete #535 removes the picture. **For C1:** mechanism `flag:terrain_L8_539` needs the condition `item:key_of_infinity`. It must not be applied just because its region is reached.

## The Key of Infinity

- **key_of_infinity** (`craft`, certain). DATA/CMB.DAT has these rules: Truth+Love → 0xE6, Truth+Courage → 0xE4, Love+Courage → 0xE5, and any two-part key plus the missing part → 0xE7. The sources are used up. No anvil or NPC is needed.
- **Key of Truth: key_of_truth_L1 … key_of_truth_L8** (11 `flag` facts, certain). game/SKILLS.C mantra_advance: mantra 21 of string block 2, **FANLO** (0x48), puts the Key of Truth in the hand (`place_new`, once, `player->key`). Talking to any shrine (0x157) starts a mantra (conv/CONVERSE.C TalkTo). There is one fact per shrine, so one reachable shrine is enough:
  - L1 #879 (46, 18)
  - L2 #834 (4, 59)
  - L3 #633 (61, 8), in the sword area
  - L3 #634 (41, 22)
  - L3 #756 (28, 44)
  - L4 #518 (60, 2), behind Rawstag's door
  - L4 #663 (9, 34)
  - L5 #843 (20, 36)
  - L6 #908 (3, 32)
  - L7 #906 (40, 54)
  - L8 #793 (31, 61)

  The painted placement (Illomo plus Gurstang's phrase) is wrong. Illomo and Gurstang (conversations 187 and 217) only talk about the key, and the word Gurstang found is a clue to the mantra. In the emulated solve the key comes in sphere 3.
- **key_of_love** (`craft`, probable). Judy (L5 (34, 8), conversation 23) takes the picture of Tom. Then `do_inv_create(226)` and `place_object(…, 33, 8)` put the Key of Love on the lava square next to her. The key has fate 0, so it does not burn. The player's hand cannot reach it from her ledge. ui/INTERACT.C InPickRange allows at most 12 z units below the feet, and the key is 2 floor steps (16) down. BlockingTerrain also refuses a reach across another terrain class. So the fact asks for `spell:lava | spell:telekinesis`. Telekinesis reaches anything, because a pick distance of 0 is never blocked. A brief unprotected step into lava costs little (1 fire damage one time in five), but the facts follow Karl's lava rule.
- **picture_tom** (`trade`, certain, adds nothing). Bolinard, in a cell behind key 27, gives the picture when he offers to sketch a map (conversation 214, take_from_npc 272).
- **Key of Courage.** No fact needed. It lies on the floor at L3 (12, 54), behind the key-10 massive door. Key 10 is in a gold coffer at L3 (2, 54). The way up from L7 needs key 26.
- **smonden_key** (`trade`, certain, adds nothing). Smonden (cell behind key 27) gives key 26 when asked (conversation 215, take_from_npc 1016).

So the Key of Infinity needs:
- a shrine;
- Tyball's key 27, for Bolinard and Smonden;
- the Key of Courage route;
- lava protection or Telekinesis, for Judy's key.

## The eight talismans

| Talisman | Where | Fact | What it needs |
| --- | --- | --- | --- |
| Sword of Justice | crafted | **sword_of_justice** craft, certain | blade #882 (L3 (58, 9), sword area) + hilt #932 (L5 (54, 49)) + Shak + coin. Shak (L2 (60, 3), conversation 2) takes both halves, says "come back in an hour" and "20 pieces of gold" (func_0ADA, func_1064, func_11F7). There is no anvil step. |
| Shield of Valor | golem #255, L6 (6, 22) | **shield_of_valor** trade `flag:golem`, certain | Fight the golem. It cannot die (WORLDEV.C death_check 0x16): it yields below 10 HP and hands over the shield (conversation 22). |
| Ring of Humility | template #934 in create trap #636, L5 (16, 59) | **ring_of_humility** trade `flag:create_L5_635`; **ring_lever_code** free `flag:var34_is_7`, certain | Lever #663 makes the ring when variable 34 reads 7. Levers #662, #670 and #664 and the floor triggers set the variable. It is an L5 lever code, so it counts as free (Karl's point 3). |
| Taper of Sacrifice | Zak #254, L3 (47, 38) | **taper_of_sacrifice** trade, probable | Zak (conversation 28) trades his four lights (candle, torch, lantern, taper; the longsword is skipped) and likes food. The fact is `(npc:zak & item:food) \| (option:kill_npcs & npc:zak)`. |
| Cup of Wonder | not in the levels | **cup_of_wonder** flag at L3 shrine #756, `item:flute`, certain | sound/SOUND.C play_cup_tune: play the tune 3 5 4 2 3 7 8 7 5 (Eyesnack, conversation 5, teaches it) on a flute within 2 squares of L3 (24, 45). The cup appears in the hand (string 0x88), once. The mantra INSAHN only tells where. Flutes: L2 #584, L4 #580, L5 #586. |
| Wine of Compassion | #826, L6 (27, 50) | none | It lies on the floor (on a bridge). |
| Book of Honesty | #918, L6 (8, 37) | none | It lies on the floor, behind a height-0 look gate ("by the key behind the hourglass", Morlock). |
| Standard of Honor | Dorna #235, L4 (8, 4) | **standard_of_honor** trade, certain | Dorna offers it once Rodrick is dead (quest 11; WORLDEV.C death_check 0x8E), whether or not the knights' quest is done (conversation 12 func_0CE2, func_0D92). Rodrick always fights (conversation 142). |

With the new kinds emulated, the solve has all eight talismans by sphere 9.

## Mantras and shrines

Karl: "the player knows all mantras, but access to a single shrine should be necessary". From game/SKILLS.C mantra_advance, the shrines give three things:
- **FANLO** gives the Key of Truth. This is the one that matters, so there are 11 shrine facts (above).
- **INSAHN** tells the way to the cup. This is knowledge only.
- **NO** prints an empty string.

The 20 skill mantras and the three group mantras (SUMM RA, MU AHM, OM CAH) spend skill points and gate nothing.

## Items NPCs hold

| Item | Holder | Quest way | Kill way | Fact |
| --- | --- | --- | --- | --- |
| key 3 | Retichall, L1 (57, 55) | none (conversation 8 only grants an audience) | `option:kill_npcs & npc:retichall` | retichall_key |
| key 4 | Drog L1 (15, 35), Vernix L1 (2, 57) | none | `option:kill_npcs & (npc:drog \| npc:vernix)` | goblin_key_4 |
| key 13 | Rodrick, L4 (32, 61) | kill him (he only fights) | n/a | rodrick_key |
| key 15 | dire ghost created at L5 (52, 3) by #648 | kill it (a monster) | n/a | ghost_key_15 |
| key 23 | Tyball; troll jailer #251 L7 (26, 56) | kill Tyball (he only fights); the jailer opens his gate for a bribe but keeps the key | `option:kill_npcs & npc:guard` | key_23 |
| key 26 | Smonden | asked (conversation 215) | not needed | smonden_key |
| key 27 | Tyball | kill him | n/a | tyball_keys |
| key 28 | Carasso, L8 (47, 44) | asked about the chamber (conversation 232) | not needed | carasso_key |
| crystal splinter | Kallistan, L7 (10, 51) | an oath against Tyball (conversation 212 func_0606 → func_06F2); no password needed | not needed | kallistan_crystal |
| picture of Tom | Bolinard | the map offer | not needed | picture_tom |
| Standard of Honor | Dorna | Rodrick dead | `option:kill_npcs & npc:dorna_ironfist` | standard_of_honor |
| Taper | Zak | trade food | `option:kill_npcs & npc:zak` | taper_of_sacrifice |
| Shield | golem | fight it (it yields) | n/a | shield_of_valor |

These are not written as facts because they do not gate the goal, or another file owns them:
- **Dragon scales** (Sethar, Phase B's trade) could take the kill alternative `item:rotworm_stew | (option:kill_npcs & npc:sethar_strongarm)`. That edit belongs in uw1-facts.json.
- **Ironwit's Fly potion** #772 is given only for his blueprints (conversation 90), and they lie high up in his storeroom ("ye cannot fly, can ye?"). Phase B counts it as a free single-use source.
- **Dr. Owl's Flam stone** is given when he is shown the rune bag (conversation 14). Casting needs the bag anyway, and another Flam lies at L6 (52, 2).
- Tyball's thread and medallion also lie on the floors. Goldthirst's Gem Cutter (for the gazer) has no use in the code.

## Doors NPCs open

| Fact | Door | Who, and what for | Confidence |
| --- | --- | --- | --- |
| eb_portcullis | L1 (47, 51) | Eb, outside, when talked to (conversation 65). Walking through closes it (#634); button #840 inside. | certain |
| drog_portcullis | L1 (15, 34) | Drog, just inside, talked to through the bars (conversation 66). Button #807 inside. | probable |
| brawnclan_portcullis | L2 (13, 30) | Brawnclan, inside (conversation 88). Lever #854 inside. | probable |
| steeltoe_vault | L2 (11, 13) | Steeltoe, for Deco Morono (conversation 21). Leads to the vault ledge, which nothing needs. | certain |
| rawstag_door | L4 (61, 5) | Rawstag, for a ruby or a red gem (conversation 144). Behind it: a shrine and the writ. | certain |
| armoury_door | L4 (3, 10) | Dorna, at the end of the knights' quest. | probable |
| guard_gate_north | L7 (19, 4) (and (22, 5)) | Tyball's guards: the medallion, or they open it to fight (conversation 209). | probable |
| guard_gate_prison | L7 (27, 54) | The same (conversation 220). The jail beyond can lock the player in (x_traps 50, 51): for C1. | probable |
| jailer_gate | L7 (21, 56) | The troll jailer, for a bribe (conversation 216). | probable |

- The talking door at L6 (4, 2) (conversation 25) has the same zone on both sides.
- Murgo's "prison" at L3 (27, 22) is the door square itself, so nothing lies behind it.

## The painted map's open flags

- **deco_morono: settled.** Goldthirst tells it (conversation 3: "say the words 'Deco Morono' to the guard at the door"). It opens only Steeltoe's vault door. Kallistan's crystal does *not* need it: the oath path gives the splinter. Facts: deco_morono, steeltoe_vault.
- **lizard_friend: settled, not needed.** Quest 6 is set when Murgo (whoami 0x18) dies (death_check), and it only turns the lizardmen friendly (Sseetharee's set_race_attitude). Sseetharee opens Murgo's door for five pieces of food (items 0xB0-0xB6, func_0959) and sets quest 0. Nothing lies behind the door.
- **crux_writ: settled.** The knights' quest goes like this:
  1. A knight (Ree, Doris, Kyle, Cecil or Meredith) sets quest 32 to 1.
  2. Dorna's questions make the player a squire (2).
  3. The writ of Lorne (scroll #552 at L4 (59, 2), in Rawstag's room) makes him a knight (3).
  4. The order's gold plate (#541, owner 31, L4 (10, 44), the plate secret area) makes Dorna open the armoury (4).

  It opens only the armoury, which nothing needs. The standard depends on Rodrick, not on this quest. Facts: writ_of_lorne, crux_gold_plate, plate_area_code, crux_writ, armoury_door.
- **bullfrog_solved: settled** (Karl: always solvable). The 8 by 8 pool at L4 (48..55, 48..55) is raised and lowered square by square (event/WORLDEV.C work_bullfrog_tiles, 63 moves). The exit ledge (58, 48) is reached by building a stair to height 12 at (55, 48) and jumping two squares east. Fact: bullfrog_puzzle (free, one way). Levitation already has its own climb edge.
- **kneenibble_code: settled by C1.** Kneenibble (L5 (58, 26)) tells the code. The levers (variables 31-33 = 7, 2, 6) are Karl's point 3, which C1 makes free, so it is not repeated here. The ring's L5 code (variable 34) is here, as ring_lever_code.
- **gazer_dead: not needed.** It only earns Goldthirst's Gem Cutter of Coulnes (conversation 3, take_from_npc 275). Nothing in the code uses the gem cutter (0x113).
- **maze_memorised: no longer matters.** With Phase B's walls on the damage-trap squares, the damaging maze is still one zone, from its entrance (54, 39) to Tyball's crib (54, 15). The safe path is what memorising means, and the walls already model it. The crown of maze navigation is not needed.
- **talismans:8: settled** (table above). The sword counts once Shak reforges it. There is no anvil step.
- **talismans_quest: settled.** What counts is burying the bones (talisman_ok), not Garamon's quest 5.
- **gurstang_phrase: settled.** The Key of Truth comes from FANLO at a shrine.
- **fought_guards: settled.** The guards open their gates both for the medallion and when they come out to fight (gronk_door appears in every branch of conversations 209, 219, 220 and 222), so these links are free.

## Gate Travel and the silver sapling

- **gate_travel** (`warp` from any zone to the moonstone, L2 #868 (9, 35), certain). combat/SPELLS.C xt_spells case 10 teleports to `player->moonstone`, and SKILLS.C do_mstone finds the stone on that level. Picking the stone up sets the level to 0 (ui/INTERACT.C), so the spell fails while it is carried. Dropping it sets the current level (inv/BAGS.C), so the stone is a movable return point. The moonorb area (L2 (8..10, 34..36)) is sealed: Gate Travel is the only way in. Move trigger #869 at (8, 34) opens (7, 34) to the main area (`flag:terrain_L2_869`).
  - The spell fails on L9 and on no-magic squares. On L7, only scrolls work until the orb is broken (no mana).
  - **gate_travel_cast** (`craft`): the runes Vas Rel Por (spell 32). **gate_travel_scroll_L2/L5/L6/L7** (`flag`, `once:gate_travel`): scrolls #560 (L2 (10, 35), inside the moonorb area), #899 (L5 (2, 27)), #726 (L6 (16, 11)) and #760 (L7 (61, 62)). Drop these if C1 adds gate_travel to the ability sources.
  - **emerald_vas** (`flag` L6 #912, probable). event/WORLDEV.C emerald_trap: with an emerald on each corner of the 9 by 9 square around (53, 18), the button at (53, 19) makes a Vas stone. Six emeralds lie at (53, 19). This is the only Vas stone besides L8 #757. It matters for Fly (Vas Hur Por) too, which Phase B did not know.
- **silver_seed** (`flag` L1 #967) and **silver_sapling** (`warp` to "planted", certain).
  - Using the silver tree at L1 (36, 2) gives the seed in the hand (obj/USEITEMS.C UseObj, class 0xA) and sets `player->tree` to 0. The starting tree is not a return point (CHARGEN sets 0).
  - game/SKILLS.C plant_seed plants the seed on the square ahead if it is open with floor texture 5-11, 18-22, 27-31 or 35-40. 161 of the 242 auto zones have such squares, and L9 has none. Planting records the level.
  - player_is_dead: with a tree planted (and not on L9), death costs an eighth of the experience and the player returns at the tree with nearly full health.
  - Using the tree again gives the seed back, so the return point moves along. Armageddon forgets it.
  - C1's model fits: once the seed is held, it can be replanted before every one-way step. A stricter model would ask for a plantable square in the zone left behind.

## The L3 sword area and the crystal splinter

- **Lever #972 is reachable from the start of L3.** The stairs from L2 at (42, 60) land at L3 (42, 61), in the main zone. The lever's square (52, 13) is reached from (52, 12) in the same zone. But (52, 13) is rock until the vines are searched: look trigger #586 (height 3) on #588 opens it (change terrain #587). With Search 0 that is 12 in 31 per look, and retries are allowed; Reveal also works. **For C1:** mechanism `flag:terrain_L3_971` should carry the condition `flag:terrain_L3_586` (probable: a lever inside a rock square cannot be seen or used). The pull drains the pool and opens (46, 7) (Phase A). The sword area holds the blade and a shrine.
- **The crystal splinter** comes from Kallistan by the oath, with no password. Walking past L7 (20, 42) with it opens (20, 41) into the tombs (Phase A: `flag:terrain_L7_887`, condition `item:crystal`).

## Other things that gate progress

- **The waterfall alcove** (painted link 39) is settled: move trigger #538 opens (57, 53) when walked onto. It is a plain mechanism.
- **Tyball's orb** (Phase B) is not needed to kill him. Breaking it halves his hit points (obj/USEITEMS.C).
- **For C1:** the Phase A lever reach counts some inside buttons as reachable from outside a closed door: #830 in the knights' armoury, and #807 and #840 behind the goblin portcullises. The NPC links here do not depend on that.
- **For C1:** item tokens named only in link, warp or goal requirements (`item:ruby`, `item:red_gem`) get no placements from the levels today. Phase B scans only craft, trade and flag requirements. Phase B also warns "Nothing in the levels grants" for tokens that flag facts grant (`item:garamon_bones`, `item:key_of_truth`, `item:writ_of_lorne`, `item:crux_plate`), because it drops only craft grants from its list. The warning is harmless.

## Check

A throwaway test (not committed) loaded this file with `FactSet`. The existing loader reads every fact. Its `Check()` rejects only the 17 facts of the new kinds (12 link, 2 warp, 1 goal, 2 free), as "unknown kind". Every requirement parses with `Requirement.TryParse`.

That test then emulated the new kinds on the Phase B zones:
- links as zone links;
- free tokens granted;
- the ring, ghost, sword-area and crystal mechanisms added by hand.

With default options, the goal zone on L9 is reached in 26 spheres (23 with single-use items).
