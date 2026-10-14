using Microsoft.AspNetCore.Mvc.ApplicationParts;
using Microsoft.AspNetCore.Mvc.Infrastructure;
using Microsoft.Extensions.Primitives;
using SharpPress.Events;
using SharpPress.Models;
using SharpPress.Plugins;
using System.Collections.Concurrent;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Security.Cryptography;

namespace SharpPress.Services
{
    public sealed record PluginLoadResult(bool Success, string Message, string? PluginName = null);

    public sealed class PluginInfo
    {
        public string Name { get; init; } = "";
        public string Version { get; init; } = "";
        public string Author { get; init; } = "";
        public string? Description { get; init; }
        public string FilePath { get; init; } = "";
        public bool Enabled { get; init; }
        public bool Running { get; init; }
        public string? Error { get; init; }
    }

    internal sealed class PluginRecord
    {
        public string Name { get; init; } = "";
        public string Version { get; init; } = "";
        public string Author { get; init; } = "";
        public string? Description { get; init; }
        public string FilePath { get; init; } = "";
        public string Hash { get; init; } = "";
        public byte[]? LastGoodBytes { get; init; }
        public bool Enabled { get; set; }
        public bool Running { get; set; }
        public string? Error { get; set; }
        public string? AssemblyKey { get; set; }
        public PluginLoadContext? Context { get; set; }
        public Assembly? Assembly { get; set; }
        public IPlugin? Instance { get; set; }
        public PluginContext? HostContext { get; set; }
        public PluginScope? Scope { get; set; }
        public SemaphoreSlim Gate { get; } = new(1, 1);
    }

    public class PluginManager
    {
        private readonly Logger _logger;
        private readonly IEventBus _eventBus;
        private readonly IServiceProvider _serviceProvider;
        private readonly IServiceScopeFactory _scopeFactory;
        private readonly IAdminMenuService _adminMenuService;
        private readonly PluginActionDescriptorChangeProvider _changeProvider;
        private readonly PluginStateService _pluginStateService;

        private readonly SemaphoreSlim _lifecycle = new(1, 1);
        private readonly ConcurrentDictionary<string, PluginRecord> _records = new(StringComparer.OrdinalIgnoreCase);
        private readonly ConcurrentDictionary<string, string> _assemblyToPlugin = new();
        private readonly ConcurrentDictionary<string, CancellationTokenSource> _pendingFileEvents = new();

        private ApplicationPartManager? _partManager;
        private FileSystemWatcher? _watcher;

        public PluginManager(
            Logger logger,
            IEventBus eventBus,
            IServiceProvider serviceProvider,
            IServiceScopeFactory scopeFactory,
            IAdminMenuService adminMenuService,
            PluginActionDescriptorChangeProvider changeProvider,
            PluginStateService pluginStateService)
        {
            _logger = logger;
            _eventBus = eventBus;
            _serviceProvider = serviceProvider;
            _scopeFactory = scopeFactory;
            _adminMenuService = adminMenuService;
            _changeProvider = changeProvider;
            _pluginStateService = pluginStateService;
        }

        public IReadOnlyDictionary<string, string> PluginToAssemblyPath
            => _records.Values.ToDictionary(r => r.Name, r => r.FilePath, StringComparer.OrdinalIgnoreCase);

        public void Initialize(ApplicationPartManager partManager, IWebHostEnvironment env)
        {
            _partManager = partManager;
            _logger.Log("✅ PluginManager initialized");
        }

        public IReadOnlyDictionary<string, PluginInfo> GetLoadedPlugins()
            => _records.Values.OrderBy(r => r.Name, StringComparer.OrdinalIgnoreCase)
                .ToDictionary(r => r.Name, ToInfo, StringComparer.OrdinalIgnoreCase);

        public IReadOnlyDictionary<string, PluginInfo> GetEnabledPlugins()
            => _records.Values.Where(r => r.Running).OrderBy(r => r.Name, StringComparer.OrdinalIgnoreCase)
                .ToDictionary(r => r.Name, ToInfo, StringComparer.OrdinalIgnoreCase);

        public bool IsPluginEnabled(string pluginName)
            => _records.TryGetValue(pluginName, out var record) && record.Running;

        public bool IsTypeFromDisabledPlugin(Type type)
        {
            var key = type.Assembly.FullName;
            if (key != null && _assemblyToPlugin.TryGetValue(key, out var pluginName))
                return !IsPluginEnabled(pluginName);

            return false;
        }

        public async Task LoadPluginsAsync(string? pluginsDirectory = null)
        {
            var root = pluginsDirectory == null ? PluginPaths.EnsureRoot() : Path.GetFullPath(pluginsDirectory);
            Directory.CreateDirectory(root);

            await _lifecycle.WaitAsync();
            try
            {
                foreach (var file in Discover(root))
                {
                    var result = await LoadFileLockedAsync(file);
                    if (!result.Success && !IsSilentFailure(result))
                        _logger.LogError($"Plugin '{Path.GetFileName(file)}' failed to load: {result.Message}");
                }
            }
            finally
            {
                _lifecycle.Release();
            }

            StartWatcher(root);
            _logger.Log($"✅ Loaded {_records.Values.Count(r => r.Running)} of {_records.Count} plugins");
        }

        public async Task<PluginLoadResult> LoadPluginFromFileAsync(string dllFile, string? unusedTempDir = null)
        {
            await _lifecycle.WaitAsync();
            try
            {
                return await LoadFileLockedAsync(dllFile);
            }
            finally
            {
                _lifecycle.Release();
            }
        }

        public Task<PluginLoadResult> LoadPluginAsync(string dllPath) => LoadPluginFromFileAsync(dllPath);

        public async Task<PluginLoadResult> InstallPluginAsync(string fileName, byte[] data, string uploadedBy = "")
        {
            var baseName = Path.GetFileNameWithoutExtension(fileName);
            if (!PluginPaths.IsSafeName(baseName) || !Path.GetExtension(fileName).Equals(".dll", StringComparison.OrdinalIgnoreCase))
                return new PluginLoadResult(false, "Invalid plugin file name.");

            if (!PluginLoadContext.IsPluginAssembly(data))
                return new PluginLoadResult(false, "The file is not a SharpPress plugin. Plugins must reference SharpPress.Abstractions.");

            await _lifecycle.WaitAsync();
            try
            {
                var root = PluginPaths.EnsureRoot();
                var existing = _records.Values.FirstOrDefault(r => Path.GetFileName(r.FilePath).Equals(fileName, StringComparison.OrdinalIgnoreCase));
                var target = existing?.FilePath ?? Path.Combine(root, fileName);

                byte[]? previous = null;
                if (existing == null && File.Exists(target))
                {
                    try { previous = ReadShared(target); }
                    catch (Exception ex) { _logger.LogWarning($"Could not read existing plugin file: {ex.Message}"); }
                }

                var temp = target + ".tmp";
                await File.WriteAllBytesAsync(temp, data);
                File.Move(temp, target, overwrite: true);

                var result = await LoadFileLockedAsync(target);

                if (!result.Success && existing == null)
                {
                    try
                    {
                        if (previous != null) await File.WriteAllBytesAsync(target, previous);
                        else File.Delete(target);
                    }
                    catch (Exception ex)
                    {
                        _logger.LogWarning($"Could not roll back plugin file: {ex.Message}");
                    }
                }

                if (result.Success)
                    await _eventBus.PublishAsync(new PluginUploadedEvent(fileName, uploadedBy));

                return result;
            }
            catch (Exception ex)
            {
                _logger.LogError($"Plugin install failed: {ex.Message}");
                return new PluginLoadResult(false, ex.Message);
            }
            finally
            {
                _lifecycle.Release();
            }
        }

        public async Task<bool> EnablePluginAsync(string pluginName)
        {
            await _lifecycle.WaitAsync();
            try
            {
                if (!_records.TryGetValue(pluginName, out var record))
                {
                    _logger.LogError($"Plugin not found: {pluginName}");
                    return false;
                }

                if (record.Running) return true;

                _pluginStateService.SetEnabled(record.Name, true);
                var result = await ReloadRecordLockedAsync(record);
                if (!result.Success)
                {
                    _logger.LogError($"Plugin '{pluginName}' could not be enabled: {result.Message}");
                    return false;
                }

                await PublishEnabledAsync(pluginName);
                _logger.Log($"✅ Plugin enabled: {pluginName}");
                return true;
            }
            finally
            {
                _lifecycle.Release();
            }
        }

        public async Task<bool> DisablePluginAsync(string pluginName)
        {
            await _lifecycle.WaitAsync();
            try
            {
                if (!_records.TryGetValue(pluginName, out var record))
                {
                    _logger.LogError($"Plugin not found: {pluginName}");
                    return false;
                }

                _pluginStateService.SetEnabled(record.Name, false);
                record.Enabled = false;
                record.Error = null;

                if (record.Running || record.Context != null)
                    await StopLockedAsync(record, callUnload: true);

                await _eventBus.PublishAsync(new PluginDisabledEvent(record.Name));
                _logger.Log($"🔇 Plugin disabled and unloaded: {pluginName}");
                return true;
            }
            finally
            {
                _lifecycle.Release();
            }
        }

        public async Task UnloadPluginAsync(string pluginName)
        {
            await _lifecycle.WaitAsync();
            try
            {
                if (!_records.TryGetValue(pluginName, out var record)) return;
                await RemoveRecordLockedAsync(record);
                await _eventBus.PublishAsync(new PluginUnloadedEvent(pluginName));
                _logger.Log($"🧹 Plugin unloaded: {pluginName}");
            }
            finally
            {
                _lifecycle.Release();
            }
        }

        public async Task<bool> RemovePluginAsync(string pluginName)
        {
            await _lifecycle.WaitAsync();
            try
            {
                if (!_records.TryGetValue(pluginName, out var record)) return false;

                var path = record.FilePath;
                await RemoveRecordLockedAsync(record);

                try
                {
                    if (File.Exists(path)) File.Delete(path);
                    var pdb = Path.ChangeExtension(path, ".pdb");
                    if (File.Exists(pdb)) File.Delete(pdb);

                    var directory = Path.GetDirectoryName(path);
                    if (directory != null
                        && !directory.TrimEnd(Path.DirectorySeparatorChar).Equals(PluginPaths.Root.TrimEnd(Path.DirectorySeparatorChar), StringComparison.OrdinalIgnoreCase)
                        && PluginPaths.IsInside(PluginPaths.Root, directory)
                        && Path.GetFileName(directory).Equals(Path.GetFileNameWithoutExtension(path), StringComparison.OrdinalIgnoreCase))
                        Directory.Delete(directory, true);
                }
                catch (Exception ex)
                {
                    _logger.LogError($"Could not delete plugin files for '{pluginName}': {ex.Message}");
                }

                _pluginStateService.Remove(pluginName);
                await _eventBus.PublishAsync(new PluginUnloadedEvent(pluginName));
                _logger.Log($"🗑️ Plugin removed: {pluginName}");
                return true;
            }
            finally
            {
                _lifecycle.Release();
            }
        }

        public async Task ReloadAllPluginsAsync()
        {
            var root = PluginPaths.EnsureRoot();

            await _lifecycle.WaitAsync();
            try
            {
                foreach (var record in _records.Values.ToList())
                    await RemoveRecordLockedAsync(record);

                foreach (var file in Discover(root))
                {
                    var result = await LoadFileLockedAsync(file);
                    if (!result.Success && !IsSilentFailure(result))
                        _logger.LogError($"Plugin '{Path.GetFileName(file)}' failed to load: {result.Message}");
                }

                await _eventBus.PublishAsync(new PluginsReloadedEvent(_records.Count));
            }
            finally
            {
                _lifecycle.Release();
            }
        }

        public async Task UnloadAllPluginsAsync()
        {
            StopWatcher();

            await _lifecycle.WaitAsync();
            try
            {
                foreach (var record in _records.Values.ToList())
                    await RemoveRecordLockedAsync(record);

                _logger.Log("🧹 Plugins unloaded");
            }
            finally
            {
                _lifecycle.Release();
            }
        }

        public async Task UpdateLoadedPluginsAsync(CancellationToken cancellationToken = default)
        {
            foreach (var record in _records.Values)
            {
                if (cancellationToken.IsCancellationRequested) break;
                if (!record.Running) continue;
                if (!await record.Gate.WaitAsync(0, cancellationToken)) continue;

                try
                {
                    if (record.Running)
                        await InvokeUpdateAsync(record);
                }
                catch (Exception ex)
                {
                    _logger.LogError($"Plugin update failed ({record.Name}): {Unwrap(ex).Message}");
                }
                finally
                {
                    record.Gate.Release();
                }
            }
        }

        private async Task<PluginLoadResult> LoadFileLockedAsync(string path)
        {
            path = Path.GetFullPath(path);

            if (!File.Exists(path))
                return new PluginLoadResult(false, "Plugin file not found.");

            byte[] bytes;
            try
            {
                bytes = ReadShared(path);
            }
            catch (Exception ex)
            {
                return new PluginLoadResult(false, $"Could not read plugin file: {ex.Message}");
            }

            if (!PluginLoadContext.IsPluginAssembly(bytes))
                return new PluginLoadResult(false, "Not a plugin assembly.");

            var hash = Convert.ToHexString(SHA256.HashData(bytes));
            var existing = FindByPath(path);

            if (existing != null)
            {
                if (existing.Hash == hash && (existing.Running || !existing.Enabled))
                    return new PluginLoadResult(true, "Unchanged.", existing.Name);

                var previous = existing.LastGoodBytes;
                _logger.Log($"♻️ Replacing plugin: {existing.Name}");
                await RemoveRecordLockedAsync(existing);

                var replaced = await ActivateLockedAsync(path, bytes, hash, keepFailed: false);
                if (replaced.Success || previous == null)
                    return replaced;

                _logger.LogError($"New version of '{existing.Name}' failed ({replaced.Message}). Restoring previous version.");
                try { await File.WriteAllBytesAsync(path, previous); }
                catch (Exception ex) { _logger.LogError($"Could not restore previous plugin file: {ex.Message}"); }

                var restored = await ActivateLockedAsync(path, previous, Convert.ToHexString(SHA256.HashData(previous)), keepFailed: true);
                var suffix = restored.Success ? " The previous version was restored." : " The previous version could not be restored.";
                return new PluginLoadResult(false, replaced.Message + suffix, existing.Name);
            }

            return await ActivateLockedAsync(path, bytes, hash, keepFailed: true);
        }

        private async Task<PluginLoadResult> ReloadRecordLockedAsync(PluginRecord record)
        {
            var path = record.FilePath;
            await RemoveRecordLockedAsync(record);

            if (!File.Exists(path))
                return new PluginLoadResult(false, "Plugin file not found.", record.Name);

            byte[] bytes;
            try { bytes = ReadShared(path); }
            catch (Exception ex) { return new PluginLoadResult(false, ex.Message, record.Name); }

            return await ActivateLockedAsync(path, bytes, Convert.ToHexString(SHA256.HashData(bytes)), keepFailed: true);
        }

        private async Task<PluginLoadResult> ActivateLockedAsync(string path, byte[] bytes, string hash, bool keepFailed)
        {
            var record = Prepare(path, bytes, hash, out var error, out var discarded);

            if (record == null)
            {
                if (discarded != null) await WaitForCollectionAsync(discarded, Path.GetFileName(path));
                return new PluginLoadResult(false, error ?? "Unknown error.");
            }

            if (!record.Enabled)
            {
                await ReleaseContextAsync(record);
                _logger.Log($"📦 Plugin registered (disabled): {record.Name}");
                return new PluginLoadResult(true, "Registered (disabled).", record.Name);
            }

            var started = await StartLockedAsync(record);
            if (!started.Success && !keepFailed)
                _records.TryRemove(record.Name, out _);

            return started;
        }

        [MethodImpl(MethodImplOptions.NoInlining)]
        private PluginRecord? Prepare(string path, byte[] bytes, string hash, out string? error, out WeakReference? discarded)
        {
            error = null;
            discarded = null;
            PluginLoadContext? context = null;

            try
            {
                byte[]? symbols = null;
                var pdbPath = Path.ChangeExtension(path, ".pdb");
                if (File.Exists(pdbPath))
                {
                    try { symbols = ReadShared(pdbPath); }
                    catch { symbols = null; }
                }

                context = new PluginLoadContext(path, $"{Path.GetFileNameWithoutExtension(path)}:{hash[..8]}");
                var assembly = context.LoadMain(bytes, symbols);

                var versionAttribute = assembly.GetCustomAttribute<PluginApiVersionAttribute>();
                if (versionAttribute != null && versionAttribute.Version > PluginApi.Version)
                {
                    error = $"The plugin requires plugin API version {versionAttribute.Version} but this server provides {PluginApi.Version}.";
                    discarded = DiscardContext(context);
                    return null;
                }

                var type = FindPluginType(assembly, out error);
                if (type == null)
                {
                    discarded = DiscardContext(context);
                    return null;
                }

                var plugin = (IPlugin)Activator.CreateInstance(type)!;
                var name = plugin.Name;

                if (string.IsNullOrWhiteSpace(name))
                {
                    error = "The plugin does not define a name.";
                    discarded = DiscardContext(context);
                    return null;
                }

                if (_records.TryGetValue(name, out var duplicate))
                {
                    error = $"A plugin named '{name}' is already loaded from '{duplicate.FilePath}'.";
                    discarded = DiscardContext(context);
                    return null;
                }

                var record = new PluginRecord
                {
                    Name = name,
                    Version = plugin.Version ?? "",
                    Author = plugin.Author ?? "",
                    Description = plugin.Description,
                    FilePath = path,
                    Hash = hash,
                    LastGoodBytes = bytes,
                    Enabled = _pluginStateService.IsEnabled(name),
                    Context = context,
                    Assembly = assembly,
                    Instance = plugin,
                    AssemblyKey = assembly.FullName
                };

                if (record.AssemblyKey != null) _assemblyToPlugin[record.AssemblyKey] = name;
                _records[name] = record;
                context = null;
                return record;
            }
            catch (Exception ex)
            {
                error = DescribeLoadFailure(ex);
                if (context != null) discarded = DiscardContext(context);
                return null;
            }
        }

        private PluginRecord? FindByPath(string path)
            => _records.Values.FirstOrDefault(r => r.FilePath.Equals(path, StringComparison.OrdinalIgnoreCase));

        private Type? FindPluginType(Assembly assembly, out string? error)
        {
            error = null;
            Type?[] types;

            try
            {
                types = assembly.GetTypes();
            }
            catch (ReflectionTypeLoadException ex)
            {
                foreach (var loaderException in ex.LoaderExceptions.Where(e => e != null).Select(e => e!.Message).Distinct())
                    _logger.LogError($"Plugin type load error: {loaderException}");

                types = ex.Types;
            }

            var candidates = types
                .Where(t => t != null && typeof(IPlugin).IsAssignableFrom(t) && !t.IsAbstract && !t.IsInterface && t.GetConstructor(Type.EmptyTypes) != null)
                .Select(t => t!)
                .ToList();

            if (candidates.Count == 0)
            {
                error = "No public IPlugin implementation with a parameterless constructor was found.";
                return null;
            }

            if (candidates.Count > 1)
                _logger.LogWarning($"Assembly {assembly.GetName().Name} contains {candidates.Count} plugins; only '{candidates[0].Name}' is used.");

            return candidates[0];
        }

        private async Task<PluginLoadResult> StartLockedAsync(PluginRecord record)
        {
            var failure = await TryStartAsync(record);

            if (failure == null)
            {
                _logger.Log($"✅ Plugin initialized: {record.Name} v{record.Version}");
                await PublishLoadedAsync(record);
                return new PluginLoadResult(true, "Loaded.", record.Name);
            }

            _logger.LogError($"Plugin '{record.Name}' failed during load: {failure}");
            await StopLockedAsync(record, callUnload: false);
            record.Error = failure;
            return new PluginLoadResult(false, failure, record.Name);
        }

        private async Task<string?> TryStartAsync(PluginRecord record)
        {
            var scope = new PluginScope(ex => _logger.LogError($"Plugin cleanup error ({record.Name}): {ex.Message}"));
            record.Scope = scope;

            try
            {
                var assembly = record.Assembly!;
                if (HasMvcContent(assembly))
                {
                    RegisterApplicationPart(assembly);
                    scope.OnDispose(() => RemoveApplicationPart(assembly));
                }

                record.HostContext = new PluginContext(_logger, _scopeFactory, _serviceProvider, _adminMenuService, scope, record.Name, Path.GetDirectoryName(record.FilePath) ?? PluginPaths.Root, assembly);
                _changeProvider.NotifyChange();

                await InvokeLoadAsync(record);

                record.Running = true;
                record.Error = null;
                return null;
            }
            catch (Exception ex)
            {
                return DescribeLoadFailure(ex);
            }
        }

        private async Task StopLockedAsync(PluginRecord record, bool callUnload)
        {
            await record.Gate.WaitAsync();
            try
            {
                if (callUnload && record.Running)
                {
                    var unloadError = await TryUnloadAsync(record);
                    if (unloadError != null)
                        _logger.LogError($"Plugin unload error ({record.Name}): {unloadError}");
                }

                record.Running = false;
                var scope = record.Scope;
                record.Scope = null;
                record.HostContext = null;
                scope?.Dispose();
                _changeProvider.NotifyChange();
                PurgeCaches(record);
            }
            finally
            {
                record.Gate.Release();
            }

            await ReleaseContextAsync(record);
        }

        [MethodImpl(MethodImplOptions.NoInlining)]
        private void PurgeCaches(PluginRecord record)
        {
            var assembly = record.Assembly;
            if (assembly != null)
                _serviceProvider.GetRequiredService<RazorCacheCleaner>().Purge(assembly);
        }

        [MethodImpl(MethodImplOptions.NoInlining)]
        private static bool HasMvcContent(Assembly assembly)
        {
            if (assembly.GetCustomAttributesData().Any(a => a.AttributeType.Name == "RazorCompiledItemAttribute"))
                return true;

            Type[] types;
            try { types = assembly.GetTypes(); }
            catch (ReflectionTypeLoadException ex) { types = ex.Types.Where(t => t != null).ToArray()!; }

            foreach (var type in types)
            {
                for (var current = type.BaseType; current != null; current = current.BaseType)
                {
                    if (current.Name is "ControllerBase" or "PageModel" or "ViewComponent")
                        return true;
                }
            }

            return false;
        }

        private async Task<string?> TryUnloadAsync(PluginRecord record)
        {
            try
            {
                await InvokeUnloadAsync(record);
                return null;
            }
            catch (Exception ex)
            {
                return Unwrap(ex).Message;
            }
        }

        private async Task RemoveRecordLockedAsync(PluginRecord record)
        {
            await StopLockedAsync(record, callUnload: true);
            _records.TryRemove(record.Name, out _);
        }

        private async Task ReleaseContextAsync(PluginRecord record)
        {
            var weak = DetachContext(record, out var assemblyWeak);
            if (weak != null)
                await WaitForCollectionAsync(weak, record.Name, assemblyWeak);
        }

        [MethodImpl(MethodImplOptions.NoInlining)]
        private WeakReference? DetachContext(PluginRecord record, out WeakReference? assemblyWeak)
        {
            assemblyWeak = null;
            var context = record.Context;
            if (context == null) return null;

            if (record.AssemblyKey != null) _assemblyToPlugin.TryRemove(record.AssemblyKey, out _);

            assemblyWeak = record.Assembly != null ? new WeakReference(record.Assembly) : null;
            record.Instance = null;
            record.Assembly = null;
            record.Context = null;

            var weak = new WeakReference(context, trackResurrection: true);
            context.Unload();
            return weak;
        }

        [MethodImpl(MethodImplOptions.NoInlining)]
        private static WeakReference DiscardContext(PluginLoadContext context)
        {
            var weak = new WeakReference(context, trackResurrection: true);
            context.Unload();
            return weak;
        }

        private async Task<bool> WaitForCollectionAsync(WeakReference weak, string label, WeakReference? assemblyWeak = null)
        {
            for (var attempt = 0; attempt < 15; attempt++)
            {
                GC.Collect();
                GC.WaitForPendingFinalizers();
                GC.Collect();

                if (!weak.IsAlive) return true;
                await Task.Delay(100);
            }

            _logger.LogWarning($"Plugin '{label}' was unloaded but something still references its assemblies; memory will be reclaimed once those references are released.");

            if (assemblyWeak != null && _serviceProvider.GetRequiredService<ServerConfig>().DebugMode)
                _logger.LogWarning($"Reference path for '{label}': {FindLeakPath(assemblyWeak) ?? "not found"}");

            return false;
        }

        [MethodImpl(MethodImplOptions.NoInlining)]
        private string? FindLeakPath(WeakReference assemblyWeak)
        {
            if (assemblyWeak.Target is not Assembly assembly) return null;
            var roots = new List<object> { _serviceProvider };
            var server = _serviceProvider.GetService<Microsoft.AspNetCore.Hosting.Server.IServer>();
            if (server != null) roots.Add(server);
            var dataSource = _serviceProvider.GetService<Microsoft.AspNetCore.Routing.EndpointDataSource>();
            if (dataSource != null) roots.Add(dataSource);
            return ReferencePathFinder.Find(roots, assembly);
        }

        private static async Task InvokeLoadAsync(PluginRecord record)
        {
            await record.Instance!.OnLoadAsync(record.HostContext!);
        }

        private static async Task InvokeUnloadAsync(PluginRecord record)
        {
            var instance = record.Instance;
            if (instance != null) await instance.OnUnloadAsync();
        }

        private static async Task InvokeUpdateAsync(PluginRecord record)
        {
            var instance = record.Instance;
            var context = record.HostContext;
            if (instance != null && context != null) await instance.OnUpdateAsync(context);
        }

        private async Task PublishLoadedAsync(PluginRecord record)
        {
            await _eventBus.PublishAsync(new PluginLoadedEvent(record.Instance!));
        }

        private async Task PublishEnabledAsync(string pluginName)
        {
            if (_records.TryGetValue(pluginName, out var record) && record.Instance != null)
                await _eventBus.PublishAsync(new PluginEnabledEvent(record.Instance));
        }

        private void RegisterApplicationPart(Assembly assembly)
        {
            if (_partManager == null)
                throw new InvalidOperationException("PluginManager.Initialize() not called");

            lock (_partManager)
            {
                if (_partManager.ApplicationParts.OfType<AssemblyPart>().Any(a => a.Assembly == assembly))
                    return;

                _partManager.ApplicationParts.Add(new AssemblyPart(assembly));
                _partManager.ApplicationParts.Add(new CompiledRazorAssemblyPart(assembly));
            }
        }

        private void RemoveApplicationPart(Assembly assembly)
        {
            if (_partManager == null) return;

            lock (_partManager)
            {
                foreach (var part in _partManager.ApplicationParts.OfType<AssemblyPart>().Where(a => a.Assembly == assembly).ToList())
                    _partManager.ApplicationParts.Remove(part);

                foreach (var part in _partManager.ApplicationParts.OfType<CompiledRazorAssemblyPart>().Where(a => a.Assembly == assembly).ToList())
                    _partManager.ApplicationParts.Remove(part);
            }
        }

        private static IEnumerable<string> Discover(string root)
        {
            if (!Directory.Exists(root)) yield break;

            foreach (var file in Directory.GetFiles(root, "*.dll", SearchOption.TopDirectoryOnly))
                yield return file;

            foreach (var directory in Directory.GetDirectories(root))
            {
                var name = Path.GetFileName(directory);
                if (name.StartsWith('.')) continue;

                var main = Path.Combine(directory, name + ".dll");
                if (File.Exists(main)) yield return main;
            }
        }

        private static bool IsDiscoverable(string path)
        {
            var root = PluginPaths.Root.TrimEnd(Path.DirectorySeparatorChar);
            var directory = Path.GetDirectoryName(Path.GetFullPath(path))?.TrimEnd(Path.DirectorySeparatorChar);
            if (directory == null) return false;
            if (directory.Equals(root, StringComparison.OrdinalIgnoreCase)) return true;

            var parent = Path.GetDirectoryName(directory)?.TrimEnd(Path.DirectorySeparatorChar);
            var directoryName = Path.GetFileName(directory);
            return parent != null
                && parent.Equals(root, StringComparison.OrdinalIgnoreCase)
                && !directoryName.StartsWith('.')
                && Path.GetFileNameWithoutExtension(path).Equals(directoryName, StringComparison.OrdinalIgnoreCase);
        }

        private static bool IsSilentFailure(PluginLoadResult result) => result.Message == "Not a plugin assembly.";

        private static byte[] ReadShared(string path)
        {
            using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
            using var buffer = new MemoryStream();
            stream.CopyTo(buffer);
            return buffer.ToArray();
        }

        private static Exception Unwrap(Exception ex)
            => ex is TargetInvocationException { InnerException: not null } tie ? tie.InnerException! : ex;

        private string DescribeLoadFailure(Exception ex)
        {
            ex = Unwrap(ex);

            if (ex is ReflectionTypeLoadException rtle)
            {
                var details = rtle.LoaderExceptions.Where(e => e != null).Select(e => e!.Message).Distinct().Take(5);
                return $"{ex.Message} {string.Join(" | ", details)}";
            }

            if (ex is TypeLoadException or MissingMethodException or MissingMemberException or FileNotFoundException or FileLoadException or BadImageFormatException)
                return $"{ex.Message} (the plugin was probably built against a different SharpPress.Abstractions version or is missing a dependency)";

            return ex.Message;
        }

        private static PluginInfo ToInfo(PluginRecord record) => new()
        {
            Name = record.Name,
            Version = record.Version,
            Author = record.Author,
            Description = record.Description,
            FilePath = record.FilePath,
            Enabled = record.Enabled,
            Running = record.Running,
            Error = record.Error
        };

        private void StartWatcher(string root)
        {
            if (_watcher != null) return;

            try
            {
                var watcher = new FileSystemWatcher(root)
                {
                    IncludeSubdirectories = true,
                    NotifyFilter = NotifyFilters.FileName | NotifyFilters.LastWrite | NotifyFilters.Size | NotifyFilters.CreationTime,
                    Filter = "*.dll"
                };

                watcher.Created += (_, e) => ScheduleFileEvent(e.FullPath);
                watcher.Changed += (_, e) => ScheduleFileEvent(e.FullPath);
                watcher.Deleted += (_, e) => ScheduleFileEvent(e.FullPath);
                watcher.Renamed += (_, e) =>
                {
                    ScheduleFileEvent(e.OldFullPath);
                    ScheduleFileEvent(e.FullPath);
                };
                watcher.EnableRaisingEvents = true;
                _watcher = watcher;
            }
            catch (Exception ex)
            {
                _logger.LogWarning($"Plugin file watcher could not be started: {ex.Message}");
            }
        }

        private void StopWatcher()
        {
            var watcher = _watcher;
            _watcher = null;
            if (watcher == null) return;

            watcher.EnableRaisingEvents = false;
            watcher.Dispose();

            foreach (var pending in _pendingFileEvents.Values)
                pending.Cancel();

            _pendingFileEvents.Clear();
        }

        private void ScheduleFileEvent(string path)
        {
            if (!path.EndsWith(".dll", StringComparison.OrdinalIgnoreCase)) return;
            if (!IsDiscoverable(path)) return;

            var cts = new CancellationTokenSource();
            _pendingFileEvents.AddOrUpdate(path, cts, (_, previous) =>
            {
                previous.Cancel();
                return cts;
            });

            _ = Task.Run(async () =>
            {
                try
                {
                    await Task.Delay(800, cts.Token);
                }
                catch (OperationCanceledException)
                {
                    return;
                }

                _pendingFileEvents.TryRemove(new KeyValuePair<string, CancellationTokenSource>(path, cts));
                await HandleFileEventAsync(path);
            });
        }

        private async Task HandleFileEventAsync(string path)
        {
            try
            {
                if (_watcher == null) return;

                await _lifecycle.WaitAsync();
                try
                {
                    var full = Path.GetFullPath(path);

                    if (File.Exists(full))
                    {
                        var result = await LoadFileLockedAsync(full);
                        if (result.Success && result.Message != "Unchanged.")
                            _logger.Log($"🔄 Plugin file change applied: {Path.GetFileName(full)}");
                        else if (!result.Success && !IsSilentFailure(result))
                            _logger.LogError($"Plugin file change for '{Path.GetFileName(full)}' failed: {result.Message}");
                    }
                    else
                    {
                        var record = FindByPath(full);
                        if (record != null)
                        {
                            await RemoveRecordLockedAsync(record);
                            await _eventBus.PublishAsync(new PluginUnloadedEvent(record.Name));
                            _logger.Log($"🧹 Plugin file removed, unloaded: {record.Name}");
                        }
                    }
                }
                finally
                {
                    _lifecycle.Release();
                }
            }
            catch (Exception ex)
            {
                _logger.LogError($"Plugin file watcher error: {ex.Message}");
            }
        }
    }

    public interface IAdminMenuService
    {
        void Register(AdminMenuItem item);
        void UnRegister(AdminMenuItem item);
        IReadOnlyList<AdminMenuItem> GetItems();
    }

    public class AdminMenuService : IAdminMenuService
    {
        private readonly object _lock = new();
        private readonly List<AdminMenuItem> _items = new();

        public void Register(AdminMenuItem item)
        {
            lock (_lock)
            {
                if (_items.Any(x => x.Url == item.Url))
                    return;

                _items.Add(item);
            }
        }

        public void UnRegister(AdminMenuItem item)
        {
            lock (_lock)
                _items.Remove(item);
        }

        public IReadOnlyList<AdminMenuItem> GetItems()
        {
            lock (_lock)
                return _items.ToList().AsReadOnly();
        }
    }

    public class AdminMenuItem
    {
        public string ResponsiblePlugin { get; set; } = "";
        public string Name { get; set; } = "";
        public string IconSvg { get; set; } = "";
        public string Url { get; set; } = "";
        public int Order { get; set; }
    }

    public class PluginActionDescriptorChangeProvider : IActionDescriptorChangeProvider
    {
        private CancellationTokenSource _cts = new();

        public IChangeToken GetChangeToken()
            => new CancellationChangeToken(_cts.Token);

        public void NotifyChange()
        {
            var newCts = new CancellationTokenSource();

            var old = Interlocked.Exchange(ref _cts, newCts);

            if (old != null)
            {
                try
                {
                    old.Cancel();
                }
                catch (ObjectDisposedException) { }

                old.Dispose();
            }
        }
    }
}
