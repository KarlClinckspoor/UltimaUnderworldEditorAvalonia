using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using UltimateEditor.Games;
using Underworld.Mods;
using UwEditor.Core.Mods;
using UwEditor.Core.Tests;
using UwModManager.Core;
using Xunit;

[assembly: CollectionBehavior(DisableTestParallelization = true)]

namespace ModRoundTrip.Tests
{
    /// <summary>
    /// One round trip (docs/mod-format.md, "Testing"): three copies of a clean game; the test edits
    /// <see cref="Edited"/> with the editor's core and saves; <see cref="Check"/> then makes the mod
    /// the way the editor's Make a mod does, and checks that
    /// <list type="bullet">
    /// <item>the package carries nothing of the original files (no 32-byte run of an original in what it adds);</item>
    /// <item>the mod manager, installing it into another clean copy, gives every game file byte for byte as the editor saved it;</item>
    /// <item>going back to the original game gives every clean file back;</item>
    /// <item>the editor's own Load a mod gives the same files;</item>
    /// <item>an Underworld Exhumed home gets the same files (the executable's changes going into its settings).</item>
    /// </list>
    /// </summary>
    public sealed class RoundTrip : IDisposable
    {
        public readonly string Game;
        public readonly GameKind Kind;
        /// <summary>Never written: the game as shipped.</summary>
        public readonly string Clean;
        /// <summary>The copy the test edits through the editor's core.</summary>
        public readonly string Edited;
        readonly string root;
        public ModPackage Mod { get; private set; }
        public string ModFile { get; private set; }

        RoundTrip(string game, string source)
        {
            Game = game;
            Kind = game == "UW2" ? GameKind.UW2 : GameKind.UW1;
            root = Path.Combine(Path.GetTempPath(), "uwmod-roundtrip", game + "-" + Guid.NewGuid().ToString("N").Substring(0, 8));
            Clean = Copy(source, "clean");
            Edited = Copy(source, "edited");
        }

        /// <summary>The round trip for a game ("UW" or "UW2"), or null when its files are not there (UW_REQUIRE_GAMEDATA=1 fails instead).</summary>
        public static RoundTrip Start(string game)
        {
            string g = GameData.Game(game);
            return g == null ? null : new RoundTrip(game, g);
        }

        /// <summary>A fresh copy of the clean game, for a target.</summary>
        public string Copy(string name) => Copy(Clean, name);

        string Copy(string from, string name)
        {
            string dst = Path.Combine(root, name);
            foreach (var f in Directory.GetFiles(from, "*", SearchOption.AllDirectories))
            {
                string rel = Path.GetRelativePath(from, f);
                // the copies are games as shipped: no saved games, and none of the editor's own files
                string top = rel.Split(Path.DirectorySeparatorChar)[0].ToUpperInvariant();
                if (top.StartsWith("SAVE") || GamePaths.IsWorkFile(rel)) continue;
                string to = Path.Combine(dst, rel);
                Directory.CreateDirectory(Path.GetDirectoryName(to));
                File.Copy(f, to);
            }
            return dst;
        }

        public void Dispose() { try { Directory.Delete(root, true); } catch (Exception) { } }

        /// <summary>Runs every check on what the test saved in <see cref="Edited"/>. Returns the mod.</summary>
        public ModPackage Check(string id, params string[] expectFiles)
        {
            // ---- the editor's Make a mod (compared with the .ORIG copies it keeps)
            var changes = EditorMods.Collect(Edited, Kind, null);
            Assert.True(changes.Problems.Count == 0, string.Join("\n", changes.Problems));
            Assert.NotEmpty(changes.Changed);
            // and the same against the clean copy, as "an untouched copy of the game"
            var against = EditorMods.Collect(Edited, Kind, Clean);
            Assert.Empty(against.Problems);
            Assert.Equal(changes.Changed.Select(c => c.Path), against.Changed.Select(c => c.Path));
            if (expectFiles.Length > 0) Assert.Equal(expectFiles.OrderBy(x => x, StringComparer.Ordinal), changes.Changed.Select(c => c.Path));
            var made = ModMaker.Make(changes, id, id, "1.0.0", "Round trip " + id, new[] { "Tests" }, EditorMods.MadeWith);
            ModFile = Path.Combine(root, id + ModPackage.Extension);
            made.Write(ModFile);
            Mod = ModPackage.Load(ModFile);

            NothingOfTheOriginals();

            // ---- the mod manager, on a DOS copy
            string target = Copy("target");
            var m = new Manager(Path.Combine(root, "manager"));
            var t = m.AddDos(target);
            var entry = m.Library.Import(ModFile);
            Assert.Empty(m.Problems(t, entry.Package));
            m.UseDos(t, entry.Key, false);
            SameGameFiles(Edited, target, "the manager's install");
            Assert.Empty(DosGame.LocalChanges(t));
            m.UseDos(t, null, false);
            SameGameFiles(Clean, target, "the original game, back");

            // ---- the editor's own Load a mod
            string loaded = Copy("loaded");
            EditorMods.Apply(ModPackage.Load(ModFile), loaded, Kind);
            SameGameFiles(Edited, loaded, "the editor's Load a mod");

            // ---- Underworld Exhumed: a home over the clean game
            var x = new Target { Kind = TargetKind.Exhumed, Folder = Clean, Game = Kind == GameKind.UW2 ? "uw2" : "uw1", Name = "Exhumed", PortExe = "uwport", Features = ExhumedGame.ParseFeatures(Features) };
            m.State.Targets.Add(x);
            if (m.Problems(x, entry.Package).Count == 0)
            {
                string home = m.UseExhumed(x, entry.Key);
                foreach (var f in Mod.Files.Where(f => f.Path != x.ExeName))
                    Assert.True(Same(Read(Edited, f.Path), Read(home, f.Path)), f.Path + " in the Exhumed home is not the edited file");
                if (ExeChanges.MovedStart(Read(Clean, x.ExeName), Read(Edited, x.ExeName), Kind, out int sx, out int sy))
                    Assert.Contains("start-square=" + sx + "," + sy, File.ReadAllText(Path.Combine(home, ExhumedGame.ConfigName(x.Game))));
            }
            return Mod;
        }

        /// <summary>The fork's --features for each port, as in the manager's tests.</summary>
        string Features => Kind == GameKind.UW2
            ? "features 1 uw2port\nstart-square\nexe-patch 0x39C50-0x6321C 0x63790-0x643D0 0x68A90-0x717A0\ndata-overlay\n"
            : "features 1 uw1port\nstart-square\nexe-patch 0x3C7B0-0x58FC4 0x59440-0x59EF0 0x5DCC0-0x65700\ndata-overlay\n";

        /// <summary>
        /// No 32-byte run of what the package adds is found in the original file it changes (for
        /// a UW2 archive's blocks, in the original blocks decompressed): a mod carries its own
        /// bytes only. A run of one repeated byte is not counted (zeros are everywhere).
        /// </summary>
        void NothingOfTheOriginals()
        {
            const int W = 32;
            foreach (var f in Mod.Files.Where(f => !f.Added))
            {
                byte[] orig = Read(Clean, f.Path);
                var windows = new HashSet<ulong>();
                void Index(byte[] b) { for (int i = 0; i + W <= b.Length; i++) if (!Uniform(b, i, W)) windows.Add(Hash(b, i, W)); }
                Index(orig);
                if (Kind == GameKind.UW2 && f.Path.EndsWith(".ARK"))
                {
                    var a = new Uw2Archive(orig);
                    for (int k = 0; k < a.Count; k++) if (a.Has(k)) { try { Index(a.Read(k)); } catch (Exception) { } }
                }
                long found = 0, added = 0;
                foreach (var (block, bytes) in Delta.Added(f.Delta))
                {
                    added += bytes.Length;
                    for (int i = 0; i + W <= bytes.Length; i++)
                        if (!Uniform(bytes, i, W) && windows.Contains(Hash(bytes, i, W))) found++;
                }
                Assert.True(found == 0, f.Path + ": " + found + " runs of " + W + " bytes the mod adds are the original's (" + added + " bytes added)");
            }
        }

        static bool Uniform(byte[] b, int at, int n) { for (int i = 1; i < n; i++) if (b[at + i] != b[at]) return false; return true; }

        static ulong Hash(byte[] b, int at, int n)
        {
            ulong h = 14695981039346656037UL;
            for (int i = 0; i < n; i++) { h ^= b[at + i]; h *= 1099511628211UL; }
            return h;
        }

        /// <summary>Every game file of the two folders (Underworld.Mods.GamePaths), byte for byte.</summary>
        public void SameGameFiles(string expected, string actual, string what)
        {
            var a = GamePaths.List(expected, Kind);
            var b = GamePaths.List(actual, Kind);
            Assert.True(a.SequenceEqual(b), what + ": the files differ: only in the expected " + string.Join(", ", a.Except(b)) + "; only in the result " + string.Join(", ", b.Except(a)));
            foreach (var rel in a)
            {
                byte[] x = Read(expected, rel), y = Read(actual, rel);
                if (Same(x, y)) continue;
                int i = 0;
                while (i < Math.Min(x.Length, y.Length) && x[i] == y[i]) i++;
                Assert.Fail(string.Format("{0}: {1} differs at byte 0x{2:X} ({3} bytes expected, {4} found)", what, rel, i, x.Length, y.Length));
            }
        }

        public static byte[] Read(string folder, string rel) { string p = GamePaths.Find(folder, rel); return p == null ? null : File.ReadAllBytes(p); }

        static bool Same(byte[] a, byte[] b) => a == null || b == null ? a == b : a.AsSpan().SequenceEqual(b);

        public override string ToString() => Game + " round trip in " + root;
    }
}
