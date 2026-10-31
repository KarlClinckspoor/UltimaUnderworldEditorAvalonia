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
using UwEditor.Core.Conversations;

namespace UwEditor.Core.Mods
{
    /// <summary>What a mod changes, at the finest level of each file's kind.</summary>
    public sealed class ModComponent
    {
        /// <summary>"ark-blocks", "strings" or "file".</summary>
        public string Kind;
        /// <summary>The file, relative to the game folder, with '/' (DATA/LEV.ARK).</summary>
        public string File;
        /// <summary>ark-blocks: block index → (hash of the block the mod was made from, or "absent"; the new bytes, or null to remove it).</summary>
        public readonly List<BlockChange> Blocks = new List<BlockChange>();
        /// <summary>strings: the strings changed or added.</summary>
        public readonly List<StringChange> Strings = new List<StringChange>();
        /// <summary>file: the whole new file, and the hash of the one it replaces ("new" for a new file).</summary>
        public byte[] Data;
        public string Base;
        /// <summary>For "bytes": the runs of bytes changed in place (an executable's patched start square).</summary>
        public readonly List<BytePatch> Patches = new List<BytePatch>();
        public override string ToString()
        {
            if (Kind == "ark-blocks") return File + ": " + Blocks.Count + " block" + (Blocks.Count == 1 ? "" : "s") + " (" + string.Join(", ", Blocks.Select(b => b.Index)) + ")";
            if (Kind == "strings") return File + ": " + Strings.Count + " string" + (Strings.Count == 1 ? "" : "s") + " in block" + (Strings.Select(s => s.Block).Distinct().Count() == 1 ? " " : "s ") + string.Join(", ", Strings.Select(s => "0x" + s.Block.ToString("X")).Distinct());
            if (Kind == "bytes") return File + ": " + Patches.Sum(p => p.New.Length) + " byte(s) patched in " + Patches.Count + " place(s)";
            return File + ": the whole file" + (Base == "new" ? " (new)" : "");
        }
    }

    public sealed class BlockChange
    {
        public int Index;
        public string Base;
        public byte[] Data;
    }

    /// <summary>Bytes changed in place at an offset: what was there, what goes there.</summary>
    public sealed class BytePatch
    {
        public int Offset;
        public byte[] Old, New;
    }

    public sealed class StringChange
    {
        public int Block, Index;
        /// <summary>The text it replaces; null for a string the mod adds.</summary>
        public string Old;
        public string Text;
    }

    public enum ComponentState { Applicable, AlreadyApplied, Conflict, MissingFile }

    public sealed class ComponentCheck
    {
        public ModComponent Component;
        public ComponentState State;
        public string Detail;
        public override string ToString() { return Component + ": " + State + (string.IsNullOrEmpty(Detail) ? "" : " (" + Detail + ")"); }
    }

    /// <summary>
    /// A mod: a zip (.uwmod) holding mod.json and its payload, the changes it makes to the game's
    /// files, each with the hash of what it was made from. Following the mod manager's plan
    /// (project doc mod-manager-plan.md section 3): archive blocks for .ARK files, strings for
    /// STRINGS.PAK, whole files for the rest. Applying checks every reference first: a file
    /// whose hash is the mod's base takes it whole; otherwise each block or string is checked on
    /// its own, so a mod still applies where the parts it touches are as it expects.
    /// </summary>
    public sealed class ModPackage
    {
        public string Id = "my.mod", Name = "My mod", Version = "1.0.0", Description = "", Game = "uw1";
        public List<string> Authors = new List<string>();
        public string MadeWith = "UwEditorFree";
        /// <summary>The hash of each file the mod changes, as it was when the mod was made (the reference).</summary>
        public readonly Dictionary<string, string> BaseFiles = new Dictionary<string, string>();
        /// <summary>The hash of each file after the mod (to recognise it already applied).</summary>
        public readonly Dictionary<string, string> TargetFiles = new Dictionary<string, string>();
        public readonly List<ModComponent> Components = new List<ModComponent>();

        public static string Hash(byte[] b)
        {
            using (var sha = SHA256.Create()) return "sha256:" + BitConverter.ToString(sha.ComputeHash(b)).Replace("-", "").ToLowerInvariant();
        }

        // ---- making a mod

        /// <summary>
        /// Compares a game folder with its reference and collects the changes. reference: a folder
        /// with the original files at the same paths, or null to use the NAME.EXT.ORIG copies the
        /// editor keeps beside each file it has written. SAVE folders are left out (a saved game is
        /// not a mod), and so are the editor's .BKP, .ORIG and temporary files.
        /// </summary>
        public static ModPackage FromChanges(string gameFolder, GameKind kind, string reference = null)
        {
            var m = new ModPackage { Game = kind == GameKind.UW2 ? "uw2" : "uw1" };
            foreach (var path in Directory.GetFiles(gameFolder, "*", SearchOption.AllDirectories).OrderBy(p => p, StringComparer.OrdinalIgnoreCase))
            {
                string rel = Rel(gameFolder, path);
                if (Skip(rel)) continue;
                string orig = reference != null ? FindCaseless(reference, rel) : SafeFile.OriginalOf(path);
                byte[] now = File.ReadAllBytes(path);
                if (orig == null)
                {
                    if (reference != null) m.AddWhole(rel, null, now);
                    continue;
                }
                byte[] old = File.ReadAllBytes(orig);
                if (old.SequenceEqual(now)) continue;
                m.AddDiff(rel, old, now, kind);
            }
            return m;
        }

        static bool Skip(string rel)
        {
            string up = rel.ToUpperInvariant();
            if (up.EndsWith(".BKP") || up.EndsWith(SafeFile.OriginalSuffix) || up.EndsWith(".TMP") || up.EndsWith(".UWMOD")) return true;
            var first = up.Split('/')[0];
            if (first.Length == 5 && first.StartsWith("SAVE") && char.IsDigit(first[4])) return true;
            return false;
        }

        static string Rel(string root, string path) { return Path.GetRelativePath(root, path).Replace('\\', '/'); }

        /// <summary>A file at a relative path under root, whatever the case of its parts.</summary>
        public static string FindCaseless(string root, string rel)
        {
            string cur = root;
            foreach (var part in rel.Split('/'))
            {
                if (!Directory.Exists(cur)) return null;
                string next = Directory.GetFileSystemEntries(cur).FirstOrDefault(e => string.Equals(Path.GetFileName(e), part, StringComparison.OrdinalIgnoreCase));
                if (next == null) return null;
                cur = next;
            }
            return File.Exists(cur) ? cur : null;
        }

        void AddDiff(string rel, byte[] old, byte[] now, GameKind kind)
        {
            BaseFiles[rel] = Hash(old);
            TargetFiles[rel] = Hash(now);
            string name = Path.GetFileName(rel).ToUpperInvariant();
            if (name.EndsWith(".ARK"))
            {
                try
                {
                    var a = Archive.Open(old, kind);
                    var b = Archive.Open(now, kind);
                    var c = new ModComponent { Kind = "ark-blocks", File = rel };
                    int n = Math.Max(a.Count, b.Count);
                    if (a.Count == b.Count)
                    {
                        for (int i = 0; i < n; i++)
                        {
                            byte[] ob = a.Has(i) ? a.Read(i) : null, nb = b.Has(i) ? b.Read(i) : null;
                            if (ob == null && nb == null) continue;
                            if (ob != null && nb != null && ob.SequenceEqual(nb)) continue;
                            c.Blocks.Add(new BlockChange { Index = i, Base = ob == null ? "absent" : Hash(ob), Data = nb });
                        }
                        Components.Add(c);
                        return;
                    }
                }
                catch (Exception) { }
            }
            if (name == "STRINGS.PAK")
            {
                try
                {
                    var a = StringsPakFile.FromBytes(old);
                    var b = StringsPakFile.FromBytes(now);
                    var c = new ModComponent { Kind = "strings", File = rel };
                    bool shrank = false;
                    foreach (int blk in b.Blocks.Union(a.Blocks).Distinct().OrderBy(x => x))
                    {
                        var os = a.Get(blk); var ns = b.Get(blk);
                        if (ns.Count < os.Count) shrank = true;
                        for (int i = 0; i < ns.Count; i++)
                            if (i >= os.Count || os[i] != ns[i]) c.Strings.Add(new StringChange { Block = blk, Index = i, Old = i < os.Count ? os[i] : null, Text = ns[i] });
                    }
                    if (!shrank) { Components.Add(c); return; }
                }
                catch (Exception) { }
            }
            if (old.Length == now.Length)
            {
                // a file changed in place (an executable patch): only the changed bytes, never the
                // whole program, so the mod carries nothing of the original
                var c = new ModComponent { Kind = "bytes", File = rel };
                int total = 0;
                for (int i = 0; i < old.Length; i++)
                {
                    if (old[i] == now[i]) continue;
                    int start = i;
                    while (i < old.Length && (old[i] != now[i] || (i + 1 < old.Length && old[i + 1] != now[i + 1] && i - start < 64))) i++;
                    int len = i - start;
                    c.Patches.Add(new BytePatch { Offset = start, Old = old.Skip(start).Take(len).ToArray(), New = now.Skip(start).Take(len).ToArray() });
                    total += len;
                }
                if (total <= 4096) { Components.Add(c); return; }
            }
            AddWhole(rel, old, now);
        }

        void AddWhole(string rel, byte[] old, byte[] now)
        {
            if (old == null) BaseFiles[rel] = "new";
            TargetFiles[rel] = Hash(now);
            Components.Add(new ModComponent { Kind = "file", File = rel, Base = old == null ? "new" : Hash(old), Data = now });
        }

        // ---- the package

        public void Write(string path)
        {
            using (var fs = new FileStream(path, FileMode.Create))
            using (var zip = new ZipArchive(fs, ZipArchiveMode.Create))
            {
                var comps = new JsonArray();
                foreach (var c in Components)
                {
                    var j = new JsonObject { ["kind"] = c.Kind, ["file"] = c.File };
                    string dir = "payload/" + c.File.Replace('/', '_');
                    if (c.Kind == "ark-blocks")
                    {
                        var arr = new JsonArray();
                        foreach (var b in c.Blocks)
                        {
                            var bj = new JsonObject { ["index"] = b.Index, ["base"] = b.Base };
                            if (b.Data == null) bj["delete"] = true;
                            else { string p = dir + "/" + b.Index.ToString("D4") + ".bin"; Entry(zip, p, b.Data); bj["data"] = p; bj["hash"] = Hash(b.Data); }
                            arr.Add(bj);
                        }
                        j["blocks"] = arr;
                    }
                    else if (c.Kind == "strings")
                    {
                        var arr = new JsonArray();
                        foreach (var s in c.Strings)
                            arr.Add(new JsonObject { ["block"] = s.Block, ["index"] = s.Index, ["old"] = s.Old, ["text"] = s.Text });
                        string p = dir + "/strings.json";
                        Entry(zip, p, Encoding.UTF8.GetBytes(arr.ToJsonString(new JsonSerializerOptions { WriteIndented = true })));
                        j["data"] = p;
                    }
                    else if (c.Kind == "bytes")
                    {
                        var arr = new JsonArray();
                        foreach (var b in c.Patches)
                            arr.Add(new JsonObject { ["offset"] = b.Offset, ["old"] = Convert.ToHexString(b.Old), ["new"] = Convert.ToHexString(b.New) });
                        j["patches"] = arr;
                    }
                    else
                    {
                        string p = "payload/files/" + c.File;
                        Entry(zip, p, c.Data);
                        j["data"] = p; j["base"] = c.Base; j["hash"] = Hash(c.Data);
                    }
                    comps.Add(j);
                }
                var baseFiles = new JsonObject();
                foreach (var kv in BaseFiles.OrderBy(k => k.Key)) baseFiles[kv.Key] = kv.Value;
                var targets = new JsonObject();
                foreach (var kv in TargetFiles.OrderBy(k => k.Key)) targets[kv.Key] = kv.Value;
                var authors = new JsonArray();
                foreach (var a in Authors) authors.Add(a);
                var root = new JsonObject
                {
                    ["format"] = 1, ["id"] = Id, ["name"] = Name, ["version"] = Version, ["game"] = Game,
                    ["authors"] = authors, ["description"] = Description, ["made_with"] = MadeWith,
                    ["base"] = new JsonObject { ["files"] = baseFiles },
                    ["targets"] = new JsonObject { ["files"] = targets },
                    ["components"] = comps,
                    ["flags"] = new JsonObject
                    {
                        ["new_game"] = Components.Any(c => c.File.EndsWith("LEV.ARK", StringComparison.OrdinalIgnoreCase) || c.File.EndsWith("SCD.ARK", StringComparison.OrdinalIgnoreCase) || c.File.EndsWith("BABGLOBS.DAT", StringComparison.OrdinalIgnoreCase)),
                        ["changes_saves"] = Components.Any(c => !(c.File.EndsWith("LEV.ARK", StringComparison.OrdinalIgnoreCase) || c.File.EndsWith("SCD.ARK", StringComparison.OrdinalIgnoreCase))),
                    },
                };
                Entry(zip, "mod.json", Encoding.UTF8.GetBytes(root.ToJsonString(new JsonSerializerOptions { WriteIndented = true })));
            }
        }

        static void Entry(ZipArchive zip, string name, byte[] b)
        {
            var e = zip.CreateEntry(name, CompressionLevel.Optimal);
            using (var s = e.Open()) s.Write(b, 0, b.Length);
        }

        public static ModPackage Read(string path)
        {
            using (var zip = ZipFile.OpenRead(path))
            {
                Func<string, byte[]> read = n =>
                {
                    var e = zip.GetEntry(n) ?? throw new InvalidDataException("The mod lacks " + n + ".");
                    using (var s = e.Open()) using (var ms = new MemoryStream()) { s.CopyTo(ms); return ms.ToArray(); }
                };
                var root = JsonNode.Parse(Encoding.UTF8.GetString(read("mod.json"))) as JsonObject ?? throw new InvalidDataException("mod.json is not an object.");
                if ((int?)root["format"] != 1) throw new InvalidDataException("This mod's format (" + root["format"] + ") is not 1.");
                var m = new ModPackage
                {
                    Id = (string)root["id"] ?? "", Name = (string)root["name"] ?? "", Version = (string)root["version"] ?? "",
                    Game = (string)root["game"] ?? "", Description = (string)root["description"] ?? "", MadeWith = (string)root["made_with"] ?? "",
                };
                if (root["authors"] is JsonArray au) foreach (var a in au) m.Authors.Add((string)a);
                if (root["base"]?["files"] is JsonObject bf) foreach (var kv in bf) m.BaseFiles[kv.Key] = (string)kv.Value;
                if (root["targets"]?["files"] is JsonObject tf) foreach (var kv in tf) m.TargetFiles[kv.Key] = (string)kv.Value;
                foreach (var n in (root["components"] as JsonArray) ?? new JsonArray())
                {
                    var c = new ModComponent { Kind = (string)n["kind"], File = (string)n["file"] };
                    if (c.File == null || c.File.Contains("..") || Path.IsPathRooted(c.File)) throw new InvalidDataException("A component names an unsafe path: " + c.File);
                    if (c.Kind == "ark-blocks")
                        foreach (var b in (n["blocks"] as JsonArray) ?? new JsonArray())
                            c.Blocks.Add(new BlockChange { Index = (int)b["index"], Base = (string)b["base"], Data = b["delete"] != null && (bool)b["delete"] ? null : read((string)b["data"]) });
                    else if (c.Kind == "strings")
                    {
                        var arr = JsonNode.Parse(Encoding.UTF8.GetString(read((string)n["data"]))) as JsonArray;
                        foreach (var s in arr ?? new JsonArray())
                            c.Strings.Add(new StringChange { Block = (int)s["block"], Index = (int)s["index"], Old = (string)s["old"], Text = (string)s["text"] });
                    }
                    else if (c.Kind == "file") { c.Data = read((string)n["data"]); c.Base = (string)n["base"]; }
                    else if (c.Kind == "bytes")
                        foreach (var b in (n["patches"] as JsonArray) ?? new JsonArray())
                            c.Patches.Add(new BytePatch { Offset = (int)b["offset"], Old = Convert.FromHexString((string)b["old"]), New = Convert.FromHexString((string)b["new"]) });
                    else throw new InvalidDataException("Unknown component kind " + c.Kind + ".");
                    m.Components.Add(c);
                }
                return m;
            }
        }

        // ---- applying

        /// <summary>How each component meets the game folder: as made from, already applied, or changed since.</summary>
        public List<ComponentCheck> Check(string gameFolder, GameKind kind)
        {
            var list = new List<ComponentCheck>();
            foreach (var c in Components) list.Add(CheckOne(gameFolder, kind, c));
            return list;
        }

        ComponentCheck CheckOne(string gameFolder, GameKind kind, ModComponent c)
        {
            var r = new ComponentCheck { Component = c };
            string path = FindCaseless(gameFolder, c.File);
            string bas; BaseFiles.TryGetValue(c.File, out bas);
            string target; TargetFiles.TryGetValue(c.File, out target);
            if (path == null)
            {
                r.State = c.Kind == "file" && c.Base == "new" ? ComponentState.Applicable : ComponentState.MissingFile;
                r.Detail = r.State == ComponentState.MissingFile ? "the game has no " + c.File : "a new file";
                return r;
            }
            byte[] now = File.ReadAllBytes(path);
            string h = Hash(now);
            if (target != null && h == target) { r.State = ComponentState.AlreadyApplied; return r; }
            if (bas != null && h == bas) { r.State = ComponentState.Applicable; r.Detail = "the file is the one the mod was made from"; return r; }
            if (c.Kind == "file") { r.State = ComponentState.Conflict; r.Detail = "the file differs from the one the mod was made from"; return r; }
            if (c.Kind == "bytes")
            {
                int pOk = 0, pDone = 0, pBad = 0;
                foreach (var b in c.Patches)
                {
                    if (b.Offset + b.Old.Length > now.Length) { pBad++; continue; }
                    var cur = now.Skip(b.Offset).Take(b.Old.Length).ToArray();
                    if (cur.SequenceEqual(b.Old)) pOk++; else if (cur.SequenceEqual(b.New)) pDone++; else pBad++;
                }
                r.State = pBad > 0 ? ComponentState.Conflict : pOk == 0 ? ComponentState.AlreadyApplied : ComponentState.Applicable;
                r.Detail = pBad > 0 ? pBad + " patched place(s) hold other bytes (another version of the file?)" : "every patched place is as the mod expects";
                return r;
            }
            if (c.Kind == "ark-blocks")
            {
                Archive a;
                try { a = Archive.Open(now, kind); } catch (Exception ex) { r.State = ComponentState.Conflict; r.Detail = ex.Message; return r; }
                int ok = 0, done = 0; var bad = new List<int>();
                foreach (var b in c.Blocks)
                {
                    byte[] cur = a.Has(b.Index) ? a.Read(b.Index) : null;
                    string ch = cur == null ? "absent" : Hash(cur);
                    if (ch == b.Base) ok++;
                    else if ((b.Data == null && cur == null) || (b.Data != null && cur != null && Hash(b.Data) == ch)) done++;
                    else bad.Add(b.Index);
                }
                r.State = bad.Count > 0 ? ComponentState.Conflict : ok == 0 ? ComponentState.AlreadyApplied : ComponentState.Applicable;
                r.Detail = bad.Count > 0 ? "blocks changed since the mod was made: " + string.Join(", ", bad) : "every block it changes is as the mod expects";
                return r;
            }
            // strings
            StringsPakFile pak;
            try { pak = StringsPakFile.FromBytes(now); } catch (Exception ex) { r.State = ComponentState.Conflict; r.Detail = ex.Message; return r; }
            int sOk = 0, sDone = 0, sBad = 0;
            foreach (var s in c.Strings)
            {
                var blk = pak.Get(s.Block);
                string cur = s.Index < blk.Count ? blk[s.Index] : null;
                if (cur == s.Old) sOk++;
                else if (cur == s.Text) sDone++;
                else sBad++;
            }
            r.State = sBad > 0 ? ComponentState.Conflict : sOk == 0 ? ComponentState.AlreadyApplied : ComponentState.Applicable;
            r.Detail = sBad > 0 ? sBad + " string(s) changed since the mod was made" : "every string it changes is as the mod expects";
            return r;
        }

        /// <summary>
        /// Applies the mod to a game folder: every component must be applicable or already applied
        /// (unless force, which writes over conflicts). Each file is written safely (.BKP, .ORIG).
        /// Returns the files written.
        /// </summary>
        public List<string> Apply(string gameFolder, GameKind kind, bool force = false)
        {
            var checks = Check(gameFolder, kind);
            var bad = checks.Where(c => c.State == ComponentState.Conflict || c.State == ComponentState.MissingFile).ToList();
            if (bad.Count > 0 && !force) throw new InvalidOperationException("The mod does not fit this game: " + string.Join("; ", bad.Select(b => b.ToString())));
            var written = new List<string>();
            foreach (var group in checks.Where(c => c.State != ComponentState.AlreadyApplied && c.State != ComponentState.MissingFile).GroupBy(c => c.Component.File))
            {
                string path = FindCaseless(gameFolder, group.Key) ?? Path.Combine(gameFolder, group.Key.Replace('/', Path.DirectorySeparatorChar));
                byte[] bytes = File.Exists(path) ? File.ReadAllBytes(path) : null;
                foreach (var chk in group)
                {
                    var c = chk.Component;
                    if (c.Kind == "file") bytes = c.Data;
                    else if (c.Kind == "bytes")
                    {
                        bytes = (byte[])bytes.Clone();
                        foreach (var b in c.Patches) Array.Copy(b.New, 0, bytes, b.Offset, b.New.Length);
                    }
                    else if (c.Kind == "ark-blocks")
                    {
                        var a = Archive.Open(bytes, kind);
                        foreach (var b in c.Blocks) { if (b.Data == null) { if (a.Has(b.Index)) a.Delete(b.Index); } else a.Write(b.Index, b.Data); }
                        bytes = a.ToBytes();
                    }
                    else
                    {
                        var pak = StringsPakFile.FromBytes(bytes);
                        foreach (var blk in c.Strings.GroupBy(s => s.Block))
                        {
                            var list = pak.Get(blk.Key);
                            foreach (var s in blk.OrderBy(x => x.Index))
                            {
                                while (list.Count <= s.Index) list.Add("");
                                list[s.Index] = s.Text;
                            }
                            pak.Set(blk.Key, list);
                        }
                        bytes = pak.ToBytes();
                    }
                }
                Directory.CreateDirectory(Path.GetDirectoryName(path));
                SafeFile.Write(path, bytes);
                written.Add(group.Key);
            }
            return written;
        }

        public string Summary
        {
            get
            {
                var sb = new StringBuilder();
                sb.AppendFormat("{0} {1} ({2}) for {3}", Name, Version, Id, Game.ToUpperInvariant());
                if (Authors.Count > 0) sb.Append(", by " + string.Join(", ", Authors));
                sb.Append('\n');
                if (!string.IsNullOrWhiteSpace(Description)) sb.Append(Description.Trim()).Append('\n');
                foreach (var c in Components) sb.Append("  ").Append(c).Append('\n');
                return sb.ToString();
            }
        }
    }
}
