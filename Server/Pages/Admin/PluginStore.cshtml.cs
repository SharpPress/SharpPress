using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using SharpPress.Models;
using SharpPress.Services;
using System.Security.Cryptography;
using System.Text.Json;

namespace SharpPress.Pages
{
    public class PluginStoreModel : PageModel
    {
        private readonly IHttpClientFactory _httpClientFactory;
        private readonly PluginManager pluginManager;
        private readonly IUserService userService;
        private readonly PackageManager packageManager;

        private const string PluginsJsonUrl = "https://github.com/SharpPress/SharpPress_Plugins/raw/refs/heads/main/plugins.json";
        public string? ErrorMessage { get; set; }

        public List<PluginInfo> Plugins { get; set; } = new();
        public List<PluginInfo> InstalledPlugins { get; set; } = new();
        public List<PluginInfo> IncompatiblePlugins { get; set; } = new();

        public PluginStoreModel(IHttpClientFactory httpClientFactory, PluginManager _pluginManager, IUserService _userService, PackageManager _packageManager)
        {
            _httpClientFactory = httpClientFactory;
            pluginManager = _pluginManager;
            userService = _userService;
            packageManager = _packageManager;
        }

        public async Task<IActionResult> OnGetAsync()
        {
            if (!await IsAdminAsync())
                return RedirectToPage("/Login");

            try
            {
                var client = _httpClientFactory.CreateClient();
                var json = await client.GetStringAsync(PluginsJsonUrl);
                var manifest = JsonSerializer.Deserialize<PluginsManifest>(json, new JsonSerializerOptions { PropertyNameCaseInsensitive = true });

                Plugins = manifest?.Plugins ?? new();

                var installedFiles = pluginManager.PluginToAssemblyPath.Values
                    .Select(v => Path.GetFileName(v))
                    .ToHashSet(StringComparer.OrdinalIgnoreCase);

                foreach (var plugin in Plugins)
                {
                    if (await IsIncompatibleAsync(plugin))
                        IncompatiblePlugins.Add(plugin);

                    if (!string.IsNullOrEmpty(plugin.DownloadUrl) && installedFiles.Contains(Path.GetFileName(plugin.DownloadUrl)))
                        InstalledPlugins.Add(plugin);
                }
            }
            catch
            {
                ErrorMessage = "Failed to load plugins. Please try again later.";
            }

            return Page();
        }

        public async Task<IActionResult> OnPostInstallAsync(string id)
        {
            var admin = await userService.GetUserAsync(User);
            if (admin == null || !admin.HasRole(UserRole.Admin))
                return RedirectToPage("/Login");

            var client = _httpClientFactory.CreateClient();
            try
            {
                var json = await client.GetStringAsync(PluginsJsonUrl);
                var manifest = JsonSerializer.Deserialize<PluginsManifest>(json, new JsonSerializerOptions { PropertyNameCaseInsensitive = true });
                Plugins = manifest?.Plugins ?? new();
            }
            catch
            {
                ErrorMessage = "Failed to load plugins. Please try again later.";
                return Page();
            }

            var plugin = Plugins.FirstOrDefault(p => p.Id == id);
            if (plugin == null)
            {
                ErrorMessage = "Plugin not found.";
                return Page();
            }

            if (await IsIncompatibleAsync(plugin))
            {
                ErrorMessage = $"This plugin requires SharpPress {plugin.MinVersion} or newer.";
                return Page();
            }

            if (!Uri.TryCreate(plugin.DownloadUrl, UriKind.Absolute, out var downloadUri) || downloadUri.Scheme != Uri.UriSchemeHttps)
            {
                ErrorMessage = "The plugin download address is not valid.";
                return Page();
            }

            var fileName = Path.GetFileName(downloadUri.LocalPath);

            byte[] pluginBytes;
            try
            {
                pluginBytes = await client.GetByteArrayAsync(downloadUri);
            }
            catch
            {
                ErrorMessage = "Failed to download plugin.";
                return Page();
            }

            if (!string.IsNullOrWhiteSpace(plugin.Sha256)
                && !Convert.ToHexString(SHA256.HashData(pluginBytes)).Equals(plugin.Sha256.Trim(), StringComparison.OrdinalIgnoreCase))
            {
                ErrorMessage = "The downloaded plugin did not match its published checksum and was not installed.";
                return Page();
            }

            PluginLoadResult result;
            if (fileName.EndsWith(".pkg", StringComparison.OrdinalIgnoreCase))
            {
                var package = await packageManager.GetPackageFromByteArray(pluginBytes);
                result = package == null
                    ? new PluginLoadResult(false, "Invalid package.")
                    : await packageManager.InstallPackage(package, admin.Username);
            }
            else
            {
                result = await pluginManager.InstallPluginAsync(fileName, pluginBytes, admin.Username);
            }

            if (!result.Success)
            {
                ErrorMessage = $"Plugin could not be installed: {result.Message}";
                return Page();
            }

            return RedirectToPage("/Admin/Plugins");
        }

        private async Task<bool> IsAdminAsync()
        {
            var user = await userService.GetUserAsync(User);
            return user != null && user.HasRole(UserRole.Admin);
        }

        private static async Task<bool> IsIncompatibleAsync(PluginInfo plugin)
        {
            var required = await AppVersion.ToVersion(plugin.MinVersion);
            return required != null && await AppVersion.IsNewer(required);
        }

        public class PluginsManifest
        {
            public List<PluginInfo> Plugins { get; set; } = new();
        }

        public class PluginInfo
        {
            public string Id { get; set; } = "";
            public string Name { get; set; } = "";
            public string Version { get; set; } = "";
            public string Author { get; set; } = "";
            public string Description { get; set; } = "";
            public string Icon { get; set; } = "";
            public string DownloadUrl { get; set; } = "";
            public string MinVersion { get; set; } = "";
            public string Sha256 { get; set; } = "";
        }
    }
}
