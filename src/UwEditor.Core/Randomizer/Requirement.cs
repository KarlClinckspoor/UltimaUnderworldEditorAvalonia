using System;
using System.Collections.Generic;
using System.Text;

namespace UwEditor.Core.Randomizer
{
	/// <summary>Solver options: what the player is assumed able to do besides holding items.</summary>
	public class SolverOptions
	{
		public bool AllowBash = true;
		/// <summary>Pick Lock skill assumed; -1 never picks. A lock of difficulty z needs 3z (z 14: 31; z 15: never), as checkLock.</summary>
		public int LockpickSkill = -1;
		public bool AllowSpells = true;
		/// <summary>Tokens the player starts with (e.g. "spell:levitate" for a scroll in hand).</summary>
		public List<string> StartTokens = new List<string>();
		/// <summary>Links marked 'todo' count as usable.</summary>
		public bool TrustTodoLinks = true;

		// ---- Phase B of the automatic zones (abilities and their sources); all off by default, so older zonings solve as before

		/// <summary>
		/// spell:X means "ability X is available": cast:X (with Magic) or use:X, or a placement
		/// granting spell:X itself. Off: spell:X follows AllowSpells as before.
		/// </summary>
		public bool AbilitySources;
		/// <summary>With AbilitySources: casting from runes counts (cast:X tokens).</summary>
		public bool Magic = true;
		/// <summary>With AbilitySources: scrolls, potions and charged wands (once:X) count for use:X, besides worn (worn:X) and unlimited (perm:X) sources.</summary>
		public bool SingleUseItems;
		/// <summary>The Search skill assumed for search:Z (a look trigger of height Z; game/SKILLCHK.C skill_check, event/TRIGGER.C UseTrigger).</summary>
		public int SearchSkill;
		/// <summary>search:Z holds when a look can pass at all (a failed look leaves no mark, so it can be tried again); off: only at even odds or better (skill at least Z).</summary>
		public bool SearchRetry = true;
		/// <summary>pick:Z holds when a try can pass at all (UseLockpickOn keeps the pick on a failure); off: skill at least 3Z, as before.</summary>
		public bool PickRetry;
		/// <summary>pick:Z also needs item:lockpick.</summary>
		public bool PickNeedsLockpick;

		// ---- Phase C: the logic options as tokens

		/// <summary>The logic options that are on, by name (option:NAME holds when NAME is here; see FactSet.KnownOptions).</summary>
		public HashSet<string> OptionsOn = new HashSet<string>();
	}

	/// <summary>
	/// A context that tells "reached now" from "reached at some time" (the timeline's return
	/// check): now_zone:N, a lever that must be worked again after a door closed behind the
	/// player, holds only for zones reached in the current search. Other contexts read it as zone:N.
	/// </summary>
	public interface INowContext
	{
		bool ZoneNow(int zone);
	}

	/// <summary>What the evaluator asks about the world.</summary>
	public interface IRequirementContext
	{
		bool HasToken(string token);
		bool ZoneReachable(int zone);
		int TalismanCount();
		SolverOptions Options { get; }
	}

	/// <summary>
	/// The little language of link requirements: tokens joined with '&amp;' (and) and '|' (or),
	/// with parentheses. See LanguageHelp for the tokens.
	/// </summary>
	public abstract class Requirement
	{
		public static readonly string[] LanguageHelp =
		{
			"key:N          hold a key whose owner field is N (the lock's key number)",
			"bash           bashing a breakable door is allowed (solver option)",
			"pick:Z         picking a lock of difficulty Z is allowed (solver option: Pick Lock skill; 3Z needed, Z 14 needs 31, Z 15 never;",
			"               with ability sources also the Open spell, a pick of skill 45)",
			"item:NAME      hold the named item (granted by a placement: medallion, crystal, lava_boots ...)",
			"talismans:N    hold at least N different talismans (each talisman grants talisman:<name>)",
			"flag:NAME      a quest or event flag (granted by a placement, e.g. gazer_dead)",
			"npc:NAME       the NPC can be talked to / is at the gate (granted by a placement)",
			"spell:NAME     levitate (also fly), waterwalk, jump, reveal, lava, gate_travel: allowed by the option, or granted by a placement;",
			"               with ability sources (Phase B): cast:NAME (runes, Magic on) | use:NAME",
			"cast:NAME      the runes of a spell giving NAME are held (granted by facts; needs the Magic option)",
			"use:NAME       an item gives NAME: worn:NAME (worn, works everywhere), perm:NAME (used, no charges), once:NAME (scroll, potion, charged wand; needs the option)",
			"search:Z       a look trigger of height Z passes with the Search skill option (Z 0: always)",
			"zone:N         zone N must be reachable (a lever or button on the other side)",
			"now_zone:N     zone N reached now (a lever to work again after a door closed behind the player); else as zone:N",
			"option:NAME    a logic option is on (magic, single_use, lockpick, search_retry, ring_needs_lava, kill_npcs ...)",
			"spell:telekinesis  Telekinesis (Ort Por Ylem): uses things from afar; the links ask for item:pole | spell:telekinesis",
			"free           nothing needed",
			"Operators: '&' and, '|' or, parentheses.",
		};

		/// <summary>
		/// Whether a look trigger of height z passes with the options' Search skill
		/// (event/TRIGGER.C UseTrigger: only z &gt; 0 is checked, skill_check(Search, z) &gt; 0; game/SKILLCHK.C:
		/// Search - z + rand() % 31 &gt; 15). With retries any chance above 0 is enough.
		/// </summary>
		public static bool SearchPasses(SolverOptions o, int z)
		{
			if (z <= 0) return true;
			return o.SearchRetry ? o.SearchSkill - z + 30 > 15 : o.SearchSkill >= z;
		}

		/// <summary>The chance in 31 that skill_check(value, target) passes (&gt; 0): value - target + rand() % 31 &gt; 15.</summary>
		public static int PassChanceIn31(int value, int target)
		{
			int need = 16 - (value - target);      // the lowest roll that passes
			return Math.Max(0, Math.Min(31, 31 - Math.Max(0, need)));
		}

		public abstract bool Eval(IRequirementContext ctx);
		/// <summary>The leaf terms that are false and would need to become true (all of them in an 'or').</summary>
		public abstract void Missing(IRequirementContext ctx, List<string> into);
		public abstract void Tokens(List<string> into);
		public abstract override string ToString();

		public static string Normalize(string token)
		{
			token = (token ?? "").Trim().ToLowerInvariant();
			if (token == "spell:fly" || token == "spell:levitation" || token == "spell:flying") token = "spell:levitate";
			if (token == "spell:water_walk" || token == "spell:waterwalking") token = "spell:waterwalk";
			return token;
		}

		// ---------------------------------------------------------------- nodes

		sealed class Leaf : Requirement
		{
			public readonly string Token;
			public Leaf(string t) { Token = Normalize(t); }

			public override bool Eval(IRequirementContext ctx)
			{
				var o = ctx.Options;
				if (Token == "free" || Token == "" || Token == "none" || Token == "true") return true;
				if (Token == "bash") return o.AllowBash;
				int colon = Token.IndexOf(':');
				string head = colon < 0 ? Token : Token.Substring(0, colon);
				string tail = colon < 0 ? "" : Token.Substring(colon + 1);
				int n;
				switch (head)
				{
					case "pick":
						if (o.LockpickSkill < 0) return false;
						if (!int.TryParse(tail, out n)) return false;
						if (n >= 15) return false;
						// the Open spell tries the lock as a pick of skill 45 (combat/SPELLS.C obj_spells, checkLock(ThePlayer, target, -45)): sure up to z 9
						if (o.AbilitySources && (o.PickRetry || n <= 9) && HasAbility(ctx, "open")) return true;
						if (o.PickNeedsLockpick && !ctx.HasToken("item:lockpick")) return false;
						if (n == 14) return o.LockpickSkill >= 31;
						// obj/USEITEMS.C checkLock: skill_check(skill, 3z) > 0, i.e. skill - 3z + rand() % 31 > 15
						return o.PickRetry ? o.LockpickSkill - n * 3 + 30 > 15 : o.LockpickSkill >= n * 3;
					case "search":
						return int.TryParse(tail, out n) && SearchPasses(o, n);
					case "use":
						return HasUse(ctx, tail);
					case "cast":
						return o.Magic && ctx.HasToken(Token);
					case "zone":
						return int.TryParse(tail, out n) && ctx.ZoneReachable(n);
					case "now_zone":
						return int.TryParse(tail, out n) && (ctx is INowContext now ? now.ZoneNow(n) : ctx.ZoneReachable(n));
					case "option":
						return o.OptionsOn != null && o.OptionsOn.Contains(tail);
					case "talismans":
						return int.TryParse(tail, out n) && ctx.TalismanCount() >= n;
					case "spell":
						if (o.AbilitySources) return HasAbility(ctx, tail);
						return o.AllowSpells || ctx.HasToken(Token);
					default:
						return ctx.HasToken(Token);
				}
			}

			public override void Missing(IRequirementContext ctx, List<string> into)
			{
				if (!Eval(ctx) && !into.Contains(Token)) into.Add(Token);
			}

			static bool HasAbility(IRequirementContext ctx, string ability)
			{
				return ctx.HasToken("spell:" + ability) || (ctx.Options.Magic && ctx.HasToken("cast:" + ability)) || HasUse(ctx, ability);
			}

			static bool HasUse(IRequirementContext ctx, string ability)
			{
				return ctx.HasToken("worn:" + ability) || ctx.HasToken("perm:" + ability) || (ctx.Options.SingleUseItems && ctx.HasToken("once:" + ability));
			}

			public override void Tokens(List<string> into) { if (!into.Contains(Token)) into.Add(Token); }
			public override string ToString() { return Token; }
		}

		sealed class And : Requirement
		{
			public readonly List<Requirement> Parts = new List<Requirement>();
			public override bool Eval(IRequirementContext ctx) { foreach (var p in Parts) if (!p.Eval(ctx)) return false; return true; }
			public override void Missing(IRequirementContext ctx, List<string> into) { foreach (var p in Parts) if (!p.Eval(ctx)) p.Missing(ctx, into); }
			public override void Tokens(List<string> into) { foreach (var p in Parts) p.Tokens(into); }
			public override string ToString()
			{
				var sb = new StringBuilder();
				for (int i = 0; i < Parts.Count; i++)
				{
					if (i > 0) sb.Append(" & ");
					bool paren = Parts[i] is Or;
					if (paren) sb.Append('(');
					sb.Append(Parts[i]);
					if (paren) sb.Append(')');
				}
				return sb.ToString();
			}
		}

		sealed class Or : Requirement
		{
			public readonly List<Requirement> Parts = new List<Requirement>();
			public override bool Eval(IRequirementContext ctx) { foreach (var p in Parts) if (p.Eval(ctx)) return true; return false; }
			public override void Missing(IRequirementContext ctx, List<string> into) { foreach (var p in Parts) p.Missing(ctx, into); }
			public override void Tokens(List<string> into) { foreach (var p in Parts) p.Tokens(into); }
			public override string ToString()
			{
				var sb = new StringBuilder();
				// an 'and' inside an 'or' is bracketed for reading ('&' binds first either way)
				for (int i = 0; i < Parts.Count; i++)
				{
					if (i > 0) sb.Append(" | ");
					bool paren = Parts[i] is And;
					if (paren) sb.Append('(');
					sb.Append(Parts[i]);
					if (paren) sb.Append(')');
				}
				return sb.ToString();
			}
		}

		// ---------------------------------------------------------------- parser

		public static Requirement Parse(string text)
		{
			string err;
			var r = TryParse(text, out err);
			if (r == null) throw new FormatException(err);
			return r;
		}

		public static Requirement TryParse(string text, out string error)
		{
			error = null;
			var p = new Parser(text ?? "");
			try
			{
				var r = p.ParseOr();
				p.SkipWs();
				if (p.Pos < p.Text.Length) { error = "Unexpected '" + p.Text[p.Pos] + "' at " + p.Pos; return null; }
				return r;
			}
			catch (FormatException e) { error = e.Message; return null; }
		}

		sealed class Parser
		{
			public readonly string Text;
			public int Pos;
			public Parser(string t) { Text = t; }

			public void SkipWs() { while (Pos < Text.Length && char.IsWhiteSpace(Text[Pos])) Pos++; }

			public Requirement ParseOr()
			{
				var first = ParseAnd();
				SkipWs();
				if (Pos < Text.Length && (Text[Pos] == '|' || StartsWord("or")))
				{
					var o = new Or();
					o.Parts.Add(first);
					while (true)
					{
						SkipWs();
						if (Pos < Text.Length && Text[Pos] == '|') Pos++;
						else if (StartsWord("or")) Pos += 2;
						else break;
						o.Parts.Add(ParseAnd());
					}
					return o;
				}
				return first;
			}

			Requirement ParseAnd()
			{
				var first = ParseTerm();
				SkipWs();
				if (Pos < Text.Length && (Text[Pos] == '&' || Text[Pos] == ',' || StartsWord("and")))
				{
					var a = new And();
					a.Parts.Add(first);
					while (true)
					{
						SkipWs();
						if (Pos < Text.Length && (Text[Pos] == '&' || Text[Pos] == ',')) Pos++;
						else if (StartsWord("and")) Pos += 3;
						else break;
						a.Parts.Add(ParseTerm());
					}
					return a;
				}
				return first;
			}

			bool StartsWord(string w)
			{
				if (Pos + w.Length > Text.Length) return false;
				if (string.Compare(Text, Pos, w, 0, w.Length, StringComparison.OrdinalIgnoreCase) != 0) return false;
				return Pos + w.Length == Text.Length || !IsTokenChar(Text[Pos + w.Length]);
			}

			static bool IsTokenChar(char c) { return char.IsLetterOrDigit(c) || c == '_' || c == ':' || c == '-' || c == '.'; }

			Requirement ParseTerm()
			{
				SkipWs();
				// only a whole empty expression is free; an operator with nothing after it is a mistake
				if (Pos >= Text.Length)
				{
					if (Text.Trim().Length == 0) return new Leaf("free");
					throw new FormatException("A term was expected after the operator at the end");
				}
				if (Text[Pos] == '(')
				{
					Pos++;
					var r = ParseOr();
					SkipWs();
					if (Pos >= Text.Length || Text[Pos] != ')') throw new FormatException("')' expected at " + Pos);
					Pos++;
					return r;
				}
				int start = Pos;
				while (Pos < Text.Length && IsTokenChar(Text[Pos])) Pos++;
				if (start == Pos) throw new FormatException("A token was expected at " + Pos);
				return new Leaf(Text.Substring(start, Pos - start));
			}
		}
	}
}
