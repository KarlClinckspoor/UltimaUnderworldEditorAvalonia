# Object record fields: what each bit does

This note records what the bits of an object record mean, checked against the reconstructed
source of both games (underworld-exhumed, `uw1/src` and `uw2/src`; paths below are relative to
`uw1/src` unless they start with `uw2`). The descriptions in
`src/Underworld.Formats/Uw1/Uw1Schemas.cs`, which the properties panel shows as tooltips, were
rewritten from it (November 2026).

Status:

- **confirmed**: the code that reads or writes the bits was read, and the meaning follows from it.
- **probable**: the use is clear but the name or a detail is a guess.
- **unknown**: nothing in either game reads the bits.

Counts are from the shipped levels (`DATA\LEV.ARK`) of both games, objects reachable from a tile
or a container only: UW1 4744 objects (657 mobile, all critters), UW2 9967 objects (803 mobile,
762 critters). "Saves" counts are from the three UW1 test saves under `gamedata/saves/uw1`
(14028 objects, 1626 critters), which show values the game writes in play. They were measured
with a throwaway test that was not kept.

## Where the old markers came from

The schemas were written by Claude on 2026-10-24 for the Unity editor
(KarlClinckspoor/UltimaUnderworldEditorUnity, `Assets/Scripts/Formats/Uw1/Uw1Schemas.cs`) and
copied into this editor on 2026-10-31. Neither Claude nor Karl inferred the "inferred" fields from
code at the time. A field was marked:

- *inferred* when its name was the one `include/object.h` of underworld-exhumed gives it (for
  example `OBJ_LONER`, `OBJ_BIN`, `OBJ_TEMP`). Those names come from the exhumed authors' reading of
  the code, or from Hank Morgan's UnderworldGodot (`uwobject.cs`), which the header cites. They
  were not checked against the code that uses them. One of them was wrong: `OBJ_BIN` is not a
  frame counter but the AI's timing slot.
- *unknown* when the header names it only by byte and bit (`OBJ_B15_6`, `OBJ_B19_4`).

## Static object (8 bytes; also the first 8 bytes of a mobile one)

| Field | Bits | Meaning | Status | Evidence | UW1 / UW2 |
| --- | --- | --- | --- | --- | --- |
| Flag 9 | id bit 9 | **Lock (0x10F):** set while locked. A pick or the key clears it (or removes the lock, see Flag 10); the key on an unlocked lock sets it. **UW2 trigger:** objects that are neither the player nor a creature (thrown items, missiles) set it off only with this bit. | confirmed | `obj/USEITEMS.C checkLock`, `uw2 obj/OBJUSE.C checkLock`, `uw2 event/TRIGGER.C UseTrigger`. UW1: 108 locks of 108 shipped set; UW2: 165 of 167 locks, 198 of 1431 triggers. | UW1 triggers ignore it. |
| Flags 9-12 as a number | id bits 9-12 | One 4-bit value for some kinds: a **trap**'s count of triggers that point at it (the last trigger deleted deletes the trap); a **door**'s swing, bits 9-11 how far open and bit 12 the direction while moving; a **lever or switch** (0x161, 0x162) position 0-7; a **bridge**'s or other 3D model's texture; a **writing**'s or gravestone's text offset. | confirmed | `event/TRIGGER.C trigger_obj_del`, `obj/EFFECT.C`, `obj/USEITEMS.C UseRect, changeDoor`, `3d/DRAWOBJ.C do_door, do_rect`, `obj/LOOK.C`. Shipped doors: 0 (closed) 210, 12 or 13 (open) 6 in UW1. | Same. UW2 sets a bridge's from a trap's owner. |
| Flag 10 | id bit 10 | **Book or scroll:** reading plays cutscene 0x100 + its text number. **Trigger:** reusable; without it the trap chain is deleted after it runs. **Lock:** kept after unlocking (re-lockable); without it the lock is removed. | confirmed | `obj/LOOK.C BookLook`, `event/TRIGGER.C UseTrigger`, `checkLock`. UW1: 182 of 292 triggers, 105 of 108 locks, 1 scroll. | Same; UW2's unlock trigger event is 0xB, UW1's 6. |
| Flag 11 | id bit 11 | **Trigger:** the player may set it off. **Spell object (0x120) or enchanted is-quantity object:** the enchantment is a spell that is cast (potions, wands, scrolls; link bits 6-8 + 12 and 0-5), and a spell object's quality is its charges, shown on look; without it a lasting enchantment (bits 4-8 and 0-3). | confirmed | `UseTrigger`; `obj/USEITEMS.C decode_obj_spell, useNSpellCharges`; `obj/LOOK.C`. UW1: 291 of 292 triggers, 29 red and 23 green potions, 21 scrolls, 17 spell objects. | UW2 hack trap 38 tests it on red potions. |
| Enchanted | id bit 12 | **Is-quantity object:** the link less 0x200 is its enchantment. **Trigger:** creatures may set it off; UW1: an enchanted trigger without Flag 11 is not set off by other objects. | confirmed | `decode_obj_spell`, `remove_spell`, `UseTrigger`. UW1: 66 weapons and armour, 51 triggers. | See Flag 9 for UW2's object rule. |
| Tenacious | id bit 13 | Four uses. **Never culled** (with anything holding it). **Cannot be damaged.** **Door:** the side it swings to. **Hostile creature:** accepts no danger on its paths. See [Tenacious and culling](#c-tenacious-and-culling). The game sets it on the player, spell-laid traps and triggers, and the silver tree. | confirmed | `obj/OBJECTS.C chkTenacious`; `combat/DAMAGE.C damage_object`; `3d/DRAWOBJ.C do_door` (`dir = (flags & 7) * (doordir * 2 - 1)`); `critter/AI.C acceptable_danger`; `event/TRIGGER.C cast_trap_spell`; `game/SKILLS.C`. Every shipped trigger, and every trap but 7 of UW2's 1353; UW1 56 of 216 doors, 4 critters; UW2 125 of 259 doors. | UW2: doors can be damaged even with it (`uw2 combat/DAMAGE.C`); marks plot items (Guardian signet rings on the lines of power, `SPELLS2.C`; book 0x138 is a crime to take, `INTERACT.C`; the telekinesis wand, `WORLDEV.C`). |
| Invisible | id bit 14 | Not drawn, so not clickable; hidden in container panels. Collision, combat and AI never test it. Cleared when the object starts moving. | confirmed | `3d/DRAWOBJ.C do_obj` returns before the object is registered for picking; `inv/BAGS.C`; `motion/OBJPHYS.C mob_init`. Every shipped trigger, and every trap but 7 of UW2's; 102 UW1 and 167 UW2 locks; no mobile object in either game. | UW2 hack trap 39 sets or clears it on an object; UW2 force fields (44) and Guardian rings use it. |
| Is quantity | id bit 15 | The link is a quantity or special property. | confirmed | (unchanged) | |
| Z | pos bits 0-6 | Height. **Lock:** pick difficulty, a skill check against Z * 3; 14 needs skill 31 (UW2 32), 15 is unpickable. **Look trigger:** Search difficulty. | confirmed | `checkLock`; `UseTrigger`. Shipped lock heights: UW1 0-15, 31 at 15; UW2 0-16, 31 at 14 and 9 at 15. | UW2: a critical failure (result 5) breaks the pick. |

The other static fields (heading, fine x and y, quality, next, owner, link) were already marked
confirmed and were not changed.

## Mobile object (27 bytes)

For a mobile object that is not a critter (missiles, thrown things), words 0x0B, 0x0D and 0x0F
hold its exact x, y and z instead (`motion/OBJPHYS.C`, file header), so the critter fields there
mean nothing for it; that explains UW2's missiles with "Bit 0D.11" set.

| Field | Bits | Meaning | Status | Evidence | UW1 / UW2 |
| --- | --- | --- | --- | --- | --- |
| Flags 9-12 | id bits 9-12 | Unused on critters. A thrown object or missile carries those of its source object. | confirmed | `combat/MISSILE.C`. 0 on every shipped critter. | Same. |
| Tenacious | id bit 13 | Never culled; a hostile critter with it accepts no danger on paths. Damage to critters goes through `damage_critter`, which does not test it. | confirmed | `chkTenacious`, `acceptable_danger`, `combat/DAMAGE.C damage_item`. UW1 4 (3 fighters), UW2 4. | Same. |
| Invisible | id bit 14 | See the static object and [question e](#e-invisible-hostile-monsters). | confirmed | 0 shipped. | Same. |
| Heading | pos bits 7-9 | Coarse facing; see [question a](#a-heading-heading-byte-and-fine-heading). | confirmed | | |
| Heading byte | 0x09 | Direction of travel, 0-255. | confirmed | `motion/OBJPHYS.C get_phys_data, set_phys_data`; `critter/AI.C constrain_movement`; `critter/CREATURE.C`. 603 of 657 UW1 critters ship with 0. | Same. |
| Update bin (was Frame counter) | 0x0A bits 0-3 | Timing slot 0-15; updated when the bin clock (16 a second) reaches it, then advanced by Rate. Far critters (over 10 tiles from player and camera) skip 8. | confirmed, renamed | `critter/AI.C move_mobile, critter_ai, timetodo`. | Same. |
| Terrain | 0x0A bits 4-6 | Footing after the last physics step (floor, water, lava ...), fed back to the next. | confirmed | `OBJPHYS.C get_phys_data, set_phys_data` (`res_to_terr`). UW1 values 0-4, 7. | Same. |
| Loner | 0x0A bit 7 | Not part of its race; see [question b](#b-loner). | confirmed | 177 of 657 UW1 critters (rats, lurkers, headless, bloodworms...); UW2 119 critters (cave bats, humans, imps) and 6 fireballs. | UW2 also uses it on missiles and in plot checks. |
| Anim frame | 0x0B bits 12-15 | Frame of the animation sequence. | confirmed | `3d/DRAWOBJ.C do_obj`; `critter/AI.C`. | UW1 4 frames a sequence; UW2 from `CRIT\CR.AN`. |
| Target height | 0x0D bits 4-7 | Destination floor height. | confirmed | `critter/PATHFIND.C set_loc`. | Same. |
| Summoned | 0x0D bit 8 | Temporary: removed when time passes for the level. | confirmed | `critter/CRITTIME.C up_crit`; `event/TRIGGER.C DoWanderingMonsters, dont_create_wandering_monster_here`. UW1 3 fire elementals; UW2 42. | UW2 also sets it on arena fighters and the castle's random monsters (`uw2 conv/BABLHACK.C`, `uw2 event/TRIGGER.C`). |
| No healing | 0x0D bit 9 | Does not heal when time passes. | confirmed | `up_crit` (`hp = (hp + max) / 2` otherwise); set by a conversation callback in `obj/USEITEMS.C`. UW1 24, UW2 27. | UW2 `WORLDEV.C` clears it. |
| Bit 0D.11 | 0x0D bit 11 | No effect: only cleared at creation. | unknown (unused) | Only `SET_B0D_11(.., 0)` in both `critter/CREATURE.C`. UW1: 13 critters, 36 in saves; UW2: none (14 missiles, part of y). | Same. |
| Inventory made | 0x0D bit 12 | Loot generated, once. | confirmed | `obj/TREASURE.C`; `conv/BARTER.C`, `conv/CONVERSE.C`. | Same. |
| Destination X, Y | 0x0F bits 0-11 | Where it is heading. | confirmed | `set_loc`. | Same. |
| Attack frame | 0x0F bits 12-15 | Melee wind-up counter (charge of the blow). | confirmed | `critter/AI.C` (`atk_charge[OBJ_ATKFRAME]`). | Same. |
| Damage taken | 0x11 | Damage since the last reaction; for fleeing. | confirmed | `damage_critter`, `should_i_flee`, `critter_mv`. | Same. |
| Last hit | 0x12 | Attacker since the last step; a missile's launcher. | confirmed | `critter_mv`, `damage_critter`, `combat/MISSILE.C`. | Same. |
| Speed, Gravity | 0x13 | Physics speed; falling. | confirmed | `get_phys_data` (`acc[2] = gravity * -4`). | UW2 gravity is any nonzero acceleration. |
| Rate | 0x14 bits 0-2 | Update interval (bin step). | confirmed | `move_mobile`. Critters ship with 4 or 6 (one with 3). | Same. |
| Pitch | 0x14 bits 3-7 | Vertical speed, (pitch - 16) * 64. | confirmed | `get_phys_data`. | Same. |
| Sequence | 0x15 bits 0-5 | Animation sequence. | confirmed | `do_obj`. | UW1 directional sequences are 0x20 and up; UW2 numbers them 0-7. |
| Bit 15.6 | 0x15 bit 6 | Standing still: the next physics step is skipped while speed is 0 and pitch 16. Cleared each step, set when the goal stands. | confirmed | `critter/AI.C critter_mv, crit_drunkwalk, constrain_movement`; `PATHFIND.C crit_head_for_loc`. UW1 21 critters (242 in saves); UW2 92. | Same. |
| Bit 15.7 | 0x15 bit 7 | Critter: following a stored path. Missile: has hit a mobile, passes through others. | confirmed | `PATHFIND.C crit_head_for_loc`; `motion/OBJPHYS.C`; `motion/COLLIDE.C ObjectCheck`. 0 shipped, 1 in saves. | Same. |
| Path index | 0x16 bits 0-3 | Path slot (of 16) while Bit 15.7 is set. | confirmed | `crit_head_for_loc`. | Same. |
| Fine heading | 0x18 bits 0-4 | Low 5 bits of the facing. | confirmed | `combat/COMBAT.C resolve_attack`, `combat/SPELLS.C`, `combat/MISSILE.C`. | Same. |
| Bit 18.5 | 0x18 bit 5 | New destination this step; drops the stored path. | confirmed | `set_loc`, `critter_mv`. UW1 433 of 657, left from the last step before the level was saved. | Same. |
| Bit 18.6 | 0x18 bit 6 | Gave up on the destination; wanders, forgets 1 step in 8. | confirmed | `crit_head_for_loc`. | Same. |
| Bit 18.7 | 0x18 bit 7 | Straight line to the destination is clear. | confirmed | `crit_head_for_loc`. | Same. |
| Bit 19.0 | 0x19 bit 0 | Knows where its target is. | confirmed | `crit_guard`, `target_found`, `critter_mv`; cleared in `up_crit`. | Same. |
| Bit 19.1 | 0x19 bit 1 | Heard something. | confirmed | `crit_guard`. | Same. |
| Casting | 0x19 bits 2-3 | Spell slot 1-3 about to be cast. | confirmed | `critter/AI.C`. | Cast on frame 4 (UW1) or 3 (UW2). |
| Bit 19.4 | 0x19 bit 4 | Cornered: fights on when hit instead of fleeing. | confirmed | `crit_flee`, `critter_mv`. | Same. |
| Bit 19.5 | 0x19 bit 5 | Committed to the attack. | confirmed | `critter_mv`, and the guard return in `critter/AI.C`. | Same. |

The names "Bit 15.6" to "Bit 19.5" were kept so the layout and the code that looks fields up by
name do not change; the tooltips now start with the meaning. Byte 0x19's bit meanings agree with
`docs/behaviour/npc-ai.md` of underworld-exhumed, which calls them inferred from use.

## Other tables touched

- COMOBJ.DAT bit 3.2: **creatures walk through it** (`motion/COLLIDE.C ObjectCheck` skips it for a
  moving creature). Set on 0x161, 0x162 and 0x170-0x17F in both games. Bits 3.0, 3.4, 7.6 and
  9.6-7 are read by nothing and clear everywhere; bits A.5-7 are read by nothing, 3 in every UW1
  entry and 0 in every UW2 one. Tenacity: the culling rule, see below.
- OBJECTS.DAT creature record: hearing, sight, noise, visibility, alertness, range and locks are
  now described from `critter/AI.C` and `critter/PATHFIND.C`; laziness and passive stay probable.
  Byte 0x2F is read by nothing (0x49 in every UW1 creature, 0x65 in every UW2 one). Death, blood,
  corpse, remains, sound, armour and weapon kind were not reviewed.

## Questions

### a. Heading, heading byte and fine heading

A mobile object has two directions.

- The **facing**, the way a critter looks, is `Heading * 32 + Fine heading`, 0 to 255. Heading
  (bits 7-9 of word 1) is its top 3 bits and Fine heading (byte 0x18 bits 0-4) its low 5. The AI
  writes both together (`SET_HEADING(meptr, head >> 5); SET_FINEHEAD(meptr, head)`,
  `critter/AI.C crit_drunkwalk`). The coarse part draws the sprite angle (`3d/DRAWOBJ.C do_obj`)
  and limits sight to 45 degrees either side (`target_found`); the full value aims blows
  (`combat/COMBAT.C resolve_attack`), spells and missiles.
- The **Heading byte** (0x09) is the direction of travel. The physics moves the object along it
  (`get_phys_data`: `pp->heading = obj->heading << 8` for mobile objects, the 3-bit heading for
  static ones) and writes it back (`set_phys_data`). Usually the AI sets it equal to the facing,
  but `constrain_movement` limits the two turns separately (45 degrees a step each, and a turn of
  more than 90 degrees in travel stops the critter), and a bump can change travel alone. For a
  missile it is the flight direction. A new critter gets `Heading << 5` (`critter/CREATURE.C`).

A static object has only the 3-bit Heading.

### b. Loner

Bit 7 of byte 0x0A cuts a critter off from its race (`docs/behaviour/npc-ai.md` agrees):

- When the player hits a loner, the game does not record the hit, so its kin do not come
  (`critter/AI.C damage_critter`: `if (who == 1 && !OBJ_LONER(obj))`).
- A loner does not join a fight of its race (`critter_mv`).
- When time passes for the level (sleep, leaving the level), a loner does not count towards its
  race's mood and does not drift with it (`critter/CRITTIME.C up_crit` and the loop after it).
- The conversation built-in `set_race_attitude` skips loners (`conv/BABLHACK.C`); UW2's
  conversations can set or clear the bit (`set_me_loner`).
- Theft: loners are told only when the owner value has bit 0x20 set, and owner 0x20 itself tells
  only loners (`CRITTIME.C critter_get_told`).
- UW2 only: plot checks on the jail guards of level 1, the pits' fighters and the schedules'
  `gronk_race` skip loners; arena fighters and the castle's random monsters are made loners; on a
  missile the bit means it has hit a creature, and a homing dart or satellite then expires
  (`uw2 motion/OBJPHYS.C`, `uw2 critter/PATHFIND.C`).

A critter that is not a loner behaves as a member of its race in all of these.

### c. Tenacious and culling

"Culling" is the game deleting objects to make room: `Obj_GarbageCollect` deletes objects on
tiles far from the player (taxicab distance over 10 - range) when the object store runs out
(range 3, from `Obj_Alloc`) and when the player sleeps (range 1, 20 objects, `game/SKILLS.C`);
`Obj_Punt` with range 10 decides whether a destroyed item, or a moving object that comes to rest,
is deleted. An object survives when `chkTenacious` says so: its id bit 13 is set, or its COMOBJ
tenacity plus half of (quantity - 1) is above the range (plus 0-2 at random); a container holding
such an object survives too (`obj/OBJECTS.C Obj_Elem_Fate`). So the Tenacious bit means "never
deleted to make room". It has nothing to do with chasing.

Separately, critters are **frozen**, not deleted, when far away: a critter more than 10 tiles
from both the player and the camera does nothing that step (`critter/AI.C critter_ai`), unless its
goal is 3 (follow, UW1). Tenacious does not change that.

On a creature the bit also makes `acceptable_danger` return 0, so a hostile tenacious creature
refuses path steps with drops, lava or jumps, like a peaceful one.

Creatures never change level. `do_teleport` refuses to move anything but the player to another
level (`event/WORLDEV.C`: `if (dest != PlayerLevel && who != ThePlayer) return 2`). When the
player leaves, `ChangeLevel` saves the level after `do_level_hacks(from, 1)` has run
`update_all_critters_whilst_player_snoozes`: every critter on it forgets its target, is put back
on its home square, may heal, and summoned ones are removed (`game/GAMEWRAP.C`,
`critter/CRITTIME.C`). So a monster chasing the player stops at the ladder and is home again when
he returns. "Never culled" does not mean it searches the level for the player either: hunting
depends on its goal, perception (sight, hearing) and the freeze distance.

### d. Creatures opening and picking doors

A walking critter that bumps into a closed door while heading somewhere (`crit_head_for_loc`)
stops, gives up one time in four, and otherwise calls `try_to_open_door` (`critter/PATHFIND.C`):

1. Secret doors (item id ending in 7) are never touched.
2. **UW1:** a critter whose attitude is not hostile gives up instead. **UW2:** no critter tries on
   level 10.
3. A creature whose OBJECTS.DAT **locks** value (byte 0x2E) is nonzero uses the door like the
   player (`UseObj` then `UseRect`): it opens unless the lock is locked (`checkLock` with no key
   returns 0) or the door is spiked (owner bit 0; only the player opens a spiked door).
4. On a closed door it then, half the time, picks the lock: `checkLock(meptr, door, -locks)`,
   the same skill check as the player's pick against the lock's Z * 3. A picked lock sets off the
   door's unlock trigger (with the critter as who, so only an enchanted trigger fires) and is removed, or only unlocked with Flag 10.
5. Otherwise (no locks value, or the other half), one time in four it bashes the door for
   `rand() % (damage of attack 1)` physical damage (`damage_item`). A door with the Tenacious bit
   (UW1) or quality class 3 cannot be damaged; a broken door swings open and loses its locks
   (`combat/DAMAGE.C remove_object`).

Path finding: UW2's `hyp_move` treats a door locked against the critter as a wall across its
frame; UW1's does not test locks. Shipped lock values: UW1 goblins, skeletons, ghosts and golems
1, trolls and ghouls 2 (the dark ghoul 3), lizardmen 3, mountainmen 4, imps 5, outcasts 6, fighters 7 (one 14), mages
8-15, the wisp 30, creature 0x7B 20; 22 UW1 creatures have 0 (rats, bats, spiders, slugs and
others) and only bash. UW2: most humanoids 1, the spectre, the haunt and one liche 45, another liche 60. Nothing
else (goal, flags) controls it beyond the attitude rule in UW1.

### e. Invisible hostile monsters

The object's **Invisible bit** (id bit 14) only stops drawing: `3d/DRAWOBJ.C do_obj` returns
before the object is put in the picking buffer, so it cannot be seen or clicked (looked at, talked
to by clicking, used). No collision (`motion/COLLIDE.C`), combat (`combat/COMBAT.C`) or AI
(`critter/*.C`) code tests it. So an invisible hostile monster:

- **can hit the player**: its AI and its blows (`critter_attack`, a probe ahead of it) work as
  usual;
- **can be hit by the player**: the player's swing finds targets with the same probe
  (`resolve_attack`, `set_hitobj`), missiles and area spells also find it; the player just has
  to aim blindly;
- **blocks movement**: `ObjectCheck` collects it like any object.

No shipped mobile object has the bit. It is cleared when an object starts moving (`mob_init`).

The **Invisibility spell** is different: it affects the player only. It takes 16 off the
visibility nibble of the player's creature entry (`game/PLAYDATA.C parse_spells`, applied in
`motion/PLAYMOVE.C set_sound`), so a critter's sight range (`sight * visibility / 16`) becomes 0.
Critters can still hear him (`target_found` uses noise), and one that was hit knows where he is.
No spell makes a monster invisible; only the Invisible bit does (set in an editor, or by UW2's hack trap 39).
