using System;
using System.Collections.Generic;
using UltimateEditor.Games;

namespace UwEditor.Core.Music
{
    /// <summary>Which of a theme's two files: the FM cards' or the Roland MT-32's.</summary>
    public enum MusicVersion { Fm, Mt32 }

    /// <summary>A music theme of a game: its number, its two files and when the game plays it.</summary>
    public sealed class MusicTheme
    {
        public int Number { get; set; }
        /// <summary>The FM cards' file: UW1 AWnn.XMI, UW2 UWAnn.XMI (nn the number in octal).</summary>
        public string FmFile { get; set; }
        /// <summary>The MT-32's file: UW1 UWnn.XMI, UW2 UWRnn.XMI.</summary>
        public string Mt32File { get; set; }
        /// <summary>When the game plays it (read from the callers of set_new_music and load_new_music).</summary>
        public string When { get; set; }
        /// <summary>What happens when it ends.</summary>
        public string AtEnd { get; set; }
        /// <summary>The game waits for the theme to end before it changes the music (it must not loop forever).</summary>
        public bool MustEnd { get; set; }

        public string File(MusicVersion v) { return v == MusicVersion.Fm ? FmFile : Mt32File; }
    }

    /// <summary>
    /// The themes of both games. Theme n is the file whose two digits are n in octal
    /// (uw1/uw2 sound/SOUND.C load_new_music). UW1's numbers and callers: include/sound.h,
    /// SOUND.C change_music_maybe and loop_music_maybe, CUTS.C (cutscenes 1 to 3 start theme 4);
    /// UW2's: include/sound.h, SOUND.C walking_music, CUTS.C cutsop_music, WORLDEV.C
    /// put_player_in_jail and SKILLS.C (theme 10 after jail, resurrection and the Void).
    /// </summary>
    public static class MusicThemes
    {
        /// <summary>UW1 reads a theme into a 6,300-byte buffer, UW2 into 9,000 bytes, without checking the length (uw1 SOUND.C init_sounds and read_file_to_mbuf; uw2 SOUND.C init_sounds).</summary>
        public static int SizeLimit(GameKind kind) { return kind == GameKind.UW2 ? 9000 : 6300; }

        /// <summary>The MIDI effects lock this many channels from the music driver: four effect slots in UW1, three in UW2 (SOUND.C fx_play).</summary>
        public static int EffectChannels(GameKind kind) { return kind == GameKind.UW2 ? 3 : 4; }

        /// <summary>Theme n's file name: the prefix and n in two octal digits.</summary>
        public static string FileName(string prefix, int n) { return prefix + (char)('0' + (n >> 3 & 7)) + (char)('0' + (n & 7)) + ".XMI"; }

        static readonly string[] Uw2Worlds = { "Britannia", "Prison Tower", "Killorn Keep", "Ice Caverns", "Talorus", "Scintillus Academy", "Pits of Carnage", "Tomb of Praecor Loth", "Ethereal Void" };

        /// <summary>UW2's walking themes by world, three each, the first played on entering the world (uw2 SOUND.C walking_music).</summary>
        public static readonly int[,] Uw2Walking =
        {
            { 0x0A, 0x0C, 0x0E }, { 0x09, 0x0A, 0x0F }, { 0x0B, 0x0D, 0x0A },
            { 0x0C, 0x0D, 0x09 }, { 0x08, 0x0B, 0x0E }, { 0x0D, 0x08, 0x0F },
            { 0x0E, 0x09, 0x08 }, { 0x0F, 0x0B, 0x0A }, { 0x08, 0x0C, 0x09 },
        };

        public static List<MusicTheme> For(GameKind kind)
        {
            var l = new List<MusicTheme>();
            if (kind == GameKind.UW2)
            {
                Add(l, "UWA", "UWR", 1, "Start-up and the main menu", "theme 10 follows in the menus");
                Add(l, "UWA", "UWR", 2, "A foe is nearly dead", "repeats while the fight goes on");
                Add(l, "UWA", "UWR", 3, "Combat", "repeats while the fight goes on");
                Add(l, "UWA", "UWR", 4, "The player is badly hurt", "repeats while the fight goes on");
                Add(l, "UWA", "UWR", 5, "The weapon is drawn", "repeats while the weapon is drawn");
                Add(l, "UWA", "UWR", 6, "Victory: a creature is killed", "plays to its end; nothing replaces it before", true);
                Add(l, "UWA", "UWR", 7, "Death", "theme 10 follows (after resurrection)");
                for (int n = 8; n <= 15; n++)
                {
                    var worlds = new List<string>();
                    for (int w = 0; w < 9; w++)
                        for (int k = 0; k < 3; k++)
                            if (Uw2Walking[w, k] == n) worlds.Add(Uw2Worlds[w] + (k == 0 ? " (first)" : ""));
                    string when = "Walking: " + string.Join(", ", worlds);
                    if (n == 10) when += "; also after jail, resurrection and the Void";
                    Add(l, "UWA", "UWR", n, when, "another walking theme of the world follows");
                }
                Add(l, "UWA", "UWR", 24, "The introduction (cutscene)", "repeats until the cutscene changes it");
                Add(l, "UWA", "UWR", 25, "Played by cutscene 1's music opcode", "a walking theme follows");
            }
            else
            {
                Add(l, "AW", "UW", 1, "Start-up and the main menu", "theme 4 follows in the menus");
                Add(l, "AW", "UW", 2, "Walking (2 to 4, one at random)", "a walking theme follows");
                Add(l, "AW", "UW", 3, "Walking (2 to 4, one at random)", "a walking theme follows");
                Add(l, "AW", "UW", 4, "Walking (2 to 4, one at random); cutscenes 1 to 3", "a walking theme follows");
                Add(l, "AW", "UW", 5, "A foe is nearly dead (below a quarter of its hit points)", "repeats while the fight goes on");
                Add(l, "AW", "UW", 6, "Combat", "repeats while the fight goes on");
                Add(l, "AW", "UW", 7, "The player is in danger (below a quarter of the hit points)", "repeats while the fight goes on");
                Add(l, "AW", "UW", 8, "The weapon is drawn", "repeats while the weapon is drawn");
                Add(l, "AW", "UW", 9, "Victory: a creature is killed", "plays to its end; nothing replaces it before", true);
                Add(l, "AW", "UW", 10, "Death", "a walking theme follows");
                Add(l, "AW", "UW", 11, "Never asked for: no call in the game plays it", "would play to its end, like theme 9", true);
                Add(l, "AW", "UW", 13, "Conversations, the automap and sleep", "a walking theme follows");
            }
            return l;
        }

        static void Add(List<MusicTheme> l, string fm, string mt, int n, string when, string atEnd, bool mustEnd = false)
        {
            l.Add(new MusicTheme { Number = n, FmFile = FileName(fm, n), Mt32File = FileName(mt, n), When = when, AtEnd = atEnd, MustEnd = mustEnd });
        }
    }
}
