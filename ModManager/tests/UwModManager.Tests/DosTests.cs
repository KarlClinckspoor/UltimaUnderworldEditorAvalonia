using UltimateEditor.Games;
using Underworld.Mods;
using UwModManager.Core;
using Xunit;

namespace UwModManager.Tests;

/// <summary>DOS folders changed in place: take over, use a mod, switch, back to the original, UltimaHacks, outside changes, the journal.</summary>
public class DosTests
{
    static (Manager m, Target t, string folder) Setup(string game)
    {
        string folder = Env.Copy(game);
        if (folder == null) return (null, null, null);
        var m = new Manager(Env.Temp("mgr"));
        return (m, m.AddDos(folder), folder);
    }

    [Theory, MemberData(nameof(Env.Games), MemberType = typeof(Env))]
    public void Taking_over_keeps_every_original_and_knows_the_gog_release(string game)
    {
        var (m, t, folder) = Setup(game);
        if (m == null) return;
        Assert.Equal(game == "UW2" ? "uw2" : "uw1", t.Game);
        Assert.True(t.Baseline.Count > 250);
        Assert.All(t.Baseline.Values, sha => Assert.True(m.Vault.Has(sha)));
        Assert.True(m.HasGogExe(t));
        Assert.Empty(DosGame.LocalChanges(t));
        // and a second manager reads the same state back
        var again = new Manager(m.Paths.Root);
        Assert.Equal(t.Baseline, again.Get(t.Id).Baseline);
    }

    [Theory, MemberData(nameof(Env.Games), MemberType = typeof(Env))]
    public void The_editors_first_versions_are_the_originals(string game)
    {
        string folder = Env.Copy(game);
        if (folder == null) return;
        var kind = Env.Kind(game);
        // the editor saved a level (keeping LEV.ARK.ORIG) and added a sound (an empty .ORIG marks it)
        string lev = GamePaths.Find(folder, "DATA/LEV.ARK");
        byte[] shipped = File.ReadAllBytes(lev);
        File.Copy(lev, lev + ".ORIG");
        Env.EditLevel(folder, kind, 1, 100, 1);
        Env.AddSound(folder, game, new byte[] { 1, 2, 3 });
        File.WriteAllBytes(new GameDir(folder, kind).PathFor(Env.NewSound(game)) + ".ORIG", Array.Empty<byte>());
        var m = new Manager(Env.Temp("mgr"));
        var t = m.AddDos(folder);
        Assert.Equal(shipped, DosGame.Original(t, m.Vault, "DATA/LEV.ARK"));
        Assert.False(t.Baseline.ContainsKey(Env.NewSound(game)));
        Assert.Equal(new[] { "DATA/LEV.ARK", Env.NewSound(game) }.OrderBy(x => x, StringComparer.Ordinal), DosGame.LocalChanges(t).OrderBy(x => x, StringComparer.Ordinal));
    }

    [Theory, MemberData(nameof(Env.Games), MemberType = typeof(Env))]
    public void A_mod_is_applied_switched_and_removed_exactly(string game)
    {
        var (m, t, folder) = Setup(game);
        if (m == null) return;
        var kind = Env.Kind(game);
        string modded = null;
        var a = Env.MakeMod(game, f => { Env.EditLevel(f, kind, 1, 100, 1); Env.EditString(f, 4, 3, "a gleaming sword"); modded = f; }, "test.a");
        var b = Env.MakeMod(game, f => Env.EditLevel(f, kind, 2, 200, 4), "test.b");
        m.Library.Add(a); m.Library.Add(b);
        string ka = Library.KeyOf(a), kb = Library.KeyOf(b);
        Assert.Empty(m.Problems(t, a));

        var plan = m.UseDos(t, ka, false);
        Assert.Equal(2, plan.Writes.Count);
        Assert.Equal(ka, t.ActiveMod);
        foreach (var rel in new[] { "DATA/LEV.ARK", "DATA/STRINGS.PAK" })
            Assert.Equal(Env.Read(modded, rel), Env.Read(folder, rel));
        Assert.Empty(DosGame.LocalChanges(t));

        // the second mod: STRINGS.PAK goes back, LEV.ARK changes
        plan = m.UseDos(t, kb, false);
        Assert.Equal(new[] { "DATA/LEV.ARK", "DATA/STRINGS.PAK" }, plan.Writes.Keys);
        Assert.Equal(DosGame.Original(t, m.Vault, "DATA/STRINGS.PAK"), Env.Read(folder, "DATA/STRINGS.PAK"));
        Assert.Single(t.Written);

        // the original game: every file as it was
        m.UseDos(t, null, false);
        Assert.Empty(t.Written);
        Assert.Null(t.ActiveMod);
        foreach (var kv in t.Baseline) Assert.Equal(kv.Value, Files.Sha(Env.Read(folder, kv.Key)));
    }

    [Theory, MemberData(nameof(Env.Games), MemberType = typeof(Env))]
    public void A_sound_the_game_opens_by_number_is_added_and_goes_with_the_original_game(string game)
    {
        var (m, t, folder) = Setup(game);
        if (m == null) return;
        var voc = new byte[] { 0x43, 0x72, 0x65, 0x61, 0x74, 0x69, 0x76, 0x65 };
        var a = Env.MakeMod(game, f => Env.AddSound(f, game, voc), "test.sound");
        Assert.True(a.Files.Single().Added);
        m.Library.Add(a);
        Assert.Empty(m.Problems(t, a));
        m.UseDos(t, Library.KeyOf(a), false);
        Assert.Equal(voc, Env.Read(folder, Env.NewSound(game)));
        m.UseDos(t, null, false);
        Assert.Null(Env.Read(folder, Env.NewSound(game)));
        Assert.Empty(DosGame.LocalChanges(t));
    }

    [Theory, MemberData(nameof(Env.Games), MemberType = typeof(Env))]
    public void UltimaHacks_and_a_start_square_go_together(string game)
    {
        var (m, t, folder) = Setup(game);
        if (m == null) return;
        var kind = Env.Kind(game);
        var mod = Env.MakeMod(game, f => Env.SetStart(f, kind, 33, 5), "test.start");
        m.Library.Add(mod);
        byte[] orig = DosGame.Original(t, m.Vault, t.ExeName);
        m.UseDos(t, null, true);
        byte[] hacked = Env.Read(folder, t.ExeName);
        Assert.Equal(Hacks.For(kind).Apply(orig), hacked);
        Assert.Equal(orig.Length + (kind == GameKind.UW2 ? 9472 : 8704), hacked.Length);
        m.UseDos(t, Library.KeyOf(mod), true);
        byte[] both = Env.Read(folder, t.ExeName);
        Assert.Equal(hacked.Length, both.Length);
        Assert.True(StartPosition.Read(both, kind, out int x, out int y));
        Assert.Equal((33, 5), (x, y));
        // without the hack: the original with the start square, byte for byte the mod's
        m.UseDos(t, Library.KeyOf(mod), false);
        byte[] plain = Env.Read(folder, t.ExeName);
        Assert.Equal(mod.BuildOne(mod.Files.Single(), orig), plain);
        Assert.True(StartPosition.Read(plain, kind, out x, out y));
        Assert.Equal((33, 5), (x, y));
        m.UseDos(t, null, false);
        Assert.Equal(orig, Env.Read(folder, t.ExeName));
    }

    [Theory, MemberData(nameof(Env.Games), MemberType = typeof(Env))]
    public void Other_changes_to_the_executable_do_not_follow_UltimaHacks(string game)
    {
        var (m, t, folder) = Setup(game);
        if (m == null) return;
        var kind = Env.Kind(game);
        byte[] orig = DosGame.Original(t, m.Vault, t.ExeName);
        int at = orig.Length - 100;
        var mod = Env.MakeMod(game, f =>
        {
            string p = GamePaths.Find(f, t.ExeName);
            byte[] e = File.ReadAllBytes(p); e[at] ^= 0x20; File.WriteAllBytes(p, e);
        }, "test.exebyte");
        m.Library.Add(mod);
        var err = Assert.Throws<ModException>(() => m.UseDos(t, Library.KeyOf(mod), true));
        Assert.Contains("UltimaHacks", err.Message);
        Assert.Equal(orig, Env.Read(folder, t.ExeName));
        // without the hack it applies as made
        m.UseDos(t, Library.KeyOf(mod), false);
        Assert.Equal((byte)(orig[at] ^ 0x20), Env.Read(folder, t.ExeName)[at]);
    }

    [Theory, MemberData(nameof(Env.Games), MemberType = typeof(Env))]
    public void Outside_changes_are_never_overwritten_without_asking(string game)
    {
        var (m, t, folder) = Setup(game);
        if (m == null) return;
        var kind = Env.Kind(game);
        var a = Env.MakeMod(game, f => Env.EditLevel(f, kind, 1, 300, 2), "test.a");
        m.Library.Add(a);
        Env.EditLevel(folder, kind, 1, 500, 8);   // the editor saved a change
        byte[] editorLevel = Env.Read(folder, "DATA/LEV.ARK");
        Assert.Equal(new[] { "DATA/LEV.ARK" }, DosGame.LocalChanges(t));
        var plan = m.PlanDos(t, Library.KeyOf(a), false);
        Assert.Equal(new[] { "DATA/LEV.ARK" }, plan.OutsideChanges);
        Assert.Throws<ModException>(() => m.UseDos(t, Library.KeyOf(a), false));
        Assert.Null(t.ActiveMod);
        // kept as a mod, then overwritten
        var (cap, diff) = m.Capture(t, new ModDetails { Id = "karl.local", Name = "Local changes" });
        Assert.NotNull(cap);
        Assert.Equal("DATA/LEV.ARK", cap.Files.Single().Path);
        m.UseDos(t, Library.KeyOf(a), false, overwrite: true);
        Assert.Empty(DosGame.LocalChanges(t));
        // the captured mod gives the editor's change back on the originals, byte for byte
        m.Library.Add(cap);
        m.UseDos(t, Library.KeyOf(cap), false);
        Assert.Equal(editorLevel, Env.Read(folder, "DATA/LEV.ARK"));
    }

    [Theory, MemberData(nameof(Env.Games), MemberType = typeof(Env))]
    public void An_interrupted_change_is_undone_at_the_next_start(string game)
    {
        var (m, t, folder) = Setup(game);
        if (m == null) return;
        var kind = Env.Kind(game);
        byte[] lev0 = Env.Read(folder, "DATA/LEV.ARK"), str0 = Env.Read(folder, "DATA/STRINGS.PAK");
        var a = Env.MakeMod(game, f => { Env.EditLevel(f, kind, 1, 100, 1); Env.EditString(f, 4, 3, "x"); }, "test.a");
        m.Library.Add(a);
        string key = Library.KeyOf(a);
        // the program stops after writing the files, before saving its state
        var plan = m.PlanDos(t, key, false);
        Assert.Throws<IOException>(() => DosGame.Apply(plan, m.Vault, m.Paths.JournalFile, () => throw new IOException("power cut")));
        Assert.True(File.Exists(m.Paths.JournalFile));
        Assert.Equal(plan.Writes["DATA/LEV.ARK"], Env.Read(folder, "DATA/LEV.ARK"));
        File.WriteAllBytes(Path.Combine(GameDir.FindDir(folder, "DATA")!, "STRINGS.PAK.uwmm-tmp"), new byte[10]);
        var m2 = new Manager(m.Paths.Root);
        Assert.Contains("undone", m2.Recover());
        var t2 = m2.Get(t.Id);
        Assert.Null(t2.ActiveMod);
        Assert.Equal(lev0, Env.Read(folder, "DATA/LEV.ARK"));
        Assert.Equal(str0, Env.Read(folder, "DATA/STRINGS.PAK"));
        Assert.False(File.Exists(m.Paths.JournalFile));
        Assert.Empty(Directory.GetFiles(folder, "*.uwmm-tmp", SearchOption.AllDirectories));
        Assert.Empty(DosGame.LocalChanges(t2));
        // and a change that finished before the stop is left as it is
        m2.UseDos(t2, key, false);
        Assert.Null(m2.Recover());
        Assert.Equal(key, m2.Get(t.Id).ActiveMod);
    }

    [Theory, MemberData(nameof(Env.Games), MemberType = typeof(Env))]
    public void A_write_that_fails_puts_back_what_was_written(string game)
    {
        var (m, t, folder) = Setup(game);
        if (m == null) return;
        var kind = Env.Kind(game);
        byte[] lev0 = Env.Read(folder, "DATA/LEV.ARK");
        var a = Env.MakeMod(game, f =>
        {
            Env.EditLevel(f, kind, 1, 100, 1);
            Env.AddSound(f, game, new byte[] { 1, 2, 3 });
        }, "test.fail");
        m.Library.Add(a);
        // the new sound comes after DATA/LEV.ARK, and cannot be written
        Directory.CreateDirectory(new GameDir(folder, kind).PathFor(Env.NewSound(game)) + ".uwmm-tmp");
        Assert.ThrowsAny<Exception>(() => m.UseDos(t, Library.KeyOf(a), false));
        Assert.Equal(lev0, Env.Read(folder, "DATA/LEV.ARK"));
        Assert.Null(t.ActiveMod);
        Assert.Empty(t.Written);
        Assert.False(File.Exists(m.Paths.JournalFile));
        Assert.Empty(DosGame.LocalChanges(t));
    }

    [Theory, MemberData(nameof(Env.Games), MemberType = typeof(Env))]
    public void A_folder_remembers_its_originals_when_the_manager_forgets_it(string game)
    {
        var (m, t, folder) = Setup(game);
        if (m == null) return;
        var kind = Env.Kind(game);
        byte[] lev0 = Env.Read(folder, "DATA/LEV.ARK");
        var a = Env.MakeMod(game, f => Env.EditLevel(f, kind, 1, 100, 1), "test.marker");
        m.Library.Add(a);
        m.UseDos(t, Library.KeyOf(a), false);
        // not forgotten with a mod in place
        Assert.Throws<InvalidOperationException>(() => m.Remove(t));
        // the state is lost: added again, the folder says what its originals are
        File.Delete(m.Paths.StateFile);
        var m2 = new Manager(m.Paths.Root);
        var t2 = m2.AddDos(folder);
        Assert.Equal(Library.KeyOf(a), t2.ActiveMod);
        Assert.Equal(t.Baseline, t2.Baseline);
        Assert.Throws<InvalidOperationException>(() => m2.AddDos(folder));
        m2.UseDos(t2, null, false);
        Assert.Equal(lev0, Env.Read(folder, "DATA/LEV.ARK"));
        // another manager, without these originals, does not take the modded files for originals
        m2.UseDos(t2, Library.KeyOf(a), false);
        var other = new Manager(Env.Temp("mgr"));
        Assert.NotNull(other.ForeignMarker(folder));
        Assert.Throws<InvalidOperationException>(() => other.AddDos(folder));
        Assert.Equal(Files.Sha(Env.Read(folder, "DATA/LEV.ARK")), other.AddDos(folder, null, asItIs: true).Baseline["DATA/LEV.ARK"]);
        // forgotten with the original game back: the marker goes too
        m2.UseDos(t2, null, false);
        m2.Remove(t2);
        Assert.Null(DosGame.ReadMarker(folder));
    }

    [Theory, MemberData(nameof(Env.Games), MemberType = typeof(Env))]
    public void A_file_of_the_player_a_mod_overwrote_comes_back_with_the_original_game(string game)
    {
        var (m, t, folder) = Setup(game);
        if (m == null) return;
        var kind = Env.Kind(game);
        var a = Env.MakeMod(game, f => Env.AddSound(f, game, new byte[] { 9, 9 }), "test.extra");
        m.Library.Add(a);
        // the player put a file of that name there after the manager took the folder over
        string mine = new GameDir(folder, kind).PathFor(Env.NewSound(game));
        File.WriteAllBytes(mine, new byte[] { 1, 2, 3, 4 });
        Assert.Throws<ModException>(() => m.UseDos(t, Library.KeyOf(a), false));
        m.UseDos(t, Library.KeyOf(a), false, overwrite: true);
        Assert.Equal(new byte[] { 9, 9 }, File.ReadAllBytes(mine));
        m.UseDos(t, null, false);
        Assert.Equal(new byte[] { 1, 2, 3, 4 }, File.ReadAllBytes(mine));
        Assert.Empty(t.Displaced);
        // a change to an original overwritten when told to is kept in the vault
        Env.EditString(folder, 4, 3, "my own edit");
        var b = Env.MakeMod(game, f => Env.EditString(f, 4, 2, "the mod's"), "test.strings");
        m.Library.Add(b);
        byte[] edited = Env.Read(folder, "DATA/STRINGS.PAK");
        m.UseDos(t, Library.KeyOf(b), false, overwrite: true);
        var o = Assert.Single(t.Overwritten);
        Assert.Equal("DATA/STRINGS.PAK", o.File);
        Assert.Equal(edited, m.Vault.Get(o.Sha));
    }

    [Theory, MemberData(nameof(Env.Games), MemberType = typeof(Env))]
    public void A_mod_made_from_other_files_or_for_the_other_game_is_refused(string game)
    {
        var (m, t, folder) = Setup(game);
        if (m == null) return;
        string other = game == "UW" ? "UW2" : "UW";
        if (Directory.Exists(Path.Combine(Env.Root, other, "DATA")))
        {
            var o = Env.MakeMod(other, f => Env.EditString(f, 4, 1, "x"), "test.other");
            Assert.Contains(m.Problems(t, o), p => p.Contains("it is for"));
        }
        // made from another STRINGS.PAK: a mod applies only to the exact originals
        string changedBase = Env.Copy(game), modded = Env.Copy(game);
        Env.EditString(changedBase, 4, 1, "changed by someone");
        Env.EditString(modded, 4, 1, "changed by someone");
        Env.EditString(modded, 4, 2, "and by the mod");
        var pkg = Env.Package(game, changedBase, modded, "test.base");
        m.Library.Add(pkg);
        Assert.Contains(m.Problems(t, pkg), p => p.Contains("STRINGS.PAK") && p.Contains("not the file"));
        byte[] before = Env.Read(folder, "DATA/STRINGS.PAK");
        Assert.Throws<ModException>(() => m.UseDos(t, Library.KeyOf(pkg), false));
        Assert.Equal(before, Env.Read(folder, "DATA/STRINGS.PAK"));
        Assert.Empty(t.Written);
        Assert.Null(t.ActiveMod);
    }
}
