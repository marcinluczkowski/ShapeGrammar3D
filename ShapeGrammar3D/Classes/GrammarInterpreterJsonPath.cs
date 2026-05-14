using System;
using System.IO;
using System.Linq;

namespace ShapeGrammar3D.Classes
{
    /// <summary>
    /// Resolves folder + file name for grammar interpreter JSON exports.
    /// Same path runs overwrite the file (<see cref="File.Create"/>, <see cref="File.WriteAllText"/>).
    /// </summary>
    public static class GrammarInterpreterJsonPath
    {
        /// <summary>
        /// Empty <paramref name="folderOrFile"/> → temp directory.
        /// If <paramref name="folderOrFile"/> ends with <c>.json</c> → full output path (ignores <paramref name="jsonFileName"/>).
        /// Otherwise → directory + file from <paramref name="jsonFileName"/> or <paramref name="defaultFileName"/>.
        /// </summary>
        public static string Resolve(string folderOrFile, string jsonFileName, string defaultFileName)
        {
            if (string.IsNullOrWhiteSpace(defaultFileName))
                defaultFileName = "run.json";

            string fileName = CombineFileName(jsonFileName, defaultFileName);

            if (string.IsNullOrWhiteSpace(folderOrFile))
                return Path.Combine(Path.GetTempPath(), fileName);

            var s = folderOrFile.Trim().Trim('"');
            if (s.EndsWith(".json", StringComparison.OrdinalIgnoreCase))
            {
                var dir = Path.GetDirectoryName(s);
                if (!string.IsNullOrEmpty(dir))
                {
                    try { Directory.CreateDirectory(dir); }
                    catch { /* writer may still work */ }
                }
                return s;
            }

            try { Directory.CreateDirectory(s); }
            catch { return Path.Combine(Path.GetTempPath(), fileName); }
            return Path.Combine(s, fileName);
        }

        private static string CombineFileName(string jsonFileName, string defaultFileName)
        {
            var n = NormalizeJsonFileName(jsonFileName);
            return string.IsNullOrEmpty(n) ? defaultFileName : n;
        }

        /// <summary>File name only; adds .json if missing; strips invalid name chars.</summary>
        public static string NormalizeJsonFileName(string raw)
        {
            if (string.IsNullOrWhiteSpace(raw)) return null;
            var name = Path.GetFileName(raw.Trim().Trim('"'));
            if (string.IsNullOrEmpty(name)) return null;
            var invalid = Path.GetInvalidFileNameChars();
            name = new string(name.Select(c => invalid.Contains(c) ? '_' : c).ToArray());
            name = name.Trim();
            if (name.Length == 0) return null;
            if (!name.EndsWith(".json", StringComparison.OrdinalIgnoreCase))
                name += ".json";
            return name;
        }
    }
}
