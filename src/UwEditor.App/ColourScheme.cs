using System;
using System.Collections.Generic;
using Avalonia;
using Avalonia.Media;
using Avalonia.Styling;

namespace UwEditor.App
{
    /// <summary>The light and dark colour schemes: Styles.axaml holds a dictionary for each; code-made brushes pick through here.</summary>
    public static class ColourScheme
    {
        public const string Dark = "Dark", Light = "Light";
        static readonly Dictionary<string, IBrush> cache = new Dictionary<string, IBrush>();

        public static bool IsLight { get { return Application.Current?.ActualThemeVariant == ThemeVariant.Light; } }
        public static string Current { get { return IsLight ? Light : Dark; } }

        public static void Set(string name)
        {
            if (Application.Current == null) return;
            Application.Current.RequestedThemeVariant = name == Light ? ThemeVariant.Light : ThemeVariant.Dark;
        }

        /// <summary>A brush of one colour in the dark scheme and another in the light one.</summary>
        public static IBrush Pick(string dark, string light)
        {
            string c = IsLight ? light : dark;
            IBrush b;
            if (!cache.TryGetValue(c, out b)) cache[c] = b = new SolidColorBrush(Color.Parse(c));
            return b;
        }

        /// <summary>The accent of a game: UW1's amber, UW2's blue, darker on the light scheme.</summary>
        public static Color Accent(bool uw2)
        {
            return Color.Parse(uw2 ? (IsLight ? "#2F6DA8" : "#6FA6DC") : (IsLight ? "#9A6420" : "#C9934C"));
        }
    }
}
