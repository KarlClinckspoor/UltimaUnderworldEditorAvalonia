using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UltimateEditor.Games;
using UwEditor.Core.Conversations;

namespace UwEditor.Core.Editing
{
    /// <summary>A block of STRINGS.PAK, for the strings window.</summary>
    public sealed class StringBlockInfo
    {
        public int Number;
        public int Count;
        public string Description;
        public bool Changed;
        public string Label { get { return string.Format("{0} (0x{0:X4}) · {1} · {2} string{3}{4}", Number, Description, Count, Count == 1 ? "" : "s", Changed ? " · changed" : ""); } }
        public override string ToString() { return Label; }
    }

    /// <summary>
    /// STRINGS.PAK with its unsaved edits, one string at a time: the strings window and the keys
    /// window edit through it. It shares the conversation library's file when that is open, so a
    /// conversation's strings and a key's description are never saved over each other.
    /// </summary>
    public sealed class StringsEditor
    {
        public GameSession Session { get; private set; }
        public StringsPakFile Pak { get; private set; }
        /// <summary>After saving: the session's strings and names are read again.</summary>
        public event Action Saved;
        /// <summary>After any change.</summary>
        public event Action Changed;

        public static StringsEditor Open(GameSession s, StringsPakFile shared = null)
        {
            if (shared == null)
            {
                string p = GameProfile.Find(s.DataFolder, "STRINGS.PAK");
                if (p == null) throw new FileNotFoundException("No STRINGS.PAK in " + s.DataFolder);
                shared = StringsPakFile.Load(p);
            }
            return new StringsEditor { Session = s, Pak = shared };
        }

        public static string Describe(GameSession s, int block)
        {
            if (block >= 0x0E00 && block < 0x0F00)
            {
                int slot = block - 0x0E00;
                string n = s.Strings.Get(7, slot + 16);
                return "Conversation " + slot + (string.IsNullOrWhiteSpace(n) ? "" : " (" + n.Trim() + ")");
            }
            if (block >= 0x0C00 && block < 0x0E00) return "Cutscene " + (block - 0x0C00);
            return StringBlock.GetDescription(block);
        }

        public List<StringBlockInfo> Blocks()
        {
            return Pak.Blocks.Select(b => new StringBlockInfo { Number = b, Count = Pak.Get(b).Count, Description = Describe(Session, b), Changed = Pak.IsChanged(b) }).ToList();
        }

        public List<string> Get(int block) { return Pak.Get(block); }

        public string Get(int block, int index)
        {
            var l = Pak.Get(block);
            return index >= 0 && index < l.Count ? l[index] : null;
        }

        /// <summary>Changes one string; throws when it has characters the file cannot write.</summary>
        public void Set(int block, int index, string text)
        {
            var l = Pak.Get(block).ToList();
            if (index < 0) throw new ArgumentOutOfRangeException("index");
            while (l.Count <= index) l.Add("");
            l[index] = (text ?? "").Replace("\r\n", "\n");
            Pak.Set(block, l);
            Changed?.Invoke();
        }

        /// <summary>Adds a string at the end of a block; returns its number.</summary>
        public int Add(int block, string text)
        {
            var l = Pak.Get(block).ToList();
            l.Add((text ?? "").Replace("\r\n", "\n"));
            Pak.Set(block, l);
            Changed?.Invoke();
            return l.Count - 1;
        }

        public void Revert(int block) { Pak.Revert(block); Changed?.Invoke(); }
        public bool HasChanges { get { return Pak.HasChanges; } }
        public bool IsChanged(int block, int index)
        {
            if (!Pak.IsChanged(block)) return false;
            var o = Pak.Original(block);
            var n = Pak.Get(block);
            return index >= o.Count || index >= n.Count || o[index] != n[index];
        }

        /// <summary>Writes STRINGS.PAK (STRINGS.PAK.BKP and, the first time, STRINGS.PAK.ORIG kept) and reloads the session's strings.</summary>
        public void Save()
        {
            Pak.Save();
            Session.ReloadStrings();
            Saved?.Invoke();
            Changed?.Invoke();
        }

        /// <summary>Strings of any block containing a text (case-insensitive).</summary>
        public IEnumerable<(int Block, int Index, string Text)> Search(string text)
        {
            if (string.IsNullOrWhiteSpace(text)) yield break;
            string f = text.Trim();
            foreach (int b in Pak.Blocks)
            {
                var l = Pak.Get(b);
                for (int i = 0; i < l.Count; i++)
                    if ((l[i] ?? "").IndexOf(f, StringComparison.OrdinalIgnoreCase) >= 0) yield return (b, i, l[i]);
            }
        }
    }
}
