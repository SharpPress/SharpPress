using Microsoft.AspNetCore.Http;
using SharpPress.Services;
using System.Threading.Tasks;

namespace SharpPress.Middleware
{
    public class PluginRouteMiddleware
    {
        private readonly RequestDelegate _next;

        public PluginRouteMiddleware(RequestDelegate next)
        {
            _next = next;
        }

        public async Task InvokeAsync(HttpContext context, PluginManager pluginManager)
        {
            var path = context.Request.Path.Value;
            var handler = pluginManager.GetRouteHandler(path);

            if (handler != null)
            {
                await handler(context);
                return;
            }

            await _next(context);
        }
    }

    public static class PluginRouteMiddlewareExtensions
    {
        public static IApplicationBuilder UsePluginRoutes(this IApplicationBuilder builder)
        {
            return builder.UseMiddleware<PluginRouteMiddleware>();
        }
    }
}