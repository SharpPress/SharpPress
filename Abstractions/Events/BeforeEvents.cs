using SharpPress.Models;

namespace SharpPress.Events
{
    public class PostCreatingEvent : CancellableEvent
    {
        public Post Post { get; }
        public PostCreatingEvent(Post post) => Post = post;
    }

    public class PostUpdatingEvent : CancellableEvent
    {
        public Post Post { get; }
        public PostUpdatingEvent(Post post) => Post = post;
    }

    public class PostDeletingEvent : CancellableEvent
    {
        public Post Post { get; }
        public PostDeletingEvent(Post post) => Post = post;
    }

    public class UserRegisteringEvent : CancellableEvent
    {
        public RegisterRequest Request { get; }
        public UserRegisteringEvent(RegisterRequest request) => Request = request;
    }

    public class LoginAttemptingEvent : CancellableEvent
    {
        public LoginRequest Request { get; }
        public LoginAttemptingEvent(LoginRequest request) => Request = request;
    }

    public class MediaUploadingEvent : CancellableEvent
    {
        public string FileName { get; set; }
        public string ContentType { get; set; }
        public string UploadedBy { get; }
        public Stream Content { get; set; }

        public MediaUploadingEvent(Stream content, string fileName, string contentType, string uploadedBy)
        {
            Content = content;
            FileName = fileName;
            ContentType = contentType;
            UploadedBy = uploadedBy;
        }
    }

    public class SettingsSavingEvent : CancellableEvent
    {
        public SiteSettings Settings { get; }
        public SettingsSavingEvent(SiteSettings settings) => Settings = settings;
    }
}
