using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UltimateEditor.Games;

namespace UwEditor.Core.Conversations
{
    /// <summary>A creature that talks through a slot: where it is.</summary>
    public sealed class ConversationUser
    {
        public int Level;
        public int Index;
        public int TileX, TileY;
        public string Name;
        public bool Generic;
        public override string ToString()
        {
            return string.Format("level {0}, #{1} {2} at ({3}, {4}){5}", Level, Index, Name, TileX, TileY, Generic ? " (generic)" : "");
        }
    }

    /// <summary>
    /// The game's conversations for editing: CNV.ARK and STRINGS.PAK from DATA (a saved game
    /// uses DATA's), the names creatures talk under, and saving both files together.
    /// </summary>
    public sealed class ConversationLibrary
    {
        public GameSession Session { get; private set; }
        public ConversationArchive Archive { get; private set; }
        public StringsPakFile Strings { get; private set; }
        public GameKind Kind { get { return Session.Kind; } }

        public static ConversationLibrary Open(GameSession s)
        {
            string cnv = GameProfile.Find(s.DataFolder, "CNV.ARK");
            if (cnv == null) throw new FileNotFoundException("There is no CNV.ARK in " + s.DataFolder + ".");
            string pak = GameProfile.Find(s.DataFolder, "STRINGS.PAK");
            if (pak == null) throw new FileNotFoundException("There is no STRINGS.PAK in " + s.DataFolder + ".");
            return new ConversationLibrary { Session = s, Archive = ConversationArchive.Load(cnv, s.Kind), Strings = StringsPakFile.Load(pak) };
        }

        public bool HasChanges { get { return Archive.HasChanges || Strings.HasChanges; } }

        /// <summary>The name a slot talks under: STRINGS.PAK block 7 string slot + 16, or for a generic slot (0x100 + item & 0x3F) the creature's name.</summary>
        public string Name(int slot)
        {
            if (slot >= 0x100)
            {
                string n = Session.Names.Get(0x40 + (slot & 0x3F));
                return "generic: " + (string.IsNullOrWhiteSpace(n) ? "creature " + (slot & 0x3F) : n);
            }
            var b = Strings.Get(7);
            string s = slot + 16 < b.Count ? b[slot + 16] : null;
            return string.IsNullOrWhiteSpace(s) ? null : s.Trim();
        }

        /// <summary>The conversation's strings (its STRINGS.PAK block, from the header's string block).</summary>
        public List<string> StringsOf(Conversation c) { return Strings.Get(c.StringBlock); }

        public DecompileResult Decompile(Conversation c) { return Decompiler.Decompile(c, StringsOf(c).ToArray(), Kind); }

        /// <summary>
        /// Saves what changed: CNV.ARK and STRINGS.PAK in DATA, each old file kept as .BKP, and
        /// the private globals' records of the conversations written (DATA's BABGLOBS.DAT, and
        /// the open save's BGLOBALS.DAT; see PrivateRecords). Returns what was written.
        /// </summary>
        public List<string> Save()
        {
            var done = new List<string>();
            if (Archive.HasChanges)
            {
                var slots = Archive.Save();
                done.Add(Path.GetFileName(Archive.Path) + (slots.Count > 0 ? " (conversation" + (slots.Count == 1 ? " " : "s ") + string.Join(", ", slots) + ")" : ""));
                done.AddRange(PrivateRecords.Update(Session, Archive, slots));
            }
            if (Strings.HasChanges)
            {
                Strings.Save();
                done.Add(Path.GetFileName(Strings.Path));
            }
            return done;
        }

        /// <summary>Every creature in LEV.ARK, by the slot it talks through (whoami, or 0x100 + item &amp; 0x3F for whoami 0).</summary>
        public Dictionary<int, List<ConversationUser>> FindUsers()
        {
            var map = new Dictionary<int, List<ConversationUser>>();
            foreach (var ls in Session.LevelSlots())
            {
                if (!ls.Present) continue;
                LevelModel m;
                try { m = Session.LoadLevel(ls.Level); } catch (Exception) { continue; }
                foreach (var o in m.Objects)
                {
                    if (o == null || o.Free || !o.IsMobile || !o.IsCritter || o.Record == null || o.Record.Length < 0x1B) continue;
                    if (o.TileX < 0 && o.Parent < 0) continue;
                    int whoami = o.Record[0x1A];
                    int slot = whoami != 0 ? whoami : 0x100 + (o.Item & 0x3F);
                    List<ConversationUser> list;
                    if (!map.TryGetValue(slot, out list)) map[slot] = list = new List<ConversationUser>();
                    list.Add(new ConversationUser { Level = ls.Level, Index = o.Index, TileX = o.TileX, TileY = o.TileY, Name = o.Name, Generic = whoami == 0 });
                }
            }
            return map;
        }
    }
}
