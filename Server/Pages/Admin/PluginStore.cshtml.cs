using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using SharpPress.Services;
using System.Text.Json;

namespace SharpPress.Pages
{
    public class PluginStoreModel : PageModel
    {
        private readonly IHttpClientFactory _httpClientFactory;
        private readonly PluginManager pluginManager;

        private const string PluginsJsonUrl = "https://github.com/SharpPress/SharpPress_Plugins/raw/refs/heads/main/plugins.json";
        public string ErrorMessage { get; set; }

        public List<PluginInfo> Plugins { get; set; } = new();
        public List<PluginInfo> InstalledPlugins { get; set; } = new();
        public List<PluginInfo> IncompatiblePlugins { get; set; } = new();


        public PluginStoreModel(IHttpClientFactory httpClientFactory, PluginManager _pluginManager)
        {
            _httpClientFactory = httpClientFactory;
            pluginManager = _pluginManager;
        }

        public async Task OnGetAsync()
        {
            try
            {
                var client = _httpClientFactory.CreateClient();
                var json = await client.GetStringAsync(PluginsJsonUrl);
                var manifest = JsonSerializer.Deserialize<PluginsManifest>(json, new JsonSerializerOptions { PropertyNameCaseInsensitive = true });

                Plugins = manifest?.Plugins ?? new();

                if (Plugins.Count > 0)
                {
                    foreach(var plugin in pluginManager.PluginToAssemblyPath)
                    {
                        foreach(var pl in Plugins)
                        {
                            if (await AppVersion.IsNewer(await AppVersion.ToVersion(pl.MinVersion) ?? AppVersion.Version))
                            {
                                IncompatiblePlugins.Add(pl);
                            }
                        }

                        var installedPlugin = Plugins.FirstOrDefault(p => Path.GetFileName(p.DownloadUrl) == Path.GetFileName(plugin.Value));
                        if (installedPlugin != null)
                            InstalledPlugins.Add(installedPlugin);
                    }
                }
            }
            catch
            {
                ErrorMessage = "Failed to load plugins. Please try again later.";
            }
        }

        public async Task<IActionResult> OnPostInstallAsync(string id)
        {
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

            var fileName = Path.GetFileName(plugin.DownloadUrl);
            var filePath = Path.Combine("plugins", fileName);

            var existingKey = pluginManager.PluginToAssemblyPath.FirstOrDefault(p => p.Value == filePath).Key;

            if (System.IO.File.Exists(filePath) && !string.IsNullOrWhiteSpace(existingKey))
            {
                await pluginManager.UnloadPluginAsync(existingKey);
                System.IO.File.Delete(filePath);
            }

            try
            {
                var pluginBytes = await client.GetByteArrayAsync(plugin.DownloadUrl);
                await System.IO.File.WriteAllBytesAsync(filePath, pluginBytes);
            }
            catch
            {
                ErrorMessage = "Failed to download plugin.";
                return Page();
            }

            await pluginManager.LoadPluginFromFileAsync(filePath);
            return RedirectToPage("/Admin/Plugins");
        }

        public class PluginsManifest
        {
            public List<PluginInfo> Plugins { get; set; }
        }

        public class PluginInfo
        {
            public string Id { get; set; }
            public string Name { get; set; }
            public string Version { get; set; }
            public string Author { get; set; }
            public string Description { get; set; }
            public string Icon { get; set; }
            public string DownloadUrl { get; set; }
            public string MinVersion { get; set; }
        }
    }
}