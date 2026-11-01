using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UltimateEditor.Uw1;
using UltimateEditor.Games;
using UwEditor.Core.Editing;
using UwEditor.Core.Graphics;
using UwEditor.Core.Randomizer;
using Xunit;

namespace UwEditor.Core.Tests
{
    /// <summary>Regressions for the code review of 2026-11-01: each test is one finding.</summary>
    public class ReviewFixTests
    {
        static bool Ok(LevelEditor e) { bool ok; e.Check(out ok); return ok; }

        [Fact]
        public void Clearing_is_quantity_drops_the_link_so_a_quantity_never_becomes_a_list()
        {
            string g = GameData.Game("UW"); if (g == null) return;
            var s = GameSession.Open(g);
            var e = new LevelEdits(s).For(1);
            var o = e.Model.Objects.First(x => !x.Free && x.TileX >= 0 && !x.IsMobile && x.IsQuantity && x.Link > 1 && x.Link < 0x200);
            e.SetObjectField(o.Index, Uw1Schemas.StaticObject["Is quantity"], 0);
            Assert.False(e.Model.Objects[o.Index].IsQuantity);
            Assert.Equal(0, e.Model.Objects[o.Index].Link);
            Assert.True(Ok(e));
            e.DeleteObject(o.Index);
            Assert.True(Ok(e));
        }

        [Fact]
        public void An_item_id_cannot_turn_a_static_record_into_a_creature_or_back()
        {
            string g = GameData.Game("UW"); if (g == null) return;
            var e = new LevelEdits(GameSession.Open(g)).For(1);
            var item = e.Model.Objects.First(x => !x.Free && x.TileX >= 0 && x.Index >= 256);
            var critter = e.Model.Objects.First(x => !x.Free && x.TileX >= 0 && x.IsCritter);
            Assert.Throws<InvalidOperationException>(() => e.SetObjectField(item.Index, Uw1Schemas.StaticObject["Item id"], 0x44));
            Assert.Throws<InvalidOperationException>(() => e.SetObjectField(critter.Index, Uw1Schemas.MobileObject["Item id"], 0x10));
            Assert.False(e.Dirty);
        }

        [Fact]
        public void A_new_creature_does_not_inherit_what_a_free_record_kept()
        {
            string g = GameData.Game("UW"); if (g == null) return;
            var e = new LevelEdits(GameSession.Open(g)).For(1);
            int kind = Enumerable.Range(0x40, 0x40).First(k => !e.Model.Objects.Any(x => !x.Free && x.Item == k));
            var open = Enumerable.Range(0, 64 * 64).Select(i => (x: i % 64, y: i / 64)).First(t => !e.Model.Tiles[t.x, t.y].IsSolid);
            int i0 = e.AddObject(new ObjectSpec { Item = kind }, open.x, open.y);
            var r = e.Model.Objects[i0].Record;
            var S = Uw1Schemas.MobileObject;
            foreach (var n in new[] { "Summoned", "Goal target", "Destination X", "Destination Y", "Talked to" })
                Assert.Equal(0, S[n].Get(r, 0));
            Assert.True(Ok(e));
        }

        [Fact]
        public void Undo_in_the_middle_of_a_stroke_closes_it_and_a_failed_or_empty_change_keeps_redo()
        {
            string g = GameData.Game("UW"); if (g == null) return;
            var e = new LevelEdits(GameSession.Open(g)).For(1);
            var before = (byte[])e.Data.Clone();
            e.SetTileField(30, 30, "Floor height", (e.TileField(30, 30, "Floor height") + 1) % 16);
            e.Undo();
            Assert.True(e.CanRedo);
            e.BeginBatch("Paint");                       // an empty stroke
            e.EndBatch();
            Assert.True(e.CanRedo);
            Assert.ThrowsAny<Exception>(() => e.SetTileField(30, 30, "Floor height", 99));
            Assert.True(e.CanRedo);
            e.Redo();
            e.BeginBatch("Paint");
            e.SetTileField(31, 30, "Floor height", (e.TileField(31, 30, "Floor height") + 1) % 16);
            e.Undo();                                     // mid-stroke: the stroke is one step, undone
            e.EndBatch();                                 // the mouse comes up later: nothing else goes
            e.Undo();
            Assert.Equal(before, e.Data);
        }

        [Fact]
        public void Undoing_a_swap_leaves_a_level_edited_since_alone_and_its_lists_whole()
        {
            string g = GameData.Game("UW"); if (g == null) return;
            var s = GameSession.Open(g);
            var edits = new LevelEdits(s);
            var o = new MonsterSwapOptions { Seed = 3, Scope = "level", Whitelist = new List<IgnoredObject>(), Ignored = new List<IgnoredObject>() };
            var r = MonsterSwap.Run(s, edits.Model, o);
            MonsterSwap.Apply(r, edits, 3);
            int lv = r.ByLevel.Keys.First(l => edits.For(l).Model.Objects.Any(x => !x.Free && x.IsCritter && x.Children.Count > 0 && r.ByLevel[l].Any(k => k.Key == x.Index)));
            var e = edits.For(lv);
            var carrier = e.Model.Objects.First(x => !x.Free && x.IsCritter && x.Children.Count > 0 && r.ByLevel[lv].Any(k => k.Key == x.Index));
            e.DeleteObject(carrier.Children[0]);
            var skipped = MonsterSwap.Revert(r, edits);
            Assert.Equal(new[] { lv }, skipped);
            Assert.True(Ok(e));
            foreach (var l in r.Original.Keys.Where(l => l != lv)) Assert.Equal(s.LoadLevel(l).Block.Data, edits.For(l).Data);
        }

        [Fact]
        public void A_talker_keeps_its_inventory_when_it_moves_within_its_level()
        {
            string g = GameData.Game("UW"); if (g == null) return;
            var s = GameSession.Open(g);
            var edits = new LevelEdits(s);
            var o = new MonsterSwapOptions { Seed = 2, Scope = "all", TalkersInLevel = true, Whitelist = new List<IgnoredObject>(), Ignored = new List<IgnoredObject>() };
            var talkers = new List<(int level, int whoami, int[] items)>();
            foreach (var slot in s.LevelSlots().Where(x => x.Present))
            {
                var m = s.LoadLevel(slot.Level);
                // a conversation number one creature of the level has (several guards can share one)
                var single = m.Objects.Where(x => !x.Free && x.IsCritter && x.TileX >= 0 && x.Record[0x1A] > 0).GroupBy(x => x.Record[0x1A]).Where(gr => gr.Count() == 1).Select(gr => gr.Key).ToHashSet();
                foreach (var c in m.Objects.Where(x => !x.Free && x.IsCritter && x.TileX >= 0 && single.Contains(x.Record[0x1A]) && x.Children.Count > 0))
                    talkers.Add((slot.Level, c.Record[0x1A], c.Children.Select(k => m.Objects[k].Item).OrderBy(k => k).ToArray()));
            }
            Assert.NotEmpty(talkers);
            MonsterSwap.Apply(MonsterSwap.Run(s, edits.Model, o), edits, 2);
            foreach (var t in talkers)
            {
                var m = edits.Model(t.level);
                var now = m.Objects.Where(x => !x.Free && x.IsCritter && x.Record[0x1A] == t.whoami).ToList();
                Assert.Single(now);
                Assert.Equal(t.items, now[0].Children.Select(k => m.Objects[k].Item).OrderBy(k => k).ToArray());
            }
            foreach (var l in s.LevelSlots().Where(x => x.Present)) Assert.True(Ok(edits.For(l.Level)));
        }

        [Fact]
        public void The_shuffle_leaves_objects_holding_triggers_as_they_are()
        {
            string g = GameData.Game("UW"); if (g == null) return;
            var s = GameSession.Open(g);
            var edits = new LevelEdits(s);
            var holders = new List<(int level, int index, byte[] rec)>();
            foreach (var slot in s.LevelSlots().Where(x => x.Present))
            {
                var m = s.LoadLevel(slot.Level);
                foreach (var x in m.Objects.Where(x => !x.Free && x.TileX >= 0 && !x.IsMobile && !x.IsQuantity && x.Children.Any(c => m.Objects[c].IsTrapOrTrigger)))
                    holders.Add((slot.Level, x.Index, (byte[])x.Record.Clone()));
            }
            Assert.NotEmpty(holders);
            var opt = ShuffleOptions.Defaults(s, edits.Model);
            opt.Seed = 7;
            ItemShuffle.Apply(ItemShuffle.Run(s, edits.Model, opt), edits, 7);
            foreach (var h in holders) Assert.Equal(h.rec, edits.Model(h.level).Objects[h.index].Record);
        }

        [Fact]
        public void A_requirement_with_nothing_after_its_operator_is_an_error_not_free()
        {
            string err;
            Assert.Null(Requirement.TryParse("key:5 |", out err));
            Assert.NotNull(err);
            Assert.Null(Requirement.TryParse("key:5 and", out err));
            Assert.NotNull(Requirement.TryParse("", out err));
            Assert.NotNull(Requirement.TryParse("key:5 | key:6", out err));
        }

        [Fact]
        public void Files_sharing_a_name_keep_backups_of_their_own()
        {
            string dir = Path.Combine(Path.GetTempPath(), "uwedit-bkp-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(dir);
            try
            {
                string a = Path.Combine(dir, "CS000.N01"), b = Path.Combine(dir, "CS000.N02");
                File.WriteAllBytes(a, new byte[] { 1 }); File.WriteAllBytes(b, new byte[] { 2 });
                Conversations.SafeFile.Write(a, new byte[] { 11 });
                Conversations.SafeFile.Write(b, new byte[] { 22 });
                Assert.Equal(new byte[] { 1 }, File.ReadAllBytes(a + ".BKP"));
                Assert.Equal(new byte[] { 2 }, File.ReadAllBytes(b + ".BKP"));
                Assert.Equal(new byte[] { 11 }, File.ReadAllBytes(a));
                Assert.Empty(Directory.GetFiles(dir, "*.tmp"));
            }
            finally { Directory.Delete(dir, true); }
        }

        [Fact]
        public void Uw2_critter_frames_with_long_runs_can_be_saved_again_unchanged()
        {
            string g = GameData.Game("UW2"); if (g == null) return;
            var files = CritterArt.Load(g, GameKind.UW2, null);
            var pages = files.SelectMany(f => f.Pages).Where(p => new[] { "CR07.00", "CR13.00", "CR13.01" }.Any(n => p.Path.EndsWith(n, StringComparison.OrdinalIgnoreCase))).ToList();
            Assert.NotEmpty(pages);
            foreach (var p in pages)
                foreach (var k in p.Frames.Keys.ToList())
                    p.Replace(k, p.Frames[k].Pixels.ToArray());
        }

        [Fact]
        public void Uw2_textures_past_the_end_of_T64_are_not_offered()
        {
            string g = GameData.Game("UW2"); if (g == null) return;
            var s = GameSession.Open(g);
            var f = PictureFile.Load(Path.Combine(s.DataFolder, "T64.TR"), s.Kind);
            var pals = Palettes.Load(s.DataFolder);
            Assert.True(f.Count > 256);
            Assert.NotNull(f.Get(0, pals, 0));
            Assert.Null(f.Get(f.Count - 1, pals, 0));
        }
    }
}
