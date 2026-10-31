using System.Linq;
using Xunit;

namespace UwEditor.Core.Tests
{
    public class SearchTests
    {
        [Fact]
        public void Texts_npc_names_and_kinds_are_searched_in_every_level()
        {
            string g = GameData.Game("UW"); if (g == null) return;
            var s = GameSession.Open(g);
            var garamon = LevelSearch.Run(s, new ObjectQuery { Text = "garamon" });
            Assert.Equal(new[] { "ghost", "gravestone", "scroll" }, garamon.Select(h => h.Name).ToArray());
            Assert.Contains(garamon, h => h.Matched == "Garamon");                         // the ghost's NPC name
            Assert.Contains(garamon, h => h.Matched.Contains("empty grave of Garamon"));   // the gravestone's text
            var keys = LevelSearch.Run(s, new ObjectQuery { Categories = ItemCategory.Keys });
            Assert.NotEmpty(keys);
            Assert.All(keys, h => Assert.True((ItemCategories.Of(h.Item, s.Kind) & ItemCategory.Keys) != 0));
            var enchantedWeapons = LevelSearch.Run(s, new ObjectQuery { Categories = ItemCategory.Weapon, EnchantedOnly = true });
            Assert.All(enchantedWeapons, h => Assert.True(h.Item < 0x20));
            Assert.Single(LevelSearch.Run(s, new ObjectQuery { Text = "#942" }).Where(h => h.Level == 1));
        }

        [Fact]
        public void Uw2_creatures_are_found_by_the_names_they_talk_under()
        {
            string g = GameData.Game("UW2"); if (g == null) return;
            var s = GameSession.Open(g);
            var lb = LevelSearch.Run(s, new ObjectQuery { Text = "lord british" });
            Assert.Contains(lb, h => h.Level == 1 && h.Matched == "Lord British");
        }
    }
}
