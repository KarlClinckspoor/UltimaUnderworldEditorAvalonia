using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UltimateEditor.Games;
using UwEditor.Core.Editing;
using UwEditor.Core.Randomizer;
using Xunit;
using Xunit.Abstractions;

namespace UwEditor.Core.Tests
{
    /// <summary>
    /// Phase D of the automatic zones (UW1): Karl's four corrections to the facts (20 coins for
    /// Shak, food for Zak, the goal on level 9, the Key of Love, the orb option), the two Named
    /// challenge exceptions, and the key item shuffle itself, on the shipped levels.
    /// </summary>
    public class KeyShuffleTests
    {
        readonly ITestOutputHelper output;
        public KeyShuffleTests(ITestOutputHelper output) { this.output = output; }

        static readonly object gate = new object();
        static GameSession session;
        static TimelineResult plain;

        static GameSession Session()
        {
            lock (gate)
            {
                if (session != null) return session;
                string g = GameData.Game("UW");
                if (g == null) return null;
                session = GameSession.Open(g);
                return session;
            }
        }

        static TimelineResult Plain()
        {
            lock (gate)
            {
                if (plain != null) return plain;
                var s = Session();
                plain = Timeline.Build(s, l => s.LoadLevel(l), new NavOptions(), new AbilityOptions(), null, null, false);
                return plain;
            }
        }

        static TimelineResult Build(GameSession s, Func<int, LevelModel> level, AbilityOptions opt = null)
        {
            return Timeline.Build(s, level, new NavOptions(), opt ?? new AbilityOptions(), null, null, false);
        }

        /// <summary>Deletes every object of the levels the predicate picks (in memory).</summary>
        static int DeleteAll(GameSession s, LevelEdits edits, Func<ObjectInfo, bool> pick)
        {
            int n = 0;
            foreach (var slot in s.LevelSlots().Where(x => x.Present))
            {
                var e = edits.For(slot.Level);
                foreach (int i in e.Model.Objects.Where(o => !o.Free && o.TileX >= 0 && pick(o)).Select(o => o.Index).OrderByDescending(i => i).ToList())
                {
                    if (e.Model.Objects[i].Free) continue;
                    e.DeleteObject(i);
                    n++;
                }
            }
            return n;
        }

        static IEnumerable<ObjectInfo> Items(LevelModel m) { return m.Objects.Where(o => !o.Free && o.TileX >= 0 && !o.IsCritter && o.Item < 0x140); }

        // ---------------------------------------------------------------- Karl's corrections

        [Fact]
        public void Sword_needs_twenty_coins_counted_by_quantity()
        {
            var s = Session(); if (s == null) return;
            var t = Plain();
            var sword = t.Facts.Facts.Single(f => f.Id == "sword_of_justice");
            Assert.Contains("item:gold_20", sword.Requires);
            Assert.DoesNotContain("item:coin", sword.Requires);
            var gold = t.Facts.Facts.Single(f => f.Id == "gold_20");
            Assert.Equal("count", gold.Kind);
            Assert.Equal(20, gold.AtLeast);
            Assert.Equal(new[] { 0xA0, 0xA1 }, gold.Items.OrderBy(x => x).ToArray());
            Assert.True(t.Solve.GoalReached);
            Assert.True(t.Solve.TokenSphere["item:gold_20"] <= t.Solve.TokenSphere["talisman:sword"]);
            // every coin gone: no sword, no goal
            var edits = new LevelEdits(s);
            Assert.True(DeleteAll(s, edits, o => o.Item == 0xA0 || o.Item == 0xA1) > 50);
            var none = Build(s, edits.Model);
            Assert.False(none.Solve.TokenSphere.ContainsKey("item:gold_20"));
            Assert.False(none.Solve.TokenSphere.ContainsKey("talisman:sword"));
            Assert.False(none.Solve.GoalReached);
            // a stack counts its quantity: 19 is not enough, 19 + 1 is
            var e1 = edits.For(1);
            var start = s.NewGameStart ?? s.Start;
            var spot = Challenges.FreeSpotBeside(s, e1.Model, (int)start.X, (int)start.Y);
            e1.AddObject(new ObjectSpec { Item = 0xA0, Link = 19, IsQuantity = true }, spot.Item1, spot.Item2);
            Assert.False(Build(s, edits.Model).Solve.TokenSphere.ContainsKey("item:gold_20"));
            e1.AddObject(new ObjectSpec { Item = 0xA1, Link = 1, IsQuantity = true }, spot.Item1, spot.Item2);
            var twenty = Build(s, edits.Model);
            Assert.Equal(0, twenty.Solve.TokenSphere["item:gold_20"]);
            Assert.True(twenty.Solve.GoalReached);
        }

        [Fact]
        public void Taper_needs_food_unless_killing_npcs()
        {
            var s = Session(); if (s == null) return;
            var t = Plain();
            Assert.Contains("item:food", t.Facts.Facts.Single(f => f.Id == "taper_of_sacrifice").Requires);
            var edits = new LevelEdits(s);
            DeleteAll(s, edits, o => o.Item >= 0xB0 && o.Item <= 0xBE);
            var none = Build(s, edits.Model);
            Assert.False(none.Solve.TokenSphere.ContainsKey("talisman:taper"));
            Assert.False(none.Solve.GoalReached);
            var kill = Timeline.Solve(none, new AbilityOptions { KillNpcs = true }, "kill");
            Assert.True(kill.TokenSphere.ContainsKey("talisman:taper"));
        }

        [Fact]
        public void The_goal_is_arriving_on_level_9()
        {
            var s = Session(); if (s == null) return;
            var t = Plain();
            Assert.Equal("goal_reach_level9", t.GoalFact.Id);
            Assert.Equal("free", t.GoalRequires);
            Assert.Equal(t.Set.ZoneAt(9, 27, 23), t.GoalZone);
            Assert.DoesNotContain(t.Facts.Facts, f => f.Id == "goal_void_end");
            Assert.True(t.Solve.GoalReached);
            // the ritual's link is the only way in, so the talismans and the burial are still needed
            Assert.True(t.Solve.TokenSphere["flag:garamon_buried"] < t.Solve.ZoneSphere[t.GoalZone]);
        }

        [Fact]
        public void Key_of_love_needs_only_Judy_and_the_picture()
        {
            var s = Session(); if (s == null) return;
            var t = Plain();
            var f = t.Facts.Facts.Single(x => x.Id == "key_of_love");
            Assert.Equal("npc:judy & item:picture_tom", f.Requires);
            // no lava protection and no Telekinesis: magic off, the ring free, nothing single-use
            var sv = Timeline.Solve(t, new AbilityOptions { Magic = false }, "no magic");
            int judy = sv.TokenSphere["npc:judy"], tom = sv.TokenSphere["item:picture_tom"];
            Assert.Equal(Math.Max(judy, tom), sv.TokenSphere["item:key_of_love"]);
        }

        [Fact]
        public void Require_breaking_the_orb_is_an_option()
        {
            var s = Session(); if (s == null) return;
            var t = Plain();
            Assert.Contains("require_orb", FactSet.KnownOptions);
            Assert.False(new AbilityOptions().RequireOrb);
            Assert.DoesNotContain("flag:orb_destroyed", t.GoalFor(new AbilityOptions().ToSolver()));
            var on = new AbilityOptions { RequireOrb = true };
            Assert.Contains("flag:orb_destroyed", t.GoalFor(Timeline.SolverFor(t, on, null)));
            var withOrb = Timeline.Solve(t, on, "orb");
            Assert.True(withOrb.GoalReached);
            // no orb rock: the goal is reached only with the option off
            var edits = new LevelEdits(s);
            Assert.True(DeleteAll(s, edits, o => o.Item == 0x112) >= 3);
            var noRocks = Build(s, edits.Model);
            Assert.True(noRocks.Solve.GoalReached);
            var sv = Timeline.Solve(noRocks, on, "orb, no rocks");
            Assert.False(sv.GoalReached);
            Assert.Contains("flag:orb_destroyed", sv.GoalMissing);
        }

        [Fact]
        public void Gate_travel_scrolls_and_Garamons_bones_follow_their_objects()
        {
            var s = Session(); if (s == null) return;
            var t = Plain();
            Assert.Contains("gate_travel", Abilities.Names);
            Assert.True(t.Set.Placements.Count(p => p.Grants == "once:gate_travel" && p.Source == "levels") >= 4);
            Assert.Contains(t.Set.Placements, p => p.Grants == "cast:gate_travel" && p.Requires.Contains("rune:vas"));
            Assert.DoesNotContain(t.Facts.Facts, f => f.Id.StartsWith("gate_travel_scroll") || f.Id == "garamon_bones");
            Assert.Contains(t.Set.Placements, p => p.Grants == "item:garamon_bones" && p.Notes.StartsWith("#820 "));
            Assert.Empty(t.Facts.Check());
        }

        // ---------------------------------------------------------------- the challenge exceptions

        static ChallengeOptions Only(GameSession s, LevelEdits edits, params Challenge[] cs)
        {
            var o = new ChallengeOptions { Seed = 3, Special = Challenges.DefaultSpecial(s, edits.Model) };
            foreach (var c in cs) o.Set(c, true);
            return o;
        }

        static bool Beside(ObjectInfo o, int x, int y) { return Math.Abs(o.TileX - x) <= 1 && Math.Abs(o.TileY - y) <= 1 && !(o.TileX == x && o.TileY == y); }

        [Fact]
        public void Vow_of_poverty_leaves_twenty_coins_beside_Shak()
        {
            var s = Session(); if (s == null) return;
            var edits = new LevelEdits(s);
            var w = ChallengeWorld.Copy(s, edits, null, null, new HashSet<int>());
            var r = Challenges.Run(w, Only(s, edits, Challenge.VowOfPoverty));
            var coins = w.Levels.SelectMany(l => Items(w.Model(l)).Where(o => o.Item == 0xA0 || o.Item == 0xA1).Select(o => Tuple.Create(l, o))).ToList();
            Assert.Single(coins);
            var pile = coins[0];
            Assert.Equal(2, pile.Item1);
            Assert.True(Beside(pile.Item2, 60, 3), "the pile at " + pile.Item2.TileX + ", " + pile.Item2.TileY);
            Assert.Equal(20, pile.Item2.Link);
            Assert.True(pile.Item2.IsQuantity);
            Assert.Contains(r.Sections[0].Notes, n => n.Contains("exception") && n.Contains("Shak"));
            output.WriteLine(string.Join(" ", r.Sections[0].Notes));
            Assert.Contains("20 coins beside Shak", Challenges.Info(Challenge.VowOfPoverty).Summary);
            var t = Build(s, w.Model);
            Assert.True(t.Solve.TokenSphere.ContainsKey("item:gold_20"));
            Assert.True(t.Solve.GoalReached);
        }

        [Fact]
        public void Starvation_leaves_the_least_food_Zak_takes_beside_him()
        {
            var s = Session(); if (s == null) return;
            var edits = new LevelEdits(s);
            var w = ChallengeWorld.Copy(s, edits, null, null, new HashSet<int>());
            // with Merchant and Fasting after it, the piece of meat still stays
            var r = Challenges.Run(w, Only(s, edits, Challenge.Starvation, Challenge.Merchant));
            Assert.Null(r.Refused);
            var food = w.Levels.SelectMany(l => Items(w.Model(l)).Where(o => Challenges.IsFood(s, o.Item)).Select(o => Tuple.Create(l, o))).ToList();
            var meat = food.Where(x => x.Item1 == 3 && Beside(x.Item2, 47, 38)).ToList();
            Assert.Single(meat);
            Assert.Equal(Challenges.ZakFood, meat[0].Item2.Item);
            Assert.Equal(1, meat[0].Item2.Link);
            Assert.Equal(meat[0].Item2.Parent, -1);
            int kept = r.Sections[0].Kept.Values.Sum();
            Assert.Equal(kept + 1, food.Count + w.Levels.Sum(l => Items(w.Model(l)).Count(o => o.Item == ItemTables.FishingPole || o.Item == 0xFF)));
            Assert.Contains("piece of meat beside Zak", Challenges.Info(Challenge.Starvation).Summary);
            var w2 = ChallengeWorld.Copy(s, edits, null, null, new HashSet<int>());
            Challenges.Run(w2, Only(s, edits, Challenge.Starvation, Challenge.Fasting));
            Assert.Single(w2.Levels.SelectMany(l => Items(w2.Model(l)).Where(o => l == 3 && o.Item == Challenges.ZakFood && Beside(o, 47, 38))));
        }

        [Fact]
        public void Both_exceptions_keep_the_game_beatable()
        {
            var s = Session(); if (s == null) return;
            var edits = new LevelEdits(s);
            var w = ChallengeWorld.Copy(s, edits, null, null, new HashSet<int>());
            Challenges.Run(w, Only(s, edits, Challenge.VowOfPoverty, Challenge.Starvation));
            var t = Build(s, w.Model);
            Assert.True(t.Solve.GoalReached, string.Join(", ", t.Solve.GoalMissing));
            // the coins and the meat are what the sword and the taper rest on: without them, neither
            var only = new LevelEdits(s);
            foreach (int l in w.Levels) only.For(l).Replace(w.Model(l).Block.Data, w.Model(l).TextureBytes, "copy");
            DeleteAll(s, only, o => o.Item == 0xA0 || (o.Item >= 0xB0 && o.Item <= 0xBE));
            var bare = Build(s, only.Model);
            Assert.False(bare.Solve.TokenSphere.ContainsKey("talisman:sword"));
            Assert.False(bare.Solve.TokenSphere.ContainsKey("talisman:taper"));
        }

        // ---------------------------------------------------------------- the shuffle

        static readonly Dictionary<string, KeyShuffleResult> runs = new Dictionary<string, KeyShuffleResult>();

        static KeyShuffleResult Run(string name, Action<KeyShuffleOptions> set, int seed)
        {
            lock (gate)
            {
                string key = name + "|" + seed;
                KeyShuffleResult r;
                if (runs.TryGetValue(key, out r)) return r;
                var o = new KeyShuffleOptions { Seed = seed };
                set?.Invoke(o);
                r = KeyShuffle.Run(Session(), null, o);
                runs[key] = r;
                return r;
            }
        }

        public static IEnumerable<object[]> Sets()
        {
            yield return new object[] { "defaults", 1 };
            yield return new object[] { "defaults", 77 };
            yield return new object[] { "filler", 5 };
            yield return new object[] { "creatures, magic off, single-use", 9 };
            yield return new object[] { "picking on, killing NPCs, orb", 13 };
        }

        static Action<KeyShuffleOptions> Set(string name)
        {
            switch (name)
            {
                case "filler": return o => o.Filler = true;
                case "creatures, magic off, single-use": return o => { o.CreatureInventories = true; o.Logic.Magic = false; o.Logic.SingleUseItems = true; };
                case "picking on, killing NPCs, orb": return o => { o.Logic.LockPicking = true; o.Logic.KillNpcs = true; o.Logic.RequireOrb = true; };
                default: return null;
            }
        }

        [Theory]
        [MemberData(nameof(Sets))]
        public void Shuffled_levels_reach_the_goal_by_a_fresh_solve(string name, int seed)
        {
            var s = Session(); if (s == null) return;
            var r = Run(name, Set(name), seed);
            output.WriteLine("{0} seed {1}: used {2} after {3} attempts, spheres {4} -> {5}, {6} moves", name, seed, r.UsedSeed, r.Attempts, r.SpheresBefore, r.SpheresAfter, r.Moves.Count(m => m.Item != m.Place));
            foreach (var f in r.Failures) output.WriteLine("  " + f);
            Assert.True(r.Success, string.Join("; ", r.Failures));
            Assert.True(r.Moves.Count(m => m.Item != m.Place && m.ByFill) > 50);
            // apply to the levels and solve them afresh with the same options
            var edits = new LevelEdits(s);
            KeyShuffle.Apply(r, edits);
            var t = Timeline.Build(s, edits.Model, r.Options.Nav, r.Options.Logic, null, null, false);
            Assert.True(t.Solve.GoalReached, string.Join(", ", t.Solve.GoalMissing));
            Assert.DoesNotContain(t.Solve.Traps, x => x.Strands.Count > 0);
            foreach (int l in r.After.Keys) { bool ok; edits.For(l).Check(out ok); Assert.True(ok, "level " + l + " fails the list check"); }
        }

        [Theory]
        [MemberData(nameof(Sets))]
        public void Items_keep_their_records(string name, int seed)
        {
            var s = Session(); if (s == null) return;
            var r = Run(name, Set(name), seed);
            Assert.True(r.Success);
            var edits = new LevelEdits(s);
            KeyShuffle.Apply(r, edits);
            Func<Func<int, LevelModel>, List<string>> records = lv => s.LevelSlots().Where(x => x.Present).SelectMany(x => lv(x.Level).Objects.Where(o => !o.Free && o.TileX >= 0)
                .Select(o => string.Format("{0:X3} q{1} o{2} l{3} e{4} n{5} f{6}", o.Item, o.Quality, o.Owner, o.Link, o.Enchanted, o.IsQuantity, (o.Record[1] >> 1) & 7))).OrderBy(x => x, StringComparer.Ordinal).ToList();
            var before = records(l => s.LoadLevel(l));
            var after = records(edits.Model);
            // the same records, item for item; only the unlocked locks (none here) differ
            Assert.Equal(before, after);
            // keys keep their lock numbers: every key moved has the owner it had
            foreach (var m in r.Moves.Where(m => KeyIndex.IsKey(m.Item.Item, s.Kind)))
            {
                var o = edits.Model(m.Place.Level).Objects[m.Place.Index];
                Assert.Equal(m.Item.Item, o.Item);
                Assert.Equal(m.Item.Record.Owner, o.Owner);
            }
            // and the objects' places did not move: positions and lists as before
            foreach (int l in r.After.Keys)
            {
                var a = s.LoadLevel(l); var b = edits.Model(l);
                for (int i = 0; i < a.Objects.Length; i++)
                {
                    Assert.Equal(a.Objects[i].Free, b.Objects[i].Free);
                    Assert.Equal(a.Objects[i].TileX, b.Objects[i].TileX);
                    Assert.Equal(a.Objects[i].Parent, b.Objects[i].Parent);
                }
            }
        }

        [Theory]
        [MemberData(nameof(Sets))]
        public void Nothing_goes_where_it_may_not(string name, int seed)
        {
            var s = Session(); if (s == null) return;
            var r = Run(name, Set(name), seed);
            Assert.True(r.Success);
            var stays = new HashSet<string>(r.Objects.Where(o => o.Stays != null).Select(o => o.Key2));
            foreach (var m in r.Moves)
            {
                Assert.Null(m.Item.Stays);
                Assert.Null(m.Place.Stays);
                Assert.DoesNotContain(m.Place.Key2, stays);
                var lm = s.LoadLevel(m.Place.Level);
                var o = lm.Objects[m.Place.Index];
                Assert.False(o.IsMobile);
                for (int up = o.Parent; up >= 0; up = lm.Objects[up].Parent)
                {
                    var u = lm.Objects[up];
                    Assert.False(u.IsTrapOrTrigger, "into a trap: " + m.Place);
                    if (u.IsCritter)
                    {
                        Assert.True(r.Options.CreatureInventories, "into a creature: " + m.Place);
                        Assert.Equal(0, u.Record[0x1A]);
                    }
                }
                // what a fact names by object, a trigger's target, Tyball's orb, the moonstone, Garamon's grave: never
                Assert.NotEqual(0x117, m.Item.Item);
                Assert.NotEqual(0x126, m.Item.Item);
            }
            // every key item went to a place the logic reaches
            Assert.All(r.Moves.Where(m => m.ByFill), m => Assert.True(m.Sphere >= 0, m.Item + " -> " + m.Place));
        }

        [Fact]
        public void Apply_is_one_step_a_level_and_undo_is_byte_for_byte()
        {
            var s = Session(); if (s == null) return;
            var r = Run("defaults", null, 1);
            var edits = new LevelEdits(s);
            var before = s.LevelSlots().Where(x => x.Present).ToDictionary(x => x.Level, x => (byte[])edits.For(x.Level).Data.Clone());
            KeyShuffle.Apply(r, edits);
            Assert.True(r.After.Count >= 5);
            foreach (int l in r.After.Keys) Assert.False(edits.For(l).Data.AsSpan().SequenceEqual(before[l]));
            // Edit ▸ Undo on a level steps the whole shuffle of that level back
            var one = r.After.Keys.First();
            edits.For(one).Undo();
            Assert.True(edits.For(one).Data.AsSpan().SequenceEqual(before[one]));
            edits.For(one).Redo();
            // Undo shuffle: everything back; a level edited since is left alone
            var skipped = KeyShuffle.Undo(r, edits);
            Assert.Empty(skipped);
            foreach (var kv in before) Assert.True(edits.For(kv.Key).Data.AsSpan().SequenceEqual(kv.Value), "level " + kv.Key);
            Assert.False(r.IsApplied);
        }

        [Fact]
        public void The_spoiler_log_names_places_by_the_painted_zones()
        {
            var s = Session(); if (s == null) return;
            var dir = new DirectoryInfo(AppContext.BaseDirectory);
            while (dir != null && !File.Exists(Path.Combine(dir.FullName, "UltimaUnderworldEditorAvalonia.sln"))) dir = dir.Parent;
            Assert.NotNull(dir);
            var names = ZoneSet.FromJson(File.ReadAllText(Path.Combine(dir.FullName, "docs", "examples", "uw1-zones.json")), s.Profile.LevelSlots);
            // the start square lies in the painted start zone 53
            var start = names.GetZone(53);
            Assert.Equal("\u201C" + start.Name.Trim() + "\u201D", KeyShuffle.PlaceName(names, 1, 32, 2));
            var r = KeyShuffle.Run(s, null, new KeyShuffleOptions { Seed = 5, Names = names, NamesFrom = "Karl's painted UW1 map" });
            Assert.True(r.Success);
            string log = r.Log.ToString();
            Assert.Contains("Places are named by Karl's painted UW1 map", log);
            Assert.Contains(" in \u201C", log);
            System.Console.WriteLine(string.Join("\n", log.Split('\n').Where(l => l.Contains("WHERE EACH") || l.TrimStart().StartsWith("Sphere") || l.Contains(" in \u201C")).Take(14)));
        }

        [Fact]
        public void The_same_seed_gives_the_same_shuffle()
        {
            var s = Session(); if (s == null) return;
            var a = KeyShuffle.Run(s, null, new KeyShuffleOptions { Seed = 42 });
            var b = KeyShuffle.Run(s, null, new KeyShuffleOptions { Seed = 42 });
            var c = KeyShuffle.Run(s, null, new KeyShuffleOptions { Seed = 43 });
            Assert.True(a.Success && b.Success && c.Success);
            Assert.Equal(a.After.Keys.OrderBy(x => x), b.After.Keys.OrderBy(x => x));
            foreach (var kv in a.After) Assert.True(kv.Value.AsSpan().SequenceEqual(b.After[kv.Key]));
            Assert.Equal(a.Log.ToString(), b.Log.ToString());
            Assert.False(a.After.All(kv => c.After.ContainsKey(kv.Key) && kv.Value.AsSpan().SequenceEqual(c.After[kv.Key])));
        }

        [Fact]
        public void With_picking_off_no_key_item_lies_behind_a_pick_only_lock()
        {
            var s = Session(); if (s == null) return;
            var r = Run("defaults", null, 77);
            Assert.True(r.Success);
            Assert.False(r.Options.Logic.LockPicking);
            // the L4 chest #867 has a lock no key opens: only picking would get what lies in it
            foreach (var m in r.Moves.Where(m => m.ByFill))
                Assert.False(m.Place.Requires.Length > 0 && !m.Place.Requires.Contains("key:"), m.Item + " in " + m.Place.Where);
            // and a solve of the shuffled levels with picking off reaches every key item's place
            Assert.All(r.Moves.Where(m => m.ByFill), m => Assert.True(m.Sphere >= 0));
        }

        [Fact]
        public void With_picking_off_a_needed_pick_only_door_is_unlocked()
        {
            var s = Session(); if (s == null) return;
            var edits = new LevelEdits(s);
            // make the door to the shadow beasts (L3 (4, 55), pick 6 or bash; the Key of Courage lies behind it)
            // a door only picking opens: its lock fits no key and the door is tenacious
            var e = edits.For(3);
            var door = e.Model.TileObjects(4, 55).First(o => o.Item >= 0x140 && o.Item <= 0x147);
            int lk = door.Children.First(c => e.Model.Objects[c].Item == 0x10F);
            e.EditBytes("test: pick-only door", (data, tex) =>
            {
                int at = LevelBlock.ObjectOffset(lk);
                int w3 = data[at + 6] | data[at + 7] << 8;
                w3 = (w3 & 0x3F) | (0x200 << 6);       // link 0x200: key 0, no key fits
                data[at + 6] = (byte)w3; data[at + 7] = (byte)(w3 >> 8);
                int d = LevelBlock.ObjectOffset(door.Index);
                data[d + 1] |= 0x20;                    // tenacious: cannot be broken
            });
            string req = ZoneDoors.Requirement(s, e.Model, e.Model.Objects[door.Index], out _);
            Assert.StartsWith("pick:", req);
            Assert.False(Timeline.Build(s, edits.Model, new NavOptions(), new AbilityOptions(), null, null, false).Solve.GoalReached);
            // the keys move: key 10 and the Key of Courage come out, nothing goes behind the door, nothing is unlocked
            var moves = KeyShuffle.Run(s, edits, new KeyShuffleOptions { Seed = 3 });
            Assert.True(moves.Success, string.Join("; ", moves.Failures));
            Assert.Empty(moves.Unlocked);
            Assert.All(moves.Moves.Where(m => m.ByFill), m => Assert.True(m.Sphere >= 0));
            // the keys and the Key of Infinity's parts stay (key 10 in the room behind the door, the Key of Courage
            // behind key 10's door): the door is unlocked, and the log says so
            var opt = new KeyShuffleOptions { Seed = 3 };
            opt.Move.Remove("infinity");
            opt.Move.Remove("keys");
            var r = KeyShuffle.Run(s, edits, opt);
            output.WriteLine(string.Join("\n", r.Unlocked.Select(u => u.ToString())));
            Assert.True(r.Success, string.Join("; ", r.Failures));
            Assert.Contains(r.Unlocked, u => u.Level == 3 && u.X == 4 && u.Y == 55 && u.Lock == lk);
            Assert.Contains("Doors unlocked: L3 (4, 55)", r.Log.ToString());
            KeyShuffle.Apply(r, edits);
            Assert.Equal("free", ZoneDoors.Requirement(s, edits.Model(3), edits.Model(3).Objects[door.Index], out _));
            Assert.True(Timeline.Build(s, edits.Model, new NavOptions(), new AbilityOptions(), null, null, false).Solve.GoalReached);
            // undo puts the lock back too
            KeyShuffle.Undo(r, edits);
            Assert.StartsWith("pick:", ZoneDoors.Requirement(s, edits.Model(3), edits.Model(3).Objects[door.Index], out _));
            // with lock picking on, nothing is unlocked
            var p = KeyShuffle.Run(s, edits, new KeyShuffleOptions { Seed = 3, Logic = new AbilityOptions { LockPicking = true }, Move = opt.Move });
            Assert.True(p.Success);
            Assert.Empty(p.Unlocked);
        }

        [Fact]
        public void The_shuffle_is_saved_into_a_game_copy()
        {
            string g = GameData.Game("UW"); if (g == null) return;
            string tmp = Path.Combine(Path.GetTempPath(), "uwedit-keyshuffle-" + Guid.NewGuid().ToString("N"));
            try
            {
                foreach (var dir in Directory.GetDirectories(g))
                {
                    string n = Path.GetFileName(dir);
                    if (n != "DATA") continue;
                    Directory.CreateDirectory(Path.Combine(tmp, n));
                    foreach (var f in Directory.GetFiles(dir)) File.Copy(f, Path.Combine(tmp, n, Path.GetFileName(f)));
                }
                foreach (var f in Directory.GetFiles(g, "*.EXE")) File.Copy(f, Path.Combine(tmp, Path.GetFileName(f)));
                var s = GameSession.Open(tmp);
                var edits = new LevelEdits(s);
                var r = KeyShuffle.Run(s, edits, new KeyShuffleOptions { Seed = 8 });
                Assert.True(r.Success);
                KeyShuffle.Apply(r, edits);
                var written = edits.Save();
                Assert.Equal(r.After.Keys.OrderBy(x => x), written.OrderBy(x => x));
                var back = GameSession.Open(tmp);
                foreach (var kv in r.After) Assert.True(back.LoadLevel(kv.Key).Block.Data.AsSpan().SequenceEqual(kv.Value), "level " + kv.Key);
                var t = Timeline.Build(back, l => back.LoadLevel(l), new NavOptions(), new AbilityOptions(), null, null, false);
                Assert.True(t.Solve.GoalReached);
                output.WriteLine(r.Log.ToString().Substring(0, Math.Min(3000, r.Log.Length)));
            }
            finally { try { Directory.Delete(tmp, true); } catch (Exception) { } }
        }

        [Fact]
        public void Uw2_is_refused()
        {
            string g = GameData.Game("UW2"); if (g == null) return;
            var s = GameSession.Open(g);
            var r = KeyShuffle.Run(s, null, new KeyShuffleOptions());
            Assert.False(r.Success);
            Assert.NotNull(r.Refused);
        }
    }
}
