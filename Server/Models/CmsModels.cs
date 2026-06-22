using System.ComponentModel.DataAnnotations;

namespace SharpPress.Models
{
    public enum PostStatus
    {
        Draft = 0,
        Published = 1,
        Scheduled = 2,
        Archived = 3
    }

    public enum PostType
    {
        Article = 0,
        Page = 1,
        BlogPost = 2
    }

    public class Post : FeatherData
    {
        [StringLength(255)] public string Title { get; set; } = "";
        [StringLength(255)] public string Slug { get; set; } = "";
        public string Content { get; set; } = "";
        public string Excerpt { get; set; } = "";
        [StringLength(50)] public string AuthorUsername { get; set; } = "";
        public PostStatus Status { get; set; } = PostStatus.Draft;
        public PostType Type { get; set; } = PostType.BlogPost;
        [StringLength(255)] public string FeaturedImage { get; set; } = "";
        public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
        public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;
        public DateTime? PublishedAt { get; set; }
        public DateTime? ScheduledAt { get; set; }
    }

    public class Category : FeatherData
    {
        [StringLength(128)] public string Name { get; set; } = "";
        [StringLength(128)] public string Slug { get; set; } = "";
        [StringLength(512)] public string Description { get; set; } = "";
        public int ParentId { get; set; } = 0;
        public int SortOrder { get; set; } = 0;
        public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    }

    public class Tag : FeatherData
    {
        [StringLength(128)] public string Name { get; set; } = "";
        [StringLength(128)] public string Slug { get; set; } = "";
        public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    }

    public class PostCategory : FeatherData
    {
        public int PostId { get; set; }
        public int CategoryId { get; set; }
    }

    public class PostTag : FeatherData
    {
        public int PostId { get; set; }
        public int TagId { get; set; }
    }

    public class MediaItem : FeatherData
    {
        [StringLength(255)] public string FileName { get; set; } = "";
        [StringLength(100)] public string ContentType { get; set; } = "";
        public long FileSize { get; set; }
        [StringLength(512)] public string FilePath { get; set; } = "";
        [StringLength(512)] public string ThumbnailPath { get; set; } = "";
        [StringLength(255)] public string AltText { get; set; } = "";
        [StringLength(128)] public string UploadedBy { get; set; } = "";
        [StringLength(50)] public string MediaType { get; set; } = "image";
        public int Width { get; set; }
        public int Height { get; set; }
        public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    }
}
