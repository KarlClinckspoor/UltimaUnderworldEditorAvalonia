using System;
using System.Collections.Generic;
using System.Linq;
using UltimateEditor.Games;
using UwEditor.Core.Editing;
using UwEditor.Core.Randomizer;
using Xunit;
using Xunit.Abstractions;
using MapNote = UwEditor.Core.Editing.MapNote;

namespace UwEditor.Core.Tests
{
    /// <summary>
    /// Place anywhere (the key item scatter and the item property shuffle put floor items on random
    /// squares) and the scatter's spoiler log in the map notes (UW1 pages 9-11 and on).
    /// </summary>
    public class PlaceAnywhereTests
    {
        readonly ITestOutputHelper output;
        public PlaceAnywhereTests(ITestOutputHelper output) { this.output = output; }

        static readonly object gate = new object();
        static GameSession uw1;
        static KeyShuffleResult scatter;

        static GameSession Uw1()
        {
            lock (gate)
            {
                if (uw1 != null) return uw1;
                string g = GameData.Game("UW");
                return g == null ? null : (uw1 = GameSession.Open(g));
            }
        }

        static KeyShuffleResult Scatter()
        {
            lock (gate) { return scatter ?? (scatter = KeyShuffle.Run(Uw1(), null, new KeyShuffleOptions { Seed = 11, Filler = true })); }
        }

        static void AssertLiesWell(GameSession s, LevelModel m, ObjectInfo o, TerrainTable terrain)
        {
            Assert.True(o.OnTile, "#" + o.Index + " is on the floor");
            Assert.True(Anywhere.Qualifies(s, m, terrain, o.TileX, o.TileY), "#" + o.Index + " on " + o.TileX + ", " + o.TileY);
            Assert.True(Anywhere.InOpenPart(m.Tiles[o.TileX, o.TileY].Type, o.FineX, o.FineY), "#" + o.Index + " inside the open half");
            var e = new LevelEditor(s, m.Level, (byte[])m.Block.Data.Clone(), m.TextureBytes);
            Assert.Equal(e.FloorZ(o.TileX, o.TileY, o.FineX, o.FineY), o.Z);
        }

        [Fact]
        public void Squares_keep_off_water_lava_doors_and_triggers_and_fine_positions_stay_in_the_open_half()
        {
            var s = Uw1(); if (s == null) return;
            var terrain = CreatureTerrain.TryLoad(s);
            var m = s.LoadLevel(1);
            var squares = Anywhere.Squares(s, m, terrain, null);
            Assert.True(squares.Count > 500);
            foreach (var (x, y) in squares)
            {
                Assert.False(m.Tiles[x, y].IsSolid);
                Assert.False(CreatureTerrain.Restricted(CreatureTerrain.FloorClass(s, terrain, m, x, y)));
                Assert.DoesNotContain(m.TileObjects(x, y), o => (o.Item >= 0x140 && o.Item <= 0x14F) || o.IsTrapOrTrigger);
            }
            // water on level 1 exists and is left out
            Assert.Contains(Enumerable.Range(0, 64 * 64), k => !m.Tiles[k % 64, k / 64].IsSolid && CreatureTerrain.FloorClass(s, terrain, m, k % 64, k / 64) == CreatureTerrain.Water);
            var rng = new Random(3);
            foreach (var type in new[] { TileType.OpenSE, TileType.OpenSW, TileType.OpenNE, TileType.OpenNW })
                for (int i = 0; i < 200; i++)
                {
                    var (fx, fy) = Anywhere.FinePosition(rng, type);
                    Assert.True(Anywhere.InOpenPart(type, fx, fy));
                }
            Assert.False(Anywhere.InOpenPart(TileType.OpenSE, 1, 6));     // the north-west corner of a south-east half
            Assert.True(Anywhere.InOpenPart(TileType.OpenSE, 6, 1));
        }

        [Fact]
        public void The_scatter_puts_floor_places_on_good_squares_the_logic_reaches_and_the_game_stays_beatable()
        {
            var s = Uw1(); if (s == null) return;
            var r = Scatter();
            Assert.True(r.Success, string.Join("; ", r.Failures));
            output.WriteLine("{0} places relocated; {1}", r.Relocated, string.Join("; ", r.AnywhereNotes));
            Assert.True(r.Relocated > 100);
            var edits = new LevelEdits(s);
            KeyShuffle.Apply(r, edits);
            var terrain = CreatureTerrain.TryLoad(s);
            int onSlopeOrDiagonal = 0;
            foreach (var mv in r.Moves.Where(m => m.Place.Relocated))
            {
                var m = edits.Model(mv.Place.Level);
                var o = m.Objects[mv.Place.Index];
                Assert.Equal(mv.Place.X, o.TileX); Assert.Equal(mv.Place.Y, o.TileY);
                Assert.Equal(mv.Item.Item, o.Item);
                AssertLiesWell(s, m, o, terrain);
                if (m.Tiles[o.TileX, o.TileY].Type != TileType.Open) onSlopeOrDiagonal++;
            }
            output.WriteLine("{0} on slopes or diagonals", onSlopeOrDiagonal);
            // the log says where each item was before
            Assert.Contains("Place anywhere: on;", r.Log.ToString());
            Assert.Contains("a random square", r.Log.ToString());
            // a fresh solve of the levels as applied
            var t = Timeline.Build(s, edits.Model, r.Options.Nav, r.Options.Logic, null, null, false);
            Assert.True(t.Solve.GoalReached, string.Join(", ", t.Solve.GoalMissing));
            foreach (int l in r.After.Keys) { bool ok; edits.For(l).Check(out ok); Assert.True(ok, "level " + l + " fails the list check"); }
            // every object is still there, once
            foreach (int l in r.After.Keys)
                Assert.Equal(s.LoadLevel(l).Objects.Count(o => !o.Free && o.TileX >= 0), edits.Model(l).Objects.Count(o => !o.Free && o.TileX >= 0));
            // Undo: byte for byte
            KeyShuffle.Undo(r, edits);
            foreach (int l in r.Before.Keys) Assert.True(edits.For(l).Data.AsSpan().SequenceEqual(r.Before[l]), "level " + l);
        }

        [Fact]
        public void Off_the_scatter_trades_the_places_where_items_lay()
        {
            var s = Uw1(); if (s == null) return;
            var r = KeyShuffle.Run(s, null, new KeyShuffleOptions { Seed = 11, Anywhere = false });
            Assert.True(r.Success);
            Assert.Equal(0, r.Relocated);
            Assert.DoesNotContain(r.Moves, m => m.Place.Relocated);
            Assert.Contains("Place anywhere: off", r.Log.ToString());
        }

        [Fact]
        public void The_spoiler_log_goes_on_map_pages_9_10_and_11_and_leaves_the_levels_notes_alone()
        {
            var s = Uw1(); if (s == null) return;
            var r = Scatter();
            Assert.True(r.Success);
            var widths = SpoilerNotes.FontWidths(s);
            Assert.Equal(2, widths[' ']);       // FONT4X5P.SYS, the notes' font
            Assert.Equal(4, widths['A']);
            var edits = new LevelEdits(s);
            var level1Notes = Automap.WriteNotes(edits.AutomapFor(1).Notes);
            var level9Own = edits.AutomapFor(9).Notes.Select(n => n.Text).ToList();
            r.Options.SpoilerNotes = true;
            KeyShuffle.Apply(r, edits);
            Assert.Contains(r.AppliedAids, a => a.Contains("spoiler log"));
            var nine = edits.AutomapFor(9).Notes;
            Assert.Equal(level9Own, nine.Take(level9Own.Count).Select(n => n.Text).ToList());
            Assert.Contains(nine, n => n.Text.StartsWith("SPOILERS AHEAD"));
            var hints = edits.AutomapFor(10);
            var answers = edits.AutomapFor(11);
            Assert.True(hints.NotesOnly && answers.NotesOnly);
            Assert.StartsWith("HINTS 1/", hints.Notes[0].Text);
            Assert.StartsWith("ANSWERS 1/", answers.Notes[0].Text);
            foreach (var page in new[] { 9, 10, 11, 12, 13 }.Where(p => edits.Automaps.Any(a => a.Level == p)))
            {
                var notes = edits.AutomapFor(page).Notes;
                Assert.True(notes.Count <= SpoilerNotes.LinesPerPage + (page == 9 ? level9Own.Count : 0));
                foreach (var n in notes.Where(n => page != 9 || !level9Own.Contains(n.Text)))
                {
                    Assert.True(n.Text.Length <= Automap.TextSize - 1, n.Text);
                    Assert.True(SpoilerNotes.Width(n.Text, widths) <= 246, n.Text);
                    Assert.InRange(n.Y, 12, 186);
                    // capitals: the notes' font ends at 0x60, and a lowercase letter hangs the game (Automap.NoteChars)
                    Assert.True(n.Text.All(c => c >= 32 && c <= Automap.LastNoteChar), n.Text);
                }
                // the lines do not overlap: 7 pixels apart
                var ys = notes.Where(n => n.X == 12).Select(n => n.Y).ToList();
                Assert.Equal(ys.Count, ys.Distinct().Count());
            }
            // every key item has a hint and an answer (wrapped lines aside)
            int keyItems = r.Moves.Count(m => m.ByFill);
            var allHints = edits.Automaps.Where(a => a.Level >= 10 && a.Level % 2 == 0).SelectMany(a => a.Notes).Count(n => !n.Text.StartsWith("  ") && !n.Text.StartsWith("HINTS"));
            var allAnswers = edits.Automaps.Where(a => a.Level >= 11 && a.Level % 2 == 1).SelectMany(a => a.Notes).Count(n => !n.Text.StartsWith("  ") && !n.Text.StartsWith("ANSWERS"));
            Assert.Equal(keyItems, allHints);
            Assert.Equal(keyItems, allAnswers);
            output.WriteLine(string.Join("\n", hints.Notes.Take(8).Select(n => n.Text)));
            output.WriteLine(string.Join("\n", answers.Notes.Take(8).Select(n => n.Text)));
            // saved: page 10's notes in block 0x2D, level 1's notes (block 0x24, what 0x1A + 10 would be) untouched
            var ark = Archive.Open(edits.ToBytes(), s.Kind);
            Assert.Equal(level1Notes, ark.Read(Automap.NotesBlock(s.Kind, 1)) ?? new byte[0]);
            Assert.Equal(hints.Notes.Select(n => n.Text), Automap.ReadNotes(ark.Read(Automap.NotesBlock(s.Kind, 10))).Select(n => n.Text));
            // Undo puts the pages back
            KeyShuffle.Undo(r, edits);
            Assert.Empty(edits.AutomapFor(10).Notes);
            Assert.Equal(level9Own, edits.AutomapFor(9).Notes.Select(n => n.Text).ToList());
        }

        [Fact]
        public void Hints_use_zone_names_or_the_part_of_the_level()
        {
            var s = Uw1(); if (s == null) return;
            var m = s.LoadLevel(1);
            Assert.Equal("center", SpoilerNotes.PartOfLevel(m, 32, 32));
            Assert.Equal("south", SpoilerNotes.PartOfLevel(m, 32, 2));
            var r = Scatter();
            var hints = SpoilerNotes.Hints(s, r, s.LoadLevel);
            Assert.All(hints, h => Assert.Matches(@"^.+: level \d, (north|south|center|east|west|northeast|northwest|southeast|southwest)(, hidden)?$", h));
            Assert.Equal("Cafe", SpoilerNotes.Ascii("Café"));
        }

        [Fact]
        public void Notes_are_capitals_the_map_screen_can_draw()
        {
            // the note that hung the game on page 9 (DOSBox and Underworld Exhumed): its lowercase letters
            // have no glyphs in FONT4X5P.SYS, and 'e', 'l' and 's' get a width of 0 (Automap.NoteChars)
            Assert.Equal("NOTES ON LVL9", Automap.Clean("Notes on lvl9"));
            Assert.Equal("CAFE (A?B)? '-'", Automap.NoteChars("Café {a|b}€ ‘–’"));
            Assert.All(Automap.NoteChars("abc xyz {}|~ äöü ß €  `"), c => Assert.InRange(c, ' ', Automap.LastNoteChar));
            var s = Uw1(); if (s == null) return;
            var r = Scatter();
            foreach (var line in SpoilerNotes.Hints(s, r, s.LoadLevel).Concat(SpoilerNotes.Warning(r)))
                foreach (var part in SpoilerNotes.Wrap(line, SpoilerNotes.FontWidths(s)))
                    Assert.Equal(Automap.Clean(part), part);
        }

        [Fact]
        public void Saving_writes_again_a_page_whose_notes_have_lowercase_even_unchanged()
        {
            var s = Uw1(); if (s == null) return;
            // a page 9 note in lowercase, as the editor 0.1.8 wrote it
            var first = new LevelEdits(s);
            first.AutomapFor(9).Replace(new byte[64 * 64], new List<MapNote> { new MapNote { Text = "Notes on lvl9", X = 197, Y = 168 } }, "test");
            var ark = Archive.Open(first.ToBytes(), s.Kind);
            Assert.Equal("NOTES ON LVL9", Automap.ReadNotes(ark.Read(Automap.NotesBlock(s.Kind, 9)))[0].Text);
            // an archive holding lowercase (written by hand here) is cleaned by a later save that only opened the page
            var bytes = new byte[Automap.NoteSize];
            System.Text.Encoding.ASCII.GetBytes("Notes on lvl9").CopyTo(bytes, 0);
            bytes[0x32] = 197; bytes[0x34] = 168;
            ark.Write(Automap.NotesBlock(s.Kind, 9), bytes);
            var s2 = GameSession.Open(GameData.Game("UW"));
            s2.ReplaceLevArk(ark.ToBytes());
            var second = new LevelEdits(s2);
            var page = second.AutomapFor(9);
            Assert.Equal("Notes on lvl9", page.Notes[0].Text);
            Assert.True(page.UnsafeNotes);
            Assert.False(second.HasChanges);
            var saved = Archive.Open(second.ToBytes(), s.Kind);
            Assert.Equal("NOTES ON LVL9", Automap.ReadNotes(saved.Read(Automap.NotesBlock(s.Kind, 9)))[0].Text);
        }

        [Fact]
        public void A_notes_page_past_level_9_reads_and_writes_only_its_notes()
        {
            var s = Uw1(); if (s == null) return;
            var edits = new LevelEdits(s);
            var page = edits.AutomapFor(12);
            Assert.True(page.NotesOnly);
            Assert.All(page.Map, b => Assert.Equal(0, b));
            Assert.False(edits.AutomapFor(9).NotesOnly);
            var level3Notes = s.LevArk.Has(Automap.NotesBlock(s.Kind, 3)) ? s.LevArk.Read(Automap.NotesBlock(s.Kind, 3)) : new byte[0];
            page.Replace(page.Map, new List<MapNote> { new MapNote { Text = "hello", X = 12, Y = 100 } }, "test");
            var ark = Archive.Open(edits.ToBytes(), s.Kind);
            Assert.Equal("HELLO", Automap.ReadNotes(ark.Read(Automap.NotesBlock(s.Kind, 12)))[0].Text);   // in capitals (Automap.NoteChars)
            // 0x1A + 12 is level 3's notes block: not written
            Assert.Equal(level3Notes, ark.Has(Automap.NotesBlock(s.Kind, 3)) ? ark.Read(Automap.NotesBlock(s.Kind, 3)) : new byte[0]);
            Assert.True(Automap.IsNotesPage(GameKind.UW1, 99));
            Assert.False(Automap.IsNotesPage(GameKind.UW1, 100));
            Assert.False(Automap.IsNotesPage(GameKind.UW2, 12));
        }

        [Theory]
        [InlineData("UW")]
        [InlineData("UW2")]
        public void The_property_shuffle_moves_rolled_floor_items_and_revert_puts_every_byte_back(string game)
        {
            string g = GameData.Game(game); if (g == null) return;
            var s = GameSession.Open(g);
            var edits = new LevelEdits(s);
            var o = ShuffleOptions.Defaults(s, edits.Model);
            o.Seed = 4;
            Assert.True(o.PlaceAnywhere);
            var r = ItemShuffle.Run(s, edits.Model, o);
            output.WriteLine("{0}: {1} changed, {2} moved", game, r.Changed, r.Moved);
            Assert.True(r.Moved > 50);
            // the rolls do not depend on Place anywhere
            var off = ItemShuffle.Run(s, edits.Model, new Func<ShuffleOptions>(() => { var x = ShuffleOptions.FromJson(o.ToJson()); x.PlaceAnywhere = false; return x; })());
            Assert.Equal(0, off.Moved);
            Assert.Equal(r.Changed, off.Changed);
            var before = r.Levels.ToDictionary(l => l, l => (byte[])edits.For(l).Data.Clone());
            ItemShuffle.Apply(r, edits, o.Seed);
            var terrain = CreatureTerrain.TryLoad(s);
            foreach (var kv in r.Moves)
            {
                var m = edits.Model(kv.Key);
                foreach (var mv in kv.Value)
                {
                    var x = m.Objects[mv.Index];
                    Assert.Equal(mv.To.X, x.TileX); Assert.Equal(mv.To.Y, x.TileY);
                    AssertLiesWell(s, m, x, terrain);
                }
                bool ok; edits.For(kv.Key).Check(out ok); Assert.True(ok, "level " + kv.Key);
            }
            // one undo step a level
            var one = r.Moves.Keys.First();
            edits.For(one).Undo();
            Assert.True(edits.For(one).Data.AsSpan().SequenceEqual(before[one]));
            edits.For(one).Redo();
            Assert.Empty(ItemShuffle.Revert(r, edits));
            foreach (var kv in before) Assert.True(edits.For(kv.Key).Data.AsSpan().SequenceEqual(kv.Value), "level " + kv.Key);
        }

        [Fact]
        public void The_swap_never_moves_items_to_new_squares()
        {
            var s = Uw1(); if (s == null) return;
            var edits = new LevelEdits(s);
            var o = ShuffleOptions.Defaults(s, edits.Model);
            o.Mode = "swap-kind";
            Assert.True(o.PlaceAnywhere);
            var r = ItemShuffle.Run(s, edits.Model, o);
            Assert.Equal(0, r.Moved);
        }
    }
}
