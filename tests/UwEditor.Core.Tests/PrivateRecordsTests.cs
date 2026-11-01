using System;
using System.IO;
using System.Linq;
using UltimateEditor.Games;
using UwEditor.Core.Conversations;
using Xunit;
using Xunit.Abstractions;

namespace UwEditor.Core.Tests
{
    /// <summary>BABGLOBS.DAT and BGLOBALS.DAT follow the conversations' variable counts when CNV.ARK is saved. On a copy of the game.</summary>
    public class PrivateRecordsTests
    {
        readonly ITestOutputHelper output;
        public PrivateRecordsTests(ITestOutputHelper output) { this.output = output; }

        static string CopyGame(string g)
        {
            string tmp = Path.Combine(Path.GetTempPath(), "uwedit-bglobals-" + Guid.NewGuid().ToString("N"));
            foreach (var dir in Directory.GetDirectories(g))
            {
                string name = Path.GetFileName(dir);
                if (name != "DATA" && !name.StartsWith("SAVE")) continue;
                Directory.CreateDirectory(Path.Combine(tmp, name));
                foreach (var f in Directory.GetFiles(dir)) File.Copy(f, Path.Combine(tmp, name, Path.GetFileName(f)));
            }
            foreach (var f in Directory.GetFiles(g, "*.EXE")) File.Copy(f, Path.Combine(tmp, Path.GetFileName(f)));
            return tmp;
        }

        [Theory]
        [InlineData("UW")]
        [InlineData("UW2")]
        public void Private_globals_files_round_trip(string game)
        {
            string g = GameData.Game(game); if (g == null) return;
            foreach (var p in new[] { GameProfile.Find(Path.Combine(g, "DATA"), "BABGLOBS.DAT") }.Concat(Directory.GetDirectories(g, "SAVE*").Select(d => GameProfile.Find(d, "BGLOBALS.DAT"))))
            {
                if (p == null) continue;
                bool words = !p.EndsWith("BABGLOBS.DAT", StringComparison.OrdinalIgnoreCase);
                var b = File.ReadAllBytes(p);
                Assert.Equal(b, BGlobals.Parse(b, words).ToBytes(words));
            }
        }

        [Theory]
        [InlineData("UW", null)]
        [InlineData("UW2", "SAVE1")]
        public void Saving_a_conversation_with_more_variables_or_in_an_empty_slot_resizes_or_adds_its_records(string game, string save)
        {
            string g = GameData.Game(game); if (g == null) return;
            string tmp = CopyGame(g);
            try
            {
                var s = GameSession.Open(save == null ? tmp : Path.Combine(tmp, save));
                Assert.Equal(save != null, s.IsSave);
                string dataPath = GameProfile.Find(s.DataFolder, "BABGLOBS.DAT");
                string savePath = save == null ? null : GameProfile.Find(s.SaveFolder, "BGLOBALS.DAT");
                if (savePath != null)
                {
                    // values in every record, to see them kept
                    var seeded = BGlobals.Parse(File.ReadAllBytes(savePath), true);
                    foreach (var r in seeded.Records) for (int i = 0; i < r.Words.Length; i++) r.Words[i] = r.Slot + i + 1;
                    File.WriteAllBytes(savePath, seeded.ToBytes(true));
                }
                var dataBefore = BGlobals.Parse(File.ReadAllBytes(dataPath), false);
                var saveBefore = savePath == null ? null : BGlobals.Parse(File.ReadAllBytes(savePath), true);
                byte[] otherSave = null;
                string otherPath = save == null ? null : GameProfile.Find(Path.Combine(tmp, "SAVE2"), "BGLOBALS.DAT");
                if (otherPath != null) otherSave = File.ReadAllBytes(otherPath);
                // a globals window open before the save, with a stale copy of BGLOBALS.DAT
                var stale = save == null ? null : GlobalVariables.Load(s, ConversationLibrary.Open(s).Archive, null);

                var lib = ConversationLibrary.Open(s);
                int grown = lib.Archive.Slots().First(x => saveBefore == null || saveBefore.Find(x) != null && saveBefore.Find(x).Words.Length > 0);
                var c = lib.Archive.Read(grown);
                int oldVars = c.Variables;
                c.Variables += 3;
                lib.Archive.Write(grown, c);
                int empty = Enumerable.Range(1, lib.Archive.Count - 1).First(x => !lib.Archive.Has(x));
                var added = lib.Archive.Read(grown);
                added.Slot = empty;
                lib.Archive.Write(empty, added);
                var done = lib.Save();
                output.WriteLine("{0}: grown {1} ({2} → {3}), added {4}; written: {5}", game, grown, oldVars, oldVars + 3, empty, string.Join("; ", done));
                Assert.Contains(done, d => d.StartsWith("BABGLOBS.DAT", StringComparison.OrdinalIgnoreCase));

                var dataAfter = BGlobals.Parse(File.ReadAllBytes(dataPath), false);
                Assert.Equal(oldVars + 3, dataAfter.Find(grown).Words.Length);
                Assert.Equal(oldVars + 3, dataAfter.Find(empty).Words.Length);
                Assert.Equal(dataBefore.Records.Count + 1, dataAfter.Records.Count);
                // in slot order, the others as they were
                Assert.Equal(dataAfter.Records.Select(r => r.Slot).OrderBy(x => x), dataAfter.Records.Select(r => r.Slot));
                foreach (var r in dataBefore.Records.Where(r => r.Slot != grown)) Assert.Equal(r.Words.Length, dataAfter.Find(r.Slot).Words.Length);
                Assert.True(File.Exists(dataPath + ".BKP"));

                if (savePath != null)
                {
                    Assert.Contains(done, d => d.StartsWith("BGLOBALS.DAT", StringComparison.OrdinalIgnoreCase));
                    var saveAfter = BGlobals.Parse(File.ReadAllBytes(savePath), true);
                    var before = saveBefore.Find(grown).Words;
                    var after = saveAfter.Find(grown).Words;
                    Assert.Equal(oldVars + 3, after.Length);
                    Assert.Equal(before, after.Take(before.Length));
                    Assert.All(after.Skip(before.Length), w => Assert.Equal(0, w));
                    Assert.All(saveAfter.Find(empty).Words, w => Assert.Equal(0, w));
                    Assert.Equal(oldVars + 3, saveAfter.Find(empty).Words.Length);
                    foreach (var r in saveBefore.Records.Where(r => r.Slot != grown)) Assert.Equal(r.Words, saveAfter.Find(r.Slot).Words);
                    // the other save is not touched
                    if (otherPath != null) Assert.Equal(otherSave, File.ReadAllBytes(otherPath));
                    // the stale globals window saving afterwards keeps the new sizes
                    var p = stale.All.First(x => x.Kind == GlobalKind.Private && x.Editable && x.Slot != grown);
                    Assert.Null(stale.Set(p, 7));
                    stale.Save();
                    var merged = BGlobals.Parse(File.ReadAllBytes(savePath), true);
                    Assert.Equal(oldVars + 3, merged.Find(grown).Words.Length);
                    Assert.NotNull(merged.Find(empty));
                    Assert.Equal(7, merged.Find(p.Slot).Words[p.Number]);
                }

                // saving again with nothing to resize writes no globals file
                var c2 = lib.Archive.Read(grown);
                c2.Code = c2.Code.Concat(new ushort[] { (ushort)BablOp.NOP }).ToArray();
                lib.Archive.Write(grown, c2);
                var again = lib.Save();
                Assert.DoesNotContain(again, d => d.Contains("GLOB"));
            }
            finally { try { Directory.Delete(tmp, true); } catch (Exception) { } }
        }
    }
}
