using System;
using System.Globalization;
using UwEditor.Core;
using UwEditor.Core.Export;

namespace UwEditor.Cli
{
    /// <summary>
    /// UwEditor.Cli export-arx --game &lt;UW folder&gt; --out &lt;folder&gt; [--level N]: writes the
    /// ArxStructor export (Core's Export/ArxExport, docs/ARX-EXPORT.md) with no window. Exit code
    /// 0 when it is written, 1 for a failure while exporting, 2 for a wrong command line; the
    /// reason goes to standard error.
    /// </summary>
    public static class Program
    {
        const string Usage = "Usage: UwEditor.Cli export-arx --game <UW folder> --out <folder> [--level N]";

        public static int Main(string[] args)
        {
            if (args.Length == 0 || args[0] == "--help" || args[0] == "-h" || args[0] == "help")
            {
                Console.Error.WriteLine(Usage);
                return args.Length == 0 ? 2 : 0;
            }
            if (args[0] != "export-arx")
            {
                Console.Error.WriteLine("Unknown command \"" + args[0] + "\".");
                Console.Error.WriteLine(Usage);
                return 2;
            }
            string game = null, output = null;
            int? level = null;
            for (int i = 1; i < args.Length; i++)
            {
                string a = args[i];
                string value = i + 1 < args.Length ? args[i + 1] : null;
                switch (a)
                {
                    case "--game": game = value; i++; break;
                    case "--out": output = value; i++; break;
                    case "--level":
                        int n;
                        if (value == null || !int.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out n))
                        {
                            Console.Error.WriteLine("--level needs a level number.");
                            return 2;
                        }
                        level = n; i++;
                        break;
                    default:
                        Console.Error.WriteLine("Unknown option \"" + a + "\".");
                        Console.Error.WriteLine(Usage);
                        return 2;
                }
                if (value == null)
                {
                    Console.Error.WriteLine(a + " needs a value.");
                    return 2;
                }
            }
            if (game == null || output == null)
            {
                Console.Error.WriteLine("Both --game and --out are needed.");
                Console.Error.WriteLine(Usage);
                return 2;
            }
            try
            {
                var session = GameSession.Open(game);
                var result = ArxExport.Run(session, output, level);
                foreach (var kv in result.Summaries)
                {
                    var tri = (JObj)kv.Value["triangles"];
                    Console.WriteLine(string.Format(CultureInfo.InvariantCulture, "level {0}: {1} open tiles, {2} triangles, {3} objects",
                        kv.Key, kv.Value["openTiles"], tri["total"], kv.Value["objects"]));
                }
                Console.WriteLine(string.Format(CultureInfo.InvariantCulture, "{0} files, {1:N0} bytes, in {2}", result.Files.Count, result.TotalBytes, System.IO.Path.GetFullPath(output)));
                return 0;
            }
            catch (Exception e)
            {
                Console.Error.WriteLine("export-arx failed: " + e.Message);
                return 1;
            }
        }
    }
}
