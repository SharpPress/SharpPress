using SharpPress.Models;

namespace SharpPress.Services
{
    public class TaxonomyService
    {
        private readonly FeatherDatabase _db;
        private readonly Logger _logger;

        public TaxonomyService(FeatherDatabase db, Logger logger)
        {
            _db = db;
            _logger = logger;
        }

        public async Task<List<Category>> GetAllCategoriesAsync()
        {
            return await _db.GetAll<Category>();
        }

        public async Task<Category?> GetCategoryByIdAsync(int id)
        {
            return await _db.GetData<Category>(id);
        }

        public async Task<Category?> GetCategoryBySlugAsync(string slug)
        {
            return await _db.GetByLinq<Category>(c => c.Slug == slug);
        }

        public async Task<List<Category>> GetChildCategoriesAsync(int parentId)
        {
            return await _db.GetListByLinq<Category>(c => c.ParentId == parentId);
        }

        public async Task<List<Category>> GetRootCategoriesAsync()
        {
            return await _db.GetListByLinq<Category>(c => c.ParentId == 0);
        }

        public async Task<Category> CreateCategoryAsync(Category category)
        {
            if (string.IsNullOrEmpty(category.Slug))
                category.Slug = await GenerateUniqueCategorySlugAsync(category.Name);

            category.CreatedAt = DateTime.UtcNow;
            await _db.SaveData(category);
            _logger.Log($"Category created: {category.Name}");
            return category;
        }

        public async Task<Category> UpdateCategoryAsync(Category category)
        {
            if (string.IsNullOrEmpty(category.Slug))
                category.Slug = await GenerateUniqueCategorySlugAsync(category.Name);

            await _db.SaveData(category);
            _logger.Log($"Category updated: {category.Name}");
            return category;
        }

        public async Task DeleteCategoryAsync(int id)
        {
            var children = await GetChildCategoriesAsync(id);
            foreach (var child in children)
            {
                child.ParentId = 0;
                await _db.SaveData(child);
            }

            var postCategories = await _db.GetListByLinq<PostCategory>(pc => pc.CategoryId == id);
            foreach (var pc in postCategories)
                await _db.Delete<PostCategory>(pc.Id);

            await _db.Delete<Category>(id);
            _logger.Log($"Category deleted: ID {id}");
        }

        public async Task<List<Category>> GetCategoryHierarchyAsync()
        {
            var all = await _db.GetAll<Category>();
            return all.OrderBy(c => c.ParentId).ThenBy(c => c.SortOrder).ToList();
        }

        public List<CategoryNode> BuildCategoryTree(List<Category> categories)
        {
            var lookup = categories.ToDictionary(c => c.Id);
            var roots = new List<CategoryNode>();

            foreach (var cat in categories.Where(c => c.ParentId == 0))
            {
                roots.Add(BuildNode(cat, lookup));
            }

            return roots;
        }

        private CategoryNode BuildNode(Category cat, Dictionary<int, Category> lookup)
        {
            var node = new CategoryNode { Category = cat };
            var children = lookup.Values.Where(c => c.ParentId == cat.Id).ToList();
            foreach (var child in children)
                node.Children.Add(BuildNode(child, lookup));
            return node;
        }

        public async Task<List<Tag>> GetAllTagsAsync()
        {
            return await _db.GetAll<Tag>();
        }

        public async Task<Tag?> GetTagByIdAsync(int id)
        {
            return await _db.GetData<Tag>(id);
        }

        public async Task<Tag?> GetTagBySlugAsync(string slug)
        {
            return await _db.GetByLinq<Tag>(t => t.Slug == slug);
        }

        public async Task<Tag> CreateTagAsync(Tag tag)
        {
            if (string.IsNullOrEmpty(tag.Slug))
                tag.Slug = await GenerateUniqueTagSlugAsync(tag.Name);

            tag.CreatedAt = DateTime.UtcNow;
            await _db.SaveData(tag);
            _logger.Log($"Tag created: {tag.Name}");
            return tag;
        }

        public async Task<Tag> UpdateTagAsync(Tag tag)
        {
            if (string.IsNullOrEmpty(tag.Slug))
                tag.Slug = await GenerateUniqueTagSlugAsync(tag.Name);

            await _db.SaveData(tag);
            _logger.Log($"Tag updated: {tag.Name}");
            return tag;
        }

        public async Task DeleteTagAsync(int id)
        {
            var postTags = await _db.GetListByLinq<PostTag>(pt => pt.TagId == id);
            foreach (var pt in postTags)
                await _db.Delete<PostTag>(pt.Id);

            await _db.Delete<Tag>(id);
            _logger.Log($"Tag deleted: ID {id}");
        }

        public async Task<Tag> GetOrCreateTagByNameAsync(string name)
        {
            var existing = await _db.GetByLinq<Tag>(t => t.Name == name);
            if (existing != null) return existing;

            return await CreateTagAsync(new Tag { Name = name });
        }

        private async Task<string> GenerateUniqueCategorySlugAsync(string name)
        {
            var slug = PostService.GenerateSlug(name);
            var originalSlug = slug;
            int counter = 1;

            while (await _db.GetByLinq<Category>(c => c.Slug == slug) != null)
            {
                slug = $"{originalSlug}-{counter}";
                counter++;
            }

            return slug;
        }

        private async Task<string> GenerateUniqueTagSlugAsync(string name)
        {
            var slug = PostService.GenerateSlug(name);
            var originalSlug = slug;
            int counter = 1;

            while (await _db.GetByLinq<Tag>(t => t.Slug == slug) != null)
            {
                slug = $"{originalSlug}-{counter}";
                counter++;
            }

            return slug;
        }
    }

    public class CategoryNode
    {
        public Category Category { get; set; }
        public List<CategoryNode> Children { get; set; } = new();
    }
}
