using System;
using System.IO;
using System.Linq;
using UltimateEditor.Games;
using UwEditor.Core.Editing;
using Xunit;

namespace UwEditor.Core.Tests
{
    public class SchedulesTests
    {
        static string Uw2 { get { return GameData.Game("UW2"); } }

        [Theory]
        [InlineData("DATA")]
        [InlineData("SAVE1")]
        [InlineData("SAVE2")]
        public void Every_block_reads_and_writes_back_byte_for_byte(string folder)
        {
            if (Uw2 == null) return;
            string path = GameProfile.Find(Path.Combine(Uw2, folder), "SCD.ARK");
            if (path == null) return;
            var bytes = File.ReadAllBytes(path);
            var a = (Uw2Archive)Archive.Open(bytes, GameKind.UW2);
            var blocks = ScdArk.Read(a);
            Assert.Equal(16, blocks.Count);
            for (int b = 0; b < blocks.Count; b++)
            {
                Assert.Equal(a.Read(b), blocks[b].ToBytes());
                Assert.True(blocks[b].FirstOutOfOrder() < 0, "block " + b + " is in time order as shipped");
                foreach (var r in blocks[b].Rows) Assert.False(string.IsNullOrEmpty(r.Describe()));
            }
            Assert.Empty(ScdArk.Write(a, blocks));
            Assert.Equal(bytes, a.ToBytes());
        }

        [Fact]
        public void An_unchanged_save_writes_nothing_and_the_names_come_from_the_game()
        {
            if (Uw2 == null) return;
            using (var c = new EditingTests.Copy(Uw2, "SAVE1"))
            {
                var s = GameSession.Open(Path.Combine(c.Folder, "SAVE1"));
                var sc = Schedules.Load(s);
                Assert.Equal("SAVE1", sc.Where);
                Assert.False(sc.HasChanges);
                Assert.Empty(sc.Save());
                Assert.False(File.Exists(sc.Path + ".BKP"));
                // a row acting on an NPC by whoami names it from its conversation
                var row = sc.Blocks.SelectMany(b => b.Rows).FirstOrDefault(r => r.Event == 2 && r.Bytes[7] == 0 && sc.NpcName(r.Bytes[8]) != null);
                Assert.NotNull(row);
                Assert.Contains("(" + sc.NpcName(row.Bytes[8]) + ")", sc.Describe(row));
                var fields = sc.Fields(0, row);
                Assert.Equal(new[] { "Time", "Level", "Once", "Event", "X", "Y", "Critters", "Whoami" }, fields.Take(8).Select(f => f.Name));
                Assert.Equal(16, fields.Sum(f => f.Size));                  // every byte of the row, once
                Assert.Equal(sc.NpcName(row.Bytes[8]), sc.Meaning(row, fields.First(f => f.Name == "Whoami")));
            }
            var d = GameSession.Open(Uw2);
            Assert.Equal("DATA", Schedules.Load(d).Where);
        }

        [Fact]
        public void Schedules_are_refused_for_ultima_underworld()
        {
            string uw1 = GameData.Game("UW");
            if (uw1 == null) return;
            Assert.Throws<InvalidOperationException>(() => Schedules.Load(GameSession.Open(uw1)));
        }

        [Fact]
        public void Edits_keep_the_block_in_time_order_save_and_revert()
        {
            if (Uw2 == null) return;
            using (var c = new EditingTests.Copy(Uw2))
            {
                var s = GameSession.Open(c.Folder);
                var sc = Schedules.Load(s);
                var blk = sc.Blocks[0];
                int n = blk.Rows.Count;
                var timeField = ScdRow.HeaderFields(0)[0];
                Assert.Equal(71, timeField.Max);
                Assert.Throws<ArgumentOutOfRangeException>(() => sc.SetField(0, 0, timeField, 72));

                // the first row moves to the end when given the latest time
                var first = blk.Rows[0];
                int at = sc.SetField(0, 0, timeField, 71);
                Assert.Same(first, blk.Rows[at]);
                Assert.True(blk.FirstOutOfOrder() < 0);
                Assert.True(sc.HasChanges);

                // a row added after another takes its time; a parameter set through its field
                int added = sc.AddRow(0, 3, false);
                Assert.Equal(n + 1, blk.Rows.Count);
                Assert.Equal(blk.Rows[3].Time, blk.Rows[added].Time);
                var r = blk.Rows[added];
                sc.SetField(0, added, ScdRow.HeaderFields(0)[3], 9);              // set a variable
                var var = sc.Fields(0, r).First(f => f.Name == "Variable");
                sc.SetField(0, added, var, 0x190 + 3);
                Assert.Contains("X clock 3", sc.Describe(r));
                int deleted = blk.Rows.Count - 1;
                sc.DeleteRow(0, deleted);

                var written = sc.Save();
                Assert.Equal(new[] { 0 }, written);
                Assert.True(File.Exists(sc.Path + ".BKP"));
                var again = Schedules.Load(GameSession.Open(c.Folder));
                Assert.Equal(blk.Rows.Count, again.Blocks[0].Rows.Count);
                for (int i = 0; i < blk.Rows.Count; i++) Assert.Equal(blk.Rows[i].Bytes, again.Blocks[0].Rows[i].Bytes);
                for (int b = 1; b < 16; b++) Assert.Equal(sc.Blocks[b].ToBytes(), again.Blocks[b].ToBytes());
                Assert.False(again.HasChanges);

                // revert forgets
                again.DeleteRow(1, 0);
                Assert.True(again.HasChanges);
                again.Revert();
                Assert.False(again.HasChanges);
                Assert.Equal(78, again.Blocks[1].Rows.Count);
            }
        }

        [Fact]
        public void A_test_row_takes_its_disabled_rows_along()
        {
            var blk = new ScdBlock();
            ScdRow Row(int t, int ev) { var r = new ScdRow { Time = t, Event = ev, Level = 0xFF }; blk.Rows.Add(r); return r; }
            var a = Row(1, 1); var test = Row(2, 10); var d1 = Row(2, -9); var d2 = Row(2, -4); var b = Row(5, 1);
            var bytes = blk.ToBytes();
            var a2 = (Uw2Archive)Archive.Open(Uw2Archive.Empty(16, 0).ToBytes(), GameKind.UW2);
            a2.Write(3, bytes);
            Assert.Equal(5, ScdArk.Read(a2)[3].Rows.Count);
            // move the test to time 9: its disabled rows go with it
            var timeField = ScdRow.HeaderFields(3)[0];
            timeField.Set(test, 9);
            for (int j = 2; j < blk.Rows.Count && blk.Rows[j].Event < 0; j++) blk.Rows[j].Time = 9;
            blk.SortStable();
            Assert.Equal(new[] { a, b, test, d1, d2 }, blk.Rows);
        }

        [Fact]
        public void Clocks_next_rows_follow_the_rows()
        {
            var blk = new ScdBlock();
            for (int t = 0; t < 4; t++) blk.Rows.Add(new ScdRow { Time = t * 10, Event = 0 });
            blk.Clocks[5 * 4 + 2] = 2;               // level 5 has run rows 0 and 1
            blk.MarkWritten();
            blk.Rows.RemoveAt(0);
            blk.RemapClocks();
            Assert.Equal(1, blk.ClockNext(5));
        }
    }
}
