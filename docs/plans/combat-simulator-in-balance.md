# Plan: the combat simulator's features in Tools ▸ Balance

Source: `..\UnderworldCombatSimulator` ("Stygian Abyss Odds" on `main`, "Labyrinth of Worlds Odds"
on `uw2`, HEAD 4bc0dbe). Target: the editor's Balance tools (`src/UwEditor.Core/Balance`,
`src/UwEditor.App/*Balance*`, about 2,000 lines today: Barter simulator, Weapons, Armour and rings).

Plan written 2026-11-07. Nothing here is built yet.

## What the site has that the editor lacks

| Site | Editor today |
|---|---|
| Character panel: attributes, 20 skills, level, HP/mana, equipment 0–10 with quality and enchantment, rune bag, buffs (UW1 8, UW2 10) with cast odds, easy mode, lefty, PLAYER.DAT load | `BalanceUi` stats panel: STR, DEX, skills, PLAYER.DAT load (no equipment, runes, buffs, easy mode) |
| Skills tab: 19 (UW1) / 24 (UW2) `skill_check` call sites with exact n/32768 odds, the 31 scores, P(pass)-vs-value curve, value × target heatmap, pinned scenarios, per-check details | Nothing |
| Combat tab, one enemy: any monster of LEV.ARK, melee or ranged, N fights, outcomes with 95% Wilson intervals, fight length, HP left, damage dealt and received by source and body part, monster attacks, wear and breakage, casting, special weapons, XP, exact one-blow distribution | Weapons window: expected damage per swing and per second at full charge (no fight) |
| Combat tab, all enemies: every combat-capable monster, win probability by level, "beaten at ≥ p" curve, expected XP | Nothing (MonsterSwap has only a heuristic strength) |
| Exact random model: Borland `rand()` with its `% n` imbalance, checked against vectors measured on UW2.EXE | `BorlandRandom` (same LCG, no state access or jump-ahead); `Dice` uses ideal weights |

## What the editor can do that the site cannot

These are the reasons to port rather than link:
- **It follows your edits.** The tables come from Object tables (unsaved edits included) and the monsters from the open levels (`vm.Edits`). Change a creature's armour, save nothing, run again.
- **Before and after.** Run the same scenario on the shipped tables (`.ORIG`) and on the edited ones, side by side. This is the balance question a modder asks: "did my change make level 3 too hard?"
- **It knows the game you opened**, both UW1 and UW2, from one code path. The site needs two branches.
- **From the map.** "Fight this creature" from the selected object, and "Use as the character" from the open save.

## Discrepancies to settle first (phase 0)

The inventory found places where the editor's existing Balance code and the site disagree, and
the game's code settled one where both were wrong (item 8). Settle each against the game's source (underworld-exhumed) before porting, so the two engines don't disagree inside one program:

1. **Swing time.** `WeaponBalance.SwingTicks` gives a UW1 longsword 1.56 s; the site's cycle gives 1.75 s, with the first blow at about 1.375 s. The editor also lacks the sword-bash delay and the time to the first blow.
2. **Monster blow charge.** `ArmourBalance.Taken` uses full charge (128). The site scales by `ATK_CHARGE[k]` (mean about 81), so the editor overstates monster damage and misstates "Saved %". The editor also ignores the powerful +7..12 to hit and +4..15 damage.
3. **Hit location.** The editor averages a creature's four zone armours before the clamp. The site weights zones by `pickloc` (swing cell, COMOBJ heights) and clamps per zone.
4. **UW2 weapon effect 15.** The editor gives +5 damage, reading `spec_damage` one past its table; the site gives nothing. Check COMBAT.C.
5. **Luck (0x31).** The editor's text says "+3 to the attacker's to-hit"; the logic is −3. Probably a sign typo.
6. **Easy mode.** The editor halves the expected value; the game halves each blow, rounding down.
7. **Creature byte 0x0A bit 1.** The editor's schema says "never summoned"; the site says "ignores being hit" (the Wisp). Both may be true: name both.
8. **Armour spells add nothing** (checked in UW.EXE and UW2.EXE, `parse_spells` at ovr133:065D
   and ovr142:0615). Resist Blows, Thick Skin and Iron Flesh keep their level in bits 4–7 of the
   bonuses, but `parse_spells` shifts the bonuses right four times in its stealth loop before
   reading them, so it adds bits 8–11: always 0. The site's buffs give 2, 3 and 5 armour; the
   port must give 0 (and the site should be corrected). Since 2026-11-07 the Armour window
   shows both, actual (Armour, Blow) and intended (Intended, Int. blow), as it does for
   Toughness on a shield or ring; the simulator should offer the same choice (the game as
   shipped, or with the slips fixed, as an Exhumed enhancement would play it).
9. **Barter likes** (found while writing the conversation recipes). The Barter simulator reads likes from any `set_likes_dislikes` call. In slots 6, 276 and 278 that call sits in a function nothing calls, and Zak (slot 28) makes it only on the first visit, while the game clears likes at every conversation start. The simulator should use only calls reachable from `main`, and say when a call is first-visit only.

Each item gets a test that pins the game's behaviour.

## Architecture

### Core: `src/UwEditor.Core/Balance/Combat/` (new, no UI)

One port of the site's engine, with game differences behind a `CombatRules` object (UW1/UW2) instead of two branches.

| New file | Ported from | Notes |
|---|---|---|
| `GameRandom.cs` | `core/rng.ts`, `randdist.ts` | Extends `BorlandRandom`: a state you can read and set, `Jump(n)` jump-ahead, `rand() % n`, `rollem`; exact weights (n/32768); an "ideal" model kept for comparison |
| `SkillCheck.cs` | `core/skillcheck.ts`, `ui/skills.ts` catalogue | `skill_check`, exact grade counts; the call-site catalogue as data (name, skill, target formula, parameter, game) |
| `CombatCharacter.cs` | `core/character.ts`, `playerdat.ts` | `FixPlayerEquips` / `player_affected_by`: zone armour, protection, resistances, defence, buffs, `levelFromExp`. Built from `PlayerSheet` |
| `Blow.cs` | `combat/blow.ts` | `pickloc`, dice, critical, creature armour, `check_res`, `is_sharp`, `should_i_flee`, `ATK_CHARGE`; pure functions taking the random source |
| `CombatSetup.cs` | `combat/setup.ts` | Player, melee and monster profiles; launcher; attack spells; area-spell geometry; best swing cell; enemy HP modes |
| `Fight.cs` | `combat/fight.ts` | Event loop in 1/16 s ticks (jumps to the next event): swings and shots, monster AI steps, casting, wear and breakage, special weapons, flee, mana regeneration, kill XP |
| `ExactBlow.cs` | `combat/exact.ts` | Exact one-blow distributions, player and monster |
| `FightStats.cs` | `combat/stats.ts` | Summed arrays, merge, Wilson intervals |
| `FightRunner.cs` | `pool.ts`, `worker.ts` | `Parallel.For` over **fixed-size** jobs (500 fights), each starting at a jump-ahead of the seed. Unlike the site, the same seed gives the same result on every machine. Cancellation and progress |
| `EnemyRoster.cs` | the extractor's `levels[]` | Monsters of the open levels (unsaved edits included): creature × HP × powerful × attitude rows, placed or trap-spawned (with spawn odds) |

### Data: new readers

| Data | Today | To add |
|---|---|---|
| Weapons, missiles, armour, creatures, common properties | `ObjectTables` | Nothing: read through `BalanceUi.Tables` (so edits count) |
| Creature sequence lengths (CR.AN byte 7, via AS.AN) | `Graphics/CritterArt` reads CR.AN for art | An accessor for the lengths (UW2; UW1 uses fixed 5 steps) |
| WEAP.DAT swing frames (UW2) | Not read | A small reader (8 × 0x61 bytes) |
| Spell table (circle, class, flags, runes, missile) | Not in a file: compiled into the EXE (SPELLS.C) | Embedded JSON per game, like `docs/facts`, with names from STRINGS block 6 (as `MonsterSwap.SpellDamage` does) |
| PLAYER.DAT: rune bag (0x44–0x46), shelf (0x47), easy mode (UW1 0xB4, UW2 0x301), enchantments of inventory items | `PlayerDat`, `PlayerSheet`, `PlayerInventory` (attributes, skills, slots, lefty) | The missing fields, and `ObjectSpell.Of` for an inventory item |

### UI: Tools ▸ Balance

- **Character** (one shared panel, replacing the stats part of `BalanceUi`): everything on the site's character panel, plus **From the open save** and **From PLAYER.DAT…**. It is kept in the settings per game and shared by every Balance window, so a change in one shows in the others.
- **Skill checks…** (new window): the catalogue, bars, the 31 scores, curve, heatmap, pinned scenarios, details.
- **Combat simulator…** (new window), two tabs:
  - **One enemy**: picker (world, level, row); **Fight this creature** from the main window's selection; settings; the exact one-blow card before any run; results cards and charts.
  - **All enemies**: the same filters and charts, with a clickable table that opens a row on One enemy.
  - **Compare**: run the same scenario on the shipped tables and on the edited ones, with the differences highlighted (win rate per level, XP, one-blow mean).
- **Weapons** and **Armour and rings** use the new engine for their numbers (after phase 0) and gain **Simulate against…**, which opens Combat with that weapon or armour equipped.
- **Charts**: extend the hand-drawn `DistributionWidgets` (`BarsEditor`, `CurveEditor`) with read-only histograms, stacked bars, a strip chart and a heatmap, in the shared look (light and dark). No new package.
- Long runs run in the background with a progress bar and Cancel. Results are kept until the inputs change.

## Phases

| Phase | Content | Size | Done when |
|---|---|---|---|
| 0 | The discrepancies above, each with a test | S–M | Weapons, Armour and Barter agree with the game's source; tests pin each rule |
| 1 | `GameRandom`, `SkillCheck`, `Blow`, `ExactBlow` + vector tests | M | Every CSV vector of the site replays exactly; exact distributions sum to 1 |
| 2 | `CombatCharacter` + PLAYER.DAT fields + the shared Character panel | M | The site's PLAYER.DAT fixtures give the same derived values (zone armour, defence, resistances) |
| 3 | **Skill checks** window | M | Same odds as the site for every call site, both games |
| 4 | `CombatSetup`, `Fight`, `FightStats`, `FightRunner`, `EnemyRoster` (UW2 rules first, then UW1) | L | The site's combat test suite ported and passing; same seed gives the same stats on any core count |
| 5 | **Combat simulator** window: One enemy, then All enemies | L | Every card of the site; Fight this creature from the map |
| 6 | **Compare** (shipped against edited), Weapons/Armour on the engine, **Simulate against…** | M | A creature edit in Object tables shows in Compare without saving |
| 7 | Manual (a Balance chapter section per window, with the model's limits), README, release | S | Manual describes each window; limits listed as the site's README does |

Rough total: phases 0–3 are a session or two; 4–5 are the bulk (the fight loop is 584 lines of TypeScript plus 416 of UI); 6–7 a session.

## Tests (reuse the site's assets)

- **EXE vectors** (MIT, measured on UW2.EXE, from underworld-exhumed): `test/vectors/skill_check.csv` (300 rows), `rollem.csv` (300), `pickloc.csv` (300), `check_res.csv` (400). Copy into `tests/Shared/vectors` with their README and licence; replay them call for call (state in, result and state out). UW1 vectors: look for them in exhumed's `uw1/`, else the C harness below covers UW1.
- **C harness** (`test/c/harness.c`, verbatim game C with rand() enumerated): run it once per game and commit its JSON output as a fixture, so the C# tests need no C compiler.
- **PLAYER.DAT fixtures** with expected JSON: UW2 `player-karl.dat`; UW1 `player-empty.dat` and `player-karl.dat` (on `main`). Also the editor's own `tests/gamedata` saves.
- **The site's suites** (`test/combat.test.ts`, `core.test.ts`), ported as properties:
  - exact distributions sum to 1, and the simulated mean matches the exact mean (z-test);
  - outcomes sum to N, damage dealt equals HP lost, and the same seed gives the same result;
  - a better weapon or more armour never lowers the win rate;
  - known cases: a 1-HP monster dies at the first blow, slingers fire at 3 tiles but not 4, Fireball is refused in Britannia, Flame Wind cannot reach in melee, a broken shield refits;
  - WEAP.DAT frames: slash 3/6/9, stab and fist 1/4/7;
  - a monster's attack recurs every seq_len + 4 AI steps.
- **App tests**: windows open on both games, one run with a fixed seed shows the expected cards, and Compare shows a change made in Object tables.

## Model limits (to state in the manual)

Taken from the site, as it does: no movement, closing in, line of sight or projectile flight (a hit-chance setting stands in); no healing, potions or regeneration in a fight; buffs last the whole fight and never fail; no summons, Paralyze, Charm or Fear; no poison on the player; one enemy at a time; the player always swings at full charge and never flees.

## Decisions for Karl

1. **Which game first** for the fight loop: UW2 (the site's newest, with EXE vectors) or UW1 (the randomizer's game)? The plan says UW2 rules first, since they are the better checked.
2. **The site's future.** Keep the site as the public, no-install version, with the editor as the modder's version? If so, the C# engine and the TS engine should share the vector files and the test list, so they stay in step.
3. **The Compare baseline.** Compare against the shipped files (`.ORIG`) or against the last saved files? The plan says shipped files, with a choice.
4. **Phase 0 changes the numbers** the Weapons and Armour windows show today, which may surprise someone who used them. Release them as fixes, with a line in the release notes.
