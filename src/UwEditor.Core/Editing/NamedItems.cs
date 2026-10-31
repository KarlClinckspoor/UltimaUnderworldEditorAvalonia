using System.Collections.Generic;
using UltimateEditor.Games;

namespace UwEditor.Core.Editing
{
    /// <summary>
    /// Proper names of unique objects whose game name is generic ("a shiny sword"), for the
    /// object list. The game itself names them only in conversations and in the plot: UW1's
    /// talismans are the objects whose COMOBJ.DAT fate is 10 (OBJPHYS.C talisman_ok), Caliburn is
    /// made from the broken blade and hilt by Shak (conversation 2), and so on. Creatures' names
    /// come from their conversation (block 7) instead; see ObjectTexts.NpcName.
    /// </summary>
    public static class NamedItems
    {
        static readonly Dictionary<int, string> uw1 = new Dictionary<int, string>
        {
            { 0x00A, "Caliburn, the Sword of Justice" },
            { 0x118, "the blade of Caliburn" },
            { 0x119, "the hilt of Caliburn" },
            { 0x036, "the Ring of Humility" },
            { 0x037, "the Shield of Valor" },
            { 0x093, "the Taper of Sacrifice" },
            { 0x097, "the Taper of Sacrifice, lit" },
            { 0x0AE, "the Cup of Wonder" },
            { 0x0BF, "the Wine of Compassion" },
            { 0x11F, "the Standard of Honor" },
            { 0x136, "the Book of Honesty" },
            { 0x0E4, "a piece of the Key of Infinity" },
            { 0x0E5, "a piece of the Key of Infinity" },
            { 0x0E6, "a piece of the Key of Infinity" },
        };

        /// <summary>The proper name of an object of this item, or null.</summary>
        public static string Of(GameKind kind, int item)
        {
            string n;
            return kind == GameKind.UW1 && uw1.TryGetValue(item, out n) ? n : null;
        }

        /// <summary>The name to add in parentheses for an object in a list: its conversation name, or its proper name.</summary>
        public static string ExtraName(GameSession s, ObjectInfo o)
        {
            return ObjectTexts.NpcName(s, o) ?? Of(s.Kind, o.Item);
        }
    }
}
