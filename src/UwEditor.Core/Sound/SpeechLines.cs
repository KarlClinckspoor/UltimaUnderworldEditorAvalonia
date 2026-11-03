using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace UwEditor.Core.Sound
{
    /// <summary>A say opcode of a cutscene script: the subtitle string and the speech file it starts.</summary>
    public sealed class SpeechLine
    {
        /// <summary>The cutscene number (the CSnnn file's digits read in octal).</summary>
        public int Cutscene;
        /// <summary>The script file, e.g. CS000.N00.</summary>
        public string Script;
        /// <summary>The frame the opcode fires on (counted from the previous record's).</summary>
        public int Frame;
        /// <summary>The string of block 0xC00 + cutscene.</summary>
        public int String;
        /// <summary>The speech file's number: UW1 nn.VOC, UW2 BSPnn.VOC.</summary>
        public int Voc;
        /// <summary>UW2: strings shown only when speech is off (say with 998) that follow this line.</summary>
        public readonly List<int> SilentFollowers = new List<int>();
        public int StringBlock { get { return 0xC00 + Cutscene; } }
    }

    /// <summary>
    /// Which speech file each cutscene line plays: the say opcodes (13) of the CUTS\CSnnn.N00
    /// scripts. A script is a stream of 16-bit words: the frame, the opcode, its arguments; say
    /// takes colour, string and file, 999 meaning text only and (UW2) 998 nothing when speech
    /// is on (uw1/src/gfx/CUTS.C cutsop_say, uw2/src/gfx/CUTS.C cutsop_say; the arguments each
    /// handler uses, from the handlers' return values and cuts_dispatch).
    /// </summary>
    public static class SpeechLines
    {
        // arguments of opcodes 0..15 (UW1) and 0..27 (UW2); -1: counted from the record (palrange, palfade)
        static readonly int[] Uw1Args = { 2, 0, 2, 1, 2, 1, 0, 1, 2, 1, 1, 1, 1, 3, 2, 0 };
        static readonly int[] Uw2Args = { 2, 0, 2, 1, 2, 0, 0, 1, 2, 1, 1, 1, 1, 3, 2, 0, -1, -1, 4, 3, 3, 2, 3, 3, 1, 1, 1, 1 };
        const int OpSay = 13, OpEnd = 6;

        /// <summary>Every say of every script under the game folder's CUTS (empty when there is none).</summary>
        public static List<SpeechLine> Find(string gameFolder, bool uw2)
        {
            var list = new List<SpeechLine>();
            string cuts = SoundLibrary.FindDir(gameFolder, "CUTS");
            if (cuts == null) return list;
            foreach (var f in Directory.GetFiles(cuts).Where(p => IsScript(Path.GetFileName(p))).OrderBy(p => Path.GetFileName(p), StringComparer.OrdinalIgnoreCase))
            {
                int cs;
                try { cs = System.Convert.ToInt32(Path.GetFileName(f).Substring(2, 3), 8); } catch (Exception) { continue; }
                try { list.AddRange(Parse(File.ReadAllBytes(f), uw2, cs, Path.GetFileName(f).ToUpperInvariant())); } catch (Exception) { }
            }
            return list;
        }

        static bool IsScript(string name)
        {
            return name.Length == 9 && name.StartsWith("CS", StringComparison.OrdinalIgnoreCase) && name.EndsWith(".N00", StringComparison.OrdinalIgnoreCase)
                && name.Substring(2, 3).All(c => c >= '0' && c <= '7');
        }

        /// <summary>The says of one script (stops at the end opcode or an opcode the game does not have).</summary>
        public static List<SpeechLine> Parse(byte[] d, bool uw2, int cutscene, string script)
        {
            var args = uw2 ? Uw2Args : Uw1Args;
            var r = new List<SpeechLine>();
            int words = d.Length / 2;
            Func<int, int> w = i => d[i * 2] | d[i * 2 + 1] << 8;
            int p = 0;
            SpeechLine last = null;
            while (p + 1 < words)
            {
                int frame = w(p), op = w(p + 1);
                p += 2;
                if (op >= args.Length) break;
                int n = args[op];
                if (n < 0)
                {
                    // palrange: code[1] * 3 + 2; palfade: code[3] * 3 + 4 (uw2/src/gfx/CUTS.C)
                    if (op == 16) n = p + 1 < words ? w(p + 1) * 3 + 2 : 0;
                    else n = p + 3 < words ? w(p + 3) * 3 + 4 : 0;
                }
                if (op == OpSay && p + 2 < words)
                {
                    int str = w(p + 1), voc = w(p + 2);
                    if (voc == 998 && last != null) last.SilentFollowers.Add(str);
                    else if (voc != 999 && voc != 998)
                    {
                        last = new SpeechLine { Cutscene = cutscene, Script = script, Frame = frame, String = str, Voc = voc };
                        r.Add(last);
                    }
                    else last = null;
                }
                p += n;
                if (op == OpEnd) break;
            }
            return r;
        }
    }
}
