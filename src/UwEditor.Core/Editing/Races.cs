using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UltimateEditor.Games;

namespace UwEditor.Core.Editing
{
    /// <summary>A value with a label, for lists of choices.</summary>
    public sealed class Choice
    {
        public int Value;
        public string Label;
        public override string ToString() { return Label; }
    }

    /// <summary>
    /// The owners an object can have. An ownable object's owner field (6 bits) is a race: the
    /// race byte of the creatures' OBJECTS.DAT records (byte 9, include/critter.h race); taking
    /// it in front of that race is stealing. Owner 0 is nobody. Races have no names in the games'
    /// strings, so each is named by the creatures that belong to it.
    /// </summary>
    public static class Races
    {
        public static List<Choice> Of(GameSession s)
        {
            var list = new List<Choice> { new Choice { Value = 0, Label = "0: nobody" } };
            string p = GameProfile.Find(s.DataFolder, "OBJECTS.DAT");
            if (p == null) return list;
            byte[] d;
            try { d = File.ReadAllBytes(p); } catch (IOException) { return list; }
            var byRace = new SortedDictionary<int, List<string>>();
            for (int i = 0; i < 64; i++)
            {
                int at = 0x132 + 0x30 * i;
                if (at + 0x30 > d.Length) break;
                int race = d[at + 9];
                if (race == 0 || race > 63) continue;
                string n = s.Names.Get(0x40 + i);
                if (string.IsNullOrWhiteSpace(n) || n.Contains("_")) continue;
                List<string> l;
                if (!byRace.TryGetValue(race, out l)) byRace[race] = l = new List<string>();
                if (!l.Contains(n)) l.Add(n);
            }
            foreach (var kv in byRace) list.Add(new Choice { Value = kv.Key, Label = kv.Key + ": " + string.Join(", ", kv.Value) });
            return list;
        }

        /// <summary>COMOBJ.DAT's can-own bit (byte 7 bit 7 of the item's 11 bytes).</summary>
        public static bool CanBeOwned(GameSession s, int item)
        {
            var d = ComObj(s);
            int at = 2 + 11 * item + 7;
            return d != null && at < d.Length && (d[at] & 0x80) != 0;
        }

        static readonly Dictionary<string, byte[]> comobj = new Dictionary<string, byte[]>();

        static byte[] ComObj(GameSession s)
        {
            lock (comobj)
            {
                byte[] d;
                if (comobj.TryGetValue(s.DataFolder, out d)) return d;
                string p = GameProfile.Find(s.DataFolder, "COMOBJ.DAT");
                try { d = p == null ? null : File.ReadAllBytes(p); } catch (IOException) { d = null; }
                comobj[s.DataFolder] = d;
                return d;
            }
        }
    }
}
