using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using SharpPress.Models;
using SharpPress.Services;
using SharpPress.Services.General;

namespace SharpPress.Pages
{
    public class TagsModel : PageModel
    {
        private readonly ITaxonomyService _taxonomyService;
        private readonly IUserService _userService;

        public TagsModel(ITaxonomyService taxonomyService, IUserService userService)
        {
            _taxonomyService = taxonomyService;
            _userService = userService;
        }

        public List<Tag> Tags { get; set; } = new();

        [BindProperty]
        public Tag NewTag { get; set; } = new();

        public async Task<IActionResult> OnGet()
        {
            var user = await _userService.GetUserAsync(User);
            if (user == null || !user.HasRole(UserRole.Admin))
                return RedirectToPage("/Login");

            Tags = await _taxonomyService.GetAllTagsAsync();
            return Page();
        }

        public async Task<IActionResult> OnPostCreateAsync()
        {
            var user = await _userService.GetUserAsync(User);
            if (user == null || !user.HasRole(UserRole.Admin))
                return RedirectToPage("/Login");

            if (!string.IsNullOrWhiteSpace(NewTag.Name))
            {
                await _taxonomyService.CreateTagAsync(NewTag);
            }
            return RedirectToPage();
        }

        public async Task<IActionResult> OnPostDeleteAsync(int id)
        {
            var user = await _userService.GetUserAsync(User);
            if (user == null || !user.HasRole(UserRole.Admin))
                return RedirectToPage("/Login");

            await _taxonomyService.DeleteTagAsync(id);
            return RedirectToPage();
        }
    }
}
