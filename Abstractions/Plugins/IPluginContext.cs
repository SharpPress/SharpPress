using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using SharpPress.Services;

namespace SharpPress.Plugins
{
    public interface IPluginContext
    {
        IPluginLogger Logger { get; }
        IServiceProvider ServiceProvider { get; }
        IServiceScopeFactory ScopeFactory { get; }
        IPluginServices Services { get; }
        IPluginEvents Events { get; }
        IPluginFilters Filters { get; }
        IPluginSettings Settings { get; }
        IPluginScheduler Scheduler { get; }
        IPluginDatabase Database { get; }
        IPluginMeta PostMeta { get; }
        CancellationToken PluginStopping { get; }
        string DataDirectory { get; }
        string PluginDirectory { get; }
        void RegisterAdminMenuItem(string name, string iconSvg, string url, int order = 0);
        void RegisterDashboardWidget(string title, Func<Task<string>> render, int order = 0);
        void MapGet(string pattern, Delegate handler);
        void MapPost(string pattern, Delegate handler);
        void Map(string pattern, Delegate handler);
        void MapRoute(string method, string pattern, Delegate handler, PluginRouteOptions? options = null);
        void UseMiddleware(Func<HttpContext, Func<Task>, Task> middleware, int order = 0);
        void MapStaticFiles(string requestPath, string directory);
    }
}
