using System.Reflection;
using UltimateEditor.Games;
using Underworld.Mods;

namespace UwModManager.Core;

/// <summary>
/// UltimaHacks (github.com/JohnGlassmyer/UltimaHacks, MIT licence, John Glassmyer): its compiled
/// hacks for the GOG executables, carried by the manager, and the executable built from the
/// original: the hack if wanted, then the start square a mod moves (docs/mod-format.md, "The
/// executable": a mod's delta is made against GOG's executable, so with the hack only a start
/// square can follow it).
/// </summary>
public static class Hacks
{
    public const string Credit = "UltimaHacks by John Glassmyer (MIT licence): mouse-look and more for the DOS games; github.com/JohnGlassmyer/UltimaHacks";

    public static UltimaHack For(GameKind game)
    {
        using var s = Assembly.GetExecutingAssembly().GetManifestResourceStream(game == GameKind.UW2 ? "hacks/uw2.hack" : "hacks/uw1.hack")
            ?? throw new FileNotFoundException("the UltimaHacks hack is missing from the program");
        using var ms = new MemoryStream();
        s.CopyTo(ms);
        return UltimaHack.Parse(ms.ToArray());
    }

    public static string License()
    {
        using var s = Assembly.GetExecutingAssembly().GetManifestResourceStream("hacks/LICENSE-UltimaHacks.txt");
        return s == null ? "" : new StreamReader(s).ReadToEnd();
    }

    /// <summary>
    /// The executable to run with UltimaHacks: <paramref name="original"/> hacked (it must be the
    /// GOG release's, unless it came hacked already), then <paramref name="start"/>, the start
    /// square a mod moved, written into it.
    /// </summary>
    public static byte[] BuildExe(byte[] original, GameKind game, (int X, int Y)? start)
    {
        var hack = For(game);
        byte[] exe;
        if (hack.IsAppliedTo(original)) exe = (byte[])original.Clone();
        else
        {
            if (!ExeIdentity.IsGogOriginal(original, game))
                throw new ModException(ExeIdentity.ExeName(game) + " is not the GOG release's, which UltimaHacks' hack is made for");
            exe = hack.Apply(original);
        }
        if (start != null && !StartPosition.Patch(exe, game, start.Value.X, start.Value.Y))
            throw new ModException("the start square was not found in the executable UltimaHacks makes");
        return exe;
    }

    /// <summary>
    /// The start square a mod's executable moves, when that is all it changes (null: it does not
    /// change the executable, or does not move the start); throws when it changes more, which
    /// cannot follow UltimaHacks.
    /// </summary>
    public static (int X, int Y)? StartOnly(ModPackage mod, byte[] originalExe, GameKind game)
    {
        var f = mod.Files.FirstOrDefault(x => x.Path == ExeIdentity.ExeName(game));
        if (f == null) return null;
        byte[] modded = mod.BuildOne(f, originalExe);
        if (!ExeChanges.OnlyStart(originalExe, modded, game))
            throw new ModException("the mod changes " + f.Path + " beyond the start square, which cannot be combined with UltimaHacks");
        return ExeChanges.MovedStart(originalExe, modded, game, out int x, out int y) ? (x, y) : null;
    }
}
