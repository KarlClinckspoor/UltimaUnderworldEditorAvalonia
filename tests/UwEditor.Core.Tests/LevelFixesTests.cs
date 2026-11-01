using System;
using System.Collections.Generic;
using System.Linq;
using UltimateEditor.Games;
using UwEditor.Core.Editing;
using Xunit;
using Xunit.Abstractions;

namespace UwEditor.Core.Tests
{
    public class LevelFixesTests
    {
        readonly ITestOutputHelper output;
        public LevelFixesTests(ITestOutputHelper o) { output = o; }

        static bool Ok(LevelEditor e) { bool ok; e.Check(out ok); return ok; }

        [Theory]
        [InlineData("UW")]
        [InlineData("UW2")]
        public void Shipped_levels_findings_and_fix_all(string game)
        {
            string g = GameData.Game(game); if (g == null) return;
            var s = GameSession.Open(g);
            var edits = new LevelEdits(s);
            var ctx = FixContext.Build(s, edits.Model);
            var counts = new SortedDictionary<string, int>();
            foreach (var slot in s.LevelSlots().Where(x => x.Present))
            {
                var e = edits.For(slot.Level);
                var found = LevelFixes.Find(e, ctx);
                foreach (var f in found)
                {
                    counts[f.Check.Title] = counts.TryGetValue(f.Check.Title, out int c) ? c + 1 : 1;
                    output.WriteLine(game + " " + f + (f.CanFix ? "  [" + f.FixText + "]" : ""));
                }
                LevelFixes.FixAll(e, true, ctx);
                Assert.True(Ok(e), game + " level " + slot.Level);
                var left = LevelFixes.Find(e, ctx).Where(f => f.CanFix).ToList();
                foreach (var f in left) output.WriteLine("LEFT " + game + " " + f);
            }
            foreach (var kv in counts) output.WriteLine(game + " total " + kv.Key + ": " + kv.Value);
        }
        // ---- damaged copies of shipped levels, in memory

        static LevelEditor Level1(string game, out GameSession s)
        {
            s = null;
            string g = GameData.Game(game); if (g == null) return null;
            s = GameSession.Open(g);
            return new LevelEdits(s).For(1);
        }

        static int W(byte[] d, int o) { return d[o] | d[o + 1] << 8; }
        static void P(byte[] d, int o, int v) { d[o] = (byte)v; d[o + 1] = (byte)(v >> 8); }
        static int Off(int i) { return LevelBlock.ObjectOffset(i); }
        static void SetNext(byte[] d, int i, int n) { int at = Off(i) + 4; P(d, at, (W(d, at) & 0x3F) | n << 6); }

        /// <summary>Damages the level (one undoable step), checks the damage is seen, then fixes it with the finding's own Fix and with Fix all.</summary>
        void DamageAndFix(LevelEditor e, Action<byte[], LevelModel> damage, FixCheck expected, bool listBroken, bool singleFixRestores = true)
        {
            var m = e.Model;
            var original = (byte[])e.Data.Clone();
            e.EditBytes("damage", (d, t) => damage(d, m));
            Assert.Equal(!listBroken, Ok(e));
            var found = LevelFixes.Find(e);
            foreach (var f in found) output.WriteLine(f + (f.CanFix ? "  [" + f.FixText + "]" : ""));
            var mine = found.Where(f => f.Check == expected).ToList();
            Assert.NotEmpty(mine);
            Assert.All(mine, f => Assert.True(f.CanFix));
            if (singleFixRestores)
            {
                Assert.True(LevelFixes.Fix(e, mine[0]));
                Assert.True(Ok(e));
                Assert.DoesNotContain(LevelFixes.Find(e), f => f.Check == expected);
                e.Undo();
                Assert.Equal(!listBroken, Ok(e));
            }
            Assert.True(LevelFixes.FixAll(e) > 0);
            Assert.True(Ok(e));
            Assert.DoesNotContain(LevelFixes.Find(e), f => f.Severity != FixSeverity.Warning && f.CanFix);
            // the fix is one step: undo brings back the damage, two undos the shipped level
            e.Undo();
            Assert.Equal(!listBroken, Ok(e));
            e.Undo();
            Assert.Equal(original, e.Data);
        }

        static ObjectInfo Chain(LevelModel m, int length, Func<ObjectInfo, bool> where = null)
        {
            // an object on a tile with at least `length` objects after it
            foreach (var o in m.Objects)
            {
                if (o.Free || !o.OnTile || (where != null && !where(o))) continue;
                int n = 0, i = o.Next;
                while (i > 0 && n < length) { n++; i = m.Objects[i].Next; }
                if (n >= length) return o;
            }
            return null;
        }

        [Theory]
        [InlineData("UW")]
        [InlineData("UW2")]
        public void A_next_link_that_loops_is_cut(string game)
        {
            GameSession s; var e = Level1(game, out s); if (e == null) return;
            DamageAndFix(e, (d, m) =>
            {
                var o = Chain(m, 2);
                int last = o.Next; while (m.Objects[last].Next > 0) last = m.Objects[last].Next;
                SetNext(d, last, o.Index);   // the tile's last object points back at the first
            }, LevelFixes.ReachedTwice, true);
        }

        [Theory]
        [InlineData("UW")]
        [InlineData("UW2")]
        public void Two_tiles_sharing_a_chain_are_parted(string game)
        {
            GameSession s; var e = Level1(game, out s); if (e == null) return;
            DamageAndFix(e, (d, m) =>
            {
                var a = Chain(m, 1);
                var b = m.Objects.First(o => !o.Free && o.OnTile && o.Next == 0 && (o.TileX != a.TileX || o.TileY != a.TileY));
                SetNext(d, b.Index, a.Index);   // another tile's list runs on into a's
            }, LevelFixes.ReachedTwice, true);
        }

        [Theory]
        [InlineData("UW")]
        [InlineData("UW2")]
        public void A_cut_off_tail_is_freed(string game)
        {
            GameSession s; var e = Level1(game, out s); if (e == null) return;
            ObjectInfo cut = null;
            DamageAndFix(e, (d, m) =>
            {
                cut = Chain(m, 2, o => !o.IsMobile);
                SetNext(d, cut.Index, 0);   // the objects after it are on no tile now
            }, LevelFixes.Unreferenced, true);
        }

        [Theory]
        [InlineData("UW")]
        [InlineData("UW2")]
        public void A_free_stack_entry_pushed_twice_is_removed(string game)
        {
            GameSession s; var e = Level1(game, out s); if (e == null) return;
            DamageAndFix(e, (d, m) =>
            {
                int top = (short)W(d, LevelBlock.StaticTopAt);
                P(d, LevelBlock.StaticFreeAt + 2 * (top + 1), W(d, LevelBlock.StaticFreeAt + 2 * top));
                P(d, LevelBlock.StaticTopAt, top + 1);
            }, LevelFixes.FreeStacks, true);
        }

        [Theory]
        [InlineData("UW")]
        [InlineData("UW2")]
        public void A_free_stack_entry_overwritten_by_another_is_mended(string game)
        {
            GameSession s; var e = Level1(game, out s); if (e == null) return;
            DamageAndFix(e, (d, m) =>
            {
                // an entry overwritten with its neighbour's index: one index twice, one lost
                int top = (short)W(d, LevelBlock.MobTopAt);
                P(d, LevelBlock.MobFreeAt + 2 * (top - 1), W(d, LevelBlock.MobFreeAt + 2 * top));
            }, LevelFixes.FreeStacks, true, singleFixRestores: false);
        }

        [Theory]
        [InlineData("UW")]
        [InlineData("UW2")]
        public void A_live_object_on_the_free_stack_is_taken_off(string game)
        {
            GameSession s; var e = Level1(game, out s); if (e == null) return;
            DamageAndFix(e, (d, m) =>
            {
                var o = m.Objects.First(x => !x.Free && x.OnTile && !x.IsMobile);
                int top = (short)W(d, LevelBlock.StaticTopAt);
                P(d, LevelBlock.StaticFreeAt + 2 * (top + 1), o.Index);
                P(d, LevelBlock.StaticTopAt, top + 1);
            }, LevelFixes.FreeStacks, true);
        }

        [Theory]
        [InlineData("UW")]
        [InlineData("UW2")]
        public void A_creature_dropped_from_the_active_list_is_put_back(string game)
        {
            GameSession s; var e = Level1(game, out s); if (e == null) return;
            int dropped = -1;
            DamageAndFix(e, (d, m) =>
            {
                int n = W(d, LevelBlock.ActiveCountAt);
                dropped = d[LevelBlock.ActiveAt];
                d[LevelBlock.ActiveAt] = d[LevelBlock.ActiveAt + n - 1];
                d[LevelBlock.ActiveAt + n - 1] = 0;
                P(d, LevelBlock.ActiveCountAt, n - 1);
            }, LevelFixes.ActiveList, false);
            e.Redo(); e.Redo();   // the damage and its Fix all again
            Assert.True(Ok(e));
            var b = new LevelBlock(e.Data, s.Kind);
            Assert.Contains(dropped, Enumerable.Range(0, b.ActiveCount).Select(b.Active));
        }

        [Theory]
        [InlineData("UW")]
        [InlineData("UW2")]
        public void A_freed_or_doubled_active_entry_is_removed(string game)
        {
            GameSession s; var e = Level1(game, out s); if (e == null) return;
            DamageAndFix(e, (d, m) =>
            {
                int n = W(d, LevelBlock.ActiveCountAt);
                d[LevelBlock.ActiveAt + n] = d[LevelBlock.ActiveAt];                       // twice
                d[LevelBlock.ActiveAt + n + 1] = (byte)W(d, LevelBlock.MobFreeAt);          // free
                P(d, LevelBlock.ActiveCountAt, n + 2);
            }, LevelFixes.ActiveList, false);
        }

        [Theory]
        [InlineData("UW")]
        [InlineData("UW2")]
        public void A_list_under_an_is_quantity_object_is_given_back(string game)
        {
            GameSession s; var e = Level1(game, out s); if (e == null) return;
            int holder = -1;
            DamageAndFix(e, (d, m) =>
            {
                var c = m.Objects.First(o => !o.Free && o.OnTile && !o.IsQuantity && o.Children.Count >= 2 && (ItemCategories.Of(o.Item, s.Kind) & ItemCategory.Container) != 0);
                holder = c.Index;
                P(d, Off(c.Index), W(d, Off(c.Index)) | 0x8000);   // is-quantity set over its contents
            }, LevelFixes.LostContents, true);
            e.Redo(); e.Redo();   // the damage and its Fix all again
            Assert.True(Ok(e));
            Assert.False(e.Model.Objects[holder].IsQuantity);
            Assert.NotEmpty(e.Model.Objects[holder].Children);
        }

        [Theory]
        [InlineData("UW")]
        [InlineData("UW2")]
        public void Mobile_tile_bytes_and_hit_points_are_mended(string game)
        {
            GameSession s; var e = Level1(game, out s); if (e == null) return;
            DamageAndFix(e, (d, m) =>
            {
                var c = m.Objects.First(o => !o.Free && o.OnTile && o.IsCritter);
                int at = Off(c.Index) + 0x16;
                P(d, at, (W(d, at) & 0xF) | ((c.TileY + 1) & 0x3F) << 4 | (c.TileX & 0x3F) << 10);
            }, LevelFixes.HomeTile, false);
            DamageAndFix(e, (d, m) =>
            {
                var c = m.Objects.Where(o => !o.Free && o.OnTile && o.IsCritter).Skip(1).First(o => Races.ComObjByte(s, o.Item, 9) >> 2 <= 10);
                d[Off(c.Index) + 8] = 0;
            }, LevelFixes.DeadCreature, false);
        }

        [Theory]
        [InlineData("UW")]
        [InlineData("UW2")]
        public void A_trigger_pointing_at_a_free_slot_is_cleared(string game)
        {
            GameSession s; var e = Level1(game, out s); if (e == null) return;
            DamageAndFix(e, (d, m) =>
            {
                var t = m.Objects.First(o => !o.Free && o.TileX >= 0 && o.Item >= 0x1A0 && o.Item < 0x1C0 && o.Link > 0);
                int free = W(d, LevelBlock.StaticFreeAt);
                int at = Off(t.Index) + 6;
                P(d, at, (W(d, at) & 0x3F) | free << 6);
            }, LevelFixes.TriggerLink, false);
        }

        [Theory]
        [InlineData("UW")]
        [InlineData("UW2")]
        public void A_lost_trap_named_by_its_trigger_goes_back_on_the_trigger_tile(string game)
        {
            GameSession s; var e = Level1(game, out s); if (e == null) return;
            int trap = -1, tx = -1, ty = -1;
            DamageAndFix(e, (d, m) =>
            {
                var t = m.Objects.First(o => !o.Free && o.OnTile && o.Item >= 0x1A0 && o.Item < 0x1C0 && o.Link > 0 && m.Objects[o.Link].OnTile && m.Objects[o.Link].Next == 0
                    && m.Tiles[m.Objects[o.Link].TileX, m.Objects[o.Link].TileY].FirstObject != o.Link);
                var trapO = m.Objects[t.Link];
                trap = trapO.Index; tx = t.TileX; ty = t.TileY;
                // unlink the trap from its tile's list (it is the last there)
                int head = m.Tiles[trapO.TileX, trapO.TileY].FirstObject, p = head;
                while (p > 0 && m.Objects[p].Next != trap) p = m.Objects[p].Next;
                Assert.True(p > 0);
                SetNext(d, p, 0);
            }, LevelFixes.Unreferenced, true);
            e.Redo(); e.Redo();   // the damage and its Fix all again
            Assert.True(Ok(e));
            // on the tile of a trap or trigger that names it (the first one, by index)
            var back = e.Model.Objects[trap];
            Assert.True(back.OnTile);
            Assert.Contains(e.Model.Objects, o => !o.Free && o.IsTrapOrTrigger && o.Link == trap && o.TileX == back.TileX && o.TileY == back.TileY);
        }

        [Fact]
        public void Fix_all_ends_with_no_fixable_finding_on_heavy_damage()
        {
            GameSession s; var e = Level1("UW", out s); if (e == null) return;
            var rnd = new Random(7);
            e.EditBytes("damage", (d, t) =>
            {
                // scramble a hundred next links and a few stack entries
                for (int k = 0; k < 100; k++) SetNext(d, rnd.Next(2, 1024), rnd.Next(0, 1024));
                for (int k = 0; k < 10; k++) P(d, LevelBlock.StaticFreeAt + 2 * rnd.Next(0, 100), rnd.Next(0, 1024));
                P(d, LevelBlock.ActiveCountAt, 300);
            });
            Assert.False(Ok(e));
            output.WriteLine(LevelFixes.Find(e).Count + " findings");
            Assert.True(LevelFixes.FixAll(e) > 0);
            Assert.True(Ok(e));
            Assert.DoesNotContain(LevelFixes.Find(e), f => f.Severity != FixSeverity.Warning && f.CanFix);
        }
    }
}
