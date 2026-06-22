using SharpPress.Models;

namespace SharpPress.Events
{
    public class PostUpdatedEvent : BaseEvent
    {
        public Post Post { get; }
        public PostUpdatedEvent(Post post) => Post = post;
    }
}
