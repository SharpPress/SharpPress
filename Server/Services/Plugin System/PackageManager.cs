using Newtonsoft.Json;
using SharpPress.Plugins;
using System.Text;

namespace SharpPress.Services
{
    public class PackageManager
    {
        private readonly Logger _logger;
        private readonly PluginManager _pluginManager;

        public PackageManager(Logger logger, PluginManager pluginManager)
        {
            _logger = logger;
            _pluginManager = pluginManager;
        }

        public async Task<PluginLoadResult> InstallPackage(Package package, string installedBy = "")
        {
            try
            {
                if (package == null || package.PluginDllData == null || package.PluginDllData.Length == 0 || string.IsNullOrWhiteSpace(package.Name))
                    return new PluginLoadResult(false, "Invalid package.");

                if (!PluginPaths.IsSafeName(package.Name))
                    return new PluginLoadResult(false, "Invalid package name.");

                var root = PluginPaths.EnsureRoot();
                var existing = _pluginManager.PluginToAssemblyPath
                    .Where(p => Path.GetFileNameWithoutExtension(p.Value).Equals(package.Name, StringComparison.OrdinalIgnoreCase))
                    .Select(p => p.Value)
                    .FirstOrDefault();

                var pluginFolder = existing != null
                    ? Path.GetDirectoryName(existing)!
                    : Path.Combine(root, package.Name);

                if (!PluginPaths.IsInside(root, Path.Combine(pluginFolder, "x")))
                    return new PluginLoadResult(false, "Invalid package location.");

                Directory.CreateDirectory(pluginFolder);

                foreach (var file in package.FileDataList)
                {
                    var normalized = file.Key.Replace('\\', '/').TrimStart('/');
                    var destination = Path.GetFullPath(Path.Combine(pluginFolder, normalized));

                    if (string.IsNullOrWhiteSpace(normalized) || !PluginPaths.IsInside(pluginFolder, destination))
                        return new PluginLoadResult(false, $"Package contains an illegal path: {file.Key}");

                    var extension = Path.GetExtension(destination);
                    if (extension.Equals(".dll", StringComparison.OrdinalIgnoreCase) && Path.GetFileName(destination).Equals(package.Name + ".dll", StringComparison.OrdinalIgnoreCase))
                        continue;

                    Directory.CreateDirectory(Path.GetDirectoryName(destination)!);
                    await File.WriteAllBytesAsync(destination, file.Value);
                }

                var targetDll = existing != null ? Path.GetFileName(existing) : package.Name + ".dll";

                if (existing == null)
                {
                    var target = Path.Combine(pluginFolder, targetDll);
                    if (!PluginPaths.IsInside(root, target))
                        return new PluginLoadResult(false, "Invalid package location.");
                }

                return await InstallIntoFolderAsync(pluginFolder, targetDll, package.PluginDllData, installedBy);
            }
            catch (Exception ex)
            {
                _logger.LogError($"Package install failed: {ex.Message}");
                return new PluginLoadResult(false, ex.Message);
            }
        }

        private async Task<PluginLoadResult> InstallIntoFolderAsync(string folder, string dllName, byte[] data, string installedBy)
        {
            var root = PluginPaths.Root.TrimEnd(Path.DirectorySeparatorChar);
            var isTopLevel = folder.TrimEnd(Path.DirectorySeparatorChar).Equals(root, StringComparison.OrdinalIgnoreCase);

            if (isTopLevel)
                return await _pluginManager.InstallPluginAsync(dllName, data, installedBy);

            var path = Path.Combine(folder, dllName);
            var temp = path + ".tmp";
            var previous = File.Exists(path) ? await File.ReadAllBytesAsync(path) : null;

            await File.WriteAllBytesAsync(temp, data);
            File.Move(temp, path, overwrite: true);

            var result = await _pluginManager.LoadPluginFromFileAsync(path);

            if (!result.Success)
            {
                try
                {
                    if (previous != null) await File.WriteAllBytesAsync(path, previous);
                    else File.Delete(path);
                }
                catch (Exception ex)
                {
                    _logger.LogWarning($"Could not roll back package file: {ex.Message}");
                }
            }

            return result;
        }

        public Task<Package?> GetPackageFromByteArray(byte[] data)
        {
            try
            {
                var json = Encoding.UTF8.GetString(data);
                if (string.IsNullOrWhiteSpace(json)) return Task.FromResult<Package?>(null);

                var package = JsonConvert.DeserializeObject<Package>(json);
                if (package == null || package.PluginDllData == null || package.Name == null) return Task.FromResult<Package?>(null);

                return Task.FromResult<Package?>(package);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex.Message);
                return Task.FromResult<Package?>(null);
            }
        }
    }

    public class Package
    {
        public string Name = "";
        public byte[] PluginDllData = [];
        public Dictionary<string, byte[]> FileDataList = new Dictionary<string, byte[]>();
    }
}
