namespace UltimateEditor.Uw1
{
	/// <summary>
	/// The UW1 record layouts with the meaning of every field, taken from the
	/// underworld-exhumed reference (uw1/src/include/*.h and docs/FORMATS.md). Source paths
	/// are relative to uw1/src in that repository.
	/// </summary>
	public static class Uw1Schemas
	{
		const Certainty C = Certainty.Certain;
		const Certainty I = Certainty.Inferred;
		const Certainty U = Certainty.Unknown;

		static FieldDef F(string name, int offset, int bit, int bits, string desc, string src = null, Certainty c = C, bool signed = false)
		{
			return new FieldDef(name, offset, bit, bits, desc, src, c, signed);
		}

		/// <summary>The first 8 bytes of every object (struct StaticObj, include/object.h).</summary>
		public static readonly RecordSchema StaticObject = new RecordSchema("Object (static part)", 8, "include/object.h struct Object, StaticObj",
			F("Item id", 0, 0, 9, "Item id: major class bits 6-8, minor class 4-5, index 0-3 (include/items.h).", "include/object.h ID_ITEM"),
			F("Flag 9", 0, 9, 1, "Lock (0x10F): set while locked. Picking it or using its key unlocks it (clears the bit, or removes the lock without Flag 10); using the key on an unlocked lock locks it again. UW2 trigger: objects other than the player and creatures (thrown items, missiles) set it off only with this bit; UW1 triggers ignore it. For some kinds bits 9-12 together are one number, 0-15: a trap's count of the triggers that point at it; a door's swing (bits 9-11 how far open, bit 12 the direction while it moves); a lever's or switch's position 0-7 (0x161, 0x162); the texture of a bridge or other 3D model; the text offset of a writing or gravestone.", "obj/USEITEMS.C checkLock; uw2 obj/OBJUSE.C checkLock; uw2 event/TRIGGER.C UseTrigger; event/TRIGGER.C trigger_obj_del; obj/EFFECT.C; 3d/DRAWOBJ.C; obj/LOOK.C"),
			F("Flag 10", 0, 10, 1, "Book or scroll: reading it plays cutscene 0x100 + its text number instead of showing its text. Trigger: reusable; without it the trap chain is deleted after it runs. Lock: stays on the door after unlocking (only marked unlocked, so its key can lock it again); without it the lock is removed. Part of the 4-bit value of bits 9-12 on traps, doors, levers, bridges and writings (see Flag 9).", "obj/LOOK.C BookLook; event/TRIGGER.C UseTrigger; obj/USEITEMS.C checkLock"),
			F("Flag 11", 0, 11, 1, "Trigger: the player may set it off. Spell object (0x120) or an enchanted is-quantity object: the enchantment is a spell that is cast (potions, wands, scrolls; decoded as link bits 6-8 + 12 and 0-5), and a spell object's quality is its charges, shown when looked at; without it the link is a lasting enchantment (bits 4-8 and 0-3). Part of the 4-bit value of bits 9-12 (see Flag 9).", "event/TRIGGER.C UseTrigger; obj/USEITEMS.C decode_obj_spell, useNSpellCharges; obj/LOOK.C"),
			F("Enchanted", 0, 12, 1, "An is-quantity object with it: the link (less 0x200) is its enchantment. Trigger: creatures may set it off; in UW1 an enchanted trigger without Flag 11 is not set off by other objects. Part of the 4-bit value of bits 9-12 (see Flag 9; on a moving door the way it moves).", "include/object.h ID_ENCHANT; obj/USEITEMS.C decode_obj_spell; event/TRIGGER.C UseTrigger"),
			F("Tenacious", 0, 13, 1, "UW-Formats calls it doordir. It does several things. Never culled: when the object store runs out, when the player sleeps, and when an item is destroyed or comes to rest, the game deletes objects far from the player at random, but never one with this bit or a container holding one (without it the item's COMOBJ tenacity decides). It cannot be damaged (UW1 any object; UW2 any object but a door). A door: the side it swings open to. A hostile creature with it accepts no danger on its paths (no drops, lava or jumps), as a peaceful one does. It does not make a creature chase the player or follow him to another level. The game sets it on the player, on traps and triggers laid by spells and on the silver tree; UW2 also uses it to mark a few plot items (Guardian signet rings on the lines of power, book 0x138, the telekinesis wand).", "obj/OBJECTS.C chkTenacious, Obj_Elem_Fate; combat/DAMAGE.C damage_object; 3d/DRAWOBJ.C do_door; critter/AI.C acceptable_danger"),
			F("Invisible", 0, 14, 1, "Not drawn in the 3D view, so it cannot be clicked (looked at, used, picked up or talked to); hidden in container panels. It still collides and blocks, can be hit, and a creature with it still attacks: no collision, combat or AI code tests the bit. When it becomes a moving object (thrown, falling) the bit is cleared. This is not the Invisibility spell, which lowers the player's visibility to creatures. UW2's hack trap 39 sets or clears it.", "3d/DRAWOBJ.C do_obj; inv/BAGS.C; motion/OBJPHYS.C mob_init; uw2 event/TRIGGER.C"),
			F("Is quantity", 0, 15, 1, "The link field is a quantity (below 0x200) or a special property (0x200 and up), not an object.", "include/object.h ID_ISQUANT"),
			F("Z", 2, 0, 7, "Height in the tile, 0-127 (8 units per floor step). A lock (0x10F): its difficulty to pick, a skill check against Z * 3; 14 needs a skill of 31 (UW2 32), 15 cannot be picked. A look trigger: its Search difficulty.", "include/object.h POS_Z; obj/USEITEMS.C checkLock"),
			F("Heading", 2, 7, 3, "Facing, in eighths of a turn.", "include/object.h POS_HEADING"),
			F("Fine Y", 2, 10, 3, "Y inside the tile, 0-7.", "include/object.h POS_YFINE"),
			F("Fine X", 2, 13, 3, "X inside the tile, 0-7.", "include/object.h POS_XFINE"),
			F("Quality", 4, 0, 6, "Quality 0-63; its meaning depends on the item (see the role list).", "include/object.h qn.f.quality"),
			F("Next", 4, 6, 10, "Next object in the same list (tile or container), 0 for the end.", "include/object.h qn.f.next"),
			F("Owner", 6, 0, 6, "Owner 0-63; its meaning depends on the item (see the role list).", "include/object.h ol.f.owner"),
			F("Link", 6, 6, 10, "Contents (first contained object) or, with Is quantity, a quantity / special property.", "include/object.h ol.f.link"));

		/// <summary>A mobile object: the 8 static bytes plus 19 (include/object.h).</summary>
		public static readonly RecordSchema MobileObject = new RecordSchema("Object (mobile)", 0x1B, "include/object.h struct Object",
			F("Item id", 0, 0, 9, "Item id (include/items.h).", "include/object.h"),
			F("Flags 9-12", 0, 9, 4, "Critters: unused (0 in every shipped critter). A thrown object or missile carries the flags of the object it was made from. See the static object for what they mean by kind.", "include/object.h; combat/MISSILE.C"),
			F("Tenacious", 0, 13, 1, "As on a static object: never culled, and no damage through damage_object (critters are damaged through damage_critter, so it does not protect them). A hostile critter with it accepts no danger on its paths (no drops, lava or jumps). It does not stop the critter from freezing when far from the player, and does not make it follow the player.", "obj/OBJECTS.C chkTenacious; critter/AI.C acceptable_danger"),
			F("Invisible", 0, 14, 1, "Not drawn and cannot be clicked, but it still blocks, can be hit and attacks (see the static object). No shipped mobile object has it.", "3d/DRAWOBJ.C do_obj"),
			F("Is quantity", 0, 15, 1, "Link is a quantity / property.", "include/object.h"),
			F("Z", 2, 0, 7, "Height in the tile.", "include/object.h"),
			F("Heading", 2, 7, 3, "Facing in eighths of a turn: the top 3 bits of the way the critter looks. Facing = Heading * 32 + Fine heading (0x18), 0-255. The sprite's angle and the sight cone (45 degrees either side) use this coarse part.", "include/object.h POS_HEADING; critter/AI.C target_found; 3d/DRAWOBJ.C"),
			F("Fine Y", 2, 10, 3, "Y in the tile.", "include/object.h"),
			F("Fine X", 2, 13, 3, "X in the tile.", "include/object.h"),
			F("Quality", 4, 0, 6, "Critter: its home tile X as the conversations see it (npc_xhome).", "conv/CONVVARS.C"),
			F("Next", 4, 6, 10, "Next object in the tile list.", "include/object.h"),
			F("Owner", 6, 0, 6, "Critter: its home tile Y as the conversations see it (npc_yhome).", "conv/CONVVARS.C"),
			F("Link", 6, 6, 10, "Critter: its inventory (first object).", "include/object.h"),
			F("HP", 8, 0, 8, "Current hit points.", "include/object.h hp"),
			F("Heading byte", 9, 0, 8, "The direction it moves in, 0-255 (256 to a turn), not the way it looks: the physics moves it along this heading at its Speed. The AI usually sets it together with the facing (Heading and Fine heading), but each step limits the two turns separately, and a bump can change it alone. A missile's flight direction. A new critter gets Heading * 32.", "motion/OBJPHYS.C get_phys_data, set_phys_data; critter/AI.C constrain_movement; critter/CREATURE.C"),
			F("Update bin", 0x0A, 0, 4, "Timing slot 0-15 (UnderworldGodot: NextFrame; this editor called it Frame counter). The object is updated when the game's bin clock, 16 steps a second, reaches it, and each update adds Rate. A critter more than 10 tiles from both the player and the camera does nothing: 8 is added instead (goal 3 excepted).", "critter/AI.C move_mobile, critter_ai"),
			F("Terrain", 0x0A, 4, 3, "The footing under it after its last physics step (floor, water, lava ...), handed back to the physics on the next.", "motion/OBJPHYS.C get_phys_data, set_phys_data"),
			F("Loner", 0x0A, 7, 1, "Not part of its race. Its kin do not come when the player hits it, and it does not come when the player hits one of them; it neither counts towards nor follows its race's drift in attitude when time passes; set_race_attitude in conversations skips it; when the player steals, loners are told only if the owner value has bit 0x20 (owner 0x20 itself tells only loners). UW2: a few plot checks skip it (the jail on level 1, the pits' guards, gronk_race schedule rows). UW2 missiles: set once the missile has hit a creature (a homing dart or satellite then expires).", "critter/AI.C critter_mv, damage_critter; critter/CRITTIME.C up_crit, critter_get_told; conv/BABLHACK.C; uw2 motion/OBJPHYS.C, critter/PATHFIND.C"),
			F("Goal", 0x0B, 0, 4, "AI goal: 0 stand, 1 go home, 2 wander, 3 follow, 4 guard, 5 attack, 6 flee, 7 stand, 8 mill about, 9 cornered, 10 talk to player, 11 flutter, 12 hover.", "include/critter.h GOAL_*"),
			F("Goal target", 0x0B, 4, 8, "Goal target: a mobile index, 1 the player.", "include/object.h OBJ_GTARG"),
			F("Anim frame", 0x0B, 12, 4, "Frame of the current animation sequence (UW1 sequences have 4). For a mobile object that is not a critter, word 0x0B is instead its x in 1/256 tiles.", "3d/DRAWOBJ.C do_obj; motion/OBJPHYS.C"),
			F("Saved goal", 0x0D, 0, 4, "Goal saved while another runs (UW-Formats 'npc_level').", "include/object.h OBJ_OLDGOAL"),
			F("Target height", 0x0D, 4, 4, "Floor height of the destination, set with Destination X and Y.", "critter/PATHFIND.C set_loc"),
			F("Summoned", 0x0D, 8, 1, "Temporary: removed when time passes for the level (sleep, leaving the level). Set on monsters made by wandering-monster create-object traps (no new one is made while one is within 4 squares) and, in UW2, on the arena fighters and the castle's random monsters.", "critter/CRITTIME.C up_crit; event/TRIGGER.C DoWanderingMonsters"),
			F("No healing", 0x0D, 9, 1, "Does not heal when time passes for the level (otherwise its hit points go half way back to the maximum).", "critter/CRITTIME.C up_crit; obj/USEITEMS.C"),
			F("Powerful", 0x0D, 10, 1, "Powerful: +7..12 to hit and +4..15 damage.", "combat/COMBAT.C critter_attack"),
			F("Bit 0D.11", 0x0D, 11, 1, "No effect found: neither game reads it; it is only cleared when a critter is created. 13 UW1 critters ship with it set. For a missile, word 0x0D is its y position instead.", "include/object.h OBJ_B0D_11; critter/CREATURE.C", U),
			F("Inventory made", 0x0D, 12, 1, "Its loot has been generated (generate_inventory), which is done once, the first time it is needed: on death, or when the player talks or trades with it.", "obj/TREASURE.C; conv/BARTER.C"),
			F("Talked to", 0x0D, 13, 1, "The player has talked to it.", "include/object.h OBJ_TALKEDTO"),
			F("Attitude", 0x0D, 14, 2, "0 hostile, 1 upset, 2 mellow, 3 friendly.", "include/object.h OBJ_ATTITUDE"),
			F("Destination X", 0x0F, 0, 6, "Destination tile X (where it is heading: home, a sound, the target).", "critter/PATHFIND.C set_loc"),
			F("Destination Y", 0x0F, 6, 6, "Destination tile Y.", "critter/PATHFIND.C set_loc"),
			F("Attack frame", 0x0F, 12, 4, "Melee wind-up counter, 0-15: within reach of its target it rises by one each step it holds the blow, and picks the blow's charge (atk_charge) when it strikes; reset after the blow.", "critter/AI.C"),
			F("Damage taken", 0x11, 0, 8, "Damage taken since it last reacted to a hit; should_i_flee weighs it against its hit points. Cleared after the reaction.", "critter/AI.C damage_critter, critter_mv"),
			F("Last hit", 0x12, 0, 8, "Who hit it since its last step (1 the player, else a mobile index); its next step reacts and clears it. For a missile or thrown object: who launched it, so a kill is credited.", "critter/AI.C critter_mv, damage_critter; combat/MISSILE.C"),
			F("Speed", 0x13, 0, 7, "Speed the physics moves it at (a critter's walking or running speed while it moves, 0 when it stands).", "motion/OBJPHYS.C get_phys_data"),
			F("Gravity", 0x13, 7, 1, "Falling: the physics applies gravity while it is set.", "motion/OBJPHYS.C get_phys_data, set_phys_data"),
			F("Rate", 0x14, 0, 3, "How often it is updated: Update bin advances by this each update (1 about 16 times a second, 4 about 4; the AI uses 4 moving or fighting, 6 standing, 7 frozen).", "critter/AI.C move_mobile"),
			F("Pitch", 0x14, 3, 5, "Vertical speed: (pitch - 16) * 64; 16 is level. Fliers and missiles.", "motion/OBJPHYS.C get_phys_data"),
			F("Sequence", 0x15, 0, 6, "Animation sequence (npc_animation): standing, walking, attacking, dying ...; with Anim frame it picks the sprite.", "3d/DRAWOBJ.C do_obj"),
			F("Standing still", 0x15, 6, 1, "Standing still: set when this step's goal made it stand; while it is set with speed 0 and pitch 16 the next physics step is skipped. Cleared at the start of every step.", "critter/AI.C critter_mv, crit_drunkwalk, constrain_movement"),
			F("On a path", 0x15, 7, 1, "Critter: it is following a stored path (Path index is the slot). Missile or thrown object: it has already hit a mobile object and passes through others.", "critter/PATHFIND.C crit_head_for_loc; motion/OBJPHYS.C; motion/COLLIDE.C ObjectCheck"),
			F("Path index", 0x16, 0, 4, "Which of the 16 shared path slots holds its path, while Bit 15.7 is set.", "critter/PATHFIND.C crit_head_for_loc"),
			F("Home Y", 0x16, 4, 6, "Home tile Y (where it goes back to).", "include/object.h OBJ_HOMEY"),
			F("Home X", 0x16, 10, 6, "Home tile X.", "include/object.h OBJ_HOMEX"),
			F("Fine heading", 0x18, 0, 5, "Low 5 bits of the facing, 0-31: facing = Heading * 32 + this. Blows, spells and missiles it aims go this way.", "combat/COMBAT.C resolve_attack; combat/SPELLS.C; critter/AI.C"),
			F("New destination", 0x18, 5, 1, "New destination: set when its destination changes (a stored path is dropped), cleared at the start of each step.", "critter/PATHFIND.C set_loc, crit_head_for_loc"),
			F("Gave up", 0x18, 6, 1, "Gave up on its destination (blocked, or no path): it wanders instead and forgets this one step in 8.", "critter/PATHFIND.C crit_head_for_loc"),
			F("Straight line clear", 0x18, 7, 1, "The straight line to its destination is clear: it walks straight at it.", "critter/PATHFIND.C crit_head_for_loc"),
			F("Knows target", 0x19, 0, 1, "Knows where its target is (saw it, was hit, or joined a fight): a hostile guard attacks at once. Cleared when time passes for the level.", "critter/AI.C crit_guard, target_found, critter_mv"),
			F("Heard something", 0x19, 1, 1, "Heard something: it keeps turning towards the sound, and forgets it with chance (15 - alertness) in 16.", "critter/AI.C crit_guard"),
			F("Casting", 0x19, 2, 2, "Which of its creature's three spells it is about to cast (1-3, 0 none): cast on the cast animation's blow frame (UW1 4, UW2 3), then cleared.", "critter/AI.C"),
			F("Cornered", 0x19, 4, 1, "Cornered: when hit it fights on (goal 9) instead of fleeing. Set when its flight is blocked or its nerve holds.", "critter/AI.C crit_flee, critter_mv"),
			F("Committed", 0x19, 5, 1, "Committed to the attack: set when hit from more than a tile away (with no ranged spell); it attacks again when hit and does not return to guarding when far from home.", "critter/AI.C critter_mv"),
			F("Ally", 0x19, 6, 1, "On the player's side.", "include/object.h OBJ_ALLY"),
			F("Fed", 0x19, 7, 1, "Fed (the conversation variable npc_hunger is 0x10 when set, 0xC0 when not).", "conv/CONVVARS.C"),
			F("Whoami", 0x1A, 0, 8, "Conversation slot to run; 0 uses the generic conversation 0x100 + creature index.", "conv/CONVERSE.C"));

		/// <summary>A tile of the level map (include/map.h struct Tile).</summary>
		public static readonly RecordSchema Tile = new RecordSchema("Tile", 4, "include/map.h struct Tile",
			F("Type", 0, 0, 4, "0 solid, 1 open, 2-5 diagonal open to SE, SW, NE, NW, 6-9 slope rising N, S, E, W.", "include/map.h enum TileType"),
			F("Floor height", 0, 4, 4, "Floor height, 0-15 (0x40 z units a step).", "map/MAP.C hgt_val"),
			F("Light", 0, 8, 2, "Tile light flag (bit 8 tested when the player moves); bit 9 unknown.", "include/map.h; motion/PHYSICS.C"),
			F("Floor texture", 0, 10, 4, "Index into the level's 10 floor textures.", "include/map.h"),
			F("No magic", 0, 14, 1, "Magic does not work here.", "include/map.h anti_magic_p"),
			F("Door", 0, 15, 1, "Keeps an open door open: DoClosingDoors (after sleeping, some events) never closes a door on this tile. Many shipped door tiles have it clear; it does not mean a door is here.", "event/TRIGGER.C DoClosingDoors"),
			F("Wall texture", 2, 0, 6, "Index into the level's 48 wall textures.", "include/map.h TILE_WALL"),
			F("First object", 2, 6, 10, "First object in the tile.", "include/map.h"));

		/// <summary>An animation overlay entry (include/object.h struct Anim, LEV.ARK block level + 8).</summary>
		public static readonly RecordSchema AnimOverlay = new RecordSchema("Animation overlay", 6, "include/object.h struct Anim; obj/EFFECT.C",
			F("Link low bits", 0, 0, 6, "Low six bits of the link word (kept as they are).", "include/object.h union Link", I),
			F("Object", 0, 6, 10, "The animated object; 0 ends the list.", "obj/EFFECT.C Anim_Load"),
			F("Frames left", 2, 0, 16, "Frames left, -1 for ever.", "include/object.h struct Anim", C, true),
			F("Tile X", 4, 0, 8, "Tile X.", "include/object.h"),
			F("Tile Y", 5, 0, 8, "Tile Y.", "include/object.h"));

		/// <summary>OBJECTS.DAT melee weapon table entry (include/object.h struct Weapon).</summary>
		public static readonly RecordSchema MeleeWeapon = new RecordSchema("Melee weapon", 8, "include/object.h struct Weapon; obj/HACK.C",
			F("Slash damage", 0, 0, 8, "Damage of a slash.", "include/object.h damage[0]"),
			F("Bash damage", 1, 0, 8, "Damage of a bash.", "include/object.h damage[1]"),
			F("Stab damage", 2, 0, 8, "Damage of a stab.", "include/object.h damage[2]"),
			F("Min charge", 3, 0, 8, "Minimum swing charge (editor: Unk1).", "include/object.h min_charge"),
			F("Speed", 4, 0, 8, "Swing speed (editor: Unk2).", "include/object.h speed"),
			F("Max charge", 5, 0, 8, "Maximum swing charge (editor: Unk3).", "include/object.h max_charge"),
			F("Skill", 6, 0, 8, "Skill used: 3 sword, 4 axe, 5 mace, 6 unarmed (the fist).", "include/object.h skill"),
			F("Durability", 7, 0, 8, "Durability, 255 unbreakable.", "include/object.h durability"));

		/// <summary>OBJECTS.DAT missile / launcher entry (include/object.h struct MissileInfo).</summary>
		public static readonly RecordSchema Missile = new RecordSchema("Missile or launcher", 3, "include/object.h struct MissileInfo",
			F("Damage", 0, 0, 8, "Damage.", "include/object.h damage"),
			F("Type", 1, 0, 8, "Missile type.", "include/object.h type"),
			F("Ammo / damage type", 2, 0, 8, "Launcher (0x18-0x1F): ammunition item - 0x10 (0 sling stone, 1 bolt, 2 arrow). Ammunition (0x10-0x17): minus its damage type (0x40 missile, 0x0B magic+fire, ...).", "combat/COMBAT.C:555", C, true));

		/// <summary>OBJECTS.DAT armour entry (include/object.h struct Armour).</summary>
		public static readonly RecordSchema Armour = new RecordSchema("Armour", 4, "include/object.h struct Armour",
			F("Protection", 0, 0, 8, "Protection.", "include/object.h"),
			F("Durability", 1, 0, 8, "Durability, 255 unbreakable.", "include/object.h"),
			F("Byte 2", 2, 0, 8, "Unknown: 4 body armour, Dragon Skin Boots and crowns; 5 leggings; 6 gloves; 7 other boots and hats; 0 shields and rings (measured). No reference code reads it.", "include/object.h b2", U),
			F("Category", 3, 0, 8, "0 shield, 1 body armour, 3 leggings, 4 gloves, 5 boots, 8 hat, 9 ring (paperdoll slot).", "inv/INVPANEL.C ItemFitsSlot"));

		/// <summary>OBJECTS.DAT container entry (include/object.h struct Container).</summary>
		public static readonly RecordSchema Container = new RecordSchema("Container", 3, "include/object.h struct Container",
			F("Capacity", 0, 0, 8, "Capacity in stones, 0 no limit.", "include/object.h"),
			F("Accepts", 1, 0, 16, "-1 anything; below 0x200 one item id; 0x200 runes, 0x201 missiles, 0x202 scrolls and the map, 0x203 food, 0x204 keys (UW2 only).", "include/object.h CONT_*", C, true));

		/// <summary>OBJECTS.DAT light entry (16 of them; lit lights 0x94-0x97 use entries 4-7).</summary>
		public static readonly RecordSchema Light = new RecordSchema("Light", 2, "include/object.h struct Light; game/PLAYTIME.C",
			F("Burn rate", 0, 0, 8, "How fast it burns down (editor: Duration); 0 never burns out.", "game/PLAYTIME.C:192"),
			F("Brightness", 1, 0, 8, "Light level it gives (SHADES.DAT row).", "game/PLAYDATA.C:395"));

		/// <summary>OBJECTS.DAT creature record, 48 bytes (include/critter.h struct Creature).</summary>
		public static readonly RecordSchema Creature = new RecordSchema("Creature", 0x30, "include/critter.h struct Creature",
			F("Armour head / level", 0, 0, 8, "Armour of hit zone 0; also read as the creature's level (ovr157). Editor: Level.", "include/critter.h armour[0]"),
			F("Armour zone 1", 1, 0, 8, "Armour of hit zone 1, 255 = use zone 0. Editor: Unk0_1.", "include/critter.h armour[1]"),
			F("Armour zone 2", 2, 0, 8, "Armour of hit zone 2. Editor: Unk0_2.", "include/critter.h armour[2]"),
			F("Armour zone 3", 3, 0, 8, "Armour of hit zone 3. Editor: Unk0_3.", "include/critter.h armour[3]"),
			F("Average HP", 4, 0, 8, "Average hit points (the player: maximum vitality). Editor: low byte of Health.", "include/critter.h avghit"),
			F("Strength", 5, 0, 8, "Strength; damage + strength / 5. Editor: high byte of Health (0 in every shipped creature).", "combat/COMBAT.C critter_attack"),
			F("Dexterity", 6, 0, 8, "Dexterity. Editor: Attack.", "include/critter.h attr[1]"),
			F("Intelligence", 7, 0, 8, "Intelligence. Editor: Unk1.", "include/critter.h attr[2]"),
			F("Death", 8, 0, 3, "Death effect.", "include/critter.h death", I),
			F("Blood", 8, 3, 2, "Blood on hit (editor: HitDecal 8 = blood 1).", "include/critter.h blood", I),
			F("Corpse", 8, 5, 3, "Corpse left (editor: high nibble shown as Remains).", "include/critter.h corpse", I),
			F("Race", 9, 0, 8, "Race, used for ownership and attitudes (editor: Species / OwnerType).", "include/critter.h race"),
			F("Passive", 0x0A, 0, 1, "Passive: a bad miss by the player against it does not wear his weapon (the only use found). Editor: part of Passiveness.", "combat/COMBAT.C frp_check", I),
			F("Bit 0A.1", 0x0A, 1, 1, "Never summoned by Summon Monster.", "combat/SPELLS.C creat_spell"),
			F("Remains", 0x0A, 2, 3, "What it leaves when killed.", "include/critter.h remains", I),
			F("Jumps", 0x0A, 5, 1, "Can jump while path-finding.", "critter/PATHFIND.C hyp_move"),
			F("Swims", 0x0A, 6, 1, "Swims.", "include/critter.h swims"),
			F("Flies", 0x0A, 7, 1, "Flies.", "include/critter.h flier"),
			F("Speed", 0x0B, 0, 8, "Walking speed. Editor: Unk2.", "include/critter.h speed"),
			F("Run", 0x0C, 0, 8, "Running speed. Editor: Speed.", "include/critter.h run"),
			F("Trade level", 0x0D, 0, 4, "Trading temper: wit (BARTER.C).", "include/critter.h level"),
			F("Shrewd", 0x0D, 4, 4, "Trading temper: shrewdness.", "include/critter.h shrewd"),
			F("Haggle", 0x0D, 8, 4, "Trading temper: haggling.", "include/critter.h haggle"),
			F("Patience", 0x0D, 12, 4, "Trading temper: patience.", "include/critter.h patience"),
			F("Poison", 0x0F, 0, 8, "Poison level its blows give the player.", "critter/AI.C:1155 critter_attack(..., mycst->b0F)"),
			F("Sound", 0x10, 0, 4, "Sound set (editor: part of MonsterType).", "include/critter.h sound", I),
			F("Armour kind", 0x10, 4, 2, "Kind of armour it wears.", "include/critter.h armour_kind", I),
			F("Weapon kind", 0x10, 6, 2, "Kind of weapon it uses.", "include/critter.h weapon_kind", I),
			F("Equip", 0x11, 0, 8, "Equipment value: half of it is added to hit. Editor: EquipmentDamage.", "combat/COMBAT.C critter_attack", C, true),
			F("Defence", 0x12, 0, 8, "Defence: the target of the player's to-hit roll. Editor: Unk4.", "combat/COMBAT.C:301", C, true),
			F("Attack 1 to-hit", 0x13, 0, 8, "Attack 1 chance to hit. Editor: Attack1Value.", "include/critter.h attacks[0].chance", C, true),
			F("Attack 1 damage", 0x14, 0, 8, "Attack 1 damage (also the damage done to doors).", "include/critter.h attacks[0].damage"),
			F("Attack 1 probability", 0x15, 0, 8, "Chance this attack is chosen, percent.", "include/critter.h attacks[0].prob"),
			F("Attack 2 to-hit", 0x16, 0, 8, "Attack 2 chance to hit.", "include/critter.h", C, true),
			F("Attack 2 damage", 0x17, 0, 8, "Attack 2 damage.", "include/critter.h"),
			F("Attack 2 probability", 0x18, 0, 8, "Attack 2 probability.", "include/critter.h"),
			F("Attack 3 to-hit", 0x19, 0, 8, "Attack 3 chance to hit.", "include/critter.h", C, true),
			F("Attack 3 damage", 0x1A, 0, 8, "Attack 3 damage.", "include/critter.h"),
			F("Attack 3 probability", 0x1B, 0, 8, "Attack 3 probability.", "include/critter.h"),
			F("Nerve", 0x1C, 0, 4, "Nerve: keeps fighting when 8 or more (1 in 256).", "critter/AI.C:604"),
			F("Range", 0x1C, 4, 4, "How far from home it roams, in tiles: a milling critter heads home beyond it, and an attacker that was guarding gives up the chase beyond twice it.", "critter/AI.C"),
			F("Noise", 0x1D, 0, 4, "How loud it is as a target: another critter hears it within hearing * noise / 16 tiles. The player's entry is rewritten in play from stealth and movement.", "critter/AI.C target_found; motion/PLAYMOVE.C set_sound"),
			F("Visibility", 0x1D, 4, 4, "How visible it is as a target: another critter sees it within sight * visibility / 16 tiles. The player's entry is rewritten in play from stealth, and the Invisibility spell takes 16 off it (to 0).", "critter/AI.C target_found; game/PLAYDATA.C parse_spells"),
			F("Hearing", 0x1E, 0, 4, "Hearing: it hears a target within hearing * the target's noise / 16 tiles, and joins a fight of its race within this many tiles (|dx| + |dy|).", "critter/AI.C target_found, critter_mv"),
			F("Sight", 0x1E, 4, 4, "Sight: it sees a target within sight * the target's visibility / 16 tiles, in a 45-degree cone each side and with a line of sight; also how far it notices a theft.", "critter/AI.C target_found; critter/CRITTIME.C critter_get_told"),
			F("Laziness", 0x1F, 0, 4, "Wandering: a standing critter starts walking with chance this in 16, a walker stops with chance (15 - this) in 16 and drifts off course with chance (this + 8) in 64. The name is provisional; the rule suggests the opposite meaning.", "critter/AI.C crit_drunkwalk", I),
			F("Alertness", 0x1F, 4, 4, "A hostile guard looks for the player with chance this in 16 each step, and keeps a heard sound with chance this in 16.", "critter/AI.C crit_guard"),
			F("Arms 1 present", 0x20, 0, 1, "First arm item present.", "include/critter.h arms[0]"),
			F("Arms 1 item", 0x20, 1, 7, "First arm item (also the missile it fires).", "include/critter.h arms[0].item"),
			F("Arms 2 present", 0x21, 0, 1, "Second arm item present.", "include/critter.h arms[1]"),
			F("Arms 2 item", 0x21, 1, 7, "Second arm item.", "include/critter.h arms[1].item"),
			F("Loot 1 chance", 0x22, 0, 4, "Chance of loot item 1.", "include/critter.h other[0].prob"),
			F("Loot 1 item", 0x22, 4, 12, "Loot item 1.", "include/critter.h other[0].item"),
			F("Loot 2 chance", 0x24, 0, 4, "Chance of loot item 2.", "include/critter.h other[1].prob"),
			F("Loot 2 item", 0x24, 4, 12, "Loot item 2.", "include/critter.h other[1].item"),
			F("Treasure chance", 0x26, 0, 4, "Chance of treasure.", "include/critter.h treasure_prob"),
			F("Treasure rate", 0x26, 4, 4, "How much treasure.", "include/critter.h treasure_rate"),
			F("Food chance", 0x26, 8, 4, "Chance of food.", "include/critter.h food_prob"),
			F("Food item", 0x26, 12, 4, "Food item (class 0x0B index).", "include/critter.h food_item"),
			F("Experience", 0x28, 0, 16, "Experience for the kill.", "include/critter.h exp", C, true),
			F("Spell 1", 0x2A, 0, 8, "First spell (spell table index, 255 none). Editor: Unk5.", "include/critter.h spells[0]"),
			F("Spell 2", 0x2B, 0, 8, "Second spell. Editor: Unk6.", "include/critter.h spells[1]"),
			F("Spell 3", 0x2C, 0, 8, "Third spell. Editor: Unk7.", "include/critter.h spells[2]"),
			F("Offensive caster", 0x2D, 0, 1, "Casts offensively; looks for targets 4 tiles around. Editor: low bit of Unk8.", "critter/AI.C:365"),
			F("Caster level", 0x2D, 1, 7, "Caster power. Editor: Unk8 >> 1.", "include/critter.h caster"),
			F("Locks", 0x2E, 0, 8, "Doors and locks. Nonzero: a creature that bumps into a closed door uses it (opens it unless it is locked or spiked) and, half the time, tries to pick its lock with this as its skill. Zero: it bashes the door instead, one time in four. UW1: only hostile critters try doors; UW2: none on level 10. Editor: Unk9.", "critter/PATHFIND.C try_to_open_door; obj/USEITEMS.C checkLock"),
			F("Byte 2F", 0x2F, 0, 8, "Unknown: no code reads it (0x49 in every UW1 creature, 0x65 in every UW2 one). Editor: Unk10.", "include/critter.h b2F", U));

		/// <summary>COMOBJ.DAT entry, 11 bytes (include/object.h struct ComObj, docs/FORMATS.md).</summary>
		public static readonly RecordSchema ComObj = new RecordSchema("Common object properties", 11, "include/object.h struct ComObj; docs/FORMATS.md COMOBJ.DAT",
			F("Height", 0, 0, 8, "Height.", "include/object.h"),
			F("Radius", 1, 0, 3, "Radius.", "include/object.h"),
			F("Animated", 1, 3, 1, "Animated (3d/GAMESORT.C). Editor: Type.", "3d/GAMESORT.C:266"),
			F("Mass", 1, 4, 12, "Mass in tenths of a stone.", "include/object.h"),
			F("Bit 3.0", 3, 0, 1, "Unused: no code reads it, clear in every shipped entry of both games.", "include/object.h b3_0", U),
			F("Solid", 3, 1, 1, "Solid: blocks movement.", "motion/MOTION.C:571"),
			F("Bit 3.2", 3, 2, 1, "Creatures walk through it: a moving creature's collision check skips such objects. Set on the lever and switch (0x161, 0x162) and on 0x170-0x17F (buttons, switches, pull chains) in both games.", "motion/COLLIDE.C ObjectCheck"),
			F("No hit", 3, 3, 1, "Not hit by missiles; also turns gravity off for a thrown object.", "motion/OBJPHYS.C:389"),
			F("Bit 3.4", 3, 4, 1, "Unused: no code reads it, clear in every shipped entry of both games.", "include/object.h b3_4", U),
			F("Pick up", 3, 5, 1, "Can be picked up.", "include/object.h pickup"),
			F("Stacking", 3, 6, 2, "Stacking rule; 1 and 3 never merge. Editor: Flag6 + 'Container' (bit 7 is not a container flag).", "inv/INVPANEL.C:698"),
			F("Value", 4, 0, 16, "Value in gold.", "include/object.h"),
			F("Touch", 6, 0, 1, "Touch action.", "motion/OBJPHYS.C:163"),
			F("Usable", 6, 1, 1, "Usable.", "motion/OBJPHYS.C:163"),
			F("Quality class", 6, 2, 2, "Toughness: damage to it is shifted right by this; 3 indestructible (and described with the fifth word of its quality type).", "combat/DAMAGE.C damage_object; obj/LOOK.C LookAt"),
			F("Light", 6, 4, 1, "Gives light.", "include/object.h light"),
			F("Bounce", 6, 5, 4, "Bounce.", "include/object.h bounce", I),
			F("Fate", 7, 1, 4, "What happens when it lands after a throw. Editor: ObjectType.", "motion/OBJPHYS.C:440"),
			F("Pickable", 7, 5, 1, "Pickable. Editor: PickupFlag.", "include/object.h pickable"),
			F("Bit 7.6", 7, 6, 1, "Unused: no code reads it, clear in every shipped entry of both games. Editor: UnkFlag1.", "include/object.h b7_6", U),
			F("Can be owned", 7, 7, 1, "Can have an owner (stealing it is a crime). Editor: Ownable.", "include/object.h can_own"),
			F("Resist magic", 8, 0, 2, "Magic resistance: resists when rand() % 3 < this.", "include/object.h DMG_MAGIC"),
			F("Resist physical", 8, 2, 1, "Resists blows.", "include/object.h DMG_PHYSICAL"),
			F("Resist fire", 8, 3, 1, "Resists fire.", "include/object.h DMG_FIRE"),
			F("Resist poison", 8, 4, 1, "Resists poison.", "include/object.h DMG_POISON"),
			F("Resist cold", 8, 5, 1, "Resists cold.", "include/object.h DMG_COLD"),
			F("Resist missiles", 8, 6, 1, "Resists missiles.", "include/object.h DMG_MISSILE"),
			F("Undead", 8, 7, 1, "Undead (Smite Undead).", "include/object.h RES_UNDEAD"),
			F("Render", 9, 0, 2, "0 sprite, 1 critter, 2 3D model or door, 3 texture-mapped model.", "3d/DRAWOBJ.C:7"),
			F("Tenacity", 9, 2, 4, "Resistance to culling: the object is kept while tenacity + (quantity - 1) / 2 is above the cull range, which is 3-5 when the object store is full, 1-3 while the player sleeps and 10-12 when an item is destroyed or comes to rest; so 13-15 is never culled. The object's Tenacious bit overrides it.", "obj/OBJECTS.C chkTenacious, Obj_Elem_Fate"),
			F("Bits 9.6-7", 9, 6, 2, "Unused: no code reads them, 0 in every shipped entry of both games.", "include/object.h b9_6", U),
			F("Quality type", 10, 0, 4, "Quality words: strings type * 6 + 1..4 of block 5 by quality (quality / 16 + 1), + 5 when indestructible.", "obj/LOOK.C LookAt"),
			F("Lookable", 10, 4, 1, "Has a printable 'look at' description.", "include/object.h lookable"),
			F("Bits A.5-7", 10, 5, 3, "Unused: no code reads them; 3 in every UW1 entry, 0 in every UW2 one.", "include/object.h bA_5", U));

		public static readonly RecordSchema[] All =
		{
			StaticObject, MobileObject, Tile, AnimOverlay, MeleeWeapon, Missile, Armour, Container, Light, Creature, ComObj
		};
	}
}
