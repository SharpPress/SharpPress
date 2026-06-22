using SharpPress.Plugins;

namespace SharpPress.Events
{
    /// <summary>
    /// Event published when a plugin is unloaded.
    /// </summary>
    public class PluginUnloadedEvent : BaseEvent
    {
        public string PluginName { get; }

        public PluginUnloadedEvent(string pluginName)
        {
            PluginName = pluginName;
        }
    }
}
