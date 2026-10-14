using SharpPress.Models;
using SharpPress.Services.Database;

namespace SharpPress.Services
{
    public class PostMetaService : IPostMetaService
    {
        private readonly object _lock = new();
        private readonly List<MetaField> _fields = new();
        private readonly FeatherDatabase _db;

        public PostMetaService(FeatherDatabase db)
        {
            _db = db;
        }

        public IReadOnlyList<MetaField> Fields
        {
            get
            {
                lock (_lock)
                    return _fields.ToList();
            }
        }

        public IDisposable RegisterField(MetaField field)
        {
            lock (_lock)
            {
                if (_fields.Any(f => f.Key == field.Key))
                    throw new InvalidOperationException($"Meta field '{field.Key}' is already registered by '{_fields.First(f => f.Key == field.Key).Owner}'.");

                _fields.Add(field);
            }

            return new Removal(() =>
            {
                lock (_lock)
                    _fields.Remove(field);
            });
        }

        public async Task<string?> GetAsync(int postId, string key)
        {
            var meta = await _db.GetByLinq<PostMeta>(m => m.PostId == postId && m.MetaKey == key);
            return meta?.MetaValue;
        }

        public async Task<Dictionary<string, string>> GetAllAsync(int postId)
        {
            var items = await _db.GetListByLinq<PostMeta>(m => m.PostId == postId);
            return items.ToDictionary(m => m.MetaKey, m => m.MetaValue);
        }

        public async Task SetAsync(int postId, string key, string? value)
        {
            var existing = await _db.GetByLinq<PostMeta>(m => m.PostId == postId && m.MetaKey == key);

            if (value == null)
            {
                if (existing != null) await _db.Delete<PostMeta>(existing.Id);
                return;
            }

            existing ??= new PostMeta { PostId = postId, MetaKey = key };
            existing.MetaValue = value;
            await _db.SaveData(existing);
        }

        public async Task DeleteAllAsync(int postId)
        {
            await _db.DeleteByLinq<PostMeta>(m => m.PostId == postId);
        }

        private sealed class Removal : IDisposable
        {
            private Action? _action;

            public Removal(Action action)
            {
                _action = action;
            }

            public void Dispose() => Interlocked.Exchange(ref _action, null)?.Invoke();
        }
    }
}
