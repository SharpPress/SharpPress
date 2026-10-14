using Microsoft.AspNetCore.Http;
using SharpPress.Events;
using SharpPress.Models;
using SharpPress.Plugins;
using SharpPress.Services;

[assembly: PluginApiVersion(1)]

namespace SamplePlugin
{
    public class SampleDemoPlugin : IPlugin
    {
        private const string MenuIcon = "<svg viewBox=\"0 0 24 24\"><polyline points=\"20 6 9 17 4 12\" /></svg>";

        public string Name => "SamplePlugin";
        public string Version => "2.0.0";
        public string Author => "SharpPress";
        public string? Description => "Upload this plugin and open Sample feature check in the admin sidebar to see every plugin feature work.";

        public Task OnLoadAsync(IPluginContext context)
        {
            SampleState.Context = context;
            SampleState.LoadedAt = DateTime.UtcNow;
            context.Logger.Log($"SamplePlugin {Version} loaded");

            RegisterRoutes(context);
            RegisterEvents(context);
            RegisterContentFeatures(context);
            RegisterServices(context);
            RegisterAdminFeatures(context);

            return Task.CompletedTask;
        }

        public Task OnUpdateAsync(IPluginContext context) => Task.CompletedTask;

        public Task OnUnloadAsync()
        {
            SampleState.Context = null;
            return Task.CompletedTask;
        }

        private void RegisterRoutes(IPluginContext context)
        {
            context.MapGet("/sample/hello/{name}", (string name) =>
            {
                var settings = context.Settings.Get<SampleSettings>();
                var text = $"{settings.Greeting} {name}";
                return Results.Text(settings.Shout ? text.ToUpperInvariant() : text);
            });

            context.MapPost("/sample/echo", async (HttpRequest request) =>
            {
                using var reader = new StreamReader(request.Body);
                return Results.Text(await reader.ReadToEndAsync());
            });

            context.MapRoute("GET", "/sample/admin-only", () => Results.Text("Only admins see this."), new PluginRouteOptions { RequiredRole = UserRole.Admin });

            var publicFolder = Path.Combine(context.DataDirectory, "public");
            Directory.CreateDirectory(publicFolder);
            File.WriteAllText(Path.Combine(publicFolder, "hello.txt"), "Hello from a static file served by SamplePlugin.");
            context.MapStaticFiles("/sample/files", publicFolder);

            context.UseMiddleware(async (http, next) =>
            {
                Interlocked.Increment(ref SampleState.MiddlewareHits);
                http.Response.Headers["X-Sample-Plugin"] = Version;
                await next();
            });
        }

        private void RegisterEvents(IPluginContext context)
        {
            context.Events.On<SampleTestEvent>(_ => Interlocked.Increment(ref SampleState.TestEvents));

            context.Events.On<PostCreatingEvent>(e =>
            {
                if (e.Post.Title.Contains("forbidden", StringComparison.OrdinalIgnoreCase))
                    e.Cancel("SamplePlugin does not allow titles containing \"forbidden\".");
            });

            context.Events.On<PostCreatedEvent>(_ => Interlocked.Increment(ref SampleState.PostEvents));
            context.Events.On<PostUpdatedEvent>(_ => Interlocked.Increment(ref SampleState.PostEvents));

            context.Events.On<PostPublishedEvent>(e =>
            {
                SampleState.LastPublishedTitle = e.Post.Title;
                context.Logger.Log($"SamplePlugin saw a post being published: {e.Post.Title}");
            });

            context.Events.On<SettingsUpdatedEvent>(_ => Interlocked.Increment(ref SampleState.SettingsEvents));
            context.Events.On<UserLoggedInEvent>(_ => Interlocked.Increment(ref SampleState.LoginEvents));
        }

        private void RegisterContentFeatures(IPluginContext context)
        {
            context.Filters.Add<string>(FilterKeys.PostTitle, title => title + " *", 10);
            context.Filters.Add<string>(FilterKeys.PageFooter, html => html + "<!-- rendered with SamplePlugin -->", 10);
            context.Filters.AddShortcode("year", _ => DateTime.UtcNow.Year.ToString());
            context.Filters.AddShortcode("shout", shortcode => shortcode.Content.ToUpperInvariant());

            context.PostMeta.RegisterField("sample.subtitle", "Subtitle", MetaFieldType.Text, "Stored by SamplePlugin next to the post.");
        }

        private void RegisterServices(IPluginContext context)
        {
            context.Services.Decorate<IEmailService>(inner => new CountingEmailService(inner));
            context.Services.Decorate<ICacheService>(inner => new CountingCacheService(inner));

            context.Scheduler.Every("heartbeat", TimeSpan.FromSeconds(10), _ =>
            {
                Interlocked.Increment(ref SampleState.HeartbeatCount);
                SampleState.LastHeartbeat = DateTime.UtcNow;
                return Task.CompletedTask;
            }, runImmediately: true);
        }

        private void RegisterAdminFeatures(IPluginContext context)
        {
            context.Settings.Expose<SampleSettings>("Sample");

            context.RegisterDashboardWidget("Sample plugin", () => Task.FromResult(
                $"<p>SamplePlugin {Version} is running. Heartbeats: {SampleState.HeartbeatCount}, last published post: {System.Net.WebUtility.HtmlEncode(SampleState.LastPublishedTitle)}.</p>" +
                "<p><a href=\"/admin/sample\">Open the feature check</a></p>"), 10);

            context.RegisterAdminMenuItem("Sample feature check", MenuIcon, "/admin/sample", 50);
        }
    }
}
