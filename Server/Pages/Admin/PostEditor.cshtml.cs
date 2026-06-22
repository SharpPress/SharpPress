using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using SharpPress.Models;
using SharpPress.Services;

namespace SharpPress.Pages
{
    public class PostEditorModel : PageModel
    {
        private readonly PostService _postService;
        private readonly TaxonomyService _taxonomyService;
        private readonly MediaService _mediaService;
        private readonly UserService _userService;

        public PostEditorModel(PostService postService, TaxonomyService taxonomyService, MediaService mediaService, UserService userService)
        {
            _postService = postService;
            _taxonomyService = taxonomyService;
            _mediaService = mediaService;
            _userService = userService;
        }

        [BindProperty]
        public Post Post { get; set; } = new();

        [BindProperty]
        public List<int> SelectedCategoryIds { get; set; } = new();

        [BindProperty]
        public List<int> SelectedTagIds { get; set; } = new();

        public List<Category> AllCategories { get; set; } = new();
        public List<Tag> AllTags { get; set; } = new();
        public List<MediaItem> MediaItems { get; set; } = new();
        public bool IsNew { get; set; } = true;

        public async Task<IActionResult> OnGetAsync(int id)
        {
            var user = await _userService.GetUserAsync(User);
            if (user == null || !user.HasRole(UserRole.Admin))
                return RedirectToPage("/Login");

            AllCategories = await _taxonomyService.GetAllCategoriesAsync();
            AllTags = await _taxonomyService.GetAllTagsAsync();
            MediaItems = await _mediaService.GetAllAsync();

            if (id > 0)
            {
                var post = await _postService.GetPostByIdAsync(id);
                if (post == null) return RedirectToPage("/Admin/Posts");

                Post = post;
                IsNew = false;

                var postCats = await _postService.GetPostCategoriesAsync(id);
                SelectedCategoryIds = postCats.Select(c => c.Id).ToList();

                var postTags = await _postService.GetPostTagsAsync(id);
                SelectedTagIds = postTags.Select(t => t.Id).ToList();
            }

            return Page();
        }

        public async Task<IActionResult> OnPostAsync()
        {
            var user = await _userService.GetUserAsync(User);
            if (user == null || !user.HasRole(UserRole.Admin))
                return RedirectToPage("/Login");

            if (string.IsNullOrWhiteSpace(Post.Title))
            {
                ModelState.AddModelError("Post.Title", "Title is required");
                AllCategories = await _taxonomyService.GetAllCategoriesAsync();
                AllTags = await _taxonomyService.GetAllTagsAsync();
                MediaItems = await _mediaService.GetAllAsync();
                return Page();
            }

            if (string.IsNullOrWhiteSpace(Post.Slug))
                Post.Slug = await _postService.GenerateUniqueSlugAsync(Post.Title);

            Post.AuthorUsername = User.Identity.Name;

            if (Post.Id == 0)
            {
                await _postService.CreatePostAsync(Post);
            }
            else
            {
                await _postService.UpdatePostAsync(Post);
            }

            if (SelectedCategoryIds != null && SelectedCategoryIds.Any())
                await _postService.SetPostCategoriesAsync(Post.Id, SelectedCategoryIds);
            else
                await _postService.SetPostCategoriesAsync(Post.Id, new List<int>());

            if (SelectedTagIds != null && SelectedTagIds.Any())
                await _postService.SetPostTagsAsync(Post.Id, SelectedTagIds);
            else
                await _postService.SetPostTagsAsync(Post.Id, new List<int>());

            return RedirectToPage("/Admin/Posts");
        }
    }
}
