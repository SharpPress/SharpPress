using SharpPress.Services;

namespace SharpPress.Plugins
{
    public sealed class PluginMeta : IPluginMeta
    {
        private readonly IPostMetaService _meta;
        private readonly PluginScope _scope;
        private readonly string _plugin;

        public PluginMeta(IPostMetaService meta, PluginScope scope, string plugin)
        {
            _meta = meta;
            _scope = scope;
            _plugin = plugin;
        }

        public void RegisterField(string key, string label, MetaFieldType type = MetaFieldType.Text, string? description = null)
        {
            var registration = _meta.RegisterField(new MetaField { Key = key, Label = label, Type = type, Description = description, Owner = _plugin });
            _scope.OnDispose(registration.Dispose);
        }

        public Task<string?> GetAsync(int postId, string key) => _meta.GetAsync(postId, key);

        public Task<Dictionary<string, string>> GetAllAsync(int postId) => _meta.GetAllAsync(postId);

        public Task SetAsync(int postId, string key, string? value) => _meta.SetAsync(postId, key, value);
    }
}
