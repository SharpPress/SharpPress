using SharpPress.Models;

namespace SharpPress.Events
{
    /// <summary>
    /// Event published when a plugin download is requested from the marketplace.
    /// </summary>
    public class MarketplaceDownloadQueuedEvent : BaseEvent
    {
        public string PluginName { get; }
        public string DownloadLink { get; }

        public MarketplaceDownloadQueuedEvent(string pluginName, string downloadLink)
        {
            PluginName = pluginName;
            DownloadLink = downloadLink;
        }
    }
}
