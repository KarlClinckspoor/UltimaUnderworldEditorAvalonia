using System;
using System.Collections.Generic;
using System.Linq;
using UwEditor.Core.Conversations;
using UwEditor.Core.Editing;
using UwEditor.Core.Randomizer;
using Xunit;
using Xunit.Abstractions;

namespace UwEditor.Core.Tests
{
    /// <summary>Randomizer ▸ Vowel play: the names, the protected words, one spelling per name everywhere, Undo.</summary>
    public class VowelPlayTests
    {
        readonly ITestOutputHelper output;
        public VowelPlayTests(ITestOutputHelper output) { this.output = output; }

        [Fact]
        public void Shuffle_keeps_length_consonants_and_case()
        {
            var rng = new Random(4);
            for (int i = 0; i < 200; i++)
            {
                string n = VowelPlay.Shuffle("Iss'leek Garamon", rng, false);
                Assert.Equal(16, n.Length);
                for (int k = 0; k < n.Length; k++)
                {
                    char a = "Iss'leek Garamon"[k], b = n[k];
                    bool va = "aeiouAEIOU".IndexOf(a) >= 0;
                    if (!va) Assert.Equal(a, b);
                    else { Assert.True("aeiouAEIOU".IndexOf(b) >= 0); Assert.Equal(char.IsUpper(a), char.IsUpper(b)); }
                }
                Assert.StartsWith("I", VowelPlay.Shuffle("Illomo", rng, true));
            }
        }

        [Fact]
        public void Plan_never_makes_a_known_word_another_name_or_a_protected_one()
        {
            var words = new HashSet<string> { "Shak", "shok", "Gurstang" };
            var plan = VowelPlay.Plan(new[] { "Shak", "Gurstang", "Folanae", "Hmm" }, new HashSet<string> { "FOLANAE" }, words, 7, false);
            var shak = plan.Single(n => n.Was == "Shak");
            Assert.Null(shak.Stays);
            Assert.NotEqual("Shok", shak.Now);
            Assert.NotEqual("Shak", shak.Now);
            Assert.NotNull(plan.Single(n => n.Was == "Folanae").Stays);
            Assert.NotNull(plan.Single(n => n.Was == "Hmm").Stays);
            // the same seed and name give the same spelling, whatever else is in the list
            Assert.Equal(shak.Now, VowelPlay.Plan(new[] { "Shak" }, new HashSet<string>(), words, 7, false)[0].Now);
        }

        [Fact]
        public void Rewrite_changes_the_name_its_possessive_and_capitals_only()
        {
            var names = new List<VowelName> { new VowelName { Was = "Garamon", Now = "Gorumen" }, new VowelName { Was = "Owl", Now = "Iwl" } };
            var swap = VowelPlay.Swaps(names);
            var f = TextRewrite.Finder(swap.Keys, false);
            Assert.Equal("Gorumen's bones; GORUMEN! Dr. Iwl saw an owl. Garamonx", VowelPlay.Rewrite("Garamon's bones; GARAMON! Dr. Owl saw an owl. Garamonx", f, swap));
        }

        [Fact]
        public void UW1_names_change_everywhere_and_the_passwords_and_mantras_stay()
        {
            string g = GameData.Game("UW"); if (g == null) return;
            var s = GameSession.Open(g);
            var lib = ConversationLibrary.Open(s);
            var st = StringsEditor.Open(s, lib.Strings);
            var names = VowelPlay.DefaultNames(st);
            foreach (var n in new[] { "Garamon", "Tyball", "Gurstang", "Illomo", "Shak", "Cabirus", "Avatar", "British" }) Assert.Contains(n, names);
            foreach (var n in new[] { "Door", "Dr", "The" }) Assert.DoesNotContain(n, names);

            var compared = VowelPlay.ComparedTexts(lib);
            output.WriteLine("compared: " + string.Join(" | ", compared.Distinct()));
            Assert.Contains(compared, c => c.Equals("Deco Morono", StringComparison.OrdinalIgnoreCase));
            var prot = VowelPlay.ProtectedWords(st, lib);
            foreach (var w in new[] { "DECO", "MORONO", "FOLANAE", "FANLO", "INSAHN", "RA" }) Assert.Contains(w, prot);

            var shipped = st.Pak.Blocks.ToDictionary(b => b, b => st.Pak.Get(b).ToList());
            var opt = new VowelPlayOptions { Seed = 12 };
            var r = VowelPlay.Apply(st, prot, opt);
            output.WriteLine(r.Report(true).Substring(0, Math.Min(6000, r.Report(true).Length)));
            var changed = r.Names.Where(n => n.Stays == null).ToDictionary(n => n.Was, n => n.Now);
            Assert.True(changed.Count > 60);
            // one spelling everywhere: no string still holds a changed name as written
            var finder = TextRewrite.Finder(changed.Keys, false);
            foreach (int b in st.Pak.Blocks)
                foreach (var text in st.Get(b)) Assert.False(finder.IsMatch(text ?? ""), "block " + b + ": " + text);
            Assert.Contains(changed["Garamon"], st.Get(7, 16 + 27));                           // the name he talks under
            Assert.Contains("Deco Morono", string.Join("\n", st.Get(0x0E00 + 21)), StringComparison.OrdinalIgnoreCase);
            Assert.Contains("Folanae", string.Join("\n", st.Get(0x0E00 + 187)));
            for (int i = MantraShuffle.First; i < MantraShuffle.First + MantraShuffle.Count; i++) Assert.Equal(shipped[2][i], st.Pak.Get(2)[i]);
            Assert.Contains(r.Edits, e => e.Block >= 0x0E00);
            Assert.Contains(r.Edits, e => e.Block >= 0x0C00 && e.Block < 0x0E00);          // the cutscenes name Tyball and Garamon

            Assert.Equal(0, VowelPlay.Undo(st, r));
            foreach (int b in st.Pak.Blocks) Assert.Equal(shipped[b], st.Pak.Get(b));
        }

        [Fact]
        public void UW2_names_change_and_no_compared_word_does()
        {
            string g = GameData.Game("UW2"); if (g == null) return;
            var s = GameSession.Open(g);
            var lib = ConversationLibrary.Open(s);
            var st = StringsEditor.Open(s, lib.Strings);
            var prot = VowelPlay.ProtectedWords(st, lib);
            output.WriteLine("compared: " + string.Join(" | ", VowelPlay.ComparedTexts(lib).Distinct()));
            var r = VowelPlay.Preview(st, prot, new VowelPlayOptions { Seed = 3 });
            output.WriteLine(r.Report(false).Substring(0, Math.Min(4000, r.Report(false).Length)));
            Assert.True(r.Names.Count(n => n.Stays == null) > 50);
            Assert.All(r.Names.Where(n => n.Stays == null), n => Assert.DoesNotContain(n.Was.ToUpperInvariant(), prot));
            Assert.NotEmpty(r.Edits);
        }
    }
}
