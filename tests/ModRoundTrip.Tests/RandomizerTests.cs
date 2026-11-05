using System.Collections.Generic;
using UwEditor.Core;
using UwEditor.Core.Editing;
using UwEditor.Core.Randomizer;
using Xunit;

namespace ModRoundTrip.Tests
{
    /// <summary>The randomizer's changes, applied through the level editor and saved, then sent as mods.</summary>
    public class RandomizerTests
    {
        [Fact]
        public void Key_item_scatter_uw1()
        {
            using var rt = RoundTrip.Start("UW");
            if (rt == null) return;
            var s = GameSession.Open(rt.Edited);
            var edits = new LevelEdits(s);
            var r = KeyShuffle.Run(s, edits, new KeyShuffleOptions { Seed = 8 });
            Assert.True(r.Success, r.Refused);
            KeyShuffle.Apply(r, edits);
            Assert.NotEmpty(edits.Save());
            rt.Check("random.scatter", "DATA/LEV.ARK");
        }

        [Fact]
        public void Key_item_scatter_with_start_kit_and_explored_maps_uw1()
        {
            using var rt = RoundTrip.Start("UW");
            if (rt == null) return;
            var s = GameSession.Open(rt.Edited);
            var edits = new LevelEdits(s);
            var r = KeyShuffle.Run(s, edits, new KeyShuffleOptions { Seed = 3, StartKit = true, ExploreMaps = true });
            Assert.True(r.Success, r.Refused);
            KeyShuffle.Apply(r, edits);
            edits.Save();
            rt.Check("random.scatter-kit", "DATA/LEV.ARK");
        }

        [Theory]
        [InlineData("UW")]
        [InlineData("UW2")]
        public void Item_shuffle(string game)
        {
            using var rt = RoundTrip.Start(game);
            if (rt == null) return;
            var s = GameSession.Open(rt.Edited);
            var edits = new LevelEdits(s);
            var o = ShuffleOptions.Defaults(s);
            o.Seed = 7;
            var r = ItemShuffle.Run(s, edits.Model, o);
            ItemShuffle.Apply(r, edits, o.Seed);
            Assert.NotEmpty(edits.Save());
            rt.Check("random.shuffle", "DATA/LEV.ARK");
        }

        [Theory]
        [InlineData("UW", "swap-any")]
        [InlineData("UW2", "swap-any")]
        [InlineData("UW", "swap-kind")]
        [InlineData("UW2", "swap-kind")]
        public void Item_swap(string game, string mode)
        {
            using var rt = RoundTrip.Start(game);
            if (rt == null) return;
            var s = GameSession.Open(rt.Edited);
            var edits = new LevelEdits(s);
            var o = ShuffleOptions.Defaults(s);
            o.Seed = 11; o.Mode = mode; o.Ignored = new List<IgnoredObject>();
            var r = ItemShuffle.Run(s, edits.Model, o);
            ItemShuffle.Apply(r, edits, o.Seed);
            Assert.NotEmpty(edits.Save());
            rt.Check("random." + mode, "DATA/LEV.ARK");
        }

        [Theory]
        [InlineData("UW")]
        [InlineData("UW2")]
        public void Monster_swap(string game)
        {
            using var rt = RoundTrip.Start(game);
            if (rt == null) return;
            var s = GameSession.Open(rt.Edited);
            var edits = new LevelEdits(s);
            var o = new MonsterSwapOptions { Seed = 4, Scope = "all", Whitelist = MonsterSwap.DefaultWhitelist(s, edits.Model), Ignored = new List<IgnoredObject>() };
            var r = MonsterSwap.Run(s, edits.Model, o);
            MonsterSwap.Apply(r, edits, o.Seed);
            Assert.NotEmpty(edits.Save());
            rt.Check("random.monsters", "DATA/LEV.ARK");
        }

        [Theory]
        [InlineData("UW")]
        [InlineData("UW2")]
        public void Named_challenges(string game)
        {
            using var rt = RoundTrip.Start(game);
            if (rt == null) return;
            var s = GameSession.Open(rt.Edited);
            var edits = new LevelEdits(s);
            var tables = ObjectTables.Load(s);
            var opt = new ChallengeOptions { Seed = 21, Special = Challenges.DefaultSpecial(s, edits.Model) };
            opt.Set(Challenge.Fasting, true);
            opt.Set(Challenge.Dystopia, true);
            var w = ChallengeWorld.Copy(s, edits, tables.ObjectsBytes, null);
            var r = Challenges.Run(w, opt);
            Assert.Null(r.Refused);
            Challenges.Apply(r, edits, (row, f, v) => tables.Set("Creatures", row, f, v), null, "Named challenges");
            edits.Save();
            tables.Save();
            rt.Check("random.challenges");
        }
    }
}
