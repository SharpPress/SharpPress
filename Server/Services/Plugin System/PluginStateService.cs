using System.Text.Json;

namespace SharpPress.Services
{
    public class PluginStateService
    {
        private readonly string _statePath;
        private Dictionary<string, bool> _state = new();

        public PluginStateService(string statePath = "plugins/plugin-state.json")
        {
            _statePath = statePath;
            Load();
        }

        public bool IsEnabled(string pluginName)
        {
            return !_state.TryGetValue(pluginName, out var enabled) || enabled;
        }

        public void SetEnabled(string pluginName, bool enabled)
        {
            _state[pluginName] = enabled;
            Save();
        }

        public void Remove(string pluginName)
        {
            _state.Remove(pluginName);
            Save();
        }

        private void Load()
        {
            try
            {
                if (!File.Exists(_statePath)) return;
                var json = File.ReadAllText(_statePath);
                _state = JsonSerializer.Deserialize<Dictionary<string, bool>>(json) ?? new();
            }
            catch
            {
                _state = new();
            }
        }

        private void Save()
        {
            try
            {
                Directory.CreateDirectory(Path.GetDirectoryName(_statePath)!);
                var json = JsonSerializer.Serialize(_state, new JsonSerializerOptions { WriteIndented = true });
                File.WriteAllText(_statePath, json);
            }
            catch { }
        }
    }
}