using Microsoft.AspNetCore.Http.Metadata;
using Microsoft.AspNetCore.Routing;
using Microsoft.AspNetCore.Routing.Patterns;
using Microsoft.Extensions.Primitives;
using SharpPress.Models;
using SharpPress.Services;

namespace SharpPress.Plugins
{
    public sealed class PluginEndpointMetadata
    {
        public string Owner { get; }
        public string Method { get; }
        public string Pattern { get; }

        public PluginEndpointMetadata(string owner, string method, string pattern)
        {
            Owner = owner;
            Method = method;
            Pattern = pattern;
        }
    }

    public sealed class PluginEndpointDataSource : EndpointDataSource
    {
        private static readonly string[] ReservedPrefixes =
        {
            "/login", "/logout", "/register", "/setup", "/health", "/api/plugins", "/api/marketplace", "/sitemap.xml", "/robots.txt", "/rss"
        };

        private readonly object _lock = new();
        private readonly List<Endpoint> _endpoints = new();
        private readonly IServiceProvider _serviceProvider;
        private readonly Logger _logger;
        private CancellationTokenSource _cts = new();
        private IChangeToken _changeToken;

        public PluginEndpointDataSource(IServiceProvider serviceProvider, Logger logger)
        {
            _serviceProvider = serviceProvider;
            _logger = logger;
            _changeToken = new CancellationChangeToken(_cts.Token);
        }

        public override IReadOnlyList<Endpoint> Endpoints
        {
            get
            {
                lock (_lock)
                    return _endpoints.ToList();
            }
        }

        public override IChangeToken GetChangeToken() => _changeToken;

        public Endpoint Add(string owner, string method, string pattern, Delegate handler, PluginRouteOptions? options)
        {
            if (string.IsNullOrWhiteSpace(pattern) || !pattern.StartsWith('/'))
                throw new ArgumentException($"Route pattern '{pattern}' must start with '/'.");

            var normalized = pattern.ToLowerInvariant();
            foreach (var reserved in ReservedPrefixes)
            {
                if (normalized == reserved || normalized.StartsWith(reserved + "/"))
                    throw new InvalidOperationException($"Route '{pattern}' is reserved by the core.");
            }

            var requiredRole = options?.RequiredRole;
            if ((normalized == "/admin" || normalized.StartsWith("/admin/")) && requiredRole == null)
                requiredRole = UserRole.Admin;

            var routePattern = RoutePatternFactory.Parse(pattern);
            var factoryOptions = new RequestDelegateFactoryOptions
            {
                ServiceProvider = _serviceProvider,
                RouteParameterNames = routePattern.Parameters.Select(p => p.Name).ToList()
            };

            var factoryResult = RequestDelegateFactory.Create(handler, factoryOptions);
            var inner = factoryResult.RequestDelegate;
            var allowAnonymous = options?.AllowAnonymous ?? false;

            RequestDelegate guarded = async context =>
            {
                try
                {
                    if (requiredRole != null && !allowAnonymous)
                    {
                        var users = context.RequestServices.GetRequiredService<IUserService>();
                        var user = await users.GetUserAsync(context.User);

                        if (user == null)
                        {
                            context.Response.StatusCode = StatusCodes.Status401Unauthorized;
                            return;
                        }

                        if (!user.HasRole(requiredRole.Value))
                        {
                            context.Response.StatusCode = StatusCodes.Status403Forbidden;
                            return;
                        }
                    }

                    await inner(context);
                }
                catch (Exception ex)
                {
                    _logger.LogError($"Plugin route {method} {pattern} ({owner}) failed: {ex.Message}");
                    if (!context.Response.HasStarted)
                    {
                        context.Response.StatusCode = StatusCodes.Status500InternalServerError;
                        await context.Response.WriteAsync("Plugin error.");
                    }
                }
            };

            var builder = new RouteEndpointBuilder(guarded, routePattern, 0)
            {
                DisplayName = $"{owner}: {method} {pattern}"
            };

            foreach (var item in factoryResult.EndpointMetadata)
                builder.Metadata.Add(item);

            if (method != "*")
                builder.Metadata.Add(new HttpMethodMetadata(new[] { method }));

            builder.Metadata.Add(new PluginEndpointMetadata(owner, method, pattern));
            var endpoint = builder.Build();

            lock (_lock)
            {
                var duplicate = _endpoints.OfType<RouteEndpoint>().FirstOrDefault(e =>
                    e.Metadata.GetMetadata<PluginEndpointMetadata>() is { } meta
                    && meta.Method == method
                    && string.Equals(meta.Pattern, pattern, StringComparison.OrdinalIgnoreCase));

                if (duplicate != null)
                {
                    var existing = duplicate.Metadata.GetMetadata<PluginEndpointMetadata>()!;
                    throw new InvalidOperationException($"Route {method} {pattern} is already registered by plugin '{existing.Owner}'.");
                }

                _endpoints.Add(endpoint);
                Notify();
            }

            _logger.Log($"🔗 Route registered: {method} {pattern} (Plugin: {owner})");
            return endpoint;
        }

        public void Remove(Endpoint endpoint)
        {
            lock (_lock)
            {
                if (_endpoints.Remove(endpoint))
                    Notify();
            }
        }

        private void Notify()
        {
            var previous = _cts;
            _cts = new CancellationTokenSource();
            _changeToken = new CancellationChangeToken(_cts.Token);
            previous.Cancel();
            previous.Dispose();
        }
    }
}
