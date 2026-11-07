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

    /// <summary>A slot's conversation and string block as the library held them (ConversationLibrary.Snapshot): null where they were not edited.</summary>
    public sealed class ConversationState
    {
        public int Slot;
        public bool Edited;
        public Conversation Conversation;
        public int Block = -1;
        public bool StringsEdited;
        /// <summary>The block's strings as the game has them (accent codes, not letters).</summary>
        public List<string> Strings;
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

        /// <summary>The accent table the strings are shown and written with (the session's).</summary>
        public AccentTable Accents { get { return Session.Accents ?? new AccentTable(); } }

        /// <summary>The conversation's strings as shown: the accent table's codes as their letters.</summary>
        public List<string> ShownStringsOf(Conversation c) { return StringsOf(c).Select(Accents.ToDisplay).ToList(); }

        /// <summary>Replaces a conversation's strings with shown text (letters turned into their codes). Throws with the reason when one cannot be written.</summary>
        public void SetShownStrings(Conversation c, IList<string> shown)
        {
            var game = new List<string>();
            for (int i = 0; i < shown.Count; i++)
            {
                try { game.Add(Accents.ToGame(shown[i] ?? "")); }
                catch (FormatException ex) { throw new InvalidDataException("String " + i + ": " + ex.Message); }
            }
            Strings.Set(c.StringBlock, game);
        }

        /// <summary>The conversation's readable view, with its names (strings as shown).</summary>
        public DecompileResult Decompile(Conversation c, bool fold = true) { return Decompiler.Decompile(c, ShownStringsOf(c).ToArray(), Kind, fold, Names.Resolve(c)); }

        /// <summary>
        /// Compiles a conversation's source (the Dialogue tab's language, ConversationCompiler)
        /// against the conversation as it is now: functions whose text did not change are its
        /// old code. Nothing is written until Apply.
        /// </summary>
        public CompileResult Compile(Conversation c, string source, CompileOptions options = null)
        {
            options = options ?? new CompileOptions();
            if (options.Accents == null) options.Accents = Accents;
            return ConversationCompiler.Compile(c, ShownStringsOf(c), Kind, Names.Resolve(c), source, options);
        }

        /// <summary>Puts a compiled conversation and its strings (new ones at the end of its block) in the archive and STRINGS.PAK until Save.</summary>
        public void Apply(CompileResult r)
        {
            if (r == null || !r.Ok) throw new InvalidOperationException("The source did not compile.");
            Archive.Write(r.Conversation.Slot, r.Conversation);
            if (r.StringsChanged) SetShownStrings(r.Conversation, r.Strings);
        }

        /// <summary>
        /// A slot's state in the library (its conversation and its string block, edited or not),
        /// to put back later: an applied edit's undo step.
        /// </summary>
        public ConversationState Snapshot(int slot)
        {
            var c = Archive.Read(slot);
            int block = c != null ? c.StringBlock : -1;
            return new ConversationState
            {
                Slot = slot,
                Edited = Archive.IsChanged(slot),
                Conversation = Archive.IsChanged(slot) ? c : null,
                Block = block,
                StringsEdited = block >= 0 && Strings.IsChanged(block),
                Strings = block >= 0 && Strings.IsChanged(block) ? Strings.Get(block) : null,
            };
        }

        /// <summary>Puts a slot back as a snapshot had it (the string block the slot has now is put back too when it is another).</summary>
        public void Restore(ConversationState st)
        {
            var now = Archive.Read(st.Slot);
            if (now != null && now.StringBlock != st.Block) Strings.Revert(now.StringBlock);
            if (st.Edited) Archive.Write(st.Slot, st.Conversation); else Archive.Revert(st.Slot);
            if (st.Block >= 0)
            {
                if (st.StringsEdited) Strings.Set(st.Block, st.Strings); else Strings.Revert(st.Block);
            }
        }

        ConversationArchive shippedArchive;
        StringsPakFile shippedStrings;
        bool shippedRead;

        /// <summary>
        /// The conversation of a slot as shipped, and its strings as the game has them: from
        /// CNV.ARK.ORIG and STRINGS.PAK.ORIG, the untouched copies kept the first time the editor
        /// wrote the files (SafeFile), or the files as they are while there are none. Null when
        /// the slot has no conversation there.
        /// </summary>
        public ConversationState Shipped(int slot)
        {
            if (!shippedRead)
            {
                shippedRead = true;
                string o = Archive.Path == null ? null : SafeFile.OriginalOf(Archive.Path);
                try { shippedArchive = o != null ? ConversationArchive.Load(o, Kind) : Archive.Path != null ? ConversationArchive.Load(Archive.Path, Kind) : null; } catch (Exception) { shippedArchive = null; }
                o = Strings.Path == null ? null : SafeFile.OriginalOf(Strings.Path) ?? Strings.Path;
                try { shippedStrings = o != null ? StringsPakFile.Load(o) : null; } catch (Exception) { shippedStrings = null; }
            }
            Conversation c;
            if (shippedArchive != null) c = shippedArchive.Has(slot) ? shippedArchive.Read(slot) : null;
            else { var raw = Archive.RawBlock(slot); c = raw == null || raw.Length < 0x10 ? null : Conversation.Parse(raw, slot); }
            if (c == null) return null;
            return new ConversationState
            {
                Slot = slot, Edited = true, Conversation = c, Block = c.StringBlock, StringsEdited = true,
                Strings = shippedStrings != null ? shippedStrings.Original(c.StringBlock) : Strings.Original(c.StringBlock),
            };
        }

        /// <summary>Puts a slot's shipped conversation and strings (Shipped) in the library until Save. Returns false when there is none.</summary>
        public bool RevertToShipped(int slot)
        {
            var st = Shipped(slot);
            if (st == null) return false;
            var now = Archive.Read(slot);
            if (now != null && now.StringBlock != st.Block) Strings.Revert(now.StringBlock);
            // the same bytes as the file: not an edit
            var raw = Archive.RawBlock(slot);
            if (raw != null && raw.SequenceEqual(st.Conversation.ToBytes())) Archive.Revert(slot); else Archive.Write(slot, st.Conversation);
            Strings.Set(st.Block, st.Strings);
            return true;
        }

        NameBook names;
        /// <summary>The names the conversations are shown with: the shipped ones, and the user's renames once the editor has set where they are kept.</summary>
        public NameBook Names { get { return names ?? (names = NameBook.Load(Kind, null)); } set { names = value; } }

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
