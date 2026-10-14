using SharpPress.Services;

namespace SharpPress.Plugins
{
    public interface IPluginFilters
    {
        void Add<T>(string key, Func<T, T> filter, int priority = 0);
        void AddShortcode(string name, Func<ShortcodeContext, string> handler);
    }
}
