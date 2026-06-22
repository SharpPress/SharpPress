using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using SharpPress.Models;
using SharpPress.Services;

namespace SharpPress.Pages
{
    public class PostsModel : PageModel
    {
        private readonly PostService _postService;
        private readonly UserService _userService;

        public PostsModel(PostService postService, UserService userService)
        {
            _postService = postService;
            _userService = userService;
        }

        public List<Post> Posts { get; set; } = new();

        public async Task<IActionResult> OnGet()
        {
            var user = await _userService.GetUserAsync(User);
            if (user == null || !user.HasRole(UserRole.Admin))
                return RedirectToPage("/Login");

            Posts = await _postService.GetAllPostsAsync();
            return Page();
        }

        public async Task<IActionResult> OnPostDeleteAsync(int id)
        {
            var user = await _userService.GetUserAsync(User);
            if (user == null || !user.HasRole(UserRole.Admin))
                return RedirectToPage("/Login");

            await _postService.DeletePostAsync(id);
            return RedirectToPage();
        }
    }
}
