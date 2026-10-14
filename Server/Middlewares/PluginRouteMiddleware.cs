using SharpPress.Services;

namespace SharpPress.Middleware
{
    public sealed class PluginMiddlewareRegistry
    {
        private readonly object _lock = new();
        private readonly List<Entry> _entries = new();
        private int _version;

        public int Version => Volatile.Read(ref _version);

        public event Action? Changed;

        public IDisposable Add(string owner, int order, Func<HttpContext, Func<Task>, Task> middleware)
        {
            var entry = new Entry(owner, order, middleware);
            lock (_lock)
            {
                _entries.Add(entry);
                Interlocked.Increment(ref _version);
            }

            Changed?.Invoke();
            return new Registration(this, entry);
        }

        public IReadOnlyList<Func<HttpContext, Func<Task>, Task>> Snapshot()
        {
            lock (_lock)
                return _entries.OrderBy(e => e.Order).Select(e => e.Middleware).ToList();
        }

        private void Remove(Entry entry)
        {
            var removed = false;
            lock (_lock)
            {
                if (_entries.Remove(entry))
                {
                    Interlocked.Increment(ref _version);
                    removed = true;
                }
            }

            if (removed) Changed?.Invoke();
        }

        private sealed record Entry(string Owner, int Order, Func<HttpContext, Func<Task>, Task> Middleware);

        private sealed class Registration : IDisposable
        {
            private readonly PluginMiddlewareRegistry _registry;
            private readonly Entry _entry;

            public Registration(PluginMiddlewareRegistry registry, Entry entry)
            {
                _registry = registry;
                _entry = entry;
            }

            public void Dispose() => _registry.Remove(_entry);
        }
    }

    public class PluginRouteMiddleware
    {
        private readonly RequestDelegate _next;
        private readonly PluginMiddlewareRegistry _registry;
        private (int Version, RequestDelegate Chain) _built = (-1, null!);

        public PluginRouteMiddleware(RequestDelegate next, PluginMiddlewareRegistry registry)
        {
            _next = next;
            _registry = registry;
            _registry.Changed += () => _built = (-1, null!);
        }

        public Task InvokeAsync(HttpContext context)
        {
            var version = _registry.Version;
            var built = _built;

            if (built.Version != version)
            {
                var chain = _next;
                foreach (var middleware in _registry.Snapshot().Reverse())
                {
                    var downstream = chain;
                    var current = middleware;
                    chain = ctx => current(ctx, () => downstream(ctx));
                }

                built = (version, chain);
                _built = built;
            }

            return built.Chain(context);
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
