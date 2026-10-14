namespace SharpPress.Events
{
    /// <summary>
    /// Event published when a plugin file is uploaded via the API or page.
    /// </summary>
    public class PluginUploadedEvent : BaseEvent
    {
        public string FileName { get; }
        public string UploadedBy { get; }

        public PluginUploadedEvent(string fileName, string uploadedBy = "")
        {
            FileName = fileName;
            UploadedBy = uploadedBy;
        }
    }
}
