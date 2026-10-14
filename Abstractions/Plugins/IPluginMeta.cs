using SharpPress.Services;

namespace SharpPress.Plugins
{
    public interface IPluginMeta
    {
        void RegisterField(string key, string label, MetaFieldType type = MetaFieldType.Text, string? description = null);
        Task<string?> GetAsync(int postId, string key);
        Task<Dictionary<string, string>> GetAllAsync(int postId);
        Task SetAsync(int postId, string key, string? value);
    }
}
