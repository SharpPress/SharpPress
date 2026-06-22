namespace SharpPress.Events
{
    /// <summary>
    /// Event published when a plugin is successfully downloaded from the marketplace.
    /// </summary>
    public class PluginDownloadedEvent : BaseEvent
    {
        public string PluginName { get; }
        public string FilePath { get; }

        public PluginDownloadedEvent(string pluginName, string filePath)
        {
            PluginName = pluginName;
            FilePath = filePath;
        }
    }
}
