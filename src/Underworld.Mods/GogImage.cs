using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using UltimateEditor.Games;

namespace Underworld.Mods
{
    /// <summary>
    /// GOG's game.gog: the CD of both games ("UW12"), as an ISO 9660 image, which GOG's DOSBox
    /// mounts as drive D: (imgmount d "..\game.gog" -t iso). The 2025 release puts the same image
    /// in both installs, beside a small UNDEROM1 or UNDEROM2 folder (the CD version's files on the
    /// hard disk: the sound setup and some cutscenes). The disc holds UW\ (UW.EXE, DATA, CRIT,
    /// CUTS, SOUND) and UW2\, each a whole game folder, the same files as the earlier release that
    /// installed the games as plain files. A zip with the same folders is read as well.
    /// </summary>
    public static class GogImage
    {
        public const string FileName = "game.gog";

        /// <summary>SHA-256 of the images this code was checked against: GOG's 2025 release of
        /// Ultima Underworld 1+2 (the same 33,867,776-byte image in both installs).</summary>
        public static readonly IReadOnlyDictionary<string, string> Known = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["846573e2e676647511206c419d23f584a2898bddb30b69603cc30bb859779ded"] = "GOG's Ultima Underworld 1+2 (2025)",
        };

        /// <summary>The image's SHA-256, lower-case hex.</summary>
        public static string Sha256(string image)
        {
            using (var f = File.OpenRead(image))
            using (var sha = SHA256.Create())
                return Convert.ToHexString(sha.ComputeHash(f)).ToLowerInvariant();
        }

        /// <summary>Which release an image is, or null when its hash is not one of <see cref="Known"/>.</summary>
        public static string Release(string sha256)
        {
            return sha256 != null && Known.TryGetValue(sha256, out var r) ? r : null;
        }

        /// <summary>The disc's folder of a game: UW or UW2.</summary>
        public static string FolderOf(GameKind game) { return game == GameKind.UW2 ? "UW2" : "UW"; }

        /// <summary>The install's hard-disk folder of a game: UNDEROM1 or UNDEROM2.</summary>
        public static string HardDiskFolderOf(GameKind game) { return game == GameKind.UW2 ? "UNDEROM2" : "UNDEROM1"; }

        /// <summary>The note the editor leaves in the folder holding its copies (UW, UW2): the GOG install they came from, whose DOSBox and settings play them.</summary>
        public const string InstallNote = "GOG install.txt";

        public static void WriteInstallNote(string copiesFolder, string install)
        {
            File.WriteAllText(Path.Combine(copiesFolder, InstallNote),
                Path.GetFullPath(install) + "\n\nThe GOG install these games were copied from (its game.gog). The editor runs them with its DOSBox and settings; the install itself is not changed.\n");
        }

        /// <summary>The GOG install a copy came from (the note in its folder or the one above), when it is still there; else null.</summary>
        public static string InstallOf(string gameFolder)
        {
            if (string.IsNullOrEmpty(gameFolder)) return null;
            string full = Path.GetFullPath(gameFolder).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
            foreach (var d in new[] { full, Path.GetDirectoryName(full) })
            {
                string note = d == null ? null : Path.Combine(d, InstallNote);
                if (note == null || !File.Exists(note)) continue;
                try
                {
                    string first = File.ReadLines(note).FirstOrDefault()?.Trim();
                    return !string.IsNullOrEmpty(first) && Directory.Exists(first) ? first : null;
                }
                catch (IOException) { return null; }
            }
            return null;
        }

        /// <summary>game.gog in a folder (any letter case), or null.</summary>
        public static string Find(string folder)
        {
            if (string.IsNullOrEmpty(folder) || !Directory.Exists(folder)) return null;
            return Directory.EnumerateFiles(folder).FirstOrDefault(f => string.Equals(Path.GetFileName(f), FileName, StringComparison.OrdinalIgnoreCase));
        }

        /// <summary>
        /// The game a GOG install is for, from its own files: UNDEROM1 or dosboxULTIMA1.conf for
        /// the first game, UNDEROM2 or dosboxULTIMA2.conf for the second. Null when it says
        /// neither, or both (the image always holds both games).
        /// </summary>
        public static GameKind? GameOfInstall(string folder)
        {
            if (string.IsNullOrEmpty(folder) || !Directory.Exists(folder)) return null;
            bool one = false, two = false;
            foreach (var p in Directory.EnumerateFileSystemEntries(folder))
            {
                string n = Path.GetFileName(p).ToUpperInvariant();
                if (n == "UNDEROM1" || n == "DOSBOXULTIMA1.CONF") one = true;
                if (n == "UNDEROM2" || n == "DOSBOXULTIMA2.CONF") two = true;
            }
            if (one == two) return null;
            return one ? GameKind.UW1 : GameKind.UW2;
        }

        /// <summary>The games the image holds (a folder with the game's executable).</summary>
        public static List<GameKind> Games(string image)
        {
            using (var img = Open(image))
            {
                var r = new List<GameKind>();
                foreach (var g in new[] { GameKind.UW1, GameKind.UW2 })
                    if (img.Entries.Any(e => e.Path == FolderOf(g) + "/" + GamePaths.ExeName(g))) r.Add(g);
                return r;
            }
        }

        /// <summary>
        /// Copies a game out of the image into <paramref name="dest"/> (made; it must not exist or
        /// be empty): every file of the disc's UW or UW2 folder, then the install's UNDEROM1 or
        /// UNDEROM2 files (the CD version reads its sound setup from there), so the copy plays as
        /// GOG's does. The files land in a temporary folder beside it first, renamed into place
        /// when all are written. Returns the number of files.
        /// </summary>
        public static int Extract(string image, GameKind game, string dest, string install = null, Action<int, int> progress = null)
        {
            dest = Path.GetFullPath(dest);
            if (Directory.Exists(dest) && Directory.EnumerateFileSystemEntries(dest).Any())
                throw new IOException(dest + " is not empty");
            string parent = Path.GetDirectoryName(dest.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar));
            Directory.CreateDirectory(parent);
            string temp = Path.Combine(parent, "." + Path.GetFileName(dest.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar)) + ".part-" + Guid.NewGuid().ToString("N").Substring(0, 8));
            int count = 0;
            try
            {
                using (var img = Open(image))
                {
                    string prefix = FolderOf(game) + "/";
                    var files = img.Entries.Where(e => e.Path.StartsWith(prefix, StringComparison.Ordinal) && e.Path.Length > prefix.Length).ToList();
                    if (!files.Any(e => e.Path == prefix + GamePaths.ExeName(game)))
                        throw new InvalidDataException(Path.GetFileName(image) + " holds no " + prefix + GamePaths.ExeName(game) + ": it is not GOG's image of the games");
                    foreach (var e in files)
                    {
                        string path = SafeJoin(temp, e.Path.Substring(prefix.Length));
                        Directory.CreateDirectory(Path.GetDirectoryName(path));
                        using (var o = File.Create(path)) img.CopyTo(e, o);
                        count++;
                        progress?.Invoke(count, files.Count);
                    }
                }
                string hd = install == null ? null : FindDir(install, HardDiskFolderOf(game));
                if (hd != null)
                    foreach (var f in Directory.EnumerateFiles(hd, "*", SearchOption.AllDirectories))
                    {
                        string rel = Path.GetRelativePath(hd, f);
                        if (string.Equals(rel, "MARKER.FIL", StringComparison.OrdinalIgnoreCase)) continue;
                        // into the folder of the same name the disc made, whatever its case
                        string path = SafeJoin(temp, MatchCase(temp, rel));
                        Directory.CreateDirectory(Path.GetDirectoryName(path));
                        File.Copy(f, path, true);
                    }
                if (Directory.Exists(dest)) Directory.Delete(dest);
                Directory.Move(temp, dest);
                return count;
            }
            catch
            {
                try { if (Directory.Exists(temp)) Directory.Delete(temp, true); } catch (IOException) { } catch (UnauthorizedAccessException) { }
                throw;
            }
        }

        static string FindDir(string folder, string name)
        {
            if (!Directory.Exists(folder)) return null;
            return Directory.EnumerateDirectories(folder).FirstOrDefault(d => string.Equals(Path.GetFileName(d), name, StringComparison.OrdinalIgnoreCase));
        }

        /// <summary>A relative path with each folder spelt as it already is under <paramref name="root"/>.</summary>
        static string MatchCase(string root, string rel)
        {
            var parts = rel.Replace('\\', '/').Split('/');
            string cur = root;
            for (int i = 0; i < parts.Length - 1; i++)
            {
                string found = FindDir(cur, parts[i]);
                if (found != null) parts[i] = Path.GetFileName(found);
                cur = Path.Combine(cur, parts[i]);
            }
            return string.Join("/", parts);
        }

        /// <summary>A path inside <paramref name="root"/>, refusing names that would lead out of it.</summary>
        static string SafeJoin(string root, string rel)
        {
            var parts = rel.Replace('\\', '/').Split('/');
            foreach (var p in parts)
                if (p.Length == 0 || p == "." || p == ".." || p.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0)
                    throw new InvalidDataException("the image names a file that cannot be written: " + rel);
            string full = Path.GetFullPath(Path.Combine(root, Path.Combine(parts)));
            string r = Path.GetFullPath(root).TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar;
            if (!full.StartsWith(r, StringComparison.OrdinalIgnoreCase)) throw new InvalidDataException("the image names a file outside its folder: " + rel);
            return full;
        }

        // ---- reading the image

        /// <summary>A file of the image: its path (upper case, '/'), where it is and how long.</summary>
        public sealed class Entry
        {
            public string Path { get; set; }
            public long Offset { get; set; }
            public long Length { get; set; }
            internal ZipArchiveEntry Zip { get; set; }
        }

        /// <summary>An opened image: its files, and their contents.</summary>
        public sealed class Reader : IDisposable
        {
            readonly Stream stream;
            readonly ZipArchive zip;
            public List<Entry> Entries { get; } = new List<Entry>();

            internal Reader(Stream s)
            {
                stream = s;
                var head = new byte[4];
                s.Position = 0;
                int n = s.Read(head, 0, 4);
                if (n == 4 && head[0] == 'P' && head[1] == 'K')
                {
                    s.Position = 0;
                    zip = new ZipArchive(s, ZipArchiveMode.Read, true);
                    foreach (var e in zip.Entries)
                        if (!e.FullName.EndsWith("/") && e.FullName.Length > 0)
                            Entries.Add(new Entry { Path = e.FullName.Replace('\\', '/').TrimStart('/').ToUpperInvariant(), Length = e.Length, Zip = e });
                    // a zip may put the disc in a folder of its own: the paths start at the one holding UW or UW2
                    string root = Entries.Select(e => e.Path).Where(p => p.EndsWith("UW/UW.EXE") || p.EndsWith("UW2/UW2.EXE"))
                        .Select(p => p.Substring(0, p.LastIndexOf('/', p.LastIndexOf('/') - 1) + 1)).OrderBy(p => p.Length).FirstOrDefault();
                    if (!string.IsNullOrEmpty(root))
                        foreach (var e in Entries) if (e.Path.StartsWith(root, StringComparison.Ordinal)) e.Path = e.Path.Substring(root.Length);
                }
                else ReadIso();
            }

            public void CopyTo(Entry e, Stream to)
            {
                if (e.Zip != null) { using (var z = e.Zip.Open()) z.CopyTo(to); return; }
                stream.Position = e.Offset;
                var buf = new byte[81920];
                long left = e.Length;
                while (left > 0)
                {
                    int n = stream.Read(buf, 0, (int)Math.Min(buf.Length, left));
                    if (n <= 0) throw new EndOfStreamException("the image ends inside " + e.Path);
                    to.Write(buf, 0, n);
                    left -= n;
                }
            }

            public byte[] Read(Entry e)
            {
                using (var m = new MemoryStream()) { CopyTo(e, m); return m.ToArray(); }
            }

            const int Sector = 2048;

            void ReadIso()
            {
                // the volume descriptors start at sector 16; the primary one (type 1) holds the root directory's record at 156
                for (int s = 16; s < 64; s++)
                {
                    byte[] vd = ReadAt((long)s * Sector, Sector);
                    if (vd == null || Encoding.ASCII.GetString(vd, 1, 5) != "CD001") break;
                    if (vd[0] == 255) break;
                    if (vd[0] != 1) continue;
                    long lba = U32(vd, 156 + 2), size = U32(vd, 156 + 10);
                    Walk(lba, size, "", 0, new HashSet<long>());
                    return;
                }
                throw new InvalidDataException("this is neither a CD image (ISO 9660) nor a zip");
            }

            void Walk(long lba, long size, string prefix, int depth, HashSet<long> seen)
            {
                if (depth > 16 || !seen.Add(lba) || size <= 0 || size > 16 << 20) return;
                byte[] d = ReadAt(lba * Sector, (int)size);
                if (d == null) throw new InvalidDataException("the image is cut short (a folder lies past its end)");
                int pos = 0;
                while (pos < d.Length)
                {
                    int len = d[pos];
                    if (len == 0) { pos = (pos / Sector + 1) * Sector; continue; }     // records do not cross sectors
                    if (pos + 33 > d.Length || pos + len > d.Length) break;
                    long ext = U32(d, pos + 2), dlen = U32(d, pos + 10);
                    byte flags = d[pos + 25];
                    int nl = d[pos + 32];
                    if (33 + nl > len) break;
                    bool self = nl == 1 && (d[pos + 33] == 0 || d[pos + 33] == 1);
                    if (!self)
                    {
                        string name = Encoding.ASCII.GetString(d, pos + 33, nl);
                        int semi = name.IndexOf(';');
                        if (semi >= 0) name = name.Substring(0, semi);
                        name = name.TrimEnd('.').ToUpperInvariant();
                        if (name.Length > 0)
                        {
                            if ((flags & 2) != 0) Walk(ext, dlen, prefix + name + "/", depth + 1, seen);
                            else if (Entries.Count < 100000) Entries.Add(new Entry { Path = prefix + name, Offset = ext * Sector, Length = dlen });
                        }
                    }
                    pos += len;
                }
            }

            byte[] ReadAt(long offset, int count)
            {
                if (offset < 0 || offset + count > stream.Length) return null;
                stream.Position = offset;
                var b = new byte[count];
                int got = 0;
                while (got < count) { int n = stream.Read(b, got, count - got); if (n <= 0) return null; got += n; }
                return b;
            }

            static long U32(byte[] b, int i) { return (uint)(b[i] | b[i + 1] << 8 | b[i + 2] << 16 | b[i + 3] << 24); }

            public void Dispose()
            {
                zip?.Dispose();
                stream.Dispose();
            }
        }

        public static Reader Open(string image)
        {
            var s = File.OpenRead(image);
            try { return new Reader(s); }
            catch { s.Dispose(); throw; }
        }
    }
}
