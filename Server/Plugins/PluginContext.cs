using Microsoft.AspNetCore.StaticFiles;
using SharpPress.Middleware;
using SharpPress.Plugins;

namespace SharpPress.Services
{
    public class PluginContext : IPluginContext
    {
        private static readonly FileExtensionContentTypeProvider ContentTypes = new();
        private const string SettingsIcon = "<svg viewBox=\"0 0 24 24\"><line x1=\"4\" y1=\"21\" x2=\"4\" y2=\"14\" /><line x1=\"4\" y1=\"10\" x2=\"4\" y2=\"3\" /><line x1=\"12\" y1=\"21\" x2=\"12\" y2=\"12\" /><line x1=\"12\" y1=\"8\" x2=\"12\" y2=\"3\" /><line x1=\"20\" y1=\"21\" x2=\"20\" y2=\"16\" /><line x1=\"20\" y1=\"12\" x2=\"20\" y2=\"3\" /></svg>";

        public IPluginLogger Logger { get; }
        public IServiceScopeFactory ScopeFactory { get; }
        public IServiceProvider ServiceProvider { get; }
        public IPluginServices Services { get; }
        public IPluginEvents Events { get; }
        public IPluginFilters Filters { get; }
        public IPluginSettings Settings { get; }
        public IPluginScheduler Scheduler { get; }
        public IPluginDatabase Database { get; }
        public IPluginMeta PostMeta { get; }
        public CancellationToken PluginStopping => _scope.Token;
        public string DataDirectory => PluginPaths.DataDirectoryFor(_pluginName);
        public string PluginDirectory { get; }

        private readonly IAdminMenuService _adminMenuService;
        private readonly PluginEndpointDataSource _endpoints;
        private readonly PluginMiddlewareRegistry _middleware;
        private readonly IDashboardService _dashboard;
        private readonly PluginScope _scope;
        private readonly string _pluginName;

        public PluginContext(Logger logger, IServiceScopeFactory scopeFactory, IServiceProvider serviceProvider, IAdminMenuService adminMenuService, PluginScope scope, string pluginName, string pluginDirectory, System.Reflection.Assembly pluginAssembly)
        {
            Logger = logger;
            ScopeFactory = scopeFactory;
            ServiceProvider = serviceProvider;
            _adminMenuService = adminMenuService;
            _endpoints = serviceProvider.GetRequiredService<PluginEndpointDataSource>();
            _middleware = serviceProvider.GetRequiredService<PluginMiddlewareRegistry>();
            _dashboard = serviceProvider.GetRequiredService<IDashboardService>();
            _scope = scope;
            _pluginName = pluginName;
            PluginDirectory = pluginDirectory;

            var registry = serviceProvider.GetRequiredService<ServiceOverrideRegistry>();
            Services = new PluginServices(registry, pluginName);
            Events = new PluginEvents(serviceProvider.GetRequiredService<IEventBus>(), scope);
            Filters = new PluginFilters(serviceProvider.GetRequiredService<IFilterPipeline>(), scope);
            Database = new PluginDatabase(serviceProvider.GetRequiredService<SharpPress.Services.Database.FeatherDatabase>(), pluginAssembly, pluginName);
            PostMeta = new PluginMeta(serviceProvider.GetRequiredService<IPostMetaService>(), scope, pluginName);
            Scheduler = new PluginScheduler(serviceProvider.GetRequiredService<IScheduler>(), scope, pluginName);
            Settings = new PluginSettings(
                serviceProvider.GetRequiredService<PluginSettingsStore>(),
                scope,
                pluginName,
                (name, url) => RegisterAdminMenuItem(name, SettingsIcon, url, 90));
            _scope.OnDispose(() => registry.RemoveOwner(pluginName));
        }

        public void RegisterAdminMenuItem(string name, string iconSvg, string url, int order = 0)
        {
            var item = new AdminMenuItem { ResponsiblePlugin = _pluginName, Name = name, IconSvg = iconSvg, Url = url, Order = order };
            _adminMenuService.Register(item);
            _scope.OnDispose(() => _adminMenuService.UnRegister(item));
        }

        public void RegisterDashboardWidget(string title, Func<Task<string>> render, int order = 0)
        {
            var registration = _dashboard.Register(new DashboardWidget { Owner = _pluginName, Title = title, Order = order, Render = render });
            _scope.OnDispose(registration.Dispose);
        }

        public void MapGet(string pattern, Delegate handler) => MapRoute("GET", pattern, handler);
        public void MapPost(string pattern, Delegate handler) => MapRoute("POST", pattern, handler);
        public void Map(string pattern, Delegate handler) => MapRoute("*", pattern, handler);

        public void MapRoute(string method, string pattern, Delegate handler, PluginRouteOptions? options = null)
        {
            var endpoint = _endpoints.Add(_pluginName, method.ToUpperInvariant(), pattern, handler, options);
            _scope.OnDispose(() => _endpoints.Remove(endpoint));
        }

        public void UseMiddleware(Func<HttpContext, Func<Task>, Task> middleware, int order = 0)
        {
            var registration = _middleware.Add(_pluginName, order, middleware);
            _scope.OnDispose(registration.Dispose);
        }

        public void MapStaticFiles(string requestPath, string directory)
        {
            var root = Path.GetFullPath(Path.IsPathRooted(directory) ? directory : Path.Combine(PluginDirectory, directory));
            var allowed = PluginPaths.IsInside(PluginDirectory, Path.Combine(root, "x")) || PluginPaths.IsInside(DataDirectory, Path.Combine(root, "x"));

            if (!allowed)
                throw new InvalidOperationException("Static file directories must be inside the plugin or plugin data directory.");

            var prefix = "/" + requestPath.Trim('/');

            MapRoute("GET", prefix + "/{**file}", async (HttpContext http, string file) =>
            {
                var full = Path.GetFullPath(Path.Combine(root, file ?? ""));

                if (!PluginPaths.IsInside(root, full) || !File.Exists(full))
                {
                    http.Response.StatusCode = StatusCodes.Status404NotFound;
                    return;
                }

                if (!ContentTypes.TryGetContentType(full, out var contentType))
                    contentType = "application/octet-stream";

                http.Response.ContentType = contentType;
                await http.Response.SendFileAsync(full);
            });
        }
    }
}
