using System.Reflection;

namespace UwEditor.Look
{
    /// <summary>
    /// The running program's version: the Version set in Directory.Build.props (the editor's in
    /// the repository's, the mod manager's in ModManager's), and the commit it was built from
    /// when the build knew it (the SDK adds it to the informational version as "+sha").
    /// </summary>
    public static class AppVersion
    {
        /// <summary>"0.1.0", from the program's entry assembly (or the one given).</summary>
        public static string Number(Assembly asm = null)
        {
            string v = Informational(asm);
            int plus = v.IndexOf('+');
            return plus > 0 ? v.Substring(0, plus) : v;
        }

        /// <summary>The commit, shortened to 8 characters, or "" when the build did not record one.</summary>
        public static string Commit(Assembly asm = null)
        {
            string v = Informational(asm);
            int plus = v.IndexOf('+');
            if (plus < 0) return "";
            string sha = v.Substring(plus + 1);
            return sha.Length > 8 ? sha.Substring(0, 8) : sha;
        }

        /// <summary>"0.1.0 (commit 26881116)", or "0.1.0".</summary>
        public static string Text(Assembly asm = null)
        {
            string c = Commit(asm);
            return Number(asm) + (c.Length > 0 ? " (commit " + c + ")" : "");
        }

        static string Informational(Assembly asm)
        {
            asm ??= Assembly.GetEntryAssembly() ?? typeof(AppVersion).Assembly;
            return asm.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion
                ?? asm.GetName().Version?.ToString(3) ?? "";
        }
    }
}
