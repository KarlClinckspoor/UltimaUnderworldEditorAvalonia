using System;
using System.Collections.Generic;
using System.Text;

namespace UltimateEditor.Games
{
	/// <summary>
	/// One of UW2's 16 schedules in SCD.ARK (ported from the Unity editor's
	/// Extensions/Core/Games/ScdArk.cs). Block 0 runs by the time of day (72 steps), block n by
	/// X clock n. Layout (uw2 include/event.h struct SCDWork, from "rows"): word row count,
	/// byte block number, byte spare, 80 level clocks of two words (time reached, next row),
	/// then the rows of 16 bytes sorted by time. The game writes rows * 16 + 0x144 bytes,
	/// uncompressed (event/SCHEDULE.C Sched_Should_Save).
	/// </summary>
	public sealed class ScdBlock
	{
		public const int HeaderSize = 0x144;
		public const int Levels = 80;
		public byte BlockByte;
		public byte Spare;
		public byte[] Clocks = new byte[Levels * 4];
		public List<ScdRow> Rows = new List<ScdRow>();
		/// <summary>The rows as read or last written, for <see cref="RemapClocks"/>.</summary>
		List<ScdRow> written = new List<ScdRow>();
		/// <summary>Bytes after the last row that the row count does not cover (kept as they were).</summary>
		byte[] tail = new byte[0];

		public int ClockTime(int level) { return Clocks[level * 4] | Clocks[level * 4 + 1] << 8; }
		public int ClockNext(int level) { return Clocks[level * 4 + 2] | Clocks[level * 4 + 3] << 8; }

		/// <summary>
		/// Moves each of the 80 level clocks' "next row" index with the rows. A clock's next is
		/// how many rows that level has already run (SCHEDULE.C FindSCDRowsToExecute starts
		/// there). After editing, next is put just past the last of those rows, wherever it now
		/// is: rows already run are not run again, rows removed no longer count, and a row added
		/// before that point counts as passed, as the game's own Sched_InsertLong treats a row
		/// inserted at a time the clock has passed (Sched_Delete likewise moves next down).
		/// </summary>
		public void RemapClocks()
		{
			for (int k = 0; k < Levels; k++)
			{
				int next = ClockNext(k);
				if (next == 0) continue;
				var run = new HashSet<ScdRow>();
				for (int i = 0; i < next && i < written.Count; i++) run.Add(written[i]);
				int n = 0;
				for (int i = 0; i < Rows.Count; i++)
					if (run.Contains(Rows[i])) n = i + 1;
				Clocks[k * 4 + 2] = (byte)n; Clocks[k * 4 + 3] = (byte)(n >> 8);
			}
		}

		/// <summary>Records the rows as they now are on disk.</summary>
		public void MarkWritten() { written = new List<ScdRow>(Rows); }

		public static ScdBlock Parse(byte[] d)
		{
			var b = new ScdBlock();
			if (d == null || d.Length < HeaderSize) return b;
			int n = d[0] | d[1] << 8;
			b.BlockByte = d[2]; b.Spare = d[3];
			Array.Copy(d, 4, b.Clocks, 0, b.Clocks.Length);
			int i = 0;
			for (; i < n && HeaderSize + 16 * i + 16 <= d.Length; i++)
			{
				var r = new byte[16];
				Array.Copy(d, HeaderSize + 16 * i, r, 0, 16);
				b.Rows.Add(new ScdRow(r));
			}
			int end = HeaderSize + 16 * i;
			if (end < d.Length) { b.tail = new byte[d.Length - end]; Array.Copy(d, end, b.tail, 0, b.tail.Length); }
			b.MarkWritten();
			return b;
		}

		/// <summary>The block's bytes. Bytes a read found past the rows are kept only while the rows are as read.</summary>
		public byte[] ToBytes()
		{
			bool same = written.Count == Rows.Count;
			for (int i = 0; same && i < Rows.Count; i++) same = ReferenceEquals(written[i], Rows[i]);
			int extra = same ? tail.Length : 0;
			var d = new byte[HeaderSize + 16 * Rows.Count + extra];
			d[0] = (byte)Rows.Count; d[1] = (byte)(Rows.Count >> 8);
			d[2] = BlockByte; d[3] = Spare;
			Array.Copy(Clocks, 0, d, 4, Clocks.Length);
			for (int i = 0; i < Rows.Count; i++) Array.Copy(Rows[i].Bytes, 0, d, HeaderSize + 16 * i, 16);
			if (extra > 0) Array.Copy(tail, 0, d, HeaderSize + 16 * Rows.Count, extra);
			return d;
		}

		/// <summary>
		/// Sorts by time, keeping rows of the same time in order, and keeping each test-variables
		/// row (10) with the disabled rows that follow it, which it runs (SCDEVENT.C ev_checkvar).
		/// The game stops at the first row past the clock (SCHEDULE.C FindSCDRowsToExecute), so
		/// a block out of order skips rows.
		/// </summary>
		public void SortStable()
		{
			var groups = new List<List<ScdRow>>();
			foreach (var r in Rows)
			{
				if (r.Event < 0 && groups.Count > 0 && groups[groups.Count - 1][0].Event == 10) groups[groups.Count - 1].Add(r);
				else groups.Add(new List<ScdRow> { r });
			}
			var keyed = new List<KeyValuePair<int, List<ScdRow>>>();
			for (int i = 0; i < groups.Count; i++) keyed.Add(new KeyValuePair<int, List<ScdRow>>(i, groups[i]));
			keyed.Sort((a, b) => a.Value[0].Time != b.Value[0].Time ? a.Value[0].Time.CompareTo(b.Value[0].Time) : a.Key.CompareTo(b.Key));
			Rows.Clear();
			foreach (var k in keyed) Rows.AddRange(k.Value);
		}

		/// <summary>The first row earlier than the row before it, or -1 when the block is in time order.</summary>
		public int FirstOutOfOrder()
		{
			for (int i = 1; i < Rows.Count; i++) if (Rows[i].Time < Rows[i - 1].Time) return i;
			return -1;
		}
	}

	/// <summary>One editable value of a schedule row: where it is in the 16 bytes, its range and what it means.</summary>
	public sealed class ScdField
	{
		public string Name { get; private set; }
		/// <summary>Offset in the row (0-15).</summary>
		public int Offset { get; private set; }
		/// <summary>1 or 2 bytes.</summary>
		public int Size { get; private set; }
		public int Min { get; private set; }
		public int Max { get; private set; }
		public bool Signed { get; private set; }
		/// <summary>What the value does, with its source.</summary>
		public string Help { get; private set; }
		/// <summary>What kind of value it is, for naming it: "time", "level", "flag", "event", "mode", "whoami", "race", "object", "variable", "op", "goal", "attitude", "square", "byte".</summary>
		public string Kind { get; private set; }

		public ScdField(string name, int offset, int size, int min, int max, string kind, string help, bool signed = false)
		{
			Name = name; Offset = offset; Size = size; Min = min; Max = max; Kind = kind; Help = help; Signed = signed;
		}

		public int Get(ScdRow r)
		{
			int v = Size == 2 ? r.Bytes[Offset] | r.Bytes[Offset + 1] << 8 : r.Bytes[Offset];
			if (Signed) v = Size == 2 ? (short)v : (sbyte)v;
			return v;
		}

		public void Set(ScdRow r, int v)
		{
			if (v < Min || v > Max) throw new ArgumentOutOfRangeException(Name, Name + " is " + Min + " to " + Max + ".");
			r.Bytes[Offset] = (byte)v;
			if (Size == 2) r.Bytes[Offset + 1] = (byte)(v >> 8);
		}

		public override string ToString() { return Name; }
	}

	/// <summary>One schedule row (uw2 include/event.h struct SCDRow): word time, byte level,
	/// byte once, signed byte event, 11 bytes of parameters (offsets 5-15, union SCDParams).</summary>
	public sealed class ScdRow
	{
		public readonly byte[] Bytes;
		public ScdRow(byte[] b) { Bytes = b; }
		public ScdRow() { Bytes = new byte[16]; }

		public int Time { get { return Bytes[0] | Bytes[1] << 8; } set { Bytes[0] = (byte)value; Bytes[1] = (byte)(value >> 8); } }
		/// <summary>0xFF any level, 0xF6 + n every level of world n, otherwise the level (1-based).</summary>
		public int Level { get { return Bytes[2]; } set { Bytes[2] = (byte)value; } }
		public bool Once { get { return Bytes[3] != 0; } set { Bytes[3] = (byte)(value ? 1 : 0); } }
		/// <summary>Negative: disabled (a test-variables row may run it).</summary>
		public int Event { get { return (sbyte)Bytes[4]; } set { Bytes[4] = (byte)(sbyte)value; } }
		public int Word(int at) { return Bytes[at] | Bytes[at + 1] << 8; }
		public void SetWord(int at, int v) { Bytes[at] = (byte)v; Bytes[at + 1] = (byte)(v >> 8); }

		public ScdRow Clone() { return new ScdRow((byte[])Bytes.Clone()); }

		/// <summary>The handlers by event code (SCDEVENT.C SCDEventCodeJumps).</summary>
		public static readonly string[] EventNames =
		{
			"nothing", "change goal", "teleport", "kill", "set quest bit", "fire scheduled triggers", "nothing",
			"special case", "set attitude", "set variable", "test variables", "remove",
		};
		/// <summary>The special cases of event 7 by hack byte (SCDEVENT.C NestedSCDEventCodeJumps).</summary>
		public static readonly string[] HackNames = { "nothing", "gargoyle (variable when on a square)", "soldier (move into a rectangle)", "freeze floors", "close doors", "nothing", "Nystul (turn)", "Gotha (move into a rectangle)" };
		static readonly string[] ops = { "add", "subtract", "set to", "and", "or", "xor", "shift left by", "count up while equal to" };
		/// <summary>include/critter.h GOAL_* (as the mobile record's goal field).</summary>
		public static readonly string[] GoalNames = { "stand", "go home", "wander", "follow", "guard", "attack", "flee", "stand", "mill about", "cornered", "talk to player", "flutter", "hover", "goal 13", "goal 14", "goal 15" };
		public static readonly string[] AttitudeNames = { "hostile", "upset", "mellow", "friendly" };
		static readonly string[] modeNames = { "by whoami (conversation slot)", "by race", "one object", "every critter" };

		public static string EventName(int ev)
		{
			int e = ev < 0 ? -ev : ev;
			string n = e < EventNames.Length ? EventNames[e] : "unknown";
			return (ev < 0 ? "disabled " : "") + n;
		}

		public static string LevelText(int level)
		{
			if (level == 0xFF) return "any level";
			if (level >= 0xF6) return "world " + (level - 0xF6) + " (" + GameProfile.UW2.WorldName(level - 0xF6) + ")";
			if (level == 0 || level > GameProfile.UW2.LevelSlots) return "level " + level + " (no such level: never runs)";
			var p = GameProfile.UW2;
			return "level " + level + " (" + p.WorldName(p.WorldOf(level)) + " " + p.PlaceInWorld(level) + ")";
		}

		/// <summary>The critters an event acts on (SCDEVENT.C gronk_critid): low byte the mode, high byte its value.</summary>
		public static string Critters(int w, Func<int, string> npcName = null, Func<int, string> raceName = null)
		{
			int mode = w & 0xFF, v = w >> 8 & 0xFF;
			switch (mode)
			{
				case 0: { string n = npcName != null ? npcName(v) : null; return "whoami " + v + (n != null ? " (" + n + ")" : ""); }
				case 1: { string n = raceName != null ? raceName(v) : null; return "race " + v + (n != null ? " (" + n + ")" : ""); }
				case 2: return "object #" + v;
				case 3: return "every critter";
			}
			return "mode " + mode + " (none)";
		}

		// ---- the fields

		/// <summary>Time, level, once and event: every row has them. Block 0's clock is the time of day, 0-71 (Sched_WrapTime); the X clocks are bytes (player.h xclock[16]).</summary>
		public static List<ScdField> HeaderFields(int block)
		{
			return new List<ScdField>
			{
				new ScdField("Time", 0, 2, 0, block == 0 ? 71 : 255, "time", block == 0
					? "When the row runs: the time of day in 72 steps (20 minutes each). Rows run once the clock has passed their time (SCHEDULE.C Sched_WrapTime)."
					: "When the row runs: once X clock " + block + " reaches this value (SCHEDULE.C Sched_SetTime; the X clocks are bytes, player.h xclock)."),
				new ScdField("Level", 2, 1, 0, 255, "level", "The level the row acts on: 1-80 one level, 246 + n every level of world n, 255 any level. Only the level the player is on runs it (SCDEVENT.C Sched_DoEvent)."),
				new ScdField("Once", 3, 1, 0, 1, "flag", "1: the row deletes itself after it runs (Sched_DoEvent, Sched_Delete)."),
				new ScdField("Event", 4, 1, -128, 127, "event", "0 nothing, 1 change goal, 2 teleport, 3 kill, 4 quest bit, 5 scheduled triggers, 7 special case, 8 attitude, 9 set variable, 10 test variables, 11 remove. Negative: disabled (a test row above it may run it). 12 or more halts the schedules (Sched_DoEvent returns 6).", true),
			};
		}

		static ScdField Mode(int at) { return new ScdField("Critters", at, 1, 0, 255, "mode", "Which critters: 0 by whoami (conversation slot), 1 by race, 2 one object, 3 every critter (SCDEVENT.C gronk_critid). Only critters on the active list of the level are found."); }

		static ScdField Who(int at, int mode)
		{
			switch (mode)
			{
				case 0: return new ScdField("Whoami", at, 1, 0, 255, "whoami", "The critter's whoami: its conversation slot (named by string whoami + 16 of block 7).");
				case 1: return new ScdField("Race", at, 1, 0, 255, "race", "The race (OBJECTS.DAT byte 9 of the creature); critters marked loner are skipped (gronk_race).");
				case 2: return new ScdField("Object", at, 1, 0, 255, "object", "The mobile object index on the current level (Obj_IntTMem).");
			}
			return new ScdField("(not read)", at, 1, 0, 255, "byte", "Not read with this mode.");
		}

		/// <summary>
		/// The named parameter fields for the row's event as it is now (union SCDParams and the
		/// handlers of event/SCDEVENT.C); bytes 5-15 that the event does not read are listed as
		/// unused, so every byte stays editable.
		/// </summary>
		public List<ScdField> ParameterFields()
		{
			var f = new List<ScdField>();
			int ev = Math.Abs(Event);
			switch (ev)
			{
				case 1:
					f.Add(Mode(5)); f.Add(Who(6, Bytes[5]));
					f.Add(new ScdField("Goal", 7, 1, 0, 15, "goal", "The new goal (change_critter_goal; 4 bits of the goal word)."));
					f.Add(new ScdField("Goal target", 8, 1, 0, 255, "byte", "The goal's target (an object index or a whoami, by goal)."));
					break;
				case 2:
					f.Add(new ScdField("X", 5, 1, 0, 63, "square", "Destination square x."));
					f.Add(new ScdField("Y", 6, 1, 0, 63, "square", "Destination square y."));
					f.Add(Mode(7)); f.Add(Who(8, Bytes[7]));
					f.Add(new ScdField("Level byte", 9, 1, 0, 255, "byte", "Passed to teleport_critter as its last argument, which it does not read (critter/CRITTIME.C): a schedule moves critters on the current level."));
					f.Add(new ScdField("Even if seen", 10, 1, 0, 255, "flag", "Not 0: move even when the player could see the critter or the destination (gronkify_teleport)."));
					f.Add(new ScdField("Set home", 11, 1, 0, 255, "flag", "Not 0: the destination becomes its home."));
					f.Add(new ScdField("Retry after", 12, 1, 0, 255, "byte", "If it could not move: a once copy of the row is queued for block 0 at the time of day + this (mod 72); 0 no retry (Sched_Migrate)."));
					break;
				case 3:
					f.Add(Mode(5)); f.Add(Who(6, Bytes[5]));
					f.Add(new ScdField("Kill the talker", 7, 1, 0, 255, "flag", "0: spare the NPC the player is talking to (gronkify_slay)."));
					f.Add(new ScdField("Count", 8, 1, 0, 255, "byte", "With the talker spared, a repeating row with this set may count up X clock 15 (gronkify_slay)."));
					break;
				case 4:
					f.Add(new ScdField("Quest flag", 5, 1, 0, 255, "byte", "Bit (n & 3) of quest variable n / 4 (SCDEVENT.C ev_set_qbit)."));
					f.Add(new ScdField("Value", 6, 1, 0, 255, "byte", "Added unmasked after clearing the bit: other than 0 or 1 spills into the next flags."));
					break;
				case 5:
					f.Add(new ScdField("X", 5, 1, 0, 63, "square", "The square whose scheduled triggers (minor class 0xC) fire (ev_trigger)."));
					f.Add(new ScdField("Y", 6, 1, 0, 63, "square", "The square's y."));
					break;
				case 7:
				{
					f.Add(new ScdField("Hack", 5, 1, 0, 255, "byte", "The special case: 1 gargoyle, 2 soldier, 3 freeze floors, 4 close doors, 6 Nystul, 7 Gotha; 0, 5 and 8 or more do nothing (ev_hack)."));
					switch (Bytes[5])
					{
						case 1:
							f.Add(Mode(6)); f.Add(Who(7, Bytes[6]));
							f.Add(new ScdField("Home X", 8, 1, 0, 63, "square", "Acts when the critter's home is this square (gronkify_garg)."));
							f.Add(new ScdField("Home Y", 9, 1, 0, 63, "square", "The square's y."));
							f.Add(new ScdField("Variable", 10, 2, 0, 0xFFFF, "variable", "The numbered variable set (set_numbered_variable)."));
							f.Add(new ScdField("Operation", 12, 1, 0, 255, "op", "0 add, 1 subtract, 2 set, 3 and, 4 or, 5 xor, 6 shift left, 7 count up (low 3 bits)."));
							f.Add(new ScdField("Value", 13, 2, 0, 0xFFFF, "byte", "The operand."));
							break;
						case 2:
							f.Add(new ScdField("At X", 6, 1, 0, 63, "square", "Only a critter whose home is this square moves; 0, 0 any (gronkify_soldier)."));
							f.Add(new ScdField("At Y", 7, 1, 0, 63, "square", "The square's y."));
							f.Add(Mode(8)); f.Add(Who(9, Bytes[8]));
							f.Add(new ScdField("From X", 10, 1, 0, 63, "square", "The rectangle a random free square is chosen from."));
							f.Add(new ScdField("From Y", 11, 1, 0, 63, "square", ""));
							f.Add(new ScdField("To X", 12, 1, 0, 63, "square", ""));
							f.Add(new ScdField("To Y", 13, 1, 0, 63, "square", ""));
							break;
						case 3:
							f.Add(new ScdField("Floor", 6, 1, 0, 255, "byte", "Tiles with this floor index (ev_freeze_hack): about half of them change."));
							f.Add(new ScdField("New floor", 7, 1, 0, 255, "byte", "Their new floor index."));
							f.Add(new ScdField("Raise by", 8, 2, 0, 0xFFFF, "byte", "Added to their height (change_terrain)."));
							break;
						case 4:
							f.Add(new ScdField("Away from player", 6, 1, 0, 255, "flag", "Passed to DoClosingDoors: each open door (not on a tile with door bit 1) closes with chance 3 in 10."));
							break;
						case 6:
							f.Add(Mode(6)); f.Add(Who(7, Bytes[6]));
							f.Add(new ScdField("Heading", 8, 1, 0, 7, "byte", "The heading it turns to, eighths of a turn (gronkify_nystul)."));
							break;
						case 7:
							f.Add(Mode(6)); f.Add(Who(7, Bytes[6]));
							f.Add(new ScdField("From X", 8, 1, 0, 63, "square", "The first free square of this rectangle, row by row, becomes its place and home (gronkify_gotha)."));
							f.Add(new ScdField("From Y", 9, 1, 0, 63, "square", ""));
							f.Add(new ScdField("To X", 10, 1, 0, 63, "square", ""));
							f.Add(new ScdField("To Y", 11, 1, 0, 63, "square", ""));
							break;
					}
					break;
				}
				case 8:
					f.Add(Mode(5)); f.Add(Who(6, Bytes[5]));
					f.Add(new ScdField("Attitude", 7, 1, 0, 3, "attitude", "0 hostile, 1 upset, 2 mellow, 3 friendly; other than hostile also forgets who last hit it (gronkify_attitude)."));
					break;
				case 9:
					f.Add(new ScdField("Variable", 5, 2, 0, 0xFFFF, "variable", "The numbered variable (TRIGGER.C set_numbered_variable): 0-255 the game variables, 256-383 quest flags, 384-399 quests 128 on, 400-415 X clocks."));
					f.Add(new ScdField("Operation", 7, 1, 0, 255, "op", "0 add, 1 subtract, 2 set, 3 and, 4 or, 5 xor, 6 shift left, 7 count up (low 3 bits)."));
					f.Add(new ScdField("Value", 8, 2, 0, 0xFFFF, "byte", "The operand."));
					break;
				case 10:
					f.Add(new ScdField("Variable", 5, 2, 0, 0xFFFF, "variable", "The first numbered variable tested (SCDEVENT.C ev_checkvar)."));
					f.Add(new ScdField("Count", 7, 1, 0, 255, "byte", "How many variables from it are combined (0 or 1: just the one)."));
					f.Add(new ScdField("Combine by", 8, 1, 0, 255, "op", "How they are combined (do_math_op): 0 add, 1 subtract, 2 set, 3 and, 4 or, 5 xor, 6 shift left, 7 count up."));
					f.Add(new ScdField("Invert", 9, 1, 0, 255, "flag", "Not 0: the test passes when the result is not the value."));
					f.Add(new ScdField("Value", 10, 2, -32768, 32767, "byte", "What the result is compared with.", true));
					break;
				case 11:
					f.Add(Mode(5)); f.Add(Who(6, Bytes[5]));
					break;
			}
			var used = new bool[16];
			foreach (var x in f) for (int k = 0; k < x.Size; k++) used[x.Offset + k] = true;
			for (int at = 5; at < 16; at++)
				if (!used[at]) f.Add(new ScdField("Byte " + at, at, 1, 0, 255, "byte", "Not read by this event."));
			return f;
		}

		/// <summary>What the row does (uw2 event/SCDEVENT.C).</summary>
		public string Describe(Func<int, string> npcName = null, Func<int, string> raceName = null)
		{
			int ev = Event;
			bool disabled = ev < 0;
			if (disabled) ev = -ev;
			var sb = new StringBuilder();
			if (disabled) sb.Append("[disabled] ");
			string what;
			switch (ev)
			{
				case 1: what = string.Format("{0}: goal {1} ({2}), target {3}", Critters(Word(5), npcName, raceName), Bytes[7], GoalNames[Bytes[7] & 15], Bytes[8]); break;
				case 2:
					// byte 9 is passed to teleport_critter, which does not read it (critter/CRITTIME.C)
					what = string.Format("{0}: teleport to ({1}, {2}) on the current level{3}{4}{5}", Critters(Word(7), npcName, raceName), Bytes[5], Bytes[6],
						Bytes[10] != 0 ? ", even if seen" : ", only if the player sees neither it nor the destination", Bytes[11] != 0 ? ", there is its new home" : "",
						Bytes[12] != 0 ? ", if it fails retry " + Bytes[12] + " steps of the day later" : "");
					break;
				case 3: what = string.Format("{0}: kill{1}", Critters(Word(5), npcName, raceName), Bytes[7] == 0 ? " (not the one talking)" : ""); break;
				// ev_set_qbit: bit (q & 3) of quests[q >> 2], the value added unmasked
				case 4: what = string.Format("quest flag {0} (bit {1} of quest variable {2}) := {3}{4}", Bytes[5], Bytes[5] & 3, Bytes[5] >> 2, Bytes[6], Bytes[6] > 1 ? " (not 0 or 1: spills into the next flags)" : ""); break;
				case 5: what = string.Format("fire the scheduled triggers on square ({0}, {1}) of the current level", Bytes[5], Bytes[6]); break;
				case 7:
				{
					int h = Bytes[5];
					string hn = h < HackNames.Length ? HackNames[h] : "no handler";
					switch (h)
					{
						case 1: what = string.Format("hack 1, {0}: {1} with home ({2}, {3}): {4}: {5}", hn, Critters(Word(6), npcName, raceName), Bytes[8], Bytes[9], Uw2Traps.VariableName(Word(10)), Uw2Traps.Operation(Word(10), Bytes[12], Word(13))); break;
						case 2: what = string.Format("hack 2, {0}: {1}{2} to ({3}, {4})-({5}, {6})", hn, Critters(Word(8), npcName, raceName), Bytes[6] != 0 || Bytes[7] != 0 ? " with home (" + Bytes[6] + ", " + Bytes[7] + ")" : "", Bytes[10], Bytes[11], Bytes[12], Bytes[13]); break;
						case 3: what = string.Format("hack 3, {0}: about half the floors {1} become {2}, height + {3}", hn, Bytes[6], Bytes[7], Word(8)); break;
						case 4: what = string.Format("hack 4, {0}: open doors may close (3 in 10){1}", hn, Bytes[6] != 0 ? " (" + Bytes[6] + ")" : ""); break;
						case 6: what = string.Format("hack 6, {0}: {1} faces heading {2}", hn, Critters(Word(6), npcName, raceName), Bytes[8]); break;
						case 7: what = string.Format("hack 7, {0}: {1} to ({2}, {3})-({4}, {5})", hn, Critters(Word(6), npcName, raceName), Bytes[8], Bytes[9], Bytes[10], Bytes[11]); break;
						default: what = "hack " + h + ", " + hn; break;
					}
					break;
				}
				case 8: what = string.Format("{0}: attitude {1} ({2})", Critters(Word(5), npcName, raceName), Bytes[7], AttitudeNames[Bytes[7] & 3]); break;
				case 9: what = string.Format("{0}: {1}", Uw2Traps.VariableName(Word(5)), Uw2Traps.Operation(Word(5), Bytes[7], Word(8))); break;
				case 10:
				{
					int count = Bytes[7];
					string vars = count <= 1 ? Uw2Traps.VariableName(Word(5)) : Uw2Traps.VariableName(Word(5)) + " and the next " + (count - 1) + " combined by " + ops[Bytes[8] & 7];
					what = string.Format("if {0} {1} {2}: run the disabled rows that follow, up to the next test", vars, Bytes[9] != 0 ? "is not" : "is", (short)Word(10));
					break;
				}
				case 11: what = string.Format("{0}: remove from the map", Critters(Word(5), npcName, raceName)); break;
				case 0: case 6: what = "nothing"; break;
				// Sched_DoEvent returns 6 for 12 or more: the block stops there, and so do the later blocks (Sched_SetAllClocks)
				default: what = "unknown event " + ev + (disabled ? " (skipped while disabled)" : " (halts this schedule and the later ones each time it is reached)"); break;
			}
			sb.Append(what);
			return sb.ToString();
		}

		/// <summary>A short meaning of a field's value (names of NPCs, races, variables, goals).</summary>
		public string Meaning(ScdField f, Func<int, string> npcName = null, Func<int, string> raceName = null)
		{
			int v = f.Get(this);
			switch (f.Kind)
			{
				case "time": return "";
				case "level": return LevelText(v);
				case "event": return EventName(v);
				case "mode": return v < 4 ? modeNames[v] : "none";
				case "whoami": { string n = npcName != null ? npcName(v) : null; return n ?? ""; }
				case "race": { string n = raceName != null ? raceName(v) : null; return n ?? ""; }
				case "variable": return Uw2Traps.VariableName(v);
				case "op": return ops[v & 7];
				case "goal": return GoalNames[v & 15];
				case "attitude": return AttitudeNames[v & 3];
				case "flag": return v != 0 ? "yes" : "no";
			}
			return "";
		}
	}

	/// <summary>SCD.ARK: 16 blocks, one schedule each (uw2 sys/ARC.C archive 5).</summary>
	public static class ScdArk
	{
		public const int Blocks = 16;

		/// <summary>What each block's clock is (include/player.h XC_*).</summary>
		public static string ClockName(int block)
		{
			switch (block)
			{
				case 0: return "time of day";
				case 1: return "X clock 1: the castle plot";
				case 2: return "X clock 2: the blackrock gems";
				case 3: return "X clock 3: the djinn capture";
				case 14: return "X clock 14: pit kills";
				case 15: return "X clock 15: changes counter";
			}
			return "X clock " + block;
		}

		public static List<ScdBlock> Read(Uw2Archive a)
		{
			var list = new List<ScdBlock>();
			for (int b = 0; b < Math.Min(Blocks, a.Count); b++)
				list.Add(a.Has(b) ? ScdBlock.Parse(a.Read(b)) : new ScdBlock());
			return list;
		}

		/// <summary>Writes the blocks whose bytes changed into the archive (in memory); returns their numbers.</summary>
		public static List<int> Write(Uw2Archive a, List<ScdBlock> blocks)
		{
			var written = new List<int>();
			for (int b = 0; b < blocks.Count && b < a.Count; b++)
			{
				if (!Changed(a, blocks, b)) continue;
				blocks[b].RemapClocks();
				a.Write(b, blocks[b].ToBytes());
				blocks[b].MarkWritten();
				written.Add(b);
			}
			return written;
		}

		/// <summary>Whether block b's rows or header differ from the archive.</summary>
		public static bool Changed(Uw2Archive a, List<ScdBlock> blocks, int b)
		{
			var now = blocks[b].ToBytes();
			var old = a.Has(b) ? a.Read(b) : null;
			if (old == null) return blocks[b].Rows.Count > 0;
			if (old.Length != now.Length) return true;
			for (int i = 0; i < old.Length; i++) if (old[i] != now[i]) return true;
			return false;
		}
	}
}
