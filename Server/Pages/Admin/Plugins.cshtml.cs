using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using SharpPress.Models;
using SharpPress.Services;

namespace SharpPress.Pages
{
    public class PluginsModel : PageModel
    {
        private readonly IUserService userService;
        private readonly PluginManager pluginManager;

        [TempData]
        public string? StatusMessage { get; set; }

        [TempData]
        public bool StatusIsError { get; set; }

        public PluginsModel(IUserService _userService, PluginManager _pluginManager)
        {
            userService = _userService;
            pluginManager = _pluginManager;
        }

        private async Task<User?> RequireAdminAsync()
        {
            var user = await userService.GetUserAsync(User);
            return user != null && user.HasRole(UserRole.Admin) ? user : null;
        }

        public async Task<IActionResult> OnGet()
        {
            if (await RequireAdminAsync() == null)
                return RedirectToPage("/Login");

            return Page();
        }

        public async Task<IActionResult> OnPostUpload(IFormFile pluginFile)
        {
            var admin = await RequireAdminAsync();
            if (admin == null)
                return RedirectToPage("/Login");

            if (pluginFile == null || !pluginFile.FileName.EndsWith(".dll", StringComparison.OrdinalIgnoreCase))
            {
                StatusMessage = "Choose a plugin .dll file.";
                StatusIsError = true;
                return RedirectToPage();
            }

            using var buffer = new MemoryStream();
            await pluginFile.CopyToAsync(buffer);

            var result = await pluginManager.InstallPluginAsync(Path.GetFileName(pluginFile.FileName), buffer.ToArray(), admin.Username);
            StatusMessage = result.Success ? $"Plugin '{result.PluginName}' is installed." : $"Plugin could not be installed: {result.Message}";
            StatusIsError = !result.Success;
            return RedirectToPage();
        }

        public async Task<IActionResult> OnPostEnableAsync(string name)
        {
            if (await RequireAdminAsync() == null)
                return RedirectToPage("/Login");

            var ok = await pluginManager.EnablePluginAsync(name);
            StatusMessage = ok ? $"Plugin '{name}' enabled." : $"Plugin '{name}' could not be enabled. See the server log.";
            StatusIsError = !ok;
            return RedirectToPage();
        }

        public async Task<IActionResult> OnPostDisableAsync(string name)
        {
            if (await RequireAdminAsync() == null)
                return RedirectToPage("/Login");

            var ok = await pluginManager.DisablePluginAsync(name);
            StatusMessage = ok ? $"Plugin '{name}' disabled." : $"Plugin '{name}' could not be disabled.";
            StatusIsError = !ok;
            return RedirectToPage();
        }

        public async Task<IActionResult> OnPostRemoveAsync(string name)
        {
            if (await RequireAdminAsync() == null)
                return RedirectToPage("/Login");

            var ok = await pluginManager.RemovePluginAsync(name);
            StatusMessage = ok ? $"Plugin '{name}' removed." : $"Plugin '{name}' could not be removed.";
            StatusIsError = !ok;
            return RedirectToPage();
        }
    }
}
