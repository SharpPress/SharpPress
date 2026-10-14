using SharpPress.Models;

namespace SharpPress.Events
{
    public class PostPublishedEvent : BaseEvent
    {
        public Post Post { get; }
        public PostPublishedEvent(Post post) => Post = post;
    }
}
