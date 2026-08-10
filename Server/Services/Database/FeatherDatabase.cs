using MySqlConnector;
using Newtonsoft.Json;
using SharpPress.Models;
using System.Collections.Concurrent;
using System.ComponentModel.DataAnnotations;
using System.Data;
using System.Linq.Expressions;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Security;
using System.Collections;
using System.Text;
using System.Text.RegularExpressions;

namespace SharpPress.Services.Database
{
    public interface IVersioned
    {
        long Version { get; set; }
    }

    public class ConcurrencyException : Exception
    {
        public ConcurrencyException(string message) : base(message) { }
    }

    public sealed class FeatherDatabase
    {
        private readonly FeatherDatabaseOptions _options;
        private readonly string _connectionString;
        private readonly ILogger<FeatherDatabase> _logger;
        private readonly CancellationToken _globalCancellationToken;
        private static readonly ConcurrentDictionary<Type, PropertyInfo[]> _propertyCache = new();

        private const int MaxRetryAttempts = 3;
        private static readonly TimeSpan[] RetryDelays = {
            TimeSpan.FromMilliseconds(100),
            TimeSpan.FromMilliseconds(250),
            TimeSpan.FromMilliseconds(500)
        };

        private static readonly HashSet<int> _transientErrorNumbers = new() { 1213, 1205, 2013, 2006, 1040 };

        private static bool IsTransient(MySqlException ex) => _transientErrorNumbers.Contains(ex.Number);

        private static readonly ConditionalWeakTable<object, Dictionary<string, object?>> _snapshots = new();

        public FeatherDatabase(ILogger<FeatherDatabase> logger, MySQL_Config mySQL_Config, CancellationToken globalCancellationToken = default, FeatherDatabaseOptions databaseOptions = null)
        {
            _logger = logger;
            _globalCancellationToken = globalCancellationToken;
            _options = databaseOptions == null ? new FeatherDatabaseOptions
            {
                AutoAddColumns = true,
                AutoCreateTable = true,
                AutoModifyColumns = true,
                AutoDeleteOrphanedColumns = false,
                DebugMode = false
            } : databaseOptions;

            try
            {
                var serverBuilder = new MySqlConnectionStringBuilder
                {
                    Server = mySQL_Config.host,
                    Port = (uint)mySQL_Config.port,
                    UserID = mySQL_Config.database_username,
                    Password = mySQL_Config.database_password,
                    SslMode = MySqlSslMode.Preferred,
                    Pooling = false,
                    ConnectionTimeout = 5,
                    DefaultCommandTimeout = 30,
                    AllowUserVariables = false
                };

                using (var serverConnection = new MySqlConnection(serverBuilder.ConnectionString))
                {
                    serverConnection.Open();
                    using var cmd = serverConnection.CreateCommand();
                    cmd.CommandText =
                        $"CREATE DATABASE IF NOT EXISTS `{mySQL_Config.database_name}` " +
                        "CHARACTER SET utf8mb4 COLLATE utf8mb4_unicode_ci";
                    cmd.ExecuteNonQuery();
                }

                var builder = new MySqlConnectionStringBuilder
                {
                    Server = mySQL_Config.host,
                    Port = (uint)mySQL_Config.port,
                    Database = mySQL_Config.database_name,
                    UserID = mySQL_Config.database_username,
                    Password = mySQL_Config.database_password,
                    SslMode = MySqlSslMode.Preferred,
                    Pooling = true,
                    MinimumPoolSize = 0,
                    MaximumPoolSize = 100,
                    ConnectionTimeout = 10,
                    DefaultCommandTimeout = 30,
                    AllowUserVariables = false
                };

                _connectionString = builder.ConnectionString;

                using var connection = new MySqlConnection(_connectionString);
                connection.Open();

                _logger.LogInformation("FeatherDatabase connected to MySQL {Host}:{Port}/{Database}",
                    mySQL_Config.host, mySQL_Config.port, mySQL_Config.database_name);
            }
            catch (Exception ex)
            {
                _logger.LogCritical(ex, "Failed to initialise FeatherDatabase");
                throw;
            }
        }

        /// <summary>
        /// When true, SQL statements and parameters are logged at Debug level.
        /// When false, no SQL is logged regardless of ILogger log level.
        /// </summary>
        public bool DebugMode
        {
            get => _options.DebugMode;
            set => _options.DebugMode = value;
        }

        public IQueryable<T> Query<T>() where T : new() => new FeatherQueryable<T>(this);

        public async Task<List<T>> GetPagedByLinq<T>(
            int pageNumber,
            int pageSize,
            Expression<Func<T, bool>> predicate,
            Expression<Func<T, object>> orderBy,
            bool descending = true,
            CancellationToken cancellationToken = default) where T : new()
        {
            if (pageNumber <= 0) throw new ArgumentOutOfRangeException(nameof(pageNumber));
            if (pageSize <= 0) throw new ArgumentOutOfRangeException(nameof(pageSize));

            var token = cancellationToken == default ? _globalCancellationToken : cancellationToken;

            var (where, args) = BuildWhere(predicate);
            var orderByProperty = GetPropertyName(orderBy);
            var tableName = GetTableName(typeof(T));
            var sql = $@"
                SELECT * FROM `{tableName}`
                WHERE {where}
                ORDER BY `{orderByProperty}` {(descending ? "DESC" : "ASC")}
                LIMIT @Limit OFFSET @Offset";

            var parameters = new List<MySqlParameter>(args)
            {
                new("@Limit", pageSize),
                new("@Offset", (pageNumber - 1) * pageSize)
            };

            return await ExecuteQuery<T>(sql, token, parameters.ToArray()).ConfigureAwait(false);
        }

        public IQueryable<T> WhereLinq<T>(Expression<Func<T, bool>> predicate) where T : new()
            => new FeatherQueryable<T>(this).Where(predicate);

        public async Task CreateTable<T>(CancellationToken cancellationToken = default) where T : new()
        {
            var token = cancellationToken == default ? _globalCancellationToken : cancellationToken;

            var type = typeof(T);
            var props = GetCachedProperties(type);
            var tableName = GetTableName(type);

            var parts = new List<string>();
            bool hasId = false;
            bool isVersioned = typeof(IVersioned).IsAssignableFrom(type);

            foreach (var prop in props)
            {
                if (prop.Name.Equals("Id", StringComparison.OrdinalIgnoreCase))
                {
                    parts.Add($"`{prop.Name}` BIGINT NOT NULL AUTO_INCREMENT PRIMARY KEY");
                    hasId = true;
                }
                else if (isVersioned && prop.Name.Equals("Version", StringComparison.OrdinalIgnoreCase))
                {
                    parts.Add($"`{prop.Name}` BIGINT NOT NULL DEFAULT 0");
                }
                else
                {
                    parts.Add($"`{prop.Name}` {GetSqlType(prop)} NULL");
                }
            }

            if (!hasId)
                throw new InvalidOperationException($"Type {type.Name} must have an Id property for FeatherDatabase.");

            var sql = $"CREATE TABLE IF NOT EXISTS `{tableName}` ({string.Join(", ", parts)}) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4;";

            await ExecuteNonQuery(sql, token).ConfigureAwait(false);
            _logger.LogInformation("Table '{TableName}' ensured", tableName);

            if (_options.AutoAddColumns || _options.AutoModifyColumns || _options.AutoDeleteOrphanedColumns)
                await EnsureTableStructure(tableName, props, token).ConfigureAwait(false);
        }

        public async Task SaveData<T>(T obj, CancellationToken cancellationToken = default) where T : new()
        {
            var token = cancellationToken == default ? _globalCancellationToken : cancellationToken;
            await using var connection = new MySqlConnection(_connectionString);
            await connection.OpenAsync(token).ConfigureAwait(false);
            await SaveInternal(connection, null, obj, token).ConfigureAwait(false);
        }

        public async Task SaveMultiData<T>(List<T> items, CancellationToken cancellationToken = default) where T : new()
        {
            if (items == null || items.Count == 0) return;

            var token = cancellationToken == default ? _globalCancellationToken : cancellationToken;

            await ExecuteInTransactionAsync(async (connection, transaction, ct) =>
            {
                foreach (var item in items)
                    await SaveInternal(connection, transaction, item, ct).ConfigureAwait(false);
            }, token).ConfigureAwait(false);
        }

        /// <summary>
        /// Saves only the properties that changed since the entity was loaded.
        /// Falls back to a full SaveData when no snapshot is available.
        /// </summary>
        public async Task SaveChangedData<T>(T obj, CancellationToken cancellationToken = default) where T : notnull, new()
        {
            var token = cancellationToken == default ? _globalCancellationToken : cancellationToken;

            var type = typeof(T);
            var props = GetCachedProperties(type);
            var tableName = GetTableName(type);

            var idProp = props.FirstOrDefault(p => p.Name.Equals("Id", StringComparison.OrdinalIgnoreCase))
                         ?? throw new InvalidOperationException($"Type {type.Name} must have an Id property.");

            var idValueObj = idProp.GetValue(obj);
            long idValue = idValueObj != null ? Convert.ToInt64(idValueObj) : 0;

            if (idValue == 0 || !_snapshots.TryGetValue(obj, out var snapshot))
            {
                await SaveData(obj, token).ConfigureAwait(false);
                return;
            }

            bool isVersioned = obj is IVersioned;
            var setParts = new List<string>();
            var parameters = new List<MySqlParameter>();
            int paramIndex = 0;

            foreach (var prop in props)
            {
                if (prop.Name.Equals("Id", StringComparison.OrdinalIgnoreCase)) continue;
                if (isVersioned && prop.Name.Equals("Version", StringComparison.OrdinalIgnoreCase)) continue;
                if (!prop.CanRead) continue;

                var currentValue = prop.GetValue(obj);
                snapshot.TryGetValue(prop.Name, out var oldValue);

                if (Equals(currentValue, oldValue)) continue;

                if (IsNumericType(prop.PropertyType) && oldValue != null && currentValue != null)
                {
                    decimal delta = Convert.ToDecimal(currentValue) - Convert.ToDecimal(oldValue);
                    var pName = $"@d{paramIndex++}";
                    setParts.Add($"`{prop.Name}` = `{prop.Name}` + {pName}");
                    parameters.Add(new MySqlParameter(pName, delta));
                }
                else
                {
                    var pName = $"@v{paramIndex++}";
                    setParts.Add($"`{prop.Name}` = {pName}");
                    parameters.Add(new MySqlParameter(pName, ToDbValue(currentValue)));
                }
            }

            if (setParts.Count == 0) return;

            long expectedVersion = 0;
            string sql;
            if (isVersioned)
            {
                expectedVersion = ((IVersioned)obj).Version;
                setParts.Add("`Version` = `Version` + 1");
                sql = $"UPDATE `{tableName}` SET {string.Join(", ", setParts)} WHERE `{idProp.Name}` = @Id AND `Version` = @ExpectedVersion";
                parameters.Add(new MySqlParameter("@ExpectedVersion", expectedVersion));
            }
            else
            {
                sql = $"UPDATE `{tableName}` SET {string.Join(", ", setParts)} WHERE `{idProp.Name}` = @Id";
            }

            parameters.Add(new MySqlParameter("@Id", idValue));

            var rowsAffected = await ExecuteNonQueryWithCount(sql, token, parameters.ToArray()).ConfigureAwait(false);

            if (isVersioned)
            {
                if (rowsAffected == 0)
                    throw new ConcurrencyException(
                        $"{type.Name} (Id={idValue}) was modified by another writer since it was loaded (expected Version {expectedVersion}).");
                ((IVersioned)obj).Version = expectedVersion + 1;
            }

            CaptureSnapshot(obj, props);
        }

        public async Task SaveMultiChangedData<T>(List<T> items, CancellationToken cancellationToken = default) where T : notnull, new()
        {
            if (items == null || items.Count == 0) return;
            var token = cancellationToken == default ? _globalCancellationToken : cancellationToken;
            foreach (var item in items)
                await SaveChangedData(item, token).ConfigureAwait(false);
        }

        public async Task<T?> GetData<T>(long id, CancellationToken cancellationToken = default) where T : new()
        {
            var token = cancellationToken == default ? _globalCancellationToken : cancellationToken;

            var type = typeof(T);
            var tableName = GetTableName(type);
            var props = GetCachedProperties(type);
            var idProp = props.FirstOrDefault(p => p.Name.Equals("Id", StringComparison.OrdinalIgnoreCase))
                         ?? throw new InvalidOperationException($"Type {type.Name} does not have an Id property.");

            var sql = $"SELECT * FROM `{tableName}` WHERE `{idProp.Name}` = @Id LIMIT 1";
            LogSql(sql, new[] { new MySqlParameter("@Id", id) });

            return await WithRetry(async ct =>
            {
                await using var connection = new MySqlConnection(_connectionString);
                await connection.OpenAsync(ct).ConfigureAwait(false);
                await using var cmd = new MySqlCommand(sql, connection);
                cmd.Parameters.AddWithValue("@Id", id);

                await using var reader = await cmd.ExecuteReaderAsync(ct).ConfigureAwait(false);
                if (await reader.ReadAsync(ct).ConfigureAwait(false))
                    return (T)ReadRow(type, reader, props);

                return default;
            }, token).ConfigureAwait(false);
        }

        public async Task<List<T>> GetAll<T>(CancellationToken cancellationToken = default) where T : new()
        {
            var token = cancellationToken == default ? _globalCancellationToken : cancellationToken;
            var sql = $"SELECT * FROM `{GetTableName(typeof(T))}`";
            return await ExecuteQuery<T>(sql, token).ConfigureAwait(false);
        }

        public async Task Delete<T>(long id, CancellationToken cancellationToken = default) where T : new()
        {
            var token = cancellationToken == default ? _globalCancellationToken : cancellationToken;

            var type = typeof(T);
            var tableName = GetTableName(type);
            var idProp = GetCachedProperties(type).FirstOrDefault(p => p.Name.Equals("Id", StringComparison.OrdinalIgnoreCase));
            if (idProp == null) return;

            var sql = $"DELETE FROM `{tableName}` WHERE `{idProp.Name}` = @Id";
            await ExecuteNonQuery(sql, token, new MySqlParameter("@Id", id)).ConfigureAwait(false);
        }

        public async Task<int> DeleteByLinq<T>(Expression<Func<T, bool>> predicate, CancellationToken cancellationToken = default) where T : new()
        {
            var token = cancellationToken == default ? _globalCancellationToken : cancellationToken;
            var (where, args) = BuildWhere(predicate);
            var sql = $"DELETE FROM `{GetTableName(typeof(T))}` WHERE {where}";
            return await ExecuteNonQuery(sql, token, args.ToArray()).ConfigureAwait(false);
        }

        public string GetConnectionString() => _connectionString;

        public Task<int> ExecuteNonQueryWithCount(string sql, params MySqlParameter[] parameters)
            => ExecuteNonQueryWithCount(sql, _globalCancellationToken, parameters);

        public async Task<int> ExecuteNonQueryWithCount(string sql, CancellationToken cancellationToken, params MySqlParameter[] parameters)
        {
            var token = cancellationToken == default ? _globalCancellationToken : cancellationToken;
            LogSql(sql, parameters);
            return await WithRetry(async ct =>
            {
                await using var connection = new MySqlConnection(_connectionString);
                await connection.OpenAsync(ct).ConfigureAwait(false);
                await using var cmd = new MySqlCommand(sql, connection);
                if (parameters != null && parameters.Length > 0)
                    cmd.Parameters.AddRange(parameters);
                return await cmd.ExecuteNonQueryAsync(ct).ConfigureAwait(false);
            }, token).ConfigureAwait(false);
        }

        public Task<List<T>> ExecuteQuery<T>(string sql, params MySqlParameter[] parameters) where T : new()
            => ExecuteQuery<T>(sql, _globalCancellationToken, parameters);

        public async Task<List<T>> ExecuteQuery<T>(string sql, CancellationToken cancellationToken, params MySqlParameter[] parameters) where T : new()
        {
            var token = cancellationToken == default ? _globalCancellationToken : cancellationToken;
            ValidateSelectOnly(sql);
            LogSql(sql, parameters);

            var type = typeof(T);
            var props = GetCachedProperties(type);

            return await WithRetry(async ct =>
            {
                var list = new List<T>();

                await using var connection = new MySqlConnection(_connectionString);
                await connection.OpenAsync(ct).ConfigureAwait(false);
                await using var cmd = new MySqlCommand(sql, connection);
                if (parameters != null && parameters.Length > 0)
                    cmd.Parameters.AddRange(parameters);

                await using var reader = await cmd.ExecuteReaderAsync(ct).ConfigureAwait(false);
                while (await reader.ReadAsync(ct).ConfigureAwait(false))
                    list.Add((T)ReadRow(type, reader, props));

                return list;
            }, token).ConfigureAwait(false);
        }

        public Task<int> ExecuteNonQuery(string sql, params MySqlParameter[] parameters)
            => ExecuteNonQuery(sql, _globalCancellationToken, parameters);

        public async Task<int> ExecuteNonQuery(string sql, CancellationToken cancellationToken, params MySqlParameter[] parameters)
        {
            var token = cancellationToken == default ? _globalCancellationToken : cancellationToken;
            LogSql(sql, parameters);
            return await WithRetry(async ct =>
            {
                await using var connection = new MySqlConnection(_connectionString);
                await connection.OpenAsync(ct).ConfigureAwait(false);
                await using var cmd = new MySqlCommand(sql, connection);
                if (parameters != null && parameters.Length > 0)
                    cmd.Parameters.AddRange(parameters);
                return await cmd.ExecuteNonQueryAsync(ct).ConfigureAwait(false);
            }, token).ConfigureAwait(false);
        }

        public Task<object?> ExecuteScalar(string sql, params MySqlParameter[] parameters)
            => ExecuteScalar(sql, _globalCancellationToken, parameters);

        public async Task<object?> ExecuteScalar(string sql, CancellationToken cancellationToken, params MySqlParameter[] parameters)
        {
            var token = cancellationToken == default ? _globalCancellationToken : cancellationToken;
            LogSql(sql, parameters);
            return await WithRetry(async ct =>
            {
                await using var connection = new MySqlConnection(_connectionString);
                await connection.OpenAsync(ct).ConfigureAwait(false);
                await using var cmd = new MySqlCommand(sql, connection);
                if (parameters != null && parameters.Length > 0)
                    cmd.Parameters.AddRange(parameters);
                return await cmd.ExecuteScalarAsync(ct).ConfigureAwait(false);
            }, token).ConfigureAwait(false);
        }

        public async Task<bool> Exists<T>(Expression<Func<T, bool>> predicate, CancellationToken cancellationToken = default) where T : new()
        {
            var token = cancellationToken == default ? _globalCancellationToken : cancellationToken;
            var (where, args) = BuildWhere(predicate);
            var sql = $"SELECT 1 FROM `{GetTableName(typeof(T))}` WHERE {where} LIMIT 1";
            var result = await ExecuteScalar(sql, token, args.ToArray()).ConfigureAwait(false);
            return result != null;
        }

        public Task<bool> Exists<T>(string whereClause = "", params MySqlParameter[] parameters) where T : new()
            => Exists<T>(whereClause, _globalCancellationToken, parameters);

        public async Task<bool> Exists<T>(string whereClause, CancellationToken cancellationToken, params MySqlParameter[] parameters) where T : new()
        {
            var token = cancellationToken == default ? _globalCancellationToken : cancellationToken;
            var tableName = GetTableName(typeof(T));
            var sql = $"SELECT 1 FROM `{tableName}`";

            if (!string.IsNullOrWhiteSpace(whereClause))
            {
                var clause = whereClause.Trim();
                if (clause.StartsWith("WHERE", StringComparison.OrdinalIgnoreCase))
                    clause = clause.Substring(5).Trim();
                ValidateWhereClause(clause);
                sql += $" WHERE {clause}";
            }

            sql += " LIMIT 1";
            var result = await ExecuteScalar(sql, token, parameters).ConfigureAwait(false);
            return result != null;
        }

        public async Task<long> Count<T>(CancellationToken cancellationToken = default) where T : new()
        {
            var token = cancellationToken == default ? _globalCancellationToken : cancellationToken;
            var sql = $"SELECT COUNT(*) FROM `{GetTableName(typeof(T))}`";
            var result = await ExecuteScalar(sql, token).ConfigureAwait(false);
            return Convert.ToInt64(result);
        }

        public async Task<List<T>> GetPaged<T>(int pageNumber, int pageSize, string orderByColumn = "Id", CancellationToken cancellationToken = default) where T : new()
        {
            if (pageNumber <= 0) throw new ArgumentOutOfRangeException(nameof(pageNumber));
            if (pageSize <= 0) throw new ArgumentOutOfRangeException(nameof(pageSize));

            var token = cancellationToken == default ? _globalCancellationToken : cancellationToken;

            var type = typeof(T);
            var tableName = GetTableName(type);
            var props = GetCachedProperties(type);

            int offset = (pageNumber - 1) * pageSize;

            if (!props.Any(p => p.Name.Equals(orderByColumn, StringComparison.OrdinalIgnoreCase)))
                throw new ArgumentException($"Table '{tableName}' doesn't have a column '{orderByColumn}' for paging.");

            var sql = $"SELECT * FROM `{tableName}` ORDER BY `{orderByColumn}` LIMIT @Limit OFFSET @Offset";
            LogSql(sql, new[] { new MySqlParameter("@Limit", pageSize), new MySqlParameter("@Offset", offset) });

            return await WithRetry(async ct =>
            {
                var list = new List<T>();

                await using var connection = new MySqlConnection(_connectionString);
                await connection.OpenAsync(ct).ConfigureAwait(false);
                await using var cmd = new MySqlCommand(sql, connection);
                cmd.Parameters.AddWithValue("@Limit", pageSize);
                cmd.Parameters.AddWithValue("@Offset", offset);

                await using var reader = await cmd.ExecuteReaderAsync(ct).ConfigureAwait(false);
                while (await reader.ReadAsync(ct).ConfigureAwait(false))
                    list.Add((T)ReadRow(type, reader, props));

                return list;
            }, token).ConfigureAwait(false);
        }

        public async Task<T?> GetByColumn<T>(string columnName, object value, CancellationToken cancellationToken = default) where T : new()
        {
            var token = cancellationToken == default ? _globalCancellationToken : cancellationToken;

            var type = typeof(T);
            var props = GetCachedProperties(type);
            var tableName = GetTableName(type);

            var columnProp = props.FirstOrDefault(p => p.Name.Equals(columnName, StringComparison.OrdinalIgnoreCase));
            if (columnProp == null)
                throw new ArgumentException($"Table '{tableName}' doesn't contain column '{columnName}'.");

            var sql = $"SELECT * FROM `{tableName}` WHERE `{columnProp.Name}` = @Value LIMIT 1";
            LogSql(sql, new[] { new MySqlParameter("@Value", ToDbValue(value)) });

            return await WithRetry(async ct =>
            {
                await using var connection = new MySqlConnection(_connectionString);
                await connection.OpenAsync(ct).ConfigureAwait(false);
                await using var cmd = new MySqlCommand(sql, connection);
                cmd.Parameters.AddWithValue("@Value", ToDbValue(value));

                await using var reader = await cmd.ExecuteReaderAsync(ct).ConfigureAwait(false);
                if (await reader.ReadAsync(ct).ConfigureAwait(false))
                    return (T)ReadRow(type, reader, props);

                return default;
            }, token).ConfigureAwait(false);
        }

        public async Task<List<T>> GetListByColumn<T>(string columnName, object value, CancellationToken cancellationToken = default) where T : new()
        {
            var token = cancellationToken == default ? _globalCancellationToken : cancellationToken;

            var type = typeof(T);
            var props = GetCachedProperties(type);
            var tableName = GetTableName(type);

            var columnProp = props.FirstOrDefault(p => p.Name.Equals(columnName, StringComparison.OrdinalIgnoreCase));
            if (columnProp == null)
                throw new ArgumentException($"Table '{tableName}' doesn't contain column '{columnName}'.");

            var sql = $"SELECT * FROM `{tableName}` WHERE `{columnProp.Name}` = @Value";
            LogSql(sql, new[] { new MySqlParameter("@Value", ToDbValue(value)) });

            return await WithRetry(async ct =>
            {
                var list = new List<T>();
                await using var connection = new MySqlConnection(_connectionString);
                await connection.OpenAsync(ct).ConfigureAwait(false);
                await using var cmd = new MySqlCommand(sql, connection);
                cmd.Parameters.AddWithValue("@Value", ToDbValue(value));

                await using var reader = await cmd.ExecuteReaderAsync(ct).ConfigureAwait(false);
                while (await reader.ReadAsync(ct).ConfigureAwait(false))
                    list.Add((T)ReadRow(type, reader, props));

                return list;
            }, token).ConfigureAwait(false);
        }

        public Task CreateIndex<T>(string columnName, bool unique = false, CancellationToken cancellationToken = default)
        {
            var token = cancellationToken == default ? _globalCancellationToken : cancellationToken;
            return CreateIndexInternal<T>([columnName], unique, token);
        }

        public Task CreateIndex<T>(Expression<Func<T, object>> columnSelector, bool unique = false, CancellationToken cancellationToken = default)
        {
            var token = cancellationToken == default ? _globalCancellationToken : cancellationToken;
            var columnNames = ExtractColumnNames(columnSelector);

            var props = GetCachedProperties(typeof(T));
            foreach (var name in columnNames)
            {
                if (!props.Any(p => p.Name.Equals(name, StringComparison.OrdinalIgnoreCase)))
                    throw new ArgumentException($"Property '{name}' does not exist on type '{typeof(T).Name}'.");
            }

            return CreateIndexInternal<T>(columnNames, unique, token);
        }

        private async Task CreateIndexInternal<T>(List<string> columnNames, bool unique, CancellationToken cancellationToken)
        {
            var tableName = GetTableName(typeof(T));
            var columnList = string.Join(", ", columnNames.Select(c => $"`{c}`"));
            var indexName = $"IDX_{tableName}_{string.Join("_", columnNames)}";
            var sql = $"CREATE {(unique ? "UNIQUE" : "")} INDEX `{indexName}` ON `{tableName}` ({columnList});";

            try
            {
                await ExecuteNonQuery(sql, cancellationToken).ConfigureAwait(false);
                _logger.LogInformation("Index '{IndexName}' created on table '{TableName}'", indexName, tableName);
            }
            catch (MySqlException ex) when (ex.Number == 1061)
            {
                _logger.LogDebug("Index '{IndexName}' already exists on table '{TableName}'", indexName, tableName);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error creating index {IndexName}", indexName);
                throw;
            }
        }

        private static List<string> ExtractColumnNames<T>(Expression<Func<T, object>> selector)
        {
            var body = selector.Body;

            if (body is UnaryExpression unary && unary.NodeType == ExpressionType.Convert)
                body = unary.Operand;

            if (body is MemberExpression member && member.Expression?.NodeType == ExpressionType.Parameter)
                return new List<string> { member.Member.Name };

            if (body is NewExpression newExpr)
            {
                var names = new List<string>();
                foreach (var arg in newExpr.Arguments)
                {
                    var unwrapped = arg;
                    if (unwrapped is UnaryExpression u && u.NodeType == ExpressionType.Convert)
                        unwrapped = u.Operand;
                    if (unwrapped is MemberExpression m && m.Expression?.NodeType == ExpressionType.Parameter)
                        names.Add(m.Member.Name);
                    else
                        throw new ArgumentException(
                            "Invalid column selector. Use property accessors of the row parameter.");
                }
                return names;
            }

            throw new ArgumentException(
                "Invalid column selector. Use a property accessor or an anonymous object initializer.");
        }

        public async Task<long> CountByLinq<T>(Expression<Func<T, bool>> predicate, CancellationToken cancellationToken = default) where T : new()
        {
            var token = cancellationToken == default ? _globalCancellationToken : cancellationToken;
            var (where, args) = BuildWhere(predicate);
            var sql = $"SELECT COUNT(*) FROM `{GetTableName(typeof(T))}` WHERE {where}";
            var result = await ExecuteScalar(sql, token, args.ToArray()).ConfigureAwait(false);
            return Convert.ToInt64(result);
        }

        public async Task<T?> GetByLinq<T>(Expression<Func<T, bool>> predicate, CancellationToken cancellationToken = default) where T : new()
        {
            var token = cancellationToken == default ? _globalCancellationToken : cancellationToken;
            var (where, args) = BuildWhere(predicate);
            var sql = $"SELECT * FROM `{GetTableName(typeof(T))}` WHERE {where} LIMIT 1";
            var list = await ExecuteQuery<T>(sql, token, args.ToArray()).ConfigureAwait(false);
            return list.FirstOrDefault();
        }

        public async Task<List<T>> GetListByLinq<T>(Expression<Func<T, bool>> predicate, CancellationToken cancellationToken = default) where T : new()
        {
            var token = cancellationToken == default ? _globalCancellationToken : cancellationToken;
            var (where, args) = BuildWhere(predicate);
            var sql = $"SELECT * FROM `{GetTableName(typeof(T))}` WHERE {where}";
            return await ExecuteQuery<T>(sql, token, args.ToArray()).ConfigureAwait(false);
        }

        private static (string where, List<MySqlParameter> args) BuildWhere<T>(Expression<Func<T, bool>> predicate)
            => SqlExpressionTranslator.BuildWhere(predicate);

        private async Task EnsureTableStructure(string tableName, PropertyInfo[] props, CancellationToken cancellationToken)
        {
            try
            {
                var existingColumns = await GetTableColumnsWithTypes(tableName, cancellationToken).ConfigureAwait(false);
                var modelColumnNames = props.Select(p => p.Name).ToHashSet(StringComparer.OrdinalIgnoreCase);

                foreach (var prop in props)
                {
                    if (prop.Name.Equals("Id", StringComparison.OrdinalIgnoreCase)) continue;

                    var desiredType = GetSqlType(prop);
                    if (!existingColumns.TryGetValue(prop.Name, out var currentType))
                    {
                        if (_options.AutoAddColumns)
                        {
                            var alterSql = $"ALTER TABLE `{tableName}` ADD COLUMN `{prop.Name}` {desiredType} NULL";
                            _logger.LogInformation("Adding column '{Column}' to table '{Table}'", prop.Name, tableName);
                            await ExecuteNonQuery(alterSql, cancellationToken).ConfigureAwait(false);
                        }
                        else
                        {
                            _logger.LogWarning("Column '{Column}' missing in table '{Table}' but AutoAddColumns is disabled", prop.Name, tableName);
                        }
                    }
                    else if (!IsTypeCompatible(currentType, desiredType))
                    {
                        if (_options.AutoModifyColumns)
                        {
                            var alterSql = $"ALTER TABLE `{tableName}` MODIFY COLUMN `{prop.Name}` {desiredType} NULL";
                            _logger.LogInformation("Changing column '{Column}' from {Current} to {Desired}", prop.Name, currentType, desiredType);
                            await ExecuteNonQuery(alterSql, cancellationToken).ConfigureAwait(false);
                        }
                        else
                        {
                            _logger.LogWarning("Column '{Column}' type mismatch (DB: {Current}, Model: {Desired}) but AutoModifyColumns is disabled", prop.Name, currentType, desiredType);
                        }
                    }
                }

                if (_options.AutoDeleteOrphanedColumns)
                {
                    foreach (var dbColumn in existingColumns.Keys)
                    {
                        if (dbColumn.Equals("Id", StringComparison.OrdinalIgnoreCase)) continue;
                        if (!modelColumnNames.Contains(dbColumn))
                        {
                            var dropSql = $"ALTER TABLE `{tableName}` DROP COLUMN `{dbColumn}`";
                            _logger.LogWarning("Deleting orphaned column '{Column}' from table '{Table}'", dbColumn, tableName);
                            await ExecuteNonQuery(dropSql, cancellationToken).ConfigureAwait(false);
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Migration failed for {TableName}", tableName);
                throw new MigrationException($"Failed to migrate table {tableName}", ex);
            }
        }

        private async Task<Dictionary<string, string>> GetTableColumnsWithTypes(string tableName, CancellationToken cancellationToken)
        {
            const string sql = @"
                SELECT COLUMN_NAME, COLUMN_TYPE
                FROM INFORMATION_SCHEMA.COLUMNS
                WHERE TABLE_SCHEMA = DATABASE()
                  AND TABLE_NAME = @TableName;";

            LogSql(sql, new[] { new MySqlParameter("@TableName", tableName) });

            await using var connection = new MySqlConnection(_connectionString);
            await connection.OpenAsync(cancellationToken).ConfigureAwait(false);
            await using var cmd = new MySqlCommand(sql, connection);
            cmd.Parameters.AddWithValue("@TableName", tableName);
            await using var reader = await cmd.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);

            var columns = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
                columns[reader.GetString(0)] = reader.GetString(1);
            return columns;
        }

        private async Task SaveInternal<T>(MySqlConnection connection, MySqlTransaction? transaction, T obj, CancellationToken cancellationToken)
        {
            var type = typeof(T);
            var props = GetCachedProperties(type);
            var tableName = GetTableName(type);

            var idProp = props.FirstOrDefault(p => p.Name.Equals("Id", StringComparison.OrdinalIgnoreCase))
                         ?? throw new InvalidOperationException($"Type {type.Name} must have an Id property.");

            var idValueObj = idProp.GetValue(obj);
            long idValue = idValueObj != null ? Convert.ToInt64(idValueObj) : 0;

            bool isVersioned = obj is IVersioned;

            if (idValue > 0)
            {
                long expectedVersion = 0;

                var updatableProps = props
                    .Where(p => !p.Name.Equals("Id", StringComparison.OrdinalIgnoreCase))
                    .Where(p => !isVersioned || !p.Name.Equals("Version", StringComparison.OrdinalIgnoreCase))
                    .ToArray();

                var setClause = string.Join(", ", updatableProps.Select(p => $"`{p.Name}` = @{p.Name}"));

                string sql;
                if (isVersioned)
                {
                    expectedVersion = ((IVersioned)obj!).Version;
                    setClause += ", `Version` = `Version` + 1";
                    sql = $"UPDATE `{tableName}` SET {setClause} WHERE `{idProp.Name}` = @Id AND `Version` = @ExpectedVersion";
                }
                else
                {
                    sql = $"UPDATE `{tableName}` SET {setClause} WHERE `{idProp.Name}` = @Id";
                }

                await using var cmd = new MySqlCommand(sql, connection, transaction);
                cmd.Parameters.AddWithValue("@Id", idValue);
                if (isVersioned)
                    cmd.Parameters.AddWithValue("@ExpectedVersion", expectedVersion);
                foreach (var prop in updatableProps)
                    cmd.Parameters.AddWithValue($"@{prop.Name}", ToDbValue(prop.GetValue(obj)));

                LogSql(sql, cmd.Parameters.Cast<MySqlParameter>());

                var rowsAffected = await cmd.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);

                if (isVersioned)
                {
                    if (rowsAffected == 0)
                        throw new ConcurrencyException(
                            $"{type.Name} (Id={idValue}) was modified by another writer since it was loaded (expected Version {expectedVersion}).");
                    ((IVersioned)obj!).Version = expectedVersion + 1;
                }

                CaptureSnapshot(obj, props);
            }
            else
            {
                var insertProps = props.Where(p => !p.Name.Equals("Id", StringComparison.OrdinalIgnoreCase)).ToArray();
                var columns = string.Join(", ", insertProps.Select(p => $"`{p.Name}`"));
                var values = string.Join(", ", insertProps.Select(p => $"@{p.Name}"));

                var sql = $"INSERT INTO `{tableName}` ({columns}) VALUES ({values}); SELECT LAST_INSERT_ID();";

                await using var cmd = new MySqlCommand(sql, connection, transaction);
                foreach (var prop in insertProps)
                    cmd.Parameters.AddWithValue($"@{prop.Name}", ToDbValue(prop.GetValue(obj)));

                LogSql(sql, cmd.Parameters.Cast<MySqlParameter>());

                var lastId = Convert.ToInt64(await cmd.ExecuteScalarAsync(cancellationToken).ConfigureAwait(false));
                if (idProp.PropertyType == typeof(int))
                    idProp.SetValue(obj, (int)lastId);
                else if (idProp.PropertyType == typeof(long))
                    idProp.SetValue(obj, lastId);

                CaptureSnapshot(obj, props);
            }
        }

        private async Task ExecuteInTransactionAsync(Func<MySqlConnection, MySqlTransaction, CancellationToken, Task> action, CancellationToken cancellationToken)
        {
            for (int attempt = 0; attempt < MaxRetryAttempts; attempt++)
            {
                await using var connection = new MySqlConnection(_connectionString);
                await connection.OpenAsync(cancellationToken).ConfigureAwait(false);
                await using var transaction = await connection.BeginTransactionAsync(IsolationLevel.ReadCommitted, cancellationToken).ConfigureAwait(false);

                try
                {
                    await action(connection, transaction, cancellationToken).ConfigureAwait(false);
                    await transaction.CommitAsync(cancellationToken).ConfigureAwait(false);
                    return;
                }
                catch (MySqlException ex) when (IsTransient(ex) && attempt < MaxRetryAttempts - 1)
                {
                    _logger.LogWarning(ex, "Transient MySQL error {Number}, retry attempt {Attempt}", ex.Number, attempt + 1);
                    await transaction.RollbackAsync(cancellationToken).ConfigureAwait(false);
                    await Task.Delay(RetryDelays[attempt], cancellationToken).ConfigureAwait(false);
                }
                catch
                {
                    await transaction.RollbackAsync(cancellationToken).ConfigureAwait(false);
                    throw;
                }
            }
        }

        private async Task<T> WithRetry<T>(Func<CancellationToken, Task<T>> action, CancellationToken cancellationToken)
        {
            for (int attempt = 0; attempt < MaxRetryAttempts - 1; attempt++)
            {
                try
                {
                    return await action(cancellationToken).ConfigureAwait(false);
                }
                catch (MySqlException ex) when (IsTransient(ex))
                {
                    _logger.LogWarning(ex, "Transient MySQL error {Number}, retry attempt {Attempt}", ex.Number, attempt + 1);
                    await Task.Delay(RetryDelays[attempt], cancellationToken).ConfigureAwait(false);
                }
            }
            return await action(cancellationToken).ConfigureAwait(false);
        }

        internal void LogSql(string sql, IEnumerable<MySqlParameter>? parameters)
        {
            if (!_options.DebugMode) return;
            if (!_logger.IsEnabled(LogLevel.Debug)) return;
            var paramText = parameters == null
                ? ""
                : string.Join(", ", parameters.Select(p => $"{p.ParameterName}={p.Value}"));
            _logger.LogDebug("SQL: {Sql} | Params: {Params}", sql, paramText);
        }

        private static string StripCommentsAndLiterals(string sql)
        {
            var sb = new StringBuilder(sql.Length);
            int i = 0;
            while (i < sql.Length)
            {
                if ((sql[i] == '-' && i + 1 < sql.Length && sql[i + 1] == '-') || sql[i] == '#')
                {
                    i++;
                    while (i < sql.Length && sql[i] != '\n') i++;
                    continue;
                }
                if (sql[i] == '/' && i + 1 < sql.Length && sql[i + 1] == '*')
                {
                    i += 2;
                    while (i + 1 < sql.Length && !(sql[i] == '*' && sql[i + 1] == '/')) i++;
                    i += 2;
                    continue;
                }
                if (sql[i] == '\'')
                {
                    i++;
                    while (i < sql.Length)
                    {
                        if (sql[i] == '\\' && i + 1 < sql.Length) { i += 2; continue; }
                        if (sql[i] == '\'') { i++; break; }
                        i++;
                    }
                    sb.Append("'x'");
                    continue;
                }
                if (sql[i] == '"')
                {
                    i++;
                    while (i < sql.Length)
                    {
                        if (sql[i] == '\\' && i + 1 < sql.Length) { i += 2; continue; }
                        if (sql[i] == '"') { i++; break; }
                        i++;
                    }
                    sb.Append("\"x\"");
                    continue;
                }
                if (sql[i] == '`')
                {
                    i++;
                    while (i < sql.Length && sql[i] != '`') i++;
                    if (i < sql.Length) i++;
                    sb.Append("`x`");
                    continue;
                }
                sb.Append(sql[i]);
                i++;
            }
            return sb.ToString();
        }

        private static readonly HashSet<string> _dangerousKeywords = new(StringComparer.OrdinalIgnoreCase)
        {
            "INSERT", "UPDATE", "DELETE", "DROP", "ALTER", "TRUNCATE",
            "CREATE", "GRANT", "REVOKE", "REPLACE", "LOAD", "CALL", "UNION", "HANDLER"
        };

        private static readonly Regex _leadingKeywordRegex =
            new(@"^(SELECT|WITH)\b", RegexOptions.Compiled | RegexOptions.IgnoreCase);

        /// <summary>
        /// Validates that the SQL is a single SELECT (or WITH ... SELECT) statement.
        /// Comments and string literals are stripped first so that keywords hidden
        /// inside them cannot be used to bypass the check.
        /// </summary>
        private static void ValidateSelectOnly(string sql)
        {
            var cleaned = StripCommentsAndLiterals(sql);

            var statements = cleaned.Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
            if (statements.Length > 1)
                throw new SecurityException("ExecuteQuery does not allow multiple statements.");

            if (statements.Length == 0)
                throw new SecurityException("ExecuteQuery received an empty SQL statement.");

            var upper = statements[0].Trim().ToUpperInvariant();

            if (!_leadingKeywordRegex.IsMatch(statements[0].Trim()))
                throw new SecurityException("ExecuteQuery is for SELECT only. Use ExecuteNonQuery for modifications.");

            if (Regex.IsMatch(upper, @"\bINTO\s+(OUTFILE|DUMPFILE)\b"))
                throw new SecurityException("ExecuteQuery does not allow SELECT INTO OUTFILE/DUMPFILE.");

            foreach (var kw in _dangerousKeywords)
            {
                if (Regex.IsMatch(upper, $@"\b{kw}\b"))
                    throw new SecurityException(
                        $"ExecuteQuery does not allow '{kw}' statements. Use ExecuteNonQuery for modifications.");
            }
        }

        private static void ValidateWhereClause(string whereClause)
        {
            var cleaned = StripCommentsAndLiterals(whereClause);

            if (cleaned.Contains(';'))
                throw new ArgumentException("Invalid WHERE clause: statement terminators are not allowed.");

            var upper = cleaned.ToUpperInvariant();
            foreach (var kw in _dangerousKeywords)
            {
                if (Regex.IsMatch(upper, $@"\b{kw}\b"))
                    throw new ArgumentException($"Invalid WHERE clause: '{kw}' is not allowed.");
            }
        }

        private static void CaptureSnapshot<T>(T obj, PropertyInfo[] props)
        {
            if (obj is null) return;
            var snapshot = new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase);
            foreach (var prop in props)
            {
                if (!prop.CanRead) continue;
                snapshot[prop.Name] = prop.GetValue(obj);
            }
            _snapshots.Remove(obj);
            _snapshots.Add(obj, snapshot);
        }

        private static bool IsNumericType(Type t)
        {
            t = Nullable.GetUnderlyingType(t) ?? t;
            return t == typeof(int) || t == typeof(long) || t == typeof(short) ||
                   t == typeof(byte) || t == typeof(float) || t == typeof(double) ||
                   t == typeof(decimal);
        }

        /// <summary>
        /// Maps a single data reader row to an object of the given type.
        /// Also captures a snapshot for SaveChangedData.
        /// </summary>
        internal object ReadRow(Type type, MySqlDataReader reader, PropertyInfo[]? props = null)
        {
            props ??= GetCachedProperties(type);
            var obj = Activator.CreateInstance(type)!;

            var columns = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
            for (int i = 0; i < reader.FieldCount; i++)
                columns[reader.GetName(i)] = i;

            foreach (var prop in props)
            {
                if (!prop.CanWrite || prop.SetMethod == null) continue;
                if (!columns.TryGetValue(prop.Name, out int ordinal)) continue;

                var val = reader.GetValue(ordinal);
                if (val == DBNull.Value) continue;

                var targetType = Nullable.GetUnderlyingType(prop.PropertyType) ?? prop.PropertyType;

                try
                {
                    if (targetType.IsEnum)
                    {
                        prop.SetValue(obj, val switch
                        {
                            int i => Enum.ToObject(targetType, i),
                            long l => Enum.ToObject(targetType, (int)l),
                            string s => int.TryParse(s, out int p)
                                ? Enum.ToObject(targetType, p)
                                : Enum.Parse(targetType, s, true),
                            _ => Convert.ChangeType(val, targetType)
                        });
                    }
                    else if (targetType == typeof(Guid))
                        prop.SetValue(obj, val is string s ? Guid.Parse(s) : new Guid((byte[])val));
                    else if (targetType == typeof(bool))
                        prop.SetValue(obj, Convert.ToInt32(val) != 0);
                    else if (targetType == typeof(byte[]))
                        prop.SetValue(obj, val is byte[] b ? b : Convert.FromBase64String((string)val));
                    else if (val is string json && !IsSimpleType(targetType))
                        prop.SetValue(obj, JsonConvert.DeserializeObject(json, targetType));
                    else
                        prop.SetValue(obj, Convert.ChangeType(val, targetType));
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Failed to map column '{Column}' to property '{Property}'", prop.Name, prop.Name);

                    if (Nullable.GetUnderlyingType(prop.PropertyType) == null && targetType.IsValueType)
                        prop.SetValue(obj, Activator.CreateInstance(targetType));
                }
            }

            CaptureSnapshot(obj, props);
            return obj;
        }

        internal static PropertyInfo[] GetCachedProperties(Type type)
            => _propertyCache.GetOrAdd(type, t => t.GetProperties(BindingFlags.Public | BindingFlags.Instance));

        private static string GetTableName(Type type) => type.Name;

        private static string GetSqlType(PropertyInfo prop)
        {
            var type = Nullable.GetUnderlyingType(prop.PropertyType) ?? prop.PropertyType;

            return type switch
            {
                _ when type == typeof(int) => "INT",
                _ when type == typeof(long) => "BIGINT",
                _ when type == typeof(bool) => "TINYINT(1)",
                _ when type == typeof(float) || type == typeof(double) => "DOUBLE",
                _ when type == typeof(decimal) => "DECIMAL(18,6)",
                _ when type == typeof(DateTime) => "DATETIME(6)",
                _ when type == typeof(Guid) => "CHAR(36)",
                _ when type == typeof(byte[]) => "LONGBLOB",
                _ when type == typeof(DateTimeOffset) => "DATETIME(6)",
                _ when type == typeof(TimeSpan) => "TIME(6)",
                _ when type == typeof(short) => "SMALLINT",
                _ when type == typeof(byte) => "TINYINT UNSIGNED",
                _ when type.IsEnum => "INT",
                _ when type == typeof(string) => GetStringColumnType(prop),
                _ => "LONGTEXT"
            };
        }

        private static string GetStringColumnType(PropertyInfo prop)
        {
            var stringLengthAttr = prop.GetCustomAttribute<StringLengthAttribute>();
            if (stringLengthAttr != null)
                return $"VARCHAR({stringLengthAttr.MaximumLength})";
            return "LONGTEXT";
        }

        internal static bool IsSimpleType(Type type)
        {
            return type.IsPrimitive
                || type == typeof(string)
                || type == typeof(decimal)
                || type == typeof(DateTime)
                || type == typeof(DateTimeOffset)
                || type == typeof(TimeSpan)
                || type == typeof(Guid)
                || type == typeof(byte[]);
        }

        private static object ToDbValue(object? value)
        {
            if (value == null) return DBNull.Value;

            var type = value.GetType();
            if (type.IsEnum)
                return Convert.ChangeType(value, Enum.GetUnderlyingType(type));

            if (value is Guid g)
                return g == Guid.Empty ? DBNull.Value : g.ToString("D");

            if (value is DateTime dt)
                return dt.Kind == DateTimeKind.Unspecified ? DateTime.SpecifyKind(dt, DateTimeKind.Utc) : dt.ToUniversalTime();

            if (type == typeof(byte[]))
                return value;

            if (value is Array or IEnumerable && type != typeof(string))
                return JsonConvert.SerializeObject(value);

            if (!IsSimpleType(type))
                return JsonConvert.SerializeObject(value);

            return value;
        }

        private static readonly Regex _intDisplayWidthRegex =
            new(@"^(bigint|int|smallint|tinyint|mediumint)\(\d+\)(\s*unsigned)?$",
                RegexOptions.Compiled);

        private static string StripIntDisplayWidth(string type)
        {
            var match = _intDisplayWidthRegex.Match(type);
            if (!match.Success) return type;
            var baseType = match.Groups[1].Value;
            var unsigned = match.Groups[2].Success ? " unsigned" : "";
            return baseType + unsigned;
        }

        private static bool IsTypeCompatible(string currentType, string desiredType)
        {
            currentType = StripIntDisplayWidth(currentType.ToLowerInvariant().Trim());
            desiredType = StripIntDisplayWidth(desiredType.ToLowerInvariant().Trim());

            if (currentType.StartsWith("longtext") && desiredType.StartsWith("varchar"))
                return false;

            if (currentType.StartsWith("varchar") && desiredType.StartsWith("varchar"))
            {
                var currentLen = ExtractLength(currentType);
                var desiredLen = ExtractLength(desiredType);
                return currentLen >= desiredLen;
            }

            return currentType == desiredType;
        }

        private static int ExtractLength(string type)
        {
            var match = Regex.Match(type, @"\((\d+)\)");
            return match.Success ? int.Parse(match.Groups[1].Value) : 0;
        }

        private static string GetPropertyName<T>(Expression<Func<T, object>> expression)
        {
            return expression.Body switch
            {
                MemberExpression m => m.Member.Name,
                UnaryExpression u when u.Operand is MemberExpression m => m.Member.Name,
                _ => throw new ArgumentException("Order by must be a simple property accessor, e.g. x => x.CreatedAt.")
            };
        }

        public class MigrationException : Exception
        {
            public MigrationException(string message, Exception inner) : base(message, inner) { }
        }
    }

    public class FeatherDatabaseOptions
    {
        public bool AutoCreateTable { get; set; } = true;
        public bool AutoAddColumns { get; set; } = true;
        public bool AutoModifyColumns { get; set; } = false;
        public bool AutoDeleteOrphanedColumns { get; set; } = false;
        public bool DebugMode { get; set; } = false;
    }

    public class FeatherQueryable<T> : IQueryable<T>, IAsyncEnumerable<T>, IOrderedQueryable<T>
    {
        internal readonly FeatherQueryProvider _provider;
        internal readonly Expression _expression;

        public FeatherQueryable(FeatherDatabase database)
        {
            _provider = new FeatherQueryProvider(database);
            _expression = Expression.Constant(this);
        }

        internal FeatherQueryable(FeatherQueryProvider provider, Expression expression)
        {
            _provider = provider ?? throw new ArgumentNullException(nameof(provider));
            _expression = expression ?? throw new ArgumentNullException(nameof(expression));
        }

        public Type ElementType => typeof(T);
        public Expression Expression => _expression;
        public IQueryProvider Provider => _provider;

        public IEnumerator<T> GetEnumerator()
            => _provider.ExecuteAsync<List<T>>(_expression).GetAwaiter().GetResult().GetEnumerator();

        IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();

        public IAsyncEnumerator<T> GetAsyncEnumerator(CancellationToken cancellationToken = default)
            => _provider.StreamAsync<T>(_expression, cancellationToken).GetAsyncEnumerator(cancellationToken);
    }

    public enum ExecutionType
    {
        List,
        FirstOrDefault,
        SingleOrDefault,
        Count,
        Any
    }

    public class SqlTranslationResult
    {
        public string Sql { get; set; } = "";
        public List<MySqlParameter> Parameters { get; set; } = new();
        public ExecutionType ExecutionType { get; set; } = ExecutionType.List;
        public bool IsProjection { get; set; }
        public Type? ProjectedType { get; set; }
        public List<MemberInfo>? ProjectedMembers { get; set; }
        public ConstructorInfo? ProjectedConstructor { get; set; }
    }

    public class FeatherQueryProvider : IQueryProvider
    {
        private readonly FeatherDatabase _database;

        public FeatherQueryProvider(FeatherDatabase database)
        {
            _database = database ?? throw new ArgumentNullException(nameof(database));
        }

        public IQueryable CreateQuery(Expression expression)
        {
            var elementType = GetElementType(expression.Type);
            var queryableType = typeof(FeatherQueryable<>).MakeGenericType(elementType);
            return (IQueryable)queryableType
                .GetConstructor(new[] { typeof(FeatherQueryProvider), typeof(Expression) })!
                .Invoke(new object[] { this, expression });
        }

        public IQueryable<TElement> CreateQuery<TElement>(Expression expression)
            => new FeatherQueryable<TElement>(this, expression);

        public object? Execute(Expression expression)
            => ExecuteAsync(expression).GetAwaiter().GetResult();

        public TResult Execute<TResult>(Expression expression)
            => (TResult)ExecuteAsync(expression).GetAwaiter().GetResult()!;

        public Task<object?> ExecuteAsync(Expression expression, CancellationToken cancellationToken = default)
            => ExecuteCoreAsync(expression, cancellationToken);

        public async Task<TResult> ExecuteAsync<TResult>(Expression expression, CancellationToken cancellationToken = default)
        {
            var result = await ExecuteCoreAsync(expression, cancellationToken).ConfigureAwait(false);

            if (result is IList sourceList && typeof(TResult) != sourceList.GetType())
            {
                if (typeof(TResult).IsGenericType && typeof(TResult).GetGenericTypeDefinition() == typeof(List<>))
                {
                    var elementType = typeof(TResult).GetGenericArguments()[0];
                    var typedList = (IList)Activator.CreateInstance(typeof(List<>).MakeGenericType(elementType))!;
                    foreach (var item in sourceList)
                        typedList.Add(item);
                    return (TResult)(object)typedList;
                }
            }

            return (TResult)result!;
        }

        internal async IAsyncEnumerable<T> StreamAsync<T>(Expression expression, [EnumeratorCancellation] CancellationToken cancellationToken)
        {
            var sourceType = FindSourceType(expression);
            var translator = new SqlExpressionTranslator(sourceType);
            var query = translator.Translate(expression);

            _database.LogSql(query.Sql, query.Parameters);

            if (query.ExecutionType != ExecutionType.List)
            {
                var result = await ExecuteAsync<List<T>>(expression, cancellationToken).ConfigureAwait(false);
                foreach (var item in result)
                    yield return item;
                yield break;
            }

            await using var connection = new MySqlConnection(_database.GetConnectionString());
            await connection.OpenAsync(cancellationToken).ConfigureAwait(false);
            await using var cmd = new MySqlCommand(query.Sql, connection);

            foreach (var p in query.Parameters)
                cmd.Parameters.Add(p);

            await using var reader = await cmd.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);

            if (query.IsProjection)
            {
                while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
                    yield return (T)ReadProjection(reader, query)!;
            }
            else
            {
                var props = FeatherDatabase.GetCachedProperties(sourceType);
                while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
                    yield return (T)_database.ReadRow(sourceType, reader, props);
            }
        }

        private async Task<object?> ExecuteCoreAsync(Expression expression, CancellationToken cancellationToken)
        {
            var sourceType = FindSourceType(expression);
            var translator = new SqlExpressionTranslator(sourceType);
            var query = translator.Translate(expression);

            _database.LogSql(query.Sql, query.Parameters);

            await using var connection = new MySqlConnection(_database.GetConnectionString());
            await connection.OpenAsync(cancellationToken).ConfigureAwait(false);
            await using var cmd = new MySqlCommand(query.Sql, connection);

            foreach (var p in query.Parameters)
                cmd.Parameters.Add(p);

            await using var reader = await cmd.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);

            if ((query.IsProjection && query.ProjectedType != null) ||
                query.ExecutionType == ExecutionType.Count ||
                query.ExecutionType == ExecutionType.Any)
            {
                return await ReadProjectionAsync(reader, query, cancellationToken).ConfigureAwait(false);
            }

            var props = FeatherDatabase.GetCachedProperties(sourceType);
            var list = new List<object>();

            while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
                list.Add(_database.ReadRow(sourceType, reader, props));

            return query.ExecutionType switch
            {
                ExecutionType.FirstOrDefault => list.FirstOrDefault(),
                ExecutionType.SingleOrDefault => list.SingleOrDefault(),
                _ => list
            };
        }

        private async Task<object?> ReadProjectionAsync(MySqlDataReader reader, SqlTranslationResult query, CancellationToken ct)
        {
            var type = query.ProjectedType!;

            if (query.ExecutionType == ExecutionType.Count)
            {
                if (await reader.ReadAsync(ct).ConfigureAwait(false))
                    return Convert.ToInt64(reader.GetValue(0));
                return 0L;
            }

            if (query.ExecutionType == ExecutionType.Any)
                return await reader.ReadAsync(ct).ConfigureAwait(false);

            if (query.ExecutionType == ExecutionType.FirstOrDefault)
            {
                if (await reader.ReadAsync(ct).ConfigureAwait(false))
                    return ReadProjection(reader, query);
                return GetDefault(type);
            }

            if (query.ExecutionType == ExecutionType.SingleOrDefault)
            {
                var rows = new List<object>();
                while (await reader.ReadAsync(ct).ConfigureAwait(false))
                    rows.Add(ReadProjection(reader, query)!);
                return rows.Count switch
                {
                    0 => GetDefault(type),
                    1 => rows[0],
                    _ => throw new InvalidOperationException("Sequence contains more than one element")
                };
            }

            var list = (IList)Activator.CreateInstance(
                typeof(List<>).MakeGenericType(type))!;
            while (await reader.ReadAsync(ct).ConfigureAwait(false))
                list.Add(ReadProjection(reader, query)!);
            return list;
        }

        private static object? ReadProjection(MySqlDataReader reader, SqlTranslationResult query)
        {
            var multiMember = query.ProjectedConstructor != null ||
                              (query.ProjectedMembers != null && query.ProjectedMembers.Count > 1);

            if (multiMember)
                return BuildProjection(reader, query.ProjectedMembers!, query.ProjectedType!, query.ProjectedConstructor);
            return ReadValue(reader.GetValue(0), query.ProjectedType!);
        }

        private static object? BuildProjection(
            MySqlDataReader reader,
            List<MemberInfo> members,
            Type targetType,
            ConstructorInfo? constructor = null)
        {
            if (constructor != null)
            {
                var ctorParams = constructor.GetParameters();
                var values = new object?[ctorParams.Length];
                for (int i = 0; i < ctorParams.Length; i++)
                    values[i] = ReadValue(reader.GetValue(i), ctorParams[i].ParameterType);
                return constructor.Invoke(values);
            }

            var types = members
                .Select(m => m is PropertyInfo pi ? pi.PropertyType : ((FieldInfo)m).FieldType)
                .ToArray();
            var ctor = targetType.GetConstructor(types);
            if (ctor != null)
            {
                var values = members
                    .Select((m, i) => ReadValue(reader.GetValue(i), types[i]))
                    .ToArray();
                return ctor.Invoke(values);
            }

            var obj = Activator.CreateInstance(targetType);
            for (int i = 0; i < members.Count; i++)
            {
                var val = ReadValue(reader.GetValue(i),
                    members[i] is PropertyInfo pi ? pi.PropertyType : ((FieldInfo)members[i]).FieldType);
                if (members[i] is PropertyInfo prop && prop.CanWrite)
                    prop.SetValue(obj, val);
                else if (members[i] is FieldInfo field)
                    field.SetValue(obj, val);
            }
            return obj;
        }

        private static object? ReadValue(object? value, Type targetType)
        {
            if (value == null || value == DBNull.Value)
                return GetDefault(targetType);

            var underlying = Nullable.GetUnderlyingType(targetType) ?? targetType;

            if (underlying == typeof(Guid))
                return value is string s ? Guid.Parse(s) : new Guid((byte[])value);

            if (underlying == typeof(bool))
                return Convert.ToInt32(value) != 0;

            if (underlying.IsEnum)
                return Enum.ToObject(underlying, Convert.ToInt32(value));

            return Convert.ChangeType(value, underlying);
        }

        private static object? GetDefault(Type type)
            => type.IsValueType ? Activator.CreateInstance(type) : null;

        private static Type FindSourceType(Expression expression)
        {
            var finder = new SourceTypeFinder();
            finder.Visit(expression);
            return finder.SourceType
                ?? throw new InvalidOperationException(
                    "Could not determine the entity type. Make sure the query starts from FeatherDatabase.Query<T>().");
        }

        private class SourceTypeFinder : ExpressionVisitor
        {
            public Type? SourceType { get; private set; }

            protected override Expression VisitConstant(ConstantExpression node)
            {
                if (node.Value != null)
                {
                    var type = node.Value.GetType();
                    if (type.IsGenericType && type.GetGenericTypeDefinition() == typeof(FeatherQueryable<>))
                        SourceType ??= type.GetGenericArguments()[0];
                }
                return base.VisitConstant(node);
            }
        }

        private static Type GetElementType(Type type)
        {
            if (type.IsGenericType)
            {
                var genericDef = type.GetGenericTypeDefinition();
                if (genericDef == typeof(IQueryable<>) ||
                    genericDef == typeof(IEnumerable<>) ||
                    genericDef == typeof(IOrderedQueryable<>))
                    return type.GetGenericArguments()[0];
            }
            return typeof(object);
        }
    }

    public class SqlExpressionTranslator
    {
        private readonly string _tableName;
        private readonly List<MySqlParameter> _parameters = new();
        private int _paramCounter;

        private static readonly HashSet<string> _supportedMethods = new()
        {
            "Where", "OrderBy", "OrderByDescending", "ThenBy", "ThenByDescending",
            "Skip", "Take", "Select", "FirstOrDefault", "SingleOrDefault",
            "Count", "LongCount", "Any", "ToList"
        };

        public SqlExpressionTranslator(Type sourceType)
        {
            _tableName = sourceType.Name;
        }

        /// <summary>
        /// Translates a predicate lambda into a WHERE clause + parameter list.
        /// Used by the non-LINQ helper methods on FeatherDatabase (GetByLinq, DeleteByLinq, etc.)
        /// so they share the same translation logic as the IQueryable provider.
        /// </summary>
        internal static (string Where, List<MySqlParameter> Parameters) BuildWhere<T>(Expression<Func<T, bool>> predicate)
        {
            var t = new SqlExpressionTranslator(typeof(T));
            var where = t.TranslatePredicate(predicate.Body);
            return (where, t._parameters);
        }

        public SqlTranslationResult Translate(Expression expression)
        {
            var ctx = new QueryContext
            {
                TableName = _tableName,
                ExecutionType = ExecutionType.List
            };

            VisitExpression(expression, ctx);
            var sql = BuildSql(ctx, _parameters);

            return new SqlTranslationResult
            {
                Sql = sql,
                Parameters = _parameters,
                ExecutionType = ctx.ExecutionType,
                IsProjection = ctx.IsProjection,
                ProjectedType = ctx.ProjectedType,
                ProjectedMembers = ctx.ProjectedMembers,
                ProjectedConstructor = ctx.ProjectedConstructor
            };
        }

        private void VisitExpression(Expression expression, QueryContext ctx)
        {
            switch (expression)
            {
                case ConstantExpression constant when constant.Value is IQueryable:
                    break;
                case MethodCallExpression methodCall:
                    VisitMethodCall(methodCall, ctx);
                    break;
                case UnaryExpression unary when unary.NodeType == ExpressionType.Quote:
                    VisitExpression(unary.Operand, ctx);
                    break;
                case LambdaExpression lambda:
                    VisitExpression(lambda.Body, ctx);
                    break;
                default:
                    throw new NotSupportedException($"Expression type '{expression.GetType().Name}' is not supported");
            }
        }

        private void VisitMethodCall(MethodCallExpression methodCall, QueryContext ctx)
        {
            var methodName = methodCall.Method.Name;

            if (!_supportedMethods.Contains(methodName))
                throw new NotSupportedException($"Method '{methodName}' is not supported by FeatherDatabase LINQ provider");

            foreach (var arg in methodCall.Arguments)
            {
                if (IsQueryableType(arg.Type))
                    VisitExpression(arg, ctx);
            }

            switch (methodName)
            {
                case "Where":
                    ProcessWhere(methodCall, ctx);
                    break;
                case "OrderBy":
                    ProcessOrderBy(methodCall, ctx, "ASC", false);
                    break;
                case "OrderByDescending":
                    ProcessOrderBy(methodCall, ctx, "DESC", false);
                    break;
                case "ThenBy":
                    ProcessOrderBy(methodCall, ctx, "ASC", true);
                    break;
                case "ThenByDescending":
                    ProcessOrderBy(methodCall, ctx, "DESC", true);
                    break;
                case "Skip":
                    ctx.Offset = GetConstantValue<int>(methodCall.Arguments.Last());
                    break;
                case "Take":
                    ctx.Limit = GetConstantValue<int>(methodCall.Arguments.Last());
                    break;
                case "FirstOrDefault":
                    ctx.ExecutionType = ExecutionType.FirstOrDefault;
                    ctx.Limit = 1;
                    break;
                case "SingleOrDefault":
                    ctx.ExecutionType = ExecutionType.SingleOrDefault;
                    ctx.Limit = 2;
                    break;
                case "Count":
                case "LongCount":
                    ctx.ExecutionType = ExecutionType.Count;
                    ctx.SelectClause = "COUNT(*)";
                    break;
                case "Any":
                    ctx.ExecutionType = ExecutionType.Any;
                    ctx.SelectClause = "1";
                    ctx.Limit = 1;
                    if (methodCall.Arguments.Count > 1)
                    {
                        var predicate = GetLambdaFromMethodCall(methodCall);
                        if (predicate != null)
                        {
                            var whereSql = TranslatePredicate(predicate.Body);
                            ctx.WhereClause = string.IsNullOrEmpty(ctx.WhereClause)
                                ? whereSql
                                : $"{ctx.WhereClause} AND {whereSql}";
                        }
                    }
                    break;
                case "ToList":
                    break;
                case "Select":
                    ProcessSelect(methodCall, ctx);
                    break;
            }
        }

        private void ProcessWhere(MethodCallExpression methodCall, QueryContext ctx)
        {
            var predicate = GetLambdaFromMethodCall(methodCall);
            if (predicate == null) return;

            var whereSql = TranslatePredicate(predicate.Body);
            ctx.WhereClause = string.IsNullOrEmpty(ctx.WhereClause)
                ? whereSql
                : $"{ctx.WhereClause} AND {whereSql}";
        }

        private void ProcessOrderBy(MethodCallExpression methodCall, QueryContext ctx, string direction, bool isThenBy)
        {
            var keySelector = GetLambdaFromMethodCall(methodCall);
            if (keySelector == null) return;

            var columnName = GetColumnName(keySelector.Body);
            var orderPart = $"`{columnName}` {direction}";

            if (isThenBy)
            {
                ctx.OrderByClause = string.IsNullOrEmpty(ctx.OrderByClause)
                    ? orderPart
                    : $"{ctx.OrderByClause}, {orderPart}";
            }
            else
            {
                ctx.OrderByClause = orderPart;
            }
        }

        private void ProcessSelect(MethodCallExpression methodCall, QueryContext ctx)
        {
            var selector = GetLambdaFromMethodCall(methodCall);
            if (selector == null) return;

            var body = selector.Body;

            if (body is UnaryExpression { NodeType: ExpressionType.Convert } unary)
                body = unary.Operand;

            if (body is MemberExpression member && member.Expression?.NodeType == ExpressionType.Parameter)
            {
                ctx.SelectClause = $"`{member.Member.Name}`";
                ctx.IsProjection = true;
                ctx.ProjectedType = selector.ReturnType;
                ctx.ProjectedMembers = new List<MemberInfo> { member.Member };
                return;
            }

            if (body is NewExpression newExpr)
            {
                if (newExpr.Members != null && newExpr.Members.Count > 0)
                {
                    ctx.SelectClause = string.Join(", ", newExpr.Members.Select(m => $"`{m.Name}`"));
                    ctx.IsProjection = true;
                    ctx.ProjectedType = newExpr.Type;
                    ctx.ProjectedMembers = newExpr.Members.ToList();
                    return;
                }

                if (newExpr.Arguments.Count > 0)
                {
                    var columnNames = new List<string>();
                    var sourceMembers = new List<MemberInfo>();

                    foreach (var arg in newExpr.Arguments)
                    {
                        Expression argExpr = arg;
                        if (argExpr is UnaryExpression { NodeType: ExpressionType.Convert } conv)
                            argExpr = conv.Operand;

                        if (argExpr is MemberExpression argMember &&
                            argMember.Expression?.NodeType == ExpressionType.Parameter)
                        {
                            columnNames.Add(argMember.Member.Name);
                            sourceMembers.Add(argMember.Member);
                        }
                        else
                        {
                            throw new NotSupportedException(
                                "Only simple property projections are supported in Select. " +
                                "Use property accesses of the row parameter, e.g. " +
                                "new ViewModel(r.Id, r.Subject) or new ViewModel { Id = r.Id }.");
                        }
                    }

                    ctx.SelectClause = string.Join(", ", columnNames.Select(c => $"`{c}`"));
                    ctx.IsProjection = true;
                    ctx.ProjectedType = newExpr.Type;
                    ctx.ProjectedMembers = sourceMembers;
                    ctx.ProjectedConstructor = newExpr.Constructor;
                    return;
                }
            }

            if (body is MemberInitExpression memberInit && memberInit.Bindings.Count > 0)
            {
                var columnMappings = new List<(string Column, MemberInfo Member)>();

                foreach (var binding in memberInit.Bindings)
                {
                    if (binding is not MemberAssignment assignment)
                        continue;

                    if (assignment.Expression is MemberExpression sourceMember &&
                        sourceMember.Expression?.NodeType == ExpressionType.Parameter)
                    {
                        columnMappings.Add((sourceMember.Member.Name, assignment.Member));
                    }
                    else if (assignment.Expression is MemberExpression srcMember)
                    {
                        columnMappings.Add((srcMember.Member.Name, assignment.Member));
                    }
                    else
                    {
                        throw new NotSupportedException(
                            "Complex expressions in Select are not supported. Use simple property assignments like " +
                            "`new Model { Id = u.Id, Name = u.Name }`.");
                    }
                }

                if (columnMappings.Count == 0)
                    throw new NotSupportedException("Select expression must contain at least one property assignment.");

                var selectParts = columnMappings.Select(m =>
                    !string.Equals(m.Column, m.Member.Name, StringComparison.OrdinalIgnoreCase)
                        ? $"`{m.Column}` AS `{m.Member.Name}`"
                        : $"`{m.Column}`");

                ctx.SelectClause = string.Join(", ", selectParts);
                ctx.IsProjection = true;
                ctx.ProjectedType = memberInit.Type;
                ctx.ProjectedMembers = columnMappings.Select(m => m.Member).ToList();
                return;
            }

            throw new NotSupportedException(
                "Only simple property projections are supported in Select (e.g., u => u.Id, " +
                "u => new { u.Id, u.Name }, new Model { Id = u.Id }, or new Model(u.Id, u.Name)).");
        }

        private string TranslatePredicate(Expression predicate)
        {
            switch (predicate)
            {
                case BinaryExpression binary:
                    return TranslateBinary(binary);
                case UnaryExpression unary when unary.NodeType == ExpressionType.Not:
                    return $"NOT ({TranslatePredicate(unary.Operand)})";
                case UnaryExpression unary when unary.NodeType == ExpressionType.Convert:
                    return TranslatePredicate(unary.Operand);
                case MethodCallExpression methodCall:
                    return TranslateMethodCall(methodCall);
                case MemberExpression member:
                    return $"{TranslateOperand(member)} = {AddParameter(1)}";
                case ConstantExpression constant:
                    if (constant.Type == typeof(bool))
                        return (bool)constant.Value ? "1=1" : "1=0";
                    throw new NotSupportedException(
                        $"Constant expression of type '{constant.Type}' is not supported in a predicate.");
                default:
                    throw new NotSupportedException($"Predicate type '{predicate.GetType().Name}' is not supported");
            }
        }

        private string TranslateBinary(BinaryExpression binary)
        {
            if (binary.NodeType == ExpressionType.Equal && IsNullConstant(binary.Right))
                return $"{TranslateOperand(binary.Left)} IS NULL";

            if (binary.NodeType == ExpressionType.NotEqual && IsNullConstant(binary.Right))
                return $"{TranslateOperand(binary.Left)} IS NOT NULL";

            if (binary.NodeType == ExpressionType.Equal && IsNullConstant(binary.Left))
                return $"{TranslateOperand(binary.Right)} IS NULL";

            if (binary.NodeType == ExpressionType.NotEqual && IsNullConstant(binary.Left))
                return $"{TranslateOperand(binary.Right)} IS NOT NULL";

            var op = binary.NodeType switch
            {
                ExpressionType.Equal => "=",
                ExpressionType.NotEqual => "!=",
                ExpressionType.GreaterThan => ">",
                ExpressionType.GreaterThanOrEqual => ">=",
                ExpressionType.LessThan => "<",
                ExpressionType.LessThanOrEqual => "<=",
                ExpressionType.AndAlso => "AND",
                ExpressionType.OrElse => "OR",
                ExpressionType.Add => "+",
                ExpressionType.Subtract => "-",
                ExpressionType.Multiply => "*",
                ExpressionType.Divide => "/",
                _ => throw new NotSupportedException($"Binary operator '{binary.NodeType}' is not supported")
            };

            if (binary.NodeType is ExpressionType.AndAlso or ExpressionType.OrElse)
            {
                var left = TranslatePredicate(binary.Left);
                var right = TranslatePredicate(binary.Right);
                return $"({left} {op} {right})";
            }

            var leftSql = TranslateOperand(binary.Left);
            var rightSql = TranslateOperand(binary.Right, binary.Right.Type);

            return $"{leftSql} {op} {rightSql}";
        }

        private string TranslateOperand(Expression expression, Type? expectedType = null)
        {
            return expression switch
            {
                MemberExpression member when member.Expression?.NodeType == ExpressionType.Parameter =>
                    $"`{member.Member.Name}`",
                MemberExpression member when member.Expression is ConstantExpression =>
                    AddParameter(GetValue(member)),
                MemberExpression member when member.Expression is MemberExpression =>
                    AddParameter(GetValue(member)),
                ConstantExpression constant => AddParameter(constant.Value),
                UnaryExpression unary when unary.NodeType == ExpressionType.Convert =>
                    TranslateOperand(unary.Operand, unary.Type),
                MethodCallExpression methodCall when methodCall.Method.Name == "ToString" =>
                    $"CAST({TranslateOperand(methodCall.Object!)} AS CHAR)",
                _ => AddParameter(GetValue(expression))
            };
        }

        private string TranslateMethodCall(MethodCallExpression methodCall)
        {
            if (methodCall.Method.DeclaringType == typeof(string))
            {
                if (methodCall.Method.IsStatic &&
                    (methodCall.Method.Name == "IsNullOrEmpty" || methodCall.Method.Name == "IsNullOrWhiteSpace"))
                {
                    var col = TranslateOperand(methodCall.Arguments[0]);
                    return methodCall.Method.Name == "IsNullOrEmpty"
                        ? $"({col} IS NULL OR {col} = '')"
                        : $"({col} IS NULL OR TRIM({col}) = '')";
                }

                var column = TranslateOperand(methodCall.Object!);
                var value = GetValue(methodCall.Arguments[0])?.ToString() ?? "";

                var pattern = methodCall.Method.Name switch
                {
                    "Contains" => $"%{EscapeLikePattern(value)}%",
                    "StartsWith" => $"{EscapeLikePattern(value)}%",
                    "EndsWith" => $"%{EscapeLikePattern(value)}",
                    _ => throw new NotSupportedException($"String method '{methodCall.Method.Name}' is not supported")
                };

                return $"{column} LIKE {AddParameter(pattern)}";
            }

            if (methodCall.Method.Name == "get_HasValue")
                return $"{TranslateOperand(methodCall.Object!)} IS NOT NULL";

            if (methodCall.Method.Name == "get_Value")
                return TranslateOperand(methodCall.Object!);

            if (methodCall.Method.Name == "Contains")
                return TranslateContains(methodCall);

            if (methodCall.Method.Name == "Equals" && methodCall.Arguments.Count == 1)
            {
                var left = TranslateOperand(methodCall.Object!);
                var right = TranslateOperand(methodCall.Arguments[0], methodCall.Arguments[0].Type);
                return $"{left} = {right}";
            }

            if (methodCall.Method.Name == "ToString")
                return $"CAST({TranslateOperand(methodCall.Object!)} AS CHAR)";

            throw new NotSupportedException($"Method '{methodCall.Method.Name}' not supported in WHERE clause");
        }

        private string TranslateContains(MethodCallExpression methodCall)
        {
            Expression? columnExpr = null;
            Expression? collectionExpr = null;

            if (methodCall.Method.DeclaringType == typeof(Enumerable))
            {
                collectionExpr = methodCall.Arguments[0];
                columnExpr = methodCall.Arguments[1];
            }
            else if (methodCall.Object != null)
            {
                collectionExpr = methodCall.Object;
                columnExpr = methodCall.Arguments[0];
            }

            if (columnExpr == null || collectionExpr == null)
                throw new NotSupportedException("Contains expression could not be parsed");

            var columnSql = TranslateOperand(columnExpr);
            var collection = GetValue(collectionExpr) as IEnumerable
                ?? throw new NotSupportedException("Collection value could not be evaluated for Contains");

            var values = new List<object?>();
            foreach (var item in collection)
                values.Add(item);

            if (values.Count == 0)
                return "1 = 0";

            var paramNames = values.Select(v => AddParameter(v)).ToList();
            return $"{columnSql} IN ({string.Join(", ", paramNames)})";
        }

        private string GetColumnName(Expression expression)
        {
            return expression switch
            {
                MemberExpression member when member.Expression?.NodeType == ExpressionType.Parameter =>
                    member.Member.Name,
                UnaryExpression unary when unary.Operand is MemberExpression member =>
                    member.Member.Name,
                _ => throw new ArgumentException("Order by must be a simple property accessor, e.g. x => x.CreatedAt.")
            };
        }

        private LambdaExpression? GetLambdaFromMethodCall(MethodCallExpression methodCall)
        {
            foreach (var arg in methodCall.Arguments.Reverse())
            {
                if (arg is LambdaExpression lambda)
                    return lambda;
                if (arg is UnaryExpression { NodeType: ExpressionType.Quote } unary && unary.Operand is LambdaExpression quotedLambda)
                    return quotedLambda;
            }
            return null;
        }

        private T GetConstantValue<T>(Expression expression)
        {
            return expression switch
            {
                ConstantExpression constant => (T)constant.Value!,
                UnaryExpression { NodeType: ExpressionType.Convert } unary =>
                    (T)GetValue(unary.Operand)!,
                _ => (T)GetValue(expression)!
            };
        }

        private object? GetValue(Expression expression)
        {
            switch (expression)
            {
                case ConstantExpression constant:
                    return constant.Value;
                case MemberExpression member:
                    if (member.Expression is ConstantExpression container)
                    {
                        return member.Member switch
                        {
                            FieldInfo field => field.GetValue(container.Value),
                            PropertyInfo prop => prop.GetValue(container.Value),
                            _ => null
                        };
                    }
                    if (member.Expression != null)
                    {
                        var parentValue = GetValue(member.Expression);
                        return member.Member switch
                        {
                            FieldInfo field => field.GetValue(parentValue),
                            PropertyInfo prop => prop.GetValue(parentValue),
                            _ => null
                        };
                    }
                    return null;
                case UnaryExpression { NodeType: ExpressionType.Convert } unary:
                    var innerValue = GetValue(unary.Operand);
                    if (innerValue != null && unary.Type != innerValue.GetType())
                        return Convert.ChangeType(innerValue, unary.Type);
                    return innerValue;
                case NewArrayExpression newArray:
                    return newArray.Expressions.Select(GetValue).ToArray();
                default:
                    try
                    {
                        var lambda = Expression.Lambda(expression);
                        return lambda.Compile().DynamicInvoke();
                    }
                    catch
                    {
                        return null;
                    }
            }
        }

        private string AddParameter(object? value)
        {
            var paramName = $"@p{_paramCounter++}";
            _parameters.Add(new MySqlParameter(paramName, value ?? DBNull.Value));
            return paramName;
        }

        private static bool IsNullConstant(Expression expression)
            => expression is ConstantExpression constant && constant.Value == null;

        private static string EscapeLikePattern(string value)
            => value.Replace("\\", "\\\\").Replace("%", "\\%").Replace("_", "\\_");

        private static string BuildSql(QueryContext ctx, List<MySqlParameter> parameters)
        {
            var sql = $"SELECT {ctx.SelectClause} FROM `{ctx.TableName}`";

            if (!string.IsNullOrEmpty(ctx.WhereClause))
                sql += $" WHERE {ctx.WhereClause}";

            if (!string.IsNullOrEmpty(ctx.OrderByClause))
                sql += $" ORDER BY {ctx.OrderByClause}";

            if (ctx.Limit > 0)
            {
                sql += " LIMIT @__Limit";
                parameters.Add(new MySqlParameter("@__Limit", ctx.Limit));
                if (ctx.Offset > 0)
                {
                    sql += " OFFSET @__Offset";
                    parameters.Add(new MySqlParameter("@__Offset", ctx.Offset));
                }
            }
            else if (ctx.Offset > 0)
            {
                sql += " LIMIT 18446744073709551615 OFFSET @__Offset";
                parameters.Add(new MySqlParameter("@__Offset", ctx.Offset));
            }

            return sql;
        }

        private static bool IsQueryableType(Type type)
        {
            if (!type.IsGenericType) return false;
            var genericDef = type.GetGenericTypeDefinition();
            return genericDef == typeof(IQueryable<>) ||
                   genericDef == typeof(IOrderedQueryable<>) ||
                   genericDef == typeof(FeatherQueryable<>);
        }

        private class QueryContext
        {
            public string TableName { get; set; } = "";
            public string SelectClause { get; set; } = "*";
            public string WhereClause { get; set; } = "";
            public string OrderByClause { get; set; } = "";
            public int Limit { get; set; } = -1;
            public int Offset { get; set; } = -1;
            public ExecutionType ExecutionType { get; set; } = ExecutionType.List;
            public bool IsProjection { get; set; }
            public Type? ProjectedType { get; set; }
            public List<MemberInfo>? ProjectedMembers { get; set; }
            public ConstructorInfo? ProjectedConstructor { get; set; }
        }
    }

    public static class FeatherAsyncExtensions
    {
        public static async Task<List<T>> ToListAsync<T>(this IQueryable<T> query, CancellationToken cancellationToken = default)
        {
            return query switch
            {
                FeatherQueryable<T> fq => await fq._provider
                    .ExecuteAsync<List<T>>(fq._expression, cancellationToken).ConfigureAwait(false),
                _ => query.ToList()
            };
        }

        public static async Task<T?> FirstOrDefaultAsync<T>(this IQueryable<T> query, CancellationToken cancellationToken = default)
        {
            return query switch
            {
                FeatherQueryable<T> fq => await fq._provider.ExecuteAsync<T?>(
                    Expression.Call(typeof(Queryable), "FirstOrDefault", new[] { typeof(T) }, fq._expression),
                    cancellationToken).ConfigureAwait(false),
                _ => query.FirstOrDefault()
            };
        }

        public static async Task<T?> SingleOrDefaultAsync<T>(this IQueryable<T> query, CancellationToken cancellationToken = default)
        {
            return query switch
            {
                FeatherQueryable<T> fq => await fq._provider.ExecuteAsync<T?>(
                    Expression.Call(typeof(Queryable), "SingleOrDefault", new[] { typeof(T) }, fq._expression),
                    cancellationToken).ConfigureAwait(false),
                _ => query.SingleOrDefault()
            };
        }

        public static async Task<long> CountAsync<T>(this IQueryable<T> query, CancellationToken cancellationToken = default)
        {
            return query switch
            {
                FeatherQueryable<T> fq => await fq._provider.ExecuteAsync<long>(
                    Expression.Call(typeof(Queryable), "LongCount", new[] { typeof(T) }, fq._expression),
                    cancellationToken).ConfigureAwait(false),
                _ => query.LongCount()
            };
        }

        public static async Task<bool> AnyAsync<T>(this IQueryable<T> query, CancellationToken cancellationToken = default)
        {
            return query switch
            {
                FeatherQueryable<T> fq => await fq._provider.ExecuteAsync<bool>(
                    Expression.Call(typeof(Queryable), "Any", new[] { typeof(T) }, fq._expression),
                    cancellationToken).ConfigureAwait(false),
                _ => query.Any()
            };
        }

        public static async Task<bool> AnyAsync<T>(this IQueryable<T> query, Expression<Func<T, bool>> predicate, CancellationToken cancellationToken = default)
        {
            return query switch
            {
                FeatherQueryable<T> fq => await fq._provider.ExecuteAsync<bool>(
                    Expression.Call(typeof(Queryable), "Any", new[] { typeof(T) }, fq._expression, predicate),
                    cancellationToken).ConfigureAwait(false),
                _ => query.Any(predicate)
            };
        }

        public static IQueryable<T> WhereIf<T>(
            this IQueryable<T> query,
            bool condition,
            Expression<Func<T, bool>> predicate)
            => condition ? query.Where(predicate) : query;

        public static IOrderedQueryable<T> OrderByIf<T, TKey>(
            this IQueryable<T> query,
            bool condition,
            Expression<Func<T, TKey>> keySelector)
            => condition ? query.OrderBy(keySelector) : (IOrderedQueryable<T>)query;

        public static IOrderedQueryable<T> OrderByDescendingIf<T, TKey>(
            this IQueryable<T> query,
            bool condition,
            Expression<Func<T, TKey>> keySelector)
            => condition ? query.OrderByDescending(keySelector) : (IOrderedQueryable<T>)query;
    }

    /// <summary>
    /// Fluent async query builder. Nothing is materialised until a terminal
    /// method (ToListAsync, FirstOrDefaultAsync, Take, Select) is awaited.
    /// </summary>
    public class FeatherAsyncQuery<T> where T : new()
    {
        private readonly FeatherDatabase _db;
        private readonly IQueryable<T> _query;

        public FeatherAsyncQuery(FeatherDatabase db)
        {
            _db = db ?? throw new ArgumentNullException(nameof(db));
            _query = new FeatherQueryable<T>(db);
        }

        private FeatherAsyncQuery(FeatherDatabase db, IQueryable<T> query)
        {
            _db = db;
            _query = query;
        }

        public FeatherAsyncQuery<T> Where(Expression<Func<T, bool>> predicate)
            => new(_db, _query.Where(predicate));

        public FeatherAsyncQuery<T> OrderBy<TKey>(Expression<Func<T, TKey>> keySelector)
            => new(_db, Queryable.OrderBy(_query, keySelector));

        public FeatherAsyncQuery<T> OrderByDescending<TKey>(Expression<Func<T, TKey>> keySelector)
            => new(_db, Queryable.OrderByDescending(_query, keySelector));

        public FeatherAsyncQuery<T> ThenBy<TKey>(Expression<Func<T, TKey>> keySelector)
            => new(_db, Queryable.ThenBy((IOrderedQueryable<T>)_query, keySelector));

        public FeatherAsyncQuery<T> ThenByDescending<TKey>(Expression<Func<T, TKey>> keySelector)
            => new(_db, Queryable.ThenByDescending((IOrderedQueryable<T>)_query, keySelector));

        public FeatherAsyncQuery<T> Skip(int count)
            => new(_db, _query.Skip(count));

        public Task<List<T>> Take(int count, CancellationToken cancellationToken = default)
            => _query.Take(count).ToListAsync(cancellationToken);

        public Task<List<TOut>> Select<TOut>(Expression<Func<T, TOut>> selector, CancellationToken cancellationToken = default)
            => _query.Select(selector).ToListAsync(cancellationToken);

        public Task<List<T>> ToListAsync(CancellationToken cancellationToken = default)
            => _query.ToListAsync(cancellationToken);

        public Task<T?> FirstOrDefaultAsync(CancellationToken cancellationToken = default)
            => _query.FirstOrDefaultAsync(cancellationToken);
    }
}