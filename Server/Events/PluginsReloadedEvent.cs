namespace SharpPress.Events
{
    /// <summary>
    /// Event published when all plugins are reloaded.
    /// </summary>
    public class PluginsReloadedEvent : BaseEvent
    {
        public int PluginCount { get; }

        public PluginsReloadedEvent(int pluginCount)
        {
            PluginCount = pluginCount;
        }
    }
}
