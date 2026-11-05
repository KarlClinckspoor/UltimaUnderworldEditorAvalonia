using System.IO;
using UwEditor.Core.Conversations;
using Xunit;

namespace ModRoundTrip.Tests
{
    /// <summary>STRINGS.PAK edited with the editor's writer.</summary>
    public class TextTests
    {
        [Theory]
        [InlineData("UW")]
        [InlineData("UW2")]
        public void Strings_changed_lengthened_and_added(string game)
        {
            using var rt = RoundTrip.Start(game);
            if (rt == null) return;
            var pak = StringsPakFile.Load(Underworld.Mods.GamePaths.Find(rt.Edited, "DATA/STRINGS.PAK"));
            var b4 = pak.Get(4);
            b4[3] = "a gleaming sword";
            b4[10] = b4[10] + " of considerable length, renamed by the round trip";
            pak.Set(4, b4);
            var b3 = pak.Get(3);
            b3.Add("a new line at the end of block 3");
            pak.Set(3, b3);
            pak.Save();
            rt.Check("text.strings", "DATA/STRINGS.PAK");
        }
    }
}
