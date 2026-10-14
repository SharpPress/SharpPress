using System.Text.RegularExpressions;

namespace SharpPress.Services
{
    public class FilterPipeline : IFilterPipeline
    {
        private static readonly Regex ShortcodePattern = new(
            @"\[(?<name>[A-Za-z][\w-]*)(?<attrs>(?:\s+[\w-]+=""[^""]*"")*)\s*\](?:(?<content>.*?)\[/\k<name>\])?",
            RegexOptions.Compiled | RegexOptions.Singleline);

        private static readonly Regex AttributePattern = new(@"(?<key>[\w-]+)=""(?<value>[^""]*)""", RegexOptions.Compiled);

        private readonly object _lock = new();
        private readonly List<FilterEntry> _filters = new();
        private readonly Dictionary<string, Func<ShortcodeContext, string>> _shortcodes = new(StringComparer.OrdinalIgnoreCase);
        private readonly Logger _logger;

        public FilterPipeline(Logger logger)
        {
            _logger = logger;
        }

        public T Apply<T>(string key, T value)
        {
            FilterEntry[] entries;
            lock (_lock)
                entries = _filters.Where(f => f.Key == key && f.ValueType == typeof(T)).OrderBy(f => f.Priority).ToArray();

            foreach (var entry in entries)
            {
                try
                {
                    value = (T)entry.Filter(value!)!;
                }
                catch (Exception ex)
                {
                    _logger.LogError($"Filter '{key}' failed: {ex.Message}");
                }
            }

            if (typeof(T) == typeof(string) && key == FilterKeys.PostContentRender && value is string text)
                return (T)(object)RenderShortcodes(text);

            return value;
        }

        public IDisposable AddFilter<T>(string key, Func<T, T> filter, int priority = 0)
        {
            var entry = new FilterEntry(key, typeof(T), priority, value => filter((T)value)!);
            lock (_lock)
                _filters.Add(entry);

            return new Removal(() =>
            {
                lock (_lock)
                    _filters.Remove(entry);
            });
        }

        public IDisposable AddShortcode(string name, Func<ShortcodeContext, string> handler)
        {
            lock (_lock)
            {
                if (_shortcodes.ContainsKey(name))
                    throw new InvalidOperationException($"Shortcode '{name}' is already registered.");

                _shortcodes[name] = handler;
            }

            return new Removal(() =>
            {
                lock (_lock)
                {
                    if (_shortcodes.TryGetValue(name, out var current) && ReferenceEquals(current, handler))
                        _shortcodes.Remove(name);
                }
            });
        }

        public string RenderShortcodes(string content)
        {
            if (string.IsNullOrEmpty(content) || content.IndexOf('[') < 0)
                return content;

            return ShortcodePattern.Replace(content, match =>
            {
                Func<ShortcodeContext, string>? handler;
                lock (_lock)
                    _shortcodes.TryGetValue(match.Groups["name"].Value, out handler);

                if (handler == null)
                    return match.Value;

                var attributes = AttributePattern.Matches(match.Groups["attrs"].Value)
                    .ToDictionary(m => m.Groups["key"].Value, m => m.Groups["value"].Value, StringComparer.OrdinalIgnoreCase);

                try
                {
                    return handler(new ShortcodeContext
                    {
                        Name = match.Groups["name"].Value,
                        Attributes = attributes,
                        Content = match.Groups["content"].Value
                    });
                }
                catch (Exception ex)
                {
                    _logger.LogError($"Shortcode '{match.Groups["name"].Value}' failed: {ex.Message}");
                    return match.Value;
                }
            });
        }

        private sealed record FilterEntry(string Key, Type ValueType, int Priority, Func<object, object?> Filter);

        private sealed class Removal : IDisposable
        {
            private Action? _action;

            public Removal(Action action)
            {
                _action = action;
            }

            public void Dispose() => Interlocked.Exchange(ref _action, null)?.Invoke();
        }
    }
}
