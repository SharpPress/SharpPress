using SharpPress.Plugins;

namespace SharpPress.Events
{
    /// <summary>
    /// Event published when a plugin is enabled.
    /// </summary>
    public class PluginEnabledEvent : BaseEvent
    {
        public IPlugin Plugin { get; }

        public PluginEnabledEvent(IPlugin plugin)
        {
            Plugin = plugin;
        }
    }
}
