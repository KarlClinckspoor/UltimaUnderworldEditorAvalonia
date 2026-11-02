using System;
using System.IO;
using System.Linq;
using Xunit;

namespace UwEditor.Core.Tests
{
    public class DosBoxConfigTests
    {
        [Fact]
        public void Gogs_configuration_is_kept_with_our_own_autoexec()
        {
            string dir = Path.Combine(Path.GetTempPath(), "uwedit-gogconf-" + Guid.NewGuid().ToString("N").Substring(0, 8));
            Directory.CreateDirectory(dir);
            try
            {
                File.WriteAllText(Path.Combine(dir, "dosboxUW_single.conf"), "[autoexec]\nmount C \"..\"\nuw.exe\n");
                File.WriteAllText(Path.Combine(dir, "dosboxUW.conf"), "# GOG\n[sdl]\nfullscreen=true\n[cpu]\ncycles=fixed 20000\n[autoexec]\n@echo off\nmount C \"..\"\nimgmount d game.ins\nuw.exe\nexit\n");
                File.WriteAllText(Path.Combine(dir, "notes.conf"), "nothing for DOSBox here");
                string gog = DosBoxConfig.FindGogConf(dir);
                Assert.Equal("dosboxUW.conf", Path.GetFileName(gog));
                string conf = DosBoxConfig.Build(File.ReadAllText(gog), @"C:\Games\Ultima Underworld\", "UW.EXE");
                var sections = DosBoxConfig.Sections(conf);
                Assert.Equal(new[] { "sdl", "cpu", "autoexec" }, sections.Select(s => s.Key));
                Assert.Contains("cycles=fixed 20000", sections[1].Value);
                var auto = sections[2].Value.Where(l => l.Trim().Length > 0).ToList();
                Assert.Equal(new[] { "@echo off", "mount c \"C:\\Games\\Ultima Underworld\"", "c:", "cls", "UW", "exit" }, auto);
                Assert.DoesNotContain("imgmount", conf);
            }
            finally { Directory.Delete(dir, true); }
        }

        [Fact]
        public void Without_gog_the_defaults_are_used_and_a_quote_is_refused()
        {
            string conf = DosBoxConfig.Build(null, "/home/me/games/uw2", "UW2.EXE");
            var sections = DosBoxConfig.Sections(conf).Select(s => s.Key).ToList();
            Assert.Contains("sblaster", sections);
            Assert.Contains("dos", sections);
            Assert.Equal("autoexec", sections.Last());
            Assert.Contains("mount c \"/home/me/games/uw2\"", conf);
            Assert.Contains("\nUW2", conf.Replace("\r", ""));
            Assert.Null(DosBoxConfig.FindGogConf(Path.Combine(Path.GetTempPath(), "no-such-folder-" + Guid.NewGuid())));
            Assert.Throws<ArgumentException>(() => DosBoxConfig.Build(null, "C:\\a\"b", "UW.EXE"));
            Assert.Equal(new[] { "-conf", "x.conf", "-noconsole", "a b" }, DosBoxConfig.Arguments("x.conf", "-noconsole \"a b\""));
        }
    }
}
