using System;
using System.Linq;
using UwEditor.Core.Conversations;
using UwEditor.Core.Editing;
using UwEditor.Core.Graphics;
using Xunit;

namespace UwEditor.Core.Tests
{
    public class PixelAndItemSlotTests
    {
        static PixelDocument Doc(int w, int h, int colours = 16)
        {
            var c = Enumerable.Range(0, colours).Select(i => i == 0 ? 0u : 0xFF000000u | (uint)(i * 0x111111)).ToArray();
            return new PixelDocument(new IndexedImage(w, h, new int[w * h], c));
        }

        [Fact]
        public void Brushes_lines_rectangles_fill_and_undo_draw_what_they_say()
        {
            var d = Doc(16, 12);
            Assert.Equal(0, d.Transparent);
            d.Begin(); d.Dab(5, 5, 1, 3); d.End();
            Assert.Equal(1, d.Pixels.Count(p => p == 3));
            d.Begin(); d.Dab(5, 5, 3, 4); d.End();
            Assert.Equal(9, d.Pixels.Count(p => p == 4));
            d.Begin(); d.Line(0, 0, 15, 0, 1, 7); d.End();
            Assert.Equal(16, Enumerable.Range(0, 16).Count(x => d.Get(x, 0) == 7));
            d.Begin(); d.Rectangle(1, 2, 4, 6, 1, 9, false); d.End();
            Assert.Equal(9, d.Get(1, 2)); Assert.Equal(9, d.Get(4, 6)); Assert.Equal(0, d.Get(2, 4));
            d.Begin(); int n = d.Fill(2, 4, 2); d.End();
            Assert.Equal(6, n);                                     // the inside of the 4 × 5 outline
            Assert.Throws<ArgumentOutOfRangeException>(() => d.Dab(0, 0, 1, 16));
            d.Undo(); Assert.Equal(0, d.Get(2, 4));
            d.Redo(); Assert.Equal(2, d.Get(2, 4));
            d.Begin(); d.End();                                     // nothing changed: no step
            d.Undo(); d.Undo(); d.Undo(); d.Undo(); d.Undo();
            Assert.False(d.Changed);
            d.Begin(); d.Dab(15, 11, 4, 5); d.End();                // a brush past the edge stays inside
            Assert.Equal(4, d.Pixels.Count(p => p == 5));
            d.Revert();
            Assert.False(d.Changed);
        }

        [Fact]
        public void An_item_without_a_picture_gets_a_blank_one_to_draw_and_it_is_written()
        {
            string g = GameData.Game("UW"); if (g == null) return;
            var s = GameSession.Open(g);
            var f = PictureFile.Load(System.IO.Path.Combine(s.DataFolder, "OBJECTS.GR"), s.Kind);
            var pals = Palettes.Load(s.DataFolder);
            Assert.Null(f.Get(0x1B, pals, 0));                       // UW1's nameless missile slot has no picture
            var blank = f.GetOrBlank(0x1B, pals, 0);
            Assert.NotNull(blank);
            Assert.All(blank.Pixels, p => Assert.Equal(0, p));
            var d = new PixelDocument(blank);
            int c = Enumerable.Range(1, d.Colors.Length - 1).First();
            d.Begin(); d.Rectangle(2, 2, 6, 6, 1, c, true); d.End();
            f.Replace(0x1B, blank, d.Pixels);
            var back = PictureFile.Load(WriteTemp(f.ToBytes()), s.Kind);
            Assert.Equal(d.Pixels, back.Get(0x1B, pals, 0).Pixels);
            Assert.Equal(f.Get(0x1A, pals, 0).Pixels, back.Get(0x1A, pals, 0).Pixels);
        }

        static string WriteTemp(byte[] b)
        {
            string p = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "uwedit-gr-" + Guid.NewGuid().ToString("N") + ".GR");
            System.IO.File.WriteAllBytes(p, b);
            return p;
        }

        [Fact]
        public void Caliburn_is_found_in_Shaks_conversation_the_games_code_and_the_quest_items()
        {
            string g = GameData.Game("UW"); if (g == null) return;
            var s = GameSession.Open(g);
            var ix = ItemUses.IndexConversations(ConversationLibrary.Open(s));
            var r = ItemUses.Find(s, 0x00A, s.LoadLevel, ix, ObjectTables.Load(s), CombinationTable.Load(s));
            Assert.Contains(r.Uses, u => u.Kind == "conversation" && u.Text.Contains("do_inv_create(10)"));
            Assert.Contains(r.Uses, u => u.Kind == "code" && u.Text.Contains("obj/LOOK"));
            Assert.Contains(r.Uses, u => u.Kind == "named" && u.Text.Contains("Caliburn"));
            Assert.True(r.Bound);
            Assert.False(r.Free);
            // the blade and hilt are taken by the same conversation
            var hilt = ItemUses.Find(s, 0x119, s.LoadLevel, ix);
            Assert.Contains(hilt.Uses, u => u.Kind == "conversation" && u.Text.Contains("do_inv_delete(281)"));
        }

        [Theory]
        [InlineData("UW")]
        [InlineData("UW2")]
        public void The_census_finds_free_slots_and_placed_items(string game)
        {
            string g = GameData.Game(game); if (g == null) return;
            var s = GameSession.Open(g);
            var census = ItemUses.Census(s, s.LoadLevel, ItemUses.IndexConversations(ConversationLibrary.Open(s)), ObjectTables.Load(s), CombinationTable.Load(s));
            Assert.Equal(ItemUses.SlotItems().Count(), census.Count);
            Assert.Contains(census, r => r.Free);
            if (!s.IsUw2) Assert.True(census.First(r => r.Item == 0x1B).Free);          // UW1's nameless missile slots
            Assert.Contains(census, r => r.Count("level") > 0);
            Assert.Contains(census, r => r.Count("creature") > 0);                       // OBJECTS.DAT loot
            Assert.NotNull(HardcodedItems.Of(s.Kind, 0x10F));                              // the lock
            Assert.Null(HardcodedItems.Of(s.Kind, 0x1B0));
        }
    }
}
