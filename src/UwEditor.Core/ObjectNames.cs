using System;
using System.Collections.Generic;
using System.IO;
using UltimateEditor.Games;

namespace UwEditor.Core
{
    /// <summary>
    /// The item names, STRINGS.PAK block 4, one string an item id in both games. A name is
    /// "article_name&amp;plural" ("a_lever&amp;levers"); <see cref="Get"/> gives the bare name.
    /// </summary>
    public sealed class ObjectNames
    {
        readonly string[] raw;

        ObjectNames(string[] raw) { this.raw = raw; }

        public static ObjectNames Load(string dataFolder)
        {
            return From(GameStrings.Load(dataFolder));
        }

        public static ObjectNames From(GameStrings strings)
        {
            return new ObjectNames(strings.Block(4));
        }

        public int Count { get { return raw.Length; } }

        /// <summary>The string as stored.</summary>
        public string Raw(int item) { return item >= 0 && item < raw.Length ? raw[item] : null; }

        /// <summary>The singular name without its article, or "item 0x1A3" when there is none.</summary>
        public string Get(int item)
        {
            string s = Raw(item);
            if (string.IsNullOrEmpty(s)) return string.Format("item 0x{0:X3}", item);
            int amp = s.IndexOf('&');
            if (amp >= 0) s = s.Substring(0, amp);
            int us = s.IndexOf('_');
            if (us >= 0 && us <= 5)
            {
                string article = s.Substring(0, us).ToLowerInvariant();
                if (article == "a" || article == "an" || article == "the" || article == "some") s = s.Substring(us + 1);
            }
            s = s.Replace('_', ' ').Trim();
            return s.Length == 0 ? string.Format("item 0x{0:X3}", item) : s;
        }
    }
}
