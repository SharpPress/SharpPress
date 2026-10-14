using Newtonsoft.Json;
using SharpPress.Events;
using SharpPress.Services;
using System.Collections.Concurrent;
using System.Reflection;

namespace SharpPress.Plugins
{
    public sealed class PluginSettingsSection
    {
        public string Plugin { get; init; } = "";
        public string Title { get; init; } = "";
        public Type SettingsType { get; init; } = typeof(object);
    }

    public sealed class PluginSettingsChangedEvent : BaseEvent
    {
        public string PluginName { get; }

        public PluginSettingsChangedEvent(string pluginName)
        {
            PluginName = pluginName;
        }
    }

    public sealed class PluginSettingsStore
    {
        private readonly object _lock = new();
        private readonly ConcurrentDictionary<string, PluginSettingsSection> _sections = new(StringComparer.OrdinalIgnoreCase);
        private readonly IEventBus _eventBus;
        private readonly Logger _logger;

        public PluginSettingsStore(IEventBus eventBus, Logger logger)
        {
            _eventBus = eventBus;
            _logger = logger;
        }

        public IReadOnlyList<PluginSettingsSection> Sections => _sections.Values.OrderBy(s => s.Title).ToList();

        public PluginSettingsSection? FindSection(string plugin) => _sections.TryGetValue(plugin, out var section) ? section : null;

        public IDisposable Expose(string plugin, string title, Type type)
        {
            var section = new PluginSettingsSection { Plugin = plugin, Title = title, SettingsType = type };
            _sections[plugin] = section;

            return new Removal(() =>
            {
                if (_sections.TryGetValue(plugin, out var current) && ReferenceEquals(current, section))
                    _sections.TryRemove(plugin, out _);
            });
        }

        public object Get(string plugin, Type type)
        {
            lock (_lock)
            {
                var path = PathFor(plugin);

                try
                {
                    if (File.Exists(path))
                    {
                        var loaded = JsonConvert.DeserializeObject(File.ReadAllText(path), type);
                        if (loaded != null) return loaded;
                    }
                }
                catch (Exception ex)
                {
                    _logger.LogWarning($"Settings for plugin '{plugin}' could not be read: {ex.Message}");
                }

                return Activator.CreateInstance(type)!;
            }
        }

        public void Save(string plugin, object value)
        {
            lock (_lock)
            {
                var path = PathFor(plugin);
                var temp = path + ".tmp";
                File.WriteAllText(temp, JsonConvert.SerializeObject(value, Formatting.Indented));
                File.Move(temp, path, overwrite: true);
            }

            _ = _eventBus.PublishAsync(new PluginSettingsChangedEvent(plugin));
        }

        public static IReadOnlyList<PropertyInfo> EditableProperties(Type type)
            => type.GetProperties(BindingFlags.Instance | BindingFlags.Public)
                .Where(p => p.CanRead && p.CanWrite && IsSupported(p.PropertyType))
                .ToList();

        private static bool IsSupported(Type type)
        {
            type = Nullable.GetUnderlyingType(type) ?? type;
            return type == typeof(string) || type == typeof(bool) || type == typeof(int) || type == typeof(long)
                || type == typeof(double) || type == typeof(decimal) || type.IsEnum;
        }

        private static string PathFor(string plugin) => Path.Combine(PluginPaths.DataDirectoryFor(plugin), "settings.json");

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

    public sealed class PluginSettings : IPluginSettings
    {
        private readonly PluginSettingsStore _store;
        private readonly PluginScope _scope;
        private readonly string _plugin;
        private readonly Action<string, string> _registerMenu;

        public PluginSettings(PluginSettingsStore store, PluginScope scope, string plugin, Action<string, string> registerMenu)
        {
            _store = store;
            _scope = scope;
            _plugin = plugin;
            _registerMenu = registerMenu;
        }

        public T Get<T>() where T : class, new() => (T)_store.Get(_plugin, typeof(T));

        public void Save<T>(T value) where T : class => _store.Save(_plugin, value);

        public void Expose<T>(string title) where T : class, new()
        {
            var registration = _store.Expose(_plugin, title, typeof(T));
            _scope.OnDispose(registration.Dispose);
            _registerMenu($"{title} Settings", $"/admin/plugin-settings/{Uri.EscapeDataString(_plugin)}");
        }
    }
}
