using SharpPress.Models;

namespace SharpPress.Middlewares
{
    public class SetupRedirectMiddleware
    {
        private static readonly string[] AllowedPaths = { "/setup", "/health" };

        private readonly RequestDelegate _next;
        private readonly ServerConfig _config;

        public SetupRedirectMiddleware(RequestDelegate next, ServerConfig config)
        {
            _next = next;
            _config = config;
        }

        public async Task InvokeAsync(HttpContext context)
        {
            var path = context.Request.Path.Value?.ToLowerInvariant() ?? "";

            if (_config.SetupStep < 4 && !AllowedPaths.Contains(path))
            {
                context.Response.Redirect("/setup");
                return;
            }

            await _next(context);
        }
    }
}
