using System;
using System.IO;
using System.Linq;
using UltimateEditor.Games;
using UltimateEditor.Sound;
using Underworld.Mods;
using UwEditor.Core;
using UwEditor.Core.Conversations;
using UwEditor.Core.Editing;
using UwEditor.Core.Graphics;
using UwEditor.Core.Music;
using UwEditor.Core.Sound;
using UwModManager.Core;
using Xunit;

namespace ModRoundTrip.Tests
{
    /// <summary>The executable's start square, sounds, music, conversations and pictures, and all of them in one mod.</summary>
    public class GameFileTests
    {
        static GameKind Kind(string game) => game == "UW2" ? GameKind.UW2 : GameKind.UW1;

        static byte[] Tone(SoundUse use, double seconds, int hz)
        {
            int rate = 22050, n = (int)(seconds * rate);
            var ch = new float[n];
            for (int i = 0; i < n; i++) ch[i] = (float)(0.3 * Math.Sin(2 * Math.PI * hz * i / rate));
            var r = SoundConversion.Convert(new WavFile(rate, new[] { ch }), use, new SoundConvertOptions());
            Assert.True(r.Ok, r.Problem);
            return r.Voc;
        }

        // ---- the executable

        [Theory]
        [InlineData("UW")]
        [InlineData("UW2")]
        public void New_game_start_square(string game)
        {
            using var rt = RoundTrip.Start(game);
            if (rt == null) return;
            var s = GameSession.Open(rt.Edited);
            s.SetNewGameStart(30, 4);
            var mod = rt.Check("exe.start", GamePaths.ExeName(Kind(game)));
            // the delta is the two bytes of the square, not the program
            Assert.True(mod.Files.Single().Delta.Length < 32);
        }

        [Theory]
        [InlineData("UW")]
        [InlineData("UW2")]
        public void The_start_square_follows_UltimaHacks(string game)
        {
            using var rt = RoundTrip.Start(game);
            if (rt == null) return;
            GameSession.Open(rt.Edited).SetNewGameStart(33, 5);
            rt.Check("exe.start-hacks");
            // with UltimaHacks the executable is the hacked one with the mod's square, not the editor's file
            string target = rt.Copy("hacks");
            var m = new Manager(Path.Combine(Path.GetDirectoryName(target), "manager-hacks"));
            var t = m.AddDos(target);
            var e = m.Library.Import(rt.ModFile);
            m.UseDos(t, e.Key, true);
            byte[] gog = RoundTrip.Read(rt.Clean, t.ExeName);
            Assert.Equal(Hacks.BuildExe(gog, Kind(game), (33, 5)), RoundTrip.Read(target, t.ExeName));
            m.UseDos(t, null, false);
            Assert.Equal(gog, RoundTrip.Read(target, t.ExeName));
        }

        // ---- sounds

        [Fact]
        public void Uw2_trap_sound_added_effect_replaced_and_sounds_dat()
        {
            using var rt = RoundTrip.Start("UW2");
            if (rt == null) return;
            var lib = new SoundLibrary(rt.Edited, GameKind.UW2);
            lib.Put(SoundLibrary.TrapName(5), Tone(SoundUse.Uw2Trap, 0.4, 600));
            lib.Put(SoundLibrary.EffectName(1), Tone(SoundUse.Uw2Effect, 0.3, 900));
            lib.Effects.Effects[13].Digital = !lib.Effects.Effects[13].Digital;
            Assert.Equal(3, lib.SaveAll().Count);
            var mod = rt.Check("sound.uw2", "SOUND/SOUNDS.DAT", "SOUND/SP01.VOC", "SOUND/UW05.VOC");
            Assert.True(mod.Files.Single(f => f.Path == "SOUND/UW05.VOC").Added);
        }

        [Fact]
        public void Uw1_speech_replaced_and_added()
        {
            using var rt = RoundTrip.Start("UW");
            if (rt == null) return;
            var lib = new SoundLibrary(rt.Edited, GameKind.UW1);
            lib.Put(lib.SpeechName(26), Tone(SoundUse.Uw1Speech, 0.5, 440));
            lib.Put(lib.SpeechName(21), Tone(SoundUse.Uw1Speech, 0.5, 660));   // not shipped; a cutscene may name it
            lib.SaveAll();
            var mod = rt.Check("sound.uw1", "SOUND/21.VOC", "SOUND/26.VOC");
            Assert.True(mod.Files.Single(f => f.Path == "SOUND/21.VOC").Added);
        }

        // ---- music

        [Theory]
        [InlineData("UW")]
        [InlineData("UW2")]
        public void A_theme_rewritten_in_both_versions(string game)
        {
            using var rt = RoundTrip.Start(game);
            if (rt == null) return;
            var lib = MusicLibrary.Open(rt.Edited, Kind(game));
            var t = lib.Themes.First(x => lib.Read(x, MusicVersion.Fm) != null && lib.Read(x, MusicVersion.Mt32) != null);
            foreach (var v in new[] { MusicVersion.Fm, MusicVersion.Mt32 })
            {
                var x = XmiFile.Parse(lib.Read(t, v));
                var ev = x.Events();
                foreach (var note in ev.Where(e => e.IsNote).Take(20)) note.Data1 = (byte)Math.Min(127, note.Data1 + 5);
                lib.Set(t, v, XmiFile.Build(x.Timbres.ToList(), ev).ToBytes());
            }
            Assert.Equal(2, lib.Save().Count);
            var mod = rt.Check("music.theme");
            Assert.Equal(2, mod.Files.Count);
            Assert.All(mod.Files, f => Assert.EndsWith(".XMI", f.Path));
        }

        // ---- conversations

        [Theory]
        [InlineData("UW")]
        [InlineData("UW2")]
        public void A_conversation_changed(string game)
        {
            using var rt = RoundTrip.Start(game);
            if (rt == null) return;
            var s = GameSession.Open(rt.Edited);
            var lib = ConversationLibrary.Open(s);
            int slot = lib.Archive.Slots().First(x => lib.Archive.Has(x) && lib.Archive.Read(x) != null);
            var conv = lib.Archive.Read(slot);
            conv.Code = conv.Code.Concat(new ushort[] { (ushort)BablOp.NOP, (ushort)BablOp.NOP }).ToArray();
            lib.Archive.Write(slot, conv);
            lib.Save();
            rt.Check("conversation.code");
        }

        // ---- pictures

        [Theory]
        [InlineData("UW")]
        [InlineData("UW2")]
        public void An_object_picture_replaced(string game)
        {
            using var rt = RoundTrip.Start(game);
            if (rt == null) return;
            var s = GameSession.Open(rt.Edited);
            var pals = Palettes.Load(s.DataFolder);
            var f = PictureFile.Load(GamePaths.Find(rt.Edited, "DATA/OBJECTS.GR"), s.Kind);
            var img = f.Get(1, pals, 0);
            f.Replace(1, img, img.Pixels.Select(v => v == 0 ? 0 : (v % 15) + 1).ToArray());
            f.Save();
            rt.Check("pictures.objects", "DATA/OBJECTS.GR");
        }

        [Theory]
        [InlineData("UW")]
        [InlineData("UW2")]
        public void A_critter_frame_redrawn(string game)
        {
            using var rt = RoundTrip.Start(game);
            if (rt == null) return;
            var s = GameSession.Open(rt.Edited);
            var files = CritterArt.Load(rt.Edited, s.Kind, s.Names);
            var page = files.First(x => x.Pages.Count > 0).Pages[0];
            int fr = page.Frames.Keys.First();
            page.Replace(fr, page.Frames[fr].Pixels.Select(v => v == 0 ? 0 : v ^ 1).ToArray());
            page.Save();
            var mod = rt.Check("pictures.critter");
            Assert.All(mod.Files, x => Assert.StartsWith("CRIT/", x.Path));
        }

        [Theory]
        [InlineData("UW")]
        [InlineData("UW2")]
        public void A_cutscene_frame_redrawn(string game)
        {
            using var rt = RoundTrip.Start(game);
            if (rt == null) return;
            var cs = Cutscene.Find(rt.Edited).First(c => c.Animations.Count > 1);
            var a = LpfAnimation.Load(cs.Animations[1]);
            var px = a.Frames()[0].ToArray();
            for (int i = 0; i < px.Length; i += 7) px[i] = (px[i] + 1) & 0xFF;
            a.Replace(0, px);
            a.Save();
            var mod = rt.Check("pictures.cutscene");
            Assert.All(mod.Files, x => Assert.StartsWith("CUTS/", x.Path));
        }

        // ---- everything in one mod

        [Theory]
        [InlineData("UW")]
        [InlineData("UW2")]
        public void Everything_at_once(string game)
        {
            using var rt = RoundTrip.Start(game);
            if (rt == null) return;
            var kind = Kind(game);
            var s = GameSession.Open(rt.Edited);
            var edits = new LevelEdits(s);
            var e = edits.For(1);
            for (int y = 10; y < 20; y++) e.SetTileField(20, y, "Floor height", (e.Model.Tiles[20, y].Height + 1) % 16);
            edits.Save();
            var pak = StringsPakFile.Load(GamePaths.Find(rt.Edited, "DATA/STRINGS.PAK"));
            var b4 = pak.Get(4); b4[3] = "everything, at once"; pak.Set(4, b4); pak.Save();
            var sh = ShadesTable.Load(s); sh.Set(2, 2, 5); sh.Save();
            var t = ObjectTables.Load(s); var cr = ObjectTables.Table("Creatures"); t.Set(cr, 2, cr.Schema["Average HP"], 99); t.Save();
            s.SetNewGameStart(31, 6);
            var music = MusicLibrary.Open(rt.Edited, kind);
            var theme = music.Themes.First(x => music.Read(x, MusicVersion.Fm) != null);
            var xm = XmiFile.Parse(music.Read(theme, MusicVersion.Fm));
            var ev = xm.Events();
            var first = ev.First(x => x.IsNote);
            first.Data1 = (byte)((first.Data1 + 7) % 128);
            music.Set(theme, MusicVersion.Fm, XmiFile.Build(xm.Timbres.ToList(), ev).ToBytes());
            music.Save();
            var snd = new SoundLibrary(rt.Edited, kind);
            snd.Put(kind == GameKind.UW2 ? SoundLibrary.TrapName(7) : snd.SpeechName(22), Tone(kind == GameKind.UW2 ? SoundUse.Uw2Trap : SoundUse.Uw1Speech, 0.3, 500));
            snd.SaveAll();
            var mod = rt.Check("everything");
            Assert.True(mod.Files.Count >= 7, string.Join(", ", mod.Files.Select(f => f.Path)));
            // still differences only: far smaller than the files it changes
            long changed = mod.Files.Where(f => !f.Added).Sum(f => f.TargetSize);
            Assert.True(new FileInfo(rt.ModFile).Length * 50 < changed, new FileInfo(rt.ModFile).Length + " bytes of package for " + changed + " bytes of files");
        }
    }
}
