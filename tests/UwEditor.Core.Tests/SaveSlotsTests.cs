using System;
using System.IO;
using System.Linq;
using UwEditor.Core.Editing;
using Xunit;

namespace UwEditor.Core.Tests
{
    public class SaveSlotsTests
    {
        [Fact]
        public void Slots_are_listed_with_their_descriptions()
        {
            string uw2 = GameData.Game("UW2");
            if (uw2 == null) return;
            var slots = SaveSlots.List(uw2);
            Assert.Equal(4, slots.Count);
            Assert.Equal("Karl", slots[0].Description);
            Assert.Equal("<not used yet>", slots[1].Description);         // a save whose description is that text
            Assert.True(slots[0].Used && slots[0].HasLevels);
            Assert.False(slots[2].Exists);
            Assert.Equal(SaveSlots.NotUsed, slots[2].Description);
        }

        [Fact]
        public void Descriptions_are_written_as_the_game_writes_them()
        {
            Assert.Equal(new byte[] { 0x20 }, SaveSlots.DescBytes(""));
            Assert.Equal(30, SaveSlots.DescBytes(new string('x', 40)).Length);
            Assert.Equal("ab", System.Text.Encoding.ASCII.GetString(SaveSlots.DescBytes("a\nb")));
        }

        [Fact]
        public void Renaming_and_copying_a_slot_keep_backups()
        {
            string uw2 = GameData.Game("UW2");
            if (uw2 == null) return;
            using (var c = new EditingTests.Copy(uw2, "SAVE1", "SAVE2"))
            {
                var slots = SaveSlots.List(c.Folder);
                SaveSlots.Rename(slots[0], "Before the castle");
                Assert.Equal("Before the castle", SaveSlots.List(c.Folder)[0].Description);
                Assert.True(File.Exists(Path.Combine(c.Folder, "SAVE1", "DESC.BKP")));

                // onto an empty slot: no backup
                Assert.Null(SaveSlots.Copy(slots[0], slots[2]));
                slots = SaveSlots.List(c.Folder);
                Assert.Equal("Before the castle", slots[2].Description);
                Assert.Equal(File.ReadAllBytes(Path.Combine(c.Folder, "SAVE1", "LEV.ARK")), File.ReadAllBytes(Path.Combine(c.Folder, "SAVE3", "LEV.ARK")));
                Assert.NotNull(GameSession.Open(slots[2].Folder));

                // onto a used slot: the old one kept as SAVE2.BKP
                var old2 = File.ReadAllBytes(Path.Combine(c.Folder, "SAVE2", "PLAYER.DAT"));
                string bkp = SaveSlots.Copy(slots[0], slots[1]);
                Assert.Equal(Path.Combine(c.Folder, "SAVE2.BKP"), bkp);
                Assert.Equal(old2, File.ReadAllBytes(Path.Combine(bkp, "PLAYER.DAT")));
                Assert.Equal("Before the castle", SaveSlots.List(c.Folder)[1].Description);
                // again: the older backup is replaced
                SaveSlots.Copy(slots[2], slots[1]);
                Assert.False(Directory.Exists(Path.Combine(c.Folder, "SAVE2.tmp")));
                Assert.Throws<InvalidOperationException>(() => SaveSlots.Copy(slots[1], slots[1]));
                Assert.Throws<DirectoryNotFoundException>(() => SaveSlots.Copy(slots[3], slots[1]));
            }
        }
    }
}
