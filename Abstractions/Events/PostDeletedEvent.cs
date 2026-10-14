using SharpPress.Models;

namespace SharpPress.Events
{
    public class PostDeletedEvent : BaseEvent
    {
        public Post Post { get; }
        public PostDeletedEvent(Post post) => Post = post;
    }
}
