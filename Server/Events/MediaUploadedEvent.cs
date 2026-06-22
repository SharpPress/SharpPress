using SharpPress.Models;

namespace SharpPress.Events
{
    public class MediaUploadedEvent : BaseEvent
    {
        public MediaItem MediaItem { get; }
        public MediaUploadedEvent(MediaItem mediaItem) => MediaItem = mediaItem;
    }
}
