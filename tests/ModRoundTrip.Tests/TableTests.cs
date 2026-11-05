using UltimateEditor.Games;
using UwEditor.Core;
using UwEditor.Core.Editing;
using Xunit;

namespace ModRoundTrip.Tests
{
    /// <summary>The DATA tables: SHADES.DAT, OBJECTS.DAT and COMOBJ.DAT, SKILLS.DAT, TERRAIN.DAT, CMB.DAT, DL.DAT, and UW2's schedules.</summary>
    public class TableTests
    {
        [Theory]
        [InlineData("UW")]
        [InlineData("UW2")]
        public void Shades(string game)
        {
            using var rt = RoundTrip.Start(game);
            if (rt == null) return;
            var s = GameSession.Open(rt.Edited);
            var t = ShadesTable.Load(s);
            t.Set(3, 3, 6);
            t.Set(0, 1, 2);
            t.Save();
            rt.Check("tables.shades", "DATA/SHADES.DAT");
        }

        [Theory]
        [InlineData("UW")]
        [InlineData("UW2")]
        public void Objects_and_common_properties(string game)
        {
            using var rt = RoundTrip.Start(game);
            if (rt == null) return;
            var s = GameSession.Open(rt.Edited);
            var t = ObjectTables.Load(s);
            var cr = ObjectTables.Table("Creatures");
            Assert.Null(t.Set(cr, 5, cr.Schema["Average HP"], 200));
            var cm = ObjectTables.Table("Common properties");
            int now = t.Get(cm, 0, cm.Schema["Can be owned"]);
            Assert.Null(t.Set(cm, 0, cm.Schema["Can be owned"], now == 0 ? 1 : 0));
            t.Save();
            rt.Check("tables.objects", "DATA/COMOBJ.DAT", "DATA/OBJECTS.DAT");
        }

        [Theory]
        [InlineData("UW")]
        [InlineData("UW2")]
        public void Common_properties_only(string game)
        {
            using var rt = RoundTrip.Start(game);
            if (rt == null) return;
            var s = GameSession.Open(rt.Edited);
            var t = ObjectTables.Load(s);
            var cm = ObjectTables.Table("Common properties");
            int now = t.Get(cm, 7, cm.Schema["Can be owned"]);
            Assert.Null(t.Set(cm, 7, cm.Schema["Can be owned"], now == 0 ? 1 : 0));
            t.Save();
            rt.Check("tables.comobj", "DATA/COMOBJ.DAT");
        }

        [Theory]
        [InlineData("UW")]
        [InlineData("UW2")]
        public void Skills_terrain_and_combinations(string game)
        {
            using var rt = RoundTrip.Start(game);
            if (rt == null) return;
            var s = GameSession.Open(rt.Edited);
            var sk = SkillsTable.Load(s);
            sk.SetAttribute(7, 3, 21);
            sk.Save();
            var te = TerrainTable.Load(s);
            te.Set(te.EntryOf(5, true), 0x20);
            te.Save();
            var cmb = CombinationTable.Load(s);
            cmb.SetItem(0, 2, 0xA0);
            cmb.Save();
            rt.Check("tables.skills-terrain-cmb", "DATA/CMB.DAT", "DATA/SKILLS.DAT", "DATA/TERRAIN.DAT");
        }

        [Fact]
        public void Minimum_light_uw2()
        {
            using var rt = RoundTrip.Start("UW2");
            if (rt == null) return;
            var s = GameSession.Open(rt.Edited);
            var dl = MinimumLightTable.Load(s);
            Assert.True(dl.Exists);
            dl.Set(6, (dl.Minimum(6) + 2) % 10, !dl.OnUnflagged(6));
            dl.Save();
            rt.Check("tables.dl", "DATA/DL.DAT");
        }

        [Fact]
        public void Schedules_uw2()
        {
            using var rt = RoundTrip.Start("UW2");
            if (rt == null) return;
            var sc = Schedules.Load(GameSession.Open(rt.Edited));
            sc.SetField(0, 0, ScdRow.HeaderFields(0)[0], 71);
            Assert.NotEmpty(sc.Save());
            rt.Check("tables.schedules", "DATA/SCD.ARK");
        }
    }
}
