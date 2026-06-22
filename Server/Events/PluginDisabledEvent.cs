using SharpPress.Plugins;

namespace SharpPress.Events
{
    /// <summary>
    /// Event published when a plugin is disabled.
    /// </summary>
    public class PluginDisabledEvent : BaseEvent
    {
        public string PluginName { get; }

        public PluginDisabledEvent(string pluginName)
        {
            PluginName = pluginName;
        }
    }
}
