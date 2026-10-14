using SharpPress.Models;
using SharpPress.Services.Database;
using System.Linq.Expressions;
using System.Reflection;

namespace SharpPress.Plugins
{
    public class PluginSchemaVersion : FeatherData
    {
        public string PluginName { get; set; } = "";
        public int Version { get; set; }
    }

    public sealed class PluginDatabase : IPluginDatabase
    {
        private static readonly SemaphoreSlim VersionLock = new(1, 1);

        private readonly FeatherDatabase _db;
        private readonly Assembly _owner;
        private readonly string _pluginName;

        public PluginDatabase(FeatherDatabase db, Assembly owner, string pluginName)
        {
            _db = db;
            _owner = owner;
            _pluginName = pluginName;
        }

        public Task CreateTableAsync<T>() where T : FeatherData, new()
        {
            EnsureOwned<T>();
            return _db.CreateTable<T>();
        }

        public Task CreateIndexAsync<T>(string columnName, bool unique = false) where T : FeatherData, new()
        {
            EnsureOwned<T>();
            return _db.CreateIndex<T>(columnName, unique);
        }

        public Task<List<T>> GetAllAsync<T>() where T : FeatherData, new()
        {
            EnsureOwned<T>();
            return _db.GetAll<T>();
        }

        public Task<T?> GetAsync<T>(int id) where T : FeatherData, new()
        {
            EnsureOwned<T>();
            return _db.GetData<T>(id);
        }

        public Task<T?> FindAsync<T>(Expression<Func<T, bool>> predicate) where T : FeatherData, new()
        {
            EnsureOwned<T>();
            return _db.GetByLinq(predicate);
        }

        public Task<List<T>> WhereAsync<T>(Expression<Func<T, bool>> predicate) where T : FeatherData, new()
        {
            EnsureOwned<T>();
            return _db.GetListByLinq(predicate);
        }

        public Task<long> CountAsync<T>() where T : FeatherData, new()
        {
            EnsureOwned<T>();
            return _db.Count<T>();
        }

        public Task SaveAsync<T>(T item) where T : FeatherData, new()
        {
            EnsureOwned<T>();
            return _db.SaveData(item);
        }

        public Task DeleteAsync<T>(int id) where T : FeatherData, new()
        {
            EnsureOwned<T>();
            return _db.Delete<T>(id);
        }

        public IQueryable<T> Query<T>() where T : FeatherData, new()
        {
            EnsureOwned<T>();
            return _db.Query<T>();
        }

        public async Task MigrateAsync(int version, Func<Task> migration)
        {
            await VersionLock.WaitAsync();
            try
            {
                await _db.CreateTable<PluginSchemaVersion>();
                var record = await _db.GetByLinq<PluginSchemaVersion>(v => v.PluginName == _pluginName);

                if (record != null && record.Version >= version)
                    return;

                await migration();

                record ??= new PluginSchemaVersion { PluginName = _pluginName };
                record.Version = version;
                await _db.SaveData(record);
            }
            finally
            {
                VersionLock.Release();
            }
        }

        private void EnsureOwned<T>()
        {
            if (typeof(T).Assembly != _owner)
                throw new InvalidOperationException($"{typeof(T).Name} does not belong to plugin '{_pluginName}'. Plugins may only use their own entity types; use the core services for core data.");
        }
    }
}
