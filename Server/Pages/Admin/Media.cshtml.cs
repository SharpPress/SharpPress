using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using SharpPress.Models;
using SharpPress.Services;

namespace SharpPress.Pages
{
    public class MediaModel : PageModel
    {
        private readonly MediaService _mediaService;
        private readonly UserService _userService;

        public MediaModel(MediaService mediaService, UserService userService)
        {
            _mediaService = mediaService;
            _userService = userService;
        }

        public List<MediaItem> MediaItems { get; set; } = new();

        [BindProperty]
        public string AltText { get; set; } = "";

        public async Task<IActionResult> OnGet()
        {
            var user = await _userService.GetUserAsync(User);
            if (user == null || !user.HasRole(UserRole.Admin))
                return RedirectToPage("/Login");

            MediaItems = await _mediaService.GetAllAsync();
            return Page();
        }

        public async Task<IActionResult> OnPostUploadAsync()
        {
            var user = await _userService.GetUserAsync(User);
            if (user == null || !user.HasRole(UserRole.Admin))
                return RedirectToPage("/Login");

            var files = Request.Form.Files;
            if (files == null || files.Count == 0)
                return RedirectToPage();

            foreach (var file in files)
            {
                if (file.Length > 0)
                {
                    using var stream = file.OpenReadStream();
                    await _mediaService.UploadAsync(stream, file.FileName, file.ContentType, User.Identity.Name);
                }
            }

            return RedirectToPage();
        }

        public async Task<IActionResult> OnPostDeleteAsync(int id)
        {
            var user = await _userService.GetUserAsync(User);
            if (user == null || !user.HasRole(UserRole.Admin))
                return RedirectToPage("/Login");

            await _mediaService.DeleteAsync(id);
            return RedirectToPage();
        }

        public async Task<IActionResult> OnPostUpdateAltTextAsync(int id, string altText)
        {
            var user = await _userService.GetUserAsync(User);
            if (user == null || !user.HasRole(UserRole.Admin))
                return RedirectToPage("/Login");

            await _mediaService.UpdateAltTextAsync(id, altText);
            return RedirectToPage();
        }
    }
}
