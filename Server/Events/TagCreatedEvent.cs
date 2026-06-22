using SharpPress.Models;

namespace SharpPress.Events
{
    public class TagCreatedEvent : BaseEvent
    {
        public Tag Tag { get; }
        public TagCreatedEvent(Tag tag) => Tag = tag;
    }
}
