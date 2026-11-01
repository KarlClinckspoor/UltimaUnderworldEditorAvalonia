using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UltimateEditor.Games;
using UwEditor.Core.Editing;
using Xunit;

namespace UwEditor.Core.Tests
{
    /// <summary>The game tables (TERRAIN.DAT, SKILLS.DAT, CMB.DAT, SHADES.DAT, DL.DAT) and a level's texture map.</summary>
    public class GameTablesTests
    {
        static IEnumerable<string> Games()
        {
            foreach (var n in new[] { "UW", "UW2" }) { var g = GameData.Game(n); if (g != null) yield return g; }
        }

        static List<DataTable> All(GameSession s)
        {
            return new List<DataTable> { TerrainTable.Load(s), SkillsTable.Load(s), CombinationTable.Load(s), ShadesTable.Load(s), MinimumLightTable.Load(s) };
        }

        /// <summary>The offsets where two arrays differ.</summary>
        static List<int> Diff(byte[] a, byte[] b)
        {
            Assert.Equal(a.Length, b.Length);
            return Enumerable.Range(0, a.Length).Where(i => a[i] != b[i]).ToList();
        }

        [Fact]
        public void Each_table_writes_its_file_back_unchanged()
        {
            foreach (var g in Games())
            {
                var s = GameSession.Open(g);
                foreach (var t in All(s))
                {
                    if (t is MinimumLightTable && !s.IsUw2) { Assert.False(t.Exists); continue; }
                    Assert.True(t.Exists, t.FileName + " in " + g);
                    Assert.Equal(File.ReadAllBytes(t.Path), t.ToBytes());
                    Assert.False(t.HasChanges, t.FileName);
                }
            }
        }

        [Fact]
        public void Terrain_edit_changes_only_its_word_and_names_the_types()
        {
            foreach (var g in Games())
            {
                var s = GameSession.Open(g);
                var t = TerrainTable.Load(s);
                var before = t.ToBytes();
                if (s.IsUw2)
                {
                    Assert.Equal(256, t.Count);
                    Assert.Equal(256, t.Textures(false));                       // T64.TR has 264; the table 256
                    Assert.Equal("water, current to the east", TerrainTable.Describe(s.Kind, true, t.Get(187)));
                    Assert.Equal("ice, grips (not slippery)", TerrainTable.Describe(s.Kind, true, 0xF8));
                    Assert.Equal("lava", TerrainTable.Describe(s.Kind, true, t.Get(60)));
                    Assert.Equal("normal, stairs down", TerrainTable.Describe(s.Kind, false, t.Get(0)));
                }
                else
                {
                    Assert.Equal(512, t.Count);
                    Assert.Equal(210, t.Textures(false));
                    Assert.Equal(52, t.Textures(true));
                    Assert.Equal(0x10, t.Get(t.EntryOf(16, true)));             // F32 16: water
                    Assert.Equal("water", TerrainTable.Describe(s.Kind, true, 0x10));
                    Assert.Equal("tapestry", TerrainTable.Describe(s.Kind, false, 0x0A));
                }
                int e = t.EntryOf(5, true);
                int v = s.IsUw2 ? TerrainTable.Uw2Compose(t.Get(e), 2, 0, 0) : 0x20;
                Assert.NotEqual(v, t.Get(e));
                t.Set(e, v);
                Assert.True(t.IsChanged(e));
                Assert.False(t.IsChanged(e + 1));
                Assert.True(t.HasChanges);
                Assert.Equal(new[] { 2 * e }, Diff(before, t.ToBytes()));
                t.Revert();
                Assert.False(t.HasChanges);
                Assert.Throws<ArgumentOutOfRangeException>(() => t.Set(e, 0x10000));
            }
        }

        [Fact]
        public void Skills_edit_changes_only_its_bytes_and_saving_reads_back()
        {
            foreach (var g in Games())
                using (var c = new EditingTests.Copy(g))
                {
                    var s = GameSession.Open(c.Folder);
                    var t = SkillsTable.Load(s);
                    var before = t.ToBytes();
                    Assert.Equal("Attack", t.SkillName(0));
                    Assert.Equal("gives Attack", t.Explain(0, 0));
                    Assert.Empty(t.Check());
                    // an attribute: one byte
                    t.SetAttribute(2, 1, 25);
                    Assert.True(t.AttributeChanged(2, 1));
                    Assert.False(t.AttributeChanged(2, 0));
                    Assert.Equal(new[] { 2 * 4 + 1 }, Diff(before, t.ToBytes()));
                    // a skill swapped for another in a menu: the length stays, one byte changes
                    var mage = t.Choice(1, 4).ToList();                   // Mage's last entry: Mana or Casting
                    Assert.Equal(new[] { 7, 9 }, mage);
                    t.SetChoice(1, 4, new[] { 7, 8 });
                    Assert.True(t.ChoiceChanged(1, 4));
                    Assert.False(t.ChoiceChanged(1, 3));
                    Assert.Equal(2, Diff(before, t.ToBytes()).Count);
                    // an empty entry before others is reported
                    t.SetChoice(0, 2, new int[0]);
                    Assert.Contains(t.Check(), m => m.Contains("Fighter") && m.Contains("entry 3"));
                    t.Revert();
                    Assert.False(t.HasChanges);
                    t.SetAttribute(7, 3, 21);
                    t.Save();
                    Assert.False(t.HasChanges);
                    Assert.True(File.Exists(t.Path + ".ORIG"));
                    var back = SkillsTable.Load(GameSession.Open(c.Folder));
                    Assert.Equal(21, back.Attribute(7, 3));
                    Assert.Equal(new[] { 7 * 4 + 3 }, Diff(before, back.ToBytes()));
                }
        }

        [Fact]
        public void Combinations_edit_changes_only_its_word_and_empty_rules_follow_the_game()
        {
            foreach (var g in Games())
            {
                var s = GameSession.Open(g);
                var t = CombinationTable.Load(s);
                var before = t.ToBytes();
                Assert.Empty(t.Check());
                if (s.IsUw2)
                {
                    Assert.Equal(0x12B, t.Item(0, 2));
                    Assert.True(t.IsEmpty(5));
                    Assert.Equal(0xFFFF, t.Raw(9, 0));
                }
                else
                {
                    Assert.Equal(0x095, t.Item(0, 0));
                    Assert.True(t.IsEmpty(9));
                    Assert.True(t.Active(0));
                    Assert.False(t.Active(9));
                }
                t.SetItem(0, 2, 0x0A0);
                Assert.True(t.Changed(0, 2));
                Assert.False(t.Changed(0, 0));
                Assert.Equal(new[] { 4 }, Diff(before, t.ToBytes()).Where(i => i != 5).ToArray());
                Assert.All(Diff(before, t.ToBytes()), i => Assert.InRange(i, 4, 5));
                t.Revert();
                // the used-up bit: bit 15 of the source word, the item kept
                int item = t.Item(1, 0);
                t.SetUsedUp(1, 0, !t.UsedUp(1, 0));
                Assert.Equal(item, t.Item(1, 0));
                Assert.Equal(new[] { 7 }, Diff(before, t.ToBytes()));
                t.Revert();
                // a rule made in an empty slot, then cleared again
                int slot = s.IsUw2 ? 5 : 9;
                t.MakeRule(slot, 0x10, 0x11, 0x12);
                Assert.True(t.Active(slot));
                t.Clear(slot);
                Assert.False(t.HasChanges);
                // the same items twice: only the first rule is used
                t.MakeRule(slot, t.Item(0, 1), t.Item(0, 0), 0x12);
                Assert.Contains(t.Check(), m => m.Contains("Rule " + (slot + 1)) && m.Contains("rule 1"));
            }
        }

        [Fact]
        public void Shades_and_minimum_light_edits_change_only_their_bytes()
        {
            foreach (var g in Games())
                using (var c = new EditingTests.Copy(g))
                {
                    var s = GameSession.Open(c.Folder);
                    var t = ShadesTable.Load(s);
                    var before = t.ToBytes();
                    Assert.Equal(56, t.Get(0, 0));
                    Assert.Equal(-3, t.Get(0, 2));
                    t.Set(3, 3, 6);
                    Assert.True(t.IsChanged(3, 3));
                    Assert.False(t.IsChanged(3, 2));
                    Assert.Equal(new[] { 3 * 12 + 3 * 2 }, Diff(before, t.ToBytes()));
                    t.Set(3, 2, -2);
                    Assert.Equal(new[] { 3 * 12 + 2 * 2, 3 * 12 + 2 * 2 + 1, 3 * 12 + 3 * 2 }, Diff(before, t.ToBytes()));
                    Assert.Throws<ArgumentOutOfRangeException>(() => t.Set(0, 3, 16));
                    t.Save();
                    var light = GameLight.Load(s.DataFolder, s.Graphics.Palette);
                    Assert.Equal(6, light.Rows[3].Range);
                    Assert.Equal(-2, light.Rows[3].Lowpass);

                    var dl = MinimumLightTable.Load(s);
                    if (!s.IsUw2) { Assert.False(dl.Exists); continue; }
                    var dlBefore = dl.ToBytes();
                    Assert.Equal(80, dl.Count);
                    Assert.Equal(4, dl.Minimum(1));                         // the castle: 14
                    Assert.True(dl.OnUnflagged(1));
                    Assert.Contains("without the light flag", dl.Explain(1));
                    dl.Set(1, 4, true);
                    Assert.False(dl.HasChanges);
                    dl.Set(6, 2, false);
                    Assert.True(dl.IsChanged(6));
                    Assert.Equal(new[] { 5 }, Diff(dlBefore, dl.ToBytes()));
                    Assert.Equal(2, dl.ToBytes()[5]);
                    dl.Save();
                    Assert.Equal(2, File.ReadAllBytes(dl.Path)[5]);
                }
        }

        [Fact]
        public void A_texture_map_edit_undoes_and_saves_only_that_levels_texture_block()
        {
            foreach (var g in Games())
                using (var c = new EditingTests.Copy(g))
                {
                    var s = GameSession.Open(c.Folder);
                    var edits = new LevelEdits(s);
                    var e = edits.For(1);
                    int old = e.Model.WallMap[3];
                    int to = old == 7 ? 8 : 7;
                    var tex0 = (byte[])e.TextureBytes.Clone();
                    TextureMap.Set(e, TextureMap.Kind.Wall, 3, to);
                    Assert.Equal(to, e.Model.WallMap[3]);
                    Assert.Equal(new[] { 6, 7 }.Where(i => tex0[i] != e.TextureBytes[i]), Diff(tex0, e.TextureBytes));
                    // a door and (UW1) a floor
                    int door = e.Model.DoorMap[2] == 1 ? 2 : 1;
                    TextureMap.Set(e, TextureMap.Kind.Door, 2, door);
                    Assert.Equal(door, e.Model.DoorMap[2]);
                    if (!s.IsUw2)
                    {
                        int f = e.Model.FloorMap[1] == 4 ? 5 : 4;
                        TextureMap.Set(e, TextureMap.Kind.Floor, 1, f);
                        Assert.Equal(f, e.Model.FloorMap[1]);
                        e.Undo();
                    }
                    else
                        Assert.Equal(to, e.Model.FloorMap[3]);                  // slots 0-15 are the floors too
                    e.Undo();
                    Assert.Equal(to, e.Model.WallMap[3]);
                    e.Undo();
                    Assert.Equal(old, e.Model.WallMap[3]);
                    Assert.Equal(tex0, e.TextureBytes);
                    e.Redo();
                    Assert.Equal(to, e.Model.WallMap[3]);
                    Assert.Throws<ArgumentOutOfRangeException>(() => TextureMap.Set(e, TextureMap.Kind.Wall, 3, TextureMap.Choices(s, TextureMap.Kind.Wall)));
                    Assert.Throws<ArgumentOutOfRangeException>(() => TextureMap.Set(e, TextureMap.Kind.Wall, TextureMap.Slots(s, TextureMap.Kind.Wall), 0));

                    var arkBefore = Archive.Open(File.ReadAllBytes(s.LevArkPath), s.Kind);
                    Assert.Equal(new[] { 1 }, edits.Save());
                    var arkAfter = Archive.Open(File.ReadAllBytes(s.LevArkPath), s.Kind);
                    var changed = Enumerable.Range(0, arkBefore.Count).Where(b => arkBefore.Has(b) != arkAfter.Has(b) || (arkBefore.Has(b) && !arkBefore.Read(b).SequenceEqual(arkAfter.Read(b)))).ToList();
                    Assert.Equal(new[] { s.Profile.TextureBlock(1) }, changed);
                    Assert.Equal(to, GameSession.Open(c.Folder).LoadLevel(1).WallMap[3]);
                }
        }

        [Fact]
        public void Uses_counts_the_tiles_of_a_slot()
        {
            foreach (var g in Games())
            {
                var s = GameSession.Open(g);
                var m = s.LoadLevel(1);
                int open = 0;
                for (int y = 0; y < 64; y++) for (int x = 0; x < 64; x++) if (!m.Tiles[x, y].IsSolid) open++;
                int floors = Enumerable.Range(0, s.IsUw2 ? 16 : 10).Sum(i => TextureMap.Uses(m, TextureMap.Kind.Floor, i));
                Assert.InRange(floors, open * 9 / 10, open);
                Assert.True(Enumerable.Range(0, TextureMap.Slots(s, TextureMap.Kind.Wall)).Sum(i => TextureMap.Uses(m, TextureMap.Kind.Wall, i)) > 0);
            }
        }

        [Fact]
        public void Light_flags_are_set_on_every_open_square_in_one_undoable_step()
        {
            string g = GameData.Game("UW2"); if (g == null) return;
            var s = GameSession.Open(g);
            var edits = new LevelEdits(s);
            var e = edits.For(1);
            var before = (byte[])e.Data.Clone();
            int open, n = LightFlags.Count(e.Model, out open);
            Assert.InRange(n, 1, open - 1);                                   // the castle's corridors
            LightFlags.SetAll(e, true);
            Assert.Equal(open, LightFlags.Count(e.Model, out open));
            // only the tiles' light bit (bit 8 of the first tile word: byte 1, bit 0) changed
            Assert.All(Enumerable.Range(0, before.Length).Where(i => before[i] != e.Data[i]), i => { Assert.True(i < 0x4000 && i % 4 == 1); Assert.Equal(1, before[i] ^ e.Data[i]); });
            e.Undo();
            Assert.Equal(before, e.Data);
            LightFlags.SetAll(e, false);
            Assert.Equal(0, LightFlags.Count(e.Model, out open));
        }
    }
}
