using System;
using System.Collections.Generic;
using System.Linq;
using UltimateEditor.Uw1;

namespace UwEditor.Core
{
    /// <summary>One line of a properties panel: a field, its value and where its meaning comes from.</summary>
    public sealed class PropertyRow
    {
        public string Name { get; set; }
        public string Value { get; set; }
        public string Hex { get; set; }
        public string Tip { get; set; }
        /// <summary>"", "inferred" or "unknown": how sure the meaning is.</summary>
        public string Certainty { get; set; }
        public bool IsHeader { get; set; }
        /// <summary>The record field the row shows, when it can be typed into.</summary>
        public FieldDef Field { get; set; }
        /// <summary>"object" or "tile": what Field belongs to.</summary>
        public string Target { get; set; }
        /// <summary>The object's index, or the tile's x + 64 y.</summary>
        public int TargetIndex { get; set; }
        public bool Editable { get { return Field != null; } }
        public bool ReadOnly { get { return Field == null; } }
        /// <summary>Values to pick from (an owner's race), or null.</summary>
        public List<UwEditor.Core.Editing.Choice> Choices { get; set; }
        public bool HasChoices { get { return Field != null && Choices != null; } }
    }

    /// <summary>Builds the panels' rows from the record schemas, so every field shows, described.</summary>
    public static class PropertyRows
    {
        public static List<PropertyRow> FromSchema(RecordSchema schema, byte[] record, string target = null, int index = -1)
        {
            var rows = new List<PropertyRow>();
            foreach (var f in schema.Fields)
            {
                int v = f.Get(record);
                bool editable = target != null && f.Name != "Next" && f.Name != "First object";
                rows.Add(new PropertyRow
                {
                    Field = editable ? f : null, Target = target, TargetIndex = index,
                    Name = f.Name,
                    Value = v.ToString(),
                    Hex = f.Bits > 3 ? "0x" + v.ToString(f.Bits > 8 ? "X3" : "X2") : "",
                    Tip = f.Description + (string.IsNullOrEmpty(f.Source) ? "" : "\nSource: " + f.Source)
                        + string.Format("\nBits: offset 0x{0:X2}, bit {1}, {2} bit{3}", f.Offset, f.Bit, f.Bits, f.Bits == 1 ? "" : "s"),
                    Certainty = f.Certainty == UltimateEditor.Uw1.Certainty.Certain ? "" : f.Certainty == UltimateEditor.Uw1.Certainty.Inferred ? "inferred" : "unknown",
                });
            }
            return rows;
        }

        public static List<PropertyRow> ForObject(LevelModel level, ObjectInfo o)
        {
            var rows = new List<PropertyRow>();
            Add(rows, "Index", o.Index.ToString(), "0x" + o.Index.ToString("X3"), "The object's slot in the level: 0-255 mobile (0x1B bytes), 256-1023 static (8 bytes).");
            Add(rows, "Kind", o.IsMobile ? "mobile" : "static", "", "Mobile objects (critters, missiles, moving things) have 19 more bytes.");
            string where = o.TileX < 0 ? (o.Free ? "free (unused)" : "not on any tile list") : string.Format("tile ({0}, {1})", o.TileX, o.TileY);
            Add(rows, "Where", where, "", "The tile whose object list holds it.");
            if (o.Parent >= 0) Add(rows, "Inside", level.Objects[o.Parent].ToString(), "", "Reached through this object's link list (container contents, inventory, a trigger's trap, a door's lock).");
            string npc = ObjectTexts.NpcName(level.Session, o);
            if (npc != null) Add(rows, "Name", npc, "", "The name the creature talks under: STRINGS.PAK block 7, string whoami + 16.");
            string proper = UwEditor.Core.Editing.NamedItems.Of(level.Session.Kind, o.Item);
            if (proper != null) Add(rows, "Known as", proper, "", "What the plot calls this object (the game's own name for it is generic).");
            string text = ObjectTexts.TextOf(level.Session, o);
            if (text != null) Add(rows, "Text", text.Trim(), "", "The object's text in STRINGS.PAK: block 3 for books and scrolls, 8 for gravestones and writings, 9 for text traps.");
            bool flag11 = (o.Record[1] & 0x08) != 0;
            string ench = Editing.Enchantments.NameOf(o.Item, o.Enchanted, o.IsQuantity, o.Link, flag11, level.Session.Strings);
            if (ench != null) Add(rows, "Enchantment", ench, "", "STRINGS.PAK block 6, by decode_obj_spell (obj/USEITEMS.C)." + (level.Session.IsUw2 ? " UW2's names do not always say what the enchantment does." : ""));
            if (Editing.KeyIndex.IsKey(o.Item, level.Session.Kind))
            {
                string kt = level.Session.Strings.Get(5, 0x64 + o.Owner);
                Add(rows, "Opens lock", o.Owner.ToString(), "", "A key opens the lock whose number (link & 0x1FF) equals its owner field (obj/USEITEMS.C checkLock)." + (string.IsNullOrWhiteSpace(kt) ? "" : "\nDescription: " + kt));
            }
            if (o.Item == 0x10F && o.IsQuantity) Add(rows, "Lock number", (o.Link & 0x1FF).ToString(), "", "Keys whose owner field is this number open it.");
            Add(rows, "Kind of item", KindName(ItemCategories.Of(o.Item, level.Session.Kind)), "", "The class of the item id (include/items.h).");
            if (o.TileX >= 0)
                Add(rows, "Position", string.Format("{0:0.###}, {1:0.###}, h {2:0.###}", o.WorldX, o.WorldY, o.WorldZ), "", "In tiles: x east, y north, height (a floor step is 0.25).");
            rows.Add(new PropertyRow { Name = "Record", IsHeader = true, Value = "", Hex = "", Tip = "" });
            var fields = FromSchema(o.IsMobile ? Uw1Schemas.MobileObject : Uw1Schemas.StaticObject, o.Record, "object", o.Index);
            var ownerRow = fields.FirstOrDefault(r => r.Name == "Owner");
            if (ownerRow != null && !o.IsMobile && !UwEditor.Core.Editing.KeyIndex.IsKey(o.Item, level.Session.Kind) && o.Item < 0x180
                && UwEditor.Core.Editing.Races.CanBeOwned(level.Session, o.Item))
            {
                var races = UwEditor.Core.Editing.Races.Of(level.Session);
                ownerRow.Choices = races;
                var r = races.FirstOrDefault(c => c.Value == o.Owner);
                ownerRow.Tip = "The race that owns it: taking it in front of them is stealing (OBJECTS.DAT creature byte 9; COMOBJ.DAT's can-own bit is set for this kind)."
                    + (r != null ? "\nNow: " + r.Label : "") + "\n" + ownerRow.Tip;
            }
            rows.AddRange(fields);
            rows.Add(new PropertyRow { Name = "Bytes", Value = BitConverter.ToString(o.Record).Replace("-", " "), Hex = "", Tip = "The record as stored." });
            return rows;
        }

        public static List<PropertyRow> ForTile(LevelModel level, TileInfo t)
        {
            var rows = new List<PropertyRow>();
            Add(rows, "Tile", string.Format("({0}, {1})", t.X, t.Y), "", "x east, y north; (0, 0) is the south-west corner.");
            Add(rows, "Type", TileInfo.TypeName(t.Type), "", "include/map.h enum TileType.");
            int wall = level.WallTextureNumber(t.WallIndex), floor = level.FloorTextureNumber(t.FloorIndex);
            Add(rows, "Wall texture", wall < 0 ? "none (index " + t.WallIndex + ")" : string.Format("{0} (index {1})", wall, t.WallIndex), "",
                "The texture number in " + level.Session.Profile.WallTextureFile + ", through the level's texture map.");
            Add(rows, "Floor texture", floor < 0 ? "none (index " + t.FloorIndex + ")" : string.Format("{0} (index {1})", floor, t.FloorIndex), "",
                "The texture number in " + level.Session.Profile.FloorTextureFile + ", through the level's texture map.");
            rows.Add(new PropertyRow { Name = "Record", IsHeader = true, Value = "", Hex = "", Tip = "" });
            rows.AddRange(FromSchema(Uw1Schemas.Tile, t.Record, "tile", t.X + 64 * t.Y));
            return rows;
        }

        static string KindName(ItemCategory c)
        {
            foreach (var choice in ItemCategories.Choices)
                if (choice.Key != ItemCategory.All && (choice.Key & c) == c) return choice.Value;
            return c.ToString();
        }

        static void Add(List<PropertyRow> rows, string name, string value, string hex, string tip)
        {
            rows.Add(new PropertyRow { Name = name, Value = value, Hex = hex, Tip = tip, Certainty = "" });
        }
    }
}
