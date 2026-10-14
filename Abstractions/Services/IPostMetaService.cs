namespace SharpPress.Services
{
    public enum MetaFieldType
    {
        Text,
        TextArea,
        Number,
        Url,
        Checkbox
    }

    public sealed class MetaField
    {
        public string Key { get; init; } = "";
        public string Label { get; init; } = "";
        public MetaFieldType Type { get; init; } = MetaFieldType.Text;
        public string? Description { get; init; }
        public string Owner { get; init; } = "";
    }

    public interface IPostMetaService
    {
        IReadOnlyList<MetaField> Fields { get; }
        IDisposable RegisterField(MetaField field);
        Task<string?> GetAsync(int postId, string key);
        Task<Dictionary<string, string>> GetAllAsync(int postId);
        Task SetAsync(int postId, string key, string? value);
        Task DeleteAllAsync(int postId);
    }
}
