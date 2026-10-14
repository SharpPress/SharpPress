using System.Text.RegularExpressions;

namespace SharpPress.Plugins
{
    public static class PluginPaths
    {
        private static readonly Regex SafeName = new(@"^[A-Za-z0-9][A-Za-z0-9._\-]*$", RegexOptions.Compiled);

        public static string Root => Path.Combine(AppContext.BaseDirectory, "plugins");

        public static string StateFile => Path.Combine(Root, "plugin-state.json");

        public static string DataRoot => Path.Combine(Root, ".data");

        public static string EnsureRoot()
        {
            Directory.CreateDirectory(Root);
            return Root;
        }

        public static bool IsSafeName(string? name)
            => !string.IsNullOrWhiteSpace(name) && name.Length <= 128 && !name.Contains("..") && SafeName.IsMatch(name);

        public static bool IsInside(string root, string path)
        {
            var fullRoot = Path.GetFullPath(root).TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar;
            var fullPath = Path.GetFullPath(path);
            return fullPath.StartsWith(fullRoot, StringComparison.OrdinalIgnoreCase);
        }

        public static string DataDirectoryFor(string pluginName)
        {
            var safe = string.Concat(pluginName.Select(c => char.IsLetterOrDigit(c) || c == '-' || c == '_' || c == '.' ? c : '_'));
            var path = Path.Combine(DataRoot, safe);
            Directory.CreateDirectory(path);
            return path;
        }
    }
}
