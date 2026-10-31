using System;
using System.Collections.Generic;
using System.IO;
using UltimateEditor.Games;

namespace UwEditor.Core
{
    /// <summary>
    /// Every block of STRINGS.PAK, by block number. The ones the editor reads: 3 books and
    /// scrolls, 4 item names, 7 creature (NPC) names, 8 gravestones and writings, 9 text traps.
    /// </summary>
    public sealed class GameStrings
    {
        readonly Dictionary<int, string[]> blocks = new Dictionary<int, string[]>();

        public static GameStrings Load(string dataFolder)
        {
            var g = new GameStrings();
            string path = GameProfile.Find(dataFolder, "STRINGS.PAK");
            if (path == null) return g;
            using (var fs = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read))
            {
                var nodes = StringsPak.ReadHuffmanNodes(fs);
                var list = StringsPak.ReadStringBlocks(fs, nodes.Item2);
                foreach (var b in list.Item1)
                {
                    try { g.blocks[b.BlockNumber] = StringsPak.ReadStrings(fs, b, nodes.Item1).ToArray(); }
                    catch (Exception) { g.blocks[b.BlockNumber] = new string[0]; }
                }
            }
            return g;
        }

        public string[] Block(int number)
        {
            string[] b;
            return blocks.TryGetValue(number, out b) ? b : new string[0];
        }

        /// <summary>A string, or null when the block or the index is not there.</summary>
        public string Get(int block, int index)
        {
            var b = Block(block);
            return index >= 0 && index < b.Length ? b[index] : null;
        }
    }

    /// <summary>
    /// The text that goes with an object, where it has one (from the Unity editor's StringData,
    /// which follows the reference): a book or scroll's text is block 3 string link − 0x200
    /// (not for an enchanted scroll, whose link is a spell); a gravestone (0x165) is block 8
    /// string link − 0x200 (1-63), a writing (0x166) the same (64-351); a text string trap
    /// (0x190) is block 9 string quality &lt;&lt; 5 | owner (UW2 takes the owner's low 5 bits;
    /// event/TRIGGER.C). A creature with a conversation slot is named by block 7 string whoami + 16.
    /// </summary>
    public static class ObjectTexts
    {
        public static string TextOf(GameSession s, ObjectInfo o)
        {
            var cat = ItemCategories.Of(o.Item, s.Kind);
            int special = o.Link - 0x200;
            if ((cat & (ItemCategory.Book | ItemCategory.Scroll)) != 0)
                return o.IsQuantity && !o.Enchanted && special >= 0 ? s.Strings.Get(3, special) : null;
            if (o.Item == 0x165 && o.IsQuantity && special >= 1 && special < 64) return s.Strings.Get(8, special);
            if (o.Item == 0x166 && o.IsQuantity && special >= 64 && special < 352) return s.Strings.Get(8, special);
            if (o.Item == 0x190)
            {
                int owner = s.IsUw2 ? o.Owner & 0x1F : o.Owner & 0x3F;
                return s.Strings.Get(9, (o.Quality & 0x3F) << 5 | owner);
            }
            return null;
        }

        /// <summary>The name a creature talks under (its conversation slot), or null.</summary>
        public static string NpcName(GameSession s, ObjectInfo o)
        {
            if (!o.IsMobile || !o.IsCritter || o.Record.Length < 0x1B) return null;
            int whoami = o.Record[0x1A];
            if (whoami <= 0) return null;
            string n = s.Strings.Get(7, whoami + 16);
            return string.IsNullOrWhiteSpace(n) ? null : n.Trim();
        }
    }
}
