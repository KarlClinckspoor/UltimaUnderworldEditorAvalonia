using System;
using System.IO;
using System.Linq;
using UltimateEditor.Games;
using UwEditor.Core.Player;
using Xunit;
using Xunit.Abstractions;

namespace UwEditor.Core.Tests
{
    public class PlayerTests
    {
        readonly ITestOutputHelper output;
        public PlayerTests(ITestOutputHelper output) { this.output = output; }

        static string Save(string n)
        {
            string g = GameData.Game("UW2");
            string p = g == null ? null : Path.Combine(g, n, "PLAYER.DAT");
            return p != null && File.Exists(p) ? p : null;
        }

        /// <summary>A UW1 PLAYER.DAT made from nothing (no UW1 save ships): a Fighter with an empty inventory.</summary>
        static byte[] Uw1File(string name, int str, int dex, int intel)
        {
            var rec = new byte[0xD2];
            for (int i = 0; i < name.Length; i++) rec[i] = (byte)name[i];
            rec[0x1E] = (byte)str; rec[0x1F] = (byte)dex; rec[0x20] = (byte)intel;
            rec[0x3D] = 1; rec[0x36] = (byte)(30 + str / 5); rec[0x35] = 20;
            rec[0x4C] = (byte)(str * 20); rec[0x4D] = (byte)(str * 20 >> 8);
            rec[0x38] = (byte)(intel >> 3);
            byte key = (byte)(rec[0] ^ 0xAA);
            var rest = new byte[2 + 0x5B + 8];
            rest[0] = 1;
            rest[2 + 8] = 20;
            return new[] { key }.Concat(PlayerDat.Cipher(rec, key, GameKind.UW1, true)).Concat(rest).ToArray();
        }

        [Fact]
        public void Uw1_story_bits_are_byte_0x62_and_the_talismans_left_byte_0x6D()
        {
            var sheet = PlayerSheet.FromBytes(Uw1File("avatar", 20, 20, 20), GameKind.UW1, null);
            Assert.True(sheet.HasStoryBits);
            Assert.False(sheet.GaramonBuried);
            Assert.False(sheet.TalismansCanBurn);
            Assert.Equal(0, sheet.TalismansLeft);
            sheet.Dat.Record[0x62] = 0x03;      // other bits of the byte (the drink level) are kept
            sheet.TalismansCanBurn = true;
            Assert.Equal(0x07, sheet.Dat.Record[0x62]);
            sheet.GaramonBuried = true;
            Assert.Equal(0x0F, sheet.Dat.Record[0x62]);      // a burial as the game makes it: 0x0C
            sheet.TalismansLeft = 8;
            Assert.Equal(8, sheet.Dat.Record[0x6D]);
            Assert.True(sheet.Dirty);
            // written and read back
            var again = PlayerSheet.FromBytes(sheet.ToBytes(), GameKind.UW1, null);
            Assert.True(again.GaramonBuried && again.TalismansCanBurn);
            Assert.Equal(8, again.TalismansLeft);
            again.TalismansCanBurn = false;
            Assert.Equal(0x0B, again.Dat.Record[0x62]);
            Assert.True(again.GaramonBuried);

            // byte 0x60: armageddon, orb, key, cup in bits 4-7; its low bits (spells, runes) are kept
            again.Dat.Record[0x60] = 0x0F;
            again.OrbDestroyed = true; Assert.Equal(0x2F, again.Dat.Record[0x60]);
            again.KeyOfTruthGiven = true; Assert.Equal(0x6F, again.Dat.Record[0x60]);
            again.CupFound = true; Assert.Equal(0xEF, again.Dat.Record[0x60]);
            again.ArmageddonCast = true; Assert.Equal(0xFF, again.Dat.Record[0x60]);
            again.OrbDestroyed = false; Assert.Equal(0xDF, again.Dat.Record[0x60]);
            Assert.True(again.KeyOfTruthGiven && again.CupFound && again.ArmageddonCast && !again.OrbDestroyed);
            // the maze spell, byte 0x62 bit 4, beside the Garamon bits
            again.MazeSpell = true; Assert.Equal(0x1B, again.Dat.Record[0x62]);
            // byte 0x5E: the moonstone's level low, the silver tree's high
            again.Dat.Record[0x5E] = 2;
            Assert.Equal(2, again.MoonstoneLevel); Assert.Equal(0, again.SilverTreeLevel);
            again.SilverTreeLevel = 5; Assert.Equal(0x52, again.Dat.Record[0x5E]);
            again.MoonstoneLevel = 7; Assert.Equal(0x57, again.Dat.Record[0x5E]);
            Assert.Throws<ArgumentOutOfRangeException>(() => again.MoonstoneLevel = 16);
            // the dreams, word 0x6E
            again.Dreams = 0x203; Assert.Equal(0x03, again.Dat.Record[0x6E]); Assert.Equal(0x02, again.Dat.Record[0x6F]);
            var third = PlayerSheet.FromBytes(again.ToBytes(), GameKind.UW1, null);
            Assert.Equal(7, third.MoonstoneLevel); Assert.Equal(5, third.SilverTreeLevel); Assert.Equal(0x203, third.Dreams);
            Assert.True(third.MazeSpell && third.CupFound);

            // UW2 keeps other things there
            string p = Save("SAVE1"); if (p == null) return;
            var uw2 = PlayerSheet.Load(p, GameKind.UW2, null);
            Assert.False(uw2.HasStoryBits);
            Assert.False(uw2.TalismansCanBurn);
            Assert.Throws<InvalidOperationException>(() => uw2.GaramonBuried = true);
        }

        [Fact]
        public void Uw2_saves_read_with_their_inventories()
        {
            var p = Save("SAVE1"); if (p == null) return;
            var s = PlayerSheet.Load(p, GameKind.UW2, Path.Combine(GameData.Game("UW2"), "DATA"));
            output.WriteLine("{0}: {1} {2}/{3}/{4}, level {5}, {6} objects", s.Name, PlayerSheet.ClassNames[s.Class], s.Strength, s.Dexterity, s.Intelligence, s.Level, s.Inventory.All().Count());
            Assert.Equal(7, s.Inventory.All().Count());
            Assert.Equal(0, s.Inventory.Unreached);
            var bag = s.Inventory.Top.Single(t => t.ItemId == 0x80);
            Assert.Equal(PlayerInventory.PackFirst, bag.Slot);
            Assert.Equal(2, bag.Contents.Count);
            Assert.All(s.Inventory.Top, t => Assert.True(t.Slot >= 0));
            // nothing changed: the same bytes
            Assert.Equal(File.ReadAllBytes(p), s.ToBytes());
            // a shipped character is reachable by creation
            Assert.DoesNotContain(s.Check(), w => w.Level != WarningLevel.Note);
        }

        [Fact]
        public void Rewriting_an_unchanged_inventory_numbers_it_as_the_game_does()
        {
            foreach (var n in new[] { "SAVE1", "SAVE2" })
            {
                var p = Save(n); if (p == null) return;
                var orig = File.ReadAllBytes(p);
                var s = PlayerSheet.FromBytes(orig, GameKind.UW2, null);
                s.Inventory.Changed = true;
                var again = s.ToBytes();
                Assert.Equal(orig.Length, again.Length);
                // only slots 19-27 (an open bag's view) may differ: file offset 1 + 0x37D + 2 + 0x23 + 2 * 19
                int bagView = 1 + 0x37D + 2 + 0x23 + 2 * 19;
                for (int i = 0; i < orig.Length; i++)
                    if (i < bagView || i >= bagView + 18) Assert.True(orig[i] == again[i], n + ": byte " + i.ToString("X") + " changed");
            }
        }

        [Fact]
        public void Adding_and_removing_objects_renumbers_links_and_slots()
        {
            var p = Save("SAVE2"); if (p == null) return;
            var s = PlayerSheet.FromBytes(File.ReadAllBytes(p), GameKind.UW2, null);
            int before = s.Inventory.All().Count();
            var bag = s.Inventory.Top.First(t => t.ItemId == 0x80);
            var coins = s.Inventory.Add(0xA0, bag, false);
            coins.Link = 250;
            var sword = s.Inventory.Add(0x04, null, false);
            Assert.InRange(sword.Slot, PlayerInventory.PackFirst, PlayerInventory.PackLast);
            s.Inventory.Remove(s.Inventory.Top.First(t => t.ItemId == 0x20));
            var back = PlayerSheet.FromBytes(s.ToBytes(), GameKind.UW2, null);
            Assert.Equal(before + 1, back.Inventory.All().Count());
            Assert.Contains(back.Inventory.Top.First(t => t.ItemId == 0x80).Contents, c => c.ItemId == 0xA0 && c.Quantity == 250);
            Assert.Equal(sword.Slot, back.Inventory.Top.Single(t => t.ItemId == 0x04).Slot);
            Assert.DoesNotContain(back.Inventory.All(), t => t.ItemId == 0x20);
            Assert.Equal(0, back.Inventory.Unreached);
            // the record is untouched
            Assert.Equal(s.Dat.Record, back.Dat.Record);
        }

        [Fact]
        public void Moving_objects_between_containers_and_slots_survives_saving()
        {
            var p = Save("SAVE2"); if (p == null) return;
            var s = PlayerSheet.FromBytes(File.ReadAllBytes(p), GameKind.UW2, null);
            var inv = s.Inventory;
            int before = inv.All().Count();
            var bag = inv.Top.First(t => t.ItemId == 0x80);
            var inner = inv.Add(0x81, bag, true);
            var coins = inv.Add(0xA0, bag, false);
            // into a container, before another object
            inv.MoveInto(coins, inner);
            Assert.Same(inner, coins.Parent);
            Assert.DoesNotContain(coins, bag.Contents);
            var gem = inv.Add(0xA6, inner, false);
            inv.MoveInto(gem, inner, coins);
            Assert.Equal(new[] { gem, coins }, inner.Contents);
            Assert.Throws<InvalidOperationException>(() => inv.MoveInto(bag, inner));
            Assert.Throws<InvalidOperationException>(() => inv.MoveInto(bag, coins));
            // out to a free slot, then onto a taken one: the object there takes the old slot
            int free = inv.FreePackSlot();
            inv.MoveToSlot(gem, free);
            Assert.Null(gem.Parent);
            Assert.Equal(free, gem.Slot);
            int bagSlot = bag.Slot;
            inv.MoveToSlot(gem, bagSlot);
            Assert.Equal(bagSlot, gem.Slot);
            Assert.Equal(free, bag.Slot);
            // from a container onto a taken slot: the object there goes to a free pack slot
            inv.MoveToSlot(coins, bagSlot);
            Assert.Equal(bagSlot, coins.Slot);
            Assert.InRange(gem.Slot, PlayerInventory.PackFirst, PlayerInventory.PackLast);
            var back = PlayerSheet.FromBytes(s.ToBytes(), GameKind.UW2, null);
            Assert.Equal(before + 3, back.Inventory.All().Count());
            Assert.Equal(0xA0, back.Inventory.InSlot(bagSlot).ItemId);
            Assert.Equal(0x80, back.Inventory.InSlot(free).ItemId);
            Assert.Contains(back.Inventory.InSlot(free).Contents, c => c.ItemId == 0x81);
            Assert.Equal(0, back.Inventory.Unreached);
        }

        [Fact]
        public void Uw2_cheat_detection_is_warned_and_creation_limits_are_checked()
        {
            string g = GameData.Game("UW2"); if (g == null) return;
            var p = Save("SAVE1"); if (p == null) return;
            var s = PlayerSheet.FromBytes(File.ReadAllBytes(p), GameKind.UW2, Path.Combine(g, "DATA"));
            s.Strength = 30; s.Dexterity = 25; s.Intelligence = 10;
            var w = s.Check();
            Assert.Contains(w, x => x.Level == WarningLevel.Detected && x.Message.Contains("CHEATED"));
            s.Strength = 22; s.Dexterity = 22; s.Intelligence = 20;   // 64: not detected, but more than creation gives
            w = s.Check();
            Assert.DoesNotContain(w, x => x.Level == WarningLevel.Detected);
            Assert.Contains(w, x => x.Level == WarningLevel.Unreachable && x.Field == "Attributes");
            s.SetSkill(3, 31);
            Assert.Contains(s.Check(), x => x.Field == "Skills");
            s.Recompute();
            Assert.Equal(30 + s.Level * 22 / 5, s.MaxVitality);
            Assert.Equal(22 * 13 + 300, s.MaxWeight);
            Assert.DoesNotContain(s.Check(), x => x.Field == "Max vitality" || x.Field == "Max weight");
        }

        [Fact]
        public void Uw1_records_save_with_their_appearance_and_have_no_detection()
        {
            string g = GameData.Game("UW"); if (g == null) return;
            var file = Uw1File("Karl", 20, 22, 18);
            var s = PlayerSheet.FromBytes(file, GameKind.UW1, Path.Combine(g, "DATA"));
            Assert.Equal("Karl", s.Name);
            Assert.Equal(file, s.ToBytes());
            s.Class = 6; s.Body = 3; s.Female = true; s.HandBit = true;
            s.Strength = 30; s.Dexterity = 30; s.Intelligence = 30;
            var w = s.Check();
            Assert.DoesNotContain(w, x => x.Level == WarningLevel.Detected);
            Assert.Contains(w, x => x.Level == WarningLevel.Unreachable && x.Message.Contains("Ranger"));
            var back = PlayerSheet.FromBytes(s.ToBytes(), GameKind.UW1, null);
            Assert.Equal(6, back.Class); Assert.Equal(3, back.Body); Assert.True(back.Female); Assert.True(back.HandBit);
            Assert.Equal(8, back.Portrait);
            Assert.Equal(0xD2, back.Dat.Record.Length);
            Assert.Equal((byte)(0x01 | 0x02 | 3 << 2 | 6 << 5), back.Dat.Record[0x64]);
            // the hand bit means right-handed in UW1 and left-handed in UW2 (their creation menus differ)
            var uw1 = GameSession.Open(g);
            Assert.Equal(new[] { "Left-handed", "Right-handed" }, PlayerSheet.HandNames(uw1.DataFolder, uw1.Strings));
            string g2 = GameData.Game("UW2");
            if (g2 != null) { var uw2 = GameSession.Open(g2); Assert.Equal(new[] { "Right-handed", "Left-handed" }, PlayerSheet.HandNames(uw2.DataFolder, uw2.Strings)); }
        }
    }
}
