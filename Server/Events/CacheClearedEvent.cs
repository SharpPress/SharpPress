namespace SharpPress.Events
{
    /// <summary>
    /// Event published when the file cache is cleared.
    /// </summary>
    public class CacheClearedEvent : BaseEvent
    {
        public string ClearedBy { get; }

        public CacheClearedEvent(string clearedBy = "")
        {
            ClearedBy = clearedBy;
        }
    }
}
