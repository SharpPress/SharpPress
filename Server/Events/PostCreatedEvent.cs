using SharpPress.Models;

namespace SharpPress.Events
{
    public class PostCreatedEvent : BaseEvent
    {
        public Post Post { get; }
        public PostCreatedEvent(Post post) => Post = post;
    }
}
