using System;
using System.IO;
using System.Linq;
using UltimateEditor.Games;
using UwEditor.Core.Editing;
using UwEditor.Core.Randomizer;
using Xunit;

namespace UwEditor.Core.Tests
{
    /// <summary>OBJECTS.DAT and COMOBJ.DAT edited as tables (ObjectTables).</summary>
    public class ObjectTablesTests
    {
        [Fact]
        public void The_layout_fills_both_files_exactly()
        {
            // obj/OBJCLASS.C init_objects: 2-byte header, the class tables back to back, nothing after
            Assert.Equal(3554, ObjectTables.SizeOf(ObjectTables.ObjectsFile));
            Assert.Equal(2 + 512 * 11, ObjectTables.SizeOf(ObjectTables.ComObjFile));
            foreach (var file in new[] { ObjectTables.ObjectsFile, ObjectTables.ComObjFile })
            {
                var ts = ObjectTables.Layout.Where(t => t.FileName == file).OrderBy(t => t.Start).ToList();
                Assert.Equal(2, ts[0].Start);
                for (int i = 1; i < ts.Count; i++) Assert.Equal(ts[i - 1].End, ts[i].Start);
            }
            Assert.Equal(0x132, ObjectTables.Table("Creatures").Start);
        }

        [Theory]
        [InlineData("UW")]
        [InlineData("UW2")]
        public void Unchanged_tables_save_nothing_and_match_the_files_and_the_old_codec(string game)
        {
            string g = GameData.Game(game); if (g == null) return;
            using (var c = new EditingTests.Copy(g))
            {
                var s = GameSession.Open(c.Folder);
                var t = ObjectTables.Load(s);
                byte[] obj = File.ReadAllBytes(t.ObjectsPath), com = File.ReadAllBytes(t.ComObjPath);
                Assert.Equal(obj, t.ObjectsBytes);
                Assert.Equal(com, t.ComObjBytes);
                Assert.False(t.HasChanges);

                // an edit and its undoing by hand leave nothing to save
                int hp = t.Get("Creatures", 3, "Average HP");
                Assert.Null(t.Set("Creatures", 3, "Average HP", (hp + 1) & 0xFF));
                Assert.True(t.HasChanges);
                Assert.Null(t.Set("Creatures", 3, "Average HP", hp));
                Assert.False(t.HasChanges);
                Assert.Empty(t.Save());
                Assert.Equal(obj, File.ReadAllBytes(t.ObjectsPath));
                Assert.Equal(com, File.ReadAllBytes(t.ComObjPath));
                Assert.False(File.Exists(t.ObjectsPath + ".ORIG"));

                // the fields agree with the editor's old reader (ObjectDataCodec)
                var od = new ObjectData();
                ObjectDataCodec.ReadObjectsDat(od, t.ObjectsPath);
                ObjectDataCodec.LoadCommonData(od, t.ComObjPath);
                for (int i = 0; i < 16; i++)
                {
                    Assert.Equal(od.WeaponData[i].Slash, t.Get("Weapons", i, "Slash damage"));
                    Assert.Equal(od.WeaponData[i].Skill, t.Get("Weapons", i, "Skill"));
                    Assert.Equal(od.ContainerData[i].Capacity, t.Get("Containers", i, "Capacity"));
                }
                for (int i = 0; i < 8; i++)
                {
                    Assert.Equal(od.ProjectileData[i].Damage, t.Get("Ranged", i, "Damage"));
                    Assert.Equal(od.RangedData[i].Ammo, t.Get("Ranged", 8 + i, "Ammo / damage type") & 0xFF);
                    Assert.Equal(od.LightData[i].Brightness, t.Get("Lights", i, "Brightness"));
                }
                for (int i = 0; i < 32; i++) Assert.Equal(od.ArmourData[i].Protection, t.Get("Armour", i, "Protection"));
                for (int i = 0; i < 64; i++)
                {
                    Assert.Equal(od.MonsterData[i].Health & 0xFF, t.Get("Creatures", i, "Average HP"));
                    Assert.Equal((short)od.MonsterData[i].Experience, t.Get("Creatures", i, "Experience"));
                }
                for (int i = 0; i < 512; i++)
                {
                    Assert.Equal(od.CommonData[i].Mass, t.Get("Common properties", i, "Mass"));
                    Assert.Equal(od.CommonData[i].Value, t.Get("Common properties", i, "Value"));
                    Assert.Equal(od.CommonData[i].Ownable, t.Get("Common properties", i, "Can be owned"));
                }
                Assert.Equal(od.UnknownData.Skip(16).Take(16).Select(b => b).ToArray(), Enumerable.Range(0, 16).Select(i => t.Get("Food", i, "Nutrition")).ToArray());
            }
        }

        [Theory]
        [InlineData("UW")]
        [InlineData("UW2")]
        public void An_edit_changes_only_its_bytes_and_readers_see_it_after_saving(string game)
        {
            string g = GameData.Game(game); if (g == null) return;
            using (var c = new EditingTests.Copy(g))
            {
                var s = GameSession.Open(c.Folder);
                var t = ObjectTables.Load(s);
                byte[] obj = File.ReadAllBytes(t.ObjectsPath), com = File.ReadAllBytes(t.ComObjPath);
                var cr = ObjectTables.Table("Creatures");
                int row = 5;
                int hp = t.Get(cr, row, cr.Schema["Average HP"]);
                int newHp = hp == 200 ? 201 : 200;
                Assert.Null(t.Set(cr, row, cr.Schema["Average HP"], newHp));
                Assert.Null(t.SetText(cr, row, cr.Schema["Experience"], "0x123"));
                Assert.True(t.IsChanged(cr, row, cr.Schema["Average HP"]));
                Assert.False(t.IsChanged(cr, row, cr.Schema["Strength"]));
                Assert.True(t.IsRowChanged(cr, row));
                Assert.False(t.IsRowChanged(cr, row + 1));

                // a bit in COMOBJ.DAT: the can-own flag of item 0x00, and its render type
                var cm = ObjectTables.Table("Common properties");
                bool own = Races.CanBeOwned(s, 0x00);      // fills Races' cache
                Assert.Null(t.Set(cm, 0, cm.Schema["Can be owned"], own ? 0 : 1));
                int render = s.RenderTypes[0x01];
                Assert.Null(t.Set(cm, 1, cm.Schema["Render"], render == 0 ? 2 : 0));
                Assert.Equal(3, t.ChangedRows);         // creature 5 and items 0 and 1

                Assert.Equal(new[] { ObjectTables.ObjectsFile, ObjectTables.ComObjFile }, t.Save());
                Assert.False(t.HasChanges);
                byte[] obj2 = File.ReadAllBytes(t.ObjectsPath), com2 = File.ReadAllBytes(t.ComObjPath);
                int at = 0x132 + 0x30 * row;
                var diff = Enumerable.Range(0, obj.Length).Where(i => obj[i] != obj2[i]).ToList();
                Assert.Equal(obj.Length, obj2.Length);
                Assert.All(diff, i => Assert.Contains(i, new[] { at + 4, at + 0x28, at + 0x29 }));
                Assert.Equal(newHp, obj2[at + 4]);
                Assert.Equal(0x123, obj2[at + 0x28] | obj2[at + 0x29] << 8);
                var cdiff = Enumerable.Range(0, com.Length).Where(i => com[i] != com2[i]).ToList();
                Assert.All(cdiff, i => Assert.Contains(i, new[] { 2 + 7, 2 + 11 + 9 }));
                Assert.Equal(com[2 + 7] ^ 0x80, com2[2 + 7]);
                Assert.True(File.Exists(t.ObjectsPath + ".ORIG"));
                Assert.Equal(obj, File.ReadAllBytes(t.ObjectsPath + ".ORIG"));

                // what reads the files sees the change: from disk, and the session's caches
                Assert.Equal(newHp, CreatureKind.Read(s)[0x40 + row].AvgHit);
                Assert.Equal(!own, Races.CanBeOwned(s, 0x00));
                Assert.Equal(render == 0 ? 2 : 0, s.RenderTypes[0x01]);
                var again = ObjectTables.Load(GameSession.Open(c.Folder));
                Assert.Equal(newHp, again.Get(cr, row, cr.Schema["Average HP"]));
                Assert.Equal(0x123, again.Get(cr, row, cr.Schema["Experience"]));
            }
        }

        [Fact]
        public void Values_out_of_range_are_refused_and_reverts_restore_the_files_values()
        {
            string g = GameData.Game("UW"); if (g == null) return;
            var s = GameSession.Open(g);
            var t = ObjectTables.Load(s);
            var w = ObjectTables.Table("Weapons");
            int slash = t.Get(w, 0, w.Schema["Slash damage"]);
            Assert.NotNull(t.Set(w, 0, w.Schema["Slash damage"], 256));
            Assert.NotNull(t.Set(w, 0, w.Schema["Slash damage"], -1));
            Assert.NotNull(t.SetText(w, 0, w.Schema["Slash damage"], "lots"));
            Assert.False(t.HasChanges);
            var cr = ObjectTables.Table("Creatures");
            Assert.NotNull(t.Set(cr, 0, cr.Schema["Defence"], 128));        // signed byte
            Assert.Null(t.Set(cr, 0, cr.Schema["Defence"], -5));
            Assert.Equal(-5, t.Get(cr, 0, cr.Schema["Defence"]));
            var ct = ObjectTables.Table("Containers");
            Assert.NotNull(t.Set(ct, 0, ct.Schema["Accepts"], 0x300));
            Assert.NotNull(t.Set(ct, 0, ct.Schema["Accepts"], -2));
            Assert.Null(t.SetText(ct, 0, ct.Schema["Accepts"], "0x203"));
            Assert.Equal("food", t.Meaning(ct, ct.Schema["Accepts"], 0x203));
            Assert.NotNull(t.Warning(w, w.Schema["Skill"], 9));
            Assert.Equal("sword", t.Meaning(w, w.Schema["Skill"], 3));

            Assert.Null(t.Set(w, 0, w.Schema["Slash damage"], slash == 9 ? 8 : 9));
            t.RevertRow(w, 0);
            Assert.Equal(slash, t.Get(w, 0, w.Schema["Slash damage"]));
            Assert.True(t.HasChanges);       // the creature and the container
            t.RevertAll();
            Assert.False(t.HasChanges);
            Assert.Equal(File.ReadAllBytes(t.ObjectsPath), t.ObjectsBytes);

            int v;
            Assert.True(ObjectTables.TryParse(" 0x1F ", out v)); Assert.Equal(31, v);
            Assert.True(ObjectTables.TryParse("-12", out v)); Assert.Equal(-12, v);
            Assert.False(ObjectTables.TryParse("", out v));
            Assert.False(ObjectTables.TryParse("0x", out v));
            Assert.Contains("COMOBJ.DAT", ObjectTables.Explain(ObjectTables.Table("Common properties"), Uw1Schema("Mass")));
        }

        static UltimateEditor.Uw1.FieldDef Uw1Schema(string name) { return UltimateEditor.Uw1.Uw1Schemas.ComObj[name]; }
    }
}
