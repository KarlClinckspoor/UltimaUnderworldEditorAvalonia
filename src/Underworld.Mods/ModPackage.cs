using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using UltimateEditor.Games;

namespace Underworld.Mods
{
    /// <summary>A mod that does not fit, or a package that breaks the rules.</summary>
    public sealed class ModException : Exception
    {
        public ModException(string message) : base(message) { }
    }

    /// <summary>One changed file: the original it applies to, the result, and the delta between them.</summary>
    public sealed class ModFile
    {
        public string Path;
        /// <summary>The original's hash; null for a file the mod adds (GamePaths.CanAdd), whose delta is made from nothing.</summary>
        public string BaseSha256;
        public string TargetSha256;
        public long BaseSize, TargetSize;
        public byte[] Delta;
        public bool Added { get { return BaseSha256 == null; } }
        public string DeltaPath { get { return "deltas/" + Path + ".uwd"; } }
        public override string ToString() { return Path + (Added ? " (added, " : " (" + BaseSize + " → ") + TargetSize + " bytes, delta " + (Delta?.Length ?? 0) + ")"; }
    }

    public enum FileState
    {
        /// <summary>The original the mod was made from (for a file the mod adds: no file): the mod applies.</summary>
        Original,
        /// <summary>Already the mod's result.</summary>
        Applied,
        /// <summary>Neither: another release, another mod, or changed by hand. The mod does not apply.</summary>
        Different,
        Missing,
    }

    public sealed class FileCheck
    {
        public ModFile File;
        public FileState State;
        public override string ToString() { return File.Path + ": " + State; }
    }

    /// <summary>
    /// A mod, format 2 (docs/mod-format.md): a zip of mod.json and one delta per changed file.
    /// Read packages are untrusted: everything is checked on reading, and again on applying.
    /// </summary>
    public sealed class ModPackage
    {
        public const int Format = 2;
        public const string Extension = ".uwmod";
        /// <summary>Largest entry read from a package (the largest game file is about 1 MB).</summary>
        const long MaxEntry = 64L << 20;

        public string Id = "my.mod", Name = "My mod", Version = "1.0.0", Description = "", MadeWith = "";
        public GameKind Game = GameKind.UW1;
        public readonly List<string> Authors = new List<string>();
        public readonly List<ModFile> Files = new List<ModFile>();

        public string GameId { get { return Game == GameKind.UW2 ? "uw2" : "uw1"; } }
        public string GameName { get { return Game == GameKind.UW2 ? "Ultima Underworld II" : "Ultima Underworld"; } }

        public static string Sha256(byte[] b) { return Convert.ToHexString(SHA256.HashData(b)).ToLowerInvariant(); }

        // ---- making

        /// <summary>
        /// Adds a changed file: its delta is made and checked here. A UW2 archive's compressed
        /// blocks are diffed decompressed. original null: a file the mod adds, which must be one
        /// the game opens by number (GamePaths.CanAdd).
        /// </summary>
        public ModFile Add(string path, byte[] original, byte[] edited)
        {
            string p = GamePaths.Normalize(path);
            if (p == null || !GamePaths.IsGameFile(p, Game)) throw new ModException(path + " is not a file of " + GameName + " that a mod may change.");
            if (original == null && !GamePaths.CanAdd(p, Game)) throw new ModException(p + " is not one of " + GameName + "'s files, and the game would never open it: a mod cannot add it.");
            if (Files.Any(f => f.Path == p)) throw new ModException(p + " is in the mod twice.");
            bool uw2Archive = Game == GameKind.UW2 && p.EndsWith(".ARK");
            var f = new ModFile
            {
                Path = p,
                BaseSha256 = original == null ? null : Sha256(original), BaseSize = original?.Length ?? 0,
                TargetSha256 = Sha256(edited), TargetSize = edited.Length,
                Delta = Delta.Make(original ?? new byte[0], edited, uw2Archive && original != null),
            };
            Files.Add(f);
            Files.Sort((a, b) => string.CompareOrdinal(a.Path, b.Path));
            return f;
        }

        // ---- the package

        public byte[] ToBytes()
        {
            var ms = new MemoryStream();
            using (var zip = new ZipArchive(ms, ZipArchiveMode.Create, true))
            {
                Entry(zip, "mod.json", Encoding.UTF8.GetBytes(Manifest().ToJsonString(new JsonSerializerOptions { WriteIndented = true }) + "\n"));
                foreach (var f in Files) Entry(zip, f.DeltaPath, f.Delta);
            }
            return ms.ToArray();
        }

        public void Write(string path) { File.WriteAllBytes(path, ToBytes()); }

        JsonObject Manifest()
        {
            var authors = new JsonArray();
            foreach (var a in Authors) authors.Add(a);
            var files = new JsonArray();
            foreach (var f in Files)
                files.Add(new JsonObject
                {
                    ["path"] = f.Path,
                    ["base"] = f.Added ? null : new JsonObject { ["sha256"] = f.BaseSha256, ["size"] = f.BaseSize },
                    ["target"] = new JsonObject { ["sha256"] = f.TargetSha256, ["size"] = f.TargetSize },
                    ["delta"] = new JsonObject { ["path"] = f.DeltaPath, ["sha256"] = Sha256(f.Delta) },
                });
            return new JsonObject
            {
                ["format"] = Format,
                ["game"] = GameId,
                ["id"] = Id,
                ["name"] = Name,
                ["version"] = Version,
                ["authors"] = authors,
                ["description"] = Description,
                ["made_with"] = MadeWith,
                ["files"] = files,
            };
        }

        static void Entry(ZipArchive zip, string name, byte[] data)
        {
            var e = zip.CreateEntry(name, CompressionLevel.Optimal);
            // a fixed time, so the same mod gives the same package
            e.LastWriteTime = new DateTimeOffset(1980, 1, 1, 0, 0, 0, TimeSpan.Zero);
            using (var s = e.Open()) s.Write(data, 0, data.Length);
        }

        public static ModPackage Load(string path) { return FromBytes(File.ReadAllBytes(path)); }

        public static ModPackage FromBytes(byte[] bytes)
        {
            ZipArchive zip;
            try { zip = new ZipArchive(new MemoryStream(bytes), ZipArchiveMode.Read); }
            catch (InvalidDataException) { throw new ModException("This is not a mod package (not a zip)."); }
            using (zip)
            {
                byte[] Read(string name)
                {
                    var e = zip.GetEntry(name) ?? throw new ModException("The package lacks " + name + ".");
                    if (e.Length > MaxEntry) throw new ModException(name + " is too large.");
                    using (var s = e.Open())
                    using (var o = new MemoryStream())
                    {
                        var buf = new byte[81920];
                        int n;
                        while ((n = s.Read(buf, 0, buf.Length)) > 0)
                        {
                            if (o.Length + n > MaxEntry) throw new ModException(name + " is too large.");
                            o.Write(buf, 0, n);
                        }
                        return o.ToArray();
                    }
                }

                JsonObject root;
                try { root = JsonNode.Parse(Read("mod.json")) as JsonObject; }
                catch (JsonException e) { throw new ModException("mod.json is not valid JSON: " + e.Message); }
                if (root == null) throw new ModException("mod.json is not a JSON object.");
                int format = Int(root, "format");
                if (format < Format) throw new ModException("This mod was made by an older version (format " + format + "), which this program no longer reads.");
                if (format > Format) throw new ModException("This mod needs a newer version of the program (format " + format + ").");
                var m = new ModPackage
                {
                    Id = Str(root, "id"), Name = Str(root, "name"), Version = Str(root, "version"),
                    Description = Str(root, "description", ""), MadeWith = Str(root, "made_with", ""),
                };
                string game = Str(root, "game");
                if (game == "uw1") m.Game = GameKind.UW1;
                else if (game == "uw2") m.Game = GameKind.UW2;
                else throw new ModException("mod.json names the game \"" + game + "\", not uw1 or uw2.");
                if (root["authors"] is JsonArray au) foreach (var a in au) m.Authors.Add(a?.GetValue<string>() ?? "");
                if (!(root["files"] is JsonArray files)) throw new ModException("mod.json has no list of files.");
                foreach (var n in files)
                {
                    if (!(n is JsonObject o)) throw new ModException("mod.json has a file that is not an object.");
                    string raw = Str(o, "path");
                    string p = GamePaths.Normalize(raw);
                    if (p == null || p != raw || !GamePaths.IsGameFile(p, m.Game))
                        throw new ModException("The mod names " + raw + ", which is not a file of " + m.GameName + " that a mod may change.");
                    if (m.Files.Any(f => f.Path == p)) throw new ModException(p + " is in the mod twice.");
                    if (!o.ContainsKey("base")) throw new ModException(p + " does not say which original it was made from.");
                    bool added = o["base"] == null;
                    if (added && !GamePaths.CanAdd(p, m.Game)) throw new ModException("The mod adds " + p + ", which " + m.GameName + " would never open.");
                    var f = new ModFile
                    {
                        Path = p,
                        BaseSha256 = added ? null : Hex(o["base"] as JsonObject, p), BaseSize = added ? 0 : Size(o["base"] as JsonObject, p),
                        TargetSha256 = Hex(o["target"] as JsonObject, p), TargetSize = Size(o["target"] as JsonObject, p),
                    };
                    var d = o["delta"] as JsonObject ?? throw new ModException(p + " has no delta.");
                    if (Str(d, "path") != f.DeltaPath) throw new ModException(p + "'s delta should be " + f.DeltaPath + ".");
                    f.Delta = Read(f.DeltaPath);
                    if (Sha256(f.Delta) != Hex(d, p)) throw new ModException(p + "'s delta is damaged (its SHA-256 does not match).");
                    m.Files.Add(f);
                }
                if (m.Files.Count == 0) throw new ModException("The mod changes nothing.");
                return m;
            }
        }

        static string Str(JsonObject o, string key, string fallback = null)
        {
            var v = o[key];
            if (v == null) return fallback ?? throw new ModException("mod.json lacks \"" + key + "\".");
            try { return v.GetValue<string>(); }
            catch (Exception) { throw new ModException("mod.json's \"" + key + "\" is not text."); }
        }

        static int Int(JsonObject o, string key)
        {
            try { return o[key]?.GetValue<int>() ?? throw new ModException("mod.json lacks \"" + key + "\"."); }
            catch (Exception e) when (!(e is ModException)) { throw new ModException("mod.json's \"" + key + "\" is not a number."); }
        }

        static string Hex(JsonObject o, string path)
        {
            if (o == null) throw new ModException(path + " lacks a hash.");
            string h = Str(o, "sha256");
            if (h.Length != 64 || h.Any(c => !(c >= '0' && c <= '9' || c >= 'a' && c <= 'f'))) throw new ModException(path + " has a malformed SHA-256.");
            return h;
        }

        static long Size(JsonObject o, string path)
        {
            long s;
            try { s = o["size"]?.GetValue<long>() ?? -1; }
            catch (Exception) { s = -1; }
            if (s < 0 || s > int.MaxValue) throw new ModException(path + " has no valid size.");
            return s;
        }

        // ---- applying

        /// <summary>How each file meets a game. <paramref name="read"/> gives a file's bytes by the mod's path, or null when it is missing.</summary>
        public List<FileCheck> Check(Func<string, byte[]> read)
        {
            var list = new List<FileCheck>();
            foreach (var f in Files)
            {
                byte[] now = read(f.Path);
                FileState s;
                if (now == null) s = f.Added ? FileState.Original : FileState.Missing;
                else
                {
                    string h = Sha256(now);
                    s = h == f.TargetSha256 && now.Length == f.TargetSize ? FileState.Applied
                        : !f.Added && h == f.BaseSha256 && now.Length == f.BaseSize ? FileState.Original
                        : FileState.Different;
                }
                list.Add(new FileCheck { File = f, State = s });
            }
            return list;
        }

        /// <summary>
        /// Every file the mod changes or adds, as the mod makes it, from the originals
        /// (<paramref name="readOriginal"/>: the original's bytes by the mod's path, or null;
        /// null is right for a file the mod adds).
        /// Throws ModException when an original is missing or is not the one the mod was made
        /// from, or a result is not the one the mod promises. Nothing is written here.
        /// </summary>
        public Dictionary<string, byte[]> Build(Func<string, byte[]> readOriginal)
        {
            var result = new Dictionary<string, byte[]>(StringComparer.Ordinal);
            foreach (var f in Files) result[f.Path] = BuildOne(f, readOriginal(f.Path));
            return result;
        }

        public byte[] BuildOne(ModFile f, byte[] original)
        {
            if (f.Added)
            {
                if (original != null) throw new ModException("The game already has a " + f.Path + ", which this mod would add.");
                original = new byte[0];
            }
            else if (original == null) throw new ModException("The game has no " + f.Path + ".");
            else if (original.Length != f.BaseSize || Sha256(original) != f.BaseSha256)
                throw new ModException(f.Path + " is not the file this mod was made from (it comes from another release of the game or another mod, or it has changed since).");
            byte[] edited;
            try { edited = Delta.Apply(original, f.Delta); }
            catch (InvalidDataException e) { throw new ModException(f.Path + ": " + e.Message); }
            if (edited.Length != f.TargetSize || Sha256(edited) != f.TargetSha256)
                throw new ModException(f.Path + ": the result is not the file the mod promises.");
            return edited;
        }

        public string Summary
        {
            get
            {
                var sb = new StringBuilder();
                sb.AppendFormat("{0} {1} ({2}) for {3}", Name, Version, Id, GameName);
                if (Authors.Count > 0) sb.Append(", by " + string.Join(", ", Authors));
                sb.Append('\n');
                if (!string.IsNullOrWhiteSpace(Description)) sb.Append(Description.Trim()).Append('\n');
                foreach (var f in Files) sb.Append("  ").Append(f.Path).Append(f.Added ? ": added, " + f.TargetSize + " bytes\n" : ": " + f.Delta.Length + " bytes of changes\n");
                return sb.ToString();
            }
        }
    }
}
