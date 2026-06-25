using System.Threading.Tasks;
using SharpPress.Events;

namespace SharpPress.Services
{
    public class EventHandler :
        IEventHandler<UserRegisteredEvent>,
        IEventHandler<VideoUploadedEvent>,
        IEventHandler<PluginLoadedEvent>,
        IEventHandler<PostCreatedEvent>,
        IEventHandler<PostUpdatedEvent>,
        IEventHandler<PostDeletedEvent>,
        IEventHandler<MediaUploadedEvent>,
        IEventHandler<MediaDeletedEvent>
    {
        private readonly Logger _logger;

        public EventHandler(Logger logger)
        {
            _logger = logger;
        }

        public async Task HandleAsync(UserRegisteredEvent eventData)
        {
            await Task.Run(() =>
            {
                _logger.Log($"New user '{eventData.User.Username}' (Email: {eventData.User.Email}) has registered.");
            });
        }

        public async Task HandleAsync(VideoUploadedEvent eventData)
        {
            await Task.Run(() =>
            {
                _logger.Log($"Video '{eventData.FileName}' from URL '{eventData.SourceUrl}' uploaded.");
            });
        }

        public async Task HandleAsync(PluginLoadedEvent eventData)
        {
        }

        public async Task HandleAsync(PostCreatedEvent eventData)
        {
            await Task.Run(() =>
            {
                _logger.Log($"Post created: '{eventData.Post.Title}' (Status: {eventData.Post.Status})");
            });
        }

        public async Task HandleAsync(PostUpdatedEvent eventData)
        {
            await Task.Run(() =>
            {
                _logger.Log($"Post updated: '{eventData.Post.Title}'");
            });
        }

        public async Task HandleAsync(PostDeletedEvent eventData)
        {
            await Task.Run(() =>
            {
                _logger.Log($"Post deleted: '{eventData.Post.Title}'");
            });
        }

        public async Task HandleAsync(MediaUploadedEvent eventData)
        {
            await Task.Run(() =>
            {
                _logger.Log($"Media uploaded: '{eventData.MediaItem.FileName}' ({eventData.MediaItem.MediaType})");
            });
        }

        public async Task HandleAsync(MediaDeletedEvent eventData)
        {
            await Task.Run(() =>
            {
                _logger.Log($"Media deleted: '{eventData.FileName}' (ID: {eventData.MediaId})");
            });
        }
    }
}
