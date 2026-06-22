namespace SharpPress.Events
{
    public class TagDeletedEvent : BaseEvent
    {
        public int TagId { get; }
        public string Name { get; }
        public TagDeletedEvent(int tagId, string name) { TagId = tagId; Name = name; }
    }
}
