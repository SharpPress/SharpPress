using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using SharpPress.Models;
using SharpPress.Services;
using SharpPress.Services.General;

namespace SharpPress.Pages
{
    public class PostsModel : PageModel
    {
        private readonly IPostService _postService;
        private readonly IUserService _userService;

        public PostsModel(IPostService postService, IUserService userService)
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
