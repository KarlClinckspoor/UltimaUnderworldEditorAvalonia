using System;
using System.IO;
using System.Linq;
using UltimateEditor.Games;
using UltimateEditor.Uw1;
using UwEditor.Core.Editing;
using UwEditor.Core.Mods;
using Xunit;
using Xunit.Abstractions;

namespace UwEditor.Core.Tests
{
    public class EditingTests
    {
        readonly ITestOutputHelper output;
        public EditingTests(ITestOutputHelper output) { this.output = output; }

        /// <summary>A copy of a game's DATA (and exe) in a temporary folder.</summary>
        internal sealed class Copy : IDisposable
        {
            public readonly string Folder = Path.Combine(Path.GetTempPath(), "uwedit-core-" + Guid.NewGuid().ToString("N"));
            public Copy(string game, params string[] dirs)
            {
                foreach (var d in new[] { "DATA" }.Concat(dirs))
                {
                    var src = Path.Combine(game, d);
                    if (!Directory.Exists(src)) continue;
                    Directory.CreateDirectory(Path.Combine(Folder, d));
                    foreach (var f in Directory.GetFiles(src)) File.Copy(f, Path.Combine(Folder, d, Path.GetFileName(f)));
                }
                foreach (var f in Directory.GetFiles(game, "*.EXE")) File.Copy(f, Path.Combine(Folder, Path.GetFileName(f)));
            }
            public void Dispose() { try { Directory.Delete(Folder, true); } catch (Exception) { } }
        }

        static int OpenTile(LevelModel m, out int y)
        {
            for (y = 10; y < 60; y++) for (int x = 10; x < 60; x++) if (m.Tiles[x, y].Type == TileType.Open && m.TileObjects(x, y).Count == 0) return x;
            throw new Exception("no open tile");
        }

        [Theory]
        [InlineData("UW")]
        [InlineData("UW2")]
        public void Adding_moving_and_deleting_objects_keeps_the_games_list_check(string game)
        {
            string g = GameData.Game(game); if (g == null) return;
            var s = GameSession.Open(g);
            var e = new LevelEditor(s, 1);
            bool ok;
            e.Check(out ok);
            Assert.True(ok);
            int y, x = OpenTile(e.Model, out y);
            var spec = ObjectSpec.Default(0xA0, s.Kind);      // coins
            spec.Link = 37;
            int coins = e.AddObject(spec, x, y);
            var o = e.Model.Objects[coins];
            Assert.Equal(0xA0, o.Item);
            Assert.Equal(37, o.Link);
            Assert.Equal(x, o.TileX);
            Assert.Equal(e.Model.Tiles[x, y].Height * 8, o.Z);
            output.WriteLine(e.Check(out ok));
            Assert.True(ok);
            // a bag with a sword inside
            int bag = e.AddObject(ObjectSpec.Default(0x80, s.Kind), x, y);
            int sword = e.AddObject(ObjectSpec.Default(0x04, s.Kind), x, y, bag);
            Assert.Equal(bag, e.Model.Objects[sword].Parent);
            // a critter joins the active list
            int active = new LevelBlock(e.Data, s.Kind).ActiveCount;
            int critter = e.AddObject(ObjectSpec.Default(0x44, s.Kind), x, y);
            Assert.True(e.Model.Objects[critter].IsMobile);
            Assert.True(e.Model.Objects[critter].Record[8] > 0);
            Assert.Equal(active + 1, new LevelBlock(e.Data, s.Kind).ActiveCount);
            e.Check(out ok); Assert.True(ok);
            // move the coins next door, delete the bag with its sword, the critter
            int x2 = x + 1, y2 = y;
            while (e.Model.Tiles[x2, y2].IsSolid) x2++;
            e.MoveObject(coins, x2, y2);
            Assert.Equal(x2, e.Model.Objects[coins].TileX);
            e.DeleteObject(bag);
            Assert.True(e.Model.Objects[sword].Free);
            e.DeleteObject(critter);
            Assert.Equal(active, new LevelBlock(e.Data, s.Kind).ActiveCount);
            output.WriteLine(e.Check(out ok));
            Assert.True(ok);
            // undo back to the start, redo once
            while (e.CanUndo) e.Undo();
            Assert.Equal(s.LoadLevel(1).Block.Data, e.Data);
            e.Redo();
            Assert.Equal(0xA0, e.Model.Objects[coins].Item);
        }

        [Fact]
        public void Tiles_paint_light_and_textures_and_ctrl_wheel_raises_the_floor_with_its_objects()
        {
            string g = GameData.Game("UW"); if (g == null) return;
            var s = GameSession.Open(g);
            var e = new LevelEditor(s, 1);
            int y, x = OpenTile(e.Model, out y);
            int coins = e.AddObject(ObjectSpec.Default(0xA0, s.Kind), x, y);
            int z = e.Model.Objects[coins].Z, h = e.Model.Tiles[x, y].Height;
            int light = e.TileField(x, y, "Light");
            e.BeginBatch("paint");
            e.SetLightBit(x, y, (light & 1) == 0);
            e.SetTileField(x, y, "Floor texture", 3);
            e.SetTileField(x, y, "Wall texture", 7);
            e.EndBatch();
            Assert.Equal(light ^ 1, e.TileField(x, y, "Light"));
            Assert.Equal(3, e.Model.Tiles[x, y].FloorIndex);
            Assert.Equal(7, e.Model.Tiles[x, y].WallIndex);
            Assert.True(e.RaiseFloor(x, y, h < 15 ? 1 : -1));
            int d = h < 15 ? 8 : -8;
            Assert.Equal(z + d, e.Model.Objects[coins].Z);
            Assert.Throws<ArgumentOutOfRangeException>(() => e.SetTileField(x, y, "Floor texture", 12));
            // the paint stroke undoes as one
            e.Undo(); e.Undo();
            Assert.Equal(light, e.TileField(x, y, "Light"));
        }

        [Fact]
        public void Saved_levels_are_written_to_lev_ark_and_read_back()
        {
            string g = GameData.Game("UW2"); if (g == null) return;
            using (var c = new Copy(g))
            {
                var s = GameSession.Open(c.Folder);
                var edits = new LevelEdits(s);
                var e = edits.For(1);
                int y, x = OpenTile(e.Model, out y);
                e.SetTileField(x, y, "Floor height", 2);
                var sword = ObjectSpec.Default(0x04, s.Kind);
                sword.Enchanted = true; sword.Link = 0x200 | 0xC0 | 2;
                int i = e.AddObject(sword, x, y);
                Assert.Equal(new[] { 1 }, edits.Save());
                Assert.False(edits.HasChanges);
                Assert.True(File.Exists(Path.Combine(c.Folder, "DATA", "LEV.ARK.ORIG")));
                var back = GameSession.Open(c.Folder).LoadLevel(1);
                Assert.Equal(2, back.Tiles[x, y].Height);
                Assert.True(back.Objects[i].Enchanted);
                Assert.Equal(0x2C2, back.Objects[i].Link);
                Assert.Equal("Great Accuracy", Enchantments.NameOf(0x04, true, true, 0x2C2, false, s.Strings));
                Assert.True(back.CheckOk, back.CheckReport);
            }
        }

        [Theory]
        [InlineData("UW")]
        [InlineData("UW2")]
        public void Enchantment_lists_name_the_weapon_and_armour_effects(string game)
        {
            string g = GameData.Game(game); if (g == null) return;
            var s = GameSession.Open(g);
            var w = Enchantments.WeaponOrArmour(s.Kind, 0x04, s.Strings);
            var a = Enchantments.WeaponOrArmour(s.Kind, 0x20, s.Strings);
            Assert.Equal(16, w.Count);
            Assert.Equal(16, a.Count);
            Assert.All(w, c => Assert.InRange(c.Link, 0x2C0, 0x2CF));
            Assert.Contains("Accuracy", w[0].Name);
            Assert.Contains("Protection", a[0].Name);
            if (s.Kind == GameKind.UW2) Assert.Contains("DAMAGE", w[0].Effect);
            else Assert.Contains("to hit", w[0].Effect);
            Assert.Equal(ItemForm.Enchantable, ObjectSpec.FormOf(0x04, s.Kind));
            Assert.Equal(ItemForm.Basic, ObjectSpec.FormOf(0xA0, s.Kind));
            Assert.Equal(ItemForm.Key, ObjectSpec.FormOf(0x103, s.Kind));
            Assert.Equal(ItemForm.Creature, ObjectSpec.FormOf(0x44, s.Kind));
            Assert.True(Enchantments.Spells(s.Strings).Count > 40);
        }

        [Theory]
        [InlineData("UW")]
        [InlineData("UW2")]
        public void Keys_are_matched_to_their_locks_with_their_descriptions(string game)
        {
            string g = GameData.Game(game); if (g == null) return;
            var s = GameSession.Open(g);
            var groups = KeyIndex.Build(s, s.LoadLevel);
            int keys = groups.Sum(k => k.Keys.Count), locks = groups.Sum(k => k.Locks.Count);
            output.WriteLine("{0}: {1} lock numbers, {2} keys, {3} locks, {4} with both", game, groups.Count, keys, locks, groups.Count(k => k.Keys.Count > 0 && k.Locks.Count > 0));
            Assert.True(keys > 10);
            Assert.True(locks > 10);
            Assert.True(groups.Count(k => k.Keys.Count > 0 && k.Locks.Count > 0) > 5);
            Assert.All(groups.SelectMany(k => k.Locks), l => Assert.True(l.Holder >= 0));   // a lock lives in a door or chest
            Assert.Contains(groups, k => !string.IsNullOrWhiteSpace(k.Text));
        }

        [Fact]
        public void A_mod_made_from_edits_applies_to_a_clean_copy_and_refuses_a_changed_one()
        {
            string g = GameData.Game("UW"); if (g == null) return;
            using (var edited = new Copy(g))
            using (var clean = new Copy(g))
            using (var other = new Copy(g))
            {
                // edit a level and a string in one copy
                var s = GameSession.Open(edited.Folder);
                var edits = new LevelEdits(s);
                var e = edits.For(2);
                int y, x = OpenTile(e.Model, out y);
                e.AddObject(ObjectSpec.Default(0xA0, s.Kind), x, y);
                edits.Save();
                var pak = Conversations.StringsPakFile.Load(Path.Combine(edited.Folder, "DATA", "STRINGS.PAK"));
                var b5 = pak.Get(5); b5[0x64 + 3] = "This key smells of mod."; pak.Set(5, b5); pak.Save();
                var mod = ModPackage.FromChanges(edited.Folder, s.Kind);
                mod.Id = "test.mod"; mod.Name = "Test"; mod.Authors.Add("Tests");
                Assert.Equal(2, mod.Components.Count);
                var ark = mod.Components.Single(c => c.Kind == "ark-blocks");
                Assert.Equal(new[] { 1 }, ark.Blocks.Select(b => b.Index));
                Assert.Single(mod.Components.Single(c => c.Kind == "strings").Strings);
                string file = Path.Combine(edited.Folder, "test.uwmod");
                mod.Write(file);
                var read = ModPackage.Read(file);
                Assert.Equal(mod.BaseFiles, read.BaseFiles);
                // a clean copy takes it, and then reads as applied
                Assert.All(read.Check(clean.Folder, s.Kind), c => Assert.Equal(ComponentState.Applicable, c.State));
                Assert.Equal(2, read.Apply(clean.Folder, s.Kind).Count);
                Assert.All(read.Check(clean.Folder, s.Kind), c => Assert.Equal(ComponentState.AlreadyApplied, c.State));
                Assert.Equal(File.ReadAllBytes(Path.Combine(edited.Folder, "DATA", "LEV.ARK")), File.ReadAllBytes(Path.Combine(clean.Folder, "DATA", "LEV.ARK")));
                Assert.Equal("This key smells of mod.", GameSession.Open(clean.Folder).Strings.Get(5, 0x64 + 3));
                // a copy whose level 2 changed otherwise refuses it
                var s3 = GameSession.Open(other.Folder);
                var ed3 = new LevelEdits(s3);
                var e3 = ed3.For(2);
                e3.SetTileField(x, y, "Floor height", (e3.Model.Tiles[x, y].Height + 1) % 16);
                ed3.Save();
                var checks = read.Check(other.Folder, s.Kind);
                Assert.Contains(checks, c => c.State == ComponentState.Conflict && c.Component.Kind == "ark-blocks");
                Assert.Throws<InvalidOperationException>(() => read.Apply(other.Folder, s.Kind));
            }
        }
    
        [Fact]
        public void Trigger_chains_targets_and_help_on_the_shipped_levels()
        {
            foreach (var g in new[] { "UW", "UW2" })
            {
                string path = GameData.Game(g);
                if (path == null) continue;
                var s = GameSession.Open(path);
                var m = s.LoadLevel(1);
                var chains = Triggers.Chains(m);
                Assert.NotEmpty(chains);
                int withTraps = chains.Count(c => c.Steps.Count > 0);
                output.WriteLine("{0} level 1: {1} triggers, {2} with traps, {3} unreached traps", g, chains.Count, withTraps, Triggers.Unreached(m, chains).Count);
                Assert.True(withTraps > chains.Count / 2);
                foreach (var c in chains)
                {
                    var t = Triggers.Targets(m, c.Trigger);
                    Assert.Contains(t, x => x.X == c.TargetX && x.Y == c.TargetY && x.Editable);
                    Assert.False(string.IsNullOrWhiteSpace(Triggers.Explain(s, c.Trigger)));
                    foreach (var step in c.Steps.Where(x => Triggers.IsTrap(x.Item)))
                    {
                        Assert.Contains(Triggers.TriggersReaching(m, step), x => x.Index == c.Trigger.Index);
                        Assert.False(string.IsNullOrWhiteSpace(Triggers.Explain(s, step)));
                    }
                }
                var tele = m.Objects.FirstOrDefault(o => !o.Free && o.TileX >= 0 && o.Item == 0x181);
                if (tele != null) Assert.Contains(Triggers.Targets(m, tele), x => x.What.Contains("teleports"));
            }
        }

        [Fact]
        public void Races_are_named_by_their_creatures_and_strings_edit_through_the_shared_file()
        {
            string path = GameData.Game("UW");
            if (path == null) return;
            var s = GameSession.Open(path);
            var races = Races.Of(s);
            Assert.Equal(0, races[0].Value);
            Assert.Contains(races, r => r.Label.Contains("goblin"));
            Assert.True(Races.CanBeOwned(s, 0x005));      // a longsword
            Assert.False(Races.CanBeOwned(s, 0x180));     // a trap
            using (var c = new Copy(path))
            {
                var s2 = GameSession.Open(c.Folder);
                var st = StringsEditor.Open(s2);
                Assert.Contains(st.Blocks(), b => b.Number == 4 && b.Description == "Item names");
                st.Set(4, 0x153, "a_huge rock&huge rocks");
                Assert.True(st.IsChanged(4, 0x153));
                Assert.False(st.IsChanged(4, 0x152));
                Assert.Throws<InvalidDataException>(() => st.Set(4, 0x152, "bad|pipe"));
                st.Save();
                Assert.Equal("huge rock", s2.Names.Get(0x153));
                Assert.Contains(st.Search("huge rock"), h => h.Block == 4 && h.Index == 0x153);
            }
        }
    }
}
