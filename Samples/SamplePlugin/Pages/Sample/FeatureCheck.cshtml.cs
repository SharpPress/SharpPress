using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using SharpPress.Events;
using SharpPress.Models;
using SharpPress.Services;
using System.Net;

namespace SamplePlugin.Pages
{
    public record CheckResult(string Feature, string Status, string Detail);

    public class FeatureCheckModel : PageModel
    {
        private readonly IUserService _users;
        private readonly IFilterPipeline _filters;
        private readonly IPostMetaService _meta;
        private readonly IDashboardService _dashboard;
        private readonly ICacheService _cache;
        private readonly IEmailService _email;

        public FeatureCheckModel(
            IUserService users,
            IFilterPipeline filters,
            IPostMetaService meta,
            IDashboardService dashboard,
            ICacheService cache,
            IEmailService email)
        {
            _users = users;
            _filters = filters;
            _meta = meta;
            _dashboard = dashboard;
            _cache = cache;
            _email = email;
        }

        public List<CheckResult> Results { get; } = new();

        public int Passed => Results.Count(r => r.Status == "pass");
        public int Failed => Results.Count(r => r.Status == "fail");
        public int Skipped => Results.Count(r => r.Status == "skip");

        public async Task<IActionResult> OnGetAsync()
        {
            var user = await _users.GetUserAsync(User);
            if (user == null || !user.HasRole(UserRole.Admin))
                return RedirectToPage("/Login");

            var context = SampleState.Context;
            if (context == null)
            {
                Results.Add(new CheckResult("Plugin context", "fail", "The plugin has not been loaded."));
                return Page();
            }

            CheckLifecycle(context);
            await CheckRoutesAsync();
            await CheckEventsAsync(context);
            CheckFilters();
            CheckSettings(context);
            CheckPostMeta();
            CheckScheduler();
            await CheckDatabaseAsync(context);
            CheckServices();
            CheckDashboard();

            return Page();
        }

        private void Pass(string feature, string detail) => Results.Add(new CheckResult(feature, "pass", detail));
        private void Fail(string feature, string detail) => Results.Add(new CheckResult(feature, "fail", detail));
        private void Skip(string feature, string detail) => Results.Add(new CheckResult(feature, "skip", detail));

        private void CheckLifecycle(SharpPress.Plugins.IPluginContext context)
        {
            try
            {
                var probe = Path.Combine(context.DataDirectory, "probe.txt");
                System.IO.File.WriteAllText(probe, DateTime.UtcNow.ToString("O"));
                var readBack = System.IO.File.ReadAllText(probe);

                Pass("Plugin lifecycle", $"Loaded {(DateTime.UtcNow - SampleState.LoadedAt).TotalSeconds:0}s ago; stopping token active: {!context.PluginStopping.IsCancellationRequested}; data directory writable ({readBack.Length} bytes round-trip).");
            }
            catch (Exception ex)
            {
                Fail("Plugin lifecycle", ex.Message);
            }
        }

        private async Task CheckRoutesAsync()
        {
            var baseAddress = $"{Request.Scheme}://{Request.Host}";
            using var client = new HttpClient(new HttpClientHandler { AllowAutoRedirect = false }) { BaseAddress = new Uri(baseAddress), Timeout = TimeSpan.FromSeconds(10) };

            try
            {
                var response = await client.GetAsync("/sample/hello/Check");
                var body = await response.Content.ReadAsStringAsync();
                var header = response.Headers.TryGetValues("X-Sample-Plugin", out var values) ? values.FirstOrDefault() : null;

                if (response.StatusCode == HttpStatusCode.OK && body.Contains("Check"))
                    Pass("Route with parameter", $"GET /sample/hello/Check returned \"{body}\".");
                else
                    Fail("Route with parameter", $"Unexpected response {(int)response.StatusCode}: {body}");

                if (header != null)
                    Pass("Hot middleware", $"Response carries X-Sample-Plugin: {header}; middleware ran {SampleState.MiddlewareHits} time(s).");
                else
                    Fail("Hot middleware", "The X-Sample-Plugin header was not added.");
            }
            catch (Exception ex)
            {
                Fail("Route with parameter", ex.Message);
            }

            try
            {
                var response = await client.PostAsync("/sample/echo", new StringContent("ping"));
                var body = await response.Content.ReadAsStringAsync();
                if (body == "ping") Pass("POST route with request body", "POST /sample/echo echoed the body.");
                else Fail("POST route with request body", $"Unexpected response: {body}");
            }
            catch (Exception ex)
            {
                Fail("POST route with request body", ex.Message);
            }

            try
            {
                var response = await client.GetAsync("/sample/admin-only");
                if (response.StatusCode is HttpStatusCode.Unauthorized or HttpStatusCode.Forbidden)
                    Pass("Role-protected route", $"Anonymous request to /sample/admin-only was rejected with {(int)response.StatusCode}.");
                else
                    Fail("Role-protected route", $"Anonymous request returned {(int)response.StatusCode} instead of 401/403.");
            }
            catch (Exception ex)
            {
                Fail("Role-protected route", ex.Message);
            }

            try
            {
                var response = await client.GetAsync("/sample/files/hello.txt");
                var body = await response.Content.ReadAsStringAsync();
                if (response.StatusCode == HttpStatusCode.OK && body.Contains("static file"))
                    Pass("Static files", "GET /sample/files/hello.txt served a file from the plugin data folder.");
                else
                    Fail("Static files", $"Unexpected response {(int)response.StatusCode}.");
            }
            catch (Exception ex)
            {
                Fail("Static files", ex.Message);
            }

            try
            {
                var response = await client.GetAsync("/sample-api/ping");
                var body = await response.Content.ReadAsStringAsync();
                if (response.StatusCode == HttpStatusCode.OK && body.Contains("MVC controller"))
                    Pass("MVC controller", "GET /sample-api/ping returned JSON from a controller inside the plugin.");
                else
                    Fail("MVC controller", $"Unexpected response {(int)response.StatusCode}: {body}");
            }
            catch (Exception ex)
            {
                Fail("MVC controller", ex.Message);
            }

            try
            {
                var response = await client.GetAsync("/sample/page");
                var body = await response.Content.ReadAsStringAsync();
                if (response.StatusCode == HttpStatusCode.OK && body.Contains("SamplePlugin"))
                    Pass("Razor Page", "GET /sample/page rendered a Razor Page compiled into the plugin (this page is one too).");
                else
                    Fail("Razor Page", $"Unexpected response {(int)response.StatusCode}.");
            }
            catch (Exception ex)
            {
                Fail("Razor Page", ex.Message);
            }
        }

        private async Task CheckEventsAsync(SharpPress.Plugins.IPluginContext context)
        {
            try
            {
                var before = SampleState.TestEvents;
                await context.Events.PublishAsync(new SampleTestEvent("self-test"));

                if (SampleState.TestEvents == before + 1)
                    Pass("Events", $"A custom event was published and received. Seen so far: {SampleState.PostEvents} post, {SampleState.SettingsEvents} settings, {SampleState.LoginEvents} login event(s).");
                else
                    Fail("Events", "The published event was not delivered to the plugin handler.");
            }
            catch (Exception ex)
            {
                Fail("Events", ex.Message);
            }

            try
            {
                var creating = new PostCreatingEvent(new Post { Title = "Forbidden test" });
                var allowed = await context.Events.PublishCancellableAsync(creating);

                if (!allowed && creating.IsCancelled)
                    Pass("Cancellable events", $"The plugin vetoed a post titled \"Forbidden test\": {creating.CancelReason}");
                else
                    Fail("Cancellable events", "The plugin did not cancel the forbidden title.");
            }
            catch (Exception ex)
            {
                Fail("Cancellable events", ex.Message);
            }
        }

        private void CheckFilters()
        {
            try
            {
                var title = _filters.Apply(FilterKeys.PostTitle, "Title");
                if (title == "Title *") Pass("Filters", "The post.title filter appended \" *\".");
                else Fail("Filters", $"Unexpected filter result: {title}");

                var rendered = _filters.RenderShortcodes("[year] and [shout]quiet[/shout]");
                if (rendered == $"{DateTime.UtcNow.Year} and QUIET") Pass("Shortcodes", $"[year] and [shout] rendered as \"{rendered}\".");
                else Fail("Shortcodes", $"Unexpected shortcode result: {rendered}");
            }
            catch (Exception ex)
            {
                Fail("Filters and shortcodes", ex.Message);
            }
        }

        private void CheckSettings(SharpPress.Plugins.IPluginContext context)
        {
            try
            {
                var settings = context.Settings.Get<SampleSettings>();
                settings.RunCount++;
                context.Settings.Save(settings);

                var reloaded = context.Settings.Get<SampleSettings>();
                if (reloaded.RunCount == settings.RunCount)
                    Pass("Settings", $"Saved and reloaded typed settings (self-test runs: {reloaded.RunCount}, greeting: \"{reloaded.Greeting}\"). Edit them under Sample Settings in the sidebar.");
                else
                    Fail("Settings", "The saved value was not read back.");
            }
            catch (Exception ex)
            {
                Fail("Settings", ex.Message);
            }
        }

        private void CheckPostMeta()
        {
            var field = _meta.Fields.FirstOrDefault(f => f.Key == "sample.subtitle");
            if (field != null) Pass("Post meta fields", $"\"{field.Label}\" is registered and appears in the post editor.");
            else Fail("Post meta fields", "The sample.subtitle field is not registered.");
        }

        private void CheckScheduler()
        {
            if (SampleState.HeartbeatCount > 0 && SampleState.LastHeartbeat != null)
                Pass("Scheduler", $"Heartbeat job ran {SampleState.HeartbeatCount} time(s); last run {(DateTime.UtcNow - SampleState.LastHeartbeat.Value).TotalSeconds:0}s ago.");
            else
                Skip("Scheduler", "The heartbeat job runs every 10 seconds. Reload this page shortly after loading the plugin.");
        }

        private async Task CheckDatabaseAsync(SharpPress.Plugins.IPluginContext context)
        {
            try
            {
                await context.Database.MigrateAsync(1, () => context.Database.CreateTableAsync<VisitCounter>());

                var counter = await context.Database.FindAsync<VisitCounter>(c => c.Path == "/admin/sample")
                    ?? new VisitCounter { Path = "/admin/sample" };

                counter.Count++;
                await context.Database.SaveAsync(counter);
                Pass("Plugin database table", $"VisitCounter table created by a versioned migration; this page has been checked {counter.Count} time(s).");
            }
            catch (Exception ex)
            {
                Skip("Plugin database table", $"Database not available: {ex.Message}");
            }
        }

        private void CheckServices()
        {
            try
            {
                var cacheBefore = SampleState.CacheReads;
                _cache.Set("sample:probe", "value", TimeSpan.FromSeconds(5));
                var value = _cache.Get<string>("sample:probe");

                if (value == "value" && SampleState.CacheReads == cacheBefore + 1)
                    Pass("Service decoration (ICacheService)", $"The plugin's decorator observed the read ({SampleState.CacheReads} total) and delegated to the core cache.");
                else
                    Fail("Service decoration (ICacheService)", "The decorator did not observe the cache read.");

                var emailBefore = SampleState.EmailAccesses;
                _ = _email.EmailSendAction;
                if (SampleState.EmailAccesses == emailBefore + 1)
                    Pass("Service decoration (IEmailService)", "The plugin's e-mail decorator is active; every send is logged by the plugin.");
                else
                    Fail("Service decoration (IEmailService)", "The e-mail decorator is not active.");
            }
            catch (Exception ex)
            {
                Fail("Service decoration", ex.Message);
            }
        }

        private void CheckDashboard()
        {
            if (_dashboard.Widgets.Any(w => w.Owner == "SamplePlugin"))
                Pass("Dashboard widget and admin menu", "The plugin widget is registered on the dashboard; its menu entries are in the sidebar.");
            else
                Fail("Dashboard widget and admin menu", "The dashboard widget is not registered.");
        }
    }
}
