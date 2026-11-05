using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using UltimateEditor.Games;
using UltimateEditor.Uw1;
using UwEditor.Core.Conversations;

namespace UwEditor.Core.Editing
{
    /// <summary>
    /// One table of OBJECTS.DAT or COMOBJ.DAT: <see cref="Count"/> records of the schema's size
    /// from byte <see cref="Start"/>, record i belonging to item <see cref="FirstItem"/> + i.
    /// </summary>
    public sealed class ObjectTable
    {
        /// <summary>The tab's name ("Weapons").</summary>
        public string Name { get; private set; }
        /// <summary>OBJECTS.DAT or COMOBJ.DAT.</summary>
        public string FileName { get; private set; }
        public int Start { get; private set; }
        public int Count { get; private set; }
        public int FirstItem { get; private set; }
        public RecordSchema Schema { get; private set; }
        /// <summary>What the table is, in a sentence or two.</summary>
        public string Summary { get; private set; }
        /// <summary>Where the game reads it (underworld-exhumed).</summary>
        public string Source { get; private set; }

        public bool IsComObj { get { return FileName == ObjectTables.ComObjFile; } }

        public ObjectTable(string name, string file, int start, int count, int firstItem, RecordSchema schema, string summary, string source)
        {
            Name = name; FileName = file; Start = start; Count = count; FirstItem = firstItem; Schema = schema; Summary = summary; Source = source;
        }

        /// <summary>The item id of record row.</summary>
        public int ItemOf(int row) { return FirstItem + row; }
        /// <summary>The byte offset of record row in its file.</summary>
        public int OffsetOf(int row) { return Start + row * Schema.Size; }
        /// <summary>The first byte after the table.</summary>
        public int End { get { return Start + Count * Schema.Size; } }
    }

    /// <summary>
    /// The class tables of OBJECTS.DAT and the common properties of COMOBJ.DAT, edited as bytes:
    /// every field is a bit field of a <see cref="RecordSchema"/>, so an unchanged file is written
    /// back byte for byte and an edit touches only its own bits.
    ///
    /// Both games read the files the same way (uw1 and uw2 obj/OBJCLASS.C init_objects): OBJECTS.DAT
    /// is a 2-byte header, then each major class's tables in class order: weapons, missiles and
    /// armour (obj/HACK.C hack_init), the creatures (critter/CREATURE.C creature_init), containers,
    /// lights and food (obj/MISC.C misc_init), the trigger modes (event/TRIGGER.C trap_init) and the
    /// animated objects (obj/ANIMOBJ.C animobj_load); 3554 bytes. COMOBJ.DAT is a 2-byte header and
    /// 512 records of 11 bytes (struct ComObj). Both are read from DATA, also for a saved game.
    /// </summary>
    public sealed class ObjectTables
    {
        public const string ObjectsFile = "OBJECTS.DAT";
        public const string ComObjFile = "COMOBJ.DAT";

        const Certainty C = Certainty.Certain;
        const Certainty I = Certainty.Inferred;
        const Certainty U = Certainty.Unknown;

        static FieldDef F(string name, int offset, int bit, int bits, string desc, string src, Certainty c = C, bool signed = false)
        {
            return new FieldDef(name, offset, bit, bits, desc, src, c, signed);
        }

        /// <summary>OBJECTS.DAT food entry: one byte (obj/MISC.C Food[]).</summary>
        public static readonly RecordSchema Food = new RecordSchema("Food", 1, "obj/MISC.C Food",
            F("Nutrition", 0, 0, 8, "How much eating it feeds the player (UseFood: nutrition = Food[index]).", "obj/USEITEMS.C UseFood"));

        /// <summary>OBJECTS.DAT trigger mode: one byte per trigger (event/TRIGGER.C Triggers[]).</summary>
        public static readonly RecordSchema Trigger = new RecordSchema("Trigger", 1, "event/TRIGGER.C Triggers",
            F("Mode", 0, 0, 8, "The action that sets it off: 0 move onto the tile, 1 step on, 2 pick up, 4 use, 5 look, 6 unlock, 7 open. UseTrigger does nothing when the action's mode differs.", "event/TRIGGER.C UseTrigger"));

        /// <summary>OBJECTS.DAT animated object class, 4 bytes (include/object.h struct AnimClass).</summary>
        public static readonly RecordSchema AnimClass = new RecordSchema("Animated object", 4, "include/object.h struct AnimClass",
            F("Cycle", 0, 0, 1, "Steps to the next frame, wrapping round after the last (ANIMF_CYCLE).", "obj/EFFECT.C do_animobj"),
            F("Random", 0, 1, 1, "Picks a random frame (ANIMF_RANDOM).", "obj/EFFECT.C do_animobj"),
            F("Door", 0, 2, 1, "A moving door: swings, or a portcullis rises (ANIMF_DOOR).", "obj/EFFECT.C do_animobj"),
            F("Flags 3-4", 0, 3, 2, "Unnamed flag bits.", "include/object.h struct AnimClass", U),
            F("Remove", 0, 5, 1, "Removed from the map when the animation ends (ANIMF_REMOVE).", "obj/EFFECT.C toast_animobj"),
            F("Flag 6", 0, 6, 1, "Unnamed flag bit.", "include/object.h struct AnimClass", U),
            F("Finish", 0, 7, 1, "Plays the remaining frames before it ends (ANIMF_FINISH).", "obj/EFFECT.C toast_animobj"),
            F("Flags byte 1", 1, 0, 8, "The high byte of the flags word. No flag is known there.", "include/object.h struct AnimClass", U),
            F("Start frame", 2, 0, 8, "The first frame (the object's owner field holds the frame). Below 0, the frame is left as it is.", "obj/EFFECT.C add_animobj", C, true),
            F("Frames", 3, 0, 8, "The number of frames. A new animation starts at the start frame plus a random number below this.", "obj/EFFECT.C add_animobj"));

        /// <summary>The tables, in file order (OBJECTS.DAT first, then COMOBJ.DAT). The same in both games.</summary>
        public static readonly ObjectTable[] Layout =
        {
            new ObjectTable("Weapons", ObjectsFile, 0x002, 16, 0x00, Uw1Schemas.MeleeWeapon,
                "The melee weapons, 0x00-0x0F, 8 bytes each. Entry 15 is the bare fist, used when no weapon is held.", "obj/HACK.C hack_init; combat/COMBAT.C GetPlayerWeapon"),
            new ObjectTable("Ranged", ObjectsFile, 0x082, 16, 0x10, Uw1Schemas.Missile,
                "Ammunition (0x10-0x17) and launchers (0x18-0x1F), 3 bytes each. A launcher names its ammunition; the ammunition's damage is done when it hits.", "obj/HACK.C hack_init; combat/MISSILE.C player_fire; motion/OBJPHYS.C missile_newhit"),
            new ObjectTable("Armour", ObjectsFile, 0x0B2, 32, 0x20, Uw1Schemas.Armour,
                "Armour and other wearables, 0x20-0x3F, 4 bytes each. The category decides the paperdoll slot it fits.", "obj/HACK.C hack_init; inv/INVPANEL.C ItemFitsSlot"),
            new ObjectTable("Creatures", ObjectsFile, 0x132, 64, 0x40, Uw1Schemas.Creature,
                "The creatures, 0x40-0x7F, 48 bytes each (include/critter.h struct Creature, the same in both games): armour, hit points, attributes, attacks, senses, loot, experience and spells. New creatures and the monster swap read it.", "critter/CREATURE.C creature_init"),
            new ObjectTable("Containers", ObjectsFile, 0xD32, 16, 0x80, Uw1Schemas.Container,
                "Containers, 0x80-0x8F, 3 bytes each: how much they hold and what they accept.", "obj/MISC.C misc_init; inv/INVPANEL.C ItemFitsSlot"),
            new ObjectTable("Lights", ObjectsFile, 0xD62, 16, 0x90, Uw1Schemas.Light,
                "Light sources, 0x90-0x97 (0-3 unlit, 4-7 lit), then the wands' entries (0x98-0x9F), 2 bytes each. A lit light carried gives its brightness and burns down by its burn rate.", "obj/MISC.C misc_init; game/PLAYTIME.C DegradeLights; game/PLAYDATA.C FixPlayerEquips"),
            new ObjectTable("Food", ObjectsFile, 0xD82, 16, 0xB0, Food,
                "Food, 0xB0-0xBF, one byte each: how much each item nourishes.", "obj/MISC.C misc_init; obj/USEITEMS.C UseFood"),
            new ObjectTable("Triggers", ObjectsFile, 0xD92, 16, 0x1A0, Trigger,
                "Trigger modes, 0x1A0-0x1AF, one byte each (Underworld II's 0x1B0-0x1BF use the same entries).", "event/TRIGGER.C trap_init, trap_class_data"),
            new ObjectTable("Animations", ObjectsFile, 0xDA2, 16, 0x1C0, AnimClass,
                "Animated objects, 0x1C0-0x1CF, 4 bytes each: how their frames run.", "obj/ANIMOBJ.C animobj_load; obj/EFFECT.C"),
            new ObjectTable("Common properties", ComObjFile, 0x002, 512, 0x000, Uw1Schemas.ComObj,
                "COMOBJ.DAT: the properties every item has, 11 bytes each: size, mass, value, flags, quality strings, resistances and how it is drawn.", "obj/OBJCLASS.C init_objects; include/object.h struct ComObj"),
        };

        /// <summary>The size each file must have at least.</summary>
        public static int SizeOf(string file) { return Layout.Where(t => t.FileName == file).Max(t => t.End); }

        public GameSession Session { get; private set; }
        /// <summary>DATA's OBJECTS.DAT and COMOBJ.DAT (both games read them from DATA, also for saves).</summary>
        public string ObjectsPath { get; private set; }
        public string ComObjPath { get; private set; }

        byte[] objects, objectsSaved, comobj, comobjSaved;

        /// <summary>Raised after any change of a value (an edit, a revert, a reload).</summary>
        public event Action Changed;

        ObjectTables() { }

        /// <summary>Reads both files from the game's DATA folder. Throws when one is missing or too short.</summary>
        public static ObjectTables Load(GameSession s)
        {
            var t = new ObjectTables { Session = s };
            t.ObjectsPath = GameProfile.Find(s.DataFolder, ObjectsFile);
            t.ComObjPath = GameProfile.Find(s.DataFolder, ComObjFile);
            if (t.ObjectsPath == null) throw new FileNotFoundException("There is no " + ObjectsFile + " in " + s.DataFolder + ".");
            if (t.ComObjPath == null) throw new FileNotFoundException("There is no " + ComObjFile + " in " + s.DataFolder + ".");
            t.Reload();
            return t;
        }

        /// <summary>Reads both files again, dropping unsaved edits.</summary>
        public void Reload()
        {
            objects = Read(ObjectsPath, ObjectsFile);
            comobj = Read(ComObjPath, ComObjFile);
            objectsSaved = (byte[])objects.Clone();
            comobjSaved = (byte[])comobj.Clone();
            Changed?.Invoke();
        }

        static byte[] Read(string path, string file)
        {
            byte[] d = File.ReadAllBytes(path);
            int need = SizeOf(file);
            if (d.Length < need) throw new InvalidDataException(string.Format("{0} has {1} bytes; it needs {2}.", file, d.Length, need));
            return d;
        }

        /// <summary>The bytes of a table's file as edited (not a copy).</summary>
        public byte[] Bytes(ObjectTable t) { return t.IsComObj ? comobj : objects; }
        byte[] Saved(ObjectTable t) { return t.IsComObj ? comobjSaved : objectsSaved; }

        /// <summary>The bytes the files would be saved with.</summary>
        public byte[] ObjectsBytes { get { return objects; } }
        public byte[] ComObjBytes { get { return comobj; } }

        public static ObjectTable Table(string name) { return Layout.First(t => t.Name == name); }

        public int Get(ObjectTable t, int row, FieldDef f) { return f.Get(Bytes(t), t.OffsetOf(row)); }
        public int Get(string table, int row, string field) { var t = Table(table); return Get(t, row, t.Schema[field]); }

        /// <summary>The value as saved on disk (before the unsaved edits).</summary>
        public int SavedValue(ObjectTable t, int row, FieldDef f) { return f.Get(Saved(t), t.OffsetOf(row)); }

        public bool IsChanged(ObjectTable t, int row, FieldDef f) { return Get(t, row, f) != SavedValue(t, row, f); }

        public bool IsRowChanged(ObjectTable t, int row)
        {
            byte[] a = Bytes(t), b = Saved(t);
            int at = t.OffsetOf(row);
            for (int i = 0; i < t.Schema.Size; i++) if (a[at + i] != b[at + i]) return true;
            return false;
        }

        public bool HasChanges { get { return !objects.SequenceEqual(objectsSaved) || !comobj.SequenceEqual(comobjSaved); } }

        /// <summary>How many records differ from the files on disk.</summary>
        public int ChangedRows { get { int n = 0; foreach (var t in Layout) for (int r = 0; r < t.Count; r++) if (IsRowChanged(t, r)) n++; return n; } }

        /// <summary>
        /// Sets a field. Returns null when done, else why not (the value is out of the field's
        /// range or one the game cannot use); nothing changes then.
        /// </summary>
        public string Set(ObjectTable t, int row, FieldDef f, int value)
        {
            if (row < 0 || row >= t.Count) return "No such row.";
            string err = Check(t, f, value);
            if (err != null) return err;
            if (Get(t, row, f) == value) return null;
            f.Set(Bytes(t), value, t.OffsetOf(row));
            Changed?.Invoke();
            return null;
        }

        public string Set(string table, int row, string field, int value) { var t = Table(table); return Set(t, row, t.Schema[field], value); }

        /// <summary>Parses and sets a cell's text: decimal, or hexadecimal with 0x.</summary>
        public string SetText(ObjectTable t, int row, FieldDef f, string text)
        {
            int v;
            if (!TryParse(text, out v)) return string.Format("{0}: \"{1}\" is not a number.", f.Name, (text ?? "").Trim());
            return Set(t, row, f, v);
        }

        /// <summary>Why a value cannot go in a field, or null. Out of the field's bits, or a value the game has no use for.</summary>
        public string Check(ObjectTable t, FieldDef f, int value)
        {
            if (value < f.Min || value > f.Max)
                return string.Format("{0} must be {1} to {2}.", f.Name, f.Min, f.Max);
            if (t.Schema == Uw1Schemas.Container && f.Name == "Accepts")
            {
                // include/object.h CONT_*: -1 anything, an item id, or a kind 0x200-0x204 (inv/INVPANEL.C ItemFitsSlot)
                if (value < -1 || value > 0x204)
                    return "Accepts must be -1 (anything), an item id 0-511 (0x1FF) or a kind 0x200-0x204.";
            }
            return null;
        }

        /// <summary>A note on a value the game takes but which may not do what is meant, or null.</summary>
        public string Warning(ObjectTable t, FieldDef f, int value)
        {
            if (t.Schema == Uw1Schemas.MeleeWeapon && f.Name == "Skill" && (value < 3 || value > 6))
                return "The weapon skills are 3 sword, 4 axe, 5 mace and 6 unarmed.";
            if (t.Schema == Uw1Schemas.Armour && f.Name == "Category" && Array.IndexOf(ArmourSlots, value) < 0)
                return "No paperdoll slot takes category " + value + ": it cannot be worn.";
            if (t.Schema == Uw1Schemas.Container && f.Name == "Accepts" && value == 0x204 && !Session.IsUw2)
                return "0x204 (keys) is Underworld II's key ring; Ultima Underworld has no case for it.";
            if (t.Schema == Uw1Schemas.Light && f.Name == "Brightness" && value > 7)
                return "The light levels are 0-7 (SHADES.DAT has 8 rows).";
            if (t.Schema == Trigger && (value == 3 || value > 7))
                return "No action reports mode " + value + ": the trigger never fires.";
            return null;
        }

        static readonly int[] ArmourSlots = { 0, 1, 3, 4, 5, 8, 9 };

        /// <summary>A short reading of a value ("sword", "anything", an item's name), or null.</summary>
        public string Meaning(ObjectTable t, FieldDef f, int value)
        {
            var s = t.Schema;
            if (s == Uw1Schemas.MeleeWeapon && f.Name == "Skill")
                return value == 3 ? "sword" : value == 4 ? "axe" : value == 5 ? "mace" : value == 6 ? "unarmed" : null;
            if (s == Uw1Schemas.MeleeWeapon && f.Name == "Durability" && value == 255) return "unbreakable";
            if (s == Uw1Schemas.Armour && f.Name == "Durability" && value == 255) return "unbreakable";
            if (s == Uw1Schemas.Armour && f.Name == "Category")
                return value == 0 ? "shield" : value == 1 ? "body armour" : value == 3 ? "leggings" : value == 4 ? "gloves" : value == 5 ? "boots" : value == 8 ? "hat" : value == 9 ? "ring" : null;
            if (s == Uw1Schemas.Missile && f.Name == "Ammo / damage type" && value >= 0 && value < 0x10) return "fires " + ItemName(0x10 + value);
            if (s == Uw1Schemas.Container && f.Name == "Accepts")
            {
                if (value == -1) return "anything";
                if (value >= 0 && value < 0x200) return "only " + ItemName(value);
                switch (value) { case 0x200: return "runestones"; case 0x201: return "missiles"; case 0x202: return "scrolls and maps"; case 0x203: return "food"; case 0x204: return "keys"; }
                return null;
            }
            if (s == Uw1Schemas.Container && f.Name == "Capacity" && value == 0) return "no limit";
            if (s == Uw1Schemas.Light && f.Name == "Burn rate" && value == 0) return "never burns down";
            if (s == Trigger)
                switch (value) { case 0: return "move"; case 1: return "step on"; case 2: return "pick up"; case 4: return "use"; case 5: return "look"; case 6: return "unlock"; case 7: return "open"; }
            if (s == Uw1Schemas.Creature)
            {
                // obj/TREASURE.C generate_weapons: the item is (item >> 4 & 3) << 4 | item & 0xF, a weapon, missile or armour;
                // generate_equipment: the loot item is an item id as it is
                if (f.Name == "Arms 1 item" || f.Name == "Arms 2 item") return ItemName(value & 0x3F);
                if (f.Name == "Loot 1 item" || f.Name == "Loot 2 item") return value < 0x200 ? ItemName(value) : null;
                if (f.Name == "Food item") return ItemName(0xB0 + value);
                if (f.Name == "Race") return value == 0 ? "none" : null;
                if (f.Name.StartsWith("Spell", StringComparison.Ordinal) && value == 255) return "none";
            }
            if (s == Uw1Schemas.ComObj)
            {
                if (f.Name == "Render") return value == 0 ? "sprite" : value == 1 ? "critter" : value == 2 ? "3D model or door" : "texture-mapped model";
                if (f.Name == "Fate")
                    return value == 0 ? "stays where it lands" : value <= 8 ? value + " in 8 chance that it vanishes on landing" : value == 9 ? "runs its landing animation" : value == 10 ? "talisman (Ultima Underworld)" : null;
                if (f.Name == "Mass") return (value / 10.0).ToString("0.0", CultureInfo.InvariantCulture) + " stones";
                if (f.Name == "Quality type" && Session.Strings != null)
                {
                    // obj/LOOK.C LookAt: string (type * 6 + q) | STR_OBJLOOK (block 5), q = quality / 16 + 1
                    var words = Enumerable.Range(1, 4).Select(q => Session.Strings.Get(5, value * 6 + q)).Where(w => !string.IsNullOrWhiteSpace(w)).ToList();
                    return words.Count == 0 ? null : string.Join(" / ", words);
                }
            }
            return null;
        }

        string ItemName(int item) { return string.Format("0x{0:X3} {1}", item, Session.Names.Get(item)); }

        /// <summary>The explanation of a field for the window: what it means, where it is, its range and source.</summary>
        public static string Explain(ObjectTable t, FieldDef f)
        {
            string where = string.Format("{0}, byte {1} of each {2}-byte record", t.FileName, f.Location, t.Schema.Size);
            string sure = f.Certainty == Certainty.Certain ? "" : f.Certainty == Certainty.Inferred ? " This is inferred from how the code uses it." : " Its meaning is not known.";
            return string.Format("{0}: {1}{2} Range {3} to {4}. {5}. Source: {6}.", f.Name, f.Description, sure, f.Min, f.Max, where, f.Source ?? t.Source);
        }

        public static bool TryParse(string text, out int value)
        {
            value = 0;
            if (text == null) return false;
            string s = text.Trim();
            bool neg = s.StartsWith("-", StringComparison.Ordinal);
            if (neg) s = s.Substring(1).Trim();
            long v;
            bool ok = s.StartsWith("0x", StringComparison.OrdinalIgnoreCase)
                ? long.TryParse(s.Substring(2), NumberStyles.HexNumber, CultureInfo.InvariantCulture, out v)
                : long.TryParse(s, NumberStyles.None, CultureInfo.InvariantCulture, out v);
            if (!ok || s.Length == 0) return false;
            if (neg) v = -v;
            if (v < int.MinValue || v > int.MaxValue) return false;
            value = (int)v;
            return true;
        }

        /// <summary>Puts a record back as it is on disk.</summary>
        public void RevertRow(ObjectTable t, int row)
        {
            Array.Copy(Saved(t), t.OffsetOf(row), Bytes(t), t.OffsetOf(row), t.Schema.Size);
            Changed?.Invoke();
        }

        /// <summary>Drops every unsaved edit.</summary>
        public void RevertAll()
        {
            Array.Copy(objectsSaved, objects, objects.Length);
            Array.Copy(comobjSaved, comobj, comobj.Length);
            Changed?.Invoke();
        }

        /// <summary>
        /// Writes the changed files (SafeFile: NAME.EXT.ORIG once, NAME.EXT.BKP) and refreshes what
        /// the session keeps from them (COMOBJ.DAT's render types, the can-own bits). Returns the
        /// names of the files written.
        /// </summary>
        public List<string> Save()
        {
            var written = new List<string>();
            if (!objects.SequenceEqual(objectsSaved))
            {
                SafeFile.Write(ObjectsPath, objects);
                objectsSaved = (byte[])objects.Clone();
                written.Add(ObjectsFile);
            }
            if (!comobj.SequenceEqual(comobjSaved))
            {
                SafeFile.Write(ComObjPath, comobj);
                comobjSaved = (byte[])comobj.Clone();
                written.Add(ComObjFile);
            }
            if (written.Count > 0)
            {
                Session.ReloadObjectData();
                Races.Forget(Session.DataFolder);
                Changed?.Invoke();
            }
            return written;
        }
    }
}
