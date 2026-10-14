using SharpPress.Services;

namespace SharpPress.Plugins
{
    public sealed class PluginFilters : IPluginFilters
    {
        private readonly IFilterPipeline _pipeline;
        private readonly PluginScope _scope;

        public PluginFilters(IFilterPipeline pipeline, PluginScope scope)
        {
            _pipeline = pipeline;
            _scope = scope;
        }

        public void Add<T>(string key, Func<T, T> filter, int priority = 0)
        {
            var registration = _pipeline.AddFilter(key, filter, priority);
            _scope.OnDispose(registration.Dispose);
        }

        public void AddShortcode(string name, Func<ShortcodeContext, string> handler)
        {
            var registration = _pipeline.AddShortcode(name, handler);
            _scope.OnDispose(registration.Dispose);
        }
    }
}
