using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using UltimateEditor.Games;

namespace UwEditor.Core.Editing
{
    /// <summary>A note the player wrote on the map: up to 49 characters (capitals: Automap.NoteChars) at a point of the map screen (struct ATM, 0x36 bytes).</summary>
    public sealed class MapNote
    {
        public string Text = "";
        /// <summary>The map screen's pixel (320 × 200, y up from the bottom), where the text starts.</summary>
        public int X, Y;
        public override string ToString() { return string.Format("({0}, {1}) {2}", X, Y, Text); }
    }

    /// <summary>
    /// The player's map of a level and the notes on it (ui/AUTOMAP.C): PlayersMap, a byte per
    /// tile, in LEV.ARK block 0x1A + level (UW1) or 0x9F + level (UW2), and up to 100 notes in
    /// block 0x23 + level (UW1) or 0xEF + level (UW2). A tile byte's low nibble is the tile type
    /// (0 never seen; UW2 0xA and up known but not seen); UW1 keeps the floor's terrain (water
    /// 0x10, lava 0x20) in bits 4-5 and a door code in bits 6-7, UW2 the terrain class (TERRAIN.DAT
    /// & 0xC0) in bits 6-7. Saves have them; a new game starts from DATA's (UW2's has some).
    /// </summary>
    public sealed class Automap
    {
        public const int MaxNotes = 100, NoteSize = 0x36, TextSize = 0x32;
        public int Level { get; private set; }
        public byte[] Map { get; private set; }
        public List<MapNote> Notes { get; private set; }
        public bool Dirty { get; private set; }
        /// <summary>
        /// A page of notes past the last level (UW1 pages 10-99): the map screen's arrows go up to
        /// page 0x63 and show a blank map with the page's notes from block 0x23 + page (ui/AUTOMAP.C
        /// ManageDungeonMap, ChangeAutoMapLevel loads a map only below 9, ShowAutoMapLevel reads the
        /// notes of any page). LEV.ARK's 135 blocks end at page 99's; those blocks are unused
        /// otherwise, and a new game copies DATA's LEV.ARK, notes included (ui/MAINMENU.C). Such a
        /// page has no map: its Map is blank, and only its notes are read and written (block
        /// 0x1A + page would be a level's notes).
        /// </summary>
        public bool NotesOnly { get; private set; }
        /// <summary>The last page of notes UW1's map screen shows (ui/AUTOMAP.C: level &lt; 0x63).</summary>
        public const int LastNotesPage = 0x63;
        readonly List<Tuple<byte[], List<MapNote>, string>> undo = new List<Tuple<byte[], List<MapNote>, string>>();

        public static int MapBlock(GameKind kind, int level) { return (kind == GameKind.UW2 ? 0x9F : 0x1A) + level; }
        public static int NotesBlock(GameKind kind, int level) { return (kind == GameKind.UW2 ? 0xEF : 0x23) + level; }

        /// <summary>Whether a page is one of UW1's notes-only pages (10-99).</summary>
        public static bool IsNotesPage(GameKind kind, int page) { return kind == GameKind.UW1 && page > 9 && page <= LastNotesPage; }

        public static Automap Load(GameSession s, int level)
        {
            var a = new Automap { Level = level, Map = new byte[64 * 64], Notes = new List<MapNote>(), NotesOnly = IsNotesPage(s.Kind, level) };
            int mb = MapBlock(s.Kind, level), nb = NotesBlock(s.Kind, level);
            if (!a.NotesOnly && s.LevArk.Has(mb)) { var b = s.LevArk.Read(mb); Array.Copy(b, a.Map, Math.Min(b.Length, a.Map.Length)); }
            if (s.LevArk.Has(nb)) a.Notes = ReadNotes(s.LevArk.Read(nb));
            return a;
        }

        public static List<MapNote> ReadNotes(byte[] b)
        {
            var l = new List<MapNote>();
            for (int i = 0; i + NoteSize <= b.Length && l.Count < MaxNotes; i += NoteSize)
            {
                int len = 0;
                while (len < TextSize && b[i + len] != 0) len++;
                if (len >= TextSize) len = TextSize - 1;           // AUTOMAP.C note_copy: at most 49 characters
                var n = new MapNote { Text = Encoding.Latin1.GetString(b, i, len), X = (short)(b[i + 0x32] | b[i + 0x33] << 8), Y = (short)(b[i + 0x34] | b[i + 0x35] << 8) };
                if (n.X >= 0) l.Add(n);                            // -1: erased
            }
            return l;
        }

        public static byte[] WriteNotes(List<MapNote> notes)
        {
            var b = new byte[Math.Min(MaxNotes, notes.Count) * NoteSize];
            for (int k = 0; k < notes.Count && k < MaxNotes; k++)
            {
                int i = k * NoteSize;
                var t = Encoding.Latin1.GetBytes(Clean(notes[k].Text));
                Array.Copy(t, 0, b, i, Math.Min(t.Length, TextSize - 1));
                b[i + 0x32] = (byte)notes[k].X; b[i + 0x33] = (byte)(notes[k].X >> 8);
                b[i + 0x34] = (byte)notes[k].Y; b[i + 0x35] = (byte)(notes[k].Y >> 8);
            }
            return b;
        }

        /// <summary>A note's text as the game can draw it, at most 49 characters (the game's buffer): see NoteChars.</summary>
        public static string Clean(string s)
        {
            string t = NoteChars(s);
            return t.Length > TextSize - 1 ? t.Substring(0, TextSize - 1) : t;
        }

        /// <summary>The last character a note can hold: the map screen draws notes in FONT4X5P.SYS (ui/AUTOMAP.C RedisplayStrings), whose 97 glyphs end at 0x60.</summary>
        public const char LastNoteChar = '`';

        /// <summary>
        /// Text in the characters a note can hold: 0x20 to 0x60, capitals and no lowercase, which is
        /// all the game itself lets the player type (ui/AUTOMAP.C ManageDungeonMap: 0x20 to 0x7A,
        /// through toupper; UW1 and UW2 alike). Letters lose their accents and become capitals,
        /// typographic quotes and dashes become plain, braces brackets, anything else a '?', and
        /// control characters are dropped. A character past 0x60 must not reach the game: FONT4X5P.SYS
        /// has 97 glyphs, gfx/GRFX.C grfx_load_font reads 128 glyphs' worth over the previous
        /// font's, and setup_font (gfx/GRLIBI.ASM _447D) points every character 0 to 127 into that
        /// buffer, so a lowercase letter gets a stale glyph and width byte. The rasteriser (GRLIBI.ASM,
        /// after _4A06) draws a character again until a width byte moves it on, so a stale width of 0
        /// ('e', 'l', 's' and 'z', with FONT5X6P.SYS loaded before) loops for ever: the game hangs on
        /// the map page that shows the note (DOSBox and Underworld Exhumed alike). Its string_width
        /// (_4C45) indexes the glyph table with bytes 0x80 and up past its 128 entries too.
        /// </summary>
        public static string NoteChars(string s)
        {
            var sb = new StringBuilder();
            foreach (char c in (s ?? "").Normalize(NormalizationForm.FormD))
            {
                if (System.Globalization.CharUnicodeInfo.GetUnicodeCategory(c) == System.Globalization.UnicodeCategory.NonSpacingMark) continue;
                char u = c >= 'a' && c <= 'z' ? (char)(c - 32)
                    : c == '‘' || c == '’' ? '\'' : c == '“' || c == '”' ? '"' : c == '–' || c == '—' ? '-'
                    : c == '{' ? '(' : c == '}' ? ')' : c;
                if (u >= ' ' && u <= LastNoteChar) sb.Append(u);
                else if (!char.IsControl(u)) sb.Append('?');
            }
            return sb.ToString();
        }

        /// <summary>Whether a note holds text the game cannot draw (written by an earlier editor or another tool): saving writes the notes again, cleaned.</summary>
        public bool UnsafeNotes { get { return Notes.Any(n => n.Text != Clean(n.Text)); } }

        /// <summary>The map screen pixel of a tile's corner (AUTOMAP.C DoTile: three pixels a tile).</summary>
        public static void TileToPixel(double tx, double ty, out double px, out double py) { px = (tx - 1) * 3 + 10; py = (ty - 1) * 3 + 7; }
        public static void PixelToTile(double px, double py, out double tx, out double ty) { tx = (px - 10) / 3.0 + 1; ty = (py - 7) / 3.0 + 1; }

        static ushort[] terrainCache;
        static string terrainPath;

        /// <summary>TERRAIN.DAT was written (the game tables): it is read again on the next use.</summary>
        public static void TerrainChanged() { terrainPath = null; }

        static int Terrain(GameSession s, int texture, bool floor)
        {
            string p = GameProfile.Find(s.DataFolder, "TERRAIN.DAT");
            if (p == null || texture < 0) return 0;
            if (terrainPath != p)
            {
                var d = File.ReadAllBytes(p);
                terrainCache = new ushort[d.Length / 2];
                for (int i = 0; i < terrainCache.Length; i++) terrainCache[i] = (ushort)(d[2 * i] | d[2 * i + 1] << 8);
                terrainPath = p;
            }
            int at = (s.IsUw2 || !floor ? 0 : 0x100) + texture;
            return at < terrainCache.Length ? terrainCache[at] : 0;
        }

        /// <summary>
        /// The byte the game writes when the player sees a tile: the tile type and its floor's
        /// terrain (UW1 3d/GRIDDB.C grdb_elem: type | TxmTerr's low byte, a door adding code 1 in
        /// bits 6-7; UW2 ui/AUTOMAP.C automap_area, as Map Area does: type | TxmTerr & 0xC0). Rock is 0.
        /// </summary>
        public static byte Seen(GameSession s, LevelModel m, int x, int y)
        {
            var t = m.Tiles[x, y];
            if (t.IsSolid) return 0;
            int code = (int)t.Type & 0xF;
            int terr = Terrain(s, m.FloorTextureNumber(t.FloorIndex), true);
            bool door = m.TileObjects(x, y).Any(o => o.Item >= 0x140 && o.Item <= 0x14F);
            // UW2: the class in bits 6-7, as Map Area writes it (its shipped maps mark few doors); UW1: the terrain in bits 4-5, a door code 1 in bits 6-7
            if (s.IsUw2) code |= terr & 0xC0;
            else code |= (terr & 0x30) | (door ? 1 << 6 : 0);
            return (byte)code;
        }

        public bool CanUndo { get { return undo.Count > 0; } }
        public string UndoLabel { get { return undo.Count > 0 ? undo[undo.Count - 1].Item3 : ""; } }

        /// <summary>Remembers the map and notes before a change (one undo step).</summary>
        public void Snapshot(string label)
        {
            undo.Add(Tuple.Create((byte[])Map.Clone(), Notes.Select(n => new MapNote { Text = n.Text, X = n.X, Y = n.Y }).ToList(), label));
            if (undo.Count > 100) undo.RemoveAt(0);
            Dirty = true;
        }

        /// <summary>Puts in another map and notes (a checkpoint), as one undo step.</summary>
        public void Replace(byte[] map, List<MapNote> notes, string label)
        {
            Snapshot(label);
            Map = new byte[64 * 64];
            Array.Copy(map, Map, Math.Min(map.Length, Map.Length));
            Notes = notes.Select(n => new MapNote { Text = n.Text, X = n.X, Y = n.Y }).ToList();
        }

        public void Undo()
        {
            if (undo.Count == 0) return;
            var u = undo[undo.Count - 1];
            undo.RemoveAt(undo.Count - 1);
            Map = u.Item1;
            Notes = u.Item2;
            Dirty = true;
        }

        /// <summary>Marks the tiles in a square brush seen (as the game would write them) or unseen.</summary>
        public int Paint(GameSession s, LevelModel m, int cx, int cy, int radius, bool seen)
        {
            int n = 0;
            for (int y = Math.Max(0, cy - radius); y <= Math.Min(63, cy + radius); y++)
                for (int x = Math.Max(0, cx - radius); x <= Math.Min(63, cx + radius); x++)
                {
                    byte v = seen ? Seen(s, m, x, y) : (byte)0;
                    if (Map[y * 64 + x] != v) { Map[y * 64 + x] = v; n++; }
                }
            return n;
        }

        /// <summary>Every open tile seen, or none.</summary>
        public void All(GameSession s, LevelModel m, bool seen)
        {
            for (int y = 0; y < 64; y++) for (int x = 0; x < 64; x++) Map[y * 64 + x] = seen ? Seen(s, m, x, y) : (byte)0;
        }

        public int SeenCount { get { int n = 0; for (int i = 0; i < Map.Length; i++) { int t = Map[i] & 0xF; if (t > 0 && t < 10) n++; } return n; } }

        public void MarkSaved() { Dirty = false; }
    }
}
