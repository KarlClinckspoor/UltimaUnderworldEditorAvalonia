using System;
using System.Collections.Generic;
using System.Linq;
using UwEditor.Core.Editing;
using Xunit;
using Xunit.Abstractions;

namespace UwEditor.Core.Tests
{
    public class ItemCleanerTests
    {
        readonly ITestOutputHelper output;
        public ItemCleanerTests(ITestOutputHelper o) { output = o; }

        static bool Ok(LevelEditor e) { bool ok; e.Check(out ok); return ok; }

        [Theory]
        [InlineData("UW")]
        [InlineData("UW2")]
        public void Debris_and_bones_go_and_the_lists_stay_good(string game)
        {
            string g = GameData.Game(game); if (g == null) return;
            var s = GameSession.Open(g);
            var edits = new LevelEdits(s);
            var levels = s.LevelSlots().Where(x => x.Present).Select(x => x.Level).ToList();
            var opt = new CleanOptions { Kinds = CleanKind.Debris | CleanKind.Bones | CleanKind.Junk };
            var plan = ItemCleaner.Preview(s, edits.Model, levels, opt);
            output.WriteLine(plan.Remove.Count + " to remove");
            foreach (var kv in plan.ByName) output.WriteLine("  " + kv.Key + ": " + kv.Value);
            foreach (var kv in plan.Kept) output.WriteLine("  kept, " + kv.Key + ": " + kv.Value);
            Assert.NotEmpty(plan.Remove);
            Assert.All(plan.Remove, c => Assert.True(c.Inside.Length == 0));   // contents not asked for
            int removed = ItemCleaner.Apply(edits, plan);
            Assert.Equal(plan.Remove.Count, removed);
            foreach (int l in levels)
            {
                var e = edits.Existing(l);
                if (e == null) continue;
                Assert.True(Ok(e), "level " + l);
                // nothing fixable left by the cleaner (the active list, the stacks)
                Assert.DoesNotContain(LevelFixes.Find(e), f => f.Severity == FixSeverity.ListError || f.Check == LevelFixes.ActiveList);
            }
            // a second preview finds nothing more, and one undo per level brings it all back
            Assert.Empty(ItemCleaner.Preview(s, edits.Model, levels, opt).Remove);
            foreach (var e in edits.Editors) { e.Undo(); Assert.False(e.CanUndo); }
            Assert.Equal(plan.Remove.Count, ItemCleaner.Preview(s, edits.Model, levels, opt).Remove.Count);
        }

        [Theory]
        [InlineData("UW")]
        [InlineData("UW2")]
        public void Every_loose_item_with_contents_keeps_the_protected_ones(string game)
        {
            string g = GameData.Game(game); if (g == null) return;
            var s = GameSession.Open(g);
            var edits = new LevelEdits(s);
            var levels = s.LevelSlots().Where(x => x.Present).Select(x => x.Level).ToList();
            var opt = new CleanOptions { Kinds = CleanKind.AllLoose, InsideContainers = true, CreatureInventories = true };
            // what must survive: keys, quest items, texts, named by traps, what talkers carry
            var keep = new Dictionary<int, HashSet<int>>();
            foreach (int l in levels)
            {
                var m = edits.Model(l);
                var set = new HashSet<int>();
                foreach (var o in m.Objects)
                {
                    if (o.Free || o.TileX < 0) continue;
                    string kind;
                    bool key = KeyIndex.IsKey(o.Item, s.Kind) || o.Item == 0x10F;
                    bool quest = UwEditor.Core.Randomizer.KeyItems.Classify(s, o, out kind) != null;
                    bool text = ObjectTexts.TextOf(s, o) != null;
                    bool named = m.Objects.Any(t => !t.Free && t.TileX >= 0 && t.IsTrapOrTrigger && t.Link == o.Index);
                    bool talker = false;
                    for (int p = o.Parent; p >= 0; p = m.Objects[p].Parent) if (m.Objects[p].IsCritter && m.Objects[p].Record[0x1A] > 0) talker = true;
                    if (key || quest || text || named || talker || o.IsCritter || o.Item >= 0x140) set.Add(o.Index);
                }
                keep[l] = set;
            }
            var plan = ItemCleaner.Preview(s, edits.Model, levels, opt);
            output.WriteLine(plan.Remove.Count + " to remove; kept: " + string.Join("; ", plan.Kept.Select(kv => kv.Key + " " + kv.Value)));
            Assert.Contains(plan.Remove, c => c.Inside.Length > 0);
            foreach (var c in plan.Remove) Assert.DoesNotContain(c.Index, keep[c.Level]);
            ItemCleaner.Apply(edits, plan);
            foreach (int l in levels)
            {
                var m = edits.Model(l);
                foreach (int i in keep[l]) Assert.False(m.Objects[i].Free, "level " + l + " #" + i);
                var e = edits.Existing(l);
                if (e != null) Assert.True(Ok(e), "level " + l);
            }
        }

        [Fact]
        public void An_area_and_a_name_narrow_the_preview()
        {
            string g = GameData.Game("UW"); if (g == null) return;
            var s = GameSession.Open(g);
            var edits = new LevelEdits(s);
            var all = ItemCleaner.Preview(s, edits.Model, new[] { 1 }, new CleanOptions { Kinds = CleanKind.AllLoose });
            var some = ItemCleaner.Preview(s, edits.Model, new[] { 1 }, new CleanOptions { Kinds = CleanKind.AllLoose, X0 = 0, Y0 = 0, X1 = 31, Y1 = 31 });
            var bones = ItemCleaner.Preview(s, edits.Model, new[] { 1 }, new CleanOptions { Kinds = CleanKind.AllLoose, Name = "bone" });
            Assert.True(some.Remove.Count < all.Remove.Count);
            Assert.All(some.Remove, c => Assert.True(c.TileX <= 31 && c.TileY <= 31));
            Assert.All(bones.Remove, c => Assert.Contains("bone", c.Name));
        }
    }
}
