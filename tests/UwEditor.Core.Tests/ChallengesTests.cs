using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UltimateEditor.Games;
using UwEditor.Core.Editing;
using UwEditor.Core.Player;
using UwEditor.Core.Randomizer;
using Xunit;
using Xunit.Abstractions;

namespace UwEditor.Core.Tests
{
    /// <summary>Randomizer ▸ Named challenges: every challenge on both games, the lists stay valid, quest items stay, the counts, Undo, conflicts and the seed.</summary>
    public class ChallengesTests
    {
        readonly ITestOutputHelper output;
        public ChallengesTests(ITestOutputHelper output) { this.output = output; }

        static ChallengeOptions Only(GameSession s, LevelEdits edits, int seed, params Challenge[] cs)
        {
            var o = new ChallengeOptions { Seed = seed, Special = Challenges.DefaultSpecial(s, edits.Model) };
            foreach (var c in cs) o.Set(c, true);
            return o;
        }

        static int Serious(GameSession s, int level, byte[] data)
        {
            return LevelFixes.Find(s, level, data).Count(f => f.Severity == FixSeverity.ListError || f.Severity == FixSeverity.Problem);
        }

        /// <summary>A quest object's words but its next link (which changes when a neighbour in its list goes).</summary>
        static string QuestShape(ObjectInfo o)
        {
            var r = o.Record;
            return string.Format("{0:X4}{1:X4}{2:X2}{3:X4}", r[0] | r[1] << 8, r[2] | r[3] << 8, r[4] & 0x3F, r[6] | r[7] << 8);
        }

        static Dictionary<string, string> QuestObjects(GameSession s, Func<int, LevelModel> level, bool place = true)
        {
            var d = new Dictionary<string, string>();
            foreach (var slot in s.LevelSlots())
            {
                if (!slot.Present) continue;
                var m = level(slot.Level);
                foreach (var o in m.Objects)
                {
                    if (o.Free || o.TileX < 0 || o.IsCritter || o.Item >= 0x140) continue;
                    string kind;
                    bool quest = (KeyItems.Classify(s, o, out kind) != null && kind != "runestone") || NamedItems.Of(s.Kind, o.Item) != null
                        || (ItemCategories.Of(o.Item, s.Kind) & (ItemCategory.Key | ItemCategory.QuestKey | ItemCategory.Quest)) != 0;
                    if (quest) d[slot.Level + ":" + o.Index] = place ? QuestShape(o) : QuestShape(o).Remove(4, 4);
                }
            }
            return d;
        }

        [Theory]
        [InlineData("UW")]
        [InlineData("UW2")]
        public void Every_challenge_keeps_every_level_valid_and_the_quest_items(string game)
        {
            string g = GameData.Game(game); if (g == null) return;
            var s = GameSession.Open(g);
            var edits = new LevelEdits(s);
            var traders = Challenges.TraderSlots(s);
            Assert.NotEmpty(traders);
            var quest = QuestObjects(s, edits.Model);
            Assert.NotEmpty(quest);
            var baseline = new Dictionary<int, int>();
            foreach (var slot in s.LevelSlots()) if (slot.Present) baseline[slot.Level] = Serious(s, slot.Level, s.LoadLevel(slot.Level).Block.Data);
            foreach (var info in Challenges.All)
            {
                var w = ChallengeWorld.Copy(s, edits, null, null, traders);
                var r = Challenges.Run(w, Only(s, edits, 11, info.Id));
                Assert.Null(r.Refused);
                Assert.True(r.Total > 0, info.Name + " changed nothing in " + game);
                foreach (int level in w.OpenedLevels)
                {
                    var e = w.Editor(level);
                    bool ok;
                    string report = e.Check(out ok);
                    Assert.True(ok, info.Name + " on level " + level + ": " + report);
                    Assert.True(Serious(s, level, e.Data) <= baseline[level], info.Name + " added list problems on level " + level);
                    Assert.False(e.AnimChanged, info.Name + " changed the animation list of level " + level);
                }
                // Ghost town puts what a creature carried on the floor where it stood: quest objects too, which only move
                bool moves = info.Id == Challenge.GhostTown;
                var after = QuestObjects(s, l => w.Model(l), !moves);
                foreach (var kv in quest)
                    Assert.True(after.ContainsKey(kv.Key) && after[kv.Key] == (moves ? kv.Value.Remove(4, 4) : kv.Value), info.Name + " touched the quest object " + kv.Key);
                output.WriteLine("{0}: {1} changes, {2} levels, {3} table fields", info.Name, r.Total, r.ChangedLevels().Count, r.TableChanges.Count);
            }
        }

        static IEnumerable<ObjectInfo> Items(LevelModel m)
        {
            return m.Objects.Where(o => !o.Free && o.TileX >= 0 && !o.IsCritter && o.Item < 0x140);
        }

        static bool InCreature(LevelModel m, ObjectInfo o)
        {
            for (int up = o.Parent, g = 0; up >= 0 && g < 64; up = m.Objects[up].Parent, g++) if (m.Objects[up].IsCritter) return true;
            return false;
        }

        [Theory]
        [InlineData("UW")]
        [InlineData("UW2")]
        public void Vow_of_poverty_leaves_no_coins_or_gems_and_no_treasure(string game)
        {
            string g = GameData.Game(game); if (g == null) return;
            var s = GameSession.Open(g);
            var edits = new LevelEdits(s);
            var w = ChallengeWorld.Copy(s, edits, null, null);
            var r = Challenges.Run(w, Only(s, edits, 3, Challenge.VowOfPoverty));
            int left = w.Levels.Sum(l => Items(w.Model(l)).Count(o => Challenges.IsCoinOrGem(s, o.Item)));
            var sec = r.Sections.Single();
            output.WriteLine("left {0}, kept {1}", left, string.Join(", ", sec.Kept.Select(k => k.Key + " " + k.Value)));
            // UW1: plus the one exception, the pile of 20 coins beside Shak (ChallengeExceptionTests)
            Assert.Equal(sec.Kept.Values.Sum() + (s.IsUw2 ? 0 : 1), left);
            if (!s.IsUw2) Assert.Equal(11, sec.Kept["kinds the game looks for (puzzles, inventory traps)"]);   // UW1's emeralds (the puzzle)
            var schema = UltimateEditor.Uw1.Uw1Schemas.Creature;
            for (int row = 0; row < 64; row++)
            {
                Assert.Equal(0, schema["Treasure chance"].Get(w.Objects, 0x132 + 0x30 * row));
                Assert.Equal(0, schema["Treasure rate"].Get(w.Objects, 0x132 + 0x30 * row));
            }
            Assert.Contains(r.TableChanges, t => t.Field == "Treasure rate");
        }

        [Theory]
        [InlineData("UW")]
        [InlineData("UW2")]
        public void Lockdown_locks_are_shaped_as_the_shipped_ones_and_skipped_doors_stay(string game)
        {
            string g = GameData.Game(game); if (g == null) return;
            var s = GameSession.Open(g);
            var edits = new LevelEdits(s);
            // a shipped lock with key 0, as found in the levels
            ObjectInfo shipped = null;
            foreach (var slot in s.LevelSlots().Where(x => x.Present))
            {
                var m0 = s.LoadLevel(slot.Level);
                shipped = m0.Objects.FirstOrDefault(o => !o.Free && o.Item == 0x10F && o.Parent >= 0 && m0.Objects[o.Parent].Item >= 0x140 && m0.Objects[o.Parent].Item <= 0x145 && o.Link == 0x200 && ((o.Record[1] >> 1) & 3) == 3);
                if (shipped != null) break;
            }
            Assert.NotNull(shipped);
            var w = ChallengeWorld.Copy(s, edits, null, null);
            var r = Challenges.Run(w, Only(s, edits, 3, Challenge.Lockdown));
            int locks = 0;
            foreach (int level in w.Levels)
            {
                var before = s.LoadLevel(level);
                var after = w.Model(level);
                foreach (var d in before.Objects.Where(o => !o.Free && o.OnTile && o.Item >= 0x140 && o.Item <= 0x14F))
                {
                    var a = after.Objects[d.Index];
                    Assert.Equal(d.Item, a.Item);
                    var added = a.Children.Where(c => !d.Children.Contains(c)).Select(c => after.Objects[c]).ToList();
                    if (added.Count == 0)
                    {
                        // untouched: the same record (the door's next may not change: nothing on its tile went) and the same contents
                        Assert.Equal(d.Record, a.Record);
                        Assert.Equal(d.Children, a.Children);
                        continue;
                    }
                    Assert.Single(added);
                    var l = added[0];
                    locks++;
                    Assert.True(d.Item <= 0x145, "only closed doors, no portcullis or secret door");
                    Assert.DoesNotContain(d.Children, c => before.Objects[c].Item == 0x10F);
                    Assert.Equal(0x10F, l.Item);
                    // everything as the shipped lock but the difficulty (z) and the next link
                    Assert.Equal(shipped.Record[0], l.Record[0]); Assert.Equal(shipped.Record[1], l.Record[1]);
                    Assert.Equal(shipped.Record[2] & 0x80, l.Record[2] & 0x80); Assert.Equal(shipped.Record[3], l.Record[3]);
                    Assert.Equal(shipped.Record[4] & 0x3F, l.Record[4] & 0x3F);
                    Assert.Equal(shipped.Record[6], l.Record[6]); Assert.Equal(shipped.Record[7], l.Record[7]);
                    Assert.InRange(l.Z, 1, 13);
                    Assert.Equal(a.Children[0], l.Index);   // first in the door's list: checkLock takes the first lock
                }
            }
            Assert.Equal(r.Total, locks);
            Assert.True(locks > 10);
            output.WriteLine("{0} locks; {1}", locks, string.Join(", ", r.Sections[0].Kept.Select(k => k.Key + " " + k.Value)));
        }

        [Fact]
        public void Ranger_draws_follow_the_weights_and_the_ammunition_counts()
        {
            var launchers = new[] { 0x18, 0x19, 0x1A, 0x1F };
            var rng = new Random(1);
            var n = new Dictionary<int, int>();
            var counts = new List<int>();
            int total = 40000, launcher = 0;
            for (int i = 0; i < total; i++)
            {
                var d = Challenges.RangerDraw(rng, launchers, new double[] { 10, 3, 2, 1 });
                if (d.Item2 == 0) { launcher++; int k; n.TryGetValue(d.Item1, out k); n[d.Item1] = k + 1; }
                else { Assert.Contains(d.Item1, new[] { 0x10, 0x11, 0x12 }); Assert.InRange(d.Item2, 1, 15); counts.Add(d.Item2); }
            }
            Assert.InRange(launcher / (double)total, 0.48, 0.52);
            double unit = launcher / 16.0;
            Assert.InRange(n[0x18] / unit, 9.5, 10.5);
            Assert.InRange(n[0x19] / unit, 2.7, 3.3);
            Assert.InRange(n[0x1A] / unit, 1.75, 2.25);
            Assert.InRange(n[0x1F] / unit, 0.85, 1.15);
            Assert.InRange(counts.Average(), 6.8, 7.4);
            Assert.InRange(Enumerable.Range(0, 20000).Select(i => Challenges.AmmoCount(rng)).Max(), 13, 15);
        }

        [Theory]
        [InlineData("UW")]
        [InlineData("UW2")]
        public void Ranger_turns_every_melee_weapon_into_a_launcher_or_ammunition(string game)
        {
            string g = GameData.Game(game); if (g == null) return;
            var s = GameSession.Open(g);
            var edits = new LevelEdits(s);
            var order = Challenges.LaunchersByDamage(s, File.ReadAllBytes(GameProfile.Find(s.DataFolder, "OBJECTS.DAT")));
            Assert.Equal(4, order.Length);
            var tally = new Dictionary<int, int>();
            for (int seed = 1; seed <= 6; seed++)
            {
                var w = ChallengeWorld.Copy(s, edits, null, null);
                var r = Challenges.Run(w, Only(s, edits, seed, Challenge.Ranger));
                int left = 0;
                foreach (int level in w.Levels)
                {
                    var before = s.LoadLevel(level);
                    var after = w.Model(level);
                    foreach (var o in Items(before).Where(o => o.Item <= 0x0E))
                    {
                        var a = after.Objects[o.Index];
                        if (a.Item == o.Item) { left++; continue; }
                        if (ItemTables.Ammo.Contains(a.Item)) { Assert.True(a.IsQuantity); Assert.InRange(a.Link, 1, 15); }
                        else { Assert.Contains(a.Item, ItemTables.Launchers); int k; tally.TryGetValue(a.Item, out k); tally[a.Item] = k + 1; }
                    }
                }
                // what stays a melee weapon is what the challenge leaves alone (quest weapons and the like)
                Assert.Equal(r.Sections[0].Kept.Values.Sum(), left);
            }
            output.WriteLine(string.Join(", ", order.Select(i => s.Names.Get(i) + " " + (tally.ContainsKey(i) ? tally[i] : 0))));
            // roughly 10:3:2:1 from the least to the most damaging
            Assert.True(tally[order[0]] > tally[order[1]] && tally[order[1]] >= tally.GetValueOrDefault(order[3]));
        }

        static bool Good(GameSession s, int item) { return Challenges.IsFood(s, item) || Challenges.IsWeapon(item) || Challenges.IsWearable(s, item); }

        [Theory]
        [InlineData("UW")]
        [InlineData("UW2")]
        public void Hoarding_keeps_the_count_and_puts_everything_in_creatures(string game)
        {
            string g = GameData.Game(game); if (g == null) return;
            var s = GameSession.Open(g);
            var edits = new LevelEdits(s);
            var w = ChallengeWorld.Copy(s, edits, null, null);
            var r = Challenges.Run(w, Only(s, edits, 9, Challenge.Hoarding));
            int before = 0, after = 0, outside = 0;
            var traders = Challenges.TraderSlots(s);
            foreach (int level in w.Levels)
            {
                var m0 = s.LoadLevel(level);
                var m = w.Model(level);
                before += Items(m0).Count(o => Good(s, o.Item));
                after += Items(m).Count(o => Good(s, o.Item));
                foreach (var o in Items(m).Where(o => Good(s, o.Item) && !InCreature(m, o)))
                {
                    // whatever is still outside a creature did not move: a quest item or one a trap names
                    var o0 = m0.Objects[o.Index];
                    Assert.True(!o0.Free && o0.Item == o.Item && !InCreature(m0, o0), "moved but not into a creature: L" + level + " " + o);
                    outside++;
                }
                foreach (var c in m.Objects.Where(c => !c.Free && c.OnTile && c.IsCritter && c.IsMobile && c.Record[0x1A] > 0 && traders.Contains(c.Record[0x1A])))
                    Assert.True(c.Children.Count <= Math.Max(Challenges.TraderCap, m0.Objects[c.Index].Children.Count), "a trader past 40: " + c);
            }
            var sec = r.Sections.Single();
            output.WriteLine("before {0}, after {1}, outside {2}; {3}", before, after, outside, string.Join(" ", sec.Notes));
            Assert.Equal(before, after);
            Assert.True(outside <= sec.Kept.Where(k => !k.Key.Contains("carries already")).Sum(k => k.Value));
        }

        [Theory]
        [InlineData("UW")]
        [InlineData("UW2")]
        public void Merchant_puts_the_food_with_the_traders(string game)
        {
            string g = GameData.Game(game); if (g == null) return;
            var s = GameSession.Open(g);
            var edits = new LevelEdits(s);
            var traders = Challenges.TraderSlots(s);
            var w = ChallengeWorld.Copy(s, edits, null, null, traders);
            var r = Challenges.Run(w, Only(s, edits, 9, Challenge.Merchant));
            foreach (int level in w.Levels)
            {
                var m = w.Model(level);
                foreach (var o in Items(m).Where(o => Challenges.IsFood(s, o.Item)))
                {
                    int up = o.Parent;
                    while (up >= 0 && !m.Objects[up].IsCritter) up = m.Objects[up].Parent;
                    bool withTrader = up >= 0 && traders.Contains(m.Objects[up].Record[0x1A]);
                    var o0 = s.LoadLevel(level).Objects[o.Index];
                    Assert.True(withTrader || (!o0.Free && o0.Item == o.Item), "food outside a trader: L" + level + " " + o);
                }
            }
            var schema = UltimateEditor.Uw1.Uw1Schemas.Creature;
            for (int row = 0; row < 64; row++)
            {
                int item = 0xB0 + schema["Food item"].Get(w.Objects, 0x132 + 0x30 * row);
                if (Challenges.IsFood(s, item)) Assert.Equal(0, schema["Food chance"].Get(w.Objects, 0x132 + 0x30 * row));
            }
            output.WriteLine(string.Join(" ", r.Sections[0].Notes));
        }

        [Theory]
        [InlineData("UW")]
        [InlineData("UW2")]
        public void Food_challenges_count_as_expected(string game)
        {
            string g = GameData.Game(game); if (g == null) return;
            var s = GameSession.Open(g);
            var edits = new LevelEdits(s);
            Func<ChallengeWorld, Func<int, bool>, int> count = (w, f) => w.Levels.Sum(l => Items(w.Model(l)).Count(o => f(o.Item)));
            var w0 = ChallengeWorld.Copy(s, edits, null, null);
            int food = count(w0, i => Challenges.IsFood(s, i));
            var w1 = ChallengeWorld.Copy(s, edits, null, null);
            var r1 = Challenges.Run(w1, Only(s, edits, 4, Challenge.Starvation));
            // UW1: less the one exception, the piece of meat beside Zak (ChallengeExceptionTests)
            Assert.Equal(r1.Sections[0].Kept.Values.Sum(), count(w1, i => Challenges.IsFood(s, i) || i == ItemTables.FishingPole || i == 0xFF) - (s.IsUw2 ? 0 : 1));
            Assert.Equal(0, count(w1, i => i == 0xFF));
            var o = Only(s, edits, 4, Challenge.Fasting);
            o.FastingKeep = 25;
            var w2 = ChallengeWorld.Copy(s, edits, null, null);
            var r2 = Challenges.Run(w2, o);
            int kept = r2.Sections[0].Kept.Values.Sum();
            Assert.Equal((int)Math.Round((food - kept) * 0.25) + kept, count(w2, i => Challenges.IsFood(s, i)));
            var w3 = ChallengeWorld.Copy(s, edits, null, null);
            Challenges.Run(w3, Only(s, edits, 4, Challenge.Vegetarian));
            Assert.True(count(w3, i => Challenges.IsMeatOrFish(s, i)) <= 2);
            Assert.True(count(w3, i => Challenges.IsFood(s, i) && !Challenges.IsMeatOrFish(s, i)) == count(w0, i => Challenges.IsFood(s, i) && !Challenges.IsMeatOrFish(s, i)));
        }

        [Theory]
        [InlineData("UW")]
        [InlineData("UW2")]
        public void Precognition_draws_every_map(string game)
        {
            string g = GameData.Game(game); if (g == null) return;
            var s = GameSession.Open(g);
            var edits = new LevelEdits(s);
            var w = ChallengeWorld.Copy(s, edits, null, null);
            Challenges.Run(w, Only(s, edits, 1, Challenge.Precognition));
            foreach (int level in w.Levels)
            {
                var a = w.Map(level);
                var m = w.Model(level);
                for (int y = 0; y < 64; y++)
                    for (int x = 0; x < 64; x++)
                        Assert.Equal(Automap.Seen(s, m, x, y), a.Map[y * 64 + x]);
                Assert.True(a.SeenCount > 0);
            }
        }

        [Theory]
        [InlineData("UW")]
        [InlineData("UW2")]
        public void Conflicts_are_refused_and_nothing_changes(string game)
        {
            string g = GameData.Game(game); if (g == null) return;
            var s = GameSession.Open(g);
            var edits = new LevelEdits(s);
            foreach (var pair in Challenges.Conflicting)
            {
                var w = ChallengeWorld.Copy(s, edits, null, null);
                var r = Challenges.Run(w, Only(s, edits, 1, pair[0], pair[1], Challenge.Precognition));
                Assert.NotNull(r.Refused);
                Assert.Contains(Challenges.NameOf(pair[0]), r.Refused);
                Assert.Empty(r.ChangedLevels());
                Assert.Throws<InvalidOperationException>(() => Challenges.Apply(r, edits, null, null, "x"));
            }
            Assert.NotNull(Challenges.Run(ChallengeWorld.Copy(s, edits, null, null), new ChallengeOptions()).Refused);
        }

        [Theory]
        [InlineData("UW")]
        [InlineData("UW2")]
        public void The_same_seed_gives_the_same_result(string game)
        {
            string g = GameData.Game(game); if (g == null) return;
            var s = GameSession.Open(g);
            var edits = new LevelEdits(s);
            var cs = new[] { Challenge.Fasting, Challenge.Dystopia, Challenge.Ranger, Challenge.Hoarding };
            var a = ChallengeWorld.Copy(s, edits, null, null);
            var b = ChallengeWorld.Copy(s, edits, null, null);
            var c = ChallengeWorld.Copy(s, edits, null, null);
            var ra = Challenges.Run(a, Only(s, edits, 77, cs));
            var rb = Challenges.Run(b, Only(s, edits, 77, cs));
            Challenges.Run(c, Only(s, edits, 78, cs));
            Assert.Equal(ra.Report.ToString(), rb.Report.ToString());
            foreach (int level in a.Levels) Assert.Equal(a.Editor(level).Data, b.Editor(level).Data);
            Assert.Equal(a.Objects, b.Objects);
            Assert.Contains(a.Levels, l => !a.Editor(l).Data.AsSpan().SequenceEqual(c.Editor(l).Data));
        }

        /// <summary>Every challenge that goes with the others, at once.</summary>
        static readonly Challenge[] Together =
        {
            Challenge.AllergicToMagic, Challenge.BonesRise, Challenge.GhostTown, Challenge.DmdMode, Challenge.Dystopia, Challenge.EternalDarkness, Challenge.Fasting, Challenge.Hoarding,
            Challenge.Illiterate, Challenge.Lockdown, Challenge.Naked, Challenge.OldSchool, Challenge.Precognition, Challenge.Ranger,
            Challenge.Starvation, Challenge.Vegetarian, Challenge.VowOfPoverty,
        };

        [Theory]
        [InlineData("UW")]
        [InlineData("UW2")]
        public void Apply_then_undo_puts_everything_back_byte_for_byte(string game)
        {
            string g = GameData.Game(game); if (g == null) return;
            var s = GameSession.Open(g);
            var edits = new LevelEdits(s);
            var tables = ObjectTables.Load(s);
            var tableBefore = (byte[])tables.ObjectsBytes.Clone();
            var before = s.LevelSlots().Where(x => x.Present).ToDictionary(x => x.Level, x => (byte[])s.LoadLevel(x.Level).Block.Data.Clone());
            var mapsBefore = before.Keys.ToDictionary(l => l, l => (byte[])Automap.Load(s, l).Map.Clone());
            var w = ChallengeWorld.Copy(s, edits, tables.ObjectsBytes, null);
            var r = Challenges.Run(w, Only(s, edits, 21, Together));
            Assert.Null(r.Refused);
            Func<int, string, int, string> set = (row, f, v) => tables.Set("Creatures", row, f, v);
            var applied = Challenges.Apply(r, edits, set, null, "Named challenges");
            Assert.NotEmpty(applied.Before);
            Assert.NotEmpty(applied.MapBefore);
            Assert.Equal(r.TableChanges.Count, applied.Table.Count);
            Assert.True(tables.HasChanges);
            foreach (int level in applied.Before.Keys)
            {
                var e = edits.For(level);
                Assert.Equal(w.Editor(level).Data, e.Data);
                bool ok; e.Check(out ok); Assert.True(ok);
                Assert.Equal("Named challenges", e.UndoLabel);
            }
            output.WriteLine(r.Report.ToString().Split('\n')[0]);
            var skipped = Challenges.Undo(applied, edits, set, null);
            Assert.Empty(skipped);
            foreach (var kv in before)
            {
                var e = edits.Existing(kv.Key);
                if (e != null) Assert.Equal(kv.Value, e.Data);
                Assert.Equal(mapsBefore[kv.Key], edits.AutomapFor(kv.Key).Map);
            }
            Assert.Equal(tableBefore, tables.ObjectsBytes);
            Assert.False(tables.HasChanges);
        }

        [Fact]
        public void A_save_changes_the_players_inventory_and_runes_and_undo_puts_them_back()
        {
            string g = GameData.Game("UW2"); if (g == null) return;
            string save = Path.Combine(g, "SAVE1");
            if (!File.Exists(Path.Combine(save, "PLAYER.DAT"))) return;
            var s = GameSession.Open(save);
            Assert.True(s.IsSave);
            var edits = new LevelEdits(s);
            var sheet = PlayerSheet.Load(GameProfile.Find(save, "PLAYER.DAT"), s.Kind, s.DataFolder);
            var bytes = sheet.ToBytes();
            int items = sheet.Inventory.All().Count();
            var w = ChallengeWorld.Copy(s, edits, null, sheet);
            var r = Challenges.Run(w, Only(s, edits, 5, Challenge.AllergicToMagic, Challenge.Hoarding, Challenge.Dystopia));
            output.WriteLine("player: {0} objects; {1}", items, string.Join("; ", r.Sections.Where(x => x.ByLevel.ContainsKey(-1)).Select(x => Challenges.NameOf(x.Id) + " " + x.ByLevel[-1].Count)));
            var applied = Challenges.Apply(r, edits, null, sheet, "Named challenges");
            if (w.PlayerChanged)
            {
                Assert.True(sheet.Dirty);
                for (int i = 0x44; i < 0x4A; i++) Assert.Equal(0, sheet.Dat.Record[i]);
                Assert.DoesNotContain(sheet.Inventory.All(), it => Challenges.IsRune(it.ItemId));
            }
            Challenges.Undo(applied, edits, null, sheet);
            Assert.Equal(bytes, sheet.ToBytes());
            Assert.Equal(items, sheet.Inventory.All().Count());
            foreach (int level in applied.Before.Keys) Assert.Equal(applied.Before[level].Item1, edits.For(level).Data);
        }

        [Theory]
        [InlineData("UW")]
        [InlineData("UW2")]
        public void Bones_rise_makes_a_skeleton_from_a_skull_and_two_bones(string game)
        {
            string g = GameData.Game(game); if (g == null) return;
            var s = GameSession.Open(g);
            var edits = new LevelEdits(s);
            int skeleton = Challenges.SkeletonItem(s);
            Assert.Equal(s.IsUw2 ? 0x48 : 0x4A, skeleton);
            var w = ChallengeWorld.Copy(s, edits, null, null);
            var r = Challenges.Run(w, Only(s, edits, 1, Challenge.BonesRise));
            int risen = 0;
            foreach (int level in w.Levels)
            {
                var m0 = s.LoadLevel(level);
                var m = w.Model(level);
                Func<LevelModel, Func<int, bool>, int> lying = (mm, f) => mm.Objects.Count(o => !o.Free && o.OnTile && f(o.Item));
                int skulls = lying(m0, Challenges.IsSkull) - lying(m, Challenges.IsSkull);
                int bones = lying(m0, i => Challenges.IsBone(s, i)) - lying(m, i => Challenges.IsBone(s, i));
                int made = lying(m, i => i == skeleton) - lying(m0, i => i == skeleton);
                Assert.Equal(made, skulls);
                Assert.Equal(2 * made, bones);
                risen += made;
                // Garamon's bones (owner 0x3E) stay where they were
                foreach (var o in m0.Objects.Where(o => !o.Free && o.TileX >= 0 && o.Owner == 0x3E && (Challenges.IsSkull(o.Item) || Challenges.IsBone(s, o.Item))))
                    Assert.Equal(QuestShape(o), QuestShape(m.Objects[o.Index]));
                foreach (var o in m.Objects.Where(o => !o.Free && o.OnTile && o.Item == skeleton && m0.Objects[o.Index].Item != skeleton))
                {
                    Assert.Equal(0, (o.Record[0x0D] | o.Record[0x0E] << 8) >> 14);
                    Assert.True(o.Record[8] > 0);
                    Assert.Equal(o.TileX, (o.Record[0x17] >> 2) & 0x3F);
                    Assert.Contains(new LevelBlock(w.Editor(level).Data, s.Kind).MobilesOnTiles(), i => i == o.Index);
                }
                bool ok; w.Editor(level).Check(out ok); Assert.True(ok);
            }
            Assert.Equal(r.Total, risen);
            output.WriteLine("{0} skeletons; {1}", risen, string.Join(" ", r.Sections[0].Notes));
            Assert.True(risen > 0);
        }

        [Theory]
        [InlineData("UW")]
        [InlineData("UW2")]
        public void Ghost_town_leaves_only_talkers_and_special_creatures_and_their_loot(string game)
        {
            string g = GameData.Game(game); if (g == null) return;
            var s = GameSession.Open(g);
            var edits = new LevelEdits(s);
            var opt = Only(s, edits, 1, Challenge.GhostTown);
            var special = new HashSet<string>(opt.Special.Select(c => c.Level + ":" + c.Index));
            var w = ChallengeWorld.Copy(s, edits, null, null);
            var r = Challenges.Run(w, opt);
            int carried = 0, onFloor = 0, templatesLeft = 0;
            foreach (int level in w.Levels)
            {
                var m0 = s.LoadLevel(level);
                var m = w.Model(level);
                foreach (var o in m.Objects.Where(o => !o.Free && o.TileX >= 0 && o.IsCritter))
                    Assert.True(o.Record[0x1A] > 0 || special.Contains(level + ":" + o.Index) || !m0.Objects[o.Index].Free && m0.Objects[o.Index].Item == o.Item, "a creature left: L" + level + " " + o);
                templatesLeft += m.Objects.Count(o => !o.Free && o.TileX >= 0 && o.IsCritter && o.Parent >= 0 && m.Objects[o.Parent].Item == 0x187 && o.Record[0x1A] == 0 && !special.Contains(level + ":" + o.Index));
                foreach (var c in m0.Objects.Where(o => !o.Free && o.OnTile && o.IsCritter && o.Record[0x1A] == 0 && !special.Contains(level + ":" + o.Index)))
                {
                    if (!m.Objects[c.Index].Free && m.Objects[c.Index].Item == c.Item) continue;   // a creature a trap names stays
                    foreach (int ch in c.Children)
                    {
                        carried++;
                        var a = m.Objects[ch];
                        if (!a.Free && a.OnTile && a.TileX == c.TileX && a.TileY == c.TileY && a.Item == m0.Objects[ch].Item) onFloor++;
                    }
                }
                bool ok; w.Editor(level).Check(out ok); Assert.True(ok);
                var b = new LevelBlock(w.Editor(level).Data, s.Kind);
                var on = b.MobilesOnTiles();
                for (int k = 0; k < b.ActiveCount; k++) Assert.Contains(b.Active(k), on);
            }
            output.WriteLine("{0} creatures gone; {1} carried objects, {2} on the floor; {3}", r.Total, carried, onFloor, string.Join(" ", r.Sections[0].Notes));
            Assert.True(carried > 0);
            Assert.Equal(carried, onFloor);
            // only templates that carry a quest object stay (they would take it with them)
            int keptTemplates; r.Sections[0].Kept.TryGetValue("templates carrying a quest object", out keptTemplates);
            Assert.Equal(keptTemplates, templatesLeft);
        }

        [Fact]
        public void The_list_is_alphabetical_and_the_order_has_every_challenge_once()
        {
            var names = Challenges.All.Select(i => i.Name).ToList();
            Assert.Equal(21, names.Count);
            Assert.Equal(names.OrderBy(n => n, StringComparer.OrdinalIgnoreCase).ToList(), names);
            Assert.Equal(Challenges.All.Select(i => i.Id).OrderBy(c => c), Challenges.Order.OrderBy(c => c));
            Assert.Null(Challenges.Conflicts(Together));
        }
    }
}
