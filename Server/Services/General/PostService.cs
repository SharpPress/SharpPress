using SharpPress.Events;
using SharpPress.Models;
using SharpPress.Services.Database;

namespace SharpPress.Services.General
{
    public class PostService
    {
        private readonly FeatherDatabase _db;
        private readonly Logger _logger;
        private readonly IEventBus _eventBus;

        public PostService(FeatherDatabase db, Logger logger, IEventBus eventBus)
        {
            _db = db;
            _logger = logger;
            _eventBus = eventBus;
        }

        public async Task<List<Post>> GetAllPostsAsync()
        {
            return await _db.GetAll<Post>();
        }

        public async Task<List<Post>> GetPostsByStatusAsync(PostStatus status)
        {
            return await _db.GetListByLinq<Post>(p => p.Status == status);
        }

        public async Task<List<Post>> GetPublishedPostsAsync()
        {
            return await _db.GetListByLinq<Post>(p => p.Status == PostStatus.Published);
        }

        public async Task<Post?> GetPostByIdAsync(int id)
        {
            return await _db.GetData<Post>(id);
        }

        public async Task<Post?> GetPostBySlugAsync(string slug)
        {
            return await _db.GetByLinq<Post>(p => p.Slug == slug);
        }

        public async Task<Post> CreatePostAsync(Post post)
        {
            if (string.IsNullOrEmpty(post.Slug))
                post.Slug = await GenerateUniqueSlugAsync(post.Title);

            post.CreatedAt = DateTime.UtcNow;
            post.UpdatedAt = DateTime.UtcNow;

            if (post.Status == PostStatus.Published)
                post.PublishedAt = DateTime.UtcNow;

            await _db.SaveData(post);
            _logger.Log($"Post created: {post.Title}");

            await _eventBus.PublishAsync(new PostCreatedEvent(post));
            return post;
        }

        public async Task<Post> UpdatePostAsync(Post post)
        {
            var existing = await _db.GetData<Post>(post.Id);
            if (existing == null) throw new Exception("Post not found");

            post.UpdatedAt = DateTime.UtcNow;

            if (post.Status == PostStatus.Published && existing.Status != PostStatus.Published)
                post.PublishedAt = DateTime.UtcNow;

            await _db.SaveData(post);
            _logger.Log($"Post updated: {post.Title}");

            await _eventBus.PublishAsync(new PostUpdatedEvent(post));
            return post;
        }

        public async Task DeletePostAsync(int id)
        {
            var post = await _db.GetData<Post>(id);
            if (post == null) return;

            var postCategories = await _db.GetListByLinq<PostCategory>(pc => pc.PostId == id);
            foreach (var pc in postCategories)
                await _db.Delete<PostCategory>(pc.Id);

            var postTags = await _db.GetListByLinq<PostTag>(pt => pt.PostId == id);
            foreach (var pt in postTags)
                await _db.Delete<PostTag>(pt.Id);

            await _db.Delete<Post>(id);
            _logger.Log($"Post deleted: {post.Title}");

            await _eventBus.PublishAsync(new PostDeletedEvent(post));
        }

        public async Task<string> GenerateUniqueSlugAsync(string title)
        {
            var slug = GenerateSlug(title);
            var originalSlug = slug;
            int counter = 1;

            while (await _db.GetByLinq<Post>(p => p.Slug == slug) != null)
            {
                slug = $"{originalSlug}-{counter}";
                counter++;
            }

            return slug;
        }

        public static string GenerateSlug(string text)
        {
            if (string.IsNullOrEmpty(text)) return "untitled";

            var slug = text.ToLowerInvariant().Trim();
            slug = System.Text.RegularExpressions.Regex.Replace(slug, @"[^a-z0-9\s-]", "");
            slug = System.Text.RegularExpressions.Regex.Replace(slug, @"[\s-]+", "-");
            slug = slug.Trim('-');

            if (string.IsNullOrEmpty(slug)) return "untitled";
            return slug;
        }

        public async Task<List<Post>> GetScheduledPostsDueAsync()
        {
            var now = DateTime.UtcNow;
            var scheduled = await _db.GetListByLinq<Post>(p => p.Status == PostStatus.Scheduled);
            return scheduled.Where(p => p.ScheduledAt.HasValue && p.ScheduledAt.Value <= now).ToList();
        }

        public async Task PublishScheduledPostsAsync()
        {
            var duePosts = await GetScheduledPostsDueAsync();
            foreach (var post in duePosts)
            {
                post.Status = PostStatus.Published;
                post.PublishedAt = DateTime.UtcNow;
                post.UpdatedAt = DateTime.UtcNow;
                await _db.SaveData(post);
                _logger.Log($"Scheduled post published: {post.Title}");
            }
        }

        public async Task SetPostCategoriesAsync(int postId, List<int> categoryIds)
        {
            var existing = await _db.GetListByLinq<PostCategory>(pc => pc.PostId == postId);
            foreach (var pc in existing)
                await _db.Delete<PostCategory>(pc.Id);

            foreach (var catId in categoryIds)
            {
                await _db.SaveData(new PostCategory { PostId = postId, CategoryId = catId });
            }
        }

        public async Task SetPostTagsAsync(int postId, List<int> tagIds)
        {
            var existing = await _db.GetListByLinq<PostTag>(pt => pt.PostId == postId);
            foreach (var pt in existing)
                await _db.Delete<PostTag>(pt.Id);

            foreach (var tagId in tagIds)
            {
                await _db.SaveData(new PostTag { PostId = postId, TagId = tagId });
            }
        }

        public async Task<List<Category>> GetPostCategoriesAsync(int postId)
        {
            var postCategories = await _db.GetListByLinq<PostCategory>(pc => pc.PostId == postId);
            var categories = new List<Category>();
            foreach (var pc in postCategories)
            {
                var cat = await _db.GetData<Category>(pc.CategoryId);
                if (cat != null) categories.Add(cat);
            }
            return categories;
        }

        public async Task<List<Tag>> GetPostTagsAsync(int postId)
        {
            var postTags = await _db.GetListByLinq<PostTag>(pt => pt.PostId == postId);
            var tags = new List<Tag>();
            foreach (var pt in postTags)
            {
                var tag = await _db.GetData<Tag>(pt.TagId);
                if (tag != null) tags.Add(tag);
            }
            return tags;
        }
    }
}
