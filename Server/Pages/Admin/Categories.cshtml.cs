using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using SharpPress.Models;
using SharpPress.Services;
using SharpPress.Services.General;

namespace SharpPress.Pages
{
    public class CategoriesModel : PageModel
    {
        private readonly TaxonomyService _taxonomyService;
        private readonly UserService _userService;

        public CategoriesModel(TaxonomyService taxonomyService, UserService userService)
        {
            _taxonomyService = taxonomyService;
            _userService = userService;
        }

        public List<Category> Categories { get; set; } = new();
        public List<CategoryNode> CategoryTree { get; set; } = new();

        [BindProperty]
        public Category NewCategory { get; set; } = new();

        public async Task<IActionResult> OnGet()
        {
            var user = await _userService.GetUserAsync(User);
            if (user == null || !user.HasRole(UserRole.Admin))
                return RedirectToPage("/Login");

            Categories = await _taxonomyService.GetAllCategoriesAsync();
            CategoryTree = _taxonomyService.BuildCategoryTree(Categories);
            return Page();
        }

        public async Task<IActionResult> OnPostCreateAsync()
        {
            var user = await _userService.GetUserAsync(User);
            if (user == null || !user.HasRole(UserRole.Admin))
                return RedirectToPage("/Login");

            if (!string.IsNullOrWhiteSpace(NewCategory.Name))
            {
                await _taxonomyService.CreateCategoryAsync(NewCategory);
            }
            return RedirectToPage();
        }

        public async Task<IActionResult> OnPostDeleteAsync(int id)
        {
            var user = await _userService.GetUserAsync(User);
            if (user == null || !user.HasRole(UserRole.Admin))
                return RedirectToPage("/Login");

            await _taxonomyService.DeleteCategoryAsync(id);
            return RedirectToPage();
        }
    }
}
