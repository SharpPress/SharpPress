using SharpPress.Models;
using System.Linq.Expressions;

namespace SharpPress.Plugins
{
    public interface IPluginDatabase
    {
        Task CreateTableAsync<T>() where T : FeatherData, new();
        Task CreateIndexAsync<T>(string columnName, bool unique = false) where T : FeatherData, new();
        Task<List<T>> GetAllAsync<T>() where T : FeatherData, new();
        Task<T?> GetAsync<T>(int id) where T : FeatherData, new();
        Task<T?> FindAsync<T>(Expression<Func<T, bool>> predicate) where T : FeatherData, new();
        Task<List<T>> WhereAsync<T>(Expression<Func<T, bool>> predicate) where T : FeatherData, new();
        Task<long> CountAsync<T>() where T : FeatherData, new();
        Task SaveAsync<T>(T item) where T : FeatherData, new();
        Task DeleteAsync<T>(int id) where T : FeatherData, new();
        IQueryable<T> Query<T>() where T : FeatherData, new();
        Task MigrateAsync(int version, Func<Task> migration);
    }
}
