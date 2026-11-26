using SharpPress.Helpers;
using SharpPress.Models;
using SharpPress.Services;
using SharpPress.Services.Database;

namespace SharpPress.Middlewares
{
    public class UserControlMiddleware
    {
        private readonly string _bannedPage = "";
        private readonly string _maintenancePage = "";

        private readonly IAuthenticationService _authService;
        private readonly RequestDelegate _next;
        private readonly Logger _logger;
        private bool _isMaintenanceMode = false;

        public UserControlMiddleware(RequestDelegate next, Logger logger, IAuthenticationService authenticationService, ServerConfig config)
        {
            _next = next;
            _logger = logger;
            _authService = authenticationService;

            var domain = config.PanelDomain.Replace("https://", "").Replace("http://", "").TrimEnd('/');
            var supportEmail = config.SiteSettings?.General?.AdminEmail ?? $"support@{domain}";
            _bannedPage = StatusPages.Banned(supportEmail);
            _maintenancePage = StatusPages.Maintenance(supportEmail);
        }

        public async Task InvokeAsync(HttpContext context, FeatherDatabase database, ServerConfig config, IUserService userService)
        {
            _isMaintenanceMode = config.SiteSettings?.Advanced?.MaintenanceMode ?? false;
            var user = await userService.GetUserAsync(context.User);

            if (_isMaintenanceMode && user != null && user.HasRole(UserRole.Moderator))
                goto SkipMaintenanceCheck;

            if (_isMaintenanceMode)
            {
                if (!IsAllowedPath(context.Request.Path.Value) && !IsStaticAsset(context.Request.Path.Value))
                {
                    context.Response.StatusCode = StatusCodes.Status503ServiceUnavailable;
                    context.Response.ContentType = "text/html; charset=utf-8";
                    await context.Response.WriteAsync(_maintenancePage);
                    return;
                }
            }

            SkipMaintenanceCheck:

            if (context.User.Identity?.IsAuthenticated ?? false)
            {
                if (user == null)
                {
                    ClearAuthCookie(context);
                    context.Response.Redirect(context.Request.Path + context.Request.QueryString);
                    return;
                }

                if (!user.IsAtLeastUser())
                {
                    context.Response.StatusCode = StatusCodes.Status403Forbidden;
                    context.Response.ContentType = "text/html; charset=utf-8";
                    await context.Response.WriteAsync(_bannedPage);
                    return;
                }

                _ = Task.Run(async () =>
                {
                    if (!user.IsActive)
                        user.IsActive = true;

                    var now = DateTime.UtcNow;
                    if ((now - user.LastActive).TotalMinutes > 5)
                    {
                        user.LastActive = now;
                        await database.SaveData(user);
                    }
                });

                if (context.Request.Cookies.TryGetValue("X-Access-Token", out var token))
                {
                    if (string.IsNullOrEmpty(token) || !_authService.ValidateJwtToken(token))
                    {
                        ClearAuthCookie(context);
                        context.Response.Redirect(context.Request.Path + context.Request.QueryString);
                        return;
                    }

                    var renewedToken = _authService.TryRenewTokenIfExpiringSoon(token, user);
                    if (renewedToken != null)
                    {
                        context.Response.Cookies.Append("X-Access-Token", renewedToken, _authService.CreateAuthCookieOptions());
                    }
                }
            }

            await _next(context);
        }

        private bool IsAllowedPath(string? path)
        {
            if (string.IsNullOrEmpty(path))
                return false;

            path = path.ToLowerInvariant();

            var allowedPaths = new[]
            {
                "/",
                "/login",
                "/register",
                "/forgot-password",
                "/reset-password",
                "/privacy",
                "/terms",
                "/health",
                "/api/auth/login",
                "/api/auth/register",
                "/robots.txt",
                "/sitemap.xml"
            };

            return allowedPaths.Any(p => path == p || path.StartsWith(p + "/"));
        }

        private bool IsStaticAsset(string? path)
        {
            if (string.IsNullOrEmpty(path))
                return false;

            path = path.ToLowerInvariant();

            var staticPaths = new[]
            {
                "/css/",
                "/js/",
                "/images/",
                "/assets/",
                "/fonts/",
                "/favicon.ico",
                ".css",
                ".js",
                ".png",
                ".jpg",
                ".gif",
                ".svg",
                ".woff",
                ".woff2"
            };

            return staticPaths.Any(p => path.Contains(p));
        }

        private void ClearAuthCookie(HttpContext context)
        {
            context.Response.Cookies.Delete("X-Access-Token");
        }
    }
}
