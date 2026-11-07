using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using UltimateEditor.Games;
using UwEditor.Core.Conversations;

namespace UwEditor.Core.Balance
{
    /// <summary>One item on a side of a trade.</summary>
    public sealed class BarterItem
    {
        public int Item;
        /// <summary>The stack's size (the link of a quantity object, 1-511).</summary>
        public int Quantity = 1;
        /// <summary>0-63.</summary>
        public int Quality = 63;
        /// <summary>Taken from the world: where it lies (for the window's text), else null.</summary>
        public string From;

        public BarterItem Clone() { return (BarterItem)MemberwiseClone(); }
    }

    /// <summary>
    /// A trader's temper (conv/BARTER.C barter_init): how much more than its own side it wants
    /// (greed, in per cent), how many bad offers it takes (patience) and how far its estimate of
    /// a value may be off (assess, in per cent). Each is its creature's figure blurred by a
    /// draw seeded with the trader's object index, so a given trader always draws the same;
    /// then the player's Charm lowers the greed by 2 a point and raises the patience by half.
    /// </summary>
    public sealed class BarterTemper
    {
        public int Greed, Patience, Assess;
        /// <summary>Drawn for a trader of the level (exact); false: the class's typical figures with ranges.</summary>
        public bool Exact;
        public int GreedMin, GreedMax, PatienceMin, PatienceMax, AssessMin, AssessMax;
    }

    /// <summary>The figures of a creature class that trading reads (OBJECTS.DAT creature bytes 0x0D-0x0E).</summary>
    public sealed class TraderClass
    {
        public int Class;
        public string Name;
        /// <summary>0-15: greed is haggle × 6 per cent.</summary>
        public int Haggle;
        /// <summary>0-15.</summary>
        public int Patience;
        /// <summary>0-15: the trader's estimate is off by up to (15 − shrewd) × 6 per cent.</summary>
        public int Shrewd;
        /// <summary>The creature's level (byte 0x0D's low nibble), used when the player demands.</summary>
        public int Level;
    }

    /// <summary>The items a trader likes (worth 1.5 times as much to it) and dislikes (worth nothing, never taken).</summary>
    public sealed class BarterLikes
    {
        /// <summary>Entries as the script has them: below 1000 an item id, 1000 and up the 16 items from (entry − 1000) × 16.</summary>
        public readonly List<int> Likes = new List<int>(), Dislikes = new List<int>();
        /// <summary>The conversation slot they come from, or −1.</summary>
        public int Slot = -1;
        /// <summary>How many calls of set_likes_dislikes the conversation has.</summary>
        public int Calls;

        public bool Any { get { return Likes.Count > 0 || Dislikes.Count > 0; } }

        /// <summary>conv/BARTER.C does_npc_like: 1 liked, −1 disliked or worthless, 0 neither.</summary>
        public int Of(int item, int comValue)
        {
            if (comValue == 0) return -1;
            int cls = (item >> 4) + 1000, liked = 0;
            foreach (int e in Likes) { if (e < 1000) { if (e == item) return 1; } else if (e == cls) liked = 1; }
            foreach (int e in Dislikes) { if (e < 1000) { if (e == item) return -1; } else if (e == cls) liked = -1; }
            return liked;
        }

        /// <summary>An entry in words.</summary>
        public static string Describe(int e, ObjectNames names)
        {
            if (e < 1000) return string.Format("{0} (0x{1:X3})", names.Get(e), e);
            int first = (e - 1000) * 16;
            return string.Format("items 0x{0:X3}-0x{1:X3}", first, first + 15);
        }
    }

    /// <summary>What a trade comes to.</summary>
    public sealed class BarterResult
    {
        /// <summary>The trader's worth of the player's side (with its likes) and of its own side (without), before its blur.</summary>
        public int PlayerWorth, TraderWorth;
        /// <summary>The worth of each item, as on the sides (player's with likes).</summary>
        public List<int> PlayerItems = new List<int>(), TraderItems = new List<int>();
        /// <summary>(P − N) × 100 / N in 16 bits; 100 when the trader's side is worth nothing.</summary>
        public int Evaluation;
        public bool Accepted;
        /// <summary>The evaluation when the trader's blur goes against the player and for the player.</summary>
        public int WorstEvaluation, BestEvaluation;
        public bool AcceptedAtWorst, AcceptedAtBest;
        /// <summary>The player's own appraisal (do_judgement), unblurred: the sentence the game would say.</summary>
        public string Judgement;
        public List<string> Notes = new List<string>();
    }

    /// <summary>
    /// Trading as both games do it (conv/BARTER.C, the same code but where noted). A trader values
    /// an item as COMOBJ value × quantity × quality / 64 (at least 1, 0 at quality 0), 1.5 times
    /// for an item it likes and nothing for one it dislikes; UW2 counts every gold coin (0xA0) at
    /// quality 63. It accepts an offer when (player's side − its side) × 100 / its side reaches
    /// its greed. Charm is the only skill that changes its mind; Appraise only colours the
    /// player's own remark.
    /// </summary>
    public static class Barter
    {
        public const int Coin = 0xA0;

        /// <summary>The trade slots a side has: UW1 4, UW2 6.</summary>
        public static int Slots(GameKind kind) { return kind == GameKind.UW2 ? 6 : 4; }

        /// <summary>
        /// conv/BARTER.C assess_value before the blur, with 16-bit arithmetic as DOS has it: value
        /// × quantity wraps past 32767, and in UW1 so does × quality (UW2 multiplies in 32 bits).
        /// </summary>
        public static int Worth(GameKind kind, int item, int comValue, int quantity, int quality, int disposition)
        {
            if (disposition < 0) return 0;
            int v = comValue;
            if (disposition > 0) v = (short)(v * 3) >> 1;
            v = (short)(v * Math.Max(1, quantity));
            int q = quality & 0x3F;
            if (kind == GameKind.UW2 && item == Coin) q = 0x3F;
            if (v > 0)
            {
                if (q > 0)
                {
                    v = kind == GameKind.UW2 ? (int)(((long)v * q) >> 6) : (short)(v * q) >> 6;
                    if (v == 0) v = 1;
                }
                else v = 0;
            }
            return v;
        }

        /// <summary>Would the 16-bit products wrap for this stack (the game then values it wrongly, even below 0)?</summary>
        public static bool Wraps(GameKind kind, int comValue, int quantity, int quality, bool liked)
        {
            long v = comValue;
            if (liked) v = (v * 3) >> 1;
            v *= Math.Max(1, quantity);
            if (v > short.MaxValue) return true;
            return kind != GameKind.UW2 && v * (quality & 0x3F) > short.MaxValue;
        }

        /// <summary>The trader's temper. With an object index the draws are the game's; without, the class's middle figure and the range.</summary>
        public static BarterTemper Temper(TraderClass c, int objectIndex, int charm)
        {
            var t = new BarterTemper();
            if (objectIndex > 0)
            {
                var r = new BorlandRandom(objectIndex);
                t.Greed = r.Range(c.Haggle * 6, -25, 25);
                t.Patience = r.Range(c.Patience, -20, 100);
                t.Assess = r.Range((15 - c.Shrewd) * 6, -25, 50);
                t.Exact = true;
            }
            else
            {
                t.Greed = c.Haggle * 6;
                t.Patience = c.Patience;
                t.Assess = (15 - c.Shrewd) * 6;
            }
            t.GreedMin = c.Haggle * 6 * 75 / 100 - 2 * charm; t.GreedMax = c.Haggle * 6 * 124 / 100 - 2 * charm;
            t.PatienceMin = c.Patience * 80 / 100 + (charm >> 1); t.PatienceMax = c.Patience * 199 / 100 + (charm >> 1);
            t.AssessMin = (15 - c.Shrewd) * 6 * 75 / 100; t.AssessMax = (15 - c.Shrewd) * 6 * 149 / 100;
            t.Greed = (short)(t.Greed - 2 * charm);
            t.Patience = (short)(t.Patience + (charm >> 1));
            return t;
        }

        /// <summary>do_offer's evaluation: (P − N) × 100 / N in 16 bits, 100 when N is 0.</summary>
        public static int Evaluate(int p, int n)
        {
            p = (short)p; n = (short)n;
            return n > 0 ? (short)((p - n) * 100) / n : 100;
        }

        /// <summary>The worth of each item of a side (with likes for the player's side, without for the trader's).</summary>
        public static List<int> Side(GameKind kind, IList<BarterItem> items, Func<int, int> comValue, BarterLikes likes, bool useLikes)
        {
            var r = new List<int>();
            foreach (var i in items)
            {
                int cv = comValue(i.Item);
                int d = useLikes ? (likes != null ? likes.Of(i.Item, cv) : (cv == 0 ? -1 : 0)) : 0;
                r.Add(Worth(kind, i.Item, cv, i.Quantity, i.Quality, d));
            }
            return r;
        }

        /// <summary>Works a trade out.</summary>
        public static BarterResult Offer(GameKind kind, IList<BarterItem> player, IList<BarterItem> trader, Func<int, int> comValue, BarterLikes likes, BarterTemper temper, int appraise, GameStrings strings)
        {
            var res = new BarterResult();
            res.PlayerItems = Side(kind, player, comValue, likes, true);
            res.TraderItems = Side(kind, trader, comValue, likes, false);
            res.PlayerWorth = (short)res.PlayerItems.Sum();
            res.TraderWorth = (short)res.TraderItems.Sum();
            res.Evaluation = Evaluate(res.PlayerWorth, res.TraderWorth);
            res.Accepted = res.Evaluation >= temper.Greed && (player.Count > 0 || trader.Count > 0);
            // the blur: each item's worth moves by up to ±assess per cent; at worst the player's side
            // is under-valued and the trader's over-valued
            int a = temper.Assess;
            int pw = (short)res.PlayerItems.Sum(v => v - v * a / 100), nb = (short)res.TraderItems.Sum(v => v + v * a / 100);
            int pb = (short)res.PlayerItems.Sum(v => v + v * a / 100), nw = (short)res.TraderItems.Sum(v => v - v * a / 100);
            res.WorstEvaluation = Evaluate(pw, nb);
            res.BestEvaluation = Evaluate(pb, nw);
            res.AcceptedAtWorst = res.WorstEvaluation >= temper.Greed;
            res.AcceptedAtBest = res.BestEvaluation >= temper.Greed;
            res.Judgement = Judgement(kind, player, trader, comValue, appraise, strings);
            for (int i = 0; i < player.Count; i++)
            {
                int cv = comValue(player[i].Item);
                if (Wraps(kind, cv, player[i].Quantity, player[i].Quality, likes != null && likes.Of(player[i].Item, cv) > 0))
                    res.Notes.Add(string.Format("The player's item {0}: value × quantity{1} passes 32767, so the game values it wrongly ({2}).", i + 1, kind == GameKind.UW2 ? "" : " × quality", res.PlayerItems[i]));
            }
            for (int i = 0; i < trader.Count; i++)
                if (Wraps(kind, comValue(trader[i].Item), trader[i].Quantity, trader[i].Quality, false))
                    res.Notes.Add(string.Format("The trader's item {0}: value × quantity{1} passes 32767, so the game values it wrongly ({2}).", i + 1, kind == GameKind.UW2 ? "" : " × quality", res.TraderItems[i]));
            int slots = Slots(kind);
            if (player.Count > slots) res.Notes.Add(string.Format("The player's side has {0} items; the game has {1} trade slots a side.", player.Count, slots));
            if (trader.Count > slots) res.Notes.Add(string.Format("The trader's side has {0} items; the game has {1} trade slots a side.", trader.Count, slots));
            return res;
        }

        /// <summary>do_judgement without its blur: "...I think that I am getting a fair deal." (block 7 strings).</summary>
        public static string Judgement(GameKind kind, IList<BarterItem> player, IList<BarterItem> trader, Func<int, int> comValue, int appraise, GameStrings strings)
        {
            int p = (short)Side(kind, player, comValue, null, false).Sum(), n = (short)Side(kind, trader, comValue, null, false).Sum();
            int e = Evaluate(p, n);
            int certainty = appraise < 6 ? 0 : appraise < 12 ? 1 : appraise < 18 ? 2 : appraise < 24 ? 3 : 4;
            int result = e > 50 ? 0 : e > 35 ? 1 : e > 25 ? 2 : e > 10 ? 3 : e > -10 ? 4 : e > -25 ? 5 : e > -35 ? 6 : e > -50 ? 7 : 8;
            string a = strings?.Get(7, certainty + 3), b = strings?.Get(7, 2), c = strings?.Get(7, result + 8);
            if (a == null || b == null || c == null) return null;
            return a + b + c;
        }

        /// <summary>How far the player's appraisal may be off, in per cent: 50 − appraise × 45 / 30.</summary>
        public static int AppraiseError(int appraise) { return 50 - appraise * 45 / 30; }

        /// <summary>
        /// Coins (0xA0) that settle a trade, as stacks of at most 511: on the player's side when the
        /// trader would refuse (the fewest it accepts), on the trader's side when the player gives
        /// more (the most it still pays). With equalWorth the target is an even trade (an
        /// evaluation of 0) instead of the trader's greed. Empty when nothing is needed or no
        /// number of coins does it within the free slots.
        /// </summary>
        public static List<BarterItem> Settle(GameKind kind, IList<BarterItem> player, IList<BarterItem> trader, Func<int, int> comValue, BarterLikes likes, BarterTemper temper, int coinQuality, bool equalWorth, out bool toPlayer, out string note)
        {
            toPlayer = true; note = null;
            int target = equalWorth ? 0 : temper.Greed;
            int slots = Slots(kind);
            int coinValue = comValue(Coin);
            if (coinValue <= 0) { note = "A gold coin has value 0 in COMOBJ.DAT: traders take no coins."; return new List<BarterItem>(); }
            int p = (short)Side(kind, player, comValue, likes, true).Sum(), n = (short)Side(kind, trader, comValue, likes, false).Sum();
            int now = Evaluate(p, n);
            var added = new List<BarterItem>();
            if (now < target || n <= 0 && p <= 0)
            {
                // the player pays: coins on the player's side, valued with the trader's likes
                int d = likes != null ? likes.Of(Coin, coinValue) : 0;
                if (d < 0) { note = "This trader dislikes gold coins: they are worth nothing to it."; return added; }
                int free = slots - player.Count;
                for (int k = 0; k < Math.Max(0, free); k++)
                {
                    int best = -1;
                    for (int c = 1; c <= 511; c++)
                        if (Evaluate(p + Worth(kind, Coin, coinValue, c, coinQuality, d), n) >= target) { best = c; break; }
                    if (best > 0) { added.Add(new BarterItem { Item = Coin, Quantity = best, Quality = coinQuality }); return added; }
                    // a full stack (the one worth the most: 16-bit products wrap past it in UW1) and go on
                    int top = BestStack(kind, coinValue, coinQuality, d);
                    added.Add(new BarterItem { Item = Coin, Quantity = top, Quality = coinQuality });
                    p = (short)(p + Worth(kind, Coin, coinValue, top, coinQuality, d));
                }
                note = string.Format("Even {0} stacks of coins do not make the trader accept: there are {1} slots a side.", added.Count, slots);
                return new List<BarterItem>();
            }
            // the trader pays the difference: coins on its side, valued without likes
            toPlayer = false;
            int tfree = slots - trader.Count;
            for (int k = 0; k < Math.Max(0, tfree); k++)
            {
                int most = 0;
                for (int c = 1; c <= 511; c++)
                {
                    if (Evaluate(p, n + Worth(kind, Coin, coinValue, c, coinQuality, 0)) >= target) most = c;
                    else break;
                }
                if (most == 0) break;
                added.Add(new BarterItem { Item = Coin, Quantity = most, Quality = coinQuality });
                n = (short)(n + Worth(kind, Coin, coinValue, most, coinQuality, 0));
                if (most < 511) break;
            }
            if (added.Count == 0) note = "The trade is already as close as coins can make it.";
            return added;
        }

        static int BestStack(GameKind kind, int coinValue, int q, int d)
        {
            int best = 1, bw = int.MinValue;
            for (int c = 1; c <= 511; c++) { int w = Worth(kind, Coin, coinValue, c, q, d); if (w > bw) { bw = w; best = c; } }
            return best;
        }

        /// <summary>The coin quality to pay with by default: UW2 always counts 63; UW1 uses the quality the levels' coins have most.</summary>
        public static int DefaultCoinQuality(GameSession s, Func<int, LevelModel> level)
        {
            if (s.IsUw2) return 63;
            var counts = new Dictionary<int, int>();
            foreach (var f in WorldObjects.All(s, level))
                if (f.Object.Item == Coin) { int q = f.Object.Quality; counts[q] = (counts.TryGetValue(q, out var c) ? c : 0) + 1; }
            return counts.Count == 0 ? 40 : counts.OrderByDescending(kv => kv.Value).ThenByDescending(kv => kv.Key).First().Key;
        }

        /// <summary>The trading figures of every creature class (OBJECTS.DAT creature table).</summary>
        public static List<TraderClass> Classes(GameSession s, Editing.ObjectTables tables)
        {
            var list = new List<TraderClass>();
            for (int c = 0; c < 64; c++)
                list.Add(new TraderClass
                {
                    Class = c,
                    Name = s.Names.Get(0x40 + c),
                    Level = tables.Get("Creatures", c, "Trade level"),
                    Shrewd = tables.Get("Creatures", c, "Shrewd"),
                    Haggle = tables.Get("Creatures", c, "Haggle"),
                    Patience = tables.Get("Creatures", c, "Patience"),
                });
            return list;
        }

        /// <summary>The conversation slot a creature talks with (conv/CONVERSE.C): its whoami, or 0x100 + its class when that is 0.</summary>
        public static int ConversationSlot(ObjectInfo o)
        {
            int who = o.IsMobile && o.Record.Length > 0x1A ? o.Record[0x1A] : 0;
            return who > 0 ? who : 0x100 + (o.Item & 0x3F);
        }

        static readonly Regex CallRx = new Regex(@"set_likes_dislikes\(\s*(\w+)\s*,\s*(\w+)\s*\)");
        static readonly Regex StoreRx = new Regex(@"^\s*(\w+?)(\d+)(?:\[(\d+)\])?\s*=\s*(-?\d+)\s*$");

        /// <summary>
        /// The lists a conversation hands set_likes_dislikes (arg 1 the liked items, arg 2 the
        /// disliked, both ending in −1), read from the stores before the first call in the same
        /// function. Null when the conversation never calls it or the lists cannot be read.
        /// </summary>
        public static BarterLikes LikesOf(ConversationLibrary lib, int slot)
        {
            if (lib == null || !lib.Archive.Slots().Contains(slot)) return null;
            Conversation c;
            try { c = lib.Archive.Read(slot); } catch (Exception) { return null; }
            DecompileResult d;
            try { d = Decompiler.Decompile(c, lib.StringsOf(c).ToArray(), lib.Kind, false); } catch (Exception) { return null; }
            var lines = d.Lines.Select(l => l.Text ?? "").ToList();
            var r = new BarterLikes { Slot = slot };
            int first = -1;
            for (int i = 0; i < lines.Count; i++) if (CallRx.IsMatch(lines[i])) { r.Calls++; if (first < 0) first = i; }
            if (first < 0) return null;
            var m = CallRx.Match(lines[first]);
            int start = first;
            while (start > 0 && !lines[start - 1].StartsWith("func_")) start--;
            // the last value stored at each place before the call: name + number (+ element - 1)
            var store = new Dictionary<string, int>();
            for (int i = start; i < first; i++)
            {
                var s = StoreRx.Match(lines[i]);
                if (!s.Success) continue;
                int at = int.Parse(s.Groups[2].Value) + (s.Groups[3].Success ? int.Parse(s.Groups[3].Value) - 1 : 0);
                store[s.Groups[1].Value + at] = int.Parse(s.Groups[4].Value);
            }
            if (!Read(m.Groups[1].Value, store, r.Likes) || !Read(m.Groups[2].Value, store, r.Dislikes)) return null;
            return r;
        }

        static bool Read(string arr, Dictionary<string, int> store, List<int> into)
        {
            var a = Regex.Match(arr, @"^(\D+?)(\d+)$");
            if (!a.Success) return false;
            string kind = a.Groups[1].Value;
            int at = int.Parse(a.Groups[2].Value);
            for (int k = 0; k < 64; k++)
            {
                int v;
                if (!store.TryGetValue(kind + (at + k), out v)) return false;
                if (v < 0) return true;
                into.Add(v);
            }
            return false;
        }
    }
}
