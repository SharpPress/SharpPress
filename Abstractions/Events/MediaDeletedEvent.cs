namespace SharpPress.Events
{
    public class MediaDeletedEvent : BaseEvent
    {
        public int MediaId { get; }
        public string FileName { get; }
        public MediaDeletedEvent(int mediaId, string fileName) { MediaId = mediaId; FileName = fileName; }
    }
}
